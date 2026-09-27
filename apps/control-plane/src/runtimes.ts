import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { ClaudeAgentRuntimeAdapter } from '@halcyonic/integration-claude-code';
import type { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { OpenCodeRuntimeAdapter } from '@halcyonic/integration-opencode';
import type { DirectoryPolicy, RuntimeAdapter } from '@halcyonic/runtime-core';
import { ConfigError, type ControlPlaneConfig } from './config.ts';

/** The file in the data directory that may hold the Anthropic API key, instead of the environment. */
export const ANTHROPIC_KEY_FILE = 'anthropic-api-key';

/** The file in the data directory where the OpenCode server Halcyonic launched is recorded. */
export const OPENCODE_SERVER_RECORD = 'opencode-server.json';

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
        inheritedEnvironment: withAnthropicKey(dependencies.environment, dependencies.dataDir),
        environment: additions,
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
        env: additions,
      }),
    );
  }
  return adapters;
}

/**
 * Stops runtime servers that an earlier control plane launched and that outlived it, such as an
 * OpenCode server left behind by a crash, before anything else can reach them.
 */
export async function stopStaleRuntimeServers(
  adapters: readonly RuntimeAdapter[],
): Promise<{ runtimeId: string; outcome: string }[]> {
  const stopped: { runtimeId: string; outcome: string }[] = [];
  for (const adapter of adapters) {
    if (!(adapter instanceof OpenCodeRuntimeAdapter)) continue;
    const result = await adapter.stopStaleServer();
    if (result.outcome !== 'none') {
      stopped.push({ runtimeId: adapter.descriptor.runtime_id, outcome: result.outcome });
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
