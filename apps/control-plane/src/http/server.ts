import websocket from '@fastify/websocket';
import type { ErrorResponse, Principal, ValidationIssue } from '@halcyonic/contracts';
import Fastify, { type FastifyError, type FastifyInstance } from 'fastify';
import type { LogLevel } from '../config.ts';
import { OwnWordsError } from '../logger.ts';
import {
  checkRequest,
  isProofChallenge,
  loopbackProof,
  PROOF_CHALLENGE_HEADER,
  PROOF_HEADER,
  PUBLIC_PATHS,
  proofAddress,
} from './security.ts';

declare module 'fastify' {
  interface FastifyRequest {
    /**
     * Who the request's guard authenticated: the local access token on loopback, a paired device on
     * the network listener. Null on the public paths.
     */
    principal: Principal | null;
  }
}

export const LOCAL_PRINCIPAL: Principal = Object.freeze({ kind: 'local' });

/** Messages over this size are refused, on both listeners. */
export const MAX_WEBSOCKET_MESSAGE_BYTES = 256 * 1024;

export interface HttpServerOptions {
  readonly logLevel: LogLevel;
  readonly token: string;
  /** Where log lines go instead of stdout, for tests that check what is logged. */
  readonly logStream?: NodeJS.WritableStream;
}

/**
 * Headers whose values are never logged, wherever a logged object carries them: the access token or
 * a device credential, cookies, and the loopback proof and its challenge (SECURITY.md). Fastify's own
 * request lines carry no headers; this keeps any line that does from showing these.
 */
const SECRET_HEADERS = [
  'authorization',
  'cookie',
  '["x-halcyonic-proof"]',
  '["x-halcyonic-challenge"]',
];
export const REDACTED_PATHS = SECRET_HEADERS.flatMap((header) => {
  const key = header.startsWith('[') ? header : `.${header}`;
  return [`headers${key}`, `req.headers${key}`, `*.headers${key}`];
});

/**
 * How an error is logged: its type, its code and its stack's frames. Never its message, which can
 * quote what it read, as a parse error does an instruction, and never its other fields, such as the
 * `rawPacket` Node gives a malformed request's error: that request's head, its Authorization header
 * in it.
 */
export function errorForLog(error: unknown): {
  type: string;
  message: string;
  stack: string;
  code?: string | number;
} {
  // The message stays empty, the shape Fastify's serializer has with nothing in it, unless it is
  // Halcyonic's own fixed words.
  if (!(error instanceof Error)) return { type: typeof error, message: '', stack: '' };
  const code = (error as { code?: unknown }).code;
  const frames = (error.stack ?? '')
    .split('\n')
    .filter((line) => /^\s+at /.test(line))
    .join('\n');
  return {
    type: error.name,
    message: error instanceof OwnWordsError ? error.message : '',
    stack: frames,
    ...(typeof code === 'string' || typeof code === 'number' ? { code } : {}),
  };
}

export function errorBody(
  code: string,
  message: string,
  issues: ValidationIssue[] = [],
): ErrorResponse {
  return { error: { code, message, issues } };
}

/**
 * The HTTP and WebSocket shell. Routes are registered separately so the control plane can use
 * this server's logger while it is being assembled.
 */
export async function createHttpServer(options: HttpServerOptions): Promise<FastifyInstance> {
  const app = Fastify({
    logger: {
      level: options.logLevel,
      redact: { paths: REDACTED_PATHS, censor: '[redacted]' },
      serializers: { err: errorForLog },
      ...(options.logStream === undefined ? {} : { stream: options.logStream }),
    },
    bodyLimit: 1024 * 1024,
    forceCloseConnections: true,
  });
  await prepareServer(app);

  // Runs for every request, including WebSocket upgrades, before any route handler.
  app.addHook('onRequest', async (request, reply) => {
    const header = (name: string): string | undefined => {
      const value = request.headers[name];
      return Array.isArray(value) ? value[0] : value;
    };
    const path = request.url.split('?')[0] ?? request.url;
    const decision = checkRequest(
      {
        host: header('host'),
        origin: header('origin'),
        secFetchSite: header('sec-fetch-site'),
        authorization: header('authorization'),
        path,
        localPort: request.raw.socket.localPort ?? 0,
      },
      options.token,
    );
    if (!decision.allowed) {
      if (decision.status === 401) reply.header('www-authenticate', 'Bearer');
      return reply.code(decision.status).send(errorBody(decision.code, decision.message));
    }
    request.principal = PUBLIC_PATHS.has(path) ? null : LOCAL_PRINCIPAL;
    // A loopback client proves who answers before it sends the token (pnpm devices, mac-setup).
    const challenge = header(PROOF_CHALLENGE_HEADER);
    if (path === '/api/health' && isProofChallenge(challenge)) {
      const socket = request.raw.socket;
      const reached = proofAddress(socket.localAddress ?? '', socket.localPort ?? 0);
      reply.header(PROOF_HEADER, loopbackProof(options.token, reached, challenge));
    }
  });

  return app;
}

/**
 * What both listeners share: JSON only, WebSockets with the same message limit, the request's
 * principal, and errors in the contract's shape.
 */
export async function prepareServer(
  app: FastifyInstance,
  options: {
    /** How long a WebSocket the server closes waits for the client's answer; ws's default is 30 s. */
    readonly webSocketCloseTimeoutMs?: number;
  } = {},
): Promise<void> {
  // The API speaks JSON only. Fastify also parses text/plain by default, which is the content
  // type a cross-site form can send without a preflight, so it is refused with 415 instead.
  app.removeContentTypeParser('text/plain');

  await app.register(websocket, {
    options: {
      maxPayload: MAX_WEBSOCKET_MESSAGE_BYTES,
      ...(options.webSocketCloseTimeoutMs === undefined
        ? {}
        : { closeTimeout: options.webSocketCloseTimeoutMs }),
    },
  });
  app.decorateRequest('principal', null);

  app.setErrorHandler((error: FastifyError, request, reply) => {
    const status =
      error.statusCode !== undefined && error.statusCode >= 400 && error.statusCode < 600
        ? error.statusCode
        : 500;
    if (status >= 500) {
      request.log.error({ err: error }, 'request failed');
      return reply
        .code(500)
        .send(errorBody('internal_error', 'The control plane failed to handle the request.'));
    }
    const code =
      status === 413
        ? 'payload_too_large'
        : status === 415
          ? 'unsupported_media_type'
          : 'invalid_request';
    return reply.code(status).send(errorBody(code, error.message));
  });

  app.setNotFoundHandler((request, reply) =>
    reply
      .code(404)
      .send(errorBody('not_found', `No route for ${request.method} ${request.url.split('?')[0]}.`)),
  );
}
