import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { isIPv4, isIPv6 } from 'node:net';
import type { DeviceView, PairingState } from '@halcyonic/contracts';
import Fastify, { type FastifyInstance, type FastifyReply, type FastifyRequest } from 'fastify';
import type { NetworkListenerConfig } from '../config.ts';
import type { ControlPlane } from '../core/control-plane.ts';
import { LOCAL_PRINCIPAL } from '../http/server.ts';
import type { DeviceAccess } from './devices.ts';
import type { Pairing } from './pairing.ts';
import type { NetworkSwitch } from './switch.ts';

/**
 * The pairing page (ADR 0047): a page on the Mac, in the person's own browser, that turns pairing
 * over Wi-Fi on or off and shows the name and code a headset types, so pairing needs no terminal.
 * A small server of its own on 127.0.0.1, as the play viewer's is, beside the control plane in its
 * process: it holds no access token and no device credential, and reaches only what pairing needs.
 * A link to it carries a one-time code, used once and only for a short time, that becomes a cookie
 * for this port alone; every request must name this server as its Host and come from no page or
 * this page's own origin; everything that changes something is a POST from this page, with its
 * origin. Nothing happens on its own: the listener, a pairing window and a code each wait for the
 * person's press.
 */

/** The page's ports: the control plane's family (47800, the network listener 47801), first free. */
export const PAIRING_PAGE_PORT_FIRST = 47802;
export const PAIRING_PAGE_PORT_LAST = 47809;

/** How long the code in a link can be used. */
export const PAGE_LINK_MS = 60_000;

/** Browsers holding the page at once; the oldest goes first past this. */
const MAX_SESSIONS = 8;

/** What the page can do: exactly what pairing needs, nothing of projects, commands or the journal. */
export interface PairingPageControls {
  readonly network: NetworkSwitch;
  /** The paired devices, revoked ones included, and which are connected now. */
  devices(): { devices: readonly DeviceView[]; connected: readonly string[] };
  /** Turns pairing on now and in the settings, so it stays on at the next start. */
  turnOn(): Promise<void>;
  /** Turns pairing off now and in the settings; paired devices stay paired. */
  turnOff(): Promise<void>;
  /** Stops accepting a device, as `pnpm devices revoke` does. */
  forget(deviceId: string): 'revoked' | 'already_revoked' | 'not_found';
}

/**
 * The page's controls over a running control plane: the listener turned on through the same
 * switch the start uses, the setting written only once that succeeded, so a listener that cannot
 * start never stops the next start; turned off the same way; a device forgotten as on loopback.
 */
export function pairingControls(deps: {
  readonly network: NetworkSwitch;
  readonly controlPlane: ControlPlane;
  readonly devices: DeviceAccess;
  /** Where the listener listens when the page turns it on. */
  readonly listenOn: () => NetworkListenerConfig;
  /** Keeps the choice in the settings for the next start. */
  readonly remember: (on: boolean) => void;
}): PairingPageControls {
  return {
    network: deps.network,
    devices: () => ({
      devices: deps.controlPlane.projection.devices(),
      connected: deps.devices.connected(),
    }),
    turnOn: async () => {
      await deps.network.start(deps.listenOn());
      deps.remember(true);
    },
    turnOff: async () => {
      await deps.network.stop();
      deps.remember(false);
    },
    forget: (deviceId) => deps.devices.revoke(deviceId, LOCAL_PRINCIPAL),
  };
}

export interface PairingPageOptions {
  readonly ports?: readonly [number, number];
  readonly linkMs?: number;
  readonly now?: () => number;
  /**
   * Another view this page serves beside pairing, under `/setup` only (the setup page, ADR 0053):
   * the same link, session, Host, Origin and Sec-Fetch-Site checks and the same headers as pairing's
   * own routes. Nothing it adds reaches pairing's state.
   */
  readonly mount?: (routes: PageRoutes) => void;
}

/** What a view mounted on the page may add: routes behind the page's session and checks, under `/setup`. */
export interface PageRoutes {
  /**
   * A GET answered only with a session. `session` is an opaque key for the caller's session (a hash
   * of it, never the cookie), so a view can keep each session's own state.
   */
  get(path: string, handler: (request: FastifyRequest, session: string) => unknown): void;
  /** A POST from the page's own origin, answered only with a session; `session` as for `get`. */
  post(path: string, handler: (request: FastifyRequest, session: string) => unknown): void;
  /** A static file behind the session. */
  asset(path: string, type: string, body: Buffer): void;
}

/** The only paths a mounted view may add. */
const MOUNT_PREFIX = '/setup';

/** What the page shows now; the code only while a window it opened is open. */
export interface PageState {
  readonly pairing: 'off' | 'on';
  readonly turning: 'on' | 'off' | null;
  readonly port: number | null;
  /**
   * Where a headset types, the computer's `.local` name first, each with the port; never a loopback
   * address, which no headset can reach.
   */
  readonly names: readonly string[];
  /** A window this page opened is open; its code is shown only beside a name a headset can reach. */
  readonly shown: boolean;
  readonly window: {
    readonly state: PairingState;
    readonly expires_at: string | null;
    /** Seconds until it runs out, by the control plane's clock; 0 once it has. */
    readonly seconds_left: number;
    readonly failed_attempts: number;
    readonly max_failed_attempts: number;
    readonly device: string | null;
  } | null;
  readonly code: string | null;
  readonly headsets: readonly {
    readonly id: string;
    readonly label: string;
    readonly connected: boolean;
  }[];
  /** Why the last press did not do what it said, in the page's words; null when it did. */
  readonly problem: string | null;
}

const ASSETS: Readonly<Record<string, { readonly type: string; readonly body: Buffer }>> = {
  '/': { type: 'text/html; charset=utf-8', body: asset('index.html') },
  '/page.js': { type: 'text/javascript; charset=utf-8', body: asset('page.js') },
  '/page.css': { type: 'text/css; charset=utf-8', body: asset('page.css') },
  // The product's reading face, with its licence (OFL.txt) beside it, never from the network.
  '/Inter-Regular.ttf': { type: 'font/ttf', body: asset('Inter-Regular.ttf') },
  '/Inter-SemiBold.ttf': { type: 'font/ttf', body: asset('Inter-SemiBold.ttf') },
};

function asset(name: string): Buffer {
  return readFileSync(new URL(`./pairing-page/${name}`, import.meta.url));
}

/** Whether a headset could reach this address: not 127.0.0.0/8, ::1 or another loopback form. */
function reachable(address: string): boolean {
  if (isIPv4(address)) return !address.startsWith('127.');
  if (isIPv6(address)) {
    const lower = address.toLowerCase();
    return lower !== '::1' && !lower.startsWith('::ffff:127.');
  }
  return address !== 'localhost';
}

export class PairingPage {
  readonly port: number;
  readonly #app: FastifyInstance;
  readonly #controls: PairingPageControls;
  readonly #linkMs: number;
  readonly #now: () => number;
  readonly #mount: ((routes: PageRoutes) => void) | undefined;
  /** Unused link codes, by their hash, with when each runs out. */
  readonly #codes = new Map<string, number>();
  readonly #sessions: string[] = [];
  /** The code of the window this page opened, while that window is the one open. */
  #shown: { readonly pairing: Pairing; readonly code: string } | null = null;
  #turning: 'on' | 'off' | null = null;
  #problem: string | null = null;

  private constructor(
    app: FastifyInstance,
    port: number,
    controls: PairingPageControls,
    linkMs: number,
    now: () => number,
    mount?: (routes: PageRoutes) => void,
  ) {
    this.#app = app;
    this.port = port;
    this.#controls = controls;
    this.#linkMs = linkMs;
    this.#now = now;
    this.#mount = mount;
  }

  /** Serves the page on the first free port of its range. */
  static async open(
    controls: PairingPageControls,
    options: PairingPageOptions = {},
  ): Promise<PairingPage> {
    const [first, last] = options.ports ?? [PAIRING_PAGE_PORT_FIRST, PAIRING_PAGE_PORT_LAST];
    for (let port = first; port <= last; port += 1) {
      const app = Fastify({ logger: false, forceCloseConnections: true, bodyLimit: 1024 });
      const page = new PairingPage(
        app,
        port,
        controls,
        options.linkMs ?? PAGE_LINK_MS,
        options.now ?? Date.now,
        options.mount,
      );
      page.#routes();
      try {
        await app.listen({ host: '127.0.0.1', port });
        return page;
      } catch (error) {
        await app.close();
        if ((error as NodeJS.ErrnoException).code !== 'EADDRINUSE') throw error;
      }
    }
    throw new Error(`every pairing page port from ${first} to ${last} is in use`);
  }

  /** A new link to the page; its code works once, within its time. */
  link(): string {
    const code = randomBytes(32);
    this.#codes.set(hash(code), this.#now() + this.#linkMs);
    for (const [key, expires] of this.#codes) if (expires < this.#now()) this.#codes.delete(key);
    return `http://127.0.0.1:${this.port}/?code=${code.toString('base64url')}`;
  }

  async close(): Promise<void> {
    await this.#app.close();
  }

  /** What the page shows now. */
  state(): PageState {
    const network = this.#controls.network;
    const listener = network.listener();
    const pairing = network.pairing;
    const status = pairing?.status() ?? null;
    if (this.#shown !== null && (this.#shown.pairing !== pairing || status?.state !== 'open')) {
      this.#shown = null;
    }
    const { devices, connected } = this.#controls.devices();
    const names =
      listener === null
        ? []
        : listener.addresses
            .filter(reachable)
            .map((address) =>
              address.includes(':')
                ? `[${address}]:${listener.port}`
                : `${address}:${listener.port}`,
            );
    return {
      pairing: listener === null ? 'off' : 'on',
      turning: this.#turning,
      port: listener?.port ?? null,
      names,
      shown: this.#shown !== null,
      window:
        status === null
          ? null
          : {
              state: status.state,
              expires_at: status.expires_at,
              seconds_left:
                status.expires_at === null
                  ? 0
                  : Math.max(0, Math.round((Date.parse(status.expires_at) - network.now()) / 1000)),
              failed_attempts: status.failed_attempts,
              max_failed_attempts: status.max_failed_attempts,
              device: status.device?.label ?? null,
            },
      // Never a code beside no name a headset can reach: the page says to join Wi-Fi instead.
      code: names.length === 0 ? null : (this.#shown?.code ?? null),
      headsets: devices
        .filter((device) => device.revoked_at === null)
        .map((device) => ({
          id: device.device_id,
          label: device.label,
          connected: connected.includes(device.device_id),
        })),
      problem: this.#problem,
    };
  }

  #routes(): void {
    const app = this.#app;
    app.addHook('onRequest', async (request, reply) => {
      reply.headers({
        'cache-control': 'no-store',
        'referrer-policy': 'no-referrer',
        'x-content-type-options': 'nosniff',
        'content-security-policy':
          "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
      });
      const refusal = this.#refusal(request);
      if (refusal !== null) return reply.code(403).type('text/plain').send(refusal);
    });
    for (const [path, { type, body }] of Object.entries(ASSETS)) {
      app.get(path, async (request, reply) => {
        const code = (request.query as { code?: unknown }).code;
        if (path === '/' && code !== undefined) return this.#exchange(code, reply);
        if (!this.#hasSession(request)) return reply.code(403).type('text/plain').send(AGAIN);
        return reply.type(type).send(body);
      });
    }
    app.get('/state', async (request, reply) => {
      if (!this.#hasSession(request)) return reply.code(403).type('text/plain').send(AGAIN);
      return this.state();
    });
    const act = (path: string, work: (request: FastifyRequest) => Promise<void> | void) =>
      app.post(path, async (request, reply) => {
        if (!this.#hasSession(request)) return reply.code(403).type('text/plain').send(AGAIN);
        this.#problem = null;
        await work(request);
        return this.state();
      });
    act('/pairing/on', () => this.#turn('on'));
    act('/pairing/off', () => this.#turn('off'));
    act('/window', () => {
      const pairing = this.#controls.network.pairing;
      if (pairing === null) {
        this.#problem = 'Pairing is off. Turn it on first.';
        return;
      }
      this.#shown = { pairing, code: pairing.open().code };
    });
    act('/headsets/forget', (request) => {
      const id = (request.body as { id?: unknown } | undefined)?.id;
      if (typeof id !== 'string' || this.#controls.forget(id) === 'not_found') {
        this.#problem = "That headset isn't paired with this computer.";
      }
    });
    if (this.#mount !== undefined) this.#mount(this.#mountedRoutes());
  }

  /** The routes a mounted view adds, each behind the session, under `/setup` only. */
  #mountedRoutes(): PageRoutes {
    const app = this.#app;
    const under = (path: string) => {
      if (
        path !== MOUNT_PREFIX &&
        !path.startsWith(`${MOUNT_PREFIX}/`) &&
        !path.startsWith(`${MOUNT_PREFIX}.`)
      )
        throw new Error(`a mounted view may add only paths under ${MOUNT_PREFIX}: ${path}`);
    };
    return {
      get: (path, handler) => {
        under(path);
        app.get(path, async (request, reply) => {
          const session = this.#sessionKey(request);
          if (session === null) return reply.code(403).type('text/plain').send(AGAIN);
          return handler(request, session);
        });
      },
      post: (path, handler) => {
        under(path);
        app.post(path, async (request, reply) => {
          const session = this.#sessionKey(request);
          if (session === null) return reply.code(403).type('text/plain').send(AGAIN);
          return handler(request, session);
        });
      },
      asset: (path, type, body) => {
        under(path);
        app.get(path, async (request, reply) => {
          if (!this.#hasSession(request)) return reply.code(403).type('text/plain').send(AGAIN);
          return reply.type(type).send(body);
        });
      },
    };
  }

  async #turn(to: 'on' | 'off'): Promise<void> {
    if (this.#turning !== null) return;
    this.#turning = to;
    try {
      if (to === 'on') await this.#controls.turnOn();
      else await this.#controls.turnOff();
    } catch {
      // Never the error's own words: they can name paths and settings.
      this.#problem =
        to === 'on'
          ? "Pairing couldn't turn on. Check that nothing else uses its port, then try again."
          : "Pairing couldn't turn off. Quit the app on this computer and start it again.";
    } finally {
      this.#turning = null;
    }
  }

  /**
   * Why a request is refused, or null. The Host must name this server, so a name that resolves here
   * is refused. A request with an origin must come from this page; one without must be the person's
   * own navigation (`none`) or this server's redirect (`same-origin`), never another page's. A POST
   * must carry this page's origin and come from it.
   */
  #refusal(request: FastifyRequest): string | null {
    const header = (name: string): string | undefined => {
      const value = request.headers[name];
      return Array.isArray(value) ? value[0] : value;
    };
    if (header('host') !== `127.0.0.1:${this.port}`) return 'This page answers only 127.0.0.1.';
    const origin = header('origin');
    if (origin !== undefined && origin !== this.#origin) return 'Not from this page.';
    const site = header('sec-fetch-site');
    if (request.method !== 'GET') {
      if (origin !== this.#origin || site !== 'same-origin') return 'Not from this page.';
      return null;
    }
    // A link's code arrives as a top-level navigation from the hand-over file (open-link.ts), whose
    // file origin counts as another site. Only the trade of a code may come so: without the code,
    // which only the access token's holder can get, it opens nothing.
    const exchange =
      request.url.split('?')[0] === '/' &&
      typeof (request.query as { code?: unknown }).code === 'string' &&
      header('sec-fetch-mode') === 'navigate' &&
      header('sec-fetch-dest') === 'document';
    if (exchange) return null;
    if (site === undefined) {
      return 'This browser does not say which page a request comes from, so this page cannot open in it. Open it in a current browser.';
    }
    if (site !== 'none' && site !== 'same-origin') return 'Not from this page.';
    return null;
  }

  get #origin(): string {
    return `http://127.0.0.1:${this.port}`;
  }

  get #cookieName(): string {
    return `halcyonic_pairing_${this.port}`;
  }

  /** Trades a link's code, once and in time, for a session cookie for this port. */
  #exchange(code: unknown, reply: FastifyReply): FastifyReply {
    const given = typeof code === 'string' ? Buffer.from(code, 'base64url') : Buffer.alloc(0);
    const key = hash(given);
    const expires = this.#codes.get(key);
    this.#codes.delete(key);
    if (expires === undefined || this.#now() > expires) {
      return reply
        .code(403)
        .type('text/plain')
        .send(`This link has been used or has run out. ${AGAIN}`);
    }
    const session = randomBytes(32).toString('hex');
    this.#sessions.push(session);
    while (this.#sessions.length > MAX_SESSIONS) this.#sessions.shift();
    return reply
      .header('set-cookie', `${this.#cookieName}=${session}; HttpOnly; SameSite=Strict; Path=/`)
      .type('text/html; charset=utf-8')
      .send(GO_ON);
  }

  /**
   * Whether any cookie of this page's name is a session it gave. Every one is read, not only the
   * first: cookies on 127.0.0.1 span its ports, so another port's page can plant one of this name
   * ahead of the person's own, which must not keep them out (AUDIT S2).
   */
  #hasSession(request: FastifyRequest): boolean {
    return this.#sessionKey(request) !== null;
  }

  /** The caller's session as an opaque key (a hash, never the cookie itself), or null without one. */
  #sessionKey(request: FastifyRequest): string | null {
    const prefix = `${this.#cookieName}=`;
    const values = (request.headers.cookie ?? '')
      .split(';')
      .map((part) => part.trim())
      .filter((part) => part.startsWith(prefix))
      .map((part) => Buffer.from(part.slice(prefix.length)));
    for (const given of values) {
      const found = this.#sessions.find((session) => same(given, Buffer.from(session)));
      if (found !== undefined) return createHash('sha256').update(found).digest('hex');
    }
    return null;
  }
}

const AGAIN = 'This page has closed. Open pairing again on this computer.';

/**
 * The answer to a link's code: the cookie, and a page that goes on to the page by itself. Not a
 * redirect: a redirect keeps the navigation the hand-over file started, from another origin, so the
 * page would be asked for as from another site and refused; this page's own navigation is from it.
 */
const GO_ON =
  '<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=/"><title>Pairing</title>\n';

function hash(value: Buffer): string {
  return createHash('sha256').update(value).digest('hex');
}

/** Constant-time comparison; hashing first makes the inputs equal length. */
function same(a: Buffer, b: Buffer): boolean {
  return timingSafeEqual(
    createHash('sha256').update(a).digest(),
    createHash('sha256').update(b).digest(),
  );
}
