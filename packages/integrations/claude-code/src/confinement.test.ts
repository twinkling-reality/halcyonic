import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createServer, request } from 'node:http';
import { type AddressInfo, connect, createServer as createTcpServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { SANDBOX_EXEC } from '@halcyonic/seatbelt';
import { apiAuthority, startApiGate } from './api-gate.ts';
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

describe("Claude Code's way to its API through the sandbox (BOX-4)", () => {
  test('an http API: absolute-form requests to it pass; any other host, and any CONNECT, are refused', async (t) => {
    const api = createServer((_req, res) => res.end('from the api'));
    const other = createServer((_req, res) => res.end('from elsewhere'));
    const port = await listening(api);
    const elsewhere = await listening(other);
    const gate = await startApiGate(new URL(`http://127.0.0.1:${port}`));
    t.after(async () => {
      await gate.close();
      api.close();
      other.close();
    });
    assert.deepEqual(await viaGate(gate.port, `http://127.0.0.1:${port}/v1/messages`), {
      status: 200,
      body: 'from the api',
    });
    assert.equal((await viaGate(gate.port, `http://127.0.0.1:${elsewhere}/`)).status, 403);
    assert.equal((await viaGate(gate.port, '/v1/messages')).status, 403);
    assert.match(await tunnel(gate.port, `127.0.0.1:${port}`), / 403 /);
  });

  test('an https API: a CONNECT to exactly its host and port is tunnelled; nothing else passes', async (t) => {
    const echo = createTcpServer((socket) => socket.pipe(socket));
    const port = await listening(echo);
    const gate = await startApiGate(new URL(`https://127.0.0.1:${port}`));
    t.after(async () => {
      await gate.close();
      echo.close();
    });
    assert.match(await tunnel(gate.port, `127.0.0.1:${port}`), / 200 /);
    assert.match(await tunnel(gate.port, `127.0.0.1:${port + 1}`), / 403 /);
    assert.match(await tunnel(gate.port, 'example.com:443'), / 403 /);
    assert.equal((await viaGate(gate.port, `http://127.0.0.1:${port}/`)).status, 403);
    assert.equal(apiAuthority(new URL('https://api.anthropic.com')), 'api.anthropic.com:443');
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
        env: { HOME: '/Users/someone', NO_PROXY: 'localhost', no_proxy: 'localhost', KEEP: 'kept' },
        signal: new AbortController().signal,
      },
      folder,
      { toolFolders: [] },
      { url: 'http://127.0.0.1:41234', port: 41234 },
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
