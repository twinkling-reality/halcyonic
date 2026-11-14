import {
  AdditiveBlending,
  BoxGeometry,
  Float32BufferAttribute,
  Mesh,
  Color,
  DynamicDrawUsage,
  Group,
  InstancedBufferAttribute,
  InstancedMesh,
  MeshBasicMaterial,
  PlaneGeometry,
  ShaderMaterial,
} from 'three';
import type { Player } from './instruments.js';
import { isBlack } from './score.js';

/**
 * Light for apps of music and light: soft glowing shapes, sparks, and a keyboard of light. Each kind is ONE instanced
 * mesh (one draw call) whose glow is drawn in its own shader, so there is no full-screen bloom pass to pay for in the
 * headset; each frame writes into arrays made once, so nothing new is made per frame. Light adds up where it
 * overlaps, as light does: keep a dark place behind it (`src/places.ts`).
 */

const VERTEX = /* glsl */ `
  uniform float facing;
  uniform float halo;
  varying vec2 vPoint;
  varying vec2 vHalf;
  varying vec3 vColour;
  void main() {
    vec3 x = instanceMatrix[0].xyz;
    vec3 y = instanceMatrix[1].xyz;
    vec2 size = vec2(length(x), length(y));
    // The quad is the shape plus its halo all round; vPoint is in metres from its centre.
    vec2 whole = size + 2.0 * halo;
    vPoint = position.xy * whole;
    vHalf = size * 0.5;
    vColour = instanceColor;
    vec4 at;
    if (facing > 1.5) {
      // Upright along its own direction, turned to the eye about it: a falling drop stays along its fall however the
      // head tilts.
      vec3 centre = (modelMatrix * vec4(instanceMatrix[3].xyz, 1.0)).xyz;
      vec3 along = normalize(mat3(modelMatrix) * y);
      vec3 side = normalize(cross(along, centre - cameraPosition) + vec3(1e-5, 0.0, 0.0));
      at = viewMatrix * vec4(centre + side * vPoint.x + along * vPoint.y, 1.0);
    } else if (facing > 0.5) {
      // Always turned to the eye: the centre placed, the quad laid out in view space.
      at = modelViewMatrix * vec4(instanceMatrix[3].xyz, 1.0);
      at.xy += vPoint;
    } else {
      vec3 local = instanceMatrix[3].xyz + normalize(x) * vPoint.x + normalize(y) * vPoint.y;
      at = modelViewMatrix * vec4(local, 1.0);
    }
    gl_Position = projectionMatrix * at;
  }
`;

const FRAGMENT = /* glsl */ `
  uniform float halo;
  uniform float corner;
  uniform float core;
  uniform float whiten;
  uniform float ramp;
  varying vec2 vPoint;
  varying vec2 vHalf;
  varying vec3 vColour;
  void main() {
    // Distance in metres from the rounded shape's edge: below zero inside.
    float r = min(corner, min(vHalf.x, vHalf.y));
    vec2 q = abs(vPoint) - vHalf + r;
    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
    // The core's own edge is soft too, in proportion to its size, so no shape shows an outline.
    float soft = max(0.0015, 0.3 * min(vHalf.x, vHalf.y));
    float inside = 1.0 - smoothstep(-soft, soft * 0.5, d);
    // How deep inside the shape, 0 at its edge to 1 at its middle: the core is hottest along its middle.
    float deep = clamp(-d / max(min(vHalf.x, vHalf.y), 1e-4), 0.0, 1.0);
    float outside = max(d, 0.0) / max(halo, 1e-4);
    // Falls to nothing at the quad's edge as a soft bell: no knee for the eye to read as a rim or a darker ring,
    // even after the screen's own curve, and stacked halos never show a plate.
    float fall = max(0.0, 1.0 - outside);
    float glow = exp(-4.0 * outside * outside) * fall * fall;
    // A hot core, whiter than its colour along its middle, its own colour at its edges, inside a halo of the colour.
    vec3 hot = mix(vColour, vec3(max(vColour.r, max(vColour.g, vColour.b))), whiten * deep);
    vec3 light = hot * inside * core * (0.45 + 0.55 * deep) + vColour * glow * 0.5;
    // A ramp, when asked: full at the shape's -y end, falling to the ramp's share at its +y end.
    if (ramp >= 0.0) light *= pow(mix(1.0, pow(ramp, 0.333), clamp(vPoint.y / max(vHalf.y, 1e-4) * 0.5 + 0.5, 0.0, 1.0)), 3.0);
    if (dot(light, vec3(1.0)) < 0.002) discard;
    gl_FragColor = vec4(light, 1.0);
    #include <colorspace_fragment>
    // A little noise below one step of the screen's colour, so soft falloffs never show as rings.
    gl_FragColor.rgb += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;
  }
`;

/** A set of soft glowing shapes drawn as one: rounded bars, dots and discs, each with a halo of light round it. */
export interface Glows {
  /** What to add to the scene. */
  readonly object: Group;
  readonly mesh: InstancedMesh;
  /** Starts a frame's shapes: everything added since the last `begin` is cleared. */
  begin(): void;
  /**
   * Adds one shape centred at (x, y, z) in the mesh's own frame, `width` by `height` metres, in `colour` at
   * `strength` (0 is dark, 1 full; light adds up where shapes overlap). Shapes past the set's capacity are skipped.
   */
  add(x: number, y: number, z: number, width: number, height: number, colour: Color, strength: number): void;
  /**
   * Adds one shape lying in a plane of its own: centred at (x, y, z), `width` along the unit direction (rx, ry, rz)
   * and `height` along the unit direction (ux, uy, uz), at right angles to it.
   */
  place(
    x: number,
    y: number,
    z: number,
    rx: number,
    ry: number,
    rz: number,
    ux: number,
    uy: number,
    uz: number,
    width: number,
    height: number,
    colour: Color,
    strength: number,
  ): void;
  /** Ends the frame: only the shapes added since `begin` are drawn. */
  end(): void;
}

/**
 * Makes a set of up to `capacity` glowing shapes. `facing: true` turns each to the eye (sparks, lanterns, motes);
 * `upright: true` keeps each along its own up (straight up for `add`, the direction `place` gives) and turns it to the
 * eye about that, so it never tips with the head (a falling drop, a column of light); otherwise each lies in the plane `place` gives it, or the mesh's own x-y plane for `add`. With `over`,
 * they are drawn over what is near them (light spilling over a neighbour) instead of hidden by it. `halo` is how
 * far the glow reaches round each shape, `round` its corner radius, both in metres; a shape whose `round` is half its
 * size is a disc; `whiten` makes the core hotter than its colour; `ramp` fades each shape from full at its -y end to
 * that share at its +y end (a drop brightest at its foot). Add `object` to the scene.
 */
export function glows(
  capacity: number,
  options: {
    facing?: boolean;
    upright?: boolean;
    over?: boolean;
    halo?: number;
    round?: number;
    core?: number;
    whiten?: number;
    ramp?: number;
  } = {},
): Glows {
  const uniforms = () => ({
    facing: { value: options.upright === true ? 2 : options.facing === true ? 1 : 0 },
    halo: { value: options.halo ?? 0.03 },
    corner: { value: options.round ?? 0.01 },
    core: { value: options.core ?? 1 },
    whiten: { value: options.whiten ?? 0 },
    ramp: { value: options.ramp ?? -1 },
  });
  const material = new ShaderMaterial({
    uniforms: uniforms(),
    vertexShader: VERTEX,
    fragmentShader: FRAGMENT,
    transparent: true,
    depthWrite: false,
    depthTest: options.over !== true,
    blending: AdditiveBlending,
    toneMapped: false,
  });
  const geometry = new PlaneGeometry(1, 1);
  const mesh = new InstancedMesh(geometry, material, capacity);
  mesh.instanceMatrix.setUsage(DynamicDrawUsage);
  mesh.instanceColor = new InstancedBufferAttribute(new Float32Array(capacity * 3), 3);
  mesh.instanceColor.setUsage(DynamicDrawUsage);
  // Positions change every frame: the mesh is never culled by a stale bound.
  mesh.frustumCulled = false;
  mesh.count = 0;
  const object = new Group();
  object.add(mesh);
  const matrices = mesh.instanceMatrix.array as Float32Array;
  const colours = mesh.instanceColor.array as Float32Array;
  let used = 0;
  return {
    object,
    mesh,
    begin() {
      used = 0;
    },
    add(x, y, z, width, height, colour, strength) {
      this.place(x, y, z, 1, 0, 0, 0, 1, 0, width, height, colour, strength);
    },
    place(x, y, z, rx, ry, rz, ux, uy, uz, width, height, colour, strength) {
      if (used >= capacity || strength <= 0) return;
      const m = used * 16;
      matrices[m] = rx * width;
      matrices[m + 1] = ry * width;
      matrices[m + 2] = rz * width;
      matrices[m + 3] = 0;
      matrices[m + 4] = ux * height;
      matrices[m + 5] = uy * height;
      matrices[m + 6] = uz * height;
      matrices[m + 7] = 0;
      matrices[m + 8] = ry * uz - rz * uy;
      matrices[m + 9] = rz * ux - rx * uz;
      matrices[m + 10] = rx * uy - ry * ux;
      matrices[m + 11] = 0;
      matrices[m + 12] = x;
      matrices[m + 13] = y;
      matrices[m + 14] = z;
      matrices[m + 15] = 1;
      const c = used * 3;
      colours[c] = colour.r * strength;
      colours[c + 1] = colour.g * strength;
      colours[c + 2] = colour.b * strength;
      used += 1;
    },
    end() {
      mesh.count = used;
      mesh.instanceMatrix.needsUpdate = true;
      if (mesh.instanceColor !== null) mesh.instanceColor.needsUpdate = true;
    },
  };
}

/** Sparks: small motes of light that rise, drift and fade. */
export interface Sparks {
  /** What to add to the scene. */
  readonly object: Group;
  readonly mesh: InstancedMesh;
  /**
   * Sends up `count` sparks from (x, y, z) in `colour`: they rise at about `rise` metres a second, spread `spread`
   * metres, and live about `life` seconds.
   */
  emit(x: number, y: number, z: number, colour: Color, count: number, options?: { rise?: number; spread?: number; life?: number; size?: number }): void;
  /** Moves every spark on by `delta` seconds and draws them; call once a frame. */
  update(delta: number): void;
}

/** Makes up to `capacity` sparks, drawn as one mesh that faces the eye (`over` to draw them over what is near). */
export function sparks(capacity = 512, options: { over?: boolean } = {}): Sparks {
  const light = glows(capacity, { facing: true, halo: 0.006, round: 1, core: 1, whiten: 0.6, over: options.over });
  const p = new Float32Array(capacity * 3);
  const v = new Float32Array(capacity * 3);
  const age = new Float32Array(capacity);
  const life = new Float32Array(capacity).fill(0);
  const size = new Float32Array(capacity);
  const tint = new Float32Array(capacity * 3);
  const colour = new Color();
  let next = 0;
  // A fixed sequence, so the same sparks fly on every load and in every check.
  let seed = 12345;
  const random = () => {
    seed = (seed * 16807) % 2147483647;
    return seed / 2147483647;
  };
  return {
    object: light.object,
    mesh: light.mesh,
    emit(x, y, z, c, count, options = {}) {
      const rise = options.rise ?? 0.35;
      const spread = options.spread ?? 0.02;
      const lasts = options.life ?? 1.6;
      for (let n = 0; n < count; n++) {
        const i = next;
        next = (next + 1) % capacity;
        p[i * 3] = x + (random() - 0.5) * spread;
        p[i * 3 + 1] = y + random() * spread * 0.5;
        p[i * 3 + 2] = z + (random() - 0.5) * spread;
        v[i * 3] = (random() - 0.5) * rise * 0.5;
        v[i * 3 + 1] = rise * (0.4 + random() * 0.9);
        v[i * 3 + 2] = (random() - 0.5) * rise * 0.5;
        age[i] = 0;
        life[i] = lasts * (0.5 + random() * 0.8);
        size[i] = (options.size ?? 0.009) * (0.6 + random() * 0.8);
        // Hotter (whiter) the brighter they start.
        const white = random() * 0.15;
        tint[i * 3] = c.r + (1 - c.r) * white;
        tint[i * 3 + 1] = c.g + (1 - c.g) * white;
        tint[i * 3 + 2] = c.b + (1 - c.b) * white;
      }
    },
    update(delta) {
      const dt = Math.min(0.05, Math.max(0, delta));
      const drag = Math.exp(-1.6 * dt);
      light.begin();
      for (let i = 0; i < capacity; i++) {
        if (age[i] >= life[i]) continue;
        age[i] += dt;
        const k = i * 3;
        const t = age[i] as number;
        // A slow sideways curl, different for each spark, as warm air carries them.
        v[k] = (v[k] as number) * drag + Math.sin(t * 2.3 + i) * 0.06 * dt;
        v[k + 1] = (v[k + 1] as number) * drag + 0.05 * dt;
        v[k + 2] = (v[k + 2] as number) * drag + Math.cos(t * 1.9 + i * 0.7) * 0.06 * dt;
        p[k] = (p[k] as number) + (v[k] as number) * dt;
        p[k + 1] = (p[k + 1] as number) + (v[k + 1] as number) * dt;
        p[k + 2] = (p[k + 2] as number) + (v[k + 2] as number) * dt;
        const left = 1 - t / (life[i] as number);
        if (left <= 0) continue;
        const flicker = 0.8 + 0.2 * Math.sin(t * 23 + i * 1.7);
        colour.setRGB(tint[k] as number, tint[k + 1] as number, tint[k + 2] as number);
        const s = (size[i] as number) * (0.5 + 0.5 * left);
        light.add(p[k] as number, p[k + 1] as number, p[k + 2] as number, s, s, colour, 0.65 * left * left * flicker);
      }
      light.end();
    },
  };
}

/** Where a key is, in its keyboard's frame: what light that lands on it needs. */
export interface KeySpot {
  readonly midi: number;
  readonly black: boolean;
  /** The middle of the key's front edge, at its top. */
  readonly x: number;
  readonly y: number;
  readonly z: number;
  /** Unit directions along the keyboard (low to high) and from the key's front to its back, level. */
  readonly tx: number;
  readonly tz: number;
  readonly ox: number;
  readonly oz: number;
  /** The key's width and length. */
  readonly width: number;
  readonly length: number;
}

/**
 * How far back from a key's front edge light lands on it: on a white key's open front, before the black keys, and in
 * the middle of a black key.
 */
export function landing(key: KeySpot): number {
  return key.length * (key.black ? 0.5 : 0.14);
}

/** A keyboard of light: dark glass keys that light in a colour when played. */
export interface LightKeys {
  readonly group: Group;
  /** Where a key is, or undefined for a key this keyboard does not have. */
  spot(midi: number): KeySpot | undefined;
  /** Every key, low to high. */
  readonly spots: readonly KeySpot[];
  /** A white key's width. */
  readonly keyWidth: number;
  /** Lights `midi` at `amount` (0 to 1) in `colour` for this frame; call between `begin` and `end` each frame. */
  light(midi: number, colour: Color, amount: number): void;
  begin(): void;
  end(): void;
}

/**
 * A keyboard from `low` to `high` (MIDI numbers, widened to white keys at both ends), its keys' tops at y = 0 in its
 * own group. With `arc`, it curves round the group's origin, its front edge `arc` metres from it, low keys to the left
 * and high to the right of -z, its middle straight ahead: stand a person at the origin and they stand inside it. Without, it runs straight
 * along x, its front edge along z = 0. Two meshes for the keys and a few for their light.
 */
export function lightKeys(
  low: number,
  high: number,
  options: { keyWidth?: number; arc?: number; idle?: Color; dark?: Color } = {},
): LightKeys {
  let first = Math.floor(low);
  while (isBlack(first)) first -= 1;
  let last = Math.ceil(high);
  while (isBlack(last)) last += 1;
  let whites = 0;
  for (let m = first; m <= last; m++) if (!isBlack(m)) whites += 1;
  const keyWidth = options.keyWidth ?? 0.05;
  const width = whites * keyWidth;
  const whiteLength = keyWidth * 4.6;
  const blackLength = whiteLength * 0.62;
  const depth = 0.018;
  const raised = depth * 1.1;
  const idle = options.idle ?? new Color('#11162A');
  const blackIdle = idle.clone().multiplyScalar(0.4);
  const arc = options.arc;
  // Along the keyboard, then round the arc: a key at distance s from the middle stands at angle s / arc.
  const at = (s: number, out: number) => {
    if (arc === undefined) return { x: s, z: -out, tx: 1, tz: 0, ox: 0, oz: -1 };
    const a = s / arc;
    const r = arc + out;
    return { x: Math.sin(a) * r, z: -Math.cos(a) * r, tx: Math.cos(a), tz: Math.sin(a), ox: Math.sin(a), oz: -Math.cos(a) };
  };
  const spots: KeySpot[] = [];
  let w = 0;
  for (let m = first; m <= last; m++) {
    const black = isBlack(m);
    const s = -width / 2 + keyWidth * (black ? w : w + 0.5);
    if (!black) w += 1;
    const place = at(s, black ? whiteLength - blackLength : 0);
    spots.push({
      midi: m,
      black,
      x: place.x,
      y: black ? raised : 0,
      z: place.z,
      tx: place.tx,
      tz: place.tz,
      ox: place.ox,
      oz: place.oz,
      width: black ? keyWidth * 0.55 : keyWidth * 0.92,
      length: black ? blackLength : whiteLength,
    });
  }
  const byKey = new Map(spots.map((spot) => [spot.midi, spot]));
  const make = (keys: KeySpot[], base: Color) => {
    const black = keys[0]?.black === true;
    const mesh = new InstancedMesh(
      new BoxGeometry(1, depth, 1),
      new MeshBasicMaterial({ toneMapped: false }),
      Math.max(1, keys.length),
    );
    mesh.count = keys.length;
    mesh.instanceColor = new InstancedBufferAttribute(new Float32Array(Math.max(1, keys.length) * 3), 3);
    mesh.instanceColor.setUsage(DynamicDrawUsage);
    mesh.instanceMatrix.setUsage(DynamicDrawUsage);
    const m = mesh.instanceMatrix.array as Float32Array;
    keys.forEach((key, i) => {
      // Turned to face the middle: its x along the keyboard, its z from back to front.
      const cx = key.x + (key.ox * key.length) / 2;
      const cz = key.z + (key.oz * key.length) / 2;
      const y = black ? depth * 0.6 : -depth / 2;
      m.set([key.tx * key.width, 0, key.tz * key.width, 0, 0, 1, 0, 0, -key.ox * key.length, 0, -key.oz * key.length, 0, cx, y, cz, 1], i * 16);
      mesh.setColorAt(i, base);
    });
    return mesh;
  };
  const whiteKeys = spots.filter((spot) => !spot.black);
  const blackKeys = spots.filter((spot) => spot.black);
  // The glass fades into the place's own dark (`dark`) toward both ends of the keyboard, so it has no cut end.
  const dark = options.dark ?? new Color('#070B1C');
  const ends = (key: KeySpot) => {
    const i = spots.indexOf(key);
    return Math.min(1, Math.min(i, spots.length - 1 - i) / 12) ** 1.5;
  };
  const tone = new Map(spots.map((key) => [key.midi, dark.clone().lerp(key.black ? blackIdle : idle, ends(key))]));
  const white = make(whiteKeys, idle);
  const black = make(blackKeys, blackIdle);
  // A played key's light: a soft round heat where the note lands, hottest at its middle, drawn over the keys so a
  // neighbour never cuts it.
  const heat = glows(spots.length, { halo: 0.045, round: 1, whiten: 0.35, over: true });
  // And the key's own face, lit softly from its front, so which key went down is seen.
  const face = glows(spots.length, { halo: 0.008, round: 0.006, ramp: 0, over: true });
  const group = new Group();
  group.add(white, black, face.object, heat.object);
  const level = new Map<number, number>();
  const hue = new Map<number, Color>();
  for (const spot of spots) hue.set(spot.midi, new Color());
  const mix = new Color();
  const paint = (mesh: InstancedMesh, keys: KeySpot[], lift: number) => {
    const m = mesh.instanceMatrix.array as Float32Array;
    keys.forEach((key, i) => {
      const amount = Math.min(1, level.get(key.midi) ?? 0);
      // The glass itself stays dark: a played key's light is the heat on it, never a tint of the whole key.
      mesh.setColorAt(i, tone.get(key.midi) as Color);
      // A played key goes down a few millimetres.
      m[i * 16 + 13] = lift - 0.004 * amount;
    });
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor !== null) mesh.instanceColor.needsUpdate = true;
  };
  return {
    group,
    spot: (midi) => byKey.get(midi),
    spots,
    keyWidth,
    light(midi, colour, amount) {
      if (!byKey.has(midi) || amount <= (level.get(midi) ?? 0)) return;
      level.set(midi, amount);
      hue.get(midi)?.copy(colour);
    },
    begin() {
      level.clear();
    },
    end() {
      paint(white, whiteKeys, -depth / 2);
      paint(black, blackKeys, depth * 0.6);
      heat.begin();
      face.begin();
      for (const key of spots) {
        const amount = Math.min(1, level.get(key.midi) ?? 0);
        if (amount <= 0) continue;
        const { x, y, z, tx, tz, ox, oz } = key;
        const into = landing(key);
        const c = hue.get(key.midi) as Color;
        heat.place(x + ox * into, y + 0.0015, z + oz * into, tx, 0, tz, ox, 0, oz, 0.016, 0.016, c, 0.35 * amount);
        const mid = key.length / 2;
        face.place(x + ox * mid, y + 0.001, z + oz * mid, tx, 0, tz, ox, 0, oz, key.width * 0.8, key.length * 0.92, c, 0.14 * amount);
      }
      heat.end();
      face.end();
    },
  };
}

/** Notes of light falling onto a keyboard of light, played by a score: what `fallingNotes` makes. */
export interface FallingNotes {
  /** The notes and their light, inside the keyboard's group: they move with it. */
  readonly group: Group;
  /** Draws a frame from the player's time; pass it to `ambientMotion`. */
  move(time: number): void;
}

/** A colour for a key by where it lies between `low` and `high`: deep blue, then sea blue and violet, then rose. */
export function registerColour(midi: number, low: number, high: number, into = new Color()): Color {
  const t = Math.min(1, Math.max(0, (midi - low) / Math.max(1, high - low)));
  const at = t * (STOPS.length - 1);
  const k = Math.min(STOPS.length - 2, Math.floor(at));
  return into.copy(STOPS[k] as Color).lerp(STOPS[k + 1] as Color, at - k);
}

/** registerColour's scale, made once: it is read for every note every frame. */
const STOPS = [new Color('#2F45D8'), new Color('#2EA7D0'), new Color('#7A6EF0'), new Color('#E06AB8')];

/**
 * Makes the light of a score playing on a keyboard of light (`lightKeys`): each note falls as a soft drop of light
 * out of the dark overhead, straight down onto its own key (or, `from: 'around'`, from its key's own direction),
 * landing on the key's top at the moment it is heard
 * (`player.now()`); the key lights where it lands and stays lit while the note is held, light blooms up from it, and
 * a few sparks rise. Each note's light is coloured by where it lies in the music, low to high, moved a little by how
 * hard it is played (`colour` to choose another). `seconds` is how long a note is seen falling,
 * `fall` how far, in metres; `reach` how far off the hall answers each note with its colour, all round the person.
 * Built inside `keys.group`, so place that group.
 */
export function fallingNotes(
  player: Player,
  keys: LightKeys,
  options: {
    colour?: (midi: number) => Color;
    seconds?: number;
    fall?: number;
    reach?: number;
    from?: 'above' | 'around';
  } = {},
): FallingNotes {
  const fall = options.fall ?? 1.7;
  const reach = options.reach ?? 14;
  const speed = fall / (options.seconds ?? 2.8);
  // The colours span the music itself, its lowest note to its highest.
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const note of player.score.notes) {
    low = Math.min(low, note.pitch);
    high = Math.max(high, note.pitch);
  }
  const colours = new Map<number, Color>();
  for (const spot of keys.spots)
    colours.set(spot.midi, options.colour?.(spot.midi) ?? registerColour(spot.midi, low, high));
  const group = new Group();
  keys.group.add(group);
  // Where each key's notes come from: straight down from above it, or, `around`, each key from its own direction out of
  // the dark above and beyond the keyboard, so notes arrive from all round.
  const paths = new Map<number, [number, number, number]>();
  for (const spot of keys.spots) {
    if (options.from !== 'around') {
      paths.set(spot.midi, [0, 1, 0]);
      continue;
    }
    const h = ((spot.midi * 7919) % 97) / 97;
    const tilt = ((30 + 30 * h) * Math.PI) / 180;
    const turn = ((((spot.midi * 104729) % 89) / 89 - 0.5) * 140 * Math.PI) / 180;
    const ox = spot.ox * Math.cos(turn) + spot.tx * Math.sin(turn);
    const oz = spot.oz * Math.cos(turn) + spot.tz * Math.sin(turn);
    paths.set(spot.midi, [ox * Math.sin(tilt), Math.cos(tilt), oz * Math.sin(tilt)]);
  }
  // Each drop a small hot head at its foot and a tail tapering up into the dark, both drawn over the key it lands on.
  const drops = glows(600, { upright: true, halo: 0.03, round: 1, whiten: 0.1, ramp: 0.05, over: true });
  const heads = glows(600, { facing: true, halo: 0.025, round: 1, whiten: 0.4, over: true });
  // A column of light rising from a struck key: a line with only a halo, fading to nothing at its top.
  const blooms = glows(160, { upright: true, halo: 0.035, round: 1, core: 0, ramp: 0, over: true });
  const flares = glows(160, { facing: true, halo: 0.12, round: 1, whiten: 0.2, over: true });
  const motes = sparks(700, { over: true });
  // The place answers: each note lights the far dark of the hall in its colour, where its key points, to both sides
  // and behind the person, so the music is all round them.
  const echoes = glows(480, { facing: true, halo: 6, round: 1, core: 0, over: true });
  group.add(drops.object, heads.object, blooms.object, flares.object, motes.object, echoes.object);
  let last = Number.NaN;
  let previous = Number.NaN;
  let now = 0;
  // Its place in the music sets a note's colour, and how hard it is played moves it a little along the same scale.
  const shade = new Color();
  const tinted = (note: { pitch: number; loud: number }) =>
    options.colour !== undefined
      ? (colours.get(note.pitch) as Color)
      : registerColour(note.pitch + (note.loud - 0.45) * 0.6 * (high - low), low, high, shade);
  const draw = (note: { pitch: number; loud: number }, start: number, end: number) => {
    const spot = keys.spot(note.pitch);
    if (spot === undefined) return;
    const colour = tinted(note);
    // Where it lands: on the key's top, where its light shows.
    const into = landing(spot);
    const lx = spot.x + spot.ox * into;
    const lz = spot.z + spot.oz * into;
    // A drop, not a bar: the same short length for every note; a held note goes on as light on its key.
    const from = (start - now) * speed;
    const to = Math.min(from + 0.1, fall);
    if (from >= 0 && to > from) {
      // Out of the dark overhead, brightest as it lands; faded as the eye sees it, then made linear.
      const up = (from + to) / 2 / fall;
      const seen = (1 - up) ** 1.4 * Math.min(1, (1 - up) / 0.3);
      const [px, py, pz] = paths.get(note.pitch) as [number, number, number];
      const mid = (from + to) / 2;
      const strength = 0.7 * seen ** 2.2;
      drops.place(lx + px * mid, spot.y + 0.004 + py * mid, lz + pz * mid, 1, 0, 0, px, py, pz, spot.width * 0.22, to - from, colour, strength);
      heads.add(lx + px * from, spot.y + 0.006 + py * from, lz + pz * from, 0.01, 0.01, colour, 0.9 * seen ** 2.2);
    }
    if (now >= start && now < end) {
      const since = now - start;
      const strike = Math.exp(-since * 5);
      // Bright at the strike, then only a low glow while the note is held: nothing stands on a key waiting.
      const amount = 0.2 + 0.8 * strike;
      keys.light(note.pitch, colour, amount * (0.65 + 0.35 * note.loud));
      flares.add(lx, spot.y + 0.012, lz, 0.022, 0.022, colour, strike * 0.4);
      // Light blooms up from the key where it landed, rising and fading.
      const rise = 0.06 + since * 0.5;
      if (since < 0.8) blooms.add(lx, spot.y + rise / 2, lz, 0.002, rise, colour, 0.3 * Math.exp(-since * 5));
    }
    // A few sparks where each note lands, on the frame it lands.
    if (!Number.isNaN(previous) && start > previous && start <= now)
      motes.emit(lx, spot.y + 0.01, lz, colour, 6 + Math.round(10 * note.loud), {
        rise: 0.42,
        spread: spot.width * 0.6,
        life: 1.1,
        size: 0.004,
      });
  };
  const echo = (note: { pitch: number; loud: number }, start: number) => {
    const spot = keys.spot(note.pitch);
    const since = now - start;
    if (spot === undefined || since < 0 || since > 3) return;
    const a = Math.atan2(spot.x, -spot.z);
    // Higher notes answer higher up, each key at its own distance, so the hall's light never lines up in a row.
    const up = (note.pitch - low) / Math.max(1, high - low);
    const far = reach * (0.8 + 0.4 * (((note.pitch * 7) % 11) / 10));
    const strength = 0.2 * (0.4 + note.loud) * Math.exp(-since * 1.1) * Math.min(1, since / 0.15);
    const c = tinted(note);
    // Where its key points, then a quarter of the way round each side, then behind.
    for (let k = 0; k < 4; k++) {
      const b = a + (k * Math.PI) / 2;
      echoes.add(Math.sin(b) * far, 1.4 + 4 * up + k * 0.4, -Math.cos(b) * far, 0.05, 0.05, c, strength);
    }
  };
  return {
    group,
    move(time) {
      now = player.now();
      const delta = Number.isNaN(last) ? 0 : time - last;
      last = time;
      keys.begin();
      drops.begin();
      heads.begin();
      blooms.begin();
      flares.begin();
      player.each(0.2, fall / speed, draw);
      echoes.begin();
      player.each(3, 0, echo);
      echoes.end();
      keys.end();
      drops.end();
      heads.end();
      blooms.end();
      flares.end();
      motes.update(delta);
      previous = now;
    },
  };
}
