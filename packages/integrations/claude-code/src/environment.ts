import { delimiter, dirname } from 'node:path';
import type { ModelServed } from '@halcyonic/contracts';

/**
 * The variables a launched agent inherits from the control plane, when they are set there.
 * Everything else is dropped, so nothing reaches an agent by accident. Names are checked against
 * the pinned Claude Code build; see docs/internal/validation/claude-code-capabilities.md.
 */
export const INHERITED_VARIABLES: readonly string[] = [
  'PATH',
  'HOME',
  'USER',
  'LOGNAME',
  'LANG',
  'LC_ALL',
  'SHELL',
  'TMPDIR',
  'TERM',
  // Claude Code's configuration directory, passed through unchanged so Salidium and Seorak observe
  // these sessions where they observe every other session on the machine.
  'CLAUDE_CONFIG_DIR',
  // Anthropic API key authentication. ANTHROPIC_BASE_URL is deliberately not inherited: it decides
  // where the key is sent, and a tool that launches the control plane can set it for its own
  // endpoint (a Claude Code desktop session does). Pass it on purpose as a configured addition.
  'ANTHROPIC_API_KEY',
  // Amazon Bedrock.
  'CLAUDE_CODE_USE_BEDROCK',
  'ANTHROPIC_BEDROCK_BASE_URL',
  'AWS_REGION',
  'AWS_PROFILE',
  'AWS_ACCESS_KEY_ID',
  'AWS_SECRET_ACCESS_KEY',
  'AWS_SESSION_TOKEN',
  'AWS_BEARER_TOKEN_BEDROCK',
  // Claude Platform on AWS (also reads the AWS credentials above).
  'CLAUDE_CODE_USE_ANTHROPIC_AWS',
  'ANTHROPIC_AWS_BASE_URL',
  'ANTHROPIC_AWS_API_KEY',
  'ANTHROPIC_AWS_WORKSPACE_ID',
  // Google Vertex AI.
  'CLAUDE_CODE_USE_VERTEX',
  'ANTHROPIC_VERTEX_BASE_URL',
  'ANTHROPIC_VERTEX_PROJECT_ID',
  'CLOUD_ML_REGION',
  'GOOGLE_APPLICATION_CREDENTIALS',
  // Microsoft Foundry.
  'CLAUDE_CODE_USE_FOUNDRY',
  'ANTHROPIC_FOUNDRY_BASE_URL',
  'ANTHROPIC_FOUNDRY_RESOURCE',
  'ANTHROPIC_FOUNDRY_API_KEY',
];

/** Variables that switch Claude Code from the Anthropic API to a cloud provider. */
const PROVIDER_SWITCHES = [
  'CLAUDE_CODE_USE_BEDROCK',
  'CLAUDE_CODE_USE_ANTHROPIC_AWS',
  'CLAUDE_CODE_USE_VERTEX',
  'CLAUDE_CODE_USE_FOUNDRY',
];

/** The values Claude Code reads as true in its boolean variables. */
const TRUE_VALUES = new Set(['1', 'true', 'yes', 'on']);

/**
 * Names configuration may not add. HOME and CLAUDE_CONFIG_DIR stay the developer's own so
 * Salidium and Seorak can observe the sessions; SALIDIUM_INTERNAL makes Salidium drop a session's
 * hooks; CLAUDE_CODE_OAUTH_TOKEN is a claude.ai login, which Anthropic does not allow third-party
 * products to offer.
 */
const FORBIDDEN_ADDITIONS = new Set([
  'HOME',
  'CLAUDE_CONFIG_DIR',
  'SALIDIUM_INTERNAL',
  'CLAUDE_CODE_OAUTH_TOKEN',
]);

export class EnvironmentError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'EnvironmentError';
  }
}

/**
 * Builds the complete environment of a launched agent: the allowlisted variables from
 * `inherited`, then `additions`. PATH always ends up able to find `node`, because the hooks that
 * let Salidium and Seorak observe a session run as `node` scripts. Error messages name variables,
 * never their values.
 */
export function buildEnvironment(
  inherited: Readonly<Record<string, string | undefined>>,
  additions: Readonly<Record<string, string>>,
  nodeDirectory: string = dirname(process.execPath),
): Record<string, string> {
  const forbidden = Object.keys(additions).filter((name) => FORBIDDEN_ADDITIONS.has(name));
  if (forbidden.length > 0) {
    throw new EnvironmentError(
      `The Claude Agent environment may not set ${forbidden.join(', ')}. HOME and CLAUDE_CONFIG_DIR stay the developer's own, SALIDIUM_INTERNAL hides sessions from Salidium, and a claude.ai login is not allowed.`,
    );
  }
  const environment: Record<string, string> = {};
  for (const name of INHERITED_VARIABLES) {
    const value = inherited[name];
    if (value !== undefined) environment[name] = value;
  }
  Object.assign(environment, additions);
  if ((environment.HOME ?? '') === '') {
    throw new EnvironmentError(
      'HOME is not set. Claude Code keeps its configuration there, and the hooks that let Salidium and Seorak observe a session write there.',
    );
  }
  environment.PATH = withDirectory(environment.PATH, nodeDirectory);
  if (!authenticatesSupportedWay(environment)) {
    throw new EnvironmentError(
      `Claude Agent needs ANTHROPIC_API_KEY or a cloud provider (${PROVIDER_SWITCHES.join(', ')}). A claude.ai login cannot be used.`,
    );
  }
  return environment;
}

function withDirectory(path: string | undefined, directory: string): string {
  const entries = path === undefined || path === '' ? [] : path.split(delimiter);
  return entries.includes(directory)
    ? entries.join(delimiter)
    : [...entries, directory].join(delimiter);
}

/** Who serves Claude Code's models, and where its requests for them go. */
export interface ModelHost {
  /** Names the host in a model's display name: "Anthropic", a cloud provider, or a gateway. */
  readonly name: string;
  readonly served: ModelServed;
}

const CLOUD_PROVIDERS = [
  {
    switch: 'CLAUDE_CODE_USE_BEDROCK',
    name: 'Amazon Bedrock',
    baseUrl: 'ANTHROPIC_BEDROCK_BASE_URL',
  },
  {
    switch: 'CLAUDE_CODE_USE_ANTHROPIC_AWS',
    name: 'Claude Platform on AWS',
    baseUrl: 'ANTHROPIC_AWS_BASE_URL',
  },
  {
    switch: 'CLAUDE_CODE_USE_VERTEX',
    name: 'Google Vertex AI',
    baseUrl: 'ANTHROPIC_VERTEX_BASE_URL',
  },
  {
    switch: 'CLAUDE_CODE_USE_FOUNDRY',
    name: 'Microsoft Foundry',
    baseUrl: 'ANTHROPIC_FOUNDRY_BASE_URL',
  },
] as const;

/**
 * Who serves the models of a Claude Code launched with `environment`: Anthropic, or the cloud
 * provider a switch selects, served remotely; or, when a base URL sends the requests to a gateway,
 * that gateway, served where its address is. What a gateway forwards to is not known. The name
 * carries the gateway's host and port only, never credentials or a path.
 */
export function modelHost(environment: Readonly<Record<string, string>>): ModelHost {
  const provider = CLOUD_PROVIDERS.find((candidate) => isTrue(environment[candidate.switch]));
  const name = provider?.name ?? 'Anthropic';
  const base = (environment[provider?.baseUrl ?? 'ANTHROPIC_BASE_URL'] ?? '').trim();
  if (base === '') return { name, served: 'remote' };
  const gateway = provider === undefined ? 'gateway' : `${name} gateway`;
  let url: URL;
  try {
    url = new URL(base);
  } catch {
    return { name: gateway, served: 'unknown' };
  }
  const loopback = /^(localhost|127(\.\d{1,3}){3}|\[::1\])$/i.test(url.hostname);
  return { name: `${gateway} at ${url.host}`, served: loopback ? 'this_mac' : 'remote' };
}

function isTrue(value: string | undefined): boolean {
  return TRUE_VALUES.has((value ?? '').trim().toLowerCase());
}

function authenticatesSupportedWay(environment: Readonly<Record<string, string>>): boolean {
  if ((environment.ANTHROPIC_API_KEY ?? '').trim() !== '') return true;
  return PROVIDER_SWITCHES.some((name) => isTrue(environment[name]));
}
