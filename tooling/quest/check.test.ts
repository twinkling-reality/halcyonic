import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { promisify } from 'node:util';

const run = promisify(execFile);
const CHECK = new URL('./check.ts', import.meta.url).pathname;

/** Made up, and planted in everything the stand-in says, so no line may repeat it. */
const TOKEN = 'tokentokentokentokentokentokentokentoken_-Z';

/** What the stand-in adb answers: each file's `stat` line, the logs, the headset's clock and how long the app has run. */
interface Headset {
  /** A file's `stat` line, or null where it is not there. */
  readonly files?: Readonly<Record<string, string | null>>;
  /** run-as can't reach the app's data folder. */
  readonly folderGone?: boolean;
  readonly log?: readonly string[];
  /** The main log's oldest stamp, in seconds. */
  readonly oldest?: number;
  readonly now?: number;
  readonly elapsed?: string;
}

const unity = (message: string) => `I/Unity   (1234): Halcyonic: ${message}`;
const NOTHING = unity("nothing was at the access token's old place on shared storage.");
const LIVE = unity('connection Live');
const TOKEN_FILE = '600 44 regular file';

/**
 * A stand-in for adb, as `HALCYONIC_ADB` names one: it answers the commands the check runs from the
 * scenario in `ADB_SCENARIO`, and as `stat` does where a file is not there.
 */
const STAND_IN = `
const scenario = JSON.parse(require('node:fs').readFileSync(process.env.ADB_SCENARIO, 'utf8'));
const args = process.argv.slice(2);
const said = args.join(' ');
const out = (text) => process.stdout.write(text + '\\n');
if (said === 'get-state') out('device');
else if (said === 'reverse --list') out('UsbFfs tcp:47800 tcp:47800');
else if (said === 'shell pidof com.halcyonic.xr') out('1234');
else if (said === 'logcat -d -v brief --pid=1234') out(scenario.log.join('\\n'));
else if (said === 'logcat -d -b main -v epoch')
  out('--------- beginning of main\\n' + scenario.oldest + '.000  1  1 I Other: ${TOKEN}');
else if (said === 'shell date +%s') out(String(scenario.now));
else if (said === 'shell ps -o ETIME -p 1234') out('     ELAPSED\\n' + scenario.elapsed);
else if (args[0] === 'shell' && said.includes("stat -c '%a %s %F'")) {
  const path = args[args.length - 1];
  if (args[1] === 'run-as' && scenario.folderGone) {
    process.stderr.write("run-as: couldn't stat /data/user/0/com.halcyonic.xr: No such file or directory\\n");
    process.exit(1);
  }
  if (typeof scenario.files[path] === 'string') out(scenario.files[path]);
  else {
    process.stderr.write("stat: '" + path + "': No such file or directory\\n");
    process.exit(1);
  }
} else {
  process.stderr.write('stand-in adb: not modelled: ' + said + '\\n');
  process.exit(2);
}
`;

describe('pnpm quest:check, against a stand-in adb', () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-quest-check-'));
  after(() => rmSync(directory, { recursive: true, force: true }));
  const adb = join(directory, 'adb');
  writeFileSync(adb, `#!${process.execPath}\n${STAND_IN}`);
  chmodSync(adb, 0o755);
  let scenarios = 0;

  /** Runs the check on `headset`, giving its exit code and lines. */
  async function check(headset: Headset, ...options: string[]) {
    const scenario = join(directory, `scenario-${scenarios++}.json`);
    writeFileSync(
      scenario,
      JSON.stringify({
        files: { 'files/access-token': TOKEN_FILE, ...headset.files },
        folderGone: headset.folderGone ?? false,
        log: [...(headset.log ?? [NOTHING, LIVE]), unity(`connection Refused: ${TOKEN}`), LIVE],
        oldest: headset.oldest ?? 1_700_000_000,
        now: headset.now ?? 1_700_000_100,
        elapsed: headset.elapsed ?? '00:50',
      }),
    );
    const env = { ...process.env, HALCYONIC_ADB: adb, ADB_SCENARIO: scenario };
    let code = 0;
    let stdout = '';
    try {
      ({ stdout } = await run(process.execPath, [CHECK, ...options], { env }));
    } catch (error) {
      const failed = error as { code?: number; stdout?: string };
      code = failed.code ?? -1;
      stdout = failed.stdout ?? '';
    }
    for (let at = 0; at + 8 <= TOKEN.length; at++) {
      assert.ok(!stdout.includes(TOKEN.slice(at, at + 8)), stdout);
    }
    return { code, lines: stdout.trim().split('\n') };
  }

  test('a session as it should be passes, nothing there read from the app', async () => {
    const { code, lines } = await check({});
    assert.equal(code, 0, lines.join('\n'));
    assert.ok(lines.includes('pass  run-as writes: no temporary file left over'), lines.join('\n'));
    assert.ok(lines.includes('pass  move: nothing was on shared storage (read as nothing there)'));
  });

  test('a write left before its move fails, by name', async () => {
    for (const name of ['files/access-token.tmp', 'files/glance-access-token.tmp']) {
      const { code, lines } = await check({ files: { [name]: TOKEN_FILE } });
      assert.equal(code, 1);
      assert.ok(
        lines.some((line) => line.startsWith(`FAIL  ${name} left over: the run-as write stopped`)),
        lines.join('\n'),
      );
    }
  });

  test('at the close, every leftover token file fails, and none passes', async () => {
    const leftovers = [
      'files/access-token.off',
      'files/access-token.new',
      'files/access-token.tmp',
      'files/glance-access-token.tmp',
    ];
    for (const name of leftovers) {
      const { code, lines } = await check(
        { files: { 'files/access-token': null, [name]: TOKEN_FILE } },
        '--closed',
      );
      assert.equal(code, 1, name);
      assert.ok(
        lines.includes(`FAIL  ${name}: still there; remove it (HEADSET_SESSION.md, "Close")`),
      );
    }
    const removed = await check({ files: { 'files/access-token': null } }, '--closed');
    assert.equal(removed.code, 0, removed.lines.join('\n'));
    for (const name of ['files/access-token', ...leftovers]) {
      assert.ok(removed.lines.includes(`pass  ${name}: removed`), name);
    }
  });

  test("where run-as can't reach the app's folder, nothing reads as removed", async () => {
    const { code, lines } = await check({ folderGone: true }, '--closed');
    assert.equal(code, 1);
    assert.ok(lines.includes('FAIL  files/access-token: could not be looked at'), lines.join('\n'));
    assert.ok(!lines.some((line) => line.endsWith(': removed')), lines.join('\n'));
  });

  test("the move is judged only from the app's line, and the log's reach says why not", async () => {
    // The log began after the app started: its start, and any line it wrote then, are gone.
    const lost = await check({ log: [LIVE], oldest: 1_700_000_080 });
    assert.ok(
      lost.lines.includes(
        "pass  move: can't be judged from this log, since the headset's log no longer holds the app's start; to judge it, restart the app and run the check at once",
      ),
      lost.lines.join('\n'),
    );
    const expected = await check({ log: [LIVE], oldest: 1_700_000_080 }, '--move', 'nothing');
    assert.equal(expected.code, 1);
    // The log holds the start, yet the app said nothing of the move: a build older than its line.
    const older = await check({ log: [LIVE] });
    assert.ok(
      older.lines.some((line) => line.includes('the app wrote no line about it at its start')),
      older.lines.join('\n'),
    );
    // Elapsed time unread: the reach is unknown, never taken as reaching.
    const unread = await check({ log: [LIVE], elapsed: 'bad' });
    assert.ok(unread.lines.some((line) => line.includes('could not be shown to hold the app')));
  });
});
