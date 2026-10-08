import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, symlinkSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { probeNetwork } from './network-probe.ts';

test('the probe watches a process running the binary that is not below its root', async (t) => {
  const server = createServer((socket) => socket.on('error', () => undefined));
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  const port = (server.address() as { port: number }).port;
  // The root has no descendants; the client runs the binary, started by this test, not by it.
  const root = spawn('/bin/sleep', ['60'], { stdio: 'ignore' });
  // A binary path of the test's own, a link to node in a temporary folder, so the probe matches
  // only this test's client, not every node process on the Mac.
  const folder = mkdtempSync(join(tmpdir(), 'halcyonic-probe-'));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  const binary = join(folder, 'codex-stand-in');
  symlinkSync(process.execPath, binary);
  const client = spawn(
    binary,
    ['-e', `require('node:net').connect(${port}, '127.0.0.1'); setInterval(() => {}, 1000);`],
    { stdio: 'ignore' },
  );
  t.after(async () => {
    root.kill();
    client.kill();
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });
  assert.ok(root.pid !== undefined);
  const probe = probeNetwork(root.pid, binary, 100);
  try {
    const deadline = Date.now() + 10_000;
    while (![...probe.loopback].some((name) => name.endsWith(`->127.0.0.1:${port}`))) {
      assert.ok(
        Date.now() < deadline,
        `the client's connection was not seen: ${[...probe.loopback]}`,
      );
      await delay(50);
    }
  } finally {
    await probe.stop();
  }
});
