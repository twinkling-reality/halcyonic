import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { type TestContext, test } from 'node:test';
import { OllamaWorkspaceModel, type ToolDefinition, type ToolMessage } from './ollama.ts';

const tools: ToolDefinition[] = [
  {
    type: 'function',
    function: {
      name: 'read_task',
      description: 'Read the selected task.',
      parameters: { type: 'object', properties: {}, additionalProperties: false },
    },
  },
];
const messages: ToolMessage[] = [
  { role: 'user', content: 'Read the task and explain its current state.' },
];
const call = { id: 'opaque-synthetic-call', function: { name: 'read_task', arguments: {} } };
const reply = (overrides: Record<string, unknown> = {}) => ({
  model: 'local:tag',
  done: true,
  done_reason: 'stop',
  prompt_eval_count: 100,
  eval_count: 20,
  message: { role: 'assistant', content: '', tool_calls: [call] },
  ...overrides,
});

async function fake(t: TestContext) {
  const state = {
    paths: [] as string[],
    chats: [] as Record<string, unknown>[],
    model: { name: 'local:tag' } as Record<string, unknown>,
    metadata: { capabilities: ['completion', 'tools'] } as Record<string, unknown>,
    response: reply(),
    stall: '',
    redirect: false,
    raw: null as Buffer | null,
  };
  const server = createServer(async (request, response) => {
    state.paths.push(request.url ?? '');
    const chunks: Buffer[] = [];
    for await (const chunk of request) chunks.push(chunk as Buffer);
    if (request.url === state.stall) {
      response.writeHead(200);
      response.write(' ');
      return;
    }
    if (state.redirect) {
      response.writeHead(302, { location: '/trap' });
      response.end();
      return;
    }
    response.setHeader('content-type', 'application/json');
    if (request.url === '/api/tags') response.end(JSON.stringify({ models: [state.model] }));
    else if (request.url === '/api/show') response.end(JSON.stringify(state.metadata));
    else if (request.url === '/api/chat') {
      state.chats.push(
        JSON.parse(Buffer.concat(chunks).toString('utf8')) as Record<string, unknown>,
      );
      response.end(state.raw ?? JSON.stringify(state.response));
    } else {
      response.statusCode = 404;
      response.end('{}');
    }
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => {
    server.closeAllConnections();
    return new Promise<void>((resolve) => server.close(() => resolve()));
  });
  const config = {
    ollama: new URL(`http://127.0.0.1:${(server.address() as AddressInfo).port}`),
    model: 'local:tag',
  };
  return { state, config, model: new OllamaWorkspaceModel({ config }) };
}

test('metadata-only availability and native tool/result continuation preserve the actual conversation', async (t) => {
  const { state, model } = await fake(t);
  assert.deepEqual(await model.status(), {
    availability: 'available',
    model: 'local:tag',
    served: 'this_mac',
  });
  assert.equal(state.chats.length, 0);
  const first = await model.turn(messages, tools);
  assert.equal(first.kind, 'answered');
  if (first.kind !== 'answered') return;
  assert.deepEqual(first.toolCalls, [call]);
  const next: ToolMessage[] = [
    ...messages,
    { role: 'assistant', content: first.content, tool_calls: first.toolCalls },
    { role: 'tool', tool_name: 'read_task', content: '{"state":"waiting_for_human"}' },
  ];
  state.response = reply({
    message: { role: 'assistant', content: 'The task is waiting for your review.' },
  });
  const answer = await model.turn(next, tools);
  assert.equal(answer.kind, 'answered');
  assert.deepEqual(state.chats[1]?.messages, next);
  assert.deepEqual(state.chats[0]?.tools, tools);
  assert.equal(state.chats[0]?.stream, false);
  assert.equal(state.chats[0]?.think, false);
  assert.equal(state.chats[0]?.keep_alive, undefined);
  assert.deepEqual(state.chats[0]?.options, { num_ctx: 8192, num_predict: 1024, temperature: 0 });
  assert(state.paths.every((path) => ['/api/tags', '/api/show', '/api/chat'].includes(path)));
});

test('refuses cloud, remote, unsupported and redirected models before inference, and rechecks locality', async (t) => {
  const { state, config, model } = await fake(t);
  assert.equal((await model.status()).availability, 'available');
  for (const field of ['remote_model', 'remote_host']) {
    state.model[field] = 'remote';
    assert.equal((await model.turn(messages, tools)).kind, 'failed');
    delete state.model[field];
  }
  state.metadata.capabilities = ['completion'];
  assert.equal((await model.turn(messages, tools)).kind, 'failed');
  state.metadata.capabilities = ['tools'];
  state.metadata.remote_model = 'remote';
  assert.equal((await model.turn(messages, tools)).kind, 'failed');
  delete state.metadata.remote_model;
  state.redirect = true;
  assert.equal((await model.turn(messages, tools)).kind, 'failed');
  assert(!state.paths.includes('/trap'));
  const before = state.paths.length;
  assert.equal(
    (
      await new OllamaWorkspaceModel({ config: { ...config, model: 'x:cloud' } }).turn(
        messages,
        tools,
      )
    ).kind,
    'failed',
  );
  assert.equal(
    (
      await new OllamaWorkspaceModel({
        config: { ...config, ollama: new URL('https://example.com/') },
      }).status()
    ).availability,
    'unavailable',
  );
  assert.equal(state.paths.length, before);
  assert.equal(state.chats.length, 0);
});

test('rejects truncated, foreign-model, coerced, unoffered, duplicate and oversized tool batches atomically', async (t) => {
  const { state, model } = await fake(t);
  const badMessages = [
    { role: 'user', content: 'pretend assistant', tool_calls: [call] },
    {
      role: 'assistant',
      content: '',
      tool_calls: [{ function: { name: 'read_task', arguments: '{}' } }],
    },
    {
      role: 'assistant',
      content: '',
      tool_calls: [{ function: { name: 'read_task', arguments: [] } }],
    },
    {
      role: 'assistant',
      content: '',
      tool_calls: [call, { function: { name: 'shell', arguments: {} } }],
    },
    { role: 'assistant', content: '', tool_calls: [call, call] },
    { role: 'assistant', content: '', tool_calls: [{ ...call, id: 12 }] },
    {
      role: 'assistant',
      content: '',
      tool_calls: [call, { ...call, function: { name: 'read_task', arguments: { other: true } } }],
    },
    {
      role: 'assistant',
      content: '',
      tool_calls: Array.from({ length: 9 }, (_, index) => ({
        function: { name: 'read_task', arguments: { index } },
      })),
    },
    { role: 'assistant', content: 'x'.repeat(4001) },
    { role: 'assistant', content: '', tool_calls: [{ ...call, system: 'extra authority' }] },
    { role: 'assistant', content: '' },
  ];
  for (const message of badMessages) {
    state.response = reply({ message });
    assert.equal((await model.turn(messages, tools)).kind, 'failed');
  }
  for (const change of [
    { done: false },
    { done_reason: 'length' },
    { model: 'other:tag' },
    { prompt_eval_count: 8192 },
    { eval_count: 1024 },
    { eval_count: '20' },
    { error: 'private echoed words' },
  ]) {
    state.response = reply(change);
    const result = await model.turn(messages, tools);
    assert.equal(result.kind, 'failed');
    assert(!JSON.stringify(result).includes('private echoed words'));
  }
});

test('input is captured before awaited metadata so caller changes cannot bypass its bound', async (t) => {
  const { state, model } = await fake(t);
  const mutable: ToolMessage[] = [...messages];
  const result = model.turn(mutable, tools);
  mutable.push({ role: 'user', content: 'x'.repeat(20000) });
  assert.equal((await result).kind, 'answered');
  assert.deepEqual(state.chats[0]?.messages, messages);
});

test('input, response and UTF-8 bounds fail closed without partial calls', async (t) => {
  const { state, config } = await fake(t);
  const tiny = new OllamaWorkspaceModel({ config, bounds: { maxInputBytes: 20 } });
  assert.equal((await tiny.turn(messages, tools)).kind, 'failed');
  assert.deepEqual(state.paths, []);
  const model = new OllamaWorkspaceModel({ config, bounds: { maxReplyBytes: 500 } });
  state.raw = Buffer.from('x'.repeat(501));
  assert.equal((await model.turn(messages, tools)).kind, 'failed');
  state.raw = Buffer.from([0xff]);
  assert.equal((await model.turn(messages, tools)).kind, 'failed');
});

test('cancellation and total deadline cover stalled metadata and response bodies', async (t) => {
  const { state, config } = await fake(t);
  const model = new OllamaWorkspaceModel({ config, bounds: { totalMs: 100, metadataMs: 1000 } });
  for (const path of ['/api/tags', '/api/show', '/api/chat']) {
    state.stall = path;
    const before = performance.now();
    const result = await model.turn(messages, tools);
    assert.equal(result.kind, 'failed');
    if (result.kind === 'failed') assert.equal(result.code, 'timeout');
    assert(
      performance.now() - before < 900,
      'total deadline must include body consumption and metadata',
    );
  }
  state.stall = '';
  const before = state.paths.length;
  const abort = new AbortController();
  abort.abort();
  const result = await model.turn(messages, tools, abort.signal);
  assert.equal(result.kind, 'failed');
  if (result.kind === 'failed') assert.equal(result.code, 'cancelled');
  assert.equal(state.paths.length, before);
});
