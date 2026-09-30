import { createHash, createHmac, randomInt, timingSafeEqual } from 'node:crypto';
import { PAIRING_GROUP, pad } from './srp.ts';

/**
 * The pairing exchange's cryptography around SRP (ADR 0017), shared by the control plane and the
 * tests' device. The device's own is in C# (Halcyonic.Client.PairingCrypto); a vector file checks
 * that both compute the same.
 *
 * Both proofs are HMAC-SHA-256 under the SRP session key K, over a transcript of everything the
 * exchange depends on, including the SHA-256 of the TLS certificate as each side knows it: the
 * control plane its own, the device the one its connection presented. A relay that terminates TLS
 * presents another certificate, so the proofs fail even if it passes every message on unchanged.
 */

/** A device credential: 32 random bytes in base64url behind a prefix a secret scanner can match. */
export const CREDENTIAL_PREFIX = 'hlcd_';
export const CREDENTIAL_PATTERN = /^hlcd_[A-Za-z0-9_-]{43}$/;
export const CREDENTIAL_BYTES = 32;

/** Eight decimal digits, drawn uniformly. */
export function createPairingCode(): string {
  return randomInt(0, 100_000_000).toString().padStart(8, '0');
}

/** What SRP takes as the password: the code's eight ASCII digits. */
export function codePassword(code: string): Buffer {
  return Buffer.from(code, 'ascii');
}

export function credentialFromBytes(raw: Buffer): string {
  return `${CREDENTIAL_PREFIX}${raw.toString('base64url')}`;
}

/** What the journal keeps of a credential. */
export function credentialSha256(credential: string): string {
  return createHash('sha256').update(credential, 'utf8').digest('hex');
}

const DOMAIN = Buffer.from('halcyonic pairing 1\0', 'utf8');

/**
 * SHA-256 over the protocol's name, the device's label (length first), the salt, A and B padded to
 * the group's 384 bytes, and the certificate's SHA-256.
 */
export function pairingTranscript(
  label: string,
  salt: Buffer,
  A: bigint,
  B: bigint,
  certificateSha256: Buffer,
): Buffer {
  const labelBytes = Buffer.from(label, 'utf8');
  const labelLength = Buffer.alloc(4);
  labelLength.writeUInt32BE(labelBytes.length);
  return createHash('sha256')
    .update(DOMAIN)
    .update(labelLength)
    .update(labelBytes)
    .update(salt)
    .update(pad(PAIRING_GROUP, A))
    .update(pad(PAIRING_GROUP, B))
    .update(certificateSha256)
    .digest();
}

export function clientProof(key: Buffer, transcript: Buffer): Buffer {
  return mac(key, 'client proof', transcript);
}

/** Also covers the device id and the sealed credential the control plane sends with it. */
export function serverProof(
  key: Buffer,
  transcript: Buffer,
  proofOfClient: Buffer,
  deviceId: string,
  sealedCredential: Buffer,
): Buffer {
  return mac(
    key,
    'server proof',
    transcript,
    proofOfClient,
    Buffer.from(deviceId, 'utf8'),
    sealedCredential,
  );
}

/**
 * The credential's 32 bytes XORed with an HMAC under K: a one-time pad, since K is new for every
 * attempt. The TLS channel already protects it; this keeps it from anyone but the device that
 * holds K even if that channel did not.
 */
export function sealCredential(raw: Buffer, key: Buffer, transcript: Buffer): Buffer {
  const pad = mac(key, 'credential', transcript);
  if (raw.length !== pad.length) throw new RangeError('a credential is 32 bytes');
  return Buffer.from(raw.map((byte, index) => byte ^ (pad[index] ?? 0)));
}

/** The same operation opens it. */
export const openCredential = sealCredential;

/** Compares two MACs in time that does not depend on where they differ. */
export function sameMac(expected: Buffer, candidate: Buffer): boolean {
  return candidate.length === expected.length && timingSafeEqual(expected, candidate);
}

function mac(key: Buffer, label: string, ...parts: Buffer[]): Buffer {
  const hmac = createHmac('sha256', key).update(`${label}\0`, 'utf8');
  for (const part of parts) hmac.update(part);
  return hmac.digest();
}
