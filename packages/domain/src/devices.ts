import type {
  DeviceEvent,
  DeviceId,
  DeviceView,
  EventId,
  Principal,
  Timestamp,
} from '@halcyonic/contracts';

/** Something in a device event that the registry could not apply as written. */
export interface DeviceNote {
  code: 'unknown_entity' | 'duplicate_entity';
  event_id: EventId;
  message: string;
}

interface DeviceState {
  readonly deviceId: DeviceId;
  readonly label: string;
  readonly pairedAt: Timestamp;
  readonly credentialSha256: string;
  readonly certificateSha256: string;
  revokedAt: Timestamp | null;
  revokedBy: Principal | null;
}

/**
 * The devices paired with the control plane, rebuilt from `device.*` events like every other part
 * of the projection (ADR 0017). It keeps each credential's SHA-256, never the credential, so the
 * control plane can recognize a device's credential without being able to present it.
 */
export class DeviceRegistry {
  readonly #devices = new Map<DeviceId, DeviceState>();
  /** Every credential hash ever issued, revoked ones included, so a revoked device is told so. */
  readonly #byCredential = new Map<string, DeviceId>();

  apply(event: DeviceEvent): DeviceNote[] {
    switch (event.event_type) {
      case 'device.paired': {
        const { device_id, credential_sha256 } = event.payload;
        if (this.#devices.has(device_id)) {
          return [note('duplicate_entity', event, `device ${device_id} already paired`)];
        }
        if (this.#byCredential.has(credential_sha256)) {
          return [note('duplicate_entity', event, `device ${device_id} reuses a credential`)];
        }
        this.#devices.set(device_id, {
          deviceId: device_id,
          label: event.payload.label,
          pairedAt: event.occurred_at,
          credentialSha256: credential_sha256,
          certificateSha256: event.payload.certificate_sha256,
          revokedAt: null,
          revokedBy: null,
        });
        this.#byCredential.set(credential_sha256, device_id);
        return [];
      }
      case 'device.revoked': {
        const device = this.#devices.get(event.payload.device_id);
        if (device === undefined) {
          return [
            note('unknown_entity', event, `device ${event.payload.device_id} was never paired`),
          ];
        }
        if (device.revokedAt !== null) {
          return [note('duplicate_entity', event, `device ${device.deviceId} was already revoked`)];
        }
        device.revokedAt = event.occurred_at;
        device.revokedBy = event.payload.revoked_by;
        return [];
      }
      default: {
        const unhandled: never = event;
        throw new Error(
          `unhandled device event ${String((unhandled as { event_type?: unknown }).event_type)}`,
        );
      }
    }
  }

  devices(): DeviceView[] {
    return [...this.#devices.values()].map(toDeviceView);
  }

  device(deviceId: string): DeviceView | undefined {
    const state = this.#devices.get(deviceId as DeviceId);
    return state === undefined ? undefined : toDeviceView(state);
  }

  /** The device a credential was issued to, revoked or not, by the credential's SHA-256. */
  deviceForCredential(credentialSha256: string): DeviceView | undefined {
    const deviceId = this.#byCredential.get(credentialSha256);
    return deviceId === undefined ? undefined : this.device(deviceId);
  }
}

function toDeviceView(state: DeviceState): DeviceView {
  return {
    device_id: state.deviceId,
    label: state.label,
    paired_at: state.pairedAt,
    certificate_sha256: state.certificateSha256,
    revoked_at: state.revokedAt,
  };
}

function note(code: DeviceNote['code'], event: DeviceEvent, message: string): DeviceNote {
  return { code, event_id: event.event_id, message };
}
