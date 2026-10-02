import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { CompanionReply } from '@halcyonic/contracts';
import {
  COMPANION_DEMONSTRATION_FILE,
  COMPANION_DEMONSTRATION_IDEA,
  type CompanionDemonstration,
  recordingProblems,
} from './companion-demonstration.ts';

const ASK: CompanionReply = {
  next: 'ask',
  line: 'A page of race times fits well.',
  view: 'unclear',
  question: { text: 'Who enters the times?', choices: ['Each runner', 'One organiser'] },
};
const PROPOSE: CompanionReply = {
  next: 'propose',
  line: 'That is clear enough to start.',
  view: 'clear',
  proposal: { project_name: 'Club Race Times', first_task: 'Create one web page of race times.' },
};

function recording(turns: CompanionDemonstration['turns']): CompanionDemonstration {
  return {
    version: 1,
    source: 'test',
    model: 'local-model:tag',
    recorded_at: '2026-10-02T12:00:00.000Z',
    start: 'idea',
    turns,
  };
}

const GOOD = recording([
  { from: 'person', text: COMPANION_DEMONSTRATION_IDEA },
  { from: 'companion', reply: ASK },
  { from: 'person', text: 'One organiser' },
  { from: 'companion', reply: PROPOSE },
]);

/** A turn of the good recording, which has four. */
function at(index: number): CompanionDemonstration['turns'][number] {
  const turn = GOOD.turns[index];
  assert.ok(turn);
  return turn;
}

describe("the companion's recorded exchange", () => {
  test('keeps to its rules: the idea, answers that were offered, and a proposal at the end', () => {
    assert.deepEqual(recordingProblems(GOOD), []);
    const askedForRecap = recording([
      { from: 'person', text: COMPANION_DEMONSTRATION_IDEA },
      { from: 'companion', reply: ASK },
      { from: 'companion', reply: PROPOSE },
    ]);
    assert.deepEqual(recordingProblems(askedForRecap), []);
  });

  test('refuses an answer that was not offered, a proposal before the end, or no proposal', () => {
    const typed = recording([at(0), at(1), { from: 'person', text: 'Someone else' }, at(3)]);
    assert.ok(
      recordingProblems(typed).some((problem) => problem.includes('not one of the choices')),
    );
    const early = recording([
      at(0),
      { from: 'companion', reply: PROPOSE },
      { from: 'person', text: 'x' },
      at(3),
    ]);
    assert.ok(recordingProblems(early).length > 0);
    const unfinished = recording(GOOD.turns.slice(0, 3));
    assert.ok(
      recordingProblems(unfinished).some((problem) => problem.includes('ends with a proposal')),
    );
  });

  test('refuses a brand a judge may not read, wherever it appears', () => {
    const branded = recording([
      at(0),
      { from: 'companion', reply: { ...ASK, line: 'A Google Sheet would do, or a page.' } },
      at(2),
      at(3),
    ]);
    assert.deepEqual(recordingProblems(branded), ['it names Google']);
    // A word that only contains one is not the brand.
    const word = recording([
      at(0),
      { from: 'companion', reply: { ...ASK, line: 'Machines and macaroni are fine.' } },
      at(2),
      at(3),
    ]);
    assert.deepEqual(recordingProblems(word), []);
  });

  test('the committed recording, once there is one, keeps its rules', () => {
    const path = fileURLToPath(COMPANION_DEMONSTRATION_FILE);
    if (!existsSync(path)) return;
    const committed = JSON.parse(readFileSync(path, 'utf8')) as CompanionDemonstration;
    assert.deepEqual(recordingProblems(committed), []);
  });
});
