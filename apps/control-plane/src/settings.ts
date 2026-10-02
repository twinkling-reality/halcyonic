import {
  chmodSync,
  lstatSync,
  mkdirSync,
  renameSync,
  rmSync,
  type Stats,
  writeFileSync,
} from 'node:fs';
import { delimiter, join } from 'node:path';
import { ConfigError, readPrivateFile, refuseOpen } from './config.ts';

/**
 * The file in the data directory that holds the settings `pnpm mac-setup` writes, read by the
 * control plane at startup for any variable the environment does not set (ADR 0024).
 */
export const SETTINGS_FILE = 'settings.json';

/** The format of the file; another number is refused rather than half understood. */
export const SETTINGS_FORMAT = 1;

const MAX_SETTINGS_BYTES = 64 * 1024;

/**
 * The variables a settings file may hold: which folders agents may use, the pinned agent apps,
 * Halcyonic's own OpenCode settings, voice, and the listener for paired devices. Nothing that
 * spends model credit or widens what reaches an agent can be set there: the Claude Agent runtime,
 * its executable and the variables passed through to agents stay in the environment alone, and no
 * setting names a model.
 */
export const SETTING_NAMES = [
  'HALCYONIC_PROJECT_ROOTS',
  'HALCYONIC_OPENCODE_BIN',
  'HALCYONIC_OPENCODE_CONFIG_HOME',
  'HALCYONIC_CODEX_BIN',
  'HALCYONIC_WHISPER_BIN',
  'HALCYONIC_WHISPER_MODEL',
  'HALCYONIC_WHISPER_VAD_MODEL',
  'HALCYONIC_NETWORK_HOST',
] as const;
export type SettingName = (typeof SETTING_NAMES)[number];

/** Settings as the environment would carry them: project roots joined by the path delimiter. */
export type HostSettings = Readonly<Partial<Record<SettingName, string>>>;

/** Variables refused in the file with a reason of their own, because they can start paid model use. */
const ENVIRONMENT_ONLY: ReadonlySet<string> = new Set([
  'HALCYONIC_CLAUDE_AGENT',
  'HALCYONIC_CLAUDE_EXECUTABLE',
  'HALCYONIC_AGENT_ENV',
]);

function isSettingName(name: string): name is SettingName {
  return (SETTING_NAMES as readonly string[]).includes(name);
}

/**
 * Reads the settings file of a data directory; empty when there is none. The file is refused,
 * and the control plane does not start, unless only this user can read or change it: a regular
 * file, not a link or a pipe, owned by this user with mode 600, in a data directory this user owns
 * that is closed to others (mode 700). Whoever can change it decides which folders agents may
 * change and which binaries run.
 */
export function readHostSettings(dataDir: string): HostSettings {
  const path = join(dataDir, SETTINGS_FILE);
  let stats: Stats;
  try {
    stats = lstatSync(path);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return {};
    throw new ConfigError(`${path} can't be read (${(error as NodeJS.ErrnoException).code}).`);
  }
  if (stats.isSymbolicLink()) throw new ConfigError(`${path} is a link, so it is not used.`);
  if (!stats.isFile()) throw new ConfigError(`${path} is not a regular file, so it is not used.`);
  const folder = lstatSync(dataDir);
  if (!folder.isDirectory()) throw new ConfigError(`${dataDir} is not a folder, or is a link.`);
  refuseOpen(dataDir, folder, 'Run chmod 700 on it.');
  return parseHostSettings(path, readPrivateFile(path, MAX_SETTINGS_BYTES));
}

/** Parses the file's text; `path` only names it in errors. */
export function parseHostSettings(path: string, text: string): HostSettings {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new ConfigError(`${path} is not valid JSON.`);
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new ConfigError(`${path} must hold one JSON object.`);
  }
  const { format, ...entries } = parsed as Record<string, unknown>;
  if (format !== SETTINGS_FORMAT) {
    throw new ConfigError(`${path} must say "format": ${SETTINGS_FORMAT}.`);
  }
  const settings: Partial<Record<SettingName, string>> = {};
  for (const [name, value] of Object.entries(entries)) {
    if (ENVIRONMENT_ONLY.has(name)) {
      throw new ConfigError(
        `${name} can't be set in ${path}: it can start paid model use, so only the environment sets it.`,
      );
    }
    if (!isSettingName(name)) throw new ConfigError(`${path} can't set ${name}.`);
    if (name === 'HALCYONIC_PROJECT_ROOTS') {
      if (!Array.isArray(value) || !value.every(isFolderEntry)) {
        throw new ConfigError(
          `${name} in ${path} must be a list of folders, none containing "${delimiter}".`,
        );
      }
      settings[name] = value.join(delimiter);
    } else {
      if (typeof value !== 'string' || value === '') {
        throw new ConfigError(`${name} in ${path} must be a non-empty string.`);
      }
      settings[name] = value;
    }
  }
  return settings;
}

function isFolderEntry(value: unknown): value is string {
  return typeof value === 'string' && value !== '' && !value.includes(delimiter);
}

/** The project roots the settings hold, in order. */
export function settingRoots(settings: HostSettings): string[] {
  const raw = settings.HALCYONIC_PROJECT_ROOTS;
  return raw === undefined ? [] : raw.split(delimiter).filter((entry) => entry !== '');
}

/**
 * The environment the control plane reads: each setting the environment does not carry is taken
 * from the file. A variable present in the environment wins, even empty, so
 * `HALCYONIC_PROJECT_ROOTS= pnpm start` runs with no roots whatever the file says.
 */
export function withSettings(env: NodeJS.ProcessEnv, settings: HostSettings): NodeJS.ProcessEnv {
  const merged: NodeJS.ProcessEnv = { ...env };
  for (const name of SETTING_NAMES) {
    const value = settings[name];
    if (env[name] === undefined && value !== undefined) merged[name] = value;
  }
  return merged;
}

/** The names of the settings the environment leaves to the file. */
export function settingsInUse(env: NodeJS.ProcessEnv, settings: HostSettings): SettingName[] {
  return SETTING_NAMES.filter((name) => env[name] === undefined && settings[name] !== undefined);
}

/**
 * Writes the settings file whole, mode 600, through a new file renamed into place, after making
 * the data directory owner-only as the control plane does. `undefined` entries are left out.
 */
export function writeHostSettings(dataDir: string, settings: HostSettings): string {
  mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  chmodSync(dataDir, 0o700);
  const path = join(dataDir, SETTINGS_FILE);
  const document: Record<string, unknown> = { format: SETTINGS_FORMAT };
  for (const name of SETTING_NAMES) {
    const value = settings[name];
    if (value === undefined) continue;
    document[name] = name === 'HALCYONIC_PROJECT_ROOTS' ? settingRoots(settings) : value;
  }
  const temporary = `${path}.${process.pid}.tmp`;
  rmSync(temporary, { force: true });
  writeFileSync(temporary, `${JSON.stringify(document, null, 2)}\n`, { mode: 0o600, flag: 'wx' });
  renameSync(temporary, path);
  return path;
}
