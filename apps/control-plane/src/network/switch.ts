import { type NetworkInterfaceInfo, networkInterfaces } from 'node:os';
import { join } from 'node:path';
import type { NetworkListener } from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import type { FastifyBaseLogger, FastifyInstance } from 'fastify';
import type { NetworkListenerConfig } from '../config.ts';
import type { ControlPlane } from '../core/control-plane.ts';
import type { RouteSources } from '../http/routes.ts';
import {
  type Announcement,
  type Announcer,
  dnsSdAnnouncer,
  endLeftoverAnnouncement,
  reachableFromTheNetwork,
} from './announce.ts';
import { loadOrCreateNetworkIdentity } from './certificate.ts';
import type { DeviceAccess } from './devices.ts';
import { describeListener, readLocalName } from './listener.ts';
import { Pairing, type PairingLimits } from './pairing.ts';
import type { PreviewTarget } from './preview-relay.ts';
import { createNetworkServer, type NetworkLimits } from './server.ts';

/** In the data folder while the computer announces itself: the announcing `dns-sd`'s pid. */
const ANNOUNCING_RECORD = 'announcing.pid';

export interface NetworkSwitchOptions {
  readonly dataDir: string;
  readonly controlPlane: ControlPlane;
  readonly sources: RouteSources;
  readonly devices: DeviceAccess;
  readonly clock: Clock;
  readonly logger: FastifyBaseLogger;
  readonly limits?: NetworkLimits;
  readonly pairingLimits?: PairingLimits;
  /** The computer's `.local` name; read from macOS by default. */
  readonly localName?: () => Promise<string | null>;
  /**
   * The host to describe the listener as, where it differs from the one it binds: a check that binds
   * loopback yet shows the page a wildcard listener's names. Tests and checks only.
   */
  readonly describeHost?: string;
  readonly interfaces?: () => NodeJS.Dict<NetworkInterfaceInfo[]>;
  /** Where a project's preview is relayed from to a paired device (ADR 0052); none, no relay. */
  readonly previewTarget?: (projectId: string) => PreviewTarget;
  /**
   * Makes what announces the computer while a pairing window is open (ADR 0017); macOS's `dns-sd` by
   * default. Tests give their own.
   */
  readonly announcer?: (describe: () => Announcement | null) => Announcer;
}

interface Listening {
  readonly server: FastifyInstance;
  readonly pairing: Pairing;
  readonly describe: () => NetworkListener;
}

/**
 * The network listener for paired devices (ADR 0017), turned on and off while the control plane
 * runs (ADR 0047): at start when the owner's settings say so, and later from the pairing page on the
 * person's press. One path for both: the same certificate, kept in the data directory, a new
 * `Pairing` and server each time it turns on. Turning it off ends every paired device's connection,
 * which each device retries; their pairings stay.
 */
export class NetworkSwitch {
  readonly #options: NetworkSwitchOptions;
  #listening: Listening | null = null;
  /** Turning on or off, one at a time, in the order asked. */
  #queue: Promise<unknown> = Promise.resolve();

  constructor(options: NetworkSwitchOptions) {
    this.#options = options;
  }

  /** The pairing windows of the listener now, or null while it is off. */
  get pairing(): Pairing | null {
    return this.#listening?.pairing ?? null;
  }

  get on(): boolean {
    return this.#listening !== null;
  }

  /** The control plane's time, by which pairing windows run out. */
  now(): number {
    return this.#options.clock.now().getTime();
  }

  /** Where devices reach the listener now, read anew; null while it is off. */
  listener(): NetworkListener | null {
    return this.#listening?.describe() ?? null;
  }

  /** Turns the listener on at `config`; already on, nothing changes. */
  start(config: NetworkListenerConfig): Promise<NetworkListener> {
    return this.#turn(async () => {
      if (this.#listening === null) this.#listening = await this.#open(config);
      return this.#listening.describe();
    });
  }

  /**
   * Turns the listener off: its pairing window closes, and every connection to it ends, a paired
   * device's realtime connection with it. Already off, nothing changes.
   */
  stop(): Promise<void> {
    return this.#turn(async () => {
      const listening = this.#listening;
      if (listening === null) return;
      this.#listening = null;
      listening.pairing.close();
      await listening.server.close();
    });
  }

  #turn<T>(work: () => Promise<T>): Promise<T> {
    const next = this.#queue.then(work, work);
    this.#queue = next.catch(() => undefined);
    return next;
  }

  async #open(config: NetworkListenerConfig): Promise<Listening> {
    const options = this.#options;
    const { identity, created } = await loadOrCreateNetworkIdentity(
      options.dataDir,
      new Date(options.clock.now().getTime()),
    );
    if (created) {
      options.logger.info(
        { certificate_sha256: identity.certificateSha256 },
        'created the network listener certificate',
      );
    }
    // Read when a window opens: by then the listener has its port.
    let announced: Announcement | null = null;
    const record = join(options.dataDir, ANNOUNCING_RECORD);
    await endLeftoverAnnouncement(record);
    const announcer = (options.announcer ?? ((describe) => dnsSdAnnouncer(describe, { record })))(
      () => announced,
    );
    const pairing = new Pairing({
      announcer,
      clock: options.clock,
      devices: options.devices,
      describe: (deviceId) => options.controlPlane.projection.device(deviceId),
      certificateSha256: identity.certificateSha256,
      logger: options.logger,
      ...(options.pairingLimits === undefined ? {} : { limits: options.pairingLimits }),
    });
    let describe: (() => NetworkListener) | null = null;
    const server = await createNetworkServer({
      logger: options.logger.child({ listener: 'network' }),
      identity,
      controlPlane: options.controlPlane,
      sources: options.sources,
      devices: options.devices,
      pairing,
      clock: options.clock,
      ...(options.limits === undefined ? {} : { limits: options.limits }),
      listener: () => describe?.() ?? null,
      ...(options.previewTarget === undefined ? {} : { previewTarget: options.previewTarget }),
    });
    try {
      await server.listen({ host: config.host, port: config.port });
    } catch (error) {
      await server.close();
      throw error;
    }
    const bound = server.server.address();
    const port = bound !== null && typeof bound === 'object' ? bound.port : config.port;
    const localName = await (options.localName ?? readLocalName)();
    const interfaces = options.interfaces ?? networkInterfaces;
    const { certificateSha256 } = identity;
    const described = { ...config, host: options.describeHost ?? config.host };
    describe = () => describeListener(described, port, certificateSha256, localName, interfaces());
    announced =
      localName === null || !reachableFromTheNetwork(config.host)
        ? null
        : {
            name: localName.replace(/\.local$/, ''),
            port,
            pinPrefix: certificateSha256.slice(0, 16),
          };
    return { server, pairing, describe };
  }
}
