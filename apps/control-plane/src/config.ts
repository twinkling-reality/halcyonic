import { existsSync, readFileSync, statSync } from 'node:fs';
import { isIP } from 'node:net';
import { homedir } from 'node:os';
import { delimiter, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

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
  /**
   * Whether the end of stdin shuts the control plane down as SIGTERM does. For a launcher, such as
   * a test harness, that runs it as a child and holds its stdin open without writing to it: when
   * the launcher exits, however it exits, the operating system closes the pipe and the control
   * plane stops instead of running on as an orphan.
   */
  readonly exitOnStdinEnd: boolean;
}

export class ConfigError extends Error {
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

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ControlPlaneConfig {
  const host = env.HALCYONIC_HOST ?? '127.0.0.1';
  if (!LOOPBACK_HOSTS.has(host)) {
    throw new ConfigError(
      `HALCYONIC_HOST=${host} is not a loopback address. Devices on the network reach the control plane through the network listener instead: set HALCYONIC_NETWORK_HOST.`,
    );
  }
  const port = parseInteger('HALCYONIC_PORT', env.HALCYONIC_PORT, DEFAULT_PORT, 0, 65535);
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
    projectRoots: parseProjectRoots(env.HALCYONIC_PROJECT_ROOTS),
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

/** The settings file in an OpenCode configuration home. */
export const OPENCODE_SETTINGS_PATH = join('opencode', 'opencode.json');

/** Other files OpenCode would read as settings beside it, which would escape the check below. */
const OTHER_OPENCODE_SETTINGS = ['opencode.jsonc', 'config.json'];

/**
 * A model OpenCode reaches through the Ollama on this Mac. Ollama serves a model whose tag ends in
 * `cloud` from its own hosted service (`packages/integrations/opencode/src/models.ts`).
 */
export function isLocalOllamaModel(model: unknown): boolean {
  return typeof model === 'string' && /^ollama\/\S+$/.test(model) && !/[:-]cloud$/.test(model);
}

/**
 * Halcyonic's own OpenCode settings are refused unless their default model, and their small model
 * when they name one, is a model this Mac serves through Ollama, so a hand edit can't make a hosted
 * model the default. A start through Halcyonic always names its model (`model_required`); the
 * default only decides what OpenCode would choose by itself.
 */
function parseOpenCodeConfigHome(raw: string | undefined): string | null {
  const name = 'HALCYONIC_OPENCODE_CONFIG_HOME';
  if (raw === undefined || raw === '') return null;
  if (!isAbsolute(raw)) throw new ConfigError(`${name} must be an absolute path, got "${raw}".`);
  const path = join(raw, OPENCODE_SETTINGS_PATH);
  let settings: unknown;
  try {
    settings = JSON.parse(readFileSync(path, 'utf8'));
  } catch {
    throw new ConfigError(`${name} ${raw} holds no readable ${OPENCODE_SETTINGS_PATH}.`);
  }
  for (const other of OTHER_OPENCODE_SETTINGS) {
    if (existsSync(join(raw, 'opencode', other))) {
      throw new ConfigError(
        `${name} ${raw} also holds opencode/${other}, which OpenCode would read too; remove it.`,
      );
    }
  }
  const { model, small_model: smallModel } = (settings ?? {}) as Record<string, unknown>;
  if (!isLocalOllamaModel(model)) {
    throw new ConfigError(
      `${path} must name a model served on this Mac through Ollama as its "model", such as "ollama/qwen3.6:35b-a3b-nvfp4".`,
    );
  }
  if (smallModel !== undefined && !isLocalOllamaModel(smallModel)) {
    throw new ConfigError(`${path} names a "small_model" that is not served on this Mac.`);
  }
  return raw;
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

function parseProjectRoots(raw: string | undefined): string[] {
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
