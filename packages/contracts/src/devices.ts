import Type, { type Static } from 'typebox';
import { DeviceId, ErrorInfo, Nullable, Timestamp } from './primitives.ts';
import { PAIRING_PROTOCOL_VERSION } from './versions.ts';

const strict = { additionalProperties: false } as const;

/**
 * Who submitted a command, as the control plane authenticated it, never as a client declared
 * itself: `local` holds the access token on the control plane's own machine, `device` is a device
 * paired over the network (ADR 0017).
 */
export const Principal = Type.Union([
  Type.Object({ kind: Type.Literal('local') }, strict),
  Type.Object({ kind: Type.Literal('device'), device_id: DeviceId }, strict),
]);
export type Principal = Static<typeof Principal>;

/** A SHA-256 digest in lowercase hexadecimal. */
export const Sha256Hex = Type.String({ pattern: '^[0-9a-f]{64}$' });

/**
 * What a device calls itself when it pairs, such as its model. Recorded for display and never
 * trusted. Control characters are refused, because the label is printed in a terminal.
 */
export const DeviceLabel = Type.String({
  minLength: 1,
  maxLength: 120,
  pattern: '^(?=.*\\S)[^\\u0000-\\u001f\\u007f]+$',
});

/** A paired device as the projection knows it. */
export const DeviceView = Type.Object(
  {
    device_id: DeviceId,
    label: DeviceLabel,
    paired_at: Timestamp,
    /** The control plane's TLS certificate, as the device pinned it when it paired. */
    certificate_sha256: Sha256Hex,
    revoked_at: Nullable(Timestamp),
  },
  strict,
);
export type DeviceView = Static<typeof DeviceView>;

/** The paired devices, for the owner on loopback. */
export const DevicesResponse = Type.Object(
  {
    devices: Type.Array(DeviceView),
    /** Devices with a connection open now. Runtime state, not journaled. */
    connected: Type.Array(DeviceId),
  },
  strict,
);
export type DevicesResponse = Static<typeof DevicesResponse>;

export const PairingState = Type.Union([
  /** No window was opened, or the owner closed it. */
  Type.Literal('closed'),
  Type.Literal('open'),
  /** A device paired; the window closed with it. */
  Type.Literal('paired'),
  Type.Literal('expired'),
  /** Too many failed attempts closed the window. */
  Type.Literal('locked'),
]);
export type PairingState = Static<typeof PairingState>;

/** The pairing window, for the owner on loopback. The code is never part of it. */
export const PairingStatus = Type.Object(
  {
    state: PairingState,
    expires_at: Nullable(Timestamp),
    failed_attempts: Type.Integer({ minimum: 0 }),
    max_failed_attempts: Type.Integer({ minimum: 1 }),
    /** The device that paired through this window. */
    device: Nullable(DeviceView),
  },
  strict,
);
export type PairingStatus = Static<typeof PairingStatus>;

/** Where paired devices reach the control plane. */
export const NetworkListener = Type.Object(
  {
    /** The address the listener is bound to, as configured. */
    host: Type.String({ minLength: 1, maxLength: 64 }),
    port: Type.Integer({ minimum: 1, maximum: 65535 }),
    /** Addresses a device on the network can use; the interfaces' addresses for a wildcard host. */
    addresses: Type.Array(Type.String({ minLength: 1, maxLength: 64 })),
    certificate_sha256: Sha256Hex,
  },
  strict,
);
export type NetworkListener = Static<typeof NetworkListener>;

/** Eight decimal digits, shown on the control plane's machine and typed on the device. */
export const PairingCode = Type.String({ pattern: '^[0-9]{8}$' });

/** Answer to opening a pairing window. The code is in this answer only; it is never logged. */
export const PairingOpenedResponse = Type.Object(
  { code: PairingCode, status: PairingStatus, listener: NetworkListener },
  strict,
);
export type PairingOpenedResponse = Static<typeof PairingOpenedResponse>;

// The pairing exchange on the network listener's /pair WebSocket (ADR 0017) -------------------

/** Standard base64, with padding, of exactly `bytes` bytes. */
function Base64(bytes: number) {
  const padding = (3 - (bytes % 3)) % 3;
  const characters = Math.ceil(bytes / 3) * 4 - padding;
  return Type.String({ pattern: `^[A-Za-z0-9+/]{${characters}}${'='.repeat(padding)}$` });
}

/** An SRP-6a public value in RFC 5054's 3072-bit group: 384 bytes, big-endian, zero padded. */
const SrpPublic = Base64(384);
/** An HMAC-SHA-256, or 32 bytes encrypted with one. */
const Mac = Base64(32);

export const PAIRING_CLIENT_MESSAGE_VARIANTS = [
  Type.Object(
    {
      type: Type.Literal('pair_request'),
      protocol: Type.Literal(PAIRING_PROTOCOL_VERSION),
      device_label: DeviceLabel,
    },
    strict,
  ),
  Type.Object(
    {
      type: Type.Literal('pair_proof'),
      /** SRP's A = g^a mod N. */
      client_public: SrpPublic,
      proof: Mac,
    },
    strict,
  ),
] as const;
export const PairingClientMessage = Type.Union([...PAIRING_CLIENT_MESSAGE_VARIANTS]);
export type PairingClientMessage = Static<typeof PairingClientMessage>;

export const PAIRING_SERVER_MESSAGE_VARIANTS = [
  Type.Object(
    {
      type: Type.Literal('pair_challenge'),
      /** 16 random bytes, fresh for every attempt. */
      salt: Base64(16),
      /** SRP's B = k * v + g^b mod N. */
      server_public: SrpPublic,
    },
    strict,
  ),
  Type.Object(
    {
      type: Type.Literal('pair_accepted'),
      device_id: DeviceId,
      /** The device's credential, 32 bytes, encrypted under the session key. */
      credential: Mac,
      proof: Mac,
    },
    strict,
  ),
  Type.Object(
    {
      type: Type.Literal('pair_refused'),
      error: ErrorInfo,
      /** How many more failed attempts the window allows, once a proof was checked. */
      attempts_left: Nullable(Type.Integer({ minimum: 0 })),
    },
    strict,
  ),
] as const;
export const PairingServerMessage = Type.Union([...PAIRING_SERVER_MESSAGE_VARIANTS]);
export type PairingServerMessage = Static<typeof PairingServerMessage>;
