import { tmpdir } from 'node:os';
import { setTimeout as delay } from 'node:timers/promises';
import type {
  ApprovalDecision,
  Provenance,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeId,
  RuntimeOptions,
} from '@halcyonic/contracts';
import {
  type Clock,
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
import { parseStartOptions } from './options.ts';
import { type PendingPermission, reconcileSession, type SessionSnapshot } from './reconcile.ts';
import {
  buildEnvironment,
  describeExit,
  type ExitStatus,
  launchServer,
  OPENCODE_VERSION,
  type OpenCodeServer,
} from './server.ts';
import {
  readServerRecord,
  removeServerRecord,
  type StopOutcome,
  stopRecordedProcess,
} from './server-record.ts';
import { SseParser } from './sse.ts';

/**
 * Verified against OpenCode 2.0.18. Instructions while a turn runs are not offered: OpenCode's
 * `steer` and `queue` deliveries exist but this adapter does not implement or test them.
 */
export const OPENCODE_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
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
   * The host's decision on which directories agents may work in. A directory it refuses is
   * refused as a start option, and a session runs in the real path it returns.
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
  lost: boolean;
}

const SESSION_CREATED: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.create',
};
const READ_TIMEOUT_MS = 5000;

/**
 * Runs OpenCode 2.0.18 executions through its v2 server API. The adapter owns one server
 * process, launched lazily from a configured binary, bound to 127.0.0.1 and authenticated with a
 * generated password. One execution is one OpenCode session. Everything the adapter reports
 * comes from OpenCode's global event stream or, after a reconnect, from reading the session back.
 *
 * Known 2.0.18 behavior, handled rather than hidden:
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
    this.#clock = options.clock ?? systemClock;
    this.descriptor = {
      runtime_id: options.runtimeId ?? ('opencode' as RuntimeId),
      kind: 'opencode',
      display_name: `OpenCode ${OPENCODE_VERSION}`,
      synthetic: false,
      capabilities: OPENCODE_CAPABILITIES,
    };
  }

  /** Pid of the running server, for diagnostics. */
  get serverPid(): number | null {
    return this.#current?.server.pid ?? null;
  }

  validateStartOptions(options: RuntimeOptions): OptionsValidation {
    const parsed = parseStartOptions(options, this.#directoryPolicy);
    return parsed.ok ? { ok: true } : { ok: false, message: parsed.message };
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
    // Checked again here: the directory may have changed since admission.
    const parsed = parseStartOptions(request.options, this.#directoryPolicy);
    if (!parsed.ok) throw new RuntimeActionError('invalid_runtime_options', parsed.message);
    if (this.#sessions.has(request.execution.execution_id)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    const connection = await this.#connection();
    const body: Record<string, unknown> = {
      title: `Halcyonic execution ${request.execution.execution_id}`,
      // The policy's real path. It also avoids the odd relative subpath OpenCode computes for a
      // directory reached through a symbolic link.
      location: { directory: parsed.value.directory },
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
    // Its first event must be observed, so the turn starts only on a connected stream.
    await ready(session.connection);
    if (session.lost) throw unreachableError();
    if (session.state.turn !== null || session.state.awaitingStart) {
      throw new RuntimeActionError(
        'turn_in_progress',
        'OpenCode is running a turn for this execution; instructions are delivered only between turns.',
      );
    }
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
    await send(session.connection, 'POST', path, body, 'approval_not_pending');
    if (session.state.approvals.has(request.approval_id)) {
      session.state.replies.set(
        request.approval_id,
        request.decision === 'approve' ? 'approved' : 'denied',
      );
    }
  }

  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    const session = this.#hosted(request.execution);
    const path = `/api/session/${encodeURIComponent(session.sessionId)}/interrupt`;
    const response = await send(
      session.connection,
      'POST',
      path,
      undefined,
      'execution_unknown_to_runtime',
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
    if (sessions.length === 0) return;
    const client = connection.server.client;
    let active: ReadonlySet<string>;
    try {
      active = await readActiveSessions(client);
    } catch (error) {
      for (const session of sessions) {
        this.#lose(
          session,
          `After the OpenCode event stream reconnected, the running sessions could not be read (${message(error)}).`,
        );
      }
      return;
    }
    for (const session of sessions) {
      if (connection.halted.signal.aborted || session.lost) continue;
      try {
        const snapshot = await readSnapshot(client, session, active.has(session.sessionId));
        if (connection.halted.signal.aborted || session.lost) continue;
        const result = reconcileSession(session.state, snapshot, this.#clock.now());
        if (result.ok) this.#emit(session, result.observations);
        else this.#lose(session, result.reason);
      } catch (error) {
        this.#lose(
          session,
          `After the OpenCode event stream reconnected, the session could not be read (${message(error)}).`,
        );
      }
    }
  }

  // Sessions -----------------------------------------------------------------------------------

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

  async #prompt(session: HostedSession, text: string): Promise<void> {
    // Set before sending: the execution can start before the response arrives.
    session.state.awaitingStart = true;
    try {
      await send(
        session.connection,
        'POST',
        `/api/session/${encodeURIComponent(session.sessionId)}/prompt`,
        { text },
        'execution_unknown_to_runtime',
      );
    } catch (error) {
      session.state.awaitingStart = false;
      throw error;
    }
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

async function readActiveSessions(client: OpenCodeClient): Promise<ReadonlySet<string>> {
  const data = await readData(client, '/api/session/active');
  if (!isRecord(data)) throw new Error('GET /api/session/active returned no session map');
  return new Set(Object.keys(data));
}

async function readSnapshot(
  client: OpenCodeClient,
  session: HostedSession,
  running: boolean,
): Promise<SessionSnapshot> {
  const base = `/api/session/${encodeURIComponent(session.sessionId)}`;
  const info = await readData(client, base);
  const permissions = await readData(client, `${base}/permission`);
  if (!isRecord(info) || !Array.isArray(permissions)) {
    throw new Error(`the session or its permissions had an unexpected shape`);
  }
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
    running,
    outcome: typeof info.outcome === 'string' ? info.outcome : null,
    idleAt: isRecord(info.time) && typeof info.time.idle === 'number' ? info.time.idle : null,
    permissions: pending,
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
