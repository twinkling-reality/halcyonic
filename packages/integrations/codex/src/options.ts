import { statSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import type { RuntimeOptions } from '@halcyonic/contracts';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';
import type { ApprovalPolicy, SandboxMode } from './protocol.ts';

export interface StartOptions {
  /** The directory the thread works in: the real path the host's directory policy returned. */
  readonly cwd: string;
  /** A model Codex is configured to reach; undefined keeps Codex's configured model. */
  readonly model: string | undefined;
  /**
   * A model provider Codex knows, built in (`openai`, `ollama`, `lmstudio`) or configured in its
   * `config.toml`; undefined keeps Codex's configured provider.
   */
  readonly modelProvider: string | undefined;
  /**
   * The context window, in tokens, Codex assumes for the thread's model, instead of what its model
   * catalog says. Codex knows nothing about a model outside its catalog, such as a local one, and
   * assumes 272,000 tokens.
   */
  readonly contextWindow: number | undefined;
  /** The token count at which Codex compacts the thread's context. */
  readonly autoCompactTokenLimit: number | undefined;
  readonly sandbox: SandboxMode;
  readonly approvalPolicy: ApprovalPolicy;
}

export type ParsedStartOptions =
  | { readonly ok: true; readonly value: StartOptions }
  | { readonly ok: false; readonly message: string };

const SUPPORTED = [
  'cwd',
  'model',
  'model_provider',
  'context_window',
  'auto_compact_token_limit',
  'sandbox',
  'approval_policy',
];
const SANDBOXES: readonly SandboxMode[] = ['read-only', 'workspace-write', 'danger-full-access'];
const APPROVAL_POLICIES: readonly ApprovalPolicy[] = ['on-request', 'untrusted'];

/** Model names as Codex and its providers spell them, for example `gpt-5.5` or `llama3:8b`. */
const MODEL_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,127}$/;
/** Provider ids as Codex's configuration keys them, for example `openai`, `ollama` or `my-gateway`. */
const PROVIDER_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
/** Bounds on token counts, generous for any model a Mac or a provider serves. */
const MAX_TOKENS = 10_000_000;

/**
 * Checks the start options of a Codex execution, then asks the host's directory policy. Every
 * thread gets an explicit sandbox and approval policy, so the developer's Codex configuration can
 * never start one that asks nobody. Refused, because approvals would stop reaching the person:
 *
 * - `approval_policy: "never"`, and Codex's granular policies, which reject some approvals
 *   themselves;
 * - `sandbox: "danger-full-access"` with `on-request`: Codex 0.157.0 then runs every command it
 *   does not flag as dangerous without asking (codex-rs/core/src/exec_policy.rs at rust-v0.157.0).
 *   With `untrusted` it asks before every command not known to be safe.
 */
export function parseStartOptions(
  options: RuntimeOptions,
  policy: DirectoryPolicy,
): ParsedStartOptions {
  const unknown = Object.keys(options).filter((key) => !SUPPORTED.includes(key));
  if (unknown.length > 0) {
    return fail(
      `Unknown Codex runtime options: ${unknown.join(', ')}. Supported: ${SUPPORTED.join(', ')}.`,
    );
  }
  const {
    cwd,
    model,
    model_provider,
    context_window,
    auto_compact_token_limit,
    sandbox = 'workspace-write',
    approval_policy = 'on-request',
  } = options;
  if (typeof cwd !== 'string' || cwd === '') {
    return fail('Option "cwd" is required: the absolute path of an existing directory.');
  }
  if (!isAbsolute(cwd)) return fail(`Option "cwd" must be an absolute path, got "${cwd}".`);
  let isDirectory: boolean;
  try {
    isDirectory = statSync(cwd).isDirectory();
  } catch {
    return fail(`Option "cwd" does not exist: ${cwd}`);
  }
  if (!isDirectory) return fail(`Option "cwd" is not a directory: ${cwd}`);
  if (model !== undefined && (typeof model !== 'string' || !MODEL_PATTERN.test(model))) {
    return fail('Option "model" must be a model name, such as "gpt-5.5".');
  }
  if (
    model_provider !== undefined &&
    (typeof model_provider !== 'string' || !PROVIDER_PATTERN.test(model_provider))
  ) {
    return fail('Option "model_provider" must be a model provider id, such as "ollama".');
  }
  const contextWindow = tokenCount(context_window);
  if (contextWindow === null) return tokenCountRefused('context_window');
  const autoCompactTokenLimit = tokenCount(auto_compact_token_limit);
  if (autoCompactTokenLimit === null) return tokenCountRefused('auto_compact_token_limit');
  if (
    contextWindow !== undefined &&
    autoCompactTokenLimit !== undefined &&
    autoCompactTokenLimit >= contextWindow
  ) {
    return fail(
      'Option "auto_compact_token_limit" must be smaller than "context_window": Codex compacts before the window is full.',
    );
  }
  const sandboxMode = SANDBOXES.find((mode) => mode === sandbox);
  if (sandboxMode === undefined) {
    return fail(`Option "sandbox" must be one of ${SANDBOXES.join(', ')}.`);
  }
  const approvalPolicy = APPROVAL_POLICIES.find((mode) => mode === approval_policy);
  if (approvalPolicy === undefined) {
    return fail(
      approval_policy === 'never'
        ? 'Option "approval_policy" cannot be "never": the person supervising the execution would never be asked.'
        : `Option "approval_policy" must be one of ${APPROVAL_POLICIES.join(', ')}.`,
    );
  }
  if (sandboxMode === 'danger-full-access' && approvalPolicy === 'on-request') {
    return fail(
      'Option "sandbox" "danger-full-access" needs "approval_policy" "untrusted": with "on-request" Codex runs commands without asking.',
    );
  }
  let decision: ReturnType<DirectoryPolicy>;
  try {
    decision = policy(cwd);
  } catch (error) {
    return fail(
      `The directory policy could not decide on ${cwd}: ${error instanceof Error ? error.message : String(error)}`,
    );
  }
  if (!decision.ok) return fail(decision.message);
  return {
    ok: true,
    value: {
      cwd: decision.directory,
      model,
      modelProvider: model_provider,
      contextWindow,
      autoCompactTokenLimit,
      sandbox: sandboxMode,
      approvalPolicy,
    },
  };
}

/** A whole number of tokens, undefined when the option is absent, or null when it is invalid. */
function tokenCount(value: unknown): number | undefined | null {
  if (value === undefined) return undefined;
  return typeof value === 'number' &&
    Number.isSafeInteger(value) &&
    value >= 1 &&
    value <= MAX_TOKENS
    ? value
    : null;
}

function tokenCountRefused(name: string): ParsedStartOptions {
  return fail(`Option "${name}" must be a whole number of tokens from 1 to ${MAX_TOKENS}.`);
}

function fail(message: string): ParsedStartOptions {
  return { ok: false, message };
}
