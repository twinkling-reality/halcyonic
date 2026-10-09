import { randomUUID } from 'node:crypto';
import { mkdir, rename, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import type {
  ApprovalDecision,
  Provenance,
  QuestionAnswer,
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
  askedCall,
  createSessionState,
  decodeEvent,
  endTurn,
  formAnswer,
  isRecord,
  type OpenCodeEvent,
  observation,
  observeEvent,
  type SessionState,
  SHELL_TOOL,
  shellCallKey,
  shellRequest,
  toTimestamp,
} from './events.ts';
import {
  type ModelRef,
  readDefaultModel,
  readModels,
  sameModel,
  toRuntimeModel,
} from './models.ts';
import { type OllamaGate, startOllamaGate } from './ollama-gate.ts';
import { parseStartOptions } from './options.ts';
import { type PendingPermission, reconcileSession, type SessionSnapshot } from './reconcile.ts';
import { openCodeFolders, sandboxProfile } from './sandbox-profile.ts';
import {
  buildEnvironment,
  describeExit,
  type ExitStatus,
  freeLoopbackPort,
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
 * The code a task fails with when something other than Halcyonic changed what it may do: its
 * session's rules, an approval Halcyonic did not give, or a session on its server it did not open.
 */
export const TAMPERED = 'runtime_tampered';

/** A permission rule, as OpenCode 2.0.18 takes it (schema/src/permission.ts). */
export interface PermissionRule {
  readonly action: string;
  readonly resource: string;
  readonly effect: 'allow' | 'ask' | 'deny';
}

/**
 * The tools every session Halcyonic creates denies outright: those that reach the network, or
 * send work to another model, whatever the session's model is (opencode-permissions.md). Code
 * Mode's `execute` runs JavaScript with a `fetch` no permission covers; `webfetch` and `websearch`
 * are the web tools; `subagent` can run a child session on any model OpenCode lists, hosted ones
 * among them, and its asks never reach the headset. A tool every rule denies is not offered to the
 * model at all.
 */
export const DENIED_TOOLS: readonly string[] = ['execute', 'webfetch', 'websearch', 'subagent'];

/** Every spelling of a name by the case of its letters, for a disk that ignores case. */
function caseSpellings(name: string): string[] {
  return [...name].reduce<string[]>(
    (spellings, character) =>
      spellings.flatMap((spelling) =>
        character.toLowerCase() === character.toUpperCase()
          ? [spelling + character]
          : [spelling + character.toLowerCase(), spelling + character.toUpperCase()],
      ),
    [''],
  );
}

/**
 * The paths OpenCode 2.0.18 reads its configuration from, as `edit` resources (relative to the
 * session's folder, or absolute outside it), and a repository's `.git`, all denied as hidden
 * paths: every path one of whose parts starts with a dot, `*` matching `/` too
 * (core/src/util/wildcard.ts). In the folder and every folder above it OpenCode reads `.opencode`
 * (whose plugins load as server code), `.claude`, `.agents`, `opencode.json` and `opencode.jsonc`
 * (core/src/config/discovery.ts), and `~/.claude` and `~/.agents`; it watches them and reloads MCP
 * servers and plugins, so an edit there could add either. A `.git` file or folder names programs
 * git runs, so an edit there would run code when a person approves a git command that looks
 * harmless. OpenCode matches resources by case on macOS, whose disk does not, so a rule for one
 * name misses its other spellings; a rule for every hidden path has none. `opencode.json` and
 * `opencode.jsonc` are not hidden, so every spelling of `opencode` by case is denied with any
 * extension of four letters or more (opencode-permissions.md). The cost: the edit tool cannot
 * change any dotfile, such as `.gitignore`; a shell command can, once the person approves it.
 */
const CONFIG_PATHS: readonly string[] = [
  '.*',
  '*/.*',
  ...caseSpellings('opencode').flatMap((name) => [`${name}.????*`, `*/${name}.????*`]),
];

/**
 * The permission rules every session Halcyonic creates carries, after every configuration file's:
 * the tools above denied, shell commands asked (the owner's decision of 2026-10-08; OpenCode asks
 * only for the commands its parse of the command line finds, opencode-permissions.md), and
 * edits to a repository's `.git` and to any path OpenCode reads its configuration from denied,
 * among them OpenCode's global
 * configuration folder (`OPENCODE_CONFIG_DIR`, or `$XDG_CONFIG_HOME/opencode`, or
 * `~/.config/opencode`; util/src/global-roots.ts) and a file `OPENCODE_CONFIG` names. Other edits
 * keep OpenCode's own rules, so they need no press, except one outside the task's folder, which
 * asks by its own path. A session's rule outranks the person's and the
 * repository's configuration; a saved "always" outranks its ask but not its deny
 * (core/src/permission.ts at v2.0.18; opencode-permissions.md).
 */
export function sessionPermissions(
  environment: Readonly<Record<string, string>>,
): PermissionRule[] {
  const configHome =
    environment.XDG_CONFIG_HOME !== undefined && environment.XDG_CONFIG_HOME !== ''
      ? environment.XDG_CONFIG_HOME
      : environment.HOME !== undefined && environment.HOME !== ''
        ? join(environment.HOME, '.config')
        : null;
  const globalFolder =
    environment.OPENCODE_CONFIG_DIR !== undefined && environment.OPENCODE_CONFIG_DIR !== ''
      ? environment.OPENCODE_CONFIG_DIR
      : configHome === null
        ? null
        : join(configHome, 'opencode');
  const paths = [
    ...CONFIG_PATHS,
    ...(globalFolder === null ? [] : [`${globalFolder}/*`]),
    ...(environment.OPENCODE_CONFIG !== undefined && environment.OPENCODE_CONFIG !== ''
      ? [environment.OPENCODE_CONFIG]
      : []),
  ];
  return [
    ...DENIED_TOOLS.map((action) => ({ action, resource: '*', effect: 'deny' as const })),
    { action: 'shell', resource: '*', effect: 'ask' },
    // An edit outside the task's folder, whose resource is its absolute path, asks by that path:
    // approving the folder (`external_directory`) names no file and does not say it writes.
    { action: 'edit', resource: '/*', effect: 'ask' },
    ...paths.map((resource) => ({ action: 'edit', resource, effect: 'deny' as const })),
  ];
}

/**
 * Verified against OpenCode 2.0.18. Instructions while a turn runs use OpenCode's `steer`
 * delivery, which hands them to the model when the running step ends.
 */
export const OPENCODE_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: true,
  respond_to_approval: true,
  // The `question` tool's forms are answered through the form API (ADR 0022).
  answer_question: true,
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
   * Delays before reading a session again when reading it back after a reconnect fails, as when
   * the server is still waking with the Mac. When they run out, the session is reported lost.
   */
  readonly snapshotRetryDelaysMs?: readonly number[];
  /**
   * Delays between attempts to read a session lost to read failures back, while its server still
   * runs; the last repeats. Each attempt reopens the event stream and reconciles.
   */
  readonly recoveryDelaysMs?: readonly number[];
  /**
   * How long a start waits for OpenCode to offer the execution's model, which it may not list yet
   * right after it starts. By default 10 s.
   */
  readonly modelWaitMs?: number;
  readonly clock?: Clock;
  /**
   * Runs the server, and everything it starts, in a Seatbelt sandbox of Halcyonic's own (ADR
   * 0028): network out only to these ports on loopback, writes only in these project roots and the
   * server's own folders, and these paths unreadable. Null runs it unsandboxed.
   */
  readonly sandbox?: {
    /** Ports on loopback the server may reach; its own and the Ollama gate's are added. */
    readonly loopbackPorts: readonly number[];
    /**
     * Ollama's address on loopback, which the server reaches only through a gate of the adapter's
     * own (`ollama-gate.ts`), never directly. Null when OpenCode is given no Ollama.
     */
    readonly ollama?: string | null;
    readonly projectRoots: readonly string[];
    readonly unreadable: readonly string[];
    /** Paths inside an unreadable folder that stay readable: OpenCode's settings, its binary. */
    readonly readable?: readonly string[];
  } | null;
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
  /** Settles once the server has exited and its Ollama gate, if it had one, is closed. */
  readonly gone: Promise<void>;
  /** Settles when the event stream is connected; rejects when the server is given up. */
  readiness: Readiness;
  /** Ends the event stream being read now, so it reopens and reconciles. */
  stream: AbortController | null;
  /** The next attempt to read back sessions lost to read failures, and how many came before. */
  recovery: { timer: NodeJS.Timeout | null; attempts: number };
}

interface HostedSession {
  readonly sessionId: string;
  readonly execution: ExecutionContext;
  readonly emit: ObservationSink;
  readonly connection: Connection;
  readonly state: SessionState;
  /** Requests sent for this session that OpenCode has not answered yet. */
  readonly inFlight: Set<Promise<unknown>>;
  /** Approvals the adapter has sent or is sending for this session, by request id. */
  readonly approving: Set<string>;
  /** Set while the session is reconciled; requests wait for it, so none races the snapshot. */
  reconciling: Promise<void> | null;
  lost: boolean;
  /**
   * Lost only because it could not be read back, while its server still runs: read it again later
   * and, once it answers, report the connection restored.
   */
  recoverable: boolean;
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
  readonly #sandbox: OpenCodeRuntimeOptions['sandbox'];
  readonly #silenceTimeoutMs: number;
  readonly #reconnectDelaysMs: readonly number[];
  readonly #snapshotRetryDelaysMs: readonly number[];
  readonly #recoveryDelaysMs: readonly number[];
  readonly #modelWaitMs: number;
  readonly #clock: Clock;
  readonly #sessions = new Map<string, HostedSession>();
  readonly #bySessionId = new Map<string, HostedSession>();
  /** Session creations in flight, and the ids they returned: sessions the adapter made itself. */
  #creating = 0;
  readonly #made = new Set<string>();
  /** Sessions the server reported created that no creation of the adapter's has claimed yet. */
  readonly #unclaimed = new Map<string, Connection>();
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
    this.#sandbox = options.sandbox ?? null;
    this.#silenceTimeoutMs = options.streamSilenceTimeoutMs ?? 45_000;
    this.#reconnectDelaysMs = options.reconnectDelaysMs ?? [100, 250, 500, 1000, 2000, 4000];
    this.#snapshotRetryDelaysMs = options.snapshotRetryDelaysMs ?? [1000, 3000];
    this.#recoveryDelaysMs = options.recoveryDelaysMs ?? [5000, 15_000, 30_000, 60_000, 300_000];
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
      reports_tool_activity: true,
    };
  }

  /** Pid of the running server, for diagnostics. */
  /** Whether the server runs inside Halcyonic's sandbox (ADR 0028); never false on macOS. */
  get sandboxed(): boolean {
    return this.#sandbox !== null && this.#sandbox !== undefined;
  }

  get serverPid(): number | null {
    return this.#current?.server.pid ?? null;
  }

  /**
   * The running server's password, which the host takes out of any runtime error text before it is
   * journaled; nothing while no server runs.
   */
  secrets(): readonly string[] {
    return this.#current === null ? [] : [this.#current.server.secret];
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
      permissions: sessionPermissions(this.#environment),
    };
    if (parsed.value.model !== null) body.model = parsed.value.model;
    this.#creating += 1;
    let created: HttpResponse;
    try {
      created = await send(connection, 'POST', '/api/session', body, 'runtime_refused');
    } finally {
      this.#creating -= 1;
    }
    const info = isRecord(created.body) && isRecord(created.body.data) ? created.body.data : {};
    if (typeof info.id === 'string') this.#made.add(info.id);
    this.#checkUnclaimed();
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
      approving: new Set(),
      reconciling: null,
      lost: false,
      recoverable: false,
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
    // Shell commands get this environment instead of the server's own, so the server's password,
    // which the adapter passes in OPENCODE_PASSWORD, is not in theirs. Defence in depth only: any
    // process of this user can read the server's starting environment (opencode-permissions.md).
    await send(
      connection,
      'PUT',
      `/api/session/${encodeURIComponent(session.sessionId)}/environment`,
      { variables: this.#environment },
      'runtime_refused',
    );
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
      // Marked before it is sent: OpenCode may report the reply before it answers the request.
      if (request.decision === 'approve') session.approving.add(request.approval_id);
      try {
        await send(session.connection, 'POST', path, body, 'approval_not_pending');
      } catch (error) {
        session.approving.delete(request.approval_id);
        throw error;
      }
      if (session.state.approvals.has(request.approval_id)) {
        session.state.replies.set(
          request.approval_id,
          request.decision === 'approve' ? 'approved' : 'denied',
        );
      }
    });
  }

  /**
   * Answers the form OpenCode's `question` tool opened (ADR 0022). Its 204 confirms the answer;
   * `form.replied` then reports it, or the next reconciliation does.
   */
  async answerQuestion(request: {
    execution: ExecutionContext;
    question_id: string;
    answers: readonly QuestionAnswer[];
  }): Promise<void> {
    const session = this.#hosted(request.execution);
    const fields = session.state.questions.get(request.question_id);
    if (fields === undefined) {
      throw new RuntimeActionError(
        'question_not_pending',
        `Question ${request.question_id} is not waiting for an answer.`,
      );
    }
    const path = `/api/session/${encodeURIComponent(session.sessionId)}/form/${encodeURIComponent(request.question_id)}/reply`;
    await this.#act(session, async () => {
      await send(
        session.connection,
        'POST',
        path,
        { answer: formAnswer(fields, request.answers) },
        'question_not_pending',
      );
      if (session.state.questions.has(request.question_id)) {
        session.state.answered.add(request.question_id);
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
    const stopping: Connection[] = [];
    const halt = () => {
      const connection = this.#current;
      if (connection === null) return;
      stopping.push(connection);
      this.#halt(connection);
    };
    halt();
    await this.#launching?.catch(() => undefined);
    halt();
    await Promise.all(
      stopping.map(async (connection) => {
        await connection.server.stop();
        await connection.gone;
      }),
    );
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

  /**
   * The server's sandbox (ADR 0028), or null when it runs unsandboxed: a profile written beside
   * its record, readable by the user only, and the environment it runs with there. Inside, it gets
   * data, state, cache and temporary folders of its own beside the profile, never the person's own
   * OpenCode folders, whose packages, binaries, saved rules and credentials the person's own
   * OpenCode uses outside the sandbox, nor the system's temporary folder every app shares.
   */
  async #sandboxFor(
    port: number,
    gate: OllamaGate | null,
  ): Promise<{ readonly profile: string; readonly environment: Record<string, string> } | null> {
    if (this.#sandbox === null || this.#sandbox === undefined) {
      // On macOS OpenCode runs only inside the sandbox (ADR 0028): its ask before a shell command
      // depends on its parse of the command, and the sandbox bounds what a command can reach.
      if (process.platform === 'darwin') {
        throw new RuntimeActionError(
          'runtime_unavailable',
          "OpenCode runs on this Mac only inside Halcyonic's sandbox, and none was set up for it.",
        );
      }
      return null;
    }
    const folder = join(dirname(this.#recordFile), 'opencode-sandbox');
    for (const name of ['data', 'state', 'cache', 'tmp']) {
      await mkdir(join(folder, name), { recursive: true, mode: 0o700 });
    }
    const environment: Record<string, string> = {
      ...this.#environment,
      XDG_DATA_HOME: join(folder, 'data'),
      XDG_STATE_HOME: join(folder, 'state'),
      XDG_CACHE_HOME: join(folder, 'cache'),
      TMPDIR: `${join(folder, 'tmp')}/`,
    };
    if (gate !== null) {
      // OpenCode ranks this above every settings file, global and project (core/src/config.ts),
      // so no settings move it off the gate; the variable is reserved, so nothing else sets it.
      environment.OPENCODE_CONFIG_CONTENT = JSON.stringify({
        providers: { ollama: { settings: { baseURL: gate.baseUrl } } },
      });
    }
    const text = sandboxProfile({
      loopbackPorts: [...this.#sandbox.loopbackPorts, ...(gate === null ? [] : [gate.port]), port],
      writable: [...this.#sandbox.projectRoots, ...openCodeFolders(environment)],
      unreadable: this.#sandbox.unreadable,
      readable: [...(this.#sandbox.readable ?? []), folder],
    });
    // Written whole under a new name, then moved into place, never through a link at its path.
    const profile = join(dirname(this.#recordFile), 'opencode-sandbox.sb');
    const draft = `${profile}.${process.pid}.${randomUUID()}`;
    await writeFile(draft, text, { mode: 0o600, flag: 'wx' });
    await rename(draft, profile);
    return { profile, environment };
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
    // Chosen here, since the sandbox's profile names the server's own port.
    const port = this.#port ?? (await freeLoopbackPort());
    const ollama = this.#sandbox?.ollama ?? null;
    let gate: OllamaGate | null = null;
    if (ollama !== null) {
      try {
        gate = await startOllamaGate({ ollama, maxChats: () => this.#runningTurns() });
      } catch (error) {
        throw new RuntimeActionError(
          'runtime_unavailable',
          `Could not open OpenCode's way to Ollama: ${message(error)}`,
        );
      }
    }
    let server: OpenCodeServer;
    try {
      const sandbox = await this.#sandboxFor(port, gate);
      server = await launchServer({
        binaryPath: this.#binaryPath,
        environment: sandbox?.environment ?? this.#environment,
        recordFile: this.#recordFile,
        port,
        cwd: tmpdir(),
        startupTimeoutMs: this.#startupTimeoutMs,
        sandboxProfile: sandbox?.profile ?? null,
      });
    } catch (error) {
      await gate?.close();
      throw error;
    }
    const gone = server.exited.then(() => gate?.close());
    if (this.#closing !== null) {
      await server.stop();
      await gone;
      throw closedError();
    }
    const connection: Connection = {
      server,
      listingSince: null,
      halted: new AbortController(),
      gone,
      readiness: readiness(),
      stream: null,
      recovery: { timer: null, attempts: 0 },
    };
    this.#current = connection;
    void server.exited.then((status) => this.#onExit(connection, status));
    void this.#follow(connection);
    await ready(connection);
    return connection;
  }

  /**
   * Turns running or starting on the current server: OpenCode 2.0.18 asks Ollama for one reply at
   * a time for each, so the gate lets no more chats through at once.
   */
  #runningTurns(): number {
    let count = 0;
    for (const session of this.#sessions.values()) {
      const state = session.state;
      if (
        session.connection === this.#current &&
        !session.lost &&
        (state.turn !== null || state.awaitingStart)
      ) {
        count += 1;
      }
    }
    return count;
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
    if (connection.recovery.timer !== null) clearTimeout(connection.recovery.timer);
    connection.recovery.timer = null;
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
    connection.stream = stream;
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
            this.#dispatch(event, connection);
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

  #dispatch(event: OpenCodeEvent, connection: Connection): void {
    if (event.sessionId === null) return;
    if (event.type === 'session.created' && !this.#made.has(event.sessionId)) {
      // Ours unless no creation of the adapter's claims it once those in flight have answered.
      this.#unclaimed.set(event.sessionId, connection);
      this.#checkUnclaimed();
      return;
    }
    const session = this.#bySessionId.get(event.sessionId);
    if (session === undefined || session.lost) return;
    if (event.type === 'session.permissions') {
      // The adapter sets a session's rules only when it creates it.
      this.#tamper(session, event.type, "changed this task's permission rules");
      return;
    }
    if (event.type === 'permission.replied') {
      const id = typeof event.data.requestID === 'string' ? event.data.requestID : '';
      const reply = event.data.reply;
      // A reject, which also settles a session's other requests, never lets anything run.
      if (reply === 'always' || (reply === 'once' && !session.approving.has(id))) {
        this.#tamper(session, event.type, 'approved a request on this task');
        return;
      }
      session.approving.delete(id);
    }
    this.#emit(session, observeEvent(session.state, event, this.#clock.now()));
  }

  /**
   * A session on the adapter's own server that the adapter did not create means its password was
   * used by something else: every task on that server is stopped, and the server with them.
   */
  #checkUnclaimed(): void {
    if (this.#creating > 0) return;
    for (const [sessionId, connection] of this.#unclaimed) {
      this.#unclaimed.delete(sessionId);
      if (this.#made.has(sessionId) || connection.halted.signal.aborted) continue;
      for (const session of this.#bySessionId.values()) {
        if (session.connection === connection) {
          this.#tamper(session, 'session.created', 'opened another session on its OpenCode server');
        }
      }
      void this.#giveUp(
        connection,
        'Something other than Halcyonic opened a session on its OpenCode server.',
      );
    }
  }

  /**
   * Stops a task when something other than the adapter changed what it may do: its turn fails with
   * `runtime_tampered`, or, at rest, the task is reported lost; either way it can no longer be
   * controlled. Only the kind of change and ids are recorded, never what was sent.
   */
  #tamper(session: HostedSession, nativeType: string, what: string): void {
    if (session.lost) return;
    const message = `Something other than Halcyonic ${what}, so Halcyonic stopped it.`;
    const path = `/api/session/${encodeURIComponent(session.sessionId)}/interrupt`;
    void send(session.connection, 'POST', path, undefined, 'runtime_unreachable').catch(
      () => undefined,
    );
    const turn = session.state.turn;
    if (turn === null) {
      this.#lose(session, message);
      return;
    }
    endTurn(session.state);
    this.#emit(session, [
      observation(
        'runtime.turn.failed',
        { turn_id: turn.id, error: { code: TAMPERED, message } },
        {
          native_event_id: null,
          sequence: null,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: nativeType },
        },
      ),
    ]);
    session.lost = true;
    session.recoverable = false;
  }

  async #reconcileAll(connection: Connection): Promise<void> {
    const readable = (session: HostedSession) => !session.lost || session.recoverable;
    const sessions = [...this.#bySessionId.values()].filter(
      (session) => session.connection === connection && readable(session),
    );
    for (const session of sessions) {
      if (connection.halted.signal.aborted) return;
      if (readable(session)) await this.#reconcile(session);
    }
    this.#scheduleRecovery(connection);
  }

  /**
   * While sessions of a running server stay lost to read failures, as after the Mac woke and the
   * server answered too slowly, tries again after a while: it ends the event stream, which reopens
   * and reads every session back before any new event is applied, as after any reconnect.
   */
  #scheduleRecovery(connection: Connection): void {
    const waiting = [...this.#bySessionId.values()].some(
      (session) => session.connection === connection && session.lost && session.recoverable,
    );
    if (!waiting) {
      connection.recovery.attempts = 0;
      return;
    }
    if (connection.recovery.timer !== null || connection.halted.signal.aborted) return;
    const delays = this.#recoveryDelaysMs;
    const wait = delays[Math.min(connection.recovery.attempts, delays.length - 1)] ?? 300_000;
    connection.recovery.attempts += 1;
    connection.recovery.timer = setTimeout(() => {
      connection.recovery.timer = null;
      if (connection.halted.signal.aborted) return;
      connection.stream?.abort();
    }, wait);
    connection.recovery.timer.unref?.();
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
    const recovering = session.lost && session.recoverable;
    const lost = (reason: string, recoverable = false) =>
      this.#lose(session, `After the OpenCode event stream reconnected, ${reason}`, recoverable);
    try {
      const answered = Promise.allSettled([...session.inFlight]);
      if (!(await settlesWithin(answered, READ_TIMEOUT_MS))) {
        lost('a request for this session was still unanswered.', true);
        return;
      }
      for (let reads = 1; ; reads += 1) {
        const snapshot = await this.#readSnapshotPatiently(session);
        if (snapshot === null) return;
        if (session.connection.halted.signal.aborted) return;
        if (session.lost && !(recovering && session.recoverable)) return;
        const result = reconcileSession(session.state, snapshot, this.#clock.now());
        if (result.kind === 'settled') {
          if (recovering) this.#restore(session);
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
      // The server still runs, so the session may answer later: the loss is recoverable.
      lost(`the session could not be read (${message(error)}).`, true);
    } finally {
      session.reconciling = null;
      release();
    }
  }

  /**
   * Reads a session back, reading it again after a failure while retry delays remain: right after
   * the Mac wakes, the server can be too slow to answer the first read. Null when the connection
   * halted or the session was lost meanwhile; the last failure is thrown when the delays run out.
   */
  async #readSnapshotPatiently(session: HostedSession): Promise<SessionSnapshot | null> {
    for (let attempt = 0; ; attempt += 1) {
      try {
        return await readSnapshot(session.connection.server.client, session);
      } catch (error) {
        const wait = this.#snapshotRetryDelaysMs[attempt];
        if (wait === undefined) throw error;
        await delay(wait, undefined, { signal: session.connection.halted.signal }).catch(
          () => undefined,
        );
        if (session.connection.halted.signal.aborted) return null;
        if (session.lost && !session.recoverable) return null;
      }
    }
  }

  // Sessions -----------------------------------------------------------------------------------

  /**
   * Waits until OpenCode can run the execution's model in its directory: the model is listed and
   * has a package. OpenCode discovers the models of local servers such as Ollama shortly after it
   * starts, per directory, and again every 30 s; until then its list lacks them, or lists a model
   * the configuration names without a package, and a session prompted with such a model fails
   * with `provider.no-route`. A named model still not runnable after the wait is refused in
   * words, before any session exists. Without a named model, the wait is for OpenCode to have a
   * default with a package, which it then chooses itself. Until discovery has listed the
   * configured default, OpenCode's default is its first available model, a hosted one of its own
   * service, which is why admission never starts a runtime that lists its models without one.
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
      // The model or the default as OpenCode lists it; without a package it cannot run yet.
      let listed: { readonly package: string | null } | undefined;
      try {
        listed =
          model === null
            ? ((await readDefaultModel(client, directory, READ_TIMEOUT_MS)) ?? undefined)
            : (await readModels(client, directory, READ_TIMEOUT_MS)).find((entry) =>
                sameModel(entry, model),
              );
      } catch (error) {
        throw new RuntimeActionError(
          'runtime_unavailable',
          `OpenCode's list of models could not be read: ${message(error)}`,
        );
      }
      if (listed !== undefined && listed.package !== null) return;
      if (Date.now() >= deadline) {
        if (model === null) return;
        if (listed !== undefined) {
          throw new RuntimeActionError(
            'model_unavailable',
            `OpenCode lists the model ${model.providerID}/${model.id} in ${directory} but cannot run it yet: it has not set up how to reach it. For a local server such as Ollama, that happens once OpenCode has reached the server.`,
          );
        }
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

  /**
   * Reports the session lost. A recoverable loss, by read failures while the server runs, is read
   * back later; any other loss, a server that exited above all, is final.
   */
  #lose(session: HostedSession, reason: string, recoverable = false): void {
    if (session.lost) {
      if (!recoverable) session.recoverable = false;
      return;
    }
    session.recoverable = recoverable;
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

  /** The session answered again after a recoverable loss; what it reads now follows this. */
  #restore(session: HostedSession): void {
    session.lost = false;
    session.recoverable = false;
    this.#emit(session, [
      observation(
        'runtime.connection.restored',
        {
          reason:
            'The OpenCode session could be read again; what changed while it could not follows.',
        },
        {
          native_event_id: null,
          sequence: null,
          occurred_at: this.#clock.now().toISOString(),
          provenance: { epistemic: 'observed', native_type: 'opencode/session.get' },
        },
      ),
    ]);
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
export async function readSnapshot(
  client: Pick<OpenCodeClient, 'request'>,
  session: Pick<HostedSession, 'sessionId' | 'state'>,
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
  const forms = await readData(client, `${base}/form`);
  if (!Array.isArray(forms)) throw new Error('the pending forms had an unexpected shape');
  const pendingForms = forms.filter(isRecord);
  const listed = new Set(pendingForms.map((form) => form.id));
  const settledForms = new Map<string, string>();
  for (const id of session.state.questions.keys()) {
    if (listed.has(id) || session.state.answered.has(id)) continue;
    // A form that cannot be read stays pending until its turn ends; it must not cost the session.
    const detail = await readData(client, `${base}/form/${encodeURIComponent(id)}`).catch(
      () => null,
    );
    const state = isRecord(detail) && isRecord(detail.state) ? detail.state.status : null;
    if (typeof state === 'string') settledForms.set(id, state);
  }
  const active = await readData(client, '/api/session/active');
  const info = await readData(client, base);
  if (!Array.isArray(permissions) || !isRecord(active) || !isRecord(info)) {
    throw new Error('the session, its permissions or the running sessions had an unexpected shape');
  }
  const running = Object.hasOwn(active, session.sessionId);
  // The tool call each pending shell request was raised for, whose command it shows.
  const askedBy = new Map<string, string>();
  for (const item of permissions) {
    if (!isRecord(item) || typeof item.id !== 'string') continue;
    const call = askedCall(item.action, item.source);
    if (call !== null) askedBy.set(item.id, call);
  }
  const toolStatus = new Map<string, string>();
  const toolNames = new Map<string, string>();
  // Newest first, as OpenCode lists messages; a call id seen again in an older message is not it.
  const commands = new Map<string, string | null>();
  // Read whenever the session runs: a call that started while the stream was down is only there.
  if (running || askedBy.size > 0) {
    const messages = await readData(client, `${base}/message`);
    for (const item of Array.isArray(messages) ? messages : []) {
      if (!isRecord(item) || item.type !== 'assistant' || !Array.isArray(item.content)) continue;
      for (const part of item.content) {
        if (!isRecord(part) || part.type !== 'tool' || typeof part.id !== 'string') continue;
        if (
          isRecord(part.state) &&
          typeof part.state.status === 'string' &&
          !toolStatus.has(part.id)
        ) {
          toolStatus.set(part.id, part.state.status);
          if (typeof part.name === 'string' && /\S/.test(part.name))
            toolNames.set(part.id, part.name);
        }
        const call = shellCallKey(item.id, part.id);
        if (call === null || commands.has(call)) continue;
        commands.set(
          call,
          part.name === SHELL_TOOL && isRecord(part.state) && isRecord(part.state.input)
            ? shellRequest(part.state.input)
            : null,
        );
      }
    }
  }
  const pending: PendingPermission[] = [];
  for (const item of permissions) {
    if (!isRecord(item) || typeof item.id !== 'string') continue;
    const call = askedBy.get(item.id);
    const command =
      call === undefined
        ? null
        : commands.has(call)
          ? (commands.get(call) ?? null)
          : (session.state.shellCalls.get(call) ?? null);
    pending.push({ id: item.id, action: item.action, resources: item.resources, command });
  }
  return {
    inbox,
    permissions: pending,
    running,
    outcome: typeof info.outcome === 'string' ? info.outcome : null,
    idleAt: isRecord(info.time) && typeof info.time.idle === 'number' ? info.time.idle : null,
    toolStatus,
    toolNames,
    forms: pendingForms,
    settledForms,
  };
}

async function readData(client: Pick<OpenCodeClient, 'request'>, path: string): Promise<unknown> {
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
