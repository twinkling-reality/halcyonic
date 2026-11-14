import { lstat, readdir, readFile, readlink, realpath, stat } from 'node:fs/promises';
import { dirname, join, relative, resolve } from 'node:path';
import type { HeldSecret } from './redaction.ts';

/**
 * Whether a tree about to leave the Mac, or be copied aside, holds a secret the host keeps (the
 * lead, 2026-11-12: Send and Save a Copy refuse a tree holding a held secret). Every secret the host
 * holds is searched for, whoever declared it: the list redaction reads (the access token, each
 * runtime's and service's key, the person's project secrets), never a list of its own. A hit names
 * the file and which secret it was, never any of its value.
 *
 * The forms searched, each stated so it can be checked:
 * - the value exactly, as a file or an env file holds it, quoted or not (`KEY=value`, `"value"`);
 * - the value as JSON escapes it inside a string, where that differs;
 * - the value as a URL encodes it (`encodeURIComponent`), where that differs;
 * - the value's base64, standard and URL-safe: for each of the three byte alignments the value can
 *   start at in a longer base64 text, the groups of four that hold value bytes alone, when they make
 *   eight characters or more. A value encoded alone, or inside a longer text, is found either way.
 * A value shorter than eight characters is not searched for, as redaction does not replace one.
 */

/** A secret's form to search for, with which secret it is. */
export interface HeldForm {
  readonly what: string;
  readonly bytes: Buffer;
}

/** A file in the tree that holds a held secret, by its path in the tree, and which one. */
export interface HeldHit {
  readonly path: string;
  readonly what: string;
  /** True when a file's or a folder's own name holds it: its path, which would show the value, is never said. */
  readonly named?: true;
}

/** Whether a path's own text holds a held form, so it is never shown and is refused as a name. */
function heldInName(path: string, forms: readonly HeldForm[]): string | null {
  return heldIn(Buffer.from(path, 'utf8'), forms);
}

/** A path as a person may read it: each held form in it, longest first, as the secret's name in brackets. */
function shownName(path: string, forms: readonly HeldForm[]): string {
  let shown = path;
  for (const form of forms) shown = shown.split(form.bytes.toString('utf8')).join(`[${form.what}]`);
  return shown;
}

/** Git, run on the repository that holds the tree: arguments, input, and its output as bytes. */
export type TreeGit = (
  args: readonly string[],
  input?: Buffer,
) => Promise<{ readonly code: number; readonly stdout: Buffer }>;

/** The most files and bytes a share's built copy is read for, links followed; past it nothing is shared. */
const FOLDER_FILES_MAX = 20_000;
const FOLDER_BYTES_MAX = 512 * 1024 * 1024;

/** As redaction: a shorter value is too likely to be an ordinary word. */
const SHORTEST = 8;

/** The most file content read by one `cat-file --batch`. */
const BATCH_BYTES = 32 * 1024 * 1024;

/** Every form of every held secret, longest first, each once. */
export function heldForms(secrets: Iterable<HeldSecret>): HeldForm[] {
  const forms = new Map<string, string>();
  const add = (text: string, what: string) => {
    if (text.length >= SHORTEST && !forms.has(text)) forms.set(text, what);
  };
  for (const { what, value } of secrets) {
    const held = value.trim();
    if (held.length < SHORTEST) continue;
    add(held, what);
    add(JSON.stringify(held).slice(1, -1), what);
    add(encodeURIComponent(held), what);
    const bytes = Buffer.from(held, 'utf8');
    for (const offset of [0, 1, 2]) {
      const encoded = Buffer.concat([Buffer.alloc(offset), bytes])
        .toString('base64')
        .replace(/=+$/, '');
      // A group of four holding a byte before the value, or a byte after it, changes with what is there.
      const start = offset === 0 ? 0 : 4;
      const end = Math.floor((offset + bytes.length) / 3) * 4;
      const middle = encoded.slice(start, end);
      add(middle, what);
      add(middle.replaceAll('+', '-').replaceAll('/', '_'), what);
    }
  }
  return [...forms]
    .sort(([a], [b]) => b.length - a.length)
    .map(([text, what]) => ({ what, bytes: Buffer.from(text, 'utf8') }));
}

/** The first held form `content` holds, or null. */
export function heldIn(content: Buffer, forms: readonly HeldForm[]): string | null {
  for (const form of forms) if (content.includes(form.bytes)) return form.what;
  return null;
}

/**
 * The first file of `tree` (every blob, a link's target included) that holds a held secret, in the
 * tree's own order, or null when none does or nothing is held. Null too is never said when git could
 * not read the tree: that is an error, so nothing leaves unread.
 */
export async function heldInTree(
  git: TreeGit,
  tree: string,
  secrets: Iterable<HeldSecret>,
): Promise<HeldHit | null> {
  const forms = heldForms(secrets);
  if (forms.length === 0) return null;
  const listed = await git(['ls-tree', '-r', '-l', '-z', '--full-tree', tree]);
  if (listed.code !== 0) throw new Error('the tree could not be listed');
  // Each entry is `<mode> <type> <object> <size>\t<path>`; only blobs hold bytes.
  const blobs: { readonly object: string; readonly size: number; readonly path: string }[] = [];
  for (const entry of listed.stdout.toString('utf8').split('\0')) {
    const tab = entry.indexOf('\t');
    if (tab < 0) continue;
    const [, type, object, size] = entry.slice(0, tab).split(/\s+/);
    // Every entry's name, a submodule's included, before what only a blob holds.
    const path = entry.slice(tab + 1);
    const named = heldInName(path, forms);
    if (named !== null) return { path: shownName(path, forms), what: named, named: true };
    if (type !== 'blob' || object === undefined) continue;
    blobs.push({ object, size: Number(size), path });
  }
  const hit = await heldInObjects(git, blobs, forms, 'the tree could not be read');
  return hit === null ? null : { path: hit.found.path, what: hit.what };
}

/**
 * The first of `objects` whose bytes hold a held form, read by `cat-file --batch` in batches of at most
 * BATCH_BYTES, in the order given, or null. Whatever git could not read is an error, never a pass.
 */
async function heldInObjects<T extends { readonly object: string; readonly size: number }>(
  git: TreeGit,
  objects: readonly T[],
  forms: readonly HeldForm[],
  unread: string,
): Promise<{ readonly found: T; readonly what: string } | null> {
  let batch: T[] = [];
  let bytes = 0;
  const read = async (): Promise<{ readonly found: T; readonly what: string } | null> => {
    if (batch.length === 0) return null;
    const asked = batch;
    batch = [];
    bytes = 0;
    const out = await git(
      ['cat-file', '--batch'],
      Buffer.from(`${asked.map((each) => each.object).join('\n')}\n`),
    );
    if (out.code !== 0) throw new Error(unread);
    // Each answer is `<object> <type> <size>\n<content>\n`, in the order asked.
    let at = 0;
    for (const each of asked) {
      const newline = out.stdout.indexOf(0x0a, at);
      if (newline < 0) throw new Error(unread);
      const header = out.stdout.subarray(at, newline).toString('utf8').split(' ');
      const size = Number(header[2]);
      if (header[0] !== each.object || !Number.isInteger(size)) throw new Error(unread);
      const content = out.stdout.subarray(newline + 1, newline + 1 + size);
      at = newline + 1 + size + 1;
      const what = heldIn(content, forms);
      if (what !== null) return { found: each, what };
    }
    return null;
  };
  for (const each of objects) {
    if (batch.length > 0 && bytes + each.size > BATCH_BYTES) {
      const hit = await read();
      if (hit !== null) return hit;
    }
    batch.push(each);
    bytes += each.size;
  }
  return read();
}

/** A held secret in history a push would carry: the file it was in, or null for a commit's own text. */
export interface HeldInHistory {
  readonly path: string | null;
  readonly what: string;
  /** True when a file's or a folder's own name in an earlier version holds it, shown with it in brackets. */
  readonly named?: true;
  /** True when the name is one inside the folder `path` (empty for the top), not `path` itself. */
  readonly within?: true;
  /** The object it was found in: a blob's, or for a commit's own text, the commit's. */
  readonly object: string;
}

/**
 * The first commit or file a push of `tip` would carry that holds a held secret (AUDIT3, TASKS's ALERT of
 * 2026-11-13T1257Z): every commit reachable from `tip` and not from `have`, the tips the remote holds that
 * this repository has too, each commit's own text (its message and its people) and every blob it brings.
 * A tip of the remote's this repository lacks leaves out nothing, so more is read, never less. 'too_large'
 * when more than `most` bytes would be read, which is refused rather than sent unread.
 */
export async function heldInHistory(
  git: TreeGit,
  tip: string,
  have: readonly string[],
  secrets: Iterable<HeldSecret>,
  most: number,
): Promise<HeldInHistory | 'too_large' | null> {
  const forms = heldForms(secrets);
  if (forms.length === 0) return null;
  const listed = await git(
    ['rev-list', '--objects', '--stdin'],
    Buffer.from(`${tip}\n${have.map((each) => `^${each}\n`).join('')}`),
  );
  if (listed.code !== 0) throw new Error('the history could not be listed');
  // Each line is `<object>` for a commit, or `<object> <path>` for a tree or a blob.
  const paths = new Map<string, string | null>();
  for (const line of listed.stdout.toString('utf8').split('\n')) {
    if (line === '') continue;
    const space = line.indexOf(' ');
    const object = space < 0 ? line : line.slice(0, space);
    const path = space < 0 ? null : line.slice(space + 1);
    if (path !== null) {
      const named = heldInName(path, forms);
      if (named !== null) return { path: shownName(path, forms), what: named, object, named: true };
    }
    if (!paths.has(object)) paths.set(object, path);
  }
  if (paths.size === 0) return null;
  const checked = await git(
    ['cat-file', '--batch-check=%(objectname) %(objecttype) %(objectsize)'],
    Buffer.from(`${[...paths.keys()].join('\n')}\n`),
  );
  if (checked.code !== 0) throw new Error('the history could not be read');
  const read: {
    readonly object: string;
    readonly size: number;
    readonly path: string | null;
    readonly folder: boolean;
  }[] = [];
  let total = 0;
  for (const line of checked.stdout.toString('utf8').split('\n')) {
    const [object, type, size] = line.split(' ');
    // A folder's own bytes hold every name in it, one with a line break or a submodule's included, which the
    // listing's paths do not show whole.
    if (object === undefined || (type !== 'commit' && type !== 'blob' && type !== 'tree')) continue;
    const bytes = Number(size);
    if (!Number.isInteger(bytes)) throw new Error('the history could not be read');
    total += bytes;
    if (total > most) return 'too_large';
    read.push({
      object,
      size: bytes,
      path: type === 'commit' ? null : (paths.get(object) ?? ''),
      folder: type === 'tree',
    });
  }
  const hit = await heldInObjects(git, read, forms, 'the history could not be read');
  if (hit === null) return null;
  if (hit.found.folder)
    return {
      path: shownName(hit.found.path ?? '', forms),
      what: hit.what,
      object: hit.found.object,
      named: true,
      within: true,
    };
  return { path: hit.found.path, what: hit.what, object: hit.found.object };
}

/**
 * What a person reads when a file Send keeps as the project's last saved version has it (a .env, say) holds a held secret,
 * and the remote may hold that commit already: taking it out of the folder's file cannot help, since that file is not
 * what is sent, so the true steps are a new value and the old one turned off where it was issued (TASKS, 2026-11-14).
 */
export function heldKeptMessage(hit: HeldHit, repository: string): string {
  const where =
    hit.named === true
      ? `A file's or folder's name saved in the project's last version (${hit.path}, with the secret's name in place of its value)`
      : `The ${hit.path} saved in the project's last version`;
  return `${where} holds a secret this Mac keeps (${hit.what}), and ${repository} may already have it, so nothing was sent. Make a new ${hit.what} with the service that gave it to you and turn the old one off there, then store the new one on this Mac and send again. Until the old one is turned off, treat it as exposed.`;
}

/** What a person reads when a tree holds a held secret: the file and which secret, never the value. */
export function heldMessage(hit: HeldHit, what: 'sent' | 'published' | 'saved' | 'shared'): string {
  const done = {
    sent: 'nothing was sent',
    published: 'nothing was published',
    saved: 'no copy was saved',
    shared: 'nothing was shared',
  }[what];
  if (hit.named === true)
    return `A file's or folder's name holds a secret this Mac keeps (${hit.what}), so ${done}. It is ${hit.path} here, with the secret's name in place of its value. Rename it in the project's folder, then try again.`;
  return `${hit.path} holds a secret this Mac keeps (${hit.what}), so ${done}. Take it out of the file, then try again.`;
}

/**
 * What a person reads when a push would carry a held secret in its history (the lead, 13:04Z; fresh readers,
 * 13:16Z and AUDIT2's at 16:49Z). Send: where it is (an earlier version of a file, a name in one, or the note
 * saved with a change) that `repository` does not have, as far as this check knows, that going now would send it
 * too, and the two true ways forward, as steps a person can do and no claim about what a service has done: a new
 * value made and the old one turned off where it was issued (making a new one does not turn the old one off at
 * most services; until it is off, it is exposed), or a developer taking the old version out. Publish: the earlier
 * version is Halcyonic's own earlier publish, which left this Mac with it, so turning the old one off comes first
 * (AUDIT2, 16:49Z and 18:07Z; the lead, 18:08Z).
 * Never the value: a name that holds it is shown with it as the secret's name in brackets.
 */
export function heldHistoryMessage(
  hit: HeldInHistory,
  what: 'sent' | 'published',
  repository: string,
): string {
  if (what === 'published') {
    const where =
      hit.within === true
        ? `A name in ${hit.path === '' ? 'the top folder' : `the folder ${hit.path}`} of an earlier publish`
        : hit.named === true
          ? `A file's or folder's name in an earlier publish (${hit.path}, with the secret's name in place of its value)`
          : hit.path === null
            ? 'The description of an earlier publish'
            : `An earlier publish of ${hit.path}`;
    return `${where} held a secret this Mac keeps (${hit.what}), and it left this Mac with that publish, so nothing was published. Until the old ${hit.what} is turned off, treat it as exposed: turn it off with the service that gave it to you, make a new one there and store it on this Mac; then Publish works.`;
  }
  const where =
    hit.within === true
      ? `A name in ${hit.path === '' ? 'the top folder' : `the folder ${hit.path}`} of a saved version, which ${repository} does not have,`
      : hit.named === true
        ? `A file's or folder's name in a saved version that ${repository} does not have (${hit.path}, with the secret's name in place of its value)`
        : hit.path === null
          ? `The description saved with an earlier change, which ${repository} does not have,`
          : `A saved version of ${hit.path}, which ${repository} does not have,`;
  const developer =
    hit.path === null
      ? 'ask a developer to change that description'
      : 'ask a developer to take that old version out';
  return `${where} holds a secret this Mac keeps (${hit.what}), and sending now would send it too, so nothing was sent. To send, make a new ${hit.what} with the service that gave it to you and turn the old one off there, then store the new one on this Mac and send again. The old one is still in that earlier version and goes with it: until it is turned off, treat it as exposed. Or ${developer}.`;
}

/** Why a share's built copy is not read: a link out of it (by its path in the copy), or more than a share reads. */
export class FolderRefused extends Error {
  readonly kind: 'link_out' | 'too_large';
  readonly path: string | null;
  constructor(kind: 'link_out' | 'too_large', path: string | null) {
    super(
      kind === 'link_out'
        ? `${path} leads out of the built copy`
        : 'the built copy is larger than a share reads',
    );
    this.kind = kind;
    this.path = path;
  }
}

/**
 * The first file under `root` (a check's built copy, as a share serves it) that holds a held secret, by its path
 * from `root`, or null, in a stable order. A link that leads out of the copy (its real place outside the copy's
 * real folder) is refused by its path (FolderRefused `link_out`), whatever is held, since the preview's server
 * would serve what it leads to; Send and Publish never send such a link either. A link within the copy is followed:
 * to a file, read as that file; to a folder, walked, each real folder once. Bounded (FolderRefused `too_large`). A
 * folder that cannot be read is an error, so nothing is shared unread.
 */
export async function heldInFolder(
  root: string,
  secrets: Iterable<HeldSecret>,
): Promise<HeldHit | null> {
  const forms = heldForms(secrets);
  const top = await realpath(root);
  const inside = (real: string) => real === top || real.startsWith(`${top}/`);
  const seen = new Set<string>();
  let files = 0;
  let bytes = 0;
  const walk = async (dir: string): Promise<HeldHit | null> => {
    const real = await realpath(dir);
    if (seen.has(real)) return null;
    seen.add(real);
    for (const entry of (await readdir(dir)).sort()) {
      const path = join(dir, entry);
      const shown = relative(root, path).split('\\').join('/');
      const named = heldInName(shown, forms);
      if (named !== null) return { path: shownName(shown, forms), what: named, named: true };
      let found = await lstat(path);
      if (found.isSymbolicLink()) {
        const target = await realpath(path).catch(() => null);
        if (target === null) {
          // A link whose target is not there serves nothing now, but would serve what appears there. Its target is
          // text the system resolves link by link, which no reading of the text alone can follow (critic, b116): one
          // that is absolute or climbs (holds a .. part) is refused, and one that does neither, as `missing.js`, stays
          // within the copy.
          const written = await readlink(path);
          if (
            written.startsWith('/') ||
            written.split('/').includes('..') ||
            !inside(resolve(await realpath(dirname(path)), written))
          )
            throw new FolderRefused('link_out', shown);
          continue;
        }
        if (!inside(target)) throw new FolderRefused('link_out', shown);
        found = await stat(path);
      }
      if (found.isDirectory()) {
        const hit = await walk(path);
        if (hit !== null) return hit;
      } else if (found.isFile()) {
        files += 1;
        bytes += found.size;
        if (files > FOLDER_FILES_MAX || bytes > FOLDER_BYTES_MAX)
          throw new FolderRefused('too_large', null);
        if (forms.length === 0) continue;
        const what = heldIn(await readFile(path), forms);
        if (what !== null) return { path: shown, what };
      }
    }
    return null;
  };
  return walk(root);
}
