/**
 * The network listener and pairing (ADR 0017) over real TLS, WebSocket and HTTP: what a paired
 * device can do, and what a hostile device, a relay in the middle, a browser or a revoked device
 * cannot. Each test starts its own control plane.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import { createServer, type TLSSocket, connect as tlsConnect } from 'node:tls';
import type {
  EventEnvelope,
  EventsResponse,
  PairingOpenedResponse,
  PairingRefusalReason,
  PairingStatus,
} from '@halcyonic/contracts';
import { RealtimeClient } from '../client/realtime-client.ts';
import { PairingRefused, pairDevice, replay } from '../testing/device.ts';
import { startTestServer, TEST_CLIENT, type TestServerOptions } from '../testing/harness.ts';
import {
  openTls,
  stageRequest,
  TlsWebSocket,
  tlsRequest,
  WebSocketRefused,
} from '../testing/tls-client.ts';
import { createNetworkIdentity } from './certificate.ts';
import { MAX_REALTIME_CONNECTIONS_PER_DEVICE } from './devices.ts';
import { credentialSha256 } from './pairing-protocol.ts';
import { MAX_NETWORK_CONNECTIONS } from './server.ts';

type Server = Awaited<ReturnType<typeof startTestServer>>;

async function start(t: TestContext, options: TestServerOptions = {}): Promise<Server> {
  const server = await startTestServer({ network: {}, ...options });
  t.after(() => server.stop());
  return server;
}

function network(server: Server) {
  assert.ok(server.network, 'the test server serves the network listener');
  return server.network;
}

async function loopback<T>(server: Server, method: string, path: string) {
  const response = await fetch(`${server.baseUrl}${path}`, {
    method,
    headers: { authorization: `Bearer ${server.token}` },
  });
  const text = await response.text();
  return { status: response.status, body: (text === '' ? null : JSON.parse(text)) as T };
}

async function openWindow(server: Server): Promise<string> {
  const opened = await loopback<PairingOpenedResponse>(server, 'POST', '/api/pairing');
  assert.equal(opened.status, 201);
  return opened.body.code;
}

function wrongCode(code: string): string {
  return ((Number(code) + 1) % 100_000_000).toString().padStart(8, '0');
}

/** A REST request to the network listener, pinned to its certificate. */
function onNetwork(
  server: Server,
  method: string,
  path: string,
  headers: Record<string, string> = {},
  body?: string,
) {
  const { target, identity } = network(server);
  return tlsRequest(target, {
    method,
    path,
    headers,
    ...(body === undefined ? {} : { body }),
    pin: identity.certificateSha256,
  });
}

const bearer = (credential: string) => ({ authorization: `Bearer ${credential}` });

function journal(server: Server): EventEnvelope[] {
  return [...server.journal.readAll()].map((stored) => stored.event);
}

/** What the pairing window turned away or cut short from this machine, by reason. */
async function refusals(server: Server): Promise<Partial<Record<PairingRefusalReason, number>>> {
  const status = await loopback<PairingStatus>(server, 'GET', '/api/pairing');
  return Object.fromEntries(
    status.body.refusals
      .filter((tally) => tally.address === '127.0.0.1')
      .map((tally) => [tally.reason, tally.count]),
  );
}

/** Resolves when `done` does, or fails the test after `ms`. */
function within<T>(ms: number, done: Promise<T>, what: string): Promise<T> {
  let timer: NodeJS.Timeout | undefined;
  const late = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${what} took longer than ${ms} ms`)), ms);
  });
  return Promise.race([done, late]).finally(() => clearTimeout(timer));
}

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

describe('pairing a device over the network', () => {
  test('a device that knows the code pairs, pins the certificate and gets a working credential', async (t) => {
    const server = await start(t);
    const code = await openWindow(server);
    const paired = await pairDevice(network(server).target, code, { label: 'Quest 3' });
    assert.equal(paired.certificateSha256, network(server).identity.certificateSha256);
    assert.match(paired.credential, /^hlcd_[A-Za-z0-9_-]{43}$/);

    const status = await loopback<PairingStatus>(server, 'GET', '/api/pairing');
    assert.equal(status.body.state, 'paired');
    assert.equal(status.body.device?.device_id, paired.deviceId);
    assert.equal(status.body.device?.label, 'Quest 3');

    const snapshot = await onNetwork(server, 'GET', '/api/snapshot', bearer(paired.credential));
    assert.equal(snapshot.status, 200);

    const events = journal(server);
    const pairedEvent = events.find((event) => event.event_type === 'device.paired');
    assert.ok(pairedEvent?.event_type === 'device.paired');
    assert.equal(pairedEvent.payload.device_id, paired.deviceId);
    assert.equal(pairedEvent.payload.credential_sha256, credentialSha256(paired.credential));
    assert.equal(pairedEvent.payload.certificate_sha256, paired.certificateSha256);
    const recorded = JSON.stringify(events);
    assert.equal(recorded.includes(paired.credential), false, 'the credential is not journaled');
    assert.equal(recorded.includes(code), false, 'the code is not journaled');

    // The window closed with the first device, so the code pairs nothing else.
    await assert.rejects(
      pairDevice(network(server).target, code),
      (error) => error instanceof WebSocketRefused && error.status === 403,
    );
  });

  test('a wrong code is refused, and three failures close the window for the right one too', async (t) => {
    const server = await start(t);
    const code = await openWindow(server);
    for (const left of [2, 1, 0]) {
      await assert.rejects(
        pairDevice(network(server).target, wrongCode(code)),
        (error) =>
          error instanceof PairingRefused &&
          error.code === 'wrong_code' &&
          error.attemptsLeft === left,
      );
    }
    const status = await loopback<PairingStatus>(server, 'GET', '/api/pairing');
    assert.equal(status.body.state, 'locked');
    assert.equal(status.body.failed_attempts, 3);
    await assert.rejects(
      pairDevice(network(server).target, code),
      (error) => error instanceof WebSocketRefused && error.status === 403,
    );
    assert.equal(
      journal(server).some((event) => event.event_type === 'device.paired'),
      false,
    );
  });

  test('a relay that terminates TLS in the middle cannot pair, even passing every message on', async (t) => {
    const server = await start(t);
    const code = await openWindow(server);
    const { target } = network(server);
    const relayIdentity = createNetworkIdentity(new Date());
    const relay = createServer(
      { key: relayIdentity.key, cert: relayIdentity.certificate },
      (inbound) => {
        const outbound = tlsConnect({
          host: target.host,
          port: target.port,
          rejectUnauthorized: false,
        });
        inbound.pipe(outbound).pipe(inbound);
        inbound.on('error', () => outbound.destroy());
        outbound.on('error', () => inbound.destroy());
      },
    );
    await new Promise<void>((resolve) => relay.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise((resolve) => relay.close(resolve)));
    const address = relay.address();
    assert.ok(address !== null && typeof address === 'object');

    // The device knows the right code, but sees the relay's certificate and binds that one; the
    // relay passes every message on, and cannot make the proof hold.
    const relayed = { host: '127.0.0.1', port: address.port };
    await assert.rejects(
      pairDevice(relayed, code, { host: `127.0.0.1:${target.port}` }),
      (error) => error instanceof PairingRefused && error.code === 'wrong_code',
    );
    const status = await loopback<PairingStatus>(server, 'GET', '/api/pairing');
    assert.equal(status.body.failed_attempts, 1);
  });

  test('a recorded exchange replayed on a new connection is refused', async (t) => {
    const server = await start(t);
    const first = await pairDevice(network(server).target, await openWindow(server));
    await openWindow(server);
    const answers = await replay(network(server).target, first.sent);
    assert.equal(answers[0]?.type, 'pair_challenge', 'every attempt gets a fresh challenge');
    assert.equal(answers[1]?.type, 'pair_refused');
    assert.deepEqual((answers[1]?.error as { code: string } | undefined)?.code, 'wrong_code');
    assert.equal(journal(server).filter((event) => event.event_type === 'device.paired').length, 1);
  });

  test('outside an open window, pairing is refused before any cryptography', async (t) => {
    const server = await start(t);
    const { target } = network(server);
    const refusedWith = async (status: number) =>
      assert.rejects(
        TlsWebSocket.connect(target, '/pair'),
        (error) => error instanceof WebSocketRefused && error.status === status,
      );
    await refusedWith(403);

    await openWindow(server);
    await server.time.advance(5 * 60_000);
    await refusedWith(403);
    assert.equal(
      (await loopback<PairingStatus>(server, 'GET', '/api/pairing')).body.state,
      'expired',
    );

    await openWindow(server);
    const closed = await loopback<PairingStatus>(server, 'DELETE', '/api/pairing');
    assert.equal(closed.body.state, 'closed');
    await refusedWith(403);
  });

  test('one address gets one exchange at a time and six pairing connections a minute', async (t) => {
    const server = await start(t);
    const code = await openWindow(server);
    const { target } = network(server);
    const holding = await TlsWebSocket.connect(target, '/pair');
    const second = await TlsWebSocket.connect(target, '/pair');
    const busy = await second.message(0);
    assert.equal((busy.error as { code: string }).code, 'busy');
    await second.closed;
    await holding.close();

    for (let attempt = 3; attempt <= 6; attempt += 1) {
      const socket = await TlsWebSocket.connect(target, '/pair');
      await socket.close();
    }
    await assert.rejects(
      TlsWebSocket.connect(target, '/pair'),
      (error) => error instanceof WebSocketRefused && error.status === 429,
    );
    // The last connection's exchange ends when the control plane sees it close.
    await pause(100);
    const counted = await refusals(server);
    assert.equal(counted.busy, 1);
    assert.equal(counted.too_many_requests, 1);
    assert.equal(counted.abandoned, 5, 'the exchanges that closed before a proof');
    await server.time.advance(60_000);
    await pairDevice(target, code);
  });

  test('an exchange that takes too long is ended', async (t) => {
    const server = await start(t, {
      network: {
        limits: {
          windowMs: 60_000,
          maxFailedAttempts: 3,
          attemptTimeoutMs: 100,
          maxConcurrentAttempts: 4,
          attemptsPerAddressPerMinute: 6,
        },
      },
    });
    await openWindow(server);
    const socket = await TlsWebSocket.connect(network(server).target, '/pair');
    const answer = await socket.message(0);
    assert.equal((answer.error as { code: string }).code, 'timeout');
    await socket.closed;
    await pause(50);
    assert.deepEqual(await refusals(server), { timeout: 1 }, 'a timeout is not also abandoned');
  });

  test('a request of another protocol version, or out of order, is refused', async (t) => {
    const server = await start(t);
    await openWindow(server);
    const { target } = network(server);
    const future = await TlsWebSocket.connect(target, '/pair');
    future.sendJson({ type: 'pair_request', protocol: 2, device_label: 'Quest 3' });
    assert.equal(
      ((await future.message(0)).error as { code: string }).code,
      'unsupported_protocol',
    );
    await future.closed;

    const early = await TlsWebSocket.connect(target, '/pair');
    early.sendJson({
      type: 'pair_proof',
      client_public: Buffer.alloc(384, 1).toString('base64'),
      proof: Buffer.alloc(32).toString('base64'),
    });
    assert.equal(((await early.message(0)).error as { code: string }).code, 'invalid_message');
    await early.closed;
    await pause(50);
    assert.deepEqual(await refusals(server), { unsupported_protocol: 1, invalid_message: 1 });
    assert.equal(
      (await loopback<PairingStatus>(server, 'GET', '/api/pairing')).body.failed_attempts,
      0,
      'neither spent an attempt',
    );
  });

  test('every exchange in a window gets the salt drawn when it opened, and a fresh server secret', async (t) => {
    const server = await start(t);
    const challenge = async () => {
      const socket = await TlsWebSocket.connect(network(server).target, '/pair');
      socket.sendJson({ type: 'pair_request', protocol: 1, device_label: 'Quest 3' });
      const message = await socket.message(0);
      await socket.close();
      // The exchange ends when the control plane sees the connection close.
      await pause(100);
      assert.equal(message.type, 'pair_challenge');
      return message as { salt: string; server_public: string };
    };
    await openWindow(server);
    const first = await challenge();
    const second = await challenge();
    assert.equal(second.salt, first.salt, 'the window derived its salt and verifier once');
    assert.notEqual(second.server_public, first.server_public, 'each attempt has its own b');
    await openWindow(server);
    const third = await challenge();
    assert.notEqual(third.salt, first.salt, 'a new window, a new salt');
  });

  test('an A of 0 mod N, which would fix the session key, counts as a failed attempt', async (t) => {
    const server = await start(t);
    await openWindow(server);
    const socket = await TlsWebSocket.connect(network(server).target, '/pair');
    socket.sendJson({ type: 'pair_request', protocol: 1, device_label: 'Quest 3' });
    await socket.message(0);
    socket.sendJson({
      type: 'pair_proof',
      client_public: Buffer.alloc(384).toString('base64'),
      proof: Buffer.alloc(32).toString('base64'),
    });
    const answer = await socket.message(1);
    assert.equal((answer.error as { code: string }).code, 'wrong_code');
    assert.equal(
      (await loopback<PairingStatus>(server, 'GET', '/api/pairing')).body.failed_attempts,
      1,
    );
  });

  test('a label with control characters is refused', async (t) => {
    const server = await start(t);
    const code = await openWindow(server);
    await assert.rejects(
      pairDevice(network(server).target, code, { label: 'Quest\u001b[2J' }),
      (error) => error instanceof PairingRefused && error.code === 'invalid_message',
    );
  });

  test('opening pairing needs the network listener', async (t) => {
    const server = await startTestServer();
    t.after(() => server.stop());
    const refused = await loopback<{ error: { code: string } }>(server, 'POST', '/api/pairing');
    assert.equal(refused.status, 409);
    assert.equal(refused.body.error.code, 'network_off');
  });
});

describe('the network listener', () => {
  test('takes device credentials only, and loopback takes only the access token', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    assert.equal(
      (await onNetwork(server, 'GET', '/api/snapshot', bearer(server.token))).status,
      401,
    );
    assert.equal((await onNetwork(server, 'GET', '/api/snapshot')).status, 401);
    const onLoopback = await fetch(`${server.baseUrl}/api/snapshot`, {
      headers: bearer(paired.credential),
    });
    assert.equal(onLoopback.status, 401);
    assert.equal((await onNetwork(server, 'GET', '/api/health')).status, 200);
  });

  test('refuses browsers and names other than an address or a .local name', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const { port } = network(server).target;
    const withHeaders = (headers: Record<string, string>) =>
      onNetwork(server, 'GET', '/api/snapshot', { ...bearer(paired.credential), ...headers });
    for (const [headers, code] of [
      [{ origin: 'https://evil.example' }, 'origin_not_allowed'],
      [{ 'sec-fetch-site': 'cross-site' }, 'cross_site_request'],
      [{ host: `evil.example:${port}` }, 'host_not_allowed'],
      [{ host: `192.168.1.23:${port + 1}` }, 'host_not_allowed'],
      [{ host: '192.168.1.23' }, 'host_not_allowed'],
    ] as const) {
      const answer = await withHeaders(headers);
      assert.equal(answer.status, 403, JSON.stringify(headers));
      assert.match(answer.body, new RegExp(code));
    }
    for (const host of [`my-mac.local:${port}`, `192.168.1.23:${port}`, `[fe80::1]:${port}`]) {
      assert.equal((await withHeaders({ host })).status, 200, host);
    }
    await assert.rejects(
      TlsWebSocket.connect(network(server).target, '/realtime', {
        headers: { ...bearer(paired.credential), origin: 'https://evil.example' },
      }),
      (error) => error instanceof WebSocketRefused && error.status === 403,
    );
  });

  test('does not serve what manages devices, which stays on loopback', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    for (const [method, path] of [
      ['POST', '/api/pairing'],
      ['GET', '/api/pairing'],
      ['GET', '/api/devices'],
      ['POST', `/api/devices/${paired.deviceId}/revoke`],
    ] as const) {
      assert.equal(
        (await onNetwork(server, method, path, bearer(paired.credential))).status,
        404,
        path,
      );
    }
  });

  test('journals who sent each command: the device, or the local token', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const fromDevice = server.commands.createProject('From the headset');
    const answer = await onNetwork(
      server,
      'POST',
      '/api/commands',
      { ...bearer(paired.credential), 'content-type': 'application/json' },
      JSON.stringify(fromDevice),
    );
    assert.equal(answer.status, 202);
    const fromMac = server.commands.createProject('From the Mac');
    const local = await fetch(`${server.baseUrl}/api/commands`, {
      method: 'POST',
      headers: { ...bearer(server.token), 'content-type': 'application/json' },
      body: JSON.stringify(fromMac),
    });
    assert.equal(local.status, 202);

    const socket = await TlsWebSocket.connect(network(server).target, '/realtime', {
      headers: bearer(paired.credential),
      pin: paired.certificateSha256,
    });
    socket.sendJson({ type: 'hello', protocol: 1, client: TEST_CLIENT, resume: null });
    const overWebSocket = server.commands.createProject('Over the realtime stream');
    socket.sendJson({ type: 'command', command: overWebSocket });
    await socket.waitFor((text) => text.includes('"command_ack"'));
    await socket.close();

    const principals = new Map(
      journal(server).flatMap((event) =>
        event.event_type === 'command.accepted'
          ? [
              [
                event.payload.command.command_id,
                [event.payload.received_via, event.payload.principal],
              ],
            ]
          : [],
      ),
    );
    const device = { kind: 'device', device_id: paired.deviceId };
    assert.deepEqual(principals.get(fromDevice.command_id), ['http', device]);
    assert.deepEqual(principals.get(overWebSocket.command_id), ['websocket', device]);
    assert.deepEqual(principals.get(fromMac.command_id), ['http', { kind: 'local' }]);
  });

  test('revoking a device refuses its credential at once and closes its realtime connection', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const socket = await TlsWebSocket.connect(network(server).target, '/realtime', {
      headers: bearer(paired.credential),
      pin: paired.certificateSha256,
    });
    socket.sendJson({ type: 'hello', protocol: 1, client: TEST_CLIENT, resume: null });
    await socket.waitFor((text) => text.includes('"welcome"'));
    const listed = await loopback<{ connected: string[] }>(server, 'GET', '/api/devices');
    assert.deepEqual(listed.body.connected, [paired.deviceId]);

    const revoked = await loopback<{ revoked_at: string | null }>(
      server,
      'POST',
      `/api/devices/${paired.deviceId}/revoke`,
    );
    assert.equal(revoked.status, 200);
    assert.ok(revoked.body.revoked_at);
    assert.equal((await socket.closed).code, 1008);

    const refused = await onNetwork(server, 'GET', '/api/snapshot', bearer(paired.credential));
    assert.equal(refused.status, 401);
    assert.match(refused.body, /device_revoked/);
    await assert.rejects(
      TlsWebSocket.connect(network(server).target, '/realtime', {
        headers: bearer(paired.credential),
      }),
      (error) => error instanceof WebSocketRefused && error.status === 401,
    );
    const event = journal(server).find((candidate) => candidate.event_type === 'device.revoked');
    assert.ok(event?.event_type === 'device.revoked');
    assert.deepEqual(event.payload.revoked_by, { kind: 'local' });
    const again = await loopback(server, 'POST', `/api/devices/${paired.deviceId}/revoke`);
    assert.equal(again.status, 200, 'revoking again changes nothing');
    assert.equal(
      journal(server).filter((candidate) => candidate.event_type === 'device.revoked').length,
      1,
    );
    const unknown = await loopback(
      server,
      'POST',
      '/api/devices/01920000-0000-7000-8000-000000000000/revoke',
    );
    assert.equal(unknown.status, 404);
  });

  test('a device can revoke itself, as forgetting the control plane does', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const forgotten = await onNetwork(
      server,
      'POST',
      '/api/device/revoke',
      bearer(paired.credential),
    );
    assert.equal(forgotten.status, 204);
    assert.equal(
      (await onNetwork(server, 'GET', '/api/snapshot', bearer(paired.credential))).status,
      401,
    );
    const event = journal(server).find((candidate) => candidate.event_type === 'device.revoked');
    assert.ok(event?.event_type === 'device.revoked');
    assert.deepEqual(event.payload.revoked_by, { kind: 'device', device_id: paired.deviceId });
  });

  test('a certificate other than the pinned one is refused before anything is sent', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const other = createNetworkIdentity(new Date());
    const received: string[] = [];
    const impostor = createServer({ key: other.key, cert: other.certificate }, (socket) => {
      received.push('connection');
      socket.on('data', () => received.push('data'));
    });
    await new Promise<void>((resolve) => impostor.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise((resolve) => impostor.close(resolve)));
    const address = impostor.address();
    assert.ok(address !== null && typeof address === 'object');
    await assert.rejects(
      tlsRequest(
        { host: '127.0.0.1', port: address.port },
        {
          method: 'GET',
          path: '/api/snapshot',
          headers: bearer(paired.credential),
          pin: paired.certificateSha256,
        },
      ),
      /not the pinned one/,
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    assert.equal(received.includes('data'), false, 'the credential never reached the impostor');
  });

  test('repeated failed credentials from one address are refused for a minute', async (t) => {
    const server = await start(t);
    const bad = bearer(`hlcd_${'A'.repeat(43)}`);
    for (let attempt = 0; attempt < 30; attempt += 1) {
      assert.equal((await onNetwork(server, 'GET', '/api/snapshot', bad)).status, 401);
    }
    assert.equal((await onNetwork(server, 'GET', '/api/snapshot', bad)).status, 429);
    await server.time.advance(60_000);
    assert.equal((await onNetwork(server, 'GET', '/api/snapshot', bad)).status, 401);
  });

  test('realtime clients never receive device events, and the gap they leave is harmless', async (t) => {
    const server = await start(t);
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    client.hello(TEST_CLIENT);
    await client.waitFor((message) => message.type === 'snapshot');
    const paired = await pairDevice(network(server).target, await openWindow(server));
    await loopback(server, 'POST', `/api/devices/${paired.deviceId}/revoke`);
    const command = server.commands.createProject('After the devices');
    client.command(command);
    const ack = await client.waitFor((message) => message.type === 'command_ack');
    assert.equal(ack.type, 'command_ack');
    const positions = client.messages.flatMap((message) =>
      message.type === 'event' ? [message.position] : [],
    );
    assert.equal(
      client.messages.some(
        (message) => message.type === 'event' && message.event.event_type.startsWith('device.'),
      ),
      false,
    );
    assert.equal(positions[0], 3, 'positions 1 and 2 are the device events');
    assert.equal(client.invalid.length, 0);
    await client.close();
  });

  test('a paired device reads no device events from the history, whose limit counts only what it returns', async (t) => {
    const server = await start(t);
    const { target } = network(server);
    const first = await pairDevice(target, await openWindow(server), { label: 'Owner headset' });
    const second = await pairDevice(target, await openWindow(server), { label: 'Other device' });
    await loopback(server, 'POST', `/api/devices/${first.deviceId}/revoke`);
    const command = server.commands.createProject('After the devices');
    const submitted = await fetch(`${server.baseUrl}/api/commands`, {
      method: 'POST',
      headers: { ...bearer(server.token), 'content-type': 'application/json' },
      body: JSON.stringify(command),
    });
    assert.equal(submitted.status, 202);

    const read = async (query: string) => {
      const answer = await onNetwork(
        server,
        'GET',
        `/api/events?${query}`,
        bearer(second.credential),
      );
      assert.equal(answer.status, 200);
      return { text: answer.body, body: JSON.parse(answer.body) as EventsResponse };
    };
    const one = await read('after=0&limit=1');
    assert.equal(one.body.events.length, 1);
    assert.equal(one.body.events[0]?.position, 4, 'positions 1 to 3 are the device events');
    assert.equal(one.body.events[0]?.event.event_type, 'command.accepted');
    const all = await read('after=0&limit=200');
    assert.equal(
      all.body.events.some((stored) => stored.event.event_type.startsWith('device.')),
      false,
    );
    for (const hidden of [credentialSha256(first.credential), 'Owner headset', first.deviceId]) {
      assert.equal(all.text.includes(hidden), false, 'nothing about another device');
    }

    const owner = await loopback<EventsResponse>(server, 'GET', '/api/events?after=0&limit=200');
    assert.equal(
      owner.body.events.filter((stored) => stored.event.event_type.startsWith('device.')).length,
      3,
      'the owner on loopback still reads them',
    );
  });

  test('nothing it logs holds the code, the credential or the access token', async (t) => {
    const lines = new PassThrough();
    let logged = '';
    lines.on('data', (chunk: Buffer) => {
      logged += chunk.toString('utf8');
    });
    const server = await start(t, { logLevel: 'trace', logStream: lines });
    const code = await openWindow(server);
    await assert.rejects(pairDevice(network(server).target, wrongCode(code)));
    const paired = await pairDevice(network(server).target, code);
    await onNetwork(server, 'GET', '/api/snapshot', bearer(paired.credential));
    await onNetwork(server, 'POST', '/api/device/revoke', bearer(paired.credential));
    await onNetwork(server, 'GET', '/api/snapshot', bearer(paired.credential));
    await new Promise((resolve) => setImmediate(resolve));
    assert.match(logged, /pairing window opened/);
    assert.match(logged, /device paired/);
    for (const secret of [code, wrongCode(code), paired.credential, server.token]) {
      assert.equal(logged.includes(secret), false, 'a secret reached the log');
    }
    const hashOfCredential = createHash('sha256').update(paired.credential).digest('hex');
    assert.equal(logged.includes(hashOfCredential), false);
  });
});

describe('what revocation stops, and how long the listener waits', () => {
  test('a command whose body arrives after its device is revoked is rejected, journaled and answered as revoked', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const command = server.commands.createProject('Staged before revocation');
    const staged = await stageRequest(network(server).target, {
      method: 'POST',
      path: '/api/commands',
      headers: { ...bearer(paired.credential), 'content-type': 'application/json' },
      body: JSON.stringify(command),
      pin: paired.certificateSha256,
    });
    // The head arrives, and is authenticated, while the device is still paired.
    await pause(300);
    const revoked = await loopback(server, 'POST', `/api/devices/${paired.deviceId}/revoke`);
    assert.equal(revoked.status, 200);
    staged.sendBody();

    const answer = await within(5_000, staged.answer, 'the answer');
    assert.equal(answer?.status, 401);
    assert.match(answer?.body ?? '', /device_revoked/);
    const events = journal(server);
    const revokedAt = events.findIndex((event) => event.event_type === 'device.revoked');
    const outcomes = events.filter((event) => JSON.stringify(event).includes(command.command_id));
    assert.equal(outcomes.length, 1, 'rejected, and nothing else');
    const [outcome] = outcomes;
    assert.ok(outcome?.event_type === 'command.rejected');
    assert.ok(events.indexOf(outcome) > revokedAt);
    assert.equal(outcome.payload.rejection.code, 'device_revoked');
    assert.deepEqual(outcome.payload.principal, { kind: 'device', device_id: paired.deviceId });
    assert.equal(server.controlPlane.projection.projects().length, 0, 'nothing was created');
  });

  test('a revoked device that ignores the close frame is cut off, and nothing it sends is handled', async (t) => {
    const server = await start(t);
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const socket = await TlsWebSocket.connect(network(server).target, '/realtime', {
      headers: bearer(paired.credential),
      pin: paired.certificateSha256,
      hostile: true,
    });
    t.after(() => socket.destroy());
    socket.sendJson({ type: 'hello', protocol: 1, client: TEST_CLIENT, resume: null });
    await socket.waitFor((text) => text.includes('"welcome"'));

    const revokedAt = Date.now();
    const revoked = await loopback(server, 'POST', `/api/devices/${paired.deviceId}/revoke`);
    assert.equal(revoked.status, 200);
    assert.deepEqual(
      (await loopback<{ connected: string[] }>(server, 'GET', '/api/devices')).body.connected,
      [],
    );
    // The client does not answer the close frame, keeps its connection, and sends a command.
    const after = server.commands.createProject('Sent after revocation');
    socket.sendJson({ type: 'command', command: after });
    assert.equal((await socket.closed).code, 1008);
    await within(5_000, socket.disconnected, 'cutting the connection off');
    assert.ok(Date.now() - revokedAt < 5_000, 'not the 30 seconds ws waits for a close frame');

    assert.equal(
      journal(server).some((event) => JSON.stringify(event).includes(after.command_id)),
      false,
      'the command was not handled at all',
    );
    assert.equal(
      socket.messages.some((text) => text.includes('"command_ack"')),
      false,
    );
  });

  test('a request that holds back its body, or a connection that sends nothing, is cut off', async (t) => {
    const server = await start(t, {
      network: { timeouts: { requestMs: 300, idleMs: 600, keepAliveMs: 300, checkEveryMs: 50 } },
    });
    const paired = await pairDevice(network(server).target, await openWindow(server));
    const staged = await stageRequest(network(server).target, {
      method: 'POST',
      path: '/api/commands',
      headers: { ...bearer(paired.credential), 'content-type': 'application/json' },
      body: JSON.stringify(server.commands.createProject('Never sent')),
      pin: paired.certificateSha256,
    });
    const answer = await within(3_000, staged.answer, 'cutting off the request');
    assert.ok(answer === null || answer.status === 408, `answered ${answer?.status}`);

    const { socket } = await openTls(network(server).target, paired.certificateSha256);
    await within(
      3_000,
      new Promise((resolve) => {
        socket.once('close', resolve);
        socket.resume();
      }),
      'closing a silent connection',
    );
    assert.equal(server.controlPlane.projection.projects().length, 0);
  });

  test('the listener holds a bounded number of connections, and a device a few realtime ones', async (t) => {
    const server = await start(t);
    const { target, identity } = network(server);
    const paired = await pairDevice(target, await openWindow(server));
    const realtime = async () => {
      const socket = await TlsWebSocket.connect(target, '/realtime', {
        headers: bearer(paired.credential),
        pin: paired.certificateSha256,
      });
      socket.sendJson({ type: 'hello', protocol: 1, client: TEST_CLIENT, resume: null });
      return socket;
    };
    const held: TlsWebSocket[] = [];
    for (let index = 0; index < MAX_REALTIME_CONNECTIONS_PER_DEVICE; index += 1) {
      const socket = await realtime();
      await socket.waitFor((text) => text.includes('"welcome"'));
      held.push(socket);
    }
    const extra = await realtime();
    const refused = await extra.message(0);
    assert.equal((refused.error as { code: string }).code, 'too_many_connections');
    assert.equal((await extra.closed).code, 1008);
    await held.pop()?.close();
    await pause(50);
    const again = await realtime();
    await again.waitFor((text) => text.includes('"welcome"'));
    held.push(again);

    const sockets: TLSSocket[] = [];
    for (let index = held.length; index < MAX_NETWORK_CONNECTIONS; index += 1) {
      sockets.push((await openTls(target, identity.certificateSha256)).socket);
    }
    t.after(() => {
      for (const socket of sockets) socket.destroy();
      for (const socket of held) socket.destroy();
    });
    await assert.rejects(openTls(target, identity.certificateSha256), 'one connection too many');
    sockets.pop()?.destroy();
    await pause(50);
    // A request, rather than a connection left open: the server has taken it up when it answers.
    const health = await tlsRequest(target, {
      method: 'GET',
      path: '/api/health',
      pin: identity.certificateSha256,
    });
    assert.equal(health.status, 200, 'a connection that closes makes room for another');
  });
});
