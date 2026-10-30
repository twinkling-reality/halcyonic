import { Agent, type ClientRequestArgs } from 'node:http';
import type { Duplex } from 'node:stream';
import type { CatalogResponse } from '@halcyonic/contracts';
import {
  CATALOG_MAX_BYTES,
  type CatalogFailure,
  checkCredential,
  connect,
  DEFAULT_TIMEOUT_MS,
  fail,
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
   * Reads the consumer credential the person created for Halcyonic, or null when there is none, or
   * says why it can't be used. It is read on every request and sent only in its own form, and only
   * after the instance proved itself.
   */
  readonly credential: () => string | null | CatalogFailure;
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
    // One connection for the proof and the catalog: the credential goes only on the connection whose
    // other end just proved it holds the secret, and never on another one, so no listener that took
    // the port in between can receive it.
    const agent = new OneConnection();
    try {
      return await this.#read(credential, timeoutMs, agent, signal);
    } finally {
      agent.destroy();
    }
  }

  async #read(
    credential: string,
    timeoutMs: number,
    agent: OneConnection,
    signal: AbortSignal | undefined,
  ): Promise<CatalogResponse> {
    const instance = await connect(this.#options.home, {
      timeoutMs,
      agent,
      ...(signal && { signal }),
    });
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
      agent,
    );
    if (agent.refused)
      return fail(
        'unavailable',
        'connection_closed',
        'Phat Porcupine closed the connection its proof came on, so the credential was not sent.',
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

/**
 * An agent that opens one connection and refuses a second: the catalog request goes on the socket the
 * proof came back on, kept open between them, or nowhere. If Phat Porcupine closes it after the proof,
 * the request fails before anything is written, and `refused` says so.
 */
class OneConnection extends Agent {
  refused = false;
  #opened = false;

  constructor() {
    super({ keepAlive: true, maxSockets: 1 });
  }

  override createConnection(
    options: ClientRequestArgs,
    callback?: (error: Error | null, stream: Duplex) => void,
  ): Duplex | null | undefined {
    if (this.#opened) {
      this.refused = true;
      const error = new Error('Only the connection the proof came on may carry the credential.');
      if (callback) {
        callback(error, undefined as unknown as Duplex);
        return undefined;
      }
      throw error;
    }
    this.#opened = true;
    return super.createConnection(options, callback);
  }
}
