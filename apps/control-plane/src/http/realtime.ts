import {
  type CommandEnvelope,
  parseClientMessage,
  REALTIME_PROTOCOL_VERSION,
  type ServerMessage,
  type ValidationIssue,
} from '@halcyonic/contracts';
import type { FastifyBaseLogger, FastifyInstance } from 'fastify';
import type { RawData, WebSocket } from 'ws';
import type { ControlPlane } from '../core/control-plane.ts';

export interface RealtimeOptions {
  /** Interval between server pings; a client that misses one is disconnected. */
  readonly heartbeatIntervalMs: number;
  /** A client this far behind is disconnected and must resynchronize from a snapshot. */
  readonly maxBufferedBytes: number;
}

export const DEFAULT_REALTIME_OPTIONS: RealtimeOptions = {
  heartbeatIntervalMs: 15_000,
  maxBufferedBytes: 8 * 1024 * 1024,
};

/** The realtime stream: snapshot on connect, then every journaled event with its effects. */
export function registerRealtime(
  app: FastifyInstance,
  controlPlane: ControlPlane,
  options: RealtimeOptions = DEFAULT_REALTIME_OPTIONS,
): void {
  app.get('/realtime', { websocket: true }, (socket, request) => {
    new RealtimeConnection(socket, controlPlane, request.log, options).start();
  });
}

/**
 * Protocol (docs/internal/architecture/REALTIME.md): the client sends `hello`; the server answers
 * `welcome`, then a `snapshot` unless the client's resume cursor is already current, then an
 * `event` message for every journaled event. Commands may be sent after `welcome`.
 */
class RealtimeConnection {
  readonly #socket: WebSocket;
  readonly #controlPlane: ControlPlane;
  readonly #log: FastifyBaseLogger;
  readonly #options: RealtimeOptions;
  #phase: 'awaiting_hello' | 'live' | 'closed' = 'awaiting_hello';
  #unsubscribe: (() => void) | null = null;
  #heartbeat: NodeJS.Timeout | null = null;
  #alive = true;

  constructor(
    socket: WebSocket,
    controlPlane: ControlPlane,
    log: FastifyBaseLogger,
    options: RealtimeOptions,
  ) {
    this.#socket = socket;
    this.#controlPlane = controlPlane;
    this.#log = log;
    this.#options = options;
  }

  start(): void {
    this.#socket.on('message', (data: RawData, isBinary: boolean) =>
      this.#onMessage(data, isBinary),
    );
    this.#socket.on('pong', () => {
      this.#alive = true;
    });
    this.#socket.on('close', () => this.#dispose());
    this.#socket.on('error', (error: Error) => {
      this.#log.warn({ err: error }, 'realtime connection error');
    });
    this.#heartbeat = setInterval(() => {
      if (!this.#alive) {
        this.#log.info({}, 'realtime client missed a heartbeat; disconnecting');
        this.#socket.terminate();
        return;
      }
      this.#alive = false;
      this.#socket.ping();
    }, this.#options.heartbeatIntervalMs);
    this.#heartbeat.unref();
  }

  #onMessage(data: RawData, isBinary: boolean): void {
    if (isBinary) {
      this.#sendError('binary_not_supported', 'Messages must be JSON text.', [], false);
      return;
    }
    let value: unknown;
    try {
      value = JSON.parse(rawDataToString(data));
    } catch {
      this.#sendError('invalid_json', 'Messages must be JSON text.', [], false);
      return;
    }
    if (isRecord(value) && value.type === 'hello' && value.protocol !== REALTIME_PROTOCOL_VERSION) {
      this.#sendError(
        'unsupported_protocol',
        `This server speaks realtime protocol ${REALTIME_PROTOCOL_VERSION}.`,
        [],
        true,
      );
      return;
    }
    const parsed = parseClientMessage(value);
    if (!parsed.ok) {
      this.#sendError(
        'invalid_message',
        'The message does not match the realtime contract.',
        parsed.issues,
        this.#phase === 'awaiting_hello',
      );
      return;
    }
    const message = parsed.value;
    switch (message.type) {
      case 'hello':
        if (this.#phase !== 'awaiting_hello') {
          this.#sendError('unexpected_hello', 'hello was already received.', [], false);
          return;
        }
        this.#hello(message.resume, message.client.name);
        return;
      case 'command':
        if (this.#phase !== 'live') {
          this.#sendError('hello_required', 'Send hello before any other message.', [], true);
          return;
        }
        this.#command(message.command);
        return;
      case 'ping':
        this.#send({ type: 'pong', nonce: message.nonce });
        return;
      default: {
        const unhandled: never = message;
        throw new Error(`unhandled message ${JSON.stringify(unhandled)}`);
      }
    }
  }

  #hello(resume: { journal_id: string; position: number } | null, clientName: string): void {
    const journal = this.#controlPlane.journal.info;
    const head = this.#controlPlane.projection.position;
    const resumed =
      resume !== null && resume.journal_id === journal.journal_id && resume.position === head;
    this.#send({
      type: 'welcome',
      protocol: REALTIME_PROTOCOL_VERSION,
      journal,
      head,
      resumed,
      server_time: this.#controlPlane.clock.now().toISOString(),
    });
    if (!resumed) this.#send({ type: 'snapshot', snapshot: this.#controlPlane.snapshot() });
    // Subscribing in the same synchronous step as the snapshot means no event can fall between them.
    this.#unsubscribe = this.#controlPlane.publisher.subscribe((published) =>
      this.#send({
        type: 'event',
        position: published.position,
        event: published.event,
        changes: published.changes,
      }),
    );
    this.#phase = 'live';
    this.#log.info({ client: clientName, resumed, head }, 'realtime client connected');
  }

  #command(command: CommandEnvelope): void {
    const outcome = this.#controlPlane.commands.submit(command, 'websocket');
    this.#send({
      type: 'command_ack',
      command_id: command.command_id,
      disposition: outcome.disposition,
      command: outcome.command,
    });
  }

  #send(message: ServerMessage): void {
    if (this.#phase === 'closed' || this.#socket.readyState !== this.#socket.OPEN) return;
    if (this.#socket.bufferedAmount > this.#options.maxBufferedBytes) {
      this.#log.warn({}, 'realtime client is too far behind; disconnecting');
      this.#socket.close(1013, 'Too far behind; reconnect to resynchronize.');
      this.#dispose();
      return;
    }
    this.#socket.send(JSON.stringify(message));
  }

  #sendError(code: string, message: string, issues: ValidationIssue[], fatal: boolean): void {
    this.#send({ type: 'error', error: { code, message, issues }, fatal });
    if (fatal) {
      this.#socket.close(1008, code);
      this.#dispose();
    }
  }

  #dispose(): void {
    this.#phase = 'closed';
    this.#unsubscribe?.();
    this.#unsubscribe = null;
    if (this.#heartbeat !== null) clearInterval(this.#heartbeat);
    this.#heartbeat = null;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function rawDataToString(data: RawData): string {
  if (Array.isArray(data)) return Buffer.concat(data).toString('utf8');
  if (data instanceof ArrayBuffer) return Buffer.from(data).toString('utf8');
  return data.toString('utf8');
}
