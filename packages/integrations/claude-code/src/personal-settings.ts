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
    instructions = readFileSync(join(folder, INSTRUCTIONS_FILE), 'utf8');
  } catch {
    instructions = null;
  }
  if (instructions !== null && Buffer.byteLength(instructions) > INSTRUCTIONS_LIMIT) {
    throw new RuntimeActionError(
      'runtime_start_failed',
      `Claude Agent was not started: ${join(folder, INSTRUCTIONS_FILE)} is over ${INSTRUCTIONS_LIMIT / 1024} KB, more than Halcyonic passes to a task.`,
    );
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
        if (!OBSERVERS.some((observer) => observer.recognises(h.command as string, event)))
          continue;
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

/** The person's own instructions file, as Claude Code names it in its configuration folder. */
const INSTRUCTIONS_FILE = 'CLAUDE.md';

/** The most of the person's instructions passed to a task (AUDIT3): 64 KB. */
const INSTRUCTIONS_LIMIT = 64 * 1024;

/** The integrations whose hooks a task outside the sandbox passes on, each with its own project's rule. */
const OBSERVERS: readonly {
  readonly name: string;
  readonly recognises: (command: string, event: string) => boolean;
}[] = [
  { name: 'Salidium', recognises: (command, event) => isSalidiumHook(command, event) },
  { name: 'Seorak', recognises: (command, event) => isSeorakHook(command, event) },
];

/**
 * What of the person's own Claude Code set-up a task outside the sandbox uses, read from what the adapter passes
 * (the lead, 2026-11-12: the page says it from this, never from a list of its own): their instructions file, the
 * hooks of the named integrations, and nothing else of their settings.
 */
export interface PersonalSetupUse {
  /** The person's own instructions file, passed whole. */
  readonly instructions: string;
  /** The integrations whose hooks are passed on, by name. */
  readonly hooksOf: readonly string[];
  /** Whether anything else of the person's settings is used: never. */
  readonly restUsed: false;
  /**
   * Whether an instructions file in the task's folder or in a folder above it reaches the model at the start. Claude
   * Agent's: no, since no settings source is loaded (`settingSources: []`, so no CLAUDE.md is walked up to). The agent
   * may still read such a file with its own tools once it works. The setup page says it from this alone (R2).
   */
  readonly folderInstructionsSent: boolean;
}

export function personalSetupUsed(): PersonalSetupUse {
  return {
    instructions: INSTRUCTIONS_FILE,
    hooksOf: OBSERVERS.map((observer) => observer.name),
    restUsed: false,
    folderInstructionsSent: false,
  };
}

/** Salidium's levels of pressure, one per event (its `CLAUDE_CODE_HOOK_EVENT_BUDGET`). */
const SALIDIUM_PRESSURES = '(?:shed-first|shed-second|lifecycle|retain)';

/** A single-quoted absolute path, apostrophes written the way `apostrophe` writes them, nothing else. */
function quotedPath(apostrophe: string): string {
  return `'(/(?:[^'\\r\\n]|${apostrophe})*)'`;
}

/**
 * Salidium's command, whole, as its installer writes it for `event` (its `relayCommand` and
 * `buildClaudeCodeHooks`): `SALIDIUM_HOOK=1 '<home>/hooks/relay.sh' claude-code <event> <pressure>`, anchored at both
 * ends, so nothing can follow or precede it (AUDIT3 F1). Older installs (a path under `/.salidium/hooks/` run through
 * `/bin/sh`) are not recognised, so not passed.
 */
export function isSalidiumHook(command: string, event: string): boolean {
  if (!/^[A-Za-z]{1,40}$/.test(event)) return false;
  const whole = new RegExp(
    `^SALIDIUM_HOOK=1 ${quotedPath("'\\\\''")} claude-code ${event} ${SALIDIUM_PRESSURES}$`,
  );
  const found = whole.exec(command);
  return found !== null && (found[1] as string).endsWith('/hooks/relay.sh');
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
 * Seorak's command, whole, as its installer writes it on this platform (its `commandFor`):
 * `'<node>' '<script>' --seorak-hook-path=<base64url file URL of the script>`, anchored at both ends; the script is
 * that event's own and the marker names the very same script (AUDIT3 F1).
 */
export function isSeorakHook(command: string, event: string): boolean {
  const script = SEORAK_SCRIPTS[event];
  if (script === undefined) return false;
  const apostrophe = `'"'"'`;
  const whole = new RegExp(
    `^${quotedPath(apostrophe)} ${quotedPath(apostrophe)} --seorak-hook-path=([A-Za-z0-9_-]+)$`,
  );
  const found = whole.exec(command);
  if (found === null) return false;
  const path = (found[2] as string).replaceAll(apostrophe, "'");
  if (!path.endsWith(`/${script}`)) return false;
  let marked: string;
  try {
    marked = fileURLToPath(Buffer.from(found[3] as string, 'base64url').toString('utf8'));
  } catch {
    return false;
  }
  return isAbsolute(marked) && marked === path;
}

/**
 * Environment variables a settings tier may not set for a run: each names or picks a credential, sends the requests
 * that carry the key elsewhere or lets something read them (a proxy, a certificate authority, TLS checks off), or
 * loads code into the process that holds the key (AUDIT3 F2).
 */
const CREDENTIAL_VARIABLE =
  /^(ANTHROPIC_|CLAUDE_CODE_USE_|CLAUDE_CODE_OAUTH_TOKEN$|CLAUDE_CODE_CLIENT_|CLAUDE_CODE_.*(CERT|KEY|TOKEN|HOST|URL|PROXY|AUTH)|AWS_|GOOGLE_APPLICATION_CREDENTIALS$|CLOUD_ML_REGION$|NODE_OPTIONS$|NODE_EXTRA_CA_CERTS$|NODE_TLS_REJECT_UNAUTHORIZED$|SSL_CERT_(FILE|DIR)$)/;
const PROXY_VARIABLE = /^(https?_proxy|all_proxy)$/i;

/** Settings that fetch a credential, or run a command of the tier's own beside the key. */
const CREDENTIAL_HELPERS = [
  'apiKeyHelper',
  'awsAuthRefresh',
  'awsCredentialExport',
  'gcpAuthRefresh',
  'otelHeadersHelper',
  'hooks',
  'statusLine',
];

/**
 * The settings Claude Code would still apply with no settings file loaded (the managed, policy tier) that could pay
 * for a run instead of the stored key, by name: never a value. Empty when none.
 */
export function credentialSettings(effective: unknown): string[] {
  const settings = record(effective);
  if (settings === null) return [];
  const named = Object.keys(record(settings.env) ?? {}).filter(
    (name) => CREDENTIAL_VARIABLE.test(name) || PROXY_VARIABLE.test(name),
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
    `Claude Agent was not started: this Mac's managed Claude Code settings set ${named.join(', ')}, which could pay for the task instead of the key you stored, send its requests elsewhere, or run beside it.`,
  );
}

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}
