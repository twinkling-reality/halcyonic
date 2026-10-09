import {
  createServer,
  request as httpRequest,
  type IncomingMessage,
  type Server,
  type ServerResponse,
} from 'node:http';
import type { AddressInfo, Socket } from 'node:net';
import { isRecord } from './events.ts';

/**
 * The only way the sandboxed OpenCode server, and every command it runs, reaches Ollama (ADR 0028):
 * a loopback server in the control plane's own process, outside the sandbox, that passes on the
 * three requests OpenCode 2.0.18 makes of Ollama (core/src/plugin/provider/ollama.ts) and refuses
 * everything else before it reaches Ollama:
 *
 * - `GET /api/tags`, the model list, answered with only the models that run on this Mac;
 * - `POST /api/show` and `POST /v1/chat/completions`, each naming one of those models in a JSON
 *   body of at most 8 MiB. The body passed on is built anew from the fields OpenCode sends, so
 *   nothing else in it reaches Ollama, and a body with keys that differ only in letter case is
 *   refused, since Ollama reads keys that way and the last would win. The chat streams back as
 *   Ollama writes it, and closing the request closes the gate's own, which stops the model.
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
   * 2.0.18 opens one at a time for each session with a running turn (runtime test, 2026-10-08),
   * since Halcyonic names its sessions itself, so OpenCode asks for no title beside a turn, and
   * denies the subagent tool, so no child session runs beside one. At least one is always let
   * through; more wait their turn.
   */
  readonly maxChats: () => number;
  /** How long a model list read from Ollama is used for the checks. By default 2 s. */
  readonly listTtlMs?: number;
  /** How long a chat waits for its turn before it is refused. By default 60 s. */
  readonly chatWaitMs?: number;
  /**
   * How long a connection may take to send a request's headers, and a request may wait to be
   * read. By default 15 s.
   */
  readonly requestTimeoutMs?: number;
  /** How long a request may take to send its body once it is being read. By default 5 s. */
  readonly bodyTimeoutMs?: number;
}

/** The largest request body passed on: OpenCode's whole conversation with the model, as JSON. */
export const MAX_BODY_BYTES = 8 * 1024 * 1024;
/** The deepest a request body nests; OpenCode's tool schemas nest far less. */
export const MAX_DEPTH = 64;
/** Chats waiting their turn at once; one more is refused. */
export const MAX_WAITING_CHATS = 4;
/**
 * Connections open at once, and request bodies read and parsed at once. Past the first, Node
 * closes a new connection at once, which OpenCode sees as a failed request.
 */
const MAX_CONNECTIONS = 256;
const MAX_READING = 2;
/**
 * How long a connection may take to send a request's headers, from when it opens or its last reply
 * ends, and how long a request may wait for a place to be read: one that sends nothing, or sends
 * slowly, is closed, so a few cannot hold the gate's places.
 */
const REQUEST_TIMEOUT_MS = 15_000;
/** How long a request may take to send its body once it has a place to be read. */
const BODY_TIMEOUT_MS = 5_000;
/**
 * The fields of a chat request OpenCode 2.0.18 sends to an OpenAI-compatible provider
 * (ai/src/protocols/openai-chat.ts, `bodyFields`), the only ones passed on.
 */
const CHAT_FIELDS: readonly string[] = [
  'model',
  'messages',
  'tools',
  'tool_choice',
  'stream',
  'stream_options',
  'store',
  'prompt_cache_key',
  'reasoning_effort',
  'tool_stream',
  'max_completion_tokens',
  'max_tokens',
  'temperature',
  'top_p',
  'frequency_penalty',
  'presence_penalty',
  'seed',
  'stop',
];
/** The largest model list read: Ollama lists every model it holds, a few hundred bytes each. */
const MAX_LIST_BYTES = 1024 * 1024;
const LIST_TIMEOUT_MS = 5_000;
/** How often waiting chats read the number allowed again, since a turn starting raises it. */
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

/**
 * Whether some object in `value` has two keys Ollama would read as one: Go's JSON decoder matches
 * a field's name in any letter case, folding `K` (U+212A) to k and `ſ` (U+017F) to s, and keeps
 * the last value it meets.
 */
export function hasFoldedDuplicate(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(hasFoldedDuplicate);
  if (!isRecord(value)) return false;
  const seen = new Set<string>();
  for (const key of Object.keys(value)) {
    const folded = key.toLowerCase().replaceAll('ſ', 's');
    if (seen.has(folded)) return true;
    seen.add(folded);
  }
  return Object.values(value).some(hasFoldedDuplicate);
}

/** Whether JSON text nests arrays and objects deeper than `limit`, read before parsing it. */
export function nestsDeeper(text: Buffer, limit: number): boolean {
  let depth = 0;
  let inString = false;
  for (let index = 0; index < text.length; index += 1) {
    const byte = text[index];
    if (inString) {
      if (byte === 0x5c) index += 1;
      else if (byte === 0x22) inString = false;
    } else if (byte === 0x22) inString = true;
    else if (byte === 0x5b || byte === 0x7b) {
      depth += 1;
      if (depth > limit) return true;
    } else if (byte === 0x5d || byte === 0x7d) depth -= 1;
  }
  return false;
}

type Waited = { readonly kind: 'go'; readonly release: () => void } | { readonly kind: 'busy' };

export async function startOllamaGate(options: OllamaGateOptions): Promise<OllamaGate> {
  const ollama = new URL(options.ollama);
  if (ollama.protocol !== 'http:' || ollama.hostname !== LOOPBACK) {
    throw new Error(`Ollama must be at http://${LOOPBACK}, not ${options.ollama}.`);
  }
  const target = { hostname: LOOPBACK, port: Number(ollama.port || 80) };
  const listTtlMs = options.listTtlMs ?? 2_000;
  const chatWaitMs = options.chatWaitMs ?? 60_000;
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

  // Bodies read at once, in order of arrival. A slot passes straight to the next request waiting,
  // and a request that leaves while it waits gives up its place.
  let reading = 0;
  const readers: { go(): void; leave(): void }[] = [];
  const read = async (req: IncomingMessage): Promise<Buffer | null> => {
    if (reading >= MAX_READING) {
      const handed = await new Promise<boolean>((resolve) => {
        if (req.destroyed) {
          resolve(false);
          return;
        }
        const finish = (outcome: boolean) => {
          clearTimeout(timer);
          req.off('close', left);
          const index = readers.indexOf(entry);
          if (index >= 0) readers.splice(index, 1);
          resolve(outcome);
        };
        const entry = { go: () => finish(true), leave: () => finish(false) };
        const left = () => finish(false);
        const timer = setTimeout(left, requestTimeoutMs);
        req.once('close', left);
        readers.push(entry);
      });
      if (!handed) throw new Busy();
    } else {
      reading += 1;
    }
    try {
      return await readBody(req, MAX_BODY_BYTES, bodyTimeoutMs);
    } finally {
      const next = readers.shift();
      if (next === undefined) reading -= 1;
      else next.go();
    }
  };

  // Chats open at once, and those waiting their turn, first come first served.
  let chats = 0;
  const queue: { admit(): void; leave(outcome: Waited | null): void }[] = [];
  let recheck: NodeJS.Timeout | null = null;
  const allowed = () => Math.max(1, options.maxChats());
  const admit = () => {
    while (queue.length > 0 && chats < allowed()) {
      chats += 1;
      queue.shift()?.admit();
    }
    if (queue.length > 0 && recheck === null) recheck = setInterval(admit, RECHECK_MS);
    if (queue.length === 0 && recheck !== null) {
      clearInterval(recheck);
      recheck = null;
    }
  };
  const releaser = () => {
    let released = false;
    return () => {
      if (released) return;
      released = true;
      chats -= 1;
      admit();
    };
  };
  /** A turn for one chat, or `busy` when too many wait or it waited too long; null if it left. */
  const turn = (res: ServerResponse): Promise<Waited | null> =>
    new Promise((resolve) => {
      if (res.destroyed) {
        resolve(null);
        return;
      }
      if (queue.length === 0 && chats < allowed()) {
        chats += 1;
        resolve({ kind: 'go', release: releaser() });
        return;
      }
      if (queue.length >= MAX_WAITING_CHATS) {
        resolve({ kind: 'busy' });
        return;
      }
      const waiter = {
        admit: () => leave({ kind: 'go', release: releaser() }),
        leave: (outcome: Waited | null) => leave(outcome),
      };
      const leave = (outcome: Waited | null) => {
        clearTimeout(timer);
        res.off('close', gone);
        const index = queue.indexOf(waiter);
        if (index >= 0) queue.splice(index, 1);
        admit();
        resolve(outcome);
      };
      const timer = setTimeout(() => leave({ kind: 'busy' }), chatWaitMs);
      const gone = () => leave(null);
      res.once('close', gone);
      queue.push(waiter);
      admit();
    });

  // Each connection's time to send a whole request, counted by the gate itself: once a request is
  // read, its reply streams back for as long as the model runs.
  const requestTimeoutMs = options.requestTimeoutMs ?? REQUEST_TIMEOUT_MS;
  const bodyTimeoutMs = options.bodyTimeoutMs ?? BODY_TIMEOUT_MS;
  const deadlines = new Map<Socket, NodeJS.Timeout>();
  const disarm = (socket: Socket) => {
    clearTimeout(deadlines.get(socket));
    deadlines.delete(socket);
  };
  const arm = (socket: Socket) => {
    disarm(socket);
    if (!socket.destroyed) {
      deadlines.set(
        socket,
        setTimeout(() => socket.destroy(), requestTimeoutMs),
      );
    }
  };

  // OpenCode sends one request at a time on a connection; one sent while a reply is still open
  // closes the connection, so a request never goes untimed behind another's reply.
  const answering = new Set<Socket>();
  const server = createServer((req, res) => {
    const socket = req.socket;
    if (answering.has(socket)) {
      socket.destroy();
      return;
    }
    answering.add(socket);
    disarm(socket);
    res.once('close', () => {
      answering.delete(socket);
      arm(socket);
    });
    void handle(req, res).catch((error: unknown) => {
      if (res.headersSent) res.destroy();
      else if (error instanceof Busy) refuse(res, 503, 'The gate to Ollama is busy.');
      else refuse(res, 502, 'Ollama could not be reached.');
    });
  });
  server.on('connection', (socket: Socket) => {
    arm(socket);
    socket.once('close', () => disarm(socket));
  });
  server.maxConnections = MAX_CONNECTIONS;

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
    const raw = await read(req);
    if (raw === null) {
      // The rest is never read: the connection closes once the refusal is sent.
      res.once('finish', () => req.destroy());
      res.setHeader('connection', 'close');
      refuse(res, 413, 'The request is too large.');
      return;
    }
    const body = parseBody(raw);
    if (body === null) {
      refuse(res, 400, 'The request is not a JSON object Halcyonic passes on.');
      return;
    }
    const model = body.model;
    if (typeof model !== 'string' || !(await list()).some((entry) => entry.model === model)) {
      refuse(res, 403, 'That model does not run on this Mac.');
      return;
    }
    // Built anew from the fields OpenCode sends: model details by the model alone.
    const sent =
      req.url === '/api/show'
        ? JSON.stringify({ model })
        : JSON.stringify(
            Object.fromEntries(
              CHAT_FIELDS.filter((key) => Object.hasOwn(body, key)).map((key) => [key, body[key]]),
            ),
          );
    if (req.url === '/api/show') {
      forward(target, '/api/show', sent, res, () => undefined);
      return;
    }
    const waited = await turn(res);
    if (waited === null) return;
    if (waited.kind === 'busy') {
      refuse(res, 503, 'Too many replies are waiting for the model on this Mac.');
      return;
    }
    res.once('close', waited.release);
    if (res.destroyed) {
      waited.release();
      return;
    }
    forward(target, '/v1/chat/completions', sent, res, waited.release);
  }

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, LOOPBACK, () => resolve());
  });
  const { port } = server.address() as AddressInfo;
  return {
    port,
    baseUrl: `http://${LOOPBACK}:${port}/v1`,
    close: async () => {
      if (recheck !== null) clearInterval(recheck);
      recheck = null;
      for (const waiter of [...queue]) waiter.leave(null);
      for (const reader of [...readers]) reader.leave();
      for (const socket of [...deadlines.keys()]) disarm(socket);
      await close(server);
    },
  };
}

/** A request body as a JSON object, or null when it is not one, nests too deep or repeats a key. */
function parseBody(raw: Buffer): Record<string, unknown> | null {
  if (nestsDeeper(raw, MAX_DEPTH)) return null;
  let body: unknown;
  try {
    body = JSON.parse(raw.toString('utf8'));
  } catch {
    return null;
  }
  return isRecord(body) && !hasFoldedDuplicate(body) ? body : null;
}

/** Passes one request on to Ollama and its answer back, streamed, until either side ends. */
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

/** The whole body, or null once it passes `limit` bytes; a body cut off before its end fails. */
function readBody(
  stream: IncomingMessage,
  limit: number,
  timeoutMs?: number,
): Promise<Buffer | null> {
  return new Promise((resolve, reject) => {
    if (stream.destroyed) {
      reject(new Error('The request ended before its body was read.'));
      return;
    }
    // Too slow: the connection is closed, which ends the read below as cut off.
    const timer =
      timeoutMs === undefined ? null : setTimeout(() => stream.socket.destroy(), timeoutMs);
    stream.once('close', () => {
      if (timer !== null) clearTimeout(timer);
    });
    const chunks: Buffer[] = [];
    let size = 0;
    let settled = false;
    stream.on('data', (chunk: Buffer) => {
      size += chunk.length;
      if (size > limit) {
        settled = true;
        stream.removeAllListeners('data');
        stream.pause();
        resolve(null);
        return;
      }
      chunks.push(chunk);
    });
    stream.once('end', () => {
      settled = true;
      if (timer !== null) clearTimeout(timer);
      resolve(Buffer.concat(chunks));
    });
    stream.once('error', reject);
    stream.once('close', () => {
      if (!settled) reject(new Error('The request ended before its body did.'));
    });
  });
}

/** A request refused because the gate is busy: it waited too long for a place to be read. */
class Busy extends Error {}

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
