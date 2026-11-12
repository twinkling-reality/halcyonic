import { readFileSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { RuntimeActionError } from '@halcyonic/runtime-core';

/**
 * What of the person's own Claude Code set-up an unconfined run takes (VRIFY, 2026-11-12, lead's ruling on the
 * settings gap): no settings file is loaded wholesale (`settingSources: []`), since a settings `env` could add a
 * credential that outranks the stored key (ANTHROPIC_AUTH_TOKEN, a provider switch) and pay for the task. Two things
 * are passed explicitly instead: the observation hooks of Salidium and Seorak, recognised by each project's own rule
 * and copied field by field, so they still see the session; and the person's own CLAUDE.md, whose words cannot pay
 * for anything. Nothing else of theirs (env, apiKeyHelper, permissions, model, other hooks, plugins) is passed.
 */
export interface PersonalSettings {
  /** The observation hooks, by event, as the flag tier takes them (the `settings` option). */
  readonly hooks: Readonly<Record<string, readonly HookGroup[]>>;
  /** The person's own CLAUDE.md, whole, or null for none. */
  readonly instructions: string | null;
}

export interface HookGroup {
  readonly hooks: readonly HookCommand[];
}

export interface HookCommand {
  readonly type: 'command';
  readonly command: string;
  readonly async?: boolean;
  readonly timeout?: number;
}

/** Where Claude Code keeps the person's own settings: `CLAUDE_CONFIG_DIR`, else `~/.claude`. */
export function personalConfigFolder(environment: Readonly<Record<string, string>>): string {
  const configured = (environment.CLAUDE_CONFIG_DIR ?? '').trim();
  return configured !== '' ? configured : join(environment.HOME ?? '', '.claude');
}

/**
 * The person's set-up as an unconfined run takes it, read from the run's own environment's folder. A settings file
 * that is there but is not JSON refuses the run: which hooks it meant cannot be told, and loading it is never the
 * fallback.
 */
export function personalSettings(environment: Readonly<Record<string, string>>): PersonalSettings {
  const folder = personalConfigFolder(environment);
  const path = join(folder, 'settings.json');
  let raw: string | null;
  try {
    raw = readFileSync(path, 'utf8');
  } catch {
    raw = null;
  }
  let hooks: Record<string, HookGroup[]> = {};
  if (raw !== null) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      throw new RuntimeActionError(
        'runtime_start_failed',
        `Claude Agent was not started: ${path} is not JSON, so Halcyonic cannot tell which of its hooks are Salidium's and Seorak's.`,
      );
    }
    hooks = observationHooks(parsed);
  }
  let instructions: string | null;
  try {
    instructions = readFileSync(join(folder, 'CLAUDE.md'), 'utf8');
  } catch {
    instructions = null;
  }
  return { hooks, instructions: instructions?.trim() === '' ? null : instructions };
}

/** The observation hooks in a person's settings: Salidium's and Seorak's alone, each entry's four fields only. */
export function observationHooks(settings: unknown): Record<string, HookGroup[]> {
  const out: Record<string, HookGroup[]> = {};
  const byEvent = record(record(settings)?.hooks);
  if (byEvent === null) return out;
  for (const [event, groups] of Object.entries(byEvent)) {
    if (!Array.isArray(groups)) continue;
    for (const group of groups) {
      const entry = record(group);
      // A group with a matcher names tools to run on; neither project writes one, so it is not theirs.
      if (entry === null || entry.matcher !== undefined || !Array.isArray(entry.hooks)) continue;
      const kept: HookCommand[] = [];
      for (const hook of entry.hooks) {
        const h = record(hook);
        if (h === null || h.type !== 'command' || typeof h.command !== 'string') continue;
        if (!isSalidiumHook(h.command) && !isSeorakHook(h.command, event)) continue;
        kept.push({
          type: 'command',
          command: h.command,
          ...(typeof h.async === 'boolean' && { async: h.async }),
          ...(typeof h.timeout === 'number' &&
            Number.isFinite(h.timeout) && { timeout: h.timeout }),
        });
      }
      if (kept.length === 0) continue;
      const groupsOut = out[event] ?? [];
      groupsOut.push({ hooks: kept });
      out[event] = groupsOut;
    }
  }
  return out;
}

/** Salidium's own rule (its `isSalidiumHook`): `SALIDIUM_HOOK=1 '<home>/hooks/relay.sh' ...`. */
export function isSalidiumHook(command: string): boolean {
  return command.startsWith("SALIDIUM_HOOK=1 '") && /\/hooks\/relay\.sh'(?:\s|$)/.test(command);
}

/** Seorak's script for each event it observes (its `EVENT_BINS`). */
const SEORAK_SCRIPTS: Readonly<Record<string, string>> = {
  SessionStart: 'hook-session-start.mjs',
  PostToolUse: 'hook-tool-use.mjs',
  PostToolUseFailure: 'hook-tool-use.mjs',
  SessionEnd: 'hook-session-end.mjs',
  Notification: 'hook-notification.mjs',
  UserPromptSubmit: 'hook-user-prompt.mjs',
  Stop: 'hook-turn-end.mjs',
  StopFailure: 'hook-turn-end.mjs',
};

/**
 * Seorak's own rule (its `seorakCommandPath`), strictly: a `--seorak-hook-path=` argument that decodes to an
 * absolute file URL whose script is that event's own.
 */
export function isSeorakHook(command: string, event: string): boolean {
  const script = SEORAK_SCRIPTS[event];
  if (script === undefined) return false;
  const marker = /(?:^|\s)--seorak-hook-path=([A-Za-z0-9_-]+)(?:\s|$)/.exec(command);
  if (marker === null) return false;
  let path: string;
  try {
    path = fileURLToPath(Buffer.from(marker[1] as string, 'base64url').toString('utf8'));
  } catch {
    return false;
  }
  return isAbsolute(path) && path.endsWith(`/${script}`);
}

/** Environment variables a settings tier may not set for a run: each names or picks a credential. */
const CREDENTIAL_VARIABLE =
  /^(ANTHROPIC_|CLAUDE_CODE_USE_|CLAUDE_CODE_OAUTH_TOKEN$|AWS_|GOOGLE_APPLICATION_CREDENTIALS$|CLOUD_ML_REGION$)/;

/** Settings that fetch a credential by running a command. */
const CREDENTIAL_HELPERS = [
  'apiKeyHelper',
  'awsAuthRefresh',
  'awsCredentialExport',
  'gcpAuthRefresh',
];

/**
 * The settings Claude Code would still apply with no settings file loaded (the managed, policy tier) that could pay
 * for a run instead of the stored key, by name: never a value. Empty when none.
 */
export function credentialSettings(effective: unknown): string[] {
  const settings = record(effective);
  if (settings === null) return [];
  const named = Object.keys(record(settings.env) ?? {}).filter((name) =>
    CREDENTIAL_VARIABLE.test(name),
  );
  for (const helper of CREDENTIAL_HELPERS) {
    if (settings[helper] !== undefined && settings[helper] !== null) named.push(helper);
  }
  return named.sort();
}

/** Refuses a run whose remaining settings would pay for it with another credential, naming them. */
export function refuseOtherCredentials(effective: unknown): void {
  const named = credentialSettings(effective);
  if (named.length === 0) return;
  throw new RuntimeActionError(
    'runtime_start_failed',
    `Claude Agent was not started: this Mac's managed Claude Code settings set ${named.join(', ')}, which would pay for the task instead of the key you stored.`,
  );
}

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}
