import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { DeviceId } from '@halcyonic/contracts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const NO_SCOPE = { project_id: null, workstream_id: null, execution_id: null } as const;
const CERTIFICATE = 'c'.repeat(64);

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const paired = (credentialSha256: string, label = 'Quest 3') => {
    const deviceId = b.id() as DeviceId;
    const result = projection.apply(
      b.controlPlane('device.paired', NO_SCOPE, {
        device_id: deviceId,
        label,
        credential_sha256: credentialSha256,
        certificate_sha256: CERTIFICATE,
      }),
    );
    return { deviceId, result };
  };
  const revoked = (deviceId: DeviceId) =>
    projection.apply(
      b.controlPlane('device.revoked', NO_SCOPE, {
        device_id: deviceId,
        revoked_by: { kind: 'local' },
      }),
    );
  return { projection, paired, revoked };
}

describe('the device registry', () => {
  test('a paired device is known by its credential, and changes no entity clients hold', () => {
    const { projection, paired } = setup();
    const { deviceId, result } = paired('a'.repeat(64));
    assert.deepEqual(result.changes, {
      projects: [],
      workstreams: [],
      executions: [],
      commands: [],
    });
    assert.deepEqual(result.notes, []);
    const device = projection.deviceForCredential('a'.repeat(64));
    assert.equal(device?.device_id, deviceId);
    assert.equal(device?.label, 'Quest 3');
    assert.equal(device?.certificate_sha256, CERTIFICATE);
    assert.equal(device?.revoked_at, null);
    assert.equal(projection.deviceForCredential('b'.repeat(64)), undefined);
    assert.deepEqual(
      projection.devices().map((view) => view.device_id),
      [deviceId],
    );
  });

  test('a revoked device stays known, so its credential is refused as revoked', () => {
    const { projection, paired, revoked } = setup();
    const { deviceId } = paired('a'.repeat(64));
    assert.deepEqual(revoked(deviceId).notes, []);
    const device = projection.deviceForCredential('a'.repeat(64));
    assert.equal(device?.device_id, deviceId);
    assert.ok(device?.revoked_at);
    assert.equal(projection.device(deviceId)?.revoked_at, device?.revoked_at);
  });

  test('what the journal holds out of order is noted and changes nothing', () => {
    const { projection, paired, revoked } = setup();
    const { deviceId } = paired('a'.repeat(64));
    assert.equal(
      revoked('01920000-0000-7000-8000-00000000ffff' as DeviceId).notes[0]?.code,
      'unknown_entity',
    );
    revoked(deviceId);
    const firstRevocation = projection.device(deviceId)?.revoked_at;
    assert.equal(revoked(deviceId).notes[0]?.code, 'duplicate_entity');
    assert.equal(projection.device(deviceId)?.revoked_at, firstRevocation);
    assert.equal(paired('a'.repeat(64)).result.notes[0]?.code, 'duplicate_entity');
    assert.equal(projection.devices().length, 1);
  });
});
