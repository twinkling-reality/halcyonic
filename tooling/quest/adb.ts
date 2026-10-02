/**
 * Runs adb for the device tools. adb comes with Unity's Android module; `HALCYONIC_ADB` names
 * another. Only `dumpsys`, `am`, `logcat` and `date` are run: these tools read the headset and
 * start or stop Halcyonic, and change nothing else on it.
 */
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);

export const ADB =
  process.env.HALCYONIC_ADB ??
  '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb';

export const PACKAGE = 'com.halcyonic.xr';
export const ACTIVITY = `${PACKAGE}/com.unity3d.player.UnityPlayerGameActivity`;

export async function adb(...args: string[]): Promise<string> {
  const { stdout } = await run(ADB, args, { maxBuffer: 16 * 1024 * 1024, timeout: 20_000 });
  return stdout;
}

/**
 * Follows the headset's log from now, with each line's device time, for the tags given (for
 * example `Unity:I`), calling `line` for each. Returns a function that stops following.
 */
export function follow(tags: readonly string[], line: (text: string) => void): () => void {
  const child = spawn(ADB, ['logcat', '-T', '1', '-v', 'epoch', '-s', ...tags], {
    stdio: ['ignore', 'pipe', 'ignore'],
  });
  let pending = '';
  child.stdout.setEncoding('utf8');
  child.stdout.on('data', (chunk: string) => {
    pending += chunk;
    const lines = pending.split('\n');
    pending = lines.pop() ?? '';
    for (const text of lines) line(text);
  });
  return () => {
    child.kill();
  };
}

export const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** `--name value` from the command line, as a number, or the default. */
export function option(name: string, fallback: number): number {
  const index = process.argv.indexOf(`--${name}`);
  const value = index < 0 ? undefined : Number(process.argv[index + 1]);
  if (value === undefined) return fallback;
  if (!Number.isFinite(value) || value <= 0) throw new Error(`--${name} needs a positive number`);
  return value;
}

/** `--name value` from the command line, as text, or the default. */
export function textOption(name: string, fallback: string): string {
  const index = process.argv.indexOf(`--${name}`);
  return index < 0 ? fallback : (process.argv[index + 1] ?? fallback);
}
