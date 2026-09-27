import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import { describe, type TestContext, test } from 'node:test';
import type { UnderstandingResult } from '@halcyonic/contracts';
import { SalidiumClient } from './client.ts';
import { isFailure, parseJson, readDiscovery, refusal } from './connection.ts';
import { openSalidiumFeed, type SalidiumFeedEvent } from './feed.ts';
import { toUnderstanding } from './report.ts';
import { SseParser } from './sse.ts';
import { consumerToken, fixture } from './testing/fake-salidium.ts';
import { until } from './testing/until.ts';
import { readFeedMessage, validateReport } from './wire.ts';

/**
 * The real wire, opt in: set SALIDIUM_CHECKOUT to a Salidium checkout whose build output exists.
 *
 * Each test runs Salidium's own consumer test daemon (`scripts/consumer-test-daemon.mjs`) from that
 * checkout as a separate process. It seeds the synthetic sessions behind the retained fixtures on the
 * fixtures' fixed clock, enables no provider adapter, creates a throwaway credential, and writes only
 * to a temporary directory; nothing of Salidium is imported here. Its environment is minimal on top of that: a scratch HOME, SALIDIUM_HOME and
 * TMPDIR, and a PATH without agent CLIs, so nothing it could spawn is found.
 */
const CHECKOUT = process.env.SALIDIUM_CHECKOUT;

interface Identity {
  readonly provider: string;
  readonly sessionId: string;
}

interface Ready {
  readonly home: string;
  readonly discovery: string;
  readonly baseUrl: string;
  readonly token: string;
  readonly sessions: {
    readonly verified: Identity;
    readonly failing: Identity;
    readonly working: Identity;
    readonly internal: Identity;
  };
}

async function daemon(t: TestContext) {
  assert.ok(CHECKOUT);
  const scratch = mkdtempSync(join(tmpdir(), 'halcyonic-salidium-live-'));
  mkdirSync(join(scratch, 'home'));
  const child = spawn(process.execPath, [join(CHECKOUT, 'scripts', 'consumer-test-daemon.mjs')], {
    cwd: scratch,
    env: {
      PATH: '/usr/bin:/bin',
      HOME: join(scratch, 'home'),
      SALIDIUM_HOME: join(scratch, 'salidium'),
      TMPDIR: scratch,
    },
    stdio: ['pipe', 'pipe', 'inherit'],
  });
  child.stdin.on('error', () => {});
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  t.after(async () => {
    child.stdin.end();
    await exited;
    rmSync(scratch, { recursive: true, force: true });
  });
  const lines: string[] = [];
  createInterface({ input: child.stdout }).on('line', (line) => lines.push(line));
  await until(() => lines.length > 0 || child.exitCode !== null, 'the daemon', 30_000);
  const ready = JSON.parse(lines.shift() ?? 'null') as Ready | null;
  assert.ok(ready, 'the daemon exited before it was ready');
  return {
    ready,
    exited,
    async command(name: 'message' | 'forget' | 'revoke' | 'stop') {
      child.stdin.write(`${name}\n`);
      await until(() => lines.length > 0, `the daemon to acknowledge ${name}`);
      assert.deepEqual(JSON.parse(lines.shift() ?? 'null'), { ok: true, command: name });
    },
  };
}

function reason(result: UnderstandingResult): string {
  return result.availability === 'available'
    ? 'available'
    : `${result.availability}/${result.reason.code}`;
}

const disconnections = (events: SalidiumFeedEvent[]) =>
  events.flatMap((event) =>
    event.type === 'disconnected' ? [`${event.availability}/${event.reason.code}`] : [],
  );

describe('the real Salidium consumer test daemon', {
  skip: CHECKOUT ? false : 'set SALIDIUM_CHECKOUT to a built Salidium checkout to run',
  timeout: 120_000,
}, () => {
  test('serves the synthetic sessions exactly as the retained fixtures describe them', async (t) => {
    const { ready } = await daemon(t);
    assert.equal(ready.discovery, join(ready.home, 'consumer.json'));
    const discovered = readDiscovery(JSON.parse(readFileSync(ready.discovery, 'utf8')), 'file');
    assert.ok(!isFailure(discovered));
    assert.equal(discovered.contract.baseUrl, ready.baseUrl);

    const client = new SalidiumClient({ home: ready.home, credential: ready.token });
    for (const [name, kind, identity] of [
      ['session-report-verified', 'claude-agent', ready.sessions.verified],
      ['session-report-failing', 'codex', ready.sessions.failing],
      ['session-report-working', 'claude-agent', ready.sessions.working],
    ] as const) {
      const result = await client.understand(kind, identity.sessionId);
      assert.equal(result.availability, 'available', reason(result));
      if (result.availability !== 'available') return;
      const report = validateReport(fixture(name));
      assert.ok(report.ok);
      // The daemon runs on the fixtures' clock, so only its instance differs from the recording.
      assert.deepEqual(result.understanding, toUnderstanding(report.value, discovered));
    }

    assert.equal(
      reason(await client.understand('codex', 'not-yet-reported')),
      'not_found/not_observed',
    );
    const internal = ready.sessions.internal.sessionId;
    assert.equal(
      reason(await client.understand('claude-agent', internal)),
      'not_found/not_observed',
    );
    const stranger = new SalidiumClient({ home: ready.home, credential: consumerToken() });
    const refused = await stranger.understand('claude-agent', ready.sessions.verified.sessionId);
    assert.equal(reason(refused), 'unauthorized/credential_rejected');
  });

  test('follows the feed through a change, a removal and a revocation', async (t) => {
    const { ready, command } = await daemon(t);
    const client = new SalidiumClient({ home: ready.home, credential: ready.token });
    const failing = ready.sessions.failing.sessionId;
    const events: SalidiumFeedEvent[] = [];
    const feed = openSalidiumFeed({
      home: ready.home,
      credential: ready.token,
      initialDelayMs: 50,
      maxDelayMs: 200,
      onEvent: (event) => events.push(event),
    });
    t.after(() => feed.close());
    await until(() => events.some((event) => event.type === 'resync'), 'resync');

    await command('message');
    await until(() => events.some((event) => event.type === 'session_changed'), 'a change');
    const changed = events.find((event) => event.type === 'session_changed');
    assert.deepEqual(changed, {
      type: 'session_changed',
      sessionId: `codex:${failing}`,
      provider: 'codex',
      nativeId: failing,
      evidenceSequence: 8,
    });
    const refreshed = await client.understand('codex', failing, { sessionId: `codex:${failing}` });
    assert.equal(
      refreshed.availability === 'available' && refreshed.understanding.source.evidence_sequence,
      8,
    );

    await command('forget');
    await until(() => events.some((event) => event.type === 'session_removed'), 'a removal');
    assert.deepEqual(
      events.find((event) => event.type === 'session_removed'),
      {
        type: 'session_removed',
        sessionId: `codex:${failing}`,
        provider: 'codex',
        nativeId: failing,
      },
    );
    assert.equal(reason(await client.understand('codex', failing)), 'not_found/not_observed');

    await command('revoke');
    await until(() => disconnections(events).length === 2, 'the refusal', 10_000);
    assert.deepEqual(disconnections(events), [
      'unauthorized/credential_revoked',
      'unauthorized/credential_rejected',
    ]);
    const verified = ready.sessions.verified.sessionId;
    assert.equal(
      reason(await client.understand('claude-agent', verified)),
      'unauthorized/credential_rejected',
    );
  });

  test('says Salidium stopped, then that it is not running', async (t) => {
    const { ready, command, exited } = await daemon(t);
    const events: SalidiumFeedEvent[] = [];
    const feed = openSalidiumFeed({
      home: ready.home,
      credential: ready.token,
      initialDelayMs: 50,
      maxDelayMs: 200,
      onEvent: (event) => events.push(event),
    });
    t.after(() => feed.close());
    await until(() => events.some((event) => event.type === 'resync'), 'resync');
    await command('stop');
    await exited;
    await until(() => disconnections(events).length === 2, 'the daemon to be gone');
    assert.deepEqual(disconnections(events), [
      'unavailable/shutting_down',
      'unavailable/not_running',
    ]);
    const client = new SalidiumClient({ home: ready.home, credential: ready.token });
    const verified = ready.sessions.verified.sessionId;
    assert.equal(
      reason(await client.understand('claude-agent', verified)),
      'unavailable/not_running',
    );
  });

  test('refuses a foreign Host, Origin or site with contract errors the client maps', async (t) => {
    const { ready } = await daemon(t);
    // fetch cannot send another Host, so these requests are made with node:http.
    const ask = (headers: Record<string, string>) =>
      new Promise<{ status: number; body: unknown }>((resolve, reject) => {
        const sent = request(`${ready.baseUrl}/discovery`, { headers }, (response) => {
          let text = '';
          response.setEncoding('utf8');
          response.on('data', (chunk: string) => {
            text += chunk;
          });
          response.on('end', () =>
            resolve({ status: response.statusCode ?? 0, body: parseJson(text) }),
          );
        });
        sent.on('error', reject);
        sent.end();
      });
    const port = new URL(ready.baseUrl).port;
    const cases: [Record<string, string>, number, string][] = [
      [{ Host: `evil.example:${port}` }, 421, 'unavailable/host_not_allowed'],
      [{ Origin: 'https://evil.example' }, 403, 'unavailable/origin_not_allowed'],
      [{ 'Sec-Fetch-Site': 'cross-site' }, 403, 'unavailable/origin_not_allowed'],
    ];
    for (const [headers, status, mapped] of cases) {
      const answer = await ask(headers);
      assert.equal(answer.status, status);
      const refused = refusal(answer.status, answer.body, 'discovery');
      assert.equal(refused && `${refused.availability}/${refused.reason.code}`, mapped);
    }
  });

  test('opens the feed with resync and sends a heartbeat every fifteen seconds', async (t) => {
    const { ready } = await daemon(t);
    const controller = new AbortController();
    t.after(() => controller.abort());
    const response = await fetch(`${ready.baseUrl}/feed`, {
      headers: { authorization: `Bearer ${ready.token}` },
      signal: controller.signal,
    });
    assert.equal(response.headers.get('content-type'), 'text/event-stream; charset=utf-8');
    const reader = response.body?.getReader();
    assert.ok(reader);
    const decoder = new TextDecoder();
    const parser = new SseParser(1024 * 1024);
    const received: { type: string; at: number }[] = [];
    while (received.filter((message) => message.type === 'heartbeat').length < 1) {
      const chunk = await reader.read();
      assert.equal(chunk.done, false);
      for (const data of parser.push(decoder.decode(chunk.value, { stream: true }))) {
        const message = readFeedMessage(JSON.parse(data));
        assert.ok(message?.ok);
        received.push({ type: message.value.type, at: Date.now() });
      }
    }
    assert.deepEqual(
      received.map((message) => message.type),
      ['resync', 'heartbeat'],
    );
    const [resync, heartbeat] = received;
    const interval = (heartbeat?.at ?? 0) - (resync?.at ?? 0);
    assert.ok(interval > 14_000 && interval < 17_000, `heartbeat after ${interval} ms`);
  });
});
