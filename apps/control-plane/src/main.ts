import { chmod, mkdir } from 'node:fs/promises';
import { join } from 'node:path';
import { loadScenarios, MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import { loadConfig } from './config.ts';
import { ControlPlane } from './core/control-plane.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { createUuidV7Generator } from './ids.ts';
import { seorakEvaluationFor } from './intelligence/evaluation.ts';
import { salidiumUnderstandingFor } from './intelligence/understanding.ts';
import { openSqliteJournal } from './journal/sqlite-journal.ts';
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
  registerRoutes(app, controlPlane, {
    understanding: salidiumUnderstandingFor(config.dataDir),
    evaluation: seorakEvaluationFor(config.dataDir),
  });
  registerRealtime(app, controlPlane);

  await app.listen({ host: config.host, port: config.port });
  app.log.info(
    {
      addresses: app.addresses(),
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

main().catch((error: unknown) => {
  process.stderr.write(
    `control plane failed to start: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
