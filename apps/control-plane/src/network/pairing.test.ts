import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { test } from 'node:test';
import { PAIRING_PROTOCOL_VERSION } from '@halcyonic/contracts';
import { createUuidV7Generator } from '../ids.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import { DeviceAccess } from './devices.ts';
import { Pairing, type PairingAttempt } from './pairing.ts';
import { clientProof, codePassword, pairingTranscript } from './pairing-protocol.ts';
import { fromBytes, PAIRING_GROUP, PAIRING_IDENTITY, pad, SrpClient } from './srp.ts';

const CERTIFICATE = 'ab'.repeat(32);

function window() {
  const harness = createTestControlPlane();
  const { logger } = capturingLogger();
  const devices = new DeviceAccess({
    controlPlane: harness.controlPlane,
    ids: createUuidV7Generator({ now: () => harness.time.now().getTime() }),
    logger,
  });
  const pairing = new Pairing({
    clock: harness.time,
    devices,
    describe: (deviceId) => harness.controlPlane.projection.device(deviceId),
    certificateSha256: CERTIFICATE,
    logger,
  });
  return { harness, pairing, code: pairing.open().code };
}

/** An exchange from `address` up to its challenge, and the proof a device knowing `code` would send. */
function challenged(pairing: Pairing, address: string, code: string) {
  const attempt = pairing.begin(address) as PairingAttempt;
  assert.ok('receive' in attempt, `${address} began`);
  const label = `Device at ${address}`;
  const answer = attempt.receive({
    type: 'pair_request',
    protocol: PAIRING_PROTOCOL_VERSION,
    device_label: label,
  });
  assert.equal(answer.reply.type, 'pair_challenge');
  if (answer.reply.type !== 'pair_challenge') throw new Error('no challenge');
  const salt = Buffer.from(answer.reply.salt, 'base64');
  const B = fromBytes(Buffer.from(answer.reply.server_public, 'base64'));
  const client = new SrpClient(
    PAIRING_GROUP,
    PAIRING_IDENTITY,
    codePassword(code),
    fromBytes(randomBytes(32)),
  );
  const key = client.sessionKey(salt, B);
  if (key === null) throw new Error('invalid B');
  const transcript = pairingTranscript(label, salt, client.A, B, Buffer.from(CERTIFICATE, 'hex'));
  const prove = () =>
    attempt.receive({
      type: 'pair_proof',
      client_public: pad(PAIRING_GROUP, client.A).toString('base64'),
      proof: clientProof(key, transcript).toString('base64'),
    }).reply;
  return { prove };
}

const wrong = (code: string) => ((Number(code) + 1) % 100_000_000).toString().padStart(8, '0');

test('exchanges under way when the third failure locks the window get no guess, the right code included (#12)', () => {
  const { harness, pairing, code } = window();
  // Two exchanges reach their challenge first: one with the right code, one with a wrong one.
  const owner = challenged(pairing, '10.0.0.2', code);
  const late = challenged(pairing, '10.0.0.3', wrong(code));
  for (const address of ['10.0.0.4', '10.0.0.5', '10.0.0.6']) {
    const reply = challenged(pairing, address, wrong(code)).prove();
    assert.equal(reply.type, 'pair_refused');
  }
  assert.equal(pairing.status().state, 'locked');
  assert.equal(pairing.status().failed_attempts, 3);

  // Their proofs arrive after the lock: neither is judged, so neither counts nor pairs.
  for (const reply of [late.prove(), owner.prove()]) {
    assert.equal(reply.type, 'pair_refused');
    if (reply.type === 'pair_refused') assert.equal(reply.error.code, 'pairing_closed');
  }
  assert.equal(pairing.status().failed_attempts, 3);
  assert.deepEqual(harness.controlPlane.projection.devices(), []);
});

test('the right code pairs while the window is open, beside an exchange still under way (#12)', () => {
  const { harness, pairing, code } = window();
  const other = challenged(pairing, '10.0.0.3', wrong(code));
  const reply = challenged(pairing, '10.0.0.2', code).prove();
  assert.equal(reply.type, 'pair_accepted');
  assert.equal(harness.controlPlane.projection.devices().length, 1);
  // The window closed on pairing, so the other exchange's proof is not judged.
  const after = other.prove();
  assert.equal(after.type === 'pair_refused' ? after.error.code : after.type, 'pairing_closed');
  assert.equal(harness.controlPlane.projection.devices().length, 1);
});
