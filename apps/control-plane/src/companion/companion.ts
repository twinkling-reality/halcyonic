import {
  COMPANION_MAX_CHARACTERS,
  COMPANION_MAX_QUESTIONS,
  type CompanionExchangeTurn,
  CompanionRepliesRequest,
  CompanionReply,
  type CompanionReplyResponse,
  type CompanionStatus,
  compileValidator,
  type Principal,
  type ValidationIssue,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import type { CompanionConfig } from '../config.ts';
import { WindowCounter } from '../network/limits.ts';
import { type ChatResult, chat, checkModel, type ModelCheck } from './ollama.ts';
import { chatMessages, proposalOnly, readReply } from './prompt.ts';

const validateRequest = compileValidator(CompanionRepliesRequest);
const validateReply = compileValidator(CompanionReply);

/** Replies one principal may ask for in a minute. */
export const REPLIES_PER_MINUTE = 12;

/** The bounds of one reply (ADR 0025); tests shorten the times. */
export interface CompanionBounds {
  /** Until the first line of the reply: waiting behind other requests, loading, reading the prompt. */
  readonly firstTokenMs: number;
  /** The whole turn, a second try included. */
  readonly totalMs: number;
  /** How long the model list may take. */
  readonly listMs: number;
  readonly contextTokens: number;
  readonly outputTokens: number;
  readonly temperature: number;
  /** The most characters of a reply read. */
  readonly maxCharacters: number;
}

export const COMPANION_BOUNDS: CompanionBounds = {
  firstTokenMs: 30_000,
  totalMs: 45_000,
  listMs: 1_000,
  contextTokens: 8_192,
  outputTokens: 512,
  temperature: 0.3,
  maxCharacters: 4_096,
};

export type CompanionAnswer =
  | {
      readonly kind: 'answered';
      readonly body: CompanionReplyResponse;
      readonly log: CompanionLog;
    }
  | {
      readonly kind: 'refused';
      readonly status: number;
      readonly code: string;
      readonly message: string;
      readonly issues?: readonly ValidationIssue[];
      readonly log: CompanionLog;
    };

/** What may be logged of a turn: counts and times, never the person's words or the reply. */
export interface CompanionLog {
  readonly attempts: number;
  readonly first_token_ms: number | null;
  readonly prompt_tokens: number | null;
  readonly output_tokens: number | null;
}

const NO_LOG: CompanionLog = {
  attempts: 0,
  first_token_ms: null,
  prompt_tokens: null,
  output_tokens: null,
};

/**
 * Create's companion (ADR 0025): one reply at a time from a local model through Ollama, to help a
 * person shape an idea into a project name and a first task. It keeps no session and nothing of
 * the exchange: the headset sends the exchange so far with each request. It never touches the
 * journal or a command; what it proposes becomes work only when the person confirms the recap on
 * the headset. Within bounds: one turn at a time for each principal and one on this computer,
 * twelve a minute for each principal, 30 s to the first token and 45 s a turn, after which the
 * request to Ollama is closed, which stops it.
 */
export class Companion {
  readonly #config: CompanionConfig | null;
  readonly #bounds: CompanionBounds;
  readonly #perMinute: WindowCounter;
  readonly #inFlight = new Set<string>();
  #busy = false;

  constructor(options: {
    readonly config: CompanionConfig | null;
    readonly clock: Clock;
    readonly bounds?: Partial<CompanionBounds>;
  }) {
    this.#config = options.config;
    this.#bounds = { ...COMPANION_BOUNDS, ...options.bounds };
    this.#perMinute = new WindowCounter(options.clock, REPLIES_PER_MINUTE, 60_000);
  }

  /** Whether the companion can be asked now, from Ollama's model list alone; the model is not asked. */
  async status(): Promise<CompanionStatus> {
    const config = this.#config;
    if (config === null) return unavailable(NOT_SET_UP);
    const check = await checkModel(config.ollama, config.model, this.#bounds.listMs);
    const problem = problemOf(check);
    if (problem !== null) return unavailable(problem);
    return {
      availability: 'available',
      companion: { name: config.model, served: 'this_mac' },
      max_questions: COMPANION_MAX_QUESTIONS,
    };
  }

  /** One reply to the exchange in `body`, or why there is none. */
  async reply(
    principal: Principal | null,
    body: unknown,
    signal?: AbortSignal,
  ): Promise<CompanionAnswer> {
    const config = this.#config;
    if (config === null) return refused(503, NOT_SET_UP);
    const parsed = validateRequest(body);
    if (!parsed.ok) {
      return {
        ...refused(400, {
          code: 'invalid_companion_request',
          message: 'The request does not match the contract.',
        }),
        issues: parsed.issues,
      };
    }
    const request = parsed.value;
    const shape = exchangeProblem(request.start, request.want, request.messages);
    if (shape !== null) return refused(400, { code: 'invalid_exchange', message: shape });
    const key = principal?.kind === 'device' ? `device:${principal.device_id}` : 'local';
    if (this.#inFlight.has(key)) {
      return refused(429, {
        code: 'companion_busy',
        message: 'The companion is already answering you.',
      });
    }
    if (this.#busy) {
      return refused(503, {
        code: 'companion_busy_on_mac',
        message: 'The companion is answering someone else; try again in a moment.',
      });
    }
    if (!this.#perMinute.take(key)) {
      return refused(429, {
        code: 'rate_limited',
        message: `At most ${REPLIES_PER_MINUTE} replies a minute; wait a moment.`,
      });
    }
    this.#busy = true;
    this.#inFlight.add(key);
    try {
      const check = await checkModel(config.ollama, config.model, this.#bounds.listMs);
      const problem = problemOf(check);
      if (problem !== null) return refused(503, problem);
      const onlyPropose = proposalOnly(request.want, request.messages, COMPANION_MAX_QUESTIONS);
      const messages = chatMessages(request.start, request.messages, onlyPropose);
      const deadline = performance.now() + this.#bounds.totalMs;
      let log: CompanionLog = NO_LOG;
      for (let attempt = 1; attempt <= 2; attempt++) {
        const left = Math.round(deadline - performance.now());
        if (left <= 0) break;
        const result: ChatResult = await chat({
          base: config.ollama,
          model: config.model,
          messages,
          contextTokens: this.#bounds.contextTokens,
          outputTokens: this.#bounds.outputTokens,
          temperature: this.#bounds.temperature,
          firstTokenMs: Math.min(this.#bounds.firstTokenMs, left),
          totalMs: left,
          maxCharacters: this.#bounds.maxCharacters,
          ...(signal === undefined ? {} : { signal }),
        });
        log = {
          attempts: attempt,
          first_token_ms: result.firstTokenMs,
          prompt_tokens: result.kind === 'answered' ? result.promptTokens : null,
          output_tokens: result.kind === 'answered' ? result.outputTokens : null,
        };
        if (result.kind === 'failed') {
          // A reply too long is unreadable, so it may be asked for once more; nothing else is.
          if (result.reason === 'too_long' && attempt === 1) continue;
          return { ...refused(...failureOf(result)), log };
        }
        const reply = readReply(result.content);
        if (
          reply !== null &&
          validateReply(reply).ok &&
          (!onlyPropose || reply.next === 'propose')
        ) {
          return {
            kind: 'answered',
            body: {
              reply,
              provenance: 'reported',
              companion: { name: config.model, served: 'this_mac' },
            },
            log,
          };
        }
      }
      return { ...refused(502, UNREADABLE), log };
    } finally {
      this.#busy = false;
      this.#inFlight.delete(key);
    }
  }
}

const NOT_SET_UP = {
  code: 'companion_not_set_up',
  message: 'The companion is not set up on this computer.',
};
const UNREADABLE = {
  code: 'companion_unreadable',
  message: "The companion's answer could not be read.",
};

function problemOf(check: ModelCheck): { code: string; message: string } | null {
  switch (check.kind) {
    case 'local':
      return null;
    case 'not_running':
      return {
        code: 'companion_not_running',
        message: "The companion's model is not running on this computer.",
      };
    case 'missing':
      return {
        code: 'companion_model_missing',
        message: "The companion's model is not on this computer.",
      };
    case 'remote':
      return {
        code: 'companion_model_not_local',
        message: "The companion's model would run on another computer, so it is not used.",
      };
    default:
      return { code: 'companion_not_running', message: check.message };
  }
}

function failureOf(
  result: Extract<ChatResult, { kind: 'failed' }>,
): [number, { code: string; message: string }] {
  switch (result.reason) {
    case 'too_slow':
      return [504, { code: 'companion_too_slow', message: 'The companion took too long.' }];
    case 'not_running':
      return [503, problemOf({ kind: 'not_running' }) as { code: string; message: string }];
    case 'missing':
      return [503, problemOf({ kind: 'missing' }) as { code: string; message: string }];
    case 'cancelled':
      return [503, { code: 'companion_cancelled', message: 'The request ended before the reply.' }];
    case 'too_long':
      return [502, UNREADABLE];
    default:
      return [502, { code: 'companion_failed', message: result.message }];
  }
}

/**
 * What is wrong with the exchange's order, or null: it holds at most so many characters in all; an
 * idea starts with the person's words; the companion never speaks twice in a row; and a new reply
 * follows the person's words, unless the person asked for the recap or Help me figure it out has
 * only just begun.
 */
export function exchangeProblem(
  start: 'idea' | 'help',
  want: 'next' | 'proposal',
  messages: readonly CompanionExchangeTurn[],
): string | null {
  let characters = 0;
  for (const message of messages) {
    if (message.from === 'person') characters += message.text.length;
    else characters += JSON.stringify(message.reply).length;
  }
  if (characters > COMPANION_MAX_CHARACTERS) {
    return `The exchange holds more than ${COMPANION_MAX_CHARACTERS} characters.`;
  }
  if (start === 'idea' && messages[0]?.from !== 'person') {
    return 'An exchange about an idea starts with the idea.';
  }
  for (let index = 1; index < messages.length; index++) {
    if (messages[index]?.from === 'companion' && messages[index - 1]?.from === 'companion') {
      return 'The companion never replies twice in a row.';
    }
  }
  const last = messages.at(-1);
  if (want === 'next' && last !== undefined && last.from !== 'person') {
    return 'There is nothing new for the companion to answer.';
  }
  return null;
}

function unavailable(reason: { code: string; message: string }): CompanionStatus {
  return { availability: 'unavailable', reason };
}

function refused(
  status: number,
  reason: { code: string; message: string },
): Extract<CompanionAnswer, { kind: 'refused' }> {
  return { kind: 'refused', status, code: reason.code, message: reason.message, log: NO_LOG };
}
