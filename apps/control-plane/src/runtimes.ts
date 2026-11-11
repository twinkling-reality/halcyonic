import { readFileSync, realpathSync, statSync } from 'node:fs';
import { dirname, join, sep } from 'node:path';
import {
  ClaudeAgentRuntimeAdapter,
  type ClaudeConfinementOptions,
} from '@halcyonic/integration-claude-code';
import {
  CodexRuntimeAdapter,
  type CodexRuntimeOptions,
  PROXY_VARIABLES,
  SIGN_IN_VARIABLES,
} from '@halcyonic/integration-codex';
import type { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { OpenCodeRuntimeAdapter } from '@halcyonic/integration-opencode';
import type { DirectoryPolicy, RuntimeAdapter } from '@halcyonic/runtime-core';
import { type Confinement, TOOL_PREFIXES, toolFoldersFor } from '@halcyonic/seatbelt';
import { ConfigError, type ControlPlaneConfig, readPrivateFile } from './config.ts';
import { type HeldSecret, looksLikeCredential } from './core/redaction.ts';
import { OMNORIUM_CREDENTIAL_FILE } from './intelligence/catalog.ts';
import { CODEX_HOME_FOLDER } from './intelligence/codex-home.ts';
import { SEORAK_CREDENTIAL_FILE } from './intelligence/evaluation.ts';
import { SALIDIUM_CREDENTIAL_FILE } from './intelligence/understanding.ts';
import { NETWORK_KEY_FILE } from './network/certificate.ts';

/** The file in the data directory that may hold the Anthropic API key, instead of the environment. */
export const ANTHROPIC_KEY_FILE = 'anthropic-api-key';

/** The file in the data directory where the OpenCode server Halcyonic launched is recorded. */
export const OPENCODE_SERVER_RECORD = 'opencode-server.json';

/** The file in the data directory listing the Claude Code processes Halcyonic launched, while they run. */
export const CLAUDE_AGENT_PROCESS_RECORD = 'claude-agent-processes.json';

/** The file in the data directory where the Codex app-server Halcyonic launched is recorded. */
export const CODEX_SERVER_RECORD = 'codex-server.json';

export { CODEX_HOME_FOLDER } from './intelligence/codex-home.ts';

export interface RuntimeDependencies {
  readonly mock: MockRuntimeAdapter;
  readonly directoryPolicy: DirectoryPolicy;
  /** The control plane's own environment, from which pass-through variables are copied. */
  readonly environment: NodeJS.ProcessEnv;
  /** The control plane's data directory, where a key file may be kept. */
  readonly dataDir: string;
  /**
   * The starter a project folder was made from, or null (ADR 0037). Codex runs such a project
   * confined to its folder (ADR 0041); asked at each start, since projects are made later.
   */
  readonly starterOf?: (directory: string) => string | null;
  /**
   * The folder of the project a project folder was made as a VR version of, or null (M1.8c-1): its
   * agent reads it but cannot change it, and never its version control or secret-shaped files.
   */
  readonly originalOf?: (directory: string) => string | null;
  /**
   * Every bound project's folder, read from the journal at each start (AUDIT BOX-3 F1): a run's
   * sandbox denies every one of them inside its own folder, and where it cannot, never ask is not
   * offered there.
   */
  readonly projectFolders?: () => readonly string[];
}

/**
 * The bound project folders strictly inside `directory`, each as the file system resolves it, so a
 * run in a folder that holds another project's folder never reaches that project (AUDIT BOX-3 F1).
 */
export function foldersInside(directory: string, folders: readonly string[]): string[] {
  const real = (path: string) => {
    try {
      return realpathSync.native(path);
    } catch {
      return path;
    }
  };
  const outer = real(directory);
  const prefix = outer.endsWith(sep) ? outer : `${outer}${sep}`;
  return [...new Set(folders.map(real))].filter((folder) => folder.startsWith(prefix)).sort();
}

/**
 * The runtimes this control plane hosts. The mock runtime is always present and labeled synthetic;
 * real runtimes are registered only when configured, and a misconfigured one stops startup rather
 * than silently disappearing.
 */
export function createRuntimeAdapters(
  config: ControlPlaneConfig,
  dependencies: RuntimeDependencies,
): RuntimeAdapter[] {
  const adapters: RuntimeAdapter[] = [dependencies.mock];
  const additions = passThrough(config.agentEnvironment, dependencies.environment);
  if (config.claudeAgent) {
    adapters.push(
      new ClaudeAgentRuntimeAdapter({
        directoryPolicy: dependencies.directoryPolicy,
        processRecordFile: join(dependencies.dataDir, CLAUDE_AGENT_PROCESS_RECORD),
        inheritedEnvironment: withAnthropicKey(dependencies.environment, dependencies.dataDir),
        environment: claudeAgentEnvironment(additions),
        ...(config.claudeExecutable !== null && {
          pathToClaudeCodeExecutable: config.claudeExecutable,
        }),
        confinement: claudeConfinement(dependencies),
      }),
    );
  }
  if (config.opencodeBinary !== null) {
    adapters.push(
      new OpenCodeRuntimeAdapter({
        binaryPath: config.opencodeBinary,
        serverRecordFile: join(dependencies.dataDir, OPENCODE_SERVER_RECORD),
        directoryPolicy: dependencies.directoryPolicy,
        env: openCodeEnvironment(config, additions),
        foldersInside: (folder) => foldersInside(folder, dependencies.projectFolders?.() ?? []),
        sandbox: process.platform === 'darwin' ? openCodeSandbox(config, dependencies) : null,
      }),
    );
  }
  if (config.codexBinary !== null) {
    adapters.push(
      new CodexRuntimeAdapter({
        binaryPath: config.codexBinary,
        codexHome: join(dependencies.dataDir, CODEX_HOME_FOLDER),
        serverRecordFile: join(dependencies.dataDir, CODEX_SERVER_RECORD),
        directoryPolicy: dependencies.directoryPolicy,
        env: codexEnvironment(additions),
        foldersInside: (folder) => foldersInside(folder, dependencies.projectFolders?.() ?? []),
        ...codexConfinement(dependencies),
      }),
    );
  }
  return adapters;
}

/**
 * Whether a runtime is held to a folder it is given, so that in a task's own copy it writes only
 * that copy (ADR 0045), on this computer:
 * - Codex, where its confinement covers the folder: every execution's folder on macOS (BOX-2).
 *   Built from the same confinement the Codex adapter is given, so it follows what that covers.
 * - OpenCode on macOS, where every execution's folder has a sandboxed server of its own that writes
 *   that folder alone (BOX-3), the sandbox `createRuntimeAdapters` gives it there and nowhere else.
 * - Never Claude Code, which runs with no sandbox, nor the mock runtime, which works in no folder.
 */
export function heldToFolder(
  adapters: readonly RuntimeAdapter[],
  dependencies: Pick<
    RuntimeDependencies,
    'starterOf' | 'originalOf' | 'dataDir' | 'projectFolders'
  >,
): (runtimeId: string, directory: string) => boolean {
  const codex = adapters.find((adapter) => adapter instanceof CodexRuntimeAdapter);
  const opencode = adapters.find((adapter) => adapter instanceof OpenCodeRuntimeAdapter);
  const confinement = codexConfinement(dependencies).confinement;
  const openCodeSandboxed = process.platform === 'darwin';
  return (runtimeId, directory) =>
    (codex !== undefined &&
      confinement !== undefined &&
      runtimeId === codex.descriptor.runtime_id &&
      confinement.forDirectory(directory) !== null) ||
    (opencode !== undefined && openCodeSandboxed && runtimeId === opencode.descriptor.runtime_id);
}

/**
 * On macOS, every execution's folder gets a Codex server of its own inside Halcyonic's sandbox (ADR
 * 0041, BOX-2): it reads and writes that folder, reads the Node installation and the tool folders,
 * and reaches only Ollama. A project made from a starter also reads the starter's shared dependencies
 * its `node_modules` links to, and a VR version its original. Elsewhere, none.
 */
export function codexConfinement(
  dependencies: Pick<
    RuntimeDependencies,
    'starterOf' | 'originalOf' | 'dataDir' | 'projectFolders'
  >,
  node: string = process.execPath,
): Pick<CodexRuntimeOptions, 'confinement'> {
  if (process.platform !== 'darwin') return {};
  return {
    confinement: {
      forDirectory: (directory) => {
        const starter = dependencies.starterOf?.(directory) ?? null;
        const inner = foldersInside(directory, dependencies.projectFolders?.() ?? []);
        return {
          project: directory,
          ...folderReads(dependencies, directory),
          // A starter's project asks nothing (ADR 0041); any other folder asks when its start asks.
          ...(starter === null ? {} : { asksNothing: true }),
          // Halcyonic's data folder is refused outright (AUDIT M18C1-M1).
          hidden: [dependencies.dataDir],
          // Every other bound project inside this folder, refused to reads and writes (AUDIT BOX-3 F1).
          ...(inner.length === 0 ? {} : { inner }),
        };
      },
      toolFolders: toolFoldersFor(node),
      loopbackPorts: [OLLAMA_PORT],
    },
  };
}

/**
 * What an execution's folder reads besides itself, never writing, on any runtime's sandbox (ADR
 * 0041, 0048): a starter's project the starter's shared dependencies its `node_modules` links to, a
 * VR version its original, without the original's version control and secret-shaped files.
 */
export function folderReads(
  dependencies: Pick<RuntimeDependencies, 'starterOf' | 'originalOf' | 'dataDir'>,
  directory: string,
): Pick<Confinement, 'readable' | 'originals'> {
  const starter = dependencies.starterOf?.(directory) ?? null;
  const original = dependencies.originalOf?.(directory) ?? null;
  return {
    readable: starter === null ? [] : [join(dependencies.dataDir, 'starters', starter)],
    ...(original === null ? {} : { originals: [original] }),
  };
}

/**
 * Claude Code's confinement (ADR 0048, BOX-4): a new app's project, made from a starter, runs inside
 * Halcyonic's closed profile, reading the starter's shared dependencies (and a VR version its
 * original) besides its folder, its own home, configuration and temporary folder under the data
 * folder, and nothing else of it. Every other folder runs as before, with the person's own settings
 * and hooks, so Salidium and Seorak observe it. The adapter confines on macOS only.
 */
export function claudeConfinement(
  dependencies: Pick<RuntimeDependencies, 'starterOf' | 'originalOf' | 'dataDir'>,
  node: string = process.execPath,
): ClaudeConfinementOptions {
  return {
    dataFolder: dependencies.dataDir,
    toolFolders: toolFoldersFor(node),
    hidden: [dependencies.dataDir],
    forDirectory: (directory) =>
      (dependencies.starterOf?.(directory) ?? null) === null
        ? null
        : folderReads(dependencies, directory),
  };
}

/**
 * What OpenCode's sandbox lets it reach on this Mac (ADR 0028, 0048): Ollama only through the
 * adapter's gate, which passes the model list, model details and chat with a model on this Mac, and
 * no other port on loopback but the server's and the gate's own, which the adapter adds. The profile
 * is closed by default (BOX-3b): each folder's server reads and writes that folder and its own
 * folders, which the adapter adds, reads Halcyonic's own OpenCode settings, the tools and what the
 * folder names (`folderReads`), and nothing else: never Halcyonic's data directory, the person's
 * home, their own OpenCode settings included, or another project.
 */
export function openCodeSandbox(
  config: Pick<ControlPlaneConfig, 'opencodeConfigHome'>,
  dependencies: Pick<RuntimeDependencies, 'starterOf' | 'originalOf' | 'dataDir'> &
    Partial<Pick<RuntimeDependencies, 'environment'>>,
  node: string = process.execPath,
) {
  // OpenCode searches with the first `rg` on its PATH, which its commands get with the product's Node
  // first (BOX-3b's open item). Under Homebrew or /usr/local it reads that prefix's tool parts, as
  // Node's; anywhere else only the `rg` file itself, never the folder around it, which in the usual
  // places (~/.local, ~/.cargo, ~/bin) holds the person's credentials (AUDIT VRIFY-4d MUST).
  const rg = ripgrepPath(
    [dirname(node), dependencies.environment?.PATH ?? ''].filter((part) => part !== '').join(':'),
  );
  const toolFolders = [
    ...new Set([...toolFoldersFor(node), ...(rg === null ? [] : ripgrepReads(rg))]),
  ].filter((folder) => !holdsTheHome(folder, dependencies.environment?.HOME));
  return {
    loopbackPorts: [],
    ollama: `http://127.0.0.1:${OLLAMA_PORT}`,
    readable: config.opencodeConfigHome === null ? [] : [config.opencodeConfigHome],
    hidden: [dependencies.dataDir],
    toolFolders,
    forFolder: (folder: string) => folderReads(dependencies, folder),
  };
}

/**
 * Whether OpenCode can search files inside its sandbox: it runs the first `rg` on the PATH it
 * inherits, and without one downloads ripgrep from GitHub, which the sandbox refuses (ADR 0028).
 */
export function ripgrepOnPath(path: string | undefined): boolean {
  return ripgrepPath(path) !== null;
}

/**
 * What the profile reads for the `rg` OpenCode would run: a Homebrew or /usr/local prefix's tool parts
 * where it lives in one, else the resolved file alone. ripgrep's release and cargo builds link only
 * the system's libraries on macOS, so the file is all it needs.
 */
export function ripgrepReads(rg: string): string[] {
  let real = rg;
  try {
    real = realpathSync(rg);
  } catch {
    return [];
  }
  return TOOL_PREFIXES.some((prefix) => real.startsWith(`${prefix}/`))
    ? toolFoldersFor(real)
    : [real];
}

/** True for `/`, the person's home, or any folder above it: never a tool folder, whatever the program. */
function holdsTheHome(folder: string, home: string | undefined): boolean {
  if (folder === '/' || folder === '') return true;
  if (home === undefined || home === '') return false;
  const realHome = (() => {
    try {
      return realpathSync(home);
    } catch {
      return home;
    }
  })();
  return realHome === folder || realHome.startsWith(folder.endsWith('/') ? folder : `${folder}/`);
}

/** The first `rg` on `path` that is a file, as OpenCode would run it, or null. */
export function ripgrepPath(path: string | undefined): string | null {
  for (const folder of (path ?? '').split(':')) {
    if (folder === '') continue;
    try {
      if (statSync(join(folder, 'rg')).isFile()) return join(folder, 'rg');
    } catch {
      // Not there: the next folder on the path.
    }
  }
  return null;
}

/** Ollama's port on this Mac, its default, which the gate in front of it for OpenCode dials. */
export const OLLAMA_PORT = 11434;

/**
 * Stops runtime processes that an earlier control plane launched and that outlived it, such as an
 * OpenCode or Codex server or Claude Code processes left behind by a crash, before anything else
 * can reach them. Reports each recorded process and what stopping it found.
 */
export async function stopStaleRuntimeServers(
  adapters: readonly RuntimeAdapter[],
): Promise<{ runtimeId: string; outcome: string; pid: number }[]> {
  const stopped: { runtimeId: string; outcome: string; pid: number }[] = [];
  for (const adapter of adapters) {
    const runtimeId = adapter.descriptor.runtime_id;
    if (adapter instanceof OpenCodeRuntimeAdapter || adapter instanceof CodexRuntimeAdapter) {
      const result = await adapter.stopStaleServer();
      if (result.outcome !== 'none') {
        stopped.push({ runtimeId, outcome: result.outcome, pid: result.pid });
      }
    } else if (adapter instanceof ClaudeAgentRuntimeAdapter) {
      for (const { pid, outcome } of await adapter.stopStaleProcesses()) {
        stopped.push({ runtimeId, outcome, pid });
      }
    }
  }
  return stopped;
}

/**
 * The environment with ANTHROPIC_API_KEY taken from `<data dir>/anthropic-api-key` when the
 * environment does not set it. A key file other users can read is refused, like the access token's.
 */
function withAnthropicKey(environment: NodeJS.ProcessEnv, dataDir: string): NodeJS.ProcessEnv {
  if (environment.ANTHROPIC_API_KEY) return environment;
  const path = join(dataDir, ANTHROPIC_KEY_FILE);
  let mode: number;
  try {
    mode = statSync(path).mode;
  } catch {
    return environment;
  }
  if ((mode & 0o077) !== 0) {
    throw new ConfigError(
      `${path} can be read by other users, so it is not used. Run chmod 600 on it.`,
    );
  }
  const key = readFileSync(path, 'utf8').trim();
  if (key === '') throw new ConfigError(`${path} is empty.`);
  return { ...environment, ANTHROPIC_API_KEY: key };
}

/**
 * The labels Halcyonic sets on the Claude Code sessions it launches, after any pass-through
 * variables so they cannot be overridden: Seorak attributes a session to its launcher with
 * SEORAK_LAUNCHER (documented in Seorak's public setup guide and ADR 007).
 */
export function claudeAgentEnvironment(additions: Record<string, string>): Record<string, string> {
  return { ...additions, SEORAK_LAUNCHER: 'halcyonic' };
}

/**
 * OpenCode's additions: the pass-through variables, then Halcyonic's own OpenCode settings as its
 * configuration home when configured, given to OpenCode alone, so the person's own OpenCode
 * settings stay as they are and nothing else Halcyonic launches sees the change.
 */
export function openCodeEnvironment(
  config: Pick<ControlPlaneConfig, 'opencodeConfigHome'>,
  additions: Record<string, string>,
): Record<string, string> {
  if (config.opencodeConfigHome === null) return additions;
  return { ...additions, XDG_CONFIG_HOME: config.opencodeConfigHome };
}

/**
 * Codex's additions: the pass-through variables without the ones Codex signs in with, the proxy
 * variables and every `OPENAI_` variable, which may be set for another runtime. A `CODEX_` variable
 * is for Codex alone, so it reaches the adapter, which refuses it. Through Halcyonic Codex runs only on models served on this Mac, never signed in, so a
 * key passed for another runtime never reaches it, and neither does a proxy, which would carry
 * its requests to Ollama, prompts and code with them, through another host.
 */
export function codexEnvironment(additions: Record<string, string>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(additions).filter(
      ([name]) =>
        !SIGN_IN_VARIABLES.includes(name) &&
        !PROXY_VARIABLES.includes(name) &&
        !name.startsWith('OPENAI_'),
    ),
  );
}

/**
 * The words of a variable name that read as a secret's, matched whole: GIT_AUTHOR_NAME is no
 * secret's name, SSH_AUTH_SOCK is (its value is only a path, and reads as its name).
 */
const SECRET_WORDS: ReadonlySet<string> = new Set([
  'KEY',
  'APIKEY',
  'TOKEN',
  'SECRET',
  'PASSWORD',
  'PASSWD',
  'PASS',
  'PASSPHRASE',
  'PAT',
  'AUTH',
  'CREDENTIAL',
  'CREDENTIALS',
  'COOKIE',
  'SESSION',
  'HEADER',
  'HEADERS',
]);

/** Where a value such as `Authorization: Bearer token` or `a=1; b=2` divides into parts. */
const VALUE_PARTS = /[\s:,;=]+/;

/**
 * A URL's user and password in a value: `scheme://user:password@host`, up to the last `@` before
 * the host, since a password can hold a raw `@`.
 */
const URL_USERINFO = /\b[a-z][a-z0-9+.-]{0,31}:\/\/([^\s/?#]+)@/gi;

/** The keys whose values are secrets, as a connection string, a query, JSON or a command names them. */
const SECRET_KEY =
  '(?:password|passwd|pwd|passphrase|secret|token|api[_-]?key|access[_-]?key|credentials?|sig)';

/** A quoted value, escaped quotes and all. */
const QUOTED = `"(?:[^"\\\\]|\\\\.)*"|'(?:[^'\\\\]|\\\\.)*'`;

/**
 * A value given to a secret's key, as a connection string, a query or JSON gives it: `password=…`,
 * `Pwd: …`, `client_secret=…`, `"api_key": "…"`, an Azure SAS's `sig=…`. Quoted, or up to `&`,
 * `;`, whitespace or a quote, a comma in it ending it only before another key.
 */
const KEYED_SECRET = new RegExp(
  `(?<![A-Za-z0-9])[A-Za-z0-9_.-]{0,32}?${SECRET_KEY}["']?\\s*[=:]\\s*(${QUOTED}|[^\\s&;,"']+(?:,(?![A-Za-z_][A-Za-z0-9_.-]*\\s*[=:])[^\\s&;,"']+)*)`,
  'gi',
);

/** A value given to a secret's command-line option: `--password value`, `--api-key "…"`. */
const OPTION_SECRET = new RegExp(
  `(?<![A-Za-z0-9-])--?[A-Za-z0-9_.-]{0,32}?${SECRET_KEY}\\s+(${QUOTED}|[^\\s"']+)`,
  'gi',
);

/** A value as written, and without its quotes, escapes and percent-encoding where it has them. */
function asWritten(given: string): string[] {
  const quoted = /^(["']).*\1$/s.test(given);
  const inner = quoted ? given.slice(1, -1) : given;
  const forms = [inner];
  if (quoted) forms.push(inner.replace(/\\(.)/g, '$1'));
  for (const form of [...forms]) {
    try {
      forms.push(decodeURIComponent(form));
    } catch {
      // Not percent-encoded as written: the form as written is held.
    }
  }
  return [...new Set(forms)].filter((form) => form !== '');
}

/**
 * The secrets within a value, however they look: each URL's password and `user:password`, a user
 * alone only when it reads as a credential, as a token in `https://token@host`; each value given to
 * a secret's key; and each value given to a secret's command-line option.
 */
function secretsWithin(value: string): string[] {
  const found: string[] = [];
  for (const [, userinfo = ''] of value.matchAll(URL_USERINFO)) {
    const colon = userinfo.indexOf(':');
    if (colon < 0) {
      if (looksLikeCredential(userinfo)) found.push(userinfo);
      continue;
    }
    const password = userinfo.slice(colon + 1);
    if (password === '') continue;
    found.push(userinfo, ...asWritten(password));
  }
  for (const pattern of [KEYED_SECRET, OPTION_SECRET]) {
    for (const [, given = ''] of value.matchAll(pattern)) found.push(...asWritten(given));
  }
  return found;
}

/**
 * A part that starts from the root or home, which names a place: never held for looking random
 * alone. A value held by its name, its key, an option or a URL is held even so, since a base64
 * secret starts with `/` about once in 64.
 */
const ABSOLUTE_PATH = /^(?:\/|~\/|[A-Za-z]:[\\/])/;

/** A part of a value without the quotes and brackets around it: `"q7Xk…"}` reads as `q7Xk…`. */
function bare(part: string): string {
  return part.replace(/^["'([{<]+|["')\]}>]+$/g, '');
}

/** Whether a variable's name reads as a secret's: one of its words is in SECRET_WORDS. */
export function secretName(name: string): boolean {
  return name
    .toUpperCase()
    .split(/[^A-Z0-9]+/)
    .some((word) => SECRET_WORDS.has(word));
}

/**
 * Every secret Halcyonic holds or passes to a runtime, each with what a person reads in its place,
 * taken out of a runtime's text before a device sees it (core/redaction.ts): the access token, the
 * Anthropic key, OpenCode's server password, Salidium's, Seorak's and Omnorium's credentials, the
 * network listener's private key, whole, with its body's lines joined and line by line, since a
 * copy a person reads may be joined or escaped as JSON escapes it, and of the
 * HALCYONIC_AGENT_ENV values, those whose names read as secret or that read as a credential by
 * themselves, each part of one that reads as a credential by itself, as the value of a header in
 * `Name: value`, but never one that is an absolute path, and the password in any URL, connection
 * string or command line in one, all under their variable's name. An address or a region passed
 * to agents, such as ANTHROPIC_BASE_URL or AWS_REGION, stays in the text a person reads. Read each time it is asked,
 * since a server's password changes with each launch and a credential when it is replaced; a file
 * that can't be read gives nothing. Device credentials are kept only as hashes, so their shape is
 * what takes them out of error text.
 */
export function heldSecrets(
  config: ControlPlaneConfig,
  dependencies: { readonly environment: NodeJS.ProcessEnv; readonly dataDir: string },
  accessToken: string,
  adapters: readonly RuntimeAdapter[],
): () => HeldSecret[] {
  const agentValues = Object.entries(
    passThrough(config.agentEnvironment, dependencies.environment),
  ).flatMap(([name, value]) =>
    [
      ...new Set([
        ...(secretName(name) || looksLikeCredential(value) ? [value] : []),
        ...value
          .split(VALUE_PARTS)
          .map(bare)
          .filter(
            (part) => part !== value && !ABSOLUTE_PATH.test(part) && looksLikeCredential(part),
          ),
        ...secretsWithin(value),
      ]),
    ].map((held) => ({ what: name, value: held })),
  );
  return () => {
    let anthropic: string | undefined;
    try {
      anthropic = withAnthropicKey(
        dependencies.environment,
        dependencies.dataDir,
      ).ANTHROPIC_API_KEY;
    } catch {
      anthropic = dependencies.environment.ANTHROPIC_API_KEY;
    }
    const credentials = (
      [
        [SALIDIUM_CREDENTIAL_FILE, 'Salidium credential'],
        [SEORAK_CREDENTIAL_FILE, 'Seorak credential'],
        [OMNORIUM_CREDENTIAL_FILE, 'Omnorium credential'],
      ] as const
    ).flatMap(([file, what]) => {
      try {
        return [{ what, value: readPrivateFile(join(dependencies.dataDir, file), 4096).trim() }];
      } catch {
        return [];
      }
    });
    let networkKey: string[] = [];
    try {
      const pem = readPrivateFile(join(dependencies.dataDir, NETWORK_KEY_FILE), 16_384).trim();
      const lines = pem
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line !== '' && !line.startsWith('-----'));
      networkKey = [pem, lines.join(''), ...lines];
    } catch {
      // No listener was ever turned on, or its key can't be read: nothing to hold.
    }
    return [
      { what: 'access token', value: accessToken },
      ...(anthropic === undefined ? [] : [{ what: 'Anthropic key', value: anthropic }]),
      ...adapters.flatMap((adapter) =>
        adapter instanceof OpenCodeRuntimeAdapter
          ? adapter.secrets().map((value) => ({ what: 'OpenCode server password', value }))
          : [],
      ),
      ...credentials,
      ...networkKey.map((value) => ({ what: 'network key', value })),
      ...agentValues,
    ];
  };
}

function passThrough(
  names: readonly string[],
  environment: NodeJS.ProcessEnv,
): Record<string, string> {
  const additions: Record<string, string> = {};
  for (const name of names) {
    const value = environment[name];
    if (value !== undefined) additions[name] = value;
  }
  return additions;
}
