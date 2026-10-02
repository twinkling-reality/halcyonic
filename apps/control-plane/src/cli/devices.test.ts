import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type { PairingRefusalReason } from '@halcyonic/contracts';
import { PairingRefused, pairDevice } from '../testing/device.ts';
import { startTestServer, type TestServerOptions } from '../testing/harness.ts';
import { TlsWebSocket } from '../testing/tls-client.ts';
import { describeRefusal, printable, RefusalReport, runDevicesCli } from './devices.ts';

type Server = Awaited<ReturnType<typeof startTestServer>>;

async function start(t: TestContext, options: TestServerOptions = { network: {} }) {
  const server = await startTestServer(options);
  const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-cli-'));
  writeFileSync(join(dataDir, 'access-token'), `${server.token}\n`);
  t.after(async () => {
    await server.stop();
    rmSync(dataDir, { recursive: true, force: true });
  });
  return { server, env: { HALCYONIC_DATA_DIR: dataDir, HALCYONIC_PORT: String(server.port) } };
}

function run(
  env: NodeJS.ProcessEnv,
  args: string[],
  interrupted: Promise<void> = new Promise(() => {}),
) {
  const lines: string[] = [];
  const status = runDevicesCli(args, {
    env,
    print: (line) => lines.push(line),
    interrupted,
    pollMs: 10,
  });
  return { lines, status };
}

async function shownCode(lines: string[]): Promise<string> {
  for (let tries = 0; tries < 500; tries += 1) {
    const line = lines.find((candidate) => candidate.trim().startsWith('Code:'));
    if (line !== undefined) return line.replace(/\D/g, '');
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(`no code shown:\n${lines.join('\n')}`);
}

function target(server: Server) {
  assert.ok(server.network);
  return server.network.target;
}

describe('pnpm devices before it sends the token', () => {
  test('sends it only to a server that proves it holds it, never to whatever listens on the port', async (t) => {
    const authorizations: (string | undefined)[] = [];
    const impostor = createServer((request, response) => {
      authorizations.push(request.headers.authorization);
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(
        request.url === '/api/health' ? '{"status":"ok"}' : '{"devices":[],"connected":[]}',
      );
    });
    await new Promise<void>((resolve) => impostor.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise<void>((resolve) => impostor.close(() => resolve())));
    const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-cli-'));
    t.after(() => rmSync(dataDir, { recursive: true, force: true }));
    writeFileSync(join(dataDir, 'access-token'), `${'a'.repeat(43)}\n`);
    const port = (impostor.address() as AddressInfo).port;
    const { status } = run({ HALCYONIC_DATA_DIR: dataDir, HALCYONIC_PORT: String(port) }, ['list']);
    await assert.rejects(
      status,
      /can't prove it holds this Mac's access token, so the token was not sent/,
    );
    assert.ok(authorizations.length >= 1, 'it asked the health check');
    assert.ok(
      authorizations.every((value) => value === undefined),
      'no request carried the token',
    );
  });
});

describe('pnpm pair and pnpm devices', () => {
  test('pair shows the address and the code, tells of a refused code, and ends when a device pairs', async (t) => {
    const { server, env } = await start(t);
    const { lines, status } = run(env, ['pair']);
    const code = await shownCode(lines);
    assert.match(code, /^\d{8}$/);
    assert.ok(lines.some((line) => line.includes(`127.0.0.1:${target(server).port}`)));
    const wrong = ((Number(code) + 1) % 100_000_000).toString().padStart(8, '0');
    await assert.rejects(pairDevice(target(server), wrong), PairingRefused);
    const paired = await pairDevice(target(server), code, { label: 'Quest 3' });
    assert.equal(await status, 0);
    assert.ok(lines.includes('A code was refused (2 attempts left).'), lines.join('\n'));
    assert.ok(lines.includes(`Paired "Quest 3" as device ${paired.deviceId}.`), lines.join('\n'));
  });

  test('pair shows the connections it turned away or cut short, which spend no attempt', async (t) => {
    const { server, env } = await start(t);
    let stop = () => {};
    const interrupted = new Promise<void>((resolve) => {
      stop = resolve;
    });
    const { lines, status } = run(env, ['pair'], interrupted);
    await shownCode(lines);
    // Something holds an exchange open, so the next connection from the address is turned away.
    const holding = await TlsWebSocket.connect(target(server), '/pair');
    const turnedAway = await TlsWebSocket.connect(target(server), '/pair');
    await turnedAway.closed;
    await holding.close();
    const shown = async (line: string) => {
      for (let tries = 0; tries < 500 && !lines.includes(line); tries += 1) {
        await new Promise((resolve) => setTimeout(resolve, 10));
      }
      assert.ok(lines.includes(line), `${line}\n---\n${lines.join('\n')}`);
    };
    await shown(
      'Turned away 1 pairing connection from 127.0.0.1, because another exchange was in progress.',
    );
    await shown('1 pairing exchange from 127.0.0.1 closed before sending a code.');
    await shown(
      'If that was not your headset, something else on this network is holding pairing up. Ctrl-C closes pairing.',
    );
    assert.equal(lines.filter((line) => line.startsWith('If that was not')).length, 1);
    assert.equal(
      lines.some((line) => line.startsWith('A code was refused')),
      false,
      'no attempt was spent',
    );
    stop();
    assert.equal(await status, 130);
  });

  test('a refusal report prints each count once, and a tally that returns', () => {
    const lines: string[] = [];
    const report = new RefusalReport((line) => lines.push(line));
    const tally = (reason: PairingRefusalReason, count: number) => ({
      address: '192.168.1.50',
      reason,
      count,
      last_at: '2026-09-29T10:00:00.000Z',
    });
    report.report([tally('timeout', 1)]);
    report.report([tally('timeout', 1)]);
    report.report([tally('timeout', 3), tally('too_many_requests', 2)]);
    report.report([tally('unsupported_protocol', 1)]);
    // Dropped for newer tallies, it comes back counting from one.
    report.report([tally('timeout', 1)]);
    assert.deepEqual(lines, [
      'Ended 1 pairing exchange from 192.168.1.50 that sent no code in time.',
      'If that was not your headset, something else on this network is holding pairing up. Ctrl-C closes pairing.',
      'Ended 2 pairing exchanges from 192.168.1.50 that sent no code in time.',
      'Turned away 2 pairing connections from 192.168.1.50, which opened too many in a minute.',
      "Turned away 1 pairing connection from 192.168.1.50 that speaks another pairing protocol; update the headset's app or the control plane.",
      'Ended 1 pairing exchange from 192.168.1.50 that sent no code in time.',
    ]);
    assert.equal(
      describeRefusal('invalid_message', '192.168.1.50', 1),
      'Turned away 1 pairing connection from 192.168.1.50 that did not follow the pairing protocol.',
    );
  });

  test('Ctrl-C closes pairing, and the code no longer pairs', async (t) => {
    const { server, env } = await start(t);
    let stop = () => {};
    const interrupted = new Promise<void>((resolve) => {
      stop = resolve;
    });
    const { lines, status } = run(env, ['pair'], interrupted);
    const code = await shownCode(lines);
    stop();
    assert.equal(await status, 130);
    await assert.rejects(pairDevice(target(server), code));
  });

  test('pair says how to turn the network listener on when it is off', async (t) => {
    const { env } = await start(t, {});
    const { lines, status } = run(env, ['pair']);
    assert.equal(await status, 1);
    assert.match(lines.join('\n'), /HALCYONIC_NETWORK_HOST/);
  });

  test('devices lists paired devices and revokes one', async (t) => {
    const { server, env } = await start(t);
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as { code: string };
    const paired = await pairDevice(target(server), code, { label: 'Quest 3' });

    const listed = run(env, []);
    assert.equal(await listed.status, 0);
    assert.match(
      listed.lines.join('\n'),
      new RegExp(`${paired.deviceId} {2}"Quest 3" {2}paired .* {2}paired$`),
    );

    const revoked = run(env, ['revoke', paired.deviceId]);
    assert.equal(await revoked.status, 0);
    assert.equal(
      revoked.lines[0],
      `Revoked "Quest 3" (${paired.deviceId}); its connections are closed.`,
    );
    const after = run(env, ['list']);
    await after.status;
    assert.match(after.lines.join('\n'), /revoked /);

    const unknown = run(env, ['revoke', '01920000-0000-7000-8000-000000000000']);
    assert.equal(await unknown.status, 1);
    assert.match(unknown.lines.join('\n'), /was never paired/);
  });

  test('a label prints without the characters that could disguise it', () => {
    assert.equal(printable('Quest 3'), '"Quest 3"');
    assert.equal(printable('Quest\u009b31m‮3'), '"Quest31m3"');
  });
});
