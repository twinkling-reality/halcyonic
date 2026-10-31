import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  bob,
  bonk,
  easeInBack,
  easeOutBack,
  hide,
  motion,
  pop,
  wiggle,
} from '../../../starters/iwsdk-vr/template/src/motion.ts';

/** ART7's values (art/STARTER-LOOK.md), exactly. */
const ART = {
  popMs: 320,
  popBack: 1.8,
  bobAmp: 0.03,
  bobPeriod: 1.6,
  squash: 0.04,
  wiggleAmp: 0.2,
  wiggleHz: 6,
  wiggleDecay: 5,
  bonkSquash: [1.25, 0.6],
  bonkMs: 90,
  settleMs: 420,
  hideMs: 200,
};

describe("the starter's motion: ART7's curves, pure functions of the time since they began (GQ1b)", () => {
  test('the values are ART7 exactly', () => {
    assert.deepEqual({ ...motion, bonkSquash: [...motion.bonkSquash] }, ART);
  });

  test('pop rises from 0.3 m below with a small overshoot and lands at 0; hide sinks to 0.3 m below', () => {
    assert.ok(Math.abs(pop(0) + 0.3) < 1e-9);
    const path = Array.from({ length: 33 }, (_, n) => pop((n * 10) / 1000));
    assert.ok(Math.max(...path) > 0, 'it overshoots, like a cork');
    assert.equal(pop(0.32), 0);
    assert.equal(pop(5), 0);
    assert.equal(hide(0), 0);
    assert.ok(Math.abs(hide(0.2) + 0.3) < 1e-9);
    assert.ok(Math.abs(hide(9) + 0.3) < 1e-9);
    assert.ok(Math.abs(easeOutBack(1) - 1) < 1e-9 && Math.abs(easeInBack(1) - 1) < 1e-9);
  });

  test('bob breathes slowly and keeps the volume', () => {
    for (const t of [0, 0.3, 0.8, 1.6, 7.25]) {
      const b = bob(t);
      assert.ok(Math.abs(b.y) <= 0.03 + 1e-9);
      assert.ok(Math.abs(b.sy * b.sxz * b.sxz - 1) < 1e-9, `volume at ${t}`);
      const later = bob(t + 1.6);
      assert.ok(Math.abs(later.y - b.y) < 1e-9, `period at ${t}`);
    }
  });

  test('wiggle is a shiver that dies out within half a second', () => {
    assert.equal(wiggle(0), 0);
    for (let t = 0.5; t < 2; t += 0.05) assert.ok(Math.abs(wiggle(t)) < 0.2 * 0.1, String(t));
  });

  test('bonk squashes to (1.25, 0.6) and springs back to exactly 1, nothing bouncing forever', () => {
    const squashed = bonk(0.09);
    assert.ok(Math.abs(squashed.sxz - 1.25) < 1e-9 && Math.abs(squashed.sy - 0.6) < 1e-9);
    assert.deepEqual(bonk(0), { sxz: 1, sy: 1 });
    assert.deepEqual(bonk(0.51), { sxz: 1, sy: 1 });
    assert.deepEqual(bonk(30), { sxz: 1, sy: 1 });
  });

  test('the same time gives the same answer, so a checked page moves the same way in every load', () => {
    assert.deepEqual(bob(2.25), bob(2.25));
    assert.deepEqual(bonk(0.2), bonk(0.2));
  });
});
