/**
 * Test support: a device's view of the network listener. It opens TLS, checks the certificate
 * against a pin (or records it, as a device does before it has paired) before it sends anything,
 * then speaks HTTP/1.1 and WebSocket by hand, as the XR client's pinned transports do. Not used by
 * the running control plane.
 */
import { createHash, randomBytes } from 'node:crypto';
import { connect, type TLSSocket } from 'node:tls';

export interface TlsTarget {
  readonly host: string;
  readonly port: number;
}

export class PinMismatch extends Error {
  readonly seen: string;

  constructor(seen: string) {
    super('the certificate is not the pinned one');
    this.seen = seen;
  }
}

/** Connects, and checks or records the certificate, before a single byte is written. */
export function openTls(
  target: TlsTarget,
  pin: string | null = null,
): Promise<{ socket: TLSSocket; certificateSha256: string }> {
  return new Promise((resolve, reject) => {
    const socket = connect({ host: target.host, port: target.port, rejectUnauthorized: false });
    socket.once('error', reject);
    socket.once('secureConnect', () => {
      socket.off('error', reject);
      const seen = createHash('sha256').update(socket.getPeerCertificate().raw).digest('hex');
      if (pin !== null && seen !== pin) {
        socket.destroy();
        reject(new PinMismatch(seen));
        return;
      }
      resolve({ socket, certificateSha256: seen });
    });
  });
}

export interface HttpAnswer {
  readonly status: number;
  readonly headers: ReadonlyMap<string, string>;
  readonly body: string;
  readonly certificateSha256: string;
}

/** One request on its own connection, closed after the answer. */
export async function tlsRequest(
  target: TlsTarget,
  request: {
    readonly method: string;
    readonly path: string;
    readonly headers?: Record<string, string>;
    readonly body?: string;
    readonly pin?: string | null;
  },
): Promise<HttpAnswer> {
  const { socket, certificateSha256 } = await openTls(target, request.pin ?? null);
  const body = request.body === undefined ? null : Buffer.from(request.body, 'utf8');
  const headers = {
    host: `${target.host}:${target.port}`,
    connection: 'close',
    ...(body === null ? {} : { 'content-length': String(body.length) }),
    ...request.headers,
  };
  socket.write(head(`${request.method} ${request.path} HTTP/1.1`, headers));
  if (body !== null) socket.write(body);
  const received = await readToEnd(socket);
  const parsed = parseHead(received);
  if (parsed === null) throw new Error('the answer has no complete head');
  return {
    status: parsed.status,
    headers: parsed.headers,
    body: received.subarray(parsed.length).toString('utf8'),
    certificateSha256,
  };
}

export class WebSocketRefused extends Error {
  readonly status: number;
  readonly body: string;

  constructor(status: number, body: string) {
    super(`the upgrade was refused with ${status}: ${body}`);
    this.status = status;
    this.body = body;
  }
}

/** A text-only WebSocket client, enough for the pairing and realtime protocols. */
export class TlsWebSocket {
  readonly messages: string[] = [];
  readonly closed: Promise<{ code: number; reason: string }>;
  readonly certificateSha256: string;
  readonly #socket: TLSSocket;
  #buffer: Buffer;
  #waiters: { predicate: (text: string) => boolean; resolve: (text: string) => void }[] = [];
  #closedWith: ((value: { code: number; reason: string }) => void) | null = null;

  private constructor(socket: TLSSocket, leftover: Buffer, certificateSha256: string) {
    this.#socket = socket;
    this.#buffer = leftover;
    this.certificateSha256 = certificateSha256;
    this.closed = new Promise((resolve) => {
      this.#closedWith = resolve;
    });
    socket.on('data', (chunk: Buffer) => {
      this.#buffer = Buffer.concat([this.#buffer, chunk]);
      this.#drain();
    });
    socket.on('close', () => this.#finish(1006, ''));
    socket.on('error', () => this.#finish(1006, ''));
    // Reading the upgrade's answer paused the socket; a 'data' listener does not undo that.
    socket.resume();
    this.#drain();
  }

  static async connect(
    target: TlsTarget,
    path: string,
    options: { readonly headers?: Record<string, string>; readonly pin?: string | null } = {},
  ): Promise<TlsWebSocket> {
    const { socket, certificateSha256 } = await openTls(target, options.pin ?? null);
    const key = randomBytes(16).toString('base64');
    socket.write(
      head(`GET ${path} HTTP/1.1`, {
        host: `${target.host}:${target.port}`,
        connection: 'Upgrade',
        upgrade: 'websocket',
        'sec-websocket-version': '13',
        'sec-websocket-key': key,
        ...options.headers,
      }),
    );
    let received = Buffer.alloc(0);
    for (;;) {
      const chunk = await nextChunk(socket);
      if (chunk === null) throw new Error('the connection closed during the upgrade');
      received = Buffer.concat([received, chunk]);
      const parsed = parseHead(received);
      if (parsed === null) continue;
      if (parsed.status !== 101) {
        const rest = Buffer.concat([received.subarray(parsed.length), await readToEnd(socket)]);
        throw new WebSocketRefused(parsed.status, rest.toString('utf8'));
      }
      const accept = createHash('sha1')
        .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
        .digest('base64');
      if (parsed.headers.get('sec-websocket-accept') !== accept) {
        socket.destroy();
        throw new Error('the server answered the upgrade with the wrong key');
      }
      return new TlsWebSocket(socket, received.subarray(parsed.length), certificateSha256);
    }
  }

  send(text: string): void {
    this.#write(0x1, Buffer.from(text, 'utf8'));
  }

  sendJson(value: unknown): void {
    this.send(JSON.stringify(value));
  }

  /** The first message, received or to come, that matches. */
  waitFor(predicate: (text: string) => boolean, timeoutMs = 5000): Promise<string> {
    const existing = this.messages.find(predicate);
    if (existing !== undefined) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(
        () => reject(new Error(`no matching message within ${timeoutMs} ms`)),
        timeoutMs,
      );
      this.#waiters.push({
        predicate,
        resolve: (text) => {
          clearTimeout(timer);
          resolve(text);
        },
      });
    });
  }

  /** The message at `index`, as JSON, once it has arrived. */
  async message(index: number, timeoutMs = 5000): Promise<Record<string, unknown>> {
    const deadline = Date.now() + timeoutMs;
    while (this.messages.length <= index) {
      if (Date.now() > deadline) throw new Error(`no message ${index} within ${timeoutMs} ms`);
      await new Promise((resolve) => setTimeout(resolve, 5));
    }
    return JSON.parse(this.messages[index] ?? 'null') as Record<string, unknown>;
  }

  close(): Promise<{ code: number; reason: string }> {
    const code = Buffer.alloc(2);
    code.writeUInt16BE(1000);
    this.#write(0x8, code);
    return this.closed;
  }

  /** A masked frame, as a client must send. */
  #write(opcode: number, payload: Buffer): void {
    if (this.#socket.destroyed) return;
    const mask = randomBytes(4);
    const length =
      payload.length < 126
        ? Buffer.from([0x80 | payload.length])
        : payload.length < 65536
          ? Buffer.from([0x80 | 126, payload.length >> 8, payload.length & 0xff])
          : Buffer.concat([Buffer.from([0x80 | 127]), bigEndian64(payload.length)]);
    const masked = Buffer.from(payload.map((byte, index) => byte ^ (mask[index % 4] ?? 0)));
    this.#socket.write(Buffer.concat([Buffer.from([0x80 | opcode]), length, mask, masked]));
  }

  #drain(): void {
    for (;;) {
      const frame = readFrame(this.#buffer);
      if (frame === null) return;
      this.#buffer = this.#buffer.subarray(frame.length);
      if (frame.opcode === 0x1) {
        const text = frame.payload.toString('utf8');
        this.messages.push(text);
        for (const waiter of [...this.#waiters]) {
          if (!waiter.predicate(text)) continue;
          this.#waiters.splice(this.#waiters.indexOf(waiter), 1);
          waiter.resolve(text);
        }
      } else if (frame.opcode === 0x9) {
        this.#write(0xa, frame.payload);
      } else if (frame.opcode === 0x8) {
        const code = frame.payload.length >= 2 ? frame.payload.readUInt16BE(0) : 1005;
        this.#finish(code, frame.payload.subarray(2).toString('utf8'));
        this.#socket.end();
        return;
      }
    }
  }

  #finish(code: number, reason: string): void {
    this.#closedWith?.({ code, reason });
    this.#closedWith = null;
  }
}

function head(requestLine: string, headers: Record<string, string>): string {
  const lines = Object.entries(headers).map(([name, value]) => `${name}: ${value}`);
  return `${[requestLine, ...lines].join('\r\n')}\r\n\r\n`;
}

function parseHead(
  received: Buffer,
): { status: number; headers: Map<string, string>; length: number } | null {
  const end = received.indexOf('\r\n\r\n');
  if (end < 0) return null;
  const [statusLine = '', ...lines] = received.subarray(0, end).toString('latin1').split('\r\n');
  const status = Number(statusLine.split(' ')[1]);
  const headers = new Map<string, string>();
  for (const line of lines) {
    const colon = line.indexOf(':');
    if (colon > 0)
      headers.set(line.slice(0, colon).trim().toLowerCase(), line.slice(colon + 1).trim());
  }
  return { status, headers, length: end + 4 };
}

function readFrame(buffer: Buffer): { opcode: number; payload: Buffer; length: number } | null {
  if (buffer.length < 2) return null;
  const opcode = (buffer[0] ?? 0) & 0x0f;
  let length = (buffer[1] ?? 0) & 0x7f;
  let offset = 2;
  if (length === 126) {
    if (buffer.length < 4) return null;
    length = buffer.readUInt16BE(2);
    offset = 4;
  } else if (length === 127) {
    if (buffer.length < 10) return null;
    length = Number(buffer.readBigUInt64BE(2));
    offset = 10;
  }
  if (buffer.length < offset + length) return null;
  return { opcode, payload: buffer.subarray(offset, offset + length), length: offset + length };
}

function bigEndian64(value: number): Buffer {
  const bytes = Buffer.alloc(8);
  bytes.writeBigUInt64BE(BigInt(value));
  return bytes;
}

function nextChunk(socket: TLSSocket): Promise<Buffer | null> {
  return new Promise((resolve) => {
    const onData = (chunk: Buffer) => {
      cleanup();
      resolve(chunk);
    };
    const onEnd = () => {
      cleanup();
      resolve(null);
    };
    const cleanup = () => {
      socket.off('data', onData);
      socket.off('end', onEnd);
      socket.off('close', onEnd);
      socket.pause();
    };
    socket.on('data', onData);
    socket.once('end', onEnd);
    socket.once('close', onEnd);
    socket.resume();
  });
}

function readToEnd(socket: TLSSocket): Promise<Buffer> {
  return new Promise((resolve) => {
    const chunks: Buffer[] = [];
    socket.on('data', (chunk: Buffer) => chunks.push(chunk));
    socket.once('close', () => resolve(Buffer.concat(chunks)));
    socket.once('end', () => resolve(Buffer.concat(chunks)));
    socket.resume();
  });
}
