/**
 * Runs adb for the device tools. adb comes with Unity's Android module; `HALCYONIC_ADB` names
 * another. Every command goes to the headset's own adb server, `-L tcp:localhost:<port>`, with the
 * port from `HALCYONIC_ADB_PORT` (5038), and names the headset with `-s`: the serial from
 * `HALCYONIC_QUEST_SERIAL`, else the one headset that server lists. The shared server on 5037
 * belongs to other projects and Unity's throwaway one on 5039 never has the headset: both are
 * refused. `-L` keeps a shell that exports Unity's `ADB_SERVER_SOCKET` from sending commands to
 * 5039.
 *
 * `check`, `session` and `cold-start` run only `dumpsys`, `am`, `logcat`, `date`, `pidof`, `ps`,
 * `reverse --list` and `stat` (directly or through `run-as`): they read the headset and start or
 * stop Halcyonic, and change nothing else on it. `stat` reads a file's mode, size and type, never
 * its contents. What `device` runs besides is listed in device.ts.
 */
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);

const adbPath = () =>
  process.env.HALCYONIC_ADB ??
  '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb';

export const PACKAGE = 'com.halcyonic.xr';
export const ACTIVITY = `${PACKAGE}/com.unity3d.player.UnityPlayerGameActivity`;

/** The headset's adb server port: `HALCYONIC_ADB_PORT`, or 5038. Never 5037, the shared server. */
export function serverPort(env: NodeJS.ProcessEnv = process.env): number {
  const given = env.HALCYONIC_ADB_PORT;
  if (given === undefined || given === '') return 5038;
  const port = Number(given);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error('HALCYONIC_ADB_PORT must be a port number');
  }
  if (port === 5037) {
    throw new Error(
      'adb port 5037 is the shared server other projects use; the headset has its own (5038)',
    );
  }
  if (port === 5039) {
    throw new Error(
      "adb port 5039 is Unity's own throwaway server, which never has the headset; it has its own (5038)",
    );
  }
  return port;
}

const server = () => ['-L', `tcp:localhost:${serverPort()}`];

/** The serial of the one authorized headset `adb devices` lists, or null when there are none or several. */
export function readSerial(text: string): string | null {
  const ready = text
    .split(/\r?\n/)
    .map((line) => /^(\S+)\s+device$/.exec(line.trim())?.[1])
    .filter((serial): serial is string => serial !== undefined);
  return ready.length === 1 ? (ready[0] ?? null) : null;
}

let knownSerial: string | null = null;

async function serial(): Promise<string> {
  const given = process.env.HALCYONIC_QUEST_SERIAL;
  if (given) return given;
  if (knownSerial !== null) return knownSerial;
  const { stdout } = await run(adbPath(), [...server(), 'devices'], { timeout: 20_000 });
  knownSerial = readSerial(stdout);
  if (knownSerial === null) {
    throw new Error(
      `adb on port ${serverPort()} lists no single headset; set HALCYONIC_QUEST_SERIAL`,
    );
  }
  return knownSerial;
}

const target = async () => [...server(), '-s', await serial()];

export async function adb(...args: string[]): Promise<string> {
  try {
    const { stdout } = await run(adbPath(), [...(await target()), ...args], {
      maxBuffer: 16 * 1024 * 1024,
      timeout: 20_000,
    });
    return stdout;
  } catch (error) {
    // Never the error itself: it carries everything adb printed, which can name the headset or
    // hold what was read from it, and an unhandled one is printed whole.
    const failed = error as { code?: unknown; signal?: unknown };
    const how =
      typeof failed.code === 'number'
        ? `exit code ${failed.code}`
        : typeof failed.signal === 'string'
          ? `signal ${failed.signal}`
          : typeof failed.code === 'string'
            ? failed.code
            : 'it did not run';
    throw new Error(`adb ${args.slice(0, 2).join(' ')} failed (${how})`);
  }
}

/** What adb printed, standard output and error together, also when the command failed, as `stat` of a missing file does. */
export async function adbSaid(...args: string[]): Promise<string> {
  try {
    const { stdout, stderr } = await run(adbPath(), [...(await target()), ...args], {
      maxBuffer: 16 * 1024 * 1024,
      timeout: 20_000,
    });
    return `${stdout}${stderr}`;
  } catch (error) {
    const failed = error as { stdout?: string; stderr?: string };
    return `${failed.stdout ?? ''}${failed.stderr ?? ''}`;
  }
}

/**
 * Follows the headset's log from now, with each line's device time, for the tags given (for
 * example `Unity:I`), calling `line` for each. Returns a function that stops following. Call
 * `requireHeadset` first: it settles which headset this is.
 */
export function follow(tags: readonly string[], line: (text: string) => void): () => void {
  const named = process.env.HALCYONIC_QUEST_SERIAL || knownSerial;
  if (!named) throw new Error('follow needs requireHeadset first');
  const child = spawn(
    adbPath(),
    [...server(), '-s', named, 'logcat', '-T', '1', '-v', 'epoch', '-s', ...tags],
    {
      stdio: ['ignore', 'pipe', 'ignore'],
    },
  );
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

/** Stops with one plain line unless exactly one headset is attached and authorized. */
export async function requireHeadset(): Promise<void> {
  let state = '';
  try {
    state = (await adb('get-state')).trim();
  } catch {
    state = '';
  }
  if (state === 'device') return;
  console.error(
    `No headset ready on adb port ${serverPort()}: connect it by USB and accept USB debugging in it; \`adb -L tcp:localhost:${serverPort()} devices\` must list it as device.`,
  );
  process.exit(1);
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
