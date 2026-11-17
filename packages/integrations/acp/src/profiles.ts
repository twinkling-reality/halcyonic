import { type ListedModel, modelsFromKiroList } from './models.ts';

/**
 * What Halcyonic knows of one agent app that speaks the Agent Client Protocol: how to start its ACP
 * server, the version it was verified at, and the few things its own requests leave unsaid that a
 * runtime test of that version showed (validation/acp-agents.md). Everything else the adapter reads
 * from the protocol itself, the same for every app.
 */
export interface AcpProfile {
  /** The runtime id and kind Halcyonic gives it, and the name clients show. */
  readonly runtimeId: string;
  readonly kind: string;
  readonly displayName: string;
  /** The version the profile was verified at, as the app's `initialize` reports it in `agentInfo.version`. */
  readonly version: string;
  /**
   * Where the version is read when `initialize` names none: the app's own version command and the pattern of its
   * line, its one group the version. Null where `initialize` always names it.
   */
  readonly versionLine: { readonly args: readonly string[]; readonly pattern: RegExp } | null;
  /** The arguments after the binary that start its ACP server over stdio. */
  readonly args: readonly string[];
  /** Variables the adapter sets for every launch, never taken from the person's environment. */
  readonly environment: Readonly<Record<string, string>>;
  /**
   * The variables the app, or the provider it calls, reads for itself: its sign-in, its endpoints, its
   * own home. Never passed on from the host's environment, so a launch signs in only the app's own way.
   */
  readonly ownVariables: readonly string[];
  /**
   * Tools whose request shows one replacement while the app makes it at every occurrence, by the name
   * the app gives in `toolCall.name`: verified per version, so an approval says `every` truly.
   */
  readonly replacesEveryOccurrence: readonly string[];
  /**
   * File tools whose input says the whole change, by field name, as a run of this version showed (Kiro carries no
   * change Halora can read in its diff alone): a replacement (`replace`: the file, the text replaced, its
   * replacement, and the field that says whether every occurrence is), and a new file (`create`: the file and its
   * whole text, carried only where nothing is at that path yet). Null where the app's diff is read instead.
   */
  readonly fileInputs: {
    readonly replace: {
      readonly path: string;
      readonly before: string;
      readonly after: string;
      readonly every: string;
    };
    readonly create: { readonly path: string; readonly text: string };
  } | null;
  /**
   * Folders the app would load from its working folder as settings of its own (hooks, rules, tool servers): the
   * agent-app profile refuses them to it, so a project cannot widen what it does unasked (AUDIT3 AC1).
   */
  readonly projectSettings: readonly string[];
  /**
   * The settings home of Halora's own the app is launched with, under the person's home, so the person's own
   * settings for it (an allow-everything mode, hooks, tool servers) never apply to a task (AUDIT3 AC2). Its
   * sign-in is kept elsewhere, so it still applies.
   */
  readonly settingsHome: {
    readonly folder: string;
    readonly args: (folder: string) => readonly string[];
    readonly environment: (folder: string) => Readonly<Record<string, string>>;
  };
  /**
   * The session mode a run of this version showed asks the person before it acts; a session that starts in
   * another (an allow-by-itself mode) is switched to it, or not started.
   */
  readonly askingMode: string;
  /**
   * Session options (`configOptions`) that must hold their asking value for the whole session, by id: set at the
   * start where the app reports another, watched afterwards like the mode, so the app never acts unasked by them.
   */
  readonly askingOptions: Readonly<Record<string, string>>;
  /**
   * Session options that keep the person's work to themselves, by id and value (Kiro's `contentCollection`
   * `disabled`, so a task's work is not used to improve the maker's service): held like the asking options, set at
   * the start and read back, a session that turns one back on stopped (the lead's ruling, 2026-11-17).
   */
  readonly privateOptions: Readonly<Record<string, string>>;
  /**
   * Where the app lists its models, and the session option a start sets to the one chosen, read back before the
   * task runs: `session`, that option's choices in `session/new`'s answer (Antigravity's `model`); `command`, the
   * app's own listing command (Kiro's `chat --list-models --format json`, since its `session/new` names its models
   * only some of the time). `service` names what serves them, beside each model's name. Null where no list was seen:
   * no model choice.
   */
  readonly models:
    | { readonly from: 'session'; readonly option: string; readonly service: string }
    | {
        readonly from: 'command';
        readonly option: string;
        readonly service: string;
        readonly args: readonly string[];
        readonly parse: (stdout: string) => readonly ListedModel[];
      }
    | null;
  /**
   * A turn-level review the app asks for once its file changes are staged, by the `_meta` field and type its request
   * carries (Kiro's `turn_approval`, under autopilot off): shown as the changes of the calls it lists, so it can be
   * approved whole. Null where the app asks for none.
   */
  readonly turnReview: { readonly meta: string; readonly type: string } | null;
  /**
   * The app's own report that it took a person's answer, where it gives one (Kiro's `_meta.kiro` `interaction_resolved`
   * with the option chosen): an approval counts as taken on it, as it does on the call's next status.
   */
  readonly answerReport: { readonly meta: string; readonly kind: string } | null;
  /**
   * The statuses a call's report takes once the app took a denial of it: `failed` for every app seen, and for Cursor
   * also `completed`, which it reports for a refused command that did not run (seen 2026-11-17). Never `in_progress`,
   * which Antigravity reports before it fails a refused call.
   */
  readonly deniedStatuses: readonly ('failed' | 'completed')[];
  /**
   * Where the app reports what a turn used, as a run of this version showed: `prompt`, its answer to `session/prompt`
   * (`usage`: `inputTokens` not read from or written to the cache, `cachedReadTokens`, `cachedWriteTokens`,
   * `outputTokens` with its reasoning, `thoughtTokens`), summed by the app over the turn's model calls. Null where it
   * reports none Halora can carry (Kiro gives credits only, Cursor nothing).
   */
  readonly usage: 'prompt' | null;
  /**
   * The ACP auth method that hands the app its own stored sign-in, for an app whose session needs it (Cursor's
   * `cursor_login`): asked only once a session is refused as not signed in, with a short wait, since a signed-out app
   * may never answer it. No secret passes: the app reads its own. Null where the app signs itself in.
   */
  readonly signIn: string | null;
  /**
   * Files the adapter writes into each launch's fresh settings home before the app starts, by path inside it, so the
   * app's asking setup is Halora's own and never its default (Cursor's `cli-config.json`, allowlist, nothing allowed).
   */
  readonly settingsFiles: Readonly<Record<string, string>>;
  /**
   * A shell tool whose input names the whole command in one field and nothing else that acts: its name (the call's
   * `name`, else its `kind`), that field, the field that says it runs in the background, the inputs that change
   * neither what runs nor where, and where its commands run as a run of this version showed (`task_folder`: each
   * starts in the session's folder, a `cd` carrying over to none; `unknown` where no run showed it). A request of it
   * carries the command in its parts (`ApprovalCommand`), its summary built from them alone.
   */
  readonly shell: {
    readonly tool: string;
    readonly command: string;
    readonly background: string | null;
    readonly quiet: readonly string[];
    readonly runsIn: 'task_folder' | 'unknown';
    /** The app's own field naming the folder a command runs in, where its tool declares one. */
    readonly cwd: string | null;
  } | null;
}

/** Google's Antigravity CLI, `agy acp` (verified 2026-11-17, validation/acp-agents.md). */
export const ANTIGRAVITY: AcpProfile = {
  runtimeId: 'antigravity',
  kind: 'antigravity',
  displayName: 'Antigravity 1.5.10',
  version: '1.5.10',
  versionLine: null,
  args: ['acp', '--disable-telemetry'],
  // Its self-updater would replace the pinned binary under a running control plane.
  environment: { AGY_CLI_DISABLE_AUTO_UPDATE: 'true' },
  ownVariables: [
    'GEMINI_API_KEY',
    'GOOGLE_API_KEY',
    'GOOGLE_GEMINI_BASE_URL',
    'GOOGLE_CLOUD_PROJECT',
    'GOOGLE_CLOUD_QUOTA_PROJECT',
    'GOOGLE_CLOUD_LOCATION',
    'GOOGLE_APPLICATION_CREDENTIALS',
    'AGY_ADC_AUTH',
    'ANTIGRAVITY_HARNESS_PATH',
  ],
  replacesEveryOccurrence: ['edit_file'],
  fileInputs: null,
  projectSettings: ['.agents', '.gemini'],
  askingMode: 'default',
  askingOptions: {},
  privateOptions: {},
  // Seen 2026-11-17: `session/new` lists its models as the `model` option, and `session/set_config_option` took one.
  models: { from: 'session', option: 'model', service: 'Antigravity' },
  turnReview: null,
  answerReport: null,
  deniedStatuses: ['failed'],
  // Seen on the stand-in (three model calls of 100 input, 40 of it cached, 10 output, 5 of it reasoning): input 180,
  // cachedRead 120, output 45, thought 15.
  usage: 'prompt',
  signIn: null,
  settingsFiles: {},
  // `--gemini-dir` moves both its settings (`antigravity-cli/`) and its hooks, tool servers and plugins
  // (`config/`); its Google sign-in is kept in the system keychain (validation/acp-agents.md).
  settingsHome: {
    folder: '.gemini/halora',
    args: (folder) => ['--gemini-dir', folder],
    environment: () => ({}),
  },
  // Seen 2026-11-17: each `run_command` starts in the session's folder; a `cd` in one reaches no later one.
  shell: {
    tool: 'run_command',
    command: 'CommandLine',
    background: 'IsDaemon',
    quiet: ['WaitMsBeforeAsync', 'toolSummary'],
    runsIn: 'task_folder',
    // Its own prompt's example of `run_command` names the folder as `Cwd` (agy 1.5.10's binary).
    cwd: 'Cwd',
  },
};

/** The AWS Kiro CLI, `kiro-cli acp` on its v3 engine (handshake verified 2026-11-17, signed out). */
export const KIRO: AcpProfile = {
  runtimeId: 'kiro',
  kind: 'kiro',
  displayName: 'Kiro 3.4.0',
  version: '3.4.0',
  // Signed in, its v3 engine's `initialize` names no `agentInfo` (seen 2026-11-17).
  versionLine: { args: ['--version'], pattern: /^kiro-cli (\d+\.\d+\.\d+)$/ },
  args: ['acp', '--agent-engine', 'v3'],
  environment: {},
  ownVariables: ['KIRO_API_KEY', 'KIRO_HOME', 'KIRO_DEFAULT_SETTINGS'],
  // Seen on signed-in runs (validation/acp-agents.md): its requests carry no input, which its earlier reports of
  // the same call give; a command is shown as its whole input, a file change from its input (`fileInputs`).
  replacesEveryOccurrence: [],
  // Seen on a signed-in run (validation/acp-agents.md): `str_replace` replaced exactly its `oldStr` with `newStr`,
  // `replace_all` false; `fs_write` asked with `{path, text}` for a new file.
  fileInputs: {
    replace: { path: 'path', before: 'oldStr', after: 'newStr', every: 'replace_all' },
    create: { path: 'path', text: 'text' },
  },
  projectSettings: ['.kiro', '.amazonq'],
  // Kiro's modes are its agents: `vibe` ("Default") asked before every write and most commands on a signed-in run.
  askingMode: 'vibe',
  // Kiro reports `autopilot` on, "Agent executes tools without confirmation"; its asks were seen with it on, but
  // Halora never relies on that: every session runs Supervised (validation/acp-agents.md).
  askingOptions: { autopilot: 'off' },
  // In Halora's fresh settings home Kiro reports `contentCollection` `enabled`, "Allow content to be used for service
  // improvement": its default, not the person's choice (their own settings name none). Held off (seen taken, 19:39Z).
  privateOptions: { contentCollection: 'disabled' },
  // Its listing command named 23 models with their context; `session/set_config_option` `model` took one and read it
  // back (seen 19:37Z and 19:39Z, no prompt).
  models: {
    from: 'command',
    option: 'model',
    service: 'Kiro',
    args: ['chat', '--list-models', '--format', 'json'],
    parse: modelsFromKiroList,
  },
  // Supervised, Kiro stages its file changes and asks once more, "Review changes", listing the calls (seen 18:46Z).
  turnReview: { meta: 'kiro', type: 'turn_approval' },
  // Kiro reports every answered request so; Supervised, a write's call shows nothing more until its review.
  answerReport: { meta: 'kiro', kind: 'interaction_resolved' },
  deniedStatuses: ['failed'],
  // Kiro reports credits only (`_meta.kiro` `turn_completion`), which the usage contract does not carry.
  usage: null,
  signIn: null,
  settingsFiles: {},
  // `KIRO_HOME` moves its settings, agents, hooks and sessions; its sign-in stays in Application Support.
  settingsHome: {
    folder: '.kiro/halora',
    args: () => [],
    environment: (folder) => ({ KIRO_HOME: folder }),
  },
  // Seen on signed-in runs: a command's call names no tool, kind `execute`, its input `{command, description,
  // run_in_background}`. Where its commands run was not seen, so it is said unknown.
  shell: {
    tool: 'execute',
    command: 'command',
    background: 'run_in_background',
    quiet: ['description'],
    runsIn: 'unknown',
    cwd: null,
  },
};

/**
 * Cursor's agent CLI, `cursor-agent acp` (handshake verified 2026-11-17, signed out): ACP v1, no `agentInfo`, so its
 * version comes from its own `--version`. A fresh `CURSOR_CONFIG_DIR` starts with `approvalMode: "allowlist"` and only
 * `Shell(ls)` allowed, so every other call asks; the person's own `~/.cursor` settings never apply.
 */
export const CURSOR: AcpProfile = {
  runtimeId: 'cursor',
  kind: 'cursor',
  displayName: 'Cursor Agent 2026.11.13',
  version: '2026.11.13-eacade1',
  versionLine: { args: ['--version'], pattern: /^(\d{4}\.\d{2}\.\d{2}-[0-9a-f]+)$/ },
  args: ['acp'],
  // So a sign-in can never open a page on the person's Mac during a start (AUDIT3 CU2).
  environment: { NO_OPEN_BROWSER: '1' },
  ownVariables: [
    'CURSOR_API_KEY',
    'CURSOR_AUTH_TOKEN',
    'CURSOR_CONFIG_DIR',
    'AGENT_CLI_CREDENTIAL_STORE',
    'NO_OPEN_BROWSER',
  ],
  replacesEveryOccurrence: [],
  fileInputs: null,
  projectSettings: ['.cursor'],
  // Not yet seen signed in: its documented modes are agent, plan and ask; a session in no `agent` mode is not started.
  askingMode: 'agent',
  askingOptions: {},
  privateOptions: {},
  // Seen signed in (20:11Z, no prompt): `session/new` lists its models as the `model` option, 48 of them, `default[]`
  // ("Auto") current; its only other option is `mode`.
  models: { from: 'session', option: 'model', service: 'Cursor' },
  turnReview: null,
  answerReport: null,
  // Seen signed in (20:12Z and 20:14Z): a rejected command's call ends `completed`, and the command did not run.
  deniedStatuses: ['failed', 'completed'],
  // Its prompt answer carried no usage on the signed-in runs.
  usage: null,
  signIn: 'cursor_login',
  // As a fresh config folder writes it (seen 2026-11-17), with nothing allowed: every call asks. Cursor's own
  // sandbox is off, as there: Halora's agent-app profile holds it.
  settingsFiles: {
    'cli-config.json': `${JSON.stringify({ version: 1, permissions: { allow: [], deny: [] }, approvalMode: 'allowlist', sandbox: { mode: 'disabled' } })}\n`,
  },
  settingsHome: {
    folder: '.cursor/halora',
    args: () => [],
    environment: (folder) => ({ CURSOR_CONFIG_DIR: folder }),
  },
  // Seen signed in: a command's call names no tool, kind `execute`, its input `{command}`. Where it runs was not seen.
  shell: {
    tool: 'execute',
    command: 'command',
    background: null,
    quiet: [],
    runsIn: 'unknown',
    cwd: null,
  },
};

export const ACP_PROFILES: readonly AcpProfile[] = [ANTIGRAVITY, KIRO, CURSOR];
