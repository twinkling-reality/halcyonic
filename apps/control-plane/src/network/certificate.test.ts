import assert from 'node:assert/strict';
import { createHash, createPrivateKey, X509Certificate } from 'node:crypto';
import { mkdtempSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { connect, createServer, type TLSSocket } from 'node:tls';
import {
  createNetworkIdentity,
  loadOrCreateNetworkIdentity,
  NETWORK_CERTIFICATE_FILE,
  NETWORK_KEY_FILE,
} from './certificate.ts';

const NOW = new Date('2026-09-29T12:00:00.000Z');
const directory = mkdtempSync(join(tmpdir(), 'halcyonic-identity-'));
after(() => rmSync(directory, { recursive: true, force: true }));
const freshDirectory = () => mkdtempSync(join(directory, 'identity-'));

describe('the network TLS identity', () => {
  test('is a self-signed ECDSA P-256 server certificate that Node parses and verifies', () => {
    const identity = createNetworkIdentity(NOW);
    const certificate = new X509Certificate(identity.certificate);
    assert.equal(certificate.subject, 'CN=Halcyonic control plane');
    assert.equal(certificate.issuer, certificate.subject);
    assert.equal(certificate.ca, false);
    assert.ok(certificate.verify(certificate.publicKey), 'signed by its own key');
    assert.ok(certificate.checkPrivateKey(createPrivateKey(identity.key)));
    assert.equal(certificate.publicKey.asymmetricKeyType, 'ec');
    assert.equal(certificate.publicKey.asymmetricKeyDetails?.namedCurve, 'prime256v1');
    assert.deepEqual(certificate.keyUsage, ['1.3.6.1.5.5.7.3.1']);
    assert.equal(new Date(certificate.validFrom).toISOString(), '2026-09-28T12:00:00.000Z');
    assert.equal(new Date(certificate.validTo).toISOString(), '2036-09-29T12:00:00.000Z');
    assert.equal(
      identity.certificateSha256,
      createHash('sha256').update(certificate.raw).digest('hex'),
    );
    assert.equal(
      identity.certificateSha256,
      certificate.fingerprint256.replaceAll(':', '').toLowerCase(),
    );
  });

  test('is different every time, in its key and its serial', () => {
    const first = new X509Certificate(createNetworkIdentity(NOW).certificate);
    const second = new X509Certificate(createNetworkIdentity(NOW).certificate);
    assert.notEqual(first.serialNumber, second.serialNumber);
    assert.notEqual(first.fingerprint256, second.fingerprint256);
  });

  test('serves TLS, and a client sees exactly the certificate a device would pin', async () => {
    const identity = createNetworkIdentity(NOW);
    const server = createServer({ key: identity.key, cert: identity.certificate }, (socket) =>
      socket.end('ok'),
    );
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    assert.ok(address !== null && typeof address === 'object');
    try {
      const seen = await new Promise<string>((resolve, reject) => {
        const socket: TLSSocket = connect({
          host: '127.0.0.1',
          port: address.port,
          rejectUnauthorized: false,
          minVersion: 'TLSv1.2',
          maxVersion: 'TLSv1.2',
        });
        socket.on('secureConnect', () => {
          resolve(createHash('sha256').update(socket.getPeerCertificate().raw).digest('hex'));
          socket.destroy();
        });
        socket.on('error', reject);
      });
      assert.equal(seen, identity.certificateSha256);
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
  });

  test('is created once in the data directory, readable by its owner only, then reused', async () => {
    const dataDir = freshDirectory();
    const created = await loadOrCreateNetworkIdentity(dataDir, NOW);
    assert.equal(created.created, true);
    for (const file of [NETWORK_KEY_FILE, NETWORK_CERTIFICATE_FILE]) {
      assert.equal(statSync(join(dataDir, file)).mode & 0o777, 0o600, file);
    }
    const reused = await loadOrCreateNetworkIdentity(dataDir, new Date('2027-01-01T00:00:00.000Z'));
    assert.equal(reused.created, false);
    assert.deepEqual(reused.identity, created.identity);
  });

  test('a key without its certificate, or a mismatched pair, is an error, never replaced', async () => {
    const lonely = freshDirectory();
    writeFileSync(join(lonely, NETWORK_KEY_FILE), createNetworkIdentity(NOW).key);
    await assert.rejects(loadOrCreateNetworkIdentity(lonely, NOW), /only one of/);

    const mismatched = freshDirectory();
    writeFileSync(join(mismatched, NETWORK_KEY_FILE), createNetworkIdentity(NOW).key);
    writeFileSync(
      join(mismatched, NETWORK_CERTIFICATE_FILE),
      createNetworkIdentity(NOW).certificate,
    );
    await assert.rejects(loadOrCreateNetworkIdentity(mismatched, NOW), /does not match/);
  });
});
