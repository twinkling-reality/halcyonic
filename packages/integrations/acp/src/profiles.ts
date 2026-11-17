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
   * A shell tool whose input names the whole command in one field and nothing else that acts: its name,
   * that field, the field that says it runs in the background, and the inputs that change neither what
   * runs nor where. A request of it is shown as the command, said to run in the task's folder.
   */
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
  readonly shell: {
    readonly tool: string;
    readonly command: string;
    readonly background: string | null;
    readonly quiet: readonly string[];
  } | null;
}

/** Google's Antigravity CLI, `agy acp` (verified 2026-11-17, validation/acp-agents.md). */
export const ANTIGRAVITY: AcpProfile = {
  runtimeId: 'antigravity',
  kind: 'antigravity',
  displayName: 'Antigravity 1.5.10',
  version: '1.5.10',
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
  projectSettings: ['.agents', '.gemini'],
  // `--gemini-dir` moves both its settings (`antigravity-cli/`) and its hooks, tool servers and plugins
  // (`config/`); its Google sign-in is kept in the system keychain (validation/acp-agents.md).
  settingsHome: {
    folder: '.gemini/halora',
    args: (folder) => ['--gemini-dir', folder],
    environment: () => ({}),
  },
  shell: {
    tool: 'run_command',
    command: 'CommandLine',
    background: 'IsDaemon',
    quiet: ['WaitMsBeforeAsync', 'toolSummary'],
  },
};

/** The AWS Kiro CLI, `kiro-cli acp` on its v3 engine (handshake verified 2026-11-17, signed out). */
export const KIRO: AcpProfile = {
  runtimeId: 'kiro',
  kind: 'kiro',
  displayName: 'Kiro 3.4.0',
  version: '3.4.0',
  args: ['acp', '--agent-engine', 'v3'],
  environment: {},
  ownVariables: ['KIRO_API_KEY', 'KIRO_HOME', 'KIRO_DEFAULT_SETTINGS'],
  // Not yet seen on a signed-in run: no tool's semantics are claimed, so an edit shown as one
  // replacement cannot be approved, and a command is shown as its whole input.
  replacesEveryOccurrence: [],
  projectSettings: ['.kiro', '.amazonq'],
  // `KIRO_HOME` moves its settings, agents, hooks and sessions; its sign-in stays in Application Support.
  settingsHome: {
    folder: '.kiro/halora',
    args: () => [],
    environment: (folder) => ({ KIRO_HOME: folder }),
  },
  shell: null,
};

export const ACP_PROFILES: readonly AcpProfile[] = [ANTIGRAVITY, KIRO];
