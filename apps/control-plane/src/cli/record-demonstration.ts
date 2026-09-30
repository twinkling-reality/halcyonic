/**
 * Regenerates the XR client's demonstration from its plan and the committed mock scenarios:
 * `pnpm demonstration:record`. Run it after any change to the contracts, the pipeline, the mock
 * runtime, those scenarios or the plan, and review the diff. `--check` exits non-zero when the
 * committed demonstration is stale instead of rewriting it.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { loadScenarios } from '@halcyonic/integration-mock';
import {
  DEMONSTRATION_FILE,
  DEMONSTRATION_SCENARIOS,
  DEMONSTRATION_SOURCE,
  recordDemonstration,
} from '../fixtures/demonstration.ts';

async function main(): Promise<void> {
  const check = process.argv.includes('--check');
  const path = fileURLToPath(DEMONSTRATION_FILE);
  const demonstration = await recordDemonstration(
    loadScenarios(fileURLToPath(DEMONSTRATION_SCENARIOS)),
  );
  if (check) {
    const committed = await readFile(path, 'utf8').catch(() => '');
    if (committed !== demonstration) {
      process.stderr.write(`${path} is stale; run pnpm demonstration:record\n`);
      process.exitCode = 1;
    }
    return;
  }
  await writeFile(path, demonstration);
  const kib = Math.round(Buffer.byteLength(demonstration) / 1024);
  process.stdout.write(
    `wrote the demonstration of ${DEMONSTRATION_SOURCE} (${kib} KiB) to ${path}\n`,
  );
}

main().catch((error: unknown) => {
  process.stderr.write(
    `recording failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
