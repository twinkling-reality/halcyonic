/**
 * What the kit draws words on and the panels it places, for the person's computer to measure when it checks the app on
 * a laptop: each mesh whose canvas carries the kit's words, with the height of its smallest running words in the mesh's
 * own metres, and each panel of the kit (a page, a row of buttons) with its size. Words an app draws on a canvas of its
 * own, and panels it places itself, are not here, so the check does not measure them. Kept free of imports so it can be
 * tested on its own.
 */

/** Where `pages()` stands its row: the panels' centres at this height, this far in front of the person by default. */
export const PAGES_EYE = 1.45;
export const PAGES_DISTANCE = 1.6;

/** Each page's size in metres, about a book page read at arm's length, and the gap between two side by side. */
export const PAGE_WIDTH = 0.64;
export const PAGE_HEIGHT = 0.8;
export const PAGE_GAP = 0.06;
/** How far a pressed page comes toward the person, and how much bigger it grows. */
export const PRESS_FORWARD = 0.32;
export const PRESS_GROW = 0.18;

/** How many pages `pages()` lays out at once: six round the person in a headset, three across a laptop's view. */
export const HEADSET_PAGES = 6;
export const LAPTOP_PAGES = 3;

/** How many pages are laid out at once in the session: in a headset or on a laptop. */
export function pagesAcross(headset: boolean): number {
  return headset ? HEADSET_PAGES : LAPTOP_PAGES;
}

/** Where a row of `across` stands after the session changes: from the first page of the group holding `first`. */
export function firstOf(first: number, across: number): number {
  return Math.floor(first / across) * across;
}

/**
 * What panel `index` of the row shows when the list takes `count` pages and the row starts at `first`, `across` at a
 * time: the list's index of its page, or null for a hidden panel, and its angle round the person (radians, 0 straight
 * ahead), the pages there are centred, each `PAGE_WIDTH` and `PAGE_GAP` apart at `distance`.
 */
export function arcSlot(
  index: number,
  first: number,
  across: number,
  count: number,
  distance: number,
): { readonly page: number | null; readonly angle: number } {
  const shown = Math.max(Math.min(count - first, across), 1);
  const step = (PAGE_WIDTH + PAGE_GAP) / distance;
  const page = index < across && first + index < count ? first + index : null;
  return { page, angle: (index - (shown - 1) / 2) * step };
}

/**
 * Where `rows()` stacks an app's rows of buttons: under the pages, at the pages' distance, the first row's top this high,
 * each row this tall and this far below the one above; each row faces the person's eye. Below a pressed page's reach and
 * inside a laptop's view for five rows (VRIFY, 2026-11-13).
 */
export const ROWS_TOP = 0.68;
export const ROW_HEIGHT = 0.08;
export const ROW_GAP = 0.03;
/** The eye the rows face: the person's own, at the origin. */
export const ROWS_FACE = 1.6;

/**
 * How far behind the person the laptop camera stands for a view from another app, and the height on the pages' plane it
 * looks at. From the person's own place (0.4 m nearer) no aim holds three pages, a pressed page and the rows at once:
 * the side pages' top corners reach past the edges at any aim low enough for the rows (VRIFY, 2026-11-13).
 */
export const VIEW_BACK = 0.4;
export const VIEW_AIM = 1.2;

/**
 * The laptop camera for a view from another app: at the person's eye height, a step behind them, looking down the
 * middle of the pages' arc at `VIEW_AIM`, so a laptop sees three pages, a pressed page and the rows under them at 1280
 * by 800 with the words over 10 pixels. A game keeps the camera in `iwsdk.config.json`, just behind the player, and the
 * headset its own place.
 */
export const VIEW_CAMERA = {
  position: [0, 1.6, VIEW_BACK],
  lookAt: [0, VIEW_AIM, -PAGES_DISTANCE],
} as const;

const worded = new Map<object, number>();

/** Notes that `mesh` carries the kit's words, the smallest `height` metres high in its own frame (the font's em). */
export function wordsOn(mesh: object, height: number): void {
  worded.set(mesh, height);
}

/** Every mesh the kit drew words on, with the height of its smallest words. */
export function wordedMeshes(): ReadonlyMap<object, number> {
  return worded;
}

/** A panel of the kit, for the check: a page or a row of buttons, its title or first label, its size in its own metres. */
export interface KitPanel {
  readonly kind: 'page' | 'row';
  readonly label: string | null;
  /** Width and height in the object's own frame, centred on its origin. */
  readonly width: number;
  readonly height: number;
}

const placed = new Map<object, KitPanel>();

/** Notes that `object` is a panel of the kit, `width` by `height` metres round its origin in its own frame. */
export function panelOn(object: object, panel: KitPanel): void {
  placed.set(object, panel);
}

/** Every panel the kit placed. */
export function kitPanels(): ReadonlyMap<object, KitPanel> {
  return placed;
}

export interface Projected {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

/** Whether a point projected to normalized device coordinates is inside the camera's view. */
export function inView(point: Projected): boolean {
  return point.z >= -1 && point.z <= 1 && Math.abs(point.x) <= 1 && Math.abs(point.y) <= 1;
}

/** The CSS pixels a segment covers on the canvas, from its two ends' projected heights and the canvas height. */
export function segmentPixels(bottomY: number, topY: number, canvasHeight: number): number {
  return (Math.abs(topY - bottomY) / 2) * canvasHeight;
}

/** A panel's box on the screen in CSS pixels: its left, top, width and height. */
export interface ScreenBox {
  readonly x: number;
  readonly y: number;
  readonly w: number;
  readonly h: number;
}

/** The farthest a box reaches either way, so a panel the camera cannot see whole reads as far off the screen. */
export const FAR_OFF = 10_000;

/**
 * The box on the screen round a panel's corners projected to normalized device coordinates, on a canvas at `left`,
 * `top`, `width` by `height` CSS pixels. A corner behind the camera or past its far plane makes the panel far off the
 * screen, never a box drawn through it.
 */
export function screenBox(
  corners: readonly Projected[],
  left: number,
  top: number,
  width: number,
  height: number,
): ScreenBox {
  if (corners.length === 0 || corners.some((corner) => corner.z < -1 || corner.z > 1))
    return { x: -FAR_OFF, y: -FAR_OFF, w: FAR_OFF, h: FAR_OFF };
  const xs = corners.map((corner) => left + ((corner.x + 1) / 2) * width);
  const ys = corners.map((corner) => top + ((1 - corner.y) / 2) * height);
  const clamp = (n: number) => Math.max(-FAR_OFF, Math.min(FAR_OFF, n));
  const x = clamp(Math.min(...xs));
  const y = clamp(Math.min(...ys));
  return {
    x,
    y,
    w: Math.min(FAR_OFF, clamp(Math.max(...xs)) - x),
    h: Math.min(FAR_OFF, clamp(Math.max(...ys)) - y),
  };
}
