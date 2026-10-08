import type {
  ApprovalSubject,
  ErrorInfo,
  EventOf,
  Provenance,
  QuestionPrompt,
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
  // `form.created` carries the whole form, which names its session.
  const owner = isRecord(data.form) ? data.form : data;
  const sessionId = typeof owner.sessionID === 'string' ? owner.sessionID : null;
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
  /** The `provider/model` OpenCode last reported using, which outlives turns. */
  model: string | null;
  /** Pending permission requests. */
  readonly approvals: Set<string>;
  /** Decisions OpenCode confirmed with a 204, kept until `permission.replied` arrives. */
  readonly replies: Map<string, ApprovalOutcome>;
  /** Tool calls that started and have not finished. */
  readonly tools: Set<string>;
  /**
   * Shell tool calls that have not finished, by tool call id, with the command the model gave once
   * OpenCode published the call (`session.tool.called`, which it does before the tool runs): what
   * an approval of the call shows, whole, in place of the parts OpenCode's parse found in it.
   */
  readonly shellCalls: Map<string, string | null>;
  /** Pending forms, the agent's questions (ADR 0022), with what answering them needs. */
  readonly questions: Map<string, FormFields>;
  /** Forms OpenCode confirmed answering with a 204, kept until `form.replied` arrives. */
  readonly answered: Set<string>;
}

/** How to put an answer to each question of a form into the form's own fields. */
export type FormFields = readonly {
  readonly key: string;
  readonly multiple: boolean;
  /** Each offered option's value, by the label the person chose it by. */
  readonly values: ReadonlyMap<string, string>;
}[];

export function createSessionState(): SessionState {
  return {
    turn: null,
    awaitingStart: false,
    pendingInboxId: null,
    since: null,
    settledThrough: null,
    steered: new Set(),
    model: null,
    approvals: new Set(),
    replies: new Map(),
    tools: new Set(),
    shellCalls: new Map(),
    questions: new Map(),
    answered: new Set(),
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
  state.shellCalls.clear();
  state.questions.clear();
  state.answered.clear();
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
  // A shell call's command is kept whenever it is seen, for the approval that may follow it.
  if (event.type === 'session.tool.input.started' && data.name === SHELL_TOOL) {
    const id = nonBlank(data.id);
    if (id !== null && !state.shellCalls.has(id)) state.shellCalls.set(id, null);
  } else if (event.type === 'session.tool.called') {
    const id = nonBlank(data.id);
    const command = isRecord(data.input) ? shellCommand(data.input) : null;
    if (id !== null && state.shellCalls.has(id) && command !== null) {
      state.shellCalls.set(id, command);
    }
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
    case 'session.step.started': {
      // Each step names the model it asks, as OpenCode's `Model.Ref`; a change is reported.
      const model = isRecord(data.model) ? data.model : {};
      const providerID = nonBlank(model.providerID);
      const id = nonBlank(model.id);
      if (providerID === null || id === null) return [];
      const ref = `${providerID}/${id}`;
      if (ref === state.model || !/^\S{1,256}$/.test(ref)) return [];
      state.model = ref;
      return make('runtime.model.used', { model_ref: ref });
    }
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
      const subject = approvalSubject(
        data.action,
        data.resources,
        askedCommand(state, data.action, data.source),
      );
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
    case 'form.created': {
      const form = isRecord(data.form) ? data.form : null;
      const asked = form === null ? null : formQuestion(form);
      if (asked === null || state.questions.has(asked.id)) return [];
      state.questions.set(asked.id, asked.fields);
      return make('runtime.question.asked', {
        question_id: asked.id,
        prompts: asked.prompts,
        answerable: asked.answerable,
      });
    }
    case 'form.replied':
    case 'form.cancelled': {
      const id = nonBlank(data.id);
      if (id === null || !state.questions.has(id)) return [];
      state.questions.delete(id);
      state.answered.delete(id);
      return make('runtime.question.resolved', {
        question_id: id,
        outcome: event.type === 'form.replied' ? 'answered' : 'dismissed',
      });
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
      if (id !== null) state.shellCalls.delete(id);
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

/**
 * A form as the agent's question (ADR 0022): OpenCode 2.0.18's `question` tool opens a form with
 * one field per question, `string`, or `multiselect` when several answers are allowed, each with
 * its options and `custom` for a typed answer. Only such forms can be answered here; any other
 * form, or one with fields of another type, is shown as waiting and is not answerable. Its texts
 * are reported whole: the control plane takes credentials out of them, then fits them to the
 * contract, and a question with anything cut can't be answered, since the person would answer
 * text they did not see whole, and a cut label would stand for a value they never read. Null for a
 * form without an id or a single field to show.
 */
export function formQuestion(form: Readonly<Record<string, unknown>>): {
  readonly id: string;
  readonly prompts: QuestionPrompt[];
  readonly answerable: boolean;
  readonly fields: FormFields;
} | null {
  const id = nonBlank(form.id);
  const raw = Array.isArray(form.fields) ? form.fields.filter(isRecord) : [];
  if (id === null || raw.length === 0) return null;
  const metadata = isRecord(form.metadata) ? form.metadata : {};
  let answerable = metadata.kind === 'question' && raw.length <= 10;
  const prompts: QuestionPrompt[] = [];
  const fields: FormFields[number][] = [];
  for (const [index, field] of raw.slice(0, 10).entries()) {
    const key = nonBlank(field.key) ?? `field-${index}`;
    // An answer names its question by this key, so it has to come back unchanged and unique.
    if (key.length > 256 || fields.some((known) => known.key === key)) answerable = false;
    const type = field.type;
    const supported = type === 'string' || type === 'multiselect';
    if (!supported) answerable = false;
    const offered = Array.isArray(field.options) ? field.options.filter(isRecord) : [];
    if (offered.length > 20) answerable = false;
    const values = new Map<string, string>();
    const options: QuestionPrompt['options'] = [];
    for (const option of offered.slice(0, 20)) {
      const label = nonBlank(option.label);
      const value = typeof option.value === 'string' ? option.value : null;
      if (label === null || value === null || values.has(label)) {
        answerable = false;
        continue;
      }
      values.set(label, value);
      const description = nonBlank(option.description);
      options.push({ label, description });
    }
    const multiple = type === 'multiselect';
    // A string field without options takes only a typed answer.
    const freeText = supported && (field.custom === true || (!multiple && offered.length === 0));
    const header = nonBlank(field.title);
    const text = nonBlank(field.description) ?? header ?? nonBlank(form.title) ?? 'Question';
    prompts.push({
      key,
      header,
      text,
      options,
      multiple,
      free_text: freeText,
      secret: false,
    });
    fields.push({ key, multiple, values });
  }
  return { id, prompts, answerable, fields };
}

/**
 * A form's answer from the person's answers: each question's chosen options as the form's values,
 * one for a single choice, several for a multiselect field, a typed answer among them.
 */
export function formAnswer(
  fields: FormFields,
  answers: readonly {
    readonly key: string;
    readonly selected: readonly string[];
    readonly text: string | null;
  }[],
): Record<string, string | string[]> {
  // Built from entries, so a field keyed `__proto__` keeps its answer as its own key.
  return Object.fromEntries(
    fields.flatMap((field) => {
      const given = answers.find((each) => each.key === field.key);
      if (given === undefined) return [];
      const chosen = given.selected.map((label) => field.values.get(label) ?? label);
      if (given.text !== null) chosen.push(given.text);
      return [[field.key, field.multiple ? chosen : (chosen[0] ?? '')]];
    }),
  );
}

/** `once` and `always` let the tool run; `reject` refuses it. Anything else is not mapped. */
export function replyOutcome(reply: unknown): ApprovalOutcome | null {
  if (reply === 'once' || reply === 'always') return 'approved';
  if (reply === 'reject') return 'denied';
  return null;
}

/** The name of OpenCode 2.0.18's shell tool, which is also the action its permission asks. */
export const SHELL_TOOL = 'shell';

/** The command a shell tool call's input gives, exactly as the model wrote it, or null. */
export function shellCommand(input: Readonly<Record<string, unknown>>): string | null {
  return typeof input.command === 'string' && /\S/.test(input.command) ? input.command : null;
}

/**
 * The command a shell permission request was raised for, when the request names the tool call
 * (`source`, `{type: "tool", id}`) and that call's command is known.
 */
function askedCommand(state: SessionState, action: unknown, source: unknown): string | null {
  if (action !== SHELL_TOOL || !isRecord(source) || source.type !== 'tool') return null;
  const id = nonBlank(source.id);
  return id === null ? null : (state.shellCalls.get(id) ?? null);
}

/**
 * A permission request as a tool use: its action names the tool, and what it would touch is the
 * shell command the request was raised for, whole, when it is known, else the resources OpenCode
 * listed. For a shell command those are only the parts its parse found, which can leave out what
 * else the command runs or writes (opencode-permissions.md), so the command itself is preferred.
 */
export function approvalSubject(
  action: unknown,
  resources: unknown,
  command: string | null = null,
): ApprovalSubject | null {
  const tool = nonBlank(action);
  if (tool === null) return null;
  const listed = Array.isArray(resources)
    ? resources.filter((item): item is string => typeof item === 'string' && /\S/.test(item))
    : [];
  return {
    kind: 'tool_use',
    tool_name: clip(tool, 128),
    // Whole: the control plane takes credentials out, then cuts it to the contract.
    summary: command ?? (listed.length > 0 ? listed.join('\n') : tool),
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
    // Whole: the control plane takes credentials out, which it finds only whole, then cuts it.
    message:
      message === null
        ? 'OpenCode reported the execution as failed without a message.'
        : message.trim(),
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
