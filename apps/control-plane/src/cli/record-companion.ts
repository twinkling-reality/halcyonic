/**
 * Records the companion's part of the demonstration once, from the real companion on this computer:
 * `HALCYONIC_COMPANION_MODEL=<model> pnpm companion:record`. It asks the local model through the
 * same service, prompt and bounds the control plane uses, and never a hosted one. A model's words
 * differ from run to run, so a recording that breaks the rules (a brand a judge may not read, an
 * answer that is not a choice) is thrown away and asked again, up to five times. Review the diff:
 * the recording is committed, and `--check` only checks its rules, never its words.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { systemClock } from '@halcyonic/runtime-core';
import { Companion } from '../companion/companion.ts';
import { ConfigError, loadConfig } from '../config.ts';
import {
  COMPANION_DEMONSTRATION_FILE,
  type CompanionDemonstration,
  recordCompanionDemonstration,
  recordingProblems,
} from '../fixtures/companion-demonstration.ts';

const ATTEMPTS = 5;

async function main(): Promise<void> {
  const path = fileURLToPath(COMPANION_DEMONSTRATION_FILE);
  if (process.argv.includes('--check')) {
    const committed = JSON.parse(await readFile(path, 'utf8')) as CompanionDemonstration;
    const problems = recordingProblems(committed);
    if (problems.length > 0) {
      process.stderr.write(`${path} breaks its rules: ${problems.join('; ')}\n`);
      process.exitCode = 1;
    }
    return;
  }
  const config = loadConfig().companion;
  if (config === null) {
    throw new ConfigError('Name the companion model to record with in HALCYONIC_COMPANION_MODEL.');
  }
  for (let attempt = 1; attempt <= ATTEMPTS; attempt++) {
    // A companion of its own for each attempt, so its per-minute bound counts one recording. No one
    // waits on a recording, so a turn may wait out a task's long step on a shared model.
    const companion = new Companion({
      config,
      clock: systemClock,
      bounds: { firstTokenMs: 240_000, totalMs: 300_000 },
    });
    const recording = await recordCompanionDemonstration(companion, config.model, new Date());
    const problems = recordingProblems(recording);
    if (problems.length === 0) {
      await writeFile(path, `${JSON.stringify(recording, null, 2)}\n`);
      process.stdout.write(
        `wrote the companion's recorded exchange (${recording.turns.length} turns) to ${path}\n`,
      );
      return;
    }
    process.stderr.write(`attempt ${attempt} thrown away: ${problems.join('; ')}\n`);
  }
  throw new Error(`no recording kept its rules in ${ATTEMPTS} attempts`);
}

main().catch((error: unknown) => {
  process.stderr.write(
    `recording failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
