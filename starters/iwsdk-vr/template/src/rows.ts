import type { World } from '@iwsdk/core';
import {
  CanvasTexture,
  Mesh,
  MeshBasicMaterial,
  Object3D,
  PlaneGeometry,
  SRGBColorSpace,
} from 'three';
import {
  PAGES_DISTANCE,
  panelOn,
  ROW_GAP,
  ROW_HEIGHT,
  ROWS_FACE,
  ROWS_TOP,
  wordsOn,
} from './legible.js';
import { gameIsland } from './look.js';
import { TYPE } from './pages.js';
import { onSelect } from './play-input.js';

/** One button of a row: the words on it and what pressing it does (a click on a laptop, the trigger or a pinch in VR). */
export interface Button {
  readonly label: string;
  readonly onSelect: () => void;
}

export interface RowsOptions {
  /** The buttons' colour, as the original paints its buttons; cream by default. */
  readonly color?: string;
  /** The words' colour; near black by default. */
  readonly ink?: string;
  /** The colour of a lit button, the original's own accent. */
  readonly accent?: string;
}

export interface Rows {
  /** Each row's buttons, row by row, already pressable. */
  readonly buttons: readonly (readonly Mesh[])[];
  /** Gives button `index` of row `row` new words ("4 to 6 of 14", a letter's case); the row is laid out again. */
  rename(row: number, index: number, label: string): void;
  /** Lights button `index` of row `row`, the choice the person made, and the rest of that row not; -1 lights none. */
  light(row: number, index: number): void;
}

/** Canvas pixels per metre, as the pages draw. */
const PIXELS = 1600;
/** The labels' size: the kit's titles, read 1.6 m away, the distance the rows stand at. */
const LABEL_PX = Math.round(TYPE.title * PIXELS);
/** Room either side of a label, the narrowest button, and the gap between two buttons, in metres. */
const PAD = 0.025;
const NARROWEST = 0.1;
const BUTTON_GAP = 0.02;
const FONT = 'system-ui, -apple-system, "Segoe UI", sans-serif';

/**
 * Places an app's rows of buttons (its choices, tabs, a pager's Back and Next, a keyboard's letters) under the pages,
 * one row under the other at the kit's own spacing, each centred and facing the person: inside what a laptop shows and
 * clear of a page while it is pressed. Each button's words are the kit's title size, and each press calls its
 * `onSelect` through `onSelect` of `./play-input.js`, so it works with a hand and with a mouse. Pass every row the app
 * has, in the order they stand, top first; never place a row of your own.
 */
export function rows(world: World, list: readonly (readonly Button[])[], options: RowsOptions = {}): Rows {
  const color = options.color ?? '#FFF8EC';
  const ink = options.ink ?? '#2B2118';
  const accent = options.accent ?? '#E8743B';
  const measure = document.createElement('canvas').getContext('2d');
  const labels = list.map((row) => row.map((button) => button.label));
  const lit = list.map(() => -1);
  const anchors: Object3D[] = [];
  const buttons = list.map((row, r) => {
    const y = ROWS_TOP - ROW_HEIGHT / 2 - r * (ROW_HEIGHT + ROW_GAP);
    // Tipped back to face the person's eye, so a row read from above stands as tall as one at eye height.
    const tilt = -Math.atan2(ROWS_FACE - y, PAGES_DISTANCE);
    const anchor = new Object3D();
    anchor.position.set(0, y, -PAGES_DISTANCE);
    anchor.rotation.x = tilt;
    gameIsland().add(anchor);
    anchors.push(anchor);
    return row.map((button) => {
      const mesh = new Mesh(
        new PlaneGeometry(NARROWEST, ROW_HEIGHT),
        new MeshBasicMaterial({ toneMapped: false, transparent: true }),
      );
      mesh.position.set(0, y, -PAGES_DISTANCE);
      mesh.rotation.x = tilt;
      gameIsland().add(mesh);
      // Its words are `TYPE.title` high in the button's own metres, for the computer's check of a laptop's view.
      wordsOn(mesh, TYPE.title);
      onSelect(world.createTransformEntity(mesh), () => button.onSelect());
      return mesh;
    });
  });

  /** A label's width in metres at the labels' size, and the button's width round it. */
  const widthOf = (label: string) => {
    if (measure === null) return NARROWEST;
    measure.font = `600 ${LABEL_PX}px ${FONT}`;
    return Math.max(NARROWEST, measure.measureText(label).width / PIXELS + PAD * 2);
  };

  /** Draws row `r` again and lays it out centred: each button as wide as its words, a gap between. */
  const lay = (r: number) => {
    const row = buttons[r] ?? [];
    const words = labels[r] ?? [];
    const widths = row.map((_, i) => widthOf(words[i] ?? ''));
    const whole = widths.reduce((sum, w) => sum + w, 0) + BUTTON_GAP * Math.max(0, row.length - 1);
    let x = -whole / 2;
    row.forEach((mesh, i) => {
      const w = widths[i] ?? NARROWEST;
      mesh.geometry.dispose();
      mesh.geometry = new PlaneGeometry(w, ROW_HEIGHT);
      mesh.position.x = x + w / 2;
      x += w + BUTTON_GAP;
      const material = mesh.material as MeshBasicMaterial;
      material.map?.dispose();
      material.map = drawButton(words[i] ?? '', w, i === lit[r], color, ink, accent);
      material.needsUpdate = true;
    });
    const anchor = anchors[r];
    // For the computer's check of a laptop's view: the row as one panel, named by its first button's words.
    if (anchor !== undefined)
      panelOn(anchor, { kind: 'row', label: words[0] ?? null, width: whole, height: ROW_HEIGHT });
  };
  list.forEach((_, r) => lay(r));

  return {
    buttons,
    rename(row, index, label) {
      const words = labels[row];
      if (words === undefined || index < 0 || index >= words.length) return;
      words[index] = label;
      lay(row);
    },
    light(row, index) {
      if (lit[row] === undefined || lit[row] === index) return;
      lit[row] = index;
      lay(row);
    },
  };
}

/** One button on a canvas: a rounded face in the button's colour, or the accent when lit, its words centred. */
function drawButton(
  label: string,
  width: number,
  lit: boolean,
  color: string,
  ink: string,
  accent: string,
): CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(width * PIXELS);
  canvas.height = Math.round(ROW_HEIGHT * PIXELS);
  const texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  texture.anisotropy = 4;
  const context = canvas.getContext('2d');
  if (context === null) return texture;
  const radius = canvas.height * 0.3;
  context.fillStyle = lit ? accent : color;
  context.beginPath();
  context.roundRect(0, 0, canvas.width, canvas.height, radius);
  context.fill();
  context.fillStyle = lit ? color : ink;
  context.font = `600 ${LABEL_PX}px ${FONT}`;
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.fillText(label, canvas.width / 2, canvas.height / 2);
  texture.needsUpdate = true;
  return texture;
}
