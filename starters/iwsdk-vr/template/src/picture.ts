import {
  BoxGeometry,
  Color,
  Group,
  LinearFilter,
  LinearMipmapLinearFilter,
  Mesh,
  MeshBasicMaterial,
  PlaneGeometry,
  SRGBColorSpace,
  type Texture,
  VideoTexture,
  CanvasTexture,
} from 'three';
import { glows } from './glow.js';
import { type Hour, type PictureColours, pictureColours } from './memory.js';

/**
 * A person's own photo or video in the app: its colours for a place (`coloursOfPicture`), and the picture itself on a
 * window standing in the scene (`pictureWindow`). The file is the app's own, in `public/` (a photo or video the person
 * added to the project is in its `media/` folder: copy it there, a video's `.mp4` play copy rather than its `.mov`).
 * Nothing goes over the network and nothing is kept.
 */

const VIDEO = /\.(mp4|m4v|webm|mov)(\?|#|$)/i;

/** Whether `url` is the app's own file: a picture from anywhere else is never fetched from the person's headset. */
function sameOrigin(url: string): boolean {
  try {
    return new URL(url, location.href).origin === location.origin;
  } catch {
    return false;
  }
}

/** Whether `url` names a video, by its ending. */
export function isVideo(url: string): boolean {
  return VIDEO.test(url);
}

function loadedVideo(url: string): Promise<HTMLVideoElement> {
  const video = document.createElement('video');
  video.muted = true;
  video.loop = true;
  video.playsInline = true;
  video.crossOrigin = 'anonymous';
  video.preload = 'auto';
  return new Promise((resolve, reject) => {
    video.addEventListener('loadeddata', () => resolve(video), { once: true });
    video.addEventListener('error', () => reject(new Error(`the video ${url} did not load`)), { once: true });
    video.src = url;
    video.load();
  });
}

function seek(video: HTMLVideoElement, seconds: number): Promise<void> {
  return new Promise((resolve) => {
    if (Math.abs(video.currentTime - seconds) < 0.01) {
      resolve();
      return;
    }
    video.addEventListener('seeked', () => resolve(), { once: true });
    video.currentTime = seconds;
  });
}

async function loadedImage(url: string): Promise<HTMLImageElement> {
  const image = new Image();
  image.crossOrigin = 'anonymous';
  image.src = url;
  await image.decode();
  return image;
}

/** Settles with `fallback` if `work` has not settled in `seconds`, or if it fails. */
function within<T>(work: Promise<T>, seconds: number, fallback: T): Promise<T> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => resolve(fallback), seconds * 1000);
    work.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      () => {
        clearTimeout(timer);
        resolve(fallback);
      },
    );
  });
}

/** The kit's own dusk, for a picture that cannot be read. */
const DUSK: PictureColours = {
  top: '#0E0D2E',
  middle: '#4B2F63',
  horizon: '#C98060',
  far: '#3A2A3E',
  near: '#1E1620',
  light: '#FFC48A',
  horizonAt: 0.5,
};

/** The pixels of `source` drawn 48 wide, as `pictureColours` reads them. */
function pixelsOf(source: CanvasImageSource, width: number, height: number) {
  const w = 48;
  const h = Math.max(6, Math.round((w * height) / Math.max(1, width)));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (context === null) throw new Error('no 2d canvas to read the picture with');
  context.drawImage(source, 0, 0, w, h);
  return { data: context.getImageData(0, 0, w, h).data, w, h };
}

/**
 * The colours of the photo or video at `url` read as a sky over a ground (`src/memory.ts`), moved to `hour` if given
 * ('dusk': a deep blue overhead, a warm low glow, a dim ground). A video is read a tenth of the way in (at most 2 s),
 * past a first frame that may be dark. Wait for it before building the place, so the place never changes colour
 * once it shows. A JPEG, PNG or WebP photo or an MP4 video reads; one that cannot be read (a HEIC photo, a missing
 * file, a file from another site, which is never fetched) or takes over 6 s answers with the kit's own dusk instead, so
 * the place is still built.
 */
export function coloursOfPicture(url: string, options: { hour?: Hour } = {}): Promise<PictureColours> {
  if (!sameOrigin(url)) return Promise.resolve(DUSK);
  return within(read(url, options), 6, DUSK);
}

async function read(url: string, options: { hour?: Hour }): Promise<PictureColours> {
  if (isVideo(url)) {
    const video = await loadedVideo(url);
    await seek(video, Math.min(2, (video.duration || 0) * 0.1));
    const { data, w, h } = pixelsOf(video, video.videoWidth, video.videoHeight);
    video.removeAttribute('src');
    video.load();
    return pictureColours(data, w, h, options);
  }
  const image = await loadedImage(url);
  const { data, w, h } = pixelsOf(image, image.naturalWidth, image.naturalHeight);
  return pictureColours(data, w, h, options);
}

/** Ground a window can stand over: its height anywhere, and, if it has one, a light it lets the window cast on it. */
export interface WindowGround {
  height(x: number, z: number): number;
  light?(x: number, y: number, z: number, colour: Color, strength: number): void;
  shade?(x: number, z: number, depth: number): void;
}

/** A picture on a window in the scene. */
export interface PictureWindow {
  /** What to add to the scene: the window, its frame and its soft glow. Place and turn it as a whole. */
  readonly group: Group;
  /** False until the picture's first frame is ready; the window is hidden until then, never half drawn. */
  readonly ready: boolean;
  /** How tall the picture is, in metres, once ready (its width is the `width` given). */
  readonly height: number;
  /**
   * Floats the window over `ground` (a place with a `height(x, z)`, such as `openLand`'s) at (x, z), upright and turned
   * to the person at the origin, its top under the eye and clear of the sand (`lift` sets its height over the sand); if the ground takes light, the
   * window's own light falls on it in front, warm or cool, bright or dim as the picture is.
   */
  standOn(ground: WindowGround, x: number, z: number): void;
  /** Fades the window in once ready and keeps its light on the sand; pass it to `ambientMotion`. */
  move(time: number): void;
}

/**
 * The photo or video at `url` floating on a window `width` metres wide (1.6 by default), its height from the
 * picture's own shape, its thin edge lit warm and glowing softly in the picture's own colour, its top under
 * the person's eye and its lower edge at least 40 cm over the sand (or `lift` metres over it, if given), the picture seen through `tint` (a warm dim light by default, so a daytime
 * picture sits in a dusk place; '#FFFFFF' for none). A video plays by itself, muted and looping (a browser that holds
 * it back until the person's first press plays it then, as the starter's sound does). Float it in the place a few
 * metres off, turned to the person: `standOn(place, x, z)`.
 */
export function pictureWindow(url: string, options: { width?: number; lift?: number; tint?: string } = {}): PictureWindow {
  const width = options.width ?? 1.6;
  const lift = options.lift;
  const group = new Group();
  group.visible = false;
  const face = new MeshBasicMaterial({ color: options.tint ?? '#C49E84', toneMapped: false, transparent: true, opacity: 0 });
  // A thin edge lit warm, as if the picture's own light caught it, never a dark outline.
  const frame = new MeshBasicMaterial({ color: '#E9C9A6', toneMapped: false, transparent: true, opacity: 0 });
  const body = new Group();
  group.add(body);
  // The picture's own light: its mean colour, read from it now and then, for the glow round its edges and the light
  // it casts on the sand.
  const glow = glows(1, { halo: 0.16, round: 0.12, core: 0 });
  glow.object.position.z = -0.05;
  body.add(glow.object);
  const own = new Color('#FFD9B0');
  const sample = document.createElement('canvas');
  sample.width = 8;
  sample.height = 8;
  const sampler = sample.getContext('2d', { willReadFrequently: true });
  let sampledAt = -1;
  let source: CanvasImageSource | null = null;
  let ground: WindowGround | null = null;
  const at = { x: 0, y: 0, z: 0, turn: 0 };
  let video: HTMLVideoElement | null = null;
  let height = width * (9 / 16);
  let ready = false;
  let shownAt = -1;
  let playing = false;
  let triedAfterPress = false;
  const play = () => {
    if (video === null || playing) return;
    playing = true;
    video.play().catch(() => {
      playing = false;
    });
  };

  const build = (texture: Texture, w: number, h: number, from: CanvasImageSource) => {
    texture.colorSpace = SRGBColorSpace;
    // A video changes every frame: no mipmaps. A photo seen smaller than itself shimmers without them.
    const still = !(texture instanceof VideoTexture);
    texture.minFilter = still ? LinearMipmapLinearFilter : LinearFilter;
    texture.generateMipmaps = still;
    // Its first frame drawn now: a video's texture otherwise waits for it to play, and would show black.
    texture.needsUpdate = true;
    face.map = texture;
    face.needsUpdate = true;
    source = from;
    height = (width * h) / Math.max(1, w);
    const border = Math.min(width, height) * 0.008;
    // A thin frame with a little depth, well behind the picture and drawn first, so the two never fight for pixels.
    const back = new Mesh(new BoxGeometry(width + border * 2, height + border * 2, 0.03), frame);
    back.position.set(0, height / 2, -0.025);
    back.renderOrder = -0.1;
    const picture = new Mesh(new PlaneGeometry(width, height), face);
    picture.position.set(0, height / 2, 0);
    glow.object.position.y = 0;
    body.add(back, picture);
    ready = true;
    place();
  };
  // Over the sand under the window's middle, the lower edge `lift` above it.
  function place() {
    if (ground === null) return;
    const sand = ground.height(at.x, at.z);
    // Unless a lift is given: its top well under the person's eye, so it never sits on the horizon, and never closer
    // than 40 cm to the sand, so it clearly floats.
    body.position.y = lift === undefined ? Math.max(sand + 0.4, 1.62 - 0.35 - height) : sand + lift;
  }

  if (sameOrigin(url) && isVideo(url)) {
    loadedVideo(url)
      .then((v) => {
        video = v;
        build(new VideoTexture(v), v.videoWidth, v.videoHeight, v);
        play();
      })
      .catch(() => {});
  } else if (sameOrigin(url)) {
    loadedImage(url)
      .then((image) => {
        // A phone's photo is far larger than a window needs: at most 2048 on its longer side.
        const scale = Math.min(1, 2048 / Math.max(image.naturalWidth, image.naturalHeight, 1));
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
        canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        build(new CanvasTexture(canvas), canvas.width, canvas.height, canvas);
      })
      .catch(() => {});
  }

  return {
    group,
    get ready() {
      return ready;
    },
    get height() {
      return height;
    },
    standOn(on, x, z) {
      ground = on;
      at.x = x;
      at.z = z;
      at.turn = Math.atan2(-x, -z);
      group.position.set(x, 0, z);
      group.rotation.set(0, at.turn, 0);
      place();
    },
    move(time: number) {
      if (!ready) return;
      if (!playing && !triedAfterPress && (navigator.userActivation?.hasBeenActive ?? false)) {
        triedAfterPress = true;
        play();
      }
      if (shownAt < 0) {
        shownAt = time;
        group.visible = true;
      }
      const fade = Math.min(1, (time - shownAt) / 1.2);
      const eased = fade * fade * (3 - 2 * fade);
      if (face.opacity < 1) {
        face.opacity = eased;
        frame.opacity = eased;
        face.transparent = eased < 1;
        frame.transparent = eased < 1;
      }
      // The picture's light, read from 8 by 8 of it a few times a second (a video's changes as it plays).
      if (sampler !== null && source !== null && time - sampledAt > 0.4) {
        sampledAt = time;
        try {
          sampler.drawImage(source, 0, 0, 8, 8);
          const data = sampler.getImageData(0, 0, 8, 8).data;
          let r = 0;
          let g = 0;
          let b = 0;
          for (let i = 0; i < data.length; i += 4) {
            r += data[i] as number;
            g += data[i + 1] as number;
            b += data[i + 2] as number;
          }
          own.setRGB(r / 16320, g / 16320, b / 16320, SRGBColorSpace);
          // Its own hue, brightened, warmed a little as the dusk light round it is; never washed to white.
          own.multiplyScalar(0.8 / Math.max(own.r, own.g, own.b, 1e-3)).multiply(face.color).multiplyScalar(1.4);
        } catch {
          // A picture the canvas cannot read keeps its last light.
        }
      }
      glow.begin();
      glow.add(0, height / 2, 0, width, height, own, 0.1 * eased);
      glow.end();
      if (ground?.light !== undefined) {
        // Its light on the sand a metre in front of it, toward the person.
        const fx = at.x + Math.sin(at.turn);
        const fz = at.z + Math.cos(at.turn);
        ground.light(fx, ground.height(fx, fz) + 1.1, fz, own, 0.8 * eased);
      }
      ground?.shade?.(at.x, at.z, 0.35 * eased);
    },
  };
}
