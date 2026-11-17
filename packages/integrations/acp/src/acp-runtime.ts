import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { promisify } from 'node:util';
import type {
  ApprovalDecision,
  ApprovalLevel,
  Provenance,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeOptions,
  Timestamp,
} from '@halcyonic/contracts';
import {
  confirmProjectLocation,
  type DirectoryPolicy,
  type ExecutionContext,
  type InstructionRequest,
  type ObservationSink,
  type OptionsValidation,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type StartExecutionRequest,
  type StartExecutionResult,
} from '@halcyonic/runtime-core';
import {
  type AgentAppScope,
  agentAppProfile,
  type HostFoldersScope,
  SANDBOX_EXEC,
} from '@halcyonic/seatbelt';
import { approvalRequest, toolNameOf } from './approvals.ts';
import type { AcpProfile } from './profiles.ts';
import { type RequestId, RpcConnection, RpcError, RpcUnanswered } from './rpc.ts';

const run = promisify(execFile);

/** The ACP protocol version this adapter speaks (agentclientprotocol.com, `initialize`). */
export const ACP_PROTOCOL_VERSION = 1;

/** ACP's error for a request that needs the agent app signed in first (`auth_required`). */
const AUTH_REQUIRED = -32000;

/**
 * What every ACP agent app gives through this adapter, each proven by a runtime test of the pinned
 * Antigravity on the stand-in model (validation/acp-agents.md): a turn from a prompt, another at
 * rest, an approval answered, a turn cancelled. Nothing runs while a turn runs (ACP has no steer) and
 * no question reaches the person (ACP has no question surface), so neither is declared.
 */
export const ACP_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  answer_question: false,
  interrupt: true,
};

/** Why a run is not counted confined, in words the headset can show (`unconfined_reason`). */
export const UNCONFINED_REASON =
  "It talks to its maker's service directly, which Halora's closed setup does not allow, so it runs outside it. Halora's own folders, and your sign-ins for cloud accounts and for pushing code, are still kept from it.";

/** How long the app has to answer `initialize` and `session/new`. */
const HANDSHAKE_MS = 60_000;
/** How long an approval or a cancel waits for the app to show it took effect. */
const CONFIRM_MS = 15_000;

export interface AcpRuntimeOptions {
  readonly profile: AcpProfile;
  readonly binaryPath: string;
  readonly directoryPolicy: DirectoryPolicy;
  /**
   * The environment the app is launched with, built by the host. The profile's own variables are
   * taken out of it, so the app signs in only its own way, never with a key from the host.
   */
  readonly env: Readonly<Record<string, string>>;
  /** The person's home, where the app's settings home of Halora's own is made (`AcpProfile.settingsHome`). */
  readonly home: string;
  /**
   * Variables of the app's own a test sets, such as a stand-in model's address and a dummy key, with
   * the ACP auth method that uses them. The control plane never passes this.
   */
  readonly standIn?: {
    readonly variables: Readonly<Record<string, string>>;
    readonly authMethod: string;
    /** Files the stand-in needs in the launch's settings home, by path inside it (its key setting). */
    readonly settings?: Readonly<Record<string, string>>;
  };
  /**
   * ACP configuration options set on every session before its first prompt (`session/set_config_option`), such as
   * a bounded test run's cheapest model; a start is refused where the app does not take one. The control plane
   * passes none.
   */
  readonly sessionOptions?: Readonly<Record<string, string>>;
  /**
   * On macOS, the host's own folders the light profile keeps the app and everything it runs from
   * (its data folder with the keys and the journal, the task copies, every `.halcyonic` home), and
   * where the profile file is written. Null runs the app with no profile.
   */
  readonly hostFolders: { readonly scope: HostFoldersScope; readonly profileFolder: string } | null;
  readonly clock?: () => Date;
}

interface Turn {
  readonly id: string;
  /** Settles when the app answered `session/prompt`. */
  readonly done: Promise<void>;
  interrupting: {
    resolve: () => void;
    reject: (error: Error) => void;
    timer: NodeJS.Timeout;
  } | null;
}

interface Approval {
  readonly id: string;
  readonly rpcId: RequestId;
  readonly toolCallId: string | null;
  readonly allow: string | null;
  readonly reject: string | null;
  answered: {
    readonly decision: 'approved' | 'denied';
    readonly resolve: () => void;
    readonly reject: (error: Error) => void;
    readonly timer: NodeJS.Timeout;
  } | null;
}

function timestamp(date: Date): Timestamp {
  return date.toISOString() as Timestamp;
}

function observed(nativeType: string): Provenance {
  return { epistemic: 'observed', native_type: nativeType.slice(0, 128) };
}

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function text(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

/** Words for an ACP stop reason that ended a turn without its work done (`StopReason`). */
const STOP_FAILURES: Readonly<Record<string, string>> = {
  max_tokens: 'it reached its token limit',
  max_turn_requests: 'it reached its limit of model requests for one turn',
  refusal: 'the model refused to go on',
};

/**
 * Directs an agent app that speaks the Agent Client Protocol (ACP, protocol version 1) over its stdio,
 * one app process per execution, the same way for every app; what differs between apps is its
 * profile. The app signs in its own way, once, on this Mac; Halcyonic never holds its key. On macOS
 * the app runs under the light profile, never reading or writing the host's own folders, and is
 * recorded not confined.
 */
export class AcpRuntimeAdapter implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  readonly #options: AcpRuntimeOptions;
  readonly #clock: () => Date;
  readonly #sessions = new Map<string, AcpSession>();
  #closed = false;

  constructor(options: AcpRuntimeOptions) {
    this.#options = options;
    this.#clock = options.clock ?? (() => new Date());
    this.descriptor = {
      runtime_id: options.profile.runtimeId as RuntimeDescriptor['runtime_id'],
      kind: options.profile.kind as RuntimeDescriptor['kind'],
      display_name: options.profile.displayName,
      synthetic: false,
      capabilities: ACP_CAPABILITIES,
      model_choice: 'none',
      uses_project_location: true,
      reports_tool_activity: false,
    };
  }

  validateStartOptions(
    options: RuntimeOptions,
    modelRef: string | null,
    approvalLevel?: ApprovalLevel,
  ): OptionsValidation {
    if (Object.keys(options).length > 0) {
      return { ok: false, message: `${this.descriptor.display_name} takes no start options.` };
    }
    if (modelRef !== null) {
      return {
        ok: false,
        message: `${this.descriptor.display_name} offers no choice of model here.`,
      };
    }
    if (approvalLevel !== undefined && approvalLevel !== 'ask') {
      return {
        ok: false,
        message: `${this.descriptor.display_name} always asks before it acts, so it cannot start without asking. Choose Ask Me First.`,
      };
    }
    return { ok: true };
  }

  /** It runs with the person's own access but for the host's folders: never a sandbox of Halcyonic's. */
  runsOutsideSandbox(): boolean {
    return true;
  }

  ownsEnvironmentName(name: string): boolean {
    return this.#options.profile.ownVariables.includes(name);
  }

  async startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    if (this.#closed) throw new RuntimeActionError('runtime_closed', 'The runtime is closing.');
    const id = request.execution.execution_id;
    if (this.#sessions.has(id)) {
      throw new RuntimeActionError('execution_exists', 'This execution already runs here.');
    }
    if ((request.approval_level ?? 'ask') !== 'ask') {
      throw new RuntimeActionError(
        'level_unsupported',
        `${this.descriptor.display_name} always asks before it acts, so it cannot start without asking. Choose Ask Me First.`,
      );
    }
    const directory = confirmProjectLocation(this.#options.directoryPolicy, request.directory);
    const session = new AcpSession(this.#options, this.#clock, request.emit, directory);
    this.#sessions.set(id, session);
    try {
      const nativeId = await session.open(() =>
        confirmProjectLocation(this.#options.directoryPolicy, request.directory),
      );
      session.prompt(request.instruction);
      return { native_id: nativeId };
    } catch (error) {
      this.#sessions.delete(id);
      await session.close();
      throw error;
    }
  }

  async sendInstruction(request: InstructionRequest): Promise<void> {
    const session = this.#session(request.execution);
    if (request.leave_plan === true) {
      throw new RuntimeActionError('plan_unsupported', 'No plan was started here.');
    }
    session.prompt(request.text);
  }

  async respondToApproval(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
  }): Promise<void> {
    await this.#session(request.execution).respond(request.approval_id, request.decision);
  }

  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    await this.#session(request.execution).interrupt();
  }

  async close(): Promise<void> {
    this.#closed = true;
    const sessions = [...this.#sessions.values()];
    this.#sessions.clear();
    await Promise.all(sessions.map((session) => session.close()));
  }

  #session(execution: ExecutionContext): AcpSession {
    const session = this.#sessions.get(execution.execution_id);
    if (session === undefined || !session.alive) {
      throw new RuntimeActionError(
        'execution_not_found',
        `${this.descriptor.display_name} is no longer running this task.`,
      );
    }
    return session;
  }
}

/** One execution: one app process, one ACP session. */
class AcpSession {
  readonly #options: AcpRuntimeOptions;
  readonly #clock: () => Date;
  readonly #emitTo: ObservationSink;
  readonly #directory: string;
  readonly #name: string;
  /** Names this launch's records, so none is reused by a later launch (`native_event_id`). */
  readonly #launch = randomUUID();
  readonly #approvals = new Map<string, Approval>();
  readonly #toolsStarted = new Set<string>();
  readonly #toolsEnded = new Set<string>();
  #counter = 0;
  #child: ChildProcess | null = null;
  #rpc: RpcConnection | null = null;
  #sessionId: string | null = null;
  #turn: Turn | null = null;
  #message = '';
  /** Each tool call as the app last reported it, its fields merged update by update (ACP `ToolCallUpdate`). */
  readonly #calls = new Map<string, Record<string, unknown>>();
  #profileFile: string | null = null;
  #settingsHome: string | null = null;
  #closing = false;
  #alive = false;

  constructor(
    options: AcpRuntimeOptions,
    clock: () => Date,
    emit: ObservationSink,
    directory: string,
  ) {
    this.#options = options;
    this.#clock = clock;
    this.#emitTo = emit;
    this.#directory = directory;
    this.#name = options.profile.displayName;
  }

  get alive(): boolean {
    return this.#alive;
  }

  /** Launches the app, checks its version, opens a session in the folder and reports it started. */
  async open(confirmAgain: () => string): Promise<string> {
    const named = await this.#versionLine();
    const rpc = this.#launch_();
    let initialized: unknown;
    try {
      initialized = await rpc.request(
        'initialize',
        {
          protocolVersion: ACP_PROTOCOL_VERSION,
          // The app reads and writes files and runs commands itself, in the folder it was given.
          clientCapabilities: {
            fs: { readTextFile: false, writeTextFile: false },
            terminal: false,
          },
          clientInfo: { name: 'halcyonic', version: '0' },
        },
        HANDSHAKE_MS,
      );
    } catch (error) {
      throw this.#startError(error);
    }
    const info = record(record(initialized)?.agentInfo);
    // Kiro's v3 engine names no version once signed in: its own `--version` line, read before the launch, does.
    const version = text(info?.version) ?? named;
    if (version !== this.#options.profile.version) {
      throw new RuntimeActionError(
        'runtime_version_unsupported',
        version === null
          ? `Halora works with ${this.#name} only, and the copy on this computer does not say its version. Install ${this.#name}, then start again.`
          : `Halora works with ${this.#name} only; this computer has ${version}. Install ${this.#name}, then start again.`,
      );
    }
    if (record(initialized)?.protocolVersion !== ACP_PROTOCOL_VERSION) {
      throw new RuntimeActionError(
        'runtime_version_unsupported',
        `${this.#name} on this computer answers in a way Halora does not know, so it could not start. Reinstall ${this.#name}, then start again.`,
      );
    }
    if (this.#options.standIn !== undefined) {
      try {
        await rpc.request(
          'authenticate',
          { methodId: this.#options.standIn.authMethod },
          HANDSHAKE_MS,
        );
      } catch (error) {
        throw this.#startError(error);
      }
    }
    // After the launch's wait, the folder is asked again right before it is handed to the app.
    const cwd = confirmAgain();
    let created: unknown;
    try {
      created = await rpc.request('session/new', { cwd, mcpServers: [] }, HANDSHAKE_MS);
    } catch (error) {
      throw this.#startError(error);
    }
    const sessionId = text(record(created)?.sessionId);
    if (sessionId === null || sessionId === '' || sessionId.length > 512) {
      throw new RuntimeActionError(
        'runtime_error',
        `${this.#name} opened a session without an id.`,
        'unknown',
      );
    }
    this.#sessionId = sessionId;
    await this.#askingMode(rpc, sessionId, record(record(created)?.modes));
    for (const [configId, value] of Object.entries(this.#options.sessionOptions ?? {})) {
      try {
        await rpc.request(
          'session/set_config_option',
          { sessionId, configId, value },
          HANDSHAKE_MS,
        );
      } catch {
        throw new RuntimeActionError(
          'option_refused',
          `${this.#name} did not take the setting ${configId} for this task.`,
        );
      }
    }
    this.#alive = true;
    this.#emit(
      'runtime.execution.started',
      { native_id: sessionId, confined: false, unconfined_reason: UNCONFINED_REASON },
      observed('session/new'),
    );
    return sessionId;
  }

  /** Starts a turn from an instruction; it ends when the app answers the prompt. */
  prompt(instruction: string): void {
    const rpc = this.#rpc;
    const sessionId = this.#sessionId;
    if (rpc === null || sessionId === null || !this.#alive) {
      throw new RuntimeActionError(
        'execution_not_found',
        `${this.#name} is no longer running this task.`,
      );
    }
    if (this.#turn !== null) {
      throw new RuntimeActionError(
        'turn_running',
        `${this.#name} takes an instruction only once its turn has ended.`,
      );
    }
    if (!rpc.open) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        `The connection to ${this.#name} has ended.`,
      );
    }
    const turnId = randomUUID();
    const answered = rpc.request(
      'session/prompt',
      { sessionId, prompt: [{ type: 'text', text: instruction }] },
      null,
    );
    const turn: Turn = {
      id: turnId,
      done: answered.then(
        () => undefined,
        () => undefined,
      ),
      interrupting: null,
    };
    this.#turn = turn;
    this.#emit(
      'runtime.turn.started',
      { turn_id: turnId },
      { epistemic: 'inferred', native_type: 'session/prompt', rule: 'acp.prompt.sent' },
    );
    answered.then(
      (result) => this.#ended(turn, result),
      (error: unknown) => this.#failed(turn, error),
    );
  }

  /** Answers an approval with the app's own once-only option and waits for the app to act on it. */
  respond(approvalId: string, decision: ApprovalDecision): Promise<void> {
    const approval = this.#approvals.get(approvalId);
    const rpc = this.#rpc;
    if (approval === undefined || rpc === null || !rpc.open) {
      return Promise.reject(
        new RuntimeActionError(
          'approval_not_found',
          `${this.#name} is no longer waiting for that answer.`,
        ),
      );
    }
    if (approval.answered !== null) {
      return Promise.reject(
        new RuntimeActionError('approval_answered', 'That request was already answered.'),
      );
    }
    const approved = decision === 'approve';
    const option = approved ? approval.allow : approval.reject;
    if (approved && option === null) {
      return Promise.reject(
        new RuntimeActionError(
          'approval_not_approvable',
          `${this.#name} offered no way to allow this once.`,
        ),
      );
    }
    const { promise, resolve, reject } = Promise.withResolvers<void>();
    approval.answered = {
      decision: approved ? 'approved' : 'denied',
      resolve,
      reject,
      timer: setTimeout(() => {
        if (approval.answered === null) return;
        approval.answered = null;
        this.#approvals.delete(approvalId);
        reject(
          new RuntimeActionError(
            'approval_unconfirmed',
            `${this.#name} did not show that it took the answer.`,
            'unknown',
          ),
        );
      }, CONFIRM_MS),
    };
    // A request with no once-only refusal is refused by withdrawing it, never by a lasting rule.
    rpc.respond(
      approval.rpcId,
      option === null
        ? { outcome: { outcome: 'cancelled' } }
        : { outcome: { outcome: 'selected', optionId: option } },
    );
    return promise;
  }

  /** Cancels the running turn (`session/cancel`) and resolves once the app ends it cancelled. */
  interrupt(): Promise<void> {
    const rpc = this.#rpc;
    const turn = this.#turn;
    if (rpc === null || !rpc.open || this.#sessionId === null) {
      return Promise.reject(
        new RuntimeActionError('runtime_unavailable', `The connection to ${this.#name} has ended.`),
      );
    }
    if (turn === null) {
      return Promise.reject(
        new RuntimeActionError('turn_not_running', 'Nothing is running in this task now.'),
      );
    }
    if (turn.interrupting !== null) return turn.done;
    const { promise, resolve, reject } = Promise.withResolvers<void>();
    turn.interrupting = {
      resolve,
      reject,
      timer: setTimeout(() => {
        reject(
          new RuntimeActionError(
            'interrupt_unconfirmed',
            `${this.#name} did not end the turn in time.`,
            'unknown',
          ),
        );
      }, CONFIRM_MS),
    };
    rpc.notify('session/cancel', { sessionId: this.#sessionId });
    // ACP: the client answers every request it still holds as cancelled once it cancels.
    for (const approval of this.#approvals.values()) {
      if (approval.answered === null)
        rpc.respond(approval.rpcId, { outcome: { outcome: 'cancelled' } });
    }
    return promise;
  }

  async close(): Promise<void> {
    this.#closing = true;
    this.#alive = false;
    const child = this.#child;
    if (child === null || child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
    // The app ends at the end of its input; a signal follows where it does not.
    this.#rpc?.end();
    const term = setTimeout(() => child.kill('SIGTERM'), 2_000);
    const kill = setTimeout(() => child.kill('SIGKILL'), 6_000);
    await exited;
    clearTimeout(term);
    clearTimeout(kill);
  }

  /**
   * The version the app's own `--version` line names, for a profile that reads it there (`versionLine`), or null.
   * A line that does not name one, or no answer in time, reads as none, and the start is refused.
   */
  async #versionLine(): Promise<string | null> {
    const line = this.#options.profile.versionLine;
    if (line === null) return null;
    try {
      const { stdout } = await run(this.#options.binaryPath, line.args, {
        cwd: this.#directory,
        env: { PATH: this.#options.env.PATH ?? '/usr/bin:/bin' },
        timeout: 15_000,
      });
      const match = line.pattern.exec(stdout.trim());
      return match?.[1] ?? null;
    } catch {
      return null;
    }
  }

  #launch_(): RpcConnection {
    const { profile, binaryPath, hostFolders } = this.#options;
    const env: Record<string, string> = { ...this.#options.env };
    for (const name of profile.ownVariables) delete env[name];
    // A settings home of Halora's own, so the person's own settings for the app never apply here (AUDIT3 AC2), and a
    // fresh one for each launch, removed at its end, so nothing written there reaches a later one (AUDIT3 AC3).
    const homes = join(this.#options.home, profile.settingsHome.folder);
    const settings = join(homes, this.#launch);
    mkdirSync(settings, { recursive: true, mode: 0o700 });
    this.#settingsHome = settings;
    for (const [file, text] of Object.entries(this.#options.standIn?.settings ?? {})) {
      mkdirSync(join(settings, file, '..'), { recursive: true, mode: 0o700 });
      writeFileSync(join(settings, file), text, { mode: 0o600 });
    }
    Object.assign(
      env,
      this.#options.standIn?.variables ?? {},
      profile.environment,
      profile.settingsHome.environment(settings),
    );
    let command = binaryPath;
    let args = [...profile.args, ...profile.settingsHome.args(settings)];
    if (hostFolders !== null) {
      const path = writeProfile(hostFolders.profileFolder, `${profile.runtimeId}-${this.#launch}`, {
        ...hostFolders.scope,
        project: this.#directory,
        projectSettings: profile.projectSettings,
        settingsHomes: { root: homes, own: settings },
      });
      this.#profileFile = path;
      args = ['-f', path, binaryPath, ...args];
      command = SANDBOX_EXEC;
    }
    const child = spawn(command, args, {
      cwd: this.#directory,
      env,
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.#child = child;
    // What the app writes to stderr can hold the person's words; it is read and dropped, never logged.
    child.stderr?.resume();
    child.on('error', () => undefined);
    const rpc = new RpcConnection(
      child.stdout as NonNullable<typeof child.stdout>,
      child.stdin as NonNullable<typeof child.stdin>,
      {
        onNotification: (method, params) => this.#notified(method, params),
        onRequest: (id, method, params, connection) =>
          this.#requested(id, method, params, connection),
      },
      this.#name,
    );
    this.#rpc = rpc;
    child.once('exit', () => this.#exited());
    return rpc;
  }

  #startError(error: unknown): RuntimeActionError {
    if (error instanceof RuntimeActionError) return error;
    if (error instanceof RpcError && error.code === AUTH_REQUIRED) {
      return new RuntimeActionError(
        'sign_in_required',
        `${this.#name} is not signed in on this Mac. Sign in to it once in a terminal, then start again.`,
      );
    }
    if (error instanceof RpcError) {
      return new RuntimeActionError(
        'runtime_error',
        `${this.#name} refused to start: ${error.message}`,
      );
    }
    if (error instanceof RpcUnanswered) {
      return new RuntimeActionError(
        'runtime_unavailable',
        error.message,
        error.reason === 'not_sent' ? 'none' : 'unknown',
      );
    }
    return new RuntimeActionError('runtime_error', `${this.#name} could not start.`, 'unknown');
  }

  #notified(method: string, params: unknown): void {
    if (method !== 'session/update') return;
    const body = record(params);
    if (body === null || body.sessionId !== this.#sessionId) return;
    const update = record(body.update);
    if (update === null) return;
    const kind = text(update.sessionUpdate);
    if (kind === 'agent_message_chunk') {
      const content = record(update.content);
      if (content?.type === 'text' && typeof content.text === 'string')
        this.#message += content.text;
      return;
    }
    if (
      kind === 'agent_thought_chunk' ||
      kind === 'usage_update' ||
      kind === 'available_commands_update'
    )
      return;
    if (kind === 'tool_call' || kind === 'tool_call_update') {
      this.#flushMessage();
      this.#toolUpdate(update, kind);
    }
  }

  #toolUpdate(update: Record<string, unknown>, kind: string): void {
    const toolCallId = text(update.toolCallId);
    if (toolCallId === null || toolCallId === '' || toolCallId.length > 400) return;
    this.#remember(toolCallId, update);
    if (!this.#toolsStarted.has(toolCallId) && this.#turn !== null) {
      this.#toolsStarted.add(toolCallId);
      const title = text(update.title);
      this.#emit(
        'runtime.tool.started',
        {
          tool_call_id: toolCallId,
          tool_name: toolNameOf(update),
          title: title !== null && title.trim() !== '' ? title : null,
        },
        observed(kind),
      );
    }
    const status = text(update.status);
    for (const approval of this.#approvals.values()) {
      if (approval.toolCallId !== toolCallId || approval.answered === null) continue;
      const decided = approval.answered.decision;
      // A refused call is settled `failed`, and Antigravity reports it `in_progress` first, so only `failed` shows a
      // denial taken. An allowed one goes on: it runs (`in_progress`), ends, or fails in running, as an approved
      // read the agent-app profile refuses may, with no `in_progress` before it.
      const applied =
        decided === 'approved'
          ? status === 'in_progress' || status === 'completed' || status === 'failed'
          : status === 'failed';
      if (applied) this.#resolveApproval(approval, decided, kind);
    }
    if ((status === 'completed' || status === 'failed') && this.#toolsStarted.has(toolCallId)) {
      if (this.#toolsEnded.has(toolCallId)) return;
      this.#toolsEnded.add(toolCallId);
      this.#emit(
        'runtime.tool.completed',
        {
          tool_call_id: toolCallId,
          turn_id: this.#turn?.id ?? null,
          outcome: status === 'completed' ? 'succeeded' : 'failed',
        },
        observed(`${kind}.${status}`),
      );
    }
  }

  /** Merges an update of a tool call into what the app said of it before: a field it gives replaces the old one. */
  #remember(toolCallId: string, update: Record<string, unknown>): void {
    const known = this.#calls.get(toolCallId) ?? {};
    for (const [key, value] of Object.entries(update)) {
      if (value !== undefined && value !== null && key !== 'sessionUpdate') known[key] = value;
    }
    this.#calls.set(toolCallId, known);
  }

  /**
   * Starts the session in the mode the profile verified asks the person before it acts, switching to it
   * (`session/set_mode`) where the app started in another, such as an allow-by-itself mode; refused otherwise.
   */
  async #askingMode(
    rpc: RpcConnection,
    sessionId: string,
    modes: Record<string, unknown> | null,
  ): Promise<void> {
    const wanted = this.#options.profile.askingMode;
    const current = text(modes?.currentModeId);
    if (current === wanted) return;
    const available = (Array.isArray(modes?.availableModes) ? modes.availableModes : []).map(
      (mode) => text(record(mode)?.id),
    );
    const refused = new RuntimeActionError(
      'mode_unavailable',
      `${this.#name} could not start in the mode where it asks you before it acts, so the task did not start.`,
    );
    if (!available.includes(wanted)) throw refused;
    try {
      await rpc.request('session/set_mode', { sessionId, modeId: wanted }, HANDSHAKE_MS);
    } catch {
      throw refused;
    }
  }

  #requested(id: RequestId, method: string, params: unknown, connection: RpcConnection): void {
    const body = record(params);
    if (
      method !== 'session/request_permission' ||
      body === null ||
      body.sessionId !== this.#sessionId
    ) {
      // ACP's file and terminal requests are not offered (`clientCapabilities`), nor anything else.
      connection.respondError(id, -32601, 'Halora offers no such method.');
      return;
    }
    const toolCall = record(body.toolCall) ?? {};
    const options = (Array.isArray(body.options) ? body.options : []).map(record);
    const optionOf = (kind: string) => {
      const option = options.find((entry) => entry !== null && entry.kind === kind);
      const optionId = text(option?.optionId);
      return optionId === null || optionId === '' ? null : optionId;
    };
    if (this.#turn === null) {
      // Nobody can see a request outside a turn: it is withdrawn at once.
      connection.respond(id, { outcome: { outcome: 'cancelled' } });
      return;
    }
    this.#flushMessage();
    const toolCallId = text(toolCall.toolCallId);
    const approvalId = `${toolCallId ?? 'request'}:${String(id)}`.slice(0, 512);
    const allow = optionOf('allow_once');
    const approval: Approval = {
      id: approvalId,
      rpcId: id,
      toolCallId,
      allow,
      reject: optionOf('reject_once'),
      answered: null,
    };
    this.#approvals.set(approvalId, approval);
    if (toolCallId !== null)
      this.#toolUpdate({ ...toolCall, sessionUpdate: 'tool_call' }, 'tool_call');
    // The request's tool call is an update of the call the app already reported (ACP): what it left out, such as
    // Kiro's input, is what the app said of the same call before (validation/acp-agents.md).
    const whole = toolCallId === null ? toolCall : (this.#calls.get(toolCallId) ?? toolCall);
    const asked = approvalRequest(whole, this.#options.profile, this.#directory);
    this.#emit(
      'runtime.approval.requested',
      {
        approval_id: approvalId,
        subject: asked.subject,
        complete: asked.complete && allow !== null,
      },
      observed('session/request_permission'),
    );
  }

  #resolveApproval(approval: Approval, decision: 'approved' | 'denied', nativeType: string): void {
    const answered = approval.answered;
    if (answered === null) return;
    approval.answered = null;
    clearTimeout(answered.timer);
    this.#approvals.delete(approval.id);
    this.#emit(
      'runtime.approval.resolved',
      { approval_id: approval.id, decision },
      observed(nativeType),
    );
    answered.resolve();
  }

  #ended(turn: Turn, result: unknown): void {
    if (this.#turn !== turn) return;
    this.#flushMessage();
    this.#settleApprovals('The turn ended before the app took the answer.');
    this.#turn = null;
    const stopReason = text(record(result)?.stopReason) ?? 'unknown';
    const native = observed(`session/prompt.${stopReason}`);
    if (stopReason === 'end_turn') {
      this.#emit('runtime.turn.completed', { turn_id: turn.id }, native);
    } else if (stopReason === 'cancelled') {
      this.#emit('runtime.turn.interrupted', { turn_id: turn.id }, native);
    } else {
      const words =
        STOP_FAILURES[stopReason] ??
        `it ended the turn for a reason Halora does not know (${stopReason})`;
      this.#emit(
        'runtime.turn.failed',
        {
          turn_id: turn.id,
          error: { code: 'turn_stopped', message: `${this.#name} stopped: ${words}.` },
        },
        native,
      );
    }
    this.#settleInterrupt(turn, stopReason === 'cancelled');
  }

  #failed(turn: Turn, error: unknown): void {
    if (this.#turn !== turn) return;
    this.#flushMessage();
    this.#settleApprovals('The turn ended before the app took the answer.');
    this.#turn = null;
    if (error instanceof RpcError) {
      this.#emit(
        'runtime.turn.failed',
        {
          turn_id: turn.id,
          error: {
            code: error.code === AUTH_REQUIRED ? 'sign_in_required' : 'runtime_error',
            message: `${this.#name} failed the turn: ${error.message}`,
          },
        },
        observed('session/prompt.error'),
      );
    }
    // An unanswered prompt is the connection's end, reported as lost when the process exits.
    this.#settleInterrupt(turn, false);
  }

  #settleInterrupt(turn: Turn, cancelled: boolean): void {
    const interrupting = turn.interrupting;
    if (interrupting === null) return;
    turn.interrupting = null;
    clearTimeout(interrupting.timer);
    if (cancelled) interrupting.resolve();
    else {
      interrupting.reject(
        new RuntimeActionError(
          'interrupt_unconfirmed',
          `${this.#name} ended the turn another way than the cancel you asked for.`,
          'unknown',
        ),
      );
    }
  }

  #settleApprovals(reason: string): void {
    for (const approval of this.#approvals.values()) {
      const answered = approval.answered;
      if (answered !== null) {
        clearTimeout(answered.timer);
        answered.reject(new RuntimeActionError('approval_withdrawn', reason, 'unknown'));
      }
    }
    this.#approvals.clear();
  }

  #exited(): void {
    if (this.#profileFile !== null) rmSync(this.#profileFile, { force: true });
    if (this.#settingsHome !== null) rmSync(this.#settingsHome, { recursive: true, force: true });
    const wasAlive = this.#alive;
    this.#alive = false;
    const turn = this.#turn;
    this.#turn = null;
    this.#flushMessage();
    this.#settleApprovals(`${this.#name} stopped.`);
    if (turn !== null) this.#settleInterrupt(turn, false);
    if (this.#closing || !wasAlive) return;
    this.#emit(
      'runtime.connection.lost',
      {
        reason: `${this.#name} stopped, so this session can no longer be followed here.`,
      },
      observed('process.exit'),
    );
  }

  #flushMessage(): void {
    const message = this.#message;
    this.#message = '';
    if (message.trim() === '') return;
    this.#emit(
      'runtime.agent_message',
      { text: message },
      { epistemic: 'reported', native_type: 'agent_message_chunk' },
    );
  }

  #emit<T extends RuntimeObservation['type']>(
    type: T,
    payload: Extract<RuntimeObservation, { type: T }>['payload'],
    provenance: Provenance,
  ): void {
    const observation = {
      type,
      occurred_at: timestamp(this.#clock()),
      native_event_id: `${this.#launch}:${++this.#counter}`,
      sequence: null,
      provenance,
      payload,
    } as RuntimeObservation;
    try {
      this.#emitTo(observation);
    } catch {
      // The sink never throws back; one that does cannot stop the session.
    }
  }
}

/** Writes one launch's agent-app profile whole, replacing any older one of its name at once. */
function writeProfile(folder: string, name: string, scope: AgentAppScope): string {
  mkdirSync(folder, { recursive: true, mode: 0o700 });
  const path = join(folder, `${name}.sb`);
  const draft = `${path}.${process.pid}.${randomUUID()}`;
  writeFileSync(draft, agentAppProfile(scope), { mode: 0o600, flag: 'wx' });
  renameSync(draft, path);
  return path;
}
