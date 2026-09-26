import { statSync } from 'node:fs';
import { homedir } from 'node:os';
import { delimiter, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const LOG_LEVELS = ['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent'] as const;
export type LogLevel = (typeof LOG_LEVELS)[number];

/**
 * Only loopback addresses are accepted. Serving on a LAN interface needs device pairing and
 * encrypted transport, which do not exist yet (see docs/internal/architecture/SECURITY.md).
 */
const LOOPBACK_HOSTS = new Set(['127.0.0.1', '::1', 'localhost']);

export interface ControlPlaneConfig {
  readonly host: string;
  readonly port: number;
  readonly dataDir: string;
  readonly logLevel: LogLevel;
  readonly commandTimeoutMs: number;
  readonly scenariosDir: string;
  /** Directories agents may work in, with everything below them. Empty allows none. */
  readonly projectRoots: readonly string[];
}

export class ConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ConfigError';
  }
}

export const DEFAULT_PORT = 47800;

export function defaultDataDir(env: NodeJS.ProcessEnv = process.env): string {
  return resolve(env.HALCYONIC_DATA_DIR ?? join(homedir(), '.halcyonic'));
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): ControlPlaneConfig {
  const host = env.HALCYONIC_HOST ?? '127.0.0.1';
  if (!LOOPBACK_HOSTS.has(host)) {
    throw new ConfigError(
      `HALCYONIC_HOST=${host} is not a loopback address. Serving beyond this machine needs device pairing and encrypted transport, which are not implemented yet.`,
    );
  }
  return {
    host,
    port: parseInteger('HALCYONIC_PORT', env.HALCYONIC_PORT, DEFAULT_PORT, 0, 65535),
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
  };
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
