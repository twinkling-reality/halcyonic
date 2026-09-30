import { tmpdir } from 'node:os';
import { setTimeout as delay } from 'node:timers/promises';
import type {
  ApprovalDecision,
  Provenance,
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
import { type HttpResponse, type OpenCodeClient, TransportError } from './client.ts';
import {
  createSessionState,
  decodeEvent,
  isRecord,
  type OpenCodeEvent,
  observation,
  observeEvent,
  type SessionState,
  toTimestamp,
} from './events.ts';
import {
  type ModelRef,
  readDefaultModel,
  readModels,
  sameModel,
  toRuntimeModel,
} from './models.ts';
import { parseStartOptions } from './options.ts';
import { type PendingPermission, reconcileSession, type SessionSnapshot } from './reconcile.ts';
import {
  buildEnvironment,
  describeExit,
  type ExitStatus,
  launchServer,
  OPENCODE_VERSION,
  type OpenCodeServer,
  settlesWithin,
} from './server.ts';
import {
  readServerRecord,
  removeServerRecord,
  type StopOutcome,
  stopRecordedProcess,
} from './server-record.ts';
import { SseParser } from './sse.ts';

/**
 * Verified against OpenCode 2.0.18. Instructions while a turn runs use OpenCode's `steer`
 * delivery, which hands them to the model when the running step ends.
 */
export const OPENCODE_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: true,
  respond_to_approval: true,
  interrupt: true,
};

export interface OpenCodeRuntimeOptions {
  /** Absolute path of the pinned OpenCode 2.0.18 binary. It is never looked up on PATH. */
  readonly binaryPath: string;
  /**
   * File recording the pid, port and binary of the server while it runs, so a later start can
   * stop a server that outlived a crash. Use one file per runtime instance.
   */
  readonly serverRecordFile: string;
  /**
   * The host's decision on which directories agents may work in, asked again about the project's
   * folder before a session starts there.
   */
  readonly directoryPolicy: DirectoryPolicy;
  readonly runtimeId?: RuntimeId;
  /**
   * Variables set on top of the inherited allowlist, for example provider credentials, or
   * temporary HOME, XDG and TMPDIR directories in tests.
   */
  readonly env?: Readonly<Record<string, string>>;
  /** Port on 127.0.0.1. By default a free port is chosen at each launch. */
  readonly port?: number;
  readonly startupTimeoutMs?: number;
  /** Silence after which the event stream is treated as dead. OpenCode sends a heartbeat every 15 s. */
  readonly streamSilenceTimeoutMs?: number;
  /** Delays before successive reconnection attempts; when they run out the server is given up. */
  readonly reconnectDelaysMs?: readonly number[];
  /**
   * How long a start waits for OpenCode to offer the execution's model, which it may not list yet
   * right after it starts. By default 10 s.
   */
  readonly modelWaitMs?: number;
  readonly clock?: Clock;
}

export type StaleServerResult =
  | { readonly outcome: 'none' }
  | { readonly outcome: StopOutcome; readonly pid: number; readonly port: number };

interface Readiness {
  readonly promise: Promise<void>;
  settled: boolean;
  resolve(): void;
  reject(error: Error): void;
}

/** A launched server and the adapter's subscription to its events. */
interface Connection {
  readonly server: OpenCodeServer;
  /**
   * When the models of the server's own location were first asked for, by `Date.now()`. OpenCode
   * loads a location on its first request, and its list settles moments later.
   */
  listingSince: number | null;
  /** Aborted when the adapter stops using the server: it exited, was given up or closed. */
  readonly halted: AbortController;
  /** Settles when the event stream is connected; rejects when the server is given up. */
  readiness: Readiness;
}

interface HostedSession {
  readonly sessionId: string;
  readonly execution: ExecutionContext;
  readonly emit: ObservationSink;
  readonly connection: Connection;
  readonly state: SessionState;
  /** Requests sent for this session that OpenCode has not answered yet. */
  readonly inFlight: Set<Promise<unknown>>;
  /** Set while the session is reconciled; requests wait for it, so none races the snapshot. */
  reconciling: Promise<void> | null;
  lost: boolean;
}

const SESSION_CREATED: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.create',
};
const READ_TIMEOUT_MS = 5000;
/** How often a snapshot caught between two recorded states is read again before giving up. */
const TRANSITIONAL_READS = 20;
/** How often the list of models is read again while a start waits for its model. */
const MODEL_POLL_MS = 250;
/**
 * How long after its first request OpenCode's list of models for a location settles: its Ollama
 * plugin reads the server with 1 s timeouts, and the list held every discovered model about 1.1 s
 * after a location's first request.
 */
const LIST_SETTLES_AFTER_MS = 4000;

/**
 * Runs OpenCode 2.0.18 executions through its v2 server API. The adapter owns one server
 * process, launched lazily from a configured binary, bound to 127.0.0.1 and authenticated with a
 * generated password. One execution is one OpenCode session. Everything the adapter reports
 * comes from OpenCode's global event stream or, after a reconnect, from reading the session back.
 *
 * Known 2.0.18 behavior, handled rather than hidden:
 * - its list of models may precede the discovery of local models, per directory, so a start
 *   waits for its model to be listed;
 * - an instruction steered into a running turn reaches the model when the running step ends; one
 *   still waiting when the turn is interrupted stays in the session's inbox and reaches the model
 *   with the next instruction;
 * - interrupting removes pending permission requests without an event; the interrupted turn
 *   clears them, as every ended turn does;
 * - a message given with an approval decision is accepted and delivered nowhere (defect);
 * - events missed while the stream is disconnected are not replayed, so a reconnect re-reads each
 *   session and reports the connection lost for any session it cannot settle;
 * - a server that exits takes its sessions' executions with it: nothing resumes after a restart.
 */
export class OpenCodeRuntimeAdapter implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  readonly #binaryPath: string;
  readonly #recordFile: string;
  readonly #directoryPolicy: DirectoryPolicy;
  readonly #environment: Readonly<Record<string, string>>;
  readonly #port: number | null;
  readonly #startupTimeoutMs: number;
  readonly #silenceTimeoutMs: number;
  readonly #reconnectDelaysMs: readonly number[];
  readonly #modelWaitMs: number;
  readonly #clock: Clock;
  readonly #sessions = new Map<string, HostedSession>();
  readonly #bySessionId = new Map<string, HostedSession>();
  #current: Connection | null = null;
  #launching: Promise<Connection> | null = null;
  #closing: Promise<void> | null = null;

  constructor(options: OpenCodeRuntimeOptions) {
    this.#binaryPath = options.binaryPath;
    this.#recordFile = options.serverRecordFile;
    this.#directoryPolicy = options.directoryPolicy;
    this.#environment = buildEnvironment(process.env, options.env ?? {});
    this.#port = options.port ?? null;
    this.#startupTimeoutMs = options.startupTimeoutMs ?? 30_000;
    this.#silenceTimeoutMs = options.streamSilenceTimeoutMs ?? 45_000;
    this.#reconnectDelaysMs = options.reconnectDelaysMs ?? [100, 250, 500, 1000, 2000, 4000];
    this.#modelWaitMs = options.modelWaitMs ?? 10_000;
    this.#clock = options.clock ?? systemClock;
    this.descriptor = {
      runtime_id: options.runtimeId ?? ('opencode' as RuntimeId),
      kind: 'opencode',
      display_name: `OpenCode ${OPENCODE_VERSION}`,
      synthetic: false,
      capabilities: OPENCODE_CAPABILITIES,
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
   * The models OpenCode offers, from `GET /api/model` at the server's own location, field by
   * field: never `/api/provider`, and never a provider's settings, keys or headers. Launches the
   * server when none runs; the first listing on a server waits for OpenCode to load the location
   * and discover local models.
   */
  async listModels(): Promise<readonly RuntimeModel[]> {
    const connection = await this.#connection();
    const read = async () => {
      if (connection.halted.signal.aborted) throw unreachableError();
      try {
        return await readModels(connection.server.client, null, READ_TIMEOUT_MS);
      } catch (error) {
        throw new RuntimeActionError(
          'runtime_unavailable',
          `OpenCode's list of models could not be read: ${message(error)}`,
        );
      }
    };
    if (connection.listingSince === null) {
      connection.listingSince = Date.now();
      // The first request makes OpenCode load the location; what it answers is not settled yet.
      await read();
    }
    const settling = connection.listingSince + LIST_SETTLES_AFTER_MS - Date.now();
    if (settling > 0) await delay(settling);
    const listed = await read();
    const models = new Map<string, RuntimeModel>();
    for (const entry of listed) {
      const model = toRuntimeModel(entry);
      // A reference the contract cannot carry is not offered.
      if (/^\S{1,256}$/.test(model.model_ref) && !models.has(model.model_ref)) {
        models.set(model.model_ref, model);
      }
    }
    return [...models.values()];
  }

  /**
   * Stops a server that an earlier run launched and that is still running, as recorded in the
   * server record file, when the process with that pid is still that server. It runs before
   * every launch; call it at startup too, so an orphaned server stops without waiting for the
   * first execution. It does nothing once this adapter has launched a server.
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
    // Asked again before the server is launched: the folder may have changed since admission.
    // OpenCode 2.0.18 creates a session in a directory that does not exist without complaint (and
    // then answers 500 on some routes), so the policy's check that it exists matters here too.
    const directory = confirmProjectLocation(this.#directoryPolicy, request.directory);
    if (this.#sessions.has(request.execution.execution_id)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    const connection = await this.#connection();
    // Asked again before each model read that carries the folder (in #awaitModel), and right
    // before the session is made there: launching and waiting take seconds.
    await this.#awaitModel(connection, directory, parsed.value.model);
    confirmProjectLocation(this.#directoryPolicy, directory);
    const body: Record<string, unknown> = {
      title: `Halcyonic execution ${request.execution.execution_id}`,
      // The policy's real path. It also avoids the odd relative subpath OpenCode computes for a
      // directory reached through a symbolic link.
      location: { directory },
    };
    if (parsed.value.model !== null) body.model = parsed.value.model;
    const created = await send(connection, 'POST', '/api/session', body, 'runtime_refused');
    const info = isRecord(created.body) && isRecord(created.body.data) ? created.body.data : {};
    if (typeof info.id !== 'string' || info.id === '') {
      throw new RuntimeActionError(
        'runtime_protocol_error',
        'OpenCode created a session without returning its id.',
        'unknown',
      );
    }
    const session: HostedSession = {
      sessionId: info.id,
      execution: request.execution,
      emit: request.emit,
      connection,
      state: createSessionState(),
      inFlight: new Set(),
      reconciling: null,
      lost: false,
    };
    this.#sessions.set(request.execution.execution_id, session);
    this.#bySessionId.set(session.sessionId, session);
    const createdAt = isRecord(info.time) ? info.time.created : null;
    this.#emit(session, [
      observation(
        'runtime.execution.started',
        { native_id: session.sessionId },
        {
          native_event_id: null,
          sequence: null,
          occurred_at: toTimestamp(createdAt, this.#clock.now()),
          provenance: SESSION_CREATED,
        },
      ),
    ]);
    if (connection.halted.signal.aborted) {
      this.#lose(session, 'The OpenCode server stopped while the execution was starting.');
    }
    await this.#prompt(session, request.instruction);
    return { native_id: session.sessionId };
  }

  async sendInstruction(request: { execution: ExecutionContext; text: string }): Promise<void> {
    const session = this.#hosted(request.execution);
    // Its first event should be observed, so the turn starts on a connected stream.
    await ready(session.connection);
    await this.#prompt(session, request.text);
  }

  async respondToApproval(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
  }): Promise<void> {
    const session = this.#hosted(request.execution);
    const path = `/api/session/${encodeURIComponent(session.sessionId)}/permission/${encodeURIComponent(request.approval_id)}/reply`;
    // Approval is "once": "always" would save a rule that outlives this execution. OpenCode
    // 2.0.18 accepts a message with either decision and delivers it nowhere; it is sent anyway.
    const body: Record<string, unknown> = {
      decision: request.decision === 'approve' ? 'once' : 'reject',
    };
    if (request.message !== null) body.message = request.message;
    await this.#act(session, async () => {
      await send(session.connection, 'POST', path, body, 'approval_not_pending');
      if (session.state.approvals.has(request.approval_id)) {
        session.state.replies.set(
          request.approval_id,
          request.decision === 'approve' ? 'approved' : 'denied',
        );
      }
    });
  }

  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    const session = this.#hosted(request.execution);
    const path = `/api/session/${encodeURIComponent(session.sessionId)}/interrupt`;
    const response = await this.#act(session, () =>
      send(session.connection, 'POST', path, undefined, 'execution_unknown_to_runtime'),
    );
    const interrupted = isRecord(response.body) ? response.body.interrupted : undefined;
    if (interrupted === true) return;
    if (interrupted === false) {
      throw new RuntimeActionError('no_running_turn', 'OpenCode had no running turn to interrupt.');
    }
    throw new RuntimeActionError(
      'runtime_protocol_error',
      'OpenCode answered the interrupt without saying whether it stopped anything.',
      'unknown',
    );
  }

  close(): Promise<void> {
    this.#closing ??= this.#shutDown();
    return this.#closing;
  }

  async #shutDown(): Promise<void> {
    // Halting first also releases a launch that is still waiting for its event stream.
    const stopping: OpenCodeServer[] = [];
    const halt = () => {
      const connection = this.#current;
      if (connection === null) return;
      stopping.push(connection.server);
      this.#halt(connection);
    };
    halt();
    await this.#launching?.catch(() => undefined);
    halt();
    await Promise.all(stopping.map((server) => server.stop()));
  }

  // Server lifecycle ---------------------------------------------------------------------------

  async #connection(): Promise<Connection> {
    if (this.#closing !== null) throw closedError();
    const current = this.#current;
    if (current !== null) {
      await ready(current);
      return current;
    }
    this.#launching ??= this.#launch().finally(() => {
      this.#launching = null;
    });
    return this.#launching;
  }

  async #launch(): Promise<Connection> {
    try {
      await this.#stopStale();
    } catch (error) {
      throw new RuntimeActionError(
        'runtime_unavailable',
        `Could not check for an OpenCode server left running by an earlier run: ${message(error)}`,
      );
    }
    const server = await launchServer({
      binaryPath: this.#binaryPath,
      environment: this.#environment,
      recordFile: this.#recordFile,
      port: this.#port,
      cwd: tmpdir(),
      startupTimeoutMs: this.#startupTimeoutMs,
    });
    if (this.#closing !== null) {
      await server.stop();
      throw closedError();
    }
    const connection: Connection = {
      server,
      listingSince: null,
      halted: new AbortController(),
      readiness: readiness(),
    };
    this.#current = connection;
    void server.exited.then((status) => this.#onExit(connection, status));
    void this.#follow(connection);
    await ready(connection);
    return connection;
  }

  async #stopStale(): Promise<StaleServerResult> {
    const record = await readServerRecord(this.#recordFile);
    if (record === null) return { outcome: 'none' };
    const outcome = await stopRecordedProcess(record);
    await removeServerRecord(this.#recordFile, record.pid);
    return { outcome, pid: record.pid, port: record.port };
  }

  /** The server died. Its sessions go with it: OpenCode 2.0.18 recovers nothing after a restart. */
  #onExit(connection: Connection, status: ExitStatus): void {
    if (connection.halted.signal.aborted) return;
    this.#halt(connection);
    this.#loseAll(
      connection,
      `The OpenCode server exited unexpectedly (${describeExit(status)}). OpenCode does not resume sessions after a restart, so this execution can no longer be observed or controlled.`,
    );
  }

  /** Stops using a server: no more events, no more reconnection, actions refused. */
  #halt(connection: Connection): void {
    connection.halted.abort();
    connection.readiness.reject(unreachableError());
    if (this.#current === connection) this.#current = null;
  }

  async #giveUp(connection: Connection, reason: string): Promise<void> {
    if (connection.halted.signal.aborted) return;
    this.#halt(connection);
    this.#loseAll(connection, `${reason} The OpenCode server was stopped.`);
    await connection.server.stop();
  }

  // Event stream -------------------------------------------------------------------------------

  /** Keeps the connection's event stream open, reconnecting and reconciling after a drop. */
  async #follow(connection: Connection): Promise<void> {
    let attempt = 0;
    let reconnecting = false;
    while (!connection.halted.signal.aborted) {
      const connected = await this.#readStream(connection, reconnecting);
      if (connection.halted.signal.aborted) return;
      if (!connected && !reconnecting) {
        await this.#giveUp(connection, 'The OpenCode event stream could not be opened.');
        return;
      }
      if (connected) attempt = 0;
      reconnecting = true;
      if (connection.readiness.settled) connection.readiness = readiness();
      const wait = this.#reconnectDelaysMs[attempt];
      attempt += 1;
      if (wait === undefined) {
        await this.#giveUp(
          connection,
          'The OpenCode event stream dropped and could not be reopened, so events were missed.',
        );
        return;
      }
      await delay(wait, undefined, { signal: connection.halted.signal }).catch(() => undefined);
    }
  }

  /**
   * Reads the stream until it ends, fails or stays silent too long. After a reconnect, sessions
   * are reconciled before any new event is applied; events keep arriving meanwhile and are read
   * afterwards, in order. Resolves whether the stream got connected.
   */
  async #readStream(connection: Connection, reconcile: boolean): Promise<boolean> {
    const stream = new AbortController();
    const stop = () => stream.abort();
    connection.halted.signal.addEventListener('abort', stop, { once: true });
    let timer: NodeJS.Timeout | undefined;
    const arm = () => {
      clearTimeout(timer);
      timer = setTimeout(stop, this.#silenceTimeoutMs);
    };
    let connected = false;
    arm();
    try {
      const body = await connection.server.client.events(stream.signal);
      const parser = new SseParser();
      for await (const chunk of body) {
        arm();
        for (const message of parser.push(chunk)) {
          if (message.kind !== 'data') continue;
          const event = decodeEvent(message.data);
          if (event === null) continue;
          if (event.type !== 'server.connected') {
            this.#dispatch(event);
            continue;
          }
          if (connected) continue;
          connected = true;
          if (reconcile) {
            clearTimeout(timer);
            await this.#reconcileAll(connection);
            arm();
          }
          connection.readiness.resolve();
        }
      }
    } catch {
      // Ended by silence, by the server going away, or by halting; the caller decides what next.
    } finally {
      clearTimeout(timer);
      connection.halted.signal.removeEventListener('abort', stop);
    }
    return connected;
  }

  #dispatch(event: OpenCodeEvent): void {
    if (event.sessionId === null) return;
    const session = this.#bySessionId.get(event.sessionId);
    if (session === undefined || session.lost) return;
    this.#emit(session, observeEvent(session.state, event, this.#clock.now()));
  }

  async #reconcileAll(connection: Connection): Promise<void> {
    const sessions = [...this.#bySessionId.values()].filter(
      (session) => session.connection === connection && !session.lost,
    );
    for (const session of sessions) {
      if (connection.halted.signal.aborted) return;
      if (!session.lost) await this.#reconcile(session);
    }
  }

  /**
   * Re-reads one session and reports what changed while the stream was down. Requests for the
   * session wait meanwhile, and answers still on their way are awaited first, so the state knows,
   * for example, whether its last prompt was accepted.
   */
  async #reconcile(session: HostedSession): Promise<void> {
    let release: () => void = () => undefined;
    session.reconciling = new Promise<void>((resolve) => {
      release = resolve;
    });
    const lost = (reason: string) =>
      this.#lose(session, `After the OpenCode event stream reconnected, ${reason}`);
    try {
      const answered = Promise.allSettled([...session.inFlight]);
      if (!(await settlesWithin(answered, READ_TIMEOUT_MS))) {
        lost('a request for this session was still unanswered.');
        return;
      }
      for (let reads = 1; ; reads += 1) {
        const snapshot = await readSnapshot(session.connection.server.client, session);
        if (session.connection.halted.signal.aborted || session.lost) return;
        const result = reconcileSession(session.state, snapshot, this.#clock.now());
        if (result.kind === 'settled') {
          this.#emit(session, result.observations);
          return;
        }
        if (result.kind === 'unsettled') {
          this.#lose(session, result.reason);
          return;
        }
        if (reads >= TRANSITIONAL_READS) {
          lost('the session did not settle into a recorded state.');
          return;
        }
        await delay(50);
      }
    } catch (error) {
      lost(`the session could not be read (${message(error)}).`);
    } finally {
      session.reconciling = null;
      release();
    }
  }

  // Sessions -----------------------------------------------------------------------------------

  /**
   * Waits until OpenCode offers the execution's model in its directory. OpenCode discovers the
   * models of local servers such as Ollama shortly after it starts, per directory, and again every
   * 30 s; until then its list lacks them, and a session prompted with such a model fails with
   * `provider.no-route`. A named model still missing after the wait is refused in words, before
   * any session exists. Without a named model, the wait is for OpenCode to have a default, which
   * it then chooses itself.
   */
  async #awaitModel(
    connection: Connection,
    directory: string,
    model: ModelRef | null,
  ): Promise<void> {
    const client = connection.server.client;
    const deadline = Date.now() + this.#modelWaitMs;
    for (;;) {
      if (connection.halted.signal.aborted) throw unreachableError();
      // Each read sends the folder to OpenCode, which reads that folder's own configuration.
      confirmProjectLocation(this.#directoryPolicy, directory);
      let found: boolean;
      try {
        found =
          model === null
            ? (await readDefaultModel(client, directory, READ_TIMEOUT_MS)) !== null
            : (await readModels(client, directory, READ_TIMEOUT_MS)).some((entry) =>
                sameModel(entry, model),
              );
      } catch (error) {
        throw new RuntimeActionError(
          'runtime_unavailable',
          `OpenCode's list of models could not be read: ${message(error)}`,
        );
      }
      if (found) return;
      if (Date.now() >= deadline) {
        if (model === null) return;
        throw new RuntimeActionError(
          'model_unavailable',
          `OpenCode does not offer the model ${model.providerID}/${model.id} in ${directory}. It lists the models its providers offer there; a local server's models appear once OpenCode has reached the server, and a model the configuration disables does not appear.`,
        );
      }
      await delay(MODEL_POLL_MS);
    }
  }

  #hosted(execution: ExecutionContext): HostedSession {
    if (this.#closing !== null) throw closedError();
    const session = this.#sessions.get(execution.execution_id);
    if (session === undefined) {
      throw new RuntimeActionError(
        'execution_unknown_to_runtime',
        'This OpenCode runtime has no session for the execution. Sessions do not survive a restart of the control plane or of OpenCode.',
      );
    }
    if (session.lost) throw unreachableError();
    return session;
  }

  /**
   * Sends a request for a session once no reconciliation of it is under way, and tracks it until
   * OpenCode answers. The request starts in the same tick as the check, so none slips in between.
   */
  async #act<T>(session: HostedSession, run: () => Promise<T>): Promise<T> {
    while (session.reconciling !== null) await session.reconciling;
    if (session.lost) throw unreachableError();
    const request = run();
    session.inFlight.add(request);
    try {
      return await request;
    } finally {
      session.inFlight.delete(request);
    }
  }

  /**
   * Starts a turn at rest, or steers the running one. OpenCode names the prompt's inbox item,
   * which traces the start of a turn at rest. A steered instruction waits in the inbox until the
   * running step ends and is then delivered into the same turn.
   */
  async #prompt(session: HostedSession, text: string): Promise<void> {
    const state = session.state;
    await this.#act(session, async () => {
      if (state.turn !== null || state.awaitingStart) {
        await this.#steer(session, text);
        return;
      }
      // Set before sending: the execution can start before the response arrives.
      state.awaitingStart = true;
      let response: HttpResponse;
      try {
        response = await send(
          session.connection,
          'POST',
          `/api/session/${encodeURIComponent(session.sessionId)}/prompt`,
          { text },
          'execution_unknown_to_runtime',
        );
      } catch (error) {
        state.awaitingStart = false;
        throw error;
      }
      const item =
        isRecord(response.body) && isRecord(response.body.data) ? response.body.data : {};
      if (typeof item.id !== 'string') {
        state.awaitingStart = false;
        throw new RuntimeActionError(
          'runtime_protocol_error',
          'OpenCode accepted the prompt without identifying it.',
          'unknown',
        );
      }
      if (state.awaitingStart) {
        state.pendingInboxId = item.id;
        const enqueuedAt = isRecord(item.time) ? item.time.created : null;
        if (typeof enqueuedAt === 'number') state.since = enqueuedAt;
      }
    });
  }

  /**
   * Delivers an instruction into the running turn with OpenCode's `steer` delivery. OpenCode
   * accepts it into the session's inbox and hands it to the model when the running step ends,
   * never cutting a model stream. Resolves once OpenCode has accepted it.
   */
  async #steer(session: HostedSession, text: string): Promise<void> {
    const response = await send(
      session.connection,
      'POST',
      `/api/session/${encodeURIComponent(session.sessionId)}/prompt`,
      { text, delivery: 'steer' },
      'execution_unknown_to_runtime',
    );
    const item = isRecord(response.body) && isRecord(response.body.data) ? response.body.data : {};
    if (typeof item.id !== 'string') {
      throw new RuntimeActionError(
        'runtime_protocol_error',
        'OpenCode accepted the instruction without identifying it.',
        'unknown',
      );
    }
    session.state.steered.add(item.id);
  }

  #emit(session: HostedSession, observations: readonly RuntimeObservation[]): void {
    if (this.#closing !== null || session.lost) return;
    for (const item of observations) {
      try {
        session.emit(item);
      } catch {
        // A sink must not throw; one that does cannot stop the adapter observing.
      }
    }
  }

  #lose(session: HostedSession, reason: string): void {
    if (session.lost) return;
    this.#emit(session, [
      observation(
        'runtime.connection.lost',
        { reason },
        {
          native_event_id: null,
          sequence: null,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: null },
        },
      ),
    ]);
    session.lost = true;
  }

  #loseAll(connection: Connection, reason: string): void {
    for (const session of this.#bySessionId.values()) {
      if (session.connection === connection) this.#lose(session, reason);
    }
  }
}

// Helpers ----------------------------------------------------------------------------------------

async function send(
  connection: Connection,
  method: string,
  path: string,
  body: unknown,
  notFoundCode: string,
): Promise<HttpResponse> {
  if (connection.halted.signal.aborted) throw unreachableError();
  let response: HttpResponse;
  try {
    response = await connection.server.client.request(
      method,
      path,
      body === undefined ? {} : { body },
    );
  } catch (error) {
    if (!(error instanceof TransportError)) throw error;
    throw new RuntimeActionError(
      'runtime_unreachable',
      `OpenCode could not be reached: ${error.message}`,
      error.refused ? 'none' : 'unknown',
    );
  }
  if (response.status >= 200 && response.status < 300) return response;
  const details = isRecord(response.body) ? response.body : {};
  const tag = typeof details._tag === 'string' ? ` ${details._tag}` : '';
  const text = typeof details.message === 'string' ? `: ${details.message}` : '';
  const description = `OpenCode answered ${response.status}${tag}${text}`;
  if (response.status === 404) throw new RuntimeActionError(notFoundCode, description);
  // A server error may have happened after the request took effect.
  if (response.status >= 500) {
    throw new RuntimeActionError('runtime_error', description, 'unknown');
  }
  throw new RuntimeActionError('runtime_refused', description);
}

async function ready(connection: Connection): Promise<void> {
  try {
    await connection.readiness.promise;
  } catch {
    throw unreachableError();
  }
}

function readiness(): Readiness {
  let resolve: () => void = () => undefined;
  let reject: (error: Error) => void = () => undefined;
  const promise = new Promise<void>((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  promise.catch(() => undefined);
  const state: Readiness = {
    promise,
    settled: false,
    resolve: () => {
      state.settled = true;
      resolve();
    },
    reject: (error) => {
      state.settled = true;
      reject(error);
    },
  };
  return state;
}

/** Reads a session in the order `SessionSnapshot` documents: each read can only be newer. */
async function readSnapshot(
  client: OpenCodeClient,
  session: HostedSession,
): Promise<SessionSnapshot> {
  const base = `/api/session/${encodeURIComponent(session.sessionId)}`;
  const inbox = new Set<string>();
  if (session.state.awaitingStart) {
    const items = await readData(client, `${base}/inbox`);
    if (!Array.isArray(items)) throw new Error('the inbox had an unexpected shape');
    for (const item of items) {
      if (isRecord(item) && typeof item.id === 'string') inbox.add(item.id);
    }
  }
  const permissions = await readData(client, `${base}/permission`);
  const active = await readData(client, '/api/session/active');
  const info = await readData(client, base);
  if (!Array.isArray(permissions) || !isRecord(active) || !isRecord(info)) {
    throw new Error('the session, its permissions or the running sessions had an unexpected shape');
  }
  const running = Object.hasOwn(active, session.sessionId);
  const toolStatus = new Map<string, string>();
  if (running && session.state.tools.size > 0) {
    const messages = await readData(client, `${base}/message`);
    for (const item of Array.isArray(messages) ? messages : []) {
      if (!isRecord(item) || item.type !== 'assistant' || !Array.isArray(item.content)) continue;
      for (const part of item.content) {
        if (!isRecord(part) || part.type !== 'tool' || typeof part.id !== 'string') continue;
        if (isRecord(part.state) && typeof part.state.status === 'string') {
          toolStatus.set(part.id, part.state.status);
        }
      }
    }
  }
  const pending: PendingPermission[] = [];
  for (const item of permissions) {
    if (!isRecord(item) || typeof item.id !== 'string') continue;
    pending.push({ id: item.id, action: item.action, resources: item.resources });
  }
  return {
    inbox,
    permissions: pending,
    running,
    outcome: typeof info.outcome === 'string' ? info.outcome : null,
    idleAt: isRecord(info.time) && typeof info.time.idle === 'number' ? info.time.idle : null,
    toolStatus,
  };
}

async function readData(client: OpenCodeClient, path: string): Promise<unknown> {
  const response = await client.request('GET', path, { timeoutMs: READ_TIMEOUT_MS });
  if (response.status !== 200 || !isRecord(response.body) || !('data' in response.body)) {
    throw new Error(`GET ${path} answered ${response.status}`);
  }
  return response.body.data;
}

function closedError(): RuntimeActionError {
  return new RuntimeActionError('runtime_closed', 'The OpenCode runtime is closed.');
}

function unreachableError(): RuntimeActionError {
  return new RuntimeActionError(
    'runtime_unreachable',
    'The OpenCode server of this execution is not reachable; the execution can no longer be observed or controlled.',
  );
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
