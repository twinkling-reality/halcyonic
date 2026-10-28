import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
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
      'tcp:localhost:5038',
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
