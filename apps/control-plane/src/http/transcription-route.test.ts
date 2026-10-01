import assert from 'node:assert/strict';
import { connect } from 'node:net';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import {
  compileValidator,
  type PairingOpenedResponse,
  TRANSCRIPTION_MAX_BYTES,
  TranscriptionResponse,
} from '@halcyonic/contracts';
import { writeWav } from '../speech/wav.ts';
import type { EngineResult, SpeechEngine } from '../speech/whisper.ts';
import { pairDevice } from '../testing/device.ts';
import { startTestServer, type TestServerOptions } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';

const validate = compileValidator(TranscriptionResponse);
const SPOKEN = 'Stop changing the database schema and ask me first.';

class ScriptedEngine implements SpeechEngine {
  readonly engine = { name: 'whisper.cpp', version: '1.9.4-test' };
  readonly received: Buffer[] = [];
  text = SPOKEN;

  async transcribe(wav: Buffer): Promise<EngineResult> {
    this.received.push(wav);
    return { kind: 'text', text: this.text };
  }

  async warmUp(): Promise<number> {
    return 0;
  }
}

async function start(t: TestContext, options: TestServerOptions = {}) {
  const engine = new ScriptedEngine();
  const server = await startTestServer({ speech: engine, ...options });
  t.after(() => server.stop());
  return { server, engine };
}

type Server = Awaited<ReturnType<typeof startTestServer>>;

function post(
  server: Server,
  body: Buffer | string,
  headers: Record<string, string>,
  path = '/api/transcriptions',
) {
  return fetch(`${server.baseUrl}${path}`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}`, ...headers },
    body,
  });
}

const second = writeWav(Buffer.alloc(32_000, 1));

/** Sends a request head, and a body only if given, on a raw connection; resolves with the status line. */
function raw(server: Server, head: string, body?: Buffer): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = connect(server.port, '127.0.0.1');
    let received = '';
    socket.on('data', (chunk: Buffer) => {
      received += chunk.toString('latin1');
      if (received.includes('\r\n')) {
        resolve(received.split('\r\n')[0] ?? '');
        socket.destroy();
      }
    });
    socket.on('error', reject);
    socket.write(head);
    if (body !== undefined) socket.write(body);
  });
}

describe('POST /api/transcriptions', () => {
  test('needs the access token; with it, a clip becomes a draft that matches the contract and is not journaled', async (t) => {
    const { server, engine } = await start(t);
    const refused = await fetch(`${server.baseUrl}/api/transcriptions`, {
      method: 'POST',
      headers: { 'content-type': 'audio/wav' },
      body: second,
    });
    assert.equal(refused.status, 401);
    assert.equal(engine.received.length, 0);
    const head = server.controlPlane.journal.head();
    const response = await post(server, second, { 'content-type': 'audio/wav' });
    assert.equal(response.status, 200);
    const body = (await response.json()) as unknown;
    assert.equal(validate(body).ok, true);
    assert.deepEqual(body, {
      outcome: 'heard',
      text: SPOKEN,
      language: 'en',
      engine: { name: 'whisper.cpp', version: '1.9.4-test' },
    });
    assert.deepEqual(engine.received, [second]);
    assert.equal(server.controlPlane.journal.head(), head);
  });

  test('takes audio/wav only, and no other route takes audio', async (t) => {
    const { server, engine } = await start(t);
    for (const type of [
      'application/json',
      'text/plain',
      'audio/mpeg',
      'application/octet-stream',
    ]) {
      const response = await post(server, type === 'application/json' ? '{}' : second, {
        'content-type': type,
      });
      assert.equal(response.status, 415, type);
    }
    const command = await post(server, second, { 'content-type': 'audio/wav' }, '/api/commands');
    assert.equal(command.status, 415);
    assert.equal(engine.received.length, 0);
    const bad = await post(server, Buffer.from('RIFF....WAVEnope'), {
      'content-type': 'audio/wav',
    });
    assert.equal(bad.status, 400);
    assert.equal(((await bad.json()) as { error: { code: string } }).error.code, 'invalid_audio');
  });

  test('the largest clip is taken; a byte more is refused before its body is read', async (t) => {
    const { server, engine } = await start(t);
    const largest = writeWav(Buffer.alloc(TRANSCRIPTION_MAX_BYTES - 44));
    assert.equal(largest.length, TRANSCRIPTION_MAX_BYTES);
    assert.equal((await post(server, largest, { 'content-type': 'audio/wav' })).status, 200);
    const declared = await raw(
      server,
      `POST /api/transcriptions HTTP/1.1\r\nhost: 127.0.0.1:${server.port}\r\nauthorization: Bearer ${server.token}\r\ncontent-type: audio/wav\r\ncontent-length: ${TRANSCRIPTION_MAX_BYTES + 1}\r\n\r\n`,
    );
    assert.match(declared, / 413 /, 'answered from the declared length, with no body sent');
    const chunked = await raw(
      server,
      `POST /api/transcriptions HTTP/1.1\r\nhost: 127.0.0.1:${server.port}\r\nauthorization: Bearer ${server.token}\r\ncontent-type: audio/wav\r\ntransfer-encoding: chunked\r\n\r\n`,
      Buffer.concat([
        Buffer.from(`${(TRANSCRIPTION_MAX_BYTES + 1).toString(16)}\r\n`),
        Buffer.alloc(TRANSCRIPTION_MAX_BYTES + 1),
      ]),
    );
    assert.match(chunked, / 413 /, 'refused once the body passed the limit');
    assert.equal(engine.received.length, 1);
  });

  test('no draft text or audio reaches the log', async (t) => {
    const lines = new PassThrough();
    let logged = '';
    lines.on('data', (chunk: Buffer) => {
      logged += chunk.toString('utf8');
    });
    const { server } = await start(t, { logLevel: 'trace', logStream: lines });
    assert.equal((await post(server, second, { 'content-type': 'audio/wav' })).status, 200);
    await new Promise((resolve) => setImmediate(resolve));
    assert.match(logged, /transcribed a clip/);
    assert.match(logged, /"outcome":"heard"/);
    assert.equal(logged.includes('database schema'), false, 'the draft reached the log');
    assert.equal(logged.includes(second.subarray(44, 64).toString('base64')), false);
  });

  test('without an engine, voice is unavailable', async (t) => {
    const server = await startTestServer();
    t.after(() => server.stop());
    const response = await post(server, second, { 'content-type': 'audio/wav' });
    assert.equal(response.status, 503);
    assert.equal(
      ((await response.json()) as { error: { code: string } }).error.code,
      'transcription_unavailable',
    );
  });

  test('a paired headset sends its clip over the network listener', async (t) => {
    const { server } = await start(t, { network: {} });
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
        path: '/api/transcriptions',
        headers: { 'content-type': 'audio/wav', ...headers },
        body: second,
        pin: network.identity.certificateSha256,
      });
    assert.equal((await send({})).status, 401);
    const answer = await send({ authorization: `Bearer ${paired.credential}` });
    assert.equal(answer.status, 200);
    assert.equal((JSON.parse(answer.body) as { outcome: string }).outcome, 'heard');
  });
});
