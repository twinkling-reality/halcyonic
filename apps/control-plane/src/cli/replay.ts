/**
 * Serves a recorded trace as a fixture journal: `pnpm replay <trace.jsonl> [--instant]`.
 *
 * The journal lives in memory and is marked `fixture`, so every client can label the data as
 * development fixtures. No runtimes are registered: replayed executions are history, and
 * commands against them are rejected rather than pretending to reach a runtime.
 */
import { chmod, mkdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import { loadConfig } from '../config.ts';
import { ControlPlane } from '../core/control-plane.ts';
import { parseTrace, replayTrace } from '../fixtures/trace.ts';
import { registerRealtime } from '../http/realtime.ts';
import { registerRoutes } from '../http/routes.ts';
import { loadOrCreateAccessToken } from '../http/security.ts';
import { createHttpServer } from '../http/server.ts';
import { createUuidV7Generator } from '../ids.ts';
import { salidiumUnderstandingFor } from '../intelligence/understanding.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const tracePath = args.find((arg) => !arg.startsWith('--'));
  if (tracePath === undefined) {
    throw new Error('usage: pnpm replay <trace.jsonl> [--instant]');
  }
  const pace = args.includes('--instant') ? 'instant' : 'recorded';
  const events = parseTrace(await readFile(resolve(tracePath), 'utf8'), tracePath);

  const config = loadConfig();
  await mkdir(config.dataDir, { recursive: true, mode: 0o700 });
  await chmod(config.dataDir, 0o700);
  const access = await loadOrCreateAccessToken(config.dataDir);
  const app = await createHttpServer({ logLevel: config.logLevel, token: access.token });
  const ids = createUuidV7Generator();
  const controlPlane = new ControlPlane({
    journal: openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids }),
    adapters: [],
    ids,
    clock: systemClock,
    scheduler: systemScheduler,
    logger: app.log,
    commandTimeoutMs: config.commandTimeoutMs,
  });
  registerRoutes(app, controlPlane, { understanding: salidiumUnderstandingFor(config.dataDir) });
  registerRealtime(app, controlPlane);
  await app.listen({ host: config.host, port: config.port });
  app.log.info(
    {
      addresses: app.addresses(),
      trace: tracePath,
      events: events.length,
      pace,
      journal: controlPlane.journal.info,
    },
    'serving fixture replay',
  );

  const stop = new AbortController();
  const shutdown = async () => {
    stop.abort();
    await app.close();
    await controlPlane.close();
  };
  process.once('SIGINT', () => void shutdown());
  process.once('SIGTERM', () => void shutdown());

  try {
    const result = await replayTrace(controlPlane.recorder, events, {
      pace,
      maxGapMs: 3000,
      scheduler: systemScheduler,
      signal: stop.signal,
    });
    app.log.info(result, 'replay complete; serving the final state until interrupted');
  } catch (error) {
    if (!stop.signal.aborted) throw error;
  }
}

main().catch((error: unknown) => {
  process.stderr.write(
    `replay failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
