import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import {
  type CompanionReply,
  CompanionReplyResponse,
  CompanionStatus,
  compileValidator,
  type PairingOpenedResponse,
} from '@halcyonic/contracts';
import { STATUS_MS } from '../companion/companion.ts';
import { modelReply } from '../companion/prompt.ts';
import { pairDevice } from '../testing/device.ts';
import { startFakeOllama } from '../testing/fake-ollama.ts';
import { startTestServer, type TestServerOptions } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';

const validateStatus = compileValidator(CompanionStatus);
const validateReply = compileValidator(CompanionReplyResponse);

const IDEA = 'a tracker for the zanthoxylum running club';
const ASK: CompanionReply = {
  next: 'ask',
  line: 'A quokka-shaped page could hold the times.',
  view: 'unclear',
  question: { text: 'Who enters the times?', choices: ['Each runner', 'One organiser'] },
};
const BODY = { start: 'idea', want: 'next', messages: [{ from: 'person', text: IDEA }] };

async function start(t: TestContext, options: TestServerOptions = {}) {
  const ollama = await startFakeOllama();
  t.after(() => ollama.stop());
  const server = await startTestServer({
    companion: {
      config: { model: 'local-model:tag', ollama: ollama.url },
      bounds: { firstTokenMs: 2_000, totalMs: 4_000 },
    },
    ...options,
  });
  t.after(() => server.stop());
  return { server, ollama };
}

type Server = Awaited<ReturnType<typeof startTestServer>>;

function post(server: Server, body: unknown, signal?: AbortSignal) {
  return fetch(`${server.baseUrl}/api/companion/replies`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
    body: JSON.stringify(body),
    ...(signal === undefined ? {} : { signal }),
  });
}

describe('GET /api/companion', () => {
  test('needs the access token, and says whether the companion can be asked', async (t) => {
    const { server, ollama } = await start(t);
    assert.equal((await fetch(`${server.baseUrl}/api/companion`)).status, 401);
    const read = async () => {
      const response = await fetch(`${server.baseUrl}/api/companion`, {
        headers: { authorization: `Bearer ${server.token}` },
      });
      assert.equal(response.status, 200);
      const body = (await response.json()) as unknown;
      assert.equal(validateStatus(body).ok, true);
      return body;
    };
    assert.deepEqual(await read(), {
      availability: 'available',
      companion: { name: 'local-model:tag', served: 'this_mac' },
      max_questions: 4,
    });
    ollama.setModels([]);
    // Each read stands for a moment; the next comes after it.
    await server.time.advance(STATUS_MS);
    assert.deepEqual(await read(), {
      availability: 'unavailable',
      reason: {
        code: 'companion_model_missing',
        message: "The companion's model is not on this computer.",
      },
    });
    assert.equal(ollama.requests.length, 0);
  });

  test('without a model named, the companion is not set up', async (t) => {
    const server = await startTestServer();
    t.after(() => server.stop());
    const status = (await (
      await fetch(`${server.baseUrl}/api/companion`, {
        headers: { authorization: `Bearer ${server.token}` },
      })
    ).json()) as { availability: string; reason: { code: string } };
    assert.equal(status.availability, 'unavailable');
    assert.equal(status.reason.code, 'companion_not_set_up');
    const response = await post(server, BODY);
    assert.equal(response.status, 503);
  });
});

describe('POST /api/companion/replies', () => {
  test('answers with a reported reply in the contract, and journals nothing', async (t) => {
    const { server, ollama } = await start(t);
    ollama.answer({ content: JSON.stringify(modelReply(ASK)) });
    const head = server.controlPlane.journal.head();
    const response = await post(server, BODY);
    assert.equal(response.status, 200);
    const body = (await response.json()) as unknown;
    assert.equal(validateReply(body).ok, true);
    assert.deepEqual(body, {
      reply: ASK,
      provenance: 'reported',
      companion: { name: 'local-model:tag', served: 'this_mac' },
    });
    assert.equal(server.controlPlane.journal.head(), head);
  });

  test('refuses a request outside the contract with its issues', async (t) => {
    const { server, ollama } = await start(t);
    const response = await post(server, { ...BODY, folder: '/Users/someone/Projects' });
    assert.equal(response.status, 400);
    const error = (await response.json()) as { error: { code: string; issues: unknown[] } };
    assert.equal(error.error.code, 'invalid_companion_request');
    assert.ok(error.error.issues.length > 0);
    assert.equal(ollama.requests.length, 0);
    const huge = await post(server, { ...BODY, padding: 'x'.repeat(300 * 1024) });
    assert.equal(huge.status, 413);
  });

  test("neither the person's words nor the reply reach the log", async (t) => {
    const lines = new PassThrough();
    let logged = '';
    lines.on('data', (chunk: Buffer) => {
      logged += chunk.toString('utf8');
    });
    const { server, ollama } = await start(t, { logLevel: 'trace', logStream: lines });
    ollama.answer({ content: JSON.stringify(modelReply(ASK)) });
    assert.equal((await post(server, BODY)).status, 200);
    ollama.answer(
      { content: 'not a reply about zanthoxylum' },
      { content: 'still quokka nothing' },
    );
    assert.equal((await post(server, BODY)).status, 502);
    await new Promise((resolve) => setImmediate(resolve));
    assert.match(logged, /companion replied/);
    assert.match(logged, /"code":"companion_unreadable"/);
    for (const secret of ['zanthoxylum', 'quokka', 'organiser'])
      assert.equal(logged.includes(secret), false, `${secret} reached the log`);
  });

  test('a headset that goes away ends the turn, which closes the request to the model', async (t) => {
    const { server, ollama } = await start(t);
    ollama.answer({ content: JSON.stringify(modelReply(ASK)), firstLineAfterMs: 1_500 });
    const gone = new AbortController();
    const sent = post(server, BODY, gone.signal).catch((error: unknown) => error);
    await waitFor(() => ollama.requests.length === 1);
    gone.abort();
    await sent;
    await waitFor(() => ollama.closedEarly === 1);
    // The companion is free again at once.
    ollama.answer({ content: JSON.stringify(modelReply(ASK)) });
    assert.equal((await post(server, BODY)).status, 200);
  });

  test('a paired headset asks over the network listener, under the same bounds', async (t) => {
    const { server, ollama } = await start(t, { network: {} });
    const network = server.network;
    assert.ok(network);
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as PairingOpenedResponse;
    const paired = await pairDevice(network.target, code);
    const send = (headers: Record<string, string>) =>
      tlsRequest(network.target, {
        method: 'POST',
        path: '/api/companion/replies',
        headers: { 'content-type': 'application/json', ...headers },
        body: Buffer.from(JSON.stringify(BODY)),
        pin: network.identity.certificateSha256,
      });
    assert.equal((await send({})).status, 401);
    ollama.answer({ content: JSON.stringify(modelReply(ASK)), firstLineAfterMs: 300 });
    const device = send({ authorization: `Bearer ${paired.credential}` });
    await waitFor(() => ollama.requests.length === 1);
    const local = await post(server, BODY);
    assert.equal(local.status, 503);
    assert.equal(
      ((await local.json()) as { error: { code: string } }).error.code,
      'companion_busy_on_mac',
    );
    const answer = await device;
    assert.equal(answer.status, 200);
    assert.equal((JSON.parse(answer.body) as { provenance: string }).provenance, 'reported');
  });
});

async function waitFor(condition: () => boolean, ms = 3_000): Promise<void> {
  const until = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > until) assert.fail('the condition never held');
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}
