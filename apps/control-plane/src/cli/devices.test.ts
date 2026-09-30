import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { PairingRefused, pairDevice } from '../testing/device.ts';
import { startTestServer, type TestServerOptions } from '../testing/harness.ts';
import { printable, runDevicesCli } from './devices.ts';

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
