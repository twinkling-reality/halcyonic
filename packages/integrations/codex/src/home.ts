import { lstat, mkdir } from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';
import { RuntimeActionError } from '@halcyonic/runtime-core';

/**
 * Makes sure Codex's home, the folder Halcyonic gives Codex as its `CODEX_HOME`, is Halcyonic's
 * own and holds no sign-in, before every launch and every start. Its mode is never changed. A missing home is made with mode 700; its parent
 * must exist. An existing one must be a real folder, not a link, owned by this user and closed to
 * others, with no `auth.json`: Codex keeps a sign-in there, and through Halcyonic it runs only on
 * models served on this Mac, with none. Codex writes the rest itself: its databases, logs,
 * installation id and each thread's rollout under `sessions`.
 */
export async function prepareHome(home: string): Promise<void> {
  if (!isAbsolute(home)) {
    throw refused(`Codex's home must be an absolute path, got "${home}".`);
  }
  let stats = await lstatOrNull(home);
  if (stats === null) {
    try {
      await mkdir(home, { mode: 0o700 });
    } catch (error) {
      if (!isCode(error, 'EEXIST')) {
        throw refused(`Could not make Codex's home ${home}: ${describe(error)}.`);
      }
    }
    stats = await lstatOrNull(home);
  }
  if (stats === null || !stats.isDirectory()) {
    throw refused(
      `Codex's home ${home} is not a folder, or is a link. Move it away; Halcyonic makes a new one.`,
    );
  }
  const uid = process.getuid?.();
  if (uid !== undefined && stats.uid !== uid) {
    throw refused(
      `Codex's home ${home} belongs to another user. Move it away; Halcyonic makes a new one.`,
    );
  }
  if ((stats.mode & 0o077) !== 0) {
    // Never made private again here: what others could reach while it was open can't be told.
    throw refused(
      `Other users can open Codex's home ${home}, so it may hold what they put there. Move it away; Halcyonic makes a new one.`,
    );
  }
  if ((await lstatOrNull(join(home, 'auth.json'))) !== null) {
    throw refused(
      `Codex's home ${home} holds a sign-in (auth.json). Halcyonic runs Codex only on models served on this Mac, without one: move the file away.`,
    );
  }
}

async function lstatOrNull(path: string) {
  try {
    return await lstat(path);
  } catch (error) {
    if (isCode(error, 'ENOENT')) return null;
    throw refused(`Could not check ${path}: ${describe(error)}.`);
  }
}

function refused(message: string): RuntimeActionError {
  return new RuntimeActionError('runtime_unavailable', message);
}

function isCode(error: unknown, code: string): boolean {
  return error instanceof Error && 'code' in error && error.code === code;
}

function describe(error: unknown): string {
  return isCode(error, 'EACCES')
    ? 'permission denied'
    : error instanceof Error && 'code' in error && typeof error.code === 'string'
      ? error.code
      : 'it failed';
}
