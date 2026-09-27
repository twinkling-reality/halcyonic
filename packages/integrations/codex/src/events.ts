import type {
  ApprovalSubject,
  ErrorInfo,
  EventOf,
  Provenance,
  RuntimeEventType,
  Timestamp,
} from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { APPROVAL_METHODS, type RequestId } from './protocol.ts';

export type ApprovalOutcome = 'approved' | 'denied';

/** An approval Codex asked for, answered through the JSON-RPC request that carried it. */
export interface PendingApproval {
  readonly approvalId: string;
  readonly requestId: RequestId;
  readonly turnId: string;
  /** The decision sent to Codex, once the person answered. */
  answer: ApprovalOutcome | null;
}

/** What the adapter knows about one hosted Codex thread, updated as its messages arrive. */
export interface ThreadState {
  /** The running turn, from `turn/start`'s answer or `turn/started` until `turn/completed`. */
  activeTurnId: string | null;
  /** Turns whose start was reported. */
  readonly reportedTurns: Set<string>;
  /** Recently ended turns, oldest first. */
  readonly endedTurns: string[];
  /** Items reported as started tools and not yet as completed ones. */
  readonly tools: Set<string>;
  /** What each file change item changes, for an approval that names the item. */
  readonly fileChanges: Map<string, string>;
  /** Approvals Codex asked for and has not resolved, by approval id. */
  readonly approvals: Map<string, PendingApproval>;
  /** The sequence of the last observation. */
  sequence: number;
}

export function createThreadState(): ThreadState {
  return {
    activeTurnId: null,
    reportedTurns: new Set(),
    endedTurns: [],
    tools: new Set(),
    fileChanges: new Map(),
    approvals: new Map(),
    sequence: 0,
  };
}

/** One message from the server about a thread: a notification, or a request from the server. */
export type ServerMessage =
  | {
      readonly kind: 'notification';
      readonly method: string;
      readonly params: unknown;
      readonly emittedAtMs: number | null;
    }
  | {
      readonly kind: 'request';
      readonly id: RequestId;
      readonly method: string;
      readonly params: unknown;
    };

export interface Observed {
  readonly observations: RuntimeObservation[];
  /** An approval this request raised, to be answered. Absent for a request the adapter refuses. */
  readonly requested?: PendingApproval;
  /** Approvals this message settled; `confirmed` when Codex took the person's decision. */
  readonly settled: { readonly approval: PendingApproval; readonly confirmed: boolean }[];
}

const ENDED_TURNS_KEPT = 16;

/**
 * Maps one server message about a thread to observations and updates the thread's state. Only
 * defensible mappings exist; every other message is ignored. A fact the state already reflects is
 * not reported twice.
 *
 * - `turn/started` and `turn/completed` (completed, failed, interrupted) are the turn's start and
 *   end. A turn's end clears its approvals. It does not claim that commands stopped: after an
 *   interrupt Codex 0.157.0 leaves running commands running, so a command is reported completed
 *   only when its own `item/completed` arrives, even after its turn ended.
 * - `commandExecution` and `fileChange` items are tools; `item/completed` of an `agentMessage` is
 *   agent text, which is `reported`.
 * - Command and file change approval requests are approvals, summarized from the structured
 *   request, never from the model's stated reason. `serverRequest/resolved` resolves one: Codex
 *   sends it when it takes the answer, and also when a turn's end aborts the request. The
 *   decision is reported only when the person answered and the turn had not ended.
 */
export function observe(state: ThreadState, message: ServerMessage, now: Date): Observed {
  const params = isRecord(message.params) ? message.params : {};
  if (message.kind === 'request') {
    return observeRequest(state, message.id, message.method, params, now);
  }
  const at = toTimestamp(message.emittedAtMs, now);
  const make = <T extends RuntimeEventType>(
    type: T,
    payload: EventOf<T>['payload'],
    nativeId: string,
    epistemic: 'observed' | 'reported' = 'observed',
  ): RuntimeObservation => {
    state.sequence += 1;
    return observation(type, payload, {
      native_event_id: nativeId,
      sequence: state.sequence,
      occurred_at: at,
      provenance: { epistemic, native_type: nativeType(message.method) },
    });
  };
  const none: Observed = { observations: [], settled: [] };

  switch (message.method) {
    case 'turn/started': {
      const turn = isRecord(params.turn) ? params.turn : {};
      const id = nonBlank(turn.id);
      if (id === null || state.reportedTurns.has(id) || state.endedTurns.includes(id)) return none;
      state.reportedTurns.add(id);
      state.activeTurnId = id;
      return {
        observations: [make('runtime.turn.started', { turn_id: id }, `${id}:turn/started`)],
        settled: [],
      };
    }
    case 'turn/completed': {
      const turn = isRecord(params.turn) ? params.turn : {};
      const id = nonBlank(turn.id);
      const known = id !== null && (state.reportedTurns.has(id) || state.activeTurnId === id);
      if (id === null || !known || !isFinal(turn.status)) return none;
      const settled = endTurn(state, id);
      state.sequence += 1;
      const ended = turnEnded(id, turn, {
        native_event_id: `${id}:turn/completed`,
        sequence: state.sequence,
        occurred_at: at,
        provenance: { epistemic: 'observed', native_type: nativeType(message.method) },
      });
      return { observations: [ended], settled };
    }
    case 'item/started': {
      const item = isRecord(params.item) ? params.item : {};
      const id = nonBlank(item.id);
      const turnId = nonBlank(params.turnId);
      if (id === null || turnId === null) return none;
      if (item.type === 'fileChange') state.fileChanges.set(id, describeChanges(item.changes));
      if ((item.type !== 'commandExecution' && item.type !== 'fileChange') || state.tools.has(id)) {
        return none;
      }
      state.tools.add(id);
      const title =
        item.type === 'commandExecution' ? nonBlank(item.command) : state.fileChanges.get(id);
      return {
        observations: [
          make(
            'runtime.tool.started',
            {
              tool_call_id: clip(id, 512),
              tool_name: item.type,
              title: title === null || title === undefined ? null : clip(title, 500),
            },
            `${turnId}:${id}:item/started`,
          ),
        ],
        settled: [],
      };
    }
    case 'item/completed': {
      const item = isRecord(params.item) ? params.item : {};
      const id = nonBlank(item.id);
      const turnId = nonBlank(params.turnId);
      if (id === null || turnId === null) return none;
      const nativeId = `${turnId}:${id}:item/completed`;
      if (item.type === 'agentMessage') {
        const text = typeof item.text === 'string' ? item.text : '';
        if (!/\S/.test(text)) return none;
        return {
          observations: [
            make('runtime.agent_message', { text: clip(text, 32_000) }, nativeId, 'reported'),
          ],
          settled: [],
        };
      }
      // A command is completed when it exited 0 and failed otherwise; a declined one never ran.
      const outcome =
        item.status === 'completed'
          ? 'succeeded'
          : item.status === 'failed' || item.status === 'declined'
            ? 'failed'
            : null;
      if (outcome === null || !state.tools.delete(id)) return none;
      state.fileChanges.delete(id);
      return {
        observations: [
          make('runtime.tool.completed', { tool_call_id: clip(id, 512), outcome }, nativeId),
        ],
        settled: [],
      };
    }
    case 'serverRequest/resolved': {
      const requestId = params.requestId;
      const approval = [...state.approvals.values()].find((item) => item.requestId === requestId);
      if (approval === undefined) return none;
      state.approvals.delete(approval.approvalId);
      if (approval.answer === null) {
        return { observations: [], settled: [{ approval, confirmed: false }] };
      }
      return {
        observations: [
          make(
            'runtime.approval.resolved',
            { approval_id: approval.approvalId, decision: approval.answer },
            `${approval.approvalId}:serverRequest/resolved`,
          ),
        ],
        settled: [{ approval, confirmed: true }],
      };
    }
    default:
      return none;
  }
}

function observeRequest(
  state: ThreadState,
  id: RequestId,
  method: string,
  params: Readonly<Record<string, unknown>>,
  now: Date,
): Observed {
  const turnId = nonBlank(params.turnId);
  const itemId = nonBlank(params.itemId);
  if (
    !(APPROVAL_METHODS as readonly string[]).includes(method) ||
    turnId === null ||
    itemId === null ||
    state.endedTurns.includes(turnId)
  ) {
    return { observations: [], settled: [] };
  }
  const approval: PendingApproval = {
    approvalId: `${turnId}:${String(id)}`,
    requestId: id,
    turnId,
    answer: null,
  };
  state.approvals.set(approval.approvalId, approval);
  const subject: ApprovalSubject =
    method === 'item/commandExecution/requestApproval'
      ? { kind: 'tool_use', tool_name: 'commandExecution', summary: commandSummary(params) }
      : {
          kind: 'tool_use',
          tool_name: 'fileChange',
          summary: fileChangeSummary(state.fileChanges.get(itemId), params.grantRoot),
        };
  state.sequence += 1;
  return {
    observations: [
      observation(
        'runtime.approval.requested',
        { approval_id: approval.approvalId, subject },
        {
          native_event_id: `${approval.approvalId}:${method}`,
          sequence: state.sequence,
          occurred_at: toTimestamp(params.startedAtMs, now),
          provenance: { epistemic: 'observed', native_type: nativeType(method) },
        },
      ),
    ],
    requested: approval,
    settled: [],
  };
}

/**
 * Records the turn a `turn/start` answer names, given the turn that was running when the request
 * was sent. The same id as that turn means Codex steered it instead of starting one. Otherwise the
 * turn runs until `turn/completed`, which may already have arrived.
 */
export function acceptTurnStart(
  state: ThreadState,
  turnId: string,
  runningBefore: string | null,
): void {
  if (turnId === runningBefore || state.endedTurns.includes(turnId)) return;
  state.activeTurnId ??= turnId;
}

/**
 * Settles a thread after its server restarted and the thread was resumed, from its latest turn as
 * Codex recorded it. A turn that was running is reported ended with its recorded status, as an
 * inference: Codex 0.157.0 records a turn cut short by its server's death as interrupted. The
 * approvals died with the server. Tools are not reported: a command can outlive a killed server.
 * `unsettled` explains why the state could not be settled.
 */
export function settleResumed(
  state: ThreadState,
  latest: unknown,
  now: Date,
): Observed & { readonly unsettled: string | null } {
  const settled = [...state.approvals.values()].map((approval) => ({ approval, confirmed: false }));
  state.approvals.clear();
  state.tools.clear();
  state.fileChanges.clear();
  const running = state.activeTurnId;
  const turn = isRecord(latest) ? latest : null;
  if (running === null) {
    return {
      observations: [],
      settled,
      unsettled:
        turn?.status === 'inProgress'
          ? 'Codex reported a turn in progress that Halcyonic did not start.'
          : null,
    };
  }
  if (turn === null || turn.id !== running || !isFinal(turn.status)) {
    return {
      observations: [],
      settled,
      unsettled: 'Codex had no final record of the turn that was running.',
    };
  }
  endTurn(state, running);
  state.sequence += 1;
  const ended = turnEnded(running, turn, {
    native_event_id: `${running}:thread/turns/list`,
    sequence: state.sequence,
    occurred_at: now.toISOString(),
    provenance: {
      epistemic: 'inferred',
      native_type: nativeType('thread/turns/list'),
      rule: 'codex.restart.turn_status',
    },
  });
  return { observations: [ended], settled, unsettled: null };
}

function isFinal(status: unknown): status is 'completed' | 'failed' | 'interrupted' {
  return status === 'completed' || status === 'failed' || status === 'interrupted';
}

/** The observation of a turn's final status. */
function turnEnded(
  turnId: string,
  turn: Readonly<Record<string, unknown>>,
  meta: ObservationMeta,
): RuntimeObservation {
  if (turn.status === 'completed')
    return observation('runtime.turn.completed', { turn_id: turnId }, meta);
  if (turn.status === 'failed') {
    return observation(
      'runtime.turn.failed',
      { turn_id: turnId, error: turnError(turn.error) },
      meta,
    );
  }
  return observation('runtime.turn.interrupted', { turn_id: turnId }, meta);
}

/** Ends a turn: nothing runs in it any more, and its unresolved approvals are dropped. */
export function endTurn(state: ThreadState, turnId: string): Observed['settled'] {
  if (state.activeTurnId === turnId) state.activeTurnId = null;
  state.reportedTurns.delete(turnId);
  state.endedTurns.push(turnId);
  if (state.endedTurns.length > ENDED_TURNS_KEPT) state.endedTurns.shift();
  const settled: { approval: PendingApproval; confirmed: boolean }[] = [];
  for (const approval of state.approvals.values()) {
    if (approval.turnId !== turnId) continue;
    state.approvals.delete(approval.approvalId);
    settled.push({ approval, confirmed: false });
  }
  return settled;
}

/** A command approval as the command Codex would run and where, or the network access it asks for. */
function commandSummary(params: Readonly<Record<string, unknown>>): string {
  const command = nonBlank(params.command);
  const network = isRecord(params.networkApprovalContext) ? params.networkApprovalContext : {};
  const host = nonBlank(network.host);
  let summary =
    command !== null
      ? params.kind === 'writeStdin'
        ? `Send input to a running command: ${command}`
        : command
      : host !== null
        ? `Network access to ${host} (${String(network.protocol)})`
        : 'Codex did not say which command it wants to run.';
  const cwd = nonBlank(params.cwd);
  if (command !== null && cwd !== null) summary += `\nin ${cwd}`;
  return clip(summary, 2000);
}

function fileChangeSummary(changes: string | undefined, grantRoot: unknown): string {
  let summary = changes ?? 'Codex did not say which files it wants to change.';
  const root = nonBlank(grantRoot);
  if (root !== null) summary += `\nand write access under ${root} for the rest of the session`;
  return clip(summary, 2000);
}

/** The files a file change item touches, one line each, from `v2/FileUpdateChange.ts`. */
function describeChanges(changes: unknown): string {
  const lines: string[] = [];
  for (const change of Array.isArray(changes) ? changes : []) {
    if (!isRecord(change) || !isRecord(change.kind)) continue;
    const path = nonBlank(change.path);
    if (path === null) continue;
    const kind = change.kind.type;
    const moved = nonBlank(change.kind.move_path);
    if (kind === 'add') lines.push(`add ${path}`);
    else if (kind === 'delete') lines.push(`delete ${path}`);
    else if (kind === 'update')
      lines.push(moved === null ? `update ${path}` : `move ${path} to ${moved}`);
  }
  return lines.length > 0 ? lines.join('\n') : 'Codex did not say which files it wants to change.';
}

/**
 * A failed turn's error. The code comes from `codexErrorInfo`, a camelCase name or an object keyed
 * by one, for example `internalServerError` becomes `codex_internal_server_error`.
 */
function turnError(error: unknown): ErrorInfo {
  const details = isRecord(error) ? error : {};
  const info = details.codexErrorInfo;
  const name = typeof info === 'string' ? info : isRecord(info) ? (Object.keys(info)[0] ?? '') : '';
  const snake = name
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const message = nonBlank(details.message);
  return {
    code: `codex_${snake === '' ? 'turn_failed' : snake}`.slice(0, 64),
    message:
      message === null
        ? 'Codex reported the turn as failed without a message.'
        : clip(message.trim(), 2000),
  };
}

export interface ObservationMeta {
  readonly native_event_id: string | null;
  readonly sequence: number | null;
  readonly occurred_at: Timestamp;
  readonly provenance: Provenance;
}

export function observation<T extends RuntimeEventType>(
  type: T,
  payload: EventOf<T>['payload'],
  meta: ObservationMeta,
): RuntimeObservation {
  return { type, payload, ...meta } as RuntimeObservation;
}

/** A millisecond time as a contract timestamp, or `fallback` when it is missing or unusable. */
export function toTimestamp(milliseconds: unknown, fallback: Date): Timestamp {
  if (typeof milliseconds === 'number' && Number.isFinite(milliseconds)) {
    const date = new Date(milliseconds);
    const iso = Number.isNaN(date.getTime()) ? '' : date.toISOString();
    if (/^\d{4}-/.test(iso)) return iso;
  }
  return fallback.toISOString();
}

const TRUNCATED = ' [truncated]';

/** Shortens text to `max` characters (code points), marking the cut. */
export function clip(text: string, max: number): string {
  const characters = Array.from(text);
  if (characters.length <= max) return text;
  return characters.slice(0, max - TRUNCATED.length).join('') + TRUNCATED;
}

export function nativeType(method: string): string {
  return `codex/${method}`.slice(0, 128);
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && /\S/.test(value) ? value : null;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
