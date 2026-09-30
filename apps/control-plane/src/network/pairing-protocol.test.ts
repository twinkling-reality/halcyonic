import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  CREDENTIAL_PATTERN,
  clientProof,
  codePassword,
  createPairingCode,
  credentialFromBytes,
  credentialSha256,
  openCredential,
  pairingTranscript,
  sameMac,
  sealCredential,
  serverProof,
} from './pairing-protocol.ts';
import {
  fromBytes,
  multiplier,
  PAIRING_GROUP,
  PAIRING_IDENTITY,
  pad,
  SrpClient,
  SrpServer,
  scramble,
  verifier,
} from './srp.ts';

/** Shared with the C# client core's tests (Halcyonic.Client.Tests/PairingTests.cs). */
const VECTORS = JSON.parse(
  readFileSync(new URL('../../../../fixtures/pairing/vectors.json', import.meta.url), 'utf8'),
) as {
  inputs: Record<string, string>;
  expected: Record<string, string>;
};

const base64 = (text: string | undefined) => Buffer.from(text ?? '', 'base64');
const hex = (text: string | undefined) => Buffer.from(text ?? '', 'hex');

describe('the pairing exchange', () => {
  test('computes the shared vectors the C# client core checks too', () => {
    const { inputs, expected } = VECTORS;
    const password = codePassword(inputs.code ?? '');
    const salt = base64(inputs.salt);
    const v = verifier(PAIRING_GROUP, PAIRING_IDENTITY, password, salt);
    const server = new SrpServer(PAIRING_GROUP, v, fromBytes(hex(inputs.b)));
    const client = new SrpClient(
      PAIRING_GROUP,
      PAIRING_IDENTITY,
      password,
      fromBytes(hex(inputs.a)),
    );
    const key = client.sessionKey(salt, server.B);
    assert.ok(key);
    assert.deepEqual(server.sessionKey(client.A), key);
    const certificate = hex(inputs.certificate_sha256);
    const transcript = pairingTranscript(inputs.label ?? '', salt, client.A, server.B, certificate);
    const proof = clientProof(key, transcript);
    const sealed = sealCredential(base64(inputs.credential), key, transcript);
    const credential = credentialFromBytes(base64(inputs.credential));
    assert.deepEqual(
      {
        k: multiplier(PAIRING_GROUP).toString(16),
        verifier: pad(PAIRING_GROUP, v).toString('base64'),
        client_public: pad(PAIRING_GROUP, client.A).toString('base64'),
        server_public: pad(PAIRING_GROUP, server.B).toString('base64'),
        scrambler: scramble(PAIRING_GROUP, client.A, server.B).toString(16),
        session_key: key.toString('base64'),
        transcript: transcript.toString('base64'),
        client_proof: proof.toString('base64'),
        sealed_credential: sealed.toString('base64'),
        server_proof: serverProof(key, transcript, proof, inputs.device_id ?? '', sealed).toString(
          'base64',
        ),
        credential,
        credential_sha256: credentialSha256(credential),
      },
      expected,
    );
  });

  test('a sealed credential opens only with the same key and transcript', () => {
    const raw = randomBytes(32);
    const key = randomBytes(32);
    const transcript = randomBytes(32);
    const sealed = sealCredential(raw, key, transcript);
    assert.notDeepEqual(sealed, raw);
    assert.deepEqual(openCredential(sealed, key, transcript), raw);
    assert.notDeepEqual(openCredential(sealed, randomBytes(32), transcript), raw);
  });

  test('credentials and codes have their documented forms', () => {
    assert.match(credentialFromBytes(randomBytes(32)), CREDENTIAL_PATTERN);
    for (let i = 0; i < 50; i += 1) assert.match(createPairingCode(), /^\d{8}$/);
    assert.equal(sameMac(Buffer.alloc(32, 1), Buffer.alloc(31, 1)), false);
    assert.equal(sameMac(Buffer.alloc(32, 1), Buffer.alloc(32, 1)), true);
  });
});
