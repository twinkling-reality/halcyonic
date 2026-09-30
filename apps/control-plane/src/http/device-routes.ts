import {
  compileValidator,
  DeviceId,
  type DevicesResponse,
  type NetworkListener,
  type PairingOpenedResponse,
  type PairingStatus,
} from '@halcyonic/contracts';
import type { FastifyInstance } from 'fastify';
import type { ControlPlane } from '../core/control-plane.ts';
import type { DeviceAccess } from '../network/devices.ts';
import { DEFAULT_PAIRING_LIMITS, type Pairing } from '../network/pairing.ts';
import { errorBody, LOCAL_PRINCIPAL } from './server.ts';

const validateDeviceId = compileValidator(DeviceId);

export interface DeviceRouteOptions {
  readonly controlPlane: ControlPlane;
  readonly devices: DeviceAccess;
  /** Null while the network listener is off. */
  readonly pairing: Pairing | null;
  /** Where devices reach the network listener, once it listens. */
  readonly listener: () => NetworkListener | null;
}

/**
 * Managing devices, on loopback only and with the access token (ADR 0017): opening a pairing
 * window, listing devices and revoking them. The network listener serves none of this. A pairing
 * window's code is in the answer that opens it and nowhere else: never in a log or the journal.
 */
export function registerDeviceRoutes(app: FastifyInstance, options: DeviceRouteOptions): void {
  const { controlPlane, devices, pairing } = options;

  app.post('/api/pairing', async (_request, reply) => {
    const listener = options.listener();
    if (pairing === null || listener === null) {
      return reply
        .code(409)
        .send(
          errorBody(
            'network_off',
            'The network listener is off. Start the control plane with HALCYONIC_NETWORK_HOST set; see docs/internal/runbooks/LOCAL_DEVELOPMENT.md.',
          ),
        );
    }
    const { code, status } = pairing.open();
    const body: PairingOpenedResponse = { code, status, listener };
    return reply.code(201).send(body);
  });

  app.get('/api/pairing', async (): Promise<PairingStatus> => pairing?.status() ?? noWindow());

  app.delete('/api/pairing', async (): Promise<PairingStatus> => pairing?.close() ?? noWindow());

  app.get(
    '/api/devices',
    async (): Promise<DevicesResponse> => ({
      devices: controlPlane.projection.devices(),
      connected: devices.connected(),
    }),
  );

  app.post('/api/devices/:device_id/revoke', async (request, reply) => {
    const deviceId = (request.params as { device_id: string }).device_id;
    if (!validateDeviceId(deviceId).ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'device_id must be a device identifier.'));
    }
    if (devices.revoke(deviceId, LOCAL_PRINCIPAL) === 'not_found') {
      return reply
        .code(404)
        .send(errorBody('device_not_found', `Device ${deviceId} was never paired.`));
    }
    return controlPlane.projection.device(deviceId);
  });
}

function noWindow(): PairingStatus {
  return {
    state: 'closed',
    expires_at: null,
    failed_attempts: 0,
    max_failed_attempts: DEFAULT_PAIRING_LIMITS.maxFailedAttempts,
    device: null,
  };
}
