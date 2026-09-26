import type { Provenance } from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { approvalSubject, endTurn, observation, type SessionState, toTimestamp } from './events.ts';

/**
 * A session's state read over HTTP after the event stream reconnected. OpenCode 2.0.18 does not
 * replay events missed while disconnected, so this is the only way to learn what happened.
 */
export interface SessionSnapshot {
  /** Listed by `GET /api/session/active`, the only authoritative running signal. */
  readonly running: boolean;
  /** `outcome` of `GET /api/session/{id}`: how the most recent execution ended. */
  readonly outcome: string | null;
  /** `time.idle` of the same read, in milliseconds. */
  readonly idleAt: number | null;
  /** `GET /api/session/{id}/permission`. */
  readonly permissions: readonly PendingPermission[];
  /** Tool part status by tool call id, from `GET /api/session/{id}/message`, read only for active tools. */
  readonly toolStatus: ReadonlyMap<string, string>;
}

export interface PendingPermission {
  readonly id: string;
  readonly action: unknown;
  readonly resources: unknown;
}

export type Reconciliation =
  | { readonly ok: true; readonly observations: RuntimeObservation[] }
  | { readonly ok: false; readonly reason: string };

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
const MESSAGE_LIST: Provenance = {
  epistemic: 'observed',
  native_type: 'opencode/session.message.list',
};

/**
 * Brings a session's state up to date with a snapshot and reports the difference. Anything the
 * snapshot cannot settle without guessing makes the reconciliation fail, and the caller reports
 * the connection as lost instead.
 *
 * Only Halcyonic starts turns on the server it launched, one at a time and only at rest, so at
 * most the turn it knew about can have ended while it was disconnected.
 */
export function reconcileSession(
  state: SessionState,
  snapshot: SessionSnapshot,
  now: Date,
): Reconciliation {
  const observations: RuntimeObservation[] = [];
  const at = now.toISOString();
  const meta = (provenance: Provenance, occurredAt = at) => ({
    native_event_id: null,
    sequence: null,
    occurred_at: occurredAt,
    provenance,
  });

  if (state.awaitingStart && !snapshot.running) {
    return {
      ok: false,
      reason:
        'A prompt was accepted while the OpenCode event stream was disconnected, and whether its turn ran could not be determined.',
    };
  }
  if (state.turn === null && (state.awaitingStart || snapshot.running)) {
    state.awaitingStart = false;
    state.turn = { id: null };
    observations.push(observation('runtime.turn.started', { turn_id: null }, meta(TURN_RULE)));
  }
  if (state.turn === null) return { ok: true, observations };

  if (!snapshot.running) {
    const turnId = state.turn.id;
    const ended = meta(TURN_RULE, toTimestamp(snapshot.idleAt, now));
    switch (snapshot.outcome) {
      case 'succeeded':
        observations.push(observation('runtime.turn.completed', { turn_id: turnId }, ended));
        break;
      case 'interrupted':
        observations.push(observation('runtime.turn.interrupted', { turn_id: turnId }, ended));
        break;
      case 'failed':
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
            ended,
          ),
        );
        break;
      default:
        return {
          ok: false,
          reason:
            'The OpenCode turn ended while the event stream was disconnected, and the session does not say how.',
        };
    }
    endTurn(state);
    return { ok: true, observations };
  }

  const pending = new Set(snapshot.permissions.map((permission) => permission.id));
  for (const id of [...state.approvals]) {
    if (pending.has(id)) continue;
    const decision = state.replies.get(id);
    if (decision === undefined) {
      return {
        ok: false,
        reason: `Approval ${id} was settled while the OpenCode event stream was disconnected, not by a decision Halcyonic sent.`,
      };
    }
    state.approvals.delete(id);
    state.replies.delete(id);
    observations.push(
      observation(
        'runtime.approval.resolved',
        { approval_id: id, decision },
        meta(CONFIRMED_REPLY),
      ),
    );
  }
  for (const permission of snapshot.permissions) {
    if (state.approvals.has(permission.id)) continue;
    const subject = approvalSubject(permission.action, permission.resources);
    if (subject === null) continue;
    state.approvals.add(permission.id);
    observations.push(
      observation(
        'runtime.approval.requested',
        { approval_id: permission.id, subject },
        meta(PERMISSION_LIST),
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
        meta(MESSAGE_LIST),
      ),
    );
  }
  return { ok: true, observations };
}
