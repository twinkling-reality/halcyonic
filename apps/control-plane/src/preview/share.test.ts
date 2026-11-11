import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, request } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, test } from 'node:test';
import type { CheckId, EventEnvelope, ProjectId } from '@halcyonic/contracts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import {
  lanAddress,
  ProjectShares,
  pinnedLookup,
  readsOnlyTo,
  ShareRefused,
  TUNNEL_CONFIG_FOLDERS,
  tunnelProfile,
} from './share.ts';

// Sharing a checked game at a public address (F1), with a stand-in tunnel: a process that prints
// an address as the tunnel does and stays up until it is stopped.

const PROJECT = '01a19000-0000-7000-8000-00000000aa01' as ProjectId;
const CHECK = '01a19000-0000-7000-8000-00000000cc01' as CheckId;
const ADDRESS = 'https://bright-quiet-garden.trycloudflare.com';
const scope = { project_id: PROJECT, workstream_id: null, execution_id: null };
const now = '2026-11-11T10:00:00.000Z' as never;
const homes: string[] = [];

afterEach(() => {
  for (const home of homes.splice(0)) rmSync(home, { recursive: true, force: true });
});

function standInTunnel(prints: boolean) {
  return () =>
    spawn(
      process.execPath,
      [
        '-e',
        prints
          ? `process.stderr.write('INF |  ${ADDRESS}  |\\n'); setInterval(() => {}, 1000);`
          : 'setInterval(() => {}, 1000);',
      ],
      { stdio: ['ignore', 'ignore', 'pipe'] },
    );
}

function setup(
  options: { prints?: boolean; answers?: boolean; previewPort?: number; lan?: string | null } = {},
) {
  const harness = createTestControlPlane({ logger: capturingLogger().logger });
  const { controlPlane } = harness;
  const record = <T extends Parameters<typeof controlPlaneDraft>[0]>(
    type: T,
    payload: Extract<EventEnvelope, { event_type: T }>['payload'],
  ) =>
    controlPlane.recorder.record(
      controlPlaneDraft(type, scope as never, payload as never, now, NO_CAUSE),
    );
  record('project.created', { name: 'Garden', location: null });
  record('project.preview.started', { check_id: CHECK, port: options.previewPort ?? 47931 });
  const home = mkdtempSync(join(tmpdir(), 'halcyonic-share-'));
  homes.push(home);
  const shares = new ProjectShares({
    recorder: controlPlane.recorder,
    projection: controlPlane.projection,
    clock: harness.time,
    logger: capturingLogger().logger,
    tunnel: '/unused',
    home,
    spawnTunnel: standInTunnel(options.prints ?? true),
    answers: async () => options.answers ?? true,
    startTimeoutMs: 2000,
    lanAddress: () => (options.lan === undefined ? '127.0.0.1' : options.lan),
    wifiPorts: [47914, 47919],
  });
  shares.attach(controlPlane.publisher);
  const types = () =>
    [...controlPlane.journal.readAll()]
      .map((stored) => stored.event.event_type)
      .filter((type) => type.startsWith('project.share.'));
  return { controlPlane, shares, record, types };
}

describe('sharing a checked game at an address anyone can open (F1)', () => {
  test('recorded only once the address answers; stopped when asked, and the address goes from the view', async (t) => {
    const { controlPlane, shares, types } = setup();
    t.after(() => shares.close());
    const shared = await shares.start(PROJECT, 'link', NO_CAUSE);
    assert.deepEqual(shared, { check_id: CHECK, url: ADDRESS, way: 'link' });
    assert.deepEqual(controlPlane.projection.project(PROJECT)?.share, {
      check_id: CHECK,
      url: ADDRESS,
      way: 'link',
      files: 'unknown',
    });
    await assert.rejects(shares.start(PROJECT, 'link', NO_CAUSE), /already shared/);
    assert.equal(await shares.stop(PROJECT, 'asked', NO_CAUSE), true);
    assert.deepEqual(types(), ['project.share.started', 'project.share.stopped']);
    assert.equal(controlPlane.projection.project(PROJECT)?.share, undefined);
  });

  test('a preview that stops takes its shared link with it', async (t) => {
    const { controlPlane, shares, record } = setup();
    t.after(() => shares.close());
    await shares.start(PROJECT, 'link', NO_CAUSE);
    record('project.preview.stopped', { check_id: CHECK, reason: 'new_check' });
    for (let i = 0; i < 100 && controlPlane.projection.share(PROJECT) !== undefined; i += 1)
      await new Promise((resolve) => setTimeout(resolve, 10));
    assert.equal(controlPlane.projection.share(PROJECT), undefined);
    const stopped = [...controlPlane.journal.readAll()].find(
      (stored) => stored.event.event_type === 'project.share.stopped',
    )?.event;
    assert.equal(
      stopped?.event_type === 'project.share.stopped' && stopped.payload.reason,
      'preview_stopped',
    );
  });

  test('nothing is recorded where no address comes or it does not answer with the game', async () => {
    const silent = setup({ prints: false });
    await assert.rejects(silent.shares.start(PROJECT, 'link', NO_CAUSE), /no address/);
    assert.deepEqual(silent.types(), []);
    const unanswered = setup({ answers: false });
    await assert.rejects(unanswered.shares.start(PROJECT, 'link', NO_CAUSE), /did not answer/);
    assert.deepEqual(unanswered.types(), []);
    assert.equal(unanswered.controlPlane.projection.share(PROJECT), undefined);
  });

  test('a share a restart left recorded as live is recorded stopped: no tunnel survives one', () => {
    const { controlPlane, record, types } = setup();
    record('project.share.started', { check_id: CHECK, url: ADDRESS, way: 'link' });
    const again = new ProjectShares({
      recorder: controlPlane.recorder,
      projection: controlPlane.projection,
      clock: { now: () => new Date() },
      logger: capturingLogger().logger,
      tunnel: '/unused',
      home: '/unused',
    });
    again.attach(controlPlane.publisher);
    assert.deepEqual(types(), ['project.share.started', 'project.share.stopped']);
    assert.equal(controlPlane.projection.share(PROJECT), undefined);
  });

  test("the address is reached where Cloudflare's resolver says, in either form Node asks for", async (t) => {
    const server = createServer((_req, res) => res.end('ok'));
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    t.after(() => server.close());
    const port = (server.address() as { port: number }).port;
    const status = await new Promise<number | string>((resolve) => {
      const req = request(
        `http://share.invalid:${port}/`,
        { lookup: pinnedLookup('127.0.0.1') },
        (res) => {
          res.resume();
          resolve(res.statusCode ?? 0);
        },
      );
      req.on('error', (error) => resolve(error.message));
      req.end();
    });
    assert.equal(status, 200);
    const single = await new Promise<string>((resolve) =>
      pinnedLookup('127.0.0.1')('share.invalid', {}, (_error, address) => resolve(String(address))),
    );
    assert.equal(single, '127.0.0.1');
  });

  test('on the Wi-Fi: only reads reach the preview, under its own host, and the address goes when stopped', async (t) => {
    const seen: {
      method: string | undefined;
      host: string | undefined;
      cookie: string | undefined;
    }[] = [];
    const preview = createServer((req, res) => {
      seen.push({ method: req.method, host: req.headers.host, cookie: req.headers.cookie });
      res.writeHead(200, {
        'content-type': 'text/html',
        'content-security-policy': "default-src 'self'",
      });
      res.end('<p>the game</p>');
    });
    await new Promise<void>((resolve) => preview.listen(0, '127.0.0.1', resolve));
    t.after(() => preview.close());
    const previewPort = (preview.address() as { port: number }).port;
    const { controlPlane, shares, types } = setup({ previewPort });
    t.after(() => shares.close());
    const shared = await shares.start(PROJECT, 'wifi', NO_CAUSE);
    assert.equal(shared.way, 'wifi');
    assert.match(shared.url, /^http:\/\/127\.0\.0\.1:4791[4-9]$/);
    assert.equal(controlPlane.projection.project(PROJECT)?.share?.way, 'wifi');
    const ask = (method: string, path = '/', headers: Record<string, string> = {}) =>
      new Promise<{ status: number; body: string; csp: string | undefined }>((resolve, reject) => {
        const req = request(`${shared.url}${path}`, { method, headers, agent: false }, (res) => {
          let body = '';
          res.on('data', (chunk) => {
            body += chunk;
          });
          res.on('end', () =>
            resolve({
              status: res.statusCode ?? 0,
              body,
              csp: res.headers['content-security-policy'] as string | undefined,
            }),
          );
        });
        req.on('error', reject);
        req.end(method === 'POST' ? 'x=1' : undefined);
      });
    const got = await ask('GET', '/index.html', { cookie: 'a=b', host: 'evil.example' });
    assert.equal(got.status, 200);
    assert.equal(got.body, '<p>the game</p>');
    assert.equal(got.csp, "default-src 'self'", "the preview's own headers come back");
    assert.equal((await ask('HEAD')).status, 200);
    assert.equal((await ask('POST')).status, 405);
    assert.equal((await ask('PUT')).status, 405);
    const forwarded = seen.at(-2);
    assert.equal(forwarded?.host, `127.0.0.1:${previewPort}`);
    assert.equal(forwarded?.cookie, undefined, 'no cookie is passed on');
    assert.ok(
      seen.every((entry) => entry.method === 'GET' || entry.method === 'HEAD'),
      'nothing but reads reached the preview',
    );
    await assert.rejects(shares.start(PROJECT, 'link', NO_CAUSE), /already shared/);
    assert.equal(await shares.stop(PROJECT, 'asked', NO_CAUSE), true);
    await assert.rejects(ask('GET'), /ECONNREFUSED/);
    assert.deepEqual(types(), ['project.share.started', 'project.share.stopped']);
  });

  test('on the Wi-Fi: refused in words off any network, and nothing recorded where the preview does not answer', async () => {
    const offline = setup({ lan: null });
    await assert.rejects(offline.shares.start(PROJECT, 'wifi', NO_CAUSE), (error: unknown) => {
      assert.ok(error instanceof ShareRefused);
      assert.equal(error.code, 'no_network');
      return true;
    });
    // Nothing listens at the preview's port: the listener answers 502, never 200.
    const silent = setup({ previewPort: 47913 });
    await assert.rejects(silent.shares.start(PROJECT, 'wifi', NO_CAUSE), /did not answer/);
    assert.deepEqual(silent.types(), []);
  });

  test("the Mac's address on the Wi-Fi is a private IPv4 one, the Wi-Fi's own first", () => {
    const entry = (address: string, internal = false) =>
      ({ address, family: 'IPv4', internal, netmask: '', mac: '', cidr: null }) as never;
    assert.equal(
      lanAddress({
        lo0: [entry('127.0.0.1', true)],
        en5: [entry('10.0.0.7')],
        en0: [entry('192.168.1.20')],
      }),
      '192.168.1.20',
    );
    assert.equal(
      lanAddress({ en0: [entry('8.8.8.8')], utun3: [entry('172.20.1.2')] }),
      '172.20.1.2',
    );
    assert.equal(lanAddress({ en0: [entry('172.32.0.1')], lo0: [entry('127.0.0.1', true)] }), null);
  });

  test('the tunnel reads no configuration outside its own home, and macOS takes its profile', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const home = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-share-home-')));
    homes.push(home);
    assert.ok(
      TUNNEL_CONFIG_FOLDERS.every((folder) =>
        tunnelProfile(home, '/usr/bin/true').includes(`(subpath "${folder}")`),
      ),
    );
    // A stand-in for a system configuration folder, with a file in it.
    const etc = join(home, 'etc-cloudflared');
    mkdirSync(etc);
    writeFileSync(join(etc, 'config.yml'), 'ingress: []\n');
    const profile = tunnelProfile(join(home, 'own'), '/usr/bin/true', undefined, [etc]);
    const run = (command: string[], sandboxed: boolean) =>
      new Promise<{ code: number | null; err: string }>((resolve) => {
        const child = sandboxed
          ? spawn('/usr/bin/sandbox-exec', ['-p', profile, ...command], {
              stdio: ['ignore', 'ignore', 'pipe'],
            })
          : spawn(command[0] as string, command.slice(1), { stdio: ['ignore', 'ignore', 'pipe'] });
        let err = '';
        child.stderr?.on('data', (chunk) => {
          err += chunk;
        });
        child.on('exit', (code) => resolve({ code, err }));
      });
    const cat = ['/bin/cat', join(etc, 'config.yml')];
    assert.equal((await run(cat, false)).code, 0, 'readable outside the sandbox');
    const refused = await run(cat, true);
    assert.notEqual(refused.code, 0);
    assert.match(refused.err, /Operation not permitted/);
    assert.equal((await run(['/usr/bin/true'], true)).code, 0);
  });

  test('the Wi-Fi listener bounds its connections as the network listener does', () => {
    const server = readsOnlyTo(47931);
    assert.equal(server.maxConnections, 32);
    assert.equal(server.keepAliveTimeout, 5_000);
  });

  test('which ways work: a link where the tunnel is set up, the Wi-Fi where the Mac has an address, else why, never a guess', () => {
    const { controlPlane } = setup();
    const ways = (tunnel: string | null, lan: () => string | null) =>
      new ProjectShares({
        recorder: controlPlane.recorder,
        projection: controlPlane.projection,
        clock: { now: () => new Date() },
        logger: capturingLogger().logger,
        tunnel,
        home: '/unused',
        lanAddress: lan,
      }).ways();
    assert.deepEqual(
      ways('/pinned/cloudflared', () => '192.168.1.20'),
      { link: 'ready', wifi: 'ready' },
    );
    assert.deepEqual(
      ways(null, () => null),
      { link: 'not_set_up', wifi: 'no_network' },
    );
    assert.deepEqual(
      ways(null, () => {
        throw new Error('no interfaces');
      }),
      { link: 'not_set_up', wifi: 'unknown' },
    );
  });
});
