import assert from 'node:assert/strict';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { request } from 'node:http';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type { PairingPageResponse } from '@halcyonic/contracts';
import { runDevicesCli } from '../cli/devices.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { SETTINGS_FILE } from '../settings.ts';
import { pairDevice } from '../testing/device.ts';
import { startTestServer } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';
import { type PageState, pairingPagePorts } from './pairing-page.ts';

/** LINK's scratch block: the page takes the first free one, as it takes 47802 to 47809 for real. */
const PORTS = [47950, 47959] as const;
const APPROVAL = DEMO_WORKSTREAMS[2] as (typeof DEMO_WORKSTREAMS)[number];

type Server = Awaited<ReturnType<typeof startTestServer>>;

/** A Mac on Wi-Fi: its `.local` name and one interface address, as the page should show them. */
const ON_WIFI = {
  localName: 'studio-mac.local',
  interfaces: {
    lo0: [
      {
        address: '127.0.0.1',
        netmask: '255.0.0.0',
        family: 'IPv4' as const,
        mac: '00:00:00:00:00:00',
        internal: true,
        cidr: null,
      },
    ],
    en0: [
      {
        address: '192.168.1.23',
        netmask: '255.255.255.0',
        family: 'IPv4' as const,
        mac: '00:00:00:00:00:00',
        internal: false,
        cidr: null,
      },
    ],
  },
};

async function start(t: TestContext, onWifi = true): Promise<Server> {
  const server = await startTestServer({
    page: { ports: PORTS, ...(onWifi ? { describeAs: ON_WIFI } : {}) },
  });
  t.after(() => server.stop());
  return server;
}

function page(server: Server) {
  assert.ok(server.page, 'the test server serves the pairing page');
  return server.page;
}

interface Answer {
  readonly status: number;
  readonly headers: Record<string, string | string[] | undefined>;
  readonly body: string;
}

/** A request with exactly these headers; a browser's own navigation by default. */
function call(
  port: number,
  method: string,
  path: string,
  headers: Record<string, string> = {},
  body?: string,
): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port,
        path,
        method,
        headers: { host: `127.0.0.1:${port}`, 'sec-fetch-site': 'none', ...headers },
      },
      (res) => {
        let text = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          text += chunk;
        });
        res.on('end', () =>
          resolve({ status: res.statusCode ?? 0, headers: res.headers, body: text }),
        );
      },
    );
    req.on('error', reject);
    if (body !== undefined) req.write(body);
    req.end();
  });
}

/** Opens a link to the page as the person's browser would, and returns the page's cookie. */
async function signIn(server: Server): Promise<string> {
  const link = new URL(page(server).link());
  const answer = await call(page(server).port, 'GET', `${link.pathname}${link.search}`);
  assert.equal(answer.status, 200);
  return String(answer.headers['set-cookie']).split(';')[0] ?? '';
}

/** A press on the page: a POST from the page itself, with its origin and cookie. */
async function press(server: Server, cookie: string, path: string, body?: unknown) {
  const port = page(server).port;
  const answer = await call(
    port,
    'POST',
    path,
    {
      cookie,
      origin: `http://127.0.0.1:${port}`,
      'sec-fetch-site': 'same-origin',
      ...(body === undefined ? {} : { 'content-type': 'application/json' }),
    },
    body === undefined ? undefined : JSON.stringify(body),
  );
  assert.equal(answer.status, 200, answer.body);
  return JSON.parse(answer.body) as PageState;
}

async function read(server: Server, cookie: string): Promise<PageState> {
  const answer = await call(page(server).port, 'GET', '/state', {
    cookie,
    'sec-fetch-site': 'same-origin',
  });
  assert.equal(answer.status, 200, answer.body);
  return JSON.parse(answer.body) as PageState;
}

function listenerTarget(server: Server) {
  const listener = server.networkSwitch?.listener();
  assert.ok(listener, 'the network listener is on');
  return { host: '127.0.0.1', port: listener.port, pin: listener.certificate_sha256 };
}

function settings(server: Server): Record<string, unknown> {
  const path = join(server.dataDir, SETTINGS_FILE);
  return existsSync(path)
    ? (JSON.parse(readFileSync(path, 'utf8')) as Record<string, unknown>)
    : {};
}

async function loopbackCommand(server: Server, command: unknown): Promise<void> {
  const answer = await fetch(`${server.baseUrl}/api/commands`, {
    method: 'POST',
    headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
    body: JSON.stringify(command),
  });
  assert.equal(answer.status, 202);
}

describe('the pairing page', () => {
  test('opens only by a link, once and in time, and turns nothing on by itself', async (t) => {
    const server = await start(t);
    const port = page(server).port;
    assert.ok(port >= PORTS[0] && port <= PORTS[1]);
    assert.equal((await call(port, 'GET', '/')).status, 403, 'no page without a link');
    const link = new URL(page(server).link());
    const first = await call(port, 'GET', `${link.pathname}${link.search}`);
    assert.equal(first.status, 200);
    assert.match(first.body, /content="0;url=\/"/, 'the page goes on to itself');
    assert.match(String(first.headers['set-cookie']), /HttpOnly; SameSite=Strict/);
    assert.equal(
      (await call(port, 'GET', `${link.pathname}${link.search}`)).status,
      403,
      'a link opens the page once',
    );
    const cookie = String(first.headers['set-cookie']).split(';')[0] ?? '';
    const shown = await call(port, 'GET', '/', { cookie });
    assert.equal(shown.status, 200);
    assert.doesNotMatch(shown.body, /pnpm/, 'the page names no tool');
    const state = await read(server, cookie);
    assert.equal(state.pairing, 'off');
    assert.equal(state.code, null);
    assert.equal(server.networkSwitch?.on, false, 'opening the page turns no listener on');
    assert.deepEqual(settings(server), {}, 'and writes no setting');
  });

  test('answers no other name, no other page and no press without its origin', async (t) => {
    const server = await start(t);
    const port = page(server).port;
    const cookie = await signIn(server);
    assert.equal(
      (await call(port, 'GET', '/state', { cookie, host: `localhost:${port}` })).status,
      403,
    );
    assert.equal(
      (await call(port, 'GET', '/state', { cookie, 'sec-fetch-site': 'cross-site' })).status,
      403,
    );
    assert.equal(
      (await call(port, 'GET', '/state', { cookie, origin: 'http://127.0.0.1:47930' })).status,
      403,
    );
    for (const headers of [
      { cookie, 'sec-fetch-site': 'same-origin' },
      { cookie, origin: 'http://127.0.0.1:47930', 'sec-fetch-site': 'same-site' },
      { origin: `http://127.0.0.1:${port}`, 'sec-fetch-site': 'same-origin' },
    ]) {
      assert.equal((await call(port, 'POST', '/pairing/on', headers)).status, 403);
    }
    assert.equal(server.networkSwitch?.on, false, 'no refused press turned anything on');
    const expired = await call(port, 'GET', '/?code=AAAA');
    assert.equal(expired.status, 403);
    // Another site may send the person only to trade a code, never to read or press.
    const navigation = {
      'sec-fetch-site': 'cross-site',
      'sec-fetch-mode': 'navigate',
      'sec-fetch-dest': 'document',
    };
    assert.equal((await call(port, 'GET', '/state', { cookie, ...navigation })).status, 403);
    assert.equal((await call(port, 'GET', '/', { cookie, ...navigation })).status, 403);
    const link = new URL(page(server).link());
    assert.equal(
      (
        await call(port, 'GET', `${link.pathname}${link.search}`, {
          'sec-fetch-site': 'cross-site',
          'sec-fetch-mode': 'no-cors',
        })
      ).status,
      403,
      'a code is traded only by a navigation',
    );
  });

  test('a cookie of its name that another port planted ahead of the person’s own keeps nobody out', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    const name = cookie.split('=')[0] ?? '';
    const planted = `${name}=${'0'.repeat(64)}; ${cookie}`;
    const answer = await call(page(server).port, 'GET', '/state', {
      cookie: planted,
      'sec-fetch-site': 'same-origin',
    });
    assert.equal(answer.status, 200);
    const only = await call(page(server).port, 'GET', '/state', {
      cookie: `${name}=${'0'.repeat(64)}`,
      'sec-fetch-site': 'same-origin',
    });
    assert.equal(only.status, 403);
  });

  test('turned on from the page, a device pairs while a task at work keeps going, and the setting stays', async (t) => {
    const server = await start(t);
    // A task waiting for its approval, started before pairing turned on.
    const project = server.commands.createProject('Paging');
    await loopbackCommand(server, project);
    await server.time.runUntilIdle();
    const projectId = server.controlPlane.projection.projects()[0]?.project_id;
    assert.ok(projectId);
    await loopbackCommand(server, server.commands.createWorkstream(projectId, APPROVAL));
    await server.time.runUntilIdle();
    const workstreamId = server.controlPlane.projection.workstreams()[0]?.workstream_id;
    assert.ok(workstreamId);
    await loopbackCommand(server, server.commands.startExecution(workstreamId, APPROVAL));
    await server.time.runUntilIdle();
    const waiting = server.controlPlane.projection.workstreams()[0];
    assert.equal(waiting?.status, 'waiting_for_human');

    const cookie = await signIn(server);
    const on = await press(server, cookie, '/pairing/on');
    assert.equal(on.pairing, 'on');
    assert.equal(on.code, null, 'turning on opens no window');
    assert.equal(settings(server).HALCYONIC_NETWORK_HOST, '0.0.0.0');
    const opened = await press(server, cookie, '/window');
    assert.match(opened.code ?? '', /^\d{8}$/);
    assert.equal(opened.window?.state, 'open');
    assert.deepEqual(opened.names, [
      `studio-mac.local:${opened.port}`,
      `192.168.1.23:${opened.port}`,
    ]);

    const target = listenerTarget(server);
    const paired = await pairDevice(target, opened.code ?? '', { label: 'Quest 3' });
    const after = await read(server, cookie);
    assert.equal(after.window?.state, 'paired');
    assert.equal(after.code, null, 'the code goes with its window');
    assert.deepEqual(
      after.headsets.map((headset) => headset.label),
      ['Quest 3'],
    );

    // The task goes on: the paired device approves it.
    const execution = server.controlPlane.projection.executions()[0];
    const approval = execution?.pending_approvals[0];
    assert.ok(execution && approval);
    const answer = await tlsRequest(target, {
      method: 'POST',
      path: '/api/commands',
      headers: { authorization: `Bearer ${paired.credential}`, 'content-type': 'application/json' },
      body: JSON.stringify(server.commands.approve(execution.execution_id, approval.approval_id)),
      pin: target.pin,
    });
    assert.equal(answer.status, 202);
    await server.time.runUntilIdle();
    assert.equal(server.controlPlane.projection.workstreams()[0]?.status, 'completed');
  });

  test('turned off from the page, devices lose the listener but stay paired, and it comes back as it was', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    await press(server, cookie, '/pairing/on');
    const { code } = await press(server, cookie, '/window');
    const before = listenerTarget(server);
    const paired = await pairDevice(before, code ?? '', { label: 'Quest 3' });

    const off = await press(server, cookie, '/pairing/off');
    assert.equal(off.pairing, 'off');
    assert.equal(server.networkSwitch?.on, false);
    assert.equal(settings(server).HALCYONIC_NETWORK_HOST, undefined);
    await assert.rejects(
      tlsRequest(before, { method: 'GET', path: '/api/health', pin: before.pin }),
      'nothing listens once pairing is off',
    );
    assert.equal(server.controlPlane.projection.device(paired.deviceId)?.revoked_at, null);

    await press(server, cookie, '/pairing/on');
    const again = listenerTarget(server);
    assert.equal(again.pin, before.pin, 'the same certificate, so the pin holds');
    const read = await tlsRequest(again, {
      method: 'GET',
      path: '/api/device/listener',
      headers: { authorization: `Bearer ${paired.credential}` },
      pin: again.pin,
    });
    assert.equal(read.status, 200, 'the paired device is accepted again');
  });

  test('Pair Headset from off turns pairing on and opens the window, in one press', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    assert.equal((await read(server, cookie)).pairing, 'off');
    const opened = await press(server, cookie, '/window');
    assert.equal(opened.pairing, 'on');
    assert.equal(opened.problem, null);
    assert.equal(opened.window?.state, 'open');
    assert.match(opened.code ?? '', /^\d{8}$/);
    assert.equal(settings(server).HALCYONIC_NETWORK_HOST, '0.0.0.0', 'kept on for the next start');
    const paired = await pairDevice(listenerTarget(server), opened.code ?? '', {
      label: 'Quest 3',
    });
    assert.equal(server.controlPlane.projection.device(paired.deviceId)?.revoked_at, null);
  });

  test('forgets a headset, and lets the code go when the person turns pairing off', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    await press(server, cookie, '/pairing/on');
    const { code } = await press(server, cookie, '/window');
    const paired = await pairDevice(listenerTarget(server), code ?? '', { label: 'Quest 3' });
    const forgotten = await press(server, cookie, '/headsets/forget', { id: paired.deviceId });
    assert.deepEqual(forgotten.headsets, []);
    assert.notEqual(server.controlPlane.projection.device(paired.deviceId)?.revoked_at, null);

    const reopened = await press(server, cookie, '/window');
    assert.ok(reopened.code);
    const stopped = await press(server, cookie, '/pairing/off');
    assert.equal(stopped.code, null);
    assert.equal(stopped.shown, false);
    const unknown = await press(server, cookie, '/headsets/forget', { id: 'nobody' });
    assert.equal(unknown.problem, "That headset isn't paired with this computer.");
  });
});

describe('what the page will not show or serve', () => {
  test('beside no name a headset can reach, it shows no code', async (t) => {
    const server = await start(t, false);
    const cookie = await signIn(server);
    await press(server, cookie, '/pairing/on');
    const opened = await press(server, cookie, '/window');
    assert.equal(opened.window?.state, 'open');
    assert.equal(opened.shown, true);
    assert.deepEqual(opened.names, [], 'a loopback address is no name for a headset');
    assert.equal(opened.code, null);
  });

  test('a browser that does not say where a request comes from is told why', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    const port = page(server).port;
    const answer = await new Promise<Answer>((resolve, reject) => {
      const req = request(
        { host: '127.0.0.1', port, path: '/state', headers: { host: `127.0.0.1:${port}`, cookie } },
        (res) => {
          let text = '';
          res.on('data', (chunk) => {
            text += chunk;
          });
          res.on('end', () =>
            resolve({ status: res.statusCode ?? 0, headers: res.headers, body: text }),
          );
        },
      );
      req.on('error', reject);
      req.end();
    });
    assert.equal(answer.status, 403);
    assert.match(answer.body, /does not say which page a request comes from/);
  });

  test('serves its own face, and nothing from another place', async (t) => {
    const server = await start(t);
    const cookie = await signIn(server);
    const font = await call(page(server).port, 'GET', '/Inter-Regular.ttf', {
      cookie,
      'sec-fetch-site': 'same-origin',
    });
    assert.equal(font.status, 200);
    assert.equal(font.headers['content-type'], 'font/ttf');
    assert.match(String(font.headers['content-security-policy']), /font-src 'self'/);
  });
});

describe('pnpm pair and the pairing page', () => {
  test('pnpm pair opens the page in the browser, with a link loopback gives only to the token', async (t) => {
    const server = await start(t);
    const noToken = await fetch(`${server.baseUrl}/api/pairing/page`, { method: 'POST' });
    assert.equal(noToken.status, 401);
    const opened: string[] = [];
    const lines: string[] = [];
    const status = await runDevicesCli(['pair'], {
      env: { HALCYONIC_DATA_DIR: server.dataDir, HALCYONIC_PORT: String(server.port) },
      print: (line) => lines.push(line),
      interrupted: new Promise(() => {}),
      open: (link) => opened.push(link),
    });
    assert.equal(status, 0);
    assert.equal(opened.length, 1);
    const file = opened[0] ?? '';
    assert.doesNotMatch(file, /code/, 'what is opened by its path carries no code');
    assert.equal(statSync(file).mode & 0o777, 0o600);
    const handed = /url=(http:\/\/127\.0\.0\.1:\d+\/\?code=[A-Za-z0-9_-]+)"/.exec(
      readFileSync(file, 'utf8'),
    );
    assert.ok(handed, 'the file sends the browser on to the link');
    assert.ok(lines.includes('Pairing opened in your browser.'), lines.join('\n'));
    assert.ok(
      lines.every((line) => !line.includes('code=')),
      'the link is not printed unless asked: terminals keep what they show',
    );
    assert.equal(server.networkSwitch?.on, false, 'opening the page turns nothing on');
    const link = new URL(handed[1] ?? '');
    // As the browser goes on from the file: a top-level navigation from another origin, the file's.
    const traded = await call(page(server).port, 'GET', `${link.pathname}${link.search}`, {
      'sec-fetch-site': 'cross-site',
      'sec-fetch-mode': 'navigate',
      'sec-fetch-dest': 'document',
    });
    assert.equal(traded.status, 200);
  });

  test('a control plane without the page answers 409, so pnpm pair pairs in the terminal', async (t) => {
    const server = await startTestServer({ network: {} });
    t.after(() => server.stop());
    const answer = await fetch(`${server.baseUrl}/api/pairing/page`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    assert.equal(answer.status, 409);
    const body = (await answer.json()) as PairingPageResponse | { error: { code: string } };
    assert.equal('error' in body && body.error.code, 'page_off');
  });
});

describe('the page’s ports', () => {
  test('follow the control plane’s own port, and a plane on port 0 gets a free one', async (t) => {
    assert.deepEqual(pairingPagePorts(47800), [47802, 47809], 'beside 47800, as before');
    assert.deepEqual(
      pairingPagePorts(47950),
      [47952, 47959],
      'beside another port, its own family',
    );
    assert.deepEqual(pairingPagePorts(0), [0, 0]);
    assert.deepEqual(pairingPagePorts(65530), [0, 0], 'too near the top for a family');
    const server = await startTestServer({ page: { ports: pairingPagePorts(0) } });
    t.after(() => server.stop());
    const port = page(server).port;
    assert.ok(port > 0 && (port < 47800 || port > 47809), `a free port of its own: ${port}`);
    const link = new URL(page(server).link());
    assert.equal(link.port, String(port), 'its link names the port it listens on');
    const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
    assert.equal(opened.status, 200, 'and it answers there');
  });
});
