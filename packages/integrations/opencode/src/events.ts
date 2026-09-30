import type {
  ApprovalSubject,
  ErrorInfo,
  EventOf,
  Provenance,
  RuntimeEventType,
  Timestamp,
} from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';

/**
 * One OpenCode 2.0.18 event from `GET /api/event`. The OpenAPI document describes event payloads
 * only as an opaque string, so these shapes come from events captured from the pinned binary
 * (the fixtures beside this package's tests).
 */
export interface OpenCodeEvent {
  /** The `evt_` id OpenCode puts inside every payload; SSE `id:` fields are never sent. */
  readonly id: string;
  readonly type: string;
  /** Milliseconds since the epoch, by OpenCode's clock. */
  readonly created: number | null;
  readonly sessionId: string | null;
  /** `durable.seq`: per-session order of durable events. Absent on volatile events such as permissions. */
  readonly seq: number | null;
  readonly data: Readonly<Record<string, unknown>>;
}

export function decodeEvent(raw: string): OpenCodeEvent | null {
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.type !== 'string') {
    return null;
  }
  const data = isRecord(value.data) ? value.data : {};
  const sessionId = typeof data.sessionID === 'string' ? data.sessionID : null;
  const durable = isRecord(value.durable) ? value.durable : null;
  // A durable sequence orders one aggregate; it is only the session's order when the session is it.
  const seq =
    durable !== null &&
    sessionId !== null &&
    durable.aggregateID === sessionId &&
    Number.isSafeInteger(durable.seq) &&
    (durable.seq as number) >= 0
      ? (durable.seq as number)
      : null;
  return {
    id: value.id,
    type: value.type,
    created: typeof value.created === 'number' ? value.created : null,
    sessionId,
    seq,
    data,
  };
}

export type ApprovalOutcome = 'approved' | 'denied';

/** What the adapter knows about one hosted OpenCode session, updated as observations are made. */
export interface SessionState {
  /** The running turn, identified by the id of the event that started it when that was seen. */
  turn: { readonly id: string | null } | null;
  /** A prompt was sent and the execution it starts has not been seen yet. */
  awaitingStart: boolean;
  /** Inbox item id OpenCode gave that prompt; it leaves the inbox when an execution takes it. */
  pendingInboxId: string | null;
  /**
   * An OpenCode time no later than the start of the running or awaited turn: when its prompt was
   * enqueued, or when it was seen starting. An outcome recorded before it belongs to an older turn.
   */
  since: number | null;
  /**
   * OpenCode time up to which a reconciliation settled the session. Transitions reported by older
   * events, still in the stream after a reconnect, were settled already and are ignored.
   */
  settledThrough: number | null;
  /** Inbox item ids of instructions steered into a running turn that OpenCode has not delivered yet. */
  readonly steered: Set<string>;
  /** Pending permission requests. */
  readonly approvals: Set<string>;
  /** Decisions OpenCode confirmed with a 204, kept until `permission.replied` arrives. */
  readonly replies: Map<string, ApprovalOutcome>;
  /** Tool calls that started and have not finished. */
  readonly tools: Set<string>;
}

export function createSessionState(): SessionState {
  return {
    turn: null,
    awaitingStart: false,
    pendingInboxId: null,
    since: null,
    settledThrough: null,
    steered: new Set(),
    approvals: new Set(),
    replies: new Map(),
    tools: new Set(),
  };
}

/** Ends the running turn. As in the projection, nothing is in flight once a turn is over. */
export function endTurn(state: SessionState): void {
  state.turn = null;
  state.awaitingStart = false;
  state.pendingInboxId = null;
  state.since = null;
  state.approvals.clear();
  state.replies.clear();
  state.tools.clear();
}

/**
 * Maps one event of a hosted session to observations and updates the session state. Only
 * defensible mappings exist; every other event type is ignored. Transitions the state already
 * reflects are skipped, so an event that repeats a fact learned another way (for example while
 * reconciling after a reconnect) is not reported twice.
 */
export function observeEvent(
  state: SessionState,
  event: OpenCodeEvent,
  now: Date,
): RuntimeObservation[] {
  const observed: Provenance = { epistemic: 'observed', native_type: nativeType(event.type) };
  const make = <T extends RuntimeEventType>(
    type: T,
    payload: EventOf<T>['payload'],
    provenance: Provenance = observed,
  ): RuntimeObservation[] => [
    observation(type, payload, {
      native_event_id: event.id,
      sequence: event.seq,
      occurred_at: toTimestamp(event.created, now),
      provenance,
    }),
  ];
  const data = event.data;
  // A steered instruction reached the model's context; it is no longer waiting in the inbox.
  if (event.type === 'session.inbox.delivered' && typeof data.inboxID === 'string') {
    state.steered.delete(data.inboxID);
    return [];
  }
  // Agent text is history, not state, so it is reported even from a settled stretch.
  if (
    state.settledThrough !== null &&
    event.created !== null &&
    event.created <= state.settledThrough &&
    event.type !== 'session.text.ended'
  ) {
    return [];
  }

  switch (event.type) {
    case 'session.execution.started': {
      state.awaitingStart = false;
      state.pendingInboxId = null;
      if (event.created !== null) state.since = event.created;
      if (state.turn !== null) return [];
      state.turn = { id: event.id };
      return make('runtime.turn.started', { turn_id: event.id });
    }
    case 'session.execution.succeeded':
    case 'session.execution.failed':
    case 'session.execution.interrupted': {
      const turn = state.turn;
      if (turn === null) return [];
      endTurn(state);
      if (event.type === 'session.execution.succeeded') {
        return make('runtime.turn.completed', { turn_id: turn.id });
      }
      if (event.type === 'session.execution.failed') {
        return make('runtime.turn.failed', { turn_id: turn.id, error: executionError(data) });
      }
      return make('runtime.turn.interrupted', { turn_id: turn.id });
    }
    case 'permission.asked': {
      const id = nonBlank(data.id);
      const subject = approvalSubject(data.action, data.resources);
      if (id === null || subject === null || state.approvals.has(id)) return [];
      state.approvals.add(id);
      return make('runtime.approval.requested', { approval_id: id, subject });
    }
    case 'permission.replied': {
      const id = nonBlank(data.requestID);
      const decision = replyOutcome(data.reply);
      if (id === null || decision === null || !state.approvals.has(id)) return [];
      state.approvals.delete(id);
      state.replies.delete(id);
      return make('runtime.approval.resolved', { approval_id: id, decision });
    }
    case 'session.tool.input.started': {
      const id = nonBlank(data.id);
      const name = nonBlank(data.name);
      if (id === null || name === null || state.tools.has(id)) return [];
      state.tools.add(id);
      return make('runtime.tool.started', {
        tool_call_id: id,
        tool_name: clip(name, 128),
        title: null,
      });
    }
    case 'session.tool.success':
    case 'session.tool.failed': {
      const id = nonBlank(data.id);
      if (id === null || !state.tools.has(id)) return [];
      state.tools.delete(id);
      return make('runtime.tool.completed', {
        tool_call_id: id,
        outcome: event.type === 'session.tool.success' ? 'succeeded' : 'failed',
      });
    }
    case 'session.text.ended': {
      const text = typeof data.text === 'string' ? data.text : '';
      if (!/\S/.test(text)) return [];
      return make(
        'runtime.agent_message',
        { text: clip(text, 32_000) },
        { epistemic: 'reported', native_type: nativeType(event.type) },
      );
    }
    default:
      return [];
  }
}

/** `once` and `always` let the tool run; `reject` refuses it. Anything else is not mapped. */
export function replyOutcome(reply: unknown): ApprovalOutcome | null {
  if (reply === 'once' || reply === 'always') return 'approved';
  if (reply === 'reject') return 'denied';
  return null;
}

/** A permission request as a tool use: its action names the tool, its resources say what it would touch. */
export function approvalSubject(action: unknown, resources: unknown): ApprovalSubject | null {
  const tool = nonBlank(action);
  if (tool === null) return null;
  const listed = Array.isArray(resources)
    ? resources.filter((item): item is string => typeof item === 'string' && /\S/.test(item))
    : [];
  return {
    kind: 'tool_use',
    tool_name: clip(tool, 128),
    summary: clip(listed.length > 0 ? listed.join('\n') : tool, 2000),
  };
}

/** OpenCode reports `{type, message}`; its type becomes the error code, for example `provider_no_route`. */
function executionError(data: Readonly<Record<string, unknown>>): ErrorInfo {
  const error = isRecord(data.error) ? data.error : {};
  const type = typeof error.type === 'string' ? error.type : '';
  const normalized = type
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const code =
    normalized === ''
      ? 'opencode_execution_failed'
      : /^[a-z]/.test(normalized)
        ? normalized
        : `opencode_${normalized}`;
  const message = nonBlank(error.message);
  return {
    code: code.slice(0, 64),
    message:
      message === null
        ? 'OpenCode reported the execution as failed without a message.'
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

/** An OpenCode millisecond time as a contract timestamp, or `fallback` when it is missing or unusable. */
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

function nativeType(type: string): string {
  return `opencode/${type}`.slice(0, 128);
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && /\S/.test(value) ? value : null;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
