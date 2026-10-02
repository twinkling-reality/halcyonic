import { setTimeout as sleep } from 'node:timers/promises';
import type { ErrorInfo } from '@halcyonic/contracts';
import {
  checkCredential,
  connect,
  credentialRejected,
  DEFAULT_TIMEOUT_MS,
  type Failure,
  fail,
  invalid,
  isFailure,
  parseJson,
  refusal,
  type SalidiumOptions,
} from './connection.ts';
import { EventStreamError, SseParser } from './sse.ts';
import { CONSUMER_BASE_PATH, readFeedMessage, type WireFeedMessage } from './wire.ts';

type FeedFailure = Failure<'unavailable' | 'incompatible' | 'unauthorized'>;

/**
 * What the feed tells its consumer. The feed carries notifications, not state:
 * - `resync`: connected (again). Discard what you hold and re-read the executions you show.
 * - `session_changed`: a report changed. Refresh the matching execution, passing `sessionId` to
 *   `SalidiumClient.understand`. Salidium sends one only when the evidence sequence, status or
 *   explanation status changed.
 * - `session_removed`: Salidium deleted the session or it expired.
 * - `disconnected`: not connected, and why. Sent when the reason changes, not on every retry.
 */
export type SalidiumFeedEvent =
  | { readonly type: 'resync' }
  | {
      readonly type: 'session_changed';
      readonly sessionId: string;
      readonly provider: string;
      readonly nativeId: string;
      readonly evidenceSequence: number;
    }
  | {
      readonly type: 'session_removed';
      readonly sessionId: string;
      readonly provider: string | null;
      readonly nativeId: string | null;
    }
  | {
      readonly type: 'disconnected';
      readonly availability: FeedFailure['availability'];
      readonly reason: ErrorInfo;
    };

export interface SalidiumFeedOptions extends Omit<SalidiumOptions, 'credential'> {
  /** The consumer credential the person created for Halcyonic, or null when none is configured. */
  readonly credential: string | null;
  /**
   * Called in order. It must not throw: an exception is not caught, stops the feed, and is thrown
   * again by `close()`, or surfaces as an unhandled rejection if `close()` is never called.
   */
  readonly onEvent: (event: SalidiumFeedEvent) => void;
  /** Silence after which the connection is presumed dead: three missed heartbeats by default. */
  readonly silenceMs?: number;
  /** The first reconnection delay, doubled after each failed attempt up to `maxDelayMs`. */
  readonly initialDelayMs?: number;
  readonly maxDelayMs?: number;
}

export interface SalidiumFeed {
  /** Stops the feed; resolves when its connection is closed. */
  close(): Promise<void>;
}

/** Salidium sends a heartbeat every fifteen seconds. */
const HEARTBEAT_MS = 15_000;
/** Feed messages are a few hundred bytes; Salidium itself drops a reader 1 MiB behind. */
const MAX_EVENT_LENGTH = 1024 * 1024;

/**
 * Follows Salidium's change feed: one connection per daemon instance, opened only after the
 * instance check, and reopened with backoff after `closing`, a dropped connection, three missed
 * heartbeats, or a failed attempt. There is no replay, so every connection starts with `resync`.
 */
export function openSalidiumFeed(options: SalidiumFeedOptions): SalidiumFeed {
  const controller = new AbortController();
  const done = run(options, controller.signal);
  return {
    close: async () => {
      controller.abort();
      await done;
    },
  };
}

export function reconnectDelay(failures: number, initialMs: number, maxMs: number): number {
  return Math.min(initialMs * 2 ** failures, maxMs);
}

async function run(options: SalidiumFeedOptions, signal: AbortSignal): Promise<void> {
  const initialMs = options.initialDelayMs ?? 1_000;
  const maxMs = options.maxDelayMs ?? 30_000;
  let failures = 0;
  let reported: string | null = null;
  const connected = () => {
    failures = 0;
    reported = null;
  };
  while (!signal.aborted) {
    let outcome: FeedFailure;
    try {
      outcome = await follow(options, signal, connected);
    } catch (error) {
      if (signal.aborted) return;
      throw error;
    }
    if (signal.aborted) return;
    const key = `${outcome.availability}/${outcome.reason.code}`;
    if (key !== reported) {
      reported = key;
      options.onEvent({
        type: 'disconnected',
        availability: outcome.availability,
        reason: outcome.reason,
      });
    }
    try {
      await sleep(reconnectDelay(failures, initialMs, maxMs), undefined, { signal });
    } catch {
      return;
    }
    failures++;
  }
}

/** One connection, from the instance check to its end. Resolves with why it ended. */
async function follow(
  options: SalidiumFeedOptions,
  signal: AbortSignal,
  connected: () => void,
): Promise<FeedFailure> {
  const instance = await connect(options.home, options.timeoutMs ?? DEFAULT_TIMEOUT_MS, signal);
  if (isFailure(instance)) return instance;
  const credential = checkCredential(options.credential);
  if (typeof credential !== 'string') return credential;

  const silenceMs = options.silenceMs ?? 3 * HEARTBEAT_MS;
  const connection = new AbortController();
  const stop = () => connection.abort();
  signal.addEventListener('abort', stop);
  let silent = false;
  const onSilence = () => {
    silent = true;
    connection.abort();
  };
  let timer = setTimeout(onSilence, silenceMs);
  const lost = () =>
    silent
      ? fail(
          'unavailable',
          'heartbeats_missed',
          `Salidium's feed sent nothing for ${silenceMs} ms, so the connection is presumed dead.`,
        )
      : fail('unavailable', 'unreachable', "Salidium's feed connection failed.");
  try {
    let response: Response;
    try {
      response = await fetch(new URL(`${CONSUMER_BASE_PATH}/feed`, instance.origin), {
        headers: { accept: 'text/event-stream', authorization: `Bearer ${credential}` },
        redirect: 'error',
        signal: connection.signal,
      });
    } catch {
      return lost();
    }
    const type = response.headers.get('content-type') ?? '';
    if (response.status !== 200 || !type.startsWith('text/event-stream') || !response.body) {
      // An error answer is a short contract document; anything else is not read.
      const body =
        response.status === 200 ? undefined : parseJson(await response.text().catch(() => ''));
      if (response.status === 200) await response.body?.cancel().catch(() => {});
      if (response.status === 401) return credentialRejected();
      const refused = refusal(response.status, body, 'feed');
      if (refused) return refused;
      return fail(
        'incompatible',
        'unexpected_status',
        `Salidium answered the feed request with HTTP ${response.status} and ${type || 'no content type'}, which consumer contract v1 does not give.`,
      );
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    const parser = new SseParser(MAX_EVENT_LENGTH);
    let resynced = false;
    let closing: 'credential-revoked' | 'shutting-down' | null = null;
    for (;;) {
      let chunk: Awaited<ReturnType<typeof reader.read>>;
      try {
        chunk = await reader.read();
      } catch {
        return lost();
      }
      if (chunk.done) break;
      clearTimeout(timer);
      timer = setTimeout(onSilence, silenceMs);
      let events: string[];
      try {
        events = parser.push(decoder.decode(chunk.value, { stream: true }));
      } catch (error) {
        if (!(error instanceof EventStreamError)) throw error;
        return fail(
          'incompatible',
          'invalid_document',
          `Salidium's feed is not a valid event stream: ${error.message}.`,
        );
      }
      for (const data of events) {
        const message = parse(data);
        if (message === null) continue;
        if (isFailure(message)) return message;
        if (!resynced && message.type !== 'resync')
          return fail(
            'incompatible',
            'invalid_document',
            "Salidium's feed did not start with resync.",
          );
        switch (message.type) {
          case 'resync':
            resynced = true;
            connected();
            options.onEvent({ type: 'resync' });
            break;
          case 'session.changed':
            options.onEvent({
              type: 'session_changed',
              sessionId: message.sessionId,
              provider: message.native.provider,
              nativeId: message.native.sessionId,
              evidenceSequence: message.evidenceSeq,
            });
            break;
          case 'session.removed':
            options.onEvent({
              type: 'session_removed',
              sessionId: message.sessionId,
              provider: message.native?.provider ?? null,
              nativeId: message.native?.sessionId ?? null,
            });
            break;
          case 'heartbeat':
            break;
          case 'closing':
            closing = message.reason;
            break;
        }
      }
    }
    if (closing === 'credential-revoked')
      return fail(
        'unauthorized',
        'credential_revoked',
        'Salidium revoked the consumer credential and closed the feed.',
      );
    if (closing === 'shutting-down')
      return fail('unavailable', 'shutting_down', 'Salidium is stopping and closed the feed.');
    return fail('unavailable', 'connection_closed', 'Salidium closed the feed without saying why.');
  } finally {
    clearTimeout(timer);
    signal.removeEventListener('abort', stop);
    connection.abort();
  }
}

/** A known message, `null` for a type this version does not know, or why the message is invalid. */
function parse(data: string): WireFeedMessage | null | Failure<'incompatible'> {
  let value: unknown;
  try {
    value = JSON.parse(data);
  } catch {
    return fail('incompatible', 'invalid_document', "A message in Salidium's feed is not JSON.");
  }
  const message = readFeedMessage(value);
  if (message === null) return null;
  return message.ok ? message.value : invalid('feed message', message.issues);
}
