import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { type FakeReply, startFakeOllama } from '../testing/fake-ollama.ts';
import { type ChatRequest, chat, checkModel, isCloudName } from './ollama.ts';

async function fake(t: TestContext, models?: Record<string, unknown>[]) {
  const ollama = await startFakeOllama(models === undefined ? {} : { models });
  t.after(() => ollama.stop());
  return ollama;
}

/** A loopback port nothing listens on. */
async function closedPort(): Promise<URL> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as { port: number };
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return new URL(`http://127.0.0.1:${port}`);
}

function request(base: URL, overrides: Partial<ChatRequest> = {}): ChatRequest {
  return {
    base,
    model: 'local-model:tag',
    messages: [{ role: 'user', content: '<person>an idea</person>' }],
    contextTokens: 8192,
    outputTokens: 512,
    temperature: 0.3,
    firstTokenMs: 2_000,
    totalMs: 4_000,
    maxCharacters: 4_096,
    ...overrides,
  };
}

describe('the model check', () => {
  test('finds a listed model that runs here, by its name or as its :latest', async (t) => {
    const ollama = await fake(t, [
      { name: 'local-model:tag', model: 'local-model:tag' },
      { name: 'plain:latest', model: 'plain:latest' },
    ]);
    assert.deepEqual(await checkModel(ollama.url, 'local-model:tag', 1000), { kind: 'local' });
    assert.deepEqual(await checkModel(ollama.url, 'plain', 1000), { kind: 'local' });
    assert.deepEqual(await checkModel(ollama.url, 'other:tag', 1000), { kind: 'missing' });
    assert.deepEqual(await checkModel(ollama.url, 'local-model', 1000), { kind: 'missing' });
  });

  test('refuses a model Ollama would pass to another host, or a cloud model, without asking it', async (t) => {
    const ollama = await fake(t, [
      { name: 'proxied:tag', remote_host: 'https://ollama.com', remote_model: 'big:tag' },
      { name: 'renamed:tag', remote_model: 'big:tag' },
      { name: 'gemma4:cloud' },
    ]);
    for (const model of ['proxied:tag', 'renamed:tag'])
      assert.deepEqual(await checkModel(ollama.url, model, 1000), { kind: 'remote' }, model);
    const reads = ollama.tagReads;
    for (const model of ['gemma4:cloud', 'gpt-oss:120b-cloud'])
      assert.deepEqual(await checkModel(ollama.url, model, 1000), { kind: 'remote' }, model);
    assert.equal(ollama.tagReads, reads);
    assert.equal(isCloudName('cloud-notes:latest'), false);
    assert.equal(isCloudName('model:tag'), false);
  });

  test('says Ollama is not running when nothing listens', async () => {
    assert.deepEqual(await checkModel(await closedPort(), 'local-model:tag', 1000), {
      kind: 'not_running',
    });
  });
});

describe('one chat reply', () => {
  test('asks for one streamed reply with thinking off, no tools, no format and no keep_alive', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: '{"next":"ask"}', chunks: 3 });
    const result = await chat(request(ollama.url));
    assert.equal(result.kind, 'answered');
    assert.equal(result.kind === 'answered' && result.content, '{"next":"ask"}');
    assert.equal(result.kind === 'answered' && result.promptTokens, 571);
    const [sent] = ollama.requests;
    assert.ok(sent);
    assert.equal(sent.stream, true);
    assert.equal(sent.think, false);
    for (const absent of ['format', 'keep_alive', 'tools'] as const)
      assert.equal(sent[absent], undefined, absent);
    assert.deepEqual(sent.options, { num_ctx: 8192, num_predict: 512, temperature: 0.3 });
    assert.deepEqual(ollama.paths, ['POST /api/chat']);
  });

  test('asks for the schema when given one, and says when the engine cannot keep to it', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: '{}' });
    const schema = { type: 'object' };
    assert.equal((await chat(request(ollama.url, { format: schema }))).kind, 'answered');
    assert.deepEqual(ollama.requests[0]?.format, schema);
    ollama.refuseFormat();
    const refused = await chat(request(ollama.url, { format: schema }));
    assert.equal(refused.kind === 'failed' && refused.reason, 'format_unavailable');
  });

  test('closes the request when the first line is late, which stops the model', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: 'late', firstLineAfterMs: 1_000 });
    const result = await chat(request(ollama.url, { firstTokenMs: 100 }));
    assert.equal(result.kind === 'failed' && result.reason, 'too_slow');
    await waitFor(() => ollama.closedEarly === 1);
  });

  test('closes the request when the whole reply is late', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: 'x'.repeat(40), chunks: 40, betweenLinesMs: 50 });
    const result = await chat(request(ollama.url, { totalMs: 300 }));
    assert.equal(result.kind === 'failed' && result.reason, 'too_slow');
    assert.ok(result.firstTokenMs !== null);
    await waitFor(() => ollama.closedEarly === 1);
  });

  test('closes the request when the reply passes its length', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: 'x'.repeat(200), chunks: 10, betweenLinesMs: 20 });
    const result = await chat(request(ollama.url, { maxCharacters: 50 }));
    assert.equal(result.kind === 'failed' && result.reason, 'too_long');
    await waitFor(() => ollama.closedEarly === 1);
  });

  test('closes the request when the caller ends it', async (t) => {
    const ollama = await fake(t);
    ollama.answer({ content: 'slow', firstLineAfterMs: 1_000 });
    const ended = new AbortController();
    setTimeout(() => ended.abort(), 50);
    const result = await chat(request(ollama.url, { signal: ended.signal }));
    assert.equal(result.kind === 'failed' && result.reason, 'cancelled');
    await waitFor(() => ollama.closedEarly === 1);
  });

  test('tells a missing model, an error line, and Ollama not running apart', async (t) => {
    const ollama = await fake(t);
    const cases: [FakeReply, string][] = [
      [{ status: 404 }, 'missing'],
      [{ status: 500 }, 'error'],
      [{ content: 'part', errorLine: true }, 'error'],
    ];
    for (const [reply, reason] of cases) {
      ollama.answer(reply);
      const result = await chat(request(ollama.url));
      assert.equal(result.kind === 'failed' && result.reason, reason, JSON.stringify(reply));
    }
    const result = await chat(request(await closedPort()));
    assert.equal(result.kind === 'failed' && result.reason, 'not_running');
  });
});

async function waitFor(condition: () => boolean, ms = 2_000): Promise<void> {
  const until = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > until) assert.fail('the condition never held');
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}
