import { type ChildProcess, spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { type AddressInfo, createServer } from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { RuntimeActionError } from '@halcyonic/runtime-core';
import { OpenCodeClient, TransportError } from './client.ts';
import { isRecord } from './events.ts';
import {
  readProcessIdentity,
  removeServerRecord,
  type ServerRecord,
  writeServerRecord,
} from './server-record.ts';

/** The only OpenCode version whose server API and events this adapter was verified against. */
export const OPENCODE_VERSION = '2.0.18';

const LOOPBACK = '127.0.0.1';
const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));

/**
 * Variables a launched server inherits from the parent environment when they are set. HOME and
 * the XDG directories stay at the user's values, where OpenCode keeps providers and keys, so
 * Salidium and Seorak can observe the sessions too. Nothing else is inherited.
 */
export const INHERITED_VARIABLES: readonly string[] = [
  'PATH',
  'HOME',
  'USER',
  'LOGNAME',
  'SHELL',
  'LANG',
  'LC_ALL',
  'LC_CTYPE',
  'TMPDIR',
  'TZ',
  'XDG_CONFIG_HOME',
  'XDG_DATA_HOME',
  'XDG_STATE_HOME',
  'XDG_CACHE_HOME',
  'XDG_RUNTIME_DIR',
];

/**
 * Variables the adapter owns and configuration may not set: the password it generates, the
 * auto-update and model-catalog switches, and Salidium's internal marker, which
 * makes Salidium drop a session's hooks and must never reach a launched agent.
 */
const RESERVED_VARIABLES: ReadonlySet<string> = new Set([
  'OPENCODE_PASSWORD',
  'OPENCODE_SERVER_PASSWORD',
  'OPENCODE_DISABLE_AUTOUPDATE',
  'OPENCODE_DISABLE_MODELS_FETCH',
  'SALIDIUM_INTERNAL',
]);

/** Builds the server's environment explicitly: the allowlist, then the configured additions. */
export function buildEnvironment(
  inherited: Readonly<Record<string, string | undefined>>,
  additions: Readonly<Record<string, string>>,
): Record<string, string> {
  const reserved = Object.keys(additions).filter((name) => RESERVED_VARIABLES.has(name));
  if (reserved.length > 0) {
    throw new Error(`These variables cannot be configured for OpenCode: ${reserved.join(', ')}.`);
  }
  const environment: Record<string, string> = {};
  for (const name of INHERITED_VARIABLES) {
    const value = inherited[name];
    if (value !== undefined) environment[name] = value;
  }
  Object.assign(environment, additions);
  environment.OPENCODE_DISABLE_AUTOUPDATE = 'true';
  environment.OPENCODE_DISABLE_MODELS_FETCH = 'true';
  return environment;
}

export interface ExitStatus {
  readonly code: number | null;
  readonly signal: NodeJS.Signals | null;
}

export interface LaunchOptions {
  readonly binaryPath: string;
  readonly environment: Readonly<Record<string, string>>;
  readonly recordFile: string;
  /** Null chooses a free port. */
  readonly port: number | null;
  readonly cwd: string;
  readonly startupTimeoutMs: number;
}

/**
 * One OpenCode server process: `opencode serve` on 127.0.0.1 with an explicit port and a
 * generated password, in its own process group, recorded on disk and watched by a watchdog
 * process while it runs.
 */
export class OpenCodeServer {
  readonly client: OpenCodeClient;
  readonly pid: number;
  /** Resolves when the server process has exited, for whatever reason. */
  readonly exited: Promise<ExitStatus>;
  readonly #recordFile: string;
  #watchdog: ChildProcess | null = null;
  #exitStatus: ExitStatus | null = null;
  #cleanup: Promise<void> | null = null;

  constructor(child: ChildProcess, pid: number, client: OpenCodeClient, recordFile: string) {
    this.pid = pid;
    this.client = client;
    this.#recordFile = recordFile;
    this.exited = new Promise((resolve) => {
      child.once('exit', (code, signal) => {
        const status = { code, signal };
        this.#exitStatus = status;
        // Whatever is left of the server's process group, such as commands the agent started
        // in the background, goes with it.
        signalGroup(pid, 'SIGKILL');
        this.#cleanup = this.#cleanUp();
        resolve(status);
      });
    });
  }

  get exitStatus(): ExitStatus | null {
    return this.#exitStatus;
  }

  /** Starts the watchdog that stops this server if the adapter's process dies. */
  watch(record: ServerRecord): void {
    if (this.#exitStatus !== null) return;
    const watchdog = spawn(process.execPath, [WATCHDOG], {
      detached: true,
      stdio: ['pipe', 'ignore', 'ignore'],
      env: {},
    });
    // Without a watchdog the record file remains the fallback; neither failure stops the server.
    watchdog.on('error', () => undefined);
    watchdog.stdin?.on('error', () => undefined);
    watchdog.stdin?.write(`${JSON.stringify(record)}\n`);
    this.#watchdog = watchdog;
  }

  /** Stops the server and its process group: SIGTERM, then SIGKILL after `graceMs`. */
  async stop(graceMs = 5000): Promise<void> {
    if (this.#exitStatus === null) {
      signalGroup(this.pid, 'SIGTERM');
      if (!(await settlesWithin(this.exited, graceMs))) {
        signalGroup(this.pid, 'SIGKILL');
        await settlesWithin(this.exited, 5000);
      }
    }
    await this.#cleanup;
  }

  async #cleanUp(): Promise<void> {
    const watchdog = this.#watchdog;
    if (watchdog !== null && watchdog.exitCode === null && watchdog.signalCode === null) {
      const gone = new Promise<void>((resolve) => watchdog.once('exit', () => resolve()));
      watchdog.stdin?.end();
      await settlesWithin(gone, 10_000);
    }
    await removeServerRecord(this.#recordFile, this.pid).catch(() => undefined);
  }
}

/**
 * Launches the pinned binary and waits until it answers, authenticated, as the expected version
 * and process. Every failure stops what was started and rejects with a `RuntimeActionError`
 * whose effect is `none`: no session exists yet.
 */
export async function launchServer(options: LaunchOptions): Promise<OpenCodeServer> {
  const port = options.port ?? (await freeLoopbackPort());
  const password = randomBytes(32).toString('base64url');
  const child = spawn(
    options.binaryPath,
    ['serve', '--hostname', LOOPBACK, '--port', String(port)],
    {
      cwd: options.cwd,
      env: { ...options.environment, OPENCODE_PASSWORD: password },
      stdio: ['ignore', 'ignore', 'pipe'],
      detached: true,
    },
  );
  const spawnError = new Promise<Error>((resolve) => child.once('error', resolve));
  child.on('error', () => undefined);
  const pid = child.pid;
  if (pid === undefined) {
    const error = await spawnError;
    throw new RuntimeActionError(
      'runtime_unavailable',
      `Could not start OpenCode at ${options.binaryPath}: ${error.message}`,
    );
  }
  drainErrorOutput(child);
  const server = new OpenCodeServer(
    child,
    pid,
    new OpenCodeClient(`http://${LOOPBACK}:${port}`, password),
    options.recordFile,
  );
  try {
    const record = await identify(server, options.binaryPath, port);
    await writeServerRecord(options.recordFile, record);
    server.watch(record);
    await waitUntilReady(server, options);
    return server;
  } catch (error) {
    const status = server.exitStatus;
    await server.stop();
    if (error instanceof RuntimeActionError) throw error;
    throw new RuntimeActionError(
      'runtime_unavailable',
      status === null
        ? `Could not start OpenCode: ${sentence(error instanceof Error ? error.message : String(error))}${UNREPORTED}`
        : `OpenCode exited during startup (${describeExit(status)}).${UNREPORTED}`,
    );
  }
}

/** Reads the launched process's identity. Until exec completes, ps may still show the parent. */
async function identify(
  server: OpenCodeServer,
  binaryPath: string,
  port: number,
): Promise<ServerRecord> {
  const binary = binaryPath.trim().split(/\s+/).join(' ');
  for (let attempt = 0; attempt < 100 && server.exitStatus === null; attempt += 1) {
    const identity = await readProcessIdentity(server.pid);
    if (identity?.command.startsWith(`${binary} `)) {
      return {
        pid: server.pid,
        port,
        binaryPath,
        command: identity.command,
        startedAt: identity.startedAt,
      };
    }
    await delay(20);
  }
  throw new Error(`could not confirm with ps that process ${server.pid} runs ${binaryPath}`);
}

async function waitUntilReady(server: OpenCodeServer, options: LaunchOptions): Promise<void> {
  const deadline = Date.now() + options.startupTimeoutMs;
  while (Date.now() < deadline) {
    const status = server.exitStatus;
    if (status !== null) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        `OpenCode exited during startup (${describeExit(status)}).${UNREPORTED}`,
      );
    }
    let response: { status: number; body: unknown } | null = null;
    try {
      response = await server.client.request('GET', '/api/info', { timeoutMs: 2000 });
    } catch (error) {
      if (!(error instanceof TransportError)) throw error;
    }
    // Right after it starts listening, 2.0.18 answers 503 with Retry-After for a few
    // milliseconds: not ready yet. Only 200 means ready.
    if (response !== null && response.status !== 503) {
      if (response.status !== 200) {
        throw new RuntimeActionError(
          'runtime_unavailable',
          `OpenCode answered ${response.status} to its readiness check.`,
        );
      }
      checkIdentity(response.body, server.pid, options.binaryPath);
      return;
    }
    await delay(50);
  }
  throw new RuntimeActionError(
    'runtime_unavailable',
    `OpenCode did not become ready within ${options.startupTimeoutMs} ms.${UNREPORTED}`,
  );
}

/** `GET /api/info` reports the server's version and pid; both must be the launched binary's. */
function checkIdentity(body: unknown, pid: number, binaryPath: string): void {
  const info = isRecord(body) ? body : {};
  if (info.version !== OPENCODE_VERSION) {
    throw new RuntimeActionError(
      'runtime_version_unsupported',
      `The OpenCode binary at ${binaryPath} reports version ${String(info.version)}; this adapter supports only ${OPENCODE_VERSION}.`,
    );
  }
  if (info.pid !== pid) {
    throw new RuntimeActionError(
      'runtime_unavailable',
      `The server on the OpenCode port reports process ${String(info.pid)}, not the launched process ${pid}.`,
    );
  }
}

export function describeExit(status: ExitStatus): string {
  return status.signal === null ? `exit code ${status.code}` : `signal ${status.signal}`;
}

/** Text that ends as one sentence does, with a single full stop. */
const sentence = (text: string) => text.replace(/[.\s]*$/, '.');

/**
 * What a failure says of OpenCode's error output, which is drained and never kept: it may hold
 * secrets, such as a provider's key in a configuration error, and a failure is journaled and shown
 * on every device.
 */
const UNREPORTED =
  ' Its error output is not reported, since it may hold secrets; run the OpenCode binary with serve in a terminal on the Mac to see it.';

/** Reads the server's error output to its end without keeping any of it, so it never blocks on a full pipe. */
function drainErrorOutput(child: ChildProcess): void {
  child.stderr?.on('error', () => undefined);
  child.stderr?.resume();
}

async function freeLoopbackPort(): Promise<number> {
  const probe = createServer();
  await new Promise<void>((resolve, reject) => {
    probe.once('error', reject);
    probe.listen(0, LOOPBACK, () => resolve());
  });
  const { port } = probe.address() as AddressInfo;
  await new Promise<void>((resolve) => probe.close(() => resolve()));
  return port;
}

function signalGroup(pid: number, name: NodeJS.Signals): void {
  try {
    process.kill(-pid, name);
  } catch {
    // The group is already gone.
  }
}

/** Waits for a promise to settle, for at most `ms`. Resolves whether it settled in time. */
export async function settlesWithin(promise: Promise<unknown>, ms: number): Promise<boolean> {
  const timer = new AbortController();
  try {
    return await Promise.race([
      promise.then(() => true),
      delay(ms, false, { signal: timer.signal }).catch(() => false),
    ]);
  } finally {
    timer.abort();
  }
}
