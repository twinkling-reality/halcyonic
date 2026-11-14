import assert from 'node:assert/strict';
import { test } from 'node:test';
import { OWN_LOOK_NOT_KNOWN, PARTLY_CHECKED } from '../preview/check.ts';
import { agoWords, checkStateOf, checkWords, looksLikeRecords, statusWords } from './fact-words.ts';

// The two answers the 30B model gave in the window at 04:40Z on 2026-11-12: right, and read like a printout.
const WINDOW_QUESTION =
  "Yes. The Mac's latest check for the project is recorded as passed:\n\n* check state: passed\n* type_check: passed\n* build: passed\n* page: errors 0, first_errors [], showed_its_own_picture: true\n\nRecorded at 2026-11-12T04:44:49.108Z, files_since_check: current.";
const WINDOW_CHANGE =
  'The agent says it changed the sphere material to blue:\n- `src/index.ts` `MeshStandardMaterial` color: `0x00ff88` to `0x0088ff`\nThe check: type_check: passed, build: passed, files_since_check: current.';

test("an answer that reads like the computer's records is found, by an identifier, a bracketed list or a time", () => {
  assert.equal(looksLikeRecords(WINDOW_QUESTION), 'time');
  assert.equal(looksLikeRecords(WINDOW_CHANGE), 'identifier');
  assert.equal(looksLikeRecords('Errors: [] so far.'), 'bracketed list');
  assert.equal(looksLikeRecords('It ran at 2026-11-12T04:44.'), 'time');
  assert.equal(looksLikeRecords('The waiting_for_person count is 0.'), 'identifier');
});

test('a plain answer passes, as do paths, file names and the words the request itself holds', () => {
  assert.equal(
    looksLikeRecords(
      "Yes. It type-checks, it builds, and the page drew with no errors. The files haven't changed since.",
    ),
    null,
  );
  assert.equal(looksLikeRecords('The agent changed src/game_loop.ts and my_scene.json.'), null);
  assert.equal(
    looksLikeRecords('Night_sky is running.', ['How is night_sky doing?']),
    null,
    "the person's own words",
  );
});

test("the host's facts are said in sentences, never as codes", () => {
  const now = new Date('2026-11-12T05:00:00.000Z');
  const said = checkWords(
    {
      state: 'passed',
      type_check: 'passed',
      build: 'passed',
      page: { errors: 0, showed_its_own_picture: true },
      files_since_check: 'current',
      recorded_at: '2026-11-12T04:44:49.108Z',
    },
    now,
  );
  assert.equal(
    said,
    "The computer's check passed: the type check passed, the build passed, the page opened with no errors and drew the scene. The files have not changed since. It ran 15 minutes ago.",
  );
  assert.equal(looksLikeRecords(said), null);
  assert.equal(
    checkWords({ state: 'not_checked' }, now),
    'The computer has not checked these files.',
  );
  assert.equal(statusWords('completed'), 'its last turn ended');
  assert.equal(statusWords('turn_ended'), 'its last turn ended');
  assert.equal(statusWords('waiting_for_human'), 'waiting for the person');
  assert.equal(statusWords('something new'), 'not known');
  assert.equal(agoWords('2026-11-12T04:59:40.000Z', now), 'just now');
  assert.equal(agoWords('2026-11-12T02:00:00.000Z', now), '3 hours ago');
});

test('a check whose own look is not known is Partly Checked, its line and the finding under it, never failed (VRIFY, 2026-11-13)', () => {
  const now = new Date('2026-11-13T05:00:00.000Z');
  const said = checkWords(
    {
      state: 'not_known',
      type_check: 'passed',
      build: 'passed',
      page: { errors: 0, showed_its_own_picture: null },
      unknown: OWN_LOOK_NOT_KNOWN,
      files_since_check: 'current',
    },
    now,
  );
  assert.equal(
    said,
    "The computer's check: Partly Checked. What was checked passed; one thing could not be checked. Not known whether it draws its own look: the check could not make the starter's picture to compare it with. Press Check to try again. The type check passed, the build passed and the page opened with no errors. The files have not changed since.",
  );
  assert.deepEqual(PARTLY_CHECKED, {
    label: 'Partly Checked',
    line: 'What was checked passed; one thing could not be checked.',
  });
  assert.doesNotMatch(said, /fail|drew nothing/i);
  const findings = {
    tsc: { exit: 0, seconds: 1 },
    build: { exit: 0, seconds: 1 },
    page: null,
    unknown: null,
    passed: false,
    reads_confined: true,
  };
  assert.equal(checkStateOf({ ...findings, verdict: 'not_known' }), 'not_known');
  assert.equal(checkStateOf({ ...findings, verdict: 'failed' }), 'failed');
  assert.equal(
    checkStateOf(findings),
    'failed',
    'a check recorded before the verdict reads by passed',
  );
  assert.equal(checkStateOf({ ...findings, passed: true, verdict: 'passed' }), 'passed');
});
