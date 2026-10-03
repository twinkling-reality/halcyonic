import {
  closeSync,
  constants,
  fstatSync,
  lstatSync,
  openSync,
  readdirSync,
  readFileSync,
  realpathSync,
  type Stats,
  statSync,
} from 'node:fs';
import { isIP } from 'node:net';
import { homedir, userInfo } from 'node:os';
import { delimiter, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isCloudName } from './companion/ollama.ts';
import { assessFolder, type FolderContext } from './folder-safety.ts';
import { OwnWordsError } from './logger.ts';

export const LOG_LEVELS = ['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent'] as const;
export type LogLevel = (typeof LOG_LEVELS)[number];

/**
 * The main listener serves loopback only. Paired devices reach the control plane through the
 * separate network listener, which is TLS only and off unless configured (ADR 0017,
 * docs/internal/architecture/SECURITY.md).
 */
const LOOPBACK_HOSTS = new Set(['127.0.0.1', '::1', 'localhost']);

/** The listener for paired devices: TLS, pairing and device credentials only (ADR 0017). */
export interface NetworkListenerConfig {
  /** An IP address: one interface's, or `0.0.0.0` or `::` for every interface. */
  readonly host: string;
  readonly port: number;
}

/**
 * The pinned whisper.cpp command-line binary, its model and its voice activity model, which
 * transcribe a held clip of speech into a draft (ADR 0021).
 */
export interface SpeechConfig {
  readonly binary: string;
  readonly model: string;
  readonly vadModel: string;
}

/**
 * Create's companion (ADR 0025): the local model it asks, by the name Ollama lists it under, and
 * Ollama's address, which must be on this computer.
 */
export interface CompanionConfig {
  readonly model: string;
  readonly ollama: URL;
}

export const DEFAULT_OLLAMA_URL = 'http://127.0.0.1:11434';

export interface ControlPlaneConfig {
  readonly host: string;
  readonly port: number;
  /** Null unless the owner turned the network listener on with HALCYONIC_NETWORK_HOST. */
  readonly network: NetworkListenerConfig | null;
  readonly dataDir: string;
  readonly logLevel: LogLevel;
  readonly commandTimeoutMs: number;
  readonly scenariosDir: string;
  /** Directories agents may work in, with everything below them. Empty allows none. */
  readonly projectRoots: readonly string[];
  /** Whether the Claude Agent runtime is registered. It needs Anthropic or cloud provider credentials. */
  readonly claudeAgent: boolean;
  /** A Claude Code executable to use instead of the one bundled with the Agent SDK. */
  readonly claudeExecutable: string | null;
  /** Names of variables copied from the control plane's environment into every launched agent's. */
  readonly agentEnvironment: readonly string[];
  /** The pinned OpenCode binary; the OpenCode runtime is registered only when it is set. */
  readonly opencodeBinary: string | null;
  /**
   * Halcyonic's own OpenCode settings: a directory OpenCode alone is given as its configuration home
   * (`XDG_CONFIG_HOME`), holding `opencode/opencode.json`. Null leaves OpenCode the person's own.
   */
  readonly opencodeConfigHome: string | null;
  /**
   * The pinned Codex binary, the native one rather than the npm launcher script; the Codex runtime
   * is registered only when it is set.
   */
  readonly codexBinary: string | null;
  /** Null, and voice off, unless the owner set all three HALCYONIC_WHISPER_ variables. */
  readonly speech: SpeechConfig | null;
  /** Null, and the companion off, unless the owner named its model in HALCYONIC_COMPANION_MODEL. */
  readonly companion: CompanionConfig | null;
  /**
   * Whether the end of stdin shuts the control plane down as SIGTERM does. For a launcher, such as
   * a test harness, that runs it as a child and holds its stdin open without writing to it: when
   * the launcher exits, however it exits, the operating system closes the pipe and the control
   * plane stops instead of running on as an orphan.
   */
  readonly exitOnStdinEnd: boolean;
}

export class ConfigError extends OwnWordsError {
  constructor(message: string) {
    super(message);
    this.name = 'ConfigError';
  }
}

export const DEFAULT_PORT = 47800;
export const DEFAULT_NETWORK_PORT = 47801;

export function defaultDataDir(env: NodeJS.ProcessEnv = process.env): string {
  return resolve(env.HALCYONIC_DATA_DIR ?? join(homedir(), '.halcyonic'));
}

/** The loopback address and port the control plane serves, as validated for the control plane. */
export function loopbackListener(env: NodeJS.ProcessEnv = process.env): {
  host: string;
  port: number;
} {
  const host = env.HALCYONIC_HOST ?? '127.0.0.1';
  if (!LOOPBACK_HOSTS.has(host)) {
    throw new ConfigError(
      `HALCYONIC_HOST=${host} is not a loopback address. Devices on the network reach the control plane through the network listener instead: set HALCYONIC_NETWORK_HOST.`,
    );
  }
  return { host, port: parseInteger('HALCYONIC_PORT', env.HALCYONIC_PORT, DEFAULT_PORT, 0, 65535) };
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ControlPlaneConfig {
  const { host, port } = loopbackListener(env);
  return {
    host,
    port,
    network: parseNetwork(env, port),
    dataDir: defaultDataDir(env),
    logLevel: parseLogLevel(env.HALCYONIC_LOG_LEVEL),
    commandTimeoutMs: parseInteger(
      'HALCYONIC_COMMAND_TIMEOUT_MS',
      env.HALCYONIC_COMMAND_TIMEOUT_MS,
      30_000,
      100,
      600_000,
    ),
    scenariosDir: resolve(
      env.HALCYONIC_MOCK_SCENARIOS_DIR ??
        fileURLToPath(new URL('../../../fixtures/scenarios', import.meta.url)),
    ),
    projectRoots: parseProjectRoots(env.HALCYONIC_PROJECT_ROOTS, {
      // The account's own home, not $HOME, which a launcher may change.
      home: userInfo().homedir,
      dataDir: defaultDataDir(env),
      uid: process.getuid?.(),
    }),
    claudeAgent: parseSwitch('HALCYONIC_CLAUDE_AGENT', env.HALCYONIC_CLAUDE_AGENT),
    claudeExecutable: parseExecutable(
      'HALCYONIC_CLAUDE_EXECUTABLE',
      env.HALCYONIC_CLAUDE_EXECUTABLE,
    ),
    agentEnvironment: parseNames('HALCYONIC_AGENT_ENV', env.HALCYONIC_AGENT_ENV),
    opencodeBinary: parseExecutable('HALCYONIC_OPENCODE_BIN', env.HALCYONIC_OPENCODE_BIN),
    opencodeConfigHome: parseOpenCodeConfigHome(env.HALCYONIC_OPENCODE_CONFIG_HOME),
    codexBinary: parseExecutable('HALCYONIC_CODEX_BIN', env.HALCYONIC_CODEX_BIN),
    speech: parseSpeech(env),
    companion: parseCompanion(env),
    exitOnStdinEnd: parseSwitch('HALCYONIC_EXIT_ON_STDIN_END', env.HALCYONIC_EXIT_ON_STDIN_END),
  };
}

function parseNetwork(env: NodeJS.ProcessEnv, loopbackPort: number): NetworkListenerConfig | null {
  const host = env.HALCYONIC_NETWORK_HOST;
  if (host === undefined || host === '') {
    if (env.HALCYONIC_NETWORK_PORT !== undefined && env.HALCYONIC_NETWORK_PORT !== '') {
      throw new ConfigError(
        'HALCYONIC_NETWORK_PORT is set, but the network listener is off; set HALCYONIC_NETWORK_HOST to turn it on.',
      );
    }
    return null;
  }
  if (isIP(host) === 0) {
    throw new ConfigError(
      `HALCYONIC_NETWORK_HOST must be an IP address, such as 0.0.0.0 for every interface, got "${host}".`,
    );
  }
  const port = parseInteger(
    'HALCYONIC_NETWORK_PORT',
    env.HALCYONIC_NETWORK_PORT,
    DEFAULT_NETWORK_PORT,
    0,
    65535,
  );
  if (port !== 0 && port === loopbackPort) {
    throw new ConfigError('HALCYONIC_NETWORK_PORT must differ from HALCYONIC_PORT.');
  }
  return { host, port };
}

function parseSwitch(name: string, raw: string | undefined): boolean {
  if (raw === undefined || raw === '' || raw === '0') return false;
  if (raw === '1') return true;
  throw new ConfigError(`${name} must be 1 or 0, got "${raw}".`);
}

function parseExecutable(name: string, raw: string | undefined): string | null {
  if (raw === undefined || raw === '') return null;
  if (!isAbsolute(raw)) throw new ConfigError(`${name} must be an absolute path, got "${raw}".`);
  let isFile = false;
  try {
    isFile = statSync(raw).isFile();
  } catch {
    throw new ConfigError(`${name} ${raw} does not exist.`);
  }
  if (!isFile) throw new ConfigError(`${name} ${raw} is not a file.`);
  return raw;
}

/** The settings file in an OpenCode configuration home, the only file it may hold. */
export const OPENCODE_SETTINGS_PATH = join('opencode', 'opencode.json');

const MAX_OPENCODE_SETTINGS_BYTES = 64 * 1024;

/** A rule OpenCode applies to an action: `allow`, `ask` or `deny`. */
export interface OpenCodePermission {
  readonly action: string;
  readonly resource: string;
  readonly effect: 'allow' | 'ask' | 'deny';
}

/** Halcyonic's own OpenCode settings, as `pnpm mac-setup local-model` writes them. */
export interface OpenCodeSettings {
  readonly model: string;
  readonly small_model?: string;
  readonly permissions: readonly OpenCodePermission[];
}

/**
 * A model OpenCode reaches through the Ollama on this Mac. Ollama serves a model whose tag ends in
 * `cloud` from its own remote service (`packages/integrations/opencode/src/models.ts`).
 */
export function isLocalOllamaModel(model: unknown): boolean {
  return typeof model === 'string' && /^ollama\/\S+$/.test(model) && !/[:-]cloud$/.test(model);
}

/**
 * Reads Halcyonic's own OpenCode settings, held to the settings file's standard (ADR 0024): the
 * configuration home and its `opencode` folder are real folders, not links, owned by this user and
 * closed to others, and the `opencode` folder holds nothing but `opencode.json`, a regular file of
 * mode 600 that holds only what `pnpm mac-setup local-model` writes. Other tools an agent runs may
 * keep their own folders beside `opencode`, since they inherit the same configuration home. Its default model, and its small model when
 * it names one, must be a model this Mac serves through Ollama, so a hand edit can't make a remote
 * model the default. A start through Halcyonic always names its model (`model_required`); the
 * default only decides what OpenCode would choose by itself.
 */
export function readOpenCodeSettings(home: string): OpenCodeSettings {
  const name = 'HALCYONIC_OPENCODE_CONFIG_HOME';
  if (!isAbsolute(home)) throw new ConfigError(`${name} must be an absolute path, got "${home}".`);
  const folder = join(home, 'opencode');
  const path = join(home, OPENCODE_SETTINGS_PATH);
  for (const directory of [home, folder]) {
    const stats = lstatOrNull(directory);
    if (stats === null || !stats.isDirectory()) {
      throw new ConfigError(`${name}: ${directory} is not a folder, or is a link.`);
    }
    refuseOpen(directory, stats, 'Run chmod 700 on it.');
  }
  const only = (directory: string, entry: string) => {
    const others = readdirSync(directory).filter((candidate) => candidate !== entry);
    if (others.length > 0) {
      throw new ConfigError(
        `${name}: ${directory} holds ${others.join(', ')} beside ${entry}, which OpenCode could read too; move them away.`,
      );
    }
  };
  only(folder, 'opencode.json');
  const settings = parseOpenCodeSettings(path, readPrivateFile(path, MAX_OPENCODE_SETTINGS_BYTES));
  return settings;
}

function parseOpenCodeSettings(path: string, text: string): OpenCodeSettings {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new ConfigError(`${path} is not valid JSON.`);
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new ConfigError(`${path} must hold one JSON object.`);
  }
  const document = parsed as Record<string, unknown>;
  const unknown = Object.keys(document).filter(
    (key) => !['model', 'small_model', 'permissions', 'providers'].includes(key),
  );
  if (unknown.length > 0) {
    throw new ConfigError(
      `${path} can't set ${unknown.join(', ')}: Halcyonic's own OpenCode settings hold only what pnpm mac-setup local-model writes.`,
    );
  }
  if (!isLocalOllamaModel(document.model)) {
    throw new ConfigError(
      `${path} must name a model served on this Mac through Ollama as its "model", such as "ollama/qwen3.6:35b-a3b-nvfp4".`,
    );
  }
  if (document.small_model !== undefined && !isLocalOllamaModel(document.small_model)) {
    throw new ConfigError(`${path} names a "small_model" that is not served on this Mac.`);
  }
  const permissions = document.permissions ?? [];
  if (!Array.isArray(permissions) || !permissions.every(isPermission)) {
    throw new ConfigError(
      `${path} must list its "permissions" as rules of an action, a resource and an effect of allow, ask or deny.`,
    );
  }
  if (document.providers !== undefined && !isOllamaLimits(document.providers)) {
    throw new ConfigError(
      `${path} may give "providers" only the context and output limits of Ollama's models.`,
    );
  }
  return {
    model: document.model as string,
    ...(document.small_model === undefined ? {} : { small_model: document.small_model as string }),
    permissions,
  };
}

function isPermission(value: unknown): value is OpenCodePermission {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const rule = value as Record<string, unknown>;
  return (
    Object.keys(rule).every((key) => ['action', 'resource', 'effect'].includes(key)) &&
    typeof rule.action === 'string' &&
    typeof rule.resource === 'string' &&
    (rule.effect === 'allow' || rule.effect === 'ask' || rule.effect === 'deny')
  );
}

/** `{"ollama": {"models": {"<tag>": {"limit": {"context": n, "output": n}}}}}` and nothing else. */
function isOllamaLimits(value: unknown): boolean {
  const object = (candidate: unknown, keys: readonly string[]) =>
    typeof candidate === 'object' &&
    candidate !== null &&
    !Array.isArray(candidate) &&
    Object.keys(candidate).every((key) => keys.includes(key));
  if (!object(value, ['ollama'])) return false;
  const ollama = (value as { ollama?: unknown }).ollama;
  if (ollama === undefined) return true;
  if (!object(ollama, ['models'])) return false;
  const models = (ollama as { models?: unknown }).models ?? {};
  if (typeof models !== 'object' || models === null || Array.isArray(models)) return false;
  return Object.values(models).every((model) => {
    if (!object(model, ['limit'])) return false;
    const limit = (model as { limit?: unknown }).limit ?? {};
    if (!object(limit, ['context', 'output'])) return false;
    return Object.values(limit as object).every(
      (number) => Number.isInteger(number) && (number as number) > 0,
    );
  });
}

function parseOpenCodeConfigHome(raw: string | undefined): string | null {
  if (raw === undefined || raw === '') return null;
  readOpenCodeSettings(raw);
  return raw;
}

/**
 * A file only this user can read or change: a regular file, opened without following a link or
 * waiting on a pipe, owned by this user, closed to others, and at most `limit` bytes.
 */
export function readPrivateFile(path: string, limit: number): string {
  const first = lstatOrNull(path);
  if (first === null) throw new ConfigError(`${path} is not there.`);
  if (!first.isFile()) throw new ConfigError(`${path} is not a regular file, so it is not used.`);
  refuseOpen(path, first, 'Run chmod 600 on it.');
  let fd: number;
  try {
    fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  } catch (error) {
    throw new ConfigError(
      `${path} can't be read (${(error as NodeJS.ErrnoException).code ?? 'unknown error'}).`,
    );
  }
  try {
    const stats = fstatSync(fd);
    if (!stats.isFile() || stats.ino !== first.ino || stats.dev !== first.dev) {
      throw new ConfigError(`${path} changed while it was read, so it is not used.`);
    }
    refuseOpen(path, stats, 'Run chmod 600 on it.');
    if (stats.size > limit) throw new ConfigError(`${path} is larger than ${limit} bytes.`);
    return readFileSync(fd, 'utf8');
  } finally {
    closeSync(fd);
  }
}

/** Refuses a file or folder another user owns, or that anyone else may read or change. */
export function refuseOpen(path: string, stats: Stats, fix: string): void {
  const uid = process.getuid?.();
  if (uid !== undefined && stats.uid !== uid) {
    throw new ConfigError(`${path} belongs to another user, so it is not used.`);
  }
  if ((stats.mode & 0o077) !== 0) {
    throw new ConfigError(`${path}: other users can read or change it, so it is not used. ${fix}`);
  }
}

function lstatOrNull(path: string): Stats | null {
  try {
    return lstatSync(path);
  } catch {
    return null;
  }
}

const SPEECH_VARIABLES = [
  'HALCYONIC_WHISPER_BIN',
  'HALCYONIC_WHISPER_MODEL',
  'HALCYONIC_WHISPER_VAD_MODEL',
] as const;

function parseSpeech(env: NodeJS.ProcessEnv): SpeechConfig | null {
  const set = SPEECH_VARIABLES.filter((name) => env[name] !== undefined && env[name] !== '');
  if (set.length === 0) return null;
  if (set.length < SPEECH_VARIABLES.length) {
    const missing = SPEECH_VARIABLES.filter((name) => !set.includes(name));
    throw new ConfigError(
      `Voice needs all of ${SPEECH_VARIABLES.join(', ')}; ${missing.join(', ')} is not set.`,
    );
  }
  return {
    binary: parseExecutable('HALCYONIC_WHISPER_BIN', env.HALCYONIC_WHISPER_BIN) as string,
    model: parseExecutable('HALCYONIC_WHISPER_MODEL', env.HALCYONIC_WHISPER_MODEL) as string,
    vadModel: parseExecutable(
      'HALCYONIC_WHISPER_VAD_MODEL',
      env.HALCYONIC_WHISPER_VAD_MODEL,
    ) as string,
  };
}

/** An Ollama model name as it lists it, such as `qwen3.6:35b-a3b-nvfp4` or `library/model:tag`. */
const OLLAMA_MODEL = /^[A-Za-z0-9][A-Za-z0-9._:/-]{0,199}$/;

/**
 * The companion's Ollama address, from HALCYONIC_COMPANION_OLLAMA_URL or the default: http:// on a
 * loopback address with a port and nothing else. Refuses anything else without repeating it, since
 * an address with credentials in it would print them.
 */
export function companionOllamaAddress(given: string | undefined): URL {
  let ollama: URL;
  try {
    ollama = new URL(given === undefined || given === '' ? DEFAULT_OLLAMA_URL : given);
  } catch {
    throw new ConfigError('HALCYONIC_COMPANION_OLLAMA_URL is not a URL.');
  }
  const host = ollama.hostname.replace(/^\[|\]$/g, '');
  if (
    ollama.protocol !== 'http:' ||
    !LOOPBACK_HOSTS.has(host) ||
    ollama.port === '' ||
    ollama.username !== '' ||
    ollama.password !== '' ||
    ollama.pathname !== '/' ||
    ollama.search !== '' ||
    ollama.hash !== ''
  ) {
    throw new ConfigError(
      `HALCYONIC_COMPANION_OLLAMA_URL must be http:// on a loopback address with a port and nothing else, such as ${DEFAULT_OLLAMA_URL}.`,
    );
  }
  return ollama;
}

function parseCompanion(env: NodeJS.ProcessEnv): CompanionConfig | null {
  const model = env.HALCYONIC_COMPANION_MODEL;
  const address = env.HALCYONIC_COMPANION_OLLAMA_URL;
  if (model === undefined || model === '') {
    if (address !== undefined && address !== '') {
      throw new ConfigError(
        'HALCYONIC_COMPANION_OLLAMA_URL is set, but the companion is off; name its model in HALCYONIC_COMPANION_MODEL.',
      );
    }
    return null;
  }
  if (!OLLAMA_MODEL.test(model)) {
    throw new ConfigError(`HALCYONIC_COMPANION_MODEL is not an Ollama model name, got "${model}".`);
  }
  if (isCloudName(model)) {
    throw new ConfigError(
      `HALCYONIC_COMPANION_MODEL ${model} is one of Ollama's cloud models; the companion runs only on this computer.`,
    );
  }
  const ollama = companionOllamaAddress(address);
  if (proxied(env, ollama)) {
    throw new ConfigError(
      "Node's environment proxy is on (NODE_USE_ENV_PROXY or --use-env-proxy with HTTP_PROXY), and NO_PROXY does not name Ollama's loopback address: the companion's requests would leave through the proxy. Add the address to NO_PROXY, or turn the proxy off.",
    );
  }
  return { model, ollama };
}

/**
 * Whether Node's fetch could send a request to this loopback address through an HTTP proxy, read as
 * its environment proxy reads the environment: on with NODE_USE_ENV_PROXY or --use-env-proxy; the
 * proxy from http_proxy, else HTTP_PROXY, and none when that is empty; the exclusions from no_proxy,
 * else NO_PROXY, where only a value of exactly `*` excludes everything and each entry, split at
 * commas and spaces, must name the host as written in the address (an IPv6 address in brackets),
 * alone or with its port. Anything else counts as proxied: a false refusal costs a setting, a missed
 * proxy would send the exchange off the computer.
 */
function proxied(env: NodeJS.ProcessEnv, address: URL): boolean {
  const on =
    (env.NODE_USE_ENV_PROXY !== undefined &&
      env.NODE_USE_ENV_PROXY !== '' &&
      env.NODE_USE_ENV_PROXY !== '0') ||
    process.execArgv.includes('--use-env-proxy') ||
    (env.NODE_OPTIONS ?? '').includes('--use-env-proxy');
  if (!on) return false;
  const proxy = env.http_proxy ?? env.HTTP_PROXY;
  if (proxy === undefined || proxy === '') return false;
  const exclusions = env.no_proxy ?? env.NO_PROXY ?? '';
  if (exclusions === '*') return false;
  const host = address.hostname.toLowerCase();
  const withPort = `${host}:${address.port}`;
  const excluded = exclusions
    .split(/[,\s]+/)
    .map((entry) => entry.toLowerCase())
    .some((entry) => entry === host || entry === withPort);
  return !excluded;
}

function parseNames(name: string, raw: string | undefined): string[] {
  if (raw === undefined || raw.trim() === '') return [];
  return raw.split(',').map((entry) => {
    const variable = entry.trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(variable)) {
      throw new ConfigError(
        `${name} must list variable names separated by commas, got "${variable}".`,
      );
    }
    return variable;
  });
}

/**
 * The project roots, each an existing folder that may hold projects (`assessFolder`): agents may
 * change everything in a root, so the control plane itself refuses the home folder and every
 * folder holding it, system and shared folders, Halcyonic's data and the rest, whether a root
 * comes from the environment or the settings file (ADR 0024).
 */
function parseProjectRoots(raw: string | undefined, context: FolderContext): string[] {
  if (raw === undefined || raw.trim() === '') return [];
  return raw
    .split(delimiter)
    .filter((entry) => entry !== '')
    .map((entry) => {
      if (!isAbsolute(entry)) {
        throw new ConfigError(
          `HALCYONIC_PROJECT_ROOTS entries must be absolute paths, got "${entry}".`,
        );
      }
      let isDirectory = false;
      try {
        isDirectory = statSync(entry).isDirectory();
      } catch {
        throw new ConfigError(`HALCYONIC_PROJECT_ROOTS entry ${entry} does not exist.`);
      }
      if (!isDirectory)
        throw new ConfigError(`HALCYONIC_PROJECT_ROOTS entry ${entry} is not a directory.`);
      const assessment = assessFolder(realpathSync.native(entry), context);
      if (assessment.verdict === 'refused') {
        throw new ConfigError(
          `HALCYONIC_PROJECT_ROOTS entry ${entry} can't hold projects: ${assessment.reason}`,
        );
      }
      return entry;
    });
}

function parseInteger(
  name: string,
  raw: string | undefined,
  fallback: number,
  min: number,
  max: number,
): number {
  if (raw === undefined || raw === '') return fallback;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < min || value > max) {
    throw new ConfigError(`${name} must be an integer from ${min} to ${max}, got "${raw}".`);
  }
  return value;
}

function parseLogLevel(raw: string | undefined): LogLevel {
  if (raw === undefined || raw === '') return 'info';
  const level = LOG_LEVELS.find((candidate) => candidate === raw);
  if (level === undefined) {
    throw new ConfigError(`HALCYONIC_LOG_LEVEL must be one of ${LOG_LEVELS.join(', ')}.`);
  }
  return level;
}
