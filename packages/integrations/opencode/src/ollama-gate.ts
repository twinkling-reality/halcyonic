import {
  createServer,
  request as httpRequest,
  type IncomingMessage,
  type Server,
  type ServerResponse,
} from 'node:http';
import type { AddressInfo } from 'node:net';
import { isRecord } from './events.ts';

/**
 * The only way the sandboxed OpenCode server, and every command it runs, reaches Ollama (ADR 0028):
 * a loopback server in the control plane's own process, outside the sandbox, that passes on the
 * three requests OpenCode 2.0.18 makes of Ollama (core/src/plugin/provider/ollama.ts) and refuses
 * everything else before it reaches Ollama:
 *
 * - `GET /api/tags`, the model list, answered with only the models that run on this Mac;
 * - `POST /api/show` and `POST /v1/chat/completions`, each naming one of those models in a JSON
 *   body of at most 8 MiB, passed on as parsed. The chat streams back as Ollama writes it, and
 *   closing the request closes the gate's own, which stops the model.
 *
 * So a command can ask a local model for a reply and nothing more: no pull, push, create or delete,
 * which would let Ollama, unsandboxed, carry data to a host the command chooses, and no cloud or
 * remote model, which Ollama would run on another host. The gate always connects to the Ollama
 * address it was given, never to one a request names.
 */
export interface OllamaGate {
  /** The gate's own port on 127.0.0.1. */
  readonly port: number;
  /** The base URL OpenCode is given for Ollama, ending in `/v1`. */
  readonly baseUrl: string;
  close(): Promise<void>;
}

export interface OllamaGateOptions {
  /** Ollama's address, on loopback: `http://127.0.0.1:11434`. */
  readonly ollama: string;
  /**
   * How many chat replies may be open at once now, read whenever a chat arrives or waits: OpenCode
   * 2.0.18 opens one at a time for each session with a running turn (runtime test, 2026-10-08).
   * One more waits until a reply ends or the number rises. At least one is always let through.
   */
  readonly maxChats: () => number;
  /** How long a model list read from Ollama is used for the checks. By default 2 s. */
  readonly listTtlMs?: number;
}

/** The largest request body passed on: OpenCode's whole conversation with the model, as JSON. */
export const MAX_BODY_BYTES = 8 * 1024 * 1024;
/** The largest model list read: Ollama lists every model it holds, a few hundred bytes each. */
const MAX_LIST_BYTES = 1024 * 1024;
const LIST_TIMEOUT_MS = 5_000;
/** How often a waiting chat reads the number allowed again, since a turn starting raises it. */
const RECHECK_MS = 250;
const LOOPBACK = '127.0.0.1';

/** A model name with a `cloud` tag, such as `gemma4:cloud` or `gpt-oss:120b-cloud`: Ollama runs it on its own service. */
export function isCloudName(model: string): boolean {
  const tag = model.includes(':') ? model.slice(model.lastIndexOf(':') + 1).toLowerCase() : '';
  return tag === 'cloud' || tag.endsWith('-cloud');
}

/**
 * The entries of Ollama's model list that run on this Mac: named, with neither `remote_host` nor
 * `remote_model`, which Ollama gives a model it runs on another host, and no `cloud` tag.
 */
export function localModels(list: unknown): Record<string, unknown>[] {
  const models = isRecord(list) && Array.isArray(list.models) ? list.models : [];
  return models.filter(
    (entry): entry is Record<string, unknown> =>
      isRecord(entry) &&
      typeof entry.model === 'string' &&
      entry.model.length > 0 &&
      (typeof entry.name !== 'string' || !isCloudName(entry.name)) &&
      !isCloudName(entry.model) &&
      !present(entry.remote_host) &&
      !present(entry.remote_model),
  );
}

export async function startOllamaGate(options: OllamaGateOptions): Promise<OllamaGate> {
  const ollama = new URL(options.ollama);
  if (ollama.protocol !== 'http:' || ollama.hostname !== LOOPBACK) {
    throw new Error(`Ollama must be at http://${LOOPBACK}, not ${options.ollama}.`);
  }
  const target = { hostname: LOOPBACK, port: Number(ollama.port || 80) };
  const listTtlMs = options.listTtlMs ?? 2_000;
  let cached: { at: number; models: Promise<Record<string, unknown>[]> } | null = null;
  const list = (): Promise<Record<string, unknown>[]> => {
    if (cached === null || Date.now() - cached.at >= listTtlMs) {
      const models = readList(target).then(localModels);
      cached = { at: Date.now(), models };
      // A failed read is not kept: the next request reads again.
      models.catch(() => {
        if (cached?.models === models) cached = null;
      });
    }
    return cached.models;
  };
  let chats = 0;
  const waiting: (() => void)[] = [];
  const slot = async (): Promise<() => void> => {
    while (chats >= Math.max(1, options.maxChats())) {
      await new Promise<void>((resolve) => {
        waiting.push(resolve);
        setTimeout(resolve, RECHECK_MS);
      });
    }
    chats += 1;
    let released = false;
    return () => {
      if (released) return;
      released = true;
      chats -= 1;
      for (const wake of waiting.splice(0)) wake();
    };
  };

  const server = createServer((req, res) => {
    void handle(req, res).catch(() => {
      if (!res.headersSent) refuse(res, 502, 'Ollama could not be reached.');
      else res.destroy();
    });
  });

  async function handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const route = `${req.method} ${req.url}`;
    if (route === 'GET /api/tags') {
      req.resume();
      sendJson(res, 200, { models: await list() });
      return;
    }
    if (route !== 'POST /api/show' && route !== 'POST /v1/chat/completions') {
      // Answered without reading the body, so the connection is not used again.
      res.setHeader('connection', 'close');
      req.resume();
      refuse(res, 404, 'Halcyonic passes on only the model list, model details and chat.');
      return;
    }
    const raw = await readBody(req, MAX_BODY_BYTES);
    if (raw === null) {
      // The rest is never read: the connection closes once the refusal is sent.
      res.once('finish', () => req.destroy());
      res.setHeader('connection', 'close');
      refuse(res, 413, 'The request is too large.');
      return;
    }
    let body: unknown;
    try {
      body = JSON.parse(raw.toString('utf8'));
    } catch {
      refuse(res, 400, 'The request is not JSON.');
      return;
    }
    const model = isRecord(body) ? body.model : undefined;
    if (typeof model !== 'string' || !(await list()).some((entry) => entry.model === model)) {
      refuse(res, 403, 'That model does not run on this Mac.');
      return;
    }
    const release = req.url === '/v1/chat/completions' ? await slot() : () => undefined;
    res.once('close', release);
    if (res.destroyed) {
      release();
      return;
    }
    forward(target, req.url as string, JSON.stringify(body), res, release);
  }

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, LOOPBACK, () => resolve());
  });
  const { port } = server.address() as AddressInfo;
  return {
    port,
    baseUrl: `http://${LOOPBACK}:${port}/v1`,
    close: () => close(server),
  };
}

/** Passes one parsed request on to Ollama and its answer back, streamed, until either side ends. */
function forward(
  target: { hostname: string; port: number },
  path: string,
  body: string,
  res: ServerResponse,
  release: () => void,
): void {
  const upstream = httpRequest(
    {
      ...target,
      path,
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'content-length': Buffer.byteLength(body),
      },
    },
    (answer) => {
      const headers: Record<string, string> = {};
      const type = answer.headers['content-type'];
      if (typeof type === 'string') headers['content-type'] = type;
      res.writeHead(answer.statusCode ?? 502, headers);
      answer.pipe(res);
      answer.once('error', () => res.destroy());
    },
  );
  upstream.once('error', () => {
    release();
    if (!res.headersSent) refuse(res, 502, 'Ollama could not be reached.');
    else res.destroy();
  });
  // OpenCode closing its request stops the reply, and with it the model.
  res.once('close', () => upstream.destroy());
  upstream.end(body);
}

function readList(target: { hostname: string; port: number }): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const req = httpRequest(
      { ...target, path: '/api/tags', method: 'GET', timeout: LIST_TIMEOUT_MS },
      (answer) => {
        if (answer.statusCode !== 200) {
          answer.resume();
          reject(new Error(`Ollama's model list answered HTTP ${answer.statusCode}.`));
          return;
        }
        readBody(answer, MAX_LIST_BYTES).then((raw) => {
          if (raw === null) reject(new Error("Ollama's model list is too large."));
          else {
            try {
              resolve(JSON.parse(raw.toString('utf8')));
            } catch (error) {
              reject(error);
            }
          }
        }, reject);
      },
    );
    req.once('timeout', () => req.destroy(new Error("Ollama's model list timed out.")));
    req.once('error', reject);
    req.end();
  });
}

/** The whole body, or null once it passes `limit` bytes. */
function readBody(stream: IncomingMessage, limit: number): Promise<Buffer | null> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let size = 0;
    stream.on('data', (chunk: Buffer) => {
      size += chunk.length;
      if (size > limit) {
        stream.removeAllListeners('data');
        stream.pause();
        resolve(null);
        return;
      }
      chunks.push(chunk);
    });
    stream.once('end', () => resolve(Buffer.concat(chunks)));
    stream.once('error', reject);
    stream.once('close', () => resolve(size > limit ? null : Buffer.concat(chunks)));
  });
}

function present(value: unknown): boolean {
  return value !== undefined && value !== null && value !== '';
}

function sendJson(res: ServerResponse, status: number, body: unknown): void {
  const text = JSON.stringify(body);
  res.writeHead(status, {
    'content-type': 'application/json',
    'content-length': Buffer.byteLength(text),
  });
  res.end(text);
}

function refuse(res: ServerResponse, status: number, message: string): void {
  sendJson(res, status, { error: message });
}

function close(server: Server): Promise<void> {
  server.closeAllConnections();
  return new Promise((resolve) => server.close(() => resolve()));
}
