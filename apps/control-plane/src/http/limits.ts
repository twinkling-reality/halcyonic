import type { FastifyInstance } from 'fastify';

/**
 * The bounds of a listener on this computer's loopback (CARBON A1-8), as the network listener has its
 * own (`DEFAULT_NETWORK_LIMITS`): any local process of any user reaches loopback, so without them one
 * could hold sockets open until the control plane had no descriptors left for its journal and the
 * agents it starts. With them a local client can still fill the connections and keep others waiting a
 * while, which it could do anyway; the control plane keeps working.
 */
export interface LoopbackLimits {
  /** For a request's headers, from its first byte. */
  readonly headersMs: number;
  /** For a whole request, headers and body, from its first byte. */
  readonly requestMs: number;
  /**
   * For a connection on which nothing moves, which includes a route working on its answer, so it
   * outlasts the slowest route: a model list may take 30 seconds and the companion 45. An upgraded
   * WebSocket is not under this bound (ws turns its socket's timeout off): it counts toward
   * `connections` until it closes, and its own pings find a peer that has gone.
   */
  readonly idleMs: number;
  /** For the next request on a kept-alive connection. */
  readonly keepAliveMs: number;
  /** How often requests are checked against `headersMs` and `requestMs`; Node's default is 30 seconds. */
  readonly checkEveryMs: number;
  /** Connections held at once. Every local client is 127.0.0.1, so there is no bound per address. */
  readonly connections: number;
}

/**
 * The control plane's own loopback listener: the CLI, the demonstration, the suites, a laptop's page
 * and the proving scripts are all its clients, so it holds more than a paired device's 32.
 */
export const LOOPBACK_LIMITS: LoopbackLimits = {
  headersMs: 10_000,
  requestMs: 10_000,
  idleMs: 60_000,
  keepAliveMs: 5_000,
  checkEveryMs: 1_000,
  connections: 64,
};

/** A play's viewer: one person, one browser, its page and one stream. */
export const VIEWER_LIMITS: LoopbackLimits = { ...LOOPBACK_LIMITS, connections: 8 };

/**
 * The pairing and setup page: one person, one browser, and a photo or video of up to
 * `MEDIA_VIDEO_BYTES` sent to it in one request (ADR 0066), so a whole request may take two minutes;
 * its headers still have 10 seconds.
 */
export const PAGE_LIMITS: LoopbackLimits = { ...VIEWER_LIMITS, requestMs: 120_000 };

/** The options that put `limits` on a Fastify server over HTTP, given when it is made. */
export function loopbackServerOptions(limits: LoopbackLimits) {
  return {
    http: {
      headersTimeout: limits.headersMs,
      requestTimeout: limits.requestMs,
      connectionsCheckingInterval: limits.checkEveryMs,
    },
    // Fastify sets these on the server it makes, over what `http` said.
    requestTimeout: limits.requestMs,
    connectionTimeout: limits.idleMs,
    keepAliveTimeout: limits.keepAliveMs,
  } as const;
}

/** Bounds how many connections the server holds at once; one more is closed as it opens. */
export function boundConnections(app: FastifyInstance, limits: LoopbackLimits): void {
  app.server.maxConnections = limits.connections;
}
