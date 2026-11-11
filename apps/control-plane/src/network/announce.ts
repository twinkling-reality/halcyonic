import { type ChildProcess, execFile, spawn as nodeSpawn } from 'node:child_process';
import { readFileSync, rmSync, writeFileSync } from 'node:fs';

/**
 * The computer announcing itself on its Wi-Fi while a person has a pairing window open (ADR 0017,
 * amendment of 2026-11-11), so a headset can offer it by name and the person types only the code.
 * Nothing is announced at any other time. Discovery is never trusted: the code and the certificate the
 * headset pins decide, as without it.
 */
export interface Announcer {
  /** Starts announcing; already announcing, nothing changes. */
  start(): void;
  /** Stops announcing; not announcing, nothing changes. */
  stop(): void;
}

/** The DNS-SD service type a headset asks for. */
export const SERVICE_TYPE = '_halcyonic._tcp';

/** What the computer announces: its name, the listener's port and the start of its certificate's hash. */
export interface Announcement {
  /** The computer's Bonjour name without `.local`, which every device on its network already sees. */
  readonly name: string;
  readonly port: number;
  /** The first 16 hex of the listener certificate's SHA-256, public, so a headset can drop a wrong answer. */
  readonly pinPrefix: string;
}

/**
 * Whether a listener on `host` is one a headset could reach, and so worth announcing: not when it
 * listens on a loopback address, as the tests' control planes do.
 */
export function reachableFromTheNetwork(host: string): boolean {
  const address = host.toLowerCase();
  return !(address.startsWith('127.') || address === '::1' || address.startsWith('::ffff:127.'));
}

/** The macOS tool that registers a service with the system's own mDNSResponder. */
const DNS_SD = '/usr/bin/dns-sd';

/**
 * Announces through macOS's own mDNSResponder: `dns-sd -R` registers the service while it runs, and the
 * registration ends with it. No other platform announces. `describe` is read at each start, so the port
 * is the one the listener took; with nothing to describe, nothing is announced.
 */
export function dnsSdAnnouncer(
  describe: () => Announcement | null,
  options: {
    readonly spawn?: (command: string, args: readonly string[]) => ChildProcess;
    readonly platform?: NodeJS.Platform;
    /** Runs `end` when this process exits, until the returned function is called; tests give their own. */
    readonly atExit?: (end: () => void) => () => void;
    /**
     * A file that holds the running `dns-sd`'s pid while it announces, so a control plane killed
     * outright has its announcement ended at its next start (`endLeftoverAnnouncement`).
     */
    readonly record?: string;
  } = {},
): Announcer {
  const spawn =
    options.spawn ??
    ((command: string, args: readonly string[]) => nodeSpawn(command, args, { stdio: 'ignore' }));
  const platform = options.platform ?? process.platform;
  const atExit =
    options.atExit ??
    ((end: () => void) => {
      process.once('exit', end);
      return () => process.off('exit', end);
    });
  let running: ChildProcess | null = null;
  let forget: (() => void) | null = null;
  const end = () => {
    const child = running;
    running = null;
    forget?.();
    forget = null;
    if (child === null) return;
    child.kill('SIGTERM');
    if (options.record !== undefined) rmSync(options.record, { force: true });
  };
  return {
    start() {
      if (running !== null || platform !== 'darwin') return;
      const announcement = describe();
      if (announcement === null) return;
      const child = spawn(DNS_SD, [
        '-R',
        announcement.name,
        SERVICE_TYPE,
        'local',
        String(announcement.port),
        `pin=${announcement.pinPrefix}`,
      ]);
      // A tool that could not start leaves pairing as it was: the person types the name.
      child.on('error', () => {
        if (running === child) end();
      });
      child.on('exit', () => {
        if (running === child) end();
      });
      running = child;
      if (options.record !== undefined && child.pid !== undefined) {
        writeFileSync(options.record, String(child.pid), { mode: 0o600 });
      }
      // A control plane that exits without closing its window takes the announcement with it; one
      // killed outright cannot: its next start ends that dns-sd by the record.
      forget = atExit(end);
    },
    stop: end,
  };
}

/** The command line of a running process, or null when there is none; read with `ps`. */
function commandLineOf(pid: number): Promise<string | null> {
  return new Promise((resolve) => {
    execFile('/bin/ps', ['-o', 'args=', '-p', String(pid)], { timeout: 2_000 }, (error, stdout) => {
      resolve(error === null ? stdout.trim() : null);
    });
  });
}

/**
 * Ends an announcement a control plane killed outright left behind, by the pid it recorded: only
 * when that process is still a `dns-sd` registering this service, so a pid reused since is left
 * alone. The record goes either way.
 */
export async function endLeftoverAnnouncement(
  record: string,
  options: {
    readonly commandLine?: (pid: number) => Promise<string | null>;
    readonly kill?: (pid: number, signal: NodeJS.Signals) => void;
  } = {},
): Promise<void> {
  let pid: number;
  try {
    pid = Number.parseInt(readFileSync(record, 'utf8'), 10);
  } catch {
    return;
  }
  if (Number.isSafeInteger(pid) && pid > 1) {
    const line = await (options.commandLine ?? commandLineOf)(pid);
    if (line?.startsWith(`${DNS_SD} -R `) === true && line.includes(` ${SERVICE_TYPE} `)) {
      try {
        (options.kill ?? process.kill)(pid, 'SIGTERM');
      } catch {
        // Gone meanwhile: nothing announces.
      }
    }
  }
  rmSync(record, { force: true });
}
