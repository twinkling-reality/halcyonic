import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/**
 * A scripted OpenAI-compatible chat completions provider for end to end tests, so OpenCode runs
 * real turns without a real model, credentials or network. It serves `GET /v1/models` and
 * `POST /v1/chat/completions` (streaming and not) on loopback, and decides each answer from the
 * request alone:
 *
 * - last message from a tool: a short text answer quoting the tool result;
 * - last user message contains `RUN_SHELL`: one call to the offered shell tool;
 * - last user message contains `ASK_QUESTION`: one call to the offered `question` tool, asking
 *   which colour to use, red or blue;
 * - last user message contains `SLOW`: text streamed one chunk per `slowChunkMs` until the client
 *   disconnects or `slowChunks` chunks were sent;
 * - last user message contains `FAIL`: HTTP 400, which a client must not retry;
 * - otherwise: a short text answer.
 */
export interface FakeProvider {
  /** Base URL to configure as the provider's `baseURL`, ending in `/v1`. */
  readonly baseUrl: string;
  /** Every chat completion request received, in arrival order. */
  readonly requests: readonly FakeProviderRequest[];
  close(): Promise<void>;
}

export interface FakeProviderRequest {
  readonly model: string | null;
  readonly stream: boolean;
  readonly toolNames: readonly string[];
  readonly lastRole: string | null;
  readonly lastUserText: string;
  /** The request body exactly as received, for assertions on what the model was shown. */
  readonly body: string;
}

export interface FakeProviderOptions {
  readonly slowChunkMs?: number;
  readonly slowChunks?: number;
}

interface ChatMessage {
  readonly role?: unknown;
  readonly content?: unknown;
}

interface ToolFunction {
  readonly name?: unknown;
  readonly parameters?: {
    readonly properties?: Record<string, unknown>;
    readonly required?: unknown;
  };
}

type Plan =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'tool'; readonly name: string; readonly args: Record<string, unknown> }
  | { readonly kind: 'slow' }
  | { readonly kind: 'fail' };

export const FAKE_SHELL_COMMAND = 'echo halcyonic-e2e';

export async function startFakeProvider(options: FakeProviderOptions = {}): Promise<FakeProvider> {
  const slowChunkMs = options.slowChunkMs ?? 250;
  const slowChunks = options.slowChunks ?? 240;
  const requests: FakeProviderRequest[] = [];
  let counter = 0;

  const server = createServer((req, res) => {
    void readBody(req).then((raw) => {
      const path = (req.url ?? '').split('?')[0];
      if (req.method === 'GET' && path === '/v1/models') {
        sendJson(res, 200, {
          object: 'list',
          data: [{ id: 'fake-model', object: 'model', created: 0, owned_by: 'halcyonic' }],
        });
        return;
      }
      if (req.method !== 'POST' || path !== '/v1/chat/completions') {
        sendJson(res, 404, { error: { message: 'not found' } });
        return;
      }
      let body: Record<string, unknown>;
      try {
        body = JSON.parse(raw) as Record<string, unknown>;
      } catch {
        sendJson(res, 400, { error: { message: 'request body is not JSON' } });
        return;
      }
      counter += 1;
      const messages = Array.isArray(body.messages) ? (body.messages as ChatMessage[]) : [];
      const tools = Array.isArray(body.tools) ? (body.tools as { function?: ToolFunction }[]) : [];
      const functions = tools.map((tool) => tool.function ?? {});
      const last = messages.at(-1);
      const request: FakeProviderRequest = {
        model: typeof body.model === 'string' ? body.model : null,
        stream: body.stream === true,
        toolNames: functions.map((fn) => String(fn.name)),
        lastRole: typeof last?.role === 'string' ? last.role : null,
        lastUserText: lastUserText(messages),
        body: raw,
      };
      requests.push(request);
      respond(res, request, plan(request, functions, counter), `chatcmpl-fake-${counter}`);
    });
  });

  function respond(res: ServerResponse, request: FakeProviderRequest, next: Plan, id: string) {
    const model = request.model ?? 'fake-model';
    if (next.kind === 'fail') {
      sendJson(res, 400, {
        error: { message: 'Fake provider refused the request.', type: 'invalid_request_error' },
      });
      return;
    }
    if (!request.stream) {
      const message =
        next.kind === 'tool'
          ? {
              role: 'assistant',
              content: null,
              tool_calls: [toolCall(id, next.name, JSON.stringify(next.args))],
            }
          : { role: 'assistant', content: next.kind === 'text' ? next.text : 'slow' };
      sendJson(res, 200, {
        id,
        object: 'chat.completion',
        created: 0,
        model,
        choices: [
          { index: 0, message, finish_reason: next.kind === 'tool' ? 'tool_calls' : 'stop' },
        ],
        usage: USAGE,
      });
      return;
    }
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
    const write = (delta: Record<string, unknown>, finish: string | null = null) =>
      res.write(`data: ${JSON.stringify(chunk(id, model, delta, finish))}\n\n`);
    const end = () => {
      res.write('data: [DONE]\n\n');
      res.end();
    };
    if (next.kind === 'tool') {
      write({ role: 'assistant', content: null, tool_calls: [toolCall(id, next.name, '')] });
      write({ tool_calls: [{ index: 0, function: { arguments: JSON.stringify(next.args) } }] });
      write({}, 'tool_calls');
      end();
      return;
    }
    if (next.kind === 'text') {
      write({ role: 'assistant', content: '' });
      write({ content: next.text });
      write({}, 'stop');
      end();
      return;
    }
    write({ role: 'assistant', content: '' });
    let sent = 0;
    const timer = setInterval(() => {
      if (res.destroyed) {
        clearInterval(timer);
        return;
      }
      sent += 1;
      write({ content: `tick ${sent}. ` });
      if (sent >= slowChunks) {
        clearInterval(timer);
        write({}, 'stop');
        end();
      }
    }, slowChunkMs);
    res.on('close', () => clearInterval(timer));
  }

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
  const { port } = server.address() as AddressInfo;
  return {
    baseUrl: `http://127.0.0.1:${port}/v1`,
    requests,
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  };
}

/** The question the `ASK_QUESTION` turn asks through OpenCode's question tool. */
export const FAKE_QUESTION = {
  questions: [
    {
      question: 'Which colour should the file mention?',
      header: 'Colour',
      options: [
        { label: 'red', description: 'The warm one' },
        { label: 'blue', description: 'The calm one' },
      ],
    },
  ],
};

const USAGE = { prompt_tokens: 12, completion_tokens: 6, total_tokens: 18 };

function plan(request: FakeProviderRequest, functions: ToolFunction[], counter: number): Plan {
  if (request.lastRole === 'tool') {
    const lastTool = toolResultText(request);
    return { kind: 'text', text: `Tool result received: ${lastTool}` };
  }
  const text = request.lastUserText;
  const shell = functions.find((fn) => fn.name === 'shell' || fn.name === 'bash');
  if (text.includes('RUN_SHELL') && shell !== undefined) {
    return { kind: 'tool', name: String(shell.name), args: shellArguments(shell) };
  }
  const question = functions.find((fn) => fn.name === 'question');
  if (text.includes('ASK_QUESTION') && question !== undefined) {
    return { kind: 'tool', name: 'question', args: FAKE_QUESTION };
  }
  if (text.includes('SLOW')) return { kind: 'slow' };
  if (text.includes('FAIL')) return { kind: 'fail' };
  return { kind: 'text', text: `Fake reply ${counter}: acknowledged.` };
}

/** Fills the shell tool's required arguments from its JSON Schema, with a harmless command. */
function shellArguments(shell: ToolFunction): Record<string, unknown> {
  const properties = shell.parameters?.properties ?? {};
  const required = Array.isArray(shell.parameters?.required)
    ? (shell.parameters.required as unknown[]).map(String)
    : [];
  const args: Record<string, unknown> = { command: FAKE_SHELL_COMMAND };
  for (const key of required) {
    if (key in args) continue;
    const type = (properties[key] as { type?: unknown } | undefined)?.type;
    args[key] = type === 'number' || type === 'integer' ? 30_000 : type === 'boolean' ? false : '';
  }
  return args;
}

function toolCall(id: string, name: string, args: string) {
  return { index: 0, id: `call_${id}`, type: 'function', function: { name, arguments: args } };
}

function chunk(id: string, model: string, delta: Record<string, unknown>, finish: string | null) {
  return {
    id,
    object: 'chat.completion.chunk',
    created: 0,
    model,
    choices: [{ index: 0, delta, finish_reason: finish }],
    ...(finish === null ? {} : { usage: USAGE }),
  };
}

function textOf(content: unknown): string {
  if (typeof content === 'string') return content;
  if (Array.isArray(content)) {
    return content
      .map((part) =>
        typeof part === 'string' ? part : String((part as { text?: unknown }).text ?? ''),
      )
      .join('');
  }
  return content === undefined || content === null ? '' : JSON.stringify(content);
}

function lastUserText(messages: readonly ChatMessage[]): string {
  for (let index = messages.length - 1; index >= 0; index -= 1) {
    const message = messages[index];
    if (message?.role === 'user') return textOf(message.content);
  }
  return '';
}

function toolResultText(request: FakeProviderRequest): string {
  const body = JSON.parse(request.body) as { messages?: ChatMessage[] };
  return textOf(body.messages?.at(-1)?.content).replace(/\s+/g, ' ').trim().slice(0, 120);
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve) => {
    const chunks: Buffer[] = [];
    req.on('data', (data: Buffer) => chunks.push(data));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', () => resolve(''));
  });
}

function sendJson(res: ServerResponse, status: number, value: unknown): void {
  res.writeHead(status, { 'content-type': 'application/json' });
  res.end(JSON.stringify(value));
}
