import {
  PAIRING_PROTOCOL_VERSION,
  type PairingServerMessage,
  parsePairingClientMessage,
} from '@halcyonic/contracts';
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
import { type Pairing, PairingAttempt, refused } from './pairing.ts';

export interface NetworkServerOptions {
  readonly logger: FastifyBaseLogger;
  readonly identity: NetworkIdentity;
  readonly controlPlane: ControlPlane;
  readonly sources: RouteSources;
  readonly devices: DeviceAccess;
  readonly pairing: Pairing;
  readonly clock: Clock;
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
  // Fastify types an HTTPS server apart from an HTTP one; the routes use nothing that differs.
  const app = Fastify({
    https: { key: options.identity.key, cert: options.identity.certificate, minVersion: 'TLSv1.2' },
    loggerInstance: options.logger,
    bodyLimit: 1024 * 1024,
    forceCloseConnections: true,
  }) as unknown as FastifyInstance;
  await prepareServer(app);
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

  registerRoutes(app, controlPlane, options.sources);
  registerRealtime(app, controlPlane, DEFAULT_REALTIME_OPTIONS, devices.track);

  // A device forgets the control plane: its own credential stops working everywhere.
  app.post('/api/device/revoke', async (request, reply) => {
    const principal = request.principal;
    if (principal?.kind !== 'device') {
      return reply
        .code(403)
        .send(errorBody('forbidden', 'Only a paired device can revoke itself.'));
    }
    devices.revoke(principal.device_id, principal);
    return reply.code(204).send();
  });

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

/** One pairing exchange on one connection, time boxed; the connection closes after the answer. */
function servePairing(
  socket: WebSocket,
  attempt: ReturnType<Pairing['begin']>,
  pairing: Pairing,
  log: FastifyBaseLogger,
): void {
  const answer = (reply: PairingServerMessage, done: boolean) => {
    if (socket.readyState === socket.OPEN) socket.send(JSON.stringify(reply));
    if (done) socket.close(1000, 'pairing ended');
  };
  if (!(attempt instanceof PairingAttempt)) {
    answer(refused(attempt), true);
    return;
  }
  const timer = setTimeout(() => {
    attempt.end();
    answer(
      refused({ code: 'timeout', message: 'Pairing took too long; start again on the device.' }),
      true,
    );
  }, pairing.limits.attemptTimeoutMs);
  timer.unref();
  socket.on('close', () => {
    clearTimeout(timer);
    attempt.end();
  });
  socket.on('error', (error: Error) => log.warn({ err: error }, 'pairing connection error'));
  socket.on('message', (data: RawData, isBinary: boolean) => {
    const reply = receive(attempt, data, isBinary);
    if (reply.done) clearTimeout(timer);
    answer(reply.reply, reply.done);
  });
}

function receive(
  attempt: PairingAttempt,
  data: RawData,
  isBinary: boolean,
): { reply: PairingServerMessage; done: boolean } {
  const end = (code: string, message: string) => {
    attempt.end();
    return { reply: refused({ code, message }), done: true };
  };
  if (isBinary) return end('invalid_message', 'Messages must be JSON text.');
  let value: unknown;
  try {
    value = JSON.parse(rawDataToString(data));
  } catch {
    return end('invalid_message', 'Messages must be JSON text.');
  }
  const record =
    typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  if (record.type === 'pair_request' && record.protocol !== PAIRING_PROTOCOL_VERSION) {
    return end(
      'unsupported_protocol',
      `This control plane pairs with protocol ${PAIRING_PROTOCOL_VERSION}.`,
    );
  }
  const parsed = parsePairingClientMessage(value);
  if (!parsed.ok) {
    return end('invalid_message', 'The message does not match the pairing contract.');
  }
  return attempt.receive(parsed.value);
}
