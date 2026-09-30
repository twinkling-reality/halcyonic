/**
 * The part of the Codex app-server protocol this adapter uses, written by hand from the TypeScript
 * bindings that `codex app-server generate-ts` produced from the pinned binary (`codex-cli`
 * 0.157.0, stable surface, without `--experimental`), trimmed to the fields the adapter reads or
 * sends. The file names in the comments are those bindings. Messages from the server are
 * untrusted input: the adapter checks every field it reads at run time, so these types document
 * the shapes rather than promise them.
 */

/** `RequestId.ts`. */
export type RequestId = string | number;

/** Every client request and notification the adapter sends. All are on the stable surface. */
export const METHODS_USED = [
  'initialize',
  'initialized',
  'thread/start',
  'thread/resume',
  'thread/turns/list',
  'turn/start',
  'turn/steer',
  'turn/interrupt',
  'config/read',
  'model/list',
] as const;

/** `ClientInfo.ts`. */
export interface ClientInfo {
  readonly name: string;
  readonly title: string | null;
  readonly version: string;
}

/**
 * `InitializeParams.ts`. `capabilities` stays null: no experimental API opt-in, no attestation,
 * no suppressed notifications.
 */
export interface InitializeParams {
  readonly clientInfo: ClientInfo;
  readonly capabilities: null;
}

/** `InitializeResponse.ts`. */
export interface InitializeResponse {
  readonly userAgent: string;
  readonly codexHome: string;
}

/** `v2/AskForApproval.ts`, restricted to the policies the adapter accepts. */
export type ApprovalPolicy = 'on-request' | 'untrusted';

/** `v2/SandboxMode.ts`. */
export type SandboxMode = 'read-only' | 'workspace-write' | 'danger-full-access';

/**
 * The configuration overrides the adapter sets in `config` of `v2/ThreadStartParams.ts`, keyed
 * as in Codex's `config.toml`; each applies to the one thread.
 */
export interface ThreadConfigOverrides {
  readonly model_context_window?: number;
  readonly model_auto_compact_token_limit?: number;
}

/** `v2/ThreadStartParams.ts`. */
export interface ThreadStartParams {
  readonly cwd: string;
  readonly approvalPolicy: ApprovalPolicy;
  /** `v2/ApprovalsReviewer.ts`: `user` routes approvals to the client, never to a reviewer agent. */
  readonly approvalsReviewer: 'user';
  readonly sandbox: SandboxMode;
  readonly model?: string;
  readonly modelProvider?: string;
  readonly config?: ThreadConfigOverrides;
  readonly threadSource: string;
}

/** `v2/ThreadResumeParams.ts`. */
export interface ThreadResumeParams extends Omit<ThreadStartParams, 'threadSource'> {
  readonly threadId: string;
  readonly excludeTurns: true;
}

/**
 * The fields of `v2/ThreadStartResponse.ts` and `v2/ThreadResumeResponse.ts` the adapter checks:
 * the settings the thread actually got. `sandbox` is `v2/SandboxPolicy.ts`, tagged by `type`.
 */
export interface ThreadSettingsResponse {
  readonly thread: { readonly id: string };
  readonly model: string;
  readonly modelProvider: string;
  readonly approvalPolicy: unknown;
  readonly approvalsReviewer: unknown;
  readonly sandbox: { readonly type: string };
}

/** `v2/ThreadTurnsListParams.ts`. */
export interface ThreadTurnsListParams {
  readonly threadId: string;
  readonly limit: number;
  readonly sortDirection: 'desc';
}

/** `v2/UserInput.ts`, text only. */
export interface TextInput {
  readonly type: 'text';
  readonly text: string;
  readonly text_elements: readonly never[];
}

/** `v2/TurnStartParams.ts`. */
export interface TurnStartParams {
  readonly threadId: string;
  readonly input: readonly TextInput[];
}

/** `v2/TurnSteerParams.ts`. The request fails unless `expectedTurnId` is the active turn. */
export interface TurnSteerParams extends TurnStartParams {
  readonly expectedTurnId: string;
}

/** `v2/TurnInterruptParams.ts`. */
export interface TurnInterruptParams {
  readonly threadId: string;
  readonly turnId: string;
}

/** `v2/ConfigReadParams.ts`: the effective configuration, without its layers. */
export interface ConfigReadParams {
  readonly includeLayers: false;
}

/**
 * The fields of `v2/Config.ts` the adapter reads, from `config/read`'s `config`: the configured
 * provider and model, and the settings that say which catalog `model/list` returns and where a
 * provider is served. `model_providers` holds only providers the configuration defines.
 */
export interface ConfigFields {
  readonly model: unknown;
  readonly model_provider: unknown;
  readonly model_context_window: unknown;
  readonly model_catalog_json: unknown;
  readonly openai_base_url: unknown;
  readonly model_providers: unknown;
}

/** `v2/ModelListParams.ts`. */
export interface ModelListParams {
  readonly cursor: string | null;
  readonly limit: number;
  readonly includeHidden: false;
}

/** The fields of `v2/Model.ts` the adapter reads, from `model/list`'s `data`. */
export interface CatalogModel {
  readonly model: string;
  readonly displayName: string;
  readonly hidden: boolean;
}

/** `v2/TurnStatus.ts`. */
export type TurnStatus = 'completed' | 'interrupted' | 'failed' | 'inProgress';

/**
 * `v2/TurnError.ts`. `codexErrorInfo` is `v2/CodexErrorInfo.ts`: a camelCase string, or an object
 * with one camelCase key.
 */
export interface TurnError {
  readonly message: string;
  readonly codexErrorInfo: string | Readonly<Record<string, unknown>> | null;
}

/** `v2/Turn.ts`. */
export interface Turn {
  readonly id: string;
  readonly status: TurnStatus;
  readonly error: TurnError | null;
}

/** `v2/CommandExecutionStatus.ts` and `v2/PatchApplyStatus.ts` share these values. */
export type ItemStatus = 'inProgress' | 'completed' | 'failed' | 'declined';

/** `v2/PatchChangeKind.ts`. */
export type PatchChangeKind =
  | { readonly type: 'add' }
  | { readonly type: 'delete' }
  | { readonly type: 'update'; readonly move_path: string | null };

/** The `v2/ThreadItem.ts` variants the adapter maps; every other item type is ignored. */
export type ThreadItem =
  | {
      readonly type: 'commandExecution';
      readonly id: string;
      readonly command: string;
      readonly cwd: string;
      readonly status: ItemStatus;
      readonly exitCode: number | null;
    }
  | {
      readonly type: 'fileChange';
      readonly id: string;
      readonly changes: readonly {
        readonly path: string;
        readonly kind: PatchChangeKind;
      }[];
      readonly status: ItemStatus;
    }
  | { readonly type: 'agentMessage'; readonly id: string; readonly text: string };

/** `v2/CommandExecutionRequestApprovalParams.ts`. */
export interface CommandApprovalParams {
  /** `v2/CommandExecutionApprovalKind.ts`: a command, or input for a command already running. */
  readonly kind: 'command' | 'writeStdin';
  readonly threadId: string;
  readonly turnId: string;
  readonly itemId: string;
  readonly startedAtMs: number;
  readonly command?: string | null;
  readonly cwd?: string | null;
  /** `v2/NetworkApprovalContext.ts`. */
  readonly networkApprovalContext?: { readonly host: string; readonly protocol: string } | null;
}

/** `v2/FileChangeRequestApprovalParams.ts`. */
export interface FileChangeApprovalParams {
  readonly threadId: string;
  readonly turnId: string;
  readonly itemId: string;
  readonly startedAtMs: number;
  /** Marked unstable in the schema: write access asked for under this root for the session. */
  readonly grantRoot?: string | null;
}

/**
 * `v2/CommandExecutionApprovalDecision.ts` and `v2/FileChangeApprovalDecision.ts`. `accept` and
 * `decline` are the only decisions the adapter sends: `acceptForSession` would outlive the one
 * request, and `cancel` interrupts the turn.
 */
export type ApprovalDecision = 'accept' | 'decline';

/** The server requests the adapter shows as approvals. Every other request is refused. */
export const APPROVAL_METHODS = [
  'item/commandExecution/requestApproval',
  'item/fileChange/requestApproval',
] as const;
