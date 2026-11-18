import {
  CLOSING_WORDS_LIMIT,
  type EventEnvelope,
  type ExecutionId,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';
import { type Cause, controlPlaneDraft } from './drafts.ts';
import { cleanWords } from './prepare.ts';
import type { EventPublisher } from './publisher.ts';
import type { Recorder } from './recorder.ts';

/**
 * Halcyonic's closing request, sent after the person's words on every start and every instruction (the lead,
 * 2026-11-18), so each turn can end with the agent's own plain sentences for the evidence view's places. It asks for
 * one sentence a place, for someone who has never written code, and says a place may be left out, so nothing is
 * invented (AUDIT2, 03:07Z). Declared in docs/internal/architecture/SECURITY.md as part of what Halcyonic sends.
 */
export const CLOSING_REQUEST = [
  'From Halora: when you finish, end your last message with these four lines, each one plain sentence for someone who has never written code:',
  'Did: what you did.',
  'Why: why you did it that way.',
  'How: how it works now.',
  'Left: what is left to do.',
  'Leave a line out when there is nothing true to say for it. Never guess.',
].join('\n');

/** The person's words, then, apart, the closing request. */
export function withClosingRequest(words: string): string {
  return `${words}\n\n${CLOSING_REQUEST}`;
}

export interface ClosingPlaces {
  readonly did: string | null;
  readonly why: string | null;
  readonly how: string | null;
  readonly left: string | null;
}

const LABELS = ['did', 'why', 'how', 'left'] as const;

/**
 * A line that answers one place: its label at the line's start, after any list mark or emphasis, then a colon, as
 * `Did: ...` or `- **Left:** ...`. The words after it are the agent's.
 */
const PLACE_LINE =
  /^\s*(?:[-*]\s+)?(?:\*\*|__)?(did|why|how|left)(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(.*)$/i;

/**
 * The places a message answers, each from the last line that answers it, its words in one line and cut to
 * `CLOSING_WORDS_LIMIT`; null for a place no line answers or a line left empty. Null when no place is answered at
 * all: the message did not answer the closing request, and nothing is made of it.
 */
export function closingPlacesOf(message: string): ClosingPlaces | null {
  const found: Record<(typeof LABELS)[number], string | null> = {
    did: null,
    why: null,
    how: null,
    left: null,
  };
  let any = false;
  for (const line of message.split(/\r?\n/)) {
    const match = PLACE_LINE.exec(line);
    if (match === null) continue;
    const label = match[1]?.toLowerCase() as (typeof LABELS)[number];
    const words = cleanWords(
      (match[2] ?? '').replace(/(?:\*\*|__)\s*$/, ''),
      true,
      CLOSING_WORDS_LIMIT,
    );
    found[label] = words.length === 0 ? null : words;
    any ||= words.length > 0;
  }
  return any ? found : null;
}

export interface ClosingWordsDeps {
  readonly recorder: Recorder;
  readonly publisher: EventPublisher;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/**
 * Follows the journal: at each turn's completion, reads the turn's last agent message, as journaled (after its
 * redaction), for the closing request's places, and records them as `execution.closing_words`, `reported`, the
 * agent's claim. A turn whose last message answers none records nothing; a failed or interrupted turn records nothing.
 * Logs carry ids only, never the words.
 */
export class ClosingWords {
  readonly #deps: ClosingWordsDeps;
  /** The latest agent message of the turn running, by execution; cleared as a turn starts or ends. */
  readonly #last = new Map<ExecutionId, string>();
  #unsubscribe: (() => void) | null = null;

  constructor(deps: ClosingWordsDeps) {
    this.#deps = deps;
  }

  attach(): void {
    if (!this.#deps.live) return;
    this.#unsubscribe = this.#deps.publisher.subscribe(({ event }) => this.#on(event));
  }

  close(): void {
    this.#unsubscribe?.();
    this.#unsubscribe = null;
    this.#last.clear();
  }

  #on(event: EventEnvelope): void {
    const executionId = event.execution_id;
    if (executionId === null) return;
    switch (event.event_type) {
      case 'runtime.agent_message':
        this.#last.set(executionId, event.payload.text);
        return;
      case 'runtime.turn.started':
      case 'runtime.turn.failed':
      case 'runtime.turn.interrupted':
        this.#last.delete(executionId);
        return;
      case 'runtime.turn.completed': {
        const message = this.#last.get(executionId);
        this.#last.delete(executionId);
        if (message === undefined) return;
        const places = closingPlacesOf(message);
        if (places === null) return;
        this.#record(event, executionId, event.payload.turn_id, places);
        return;
      }
      default:
        return;
    }
  }

  #record(
    event: EventEnvelope,
    executionId: ExecutionId,
    turnId: string | null,
    places: ClosingPlaces,
  ): void {
    const scope = {
      project_id: event.project_id,
      workstream_id: event.workstream_id,
      execution_id: executionId,
    };
    const cause = {
      correlation_id: event.correlation_id,
      causation_id: event.event_id,
    } as unknown as Cause;
    try {
      this.#deps.recorder.record({
        ...controlPlaneDraft(
          'execution.closing_words',
          scope as never,
          { turn_id: turnId, ...places },
          this.#deps.clock.now().toISOString() as Timestamp,
          cause,
        ),
        // The agent's own words: its claim, never an observation (AGENTS.md).
        provenance: { epistemic: 'reported', native_type: null },
      } as never);
    } catch {
      // Not the error itself: it can hold the agent's words.
      this.#deps.logger.error({ execution_id: executionId }, 'closing words could not be recorded');
    }
  }
}
