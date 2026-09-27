import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { describe, test } from 'node:test';
import type { RequestId } from './protocol.ts';
import { RpcConnection, RpcError, RpcUnanswered } from './rpc.ts';

/** A connection over in-memory streams: `server` writes what the client reads. */
function connect() {
  const server = new PassThrough();
  const client = new PassThrough();
  const written: unknown[] = [];
  client.setEncoding('utf8');
  client.on('data', (chunk: string) => {
    for (const line of chunk.split('\n')) if (line !== '') written.push(JSON.parse(line));
  });
  const notifications: [string, unknown, number | null][] = [];
  const requests: [RequestId, string][] = [];
  const rpc = new RpcConnection(server, client, {
    onNotification: (method, params, emittedAtMs) => {
      if (method === 'boom') throw new Error('a handler failed');
      notifications.push([method, params, emittedAtMs]);
    },
    onRequest: (id, method, _params, connection) => {
      requests.push([id, method]);
      connection.respondError(id, -32601, 'no');
    },
  });
  return { server, rpc, written, notifications, requests };
}

describe('the Codex JSON-RPC connection', () => {
  test('reads messages split across chunks, in order, and skips lines that are not messages', async () => {
    const { server, notifications, requests, written } = connect();
    server.write('{"method":"a","params":{"x":1},"emitted');
    server.write('AtMs":17}\nnot json\n[1]\n{"method":"boom"}\n');
    server.write('{"id":0,"method":"item/tool/requestUserInput","params":{}}\n{"method":"b"}\n');
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(notifications, [
      ['a', { x: 1 }, 17],
      ['b', undefined, null],
    ]);
    assert.deepEqual(requests, [[0, 'item/tool/requestUserInput']]);
    assert.deepEqual(written, [{ id: 0, error: { code: -32601, message: 'no' } }]);
  });

  test('answers requests with results and errors, and never sends a jsonrpc member', async () => {
    const { server, rpc, written } = connect();
    const first = rpc.request('turn/start', { threadId: 't' }, 1000);
    const second = rpc.request('turn/steer', {}, 1000);
    server.write('{"id":2,"error":{"code":-32600,"message":"no active turn to steer"}}\n');
    server.write('{"id":1,"result":{"turn":{"id":"u"}}}\n');
    assert.deepEqual(await first, { turn: { id: 'u' } });
    await assert.rejects(
      second,
      (error: unknown) =>
        error instanceof RpcError &&
        error.code === -32600 &&
        error.message === 'no active turn to steer',
    );
    assert.deepEqual(written, [
      { id: 1, method: 'turn/start', params: { threadId: 't' } },
      { id: 2, method: 'turn/steer', params: {} },
    ]);
  });

  test('a request left unanswered times out, or fails when the output ends; after end nothing is sent', async () => {
    const { server, rpc, written } = connect();
    await assert.rejects(
      rpc.request('turn/interrupt', {}, 20),
      (error: unknown) => error instanceof RpcUnanswered && error.reason === 'timeout',
    );
    server.write('{"id":1,"result":{}}\n');
    const pending = rpc.request('thread/start', {}, 1000);
    server.end();
    await assert.rejects(
      pending,
      (error: unknown) => error instanceof RpcUnanswered && error.reason === 'closed',
    );
    await rpc.ended;
    assert.equal(rpc.open, false);
    await assert.rejects(
      rpc.request('turn/start', {}, 1000),
      (error: unknown) => error instanceof RpcUnanswered && error.reason === 'not_sent',
    );
    assert.equal(written.length, 2);
  });
});
