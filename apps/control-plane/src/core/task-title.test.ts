import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { charactersOf, MADE_TITLE_LENGTH, taskTitleOf, UNNAMED_TASK } from './task-title.ts';

/** The one table of cases the headset's rule (NewWorkDraft.TitleOf) is held to as well. */
const CASES = JSON.parse(
  readFileSync(new URL('../../../../fixtures/titles/cases.json', import.meta.url), 'utf8'),
) as { name: string; text: string; title: string }[];

describe("a task title made from the person's words", () => {
  for (const each of CASES) {
    test(each.name, () => {
      assert.equal(taskTitleOf(each.text), each.title);
    });
  }

  test('is never empty, never over sixty characters, and holds no ellipsis Halcyonic added', () => {
    for (const each of CASES) {
      const title = taskTitleOf(each.text);
      assert.notEqual(title, '');
      assert.ok(charactersOf(title).length <= MADE_TITLE_LENGTH, each.name);
      assert.ok(!title.includes('…'), each.name);
    }
    assert.equal(taskTitleOf(''), UNNAMED_TASK);
  });

  // Held in each suite, since a JSON reader may turn an unpaired surrogate into U+FFFD before the rule sees it.
  test('drops an unpaired surrogate', () => {
    assert.equal(taskTitleOf('Fix \ud800the login'), 'Fix the login');
    assert.equal(taskTitleOf('Fix the login\udc00'), 'Fix the login');
  });

  test('counts what a person sees, never UTF-16 units', () => {
    const at60 = CASES.find((each) => each.name.startsWith('an emoji at the sixtieth'));
    assert.ok(at60 !== undefined);
    assert.equal(charactersOf(at60.title).length, MADE_TITLE_LENGTH);
    assert.ok(
      at60.title.length > MADE_TITLE_LENGTH,
      'the case holds a pair a unit count would cut',
    );
  });
});
