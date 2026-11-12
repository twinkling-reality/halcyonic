import { randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import {
  type CanUseTool,
  getSessionInfo,
  type ModelInfo,
  type Options,
  type PermissionMode,
  type PermissionResult,
  type Query,
  query,
  type SDKAssistantMessage,
  type SDKMessage,
  type SDKResultMessage,
  type SDKUserMessage,
} from '@anthropic-ai/claude-agent-sdk';
import type {
  ApprovalDecision,
  ApprovalLevel,
  ApprovalSubject,
  ErrorInfo,
  EventOf,
  ExecutionId,
  Provenance,
  QuestionAnswer,
  QuestionPrompt,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeEventType,
  RuntimeId,
  RuntimeModel,
  RuntimeOptions,
} from '@halcyonic/contracts';
import {
  type Clock,
  CREDENTIAL_REFUSED,
  confirmProjectLocation,
  type DirectoryPolicy,
  type ExecutionContext,
  type InstructionPicture,
  type InstructionRequest,
  type ObservationSink,
  type OptionsValidation,
  type ReattachRequest,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type StartExecutionRequest,
  type StartExecutionResult,
  systemClock,
} from '@halcyonic/runtime-core';
import { type ApiGate, type KeyGateDeclaration, startKeyGate } from '@halcyonic/seatbelt';
import {
  type ClaudeConfinementOptions,
  type ConfinedFolder,
  confinedFolder,
  confinedLaunch,
  findInConfinedStore,
  prepareConfinedFolder,
} from './confinement.ts';
import { buildEnvironment, cloudProvider, type ModelHost, modelHost } from './environment.ts';
import { ProcessGuard, type StaleProcess } from './process-guard.ts';

/**
 * What the adapter implements on the verified Agent SDK surface. How the CLI treats a message
 * queued while a turn runs is not verified, so instructions are accepted only between turns.
 */
export const CLAUDE_AGENT_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  // AskUserQuestion is answered through canUseTool's updatedInput (ADR 0022).
  answer_question: true,
  interrupt: true,
};

/** The part of a running SDK query the adapter uses. The SDK's `Query` satisfies it. */
export type QueryHandle = AsyncIterable<SDKMessage> &
  Pick<Query, 'interrupt' | 'close' | 'supportedModels' | 'setPermissionMode'>;

/**
 * The shape of the SDK's `query()` as the adapter calls it: streaming input, explicit options.
 * It is the one seam between the adapter and the SDK, so tests can script a run.
 */
export type QueryFunction = (params: {
  prompt: AsyncIterable<SDKUserMessage>;
  options: Options;
}) => QueryHandle;

export interface ClaudeAgentRuntimeOptions {
  /**
   * Which directories sessions may work in, decided by the host, asked again about the project's
   * folder before a session starts there.
   */
  readonly directoryPolicy: DirectoryPolicy;
  /**
   * File listing the Claude Code processes this adapter launched while they run, so that a later
   * start can stop any that outlived a crash. Use one file per runtime instance.
   */
  readonly processRecordFile: string;
  readonly runtimeId?: RuntimeId;
  /** Where allowlisted variables are read from. Defaults to the control plane's environment. */
  readonly inheritedEnvironment?: Readonly<Record<string, string | undefined>>;
  /** Variables added to every launched agent's environment, for example a launcher label. */
  readonly environment?: Readonly<Record<string, string>>;
  /** A Claude Code executable to run instead of the one bundled with the SDK. */
  readonly pathToClaudeCodeExecutable?: string;
  readonly clock?: Clock;
  readonly query?: QueryFunction;
  /**
   * Reads a session from Claude Code's own store, as the SDK's `getSessionInfo` does, which a
   * re-attach asks before it takes a session for found (ADR 0046). The SDK's by default.
   */
  readonly sessionInfo?: SessionInfoFunction;
  /**
   * Halcyonic's closed sandbox for the starts that ask for it (ADR 0048, BOX-4), on macOS. Null or
   * absent: every start runs as before.
   */
  readonly confinement?: ClaudeConfinementOptions | null;
  /** How long carrying a plan out waits for Claude Code to report its new mode; tests shorten it. */
  readonly planSwitchWaitMs?: number;
}

/**
 * Where a start runs: inside its folder's own place in the sandbox, or as before, with why not
 * where a start asked for the sandbox and did not get it (kept for the headset to say).
 */
type StartScope =
  | { readonly folder: ConfinedFolder }
  | { readonly folder: null; readonly reason: string | null };

/** The SDK's `getSessionInfo`, narrowed to what a re-attach reads. */
export type SessionInfoFunction = (
  sessionId: string,
  options: { readonly dir: string },
) => Promise<{ readonly cwd?: string } | undefined>;

/** A session id as Halcyonic chooses one: a UUID, so it can never read as a flag. */
const SESSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const START_OPTIONS = ['model', 'permission_mode'];

/**
 * Claude Code does not offer working without asking (ADR 0043): Halcyonic gives it no sandbox of its
 * own, so nothing would bound a command it ran unasked.
 */
/** How long leaving a plan waits for Claude Code to report its new mode (PLAN 4a). */
const LEAVE_PLAN_WAIT_MS = 10_000;

/**
 * How Claude Code takes a key, for its gate (VRIFY, 2026-11-12; docs/internal/validation/claude-code-key-gate.md):
 * its API's address from `ANTHROPIC_BASE_URL` and its key from `ANTHROPIC_API_KEY`, sent as `x-api-key`, which is
 * how the Anthropic API takes a key; `ANTHROPIC_AUTH_TOKEN` is the other credential it would take.
 */
export const CLAUDE_CODE_KEY_GATE: KeyGateDeclaration = {
  baseVariable: 'ANTHROPIC_BASE_URL',
  keyVariable: 'ANTHROPIC_API_KEY',
  otherCredentials: ['ANTHROPIC_AUTH_TOKEN'],
  header: 'x-api-key',
  bearer: false,
  upstream: 'https://api.anthropic.com',
};

const NEVER_ASK_REFUSED =
  "Claude Code does not work without asking yet: Halcyonic's sandbox for it has not been tried on a real run.";

/**
 * Permission modes a start may choose. `bypassPermissions` and `auto` are refused because they
 * take decisions away from the person supervising the execution.
 */
const PERMISSION_MODES: readonly PermissionMode[] = ['default', 'acceptEdits', 'plan', 'dontAsk'];

/** Model names and ids as Claude Code accepts them. A leading `-` could read as a CLI flag. */
const MODEL_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:/@[\]-]{0,255}$/;

interface StartOptions {
  /** The project's folder, where the session works, as the host's directory policy confirmed it. */
  readonly cwd: string;
  readonly model: string | undefined;
  readonly permissionMode: PermissionMode | undefined;
  /**
   * The project's secrets for this session's process (ADR 0057), under the adapter's own variables,
   * which a secret never replaces.
   */
  readonly secrets?: Readonly<Record<string, string>>;
}

/**
 * Runs Claude Code sessions through the Claude Agent SDK in streaming input mode. Halcyonic
 * chooses each session id at launch, so an execution can be correlated with what Salidium and
 * Seorak record before the runtime reports anything. Only executions it starts are controlled.
 *
 * Every Claude Code process is launched through a `ProcessGuard`: recorded in the process record
 * file and watched by a watchdog process before it receives any input, so none outlives the
 * process hosting the adapter, however that process ends. This relies on `ps` and POSIX signals:
 * macOS and Linux only.
 */
export class ClaudeAgentRuntimeAdapter implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  readonly #environment: Readonly<Record<string, string>>;
  readonly #directoryPolicy: DirectoryPolicy;
  readonly #executable: string | undefined;
  readonly #clock: Clock;
  readonly #query: QueryFunction;
  readonly #sessionInfo: SessionInfoFunction;
  readonly #guard: ProcessGuard;
  /** Who serves Claude Code's models in this environment, and where. */
  readonly #modelHost: ModelHost;
  readonly #sessions = new Map<ExecutionId, ClaudeSession>();
  readonly #confinement: ClaudeConfinementOptions | null;
  readonly #planSwitchWaitMs: number;
  /** The one way out of the sandbox, started for the first confined start (BOX-4). */
  #gate: Promise<ApiGate> | null = null;
  #closed = false;

  constructor(options: ClaudeAgentRuntimeOptions) {
    this.#environment = buildEnvironment(
      options.inheritedEnvironment ?? process.env,
      options.environment ?? {},
    );
    this.#guard = new ProcessGuard(options.processRecordFile);
    this.#modelHost = modelHost(this.#environment);
    this.#directoryPolicy = options.directoryPolicy;
    this.#executable = options.pathToClaudeCodeExecutable;
    this.#clock = options.clock ?? systemClock;
    this.#query = options.query ?? query;
    this.#sessionInfo = options.sessionInfo ?? getSessionInfo;
    this.#confinement = options.confinement ?? null;
    this.#planSwitchWaitMs = options.planSwitchWaitMs ?? LEAVE_PLAN_WAIT_MS;
    this.descriptor = {
      runtime_id: options.runtimeId ?? ('claude-agent' as RuntimeId),
      kind: 'claude-agent',
      display_name: 'Claude Agent',
      synthetic: false,
      capabilities: CLAUDE_AGENT_CAPABILITIES,
      model_choice: 'listed',
      uses_project_location: true,
      // Agents run in the background by default (SDK 0.3.283), so between a subagent's reported calls work
      // can go on unseen: no open call does not mean none runs (OPEN_QUESTIONS.md).
      reports_tool_activity: false,
      reports_tool_results: false,
      // A session's transcript outlives its process, and `resume` continues it (restart-reattach.md).
      resumes_after_restart: true,
      // Its own plan mode, which `system/init` reports back; every write still asks, as on every Claude
      // Code start (agent-plan-modes.md, the bundled 2.1.283's run on a fake host).
      offers_plan: true,
      // Each session's own process takes the project's secrets as variables, which its tool
      // commands inherit (ADR 0057, project-secrets.md).
      secret_environment: true,
      // A base64 image block reaches the Messages API byte for byte (picture-and-file-input.md).
      takes_pictures: true,
    };
  }

  /** Pid of the watchdog process while one runs, for diagnostics. */
  get watchdogPid(): number | null {
    return this.#guard.watchdogPid;
  }

  validateStartOptions(
    options: RuntimeOptions,
    modelRef: string | null,
    approvalLevel?: ApprovalLevel,
  ): OptionsValidation {
    if (approvalLevel === 'never_ask') return { ok: false, message: NEVER_ASK_REFUSED };
    const parsed = parseStartOptions(options, modelRef);
    return parsed.ok ? { ok: true } : parsed;
  }

  /**
   * Held where a start runs inside Halcyonic's closed profile (ADR 0048, BOX-4), as `#scopeFor`
   * decides it: a folder its confinement covers, on macOS, with no cloud provider switched on. There
   * it writes that folder and its own place alone, and holds no key: its gate does.
   */
  holdsFolder(directory: string): boolean {
    return this.#scopeFor(directory).folder !== null;
  }

  /** Outside the closed profile it has no sandbox at all: on a folder the person connected, always. */
  runsOutsideSandbox(): boolean {
    return true;
  }

  /** Claude Code's and its provider's own variables, and Bun's, which the bundled build runs on. */
  ownsEnvironmentName(name: string): boolean {
    return ['CLAUDE_', 'ANTHROPIC_', 'BUN_'].some((prefix) => name.startsWith(prefix));
  }

  /**
   * The models Claude Code offers, from the Agent SDK's `supportedModels()`, which answers once a
   * Claude Code process has started. The process gets no prompt and is closed at once; it runs
   * guarded like any other. Each model is named by the concrete model it resolves to where the SDK
   * says, which is also how Claude Code reports the model a session uses.
   */
  async listModels(): Promise<readonly RuntimeModel[]> {
    if (this.#closed) throw closedError();
    const input = new InputQueue();
    let handle: QueryHandle;
    try {
      const label = randomUUID();
      handle = this.#query({
        prompt: input,
        options: {
          cwd: tmpdir(),
          env: { ...this.#environment },
          // The guard knows a launched process by the session id its command line carries, so the
          // listing's process carries its label as one.
          sessionId: label,
          spawnClaudeCodeProcess: (options) => this.#guard.spawn(options, label),
          systemPrompt: { type: 'preset', preset: 'claude_code' },
          // As for a start: the temporary folder's own `.claude` settings, which anything may
          // write, never load.
          settingSources: ['user'],
          ...(this.#executable !== undefined && { pathToClaudeCodeExecutable: this.#executable }),
        },
      });
    } catch (error) {
      throw new RuntimeActionError(
        'runtime_start_failed',
        wholeError(errorText(error)) ?? 'The SDK refused to start Claude Code.',
      );
    }
    try {
      return toRuntimeModels(await handle.supportedModels(), this.#modelHost);
    } catch (error) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        wholeError(`Claude Code could not list its models: ${errorText(error)}`) ??
          'Claude Code could not list its models.',
      );
    } finally {
      input.end();
      handle.close();
    }
  }

  /**
   * Stops Claude Code processes that an earlier run launched and that are still running, as listed
   * in the process record file, each only while the process with its pid is still that process.
   * Call it at startup; the first launch also waits for it. Once it has succeeded, it returns
   * nothing.
   */
  stopStaleProcesses(): Promise<readonly StaleProcess[]> {
    return this.#guard.stopStale();
  }

  async startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    if (this.#closed) throw closedError();
    // Refused, never ignored: a choice the runtime does not apply must not be accepted (ADR 0043).
    if (request.approval_level === 'never_ask') {
      throw new RuntimeActionError('invalid_runtime_options', NEVER_ASK_REFUSED);
    }
    const planned = withPlan(parseStartOptions(request.options, request.model_ref), request.plan);
    if (!planned.ok) throw new RuntimeActionError('invalid_runtime_options', planned.message);
    const parsed = planned;
    // Asked again before anything is launched: the folder may have changed since admission.
    const cwd = confirmProjectLocation(this.#directoryPolicy, request.directory);
    // A chosen model is checked against a fresh list before a session is launched for it.
    if (request.model_ref !== null) {
      const listed = await this.listModels();
      if (!listed.some((model) => model.model_ref === request.model_ref)) {
        throw new RuntimeActionError(
          'model_unavailable',
          `Claude Code does not list the model ${request.model_ref}.`,
        );
      }
    }
    const executionId = request.execution.execution_id;
    if (this.#sessions.has(executionId)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    const scope = this.#scopeFor(cwd);
    const gate = scope.folder === null ? null : await this.#apiGate();
    if (scope.folder !== null) prepareConfinedFolder(scope.folder);
    // Asked again right before the folder is handed over: the model listing above waits.
    confirmProjectLocation(this.#directoryPolicy, cwd);
    if (this.#sessions.has(executionId)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    const session = new ClaudeSession(
      randomUUID(),
      request.emit,
      this.#clock,
      false,
      this.#recordFor(scope),
      this.#planSwitchWaitMs,
    );
    this.#sessions.set(executionId, session);
    let started: Promise<string>;
    try {
      started = session.start(
        this.#query,
        this.#options(
          { ...parsed.value, cwd, secrets: request.environment ?? {} },
          session,
          false,
          scope.folder === null || gate === null ? null : { folder: scope.folder, gate },
        ),
        request.instruction,
        request.pictures ?? [],
      );
    } catch (error) {
      // query() refused before starting a process, so nothing ran.
      this.#sessions.delete(executionId);
      throw new RuntimeActionError(
        'runtime_start_failed',
        wholeError(errorText(error)) ?? 'The SDK refused to start Claude Code.',
      );
    }
    try {
      return { native_id: await started };
    } catch (error) {
      // A start Claude Code broke before it was confirmed (a plan reported in another mode) is not kept.
      if (this.#sessions.get(executionId) === session) this.#sessions.delete(executionId);
      throw error;
    }
  }

  /**
   * Re-attaches a session an earlier control plane started (ADR 0046), with the bounds it started
   * with: the same options, model and folder, asked of the policy again, and the level `ask`, the
   * only one Claude Code offers, never confined. The session must be in Claude Code's own store for
   * that folder. No process is launched until the next instruction, which resumes the session by
   * its id; the turn the restart cut is reported ended.
   */
  async reattach(request: ReattachRequest): Promise<void> {
    if (this.#closed) throw closedError();
    const refuse = (message: string) => new RuntimeActionError('reattach_refused', message);
    if (request.approval_level !== undefined && request.approval_level !== 'ask') {
      throw refuse(
        `The session started at the level ${request.approval_level}; ${NEVER_ASK_REFUSED}`,
      );
    }
    // Resumed in plan mode where its start recorded one (PLAN).
    const parsed = withPlan(parseStartOptions(request.options, request.model_ref), request.plan);
    if (!parsed.ok) throw refuse(parsed.message);
    if (!SESSION_ID.test(request.native_id)) {
      throw refuse(`${request.native_id} is not a session id Halcyonic chose.`);
    }
    const cwd = confirmProjectLocation(this.#directoryPolicy, request.directory);
    const executionId = request.execution.execution_id;
    if (this.#sessions.has(executionId)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution is already attached.');
    }
    // Re-attached only as it ran, as Codex's and OpenCode's are: a confined session goes on only in
    // its folder's own place, and one that ran as before is never moved into the sandbox.
    const scope = this.#scopeFor(cwd);
    const record = this.#recordFor(scope);
    if (record.confined !== (request.confined === true)) {
      throw refuse(
        record.confined
          ? "The session ran outside Halcyonic's sandbox, and its folder is now confined: it is not moved."
          : `The session was recorded confined, and here it would not be${record.unconfined_reason === undefined ? '' : ` (${record.unconfined_reason})`}: it is not re-attached.`,
      );
    }
    let info: { readonly cwd?: string } | undefined;
    try {
      // A confined session's transcript is in its folder's own store, which the SDK's reader,
      // reading this process's CLAUDE_CONFIG_DIR, does not see.
      info =
        scope.folder === null
          ? await this.#sessionInfo(request.native_id, { dir: cwd })
          : findInConfinedStore(scope.folder.config, request.native_id);
    } catch (error) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        wholeError(`Claude Code's sessions could not be read: ${errorText(error)}`) ??
          "Claude Code's sessions could not be read.",
      );
    }
    if (info === undefined) {
      throw new RuntimeActionError(
        'session_not_found',
        `Claude Code has no session ${request.native_id} for ${cwd}.`,
      );
    }
    if (info.cwd !== undefined && info.cwd !== cwd) {
      throw refuse(`The session works in ${info.cwd}, not in the task's folder ${cwd}.`);
    }
    if (request.model_ref !== null) {
      const listed = await this.listModels();
      if (!listed.some((model) => model.model_ref === request.model_ref)) {
        throw refuse(`Claude Code does not list the model ${request.model_ref} any more.`);
      }
    }
    // Asked again right before the session is taken: the reads above wait.
    confirmProjectLocation(this.#directoryPolicy, cwd);
    if (this.#closed) throw closedError();
    if (this.#sessions.has(executionId)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution is already attached.');
    }
    const gate = scope.folder === null ? null : await this.#apiGate();
    if (this.#closed) throw closedError();
    const session = new ClaudeSession(
      request.native_id,
      request.emit,
      this.#clock,
      true,
      { confined: false },
      this.#planSwitchWaitMs,
    );
    session.prepare(
      this.#query,
      this.#options(
        { ...parsed.value, cwd, secrets: request.environment ?? {} },
        session,
        true,
        scope.folder === null || gate === null ? null : { folder: scope.folder, gate },
      ),
    );
    this.#sessions.set(executionId, session);
    session.restored(request.cut_turn);
  }

  async sendInstruction(request: InstructionRequest): Promise<void> {
    const session = this.#session(request.execution);
    if (request.leave_plan === true) await session.leavePlan();
    await session.instruct(request.text, request.pictures ?? []);
  }

  async respondToApproval(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
  }): Promise<void> {
    this.#session(request.execution).respond(
      request.approval_id,
      request.decision,
      request.message,
    );
  }

  async answerQuestion(request: {
    execution: ExecutionContext;
    question_id: string;
    answers: readonly QuestionAnswer[];
  }): Promise<void> {
    this.#session(request.execution).answer(request.question_id, request.answers);
  }

  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    await this.#session(request.execution).interrupt();
  }

  /**
   * Ends every query this adapter started. The SDK then stops each Claude Code process; this
   * resolves once they have exited, the process record file is removed and the watchdog stopped.
   */
  async close(): Promise<void> {
    this.#closed = true;
    const sessions = [...this.#sessions.values()];
    this.#sessions.clear();
    await Promise.all(sessions.map((session) => session.close()));
    await this.#guard.close();
    const gate = this.#gate;
    this.#gate = null;
    if (gate !== null) await gate.then((open) => open.close()).catch(() => undefined);
  }

  /**
   * Where a start in `cwd` runs (ADR 0048, BOX-4): confined only where the host's confinement asks
   * for the folder, on macOS, and not on a cloud provider, whose hosts are not known ahead; else as
   * before, with the person's own settings and hooks, and why where the folder asked for it.
   */
  #scopeFor(cwd: string): StartScope {
    const settings = this.#confinement;
    if (settings === null) return { folder: null, reason: null };
    const reads = settings.forDirectory(cwd);
    if (reads === null) return { folder: null, reason: null };
    if (process.platform !== 'darwin') {
      return {
        folder: null,
        reason: "Halcyonic's sandbox is on macOS only, so this run is not confined.",
      };
    }
    const provider = cloudProvider(this.#environment);
    if (provider !== null) {
      return {
        folder: null,
        reason: `Claude Code runs on ${provider} here, whose hosts Halcyonic cannot know ahead, so this run is not confined.`,
      };
    }
    return { folder: confinedFolder(settings, cwd, reads) };
  }

  /**
   * What a start in `scope` records (S2, BOX-4). Inside the sandbox the agent holds no key: its gate
   * adds the person's key to each request on its way out, and the agent's environment carries only a
   * placeholder (VRIFY, 2026-11-12; AUDIT BOX-4 F1 closed), so such a run is recorded confined.
   */
  #recordFor(scope: StartScope): {
    readonly confined: boolean;
    readonly unconfined_reason?: string;
  } {
    if (scope.folder === null) {
      return { confined: false, ...(scope.reason !== null && { unconfined_reason: scope.reason }) };
    }
    return { confined: true };
  }

  /**
   * The API gate, started once: to `ANTHROPIC_BASE_URL` where set, else Anthropic's API, adding the
   * person's key as the header this adapter declares. Without a key there is no gate, and no
   * confined start.
   */
  #apiGate(): Promise<ApiGate> {
    if (this.#gate === null) {
      const gate = startKeyGate(CLAUDE_CODE_KEY_GATE, this.#environment);
      this.#gate = gate;
      gate.catch(() => {
        if (this.#gate === gate) this.#gate = null;
      });
    }
    return this.#gate.catch((error: unknown) => {
      throw new RuntimeActionError(
        'runtime_unavailable',
        `Could not open Claude Code's way to its API: ${errorText(error)}`,
      );
    });
  }

  #options(
    start: StartOptions,
    session: ClaudeSession,
    resume = false,
    confined: { readonly folder: ConfinedFolder; readonly gate: ApiGate } | null = null,
  ): Options {
    const settings = this.#confinement;
    return {
      cwd: start.cwd,
      env: { ...start.secrets, ...this.#environment },
      // A resumed session keeps its id: `resume` and `sessionId` together would fork it.
      ...(resume ? { resume: session.id } : { sessionId: session.id }),
      canUseTool: session.canUseTool,
      // Inside the sandbox where the start is confined (BOX-4): the person's settings and hooks stay
      // outside it, since its home and configuration are the folder's own.
      spawnClaudeCodeProcess: (options) => {
        if (confined === null || settings === null) return this.#guard.spawn(options, session.id);
        // This process's own credential for the gate: dead the moment the process ends (AUDIT3).
        const credential = confined.gate.issue();
        try {
          const spawned = this.#guard.spawn(
            confinedLaunch(
              options,
              confined.folder,
              settings,
              confined.gate,
              CLAUDE_CODE_KEY_GATE,
              credential,
            ),
            session.id,
          );
          spawned.once('exit', () => confined.gate.revoke(credential));
          return spawned;
        } catch (error) {
          confined.gate.revoke(credential);
          throw error;
        }
      },
      // Claude Code's own system prompt. Without this option the SDK sends an empty one.
      systemPrompt: { type: 'preset', preset: 'claude_code' },
      // The person's own settings only: their hooks run, so Salidium and Seorak observe the
      // session. A project's own CLAUDE.md, its AGENTS.md and the hooks, servers and permissions in
      // its `.claude` settings never load by themselves: they are the repository's words, and a
      // hook there would run a command for every task (E5). Where the start is confined, none
      // at all: the 'user' source there is the folder's own configuration, which the agent inside
      // can write, so its hooks or allow rules would load at the next start (AUDIT2 S1, BOX-4).
      settingSources: confined === null || settings === null ? ['user'] : [],
      ...(start.model !== undefined && { model: start.model }),
      ...(start.permissionMode !== undefined && { permissionMode: start.permissionMode }),
      ...(this.#executable !== undefined && { pathToClaudeCodeExecutable: this.#executable }),
    };
  }

  #session(execution: ExecutionContext): ClaudeSession {
    if (this.#closed) throw closedError();
    const session = this.#sessions.get(execution.execution_id);
    if (session === undefined) {
      throw new RuntimeActionError(
        'execution_unknown_to_runtime',
        'Claude Agent has no session for this execution.',
      );
    }
    return session;
  }
}

/**
 * Checks the start options of a Claude Agent execution. Where the session works is not an option:
 * it is the project's folder, which the host resolves and checks (`confirmProjectLocation`).
 */
/**
 * A plan start (PLAN) runs in Claude Code's own `plan` permission mode; a start that also names
 * another mode is refused rather than either choice dropped.
 */
function withPlan(
  parsed: ReturnType<typeof parseStartOptions>,
  plan: boolean | undefined,
): ReturnType<typeof parseStartOptions> {
  if (!parsed.ok || plan !== true) return parsed;
  if (parsed.value.permissionMode !== undefined && parsed.value.permissionMode !== 'plan') {
    return invalid(
      `A plan runs in Claude Code's plan mode, so it is not started with the permission mode ${parsed.value.permissionMode}.`,
    );
  }
  return { ok: true, value: { ...parsed.value, permissionMode: 'plan' } };
}

function parseStartOptions(
  options: RuntimeOptions,
  modelRef: string | null = null,
):
  | { readonly ok: true; readonly value: Omit<StartOptions, 'cwd'> }
  | { readonly ok: false; readonly message: string } {
  const unknown = Object.keys(options).filter((key) => !START_OPTIONS.includes(key));
  if (unknown.length > 0) {
    return invalid(
      `Unknown Claude Agent options: ${unknown.join(', ')}. Supported: ${START_OPTIONS.join(', ')}. The session works in the project's folder.`,
    );
  }
  const { model, permission_mode } = options;
  if (model !== undefined && (typeof model !== 'string' || !MODEL_PATTERN.test(model))) {
    return invalid('Option "model" must be a Claude model name or id.');
  }
  if (modelRef !== null) {
    if (model !== undefined) {
      return invalid(
        'Choose the model either with model_ref or with the "model" option, not both.',
      );
    }
    if (!MODEL_PATTERN.test(modelRef))
      return invalid(`${modelRef} is not a model Claude Code lists.`);
  }
  const permissionMode = PERMISSION_MODES.find((mode) => mode === permission_mode);
  if (permission_mode !== undefined && permissionMode === undefined) {
    return invalid(`Option "permission_mode" must be one of ${PERMISSION_MODES.join(', ')}.`);
  }
  return { ok: true, value: { model: modelRef ?? model, permissionMode } };
}

function invalid(message: string): { readonly ok: false; readonly message: string } {
  return { ok: false, message };
}

function closedError(): RuntimeActionError {
  return new RuntimeActionError('runtime_closed', 'The Claude Agent runtime is closed.');
}

/**
 * Claude Code's models as the contract describes them (ADR 0016). Anthropic or a cloud provider
 * serves every one, so all are remote. The SDK's list says nothing about tools. A row whose alias
 * resolves to a model already listed is not listed twice.
 */
export function toRuntimeModels(models: readonly ModelInfo[], host: ModelHost): RuntimeModel[] {
  const listed = new Map<string, RuntimeModel>();
  for (const model of models) {
    const modelRef = model.resolvedModel ?? model.value;
    if (typeof modelRef !== 'string' || !MODEL_PATTERN.test(modelRef) || listed.has(modelRef)) {
      continue;
    }
    const name = clip(model.displayName, 180) ?? modelRef;
    listed.set(modelRef, {
      model_ref: modelRef,
      display_name: `${name} (${host.name})`,
      served: host.served,
      tool_calling: 'unknown',
      context_tokens: null,
    });
  }
  return [...listed.values()];
}

interface Turn {
  /** Unique within the session; used to build native event ids. */
  readonly key: string;
  /** The uuid of the user message the adapter delivered, or null for a turn the runtime began. */
  readonly id: string | null;
  confirmed: boolean;
  interruptRequested: boolean;
  /** Set when an assistant message of the turn says the API refused the key or the cloud credential. */
  credentialRefused: boolean;
  /** Settles when the runtime shows it has taken up the turn, or rejects when it never will. */
  readonly confirmation: PromiseWithResolvers<void>;
}

function newTurn(key: string, id: string | null): Turn {
  const confirmation = Promise.withResolvers<void>();
  // Awaited by whoever delivered the turn; a turn nobody awaits must not become an unhandled rejection.
  confirmation.promise.catch(() => {});
  return {
    key,
    id,
    confirmed: false,
    interruptRequested: false,
    credentialRefused: false,
    confirmation,
  };
}

interface PendingApproval {
  readonly input: Record<string, unknown>;
  readonly answer: (result: PermissionResult) => void;
}

/** An AskUserQuestion call waiting in canUseTool for the person's answers (ADR 0022). */
interface PendingQuestion {
  readonly input: Record<string, unknown>;
  /** Each question's full text by its prompt key: Claude Code takes answers keyed by the text. */
  readonly texts: ReadonlyMap<string, string>;
  readonly answer: (result: PermissionResult) => void;
}

/** The tool through which Claude Code asks the person questions. */
const ASK_USER_QUESTION = 'AskUserQuestion';

const DENIED_MESSAGE = 'The person supervising this session in Halcyonic denied this request.';
const WITHDRAWN_MESSAGE =
  'The question was withdrawn: the person stopped the work in Halcyonic before answering it.';

/** One Claude Code session: one SDK query, fed one user message per turn. */
class ClaudeSession {
  /** The session id Halcyonic chose and passed as `--session-id`. */
  readonly id: string;
  readonly #emitObservation: ObservationSink;
  readonly #clock: Clock;
  readonly #input = new InputQueue();
  readonly #approvals = new Map<string, PendingApproval>();
  readonly #questions = new Map<string, PendingQuestion>();
  readonly #activeTools = new Set<string>();
  #query: QueryHandle | null = null;
  #consuming: Promise<void> = Promise.resolve();
  #turn: Turn | null = null;
  /** The session id the runtime reported, once it has confirmed the session. */
  #nativeId: string | null = null;
  #state: 'open' | 'lost' | 'closed' = 'open';
  #sequence = 0;
  /** The model Claude Code last reported for the session, and how many times it reported one. */
  #model: string | null = null;
  #modelReports = 0;
  /**
   * What every native event id starts with: the session id, and for a re-attached session also a
   * mark of this attachment, since the journal keeps every id the session's earlier runs used.
   */
  readonly #eventPrefix: string;
  /** False for a re-attached session, whose events carry no sequence: its earlier runs' are unknown here. */
  readonly #sequenced: boolean;
  /** For a re-attached session, the query its next instruction launches, resuming it. */
  #pending: { readonly query: QueryFunction; readonly options: Options } | null = null;

  /** Waits for the mode a `system/status` message reports, while a plan is being left (PLAN 4a). */
  #modeWaiter: ((mode: string) => void) | null = null;
  /** Whether a plan was asked for, at the start or the resume; then `system/init` must report it. */
  #planExpected = false;
  #planChecked = false;
  /** How long leaving a plan waits for the new mode to be reported. */
  readonly #planSwitchWaitMs: number;
  /** The permission mode the session's first `system/init` reported (PLAN). */
  #initMode: string | undefined = undefined;
  /** What the session's start records of its confinement (S2, BOX-4). */
  readonly #record: { readonly confined: boolean; readonly unconfined_reason?: string };

  constructor(
    id: string,
    emit: ObservationSink,
    clock: Clock,
    reattached = false,
    record: { readonly confined: boolean; readonly unconfined_reason?: string } = {
      confined: false,
    },
    planSwitchWaitMs = LEAVE_PLAN_WAIT_MS,
  ) {
    this.id = id;
    this.#record = record;
    this.#planSwitchWaitMs = planSwitchWaitMs;
    this.#emitObservation = emit;
    this.#clock = clock;
    this.#eventPrefix = reattached ? `${id}:${randomUUID()}` : id;
    this.#sequenced = !reattached;
    // A re-attached session was confirmed by its first run; its start is not reported again.
    if (reattached) this.#nativeId = id;
  }

  /** Sets the query a re-attached session's next instruction launches, resuming it. */
  prepare(queryFunction: QueryFunction, options: Options): void {
    this.#planExpected = options.permissionMode === 'plan';
    this.#pending = { query: queryFunction, options };
  }

  /**
   * Reports the session observable again, found in Claude Code's store, and the turn the restart
   * cut, if any, ended: the restart stopped its process, and Claude Code records no end for it.
   */
  restored(cut: { readonly turn_id: string | null } | null): void {
    this.#emit(
      'runtime.connection.restored',
      { reason: 'Claude Code still has the session, so it is re-attached after the restart.' },
      observed('session_info'),
      'restored',
    );
    if (cut !== null) {
      this.#emit(
        'runtime.turn.interrupted',
        { turn_id: cut.turn_id },
        { epistemic: 'inferred', native_type: null, rule: 'claude.restart.turn_status' },
        'cut',
      );
    }
  }

  /** Launches the query with the first instruction. Resolves with the confirmed session id. */
  start(
    queryFunction: QueryFunction,
    options: Options,
    instruction: string,
    pictures: readonly InstructionPicture[] = [],
  ): Promise<string> {
    this.#planExpected = options.permissionMode === 'plan';
    const turn = this.#deliver(instruction, pictures);
    const handle = queryFunction({ prompt: this.#input, options });
    this.#query = handle;
    this.#consuming = this.#consume(handle);
    return turn.confirmation.promise.then(() => this.#nativeId ?? this.id);
  }

  async instruct(text: string, pictures: readonly InstructionPicture[] = []): Promise<void> {
    this.#assertOpen();
    const pending = this.#pending;
    if (pending !== null && this.#query === null) {
      // The first instruction to a re-attached session launches Claude Code, resuming it.
      this.#pending = null;
      const turn = this.#deliver(text, pictures);
      let handle: QueryHandle;
      try {
        handle = pending.query({ prompt: this.#input, options: pending.options });
      } catch (error) {
        this.#turn = null;
        this.#pending = pending;
        throw new RuntimeActionError(
          'runtime_start_failed',
          wholeError(errorText(error)) ?? 'The SDK refused to start Claude Code.',
        );
      }
      this.#query = handle;
      this.#consuming = this.#consume(handle);
      await turn.confirmation.promise;
      return;
    }
    if (this.#turn !== null) {
      throw new RuntimeActionError(
        'turn_in_progress',
        'Claude Agent accepts instructions only between turns.',
      );
    }
    await this.#deliver(text, pictures).confirmation.promise;
  }

  /**
   * Carries a plan out (PLAN 4a): switches the running session from Claude Code's plan mode to its
   * default one and reports `runtime.plan.left` only once Claude Code reports the new mode in a
   * `system/status` message. Anything else refuses the instruction, with nothing sent. A
   * re-attached session that has not resumed has no process to confirm it, so it is refused too.
   */
  async leavePlan(): Promise<void> {
    this.#assertOpen();
    const handle = this.#query;
    if (handle === null) {
      throw new RuntimeActionError(
        'runtime_refused',
        'Claude Code has not resumed this session since the restart, so it cannot confirm leaving the plan. Send it an instruction first, then carry the plan out.',
      );
    }
    if (this.#turn !== null) {
      throw new RuntimeActionError(
        'turn_in_progress',
        'Claude Agent accepts instructions only between turns.',
      );
    }
    // Every reported mode counts, not only the first: one still saying `plan` may come before the
    // change lands (AUDIT3 4b S2). Only `default` is the plan left; any other mode is outside the
    // level the task started at; only `plan` reported to the end says it is still a plan.
    let latest: string | undefined;
    const verdict = new Promise<'left' | 'still_plan' | 'other' | 'unknown'>((resolve) => {
      this.#modeWaiter = (mode) => {
        latest = mode;
        if (mode === 'default') resolve('left');
        else if (mode !== 'plan') resolve('other');
      };
      setTimeout(
        () => resolve(latest === 'plan' ? 'still_plan' : 'unknown'),
        this.#planSwitchWaitMs,
      ).unref?.();
    });
    try {
      await handle.setPermissionMode('default');
    } catch {
      this.#modeWaiter = null;
      throw new RuntimeActionError(
        'runtime_refused',
        'Claude Code did not take the change out of plan mode, so the instruction was not sent and the task is still a plan.',
      );
    }
    const outcome = await verdict;
    this.#modeWaiter = null;
    if (outcome === 'still_plan') {
      throw new RuntimeActionError(
        'runtime_refused',
        'Claude Code reported the session still in plan mode, so the instruction was not sent and the task is still a plan.',
      );
    }
    if (outcome === 'other') {
      const reason = `Claude Code reported the ${latest} mode on leaving plan mode, outside the level the task started at, so the session was stopped and the instruction was not sent.`;
      this.#stopFor(reason);
      throw new RuntimeActionError('runtime_refused', reason, 'unknown');
    }
    if (outcome === 'unknown') {
      throw new RuntimeActionError(
        'runtime_refused',
        'Claude Code took the change out of plan mode but did not report its new mode, so the instruction was not sent, and the task may have left its plan.',
        'unknown',
      );
    }
    this.#emit('runtime.plan.left', {}, observed('system.status'), 'plan-left');
  }

  /** Answers a pending permission request. The runtime receives the decision as it is resolved. */
  respond(approvalId: string, decision: ApprovalDecision, message: string | null): void {
    this.#assertOpen();
    const approval = this.#approvals.get(approvalId);
    if (approval === undefined) {
      throw new RuntimeActionError(
        'approval_not_pending',
        `Approval ${approvalId} is not pending.`,
      );
    }
    this.#approvals.delete(approvalId);
    this.#emit(
      'runtime.approval.resolved',
      { approval_id: approvalId, decision: decision === 'approve' ? 'approved' : 'denied' },
      observed('can_use_tool.response'),
      `${approvalId}:resolved`,
    );
    approval.answer(
      decision === 'approve'
        ? { behavior: 'allow', updatedInput: approval.input }
        : { behavior: 'deny', message: message ?? DENIED_MESSAGE },
    );
  }

  /**
   * Answers a pending AskUserQuestion: Claude Code reads the answers from the tool's input, keyed by
   * each question's text, the chosen labels and any typed text joined with ", " as it shows them.
   */
  answer(questionId: string, answers: readonly QuestionAnswer[]): void {
    this.#assertOpen();
    const question = this.#questions.get(questionId);
    if (question === undefined) {
      throw new RuntimeActionError(
        'question_not_pending',
        `Question ${questionId} is not waiting for an answer.`,
      );
    }
    this.#questions.delete(questionId);
    // Built from entries, so a question whose text is `__proto__` keeps its answer as its own key.
    const byText: Record<string, string> = Object.fromEntries(
      answers.flatMap((given) => {
        const text = question.texts.get(given.key);
        if (text === undefined) return [];
        const parts = [...given.selected, ...(given.text === null ? [] : [given.text])];
        return [[text, parts.join(', ')]];
      }),
    );
    this.#emit(
      'runtime.question.resolved',
      { question_id: questionId, outcome: 'answered' },
      observed('can_use_tool.response'),
      `${questionId}:resolved`,
    );
    question.answer({ behavior: 'allow', updatedInput: { ...question.input, answers: byText } });
  }

  /** Resolves when Claude Code confirms the interrupt; the turn's end is reported separately. */
  async interrupt(): Promise<void> {
    this.#assertOpen();
    const turn = this.#turn;
    const handle = this.#query;
    if (turn === null || handle === null) {
      // A question asked outside a turn, as by a background task, would otherwise wait for good.
      if (this.#withdrawQuestions() > 0) return;
      throw new RuntimeActionError('no_running_turn', 'There is no running turn to interrupt.');
    }
    turn.interruptRequested = true;
    try {
      await handle.interrupt();
    } catch (error) {
      throw new RuntimeActionError(
        'interrupt_failed',
        wholeError(`Claude Code did not confirm the interrupt: ${errorText(error)}`) ??
          'Claude Code did not confirm the interrupt.',
        'unknown',
      );
    }
  }

  async close(): Promise<void> {
    if (this.#state === 'closed') return;
    this.#state = 'closed';
    const turn = this.#turn;
    this.#turn = null;
    turn?.confirmation.reject(
      new RuntimeActionError(
        'runtime_closed',
        'The Claude Agent runtime closed before Claude Code took up the instruction.',
        'unknown',
      ),
    );
    this.#approvals.clear();
    this.#questions.clear();
    this.#input.end();
    this.#query?.close();
    await this.#consuming;
  }

  /** The SDK calls this for every tool use that needs a person's decision. It may wait indefinitely. */
  readonly canUseTool: CanUseTool = (toolName, input, options) => {
    if (this.#state !== 'open') return Promise.reject(new Error('The session is no longer open.'));
    if (options.signal.aborted)
      return Promise.reject(new Error('The permission request was withdrawn.'));
    this.#evidence(this.#nativeId ?? this.id, 'can_use_tool');
    const approvalId = options.requestId || options.toolUseID;
    const { promise, resolve, reject } = Promise.withResolvers<PermissionResult>();
    const asked = toolName === ASK_USER_QUESTION ? askedQuestions(input) : null;
    if (asked !== null) {
      // A question, not a permission: approving it would answer nothing (agent-questions.md).
      this.#questions.set(approvalId, { input, texts: asked.texts, answer: resolve });
      options.signal.addEventListener(
        'abort',
        () => {
          if (this.#questions.delete(approvalId)) reject(new Error('The question was withdrawn.'));
        },
        { once: true },
      );
      this.#emit(
        'runtime.question.asked',
        { question_id: approvalId, prompts: asked.prompts, answerable: asked.answerable },
        observed('can_use_tool'),
        `${approvalId}:asked`,
      );
      return promise;
    }
    this.#approvals.set(approvalId, { input, answer: resolve });
    // Claude Code withdraws a request it no longer needs, for example when the turn is interrupted.
    options.signal.addEventListener(
      'abort',
      () => {
        if (this.#approvals.delete(approvalId))
          reject(new Error('The permission request was withdrawn.'));
      },
      { once: true },
    );
    this.#emit(
      'runtime.approval.requested',
      {
        approval_id: approvalId,
        ...approvalRequest(toolName, input),
      },
      observed('can_use_tool'),
      `${approvalId}:requested`,
    );
    return promise;
  };

  #deliver(text: string, pictures: readonly InstructionPicture[] = []): Turn {
    const id = randomUUID();
    const turn = newTurn(id, id);
    this.#turn = turn;
    // The same shape the SDK itself sends for a string prompt, plus a uuid that names the turn; any
    // pictures go first, as base64 image blocks of the bytes the host read (A4).
    this.#input.push({
      type: 'user',
      session_id: '',
      message: {
        role: 'user',
        content: [
          ...pictures.map((picture) => ({
            type: 'image' as const,
            source: {
              type: 'base64' as const,
              media_type: picture.media_type,
              data: Buffer.from(picture.bytes).toString('base64'),
            },
          })),
          { type: 'text' as const, text },
        ],
      },
      parent_tool_use_id: null,
      uuid: id,
    });
    return turn;
  }

  #assertOpen(): void {
    if (this.#state === 'closed') throw closedError();
    if (this.#state === 'lost') {
      throw new RuntimeActionError(
        'runtime_unreachable',
        'The Claude Code process for this execution has ended.',
      );
    }
  }

  async #consume(handle: QueryHandle): Promise<void> {
    let failure: unknown;
    try {
      for await (const message of handle) {
        if (this.#state === 'open') this.#handle(message);
      }
    } catch (error) {
      failure = error;
    }
    if (this.#state === 'open') this.#lose(failure);
  }

  #handle(message: SDKMessage): void {
    switch (message.type) {
      case 'system':
        if (message.subtype === 'status' && message.permissionMode !== undefined) {
          this.#modeWaiter?.(message.permissionMode);
        }
        if (message.subtype === 'init') {
          // A plan asked for, at a start or a resume, that Claude Code reports in another mode is
          // stopped before anything is recorded of it (AUDIT3 4b F1 and S1), as OpenCode's is.
          if (this.#planExpected && !this.#planChecked) {
            this.#planChecked = true;
            if (message.permissionMode !== 'plan') {
              this.#stopFor(
                this.#nativeId === null
                  ? `Claude Code started the session in the ${message.permissionMode} mode, not plan mode, so it was stopped and is not used; whether it began the instruction is unknown.`
                  : `Claude Code resumed the session in the ${message.permissionMode} mode, not the plan mode it was recorded in, so it was stopped.`,
              );
              return;
            }
          }
          // The mode the start runs in, as Claude Code reports it: a plan is recorded only from this.
          this.#initMode ??= message.permissionMode;
          this.#evidence(message.session_id, 'system.init', message.uuid);
          this.#reportModel(message.model);
        }
        return;
      case 'assistant':
        this.#evidence(message.session_id, 'assistant');
        this.#assistant(message);
        return;
      case 'user':
        if ('isReplay' in message) return;
        this.#evidence(message.session_id ?? this.id, 'user');
        this.#toolResults(message);
        return;
      case 'result':
        this.#evidence(message.session_id, `result.${message.subtype}`);
        this.#finishTurn(message);
        return;
      default:
        return;
    }
  }

  /**
   * Records that the runtime is working on a turn. The first evidence of a delivered turn confirms
   * it, and the first confirmation also confirms the session. A system/init message outside any
   * turn marks a turn the runtime began by itself; other messages never invent a turn.
   */
  #evidence(sessionId: string, nativeType: string, initUuid?: string): void {
    let turn = this.#turn;
    if (turn === null) {
      if (initUuid === undefined) return;
      turn = newTurn(initUuid, null);
      this.#turn = turn;
    }
    if (turn.confirmed) return;
    turn.confirmed = true;
    if (this.#nativeId === null) {
      this.#nativeId = sessionId;
      this.#emit(
        'runtime.execution.started',
        { native_id: sessionId, ...this.#record, ...(this.#initMode === 'plan' && { plan: true }) },
        observed(nativeType),
        'session',
      );
    }
    this.#emit(
      'runtime.turn.started',
      { turn_id: turn.id },
      observed(nativeType),
      `${turn.key}:started`,
    );
    turn.confirmation.resolve();
  }

  /** Reports the model Claude Code says the session uses, when it is new for the session. */
  #reportModel(model: unknown): void {
    if (typeof model !== 'string' || model === this.#model || !MODEL_PATTERN.test(model)) return;
    if (this.#nativeId === null) return;
    this.#model = model;
    this.#modelReports += 1;
    this.#emit(
      'runtime.model.used',
      { model_ref: model },
      observed('system.init'),
      `model:${this.#modelReports}`,
    );
  }

  #assistant(message: SDKAssistantMessage): void {
    // Subagent text is not the agent speaking to the person; synthetic error messages are not prose.
    const speaks = message.parent_tool_use_id === null && message.error === undefined;
    // The SDK marks the synthetic message an API error leaves (SDKAssistantMessageError, 0.3.283): a
    // key the API does not know, or a cloud provider's credential it refused.
    if (
      this.#turn !== null &&
      (message.error === 'authentication_failed' || message.error === 'cloud_credential_error')
    )
      this.#turn.credentialRefused = true;
    for (const [index, block] of message.message.content.entries()) {
      const eventId = `${message.uuid}:${index}`;
      if (block.type === 'text' && speaks) {
        const text = clip(block.text, 32_000);
        if (text !== null) {
          this.#emit('runtime.agent_message', { text }, reported('assistant.text'), eventId);
        }
      } else if (block.type === 'tool_use') {
        this.#activeTools.add(block.id);
        this.#emit(
          'runtime.tool.started',
          {
            tool_call_id: block.id,
            tool_name: block.name.slice(0, 128),
            title: describeInput(block.input),
          },
          observed('assistant.tool_use'),
          eventId,
        );
      }
    }
  }

  #toolResults(message: SDKUserMessage): void {
    const content = message.message.content;
    if (typeof content === 'string') return;
    for (const [index, block] of content.entries()) {
      if (block.type !== 'tool_result' || !this.#activeTools.delete(block.tool_use_id)) continue;
      this.#emit(
        'runtime.tool.completed',
        {
          tool_call_id: block.tool_use_id,
          outcome: block.is_error === true ? 'failed' : 'succeeded',
        },
        observed('user.tool_result'),
        `${message.uuid ?? block.tool_use_id}:${index}`,
      );
    }
  }

  /**
   * Denies every question still waiting, each reported dismissed: the person can no longer answer
   * it once the turn it belongs to has ended or they stopped the work. Returns how many there were.
   */
  #withdrawQuestions(): number {
    const waiting = [...this.#questions.entries()];
    this.#questions.clear();
    for (const [questionId, question] of waiting) {
      this.#emit(
        'runtime.question.resolved',
        { question_id: questionId, outcome: 'dismissed' },
        observed('can_use_tool.response'),
        `${questionId}:resolved`,
      );
      question.answer({ behavior: 'deny', message: WITHDRAWN_MESSAGE });
    }
    return waiting.length;
  }

  /**
   * The CLI emits exactly one result per turn; it ends the turn. A question still waiting then was
   * asked outside the turn, since a question asked in it holds the turn open; the turn's end takes
   * it away from the person, so it is withdrawn rather than left for an agent to wait on.
   */
  #finishTurn(result: SDKResultMessage): void {
    const turn = this.#turn;
    if (turn === null) return;
    this.#turn = null;
    this.#activeTools.clear();
    this.#withdrawQuestions();
    const provenance = observed(`result.${result.subtype}`);
    const succeeded = result.subtype === 'success' && !result.is_error;
    const aborted =
      result.terminal_reason === 'aborted_streaming' || result.terminal_reason === 'aborted_tools';
    if (turn.interruptRequested && (!succeeded || aborted)) {
      this.#emit('runtime.turn.interrupted', { turn_id: turn.id }, provenance, result.uuid);
    } else if (succeeded) {
      this.#emit('runtime.turn.completed', { turn_id: turn.id }, provenance, result.uuid);
    } else {
      const error = resultError(result);
      this.#emit(
        'runtime.turn.failed',
        {
          turn_id: turn.id,
          error: turn.credentialRefused
            ? { code: CREDENTIAL_REFUSED, message: error.message }
            : error,
        },
        provenance,
        result.uuid,
      );
    }
  }

  /** The query ended on its own, so the process is gone. A query that ends without a result has an unknown effect. */
  /**
   * Stops the session because what Claude Code reported broke what it was started or resumed with
   * (PLAN): its turn, if one is waiting, fails with the reason; the session is lost and its process
   * closed. Effect unknown: Claude Code may have begun before the report came.
   */
  #stopFor(reason: string): void {
    if (this.#state !== 'open') return;
    this.#state = 'lost';
    const turn = this.#turn;
    this.#turn = null;
    turn?.confirmation.reject(new RuntimeActionError('runtime_refused', reason, 'unknown'));
    if (this.#nativeId !== null) {
      this.#emit('runtime.connection.lost', { reason }, observed('system.init'), 'stopped');
    }
    this.#approvals.clear();
    this.#questions.clear();
    this.#activeTools.clear();
    this.#input.end();
    try {
      this.#query?.close();
    } catch {
      // Closing a query that already ended changes nothing.
    }
  }

  #lose(failure: unknown): void {
    this.#state = 'lost';
    const reason =
      wholeError(
        failure === undefined
          ? 'The Claude Code process ended.'
          : `The Claude Code process ended: ${errorText(failure)}`,
      ) ?? 'The Claude Code process ended.';
    const turn = this.#turn;
    this.#turn = null;
    if (turn !== null) {
      if (turn.confirmed) {
        this.#emit(
          'runtime.turn.failed',
          {
            turn_id: turn.id,
            error: {
              code: 'no_result',
              message:
                wholeError(
                  `The turn ended without a result, so whether its work took effect is unknown. ${reason}`,
                ) ?? reason,
            },
          },
          { epistemic: 'inferred', native_type: null, rule: 'query_ended_without_result' },
          `${turn.key}:no_result`,
        );
      }
      turn.confirmation.reject(new RuntimeActionError('runtime_exited', reason, 'unknown'));
    }
    if (this.#nativeId !== null) {
      this.#emit('runtime.connection.lost', { reason }, observed('query.end'), 'connection_lost');
    }
    this.#approvals.clear();
    this.#questions.clear();
    this.#activeTools.clear();
    this.#input.end();
  }

  #emit<T extends RuntimeEventType>(
    type: T,
    payload: EventOf<T>['payload'],
    provenance: Provenance,
    localId: string,
  ): void {
    if (this.#state === 'closed') return;
    this.#sequence += 1;
    this.#emitObservation({
      type,
      payload,
      provenance,
      occurred_at: this.#clock.now().toISOString(),
      native_event_id: `${this.#eventPrefix}:${localId}`,
      sequence: this.#sequenced ? this.#sequence : null,
    } as RuntimeObservation);
  }
}

/** A single-consumer queue of the user messages the adapter delivers to the SDK. */
class InputQueue implements AsyncIterable<SDKUserMessage> {
  readonly #buffered: SDKUserMessage[] = [];
  #waiting: ((result: IteratorResult<SDKUserMessage>) => void) | null = null;
  #ended = false;

  push(message: SDKUserMessage): void {
    const waiting = this.#waiting;
    this.#waiting = null;
    if (waiting !== null) waiting({ done: false, value: message });
    else this.#buffered.push(message);
  }

  end(): void {
    this.#ended = true;
    const waiting = this.#waiting;
    this.#waiting = null;
    waiting?.({ done: true, value: undefined });
  }

  [Symbol.asyncIterator](): AsyncIterator<SDKUserMessage> {
    return {
      next: () => {
        const message = this.#buffered.shift();
        if (message !== undefined) return Promise.resolve({ done: false, value: message });
        if (this.#ended) return Promise.resolve({ done: true, value: undefined });
        return new Promise((resolve) => {
          this.#waiting = resolve;
        });
      },
      return: () => {
        this.end();
        return Promise.resolve({ done: true, value: undefined });
      },
    };
  }
}

function observed(nativeType: string): Provenance {
  return { epistemic: 'observed', native_type: `claude-agent-sdk/${nativeType}` };
}

function reported(nativeType: string): Provenance {
  return { epistemic: 'reported', native_type: `claude-agent-sdk/${nativeType}` };
}

const CODE_PATTERN = /^[a-z][a-z0-9_]{0,63}$/;

function resultError(result: SDKResultMessage): ErrorInfo {
  if (result.subtype === 'success') {
    // A success result flagged as an error carries the API error text.
    return {
      code: 'api_error',
      message: wholeError(result.result) ?? 'The turn ended with an API error.',
    };
  }
  return {
    code: CODE_PATTERN.test(result.subtype) ? result.subtype : 'turn_failed',
    message: wholeError(result.errors.join('; ')) ?? `The turn ended with ${result.subtype}.`,
  };
}

/** Structured input fields that say what a tool call will do, in order of preference. */
const DESCRIBING_FIELDS = ['command', 'file_path', 'notebook_path', 'url', 'query', 'pattern'];

/**
 * Describes a tool call from its structured input, for example the Bash command. Never from model
 * prose. Whole: the control plane takes credentials out of it, which it has to see whole to find,
 * and then cuts it to the contract.
 */
function describeInput(input: unknown): string | null {
  if (typeof input !== 'object' || input === null) return null;
  for (const field of DESCRIBING_FIELDS) {
    const value: unknown = Reflect.get(input, field);
    if (typeof value === 'string' && /\S/.test(value)) return value;
  }
  return null;
}

/**
 * AskUserQuestion's input as questions: `questions: [{question, header, options: [{label,
 * description}], multiSelect}]`, a typed answer always possible. Null when the input does not have
 * that shape, so it is shown as an ordinary approval instead. Its texts are reported whole: the
 * control plane takes credentials out of them, then fits them to the contract, and a question with
 * anything cut can't be answered. Not answerable here when a question text or label is repeated,
 * since Claude Code matches answers by text, or there are more questions or options than the
 * contract holds.
 */
function askedQuestions(input: Record<string, unknown>): {
  readonly prompts: QuestionPrompt[];
  readonly texts: ReadonlyMap<string, string>;
  readonly answerable: boolean;
} | null {
  const raw = Array.isArray(input.questions) ? input.questions : null;
  if (raw === null || raw.length === 0) return null;
  let answerable = raw.length <= 10;
  const visible = (value: unknown): value is string =>
    typeof value === 'string' && /\S/.test(value);
  const prompts: QuestionPrompt[] = [];
  const texts = new Map<string, string>();
  for (const [index, item] of raw.slice(0, 10).entries()) {
    if (typeof item !== 'object' || item === null) return null;
    const entry = item as Record<string, unknown>;
    if (!visible(entry.question)) return null;
    const question = entry.question;
    if ([...texts.values()].includes(question)) answerable = false;
    const offered = Array.isArray(entry.options) ? entry.options : [];
    if (offered.length > 20) answerable = false;
    const options: QuestionPrompt['options'] = [];
    for (const option of offered.slice(0, 20)) {
      const record =
        typeof option === 'object' && option !== null ? (option as Record<string, unknown>) : {};
      const label = visible(record.label) ? record.label : null;
      if (label === null || options.some((known) => known.label === label)) {
        answerable = false;
        if (label === null) continue;
      }
      const description = visible(record.description) ? record.description : null;
      options.push({ label, description });
    }
    const key = `q${index}`;
    texts.set(key, question);
    prompts.push({
      key,
      header: visible(entry.header) ? entry.header : null,
      text: question,
      options,
      multiple: entry.multiSelect === true,
      free_text: true,
      secret: false,
    });
  }
  return { prompts, texts, answerable };
}

/**
 * The inputs of Claude Code's own tools that change neither what runs nor where a change is
 * written, besides the field that describes the call: a command's description and time limit, a
 * read's range and a fetch's question, and a change's content, which a request does not show
 * (`runtime.approval.requested`'s `complete`).
 */
const NOT_ACTING_INPUTS: Readonly<Record<string, readonly string[]>> = {
  Bash: ['description', 'timeout', 'run_in_background'],
  // Reads that run nothing and write nothing: which part of a file, what to ask of a page.
  Read: ['offset', 'limit'],
  WebFetch: ['prompt'],
  Edit: ['old_string', 'new_string', 'replace_all'],
  Write: ['content'],
  NotebookEdit: ['new_source', 'cell_id', 'cell_type', 'edit_mode'],
};

/**
 * What an approval asks for, whole: the field that describes the call (`describeInput`), after a
 * bracket saying where a Bash command runs and whether in the background, or else the whole input
 * as JSON. Complete
 * when that is all the call says that acts: the whole input, or the describing field of one of
 * Claude Code's own tools beside only inputs that do not act. A call of any other tool described by
 * one field, while it has other inputs, is not complete, so it can only be denied.
 */
function approvalRequest(
  toolName: string,
  input: Record<string, unknown>,
): { readonly subject: ApprovalSubject; readonly complete: boolean } {
  const described = describeInput(input);
  const field = DESCRIBING_FIELDS.find((name) => input[name] === described);
  const others = Object.keys(input).filter((name) => name !== field);
  const quiet = NOT_ACTING_INPUTS[toolName] ?? [];
  // A Bash command names no folder: it runs in the session's shell, which started in the task's
  // folder (the session's cwd) and keeps where an earlier command moved it. Said in a bracket that
  // always comes first, so a command that starts with a bracket of its own follows the real one.
  const where =
    toolName === 'Bash' && described !== null
      ? `[in its shell, which started in the task's folder${input.run_in_background === true ? ', in the background' : ''}] `
      : '';
  return {
    subject: {
      kind: 'tool_use',
      tool_name: toolName.slice(0, 128),
      summary: described === null ? JSON.stringify(input) : `${where}${described}`,
    },
    // A tool's name cut to the contract could read as another's.
    complete:
      toolName.length <= 128 &&
      (described === null || others.every((name) => quiet.includes(name))),
  };
}

/** Shortens text to a contract limit. Null when nothing visible remains. */
function clip(text: string, max: number): string | null {
  const clipped = text.slice(0, max);
  return /\S/.test(clipped) ? clipped : null;
}

/**
 * Error text, whole: the control plane takes credentials out of it, which it finds only whole,
 * then cuts it to the contract. Null when nothing visible is in it.
 */
function wholeError(text: string): string | null {
  return /\S/.test(text) ? text : null;
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
