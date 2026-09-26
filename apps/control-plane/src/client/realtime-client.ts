import {
  type ClientInfo,
  type CommandEnvelope,
  compileValidator,
  REALTIME_PROTOCOL_VERSION,
  type ResumeCursor,
  ServerMessage,
  type ValidationIssue,
} from '@halcyonic/contracts';

const validateServerMessage = compileValidator(ServerMessage);

type Listener = (message: ServerMessage) => void;

interface Waiter {
  readonly predicate: (message: ServerMessage) => boolean;
  readonly resolve: (message: ServerMessage) => void;
  readonly reject: (error: Error) => void;
  readonly timer: NodeJS.Timeout;
}

/**
 * A minimal client for the realtime protocol, used by `pnpm demo` and by tests. It validates
 * every server message against the contract and keeps the ones that fail in `invalid`.
 */
export class RealtimeClient {
  readonly messages: ServerMessage[] = [];
  readonly invalid: { raw: unknown; issues: ValidationIssue[] }[] = [];
  readonly closed: Promise<{ code: number; reason: string }>;
  readonly #socket: WebSocket;
  readonly #listeners = new Set<Listener>();
  readonly #waiters = new Set<Waiter>();

  private constructor(socket: WebSocket) {
    this.#socket = socket;
    socket.addEventListener('message', (event) => this.#receive(event.data));
    this.closed = new Promise((resolve) => {
      socket.addEventListener('close', (event) => {
        for (const waiter of this.#waiters) {
          clearTimeout(waiter.timer);
          waiter.reject(new Error(`connection closed (${event.code}) before the expected message`));
        }
        this.#waiters.clear();
        resolve({ code: event.code, reason: event.reason });
      });
    });
  }

  /** Opens the connection with a bearer token. Rejects if the server refuses the upgrade. */
  static connect(
    url: string,
    token: string,
    extraHeaders: Record<string, string> = {},
  ): Promise<RealtimeClient> {
    const socket = new WebSocket(url, {
      headers: { authorization: `Bearer ${token}`, ...extraHeaders },
    });
    return new Promise((resolve, reject) => {
      const client = new RealtimeClient(socket);
      socket.addEventListener('open', () => resolve(client), { once: true });
      socket.addEventListener('error', () => reject(new Error(`could not connect to ${url}`)), {
        once: true,
      });
    });
  }

  hello(client: ClientInfo, resume: ResumeCursor | null = null): void {
    this.sendRaw({ type: 'hello', protocol: REALTIME_PROTOCOL_VERSION, client, resume });
  }

  command(command: CommandEnvelope): void {
    this.sendRaw({ type: 'command', command });
  }

  /** Sends any JSON value, including invalid messages in tests. */
  sendRaw(value: unknown): void {
    this.#socket.send(JSON.stringify(value));
  }

  onMessage(listener: Listener): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  /** Resolves with the first message, already received or future, that matches. */
  waitFor(
    predicate: (message: ServerMessage) => boolean,
    timeoutMs = 5000,
  ): Promise<ServerMessage> {
    const existing = this.messages.find(predicate);
    if (existing !== undefined) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const waiter: Waiter = {
        predicate,
        resolve,
        reject,
        timer: setTimeout(() => {
          this.#waiters.delete(waiter);
          reject(new Error(`no matching realtime message within ${timeoutMs} ms`));
        }, timeoutMs),
      };
      this.#waiters.add(waiter);
    });
  }

  close(): Promise<{ code: number; reason: string }> {
    this.#socket.close(1000, 'client closing');
    return this.closed;
  }

  #receive(data: unknown): void {
    let value: unknown;
    try {
      value = JSON.parse(typeof data === 'string' ? data : String(data));
    } catch {
      this.invalid.push({ raw: data, issues: [{ path: '/', message: 'not JSON' }] });
      return;
    }
    const parsed = validateServerMessage(value);
    if (!parsed.ok) {
      this.invalid.push({ raw: value, issues: parsed.issues });
      return;
    }
    const message = parsed.value;
    this.messages.push(message);
    for (const listener of this.#listeners) listener(message);
    for (const waiter of [...this.#waiters]) {
      if (!waiter.predicate(message)) continue;
      clearTimeout(waiter.timer);
      this.#waiters.delete(waiter);
      waiter.resolve(message);
    }
  }
}
