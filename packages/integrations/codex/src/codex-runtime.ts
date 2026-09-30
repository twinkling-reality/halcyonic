import { tmpdir } from 'node:os';
import { isAbsolute } from 'node:path';
import type {
  ApprovalDecision,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeId,
  RuntimeModel,
  RuntimeOptions,
} from '@halcyonic/contracts';
import {
  type Clock,
  confirmProjectLocation,
  type DirectoryPolicy,
  type ExecutionContext,
  type ObservationSink,
  type OptionsValidation,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type StartExecutionRequest,
  type StartExecutionResult,
  systemClock,
} from '@halcyonic/runtime-core';
import {
  acceptTurnStart,
  createThreadState,
  isRecord,
  nativeType,
  type Observed,
  observation,
  observe,
  type PendingApproval,
  type ServerMessage,
  settleResumed,
  type ThreadState,
} from './events.ts';
import { modelsFromCodex } from './models.ts';
import { parseStartOptions, type StartOptions, toModelRef } from './options.ts';
import type {
  ApprovalDecision as CodexDecision,
  ConfigReadParams,
  ModelListParams,
  TextInput,
  ThreadConfigOverrides,
  ThreadResumeParams,
  ThreadStartParams,
  ThreadTurnsListParams,
  TurnInterruptParams,
  TurnStartParams,
  TurnSteerParams,
} from './protocol.ts';
import { RpcError, type RpcHandlers, RpcUnanswered } from './rpc.ts';
import {
  buildEnvironment,
  CODEX_VERSION,
  type CodexServer,
  describeExit,
  type ExitStatus,
  launchServer,
} from './server.ts';
import {
  readServerRecord,
  removeServerRecord,
  type StopOutcome,
  stopRecordedProcess,
} from './server-record.ts';

/** Verified against Codex 0.157.0's app-server, stable surface (ADR 0011). */
export const CODEX_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: true,
  respond_to_approval: true,
  interrupt: true,
};

/** Halcyonic's tag on every thread it starts, persisted by Codex as the thread's source. */
export const THREAD_SOURCE = 'halcyonic';

export interface CodexRuntimeOptions {
  /** Absolute path of the pinned Codex 0.157.0 native binary. It is never looked up on PATH. */
  readonly binaryPath: string;
  /**
   * File recording the pid and binary of the server while it runs, so a later start can stop a
   * server that outlived a crash. Use one file per runtime instance.
   */
  readonly serverRecordFile: string;
  /**
   * The host's decision on which directories agents may work in, asked again about the project's
   * folder before a thread starts there.
   */
  readonly directoryPolicy: DirectoryPolicy;
  readonly runtimeId?: RuntimeId;
  /**
   * Variables set on top of the inherited allowlist, for example provider credentials, or
   * temporary HOME, CODEX_HOME, XDG and TMPDIR directories in tests.
   */
  readonly env?: Readonly<Record<string, string>>;
  readonly startupTimeoutMs?: number;
  /** How long Codex has to answer a request, other than an interrupt. */
  readonly requestTimeoutMs?: number;
  /** How long Codex has to answer an interrupt; an unanswered one may still take effect. */
  readonly interruptTimeoutMs?: number;
  /** How long Codex has to take an approval decision once it was sent. */
  readonly approvalTimeoutMs?: number;
  readonly clock?: Clock;
}

export type StaleServerResult =
  | { readonly outcome: 'none' }
  | { readonly outcome: StopOutcome; readonly pid: number };

/** A launched server as the adapter uses it. */
interface Connection {
  readonly server: CodexServer;
  /** Aborted when the adapter stops using the server: it exited, was given up or closed. */
  readonly halted: AbortController;
  readonly launchedAt: number;
  /** Launched to replace a server that exited unexpectedly. */
  readonly relaunch: boolean;
}

interface Confirmation {
  readonly resolve: () => void;
  readonly reject: (error: RuntimeActionError) => void;
}

/** One execution: one Codex thread on the current server. */
interface HostedThread {
  readonly threadId: string;
  readonly execution: ExecutionContext;
  readonly emit: ObservationSink;
  readonly options: StartOptions;
  readonly state: ThreadState;
  connection: Connection;
  /** Waiters for Codex to take an approval decision, by approval id. */
  readonly confirmations: Map<string, Confirmation>;
  /** The last turn action; actions on a thread run one at a time. */
  queue: Promise<unknown>;
  /** Set while the thread is resumed on a relaunched server; actions wait for it. */
  recovering: Promise<void> | null;
  lost: boolean;
}

/** A relaunched server that exits again within this long is not relaunched again. */
const RELAUNCH_MIN_UPTIME_MS = 30_000;
/** Pages of `model/list` read at most; the catalog built into 0.157.0 fits in one. */
const MAX_MODEL_PAGES = 10;
/** Codex refuses a request as invalid with this JSON-RPC code; anything else may have acted. */
const INVALID_REQUEST = -32600;
const UNSUPPORTED_REQUEST = -32601;

/**
 * Runs Codex 0.157.0 threads through `codex app-server` over stdio, using only its stable API
 * surface (ADR 0011). The adapter owns one server process, launched lazily from a configured
 * binary in its own process group, with the developer's HOME and CODEX_HOME so Salidium and
 * Seorak can observe the threads. One execution is one thread, tagged with Halcyonic's client
 * name and thread source; its native id is the thread id.
 *
 * Known 0.157.0 behavior, handled rather than hidden:
 * - `turn/start` on a busy thread silently steers, so it is sent only at rest and a returned turn
 *   id equal to the running turn's counts as a steer; instructions while a turn runs use
 *   `turn/steer` with the running turn's id, and reach the model at its next request;
 * - an interrupt for a turn that already ended gets no answer, so only the turn known to be
 *   running is interrupted, and an unanswered interrupt is reported with an unknown effect;
 * - an interrupt ends the turn but leaves running commands running, so no command is reported
 *   stopped unless Codex reports it completed;
 * - the approval response has no field for a message, so a message given with a decision is not
 *   delivered: the model sees Codex's own text;
 * - commands run in sessions of their own, out of reach of a signal to the server's process
 *   group; Codex stops them when its input ends, which is how the server is stopped;
 * - a server that exits unexpectedly is relaunched and its threads are resumed, and a turn it was
 *   running is settled from Codex's own record; a thread another Codex process holds cannot be
 *   resumed. A relaunched server that exits again within 30 s is given up, and anything that
 *   cannot be settled is reported as a lost connection.
 */
export class CodexRuntimeAdapter implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  readonly #binaryPath: string;
  readonly #recordFile: string;
  readonly #directoryPolicy: DirectoryPolicy;
  readonly #environment: Readonly<Record<string, string>>;
  readonly #startupTimeoutMs: number;
  readonly #requestTimeoutMs: number;
  readonly #interruptTimeoutMs: number;
  readonly #approvalTimeoutMs: number;
  readonly #clock: Clock;
  readonly #threads = new Map<string, HostedThread>();
  readonly #byThreadId = new Map<string, HostedThread>();
  #current: Connection | null = null;
  #launching: Promise<Connection> | null = null;
  #closing: Promise<void> | null = null;

  constructor(options: CodexRuntimeOptions) {
    if (!isAbsolute(options.binaryPath)) {
      throw new Error(
        `The Codex binary path must be absolute, got "${options.binaryPath}": it is never looked up on PATH.`,
      );
    }
    this.#binaryPath = options.binaryPath;
    this.#recordFile = options.serverRecordFile;
    this.#directoryPolicy = options.directoryPolicy;
    this.#environment = buildEnvironment(process.env, options.env ?? {});
    this.#startupTimeoutMs = options.startupTimeoutMs ?? 20_000;
    this.#requestTimeoutMs = options.requestTimeoutMs ?? 20_000;
    this.#interruptTimeoutMs = options.interruptTimeoutMs ?? 10_000;
    this.#approvalTimeoutMs = options.approvalTimeoutMs ?? 10_000;
    this.#clock = options.clock ?? systemClock;
    this.descriptor = {
      runtime_id: options.runtimeId ?? ('codex' as RuntimeId),
      kind: 'codex',
      display_name: `Codex ${CODEX_VERSION}`,
      synthetic: false,
      capabilities: CODEX_CAPABILITIES,
      model_choice: 'listed',
      uses_project_location: true,
    };
  }

  /** Pid of the running server, for diagnostics. */
  get serverPid(): number | null {
    return this.#current?.server.pid ?? null;
  }

  validateStartOptions(options: RuntimeOptions, modelRef: string | null): OptionsValidation {
    const parsed = parseStartOptions(options, modelRef);
    return parsed.ok ? { ok: true } : { ok: false, message: parsed.message };
  }

  /**
   * The models Codex can run a thread on, from `config/read` and `model/list` (see
   * `modelsFromCodex`). Launches the server when none runs.
   */
  async listModels(): Promise<readonly RuntimeModel[]> {
    if (this.#closing !== null) throw closedError();
    const connection = await this.#connection();
    const params: ConfigReadParams = { includeLayers: false };
    const read = await send(connection, 'config/read', params, this.#requestTimeoutMs);
    const config = isRecord(read) && isRecord(read.config) ? read.config : null;
    if (config === null) {
      throw new RuntimeActionError(
        'runtime_protocol_error',
        'Codex answered config/read without a configuration.',
      );
    }
    const catalog: unknown[] = [];
    let cursor: string | null = null;
    for (let page = 0; page < MAX_MODEL_PAGES; page += 1) {
      const list: ModelListParams = { cursor, limit: 100, includeHidden: false };
      const result = await send(connection, 'model/list', list, this.#requestTimeoutMs);
      const data = isRecord(result) && Array.isArray(result.data) ? result.data : null;
      if (data === null) {
        throw new RuntimeActionError(
          'runtime_protocol_error',
          'Codex answered model/list without a list of models.',
        );
      }
      catalog.push(...data);
      cursor = isRecord(result) && typeof result.nextCursor === 'string' ? result.nextCursor : null;
      if (cursor === null) break;
    }
    return modelsFromCodex(config, catalog, this.#environment);
  }

  /**
   * Stops a server that an earlier run launched and that is still running, as recorded in the
   * server record file, when the process with that pid is still that server. It runs before every
   * launch; call it at startup too, so an orphaned server stops without waiting for the first
   * execution. It does nothing once this adapter has launched a server.
   */
  async stopStaleServer(): Promise<StaleServerResult> {
    if (this.#current !== null || this.#launching !== null || this.#closing !== null) {
      return { outcome: 'none' };
    }
    return this.#stopStale();
  }

  async startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    if (this.#closing !== null) throw closedError();
    const parsed = parseStartOptions(request.options, request.model_ref);
    if (!parsed.ok) throw new RuntimeActionError('invalid_runtime_options', parsed.message);
    // Asked again before anything is launched: the folder may have changed since admission.
    const cwd = confirmProjectLocation(this.#directoryPolicy, request.directory);
    if (this.#threads.has(request.execution.execution_id)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    const options: StartOptions = { ...parsed.value, cwd };
    // A chosen model is checked against a fresh list: Codex itself would take any name.
    if (request.model_ref !== null) {
      const listed = await this.listModels();
      if (!listed.some((model) => model.model_ref === request.model_ref)) {
        throw new RuntimeActionError(
          'model_unavailable',
          `Codex does not list the model ${request.model_ref}: it is neither the model its configuration names nor in the catalog of the configured provider.`,
        );
      }
    }
    const connection = await this.#connection();
    // Asked again right before the folder is handed over: listing and launching wait.
    confirmProjectLocation(this.#directoryPolicy, cwd);
    const params: ThreadStartParams = {
      ...threadSettings(options),
      threadSource: THREAD_SOURCE,
    };
    const reported = checkSettings(
      await send(connection, 'thread/start', params, this.#requestTimeoutMs),
      options,
    );
    const threadId = reported.threadId;
    const thread: HostedThread = {
      threadId,
      execution: request.execution,
      emit: request.emit,
      options,
      state: createThreadState(),
      connection,
      confirmations: new Map(),
      queue: Promise.resolve(),
      recovering: null,
      lost: false,
    };
    this.#threads.set(request.execution.execution_id, thread);
    this.#byThreadId.set(threadId, thread);
    thread.state.sequence += 1;
    this.#emit(thread, [
      observation(
        'runtime.execution.started',
        { native_id: threadId },
        {
          native_event_id: `${threadId}:thread/start`,
          sequence: thread.state.sequence,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: nativeType('thread/start') },
        },
      ),
    ]);
    this.#reportModel(thread, reported.provider, reported.model, 'thread/start');
    if (connection.halted.signal.aborted) {
      this.#lose(thread, 'The Codex server stopped while the execution was starting.');
    }
    await this.#act(thread, () => this.#startTurn(thread, request.instruction));
    return { native_id: threadId };
  }

  /** Starts a turn at rest, or steers the running turn. */
  async sendInstruction(request: { execution: ExecutionContext; text: string }): Promise<void> {
    const thread = this.#hosted(request.execution);
    await this.#act(thread, () =>
      thread.state.activeTurnId === null
        ? this.#startTurn(thread, request.text)
        : this.#steer(thread, request.text),
    );
  }

  /**
   * Answers an approval: approve as `accept`, deny as `decline`. Resolves once Codex has taken the
   * decision, which it confirms with `serverRequest/resolved`. The protocol has no field for a
   * message, so `message` is not delivered.
   */
  async respondToApproval(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
  }): Promise<void> {
    const thread = this.#hosted(request.execution);
    while (thread.recovering !== null) await thread.recovering;
    const approval = thread.state.approvals.get(request.approval_id);
    if (thread.lost) throw unreachableError();
    if (approval === undefined || approval.answer !== null) {
      throw new RuntimeActionError(
        'approval_not_pending',
        `Approval ${request.approval_id} is not pending.`,
      );
    }
    const rpc = thread.connection.server.rpc;
    if (!rpc.open) throw unreachableError();
    const confirmed = new Promise<void>((resolve, reject) => {
      thread.confirmations.set(approval.approvalId, { resolve, reject });
    });
    approval.answer = request.decision === 'approve' ? 'approved' : 'denied';
    const decision: CodexDecision = request.decision === 'approve' ? 'accept' : 'decline';
    rpc.respond(approval.requestId, { decision });
    const timer = setTimeout(
      () =>
        this.#confirm(thread, approval, false, 'Codex did not confirm that it took the decision.'),
      this.#approvalTimeoutMs,
    );
    try {
      await confirmed;
    } finally {
      clearTimeout(timer);
    }
  }

  /** Interrupts the turn known to be running. Resolves when Codex answers the interrupt. */
  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    const thread = this.#hosted(request.execution);
    await this.#act(thread, async () => {
      const turnId = thread.state.activeTurnId;
      if (turnId === null) throw noRunningTurn();
      const params: TurnInterruptParams = { threadId: thread.threadId, turnId };
      try {
        await thread.connection.server.rpc.request(
          'turn/interrupt',
          params,
          this.#interruptTimeoutMs,
        );
      } catch (error) {
        if (error instanceof RpcError && error.code === INVALID_REQUEST) throw noRunningTurn();
        if (error instanceof RpcUnanswered && error.reason === 'timeout') {
          throw new RuntimeActionError(
            'interrupt_unconfirmed',
            'Codex did not answer the interrupt. The turn may still stop.',
            'unknown',
          );
        }
        throw actionError(error);
      }
    });
  }

  close(): Promise<void> {
    this.#closing ??= this.#shutDown();
    return this.#closing;
  }

  async #shutDown(): Promise<void> {
    const stopping: CodexServer[] = [];
    const halt = () => {
      const connection = this.#current;
      if (connection === null) return;
      stopping.push(connection.server);
      this.#halt(connection);
    };
    halt();
    await this.#launching?.catch(() => undefined);
    halt();
    for (const thread of this.#threads.values()) this.#dropConfirmations(thread, closedError());
    await Promise.all(stopping.map((server) => server.stop()));
  }

  // Turns --------------------------------------------------------------------------------------

  async #startTurn(thread: HostedThread, text: string): Promise<void> {
    const running = thread.state.activeTurnId;
    const params: TurnStartParams = { threadId: thread.threadId, input: [textInput(text)] };
    const result = await send(thread.connection, 'turn/start', params, this.#requestTimeoutMs);
    const turnId = isRecord(result) && isRecord(result.turn) ? result.turn.id : undefined;
    if (typeof turnId !== 'string' || turnId === '') {
      throw new RuntimeActionError(
        'runtime_protocol_error',
        'Codex accepted the turn without identifying it.',
        'unknown',
      );
    }
    acceptTurnStart(thread.state, turnId, running);
  }

  async #steer(thread: HostedThread, text: string): Promise<void> {
    const expectedTurnId = thread.state.activeTurnId;
    if (expectedTurnId === null) throw noRunningTurn();
    const params: TurnSteerParams = {
      threadId: thread.threadId,
      expectedTurnId,
      input: [textInput(text)],
    };
    try {
      await thread.connection.server.rpc.request('turn/steer', params, this.#requestTimeoutMs);
    } catch (error) {
      if (error instanceof RpcError && error.code === INVALID_REQUEST) {
        throw new RuntimeActionError(
          'turn_not_steerable',
          `Codex did not take the instruction for the running turn: ${error.message}`,
        );
      }
      throw actionError(error);
    }
  }

  // Server lifecycle ---------------------------------------------------------------------------

  async #connection(relaunch = false): Promise<Connection> {
    if (this.#closing !== null) throw closedError();
    const current = this.#current;
    if (current !== null) return current;
    this.#launching ??= this.#launch(relaunch).finally(() => {
      this.#launching = null;
    });
    return this.#launching;
  }

  async #launch(relaunch: boolean): Promise<Connection> {
    try {
      await this.#stopStale();
    } catch (error) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        `Could not check for a Codex server left running by an earlier run: ${message(error)}`,
      );
    }
    let connection: Connection | null = null;
    const server = await launchServer({
      binaryPath: this.#binaryPath,
      environment: this.#environment,
      recordFile: this.#recordFile,
      cwd: tmpdir(),
      startupTimeoutMs: this.#startupTimeoutMs,
      handlers: this.#handlers(() => connection),
    });
    if (this.#closing !== null) {
      await server.stop();
      throw closedError();
    }
    connection = { server, halted: new AbortController(), launchedAt: Date.now(), relaunch };
    this.#current = connection;
    const launched = connection;
    void server.exited.then((status) => this.#onExit(launched, status));
    return connection;
  }

  async #stopStale(): Promise<StaleServerResult> {
    const record = await readServerRecord(this.#recordFile);
    if (record === null) return { outcome: 'none' };
    const outcome = await stopRecordedProcess(record);
    await removeServerRecord(this.#recordFile, record.pid);
    return { outcome, pid: record.pid };
  }

  #handlers(connection: () => Connection | null): RpcHandlers {
    const route = (params: unknown): HostedThread | null => {
      const threadId = isRecord(params) ? params.threadId : undefined;
      const thread = typeof threadId === 'string' ? this.#byThreadId.get(threadId) : undefined;
      const current = connection();
      return thread !== undefined &&
        current !== null &&
        thread.connection === current &&
        !thread.lost
        ? thread
        : null;
    };
    return {
      onNotification: (method, params, emittedAtMs) => {
        const thread = route(params);
        if (thread !== null) {
          this.#observe(thread, { kind: 'notification', method, params, emittedAtMs });
        }
      },
      onRequest: (id, method, params, rpc) => {
        const thread = route(params);
        const observed =
          thread === null ? null : this.#observe(thread, { kind: 'request', id, method, params });
        // Refused: a request Halcyonic does not show the person. Codex 0.157.0 takes an error
        // answer as a denial, an empty grant, a declined elicitation or an empty answer
        // (codex-rs/app-server/src/bespoke_event_handling.rs at rust-v0.157.0).
        if (observed?.requested === undefined) {
          rpc.respondError(
            id,
            UNSUPPORTED_REQUEST,
            `Halcyonic does not handle ${method} requests.`,
          );
        }
      },
    };
  }

  /** The server exited. Its threads are resumed on a relaunched server, or lost. */
  #onExit(connection: Connection, status: ExitStatus): void {
    if (connection.halted.signal.aborted) return;
    this.#halt(connection);
    const threads = [...this.#threads.values()].filter(
      (thread) => thread.connection === connection && !thread.lost,
    );
    const reason = `The Codex server exited unexpectedly (${describeExit(status)}).`;
    for (const thread of threads) {
      this.#dropConfirmations(
        thread,
        new RuntimeActionError(
          'runtime_unreachable',
          `${reason} Whether it took the decision is not known.`,
          'unknown',
        ),
      );
    }
    if (threads.length === 0) return;
    if (connection.relaunch && Date.now() - connection.launchedAt < RELAUNCH_MIN_UPTIME_MS) {
      for (const thread of threads) {
        this.#lose(
          thread,
          `${reason} It had been started again moments before, so it was given up.`,
        );
      }
      return;
    }
    void this.#recover(threads, reason);
  }

  /** Relaunches the server and resumes each thread on it, one at a time. */
  async #recover(threads: readonly HostedThread[], reason: string): Promise<void> {
    let done: () => void = () => undefined;
    const recovering = new Promise<void>((resolve) => {
      done = resolve;
    });
    for (const thread of threads) thread.recovering = recovering;
    try {
      let connection: Connection;
      try {
        connection = await this.#connection(true);
      } catch (error) {
        for (const thread of threads) {
          this.#lose(thread, `${reason} It could not be started again: ${message(error)}`);
        }
        return;
      }
      for (const thread of threads) await this.#resume(thread, connection, reason);
    } finally {
      for (const thread of threads) thread.recovering = null;
      done();
    }
  }

  async #resume(thread: HostedThread, connection: Connection, reason: string): Promise<void> {
    if (this.#closing !== null || thread.lost) return;
    thread.connection = connection;
    const lost = (detail: string) =>
      this.#lose(thread, `${reason} After it was started again, ${detail}`);
    try {
      // The folder is handed to Codex again on resume, so it is asked about again first.
      confirmProjectLocation(this.#directoryPolicy, thread.options.cwd);
      const params: ThreadResumeParams = {
        threadId: thread.threadId,
        ...threadSettings(thread.options),
        excludeTurns: true,
      };
      const reported = checkSettings(
        await send(connection, 'thread/resume', params, this.#requestTimeoutMs),
        thread.options,
      );
      this.#reportModel(thread, reported.provider, reported.model, 'thread/resume');
      const list: ThreadTurnsListParams = {
        threadId: thread.threadId,
        limit: 1,
        sortDirection: 'desc',
      };
      const page = await send(connection, 'thread/turns/list', list, this.#requestTimeoutMs);
      const latest = isRecord(page) && Array.isArray(page.data) ? page.data[0] : undefined;
      const result = settleResumed(thread.state, latest ?? null, this.#clock.now());
      this.#apply(thread, result);
      if (result.unsettled !== null) lost(result.unsettled);
    } catch (error) {
      if (error instanceof RuntimeActionError && /active writer/.test(error.message)) {
        lost('the thread could not be resumed because another Codex process holds it.');
        return;
      }
      lost(`the thread could not be resumed: ${message(error)}`);
    }
  }

  /** Stops using a server: its messages are ignored and actions on it are refused. */
  #halt(connection: Connection): void {
    connection.halted.abort();
    if (this.#current === connection) this.#current = null;
  }

  // Threads ------------------------------------------------------------------------------------

  #hosted(execution: ExecutionContext): HostedThread {
    if (this.#closing !== null) throw closedError();
    const thread = this.#threads.get(execution.execution_id);
    if (thread === undefined) {
      throw new RuntimeActionError(
        'execution_unknown_to_runtime',
        'This Codex runtime has no thread for the execution. Threads are not reattached after the control plane restarts.',
      );
    }
    if (thread.lost) throw unreachableError();
    return thread;
  }

  /** Runs a turn action once the thread's earlier actions and any recovery have finished. */
  #act(thread: HostedThread, action: () => Promise<void>): Promise<void> {
    const run = async () => {
      while (thread.recovering !== null) await thread.recovering;
      if (this.#closing !== null) throw closedError();
      if (thread.lost) throw unreachableError();
      await action();
    };
    const result = thread.queue.then(run, run);
    thread.queue = result.catch(() => undefined);
    return result;
  }

  #observe(thread: HostedThread, message: ServerMessage): Observed {
    const observed = observe(thread.state, message, this.#clock.now());
    this.#apply(thread, observed);
    return observed;
  }

  #apply(thread: HostedThread, observed: Observed): void {
    this.#emit(thread, observed.observations);
    for (const { approval, confirmed } of observed.settled) {
      this.#confirm(
        thread,
        approval,
        confirmed,
        'The turn ended before Codex confirmed that it took the decision.',
      );
    }
  }

  #confirm(thread: HostedThread, approval: PendingApproval, confirmed: boolean, why: string): void {
    const waiter = thread.confirmations.get(approval.approvalId);
    if (waiter === undefined) return;
    thread.confirmations.delete(approval.approvalId);
    if (confirmed) waiter.resolve();
    else waiter.reject(new RuntimeActionError('approval_unconfirmed', why, 'unknown'));
  }

  #dropConfirmations(thread: HostedThread, error: RuntimeActionError): void {
    for (const waiter of thread.confirmations.values()) waiter.reject(error);
    thread.confirmations.clear();
    thread.state.approvals.clear();
  }

  #emit(thread: HostedThread, observations: readonly RuntimeObservation[]): void {
    if (this.#closing !== null || thread.lost) return;
    for (const item of observations) {
      try {
        thread.emit(item);
      } catch {
        // A sink must not throw; one that does cannot stop the adapter observing.
      }
    }
  }

  /** Reports the model Codex says the thread runs on, when it is new for the thread. */
  #reportModel(
    thread: HostedThread,
    provider: string | null,
    model: string | null,
    method: string,
  ): void {
    if (provider === null || model === null) return;
    if (provider === thread.state.provider && model === thread.state.model) return;
    thread.state.provider = provider;
    thread.state.model = model;
    const modelRef = toModelRef(provider, model);
    if (!/^\S{1,256}$/.test(modelRef)) return;
    thread.state.sequence += 1;
    this.#emit(thread, [
      observation(
        'runtime.model.used',
        { model_ref: modelRef },
        {
          native_event_id: `${thread.threadId}:model:${thread.state.sequence}`,
          sequence: thread.state.sequence,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: nativeType(method) },
        },
      ),
    ]);
  }

  #lose(thread: HostedThread, reason: string): void {
    if (thread.lost) return;
    this.#dropConfirmations(thread, unreachableError());
    thread.state.sequence += 1;
    this.#emit(thread, [
      observation(
        'runtime.connection.lost',
        { reason },
        {
          native_event_id: null,
          sequence: thread.state.sequence,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: null },
        },
      ),
    ]);
    thread.lost = true;
  }
}

// Helpers ----------------------------------------------------------------------------------------

/** The settings every thread is started and resumed with. */
function threadSettings(options: StartOptions): Omit<ThreadStartParams, 'threadSource'> {
  const config: ThreadConfigOverrides = {
    ...(options.contextWindow !== undefined && { model_context_window: options.contextWindow }),
    ...(options.autoCompactTokenLimit !== undefined && {
      model_auto_compact_token_limit: options.autoCompactTokenLimit,
    }),
  };
  return {
    cwd: options.cwd,
    approvalPolicy: options.approvalPolicy,
    approvalsReviewer: 'user',
    sandbox: options.sandbox,
    ...(options.model !== undefined && { model: options.model }),
    ...(options.modelProvider !== undefined && { modelProvider: options.modelProvider }),
    ...(Object.keys(config).length > 0 && { config }),
  };
}

const SANDBOX_TYPES: Readonly<Record<StartOptions['sandbox'], string>> = {
  'read-only': 'readOnly',
  'workspace-write': 'workspaceWrite',
  'danger-full-access': 'dangerFullAccess',
};

/**
 * Checks that Codex gave a started or resumed thread the settings it was asked for, so that
 * nothing in the developer's configuration or managed requirements quietly stops approvals from
 * reaching the person, sends the thread to another model or provider than the one asked for
 * (a thread asked to stay on a local provider must not reach a hosted one), or has it work in
 * another folder than the project's. Returns the thread id with the model and provider Codex
 * reports for it.
 */
function checkSettings(
  result: unknown,
  options: StartOptions,
): { readonly threadId: string; readonly model: string | null; readonly provider: string | null } {
  const response = isRecord(result) ? result : {};
  const thread = isRecord(response.thread) ? response.thread : {};
  if (typeof thread.id !== 'string' || thread.id === '') {
    throw new RuntimeActionError(
      'runtime_protocol_error',
      'Codex answered without a thread id.',
      'unknown',
    );
  }
  if (response.cwd !== options.cwd) {
    throw new RuntimeActionError(
      'runtime_refused',
      `Codex reports the thread working in ${JSON.stringify(response.cwd)}, not in the project's folder ${options.cwd}. The thread is not used.`,
    );
  }
  const sandbox = isRecord(response.sandbox) ? response.sandbox.type : undefined;
  if (
    response.approvalPolicy !== options.approvalPolicy ||
    response.approvalsReviewer !== 'user' ||
    sandbox !== SANDBOX_TYPES[options.sandbox]
  ) {
    throw new RuntimeActionError(
      'runtime_refused',
      `Codex did not apply the requested settings: approval policy ${JSON.stringify(response.approvalPolicy)}, reviewer ${JSON.stringify(response.approvalsReviewer)}, sandbox ${JSON.stringify(sandbox)}. The thread is not used.`,
    );
  }
  const model = typeof response.model === 'string' && response.model !== '' ? response.model : null;
  const provider =
    typeof response.modelProvider === 'string' && response.modelProvider !== ''
      ? response.modelProvider
      : null;
  if (
    (options.model !== undefined && model !== options.model) ||
    (options.modelProvider !== undefined && provider !== options.modelProvider)
  ) {
    throw new RuntimeActionError(
      'runtime_refused',
      `Codex did not apply the requested model: it reports model ${JSON.stringify(model)} from provider ${JSON.stringify(provider)}. The thread is not used.`,
    );
  }
  return { threadId: thread.id, model, provider };
}

/** Sends a request and maps its failure to a `RuntimeActionError`. */
async function send(
  connection: Connection,
  method: string,
  params: unknown,
  timeoutMs: number,
): Promise<unknown> {
  if (connection.halted.signal.aborted) throw unreachableError();
  try {
    return await connection.server.rpc.request(method, params, timeoutMs);
  } catch (error) {
    throw actionError(error);
  }
}

function actionError(error: unknown): RuntimeActionError {
  if (error instanceof RuntimeActionError) return error;
  if (error instanceof RpcError) {
    return new RuntimeActionError(
      'runtime_refused',
      `Codex refused the request: ${error.message}`,
      error.code === INVALID_REQUEST ? 'none' : 'unknown',
    );
  }
  if (error instanceof RpcUnanswered) {
    return new RuntimeActionError(
      error.reason === 'timeout' ? 'runtime_timeout' : 'runtime_unreachable',
      error.message,
      error.reason === 'not_sent' ? 'none' : 'unknown',
    );
  }
  return new RuntimeActionError('runtime_error', message(error), 'unknown');
}

function textInput(text: string): TextInput {
  return { type: 'text', text, text_elements: [] };
}

function noRunningTurn(): RuntimeActionError {
  return new RuntimeActionError('no_running_turn', 'Codex has no running turn for this execution.');
}

function closedError(): RuntimeActionError {
  return new RuntimeActionError('runtime_closed', 'The Codex runtime is closed.');
}

function unreachableError(): RuntimeActionError {
  return new RuntimeActionError(
    'runtime_unreachable',
    'The Codex server of this execution is not reachable; the execution can no longer be observed or controlled.',
  );
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
