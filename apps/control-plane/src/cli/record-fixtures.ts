/**
 * Regenerates the traces in fixtures/traces from their plans. Run it after any change to the
 * contracts, the pipeline or the scenarios, and review the diff.
 * `--check` exits non-zero when a committed trace is stale instead of rewriting it.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { loadConfig } from '../config.ts';
import { TRACE_PLANS } from '../demo-plan.ts';
import { recordTrace } from '../fixtures/record.ts';

const TRACES_DIR = new URL('../../../../fixtures/traces/', import.meta.url);

async function main(): Promise<void> {
  const check = process.argv.includes('--check');
  for (const plan of TRACE_PLANS) {
    const path = fileURLToPath(new URL(plan.file, TRACES_DIR));
    const trace = await recordTrace(plan, loadConfig().scenariosDir);
    if (check) {
      const committed = await readFile(path, 'utf8').catch(() => '');
      if (committed !== trace) {
        process.stderr.write(`${path} is stale; run pnpm fixtures:record\n`);
        process.exitCode = 1;
      }
      continue;
    }
    await writeFile(path, trace);
    process.stdout.write(`wrote ${trace.split('\n').length - 1} events to ${path}\n`);
  }
}

main().catch((error: unknown) => {
  process.stderr.write(
    `recording failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
