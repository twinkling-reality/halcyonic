import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as sleep } from 'node:timers/promises';
import {
  openSalidiumFeed,
  reconnectDelay,
  type SalidiumFeedEvent,
  type SalidiumFeedOptions,
} from './feed.ts';
import { consumerToken, contractError, FakeSalidium, fixture } from './testing/fake-salidium.ts';
import { until } from './testing/until.ts';

async function start(t: TestContext): Promise<FakeSalidium> {
  const fake = await FakeSalidium.start();
  t.after(() => fake.close());
  return fake;
}

function follow(
  t: TestContext,
  fake: FakeSalidium,
  options: Partial<SalidiumFeedOptions> = {},
): SalidiumFeedEvent[] {
  const events: SalidiumFeedEvent[] = [];
  const feed = openSalidiumFeed({
    home: fake.home,
    credential: fake.token,
    timeoutMs: 1_000,
    initialDelayMs: 10,
    maxDelayMs: 40,
    onEvent: (event) => events.push(event),
    ...options,
  });
  t.after(() => feed.close());
  return events;
}

const count = (events: SalidiumFeedEvent[], type: SalidiumFeedEvent['type']) =>
  events.filter((event) => event.type === type).length;

const disconnections = (events: SalidiumFeedEvent[]) =>
  events.flatMap((event) =>
    event.type === 'disconnected' ? [`${event.availability}/${event.reason.code}`] : [],
  );

describe("following Salidium's change feed", () => {
  test('opens after the instance check, starts with resync and relays notifications', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake);
    await until(() => count(events, 'resync') === 1, 'resync');
    fake.send(fixture('session-feed-heartbeat'));
    fake.send({ ...fixture('session-feed-heartbeat'), type: 'session.renamed' });
    fake.send(fixture('session-feed-session-changed'));
    fake.send(fixture('session-feed-session-removed'));
    fake.send({ ...fixture('session-feed-session-removed'), native: null });
    await until(() => events.length === 4, 'notifications');
    assert.deepEqual(events, [
      { type: 'resync' },
      {
        type: 'session_changed',
        sessionId: 'claude-code:6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11',
        provider: 'claude-code',
        nativeId: '6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11',
        evidenceSequence: 22,
      },
      {
        type: 'session_removed',
        sessionId: 'codex:0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47',
        provider: 'codex',
        nativeId: '0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47',
      },
      {
        type: 'session_removed',
        sessionId: 'codex:0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47',
        provider: null,
        nativeId: null,
      },
    ]);
    assert.deepEqual(
      fake.requests.map((request) => [request.path, request.headers.authorization]),
      [
        ['/consumer/v1/discovery', undefined],
        ['/consumer/v1/feed', `Bearer ${fake.token}`],
      ],
    );
    assert.equal(fake.requests[1]?.headers.accept, 'text/event-stream');
  });

  test('reconnects after Salidium stops and starts again, and resyncs', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake);
    await until(() => count(events, 'resync') === 1, 'first resync');
    fake.send({ ...fixture('session-feed-closing'), reason: 'shutting-down' });
    fake.removeDiscovery();
    fake.endFeeds();
    await until(() => disconnections(events).length === 2, 'not running');
    fake.instanceId = randomBytes(16).toString('hex');
    fake.writeDiscovery();
    await until(() => count(events, 'resync') === 2, 'second resync');
    assert.deepEqual(disconnections(events), [
      'unavailable/shutting_down',
      'unavailable/not_running',
    ]);
    assert.equal(fake.feedConnections, 2);
  });

  test('stops sending a credential Salidium revoked, and keeps asking whether it is accepted again', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake);
    await until(() => count(events, 'resync') === 1, 'resync');
    fake.token = consumerToken();
    fake.send(fixture('session-feed-closing'));
    fake.endFeeds();
    await until(() => disconnections(events).length === 2, 'refusal');
    assert.deepEqual(disconnections(events), [
      'unauthorized/credential_revoked',
      'unauthorized/credential_rejected',
    ]);
  });

  test('reconnects when three heartbeats are missed', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake, { silenceMs: 150 });
    await until(() => count(events, 'resync') === 2, 'reconnection');
    assert.deepEqual(disconnections(events), ['unavailable/heartbeats_missed']);
    assert.equal(fake.feedConnections, 2);
  });

  test('keeps a quiet connection that still receives heartbeats', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake, { silenceMs: 150 });
    await until(() => count(events, 'resync') === 1, 'resync');
    for (let beat = 0; beat < 6; beat++) {
      await sleep(50);
      fake.send(fixture('session-feed-heartbeat'));
    }
    assert.equal(fake.feedConnections, 1);
    assert.deepEqual(disconnections(events), []);
  });

  test('treats a malformed message as incompatibility, and resyncs on a new connection', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake);
    await until(() => count(events, 'resync') === 1, 'resync');
    fake.send({ ...fixture('session-feed-session-changed'), evidenceSeq: -1 });
    await until(() => count(events, 'resync') === 2, 'reconnection after an invalid message');
    fake.sendRaw('data: {not json\n\n');
    await until(() => count(events, 'resync') === 3, 'reconnection after non-JSON');
    assert.deepEqual(disconnections(events), [
      'incompatible/invalid_document',
      'incompatible/invalid_document',
    ]);
    assert.equal(count(events, 'session_changed'), 0);
  });

  test('requires every connection to start with resync', async (t) => {
    const fake = await start(t);
    fake.resyncOnConnect = false;
    const events = follow(t, fake);
    await until(() => fake.openFeeds === 1, 'connection');
    fake.send(fixture('session-feed-session-changed'));
    await until(() => disconnections(events).length === 1, 'refusal');
    assert.deepEqual(disconnections(events), ['incompatible/invalid_document']);
    assert.equal(count(events, 'session_changed'), 0);
  });

  test('says why it cannot connect once, while it keeps trying without sending a credential', async (t) => {
    const fake = await start(t);
    const events = follow(t, fake, { credential: null });
    await until(() => fake.requests.length >= 3, 'retries');
    assert.deepEqual(events, [
      {
        type: 'disconnected',
        availability: 'unauthorized',
        reason: {
          code: 'credential_missing',
          message:
            'No Salidium consumer credential is configured. Create one with `salidium consumer create <label>`.',
        },
      },
    ]);
    assert.deepEqual(fake.authorizedRequests(), []);
  });

  test("says unavailable when Salidium's loopback guard or an internal failure refuses the feed", async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/feed', (response) =>
      contractError(response, 421, 'host-not-allowed', 'only a loopback Host is accepted'),
    );
    const events = follow(t, fake);
    await until(() => disconnections(events).length === 1, 'the refusal');
    fake.overrides.set('/consumer/v1/feed', (response) =>
      contractError(response, 500, 'internal', 'the request could not be completed'),
    );
    await until(() => disconnections(events).length === 2, 'the failure');
    assert.deepEqual(disconnections(events), [
      'unavailable/host_not_allowed',
      'unavailable/server_error',
    ]);
  });

  test('says incompatible when the feed endpoint answers outside the contract', async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/feed', (response) => {
      response.statusCode = 200;
      response.setHeader('Content-Type', 'application/json');
      response.end('{}');
    });
    const events = follow(t, fake);
    await until(() => disconnections(events).length === 1, 'refusal');
    assert.deepEqual(disconnections(events), ['incompatible/unexpected_status']);
  });

  test('closes its connection and stops trying when closed', async (t) => {
    const fake = await start(t);
    const events: SalidiumFeedEvent[] = [];
    const feed = openSalidiumFeed({
      home: fake.home,
      credential: fake.token,
      onEvent: (event) => events.push(event),
    });
    await until(() => count(events, 'resync') === 1, 'resync');
    await feed.close();
    await until(() => fake.openFeeds === 0, 'the connection to close');
    const requests = fake.requests.length;
    await sleep(50);
    assert.equal(fake.requests.length, requests);
    assert.deepEqual(events, [{ type: 'resync' }]);
  });

  test('closes promptly while waiting to reconnect', async (t) => {
    const fake = await start(t);
    fake.removeDiscovery();
    const events: SalidiumFeedEvent[] = [];
    const feed = openSalidiumFeed({
      home: fake.home,
      credential: fake.token,
      initialDelayMs: 60_000,
      onEvent: (event) => events.push(event),
    });
    await until(() => events.length === 1, 'the first failure');
    const began = Date.now();
    await feed.close();
    assert.ok(Date.now() - began < 1_000);
  });

  test('doubles the reconnection delay up to the maximum', () => {
    assert.deepEqual(
      [0, 1, 2, 3, 4, 5, 10].map((failures) => reconnectDelay(failures, 1_000, 30_000)),
      [1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000],
    );
  });
});
