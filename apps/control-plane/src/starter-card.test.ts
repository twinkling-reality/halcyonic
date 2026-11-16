import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  CARD_SECONDS,
  cardOpacity,
  FADE_IN,
  FADE_OUT,
} from '../../../starters/iwsdk-vr/template/src/card-fade.ts';

// The starter's welcome card (VRIFY, 2026-11-16): part of the place, never a white page cut from view. Its fade is
// SDK-free and runs here; the panel's SDK side is drawn by the host's check in a browser.
const template = new URL('../../../starters/iwsdk-vr/template/', import.meta.url);
const read = (path: string) => readFileSync(new URL(path, template), 'utf8');

describe("the IWSDK starter's welcome card", () => {
  const near = (actual: number, expected: number, message?: string) =>
    assert.ok(Math.abs(actual - expected) < 1e-9, `${message ?? ''} ${actual} is not ${expected}`);

  test('fades in, holds, and fades out by game time, ending at CARD_SECONDS; never a cut', () => {
    near(cardOpacity(0, 0), 0);
    near(cardOpacity(FADE_IN / 2, 0), 0.5);
    near(cardOpacity(FADE_IN, 0), 1);
    near(cardOpacity(CARD_SECONDS - FADE_OUT, 0), 1);
    near(cardOpacity(CARD_SECONDS - FADE_OUT / 2, 0), 0.5);
    near(cardOpacity(CARD_SECONDS, 0), 0);
    near(cardOpacity(CARD_SECONDS + 10, 0), 0);
    // H on a laptop shows it again from the moment it was pressed.
    near(cardOpacity(20, 20), 0);
    near(cardOpacity(20 + FADE_IN, 20), 1);
    near(cardOpacity(19, 20), 0, 'before it was shown');
    near(cardOpacity(Number.NaN, 0), 0);
    // Every step of a fade is small: no frame jumps from shown to gone.
    let last = cardOpacity(0, 0);
    for (let t = 1 / 72; t <= CARD_SECONDS + 1; t += 1 / 72) {
      const now = cardOpacity(t, 0);
      assert.ok(Math.abs(now - last) <= 1 / (72 * Math.min(FADE_IN, FADE_OUT)) + 1e-9, `t ${t}`);
      last = now;
    }
  });

  test('the panel fades by that function and drops the card only once it has faded', () => {
    const panel = read('src/panel.ts');
    assert.match(panel, /from '\.\/card-fade\.js'/);
    assert.match(panel, /cardOpacity\(time, this\.#shownAt\)/);
    assert.match(panel, /if \(time < this\.#shownAt \+ CARD_SECONDS\) return;/);
  });

  test("it is the kit's dark glass at the bottom left on a laptop, and low beside the person in a headset", () => {
    const card = read('public/ui/welcome.uikitml');
    assert.match(card, /background-color: rgba\(2, 3, 8, 0\.97\)/);
    assert.doesNotMatch(card, /#272727/, 'no dark-on-white text left');
    const scene = JSON.parse(read('public/scenes/main.iwsdk.scene.json')) as {
      nodes: {
        id: string;
        transform: Record<string, unknown>;
        components: Record<string, Record<string, string>>;
      }[];
    };
    const node = scene.nodes.find((each) => each.id === 'welcome-panel');
    assert.ok(node !== undefined);
    assert.deepEqual(node.components.ScreenSpace, {
      bottom: '20px',
      left: '20px',
      width: '22vw',
      height: '30%',
    });
    assert.deepEqual(node.transform, {
      position: [0.75, 1.05, -1.1],
      rotationDeg: [0, -35, 0],
      scale: 0.3,
    });
  });
});
