import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { connect, type Socket } from 'node:net';
import { describe, test } from 'node:test';
import { setTimeout as sleep } from 'node:timers/promises';
import type { FastifyInstance } from 'fastify';
import { RealtimeClient } from '../client/realtime-client.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { LOOPBACK_LIMITS, type LoopbackLimits, PAGE_LIMITS, VIEWER_LIMITS } from './limits.ts';
import { DEFAULT_REALTIME_OPTIONS, registerRealtime } from './realtime.ts';
import { createHttpServer } from './server.ts';

// The loopback listener's bounds (CARBON A1-8): a local client that opens sockets and says nothing, or
// says half a request, is cut off, and no more than the bound are held at once.

const SHORT: LoopbackLimits = {
  headersMs: 300,
  requestMs: 300,
  idleMs: 400,
  keepAliveMs: 200,
  checkEveryMs: 100,
  connections: 3,
};

async function served(limits: LoopbackLimits, realtime = false) {
  const token = randomBytes(32).toString('hex');
  const app: FastifyInstance = await createHttpServer({ logLevel: 'silent', token, limits });
  const harness = realtime ? createTestControlPlane() : null;
  if (harness !== null)
    registerRealtime(app, harness.controlPlane, {
      ...DEFAULT_REALTIME_OPTIONS,
      heartbeatIntervalMs: 100,
    });
  app.get('/api/health', async () => ({ ok: true }));
  await app.listen({ host: '127.0.0.1', port: 0 });
  const address = app.server.address();
  if (address === null || typeof address === 'string') throw new Error('server has no port');
  return {
    port: address.port,
    token,
    close: async () => {
      await app.close();
      await harness?.controlPlane.close();
    },
  };
}

function open(port: number): Promise<Socket & { closedAt: Promise<number> }> {
  return new Promise((resolve, reject) => {
    const socket = connect({ host: '127.0.0.1', port });
    const closedAt = new Promise<number>((done) => socket.on('close', () => done(Date.now())));
    socket.on('error', () => {});
    // Read what comes, as a client does: a socket left paused never sees the server's end.
    socket.resume();
    socket.once('connect', () => resolve(Object.assign(socket, { closedAt })));
    socket.once('error', reject);
  });
}

const within = <T>(promise: Promise<T>, ms: number) =>
  Promise.race([promise, sleep(ms).then(() => 'still open' as const)]);

describe('the loopback listener’s bounds (CARBON A1-8)', () => {
  test('a socket that opens and sends nothing is closed after the idle bound', async (t) => {
    // Node starts the headers bound at the connection too, so it is set past the idle one here.
    const server = await served({ ...SHORT, headersMs: 2_000, requestMs: 2_000 });
    t.after(server.close);
    const socket = await open(server.port);
    const started = Date.now();
    const closed = await within(socket.closedAt, 3_000);
    assert.notEqual(closed, 'still open');
    assert.ok((closed as number) - started >= SHORT.idleMs - 50, 'not before the bound');
  });

  test('half a request is cut off at the headers bound', async (t) => {
    const server = await served({ ...SHORT, idleMs: 5_000 });
    t.after(server.close);
    const socket = await open(server.port);
    let answer = '';
    socket.on('data', (chunk) => {
      answer += chunk.toString('latin1');
    });
    socket.write('GET /api/health HTTP/1.1\r\nHost: 127.0.0.1\r\n');
    assert.notEqual(await within(socket.closedAt, 3_000), 'still open');
    assert.match(answer, /^HTTP\/1\.1 408/);
  });

  test('no more connections than the bound are held; the next is closed as it opens', async (t) => {
    const server = await served({ ...SHORT, idleMs: 5_000, headersMs: 5_000, requestMs: 5_000 });
    t.after(server.close);
    const held = [];
    for (let index = 0; index < SHORT.connections; index += 1) held.push(await open(server.port));
    const over = await open(server.port);
    assert.notEqual(await within(over.closedAt, 2_000), 'still open', 'one past the bound');
    for (const socket of held) {
      assert.equal(await within(socket.closedAt, 100), 'still open', 'the bound itself is held');
      socket.destroy();
    }
  });

  test('a realtime client that sends nothing of its own stays open past the idle bound (AUDIT3 B1; ws takes an upgraded socket out of it, F1)', async (t) => {
    const server = await served({ ...SHORT, idleMs: 400 }, true);
    t.after(server.close);
    const client = await RealtimeClient.connect(
      `ws://127.0.0.1:${String(server.port)}/realtime`,
      server.token,
    );
    t.after(() => client.close());
    client.hello({ name: 'limits test', version: null, device_label: null });
    await client.waitFor((message) => message.type === 'snapshot');
    // Three idle bounds with nothing sent by the client: an upgraded socket is not under the idle bound.
    assert.equal(await within(client.closed, 1_200), 'still open');
  });

  test('a held WebSocket counts toward the bound: past it the next connection is closed (AUDIT3 F3)', async (t) => {
    const server = await served(
      { ...SHORT, connections: 2, idleMs: 5_000, headersMs: 5_000, requestMs: 5_000 },
      true,
    );
    t.after(server.close);
    const client = await RealtimeClient.connect(
      `ws://127.0.0.1:${String(server.port)}/realtime`,
      server.token,
    );
    t.after(() => client.close());
    const held = await open(server.port);
    const over = await open(server.port);
    assert.notEqual(await within(over.closedAt, 2_000), 'still open', 'one past the bound');
    assert.equal(await within(client.closed, 100), 'still open', 'the WebSocket is held');
    held.destroy();
  });

  test('the heartbeat is well inside every idle bound, so neither moves alone (AUDIT3 B2)', () => {
    for (const limits of [LOOPBACK_LIMITS, PAGE_LIMITS, VIEWER_LIMITS])
      assert.ok(DEFAULT_REALTIME_OPTIONS.heartbeatIntervalMs * 3 <= limits.idleMs);
  });

  test('the page and the viewer each hold 8 at once; the page takes a photo or video in one request (AUDIT3 B3)', () => {
    assert.equal(LOOPBACK_LIMITS.connections, 64);
    assert.equal(PAGE_LIMITS.connections, 8);
    assert.equal(VIEWER_LIMITS.connections, 8);
    assert.equal(
      PAGE_LIMITS.headersMs,
      LOOPBACK_LIMITS.headersMs,
      'its headers are bounded as any',
    );
    assert.ok(PAGE_LIMITS.requestMs >= 120_000);
  });
});
