import type { Readable, Writable } from 'node:stream';
import type { RequestId } from './protocol.ts';

/** The server answered a request with a JSON-RPC error: it refused the request. */
export class RpcError extends Error {
  readonly code: number;

  constructor(code: number, message: string) {
    super(message);
    this.name = 'RpcError';
    this.code = code;
  }
}

/**
 * A request got no answer. `not_sent`: the connection had already ended, so nothing was sent.
 * `closed` and `timeout`: it was sent, and the server may have acted on it.
 */
export class RpcUnanswered extends Error {
  readonly reason: 'not_sent' | 'closed' | 'timeout';

  constructor(reason: 'not_sent' | 'closed' | 'timeout', method: string) {
    super(
      reason === 'timeout'
        ? `Codex did not answer ${method} in time.`
        : `The Codex server connection ended before ${method} was answered.`,
    );
    this.name = 'RpcUnanswered';
    this.reason = reason;
  }
}

export interface RpcHandlers {
  /** A notification, in stream order. `emittedAtMs` is the server's clock when it sent it. */
  onNotification(method: string, params: unknown, emittedAtMs: number | null): void;
  /** A request from the server, in stream order, to answer with `respond` or `respondError`. */
  onRequest(id: RequestId, method: string, params: unknown, connection: RpcConnection): void;
}

interface Pending {
  readonly method: string;
  readonly resolve: (result: unknown) => void;
  readonly reject: (error: Error) => void;
  readonly timer: NodeJS.Timeout;
}

/**
 * JSON-RPC over the app-server's stdio: one JSON message per line. Codex omits the `jsonrpc`
 * member, and so does this client. Messages are handled in the order the server wrote them;
 * handlers run synchronously, before the next line is read.
 */
export class RpcConnection {
  /** Resolves when the server's output has ended; every unanswered request is rejected by then. */
  readonly ended: Promise<void>;
  readonly #output: Writable;
  readonly #handlers: RpcHandlers;
  readonly #pending = new Map<RequestId, Pending>();
  #nextId = 1;
  #open = true;
  #buffered = '';

  constructor(input: Readable, output: Writable, handlers: RpcHandlers) {
    this.#output = output;
    this.#handlers = handlers;
    output.on('error', () => undefined);
    input.setEncoding('utf8');
    input.on('data', (chunk: string) => this.#read(chunk));
    this.ended = new Promise((resolve) => {
      const end = () => {
        this.#shut();
        resolve();
      };
      input.once('end', end);
      input.once('close', end);
      input.once('error', end);
    });
  }

  get open(): boolean {
    return this.#open;
  }

  /** Sends a request and resolves with its result, or rejects with `RpcError` or `RpcUnanswered`. */
  request(method: string, params: unknown, timeoutMs: number): Promise<unknown> {
    if (!this.#open || !this.#output.writable) {
      return Promise.reject(new RpcUnanswered('not_sent', method));
    }
    const id = this.#nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.#pending.delete(id);
        reject(new RpcUnanswered('timeout', method));
      }, timeoutMs);
      this.#pending.set(id, { method, resolve, reject, timer });
      this.#write({ id, method, params });
    });
  }

  notify(method: string, params?: unknown): void {
    this.#write(params === undefined ? { method } : { method, params });
  }

  respond(id: RequestId, result: unknown): void {
    this.#write({ id, result });
  }

  respondError(id: RequestId, code: number, message: string): void {
    this.#write({ id, error: { code, message } });
  }

  /** Ends the server's input. The app-server shuts down at the end of its input. */
  end(): void {
    this.#output.end();
  }

  #write(message: object): void {
    if (this.#open && this.#output.writable) this.#output.write(`${JSON.stringify(message)}\n`);
  }

  #read(chunk: string): void {
    this.#buffered += chunk;
    for (let newline = this.#buffered.indexOf('\n'); newline >= 0; ) {
      const line = this.#buffered.slice(0, newline);
      this.#buffered = this.#buffered.slice(newline + 1);
      newline = this.#buffered.indexOf('\n');
      if (line.trim() !== '') this.#handle(line);
    }
  }

  #handle(line: string): void {
    let message: unknown;
    try {
      message = JSON.parse(line);
    } catch {
      return;
    }
    if (typeof message !== 'object' || message === null || Array.isArray(message)) return;
    const { id, method, params, result, error, emittedAtMs } = message as Record<string, unknown>;
    const hasId = typeof id === 'number' || typeof id === 'string';
    try {
      if (typeof method === 'string') {
        if (hasId) this.#handlers.onRequest(id, method, params, this);
        else {
          this.#handlers.onNotification(
            method,
            params,
            typeof emittedAtMs === 'number' && Number.isFinite(emittedAtMs) ? emittedAtMs : null,
          );
        }
        return;
      }
    } catch {
      // A handler must not throw; one that does cannot stop the connection.
      return;
    }
    if (!hasId) return;
    const pending = this.#pending.get(id);
    if (pending === undefined) return;
    this.#pending.delete(id);
    clearTimeout(pending.timer);
    if (typeof error === 'object' && error !== null) {
      const { code, message: text } = error as Record<string, unknown>;
      pending.reject(
        new RpcError(
          typeof code === 'number' ? code : 0,
          typeof text === 'string' ? text : 'Codex refused the request without a message.',
        ),
      );
      return;
    }
    pending.resolve(result);
  }

  #shut(): void {
    if (!this.#open) return;
    this.#open = false;
    for (const [id, pending] of this.#pending) {
      this.#pending.delete(id);
      clearTimeout(pending.timer);
      pending.reject(new RpcUnanswered('closed', pending.method));
    }
  }
}
