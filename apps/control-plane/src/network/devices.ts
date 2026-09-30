import { randomBytes } from 'node:crypto';
import type { DeviceId, Principal } from '@halcyonic/contracts';
import type { WebSocket } from 'ws';
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

/**
 * Paired devices' credentials (ADR 0017). Pairing and revocation are journaled through the
 * recorder, and the projection is what every credential is checked against, so a revocation
 * applies to the next request at once. A revoked device's open realtime connections are closed.
 * Credentials are never logged; log lines name devices by id.
 */
export class DeviceAccess {
  readonly #controlPlane: ControlPlane;
  readonly #ids: IdGenerator;
  readonly #logger: Logger;
  readonly #sockets = new Map<DeviceId, Set<WebSocket>>();

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

  /** Stops accepting the device's credential and closes its open connections. */
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
    const sockets = this.#sockets.get(device.device_id);
    this.#sockets.delete(device.device_id);
    for (const socket of sockets ?? []) socket.close(1008, 'device revoked');
    this.#logger.info(
      { device_id: device.device_id, revoked_by: by.kind, closed_connections: sockets?.size ?? 0 },
      'device revoked',
    );
    return 'revoked';
  }

  /** For the realtime stream: remembers a device's connection so revoking the device closes it. */
  readonly track: TrackConnection = (principal, socket) => {
    if (principal.kind !== 'device') return () => {};
    const deviceId = principal.device_id;
    const sockets = this.#sockets.get(deviceId) ?? new Set<WebSocket>();
    sockets.add(socket);
    this.#sockets.set(deviceId, sockets);
    return () => {
      const current = this.#sockets.get(deviceId);
      current?.delete(socket);
      if (current?.size === 0) this.#sockets.delete(deviceId);
    };
  };

  /** Devices with a realtime connection open now. */
  connected(): DeviceId[] {
    return [...this.#sockets.keys()];
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
