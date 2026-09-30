import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { NetworkListener } from '@halcyonic/contracts';
import { loadScenarios, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { createVirtualTime, type RuntimeAdapter, type VirtualTime } from '@halcyonic/runtime-core';
import type { FastifyInstance } from 'fastify';
import type { LogLevel } from '../config.ts';
import { ControlPlane } from '../core/control-plane.ts';
import { createCommandFactory } from '../demo-plan.ts';
import { registerDeviceRoutes } from '../http/device-routes.ts';
import { registerRealtime } from '../http/realtime.ts';
import { registerRoutes } from '../http/routes.ts';
import { loadOrCreateAccessToken } from '../http/security.ts';
import { createHttpServer } from '../http/server.ts';
import { createUuidV7Generator } from '../ids.ts';
import { type EvaluationSource, seorakEvaluation } from '../intelligence/evaluation.ts';
import { salidiumUnderstanding, type UnderstandingSource } from '../intelligence/understanding.ts';
import type { EventJournal } from '../journal/journal.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import type { Logger } from '../logger.ts';
import { createNetworkIdentity, type NetworkIdentity } from '../network/certificate.ts';
import { DeviceAccess } from '../network/devices.ts';
import { Pairing, type PairingLimits } from '../network/pairing.ts';
import { createNetworkServer, type NetworkTimeouts } from '../network/server.ts';
import type { TlsTarget } from './tls-client.ts';

/** Test support only. Not used by the running control plane. */
export const SCENARIOS_DIR = fileURLToPath(
  new URL('../../../../fixtures/scenarios', import.meta.url),
);
export const SCENARIOS = loadScenarios(SCENARIOS_DIR);
export const TEST_CLIENT = { name: 'test', version: null, device_label: null };

export interface CapturedLog {
  readonly level: 'debug' | 'info' | 'warn' | 'error';
  readonly context: object;
  readonly message: string;
}

export function capturingLogger(): { logger: Logger; entries: CapturedLog[] } {
  const entries: CapturedLog[] = [];
  const at =
    (level: CapturedLog['level']) =>
    (context: object, message: string): void => {
      entries.push({ level, context, message });
    };
  return {
    entries,
    logger: { debug: at('debug'), info: at('info'), warn: at('warn'), error: at('error') },
  };
}

export interface TestControlPlaneOptions {
  readonly path?: string;
  readonly time?: VirtualTime;
  readonly adapters?: (time: VirtualTime) => RuntimeAdapter[];
  readonly logger?: Logger;
  readonly commandTimeoutMs?: number;
  /** Defaults to Salidium at a location where it never runs. */
  readonly understanding?: UnderstandingSource;
  /** Defaults to Seorak without a credential, so it is never sent a request. */
  readonly evaluation?: EvaluationSource;
}

/** A control plane on virtual time with the mock runtime, so tests decide when work progresses. */
export function createTestControlPlane(options: TestControlPlaneOptions = {}) {
  const time = options.time ?? createVirtualTime(new Date('2026-09-26T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => time.now().getTime() });
  const journal: EventJournal = openSqliteJournal({
    path: options.path ?? ':memory:',
    originIfNew: 'live',
    ids,
  });
  const adapters = options.adapters?.(time) ?? [
    new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
  ];
  const controlPlane = new ControlPlane({
    journal,
    adapters,
    ids,
    clock: time,
    scheduler: time,
    logger: options.logger ?? capturingLogger().logger,
    commandTimeoutMs: options.commandTimeoutMs ?? 30_000,
  });
  const commands = createCommandFactory(
    createUuidV7Generator({ now: () => time.now().getTime() }),
    time,
    TEST_CLIENT,
  );
  return { time, controlPlane, commands, journal };
}

export interface TestServerOptions extends TestControlPlaneOptions {
  /** Also serve the network listener, on 127.0.0.1, with a certificate of its own (ADR 0017). */
  readonly network?: { readonly limits?: PairingLimits; readonly timeouts?: NetworkTimeouts };
  readonly logLevel?: LogLevel;
  /** Receives both listeners' log lines, for tests that check what is logged. */
  readonly logStream?: NodeJS.WritableStream;
}

export interface TestNetworkListener {
  readonly server: FastifyInstance;
  readonly pairing: Pairing;
  readonly identity: NetworkIdentity;
  readonly target: TlsTarget;
  readonly listener: NetworkListener;
}

/**
 * Serves a test control plane over real HTTP and WebSocket on an ephemeral loopback port, and with
 * `network`, its network listener over TLS on another.
 */
export async function startTestServer(options: TestServerOptions = {}) {
  const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-server-'));
  const { token } = await loadOrCreateAccessToken(dataDir);
  const app: FastifyInstance = await createHttpServer({
    logLevel: options.logLevel ?? 'silent',
    token,
    ...(options.logStream === undefined ? {} : { logStream: options.logStream }),
  });
  const harness = createTestControlPlane(options);
  const sources = {
    understanding:
      options.understanding ??
      salidiumUnderstanding({
        home: join(dataDir, 'salidium'),
        credentialPath: join(dataDir, 'salidium-credential'),
      }),
    evaluation:
      options.evaluation ??
      seorakEvaluation({ credentialPath: join(dataDir, 'seorak-credential') }),
  };
  const devices = new DeviceAccess({
    controlPlane: harness.controlPlane,
    ids: createUuidV7Generator({ now: () => harness.time.now().getTime() }),
    logger: app.log,
  });
  const network =
    options.network === undefined
      ? null
      : await startNetworkListener(app, harness, devices, sources, options.network);
  registerRoutes(app, harness.controlPlane, sources);
  registerRealtime(app, harness.controlPlane);
  registerDeviceRoutes(app, {
    controlPlane: harness.controlPlane,
    devices,
    pairing: network?.pairing ?? null,
    listener: () => network?.listener ?? null,
  });
  await app.listen({ host: '127.0.0.1', port: 0 });
  const address = app.server.address();
  if (address === null || typeof address === 'string') throw new Error('server has no port');
  const origin = `127.0.0.1:${address.port}`;
  return {
    ...harness,
    app,
    token,
    devices,
    network,
    port: address.port,
    baseUrl: `http://${origin}`,
    wsUrl: `ws://${origin}/realtime`,
    async stop() {
      await network?.server.close();
      await app.close();
      await harness.controlPlane.close();
      rmSync(dataDir, { recursive: true, force: true });
    },
  };
}

async function startNetworkListener(
  app: FastifyInstance,
  harness: ReturnType<typeof createTestControlPlane>,
  devices: DeviceAccess,
  sources: Parameters<typeof createNetworkServer>[0]['sources'],
  { limits, timeouts }: NonNullable<TestServerOptions['network']>,
): Promise<TestNetworkListener> {
  const identity = createNetworkIdentity(harness.time.now());
  const pairing = new Pairing({
    clock: harness.time,
    devices,
    describe: (deviceId) => harness.controlPlane.projection.device(deviceId),
    certificateSha256: identity.certificateSha256,
    logger: app.log,
    ...(limits === undefined ? {} : { limits }),
  });
  const server = await createNetworkServer({
    logger: app.log.child({ listener: 'network' }),
    identity,
    controlPlane: harness.controlPlane,
    sources,
    devices,
    pairing,
    clock: harness.time,
    ...(timeouts === undefined ? {} : { timeouts }),
  });
  await server.listen({ host: '127.0.0.1', port: 0 });
  const bound = server.server.address();
  if (bound === null || typeof bound === 'string') throw new Error('network server has no port');
  return {
    server,
    pairing,
    identity,
    target: { host: '127.0.0.1', port: bound.port },
    listener: {
      host: '127.0.0.1',
      port: bound.port,
      addresses: ['127.0.0.1'],
      certificate_sha256: identity.certificateSha256,
    },
  };
}
