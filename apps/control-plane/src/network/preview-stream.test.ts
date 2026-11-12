import assert from 'node:assert/strict';
import { createServer, type Server, type Socket } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { pairDevice } from '../testing/device.ts';
import { startTestServer } from '../testing/harness.ts';
import { TlsWebSocket, WebSocketRefused } from '../testing/tls-client.ts';
import type { PreviewTarget } from './preview-relay.ts';

const PROJECT = '01920000-0000-7000-8000-000000000001';
const STREAM = `/api/projects/${PROJECT}/preview-stream`;

/** A stand-in for a project's own server: it answers every line it is sent with the line, upper case. */
async function echoServer(t: TestContext): Promise<{ port: number; dialled: () => number }> {
  let dialled = 0;
  const sockets = new Set<Socket>();
  const server: Server = createServer((socket) => {
    dialled += 1;
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.on('data', (chunk) => socket.write(chunk.toString('latin1').toUpperCase()));
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => {
    for (const socket of sockets) socket.destroy();
    return new Promise((resolve) => server.close(resolve));
  });
  return { port: (server.address() as { port: number }).port, dialled: () => dialled };
}

async function pairedServer(
  t: TestContext,
  target: () => PreviewTarget,
  previewStream = { perDevice: 6, idleMs: 60_000, checkMs: 50 },
) {
  const server = await startTestServer({
    network: { previewTarget: () => target(), previewStream },
  });
  t.after(() => server.stop());
  assert.ok(server.network);
  const opened = await fetch(`${server.baseUrl}/api/pairing`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}` },
  });
  const { code } = (await opened.json()) as { code: string };
  const paired = await pairDevice(server.network.target, code);
  const network = server.network;
  const open = (credential: string | null = paired.credential) =>
    TlsWebSocket.connect(network.target, STREAM, {
      pin: network.identity.certificateSha256,
      headers: credential === null ? {} : { authorization: `Bearer ${credential}` },
    });
  return { server, paired, open };
}

const refusal = async (opening: Promise<TlsWebSocket>) => {
  try {
    await opening;
  } catch (error) {
    if (error instanceof WebSocketRefused) {
      return {
        status: error.status,
        code: (JSON.parse(error.body) as { error: { code: string } }).error.code,
      };
    }
    throw error;
  }
  return null;
};

describe('the preview stream on the network listener (ADR 0052, amended 2026-11-12)', () => {
  test('carries a browser connection whole to the preview’s own server, any bytes either way', async (t) => {
    const preview = await echoServer(t);
    const { open } = await pairedServer(t, () => ({ ok: true, port: preview.port }));
    const stream = await open();
    stream.sendBytes(Buffer.from('post /api/score http/1.1\r\n'));
    assert.match(await stream.waitForBytes('POST /API/SCORE'), /POST \/API\/SCORE HTTP\/1\.1/);
    stream.sendBytes(Buffer.from('a websocket frame, later'));
    await stream.waitForBytes('A WEBSOCKET FRAME, LATER');
    assert.equal(preview.dialled(), 1, 'one connection to the preview per stream');
    await stream.close();
  });

  test('is refused before it opens: no credential, the gate says no, a bad id, past the device’s cap', async (t) => {
    const preview = await echoServer(t);
    let allowed: PreviewTarget = { ok: true, port: preview.port };
    const { open, server } = await pairedServer(t, () => allowed, {
      perDevice: 2,
      idleMs: 60_000,
      checkMs: 50,
    });
    assert.equal((await refusal(open(null)))?.status, 401, 'a paired device only');
    allowed = { ok: false, code: 'preview_not_confined', message: 'not confined' };
    assert.deepEqual(await refusal(open()), { status: 409, code: 'preview_not_confined' });
    allowed = { ok: true, port: preview.port };
    assert.ok(server.network);
    const badId = await refusal(
      TlsWebSocket.connect(server.network.target, '/api/projects/not-an-id/preview-stream', {
        pin: server.network.identity.certificateSha256,
        headers: { authorization: `Bearer ${await pairedCredential(server)}` },
      }),
    );
    assert.equal(badId?.status, 400);
    const first = await open();
    const second = await open();
    assert.deepEqual(
      await refusal(open()),
      { status: 429, code: 'too_many_streams' },
      'the third, past a cap of two',
    );
    await first.close();
    await new Promise((resolve) => setTimeout(resolve, 100));
    const third = await open();
    await second.close();
    await third.close();
    assert.equal(preview.dialled(), 3, 'nothing dialled for a refused stream');
  });

  test('closes at once when the gate no longer admits it, its device is forgotten, it idles, or pairing goes off', async (t) => {
    const preview = await echoServer(t);
    let allowed: PreviewTarget = { ok: true, port: preview.port };
    const { open, server, paired } = await pairedServer(t, () => allowed, {
      perDevice: 6,
      idleMs: 400,
      checkMs: 50,
    });

    const stale = await open();
    allowed = { ok: false, code: 'preview_not_latest', message: 'not the latest' };
    assert.deepEqual(
      await stale.closed,
      { code: 4409, reason: 'preview_not_latest' },
      'the check went stale',
    );
    allowed = { ok: true, port: preview.port };

    const moved = await open();
    allowed = { ok: true, port: preview.port === 65535 ? 1 : preview.port + 1 };
    assert.deepEqual(
      await moved.closed,
      { code: 4409, reason: 'preview_not_latest' },
      'the preview moved',
    );
    allowed = { ok: true, port: preview.port };

    const quiet = await open();
    const started = Date.now();
    assert.deepEqual(await quiet.closed, { code: 1000, reason: 'idle' });
    assert.ok(Date.now() - started >= 350, 'not before the idle time');

    const forgotten = await open();
    server.devices.revoke(paired.deviceId, { kind: 'local' });
    assert.deepEqual(
      await forgotten.closed,
      { code: 4409, reason: 'not_paired' },
      'the device was forgotten',
    );
  });

  test('ends every stream it holds when the listener closes, as pairing turned off closes it', async (t) => {
    const preview = await echoServer(t);
    const { open, server } = await pairedServer(t, () => ({ ok: true, port: preview.port }));
    const stream = await open();
    assert.ok(server.network);
    await server.network.server.close();
    await stream.disconnected;
  });

  test('dials only 127.0.0.1 at the gate’s port, whatever the request names', async (t) => {
    const preview = await echoServer(t);
    const { open } = await pairedServer(t, () => ({ ok: true, port: preview.port }));
    // A request that names another host and port in its own head is carried to the gate's port all the same.
    const stream = await open();
    stream.sendBytes(Buffer.from('GET / HTTP/1.1\r\nHost: 10.0.0.9:22\r\n\r\n'));
    await stream.waitForBytes('HOST: 10.0.0.9:22');
    assert.equal(preview.dialled(), 1);
    await stream.close();
  });
});

/** The first paired device's credential is not kept by the harness: pair a second one for the bad-id case. */
async function pairedCredential(
  server: Awaited<ReturnType<typeof startTestServer>>,
): Promise<string> {
  assert.ok(server.network);
  const opened = await fetch(`${server.baseUrl}/api/pairing`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}` },
  });
  const { code } = (await opened.json()) as { code: string };
  return (await pairDevice(server.network.target, code)).credential;
}
