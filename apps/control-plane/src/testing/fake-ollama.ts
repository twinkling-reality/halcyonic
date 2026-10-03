import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/** Test support only: how the fake answers one `POST /api/chat`. */
export interface FakeReply {
  /** The reply's text, sent in `chunks` pieces; ignored with `status`. */
  readonly content?: string;
  /** Answer with this status and an `{"error": ...}` body instead. */
  readonly status?: number;
  /** Wait before the first line, as Ollama does while another request runs or a model loads. */
  readonly firstLineAfterMs?: number;
  /** Wait between lines. */
  readonly betweenLinesMs?: number;
  /** How many lines the content is split into. */
  readonly chunks?: number;
  /** End the stream with an error line instead of a done line. */
  readonly errorLine?: boolean;
}

export interface ChatBody {
  readonly model: string;
  readonly messages: { role: string; content: string }[];
  readonly stream?: boolean;
  readonly think?: unknown;
  readonly format?: unknown;
  readonly keep_alive?: unknown;
  readonly tools?: unknown;
  readonly options?: Record<string, unknown>;
}

/**
 * A stand-in for Ollama's HTTP API on loopback (`GET /api/tags`, `POST /api/chat` streamed), as
 * documented and as observed in docs/internal/validation/companion-model.md. It records every chat
 * request and counts the ones the client closed before the reply ended.
 */
export async function startFakeOllama(
  options: { readonly models?: Record<string, unknown>[] } = {},
) {
  const state = {
    models: options.models ?? [{ name: 'local-model:tag', model: 'local-model:tag', size: 1 }],
    replies: [] as FakeReply[],
    requests: [] as ChatBody[],
    tagReads: 0,
    closedEarly: 0,
    /** Refuse a request that asks for a schema, as the MLX engine does. */
    refuseFormat: false,
    paths: [] as string[],
  };
  const server = createServer((request: IncomingMessage, response: ServerResponse) => {
    state.paths.push(`${request.method} ${request.url}`);
    if (request.method === 'GET' && request.url === '/api/tags') {
      state.tagReads += 1;
      response.setHeader('content-type', 'application/json');
      response.end(JSON.stringify({ models: state.models }));
      return;
    }
    if (request.method === 'POST' && request.url === '/api/chat') {
      const chunks: Buffer[] = [];
      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        const body = JSON.parse(Buffer.concat(chunks).toString('utf8')) as ChatBody;
        state.requests.push(body);
        if (state.refuseFormat && body.format !== undefined) {
          response.statusCode = 501;
          response.setHeader('content-type', 'application/json');
          response.end(JSON.stringify({ error: 'structured output is unavailable' }));
          return;
        }
        void answer(response, state.replies.shift() ?? { content: '{}' }).catch(() => undefined);
      });
      return;
    }
    response.statusCode = 404;
    response.end(JSON.stringify({ error: 'not found' }));
  });
  const answer = async (response: ServerResponse, reply: FakeReply) => {
    let finished = false;
    response.on('close', () => {
      if (!finished) state.closedEarly += 1;
    });
    if (reply.status !== undefined) {
      finished = true;
      response.statusCode = reply.status;
      response.setHeader('content-type', 'application/json');
      response.end(JSON.stringify({ error: 'model not found' }));
      return;
    }
    response.setHeader('content-type', 'application/x-ndjson');
    response.flushHeaders();
    await waitOrClosed(response, reply.firstLineAfterMs ?? 0);
    const content = reply.content ?? '';
    const pieces = Math.max(1, reply.chunks ?? 1);
    const size = Math.ceil(content.length / pieces) || 1;
    for (let at = 0; at < content.length || at === 0; at += size) {
      if (response.destroyed) return;
      const line = {
        message: { role: 'assistant', content: content.slice(at, at + size) },
        done: false,
      };
      response.write(`${JSON.stringify(line)}\n`);
      if (at + size < content.length) await sleep(reply.betweenLinesMs ?? 0);
      if (content.length === 0) break;
    }
    if (response.destroyed) return;
    finished = true;
    if (reply.errorLine === true) {
      response.end(
        `${JSON.stringify({ error: 'an error was encountered while running the model' })}\n`,
      );
      return;
    }
    response.end(
      `${JSON.stringify({ message: { role: 'assistant', content: '' }, done: true, done_reason: 'stop', prompt_eval_count: 571, eval_count: 99 })}\n`,
    );
  };
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  return {
    get requests() {
      return state.requests;
    },
    get tagReads() {
      return state.tagReads;
    },
    get closedEarly() {
      return state.closedEarly;
    },
    get paths() {
      return state.paths;
    },
    url: new URL(`http://127.0.0.1:${port}`),
    /** Queues how the next chat requests are answered, in order. */
    answer(...replies: FakeReply[]) {
      state.replies.push(...replies);
    },
    /** Refuses every request that asks for a schema from now on, as Ollama's MLX engine does. */
    refuseFormat() {
      state.refuseFormat = true;
    },
    setModels(models: Record<string, unknown>[]) {
      state.models = models;
    },
    async stop() {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}

export type FakeOllama = Awaited<ReturnType<typeof startFakeOllama>>;

function sleep(ms: number): Promise<void> {
  return ms <= 0 ? Promise.resolve() : new Promise((resolve) => setTimeout(resolve, ms));
}

/** Waits `ms`, or less once the client has closed the request, so a model kept late never holds a test open. */
function waitOrClosed(response: ServerResponse, ms: number): Promise<void> {
  if (ms <= 0 || response.destroyed) return Promise.resolve();
  return new Promise((resolve) => {
    const done = () => {
      clearTimeout(timer);
      response.off('close', done);
      resolve();
    };
    const timer = setTimeout(done, ms);
    response.once('close', done);
  });
}
