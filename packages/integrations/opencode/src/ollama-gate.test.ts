import assert from 'node:assert/strict';
import { createServer, type IncomingMessage, request, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import {
  hasFoldedDuplicate,
  isCloudName,
  localModels,
  MAX_BODY_BYTES,
  MAX_DEPTH,
  MAX_WAITING_CHATS,
  nestsDeeper,
  startOllamaGate,
} from './ollama-gate.ts';

const TAG = 'stand-in:1b';
const LIST = {
  models: [
    { name: TAG, model: TAG, digest: 'a' },
    { name: 'gemma4:cloud', model: 'gemma4:cloud', digest: 'c' },
    { name: 'gpt-oss:120b-cloud', model: 'gpt-oss:120b-cloud', digest: 'd' },
    { name: 'big:70b', model: 'big:70b', remote_host: 'https://ollama.com:443', digest: 'r' },
    { name: 'other:7b', model: 'other:7b', remote_model: 'other:7b', digest: 's' },
    { name: '', model: '', digest: 'e' },
  ],
};

interface Received {
  readonly method: string;
  readonly url: string;
  readonly headers: IncomingMessage['headers'];
  readonly body: string;
}

/** Plays Ollama: the list above, show and a chat that streams until `release` or the client goes. */
async function upstream(t: TestContext, options: { listStatus?: number } = {}) {
  const received: Received[] = [];
  const open = new Set<ServerResponse>();
  const closed: string[] = [];
  const server = createServer((req, res) => {
    const chunks: Buffer[] = [];
    req.on('data', (chunk: Buffer) => chunks.push(chunk));
    req.on('end', () => {
      const body = Buffer.concat(chunks).toString('utf8');
      received.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers, body });
      if (req.url === '/api/tags') {
        res.writeHead(options.listStatus ?? 200, { 'content-type': 'application/json' });
        res.end(JSON.stringify(LIST));
        return;
      }
      if (req.url === '/api/show') {
        res.writeHead(200, { 'content-type': 'application/json', 'x-ollama': 'extra' });
        res.end(JSON.stringify({ capabilities: ['completion'] }));
        return;
      }
      res.writeHead(200, { 'content-type': 'text/event-stream' });
      res.write('data: first\n\n');
      open.add(res);
      res.on('close', () => {
        open.delete(res);
        closed.push(body);
      });
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  t.after(
    () =>
      new Promise<void>((resolve) => {
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  );
  return {
    origin: `http://127.0.0.1:${(server.address() as AddressInfo).port}`,
    received,
    open,
    closed,
    /** Ends every open chat. */
    finish() {
      for (const res of open) res.end('data: [DONE]\n\n');
    },
  };
}

async function gateFor(
  t: TestContext,
  origin: string,
  maxChats: () => number = () => 4,
  chatWaitMs?: number,
): Promise<string> {
  const gate = await startOllamaGate({
    ollama: origin,
    maxChats,
    listTtlMs: 0,
    ...(chatWaitMs === undefined ? {} : { chatWaitMs }),
  });
  t.after(() => gate.close());
  assert.equal(gate.baseUrl, `http://127.0.0.1:${gate.port}/v1`);
  return `http://127.0.0.1:${gate.port}`;
}

interface Answer {
  readonly status: number;
  readonly headers: IncomingMessage['headers'];
  readonly body: string;
}

function send(
  url: string,
  init: { method?: string; body?: string | Buffer; headers?: Record<string, string> } = {},
): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const req = request(url, { method: init.method ?? 'GET', headers: init.headers }, (res) => {
      const chunks: Buffer[] = [];
      res.on('data', (chunk: Buffer) => chunks.push(chunk));
      res.on('end', () =>
        resolve({
          status: res.statusCode ?? 0,
          headers: res.headers,
          body: Buffer.concat(chunks).toString('utf8'),
        }),
      );
    });
    req.on('error', reject);
    req.end(init.body);
  });
}

/** Opens a chat and resolves with its first streamed bytes; `abort` closes it. */
function openChat(gate: string, model = TAG) {
  const controller = new AbortController();
  let status = 0;
  const first = new Promise<string>((resolve, reject) => {
    const req = request(
      `${gate}/v1/chat/completions`,
      { method: 'POST', signal: controller.signal },
      (res) => {
        status = res.statusCode ?? 0;
        res.once('data', (chunk: Buffer) => resolve(chunk.toString('utf8')));
      },
    );
    req.on('error', reject);
    req.end(JSON.stringify({ model, messages: [], stream: true }));
  });
  first.catch(() => undefined);
  return { first, abort: () => controller.abort(), status: () => status };
}

/** Whether `promise` settles within `ms`. */
async function settlesWithin(promise: Promise<unknown>, ms: number): Promise<boolean> {
  let settled = false;
  void promise.then(
    () => {
      settled = true;
    },
    () => {
      settled = true;
    },
  );
  await delay(ms);
  return settled;
}

describe('the model list it passes on', () => {
  test('names a model with a cloud tag as running elsewhere', () => {
    for (const name of ['gemma4:cloud', 'gpt-oss:120b-cloud', 'x:CLOUD']) {
      assert.equal(isCloudName(name), true, name);
    }
    for (const name of ['qwen3:4b', 'cloud', 'cloudy:1b', 'cloud-model:latest']) {
      assert.equal(isCloudName(name), false, name);
    }
  });

  test('keeps only models that run on this Mac', () => {
    assert.deepEqual(
      localModels(LIST).map((entry) => entry.model),
      [TAG],
    );
    assert.deepEqual(localModels(null), []);
    assert.deepEqual(localModels({ models: 'none' }), []);
    assert.deepEqual(localModels({ models: [{ model: 'x:1', remote_host: '' }] }), [
      { model: 'x:1', remote_host: '' },
    ]);
  });
});

describe('the bodies it passes on', () => {
  test('finds keys anywhere that Ollama would read as one', () => {
    assert.equal(hasFoldedDuplicate({ model: 'a', Model: 'b' }), true);
    assert.equal(hasFoldedDuplicate({ model: 'a', MODEL: 'b' }), true);
    assert.equal(hasFoldedDuplicate({ messages: [{ role: 'user', ROLE: 'system' }] }), true);
    assert.equal(hasFoldedDuplicate({ key: 1, '\u212Aey': 2 }), true, 'the Kelvin sign');
    assert.equal(hasFoldedDuplicate({ seed: 1, '\u017Feed': 2 }), true, 'the long s');
    assert.equal(hasFoldedDuplicate({ model: 'a', messages: [{ role: 'user' }], m: 1 }), false);
    assert.equal(hasFoldedDuplicate([1, 'a', null]), false);
  });

  test('reads how deep a body nests before parsing it', () => {
    const nest = (depth: number) => Buffer.from(`${'['.repeat(depth)}${']'.repeat(depth)}`);
    assert.equal(nestsDeeper(nest(MAX_DEPTH), MAX_DEPTH), false);
    assert.equal(nestsDeeper(nest(MAX_DEPTH + 1), MAX_DEPTH), true);
    assert.equal(nestsDeeper(Buffer.from('{"a":"[[[[\\"[[[["}'), 2), false, 'brackets in a string');
    assert.equal(nestsDeeper(Buffer.from('{"a":{"b":[1]}}'), 2), true);
  });
});

describe("Ollama's gate", () => {
  test('is only for an Ollama on 127.0.0.1 over plain HTTP', async () => {
    for (const address of [
      'http://localhost:11434',
      'http://10.0.0.2:11434',
      'https://127.0.0.1:11434',
    ]) {
      await assert.rejects(
        startOllamaGate({ ollama: address, maxChats: () => 1 }),
        /Ollama must be at http:\/\/127\.0\.0\.1/,
        address,
      );
    }
  });

  test('answers the model list with only the models that run on this Mac', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const answer = await send(`${gate}/api/tags`);
    assert.equal(answer.status, 200);
    assert.deepEqual(JSON.parse(answer.body), { models: [LIST.models[0]] });
  });

  test('refuses every other request before it reaches Ollama', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const body = JSON.stringify({ model: TAG, name: TAG });
    for (const [method, path] of [
      ['POST', '/api/pull'],
      ['POST', '/api/push'],
      ['POST', '/api/create'],
      ['DELETE', '/api/delete'],
      ['POST', '/api/copy'],
      ['POST', '/api/generate'],
      ['POST', '/api/chat'],
      ['POST', '/api/embed'],
      ['GET', '/api/ps'],
      ['GET', '/api/version'],
      ['GET', '/v1/models'],
      ['POST', '/api/tags'],
      ['GET', '/api/show'],
      ['GET', '/api/tags?x=1'],
      ['POST', '/api/show?x=1'],
      ['POST', '/v1/chat/completions/'],
      ['POST', '//api/show'],
    ] as const) {
      const answer = await send(`${gate}${path}`, { method, body });
      assert.equal(answer.status, 404, `${method} ${path}`);
    }
    assert.deepEqual(ollama.received, []);
  });

  test('refuses a body that is too large, not JSON, or names a model that does not run here', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const show = `${gate}/api/show`;
    const large = Buffer.alloc(MAX_BODY_BYTES + 1, 0x20);
    assert.equal((await send(show, { method: 'POST', body: large })).status, 413);
    assert.equal((await send(show, { method: 'POST', body: '{"model":' })).status, 400);
    for (const body of [
      {},
      { model: 7 },
      { model: 'gemma4:cloud' },
      { model: 'big:70b' },
      { model: 'other:7b' },
      { model: 'stand-in' },
      { model: '' },
    ]) {
      const answer = await send(show, { method: 'POST', body: JSON.stringify(body) });
      assert.equal(answer.status, 403, JSON.stringify(body));
      const chat = await send(`${gate}/v1/chat/completions`, {
        method: 'POST',
        body: JSON.stringify(body),
      });
      assert.equal(chat.status, 403, JSON.stringify(body));
    }
    assert.deepEqual(
      ollama.received.filter((item) => item.url !== '/api/tags'),
      [],
    );
  });

  test('passes model details on as parsed, with nothing of the request but its JSON', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const answer = await send(`${gate}/api/show`, {
      method: 'POST',
      body: `  {"model": "${TAG}"}  `,
      headers: { authorization: 'Bearer secret', 'x-forwarded-host': 'example.com' },
    });
    assert.equal(answer.status, 200);
    assert.deepEqual(JSON.parse(answer.body), { capabilities: ['completion'] });
    assert.equal(answer.headers['x-ollama'], undefined);
    const shown = ollama.received.find((item) => item.url === '/api/show');
    assert.ok(shown !== undefined);
    assert.equal(shown.method, 'POST');
    assert.equal(shown.body, JSON.stringify({ model: TAG }));
    assert.equal(shown.headers.authorization, undefined);
    assert.equal(shown.headers['x-forwarded-host'], undefined);
    assert.equal(shown.headers['content-type'], 'application/json');
  });

  test('streams a chat back, and closing it closes the reply from Ollama', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const chat = openChat(gate);
    assert.equal(await chat.first, 'data: first\n\n');
    assert.equal(ollama.open.size, 1);
    chat.abort();
    for (let i = 0; i < 100 && ollama.closed.length === 0; i += 1) await delay(10);
    assert.equal(ollama.closed.length, 1, "Ollama's reply was not closed");
  });

  test('lets as many chats through at once as it is told, and the next waits for one to end or for the number to rise', async (t) => {
    const ollama = await upstream(t);
    let allowed = 1;
    const gate = await gateFor(t, ollama.origin, () => allowed);
    const first = openChat(gate);
    await first.first;
    const second = openChat(gate);
    let through = false;
    void second.first.then(() => {
      through = true;
    });
    await delay(400);
    assert.equal(through, false, 'a second chat went through past the number allowed');
    assert.equal(ollama.open.size, 1);
    allowed = 2;
    await second.first;
    const third = openChat(gate);
    let thirdThrough = false;
    void third.first.then(() => {
      thirdThrough = true;
    });
    await delay(400);
    assert.equal(thirdThrough, false);
    ollama.finish();
    await third.first;
    third.abort();
  });

  test('lets one chat through when told none', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin, () => 0);
    const chat = openChat(gate);
    assert.equal(await chat.first, 'data: first\n\n');
    chat.abort();
  });

  test('answers 502 when Ollama cannot give its model list', async (t) => {
    const ollama = await upstream(t, { listStatus: 500 });
    const gate = await gateFor(t, ollama.origin);
    assert.equal((await send(`${gate}/api/tags`)).status, 502);
    const answer = await send(`${gate}/api/show`, {
      method: 'POST',
      body: JSON.stringify({ model: TAG }),
    });
    assert.equal(answer.status, 502);
    assert.equal(ollama.received.filter((item) => item.url !== '/api/tags').length, 0);
  });

  test('refuses a body with keys that differ only in case, so another model never rides along', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    for (const [path, body] of [
      ['/v1/chat/completions', `{"model":"${TAG}","Model":"gemma4:cloud","messages":[]}`],
      ['/v1/chat/completions', `{"model":"${TAG}","MODEL":"big:70b","messages":[]}`],
      ['/api/show', `{"model":"${TAG}","Model":"","name":"gemma4:cloud"}`],
      ['/v1/chat/completions', `{"model":"${TAG}","messages":[{"role":"user","Role":"x"}]}`],
    ] as const) {
      const answer = await send(`${gate}${path}`, { method: 'POST', body });
      assert.equal(answer.status, 400, body);
    }
    assert.deepEqual(
      ollama.received.filter((item) => item.url !== '/api/tags'),
      [],
    );
  });

  test('passes on only the fields OpenCode sends', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const shown = await send(`${gate}/api/show`, {
      method: 'POST',
      body: JSON.stringify({ model: TAG, name: 'gemma4:cloud', verbose: true, options: {} }),
    });
    assert.equal(shown.status, 200);
    // Read up to the first streamed bytes, then closed.
    await new Promise<void>((resolve, reject) => {
      const req = request(`${gate}/v1/chat/completions`, { method: 'POST' }, (res) => {
        res.once('data', () => {
          req.destroy();
          resolve();
        });
      });
      req.on('error', reject);
      req.end(
        JSON.stringify({
          model: TAG,
          messages: [{ role: 'user', content: 'hi' }],
          stream: true,
          temperature: 0.2,
          keep_alive: -1,
          options: { num_ctx: 1 },
          think: true,
          _debug_render_only: true,
        }),
      );
    });
    const bodies = ollama.received
      .filter((item) => item.url !== '/api/tags')
      .map((item) => [item.url, JSON.parse(item.body)]);
    assert.deepEqual(bodies[0], ['/api/show', { model: TAG }]);
    assert.deepEqual(bodies.at(-1), [
      '/v1/chat/completions',
      { model: TAG, messages: [{ role: 'user', content: 'hi' }], stream: true, temperature: 0.2 },
    ]);
  });

  test('refuses a body that nests too deep before parsing it', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin);
    const deep = `{"model":"${TAG}","messages":${'['.repeat(MAX_DEPTH)}${']'.repeat(MAX_DEPTH)}}`;
    const answer = await send(`${gate}/v1/chat/completions`, { method: 'POST', body: deep });
    assert.equal(answer.status, 400);
  });

  test('forgets a waiting chat whose request closes', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin, () => 1);
    const first = openChat(gate);
    await first.first;
    const leaving = openChat(gate);
    assert.equal(await settlesWithin(leaving.first, 300), false);
    leaving.abort();
    await delay(50);
    first.abort();
    // The slot goes to the next chat, not to the one that left.
    const next = openChat(gate);
    assert.equal(await next.first, 'data: first\n\n');
    next.abort();
  });

  test('refuses a chat when too many wait, and one that waits too long', async (t) => {
    const ollama = await upstream(t);
    const gate = await gateFor(t, ollama.origin, () => 1, 600);
    const first = openChat(gate);
    await first.first;
    const waiting = Array.from({ length: MAX_WAITING_CHATS }, () => openChat(gate));
    await delay(100);
    const over = openChat(gate);
    assert.match(await over.first, /Too many replies are waiting/);
    assert.equal(over.status(), 503);
    // Past the wait, every one waiting is refused too, while the first still holds the model.
    for (const chat of waiting) {
      assert.match(await chat.first, /Too many replies are waiting/);
      assert.equal(chat.status(), 503);
    }
    assert.equal(ollama.open.size, 1);
    first.abort();
  });
});
