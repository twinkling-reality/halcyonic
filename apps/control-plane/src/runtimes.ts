import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { ClaudeAgentRuntimeAdapter } from '@halcyonic/integration-claude-code';
import { CodexRuntimeAdapter } from '@halcyonic/integration-codex';
import type { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { OpenCodeRuntimeAdapter } from '@halcyonic/integration-opencode';
import type { DirectoryPolicy, RuntimeAdapter } from '@halcyonic/runtime-core';
import { ConfigError, type ControlPlaneConfig, readPrivateFile } from './config.ts';
import { type HeldSecret, looksLikeCredential } from './core/redaction.ts';
import { SEORAK_CREDENTIAL_FILE } from './intelligence/evaluation.ts';
import { SALIDIUM_CREDENTIAL_FILE } from './intelligence/understanding.ts';

/** The file in the data directory that may hold the Anthropic API key, instead of the environment. */
export const ANTHROPIC_KEY_FILE = 'anthropic-api-key';

/** The file in the data directory where the OpenCode server Halcyonic launched is recorded. */
export const OPENCODE_SERVER_RECORD = 'opencode-server.json';

/** The file in the data directory listing the Claude Code processes Halcyonic launched, while they run. */
export const CLAUDE_AGENT_PROCESS_RECORD = 'claude-agent-processes.json';

/** The file in the data directory where the Codex app-server Halcyonic launched is recorded. */
export const CODEX_SERVER_RECORD = 'codex-server.json';

export interface RuntimeDependencies {
  readonly mock: MockRuntimeAdapter;
  readonly directoryPolicy: DirectoryPolicy;
  /** The control plane's own environment, from which pass-through variables are copied. */
  readonly environment: NodeJS.ProcessEnv;
  /** The control plane's data directory, where a key file may be kept. */
  readonly dataDir: string;
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
      }),
    );
  }
  if (config.codexBinary !== null) {
    adapters.push(
      new CodexRuntimeAdapter({
        binaryPath: config.codexBinary,
        serverRecordFile: join(dependencies.dataDir, CODEX_SERVER_RECORD),
        directoryPolicy: dependencies.directoryPolicy,
        env: additions,
      }),
    );
  }
  return adapters;
}

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

/**
 * A value given to a password, secret or token key, as a connection string or a query gives it:
 * `password=…`, `Pwd: …`, `client_secret=…`, `"token": "…"`, quoted or up to `&`, `;`, `,` or a
 * space.
 */
const KEYED_SECRET =
  /(?<![A-Za-z0-9])[A-Za-z0-9_.-]{0,32}?(?:password|passwd|pwd|secret|token)["']?\s*[=:]\s*("[^"]*"|'[^']*'|[^\s&;,"']+)/gi;

/** A password, as written and, where it is percent-encoded, decoded. */
function asWrittenAndDecoded(password: string): string[] {
  try {
    const decoded = decodeURIComponent(password);
    return decoded === password ? [password] : [password, decoded];
  } catch {
    // Not percent-encoded as written: the password as written is held.
    return [password];
  }
}

/**
 * The secrets in a value's URLs and connection strings, however they look: each URL's password,
 * as written and decoded, and `user:password`, a user alone only when it reads as a credential, as
 * a token in `https://token@host`; and each value given to a password, secret or token key.
 */
function urlSecrets(value: string): string[] {
  const found: string[] = [];
  for (const [, userinfo = ''] of value.matchAll(URL_USERINFO)) {
    const colon = userinfo.indexOf(':');
    if (colon < 0) {
      if (looksLikeCredential(userinfo)) found.push(userinfo);
      continue;
    }
    const password = userinfo.slice(colon + 1);
    if (password === '') continue;
    found.push(userinfo, ...asWrittenAndDecoded(password));
  }
  for (const [, given = ''] of value.matchAll(KEYED_SECRET)) {
    const unquoted = /^(["']).*\1$/.test(given) ? given.slice(1, -1) : given;
    if (unquoted !== '') found.push(...asWrittenAndDecoded(unquoted));
  }
  return found;
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
 * Anthropic key, OpenCode's server password, Salidium's and Seorak's credentials, and of the
 * HALCYONIC_AGENT_ENV values, those whose names read as secret or that read as a credential by
 * themselves, each part of one that reads as a credential by itself, as the value of a header in
 * `Name: value`, and the password in any URL or connection string in one, all under their
 * variable's name. An address or a region passed to agents, such as
 * ANTHROPIC_BASE_URL or AWS_REGION, stays in the text a person reads. Read each time it is asked,
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
      ...(secretName(name) || looksLikeCredential(value) ? [value] : []),
      ...value.split(VALUE_PARTS).filter((part) => part !== value && looksLikeCredential(part)),
      ...urlSecrets(value),
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
      ] as const
    ).flatMap(([file, what]) => {
      try {
        return [{ what, value: readPrivateFile(join(dependencies.dataDir, file), 4096).trim() }];
      } catch {
        return [];
      }
    });
    return [
      { what: 'access token', value: accessToken },
      ...(anthropic === undefined ? [] : [{ what: 'Anthropic key', value: anthropic }]),
      ...adapters.flatMap((adapter) =>
        adapter instanceof OpenCodeRuntimeAdapter
          ? adapter.secrets().map((value) => ({ what: 'OpenCode server password', value }))
          : [],
      ),
      ...credentials,
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
