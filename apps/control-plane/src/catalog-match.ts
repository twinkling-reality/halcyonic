import { constants } from 'node:fs';
import { lstat, open, realpath } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import type { LocationsResponse } from '@halcyonic/contracts';

/**
 * Which folders the host lists are clones of which GitHub repositories, by their own git
 * configuration, for the projects gallery's suggestions (ADR 0039). A new read of one file inside
 * each listed folder, bound by AUDIT's G1 to G5 (SECURITY.md, "Project folders and clients"): only
 * `owner/name` survives it; the remote's URL, which can hold a credential, is never kept, returned
 * or logged. A match is inferred and grants nothing: only the person's link does. An agent can write
 * a folder's `.git/config`, so a match is never proof of what a folder holds.
 */

/** The most of a `.git/config` read. A real one is a few hundred bytes. */
export const CONFIG_MAX_BYTES = 64 * 1024;

/** The most folders one match reads, across every root. */
export const MAX_FOLDERS = 400;

/** How long one match may take, in milliseconds, checked between folders. */
export const BUDGET_MS = 2000;

const OWNER = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;
const NAME = /^[A-Za-z0-9._-]{1,100}$/;

export interface FolderOrigin {
  readonly root: string;
  readonly folderName: string;
  readonly path: string;
  /** The repository's `owner/name`, folded to lower case. */
  readonly fullName: string;
}

export interface MatchOptions {
  /** Halcyonic's own data: a folder holding any of these is never read. */
  readonly protect: readonly string[];
  readonly maxFolders?: number;
  readonly budgetMs?: number;
  readonly now?: () => number;
}

/**
 * Reads the listed folders' origins, on demand, within `maxFolders` and `budgetMs`. Folders the
 * listing does not mark as repositories, and folders holding Halcyonic's data, are not read.
 */
export async function matchFolders(
  listing: LocationsResponse,
  options: MatchOptions,
): Promise<FolderOrigin[]> {
  const now = options.now ?? Date.now;
  const started = now();
  const budget = options.budgetMs ?? BUDGET_MS;
  const limit = options.maxFolders ?? MAX_FOLDERS;
  const protect = await Promise.all(
    options.protect.map((path) => realpath(path).catch(() => path)),
  );
  const found: FolderOrigin[] = [];
  let read = 0;
  for (const root of listing.roots) {
    if (root.status !== 'available') continue;
    for (const folder of root.folders) {
      if (folder.repository !== true) continue;
      if (read >= limit || now() - started > budget) return found;
      read += 1;
      if (await holdsAny(folder.path, protect)) continue;
      const fullName = await readOrigin(folder.path);
      if (fullName !== null)
        found.push({ root: root.path, folderName: folder.name, path: folder.path, fullName });
    }
  }
  return found;
}

/**
 * The `owner/name` of a folder's origin on github.com, or null (AUDIT G1): only from a `.git` that is
 * itself a directory, never a link or a `.git` file pointing elsewhere, whose `config` is a regular
 * file of at most 64 KiB, opened without following a link and without waiting on a pipe, and checked
 * again through the open handle. Git is never run.
 */
export async function readOrigin(folder: string): Promise<string | null> {
  try {
    const git = await lstat(join(folder, '.git'));
    if (!git.isDirectory()) return null;
    const path = join(folder, '.git', 'config');
    const named = await lstat(path);
    if (!named.isFile() || named.size > CONFIG_MAX_BYTES) return null;
    const file = await open(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
    try {
      const opened = await file.stat();
      if (!opened.isFile() || opened.ino !== named.ino || opened.dev !== named.dev) return null;
      const buffer = Buffer.alloc(CONFIG_MAX_BYTES + 1);
      const { bytesRead } = await file.read(buffer, 0, buffer.length, 0);
      if (bytesRead > CONFIG_MAX_BYTES) return null;
      const url = originUrl(buffer.subarray(0, bytesRead).toString('utf8'));
      return url === null ? null : originFullName(url);
    } finally {
      await file.close();
    }
  } catch {
    return null;
  }
}

/**
 * The one `url` of `[remote "origin"]` (AUDIT G3), or null when there is none or several. Sections are
 * read as git writes them, the section name in any case and the subsection exactly; `include`,
 * `includeIf` and `insteadOf` are not applied, so nothing outside this file decides the answer.
 */
export function originUrl(config: string): string | null {
  let inOrigin = false;
  const urls: string[] = [];
  for (const raw of config.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '' || line.startsWith('#') || line.startsWith(';')) continue;
    const section = /^\[\s*([A-Za-z0-9.-]+)(?:\s+"((?:[^"\\]|\\.)*)")?\s*\]$/.exec(line);
    if (section) {
      inOrigin = section[1]?.toLowerCase() === 'remote' && section[2] === 'origin';
      continue;
    }
    if (line.startsWith('[')) {
      inOrigin = false;
      continue;
    }
    if (!inOrigin) continue;
    const entry = /^([A-Za-z][A-Za-z0-9-]*)\s*=\s*(.*)$/.exec(line);
    if (entry?.[1]?.toLowerCase() !== 'url') continue;
    let value = (entry[2] ?? '').trim();
    if (value.length >= 2 && value.startsWith('"') && value.endsWith('"'))
      value = value.slice(1, -1);
    urls.push(value);
  }
  return urls.length === 1 ? (urls[0] ?? null) : null;
}

/**
 * A remote's URL as `owner/name` on github.com, folded to lower case, or null (AUDIT G2). Any user
 * information, where a credential would sit, is dropped before anything else. The https, ssh and
 * scp-like forms are read; the host must be exactly github.com, and the owner and name GitHub's own
 * characters, the name neither `.` nor `..`.
 */
export function originFullName(url: string): string | null {
  let host: string;
  let path: string;
  const scheme = /^([a-z][a-z0-9+.-]*):\/\/([^/]*)(\/.*)?$/i.exec(url);
  if (scheme) {
    const protocol = scheme[1]?.toLowerCase();
    if (protocol !== 'https' && protocol !== 'ssh') return null;
    const authority = (scheme[2] ?? '').slice((scheme[2] ?? '').lastIndexOf('@') + 1);
    const port = /^(.*):([0-9]{1,5})$/.exec(authority);
    host = port ? (port[1] ?? '') : authority;
    path = scheme[3] ?? '';
  } else {
    // The scp-like form, `[user@]host:owner/name`; a path with no host is not a remote on GitHub.
    const afterUser = url.slice(url.lastIndexOf('@') + 1);
    const colon = afterUser.indexOf(':');
    if (colon <= 0 || afterUser.startsWith('/')) return null;
    host = afterUser.slice(0, colon);
    path = `/${afterUser.slice(colon + 1)}`;
  }
  if (host.toLowerCase() !== 'github.com') return null;
  const parts = path.replace(/\/$/, '').split('/');
  if (parts.length !== 3 || parts[0] !== '') return null;
  const owner = parts[1] ?? '';
  const name = (parts[2] ?? '').replace(/\.git$/, '');
  if (!OWNER.test(owner) || !NAME.test(name) || name === '.' || name === '..') return null;
  return `${owner}/${name}`.toLowerCase();
}

/** Whether `folder`, as it really is, holds any of `protect`. */
async function holdsAny(folder: string, protect: readonly string[]): Promise<boolean> {
  let real: string;
  try {
    real = await realpath(folder);
  } catch {
    return true;
  }
  return protect.some((path) => {
    const inside = relative(real, path);
    return (
      inside === '' || (!inside.startsWith('..') && !inside.startsWith(sep) && inside !== path)
    );
  });
}
