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
    if (type !== 'blob' || object === undefined) continue;
    blobs.push({ object, size: Number(size), path: entry.slice(tab + 1) });
  }
  let batch: typeof blobs = [];
  let bytes = 0;
  const read = async (): Promise<HeldHit | null> => {
    if (batch.length === 0) return null;
    const asked = batch;
    batch = [];
    bytes = 0;
    const out = await git(
      ['cat-file', '--batch'],
      Buffer.from(`${asked.map((blob) => blob.object).join('\n')}\n`),
    );
    if (out.code !== 0) throw new Error('the tree could not be read');
    // Each answer is `<object> blob <size>\n<content>\n`, in the order asked.
    let at = 0;
    for (const blob of asked) {
      const newline = out.stdout.indexOf(0x0a, at);
      if (newline < 0) throw new Error('the tree could not be read');
      const header = out.stdout.subarray(at, newline).toString('utf8').split(' ');
      const size = Number(header[2]);
      if (header[0] !== blob.object || !Number.isInteger(size))
        throw new Error('the tree could not be read');
      const content = out.stdout.subarray(newline + 1, newline + 1 + size);
      at = newline + 1 + size + 1;
      const what = heldIn(content, forms);
      if (what !== null) return { path: blob.path, what };
    }
    return null;
  };
  for (const blob of blobs) {
    if (batch.length > 0 && bytes + blob.size > BATCH_BYTES) {
      const hit = await read();
      if (hit !== null) return hit;
    }
    batch.push(blob);
    bytes += blob.size;
  }
  return read();
}

/** What a person reads when a tree holds a held secret: the file and which secret, never the value. */
export function heldMessage(hit: HeldHit, what: 'sent' | 'published' | 'saved' | 'shared'): string {
  const done = {
    sent: 'nothing was sent',
    published: 'nothing was published',
    saved: 'no copy was saved',
    shared: 'nothing was shared',
  }[what];
  return `${hit.path} holds a secret this Mac keeps (${hit.what}), so ${done}. Take it out of the file, then try again.`;
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
