import { spawn } from 'node:child_process';
import { realpath } from 'node:fs/promises';
import { join } from 'node:path';
import { SANDBOX_EXEC } from './sandbox-profile.ts';

/**
 * A confined server's temporary folder (ADR 0041): `tmp` in its own Codex home, given to the server
 * as `TMPDIR`, with zsh's `TMPPREFIX` inside it. A command of the server's may write anything in
 * its home, so it may leave a link in the folder, or move the home away and put a link in its
 * place (tried on this Mac). The folder is therefore made and removed only by macOS's own
 * `chmod`, `rm` and `mkdir`, inside a profile that may write that folder alone: a path through such
 * a link leads outside the folder and is refused, so neither can reach anything the server could
 * not reach itself.
 */

/** How long each of those commands may run. */
const COMMAND_MS = 60_000;

/**
 * Where the folder is: `homes`, the folder that holds every confined home, which is Halcyonic's,
 * spelled as the file system resolves it, then the home's key and `tmp`, never resolved, since the
 * home may have been replaced with a link.
 */
export async function tempFolder(homes: string, key: string): Promise<string> {
  return join(await realpath(homes), key, 'tmp');
}

/**
 * The profile `chmod`, `rm` and `mkdir` run inside: every write but in `temp` is refused, and so is
 * the network. Nothing else is closed, since they only change that folder.
 */
export function tempProfile(temp: string): string {
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    '(deny file-write*)',
    `(allow file-write* (subpath ${JSON.stringify(temp)}))`,
    '',
  ].join('\n');
}

/**
 * Removes the folder: folders a command made unwritable, as Go's module cache is, are made writable
 * first, and a link in it is removed, never followed. Whether anything is left is not checked here;
 * `makeTemp` never makes the folder over anything left.
 */
export async function removeTemp(temp: string): Promise<void> {
  const profile = tempProfile(temp);
  await inside(profile, ['/bin/chmod', '-R', 'u+rwX', '--', temp]);
  await inside(profile, ['/bin/rm', '-rf', '--', temp]);
}

/** Makes the folder anew, empty and closed to others (mode 700). Resolves whether it was made. */
export async function makeTemp(temp: string): Promise<boolean> {
  await removeTemp(temp);
  return (await inside(tempProfile(temp), ['/bin/mkdir', '-m', '700', '--', temp])) === 0;
}

/** Runs a command inside a profile, with the system's PATH alone; resolves its exit code. */
function inside(profile: string, command: readonly string[]): Promise<number | null> {
  return new Promise((resolve) => {
    const child = spawn(SANDBOX_EXEC, ['-p', profile, ...command], {
      env: { PATH: '/usr/bin:/bin' },
      stdio: 'ignore',
      timeout: COMMAND_MS,
    });
    child.once('error', () => resolve(null));
    child.once('exit', (code) => resolve(code));
  });
}
