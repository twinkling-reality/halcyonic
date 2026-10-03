import { type ChildProcess, type ExecFileException, execFile, spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { RuntimeActionError } from '@halcyonic/runtime-core';
import type { ClientInfo, InitializeParams } from './protocol.ts';
import { RpcConnection, type RpcHandlers } from './rpc.ts';
import {
  killWithDescendants,
  readDescendants,
  readProcessIdentity,
  removeServerRecord,
  runsAppServer,
  type ServerRecord,
  writeServerRecord,
} from './server-record.ts';

/** The only Codex version whose app-server this adapter was verified against. */
export const CODEX_VERSION = '0.157.0';

/**
 * How Halcyonic identifies itself. Codex records `name` as the originator of every thread this
 * client starts, and sends it to the model provider.
 */
export const CLIENT_INFO: ClientInfo = { name: 'halcyonic', title: 'Halcyonic', version: '0.0.0' };

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));

/**
 * Variables a launched server inherits when they are set. HOME and CODEX_HOME stay the developer's
 * own: Codex keeps its configuration, credentials and session rollouts there, and Salidium and
 * Seorak observe Codex by reading those rollouts. Nothing else is inherited; provider credentials
 * are passed on purpose, as configured additions.
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
  'CODEX_HOME',
  'XDG_CONFIG_HOME',
  'XDG_DATA_HOME',
  'XDG_STATE_HOME',
  'XDG_CACHE_HOME',
  'XDG_RUNTIME_DIR',
];

/**
 * Keeps the server from enabling remote control, a second control channel through chatgpt.com.
 * Codex 0.157.0 reads it at startup (codex-rs/cli/src/main.rs and
 * app-server-transport/src/transport/remote_control/mod.rs at rust-v0.157.0).
 */
const REMOTE_CONTROL_DISABLED = 'CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED';

/**
 * Variables configuration may not set: the remote control switch the adapter owns, the originator
 * override that would replace Halcyonic's identity on its threads, and Salidium's internal marker,
 * which makes Salidium drop a session's hooks and must never reach a launched agent.
 */
const RESERVED_VARIABLES: ReadonlySet<string> = new Set([
  REMOTE_CONTROL_DISABLED,
  'CODEX_INTERNAL_ORIGINATOR_OVERRIDE',
  'SALIDIUM_INTERNAL',
]);

/** Builds the server's environment explicitly: the allowlist, then the configured additions. */
export function buildEnvironment(
  inherited: Readonly<Record<string, string | undefined>>,
  additions: Readonly<Record<string, string>>,
): Record<string, string> {
  const reserved = Object.keys(additions).filter((name) => RESERVED_VARIABLES.has(name));
  if (reserved.length > 0) {
    throw new Error(`These variables cannot be configured for Codex: ${reserved.join(', ')}.`);
  }
  const environment: Record<string, string> = {};
  for (const name of INHERITED_VARIABLES) {
    const value = inherited[name];
    if (value !== undefined) environment[name] = value;
  }
  Object.assign(environment, additions);
  environment[REMOTE_CONTROL_DISABLED] = '1';
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
  readonly cwd: string;
  readonly startupTimeoutMs: number;
  readonly handlers: RpcHandlers;
}

/**
 * One `codex app-server` process speaking JSON-RPC over stdio, in its own process group,
 * recorded on disk and watched by a watchdog process while it runs.
 */
export class CodexServer {
  readonly pid: number;
  readonly rpc: RpcConnection;
  /** Resolves when the server process has exited, for whatever reason. */
  readonly exited: Promise<ExitStatus>;
  readonly #recordFile: string;
  #watchdog: ChildProcess | null = null;
  #exitStatus: ExitStatus | null = null;
  #cleanup: Promise<void> | null = null;

  constructor(child: ChildProcess, pid: number, rpc: RpcConnection, recordFile: string) {
    this.pid = pid;
    this.rpc = rpc;
    this.#recordFile = recordFile;
    this.exited = new Promise((resolve) => {
      child.once('exit', (code, signal) => {
        const status = { code, signal };
        this.#exitStatus = status;
        // What is left of the server's process group goes with it. Commands are not in it: Codex
        // starts each in a session of its own and stops them itself when it shuts down.
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

  /**
   * Stops the server by ending its input, which makes Codex stop its commands and exit. A server
   * still running after `graceMs` gets SIGTERM, then SIGKILL for its group and its commands.
   */
  async stop(graceMs = 10_000): Promise<void> {
    if (this.#exitStatus === null) {
      this.rpc.end();
      if (!(await settlesWithin(this.exited, graceMs))) {
        signalGroup(this.pid, 'SIGTERM');
        if (!(await settlesWithin(this.exited, 5000))) {
          const descendants = await readDescendants(this.pid).catch(() => []);
          await killWithDescendants(this.pid, descendants);
          await settlesWithin(this.exited, 5000);
        }
      }
    }
    await this.#cleanup;
  }

  async #cleanUp(): Promise<void> {
    const watchdog = this.#watchdog;
    if (watchdog !== null && watchdog.exitCode === null && watchdog.signalCode === null) {
      const gone = new Promise<void>((resolve) => watchdog.once('exit', () => resolve()));
      watchdog.stdin?.end();
      await settlesWithin(gone, 15_000);
    }
    await removeServerRecord(this.#recordFile, this.pid).catch(() => undefined);
  }
}

/**
 * Checks the binary's version, launches `codex app-server` and initializes it as Halcyonic. Every
 * failure stops what was started and rejects with a `RuntimeActionError` whose effect is `none`:
 * no thread exists yet.
 */
export async function launchServer(options: LaunchOptions): Promise<CodexServer> {
  const version = await readVersion(options, Math.min(options.startupTimeoutMs, 10_000));
  if (version !== CODEX_VERSION) throw unsupported(options.binaryPath, version);
  const child = spawn(options.binaryPath, ['app-server'], {
    cwd: options.cwd,
    env: options.environment,
    stdio: ['pipe', 'pipe', 'pipe'],
    detached: true,
  });
  const spawnError = new Promise<Error>((resolve) => child.once('error', resolve));
  child.on('error', () => undefined);
  const pid = child.pid;
  if (pid === undefined || child.stdout === null || child.stdin === null) {
    const error = await spawnError;
    throw new RuntimeActionError(
      'runtime_unavailable',
      `Could not start Codex at ${options.binaryPath}: ${error.message}`,
    );
  }
  drainErrorOutput(child);
  const rpc = new RpcConnection(child.stdout, child.stdin, options.handlers);
  const server = new CodexServer(child, pid, rpc, options.recordFile);
  try {
    const record = await identify(server, options.binaryPath);
    await writeServerRecord(options.recordFile, record);
    server.watch(record);
    const params: InitializeParams = { clientInfo: CLIENT_INFO, capabilities: null };
    const result = await rpc.request('initialize', params, options.startupTimeoutMs);
    const agent = isRecord(result) && typeof result.userAgent === 'string' ? result.userAgent : '';
    const reported = new RegExp(`^${CLIENT_INFO.name}/(\\S+) `).exec(agent)?.[1] ?? 'unknown';
    if (reported !== CODEX_VERSION) throw unsupported(options.binaryPath, reported);
    rpc.notify('initialized');
    return server;
  } catch (error) {
    const status = server.exitStatus;
    await server.stop(1000);
    if (error instanceof RuntimeActionError) throw error;
    throw new RuntimeActionError(
      'runtime_unavailable',
      status === null
        ? `Could not start Codex: ${sentence(error instanceof Error ? error.message : String(error))}${UNREPORTED}`
        : `Codex exited during startup (${describeExit(status)}).${UNREPORTED}`,
    );
  }
}

/** Runs `codex --version`, which prints `codex-cli <version>`. */
function readVersion(options: LaunchOptions, timeoutMs: number): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(
      options.binaryPath,
      ['--version'],
      { cwd: options.cwd, env: options.environment, timeout: timeoutMs },
      (error, stdout) => {
        const version = /^codex-cli (\S+)\s*$/m.exec(stdout)?.[1];
        if (version !== undefined) resolve(version);
        else {
          reject(
            new RuntimeActionError(
              'runtime_unavailable',
              // Never the error's message, which carries what the command printed to its error output.
              `Could not read the version of the Codex binary at ${options.binaryPath}${error === null ? '' : ` (${describeFailure(error)})`}.${UNREPORTED}`,
            ),
          );
        }
      },
    );
  });
}

function unsupported(binaryPath: string, version: string): RuntimeActionError {
  return new RuntimeActionError(
    'runtime_version_unsupported',
    `The Codex binary at ${binaryPath} reports version ${version}; this adapter supports only ${CODEX_VERSION}.`,
  );
}

/** Reads the launched process's identity. Until exec completes, ps may still show the parent. */
async function identify(server: CodexServer, binaryPath: string): Promise<ServerRecord> {
  for (let attempt = 0; attempt < 100 && server.exitStatus === null; attempt += 1) {
    const identity = await readProcessIdentity(server.pid);
    if (identity !== null && runsAppServer(identity.command, binaryPath)) {
      return {
        pid: server.pid,
        binaryPath,
        command: identity.command,
        startedAt: identity.startedAt,
      };
    }
    await delay(20);
  }
  throw new Error(`could not confirm with ps that process ${server.pid} runs ${binaryPath}`);
}

export function describeExit(status: ExitStatus): string {
  return status.signal === null ? `exit code ${status.code}` : `signal ${status.signal}`;
}

/** Text that ends as one sentence does, with a single full stop. */
const sentence = (text: string) => text.replace(/[.\s]*$/, '.');

/** How a command that ran failed: its exit code or signal, or the error that kept it from running. */
function describeFailure(error: ExecFileException): string {
  if (error.signal) return `signal ${error.signal}`;
  if (typeof error.code === 'number') return `exit code ${error.code}`;
  return typeof error.code === 'string' ? error.code : 'it did not run';
}

/**
 * What a failure says of Codex's error output, which is drained and never kept: it may hold
 * secrets, such as a key that verbose logging or a configuration error prints, and a failure is
 * journaled and shown on every device.
 */
const UNREPORTED =
  ' Its error output is not reported, since it may hold secrets; run the Codex binary with app-server in a terminal on the Mac to see it.';

/** Reads the server's error output to its end without keeping any of it, so it never blocks on a full pipe. */
function drainErrorOutput(child: ChildProcess): void {
  child.stderr?.on('error', () => undefined);
  child.stderr?.resume();
}

function signalGroup(pid: number, name: NodeJS.Signals): void {
  try {
    process.kill(-pid, name);
  } catch {
    // The group is already gone.
  }
}

/** Waits for a promise to settle, for at most `ms`. Resolves whether it settled in time. */
async function settlesWithin(promise: Promise<unknown>, ms: number): Promise<boolean> {
  const timer = new AbortController();
  try {
    return await Promise.race([
      promise.then(
        () => true,
        () => true,
      ),
      delay(ms, false, { signal: timer.signal }).catch(() => false),
    ]);
  } finally {
    timer.abort();
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
