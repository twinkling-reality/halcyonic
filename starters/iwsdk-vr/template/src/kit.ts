/**
 * Everything a game takes from the starter, in one place: `import { critter, onSelect, scoreSign } from './kit.js';`.
 * Each name lives in its own file (`src/look.ts`, `src/decor.ts`, `src/motion.ts`, `src/palette.ts`,
 * `src/play-input.ts`, `src/worlds.ts`, `src/relief.ts`, `src/pages.ts`, and for music and light `src/instruments.ts`,
 * `src/score.ts`, `src/pieces.ts`, `src/glow.ts`, `src/places.ts`); this file only gathers them, so an import never
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
export { fitText } from './page-fit.js';
export { type Page, type Pages, type PagesOptions, MAX_PAGES, pages, TYPE } from './pages.js';
export { type HeightEncoding, type HeightGrid, type ReliefOptions, relief } from './relief.js';
export {
  type Music,
  type Player,
  type PlayOptions,
  pageMusic,
  playScore,
  type Voice,
  type VoiceName,
} from './instruments.js';
export { type Heard, isBlack, join, type Note, range, type Score, scoreSeconds } from './score.js';
export { type PieceName, pieces, pitch } from './pieces.js';
export {
  type FallingNotes,
  fallingNotes,
  type Glows,
  glows,
  type KeySpot,
  landing,
  type LightKeys,
  lightKeys,
  registerColour,
  type Sparks,
  sparks,
} from './glow.js';
export { darkHall, type Place } from './places.js';
export { type Button, type Rows, type RowsOptions, rows } from './rows.js';
