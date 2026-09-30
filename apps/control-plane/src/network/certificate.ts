import {
  createHash,
  createPrivateKey,
  generateKeyPairSync,
  type KeyObject,
  randomBytes,
  sign,
  X509Certificate,
} from 'node:crypto';
import { chmod, readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

export const NETWORK_KEY_FILE = 'network-key.pem';
export const NETWORK_CERTIFICATE_FILE = 'network-certificate.pem';

/**
 * The network listener's TLS identity (ADR 0017). Nothing on a LAN can get a certificate from a
 * public authority, so the control plane signs its own, and a device pins its SHA-256 when it
 * pairs. Anyone who can read the key can impersonate the control plane to paired devices, so it is
 * readable by its owner only and never logged.
 */
export interface NetworkIdentity {
  /** PKCS #8 PEM. */
  readonly key: string;
  readonly certificate: string;
  /** SHA-256 of the certificate's DER, in lowercase hex: what devices pin. */
  readonly certificateSha256: string;
}

/** The certificate's subject and issuer. */
const COMMON_NAME = 'Halcyonic control plane';
const VALIDITY_YEARS = 10;

/**
 * A self-signed ECDSA P-256 certificate for TLS servers: version 3, a random serial, not a
 * certificate authority, for digital signatures and server authentication, valid from a day before
 * `now` for ten years. Devices pin it, so its names and dates only need to parse; clocks that
 * disagree about dates do not affect pinning.
 */
export function createNetworkIdentity(now: Date): NetworkIdentity {
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const serial = randomBytes(16);
  serial[0] = ((serial[0] ?? 0) & 0x7f) | 0x40;
  const notBefore = new Date(now.getTime() - 24 * 60 * 60 * 1000);
  const notAfter = new Date(now);
  notAfter.setUTCFullYear(notAfter.getUTCFullYear() + VALIDITY_YEARS);
  const name = sequence(set(sequence(oid('2.5.4.3'), utf8(COMMON_NAME))));
  const tbs = sequence(
    explicit(0, integer(Buffer.from([2]))),
    integer(serial),
    ECDSA_WITH_SHA256,
    name,
    sequence(time(notBefore), time(notAfter)),
    name,
    publicKey.export({ type: 'spki', format: 'der' }),
    explicit(
      3,
      sequence(
        // Basic constraints, critical: not a certificate authority.
        sequence(oid('2.5.29.19'), boolean(true), octetString(sequence())),
        // Key usage, critical: digital signature only.
        sequence(oid('2.5.29.15'), boolean(true), octetString(tlv(0x03, Buffer.from([7, 0x80])))),
        // Extended key usage: TLS server authentication.
        sequence(oid('2.5.29.37'), octetString(sequence(oid('1.3.6.1.5.5.7.3.1')))),
      ),
    ),
  );
  const signature = sign('sha256', tbs, privateKey);
  const der = sequence(tbs, ECDSA_WITH_SHA256, bitString(signature));
  return {
    key: privateKey.export({ type: 'pkcs8', format: 'pem' }).toString(),
    certificate: pem('CERTIFICATE', der),
    certificateSha256: createHash('sha256').update(der).digest('hex'),
  };
}

/**
 * Reads the identity from the data directory, or creates it there on first use. A key without its
 * certificate, or a pair that does not match, is an error rather than a silent replacement:
 * replacing the identity makes every paired device refuse the control plane until it pairs again.
 */
export async function loadOrCreateNetworkIdentity(
  dataDir: string,
  now: Date,
): Promise<{ identity: NetworkIdentity; created: boolean }> {
  const keyPath = join(dataDir, NETWORK_KEY_FILE);
  const certificatePath = join(dataDir, NETWORK_CERTIFICATE_FILE);
  const [key, certificate] = await Promise.all([
    readOptional(keyPath),
    readOptional(certificatePath),
  ]);
  if (key === null && certificate === null) {
    const identity = createNetworkIdentity(now);
    await writeFile(keyPath, identity.key, { mode: 0o600, flag: 'wx' });
    await writeFile(certificatePath, identity.certificate, { mode: 0o600, flag: 'wx' });
    return { identity, created: true };
  }
  if (key === null || certificate === null) {
    throw new Error(
      `${dataDir} holds only one of ${NETWORK_KEY_FILE} and ${NETWORK_CERTIFICATE_FILE}; delete the other to create a new identity, which every paired device must then pair with again`,
    );
  }
  await Promise.all([chmod(keyPath, 0o600), chmod(certificatePath, 0o600)]);
  let parsed: X509Certificate;
  let privateKey: KeyObject;
  try {
    parsed = new X509Certificate(certificate);
    privateKey = createPrivateKey(key);
  } catch {
    throw new Error(
      `${NETWORK_KEY_FILE} or ${NETWORK_CERTIFICATE_FILE} in ${dataDir} cannot be read`,
    );
  }
  if (!parsed.checkPrivateKey(privateKey)) {
    throw new Error(`${NETWORK_KEY_FILE} does not match ${NETWORK_CERTIFICATE_FILE} in ${dataDir}`);
  }
  return {
    identity: {
      key,
      certificate,
      certificateSha256: createHash('sha256').update(parsed.raw).digest('hex'),
    },
    created: false,
  };
}

async function readOptional(path: string): Promise<string | null> {
  try {
    return await readFile(path, 'utf8');
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
    throw error;
  }
}

// DER, as much of it as a certificate needs (ITU-T X.690) ---------------------------------------

const ECDSA_WITH_SHA256 = sequence(oid('1.2.840.10045.4.3.2'));

function tlv(tag: number, content: Buffer): Buffer {
  const length = content.length;
  if (length < 0x80) return Buffer.concat([Buffer.from([tag, length]), content]);
  const octets: number[] = [];
  for (let rest = length; rest > 0; rest = Math.floor(rest / 256)) octets.unshift(rest % 256);
  return Buffer.concat([Buffer.from([tag, 0x80 | octets.length, ...octets]), content]);
}

function sequence(...items: Buffer[]): Buffer {
  return tlv(0x30, Buffer.concat(items));
}

function set(...items: Buffer[]): Buffer {
  return tlv(0x31, Buffer.concat(items));
}

function explicit(tagNumber: number, content: Buffer): Buffer {
  return tlv(0xa0 + tagNumber, content);
}

/** A non-negative INTEGER from big-endian bytes, minimally encoded. */
function integer(value: Buffer): Buffer {
  let start = 0;
  while (start < value.length - 1 && value[start] === 0) start += 1;
  const minimal = value.subarray(start);
  const negative = ((minimal[0] ?? 0) & 0x80) !== 0;
  return tlv(0x02, negative ? Buffer.concat([Buffer.from([0]), minimal]) : Buffer.from(minimal));
}

function boolean(value: boolean): Buffer {
  return tlv(0x01, Buffer.from([value ? 0xff : 0]));
}

function octetString(content: Buffer): Buffer {
  return tlv(0x04, content);
}

function bitString(content: Buffer): Buffer {
  return tlv(0x03, Buffer.concat([Buffer.from([0]), content]));
}

function utf8(text: string): Buffer {
  return tlv(0x0c, Buffer.from(text, 'utf8'));
}

function oid(dotted: string): Buffer {
  const [first = 0, second = 0, ...rest] = dotted.split('.').map(Number);
  const octets = [first * 40 + second];
  for (const arc of rest) {
    const base128 = [arc & 0x7f];
    for (let value = Math.floor(arc / 128); value > 0; value = Math.floor(value / 128)) {
      base128.unshift((value & 0x7f) | 0x80);
    }
    octets.push(...base128);
  }
  return tlv(0x06, Buffer.from(octets));
}

/** UTCTime until 2049, GeneralizedTime from 2050 (RFC 5280 section 4.1.2.5). */
function time(instant: Date): Buffer {
  const iso = instant.toISOString();
  const digits = `${iso.slice(0, 4)}${iso.slice(5, 7)}${iso.slice(8, 10)}${iso.slice(11, 13)}${iso.slice(14, 16)}${iso.slice(17, 19)}Z`;
  return instant.getUTCFullYear() < 2050
    ? tlv(0x17, Buffer.from(digits.slice(2), 'ascii'))
    : tlv(0x18, Buffer.from(digits, 'ascii'));
}

function pem(label: string, der: Buffer): string {
  const lines = der.toString('base64').match(/.{1,64}/g) ?? [];
  return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----\n`;
}
