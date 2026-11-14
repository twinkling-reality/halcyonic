import {
  COMMAND_SCHEMA_VERSION,
  type CommandId,
  compileValidator,
  type EventEnvelope,
  NativeId,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import {
  type Clock,
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import type { CommandService } from './command-service.ts';
import { APPROVABLE_LIMIT, type Cause, controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import type { EventPublisher } from './publisher.ts';
import type { Recorder } from './recorder.ts';

const CLIENT = { name: 'halcyonic-control-plane', version: null, device_label: null } as const;

/**
 * What the agent is told, where its app carries a denial's words, when its request was too long to show: why, and
 * nothing more. No way round is suggested while a file write's approval shows only its path (the lead, 2026-11-13
 * 19:46Z): "write it as a file" comes back only once a write's card shows what it writes (AUDIT2 W1, AUDIT3).
 */
export function tooLongWords(length: number): string {
  return `This request is ${length} characters long, and a person can be shown at most ${APPROVABLE_LIMIT} whole, so it was not done.`;
}

/**
 * What the agent is told, where its app carries a denial's words, when its request could not be recorded: that no
 * person saw it and it was not done, with no advice and never the request's own text (the words reviewer, 2026-11-13).
 */
export const NOT_RECORDED_WORDS =
  'This request could not be shown to a person, so it was not done.';

export interface UnrecordedRequestsDeps {
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly adapter: RuntimeAdapter;
  readonly execution: ExecutionContext;
  readonly clock: Clock;
  readonly logger: Logger;
}

/** The contract's own check of a native id, so the host keeps no id the journal would refuse. */
const validNativeId = compileValidator(NativeId);

/**
 * The host's answer to a request its journal could not record (ADR 0065, 2026-11-13): an agent app's approval request
 * the contract refuses is never a card, so the agent would wait on it until its run's bound. For one with an id the
 * agent app can be answered by, and that the projection does not hold pending, the host records
 * `execution.approval.denied_by_host` because `not_recorded`, then denies it through the agent app itself, not through
 * the command a press sends: admission refuses a decision on a request it never saw. The denial is sent even where its
 * record fails, since a refusal nobody sees still frees the agent and a wait nobody sees does not. A question has no
 * declared way to be declined (`answerQuestion` carries answers only), so its turn is ended instead
 * (`unrecordedQuestion`). Logs carry ids, codes and counts, never the request's text.
 */
export function unrecordedRequests(
  deps: UnrecordedRequestsDeps,
): (observation: RuntimeObservation) => void {
  const { adapter, execution, logger } = deps;
  const runtimeId = adapter.descriptor.runtime_id;
  const ids = { execution_id: execution.execution_id, runtime_id: runtimeId };
  const question = unrecordedQuestion(deps);
  return (observation) => {
    if (observation.type === 'runtime.question.asked') {
      question(observation);
      return;
    }
    if (observation.type !== 'runtime.approval.requested') return;
    const approvalId = (observation.payload as { approval_id?: unknown } | null)?.approval_id;
    if (typeof approvalId !== 'string' || approvalId.length === 0 || approvalId.length > 512) {
      logger.error(ids, 'an unrecorded request has no id it can be denied by');
      return;
    }
    // Recorded after all, as when a later step of the write failed: the person decides it.
    const view = deps.projection.execution(execution.execution_id);
    if (view?.pending_approvals.some((each) => each.approval_id === approvalId) === true) return;
    const respond = adapter.respondToApproval;
    if (adapter.descriptor.capabilities.respond_to_approval !== true || respond === undefined) {
      logger.error(ids, 'an unrecorded request could not be denied: its app takes no decisions');
      return;
    }
    const delivered = adapter.descriptor.delivers_denial_message === true;
    let recorded = false;
    try {
      deps.recorder.record(
        controlPlaneDraft(
          'execution.approval.denied_by_host',
          execution,
          { approval_id: approvalId, because: 'not_recorded', delivered },
          deps.clock.now().toISOString() as Timestamp,
          NO_CAUSE,
        ),
      );
      recorded = true;
    } catch {
      // Not the error itself: it can hold the request.
    }
    // After the adapter's own emit returns, outside its call.
    void Promise.resolve()
      .then(() =>
        respond.call(adapter, {
          execution,
          approval_id: approvalId,
          decision: 'deny',
          message: NOT_RECORDED_WORDS,
        }),
      )
      .then(
        () =>
          logger.warn(
            { ...ids, recorded, delivered, outcome: 'denied' },
            'denied a request that was not recorded',
          ),
        (error: unknown) =>
          logger.error(
            {
              ...ids,
              recorded,
              delivered,
              outcome: 'failed',
              code:
                error instanceof RuntimeActionError
                  ? error.code
                  : error instanceof Error
                    ? error.name
                    : typeof error,
            },
            'a request that was not recorded could not be denied',
          ),
      );
  };
}

/** What came of the host's ask to end a refused question's turn: a fixed word, the one thing its log says. */
type AskOutcome =
  | 'interrupt_requested'
  | 'interrupt_refused'
  | 'interrupt_already_asked'
  | 'no_interrupt_declared';

/** Why a refused question asks nothing: its id is one the contract refuses, or it was recorded after all. */
type QuestionOutcome = AskOutcome | 'no_valid_id' | 'recorded_after_all';

/**
 * What becomes of a question the journal could not record (ADR 0065 point 6, AUDIT3 F1): the agent waits on it and no
 * person can answer it, and no agent app declares a way to decline one, so the host asks the agent app to end its turn
 * (`interrupt`), at most once in a turn and never again after a refusal, and then records
 * `execution.question.ended_by_host` with whether the ask was taken. The turn interrupted is always the execution this
 * sink was made for, never one the refused payload names. Its end is recorded only from the runtime's own
 * `runtime.turn.interrupted`. No question is answered, resolved or dismissed here, and one the projection holds open
 * with the same id was recorded after all: it is the person's, and left as it is. A question id the contract would
 * refuse is kept nowhere. Logs carry the execution's and runtime's ids and the outcome only, never the question's id,
 * which the agent chose, or its words.
 */
function unrecordedQuestion(
  deps: UnrecordedRequestsDeps,
): (observation: RuntimeObservation) => void {
  const { adapter, execution, logger } = deps;
  const ids = { execution_id: execution.execution_id, runtime_id: adapter.descriptor.runtime_id };
  const log = (outcome: QuestionOutcome | `${AskOutcome}_not_recorded`) =>
    logger.warn({ ...ids, outcome }, 'a question that was not recorded');
  /** The turns (by the projection's count) the host has already asked to end, whatever came of it. */
  const asked = new Set<number>();
  const record = (questionId: string, requested: boolean) => {
    try {
      deps.recorder.record(
        controlPlaneDraft(
          'execution.question.ended_by_host',
          execution,
          { question_id: questionId, because: 'not_recorded', interrupt_requested: requested },
          deps.clock.now().toISOString() as Timestamp,
          NO_CAUSE,
        ),
      );
      return true;
    } catch {
      // Not the error itself: it can hold the question.
      return false;
    }
  };
  return (observation) => {
    const given = (observation.payload as { question_id?: unknown } | null)?.question_id;
    const checked = validNativeId(given);
    if (!checked.ok) {
      log('no_valid_id');
      return;
    }
    const questionId = checked.value;
    const view = deps.projection.execution(execution.execution_id);
    if (view?.pending_questions.some((each) => each.question_id === questionId) === true) {
      log('recorded_after_all');
      return;
    }
    const turn = view?.turn_count ?? 0;
    const interrupt = adapter.interrupt;
    const declared = adapter.descriptor.capabilities.interrupt === true && interrupt !== undefined;
    const call = declared && !asked.has(turn);
    if (call) asked.add(turn);
    // After the adapter's own emit returns, outside its call. Each on its own, so an ask that never settles holds up
    // no other question's record.
    void Promise.resolve().then(async () => {
      let requested = false;
      let outcome: AskOutcome = declared ? 'interrupt_already_asked' : 'no_interrupt_declared';
      if (call && interrupt !== undefined) {
        try {
          await interrupt.call(adapter, { execution });
          requested = true;
          outcome = 'interrupt_requested';
        } catch {
          // Not the error itself: it can quote the agent. Never asked again in this turn.
          outcome = 'interrupt_refused';
        }
      }
      log(record(questionId, requested) ? outcome : `${outcome}_not_recorded`);
    });
  };
}

/** What happens to a request: the host answers it, the host denies it for length, or a person decides. */
export type RequestOutcome = 'answer' | 'deny' | 'person';

/**
 * The ONE decision for each request (AUDIT3 M1, 2026-11-13), which the host's answers and its denials both act on, so
 * neither ordering of their listeners lets both act or neither: a confined app's own work the no-ask rule takes is
 * answered by the host whatever its length, since no person reads it and the closed profile holds it (`answer`); a
 * request that would reach a person and is too long to show whole is denied with its reason (`deny`); anything else
 * reaches the person.
 */
export function requestOutcome(event: EventEnvelope, hostAnswers: boolean): RequestOutcome {
  if (event.event_type !== 'runtime.approval.requested') return 'person';
  if (hostAnswers) return 'answer';
  return event.payload.too_long === undefined ? 'person' : 'deny';
}

export interface HostDenialsDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly publisher: EventPublisher;
  readonly commands: Pick<CommandService, 'submit'>;
  /** Whether the runtime declares that a denial's words reach its agent (`delivers_denial_message`). */
  readonly delivers: (runtimeId: string) => boolean;
  /** The one decision for a request (`requestOutcome`); this acts only on `deny`. */
  readonly outcome: (event: EventEnvelope) => RequestOutcome;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/**
 * The host's own denial of a request a person cannot be shown whole (`too_long` on the request, set where the control
 * plane fits it), for every run, confined or not (AUDIT3, 2026-11-13): it records `execution.approval.denied_by_host`
 * first, then denies through the same command a press sends, with words the agent can act on where its app carries
 * them. So no card that can only be denied is left waiting. A denial that is not accepted leaves the card, as before.
 */
export class HostDenials {
  readonly #deps: HostDenialsDeps;
  #unsubscribe: (() => void) | null = null;
  #closed = false;

  constructor(deps: HostDenialsDeps) {
    this.#deps = deps;
  }

  attach(): void {
    if (!this.#deps.live) return;
    this.#unsubscribe = this.#deps.publisher.subscribe(({ event }) => this.#on(event));
  }

  close(): void {
    this.#closed = true;
    this.#unsubscribe?.();
    this.#unsubscribe = null;
  }

  #on(event: EventEnvelope): void {
    if (this.#closed || event.event_type !== 'runtime.approval.requested') return;
    const tooLong = event.payload.too_long;
    if (tooLong === undefined || event.execution_id === null) return;
    if (this.#deps.outcome(event) !== 'deny') return;
    const approvalId = event.payload.approval_id;
    // After the request is projected, so the denial finds it pending.
    queueMicrotask(() => {
      if (this.#closed) return;
      try {
        this.#deny(event, approvalId, tooLong.length);
      } catch {
        // Not the error itself: it can hold the request. The card stays for the person.
        this.#deps.logger.error({ execution_id: event.execution_id }, 'host denial failed');
      }
    });
  }

  #deny(event: EventEnvelope, approvalId: string, length: number): void {
    const executionId = event.execution_id;
    if (executionId === null) return;
    const view = this.#deps.projection.execution(executionId);
    if (view?.pending_approvals.some((each) => each.approval_id === approvalId) !== true) return;
    const delivered = this.#deps.delivers(view.runtime.runtime_id);
    const scope = {
      project_id: event.project_id,
      workstream_id: event.workstream_id,
      execution_id: executionId,
    };
    const cause = {
      correlation_id: event.correlation_id,
      causation_id: event.event_id,
    } as unknown as Cause;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.approval.denied_by_host',
        scope as never,
        { approval_id: approvalId, because: 'too_long_to_show', length, delivered },
        this.#now(),
        cause,
      ),
    );
    const commandId = this.#deps.ids.next() as CommandId;
    const outcome = this.#deps.commands.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId,
        issued_at: this.#now(),
        client: CLIENT,
        command_type: 'execution.respond_to_approval',
        payload: {
          execution_id: executionId,
          approval_id: approvalId,
          decision: 'deny',
          message: tooLongWords(length),
        },
      },
      'internal',
      null,
    );
    this.#deps.logger.info(
      {
        execution_id: executionId,
        command_id: commandId,
        delivered,
        disposition: outcome.disposition,
      },
      'denied a request too long to show',
    );
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}
