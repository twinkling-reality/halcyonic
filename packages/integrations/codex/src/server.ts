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
 * Variables a launched server inherits when they are set. HOME stays the developer's own, for the
 * commands Codex runs. CODEX_HOME is never inherited: the adapter gives Codex a home of its own
 * (`prepareHome`), so the developer's Codex configuration, sign-in and plugins never apply.
 * Nothing else is inherited.
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
 * Keeps the server from enabling remote control, a second control channel through chatgpt.com.
 * Codex 0.157.0 reads it at startup (codex-rs/cli/src/main.rs and
 * app-server-transport/src/transport/remote_control/mod.rs at rust-v0.157.0).
 */
const REMOTE_CONTROL_DISABLED = 'CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED';

/**
 * The variables Codex 0.157.0 signs in with, named in the pinned binary's own text: an OpenAI or
 * Codex key, a ChatGPT access token, an identity token file, and the tokens of its connectors and
 * its GitHub plugins. Through Halcyonic, Codex runs only on models served on this Mac and is
 * never signed in, so none reaches it.
 */
export const SIGN_IN_VARIABLES: readonly string[] = [
  'OPENAI_API_KEY',
  'CODEX_API_KEY',
  'CODEX_ACCESS_TOKEN',
  'OPENAI_IDENTITY_TOKEN_FILE',
  'CODEX_CONNECTORS_TOKEN',
  'CODEX_GITHUB_PERSONAL_ACCESS_TOKEN',
];

/**
 * The proxy variables, which would send Codex's requests, the ones to Ollama with the prompts and
 * the code among them, through another host. The control plane passes none to Codex, and the
 * adapter sets NO_PROXY to loopback whatever it is given.
 */
export const PROXY_VARIABLES: readonly string[] = [
  'HTTP_PROXY',
  'HTTPS_PROXY',
  'ALL_PROXY',
  'http_proxy',
  'https_proxy',
  'all_proxy',
];

/** Loopback, which Codex reaches without a proxy whatever the environment says. */
const LOOPBACK_HOSTS = 'localhost,127.0.0.1,::1';

/**
 * Variables configuration may not set: the home and the remote control switch the adapter owns,
 * the originator override that would replace Halcyonic's identity on its threads, Salidium's
 * internal marker, which makes Salidium drop a session's hooks and must never reach a launched
 * agent, every sign-in, and what moves Codex's work or data off the Mac or out of its home: the
 * built-in local providers' address (`CODEX_OSS_BASE_URL`, `CODEX_OSS_PORT`), the state
 * databases' folder (`CODEX_SQLITE_HOME`), and every `CODEX_EXEC_SERVER_` variable, with which
 * commands run on a remote exec server (codex-rs/exec-server/src/environment_provider.rs at
 * rust-v0.157.0).
 */
const RESERVED_VARIABLES: ReadonlySet<string> = new Set([
  'CODEX_HOME',
  REMOTE_CONTROL_DISABLED,
  'CODEX_INTERNAL_ORIGINATOR_OVERRIDE',
  'SALIDIUM_INTERNAL',
  'CODEX_OSS_BASE_URL',
  'CODEX_OSS_PORT',
  'CODEX_SQLITE_HOME',
  ...SIGN_IN_VARIABLES,
]);
/**
 * Every `CODEX_` and `OPENAI_` variable is Codex's or its provider's to read, and some move its data
 * elsewhere (`CODEX_ROLLOUT_TRACE_ROOT` writes prompts, responses and terminal output wherever it
 * points), so configuration sets none: the adapter sets the two it needs itself.
 */
const RESERVED_PREFIXES: readonly string[] = ['CODEX_', 'OPENAI_'];

function reserved(name: string): boolean {
  return (
    RESERVED_VARIABLES.has(name) || RESERVED_PREFIXES.some((prefix) => name.startsWith(prefix))
  );
}

/**
 * The features of 0.157.0 that are on by default and reach the network or another app: plugins
 * and their sync, apps, the remote plugin catalog and plugin sharing, in-app updates, image
 * generation, browser and computer use, installing a skill's MCP dependencies, tool suggestions,
 * starting the shared daemon, and retrying through the system proxy (codex-rs/features/src/lib.rs
 * at rust-v0.157.0, each `default_enabled: true`).
 */
const FEATURES_OFF: readonly string[] = [
  'plugins',
  'apps',
  'remote_plugin',
  'plugin_sharing',
  'in_app_updates',
  'image_generation',
  'browser_use',
  'browser_use_external',
  'computer_use',
  'skill_mcp_dependency_install',
  'tool_suggest',
  'daemon_auto_start',
  'system_proxy_fallback',
];

/**
 * The settings every launch passes as `-c` overrides, with the value `config/read` must then
 * report for each, checked after every launch and at every start (`unappliedSettings`). An
 * override outranks the home's `config.toml`, so no edit there turns one back on. A managed
 * configuration file (`/etc/codex/managed_config.toml`, a device profile) outranks an override,
 * which this check catches; managed requirements (`requirements.toml`) can pin a feature on while
 * `config/read` still reports it off, so the adapter also refuses any requirements at all
 * (`configRequirements/read`). On 0.157.0
 * `features.plugins = false` is the one that stops the plugin sync connecting to GitHub at
 * startup; it is undocumented, so every Codex upgrade repeats the network probe of the end to end
 * tests (local-models.md). The rest turn off the other features above, the update check,
 * analytics and the metrics exporter, and web search, and keep sign-ins and MCP credentials out of
 * the keychain.
 */
export const LOCAL_ONLY_SETTINGS: readonly {
  readonly key: string;
  readonly value: boolean | string;
}[] = [
  ...FEATURES_OFF.map((name) => ({ key: `features.${name}`, value: false })),
  { key: 'check_for_update_on_startup', value: false },
  { key: 'analytics.enabled', value: false },
  { key: 'web_search', value: 'disabled' },
  { key: 'cli_auth_credentials_store', value: 'file' },
  { key: 'mcp_oauth_credentials_store', value: 'file' },
];

/** The server's command line after the binary: app-server, then every local-only setting. */
export const APP_SERVER_ARGUMENTS: readonly string[] = [
  'app-server',
  ...LOCAL_ONLY_SETTINGS.flatMap(({ key, value }) => [
    '-c',
    `${key}=${typeof value === 'string' ? JSON.stringify(value) : String(value)}`,
  ]),
];

/** The local-only settings Codex's configuration, as `config/read` reports it, does not hold. */
export function unappliedSettings(config: Readonly<Record<string, unknown>>): string[] {
  return LOCAL_ONLY_SETTINGS.filter(({ key, value }) => {
    let found: unknown = config;
    for (const part of key.split('.')) {
      found =
        typeof found === 'object' && found !== null
          ? (found as Record<string, unknown>)[part]
          : undefined;
    }
    return found !== value;
  }).map(({ key }) => key);
}

/**
 * Builds the server's environment explicitly: the allowlist, then the configured additions, then
 * the home the adapter gives Codex.
 */
export function buildEnvironment(
  inherited: Readonly<Record<string, string | undefined>>,
  additions: Readonly<Record<string, string>>,
  home: string,
): Record<string, string> {
  const refused = Object.keys(additions).filter(reserved);
  if (refused.length > 0) {
    throw new Error(`These variables cannot be configured for Codex: ${refused.join(', ')}.`);
  }
  const environment: Record<string, string> = {};
  for (const name of INHERITED_VARIABLES) {
    const value = inherited[name];
    if (value !== undefined) environment[name] = value;
  }
  Object.assign(environment, additions);
  environment.NO_PROXY = LOOPBACK_HOSTS;
  environment.no_proxy = LOOPBACK_HOSTS;
  environment.CODEX_HOME = home;
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
  const child = spawn(options.binaryPath, APP_SERVER_ARGUMENTS, {
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
