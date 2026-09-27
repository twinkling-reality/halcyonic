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

/** The file in the data directory listing the Claude Code processes Halcyonic launched, while they run. */
export const CLAUDE_AGENT_PROCESS_RECORD = 'claude-agent-processes.json';

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
        env: additions,
      }),
    );
  }
  return adapters;
}

/**
 * Stops runtime processes that an earlier control plane launched and that outlived it, such as an
 * OpenCode server or Claude Code processes left behind by a crash, before anything else can reach
 * them. Reports each recorded process and what stopping it found.
 */
export async function stopStaleRuntimeServers(
  adapters: readonly RuntimeAdapter[],
): Promise<{ runtimeId: string; outcome: string; pid: number }[]> {
  const stopped: { runtimeId: string; outcome: string; pid: number }[] = [];
  for (const adapter of adapters) {
    const runtimeId = adapter.descriptor.runtime_id;
    if (adapter instanceof OpenCodeRuntimeAdapter) {
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
