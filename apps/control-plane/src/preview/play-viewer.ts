import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import websocket from '@fastify/websocket';
import Fastify, { type FastifyInstance, type FastifyReply, type FastifyRequest } from 'fastify';

/**
 * The viewer of one play on the Mac (P-A): a small server of its own on 127.0.0.1, apart from the
 * control plane, which holds no access token and reaches nothing but its one play. It serves the
 * viewer page and one WebSocket that carries the play's frames out and the person's mouse and key
 * input in. The link that opens it carries a one-time code, used once and only for a short time, that
 * becomes a cookie for this port alone. Cookies on 127.0.0.1 span every port, so a game's page in
 * another tab of the same browser would carry it: every request must also come from this server's own
 * origin, or from no page at all, and the WebSocket must name this server's exact origin.
 */

/** The viewers' ports: one per play, apart from the control plane's and the previews' (47930 to 47939). */
export const VIEWER_PORT_FIRST = 47940;
export const VIEWER_PORT_LAST = 47949;

/** How long the code in a viewer's link can be used. */
export const VIEWER_CODE_MS = 60_000;

/** The largest message the viewer takes: an input is a few short fields. */
const MAX_MESSAGE_BYTES = 1024;

/** Frames waiting to reach a slow viewer past this are dropped rather than queued. */
const MAX_BUFFERED_BYTES = 4 * 1024 * 1024;

/** What a viewer can do with its play: watch its frames and send it input. Nothing else. */
export interface ViewerPlay {
  /** Streams frames (base64 JPEG) until the returned function is called. */
  watch(listener: (frame: string) => void): () => void;
  /** Sends a viewer message to the page if it is a mouse or key input. */
  input(message: unknown): Promise<boolean>;
}

export interface ViewerOptions {
  /** The ports to try, first to last. */
  readonly ports?: readonly [number, number];
  readonly codeMs?: number;
  readonly now?: () => number;
  /** The viewer page's HTML. */
  readonly page?: string;
}

/** Until the viewer page is built (P-B): a page with nothing on it. */
const BLANK_PAGE = '<!doctype html><meta charset="utf-8"><title>Play</title>';

export class PlayViewer {
  readonly port: number;
  readonly #app: FastifyInstance;
  readonly #code: Buffer;
  readonly #session: string;
  readonly #expires: number;
  readonly #now: () => number;
  #used = false;

  private constructor(
    app: FastifyInstance,
    port: number,
    code: Buffer,
    session: string,
    expires: number,
    now: () => number,
  ) {
    this.#app = app;
    this.port = port;
    this.#code = code;
    this.#session = session;
    this.#expires = expires;
    this.#now = now;
  }

  /** Opens a viewer for `play` on the first free port of the range. */
  static async open(play: ViewerPlay, options: ViewerOptions = {}): Promise<PlayViewer> {
    const [first, last] = options.ports ?? [VIEWER_PORT_FIRST, VIEWER_PORT_LAST];
    const now = options.now ?? Date.now;
    for (let port = first; port <= last; port += 1) {
      const app = Fastify({ logger: false, forceCloseConnections: true, bodyLimit: 1024 });
      const viewer = new PlayViewer(
        app,
        port,
        randomBytes(32),
        randomBytes(32).toString('hex'),
        now() + (options.codeMs ?? VIEWER_CODE_MS),
        now,
      );
      await viewer.#routes(play, options.page ?? BLANK_PAGE);
      try {
        await app.listen({ host: '127.0.0.1', port });
        return viewer;
      } catch (error) {
        await app.close();
        if ((error as NodeJS.ErrnoException).code !== 'EADDRINUSE') throw error;
      }
    }
    throw new Error('every viewer port is in use');
  }

  /** The link that opens the viewer; its code works once, within its time. */
  get link(): string {
    return `http://127.0.0.1:${this.port}/?code=${this.#code.toString('base64url')}`;
  }

  get #origin(): string {
    return `http://127.0.0.1:${this.port}`;
  }

  get #cookieName(): string {
    return `halcyonic_play_${this.port}`;
  }

  /** Stops the server and every connection to it. */
  async close(): Promise<void> {
    await this.#app.close();
  }

  async #routes(play: ViewerPlay, page: string): Promise<void> {
    const app = this.#app;
    await app.register(websocket, { options: { maxPayload: MAX_MESSAGE_BYTES } });
    app.addHook('onRequest', async (request, reply) => {
      reply.headers({
        'cache-control': 'no-store',
        'referrer-policy': 'no-referrer',
        'x-content-type-options': 'nosniff',
        'content-security-policy': `default-src 'none'; script-src 'self'; style-src 'self'; img-src blob:; connect-src ws://127.0.0.1:${this.port}; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`,
      });
      const refusal = this.#refusal(request);
      if (refusal !== null) return reply.code(403).type('text/plain').send(refusal);
    });
    app.get('/', async (request, reply) => {
      const code = (request.query as { code?: unknown }).code;
      if (code !== undefined) return this.#exchange(code, reply);
      if (!this.#hasSession(request))
        return reply.code(403).type('text/plain').send('Open the game again from Halcyonic.');
      return reply.type('text/html; charset=utf-8').send(page);
    });
    app.get('/stream', { websocket: true }, (socket) => {
      const off = play.watch((frame) => {
        if (socket.bufferedAmount < MAX_BUFFERED_BYTES) socket.send(Buffer.from(frame, 'base64'));
      });
      socket.on('message', (data, isBinary) => {
        if (isBinary) return;
        let message: unknown;
        try {
          message = JSON.parse(data.toString());
        } catch {
          return;
        }
        void play.input(message).catch(() => undefined);
      });
      socket.on('close', off);
    });
  }

  /**
   * Why a request is refused, or null. The Host must name this server, so a name that resolves here
   * is refused. A page's request names its origin, which must be this server's; the WebSocket must
   * name it. A request with no origin must be one no page made: the person's own navigation (`none`)
   * or this server's redirect (`same-origin`), never one another port's page started.
   */
  #refusal(request: FastifyRequest): string | null {
    const header = (name: string): string | undefined => {
      const value = request.headers[name];
      return Array.isArray(value) ? value[0] : value;
    };
    if (header('host') !== `127.0.0.1:${this.port}`) return 'This viewer answers only 127.0.0.1.';
    const origin = header('origin');
    if (origin !== undefined && origin !== this.#origin) return 'Not from this viewer.';
    const site = header('sec-fetch-site');
    const upgrade = header('upgrade')?.toLowerCase() === 'websocket';
    if (upgrade) {
      if (origin === undefined) return 'Not from this viewer.';
      if (site !== undefined && site !== 'same-origin') return 'Not from this viewer.';
      if (!this.#hasSession(request)) return 'Open the game again from Halcyonic.';
      return null;
    }
    if (site !== 'none' && site !== 'same-origin') return 'Not from this viewer.';
    return null;
  }

  /** Trades the link's code, once and in time, for this port's session cookie. */
  #exchange(code: unknown, reply: FastifyReply): FastifyReply {
    const given = typeof code === 'string' ? Buffer.from(code, 'base64url') : Buffer.alloc(0);
    const valid = !this.#used && this.#now() <= this.#expires && same(given, this.#code);
    if (!valid)
      return reply
        .code(403)
        .type('text/plain')
        .send('This link has been used or has run out. Open the game again from Halcyonic.');
    this.#used = true;
    return reply
      .header(
        'set-cookie',
        `${this.#cookieName}=${this.#session}; HttpOnly; SameSite=Strict; Path=/`,
      )
      .redirect('/', 303);
  }

  #hasSession(request: FastifyRequest): boolean {
    if (!this.#used) return false;
    const cookies = request.headers.cookie ?? '';
    const prefix = `${this.#cookieName}=`;
    const value = cookies
      .split(';')
      .map((part) => part.trim())
      .find((part) => part.startsWith(prefix))
      ?.slice(prefix.length);
    return value !== undefined && same(Buffer.from(value), Buffer.from(this.#session));
  }
}

/** Constant-time comparison; hashing first makes the inputs equal length. */
function same(a: Buffer, b: Buffer): boolean {
  return timingSafeEqual(
    createHash('sha256').update(a).digest(),
    createHash('sha256').update(b).digest(),
  );
}
