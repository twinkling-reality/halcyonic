import { randomBytes } from 'node:crypto';
import type { DeviceId, Principal } from '@halcyonic/contracts';
import type { ControlPlane } from '../core/control-plane.ts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { TrackConnection } from '../http/realtime.ts';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import {
  CREDENTIAL_BYTES,
  CREDENTIAL_PATTERN,
  credentialFromBytes,
  credentialSha256,
} from './pairing-protocol.ts';

const NO_SCOPE = { project_id: null, workstream_id: null, execution_id: null } as const;

export type DevicePrincipal = Extract<Principal, { kind: 'device' }>;

export type Authentication =
  | { readonly ok: true; readonly principal: DevicePrincipal }
  | {
      readonly ok: false;
      readonly code: 'unauthorized' | 'device_revoked';
      readonly message: string;
    };

export type Revocation = 'revoked' | 'not_found' | 'already_revoked';

/** Realtime connections one device may hold at once; a reconnect can briefly overlap its last. */
export const MAX_REALTIME_CONNECTIONS_PER_DEVICE = 4;

/**
 * Paired devices' credentials (ADR 0017). Pairing and revocation are journaled through the
 * recorder, and the projection is what every credential is checked against, so a revocation
 * applies to the next request at once. It also applies to what is already open: a revoked device's
 * realtime connections end at once, and the network listener and the command service check again
 * before they answer or act. Credentials are never logged; log lines name devices by id.
 */
export class DeviceAccess {
  readonly #controlPlane: ControlPlane;
  readonly #ids: IdGenerator;
  readonly #logger: Logger;
  /** What ends each open realtime connection, by device. */
  readonly #connections = new Map<DeviceId, Set<() => void>>();

  constructor(options: { controlPlane: ControlPlane; ids: IdGenerator; logger: Logger }) {
    this.#controlPlane = options.controlPlane;
    this.#ids = options.ids;
    this.#logger = options.logger;
  }

  /**
   * Records a device that proved it saw the pairing code, and returns its new credential's bytes.
   * The journal keeps only the credential's SHA-256.
   */
  pair(label: string, certificateSha256: string): { deviceId: DeviceId; credential: Buffer } {
    const deviceId = this.#ids.next() as DeviceId;
    const credential = randomBytes(CREDENTIAL_BYTES);
    this.#controlPlane.recorder.record(
      controlPlaneDraft(
        'device.paired',
        NO_SCOPE,
        {
          device_id: deviceId,
          label,
          credential_sha256: credentialSha256(credentialFromBytes(credential)),
          certificate_sha256: certificateSha256,
        },
        this.#now(),
        NO_CAUSE,
      ),
    );
    this.#logger.info({ device_id: deviceId }, 'device paired');
    return { deviceId, credential };
  }

  /** Checks an `Authorization` header against the paired devices. */
  authenticate(authorization: string | undefined): Authentication {
    const credential = authorization?.startsWith('Bearer ') ? authorization.slice(7) : null;
    if (credential === null || !CREDENTIAL_PATTERN.test(credential)) {
      return unauthorized();
    }
    const device = this.#controlPlane.projection.deviceForCredential(credentialSha256(credential));
    if (device === undefined) return unauthorized();
    if (device.revoked_at !== null) {
      return {
        ok: false,
        code: 'device_revoked',
        message: 'This device was revoked on the control plane; pair it again.',
      };
    }
    return { ok: true, principal: { kind: 'device', device_id: device.device_id } };
  }

  /** Whether a device is paired and not revoked, for a request authenticated a moment ago. */
  isActive(deviceId: string): boolean {
    const device = this.#controlPlane.projection.device(deviceId);
    return device !== undefined && device.revoked_at === null;
  }

  /** Stops accepting the device's credential and ends its open connections. */
  revoke(deviceId: string, by: Principal): Revocation {
    const device = this.#controlPlane.projection.device(deviceId);
    if (device === undefined) return 'not_found';
    if (device.revoked_at !== null) return 'already_revoked';
    this.#controlPlane.recorder.record(
      controlPlaneDraft(
        'device.revoked',
        NO_SCOPE,
        { device_id: device.device_id, revoked_by: by },
        this.#now(),
        NO_CAUSE,
      ),
    );
    // Synchronously, in the step that journaled the revocation: no message a connection has
    // already received is handled after this, and nothing more is sent to it.
    const connections = this.#connections.get(device.device_id);
    this.#connections.delete(device.device_id);
    for (const end of connections ?? []) end();
    this.#logger.info(
      {
        device_id: device.device_id,
        revoked_by: by.kind,
        closed_connections: connections?.size ?? 0,
      },
      'device revoked',
    );
    return 'revoked';
  }

  /**
   * For the realtime stream: remembers a device's connection so revoking the device ends it, and
   * refuses one from a device revoked since its upgrade was authenticated, or past the device's cap.
   */
  readonly track: TrackConnection = (principal, end) => {
    if (principal.kind !== 'device') return { ok: true, untrack: () => {} };
    const deviceId = principal.device_id;
    if (!this.isActive(deviceId)) {
      return {
        ok: false,
        code: 'device_revoked',
        message: 'This device was revoked on the control plane; pair it again.',
      };
    }
    const connections = this.#connections.get(deviceId) ?? new Set<() => void>();
    if (connections.size >= MAX_REALTIME_CONNECTIONS_PER_DEVICE) {
      this.#logger.warn({ device_id: deviceId }, 'device has too many realtime connections');
      return {
        ok: false,
        code: 'too_many_connections',
        message: `A device may hold ${MAX_REALTIME_CONNECTIONS_PER_DEVICE} realtime connections at once; close one first.`,
      };
    }
    connections.add(end);
    this.#connections.set(deviceId, connections);
    return {
      ok: true,
      untrack: () => {
        const current = this.#connections.get(deviceId);
        current?.delete(end);
        if (current?.size === 0) this.#connections.delete(deviceId);
      },
    };
  };

  /** Devices with a realtime connection open now. */
  connected(): DeviceId[] {
    return [...this.#connections.keys()];
  }

  #now(): string {
    return this.#controlPlane.clock.now().toISOString();
  }
}

function unauthorized(): Authentication {
  return {
    ok: false,
    code: 'unauthorized',
    message: 'A paired device credential is required.',
  };
}
