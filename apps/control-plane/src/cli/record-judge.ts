/**
 * Records a real run for the judge build: `pnpm judge:record <trace.jsonl> --root <project folder>
 * --home <home folder> --deny <file> [--out <file>] [--check]`.
 *
 * The trace is the run's journal exported as JSON Lines, read only, for example with
 * `sqlite3 'file:control-plane.db?immutable=1' "select envelope from events order by position"`.
 * The run is sanitized (`sanitizeRecordedRun`) and recorded (`recordRecordedRun`); if it carries
 * anything a judge must not read, nothing is written and every place is listed by event and field,
 * never by its text. `--deny` names a file of further words, one a line, kept out of the
 * repository: at least the owner's names, so it is required. `--check` writes nothing and reports whether the file at `--out` is that recording.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordRecordedRun } from '../fixtures/recorded-run.ts';
import { parseTrace } from '../fixtures/trace.ts';

/** Where the Unity project bundles the recorded run, as a text asset under Resources. */
const RECORDED_RUN_FILE = new URL(
  '../../../xr/Assets/Halcyonic/Resources/HalcyonicRecordedRun.json',
  import.meta.url,
);

function option(args: readonly string[], name: string): string | undefined {
  const at = args.indexOf(name);
  return at < 0 ? undefined : args[at + 1];
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const tracePath = args[0];
  const root = option(args, '--root');
  const home = option(args, '--home');
  const denyPath = option(args, '--deny');
  if (
    tracePath === undefined ||
    tracePath.startsWith('--') ||
    root === undefined ||
    home === undefined ||
    denyPath === undefined
  ) {
    throw new Error(
      'usage: pnpm judge:record <trace.jsonl> --root <project folder> --home <home folder> --deny <file> [--out <file>] [--check]',
    );
  }
  const deny = (await readFile(resolve(denyPath), 'utf8'))
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line !== '');
  const out = resolve(option(args, '--out') ?? fileURLToPath(RECORDED_RUN_FILE));
  const events = parseTrace(await readFile(resolve(tracePath), 'utf8'), tracePath);
  const recording = await recordRecordedRun(events, { projectRoot: root, home, deny });
  if (args.includes('--check')) {
    const committed = await readFile(out, 'utf8').catch(() => '');
    if (committed !== recording) {
      process.stderr.write(`${out} is not this run's recording; run pnpm judge:record\n`);
      process.exitCode = 1;
    }
    return;
  }
  await writeFile(out, recording);
  const kib = Math.round(Buffer.byteLength(recording) / 1024);
  process.stdout.write(
    `wrote the recorded run of ${events.length} events (${kib} KiB) to ${out}\n`,
  );
}

main().catch((error: unknown) => {
  process.stderr.write(
    `recording failed: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
