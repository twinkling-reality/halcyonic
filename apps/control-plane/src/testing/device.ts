/**
 * Test support: a device that pairs over the network listener as the XR client does (ADR 0017),
 * with ways to misbehave for the hostile cases. Not used by the running control plane.
 */
import { randomBytes } from 'node:crypto';
import { PAIRING_PROTOCOL_VERSION } from '@halcyonic/contracts';
import {
  clientProof,
  codePassword,
  credentialFromBytes,
  openCredential,
  pairingTranscript,
  sameMac,
  serverProof,
} from '../network/pairing-protocol.ts';
import { fromBytes, PAIRING_GROUP, PAIRING_IDENTITY, pad, SrpClient } from '../network/srp.ts';
import { type TlsTarget, TlsWebSocket } from './tls-client.ts';

export interface PairedDevice {
  readonly deviceId: string;
  readonly credential: string;
  /** What the device pins: the certificate its pairing connection presented. */
  readonly certificateSha256: string;
  /** The messages the device sent, for replaying them. */
  readonly sent: readonly Record<string, unknown>[];
}

export class PairingRefused extends Error {
  readonly code: string;
  readonly attemptsLeft: number | null;

  constructor(code: string, message: string, attemptsLeft: number | null) {
    super(`${code}: ${message}`);
    this.code = code;
    this.attemptsLeft = attemptsLeft;
  }
}

export interface PairOptions {
  readonly label?: string;
  /**
   * The certificate the device binds into its proof instead of the one it saw, as a device behind
   * a relay that terminates TLS would, since it sees the relay's certificate.
   */
  readonly certificateSha256?: string;
  /** The Host header, when connecting through a relay that passes it on unchanged. */
  readonly host?: string;
}

/** Pairs through `/pair` with the code; throws PairingRefused when the control plane refuses. */
export async function pairDevice(
  target: TlsTarget,
  code: string,
  options: PairOptions = {},
): Promise<PairedDevice> {
  const label = options.label ?? 'Test headset';
  const socket = await TlsWebSocket.connect(target, '/pair', {
    ...(options.host === undefined ? {} : { headers: { host: options.host } }),
  });
  const sent: Record<string, unknown>[] = [];
  const send = (message: Record<string, unknown>) => {
    sent.push(message);
    socket.sendJson(message);
  };
  try {
    send({ type: 'pair_request', protocol: PAIRING_PROTOCOL_VERSION, device_label: label });
    const challenge = await socket.message(0);
    refuseOn(challenge);
    const salt = Buffer.from(String(challenge.salt), 'base64');
    const B = fromBytes(Buffer.from(String(challenge.server_public), 'base64'));
    const client = new SrpClient(
      PAIRING_GROUP,
      PAIRING_IDENTITY,
      codePassword(code),
      fromBytes(randomBytes(32)),
    );
    const key = client.sessionKey(salt, B);
    if (key === null) throw new Error('the control plane sent an invalid B');
    const certificate = options.certificateSha256 ?? socket.certificateSha256;
    const transcript = pairingTranscript(label, salt, client.A, B, Buffer.from(certificate, 'hex'));
    const proof = clientProof(key, transcript);
    send({
      type: 'pair_proof',
      client_public: pad(PAIRING_GROUP, client.A).toString('base64'),
      proof: proof.toString('base64'),
    });
    const answer = await socket.message(1);
    refuseOn(answer);
    const deviceId = String(answer.device_id);
    const sealed = Buffer.from(String(answer.credential), 'base64');
    const expected = serverProof(key, transcript, proof, deviceId, sealed);
    if (!sameMac(expected, Buffer.from(String(answer.proof), 'base64'))) {
      throw new Error("the control plane's proof does not hold");
    }
    return {
      deviceId,
      credential: credentialFromBytes(openCredential(sealed, key, transcript)),
      certificateSha256: socket.certificateSha256,
      sent,
    };
  } finally {
    socket.close().catch(() => {});
  }
}

/** Sends recorded messages on a new connection and returns what the control plane answered. */
export async function replay(
  target: TlsTarget,
  messages: readonly Record<string, unknown>[],
): Promise<Record<string, unknown>[]> {
  const socket = await TlsWebSocket.connect(target, '/pair');
  const answers: Record<string, unknown>[] = [];
  try {
    for (const [index, message] of messages.entries()) {
      socket.sendJson(message);
      const answer = await socket.message(index);
      answers.push(answer);
      if (answer.type === 'pair_refused') break;
    }
  } finally {
    socket.close().catch(() => {});
  }
  return answers;
}

function refuseOn(message: Record<string, unknown>): void {
  if (message.type !== 'pair_refused') return;
  const error = message.error as { code: string; message: string };
  throw new PairingRefused(error.code, error.message, message.attempts_left as number | null);
}
