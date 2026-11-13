import { isAbsolute, join } from 'node:path';
import type { ApprovalChange, ToolResult } from '@halcyonic/contracts';
import {
  type CommandId,
  type ControlPlaneEvent,
  type EventOf,
  fitQuestion,
  type QuestionPrompt,
  type RuntimeId,
  type Timestamp,
} from '@halcyonic/contracts';
import type { ExecutionContext, ObservationSink } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';
import { resolvedPath } from './real-path.ts';
import type { EventDraft, Recorder } from './recorder.ts';
import { fitted, type Redaction, redaction as redactionOf, withinLimit } from './redaction.ts';
import { normalizedToolResult } from './tool-result-redaction.ts';

type ControlPlaneEventType = ControlPlaneEvent['event_type'];
type ScopeOf<T extends ControlPlaneEventType> = Pick<
  EventOf<T>,
  'project_id' | 'workstream_id' | 'execution_id'
>;

export interface Cause {
  readonly correlation_id: string | null;
  readonly causation_id: string | null;
}

export const NO_CAUSE: Cause = { correlation_id: null, causation_id: null };

export function causedBy(commandId: CommandId): Cause {
  return { correlation_id: commandId, causation_id: commandId };
}

/**
 * Builds a control plane event draft. TypeScript cannot correlate a generic event type with its
 * payload inside the union, so the object is asserted here, in one place, and the recorder
 * validates it against the contract before anything is written.
 */
export function controlPlaneDraft<T extends ControlPlaneEventType>(
  eventType: T,
  scope: ScopeOf<T>,
  payload: EventOf<T>['payload'],
  occurredAt: Timestamp,
  cause: Cause,
): EventDraft {
  return {
    event_type: eventType,
    project_id: scope.project_id,
    workstream_id: scope.workstream_id,
    execution_id: scope.execution_id,
    source: { kind: 'control_plane' },
    source_native_id: null,
    sequence: null,
    occurred_at: occurredAt,
    correlation_id: cause.correlation_id,
    causation_id: cause.causation_id,
    provenance: { epistemic: 'observed', native_type: null },
    payload,
  } as unknown as EventDraft;
}

/**
 * Turns an adapter's observations into journaled runtime events for one execution. An invalid
 * observation is an adapter defect: it is logged with its identifiers and dropped, never
 * repaired into something that looks valid.
 */
export function createObservationSink(
  recorder: Recorder,
  logger: Logger,
  execution: ExecutionContext,
  runtimeId: RuntimeId,
  redaction: Redaction = redactionOf(),
  /** The task's folder, where a change's relative path lands; the host adds where it really writes (W1). */
  folder: string | null = null,
): ObservationSink {
  return (observation) => {
    try {
      const draft = {
        event_type: observation.type,
        project_id: execution.project_id,
        workstream_id: execution.workstream_id,
        execution_id: execution.execution_id,
        source: { kind: 'runtime', runtime_id: runtimeId },
        source_native_id: observation.native_event_id,
        sequence: observation.sequence,
        occurred_at: observation.occurred_at,
        correlation_id: null,
        causation_id: null,
        provenance: observation.provenance,
        // Inside the try: a payload too malformed to clean is an adapter defect like any other.
        payload: withoutCredentials(observation, redaction, folder),
      } as unknown as EventDraft;
      recorder.record(draft);
    } catch (error) {
      logger.error(
        {
          err: error,
          execution_id: execution.execution_id,
          runtime_id: runtimeId,
          observation_type: observation.type,
        },
        'runtime observation rejected',
      );
    }
  };
}

/** What an approval's request keeps for the record, whole: the bound an agent's message has. */
export const SUMMARY_LIMIT = 32000;

/**
 * The longest request a person can approve: one the headset's card shows whole. 2000 until the card pages a longer
 * one and holds Approve to its last part (AUDIT3, 2026-11-13); beyond it the host denies the step with its reason.
 */
export const APPROVABLE_LIMIT = 2000;

/**
 * `text` with every character a card does not draw written as its escape (`\u{202E}`): C0 and C1 controls but tab and
 * newline, carriage return, the bidirectional overrides and isolates, the zero-width characters and the byte order
 * mark, so a request cannot read otherwise than it runs (AUDIT3 L1).
 */
export function visible(text: string): string {
  return text.replace(
    // biome-ignore lint/suspicious/noControlCharactersInRegex: finding control characters is the rule.
    /[\u0000-\u0008\u000B-\u001F\u007F-\u009F\u00AD\u061C\u200B-\u200F\u2028-\u202E\u2060\u2066-\u2069\uFEFF\u{E0000}-\u{E007F}]/gu,
    (c) => `\\u{${(c.codePointAt(0) ?? 0).toString(16).toUpperCase().padStart(4, '0')}}`,
  );
}

/**
 * One change of a request, each text part with what Halcyonic holds taken out and what a card does not draw escaped,
 * kept whole up to the record's bound (`whole` before fitting, `fitted` after, `cut` where any part was cut), and
 * `real_path` set by the host where the path, or a folder on it, is a link (AUDIT3 W1).
 */
function fittedChange(
  change: ApprovalChange,
  redaction: Redaction,
  folder: string | null,
): { readonly whole: ApprovalChange; readonly fitted: ApprovalChange; readonly cut: boolean } {
  const shown = (text: string | null) => (text === null ? null : visible(redaction.held(text)));
  const real = (path: string) => {
    if (folder === null && !isAbsolute(path)) return null;
    const plain = isAbsolute(path) ? join(path) : join(folder ?? '', path);
    const resolved = resolvedPath(plain);
    return resolved === plain ? null : resolved;
  };
  const whole: ApprovalChange = {
    ...change,
    path: visible(change.path),
    real_path: real(change.path),
    to_path: change.to_path === null ? null : visible(change.to_path),
    before: shown(change.before),
    after: shown(change.after),
  };
  const fit = (text: string | null, limit: number) => (text === null ? null : fitted(text, limit));
  const fittedOne: ApprovalChange = {
    ...whole,
    path: fitted(whole.path, 4096),
    real_path: fit(whole.real_path, 4096),
    to_path: fit(whole.to_path, 4096),
    before: fit(whole.before, SUMMARY_LIMIT),
    after: fit(whole.after, SUMMARY_LIMIT),
  };
  const cut =
    fittedOne.path !== whole.path ||
    fittedOne.real_path !== whole.real_path ||
    fittedOne.to_path !== whole.to_path ||
    fittedOne.before !== whole.before ||
    fittedOne.after !== whole.after;
  return { whole, fitted: fittedOne, cut };
}

/**
 * An observation's payload with credentials taken out, then cut to the contract's limits, since
 * what stands in for a credential can be longer than it, and a held secret is found only whole:
 * the adapters pass these fields uncut, and this is the one place they are cut. The runtime's error
 * text, a turn's failure and why the connection was lost or restored, loses what Halcyonic holds
 * and every credential shape. What a person reads to decide, a tool's title, what an approval asks
 * for, a question's prompt, a test run's label and summary, loses only exact copies of what
 * Halcyonic holds, so the command a person approves is never guessed away; so does the agent's own
 * message, which can echo a key it was given (2026-11-03). A question's options are left as given,
 * since an answer names them. Everything else is reported as given.
 */
function withoutCredentials(
  observation: Parameters<ObservationSink>[0],
  redaction: Redaction,
  folder: string | null = null,
): unknown {
  const payload = observation.payload as Record<string, unknown>;
  const held = (field: string, limit: number) =>
    typeof payload[field] === 'string'
      ? { ...payload, [field]: fitted(redaction.held(payload[field]), limit) }
      : payload;
  switch (observation.type) {
    case 'runtime.turn.failed': {
      const error = payload.error as { message?: unknown } | undefined;
      return typeof error?.message === 'string'
        ? {
            ...payload,
            error: { ...error, message: withinLimit(redaction.errorText(error.message)) },
          }
        : payload;
    }
    case 'runtime.connection.lost':
    case 'runtime.connection.restored':
      return typeof payload.reason === 'string'
        ? { ...payload, reason: withinLimit(redaction.errorText(payload.reason)) }
        : payload;
    case 'runtime.approval.requested': {
      // `too_long` is the host's own fact, never an adapter's: whatever the observation carried is dropped here.
      const { too_long: _adapters, ...given } = payload;
      const subject = given.subject as { summary?: unknown; changes?: unknown } | undefined;
      if (typeof subject?.summary !== 'string') return given;
      // What a card does not draw is drawn as its escape, so what is shown is what runs (AUDIT3 L1).
      const held = visible(redaction.held(subject.summary));
      const summary = fitted(held, SUMMARY_LIMIT);
      // Every change the request makes, each part escaped and kept whole up to the record's bound, with where the
      // write really lands (ADR 0065, AUDIT3 W1, W3).
      const changes = Array.isArray(subject.changes)
        ? subject.changes.map((change) => fittedChange(change as ApprovalChange, redaction, folder))
        : undefined;
      const parts = [
        held,
        ...(changes ?? []).flatMap((change) => [
          change.whole.path,
          change.whole.real_path ?? '',
          change.whole.to_path ?? '',
          change.whole.before ?? '',
          change.whole.after ?? '',
        ]),
      ];
      // One bound for all a person reads before Approve, counted in code points as the contract counts text.
      const length = parts.reduce((sum, part) => sum + [...part].length, 0);
      const cut = summary !== held || (changes ?? []).some((change) => change.cut);
      const tooLong = length > APPROVABLE_LIMIT;
      return {
        ...given,
        subject: {
          ...subject,
          summary,
          ...(changes !== undefined && { changes: changes.map((change) => change.fitted) }),
        },
        complete: given.complete === true && !cut && !tooLong,
        ...(tooLong && { too_long: { length, reported_complete: given.complete === true } }),
      };
    }
    case 'runtime.question.asked':
      return fittedQuestion(payload, redaction);
    case 'runtime.tool.started':
      return held('title', 500);
    case 'runtime.tool.result':
      return normalizedToolResult(observation.payload as ToolResult, redaction);
    case 'runtime.test_run.started':
      return held('label', 500);
    case 'runtime.test_run.completed':
      return held('summary', 2000);
    case 'runtime.agent_message':
      return held('text', 32000);
    default:
      return payload;
  }
}

/**
 * A question with what Halcyonic holds taken out of each prompt's text, then fitted to the contract
 * with `fitQuestion`, here and nowhere else: the adapters report it whole, since a secret cut in two
 * is no longer found. A question with anything cut can no longer be answered. Its options are left
 * as given, since an answer names them.
 */
function fittedQuestion(payload: Record<string, unknown>, redaction: Redaction): unknown {
  const asked = payload as { prompts: readonly QuestionPrompt[]; answerable: boolean };
  const held = asked.prompts.map((prompt) => ({ ...prompt, text: redaction.held(prompt.text) }));
  const fitted = fitQuestion(held, asked.answerable);
  return { ...payload, prompts: [...fitted.prompts], answerable: fitted.answerable };
}
