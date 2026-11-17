import type { World } from '@iwsdk/core';

/**
 * A part of the app: one thing added to it (a field of flowers, lanterns, a lake), in a file of its own in
 * `src/parts/`, whose default export takes the world: `export default function lanterns(world: World): void`.
 */
export type Part = (world: World) => void;

/**
 * Every file in `src/parts/`, found by the build itself, so a part is added by writing its file and never by listing
 * it in a shared one: several tasks can each add a part to the same app, and their changes never touch the same lines.
 */
const found = import.meta.glob<{ default?: unknown }>('./parts/*.ts', { eager: true });

/**
 * Runs each part with the world, in file-name order, once the app's own setup in `src/index.ts` is done, so a part
 * stands in the world, the island and the decor the app chose. A file there that does not export a part by default
 * stops the app with its name, so the page's error says which file to mend.
 */
export function addParts(world: World): void {
  for (const path of Object.keys(found).sort()) {
    const part = found[path]?.default;
    if (typeof part !== 'function') {
      throw new Error(`${path.replace('./', 'src/')} must export by default a function that takes the world`);
    }
    (part as Part)(world);
  }
}
