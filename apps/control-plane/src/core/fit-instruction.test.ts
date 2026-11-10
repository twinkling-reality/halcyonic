import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { fitInstruction } from './command-service.ts';

describe("the instruction a fit gives the task's agent (ADR 0045)", () => {
  test('names the marked files and those holding the project version, and nothing when none met', () => {
    assert.equal(fitInstruction([]), null);
    const text = fitInstruction([
      { path: 'src/a.ts', kind: 'text' },
      { path: 'img.png', kind: 'binary' },
      { path: 'old.txt', kind: 'deleted' },
    ]);
    assert.ok(text !== null);
    assert.match(text, /In src\/a\.ts, each place where both changed is marked/);
    assert.match(text, /<<<<<<<, ======= and >>>>>>>/);
    assert.match(text, /img\.png, old\.txt could not be marked and now hold the project's version/);
    assert.doesNotMatch(text, /\u2014/);
  });
});
