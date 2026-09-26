/**
 * Regenerates fixtures/traces/multiple_workstreams.jsonl from the demo plan. Run it after any
 * change to the contracts, the pipeline or the scenarios, and review the diff.
 * `--check` exits non-zero when the committed trace is stale instead of rewriting it.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { loadConfig } from '../config.ts';
import { recordDemoTrace } from '../fixtures/record.ts';

export const DEMO_TRACE_PATH = fileURLToPath(
  new URL('../../../../fixtures/traces/multiple_workstreams.jsonl', import.meta.url),
);

async function main(): Promise<void> {
  const trace = await recordDemoTrace(loadConfig().scenariosDir);
  if (process.argv.includes('--check')) {
    const committed = await readFile(DEMO_TRACE_PATH, 'utf8').catch(() => '');
    if (committed !== trace) {
      process.stderr.write(`${DEMO_TRACE_PATH} is stale; run pnpm fixtures:record\n`);
      process.exitCode = 1;
    }
    return;
  }
  await writeFile(DEMO_TRACE_PATH, trace);
  process.stdout.write(`wrote ${trace.split('\n').length - 1} events to ${DEMO_TRACE_PATH}\n`);
}

main().catch((error: unknown) => {
  process.stderr.write(
    `recording failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
