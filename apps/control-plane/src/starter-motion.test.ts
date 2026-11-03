import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  ambient,
  bob,
  bonk,
  drift,
  easeInBack,
  easeOutBack,
  flap,
  flight,
  hide,
  motion,
  pop,
  sway,
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

/** Samples a curve every 1/60 s over `seconds`. */
const frames = (seconds: number) =>
  Array.from({ length: Math.round(seconds * 60) }, (_, n) => n / 60);

describe("the world's own motion: small, slow and never a jump (U3)", () => {
  test('a breeze sways flowers and grass a little, and crosses the island rather than moving all at once', () => {
    for (const t of frames(6)) {
      assert.ok(Math.abs(sway(t, 0.4, -1.2)) <= ambient.swayAmp + 1e-9, `small at ${t}`);
      assert.ok(
        Math.abs(sway(t + ambient.swayPeriod, 0.4, -1.2) - sway(t, 0.4, -1.2)) < 1e-9,
        `periodic at ${t}`,
      );
    }
    // One place leads another: the same moment differs across a metre of island.
    assert.ok(Math.abs(sway(1, 0, 0) - sway(1, 1, 0)) > 0.01);
    // A tree's amplitude and period are its own.
    for (const t of frames(9))
      assert.ok(
        Math.abs(sway(t, 2, -1, ambient.treeAmp, ambient.treePeriod)) <= ambient.treeAmp + 1e-9,
      );
  });

  test('a cloud wanders a few metres from its home and back, slowly, with no jump', () => {
    let last = drift(0, 0.7);
    for (const t of frames(ambient.cloudPeriod)) {
      const d = drift(t, 0.7);
      assert.ok(
        Math.abs(d.x) <= ambient.cloudDrift + 1e-9 && Math.abs(d.y) <= ambient.cloudLift + 1e-9,
        String(t),
      );
      // Under 2 cm a frame: at 15 m away, less than a tenth of a degree.
      assert.ok(Math.hypot(d.x - last.x, d.y - last.y) < 0.02, `no jump at ${t}`);
      last = d;
    }
    const later = drift(13 + ambient.cloudPeriod, 0.7);
    assert.ok(Math.abs(later.x - drift(13, 0.7).x) < 1e-9);
    // Seen within 3 s: a cloud moves at least 0.3 m in any 3 s near the middle of its wander.
    assert.ok(Math.abs(drift(3, 0).x - drift(0, 0).x) >= 0.3);
  });

  test("a butterfly's wings beat quickly, open wide and close nearly shut, never past upright", () => {
    const angles = frames(2).map((t) => flap(t, 0.5));
    assert.ok(Math.min(...angles) >= 0.15 - 1e-9 && Math.max(...angles) <= 1.2 + 1e-9);
    assert.ok(Math.max(...angles) - Math.min(...angles) > 0.9, 'a full beat');
    assert.ok(
      Math.abs(flap(0.3 + 1 / ambient.flapHz, 0.5) - flap(0.3, 0.5)) < 1e-9,
      'at its own rate',
    );
  });

  test('a butterfly loops near its spot and heads the way it flies', () => {
    const reach = 0.5;
    for (const t of frames(ambient.flightPeriod)) {
      const here = flight(t, 1.1, reach);
      assert.ok(
        Math.abs(here.x) <= reach + 1e-9 && Math.abs(here.z) <= reach + 1e-9,
        `near its spot at ${t}`,
      );
      assert.ok(Math.abs(here.y) <= 0.15 + 1e-9, `a gentle rise and dip at ${t}`);
      const next = flight(t + 1e-4, 1.1, reach);
      const course = Math.atan2(next.x - here.x, next.z - here.z);
      const off = Math.atan2(Math.sin(course - here.heading), Math.cos(course - here.heading));
      assert.ok(Math.abs(off) < 0.01, `heading along its path at ${t}`);
    }
    const a = flight(2, 1.1, reach);
    const b = flight(2 + ambient.flightPeriod, 1.1, reach);
    assert.ok(Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z) < 1e-9, 'one loop, again and again');
  });
});
