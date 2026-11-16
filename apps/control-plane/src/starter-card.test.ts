import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  CARD_FROM,
  CARD_SECONDS,
  cardOpacity,
  cardWait,
  FADE_IN,
  FADE_OUT,
  FIRST_CARD_END,
  MIN_HOLD,
} from '../../../starters/iwsdk-vr/template/src/card-fade.ts';
import { TWIN_FRAMES } from './preview/laptop-probe.ts';

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

  test('the first card waits for the place to open from black, and has gone by FIRST_CARD_END', () => {
    // The kit's places lift their black veil over game time 0.1 to 0.9 s; the card stays out of all of it.
    assert.ok(CARD_FROM >= 0.9);
    const first = (t: number, ready: number | null) =>
      cardOpacity(t, 0, cardWait(0, CARD_FROM, ready), FIRST_CARD_END);
    for (let t = 0; t < CARD_FROM; t += 1 / 72) near(first(t, 0), 0, `t ${t}`);
    near(first(CARD_FROM + FADE_IN / 2, 0), 0.5);
    near(first(CARD_FROM + FADE_IN, 0), 1);
    near(first(FIRST_CARD_END - FADE_OUT, 0), 1);
    near(first(FIRST_CARD_END - FADE_OUT / 2, 0), 0.5);
    near(first(FIRST_CARD_END, 0), 0);
    assert.ok(FIRST_CARD_END - CARD_FROM >= FADE_IN + FADE_OUT + 2, 'held long enough to read');
  });

  test('a card waits for its words: no empty box, and no blink when they come too late', () => {
    const first = (t: number, ready: number | null) =>
      cardOpacity(t, 0, cardWait(0, CARD_FROM, ready), FIRST_CARD_END);
    // Words not ready: no card at all.
    for (let t = 0; t <= CARD_SECONDS; t += 0.25) near(first(t, null), 0, `t ${t}`);
    // Ready before the wait is over: the wait stands.
    assert.equal(cardWait(0, CARD_FROM, 0.3), CARD_FROM);
    // Ready later: it fades in from then, still gone by FIRST_CARD_END.
    near(first(1.5 + FADE_IN, 1.5), 1);
    near(first(FIRST_CARD_END, 1.5), 0);
    // Too late to fade in, hold MIN_HOLD and fade out: no card, never a blink.
    const late = FIRST_CARD_END - (FADE_IN + MIN_HOLD + FADE_OUT) + 0.05;
    for (let t = 0; t <= CARD_SECONDS; t += 1 / 72) near(first(t, late), 0, `t ${t}`);
    // H's card: its words were ready long before, and it stays CARD_SECONDS.
    assert.equal(cardWait(20, 0, 1.2), 0);
    near(cardOpacity(20 + CARD_SECONDS - FADE_OUT, 20, 0, CARD_SECONDS), 1);
  });

  test('whenever its words are ready, the first card holds at least MIN_HOLD or never shows, and no twin frame of the check sees it (AUDIT, AUDIT3)', () => {
    // The check's twin loads compare whole frames from TWIN_FRAMES[0] on: the card is gone a little before, read from
    // the check's own constant so the coupling breaks loudly if either side moves.
    const twinsFrom = TWIN_FRAMES[0] / 60;
    const step = 1 / 120;
    for (let ready = 0; ready <= 6 + 1e-9; ready += 0.05) {
      const wait = cardWait(0, CARD_FROM, ready);
      let full = 0;
      let shown = false;
      for (let t = 0; t <= CARD_SECONDS + 1; t += step) {
        const opacity = cardOpacity(t, 0, wait, FIRST_CARD_END);
        if (opacity > 0) shown = true;
        if (opacity >= 1) full += step;
        if (t >= twinsFrom - 0.1)
          near(opacity, 0, `ready ${ready}: seen at ${t} s, in the twins' window`);
      }
      assert.ok(!shown || full >= MIN_HOLD - 2 * step, `ready ${ready}: held ${full} s`);
    }
  });

  test('the panel fades by that function and drops the card only once it has faded', () => {
    const panel = read('src/panel.ts');
    assert.match(panel, /from '\.\/card-fade\.js'/);
    assert.match(
      panel,
      /cardOpacity\(time, this\.#shownAt, cardWait\(this\.#shownAt, this\.#from, this\.#readyAt\), this\.#end\)/,
    );
    assert.match(
      panel,
      /if \(this\.#readyAt === null && wordsReady\(this\.#card\)\) this\.#readyAt = time;/,
    );
    assert.match(panel, /fontSignal/);
    assert.match(panel, /#from = CARD_FROM;/);
    assert.match(panel, /#end = FIRST_CARD_END;/);
    assert.match(
      panel,
      /this\.#shownAt = time;\n\s*this\.#from = 0;\n\s*this\.#end = CARD_SECONDS;/,
    );
    // Drawn in a group of its own, as the kit's Panel is, never the library's shared one (VRIFY, 2026-11-16).
    assert.match(panel, /class CardGlass extends MeshBasicMaterial \{\}/);
    assert.match(panel, /card\?\.setProperties\(\{ panelMaterialClass: CardGlass \}\);/);
    assert.match(panel, /if \(time < this\.#shownAt \+ this\.#end\) return;/);
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
