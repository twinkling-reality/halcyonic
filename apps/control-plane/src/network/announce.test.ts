import assert from 'node:assert/strict';
import type { ChildProcess } from 'node:child_process';
import { EventEmitter } from 'node:events';
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { createUuidV7Generator } from '../ids.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import {
  type Announcement,
  type Announcer,
  dnsSdAnnouncer,
  endLeftoverAnnouncement,
  reachableFromTheNetwork,
  SERVICE_TYPE,
} from './announce.ts';
import { DeviceAccess } from './devices.ts';
import { DEFAULT_PAIRING_LIMITS, Pairing } from './pairing.ts';

const ANNOUNCED: Announcement = { name: 'studio-mac', port: 47801, pinPrefix: '0123456789abcdef' };

/** A stand-in for spawn: records each start and whether it was ended. */
function spawned() {
  const runs: { command: string; args: readonly string[]; killed: boolean; child: EventEmitter }[] =
    [];
  const spawn = (command: string, args: readonly string[]) => {
    const child = new EventEmitter() as EventEmitter & {
      kill: (signal: string) => boolean;
      pid: number;
    };
    child.pid = 4000 + runs.length;
    const run = { command, args, killed: false, child };
    child.kill = () => {
      run.killed = true;
      return true;
    };
    runs.push(run);
    return child as unknown as ChildProcess;
  };
  return { runs, spawn };
}

describe('announcing the computer while pairing is open (ADR 0017)', () => {
  test("registers through macOS's own dns-sd, once, and ends the registration on stop", () => {
    const { runs, spawn } = spawned();
    const atExit: (() => void)[] = [];
    const announcer = dnsSdAnnouncer(() => ANNOUNCED, {
      spawn,
      platform: 'darwin',
      atExit: (end) => {
        atExit.push(end);
        return () => atExit.splice(atExit.indexOf(end), 1);
      },
    });
    announcer.start();
    announcer.start();
    assert.equal(runs.length, 1, 'already announcing, nothing more');
    assert.equal(atExit.length, 1, 'the process exiting ends the announcement');
    assert.equal(runs[0]?.command, '/usr/bin/dns-sd');
    assert.deepEqual(runs[0]?.args, [
      '-R',
      'studio-mac',
      SERVICE_TYPE,
      'local',
      '47801',
      'pin=0123456789abcdef',
    ]);
    announcer.stop();
    assert.equal(runs[0]?.killed, true);
    assert.equal(atExit.length, 0);
    announcer.stop();
    announcer.start();
    assert.equal(runs.length, 2, 'announced again after a stop');
    atExit[0]?.();
    assert.equal(runs[1]?.killed, true, 'exiting with a window open ends it');
  });

  test('announces nothing off macOS, or with nothing to describe, or once the tool has gone', () => {
    const { runs, spawn } = spawned();
    dnsSdAnnouncer(() => ANNOUNCED, { spawn, platform: 'linux' }).start();
    dnsSdAnnouncer(() => null, { spawn, platform: 'darwin' }).start();
    assert.equal(runs.length, 0);
    const announcer = dnsSdAnnouncer(() => ANNOUNCED, { spawn, platform: 'darwin' });
    announcer.start();
    runs[0]?.child.emit('exit', 1);
    announcer.start();
    assert.equal(runs.length, 2, 'a tool that ended is started again at the next window');
  });
});

test('a listener only this computer can reach is never announced', () => {
  for (const host of ['0.0.0.0', '::', '192.168.1.20', 'fe80::1'])
    assert.equal(reachableFromTheNetwork(host), true, host);
  for (const host of ['127.0.0.1', '127.0.0.2', '::1', '::ffff:127.0.0.1'])
    assert.equal(reachableFromTheNetwork(host), false, host);
});

describe('an announcement a killed control plane left behind', () => {
  const folder = () => mkdtempSync(join(tmpdir(), 'halcyonic-announce-'));
  const LINE = `/usr/bin/dns-sd -R studio-mac ${SERVICE_TYPE} local 47801 pin=0123456789abcdef`;

  test('the running dns-sd is recorded while it announces, and the record goes with it', () => {
    const record = join(folder(), 'announcing.pid');
    const { runs, spawn } = spawned();
    const announcer = dnsSdAnnouncer(() => ANNOUNCED, {
      spawn,
      platform: 'darwin',
      atExit: () => () => {},
      record,
    });
    announcer.start();
    assert.equal(readFileSync(record, 'utf8'), '4000');
    announcer.stop();
    assert.equal(runs[0]?.killed, true);
    assert.equal(existsSync(record), false);
  });

  test('the next start ends it, only while it is still a dns-sd registering this service', async () => {
    for (const [line, ended] of [
      [LINE, true],
      ['/usr/bin/dns-sd -R someone-else _other._tcp local 80', false],
      ['/usr/bin/vim notes.txt', false],
      [null, false],
    ] as const) {
      const record = join(folder(), 'announcing.pid');
      writeFileSync(record, '4321');
      const signalled: number[] = [];
      await endLeftoverAnnouncement(record, {
        commandLine: async (pid) => (pid === 4321 ? line : null),
        kill: (pid) => signalled.push(pid),
      });
      assert.deepEqual(signalled, ended ? [4321] : [], String(line));
      assert.equal(existsSync(record), false, 'the record goes either way');
    }
  });

  test('no record or a record that names no process is no failure', async () => {
    const signalled: number[] = [];
    const kill = (pid: number) => signalled.push(pid);
    await endLeftoverAnnouncement(join(folder(), 'absent.pid'), { kill });
    const garbled = join(folder(), 'announcing.pid');
    writeFileSync(garbled, 'not a pid');
    await endLeftoverAnnouncement(garbled, { commandLine: async () => LINE, kill });
    assert.deepEqual(signalled, []);
    assert.equal(existsSync(garbled), false);
  });
});

describe('the pairing window decides when the computer is announced', () => {
  function pairingWith(windowMs = DEFAULT_PAIRING_LIMITS.windowMs) {
    const calls: string[] = [];
    const announcer: Announcer = {
      start: () => calls.push('start'),
      stop: () => calls.push('stop'),
    };
    const harness = createTestControlPlane();
    const { logger } = capturingLogger();
    const pairing = new Pairing({
      clock: harness.time,
      devices: new DeviceAccess({
        controlPlane: harness.controlPlane,
        ids: createUuidV7Generator({ now: () => harness.time.now().getTime() }),
        logger,
      }),
      describe: (deviceId) => harness.controlPlane.projection.device(deviceId),
      certificateSha256: 'ab'.repeat(32),
      logger,
      limits: { ...DEFAULT_PAIRING_LIMITS, windowMs },
      announcer,
    });
    return { pairing, calls };
  }

  test('only while a window is open: on opening, and never before', () => {
    const { pairing, calls } = pairingWith();
    assert.deepEqual(calls, [], 'a pairing made announces nothing');
    pairing.open();
    assert.deepEqual(calls, ['start']);
    pairing.close();
    assert.deepEqual(calls, ['start', 'stop']);
    pairing.close();
    assert.deepEqual(calls, ['start', 'stop'], 'closed already, nothing more');
  });

  test('a window replaced ends its announcement and starts the next', () => {
    const { pairing, calls } = pairingWith();
    pairing.open();
    pairing.open();
    assert.deepEqual(calls, ['start', 'stop', 'start']);
    pairing.close();
  });

  test("a window whose time runs out stops announcing with nobody's press", async () => {
    const { pairing, calls } = pairingWith(50);
    pairing.open();
    await new Promise((resolve) => setTimeout(resolve, 120));
    assert.deepEqual(calls, ['start', 'stop']);
  });
});
