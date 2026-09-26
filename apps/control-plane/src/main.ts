import { chmod, mkdir } from 'node:fs/promises';
import { join } from 'node:path';
import { loadScenarios, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import { loadConfig } from './config.ts';
import { ControlPlane } from './core/control-plane.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { createUuidV7Generator } from './ids.ts';
import { salidiumUnderstandingFor } from './intelligence/understanding.ts';
import { openSqliteJournal } from './journal/sqlite-journal.ts';

async function main(): Promise<void> {
  const config = loadConfig();
  await mkdir(config.dataDir, { recursive: true, mode: 0o700 });
  await chmod(config.dataDir, 0o700);
  const access = await loadOrCreateAccessToken(config.dataDir);

  const app = await createHttpServer({ logLevel: config.logLevel, token: access.token });
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
    adapters: [new MockRuntimeAdapter({ scenarios: loadScenarios(config.scenariosDir) })],
    ids,
    clock: systemClock,
    scheduler: systemScheduler,
    logger: app.log,
    commandTimeoutMs: config.commandTimeoutMs,
  });
  controlPlane.reconcile();
  registerRoutes(app, controlPlane, { understanding: salidiumUnderstandingFor(config.dataDir) });
  registerRealtime(app, controlPlane);

  await app.listen({ host: config.host, port: config.port });
  app.log.info(
    {
      addresses: app.addresses(),
      data_dir: config.dataDir,
      token_file: access.path,
      journal: controlPlane.journal.info,
      runtimes: controlPlane.registry.descriptors().map((runtime) => runtime.runtime_id),
    },
    'control plane ready',
  );

  let stopping = false;
  const shutdown = async (signal: string) => {
    if (stopping) return;
    stopping = true;
    app.log.info({ signal }, 'shutting down');
    await app.close();
    await controlPlane.close();
  };
  process.once('SIGINT', () => void shutdown('SIGINT'));
  process.once('SIGTERM', () => void shutdown('SIGTERM'));
}

main().catch((error: unknown) => {
  process.stderr.write(
    `control plane failed to start: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
