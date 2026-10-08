import type { Provenance } from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import {
  approvalSubject,
  endTurn,
  formQuestion,
  observation,
  type SessionState,
  toTimestamp,
} from './events.ts';

/**
 * A session's state read over HTTP after the event stream reconnected. OpenCode 2.0.18 does not
 * replay events missed while disconnected, so this is the only way to learn what happened.
 *
 * The adapter reads it in this order, and the rules below rely on it: the inbox, the pending
 * permissions, the running sessions, the session, and the tool parts of its messages. Work only
 * moves forward (queued, delivered and running, ended), and nothing new can start meanwhile
 * because the adapter holds back its own requests while it reconciles.
 */
export interface SessionSnapshot {
  /** Ids in `GET /api/session/{id}/inbox`: enqueued work not yet delivered. Read only while a prompt awaits its start. */
  readonly inbox: ReadonlySet<string>;
  /** `GET /api/session/{id}/permission`. */
  readonly permissions: readonly PendingPermission[];
  /** Listed by `GET /api/session/active`, the only authoritative running signal. */
  readonly running: boolean;
  /** `outcome` of `GET /api/session/{id}`: how the most recent execution ended. */
  readonly outcome: string | null;
  /** `time.idle` of the same read, in milliseconds by OpenCode's clock. */
  readonly idleAt: number | null;
  /** Tool part status by tool call id, from `GET /api/session/{id}/message`, read only for active tools. */
  readonly toolStatus: ReadonlyMap<string, string>;
  /** `GET /api/session/{id}/form`: the session's pending forms, the agent's questions (ADR 0022). */
  readonly forms: readonly Readonly<Record<string, unknown>>[];
  /**
   * `state.status` of `GET /api/session/{id}/form/{formID}` for each question the adapter knew
   * pending that the list no longer holds: `answered` or `cancelled`.
   */
  readonly settledForms: ReadonlyMap<string, string>;
}

export interface PendingPermission {
  readonly id: string;
  readonly action: unknown;
  readonly resources: unknown;
  /** For a shell request, what the tool call it was raised for asks to run, when that is known. */
  readonly command: string | null;
}

export type Reconciliation =
  | { readonly kind: 'settled'; readonly observations: RuntimeObservation[] }
  /** The session was caught between two recorded states; read it again. The state is unchanged. */
  | { readonly kind: 'transitional' }
  | { readonly kind: 'unsettled'; readonly reason: string };

const TURN_RULE: Provenance = {
  epistemic: 'inferred',
  native_type: 'opencode/session',
  rule: 'opencode.reconnect.session_state',
};
const PERMISSION_LIST: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.permission.list',
};
const CONFIRMED_REPLY: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.permission.reply',
};
const FORM_LIST: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.form.list',
};
const FORM_STATE: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.form.get',
};
const MESSAGE_LIST: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.message.list',
};
const OUTCOMES = new Set(['succeeded', 'failed', 'interrupted']);

/**
 * Brings a session's state up to date with a snapshot and reports the difference. It decides
 * before it changes anything: a snapshot caught between two recorded states is `transitional`,
 * and one that cannot be explained without guessing is `unsettled`, which the adapter reports as a
 * lost connection.
 *
 * Only Halcyonic starts turns on the server it launched, one at a time and only at rest, so at
 * most the turn it knew about, or the one its last prompt started, can have run meanwhile. An
 * instruction steered into a running turn never starts one: OpenCode delivers it into that turn,
 * or keeps it for the next prompt.
 */
export function reconcileSession(
  state: SessionState,
  snapshot: SessionSnapshot,
  now: Date,
): Reconciliation {
  let starts = false;
  if (state.awaitingStart) {
    if (state.pendingInboxId === null) {
      return unsettled(
        'A prompt was sent but OpenCode did not identify it, so its turn cannot be traced.',
      );
    }
    // Still in the inbox: no execution has taken it, and the stream will report when one does.
    if (snapshot.inbox.has(state.pendingInboxId) && !snapshot.running) return settled([]);
    // Delivered: an execution took it, so the turn started. It is running, or it ran and ended.
    starts = true;
  } else if (state.turn === null) {
    if (!snapshot.running) return settled([]);
    starts = true;
  }

  const ended = !snapshot.running;
  if (ended) {
    const fresh =
      snapshot.outcome !== null &&
      snapshot.idleAt !== null &&
      (state.since === null || snapshot.idleAt >= state.since);
    // Not running, yet the recorded outcome is older than this turn: read it again.
    if (!fresh) return { kind: 'transitional' };
    if (!OUTCOMES.has(snapshot.outcome ?? '')) {
      return unsettled(
        `The OpenCode turn ended while the event stream was disconnected, with an outcome this adapter does not know (${snapshot.outcome}).`,
      );
    }
  } else {
    const pending = new Set(snapshot.permissions.map((permission) => permission.id));
    for (const id of state.approvals) {
      if (!pending.has(id) && !state.replies.has(id)) {
        return unsettled(
          `Approval ${id} was settled while the OpenCode event stream was disconnected, not by a decision Halcyonic sent.`,
        );
      }
    }
  }

  const observations: RuntimeObservation[] = [];
  const at = (occurredAt: unknown, provenance: Provenance) => ({
    native_event_id: null,
    sequence: null,
    occurred_at: toTimestamp(occurredAt, now),
    provenance,
  });
  if (starts) {
    state.awaitingStart = false;
    state.pendingInboxId = null;
    state.turn = { id: null };
    // The prompt's enqueue time bounds the start from below, and keeps it before an end.
    observations.push(
      observation('runtime.turn.started', { turn_id: null }, at(state.since, TURN_RULE)),
    );
  }
  const turnId = state.turn?.id ?? null;

  if (ended) {
    const end = at(snapshot.idleAt, TURN_RULE);
    if (snapshot.outcome === 'succeeded') {
      observations.push(observation('runtime.turn.completed', { turn_id: turnId }, end));
    } else if (snapshot.outcome === 'interrupted') {
      observations.push(observation('runtime.turn.interrupted', { turn_id: turnId }, end));
    } else {
      observations.push(
        observation(
          'runtime.turn.failed',
          {
            turn_id: turnId,
            error: {
              code: 'opencode_execution_failed',
              message:
                'The OpenCode execution failed while the event stream was disconnected; its error was not observed.',
            },
          },
          end,
        ),
      );
    }
    endTurn(state);
    state.settledThrough = Math.max(state.settledThrough ?? 0, snapshot.idleAt ?? 0);
    return settled(observations);
  }

  const pending = new Set(snapshot.permissions.map((permission) => permission.id));
  for (const id of [...state.approvals]) {
    if (pending.has(id)) continue;
    const decision = state.replies.get(id);
    if (decision === undefined) continue;
    state.approvals.delete(id);
    state.replies.delete(id);
    observations.push(
      observation(
        'runtime.approval.resolved',
        { approval_id: id, decision },
        at(null, CONFIRMED_REPLY),
      ),
    );
  }
  for (const permission of snapshot.permissions) {
    if (state.approvals.has(permission.id)) continue;
    const subject = approvalSubject(permission.action, permission.resources, permission.command);
    if (subject === null) continue;
    state.approvals.add(permission.id);
    observations.push(
      observation(
        'runtime.approval.requested',
        { approval_id: permission.id, subject },
        at(null, PERMISSION_LIST),
      ),
    );
  }
  const forms = new Set(snapshot.forms.map((form) => form.id));
  for (const id of [...state.questions.keys()]) {
    if (forms.has(id)) continue;
    // Settled while the stream was down, by Halcyonic's answer or another client's; its own state
    // says which. One whose state could not be read stays pending until the turn ends.
    const status = state.answered.has(id) ? 'answered' : snapshot.settledForms.get(id);
    if (status !== 'answered' && status !== 'cancelled') continue;
    state.questions.delete(id);
    state.answered.delete(id);
    observations.push(
      observation(
        'runtime.question.resolved',
        { question_id: id, outcome: status === 'answered' ? 'answered' : 'dismissed' },
        at(null, FORM_STATE),
      ),
    );
  }
  for (const form of snapshot.forms) {
    const asked = formQuestion(form);
    if (asked === null || state.questions.has(asked.id)) continue;
    state.questions.set(asked.id, asked.fields);
    observations.push(
      observation(
        'runtime.question.asked',
        { question_id: asked.id, prompts: asked.prompts, answerable: asked.answerable },
        at(null, FORM_LIST),
      ),
    );
  }
  for (const id of [...state.tools]) {
    const status = snapshot.toolStatus.get(id);
    if (status !== 'completed' && status !== 'error') continue;
    state.tools.delete(id);
    observations.push(
      observation(
        'runtime.tool.completed',
        { tool_call_id: id, outcome: status === 'completed' ? 'succeeded' : 'failed' },
        at(null, MESSAGE_LIST),
      ),
    );
  }
  return settled(observations);
}

function settled(observations: RuntimeObservation[]): Reconciliation {
  return { kind: 'settled', observations };
}

function unsettled(reason: string): Reconciliation {
  return { kind: 'unsettled', reason };
}
