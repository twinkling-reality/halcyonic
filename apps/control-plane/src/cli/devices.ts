/**
 * Pairs and manages devices through the running control plane, on loopback with the access token
 * (ADR 0017, docs/internal/runbooks/LOCAL_DEVELOPMENT.md):
 *
 *   pnpm pair                          open a pairing window and show the address and code
 *   pnpm devices                       list paired devices
 *   pnpm devices revoke <device id>    stop accepting a device
 *
 * The control plane must run with HALCYONIC_NETWORK_HOST set for pairing. The code is printed here
 * and nowhere else.
 */
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import type {
  DevicesResponse,
  DeviceView,
  ErrorResponse,
  PairingOpenedResponse,
  PairingRefusalReason,
  PairingRefusalTally,
  PairingStatus,
} from '@halcyonic/contracts';
import { loadConfig } from '../config.ts';
import { ACCESS_TOKEN_FILE } from '../http/security.ts';

export interface CliIo {
  readonly env: NodeJS.ProcessEnv;
  readonly print: (line: string) => void;
  /** Resolves when the person asks to stop, as with Ctrl-C. */
  readonly interrupted: Promise<void>;
  readonly pollMs?: number;
}

/** Runs one command; returns the process exit status. */
export async function runDevicesCli(args: readonly string[], io: CliIo): Promise<number> {
  const [command = 'list', deviceId] = args;
  switch (command) {
    case 'pair':
      return pair(await connect(io.env), io);
    case 'list':
      return list(await connect(io.env), io);
    case 'revoke':
      if (deviceId === undefined) {
        io.print('Usage: pnpm devices revoke <device id>; pnpm devices lists them.');
        return 2;
      }
      return revoke(await connect(io.env), io, deviceId);
    default:
      io.print('Usage: pnpm pair | pnpm devices [list] | pnpm devices revoke <device id>');
      return 2;
  }
}

interface Api {
  request<T>(method: string, path: string): Promise<{ status: number; body: T | ErrorResponse }>;
}

async function connect(env: NodeJS.ProcessEnv): Promise<Api> {
  const config = loadConfig(env);
  const token = (await readFile(join(config.dataDir, ACCESS_TOKEN_FILE), 'utf8')).trim();
  const host = config.host === '::1' ? '[::1]' : config.host;
  const base = `http://${host}:${config.port}`;
  return {
    async request<T>(method: string, path: string) {
      let response: Response;
      try {
        response = await fetch(`${base}${path}`, {
          method,
          headers: { authorization: `Bearer ${token}` },
        });
      } catch {
        throw new Error(`The control plane is not answering at ${base}. Start it with pnpm dev.`);
      }
      const text = await response.text();
      return {
        status: response.status,
        body: (text === '' ? null : JSON.parse(text)) as T | ErrorResponse,
      };
    },
  };
}

async function pair(api: Api, io: CliIo): Promise<number> {
  const opened = await api.request<PairingOpenedResponse>('POST', '/api/pairing');
  if (opened.status !== 201 || !('code' in opened.body)) {
    io.print(describeError(opened.body));
    return 1;
  }
  const { code, listener, status } = opened.body;
  const [first, ...others] = listener.addresses.map((address) => hostPort(address, listener.port));
  io.print(`Pairing is open until ${clock(status.expires_at)}.`);
  io.print('');
  io.print('On the headset, choose "Pair with a Mac" and enter:');
  io.print(`  Address:  ${first ?? `(no network address found; port ${listener.port})`}`);
  for (const other of others) io.print(`            or ${other}`);
  io.print(`  Code:     ${code.slice(0, 4)} ${code.slice(4)}`);
  io.print('');
  io.print(
    `The code pairs one device, and ${status.max_failed_attempts} wrong attempts close pairing. Ctrl-C closes it.`,
  );

  let stopped = false;
  void io.interrupted.then(() => {
    stopped = true;
  });
  let failures = 0;
  const refusals = new RefusalReport(io.print);
  for (;;) {
    if (stopped) {
      await api.request<PairingStatus>('DELETE', '/api/pairing');
      io.print('Pairing closed; nothing paired.');
      return 130;
    }
    const current = await api.request<PairingStatus>('GET', '/api/pairing');
    if (current.status !== 200 || !('state' in current.body)) {
      io.print(describeError(current.body));
      return 1;
    }
    const window = current.body;
    refusals.report(window.refusals);
    if (window.failed_attempts > failures) {
      failures = window.failed_attempts;
      const left = window.max_failed_attempts - failures;
      if (left > 0)
        io.print(`A code was refused (${left} ${left === 1 ? 'attempt' : 'attempts'} left).`);
    }
    switch (window.state) {
      case 'open':
        break;
      case 'paired': {
        const device = window.device;
        io.print(
          device === null
            ? 'A device paired.'
            : `Paired ${printable(device.label)} as device ${device.device_id}.`,
        );
        if (device !== null) io.print(`Revoke it with: pnpm devices revoke ${device.device_id}`);
        return 0;
      }
      case 'expired':
        io.print('The code expired and nothing paired. Run pnpm pair again.');
        return 1;
      case 'locked':
        io.print(
          `Pairing closed after ${window.failed_attempts} failed attempts, and nothing paired. If they were not yours, something on this network tried to pair.`,
        );
        return 1;
      case 'closed':
        io.print('Pairing was closed, perhaps by another pnpm pair; nothing paired here.');
        return 1;
    }
    await Promise.race([delay(io.pollMs ?? 500), io.interrupted]);
  }
}

/** Reasons that mean something may be holding pairing up rather than guessing the code. */
const HOLDING_UP: ReadonlySet<PairingRefusalReason> = new Set([
  'busy',
  'too_many_requests',
  'timeout',
  'abandoned',
]);

/**
 * Prints the connections a pairing window turned away or cut short without checking a code, each
 * once, so the owner sees another device holding pairing up, which spends none of its attempts.
 */
export class RefusalReport {
  readonly #print: (line: string) => void;
  readonly #shown = new Map<string, number>();
  #warned = false;

  constructor(print: (line: string) => void) {
    this.#print = print;
  }

  report(tallies: readonly PairingRefusalTally[]): void {
    for (const tally of tallies) {
      const key = `${tally.reason} ${tally.address}`;
      const shown = this.#shown.get(key) ?? 0;
      // A tally dropped for newer ones starts again from one when it comes back.
      const added = tally.count >= shown ? tally.count - shown : tally.count;
      this.#shown.set(key, tally.count);
      if (added === 0) continue;
      this.#print(describeRefusal(tally.reason, tally.address, added));
      if (!this.#warned && HOLDING_UP.has(tally.reason)) {
        this.#warned = true;
        this.#print(
          'If that was not your headset, something else on this network is holding pairing up. Ctrl-C closes pairing.',
        );
      }
    }
  }
}

export function describeRefusal(
  reason: PairingRefusalReason,
  address: string,
  count: number,
): string {
  const connections = counted(count, 'pairing connection');
  const exchanges = counted(count, 'pairing exchange');
  switch (reason) {
    case 'busy':
      return `Turned away ${connections} from ${address}, because another exchange was in progress.`;
    case 'too_many_requests':
      return `Turned away ${connections} from ${address}, which opened too many in a minute.`;
    case 'timeout':
      return `Ended ${exchanges} from ${address} that sent no code in time.`;
    case 'abandoned':
      return `${exchanges} from ${address} closed before sending a code.`;
    case 'invalid_message':
      return `Turned away ${connections} from ${address} that did not follow the pairing protocol.`;
    case 'unsupported_protocol':
      return `Turned away ${connections} from ${address} that speaks another pairing protocol; update the headset's app or the control plane.`;
  }
}

function counted(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

async function list(api: Api, io: CliIo): Promise<number> {
  const response = await api.request<DevicesResponse>('GET', '/api/devices');
  if (response.status !== 200 || !('devices' in response.body)) {
    io.print(describeError(response.body));
    return 1;
  }
  const { devices, connected } = response.body;
  if (devices.length === 0) {
    io.print('No device has paired. Pair one with pnpm pair.');
    return 0;
  }
  for (const device of devices) {
    io.print(describeDevice(device, connected.includes(device.device_id)));
  }
  return 0;
}

async function revoke(api: Api, io: CliIo, deviceId: string): Promise<number> {
  const response = await api.request<DeviceView>(
    'POST',
    `/api/devices/${encodeURIComponent(deviceId)}/revoke`,
  );
  if (response.status !== 200 || !('device_id' in response.body)) {
    io.print(describeError(response.body));
    return 1;
  }
  io.print(
    `Revoked ${printable(response.body.label)} (${response.body.device_id}); its connections are closed.`,
  );
  return 0;
}

export function describeDevice(device: DeviceView, connected: boolean): string {
  const state =
    device.revoked_at !== null
      ? `revoked ${clock(device.revoked_at)}`
      : connected
        ? 'connected'
        : 'paired';
  return `${device.device_id}  ${printable(device.label)}  paired ${clock(device.paired_at)}  ${state}`;
}

/**
 * A label as a terminal may print it. The contract already refuses control characters; this also
 * drops C1 controls and the marks that reorder text, which a device could use to disguise itself.
 */
export function printable(label: string): string {
  const kept = [...label].filter((character) => !hidden(character.codePointAt(0) ?? 0));
  return `"${kept.join('')}"`;
}

/** C0 and C1 controls, DEL, and the marks and embeddings that reorder text. */
function hidden(code: number): boolean {
  return (
    code < 0x20 ||
    (code >= 0x7f && code <= 0x9f) ||
    code === 0x200e ||
    code === 0x200f ||
    (code >= 0x202a && code <= 0x202e) ||
    (code >= 0x2066 && code <= 0x2069)
  );
}

function describeError(body: unknown): string {
  const error = (body as ErrorResponse | null)?.error;
  return error === undefined ? 'The control plane gave an unexpected answer.' : error.message;
}

function hostPort(address: string, port: number): string {
  return address.includes(':') ? `[${address}]:${port}` : `${address}:${port}`;
}

function clock(timestamp: string | null): string {
  return timestamp === null ? 'unknown' : new Date(timestamp).toLocaleString();
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

if (import.meta.main) {
  const interrupted = new Promise<void>((resolve) => process.once('SIGINT', () => resolve()));
  runDevicesCli(process.argv.slice(2), {
    env: process.env,
    print: (line) => process.stdout.write(`${line}\n`),
    interrupted,
  })
    .then((status) => process.exit(status))
    .catch((error: unknown) => {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
      process.exit(1);
    });
}
