/**
 * Everything a game takes from the starter, in one place: `import { critter, onSelect, scoreSign } from './kit.js';`.
 * Each name lives in its own file (`src/look.ts`, `src/decor.ts`, `src/motion.ts`, `src/palette.ts`,
 * `src/play-input.ts`, `src/worlds.ts`, `src/relief.ts`, `src/pages.ts`); this file only gathers them, so an import never
 * names the wrong one.
 */
export {
  balloon,
  basket,
  celebrate,
  critter,
  type DecorCounts,
  decorate,
  scoreSign,
  toyStar,
} from './decor.js';
export {
  ambientMotion,
  contactShadow,
  currentWorld,
  gameIsland,
  gameTime,
  ISLAND_CENTRE,
  ISLAND_RADIUS,
  PLAYER,
  setupLook,
  setupScene,
  TIER,
} from './look.js';
export { bob, bonk, drift, flap, flight, hide, motion, pop, pulse, sway, wiggle } from './motion.js';
export { type PaletteName, palette, toyMaterial } from './palette.js';
export { mode, moveAxis, onSelect, onTouch, pressed, setupPlayInput } from './play-input.js';
export { type WorldLook, type WorldName, worlds } from './worlds.js';
export { type Page, type Pages, type PagesOptions, pages } from './pages.js';
export { type HeightEncoding, type HeightGrid, type ReliefOptions, relief } from './relief.js';
