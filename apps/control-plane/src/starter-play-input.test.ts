import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  BUTTONS,
  Counts,
  DRAG_PIXELS,
  Edge,
  headsetSource,
  isDrag,
  LOOK,
  look,
  within,
  yawPitchOf,
} from '../../../starters/iwsdk-vr/template/src/play-input-core.ts';

// The starter's input layer (M1.8a): its SDK-free core runs here; the SDK side is checked in a browser by the
// host's check, since the repository does not install the starter's dependencies.
const template = new URL('../../../starters/iwsdk-vr/template/', import.meta.url);
const read = (path: string) => readFileSync(new URL(path, template), 'utf8');

describe("the IWSDK starter's input layer", () => {
  test('a held press counts once, and again only after it lifts', () => {
    const edge = new Edge();
    assert.deepEqual(
      [false, true, true, true, false, true].map((now) => edge.rise(now)),
      [false, true, false, false, false, true],
    );
  });

  test('counts each source on its own and hands out copies', () => {
    const counts = new Counts();
    counts.add('mouse');
    counts.add('mouse');
    counts.add('pinch');
    const snapshot = counts.snapshot();
    assert.deepEqual(snapshot, { mouse: 2, key: 0, trigger: 0, pinch: 1, touch: 0 });
    snapshot.mouse = 99;
    assert.equal(counts.snapshot().mouse, 2);
  });

  test('dragging right turns right, dragging up looks up, and the view never tips past its limit', () => {
    const turned = look(0, 0, 100, 0);
    assert.ok(turned.yaw < 0, 'yaw decreases to the right in three.js');
    assert.equal(turned.pitch, 0);
    assert.ok(look(0, 0, 0, -100).pitch > 0);
    assert.equal(look(0, 0, 0, -100_000).pitch, LOOK.pitchLimit);
    assert.equal(look(0, 0, 0, 100_000).pitch, -LOOK.pitchLimit);
  });

  test("reads a camera's yaw and pitch from the way it looks", () => {
    const ahead = yawPitchOf({ x: 0, y: 0, z: -1 });
    assert.ok(Math.abs(ahead.yaw) < 1e-9 && Math.abs(ahead.pitch) < 1e-9);
    assert.ok(Math.abs(yawPitchOf({ x: -1, y: 0, z: 0 }).yaw - Math.PI / 2) < 1e-9, 'looking left');
    assert.ok(Math.abs(yawPitchOf({ x: 0, y: 1, z: -1 }).pitch - Math.PI / 4) < 1e-9, 'looking up');
  });

  test('a small wobble is a click, a longer move is a drag', () => {
    assert.ok(!isDrag(DRAG_PIXELS, 0));
    assert.ok(isDrag(DRAG_PIXELS + 1, 0));
    assert.ok(isDrag(5, 5));
  });

  test('a touch is within the radius, inclusive', () => {
    assert.ok(within({ x: 0, y: 0, z: 0 }, { x: 0.12, y: 0, z: 0 }, 0.12));
    assert.ok(!within({ x: 0, y: 0, z: 0 }, { x: 0.1, y: 0.1, z: 0 }, 0.12));
  });

  test('a press on a headset is a trigger with a controller held, a pinch without', () => {
    assert.equal(headsetSource(true), 'trigger');
    assert.equal(headsetSource(false), 'pinch');
  });

  test('the two buttons are Space and E on a laptop, A and B on a headset', () => {
    assert.deepEqual(BUTTONS, {
      jump: { key: 'Space', headset: 'a-button' },
      action: { key: 'KeyE', headset: 'b-button' },
    });
  });

  test('a new project sets the layer up, and tells the agent to read every input through it, both ways', () => {
    assert.match(
      read('src/index.ts'),
      /setupPlayInput\(world\);\n {2}world\.registerSystem\(PanelSystem\);/,
    );
    const agents = read('AGENTS.md');
    assert.match(agents, /Every game must play on a laptop with a mouse and keyboard and in VR/);
    assert.match(agents, /Read every input through `src\/play-input\.ts`/);
    assert.match(agents, /Never add listeners to the window, document or canvas/);
  });

  test('the layer is the one place that listens to the canvas, and its check hook appears only with ?check', () => {
    const layer = read('src/play-input.ts');
    assert.match(layer, /if \(new URLSearchParams\(location\.search\)\.has\('check'\)\)/);
    assert.match(layer, /canvas\.addEventListener\('pointerdown'/);
    for (const file of ['src/index.ts', 'src/panel.ts', 'src/components.ts', 'src/assets.ts']) {
      assert.doesNotMatch(
        read(file),
        /(window|document|canvas|domElement)\.addEventListener/,
        file,
      );
    }
  });

  test('on a laptop the panel says how to play instead of offering a VR button that cannot work', () => {
    const panel = read('src/panel.ts');
    assert.match(panel, /navigator\.xr\?\.isSessionSupported\('immersive-vr'\)/);
    assert.match(
      panel,
      /xrButton\.setProperties\(\{ display: is2D && vrSupported \? 'flex' : 'none' \}\)/,
    );
    assert.match(read('public/ui/welcome.uikitml'), /id="laptop-help"/);
  });
});
