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
  readonly sandbox: SandboxMode;
  readonly approvalPolicy: ApprovalPolicy;
}

export type ParsedStartOptions =
  | { readonly ok: true; readonly value: StartOptions }
  | { readonly ok: false; readonly message: string };

const SUPPORTED = ['cwd', 'model', 'sandbox', 'approval_policy'];
const SANDBOXES: readonly SandboxMode[] = ['read-only', 'workspace-write', 'danger-full-access'];
const APPROVAL_POLICIES: readonly ApprovalPolicy[] = ['on-request', 'untrusted'];

/** Model names as Codex and its providers spell them, for example `gpt-5.5` or `llama3:8b`. */
const MODEL_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,127}$/;

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
  const { cwd, model, sandbox = 'workspace-write', approval_policy = 'on-request' } = options;
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
    value: { cwd: decision.directory, model, sandbox: sandboxMode, approvalPolicy },
  };
}

function fail(message: string): ParsedStartOptions {
  return { ok: false, message };
}
