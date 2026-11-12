import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createServer, request } from 'node:http';
import { type AddressInfo, connect, type createServer as createTcpServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import {
  apiAuthority,
  declaredUpstream,
  gatedEnvironment,
  type KeyGateDeclaration,
  RUN_CREDENTIAL_PREFIX,
  SANDBOX_EXEC,
  startApiGate,
  startKeyGate,
  startProviderGate,
} from '@halcyonic/seatbelt';
import { CLAUDE_CODE_KEY_GATE } from './claude-agent-runtime.ts';
import { confinedFolder, confinedLaunch, findInConfinedStore } from './confinement.ts';

function scratch(t: TestContext): string {
  const folder = mkdtempSync(join(tmpdir(), 'halcyonic-claude-confine-'));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  return folder;
}

/** A request through the gate as a proxy client sends one: absolute-form to `url`. */
function viaGate(gate: number, url: string): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    const req = request({ host: '127.0.0.1', port: gate, method: 'GET', path: url }, (res) => {
      let body = '';
      res.on('data', (chunk) => {
        body += chunk;
      });
      res.on('end', () => resolve({ status: res.statusCode ?? 0, body }));
    });
    req.on('error', reject);
    req.end();
  });
}

/** A CONNECT through the gate: the status line it answers. */
function tunnel(gate: number, authority: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = connect(gate, '127.0.0.1', () =>
      socket.write(`CONNECT ${authority} HTTP/1.1\r\nHost: ${authority}\r\n\r\n`),
    );
    socket.once('data', (data) => {
      resolve(data.toString().split('\r\n')[0] ?? '');
      socket.destroy();
    });
    socket.on('error', reject);
  });
}

async function listening(
  server: ReturnType<typeof createServer> | ReturnType<typeof createTcpServer>,
) {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  return (server.address() as AddressInfo).port;
}

const REAL = 'sk-ant-fake-real-key-for-a-test';

/** A second agent app, a test stand-in: its key and base address under other names, sent as a Bearer token. */
const SECOND: KeyGateDeclaration = {
  baseVariable: 'OPENAI_BASE_URL',
  keyVariable: 'OPENAI_API_KEY',
  otherCredentials: ['OPENAI_ORG_TOKEN'],
  header: 'authorization',
  bearer: true,
  upstream: 'https://api.example.test/v1',
};

/** An upstream that answers with what it was sent, and counts what reached it. */
function echoUpstream() {
  const reached: { key: string | null; authorization: string | null }[] = [];
  const server = createServer((req, res) => {
    reached.push({
      key: (req.headers['x-api-key'] as string | undefined) ?? null,
      authorization: req.headers.authorization ?? null,
    });
    res.writeHead(200, { 'content-type': 'text/event-stream' });
    res.write('event: message_start\n\n');
    setTimeout(
      () =>
        res.end(
          JSON.stringify({
            path: req.url,
            key: req.headers['x-api-key'] ?? null,
            authorization: req.headers.authorization ?? null,
            version: req.headers['anthropic-version'] ?? null,
            beta: req.headers['anthropic-beta'] ?? null,
            host: req.headers.host ?? null,
          }),
        ),
      30,
    );
  });
  return { server, reached };
}

/** A request to the gate as an app sends one: to `path`, with `headers` (the run credential among them, or not). */
function toGate(
  gate: number,
  path: string,
  headers: Record<string, string>,
): Promise<{ status: number; type: string | undefined; body: string }> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port: gate,
        method: 'POST',
        path,
        headers: { 'anthropic-version': '2023-06-01', 'anthropic-beta': 'a-beta', ...headers },
      },
      (res) => {
        let body = '';
        res.on('data', (chunk) => {
          body += chunk;
        });
        res.on('end', () =>
          resolve({ status: res.statusCode ?? 0, type: res.headers['content-type'], body }),
        );
      },
    );
    req.on('error', reject);
    req.end('{}');
  });
}

/** How `declaration`'s app presents `credential`. */
function presenting(declaration: KeyGateDeclaration, credential: string): Record<string, string> {
  return { [declaration.header]: declaration.bearer ? `Bearer ${credential}` : credential };
}

/** A gate for `declaration` against a fresh echo upstream. */
async function gated(t: TestContext, declaration: KeyGateDeclaration) {
  const api = echoUpstream();
  const port = await listening(api.server);
  const gate = await startKeyGate(declaration, {
    [declaration.keyVariable]: REAL,
    [declaration.baseVariable]: `http://127.0.0.1:${port}`,
  });
  t.after(async () => {
    await gate.close();
    api.server.close();
  });
  return { api, port, gate };
}

describe("the gate holds the agent app's key, the agent holds none (VRIFY, 2026-11-12)", () => {
  test("a run's own credential reaches the declared upstream with the person's key alone, its anthropic headers, and the answer streamed back", async (t) => {
    const { gate, port } = await gated(t, CLAUDE_CODE_KEY_GATE);
    const run = gate.issue();
    assert.match(run, new RegExp(`^${RUN_CREDENTIAL_PREFIX}[0-9a-f]{64}$`));
    for (const path of ['/v1/messages?beta=true', `${gate.url}/v1/messages?beta=true`]) {
      const answer = await toGate(gate.port, path, {
        ...presenting(CLAUDE_CODE_KEY_GATE, run),
        authorization: 'Bearer from-the-app',
      });
      assert.equal(answer.status, 200);
      assert.equal(answer.type, 'text/event-stream');
      const [first, last] = answer.body.split('\n\n');
      assert.equal(first, 'event: message_start');
      assert.deepEqual(JSON.parse(last ?? ''), {
        path: '/v1/messages?beta=true',
        key: REAL,
        authorization: null,
        version: '2023-06-01',
        beta: 'a-beta',
        host: `127.0.0.1:${port}`,
      });
    }
  });

  test("a gateway's own path comes before the request's", async (t) => {
    const api = echoUpstream();
    const port = await listening(api.server);
    const gate = await startKeyGate(CLAUDE_CODE_KEY_GATE, {
      ANTHROPIC_API_KEY: REAL,
      ANTHROPIC_BASE_URL: `http://127.0.0.1:${port}/gateway/anthropic/`,
    });
    t.after(async () => {
      await gate.close();
      api.server.close();
    });
    const answer = await toGate(
      gate.port,
      '/v1/messages',
      presenting(CLAUDE_CODE_KEY_GATE, gate.issue()),
    );
    assert.equal(
      JSON.parse(answer.body.split('\n\n')[1] ?? '').path,
      '/gateway/anthropic/v1/messages',
    );
  });

  for (const declaration of [CLAUDE_CODE_KEY_GATE, SECOND]) {
    test(`for ${declaration.keyVariable}: no credential, a wrong one, another gate's, an ended run's, and a foreign Host are refused before the key is touched (AUDIT3)`, async (t) => {
      const { api, gate } = await gated(t, declaration);
      const other = await gated(t, declaration);
      const run = gate.issue();
      const ended = gate.issue();
      gate.revoke(ended);
      const refusals: [string, Record<string, string>][] = [
        ['no credential', {}],
        ['the person key itself', presenting(declaration, REAL)],
        ['a wrong one', presenting(declaration, `${RUN_CREDENTIAL_PREFIX}${'0'.repeat(64)}`)],
        ["another gate's run", presenting(declaration, other.gate.issue())],
        ["an ended run's", presenting(declaration, ended)],
        [
          'a foreign Host',
          { ...presenting(declaration, run), host: `rebound.example.test:${gate.port}` },
        ],
        ['localhost as Host', { ...presenting(declaration, run), host: `localhost:${gate.port}` }],
      ];
      for (const [what, headers] of refusals) {
        const answer = await toGate(gate.port, '/v1/messages', headers);
        assert.equal(answer.status, 403, what);
        assert.equal(answer.body, '', what);
        assert.ok(!answer.body.includes(REAL), what);
      }
      assert.deepEqual(api.reached, [], 'a refused request reached the upstream');
      // The run's own credential passes, the key in the declared header alone.
      const passed = JSON.parse(
        (await toGate(gate.port, '/v1/messages', presenting(declaration, run))).body.split(
          '\n\n',
        )[1] ?? '',
      );
      assert.deepEqual(
        { key: passed.key, authorization: passed.authorization },
        declaration.bearer
          ? { key: null, authorization: `Bearer ${REAL}` }
          : { key: REAL, authorization: null },
      );
      // Ended, it is dead too; and a closed gate takes nothing.
      gate.revoke(run);
      assert.equal(
        (await toGate(gate.port, '/v1/messages', presenting(declaration, run))).status,
        403,
      );
      assert.equal(api.reached.length, 1);
    });
  }

  test('anything but a request to the gate is refused: a proxied request for another host, the upstream itself, and every tunnel', async (t) => {
    const { gate, port } = await gated(t, CLAUDE_CODE_KEY_GATE);
    const other = createServer((_req, res) => res.end('from elsewhere'));
    const elsewhere = await listening(other);
    t.after(() => other.close());
    assert.equal((await viaGate(gate.port, `http://127.0.0.1:${elsewhere}/`)).status, 403);
    assert.equal((await viaGate(gate.port, `http://127.0.0.1:${port}/v1/messages`)).status, 403);
    assert.equal((await viaGate(gate.port, `https://127.0.0.1:${gate.port}/`)).status, 403);
    assert.match(await tunnel(gate.port, `127.0.0.1:${port}`), / 403 /);
    assert.match(await tunnel(gate.port, 'api.anthropic.com:443'), / 403 /);
    assert.equal(apiAuthority(new URL('https://api.anthropic.com')), 'api.anthropic.com:443');
  });

  test("one mechanism, two declarations: Claude Code's and a second app that takes a Bearer key, each gated and its environment made from its declaration alone", async (t) => {
    for (const declaration of [CLAUDE_CODE_KEY_GATE, SECOND]) {
      const { gate } = await gated(t, declaration);
      const run = gate.issue();
      const agent = gatedEnvironment(declaration, gate.url, run, {
        [declaration.keyVariable]: REAL,
        [declaration.baseVariable]: 'https://elsewhere.example.test',
        ...Object.fromEntries(declaration.otherCredentials.map((name) => [name, REAL])),
        KEEP: 'kept',
      });
      assert.equal(agent[declaration.baseVariable], gate.url);
      assert.equal(agent[declaration.keyVariable], run);
      for (const name of declaration.otherCredentials) assert.equal(agent[name], undefined);
      assert.equal(agent.KEEP, 'kept');
      assert.ok(!Object.values(agent).includes(REAL));
      assert.equal(declaredUpstream(declaration, {}).href, new URL(declaration.upstream).href);
    }
    // No key where the declaration says: no gate.
    await assert.rejects(startKeyGate(SECOND, { OPENAI_BASE_URL: 'http://127.0.0.1:9' }));
  });

  test("a provider's declared header and the app's way of presenting are apart: the app's Bearer run credential in, the provider's own header out", async (t) => {
    const reached: Record<string, string | null>[] = [];
    const api = createServer((req, res) => {
      reached.push({
        apiKey: (req.headers['api-key'] as string | undefined) ?? null,
        authorization: req.headers.authorization ?? null,
      });
      res.end('{}');
    });
    const port = await listening(api);
    const gate = await startProviderGate(
      {
        id: 'third',
        name: 'A third host',
        upstream: `http://127.0.0.1:${port}/v1`,
        header: 'api-key',
        bearer: false,
        key: REAL,
        models: ['third-1'],
      },
      { header: 'authorization', bearer: true },
    );
    t.after(async () => {
      await gate.close();
      api.close();
    });
    const run = gate.issue();
    assert.equal(
      (await toGate(gate.port, '/chat/completions', { authorization: `Bearer ${run}` })).status,
      200,
    );
    assert.deepEqual(reached, [{ apiKey: REAL, authorization: null }]);
    // The run credential in the provider's header, not the app's way: refused, nothing reaches the upstream.
    assert.equal((await toGate(gate.port, '/chat/completions', { 'api-key': run })).status, 403);
    assert.equal(reached.length, 1);
  });

  test('a gate without a key, or with a header name that is not one, is never opened', async () => {
    const presented = { header: 'x-api-key', bearer: false };
    await assert.rejects(
      startApiGate(new URL('http://127.0.0.1:9'), { header: 'x-api-key', value: '' }, presented),
    );
    await assert.rejects(
      startApiGate(new URL('http://127.0.0.1:9'), { header: 'x api key', value: REAL }, presented),
    );
    await assert.rejects(
      startApiGate(new URL('ftp://127.0.0.1:9'), { header: 'x-api-key', value: REAL }, presented),
    );
  });
});

describe("a confined Claude Code's launch and store (BOX-4)", () => {
  test('runs the program through sandbox-exec with its own profile, home, configuration and temporary folder, and the gate as proxy', (t) => {
    const data = scratch(t);
    const project = join(data, 'project');
    mkdirSync(project);
    const folder = confinedFolder(
      { dataFolder: data, toolFolders: [], forDirectory: () => ({ readable: [] }) },
      project,
      { readable: [] },
    );
    const launched = confinedLaunch(
      {
        command: '/bin/echo',
        args: ['--session-id=abc'],
        cwd: project,
        env: {
          HOME: '/Users/someone',
          NO_PROXY: 'localhost',
          no_proxy: 'localhost',
          KEEP: 'kept',
          ANTHROPIC_API_KEY: REAL,
          ANTHROPIC_AUTH_TOKEN: 'a-bearer-token',
          ANTHROPIC_BASE_URL: 'https://gateway.example.test',
        },
        signal: new AbortController().signal,
      },
      folder,
      { toolFolders: [] },
      { url: 'http://127.0.0.1:41234', port: 41234 },
      CLAUDE_CODE_KEY_GATE,
      `${RUN_CREDENTIAL_PREFIX}${'a'.repeat(64)}`,
    );
    assert.equal(launched.command, SANDBOX_EXEC);
    assert.deepEqual(launched.args, ['-f', `${folder.root}.sb`, '/bin/echo', '--session-id=abc']);
    assert.equal(launched.cwd, project);
    assert.equal(launched.env.HOME, folder.home);
    assert.equal(launched.env.CLAUDE_CONFIG_DIR, join(folder.home, '.claude'));
    assert.equal(launched.env.CLAUDE_CODE_TMPDIR, folder.temp);
    assert.equal(launched.env.HTTPS_PROXY, 'http://127.0.0.1:41234');
    assert.equal(launched.env.HTTP_PROXY, 'http://127.0.0.1:41234');
    assert.equal(launched.env.NO_PROXY, undefined);
    assert.equal(launched.env.no_proxy, undefined);
    assert.equal(launched.env.KEEP, 'kept');
    // The agent holds no key: the gate is its API, and its key a placeholder that opens nothing.
    assert.equal(launched.env.ANTHROPIC_BASE_URL, 'http://127.0.0.1:41234');
    assert.equal(launched.env.ANTHROPIC_API_KEY, `${RUN_CREDENTIAL_PREFIX}${'a'.repeat(64)}`);
    assert.equal(launched.env.ANTHROPIC_AUTH_TOKEN, undefined);
    assert.ok(!Object.values(launched.env).includes(REAL));
    const profile = readFileSync(`${folder.root}.sb`, 'utf8');
    assert.match(profile, /\n\(deny default\)\n/);
    assert.ok(profile.includes('(remote ip "localhost:41234")'));
    assert.doesNotMatch(profile, /network-bind|network-inbound/);
    assert.equal(statSync(`${folder.root}.sb`).mode & 0o777, 0o600);
  });

  test("reads a session from the folder's own store, and nothing when it is not there", (t) => {
    const config = scratch(t);
    const id = '0f0e0d0c-0000-4000-8000-0000000000cc';
    assert.equal(findInConfinedStore(config, id), undefined);
    mkdirSync(join(config, 'projects', '-some-folder'), { recursive: true });
    assert.equal(findInConfinedStore(config, id), undefined);
    writeFileSync(
      join(config, 'projects', '-some-folder', `${id}.jsonl`),
      `${JSON.stringify({ type: 'summary' })}\n${JSON.stringify({ type: 'user', cwd: '/p/garden' })}\n`,
    );
    assert.deepEqual(findInConfinedStore(config, id), { cwd: '/p/garden' });
    assert.equal(findInConfinedStore(config, '0f0e0d0c-0000-4000-8000-0000000000dd'), undefined);
  });
});
