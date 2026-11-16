import {
  BoxGeometry,
  CircleGeometry,
  Float32BufferAttribute,
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
import { type Hour, type PictureColours, pictureColours } from './memory.js';

/**
 * A person's own photo or video in the app: its colours for a place (`coloursOfPicture`), and the picture itself on a
 * window standing in the scene (`pictureWindow`). The file is the app's own, in `public/` (a photo or video the person
 * added to the project is in its `media/` folder: copy it there, a video's `.mp4` play copy rather than its `.mov`).
 * Nothing goes over the network and nothing is kept.
 */

const VIDEO = /\.(mp4|m4v|webm|mov)(\?|#|$)/i;

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
 * file) or takes over 6 s answers with the kit's own dusk instead, so the place is still built.
 */
export function coloursOfPicture(url: string, options: { hour?: Hour } = {}): Promise<PictureColours> {
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

/** A picture on a window in the scene. */
export interface PictureWindow {
  /** What to add to the scene: the window, its frame and the two posts it stands on. Place and turn it as a whole. */
  readonly group: Group;
  /** False until the picture's first frame is ready; the window is hidden until then, never half drawn. */
  readonly ready: boolean;
  /** How tall the picture is, in metres, once ready (its width is the `width` given). */
  readonly height: number;
  /**
   * Stands the window on its two posts on `ground` (a place with a `height(x, z)`, such as `openLand`'s) at (x, z),
   * upright and turned to the person at the origin, each post down to the sand under it, the window's lower edge
   * `lift` metres above the middle of the two (stand it where the sand is fairly level).
   */
  standOn(ground: { height(x: number, z: number): number }, x: number, z: number): void;
  /** Fades the window in once ready; pass it to `ambientMotion`. */
  move(time: number): void;
}

/**
 * The photo or video at `url` on a window `width` metres wide (1.6 by default), its height from the picture's own
 * shape, in a thin dark frame on two slim posts that hold its lower edge `lift` metres above the ground (0.2), its
 * picture seen through `tint` (a warm dim light by default, so a daytime picture sits in a dusk place; '#FFFFFF' for
 * none). A
 * video plays by itself, muted and looping (a browser that holds it back until the person's first press plays it
 * then, as the starter's sound does). Stand it in the place a few metres off, turned to the person:
 * `standOn(place, x, z)`.
 */
export function pictureWindow(url: string, options: { width?: number; lift?: number; tint?: string } = {}): PictureWindow {
  const width = options.width ?? 1.6;
  const lift = options.lift ?? 0.2;
  const group = new Group();
  group.visible = false;
  const face = new MeshBasicMaterial({ color: options.tint ?? '#C49E84', toneMapped: false, transparent: true, opacity: 0 });
  // Weathered wood, darkened by the dusk.
  const frame = new MeshBasicMaterial({ color: '#4A3A2C', toneMapped: false, transparent: true, opacity: 0 });
  const posts = [new Mesh(new BoxGeometry(0.09, 1, 0.09), frame), new Mesh(new BoxGeometry(0.09, 1, 0.09), frame)];
  const body = new Group();
  // Where each post goes into the sand, the sand a little darker round it, softly.
  const meeting = new CircleGeometry(0.16, 20);
  meeting.rotateX(-Math.PI / 2);
  const fade: number[] = [];
  const at = meeting.getAttribute('position');
  for (let i = 0; i < at.count; i++) fade.push(0, 0, 0, Math.hypot(at.getX(i), at.getZ(i)) < 0.01 ? 0.5 : 0);
  meeting.setAttribute('color', new Float32BufferAttribute(fade, 4));
  const dark = new MeshBasicMaterial({ vertexColors: true, transparent: true, depthWrite: false, toneMapped: false });
  const marks = [new Mesh(meeting, dark), new Mesh(meeting, dark)];
  group.add(body, ...posts, ...marks);
  // Where the posts meet the ground, as heights under each, once the window is stood on something.
  const feet = [0, 0];
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

  const build = (texture: Texture, w: number, h: number) => {
    texture.colorSpace = SRGBColorSpace;
    // A video changes every frame: no mipmaps. A photo seen smaller than itself shimmers without them.
    const still = !(texture instanceof VideoTexture);
    texture.minFilter = still ? LinearMipmapLinearFilter : LinearFilter;
    texture.generateMipmaps = still;
    // Its first frame drawn now: a video's texture otherwise waits for it to play, and would show black.
    texture.needsUpdate = true;
    face.map = texture;
    face.needsUpdate = true;
    height = (width * h) / Math.max(1, w);
    const border = Math.min(width, height) * 0.015;
    // The frame well behind the picture, and drawn first, so the two never fight for the same pixels.
    // A frame with some depth to it, a few centimetres deep.
    const back = new Mesh(new BoxGeometry(width + border * 2, height + border * 2, 0.05), frame);
    back.position.set(0, border + height / 2, -0.03);
    back.renderOrder = -0.1;
    const picture = new Mesh(new PlaneGeometry(width, height), face);
    picture.position.set(0, border + height / 2, 0);
    body.add(back, picture);
    ready = true;
    layout();
  };
  // The window's lower edge `lift` above the middle of its two feet; each post from 5 cm under its own foot up to the frame's
  // middle, behind it.
  function layout() {
    const base = ((feet[0] as number) + (feet[1] as number)) / 2 + lift;
    body.position.y = base;
    for (let i = 0; i < 2; i++) {
      const post = posts[i] as Mesh;
      const foot = (feet[i] as number) - 0.05;
      const top = base + height / 2;
      post.scale.y = top - foot;
      post.position.set((i === 0 ? -1 : 1) * width * 0.36, (top + foot) / 2, -0.05);
      (marks[i] as Mesh).position.set((i === 0 ? -1 : 1) * width * 0.36, (feet[i] as number) + 0.015, -0.05);
    }
  }

  if (isVideo(url)) {
    loadedVideo(url)
      .then((v) => {
        video = v;
        build(new VideoTexture(v), v.videoWidth, v.videoHeight);
        play();
      })
      .catch(() => {});
  } else {
    loadedImage(url)
      .then((image) => {
        // A phone's photo is far larger than a window needs: at most 2048 on its longer side.
        const scale = Math.min(1, 2048 / Math.max(image.naturalWidth, image.naturalHeight, 1));
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
        canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        build(new CanvasTexture(canvas), canvas.width, canvas.height);
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
    standOn(ground, x, z) {
      const turn = Math.atan2(-x, -z);
      group.position.set(x, 0, z);
      group.rotation.set(0, turn, 0);
      // The posts stand at +-0.36 of the width along the window's own x, which points along (cos turn, -sin turn).
      for (let i = 0; i < 2; i++) {
        const side = (i === 0 ? -1 : 1) * width * 0.36;
        feet[i] = ground.height(x + side * Math.cos(turn), z - side * Math.sin(turn));
      }
      layout();
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
      if (face.opacity >= 1) return;
      const fade = Math.min(1, (time - shownAt) / 1.2);
      const eased = fade * fade * (3 - 2 * fade);
      face.opacity = eased;
      frame.opacity = eased;
      face.transparent = eased < 1;
      frame.transparent = eased < 1;
    },
  };
}
