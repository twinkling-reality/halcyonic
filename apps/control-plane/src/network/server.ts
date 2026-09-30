import type { Socket } from 'node:net';
import { PAIRING_PROTOCOL_VERSION, parsePairingClientMessage } from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import Fastify, { type FastifyBaseLogger, type FastifyInstance } from 'fastify';
import type { RawData, WebSocket } from 'ws';
import type { ControlPlane } from '../core/control-plane.ts';
import { DEFAULT_REALTIME_OPTIONS, rawDataToString, registerRealtime } from '../http/realtime.ts';
import { type RouteSources, registerRoutes } from '../http/routes.ts';
import { errorBody, prepareServer } from '../http/server.ts';
import type { NetworkIdentity } from './certificate.ts';
import type { DeviceAccess } from './devices.ts';
import { checkNetworkRequest } from './guard.ts';
import { WindowCounter } from './limits.ts';
import { type Pairing, type PairingAnswer, PairingAttempt, refused } from './pairing.ts';

declare module 'fastify' {
  interface FastifyContextConfig {
    /** The route may answer a device it has just revoked, as a device revoking itself. */
    answersRevokedDevice?: boolean;
  }
}

/**
 * How long the network listener waits for a client, which may be anything on the network, and how
 * many connections it holds.
 */
export interface NetworkLimits {
  /** For the TLS handshake, from the connection. Node's default is two minutes. */
  readonly handshakeMs: number;
  /** For a whole request, headers and body, from its first byte. */
  readonly requestMs: number;
  /**
   * For a connection on which nothing moves, which includes a route working on its answer, so it
   * outlasts the slowest route: a model list may take 30 seconds. An upgraded WebSocket has its
   * own heartbeat instead.
   */
  readonly idleMs: number;
  /** For the next request on a kept-alive connection. */
  readonly keepAliveMs: number;
  /** How often requests are checked against `requestMs`; Node's default is 30 seconds. */
  readonly checkEveryMs: number;
  /** Connections held at once, from every address. */
  readonly connections: number;
  /** Connections held at once from one address: a headset needs a few. */
  readonly connectionsPerAddress: number;
}

/** A device's requests are small, and a headset holds a realtime connection and a few requests. */
export const DEFAULT_NETWORK_LIMITS: NetworkLimits = {
  handshakeMs: 5_000,
  requestMs: 10_000,
  idleMs: 60_000,
  keepAliveMs: 5_000,
  checkEveryMs: 1_000,
  connections: 32,
  connectionsPerAddress: 8,
};

/** How long a WebSocket the listener closes waits for the client to answer the close frame. */
export const WEBSOCKET_CLOSE_TIMEOUT_MS = 1_000;

export interface NetworkServerOptions {
  readonly logger: FastifyBaseLogger;
  readonly identity: NetworkIdentity;
  readonly controlPlane: ControlPlane;
  readonly sources: RouteSources;
  readonly devices: DeviceAccess;
  readonly pairing: Pairing;
  readonly clock: Clock;
  readonly limits?: NetworkLimits;
}

/** Paths that answer without a device credential. The pairing path has checks of its own. */
const PUBLIC_PATHS: ReadonlySet<string> = new Set(['/api/health', '/pair']);

/** Failed credentials one address may present in a minute before it is refused outright. */
const FAILED_CREDENTIALS_PER_MINUTE = 30;

/**
 * The listener paired devices reach over the network (ADR 0017): TLS with the control plane's own
 * certificate, which devices pin when they pair. It serves the pairing exchange, and to paired
 * devices the same REST and realtime API as loopback, never the access token and never what
 * manages devices, which stays on loopback.
 */
export async function createNetworkServer(options: NetworkServerOptions): Promise<FastifyInstance> {
  const { controlPlane, devices, pairing } = options;
  const limits = options.limits ?? DEFAULT_NETWORK_LIMITS;
  // Fastify types an HTTPS server apart from an HTTP one; the routes use nothing that differs.
  const app = Fastify({
    https: {
      key: options.identity.key,
      cert: options.identity.certificate,
      minVersion: 'TLSv1.2',
      handshakeTimeout: limits.handshakeMs,
      headersTimeout: limits.requestMs,
      requestTimeout: limits.requestMs,
      connectionsCheckingInterval: limits.checkEveryMs,
    },
    loggerInstance: options.logger,
    bodyLimit: 1024 * 1024,
    forceCloseConnections: true,
    // Fastify sets these on the server it creates, over what the https options said.
    requestTimeout: limits.requestMs,
    connectionTimeout: limits.idleMs,
    keepAliveTimeout: limits.keepAliveMs,
  }) as unknown as FastifyInstance;
  app.server.maxConnections = limits.connections;
  limitConnectionsPerAddress(app, limits.connectionsPerAddress);
  // A client that never answers a close frame, such as a revoked device's, is cut off a second on.
  await prepareServer(app, { webSocketCloseTimeoutMs: WEBSOCKET_CLOSE_TIMEOUT_MS });
  const failures = new WindowCounter(options.clock, FAILED_CREDENTIALS_PER_MINUTE, 60_000);

  app.addHook('onRequest', async (request, reply) => {
    const header = (name: string): string | undefined => {
      const value = request.headers[name];
      return Array.isArray(value) ? value[0] : value;
    };
    const decision = checkNetworkRequest({
      host: header('host'),
      origin: header('origin'),
      secFetchSite: header('sec-fetch-site'),
      localPort: request.raw.socket.localPort ?? 0,
    });
    if (!decision.allowed) {
      return reply.code(403).send(errorBody(decision.code, decision.message));
    }
    if (PUBLIC_PATHS.has(request.url.split('?')[0] ?? request.url)) return;
    if (failures.exhausted(request.ip)) {
      return reply
        .code(429)
        .send(errorBody('too_many_requests', 'Too many failed credentials; wait a minute.'));
    }
    const authentication = devices.authenticate(header('authorization'));
    if (!authentication.ok) {
      failures.take(request.ip);
      reply.header('www-authenticate', 'Bearer');
      return reply.code(401).send(errorBody(authentication.code, authentication.message));
    }
    request.principal = authentication.principal;
  });

  // The credential is checked again as the answer leaves: a request authenticated before its
  // device was revoked, such as one whose body arrived later or whose read took a while, gets the
  // refusal a new request would get, not what the route answered (ADR 0017).
  app.addHook('onSend', async (request, reply, payload) => {
    const principal = request.principal;
    if (principal?.kind !== 'device' || devices.isActive(principal.device_id)) return payload;
    if (request.routeOptions.config.answersRevokedDevice === true) return payload;
    reply.code(401).header('www-authenticate', 'Bearer').type('application/json; charset=utf-8');
    return JSON.stringify(
      errorBody('device_revoked', 'This device was revoked on the control plane; pair it again.'),
    );
  });

  registerRoutes(app, controlPlane, options.sources);
  registerRealtime(app, controlPlane, DEFAULT_REALTIME_OPTIONS, devices.track);

  // A device forgets the control plane: its own credential stops working everywhere.
  app.post(
    '/api/device/revoke',
    { config: { answersRevokedDevice: true } },
    async (request, reply) => {
      const principal = request.principal;
      if (principal?.kind !== 'device') {
        return reply
          .code(403)
          .send(errorBody('forbidden', 'Only a paired device can revoke itself.'));
      }
      devices.revoke(principal.device_id, principal);
      return reply.code(204).send();
    },
  );

  app.get(
    '/pair',
    {
      websocket: true,
      onRequest: async (request, reply) => {
        const refusal = pairing.admit(request.ip);
        if (refusal !== null) {
          return reply.code(refusal.status).send(errorBody(refusal.code, refusal.message));
        }
      },
    },
    (socket, request) => servePairing(socket, pairing.begin(request.ip), pairing, request.log),
  );

  return app;
}

/**
 * Refuses a connection from an address already holding `max`, before its TLS handshake, so one
 * device on the network cannot take every connection the listener holds.
 */
function limitConnectionsPerAddress(app: FastifyInstance, max: number): void {
  const open = new Map<string, number>();
  app.server.on('connection', (socket: Socket) => {
    const address = socket.remoteAddress ?? '';
    const count = open.get(address) ?? 0;
    if (count >= max) {
      socket.destroy();
      return;
    }
    open.set(address, count + 1);
    socket.once('close', () => {
      const left = (open.get(address) ?? 1) - 1;
      if (left > 0) open.set(address, left);
      else open.delete(address);
    });
  });
}

/** One pairing exchange on one connection, time boxed; the connection closes after the answer. */
function servePairing(
  socket: WebSocket,
  attempt: ReturnType<Pairing['begin']>,
  pairing: Pairing,
  log: FastifyBaseLogger,
): void {
  const answer = ({ reply, done }: PairingAnswer) => {
    if (socket.readyState === socket.OPEN) socket.send(JSON.stringify(reply));
    if (done) socket.close(1000, 'pairing ended');
  };
  if (!(attempt instanceof PairingAttempt)) {
    answer({ reply: refused(attempt), done: true });
    return;
  }
  const timer = setTimeout(() => answer(attempt.timeOut()), pairing.limits.attemptTimeoutMs);
  timer.unref();
  socket.on('close', () => {
    clearTimeout(timer);
    attempt.closed();
  });
  socket.on('error', (error: Error) => log.warn({ err: error }, 'pairing connection error'));
  socket.on('message', (data: RawData, isBinary: boolean) => {
    let reply: PairingAnswer;
    try {
      reply = receive(attempt, data, isBinary);
    } catch (error) {
      // Never the message: it can hold a proof. The window stays as it was.
      log.error({ err: error }, 'pairing exchange failed');
      reply = attempt.fail();
    }
    if (reply.done) clearTimeout(timer);
    answer(reply);
  });
}

function receive(attempt: PairingAttempt, data: RawData, isBinary: boolean): PairingAnswer {
  if (isBinary) return attempt.reject('invalid_message', 'Messages must be JSON text.');
  let value: unknown;
  try {
    value = JSON.parse(rawDataToString(data));
  } catch {
    return attempt.reject('invalid_message', 'Messages must be JSON text.');
  }
  const record =
    typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  if (record.type === 'pair_request' && record.protocol !== PAIRING_PROTOCOL_VERSION) {
    return attempt.reject(
      'unsupported_protocol',
      `This control plane pairs with protocol ${PAIRING_PROTOCOL_VERSION}.`,
    );
  }
  const parsed = parsePairingClientMessage(value);
  if (!parsed.ok) {
    return attempt.reject('invalid_message', 'The message does not match the pairing contract.');
  }
  return attempt.receive(parsed.value);
}
