import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstatSync, readFileSync } from 'node:fs';
import { chmod, mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Halcyonic's small native programs (S1), each built on this Mac from a tracked source in `native/`
 * by `pnpm mac-setup launcher`. Each lives in the data folder, outside every folder an agent may
 * write, runnable only by its owner; its hash is recorded when it is built and checked before every
 * use, and one missing or changed is not used.
 *
 * The launcher (`disclaim.c`) starts the check's and the play's browser as its own responsible
 * process, so it is given none of the macOS privacy permissions of the app that started Halcyonic.
 */
export interface NativeProgram {
  /** Its file name in `<data>/bin`. */
  readonly name: string;
  /** The tracked source it is built from. */
  readonly source: string;
  /** Apple frameworks it links. */
  readonly frameworks: readonly string[];
}

export const LAUNCHER: NativeProgram = {
  name: 'halcyonic-disclaim',
  source: fileURLToPath(new URL('../../native/disclaim.c', import.meta.url)),
  frameworks: [],
};

/** The tracked source the launcher is built from. */
export const LAUNCHER_SOURCE = LAUNCHER.source;

/** Where a native program is built in the data folder. */
export function programPath(dataDir: string, program: NativeProgram): string {
  return join(dataDir, 'bin', program.name);
}

/**
 * Why a native program may not be used now, or null: it must be a plain file of this user's that no
 * one else can change, and hash to what was recorded when it was built.
 */
export function programProblem(dataDir: string, program: NativeProgram): string | null {
  const path = programPath(dataDir, program);
  const what = program === LAUNCHER ? 'The launcher' : `${program.name}`;
  let stats: ReturnType<typeof lstatSync>;
  try {
    stats = lstatSync(path);
  } catch {
    return `${what} is not set up: run pnpm mac-setup launcher.`;
  }
  if (!stats.isFile()) return `${what} is not a plain file.`;
  if (process.getuid !== undefined && stats.uid !== process.getuid())
    return `${what} belongs to another user.`;
  if ((stats.mode & 0o022) !== 0) return `${what} is one others can change.`;
  let recorded: string;
  try {
    recorded = readFileSync(`${path}.sha256`, 'utf8').trim();
  } catch {
    return `${what} has no recorded hash: run pnpm mac-setup launcher.`;
  }
  const actual = createHash('sha256').update(readFileSync(path)).digest('hex');
  if (actual !== recorded) return `${what} has changed since it was built.`;
  return null;
}

/** A native program's path when it may be used now, else null. */
export function programFor(dataDir: string, program: NativeProgram): string | null {
  return programProblem(dataDir, program) === null ? programPath(dataDir, program) : null;
}

/**
 * Builds a native program from its tracked source with `clang`, into the data folder, replacing any
 * earlier one, and records its hash. Resolves with the hash.
 */
export async function installProgram(
  program: NativeProgram,
  options: { readonly dataDir: string; readonly clang: string },
): Promise<string> {
  const folder = join(options.dataDir, 'bin');
  await mkdir(folder, { recursive: true, mode: 0o700 });
  await chmod(folder, 0o700);
  const path = programPath(options.dataDir, program);
  const building = join(folder, `.${program.name}.${process.pid}`);
  try {
    await new Promise<void>((resolve, reject) =>
      execFile(
        options.clang,
        [
          '-Wall',
          '-Wextra',
          '-Werror',
          '-O2',
          '-o',
          building,
          program.source,
          ...program.frameworks.flatMap((framework) => ['-framework', framework]),
        ],
        { env: { PATH: '/usr/bin:/bin' }, timeout: 120_000 },
        (error) =>
          error === null ? resolve() : reject(new Error(`${program.name} did not build`)),
      ),
    );
    await chmod(building, 0o500);
    const sha256 = createHash('sha256')
      .update(await readFile(building))
      .digest('hex');
    // The record is written first: a program without its record is never used.
    await writeFile(`${building}.sha256`, `${sha256}\n`, { mode: 0o600 });
    await rename(`${building}.sha256`, `${path}.sha256`);
    await rename(building, path);
    return sha256;
  } finally {
    await rm(building, { force: true });
    await rm(`${building}.sha256`, { force: true });
  }
}

/** Where the launcher is built in the data folder. */
export function launcherPath(dataDir: string): string {
  return programPath(dataDir, LAUNCHER);
}

/** Why the launcher may not be used now, or null. */
export function launcherProblem(dataDir: string): string | null {
  return programProblem(dataDir, LAUNCHER);
}

/** The launcher's path when it may be used now, else null. */
export function launcherFor(dataDir: string): string | null {
  return programFor(dataDir, LAUNCHER);
}

/** Builds the launcher; resolves with its hash. */
export function installLauncher(options: {
  readonly dataDir: string;
  readonly clang: string;
}): Promise<string> {
  return installProgram(LAUNCHER, options);
}
