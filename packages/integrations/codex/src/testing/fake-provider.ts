import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/**
 * A scripted OpenAI Responses API provider for end to end tests, so Codex 0.157.0 (whose only wire
 * API is `responses`) runs real turns without a real model, credentials or network. It is a port of
 * the fake used by the Codex smoke test of 2026-09-27. It serves `POST /v1/responses` as
 * server-sent events on loopback and decides each answer from the request alone, by its last input
 * item and its last user text:
 *
 * - last item a tool output: a short text quoting the output;
 * - `FAIL`: `response.failed`;
 * - `PATCH_ADD:<file>`: an `apply_patch` call adding `<file>`;
 * - `CMD_ESC:<command>`: an `exec_command` call asking to run outside the sandbox, which needs
 *   approval under the `on-request` policy;
 * - `CMD:<command>`: a plain `exec_command` call;
 * - `SLOW_TEXT`: text streamed one delta per `slowChunkMs` until the client disconnects;
 * - `STEER`: text acknowledging the steer;
 * - otherwise: `Fake reply <n>: acknowledged.`
 *
 * A command runs to the end of its line. Every request is recorded; any other path is answered 404.
 */
export interface FakeProvider {
  /** The `base_url` to configure for the provider, ending in `/v1`. */
  readonly baseUrl: string;
  /** Every request received, in arrival order. */
  readonly requests: readonly FakeProviderRequest[];
  /** Slow streams still being written. */
  readonly streaming: number;
  close(): Promise<void>;
}

export interface FakeProviderRequest {
  readonly method: string;
  readonly path: string;
  /** Request headers; the authorization header is never recorded. */
  readonly headers: Readonly<Record<string, string>>;
  /** The JSON body of a Responses request, or null for anything else. */
  readonly body: Readonly<Record<string, unknown>> | null;
  readonly lastUserText: string;
  readonly plan: Plan['kind'] | 'not_found';
}

export interface FakeProviderOptions {
  readonly slowChunkMs?: number;
  readonly slowChunks?: number;
}

type Plan =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'function_call'; readonly name: string; readonly args: object }
  | { readonly kind: 'custom_tool_call'; readonly name: string; readonly input: string }
  | { readonly kind: 'slow' }
  | { readonly kind: 'failed' };

interface Tool {
  readonly type?: unknown;
  readonly name?: unknown;
}

interface InputItem {
  readonly type?: unknown;
  readonly role?: unknown;
  readonly content?: unknown;
  readonly output?: unknown;
}

const USAGE = {
  input_tokens: 12,
  input_tokens_details: { cached_tokens: 0 },
  output_tokens: 6,
  output_tokens_details: { reasoning_tokens: 0 },
  total_tokens: 18,
};

export const PATCH_CONTENT = 'hello from the fake provider';

export async function startFakeProvider(options: FakeProviderOptions = {}): Promise<FakeProvider> {
  const slowChunkMs = options.slowChunkMs ?? 200;
  const slowChunks = options.slowChunks ?? 150;
  const requests: FakeProviderRequest[] = [];
  let counter = 0;
  let streaming = 0;

  const server = createServer((req, res) => {
    void readBody(req).then((raw) => {
      const path = (req.url ?? '').split('?')[0] ?? '';
      const headers = recordedHeaders(req);
      if (req.method !== 'POST' || path !== '/v1/responses' || req.headers['content-encoding']) {
        requests.push({
          method: req.method ?? '',
          path,
          headers,
          body: null,
          lastUserText: '',
          plan: 'not_found',
        });
        sendJson(res, 404, { error: { message: 'not found in the fake provider' } });
        return;
      }
      let body: Record<string, unknown>;
      try {
        body = JSON.parse(raw) as Record<string, unknown>;
      } catch {
        sendJson(res, 400, { error: { message: 'the request body is not JSON' } });
        return;
      }
      counter += 1;
      const input = Array.isArray(body.input) ? (body.input as InputItem[]) : [];
      const tools = Array.isArray(body.tools) ? (body.tools as Tool[]) : [];
      const next = plan(input, tools, counter);
      requests.push({
        method: 'POST',
        path,
        headers,
        body,
        lastUserText: lastUserText(input),
        plan: next.kind,
      });
      respond(res, next, counter, body.model);
    });
  });

  function respond(res: ServerResponse, next: Plan, n: number, model: unknown): void {
    const id = `resp_fake_${n}`;
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
    const send = (event: Record<string, unknown>) =>
      res.write(`event: ${String(event.type)}\ndata: ${JSON.stringify(event)}\n\n`);
    send({
      type: 'response.created',
      response: { id, object: 'response', status: 'in_progress', model, output: [] },
    });
    if (next.kind === 'failed') {
      send({
        type: 'response.failed',
        response: {
          id,
          object: 'response',
          status: 'failed',
          error: { code: 'invalid_prompt', message: 'Fake provider scripted failure.' },
          usage: null,
        },
      });
      res.end();
      return;
    }
    const complete = (item: Record<string, unknown>) => {
      send({ type: 'response.output_item.done', output_index: 0, item });
      send({
        type: 'response.completed',
        response: {
          id,
          object: 'response',
          status: 'completed',
          model,
          output: [item],
          usage: USAGE,
        },
      });
      res.end();
    };
    if (next.kind === 'function_call' || next.kind === 'custom_tool_call') {
      const item =
        next.kind === 'function_call'
          ? {
              type: 'function_call',
              id: `fc_fake_${n}`,
              status: 'completed',
              name: next.name,
              arguments: JSON.stringify(next.args),
              call_id: `call_fake_${n}`,
            }
          : {
              type: 'custom_tool_call',
              id: `ctc_fake_${n}`,
              status: 'completed',
              name: next.name,
              input: next.input,
              call_id: `call_fake_${n}`,
            };
      send({
        type: 'response.output_item.added',
        output_index: 0,
        item: { ...item, status: 'in_progress' },
      });
      complete(item);
      return;
    }
    const messageId = `msg_fake_${n}`;
    send({
      type: 'response.output_item.added',
      output_index: 0,
      item: {
        type: 'message',
        id: messageId,
        status: 'in_progress',
        role: 'assistant',
        content: [],
      },
    });
    const message = (text: string) => ({
      type: 'message',
      id: messageId,
      status: 'completed',
      role: 'assistant',
      content: [{ type: 'output_text', text, annotations: [] }],
    });
    const delta = (text: string) =>
      send({
        type: 'response.output_text.delta',
        item_id: messageId,
        output_index: 0,
        content_index: 0,
        delta: text,
      });
    if (next.kind === 'text') {
      delta(next.text);
      complete(message(next.text));
      return;
    }
    streaming += 1;
    let text = '';
    let sent = 0;
    let stopped = false;
    const stop = () => {
      if (stopped) return;
      stopped = true;
      clearInterval(timer);
      streaming -= 1;
    };
    const timer = setInterval(() => {
      sent += 1;
      const chunk = `tick ${sent}. `;
      text += chunk;
      delta(chunk);
      if (sent >= slowChunks) {
        stop();
        complete(message(text));
      }
    }, slowChunkMs);
    res.on('close', stop);
  }

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
  const { port } = server.address() as AddressInfo;
  return {
    baseUrl: `http://127.0.0.1:${port}/v1`,
    requests,
    get streaming() {
      return streaming;
    },
    close: () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  };
}

function plan(input: readonly InputItem[], tools: readonly Tool[], n: number): Plan {
  const last = input.at(-1);
  if (last?.type === 'function_call_output' || last?.type === 'custom_tool_call_output') {
    const output = outputText(last.output).replace(/\s+/g, ' ').trim().slice(0, 300);
    return { kind: 'text', text: `Tool result received: ${output}` };
  }
  const user = lastUserText(input);
  if (user.includes('FAIL')) return { kind: 'failed' };
  const patch = /PATCH_ADD:(\S+)/.exec(user);
  if (patch !== null) {
    const body = `*** Begin Patch\n*** Add File: ${patch[1]}\n+${PATCH_CONTENT}\n*** End Patch\n`;
    return tools.some((tool) => tool.name === 'apply_patch' && tool.type === 'custom')
      ? { kind: 'custom_tool_call', name: 'apply_patch', input: body }
      : { kind: 'text', text: 'No apply_patch tool was offered.' };
  }
  const escalated = /CMD_ESC:(.+)$/m.exec(user);
  const plain = /CMD:(.+)$/m.exec(user);
  const command = (escalated ?? plain)?.[1]?.trim();
  if (command !== undefined) {
    if (!tools.some((tool) => tool.name === 'exec_command')) {
      return { kind: 'text', text: 'No exec_command tool was offered.' };
    }
    const args: Record<string, unknown> = { cmd: command, yield_time_ms: 30_000 };
    if (escalated !== null) {
      args.sandbox_permissions = 'require_escalated';
      args.justification = 'The Halcyonic end to end test needs this command.';
    }
    return { kind: 'function_call', name: 'exec_command', args };
  }
  if (user.includes('SLOW_TEXT')) return { kind: 'slow' };
  if (user.includes('STEER')) return { kind: 'text', text: `Steer received: ${user.slice(0, 80)}` };
  return { kind: 'text', text: `Fake reply ${n}: acknowledged.` };
}

/** The last user message a person wrote, skipping the context messages Codex adds. */
function lastUserText(input: readonly InputItem[]): string {
  for (let index = input.length - 1; index >= 0; index -= 1) {
    const item = input[index];
    if (item?.type !== 'message' || item.role !== 'user') continue;
    const text = contentText(item.content);
    if (!text.trimStart().startsWith('<') && !text.startsWith('# AGENTS.md')) return text;
  }
  return '';
}

function contentText(content: unknown): string {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content
    .map((part) =>
      typeof part === 'string' ? part : String((part as { text?: unknown }).text ?? ''),
    )
    .join('');
}

function outputText(output: unknown): string {
  if (typeof output === 'string') return output;
  if (Array.isArray(output)) return contentText(output);
  return JSON.stringify(output ?? '');
}

function recordedHeaders(req: IncomingMessage): Record<string, string> {
  const headers: Record<string, string> = {};
  for (const [name, value] of Object.entries(req.headers)) {
    if (name === 'authorization' || value === undefined) continue;
    headers[name] = Array.isArray(value) ? value.join(', ') : value;
  }
  return headers;
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
