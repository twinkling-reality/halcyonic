import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { type AddressInfo, createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';

describe('adb for the device tools', () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-adb-'));
  after(() => rmSync(directory, { recursive: true, force: true }));

  test('a failure names the subcommand and how it ended, never what adb printed', async () => {
    // A stand-in that, as adb does, names the headset in what it prints, then fails.
    const fake = join(directory, 'adb');
    writeFileSync(
      fake,
      '#!/bin/sh\necho "device SERIAL-XYZ said HEADSET-OUTPUT"\necho "error: device SERIAL-XYZ not found" >&2\nexit 1\n',
    );
    chmodSync(fake, 0o755);
    process.env.HALCYONIC_ADB = fake;
    const { adb } = await import('./adb.ts');
    await assert.rejects(adb('shell', 'getprop', 'ro.product.model'), (error: unknown) => {
      assert.equal((error as Error).message, 'adb shell getprop failed (exit code 1)');
      const everything = JSON.stringify(error, Object.getOwnPropertyNames(error));
      assert.ok(
        !everything.includes('SERIAL-XYZ') && !everything.includes('HEADSET-OUTPUT'),
        everything,
      );
      return true;
    });
  });

  test("every command goes to the headset's own server and names the headset", async () => {
    // A stand-in that prints the arguments it was given, one per line.
    const fake = join(directory, 'adb-args');
    writeFileSync(fake, '#!/bin/sh\nfor a in "$@"; do echo "$a"; done\n');
    chmodSync(fake, 0o755);
    process.env.HALCYONIC_ADB = fake;
    process.env.HALCYONIC_QUEST_SERIAL = 'TEST-SERIAL-1';
    delete process.env.HALCYONIC_ADB_PORT;
    const { adb } = await import('./adb.ts');
    const said = (await adb('shell', 'pidof', 'com.example.app')).trim().split('\n');
    assert.deepEqual(said, [
      '-L',
      'tcp:127.0.0.1:5038',
      '-s',
      'TEST-SERIAL-1',
      'shell',
      'pidof',
      'com.example.app',
    ]);
    delete process.env.HALCYONIC_QUEST_SERIAL;
  });

  test("the port comes from HALCYONIC_ADB_PORT; the shared 5037 and Unity's 5039 are refused", async () => {
    const { serverPort } = await import('./adb.ts');
    assert.equal(serverPort({}), 5038);
    assert.equal(serverPort({ HALCYONIC_ADB_PORT: '5040' }), 5040);
    assert.throws(() => serverPort({ HALCYONIC_ADB_PORT: '5037' }), /5037/);
    assert.throws(() => serverPort({ HALCYONIC_ADB_PORT: '5039' }), /5039/);
    assert.throws(() => serverPort({ HALCYONIC_ADB_PORT: 'five' }), /HALCYONIC_ADB_PORT/);
  });

  test("the server's version is read with adb's own host:version query, and nothing else is sent", async () => {
    // adb restarts a server whose version differs from its own; the tools read it first, without adb.
    const { serverVersion } = await import('./adb.ts');
    const received: Buffer[] = [];
    const server = createServer((socket) => {
      socket.on('data', (chunk: Buffer) => {
        received.push(chunk);
        if (Buffer.concat(received).toString() === '000chost:version') socket.end('OKAY00040029');
      });
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const { port } = server.address() as AddressInfo;
    try {
      assert.equal(await serverVersion(port), 41);
      assert.equal(Buffer.concat(received).toString(), '000chost:version');
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
    assert.equal(await serverVersion(port), null);
  });

  test('a listener that does not answer as an adb server has no version', async () => {
    const { serverVersion } = await import('./adb.ts');
    // It reads what it is sent, so it sees the client close and can itself be closed.
    const server = createServer((socket) =>
      socket.resume().end('HTTP/1.1 400 Bad Request\r\n\r\n'),
    );
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const { port } = server.address() as AddressInfo;
    try {
      assert.equal(await serverVersion(port), null);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });

  test("adb's own version and the headset's transport id are read from what adb prints", async () => {
    const { readClientVersion, readTransportId } = await import('./adb.ts');
    assert.equal(
      readClientVersion('Android Debug Bridge version 1.0.41\nVersion 36.0.0-13206524\n'),
      41,
    );
    assert.equal(readClientVersion('adb: unknown command'), null);
    const listed =
      'List of devices attached\nOTHER device usb:1-2 transport_id:7\nTEST-SERIAL            device usb:1-1 product:eureka model:Quest_3 device:eureka transport_id:3\n';
    assert.equal(readTransportId(listed, 'TEST-SERIAL'), '3');
    assert.equal(readTransportId(listed, 'MISSING'), null);
    assert.equal(
      readTransportId('TEST-SERIAL unauthorized usb:1-1 transport_id:3', 'TEST-SERIAL'),
      null,
    );
  });

  test('a long command gets the time it is given, and is stopped after it', async () => {
    // A screenrecord clip runs for its whole length; adb's usual 20 s would cut it off.
    const fake = join(directory, 'adb-slow');
    writeFileSync(fake, '#!/bin/sh\nsleep 1\necho finished\n');
    chmodSync(fake, 0o755);
    process.env.HALCYONIC_ADB = fake;
    process.env.HALCYONIC_QUEST_SERIAL = 'TEST-SERIAL-3';
    try {
      const { adbFor } = await import('./adb.ts');
      await assert.rejects(
        adbFor(200, 'shell', 'screenrecord'),
        /^Error: adb shell screenrecord failed \(signal SIGTERM\)$/,
      );
      assert.equal((await adbFor(5000, 'shell', 'screenrecord')).trim(), 'finished');
    } finally {
      delete process.env.HALCYONIC_QUEST_SERIAL;
    }
  });

  test('a file goes to adb on standard input only, never on its command line', async () => {
    // The glance token reaches the headset this way: never in an argument, a variable or a log.
    const fake = join(directory, 'adb-input');
    const seen = join(directory, 'adb-input-seen');
    writeFileSync(fake, `#!/bin/sh\necho "$@" > ${seen}.args\ncat > ${seen}.input\n`);
    chmodSync(fake, 0o755);
    const input = join(directory, 'input-file');
    writeFileSync(input, 'bytes that stay off every command line', { mode: 0o600 });
    process.env.HALCYONIC_ADB = fake;
    process.env.HALCYONIC_QUEST_SERIAL = 'TEST-SERIAL-4';
    try {
      const { adbWithInput } = await import('./adb.ts');
      await adbWithInput(
        input,
        5000,
        'exec-in',
        "run-as com.example.app sh -c 'cat > files/x.tmp'",
      );
      assert.equal(readFileSync(`${seen}.input`, 'utf8'), 'bytes that stay off every command line');
      assert.ok(!readFileSync(`${seen}.args`, 'utf8').includes('stay off'));
      writeFileSync(fake, '#!/bin/sh\nexit 3\n');
      await assert.rejects(
        adbWithInput(input, 5000, 'exec-in', 'x'),
        /^Error: adb exec-in failed \(exit code 3\)$/,
      );
    } finally {
      delete process.env.HALCYONIC_QUEST_SERIAL;
    }
  });

  test('without HALCYONIC_QUEST_SERIAL the one headset the server lists is used', async () => {
    const { readSerial } = await import('./adb.ts');
    assert.equal(
      readSerial('List of devices attached\nTEST-SERIAL-2\tdevice\n\n'),
      'TEST-SERIAL-2',
    );
    assert.equal(readSerial('List of devices attached\n\n'), null);
    assert.equal(readSerial('List of devices attached\nTEST-SERIAL-2\tunauthorized\n'), null);
    assert.equal(readSerial('List of devices attached\nTEST-A\tdevice\nTEST-B\tdevice\n'), null);
  });
});
