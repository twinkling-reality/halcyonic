/**
 * What the kit draws words on, for the person's computer to measure when it checks the app on a laptop: each mesh whose
 * canvas carries the kit's words, with the height of its smallest running words in the mesh's own metres. Words an app
 * draws on a canvas of its own are not here, so the check does not measure them. Kept free of imports so it can be
 * tested on its own.
 */

/** Where `pages()` stands its row: the panels' centres at this height, this far in front of the person by default. */
export const PAGES_EYE = 1.45;
export const PAGES_DISTANCE = 1.6;

/**
 * The laptop camera for a view from another app: at the person's own place, eye height at the origin, looking at the
 * middle of the pages' arc, so a laptop sees what the headset sees from 1.6 m. A game keeps the camera in
 * `iwsdk.config.json`, just behind the player.
 */
export const VIEW_CAMERA = {
  position: [0, 1.6, 0],
  lookAt: [0, PAGES_EYE, -PAGES_DISTANCE],
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
