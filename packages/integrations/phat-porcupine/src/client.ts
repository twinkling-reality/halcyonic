import type { CatalogResponse } from '@halcyonic/contracts';
import {
  CATALOG_MAX_BYTES,
  checkCredential,
  connect,
  DEFAULT_TIMEOUT_MS,
  get,
  isFailure,
  isReply,
  refusal,
  unanswered,
} from './connection.ts';
import { readCatalog, toCatalogResponse } from './wire.ts';

export interface PhatPorcupineOptions {
  /** Phat Porcupine's home, where it publishes `consumer.json`. See `defaultPhatPorcupineHome`. */
  readonly home: string;
  /**
   * Reads the consumer credential the person created for Halcyonic, or null when there is none. It is
   * read on every request and sent only in its own form, and only after the instance proved itself.
   */
  readonly credential: () => string | null;
  /** How long one request may take, in milliseconds. Defaults to 10,000. */
  readonly timeoutMs?: number;
  /** The most an answer may hold, in bytes. Defaults to version 1's bound, 8 MiB. */
  readonly maxBytes?: number;
}

/**
 * Reads the person's catalog from Phat Porcupine's catalog contract, version 1 (ADR 0039): finds the
 * instance through its discovery file, has it prove itself, then asks for the catalog with the
 * credential. Every answer is a `CatalogResponse`, never a thrown error, so the control plane can say
 * why there is no catalog. Keeps the last catalog with its ETag, so an unchanged one costs a 304.
 */
export class PhatPorcupineClient {
  readonly #options: PhatPorcupineOptions;
  #last: {
    readonly instanceId: string;
    readonly etag: string;
    readonly answer: CatalogResponse;
  } | null = null;

  constructor(options: PhatPorcupineOptions) {
    this.#options = options;
  }

  async catalog(signal?: AbortSignal): Promise<CatalogResponse> {
    const credential = checkCredential(this.#options.credential());
    if (typeof credential !== 'string') return credential;
    const timeoutMs = this.#options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    const instance = await connect(this.#options.home, { timeoutMs, ...(signal && { signal }) });
    if (isFailure(instance)) return instance;
    const last = this.#last?.instanceId === instance.instanceId ? this.#last : null;
    const reply = await get(
      instance.port,
      '/consumer/v1/catalog',
      credential,
      timeoutMs,
      this.#options.maxBytes ?? CATALOG_MAX_BYTES,
      signal,
      last === null ? {} : { 'if-none-match': last.etag },
    );
    if (!isReply(reply)) return unanswered(reply);
    if (reply.status === 304 && last !== null) return last.answer;
    if (reply.status !== 200) return refusal(reply.status, reply.body);
    const wire = readCatalog(reply.body);
    if (isFailure(wire)) return wire;
    const answer = toCatalogResponse(wire, instance);
    const etag = reply.headers.etag;
    this.#last =
      answer.availability === 'available' && typeof etag === 'string'
        ? { instanceId: instance.instanceId, etag, answer }
        : null;
    return answer;
  }
}
