import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  landShape,
  pictureColours,
  swellAt,
} from '../../../starters/iwsdk-vr/template/src/memory.ts';

// The starter's reading of a person's own picture as a sky over a ground, as numbers: pictures are made here as RGBA
// pixel grids, as a canvas would hand them over.

type Rgb = readonly [number, number, number];

/** A `width` x `height` RGBA grid coloured row by row by `at(y / (height - 1))`, in 0..255. */
function picture(
  width: number,
  height: number,
  at: (t: number, x: number) => Rgb,
): Uint8ClampedArray {
  const out = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const c = at(y / (height - 1), x);
      out.set([c[0], c[1], c[2], 255], (y * width + x) * 4);
    }
  }
  return out;
}

const lerp = (a: Rgb, b: Rgb, t: number): Rgb => [
  a[0] + (b[0] - a[0]) * t,
  a[1] + (b[1] - a[1]) * t,
  a[2] + (b[2] - a[2]) * t,
];

const rgb = (hex: string): Rgb => [
  Number.parseInt(hex.slice(1, 3), 16),
  Number.parseInt(hex.slice(3, 5), 16),
  Number.parseInt(hex.slice(5, 7), 16),
];

/** A desert at noon: a blue sky paling to the horizon at 55% down, then pale sand darkening toward the person. */
const noon = (t: number): Rgb =>
  t < 0.55
    ? lerp([70, 130, 210], [205, 220, 235], t / 0.55)
    : lerp([200, 165, 115], [150, 110, 70], (t - 0.55) / 0.45);

describe('a picture read as a sky over a ground', () => {
  test('finds the horizon where the picture darkens most going down', () => {
    const c = pictureColours(picture(32, 60, noon), 32, 60);
    assert.ok(Math.abs(c.horizonAt - 0.55) < 0.04, `horizon at ${c.horizonAt}`);
  });

  test('takes the sky from above the horizon and the ground from below it', () => {
    const c = pictureColours(picture(32, 60, noon), 32, 60);
    const [tr, , tb] = rgb(c.top);
    assert.ok(tb > tr + 60, `the top is blue: ${c.top}`);
    const [hr, hg, hb] = rgb(c.horizon);
    assert.ok(hr > 170 && hg > 180 && hb > 190, `the horizon is pale: ${c.horizon}`);
    const [nr, , nb] = rgb(c.near);
    assert.ok(nr > nb + 40, `the near ground is warm sand: ${c.near}`);
    const far = rgb(c.far);
    const near = rgb(c.near);
    assert.ok(
      far[0] + far[1] + far[2] > near[0] + near[1] + near[2],
      'the far sand is lighter than the near',
    );
  });

  test('takes the light from the brightest part of the sky, wherever it is across', () => {
    const sun = (t: number, x: number): Rgb =>
      t < 0.3 && x >= 24 && x < 28 ? [255, 236, 190] : noon(t);
    const c = pictureColours(picture(32, 60, sun), 32, 60);
    const [r, g, b] = rgb(c.light);
    assert.ok(r > 240 && g > 220 && b > 170, `the light is the sun: ${c.light}`);
  });

  test('keeps a grey picture grey: no hue is made up', () => {
    const grey = (t: number): Rgb => {
      const v = t < 0.5 ? 200 - 60 * t : 90 - 40 * t;
      return [v, v, v];
    };
    const c = pictureColours(picture(16, 48, grey), 16, 48);
    for (const hex of [c.top, c.middle, c.horizon, c.far, c.near, c.light]) {
      const [r, g, b] = rgb(hex);
      assert.ok(Math.max(r, g, b) - Math.min(r, g, b) <= 1, `${hex} stays grey`);
    }
  });

  test('lifts a hue a little, never past full colour', () => {
    const flat = (t: number): Rgb => (t < 0.5 ? [120, 130, 150] : [110, 100, 90]);
    const c = pictureColours(picture(8, 24, flat), 8, 24);
    const [r, , b] = rgb(c.top);
    assert.ok(b - r > 30, `the top's blue is lifted from 30: ${c.top}`);
    assert.ok(b - r < 45, `but only by a quarter: ${c.top}`);
  });

  test('moved to dusk: a deep blue overhead, a warm glow at the horizon, the ground still its own warm sand', () => {
    const as = pictureColours(picture(32, 60, noon), 32, 60);
    const dusk = pictureColours(picture(32, 60, noon), 32, 60, { hour: 'dusk' });
    const [tr, tg, tb] = rgb(dusk.top);
    assert.ok(tr + tg + tb < 200 && tb > tr, `the top is deep blue: ${dusk.top}`);
    const [hr, , hb] = rgb(dusk.horizon);
    assert.ok(hr > hb, `the horizon is warm: ${dusk.horizon}`);
    const sum = (hex: string) => rgb(hex).reduce((a, v) => a + v, 0);
    assert.ok(sum(dusk.near) < sum(as.near), 'the near ground is a little dimmer');
    // Warmer, not cooler: red over blue grows (as a ratio, since dusk also dims it), so a golden video's sand stays
    // golden at dusk.
    const [ar, , ab] = rgb(as.near);
    const [nr, , nb] = rgb(dusk.near);
    assert.ok(
      nr / nb >= ar / ab,
      `the near ground stays as warm or warmer: ${as.near} to ${dusk.near}`,
    );
    assert.equal(dusk.horizonAt, as.horizonAt);
  });

  test('refuses a picture too small to have a sky and a ground', () => {
    assert.throws(() => pictureColours(new Uint8ClampedArray(4 * 4 * 4), 4, 4), RangeError);
    assert.throws(() => pictureColours(new Uint8ClampedArray(8), 4, 8), RangeError);
  });
});

describe('open land: long swells of ground round the person', () => {
  test('the person stands level on a crest, and the ground falls away from it along the wind', () => {
    const land = landShape({ crest: 1 });
    for (let a = 0; a < Math.PI * 2; a += 0.1) {
      for (const r of [0, 0.25, 0.5])
        assert.ok(
          Math.abs(land.height(Math.sin(a) * r, -Math.cos(a) * r)) < 1e-9,
          `level at r ${r}`,
        );
    }
    const [wx, wz] = land.wind;
    assert.ok(land.height(wx * 4, wz * 4) < -1, 'the lee falls away steeply');
    assert.ok(land.height(-wx * 4, -wz * 4) < -0.4, 'the windward side falls away gently');
    assert.ok(
      land.height(wx * 4, wz * 4) < land.height(-wx * 4, -wz * 4),
      'steeper on the lee than into the wind',
    );
  });

  test('the field lies below the person, great dunes rise past their eye far off, and no step anywhere', () => {
    const land = landShape({ crest: 1 });
    let lowest = 0;
    let highest = -99;
    for (let a = 0; a < Math.PI * 2; a += 0.02) {
      for (const r of [30, 60, 100]) {
        const h = land.height(Math.sin(a) * r, -Math.cos(a) * r);
        lowest = Math.min(lowest, h);
        highest = Math.max(highest, h);
      }
      let last = 0;
      for (let r = 0; r < 25; r += 0.01) {
        const h = land.height(Math.sin(a) * r, -Math.cos(a) * r);
        assert.ok(Math.abs(h - last) < 0.03, `no step at r ${r.toFixed(2)}: ${last} to ${h}`);
        last = h;
      }
    }
    assert.ok(lowest < -2, `troughs well under the person: ${lowest}`);
    assert.ok(highest > 4, `great dunes far off stand well over the eye (1.62 m): ${highest}`);
  });

  test('toward the sun the skyline stays under its disc, so the sun sets over open land, never behind a dune', () => {
    // The sun's disc stands 0.1 rad up (places.ts) with a 0.04 rad radius: the land under it keeps below 0.05 rad.
    for (const sun of [0.8, -0.4, 2]) {
      const land = landShape({ crest: 1, sun });
      let skyline = -1;
      for (let a = sun - 0.06; a <= sun + 0.06; a += 0.01) {
        for (let r = 15; r <= 115; r += 0.5) {
          const h = land.height(Math.sin(a) * r, -Math.cos(a) * r);
          skyline = Math.max(skyline, Math.atan2(h - 1.62, r));
        }
      }
      assert.ok(skyline < 0.05, `sun ${sun}: the land under the sun rises to ${skyline} rad`);
    }
  });

  test('a dune rises slowly into the wind and falls steeply after its crest; a soft hill does neither', () => {
    // Steepness from the trough up to the crest (at phase 0.78), against from the crest down to the next trough.
    const rise = (crest: number) => (swellAt(0.78, crest) - swellAt(0, crest)) / 0.78;
    const fall = (crest: number) => (swellAt(0.78, crest) - swellAt(1, crest)) / 0.22;
    assert.ok(fall(1) > rise(1) * 3, `dune: rise ${rise(1)}, fall ${fall(1)}`);
    for (let s = 0; s <= 1; s += 0.05) {
      assert.ok(
        Math.abs(swellAt(s, 0) - swellAt(1 - s, 0)) < 1e-9,
        'a soft hill is the same both ways',
      );
      assert.ok(swellAt(s, 1) >= 0 && swellAt(s, 1) <= 1);
    }
  });

  test('the same seed makes the same land, another seed another', () => {
    const a = landShape({ seed: 3 });
    const b = landShape({ seed: 3 });
    const c = landShape({ seed: 4 });
    assert.equal(a.height(20, -15), b.height(20, -15));
    assert.notEqual(a.height(20, -15), c.height(20, -15));
    const [wx, wz] = a.wind;
    assert.ok(Math.abs(Math.hypot(wx, wz) - 1) < 1e-12, 'the wind is a unit direction');
  });
});
