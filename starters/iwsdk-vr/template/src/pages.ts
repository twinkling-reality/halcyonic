import {
  CanvasTexture,
  Color,
  Group,
  Mesh,
  MeshBasicMaterial,
  PlaneGeometry,
  SRGBColorSpace,
  type Texture,
} from 'three';
import { ambientMotion, gameIsland, gameTime } from './look.js';
import { fitText, wrapLines } from './page-fit.js';

/** One of the original's own cards, steps or pages: its title, its words and, if it has one, its picture. */
export interface Page {
  readonly title: string;
  readonly text: string;
  /** A picture the original shows on this card, named in `src/assets.ts` and read with `AssetManager.getTexture`. */
  readonly picture?: Texture;
}

export interface PagesOptions {
  /** The card's colour, as the original paints its cards; cream by default. */
  readonly color?: string;
  /** The words' colour; near black by default. */
  readonly ink?: string;
  /** The colour of the band along each card's top, the original's own accent. */
  readonly accent?: string;
  /** How far in front of the person the row stands, in metres; 1.6 by default. */
  readonly distance?: number;
}

export interface Pages {
  /** The row, already in the scene; move or turn it as a whole if the view needs it. */
  readonly group: Group;
  /** The row's six flat panels: wrap each in an entity to press it. Panel `i` shows page `first + i`, if any. */
  readonly panels: readonly Mesh[];
  /** Brings panel `index` forward and lights it, the others back; on the game's own clock. */
  show(index: number): void;
  /** The list's index of the page the first panel shows: 0, then 6, 12 and on as the row turns. */
  readonly first: number;
  /**
   * How many pages the list takes now, so a person can be told where they are ("7 to 12 of 14"): a page whose words do
   * not fit goes on to pages of its own title after it, so this can be more than the list's length.
   */
  readonly count: number;
  /**
   * Lays out the next six pages (`turn(1)`) or the previous six (`turn(-1)`), so a long list is read to its end;
   * give the person a Next and a Back to press. Returns false, changing nothing, past either end.
   */
  turn(by: 1 | -1): boolean;
  /** Shows another list in the same row from its first page: what a search or a filter leaves, say. */
  set(list: readonly Page[]): void;
  /**
   * The index, in the list given, of the entry panel `index` shows: map a press back to your own data with it, never
   * with `first + index`, since a long entry takes more than one page. Undefined for a hidden panel.
   */
  source(index: number): number | undefined;
}

/** A page as laid out: the page drawn and the index of the entry it comes from in the list given. */
interface Laid {
  readonly page: Page;
  readonly source: number;
}

/**
 * The kit's type scale: font sizes in metres for words read 1.6 m away, the distance `pages` stands at; scale them
 * with the distance they are read from (twice as far, twice as big). Running words are never drawn smaller than
 * `words`, and a title takes `title` with its own line below it. On a canvas of `p` pixels per metre the size in
 * pixels is `TYPE.words * p`.
 */
export const TYPE = { title: 0.04, words: 0.026 } as const;

/** Each page's size in metres: about a book page, read at arm's length. */
const WIDTH = 0.64;
const HEIGHT = 0.8;
/** Canvas pixels per metre: 1600, sharp enough to read 3 cm letters in a headset. */
const PIXELS = 1600;
/** The panels' centres, at the height a standing person reads at. */
const EYE = 1.45;
/** The most pages laid out at once: more would widen the arc past where a person reads without turning. */
export const MAX_PAGES = 6;
/** How far the pressed page comes toward the person, and how much bigger it grows. */
const FORWARD = 0.32;
const GROW = 0.18;

/**
 * Lays the original's own cards out as readable pages in a shallow arc in front of the person, facing them, each drawn
 * from its title, words and picture on a canvas of its own. Pressing one should answer: call `show(i)` from
 * `onSelect`, and that page comes forward and brightens while the rest step back, on the game's own clock.
 * Six (`MAX_PAGES`) are laid out at a time; `turn(1)` and `turn(-1)` lay out the next and previous six, so pass the
 * whole list, and `set(list)` shows another (what a search or a filter leaves).
 */
export function pages(list: readonly Page[], options: PagesOptions = {}): Pages {
  const color = options.color ?? '#FFF8EC';
  const ink = options.ink ?? '#2B2118';
  const accent = options.accent ?? '#E8743B';
  const distance = options.distance ?? 1.6;
  const group = new Group();
  // Side by side with a 6 cm gap, bent round the person so each faces them.
  const step = (WIDTH + 0.06) / distance;
  let items = laid(list);
  let first = 0;
  const panels = Array.from({ length: MAX_PAGES }, () => {
    const panel = new Mesh(
      new PlaneGeometry(WIDTH, HEIGHT),
      new MeshBasicMaterial({ toneMapped: false }),
    );
    group.add(panel);
    return panel;
  });
  let chosen = -1;
  let chosenAt = Number.NEGATIVE_INFINITY;
  /** Draws the six pages from `first` on and centres the ones there are; a panel with no page is hidden. */
  const lay = () => {
    const across = Math.max(Math.min(items.length - first, MAX_PAGES), 1);
    chosen = -1;
    chosenAt = Number.NEGATIVE_INFINITY;
    panels.forEach((panel, index) => {
      const page = items[first + index]?.page;
      const material = panel.material as MeshBasicMaterial;
      material.map?.dispose();
      material.map = page === undefined ? null : drawPage(page, color, ink, accent);
      material.needsUpdate = true;
      panel.visible = page !== undefined;
      const angle = (index - (across - 1) / 2) * step;
      panel.position.set(Math.sin(angle) * distance, EYE, -Math.cos(angle) * distance);
      panel.rotation.y = -angle;
      panel.userData.home = panel.position.clone();
    });
  };
  lay();
  gameIsland().add(group);
  const bright = new Color('#FFFFFF');
  const dim = new Color('#B8B2A8');
  ambientMotion(() => {
    // Half a second from one choice to the next, eased, on the game's clock.
    const t = Math.min(1, Math.max(0, (gameTime() - chosenAt) / 0.5));
    const eased = 1 - (1 - t) ** 3;
    panels.forEach((panel, index) => {
      const home = panel.userData.home as { x: number; y: number; z: number };
      const near = index === chosen ? eased : 0;
      const towards = Math.hypot(home.x, home.z) || 1;
      panel.position.set(
        home.x - (home.x / towards) * FORWARD * near,
        home.y,
        home.z - (home.z / towards) * FORWARD * near,
      );
      // A hidden panel has no size, so a pointer passes through it and presses nothing.
      panel.scale.setScalar(panel.visible ? 1 + GROW * near : 0);
      const material = panel.material as MeshBasicMaterial;
      material.color.copy(chosen === -1 || index === chosen ? bright : dim);
    });
  });
  return {
    group,
    panels,
    show(index) {
      if (index === chosen || index < 0 || index >= panels.length || !panels[index]?.visible) return;
      chosen = index;
      chosenAt = gameTime();
    },
    get first() {
      return first;
    },
    get count() {
      return items.length;
    },
    turn(by) {
      const next = first + by * MAX_PAGES;
      if (next < 0 || next >= items.length) return false;
      first = next;
      lay();
      return true;
    },
    set(next) {
      items = laid(next);
      first = 0;
      lay();
    },
    source(index) {
      return panels[index]?.visible ? items[first + index]?.source : undefined;
    },
  };
}

/** The page's canvas size, its margin and its type, as `drawPage` draws and `laid` measures. */
const MARGIN = 64;
const FONT = 'system-ui, -apple-system, "Segoe UI", sans-serif';
const TITLE_PX = Math.round(TYPE.title * PIXELS);
const WORDS_PX = Math.round(TYPE.words * PIXELS);
const LEADING = Math.round(WORDS_PX * 1.33);
const CANVAS = { w: Math.round(WIDTH * PIXELS), h: Math.round(HEIGHT * PIXELS) };

/** Where a page's words start below its title (at most two lines, marked when cut), and how many lines it holds. */
function room(context: CanvasRenderingContext2D, title: string, picture: boolean) {
  context.font = `700 ${TITLE_PX}px ${FONT}`;
  const all = wrapLines(title, CANVAS.w - MARGIN * 2, (text) => context.measureText(text).width);
  const titles = all.slice(0, 2).map((each) => each.line);
  // A title past two lines is marked as cut, never dropped without a sign.
  if (all.length > 2) titles[1] = `${(titles[1] ?? '').replace(/\s*\S*$/, '')}\u2026`;
  const top = MARGIN + 24 + titles.length * Math.round(TITLE_PX * 1.22) + 18;
  const pictureHeight = picture ? Math.round(CANVAS.h * 0.36) : 0;
  const lines = Math.max(0, Math.floor((CANVAS.h - MARGIN - pictureHeight - top) / LEADING));
  return { titles, top, pictureHeight, lines };
}

/**
 * The list as the pages it takes: a page whose words do not fit is followed by pages of the same title holding the
 * rest, its picture on the first, so nothing is cut off and `turn()` reaches every word.
 */
function laid(list: readonly Page[]): Laid[] {
  const context = document.createElement('canvas').getContext('2d');
  if (context === null) return list.map((page, source) => ({ page, source }));
  return list.flatMap((page, source) => {
    const firstRoom = room(context, page.title, page.picture !== undefined).lines;
    const restRoom = room(context, page.title, false).lines;
    context.font = `400 ${WORDS_PX}px ${FONT}`;
    const measure = (text: string) => context.measureText(text).width;
    const texts = fitText(page.text, CANVAS.w - MARGIN * 2, measure, firstRoom, restRoom);
    return texts.map((text, index) => ({
      page: index === 0 ? { ...page, text } : { title: page.title, text },
      source,
    }));
  });
}

/** One page on a canvas: the accent band, the title, the words wrapped to the page and the picture below them. */
function drawPage(page: Page, color: string, ink: string, accent: string): CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = CANVAS.w;
  canvas.height = CANVAS.h;
  const texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  texture.anisotropy = 4;
  const context = canvas.getContext('2d');
  if (context === null) return texture;
  context.fillStyle = color;
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = accent;
  context.fillRect(0, 0, canvas.width, 28);
  const picture = page.picture?.image as CanvasImageSource | undefined;
  const fits = room(context, page.title, picture !== undefined);
  context.fillStyle = ink;
  context.textBaseline = 'top';
  context.font = `700 ${TITLE_PX}px ${FONT}`;
  fits.titles.forEach((line, index) => {
    context.fillText(line, MARGIN, MARGIN + 24 + index * Math.round(TITLE_PX * 1.22));
  });
  // The words fit: `laid` gave the page no more than it holds and put the rest on the pages after it.
  context.font = `400 ${WORDS_PX}px ${FONT}`;
  wrapLines(page.text, canvas.width - MARGIN * 2, (text) => context.measureText(text).width)
    .slice(0, fits.lines)
    .forEach((each, index) => {
      context.fillText(each.line, MARGIN, fits.top + index * LEADING);
    });
  if (picture !== undefined) {
    const box = {
      x: MARGIN,
      y: canvas.height - MARGIN - fits.pictureHeight,
      w: canvas.width - MARGIN * 2,
      h: fits.pictureHeight,
    };
    const size = sizeOf(picture);
    const scale = Math.min(box.w / size.w, box.h / size.h);
    const w = size.w * scale;
    const h = size.h * scale;
    context.drawImage(picture, box.x + (box.w - w) / 2, box.y + (box.h - h) / 2, w, h);
  }
  texture.needsUpdate = true;
  return texture;
}

/** A picture's own size in pixels, whatever kind of image it is. */
function sizeOf(picture: CanvasImageSource): { w: number; h: number } {
  const sized = picture as {
    width?: number;
    height?: number;
    naturalWidth?: number;
    naturalHeight?: number;
  };
  return { w: sized.naturalWidth || sized.width || 1, h: sized.naturalHeight || sized.height || 1 };
}
