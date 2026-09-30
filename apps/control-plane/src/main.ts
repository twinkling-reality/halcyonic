import { chmod, mkdir } from 'node:fs/promises';
import { type NetworkInterfaceInfo, networkInterfaces } from 'node:os';
import { join } from 'node:path';
import type { NetworkListener } from '@halcyonic/contracts';
import { loadScenarios, MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import type { FastifyInstance } from 'fastify';
import { loadConfig, type NetworkListenerConfig } from './config.ts';
import { ControlPlane } from './core/control-plane.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import { registerDeviceRoutes } from './http/device-routes.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { createUuidV7Generator } from './ids.ts';
import { seorakEvaluationFor } from './intelligence/evaluation.ts';
import { salidiumUnderstandingFor } from './intelligence/understanding.ts';
import { openSqliteJournal } from './journal/sqlite-journal.ts';
import { loadOrCreateNetworkIdentity } from './network/certificate.ts';
import { DeviceAccess } from './network/devices.ts';
import { Pairing } from './network/pairing.ts';
import { createNetworkServer } from './network/server.ts';
import { createRuntimeAdapters, stopStaleRuntimeServers } from './runtimes.ts';

async function main(): Promise<void> {
  const config = loadConfig();
  await mkdir(config.dataDir, { recursive: true, mode: 0o700 });
  await chmod(config.dataDir, 0o700);
  const access = await loadOrCreateAccessToken(config.dataDir);
  // Built first, so a misconfigured runtime stops startup before anything else opens.
  const adapters = createRuntimeAdapters(config, {
    mock: new MockRuntimeAdapter({
      scenarios: loadScenarios(config.scenariosDir),
      models: MOCK_MODELS,
    }),
    directoryPolicy: createDirectoryPolicy(config.projectRoots),
    environment: process.env,
    dataDir: config.dataDir,
  });

  const app = await createHttpServer({ logLevel: config.logLevel, token: access.token });
  for (const stale of await stopStaleRuntimeServers(adapters)) {
    app.log.warn(stale, 'a runtime server from an earlier run was still recorded');
  }
  const ids = createUuidV7Generator();
  const journal = openSqliteJournal({
    path: join(config.dataDir, 'control-plane.db'),
    originIfNew: 'live',
    ids,
  });
  if (journal.info.origin !== 'live') {
    journal.close();
    throw new Error(
      `${config.dataDir} holds a fixture journal; the live control plane refuses to serve it.`,
    );
  }

  const controlPlane = new ControlPlane({
    journal,
    adapters,
    ids,
    clock: systemClock,
    scheduler: systemScheduler,
    logger: app.log,
    commandTimeoutMs: config.commandTimeoutMs,
  });
  controlPlane.reconcile();
  const sources = {
    understanding: salidiumUnderstandingFor(config.dataDir),
    evaluation: seorakEvaluationFor(config.dataDir),
  };
  const devices = new DeviceAccess({ controlPlane, ids, logger: app.log });

  // Paired devices reach the control plane only through the network listener, which is off unless
  // the owner configured it (ADR 0017).
  let network: { server: FastifyInstance; pairing: Pairing; certificateSha256: string } | null =
    null;
  if (config.network !== null) {
    const { identity, created } = await loadOrCreateNetworkIdentity(config.dataDir, new Date());
    if (created) {
      app.log.info(
        { certificate_sha256: identity.certificateSha256 },
        'created the network listener certificate',
      );
    }
    const pairing = new Pairing({
      clock: systemClock,
      devices,
      describe: (deviceId) => controlPlane.projection.device(deviceId),
      certificateSha256: identity.certificateSha256,
      logger: app.log,
    });
    const server = await createNetworkServer({
      logger: app.log.child({ listener: 'network' }),
      identity,
      controlPlane,
      sources,
      devices,
      pairing,
      clock: systemClock,
    });
    network = { server, pairing, certificateSha256: identity.certificateSha256 };
  }
  let listener: NetworkListener | null = null;
  registerRoutes(app, controlPlane, sources);
  registerRealtime(app, controlPlane);
  registerDeviceRoutes(app, {
    controlPlane,
    devices,
    pairing: network?.pairing ?? null,
    listener: () => listener,
  });

  try {
    await app.listen({ host: config.host, port: config.port });
    if (network !== null && config.network !== null) {
      await network.server.listen({ host: config.network.host, port: config.network.port });
      listener = describeListener(config.network, network.server, network.certificateSha256);
    }
  } catch (error) {
    await network?.server.close();
    await app.close();
    await controlPlane.close();
    throw error;
  }
  app.log.info(
    {
      addresses: app.addresses(),
      network: listener,
      data_dir: config.dataDir,
      token_file: access.path,
      journal: controlPlane.journal.info,
      runtimes: controlPlane.registry.descriptors().map((runtime) => runtime.runtime_id),
      project_roots: config.projectRoots,
    },
    'control plane ready',
  );

  let stopping = false;
  let signalled = false;
  // Runs once; later calls do nothing. Causes repeat and coincide: stdin ends and then closes, and
  // a launcher ended by the same Ctrl-C as this process closes stdin during the shutdown the
  // signal started.
  const shutdown = async (cause: { signal: NodeJS.Signals } | { reason: string }) => {
    if (stopping) return;
    stopping = true;
    app.log.info(cause, 'shutting down');
    // A stdin still being read would keep the process alive after a signal.
    if (config.exitOnStdinEnd) process.stdin.destroy();
    await network?.server.close();
    await app.close();
    await controlPlane.close();
  };
  const onSignal = (signal: NodeJS.Signals) => {
    if (signalled) {
      // A second signal exits at once; process exit handlers still stop launched agent processes.
      app.log.warn({ signal }, 'second signal; exiting without waiting');
      process.exit(1);
    }
    signalled = true;
    void shutdown({ signal });
  };
  process.on('SIGINT', onSignal);
  process.on('SIGTERM', onSignal);
  if (config.exitOnStdinEnd) {
    // The launcher holds stdin open and never writes to it, so stdin ends when the launcher exits,
    // however it exits. Anything written is discarded. The end of stdin, or a failure to read it,
    // stops the control plane as SIGTERM does, but never counts as a second signal.
    const stdinEnded = () => void shutdown({ reason: 'stdin ended' });
    process.stdin.on('end', stdinEnded);
    process.stdin.on('close', stdinEnded);
    process.stdin.on('error', stdinEnded);
    process.stdin.resume();
  }
}

/**
 * Where devices on the network can reach the listener: its own address, or for a wildcard, every
 * interface's external addresses (IPv4 for `0.0.0.0`, both families for `::`).
 */
function describeListener(
  config: NetworkListenerConfig,
  server: FastifyInstance,
  certificateSha256: string,
): NetworkListener {
  const bound = server.server.address();
  const port = bound !== null && typeof bound === 'object' ? bound.port : config.port;
  const wildcard = config.host === '0.0.0.0' || config.host === '::';
  const reachable = (entry: NetworkInterfaceInfo) =>
    !entry.internal &&
    (entry.family === 'IPv4' || (config.host === '::' && !entry.address.startsWith('fe80:')));
  const addresses = wildcard
    ? Object.values(networkInterfaces())
        .flatMap((entries) => entries ?? [])
        .filter(reachable)
        .map((entry) => entry.address)
    : [config.host];
  return { host: config.host, port, addresses, certificate_sha256: certificateSha256 };
}

main().catch((error: unknown) => {
  process.stderr.write(
    `control plane failed to start: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
