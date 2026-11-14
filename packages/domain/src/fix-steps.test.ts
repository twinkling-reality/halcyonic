import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { CheckFindings, CheckReading } from '@halcyonic/contracts';
import {
  checkServable,
  checkVerdict,
  fixSteps,
  readingFix,
  readingPasses,
  readingUnder,
} from './fix-steps.ts';

/** A check that passed: its page drew, nothing threw, and its laptop step read `reading`. */
function passed(reading?: CheckReading): CheckFindings {
  return {
    tsc: { exit: 0, seconds: 1 },
    build: { exit: 0, seconds: 2 },
    page: {
      exceptions: [],
      exception_count: 0,
      console_errors: 0,
      canvases: 1,
      edges_at_3s: 9000,
      edges_at_10s: 9000,
      drawn: true,
      pictures: [],
    },
    unknown: null,
    passed: true,
    reads_confined: true,
    laptop: {
      layer: 'starter',
      targets: 1,
      mouse: 'reached',
      key: 'reached',
      screen_changed: true,
      ...(reading === undefined ? {} : { reading }),
      ready: false,
    },
  };
}

const measured = (px: number): CheckReading => ({
  state: 'measured',
  smallest_px: px,
  measured: 3,
  fill_share: 0.05,
  floor_px: 10,
  last_measured_px: null,
  words: 'The words are too small to read on a laptop',
  detail: null,
});

/** A reading that could not be measured, with the last measured failure it holds, if any. */
const notMeasured = (state: 'unknown' | 'changed', held: number | null): CheckReading => ({
  state,
  smallest_px: null,
  measured: 0,
  fill_share: null,
  floor_px: 10,
  last_measured_px: held,
  words:
    "Not known whether it reads on a laptop: no text from the app's built-in pages could be measured",
  detail: null,
});

describe("the laptop reading goes back to its agent, a check's second trigger (ADR 0044, VRIFY 2026-11-13)", () => {
  test('words measured under the floor are a step to fix, whether or not the check passed', () => {
    assert.deepEqual(fixSteps(passed(measured(6))), ['reading']);
    const failed = { ...passed(measured(6)), tsc: { exit: 2, seconds: 1 }, passed: false };
    assert.deepEqual(fixSteps(failed), ['typecheck', 'reading']);
  });

  test('words at or over the floor, a small fill, or a reading merely not known is nothing to fix (the lead, AUDIT3)', () => {
    assert.deepEqual(fixSteps(passed(measured(10))), []);
    assert.deepEqual(fixSteps(passed(measured(14))), [], 'a fill share of 5% gates nothing');
    assert.deepEqual(fixSteps(passed()), [], 'a check recorded before the reading');
    for (const state of ['unknown', 'changed'] as const) {
      assert.equal(readingUnder(passed(notMeasured(state, null))), null, state);
      assert.equal(readingPasses(notMeasured(state, null)), true, state);
    }
  });

  test('a failure last measured and not measured since is still one: held, sent, and not passed (AUDIT3)', () => {
    for (const state of ['unknown', 'changed'] as const) {
      assert.deepEqual(fixSteps(passed(notMeasured(state, 6))), ['reading'], state);
      assert.equal(readingPasses(notMeasured(state, 6)), false, state);
    }
    assert.equal(
      readingFix(passed(notMeasured('changed', 6))),
      "Words were drawn 6 px high at a laptop's 1280 by 800 view when last measured; a person reads at least 10 px. The check can no longer measure them: a starter kit file that measures them (src/pages.ts, src/decor.ts, src/look.ts, src/legible.ts or the input layer) differs from the starter's own; put it back. Bring the pages nearer the laptop camera or make the words larger (TYPE in src/pages.ts), so the laptop reads as the headset does.",
    );
  });

  test("the agent is told the numbers, the floor and what to change, naming the kit's parts", () => {
    assert.equal(
      readingFix(passed(measured(6.04))),
      "Words are drawn 6 px high at a laptop's 1280 by 800 view; a person reads at least 10 px. Bring the pages nearer the laptop camera or make the words larger (TYPE in src/pages.ts), so the laptop reads as the headset does.",
    );
    assert.equal(readingFix(passed(measured(12))), null);
  });
});

describe("a check's whole answer beside passed (VRIFY, 2026-11-13: a reference the product could not make is never the app's failure)", () => {
  /** Everything seen passed; the starter's own picture could not be made, so whether it draws its own look is not known. */
  const unmade = (reading?: CheckReading): CheckFindings => {
    const seen = passed(reading);
    return {
      ...seen,
      page:
        seen.page === null
          ? null
          : { ...seen.page, drawn: null, own: { share: null, reference: 'unavailable' } },
      passed: false,
    };
  };

  test('only the own look not known reads not_known, never passed, and serves', () => {
    assert.equal(checkVerdict(unmade()), 'not_known');
    assert.equal(unmade().passed, false, 'passed keeps its meaning');
    assert.equal(checkServable({ ...unmade(), verdict: 'not_known' }), true);
    assert.equal(checkVerdict(passed()), 'passed');
    assert.equal(checkVerdict(passed(measured(14))), 'passed');
  });

  test('any failure the check measured or saw wins over what is not known (C1)', () => {
    const page = unmade().page;
    assert.ok(page !== null);
    const failures: CheckFindings[] = [
      { ...unmade(), page: { ...page, exceptions: ['Uncaught TypeError'], exception_count: 1 } },
      { ...unmade(), tsc: { exit: 2, seconds: 1 } },
      { ...unmade(), build: { exit: null, seconds: 180 } },
      { ...unmade(), page: { ...page, edges_at_3s: 0, edges_at_10s: 0, drawn: false } },
      { ...unmade(), page: null },
      unmade(measured(6)),
      {
        ...unmade(),
        refusals: [
          {
            kind: 'original_share_image',
            words: 'Make your own share picture.',
            file: 'public/og.png',
          },
        ],
      },
    ];
    for (const findings of failures) {
      assert.equal(checkVerdict(findings), 'failed', JSON.stringify(findings));
      assert.equal(checkServable({ ...findings, verdict: checkVerdict(findings) }), false);
    }
    // The laptop reading measured under its floor fails the whole answer, even where everything else passed.
    assert.equal(checkVerdict(passed(measured(6))), 'failed');
    assert.equal(passed(measured(6)).passed, true, 'passed keeps its meaning');
  });

  test('a check recorded before the verdict is served by passed alone', () => {
    assert.equal(checkServable(passed()), true);
    assert.equal(checkServable(unmade()), false);
  });
});
