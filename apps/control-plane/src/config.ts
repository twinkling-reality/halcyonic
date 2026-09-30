import { statSync } from 'node:fs';
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
   * The pinned Codex binary, the native one rather than the npm launcher script; the Codex runtime
   * is registered only when it is set.
   */
  readonly codexBinary: string | null;
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
    codexBinary: parseExecutable('HALCYONIC_CODEX_BIN', env.HALCYONIC_CODEX_BIN),
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
