import { createHash } from 'node:crypto';
import { lstat, readdir, readFile, realpath } from 'node:fs/promises';
import { join, posix, relative, sep } from 'node:path';
import type { CheckRefusal, CheckRefusalKind } from '@halcyonic/contracts';
import { plainLine } from './check-output.ts';

/** A refusal a starter declares for its check (`check_refuses` in its `starter.json`): a kind and its own words. */
export interface DeclaredRefusal {
  readonly kind: CheckRefusalKind;
  readonly words: string;
}

/** The kinds this host knows how to find. A starter's entry of any other kind is not a refusal. */
export const REFUSAL_KINDS: readonly CheckRefusalKind[] = ['original_share_image'];

/** The largest file read or hashed, the most files a folder walk reads, and how deep it goes. */
const MAX_FILE_BYTES = 8 * 1024 * 1024;
const MAX_FILES = 5000;
const MAX_DEPTH = 8;
/** Folders a walk of the checked project passes over: its dependencies, its build, its version control. */
const PASSED_OVER = new Set(['node_modules', 'dist', '.git']);
/** The meta names and properties a page names its link-preview image by. */
const SHARE_NAMES = new Set([
  'og:image',
  'og:image:url',
  'og:image:secure_url',
  'twitter:image',
  'twitter:image:src',
]);

/**
 * The share images the original's own pages name (`<meta property="og:image">`, `twitter:image`),
 * by SHA-256: each `.html` file at the original's top level and in its `public/`, each named image
 * resolved as the page's server would serve it, from the folder or its `public/`. Only plain files
 * inside the original, never through a link, each at most 8 MiB. Empty when there are none.
 */
export async function shareImages(original: string): Promise<Set<string>> {
  const root = await realpath(original);
  const hashes = new Set<string>();
  for (const folder of [root, join(root, 'public')]) {
    let names: string[];
    try {
      names = await readdir(folder);
    } catch {
      continue;
    }
    for (const name of names.filter((each) => each.endsWith('.html')).slice(0, 20)) {
      const page = await plainFile(join(folder, name), root);
      if (page === null) continue;
      for (const target of shareTargets(page.toString('utf8'))) {
        for (const candidate of [join(root, target), join(root, 'public', target)]) {
          const image = await plainFile(candidate, root);
          if (image !== null) hashes.add(sha256(image));
        }
      }
    }
  }
  return hashes;
}

/** The paths a page's share-image tags name, relative and without query or fragment; never one leaving its folder. */
export function shareTargets(html: string): string[] {
  const targets: string[] = [];
  for (const tag of html.match(/<meta\b[^>]*>/gi) ?? []) {
    const named = attribute(tag, 'property') ?? attribute(tag, 'name');
    const content = attribute(tag, 'content');
    if (named === null || content === null || !SHARE_NAMES.has(named.toLowerCase())) continue;
    let path = content.trim();
    try {
      // An address names its path; a path stays as it is.
      if (/^[a-z][a-z0-9+.-]*:/i.test(path)) path = new URL(path).pathname;
      path = decodeURIComponent(path.split(/[?#]/)[0] ?? '');
    } catch {
      continue;
    }
    const normal = posix.normalize(`/${path}`).slice(1);
    if (normal === '' || normal.startsWith('..')) continue;
    targets.push(normal);
  }
  return targets;
}

function attribute(tag: string, name: string): string | null {
  const found = new RegExp(`\\b${name}\\s*=\\s*("([^"]*)"|'([^']*)')`, 'i').exec(tag);
  return found === null ? null : (found[2] ?? found[3] ?? null);
}

/** A plain file inside `root`, read whole, or null: a link, a folder, a file over the cap, or one outside. */
async function plainFile(path: string, root: string): Promise<Buffer | null> {
  try {
    const stat = await lstat(path);
    if (!stat.isFile() || stat.size > MAX_FILE_BYTES) return null;
    const real = await realpath(path);
    if (real !== root && !real.startsWith(root + sep)) return null;
    return await readFile(real);
  } catch {
    return null;
  }
}

function sha256(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

/**
 * What the checked folder's starter refuses in it (`check_refuses`), each kind found at most once,
 * with the starter's own words and the file, relative to `project`. A kind that needs the original
 * finds nothing without one. What cannot be read is passed over: a refusal is only ever found,
 * never assumed.
 */
export async function refusalsFor(
  declared: readonly DeclaredRefusal[],
  project: string,
  original: string | null,
): Promise<CheckRefusal[]> {
  const refusals: CheckRefusal[] = [];
  for (const each of declared) {
    if (each.kind === 'original_share_image') {
      if (original === null) continue;
      const shared = await shareImages(original).catch(() => new Set<string>());
      if (shared.size === 0) continue;
      const file = await firstFileAmong(project, shared);
      if (file !== null) refusals.push({ kind: each.kind, words: each.words, file });
    }
  }
  return refusals;
}

/** The first plain file of `project`, in name order, whose SHA-256 is one of `hashes`, relative and plain; or null. */
async function firstFileAmong(
  project: string,
  hashes: ReadonlySet<string>,
): Promise<string | null> {
  const root = await realpath(project);
  let seen = 0;
  const walk = async (folder: string, depth: number): Promise<string | null> => {
    let names: string[];
    try {
      names = (await readdir(folder)).sort();
    } catch {
      return null;
    }
    for (const name of names) {
      if (seen >= MAX_FILES) return null;
      const path = join(folder, name);
      const stat = await lstat(path).catch(() => null);
      if (stat === null || stat.isSymbolicLink()) continue;
      if (stat.isDirectory()) {
        if (depth >= MAX_DEPTH || PASSED_OVER.has(name)) continue;
        const found = await walk(path, depth + 1);
        if (found !== null) return found;
      } else if (stat.isFile() && stat.size <= MAX_FILE_BYTES) {
        seen += 1;
        const bytes = await readFile(path).catch(() => null);
        if (bytes !== null && hashes.has(sha256(bytes)))
          return plainLine(relative(root, path).split(sep).join('/')).slice(0, 300);
      }
    }
    return null;
  };
  return walk(root, 0);
}
