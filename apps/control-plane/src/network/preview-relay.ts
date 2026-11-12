import { readFileSync } from 'node:fs';
import { request as httpRequest, type IncomingMessage } from 'node:http';
import { connect } from 'node:net';
import { join } from 'node:path';
import { Transform } from 'node:stream';
import { PREVIEW_PORT_FIRST, PREVIEW_PORT_LAST } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { FastifyInstance, FastifyReply } from 'fastify';
import type { RawData, WebSocket } from 'ws';
import { errorBody } from '../http/server.ts';

/**
 * A project's preview, relayed to a paired headset over the network listener (ADR 0052), so the
 * person sees the app they made in the headset's browser with no cable: the headset's app answers its
 * browser on its own loopback address and asks here for each file. Only GET and HEAD, only for a preview
 * that serves the project's latest check, which passed and recorded that every agent's reads were
 * confined to the project's folder (`reads_confined`, ADR 0041), and only from that preview's own port on
 * 127.0.0.1: nothing else on the Mac is reached.
 */

/** The largest file the relay passes on; a built preview's files are far smaller. */
export const MAX_RELAYED_BYTES = 32 * 1024 * 1024;

/** How long the preview server may take to answer one request. */
export const RELAY_TIMEOUT_MS = 15_000;

export type PreviewTarget =
  | { readonly ok: true; readonly port: number }
  | { readonly ok: false; readonly code: string; readonly message: string };

const refuse = (code: string, message: string): PreviewTarget => ({ ok: false, code, message });

/**
 * Where a project's preview can be relayed from, or why not, from the journal's projection and the
 * host's own record of the server it runs (written before it serves, removed when it stops).
 */
export function confinedPreviewTarget(
  projection: Projection,
  dataDir: string,
  projectId: string,
  ports: readonly [number, number] = [PREVIEW_PORT_FIRST, PREVIEW_PORT_LAST],
): PreviewTarget {
  const preview = projection.preview(projectId);
  if (preview === undefined)
    return refuse('preview_not_running', 'No preview runs for this project.');
  const check = projection.latestCheck(projectId);
  if (check === undefined || check.check_id !== preview.check_id)
    return refuse('preview_not_latest', 'The preview serves a copy that is not the latest check.');
  if (!check.findings.passed)
    return refuse('preview_not_passed', 'The check the preview serves did not pass.');
  if (check.findings.reads_confined !== true)
    return refuse(
      'preview_not_confined',
      "The agents' reads were not confined to the project's folder, so its preview stays on this computer.",
    );
  const port = preview.port;
  if (!Number.isInteger(port) || port < ports[0] || port > ports[1])
    return refuse('preview_not_running', 'The preview is not on a preview port.');
  let record: { port?: unknown; check_id?: unknown };
  try {
    record = JSON.parse(readFileSync(join(dataDir, 'previews', `${projectId}.json`), 'utf8'));
  } catch {
    return refuse('preview_not_running', "This computer has no record of the preview's server.");
  }
  if (record.port !== port || record.check_id !== check.check_id)
    return refuse('preview_not_running', "This computer's record names another preview.");
  return { ok: true, port };
}

/** Request headers passed on to the preview server: what a page's loads need, nothing that identifies. */
const PASSED_REQUEST_HEADERS = [
  'accept',
  'accept-language',
  'range',
  'if-none-match',
  'if-modified-since',
];

/** Response headers passed back: the file's own description, nothing that sets state in the browser. */
const PASSED_RESPONSE_HEADERS = [
  'content-type',
  'content-length',
  'content-range',
  'accept-ranges',
  'etag',
  'last-modified',
];

const PROJECT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/**
 * Registers `GET|HEAD /api/projects/:project_id/preview/*` on the network listener, for a paired
 * device only. `target` decides, per request, whether and from which port.
 */
export function registerPreviewRelay(
  app: FastifyInstance,
  target: (projectId: string) => PreviewTarget,
): void {
  const relay = async (
    request: { params: unknown; raw: IncomingMessage; principal?: { kind: string } | null },
    reply: FastifyReply,
  ) => {
    if (request.principal?.kind !== 'device') {
      return reply.code(403).send(errorBody('forbidden', 'Only a paired device reads a preview.'));
    }
    const { project_id: projectId } = request.params as { project_id: string };
    if (!PROJECT_ID.test(projectId)) {
      return reply.code(400).send(errorBody('invalid_request', 'The project id is invalid.'));
    }
    // A service worker registered on the headset's address would answer later previews there with
    // bytes no check saw: its script is never relayed.
    if (request.raw.headers['service-worker'] !== undefined) {
      return reply
        .code(403)
        .send(errorBody('forbidden', 'A preview is not relayed as a service worker.'));
    }
    const found = target(projectId);
    if (!found.ok) return reply.code(409).send(errorBody(found.code, found.message));
    const prefix = `/api/projects/${projectId}/preview`;
    const rest = (request.raw.url ?? '').slice(prefix.length) || '/';
    if (!rest.startsWith('/') || rest.length > 2048) {
      return reply.code(400).send(errorBody('invalid_request', 'The path is invalid.'));
    }
    const headers: Record<string, string> = { host: `127.0.0.1:${found.port}` };
    for (const name of PASSED_REQUEST_HEADERS) {
      const value = request.raw.headers[name];
      if (typeof value === 'string') headers[name] = value;
    }
    return new Promise<FastifyReply>((resolve) => {
      const upstream = httpRequest(
        {
          host: '127.0.0.1',
          port: found.port,
          method: request.raw.method,
          path: rest,
          headers,
          timeout: RELAY_TIMEOUT_MS,
        },
        (answer) => {
          const length = Number(answer.headers['content-length'] ?? 0);
          if (length > MAX_RELAYED_BYTES) {
            answer.destroy();
            resolve(
              reply
                .code(502)
                .send(errorBody('preview_too_large', 'That file is too large to show.')),
            );
            return;
          }
          const out: Record<string, string> = {
            // The headset's browser keeps nothing across previews that share its address.
            'cache-control': 'no-store',
            'x-content-type-options': 'nosniff',
          };
          for (const name of PASSED_RESPONSE_HEADERS) {
            const value = answer.headers[name];
            if (typeof value === 'string') out[name] = value;
          }
          // Counted as it passes, so a server that sends more than it said is cut off.
          let size = 0;
          const counted = new Transform({
            transform(chunk: Buffer, _encoding, done) {
              size += chunk.length;
              done(size > MAX_RELAYED_BYTES ? new Error('too large') : null, chunk);
            },
          });
          answer.on('error', (error) => counted.destroy(error));
          resolve(
            reply
              .code(answer.statusCode ?? 502)
              .headers(out)
              .send(answer.pipe(counted)),
          );
        },
      );
      upstream.on('timeout', () => upstream.destroy(new Error('timed out')));
      upstream.on('error', () => {
        if (!reply.sent) {
          resolve(
            reply
              .code(502)
              .send(
                errorBody('preview_unreachable', "The preview isn't answering on this computer."),
              ),
          );
        }
      });
      upstream.end();
    });
  };
  app.get('/api/projects/:project_id/preview', relay);
  app.get('/api/projects/:project_id/preview/*', relay);
}

/** How many preview streams one device may hold at once, how long one may sit idle, how often each re-asks its gate. */
export interface PreviewStreamLimits {
  readonly perDevice: number;
  readonly idleMs: number;
  readonly checkMs: number;
}

/**
 * A browser opens about six connections to one address; the listener allows a device eight in all, its realtime
 * connection among them.
 */
export const PREVIEW_STREAM_LIMITS: PreviewStreamLimits = {
  perDevice: 6,
  idleMs: 60_000,
  checkMs: 1_000,
};

/** The WebSocket close code a stream ends with when its gate no longer admits it: 4000 plus the HTTP status. */
const GATE_CLOSED = 4409;
/** The close code where the preview's own server did not take the connection. */
const PREVIEW_UNREACHABLE = 4502;

/**
 * Registers `GET /api/projects/:project_id/preview-stream`, a WebSocket upgrade for a paired device only (ADR 0052,
 * amended 2026-11-12): one browser connection on the headset, carried whole to the preview's own server on this Mac,
 * so an app plays in the headset as it does at the desk (any method, live reload, its own WebSockets). The upgrade is
 * admitted by `target`, the same gate as the file route; the Mac then dials 127.0.0.1 at the port the gate names,
 * never one the request names, and pipes bytes both ways. Each open stream re-asks the gate, and whether its device is
 * still paired, every `checkMs`; when either says no, an idle `idleMs` passes, or the listener closes (pairing turned
 * off), the stream ends at once. A device holds at most `perDevice` streams.
 */
export function registerPreviewStream(
  app: FastifyInstance,
  target: (projectId: string) => PreviewTarget,
  isActive: (deviceId: string) => boolean,
  limits: PreviewStreamLimits = PREVIEW_STREAM_LIMITS,
): void {
  const open = new Map<string, Set<() => void>>();
  /** The place each admitted upgrade holds until its stream takes it. */
  const holds = new WeakMap<IncomingMessage, () => void>();
  // Pairing turned off closes the listener: every stream it holds ends with it.
  app.addHook('onClose', async () => {
    for (const ends of open.values()) for (const end of [...ends]) end();
  });
  app.get(
    '/api/projects/:project_id/preview-stream',
    {
      websocket: true,
      preHandler: async (request, reply) => {
        const principal = request.principal;
        if (principal?.kind !== 'device') {
          return reply
            .code(403)
            .send(errorBody('forbidden', 'Only a paired device reads a preview.'));
        }
        const { project_id: projectId } = request.params as { project_id: string };
        if (!PROJECT_ID.test(projectId)) {
          return reply.code(400).send(errorBody('invalid_request', 'The project id is invalid.'));
        }
        if ((open.get(principal.device_id)?.size ?? 0) >= limits.perDevice) {
          return reply
            .code(429)
            .send(
              errorBody(
                'too_many_streams',
                `A device holds at most ${limits.perDevice} preview connections at once.`,
              ),
            );
        }
        const found = target(projectId);
        if (!found.ok) return reply.code(409).send(errorBody(found.code, found.message));
        // The place is held now, before the upgrade, so streams opened together cannot pass the cap together; an
        // upgrade that never completes gives it back within ten seconds.
        const ends = open.get(principal.device_id) ?? new Set<() => void>();
        open.set(principal.device_id, ends);
        const hold = () => {
          ends.delete(hold);
          if (ends.size === 0) open.delete(principal.device_id);
        };
        ends.add(hold);
        setTimeout(hold, 10_000).unref();
        holds.set(request.raw, hold);
      },
    },
    (socket: WebSocket, request) => {
      const principal = request.principal;
      const { project_id: projectId } = request.params as { project_id: string };
      const found = target(projectId);
      if (principal?.kind !== 'device' || !found.ok) {
        socket.close(GATE_CLOSED, found.ok ? 'forbidden' : found.code);
        return;
      }
      const deviceId = principal.device_id;
      const port = found.port;
      holds.get(request.raw)?.();
      const ends = open.get(deviceId) ?? new Set<() => void>();
      open.set(deviceId, ends);
      // Only this Mac's own loopback, at the port the gate named.
      const upstream = connect({ host: '127.0.0.1', port });
      let ended = false;
      let idle: NodeJS.Timeout | undefined;
      const touch = () => {
        if (idle !== undefined) clearTimeout(idle);
        idle = setTimeout(() => end(1000, 'idle'), limits.idleMs);
        idle.unref();
      };
      const end = (code = 1000, reason = 'ended') => {
        if (ended) return;
        ended = true;
        clearInterval(check);
        if (idle !== undefined) clearTimeout(idle);
        upstream.destroy();
        if (socket.readyState === socket.OPEN || socket.readyState === socket.CONNECTING) {
          socket.close(code, reason);
        }
        ends.delete(end);
        if (ends.size === 0) open.delete(deviceId);
      };
      ends.add(end);
      const check = setInterval(() => {
        if (!isActive(deviceId)) return end(GATE_CLOSED, 'not_paired');
        const now = target(projectId);
        if (!now.ok) return end(GATE_CLOSED, now.code);
        if (now.port !== port) end(GATE_CLOSED, 'preview_not_latest');
      }, limits.checkMs);
      check.unref();
      touch();
      // The browser's bytes to the preview, and the preview's back, as they come; a slow headset slows the preview.
      let unsent = 0;
      upstream.on('data', (chunk: Buffer) => {
        touch();
        unsent += 1;
        if (unsent > 16) upstream.pause();
        socket.send(chunk, { binary: true }, () => {
          unsent -= 1;
          if (unsent <= 4) upstream.resume();
        });
      });
      upstream.on('error', () => end(PREVIEW_UNREACHABLE, 'preview_unreachable'));
      upstream.on('end', () => end());
      upstream.on('close', () => end());
      // And a preview slow to read slows the headset: the stream stops reading until the preview has drained.
      socket.on('message', (data: RawData) => {
        touch();
        const chunks = Array.isArray(data) ? data : [data];
        let full = false;
        for (const chunk of chunks) {
          full =
            !upstream.write(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as ArrayBuffer)) ||
            full;
        }
        if (full) {
          socket.pause();
          upstream.once('drain', () => socket.resume());
        }
      });
      socket.on('close', () => end());
      socket.on('error', () => end());
    },
  );
}
