/**
 * Runs adb for the device tools. adb comes with Unity's Android module; `HALCYONIC_ADB` names
 * another. Every command goes to the headset's own adb server, `-L tcp:127.0.0.1:<port>`, with the
 * port from `HALCYONIC_ADB_PORT` (5038), and names the headset with `-s`: the serial from
 * `HALCYONIC_QUEST_SERIAL`, else the one headset that server lists. The shared server on 5037
 * belongs to other projects and Unity's throwaway one on 5039 never has the headset: both are
 * refused. `-L` keeps a shell that exports Unity's `ADB_SERVER_SOCKET` from sending commands to
 * 5039. adb starts a server of its own on the port when none answers there, and restarts one whose
 * version differs from its own, so `requireHeadset` first asks the server its version with adb's own
 * `host:version` request and stops unless it answers with this adb's: the tools never start or
 * restart a server.
 *
 * `check`, `session` and `cold-start` run only `dumpsys`, `am`, `logcat`, `date`, `pidof`, `ps`,
 * `reverse --list` and `stat` (directly or through `run-as`): they read the headset and start or
 * stop Halcyonic, and change nothing else on it. `stat` reads a file's mode, size and type, never
 * its contents. What `device` runs besides is listed in device.ts.
 */
import { execFile, spawn } from 'node:child_process';
import { connect } from 'node:net';
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

/** 127.0.0.1, the address `requireHeadset` asks, so the check and adb mean one listener. */
const server = () => ['-L', `tcp:127.0.0.1:${serverPort()}`];

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

export function adb(...args: string[]): Promise<string> {
  return adbFor(20_000, ...args);
}

/** As `adb`, for a command that runs longer than adb's usual 20 s, such as a screenrecord clip: stopped after `ms`. */
export async function adbFor(ms: number, ...args: string[]): Promise<string> {
  try {
    const { stdout } = await run(adbPath(), [...(await target()), ...args], {
      maxBuffer: 16 * 1024 * 1024,
      timeout: ms,
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

/**
 * The adb server's version on 127.0.0.1:`port`, read with adb's own `host:version` request, or null
 * when nothing there answers as an adb server.
 */
export function serverVersion(port: number): Promise<number | null> {
  return new Promise((resolve) => {
    const socket = connect({ host: '127.0.0.1', port });
    let answer = '';
    const settle = (version: number | null) => {
      socket.destroy();
      resolve(version);
    };
    socket.setTimeout(5000, () => settle(null));
    socket.once('connect', () => socket.write('000chost:version'));
    socket.on('data', (chunk: Buffer) => {
      answer += chunk.toString('latin1');
      if (answer.length < 12) return;
      const hex = /^OKAY0004([0-9a-fA-F]{4})/.exec(answer)?.[1];
      settle(hex === undefined ? null : Number.parseInt(hex, 16));
    });
    socket.once('end', () => settle(null));
    socket.once('error', () => settle(null));
  });
}

/** `adb version`: this adb's version, numbered as its server's is (1.0.41 is 41), or null. */
export function readClientVersion(text: string): number | null {
  const match = /Android Debug Bridge version \d+\.\d+\.(\d+)/.exec(text);
  return match?.[1] === undefined ? null : Number(match[1]);
}

/** This adb's version. `adb version` answers without reaching any server. */
async function clientVersion(): Promise<number | null> {
  try {
    const { stdout } = await run(adbPath(), ['version'], { timeout: 20_000 });
    return readClientVersion(stdout);
  } catch {
    return null;
  }
}

/** `adb devices -l`: the transport id the server gave `serial`'s connection, or null. */
export function readTransportId(text: string, serial: string): string | null {
  for (const line of text.split(/\r?\n/)) {
    const fields = line.trim().split(/\s+/);
    if (fields[0] !== serial) continue;
    if (fields[1] !== 'device') return null;
    const id = fields
      .find((field) => field.startsWith('transport_id:'))
      ?.slice('transport_id:'.length);
    return id !== undefined && /^\d+$/.test(id) ? id : null;
  }
  return null;
}

/**
 * The transport id of the headset's connection, or null when unread. A USB reconnect drops every
 * reverse rule and gives a new id, so a reverse recorded with another id is gone; an adb server
 * restart drops them too and can give the same id again.
 */
export async function transportId(): Promise<string | null> {
  try {
    const named = await serial();
    const { stdout } = await run(adbPath(), [...server(), 'devices', '-l'], { timeout: 20_000 });
    return readTransportId(stdout, named);
  } catch {
    return null;
  }
}

/** Stops with one plain line unless exactly one headset is attached and authorized. */
export async function requireHeadset(): Promise<void> {
  const port = serverPort();
  const spoken = await serverVersion(port);
  if (spoken === null) {
    console.error(
      `No adb server listens on port ${port}, the headset's: the device tools never start one, so nothing ran.`,
    );
    process.exit(1);
  }
  const mine = await clientVersion();
  if (mine !== spoken) {
    console.error(
      `The adb server on port ${port} speaks adb version ${spoken}, and this adb ${mine ?? 'an unknown version'}: adb would restart the headset's server, so nothing ran. Name the adb that started it with HALCYONIC_ADB.`,
    );
    process.exit(1);
  }
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
