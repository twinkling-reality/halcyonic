import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { bob, ease, pop, spin, wiggle } from '../../../starters/iwsdk-vr/template/src/motion.ts';

const near = (a: number, b: number) => Math.abs(a - b) < 1e-9;

describe("the starter's motion helpers: pure functions of the time a system is given (GQ1a)", () => {
  test('bob, wiggle and spin repeat with their period and stay within their size', () => {
    for (const t of [0, 0.37, 1.5, 6, 59.9]) {
      assert.ok(near(bob(t, 0.2, 2), bob(t + 0.5, 0.2, 2)), `bob at ${t}`);
      assert.ok(Math.abs(bob(t, 0.2, 2)) <= 0.2 + 1e-9);
      assert.ok(near(wiggle(t, 12, 3), wiggle(t + 1 / 3, 12, 3)), `wiggle at ${t}`);
      assert.ok(Math.abs(wiggle(t, 12, 3)) <= (12 * Math.PI) / 180 + 1e-9);
      assert.ok(near(Math.sin(spin(t, 0.25)), Math.sin(spin(t + 4, 0.25))), `spin at ${t}`);
    }
    assert.equal(bob(0), 0);
    assert.equal(wiggle(0), 0);
    assert.equal(spin(0), 0);
  });

  test('pop grows to its size and settles back to exactly 1, and is 1 before a hit', () => {
    assert.equal(pop(-1), 1);
    assert.equal(pop(0), 1);
    const peak = Math.max(...Array.from({ length: 31 }, (_, n) => pop(n / 100)));
    assert.ok(peak > 1.3 && peak <= 1.4 + 1e-9, String(peak));
    assert.equal(pop(0.3), 1);
    assert.equal(pop(5), 1);
  });

  test('ease runs from 0 to 1, never back, and holds outside 0 to 1', () => {
    assert.equal(ease(0), 0);
    assert.equal(ease(1), 1);
    assert.equal(ease(-2), 0);
    assert.equal(ease(3), 1);
    let last = -1;
    for (let n = 0; n <= 20; n += 1) {
      const value = ease(n / 20);
      assert.ok(value >= last, String(n));
      last = value;
    }
  });

  test('the same time gives the same answer, so a checked page moves the same way in every load', () => {
    assert.equal(bob(2.25, 0.1, 1), bob(2.25, 0.1, 1));
    assert.equal(pop(0.12), pop(0.12));
  });
});
