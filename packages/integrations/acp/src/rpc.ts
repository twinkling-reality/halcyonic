import type { Readable, Writable } from 'node:stream';

/** A JSON-RPC request id, as the agent or this client chose it. */
export type RequestId = number | string;

/** The agent answered a request with a JSON-RPC error: it refused the request. */
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
 * `closed` and `timeout`: it was sent, and the agent may have acted on it.
 */
export class RpcUnanswered extends Error {
  readonly reason: 'not_sent' | 'closed' | 'timeout';

  constructor(reason: 'not_sent' | 'closed' | 'timeout', method: string, agent: string) {
    super(
      reason === 'timeout'
        ? `${agent} did not answer ${method} in time.`
        : `The connection to ${agent} ended before ${method} was answered.`,
    );
    this.name = 'RpcUnanswered';
    this.reason = reason;
  }
}

export interface RpcHandlers {
  /** A notification, in stream order. */
  onNotification(method: string, params: unknown): void;
  /** A request from the agent, in stream order, to answer with `respond` or `respondError`. */
  onRequest(id: RequestId, method: string, params: unknown, connection: RpcConnection): void;
}

interface Pending {
  readonly method: string;
  readonly resolve: (result: unknown) => void;
  readonly reject: (error: Error) => void;
  readonly timer: NodeJS.Timeout | null;
}

/**
 * JSON-RPC 2.0 over an ACP agent's stdio: one JSON message per line, as the Agent Client Protocol
 * frames it. Messages are handled in the order the agent wrote them; handlers run synchronously,
 * before the next line is read.
 */
export class RpcConnection {
  /** Resolves when the agent's output has ended; every unanswered request is rejected by then. */
  readonly ended: Promise<void>;
  readonly #output: Writable;
  readonly #handlers: RpcHandlers;
  readonly #agent: string;
  readonly #pending = new Map<RequestId, Pending>();
  #nextId = 1;
  #open = true;
  #buffered = '';

  constructor(input: Readable, output: Writable, handlers: RpcHandlers, agent: string) {
    this.#output = output;
    this.#handlers = handlers;
    this.#agent = agent;
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

  /**
   * Sends a request and resolves with its result, or rejects with `RpcError` or `RpcUnanswered`.
   * A null timeout waits as long as the connection stays open: an ACP prompt request is answered
   * only when its turn ends.
   */
  request(method: string, params: unknown, timeoutMs: number | null): Promise<unknown> {
    if (!this.#open || !this.#output.writable) {
      return Promise.reject(new RpcUnanswered('not_sent', method, this.#agent));
    }
    const id = this.#nextId++;
    return new Promise((resolve, reject) => {
      const timer =
        timeoutMs === null
          ? null
          : setTimeout(() => {
              this.#pending.delete(id);
              reject(new RpcUnanswered('timeout', method, this.#agent));
            }, timeoutMs);
      this.#pending.set(id, { method, resolve, reject, timer });
      this.#write({ jsonrpc: '2.0', id, method, params });
    });
  }

  notify(method: string, params?: unknown): void {
    this.#write(
      params === undefined ? { jsonrpc: '2.0', method } : { jsonrpc: '2.0', method, params },
    );
  }

  respond(id: RequestId, result: unknown): void {
    this.#write({ jsonrpc: '2.0', id, result });
  }

  respondError(id: RequestId, code: number, message: string): void {
    this.#write({ jsonrpc: '2.0', id, error: { code, message } });
  }

  /** Ends the agent's input. */
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
    const { id, method, params, result, error } = message as Record<string, unknown>;
    const hasId = typeof id === 'number' || typeof id === 'string';
    if (typeof method === 'string') {
      try {
        if (hasId) this.#handlers.onRequest(id, method, params, this);
        else this.#handlers.onNotification(method, params);
      } catch {
        // A handler must not throw; one that does cannot stop the connection.
      }
      return;
    }
    if (!hasId) return;
    const pending = this.#pending.get(id);
    if (pending === undefined) return;
    this.#pending.delete(id);
    if (pending.timer !== null) clearTimeout(pending.timer);
    if (typeof error === 'object' && error !== null) {
      const { code, message: text } = error as Record<string, unknown>;
      pending.reject(
        new RpcError(
          typeof code === 'number' ? code : 0,
          typeof text === 'string' ? text : `${this.#agent} refused the request without a message.`,
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
      if (pending.timer !== null) clearTimeout(pending.timer);
      pending.reject(new RpcUnanswered('closed', pending.method, this.#agent));
    }
  }
}
