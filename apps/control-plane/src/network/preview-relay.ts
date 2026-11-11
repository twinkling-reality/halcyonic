import { readFileSync } from 'node:fs';
import { request as httpRequest, type IncomingMessage } from 'node:http';
import { join } from 'node:path';
import { Transform } from 'node:stream';
import { PREVIEW_PORT_FIRST, PREVIEW_PORT_LAST } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { FastifyInstance, FastifyReply } from 'fastify';
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
  if (!Number.isInteger(port) || port < PREVIEW_PORT_FIRST || port > PREVIEW_PORT_LAST)
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
