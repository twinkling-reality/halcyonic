import { chmod, mkdir } from 'node:fs/promises';
import { type NetworkInterfaceInfo, networkInterfaces } from 'node:os';
import { join } from 'node:path';
import type { NetworkListener } from '@halcyonic/contracts';
import { loadScenarios, MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import type { FastifyInstance } from 'fastify';
import { Companion } from './companion/companion.ts';
import {
  type ControlPlaneConfig,
  defaultDataDir,
  loadConfig,
  type NetworkListenerConfig,
} from './config.ts';
import { ControlPlane } from './core/control-plane.ts';
import { registerDeviceRoutes } from './http/device-routes.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { createUuidV7Generator } from './ids.ts';
import { seorakEvaluationFor } from './intelligence/evaluation.ts';
import { salidiumUnderstandingFor } from './intelligence/understanding.ts';
import { openSqliteJournal } from './journal/sqlite-journal.ts';
import { createHostLocations } from './locations.ts';
import { loadOrCreateNetworkIdentity } from './network/certificate.ts';
import { DeviceAccess } from './network/devices.ts';
import { Pairing } from './network/pairing.ts';
import { createNetworkServer } from './network/server.ts';
import { matchesPin, pinsForThisMac } from './pins.ts';
import { createRuntimeAdapters, heldSecrets, stopStaleRuntimeServers } from './runtimes.ts';
import { readHostSettings, SETTINGS_FILE, settingsInUse, withSettings } from './settings.ts';
import { Transcriptions } from './speech/transcriptions.ts';
import { WhisperEngine } from './speech/whisper.ts';

async function main(): Promise<void> {
  // What `pnpm mac-setup` wrote fills in what the environment leaves unset (ADR 0024).
  const settings = readHostSettings(defaultDataDir(process.env));
  const config = loadConfig(withSettings(process.env, settings));
  await mkdir(config.dataDir, { recursive: true, mode: 0o700 });
  await chmod(config.dataDir, 0o700);
  const access = await loadOrCreateAccessToken(config.dataDir);
  const locations = createHostLocations(config.projectRoots);
  // Built first, so a misconfigured runtime stops startup before anything else opens.
  const adapters = createRuntimeAdapters(config, {
    mock: new MockRuntimeAdapter({
      scenarios: loadScenarios(config.scenariosDir),
      models: MOCK_MODELS,
    }),
    directoryPolicy: locations.policy,
    environment: process.env,
    dataDir: config.dataDir,
  });
  // Opened before anything listens, so a speech engine that cannot report its version stops startup.
  const speech = config.speech === null ? null : await WhisperEngine.open(config.speech);

  const app = await createHttpServer({ logLevel: config.logLevel, token: access.token });
  // Whether each agent binary is the copy Halcyonic was checked with, whoever named it (ADR 0024).
  const agentBinaries = await checkAgentBinaries(config);
  for (const binary of agentBinaries.filter((entry) => entry.pinned === 'differs')) {
    app.log.warn(binary, 'an agent binary is not the copy Halcyonic was checked with');
  }
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
    locations,
    secrets: heldSecrets(
      config,
      { environment: process.env, dataDir: config.dataDir },
      access.token,
      adapters,
    ),
  });
  controlPlane.reconcile();
  const transcriptions = new Transcriptions({ engine: speech, clock: systemClock });
  const companion = new Companion({ config: config.companion, clock: systemClock });
  const sources = {
    understanding: salidiumUnderstandingFor(config.dataDir),
    evaluation: seorakEvaluationFor(config.dataDir),
    transcriptions,
    companion,
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
      speech: speech?.engine ?? null,
      settings: {
        file: join(config.dataDir, SETTINGS_FILE),
        used: settingsInUse(process.env, settings),
      },
      agent_binaries: agentBinaries,
      companion: config.companion?.model ?? null,
    },
    'control plane ready',
  );
  // The first transcription after whisper.cpp is built or updated compiles its GPU shaders, which
  // can take longer than a clip may; done here, it never keeps a person waiting.
  void transcriptions.warmUp().then(
    (ms) => {
      if (ms !== null) app.log.info({ ms }, 'speech engine warmed up');
    },
    (error: unknown) => app.log.warn({ err: error }, 'speech engine warm-up failed'),
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
 * Each configured OpenCode and Codex binary's path, and whether its SHA-256 is the pinned one:
 * `matches`, `differs`, or `no_pin` where Halcyonic has no pin for this processor.
 */
async function checkAgentBinaries(
  config: ControlPlaneConfig,
): Promise<{ app: string; path: string; pinned: 'matches' | 'differs' | 'no_pin' }[]> {
  const pins = pinsForThisMac();
  const binaries = [
    ...(config.opencodeBinary === null
      ? []
      : [{ app: 'opencode', path: config.opencodeBinary, pin: pins?.opencode ?? null }]),
    ...(config.codexBinary === null
      ? []
      : [{ app: 'codex', path: config.codexBinary, pin: pins?.codex ?? null }]),
  ];
  return Promise.all(
    binaries.map(async ({ app, path, pin }) => {
      const matches = await matchesPin(path, pin);
      return {
        app,
        path,
        pinned:
          matches === null
            ? ('no_pin' as const)
            : matches
              ? ('matches' as const)
              : ('differs' as const),
      };
    }),
  );
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
