import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { spokenNames, tidySpeech } from './tidy.ts';
import { PROMPT_WORDS } from './whisper.ts';

const NAMES = spokenNames(['Space Garden']);
const tidy = (text: string) => tidySpeech(text, NAMES);

describe('tidying spoken words', () => {
  test('hesitation sounds go, with the pauses around them', () => {
    for (const [heard, tidied] of [
      ['Um, make the sky blue.', 'Make the sky blue.'],
      ['Make it, uh, bigger.', 'Make it bigger.'],
      ['Make it bigger, um.', 'Make it bigger.'],
      ['Make it bigger, um', 'Make it bigger'],
      ['Make it... erm... bigger.', 'Make it bigger.'],
      ['Sure. Um, make it red.', 'Sure. Make it red.'],
      ['Hmm. Erm, add a door. Uhm, and a window.', 'Add a door. And a window.'],
      ['UMM, UHH, stop.', 'Stop.'],
    ] as const) {
      assert.equal(tidy(heard), tidied, heard);
    }
  });

  test('words that only look like hesitation are kept', () => {
    for (const heard of [
      'Uh-oh, the build broke.',
      'Bring the umbrella and the drum.',
      'Er, which one? Ah, the red one.',
      'Make it like the old one, you know, so it fits.',
      "Show the hmm's count.",
      'Make the gap 5 mm wider.',
      'Set the margin to 12 mm, then rebuild.',
    ]) {
      assert.equal(tidy(heard), heard);
    }
  });

  test('a short word said twice in a row is said once', () => {
    for (const [heard, tidied] of [
      ['Put the the tree on the left.', 'Put the tree on the left.'],
      ['I I think so.', 'I think so.'],
      ['The the the end.', 'The end.'],
      ['Add it to to the list, um, the the list.', 'Add it to the list the list.'],
    ] as const) {
      assert.equal(tidy(heard), tidied, heard);
    }
  });

  test('words that may be meant twice are kept', () => {
    for (const heard of [
      'No, no, the other one.',
      'Make it very very big.',
      'I know that that works.',
      'It had had a door.',
      'What it is is a game.',
      'The, the tree.',
      'Plug it in in the kitchen.',
      'Turn it on on Monday.',
    ]) {
      assert.equal(tidy(heard), heard);
    }
  });

  test("Halcyonic's names and the projects' names are spelled as written", () => {
    for (const [heard, tidied] of [
      ['Ask opencode to do it.', 'Ask OpenCode to do it.'],
      ['Ask Open-Code, then opencode.', 'Ask OpenCode, then OpenCode.'],
      ['Use claude code and codex.', 'Use Claude Code and Codex.'],
      ["Show halcyonic's room.", "Show Halcyonic's room."],
      ['Um, open space garden.', 'Open Space Garden.'],
      ['Open spacegarden and SPACE-GARDEN.', 'Open Space Garden and Space Garden.'],
    ] as const) {
      assert.equal(tidy(heard), tidied, heard);
    }
  });

  test('a name matches only whole words with the same letters', () => {
    for (const heard of [
      'Two codexes and the halcyonics.',
      'Open the space, then the garden.',
      'An opener coded it.',
      'Ask Open Codes.',
      'Open code review in the editor.',
    ]) {
      assert.equal(tidy(heard), heard);
    }
  });

  test('a name written joined matches only joined or hyphenated, never across a space', () => {
    const names = spokenNames(['ToDo']);
    for (const [heard, tidied] of [
      ['I need to do this first.', 'I need to do this first.'],
      ['Open the todo and the to-do.', 'Open the ToDo and the ToDo.'],
      ['Open code review, then open-code.', 'Open code review, then OpenCode.'],
      ['Ask claudecode and claude-code.', 'Ask Claude Code and Claude Code.'],
    ] as const) {
      assert.equal(tidySpeech(heard, names), tidied, heard);
    }
  });

  test('the longest name wins where names overlap', () => {
    const names = spokenNames(['Claude', 'code', 'Claude Code Club']);
    assert.equal(
      tidySpeech('claude code club and claude code', names),
      'Claude Code Club and Claude Code',
    );
    assert.equal(tidySpeech('ask claude about the code', names), 'ask Claude about the code');
  });

  test('a one-word project name is spelled as its person wrote it', () => {
    assert.equal(tidySpeech('water the garden', spokenNames(['Garden'])), 'water the Garden');
  });

  test('tidying twice changes nothing more', () => {
    for (const heard of [
      'Um, ask open code to, uh, fix the the space garden.',
      'Sure. Hmm, I I think, um... yes.',
      'Uh-oh, no, no.',
    ]) {
      const once = tidy(heard);
      assert.equal(tidy(once), once, heard);
    }
  });

  test('only hesitation leaves nothing, for the caller to show the words as heard', () => {
    assert.equal(tidy('Um.'), '');
    assert.equal(tidy('Uh, um...'), '');
  });

  test("a filler beside anything but a pause or a sentence's end is kept", () => {
    for (const heard of ['He said "um" twice.', 'Say (uh) once.']) {
      assert.equal(tidy(heard), heard);
    }
  });
});

describe('the names a draft is tidied with', () => {
  test("Halcyonic's own words first, then each project's name once", () => {
    assert.deepEqual(spokenNames(['Space Garden', 'codex', 'space garden', 'Pond']), [
      ...PROMPT_WORDS,
      'Space Garden',
      'Pond',
    ]);
  });

  test('blank, wordless and over-long names are left out, and at most 200 are kept', () => {
    const many = Array.from({ length: 300 }, (_, index) => `Project ${index}`);
    const names = spokenNames(['  ', '!!!', 'x'.repeat(61), ' Padded ', ...many]);
    assert.equal(names.length, 200);
    assert.deepEqual(names.slice(PROMPT_WORDS.length, PROMPT_WORDS.length + 2), [
      'Padded',
      'Project 0',
    ]);
  });
});
