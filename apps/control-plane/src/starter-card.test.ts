import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  CARD_FROM,
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

  test('the first card waits for the place to open from black, and is gone by the same moment', () => {
    // The kit's places lift their black veil over game time 0.1 to 0.9 s; the card stays out of all of it.
    assert.ok(CARD_FROM >= 0.9);
    for (let t = 0; t < CARD_FROM; t += 1 / 72) near(cardOpacity(t, 0, CARD_FROM), 0, `t ${t}`);
    near(cardOpacity(CARD_FROM + FADE_IN / 2, 0, CARD_FROM), 0.5);
    near(cardOpacity(CARD_FROM + FADE_IN, 0, CARD_FROM), 1);
    near(cardOpacity(CARD_SECONDS - FADE_OUT, 0, CARD_FROM), 1);
    near(cardOpacity(CARD_SECONDS - FADE_OUT / 2, 0, CARD_FROM), 0.5);
    // Gone by CARD_SECONDS, before the check's own picture at 6 s, as without the wait.
    near(cardOpacity(CARD_SECONDS, 0, CARD_FROM), 0);
    assert.ok(CARD_SECONDS - CARD_FROM >= FADE_IN + FADE_OUT + 3, 'still held long enough to read');
  });

  test('the panel fades by that function and drops the card only once it has faded', () => {
    const panel = read('src/panel.ts');
    assert.match(panel, /from '\.\/card-fade\.js'/);
    assert.match(panel, /cardOpacity\(time, this\.#shownAt, this\.#from\)/);
    assert.match(panel, /#from = CARD_FROM;/);
    // Drawn in a group of its own, as the kit's Panel is, never the library's shared one (VRIFY, 2026-11-16).
    assert.match(panel, /class CardGlass extends MeshBasicMaterial \{\}/);
    assert.match(panel, /card\?\.setProperties\(\{ panelMaterialClass: CardGlass \}\);/);
    assert.match(panel, /this\.#shownAt = time;\n\s*this\.#from = 0;/);
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
