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
    if (facing > 0.5) {
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
    float inside = 1.0 - smoothstep(-0.0015, 0.0015, d);
    // How deep inside the shape, 0 at its edge to 1 at its middle: the core is hottest along its middle.
    float deep = clamp(-d / max(min(vHalf.x, vHalf.y), 1e-4), 0.0, 1.0);
    float outside = max(d, 0.0) / max(halo, 1e-4);
    // Falls smoothly to nothing at the quad's edge, so stacked halos never show a plate.
    float fall = max(0.0, 1.0 - outside);
    float glow = fall * fall * fall * exp(-outside * 2.0);
    // A hot core, whiter than its colour along its middle, its own colour at its edges, inside a halo of the colour.
    vec3 hot = mix(vColour, vec3(max(vColour.r, max(vColour.g, vColour.b))), whiten * deep);
    vec3 light = hot * inside * core * (0.75 + 0.25 * deep) + vColour * glow * 0.5;
    // A ramp, when asked: full at the shape's -y end, falling to the ramp's share at its +y end.
    if (ramp >= 0.0) light *= pow(mix(1.0, pow(ramp, 0.333), clamp(vPoint.y / max(vHalf.y, 1e-4) * 0.5 + 0.5, 0.0, 1.0)), 3.0);
    if (dot(light, vec3(1.0)) < 0.002) discard;
    gl_FragColor = vec4(light, 1.0);
    #include <colorspace_fragment>
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
  /** Ends the frame: only the shapes added since `begin` are drawn. */
  end(): void;
}

/**
 * Makes a set of up to `capacity` glowing shapes. `facing: true` turns each to the eye (sparks, lanterns, motes);
 * otherwise they lie in the mesh's own x-y plane (bars on a lane, a strip along a keyboard: turn the mesh to place
 * them). `halo` is how far the glow reaches round each shape, `round` its corner radius, both in metres; a shape
 * whose `round` is half its size is a disc; `whiten` makes the core hotter than its colour; `ramp` fades each shape
 * from full at its -y end to that share at its +y end (a key lit from its front). Add `object` to the scene.
 */
export function glows(
  capacity: number,
  options: {
    facing?: boolean;
    halo?: number;
    round?: number;
    core?: number;
    whiten?: number;
    ramp?: number;
  } = {},
): Glows {
  const uniforms = () => ({
    facing: { value: options.facing === true ? 1 : 0 },
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
      if (used >= capacity || strength <= 0) return;
      const m = used * 16;
      matrices[m] = width;
      matrices[m + 1] = 0;
      matrices[m + 2] = 0;
      matrices[m + 3] = 0;
      matrices[m + 4] = 0;
      matrices[m + 5] = height;
      matrices[m + 6] = 0;
      matrices[m + 7] = 0;
      matrices[m + 8] = 0;
      matrices[m + 9] = 0;
      matrices[m + 10] = 1;
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

/** Makes up to `capacity` sparks, drawn as one mesh that faces the eye. Add `mesh` to the scene. */
export function sparks(capacity = 512): Sparks {
  const light = glows(capacity, { facing: true, halo: 0.006, round: 1, core: 1, whiten: 0.6 });
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

/** A keyboard of light: dark glass keys that light in a colour when played. */
export interface LightKeys {
  readonly group: Group;
  /** The key's centre left to right in the keyboard's frame, and its top's height; NaN for a key it does not have. */
  x(midi: number): number;
  readonly top: number;
  /** The front edge's z in the keyboard's frame; the keys run back from it toward -z. */
  readonly front: number;
  readonly back: number;
  /** A white key's width. */
  readonly keyWidth: number;
  /** How far a black key's top stands above a white key's. */
  readonly raised: number;
  /** Lights `midi` at `amount` (0 to 1) in `colour` for this frame; call between `begin` and `end` each frame. */
  light(midi: number, colour: Color, amount: number): void;
  begin(): void;
  end(): void;
}

/**
 * A keyboard from `low` to `high` (MIDI numbers, widened to white keys at both ends), `width` metres long, its front
 * edge along z = 0 and its keys' tops at y = 0 in its own group: place the group. Two meshes, white and black keys.
 */
export function lightKeys(low: number, high: number, options: { width?: number; idle?: Color } = {}): LightKeys {
  let first = Math.floor(low);
  while (isBlack(first)) first -= 1;
  let last = Math.ceil(high);
  while (isBlack(last)) last += 1;
  let whites = 0;
  for (let m = first; m <= last; m++) if (!isBlack(m)) whites += 1;
  const width = options.width ?? whites * 0.05;
  const keyWidth = width / whites;
  const whiteLength = keyWidth * 6;
  const blackLength = whiteLength * 0.62;
  const idle = options.idle ?? new Color('#0D1222');
  const blackIdle = idle.clone().multiplyScalar(0.4);
  const xs = new Map<number, number>();
  let w = 0;
  for (let m = first; m <= last; m++) {
    if (!isBlack(m)) {
      xs.set(m, -width / 2 + keyWidth * (w + 0.5));
      w += 1;
    } else xs.set(m, -width / 2 + keyWidth * w);
  }
  const whiteKeys = [...xs.keys()].filter((m) => !isBlack(m));
  const blackKeys = [...xs.keys()].filter((m) => isBlack(m));
  const make = (keys: number[], size: [number, number, number], y: number, z: number, base: Color) => {
    const mesh = new InstancedMesh(new BoxGeometry(...size), new MeshBasicMaterial({ toneMapped: false }), keys.length);
    mesh.instanceColor = new InstancedBufferAttribute(new Float32Array(keys.length * 3), 3);
    mesh.instanceColor.setUsage(DynamicDrawUsage);
    mesh.instanceMatrix.setUsage(DynamicDrawUsage);
    const m = mesh.instanceMatrix.array as Float32Array;
    keys.forEach((key, i) => {
      m.set([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, xs.get(key) as number, y, z, 1], i * 16);
      mesh.setColorAt(i, base);
    });
    return mesh;
  };
  const depth = 0.018;
  const white = make(whiteKeys, [keyWidth * 0.92, depth, whiteLength], -depth / 2, -whiteLength / 2, idle);
  const black = make(blackKeys, [keyWidth * 0.55, depth, blackLength], depth * 0.6, -whiteLength + blackLength / 2, blackIdle);
  const group = new Group();
  group.add(white, black);
  // The glass's edge: a faint line along each key's front top, and the glow a played key throws along the front.
  const lips = glows(xs.size * 2, { halo: 0.004, round: 0.0005 });
  const front = glows(xs.size, { halo: 0.022, round: 0.002 });
  // A played key's light from within: a soft glow lying on its top, brightest toward the front.
  const tops = glows(xs.size, { halo: 0.012, round: 0.004, whiten: 0.15, ramp: 0.05 });
  tops.object.rotation.x = -Math.PI / 2;
  // And a near-white heat across the front fifth of a played key, where it is struck.
  const heat = glows(xs.size, { halo: 0.025, round: 0.006, whiten: 0.6 });
  heat.object.rotation.x = -Math.PI / 2;
  const seams = glows(xs.size, { halo: 0.002, round: 0.0003 });
  seams.object.rotation.x = -Math.PI / 2;
  group.add(lips.object, front.object, tops.object, heat.object, seams.object);
  const lipColour = new Color('#9DB4FF');
  const level = new Map<number, number>();
  const hue = new Map<number, Color>();
  for (const key of xs.keys()) hue.set(key, new Color());
  const mix = new Color();
  const paint = (mesh: InstancedMesh, keys: number[], base: Color, lift: number) => {
    const m = mesh.instanceMatrix.array as Float32Array;
    keys.forEach((key, i) => {
      const amount = Math.min(1, level.get(key) ?? 0);
      // Light adds to the glass: a played key glows in its colour, brighter the harder it is played.
      mix.copy(hue.get(key) as Color).multiplyScalar(0.04 * amount).add(base);
      mesh.setColorAt(i, mix);
      // A played key goes down a few millimetres.
      m[i * 16 + 13] = lift - 0.004 * amount;
    });
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor !== null) mesh.instanceColor.needsUpdate = true;
  };
  return {
    group,
    x: (midi) => xs.get(midi) ?? Number.NaN,
    top: 0,
    front: 0,
    back: -whiteLength,
    keyWidth,
    raised: depth * 1.1,
    light(midi, colour, amount) {
      if (!xs.has(midi) || amount <= (level.get(midi) ?? 0)) return;
      level.set(midi, amount);
      hue.get(midi)?.copy(colour);
    },
    begin() {
      level.clear();
    },
    end() {
      paint(white, whiteKeys, idle, -depth / 2);
      paint(black, blackKeys, blackIdle, depth * 0.6);
      lips.begin();
      front.begin();
      tops.begin();
      heat.begin();
      seams.begin();
      for (const [key, x] of xs) {
        const amount = Math.min(1, level.get(key) ?? 0);
        const raised = isBlack(key) ? depth * 1.1 : 0;
        const z = isBlack(key) ? -whiteLength + blackLength : 0;
        const wide = keyWidth * (isBlack(key) ? 0.5 : 0.86);
        if (amount > 0) {
          const long = isBlack(key) ? blackLength : whiteLength;
          const middle = isBlack(key) ? -whiteLength + blackLength / 2 : -whiteLength / 2;
          // One ramp of light along the key: hottest at its front, where it is struck, dim at its back. Laid flat, the
          // glow's y is the key's -z (its -y end the key's front) and its z the height above the top.
          tops.add(x, -middle, raised + 0.001, wide * 0.92, long, hue.get(key) as Color, 0.3 * amount);
          const near = isBlack(key) ? -whiteLength + blackLength : 0;
          heat.add(x, -(near - long * 0.1), raised + 0.0015, wide * 0.7, long * 0.2, hue.get(key) as Color, 0.35 * amount);
          // A soft strip along the front edge, no wider than the key.
          if (!isBlack(key)) front.add(x, -depth * 0.55, 0.003, keyWidth * 0.9, 0.005, hue.get(key) as Color, 0.4 * amount);
        } else lips.add(x, raised + 0.0005, z + 0.001, wide, 0.0012, lipColour, isBlack(key) ? 0.04 : 0.06);
        // A faint seam down each white key's left side, so the keys read apart, not as one plate.
        if (!isBlack(key)) seams.add(x - keyWidth / 2, whiteLength / 2, 0.0005, 0.0008, whiteLength * 0.96, lipColour, 0.012);
      }
      lips.end();
      front.end();
      tops.end();
      heat.end();
      seams.end();
    },
  };
}

/** Notes of light falling toward a keyboard of light, played by a score: what `fallingNotes` makes. */
export interface FallingNotes {
  /** The lane, the notes and their light, inside the keyboard's group: they move with it. */
  readonly group: Group;
  /** Draws a frame from the player's time; pass it to `ambientMotion`. */
  move(time: number): void;
}

/**
 * Makes the light of a score playing on a keyboard of light (`lightKeys`): bars of light come down a long dark lane
 * rising away from the keys' back edge, each landing on its key at the moment its note is heard (`player.now()`); the
 * key lights in its part's colour, a flare and a cloud of sparks rise where it lands, and its light lies on the floor
 * `height` metres below the keys, reflected as on dark glass. Built inside `keys.group`, so place that group.
 * `colours` are the parts' colours, by part number; `seconds` is how far ahead the lane shows.
 */
export function fallingNotes(
  player: Player,
  keys: LightKeys,
  options: { colours?: readonly Color[]; seconds?: number; length?: number; rise?: number; height?: number } = {},
): FallingNotes {
  const colours = options.colours ?? [new Color('#2F9BFF'), new Color('#C77DFF')];
  const length = options.length ?? 7;
  const speed = length / (options.seconds ?? 6.5);
  const height = options.height ?? 0.86;
  const group = new Group();
  keys.group.add(group);
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const note of player.score.notes) {
    if (Number.isNaN(keys.x(note.pitch))) continue;
    low = Math.min(low, keys.x(note.pitch));
    high = Math.max(high, keys.x(note.pitch));
  }
  const width = Math.max(0.4, high - low + keys.keyWidth * 6);
  const centre = (low + high) / 2;

  // The lane: dark, darker into the distance and at its sides, so it has no edge but the light on it.
  const lane = new Group();
  // From the black keys' height, so a note landing among them is never cut by one.
  lane.position.set(0, keys.top + keys.raised, keys.back - 0.004);
  lane.rotation.x = -Math.PI / 2 + ((options.rise ?? 18) * Math.PI) / 180;
  group.add(lane);
  const surface = new PlaneGeometry(width * 1.3, length, 12, 24);
  surface.translate(centre, length / 2, -0.002);
  const shade: number[] = [];
  const near = new Color('#0A1026');
  const position = surface.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const side = Math.abs(position.getX(i) - centre) / (width * 0.65);
    const along = position.getY(i) / length;
    // Clear, not black, at its far end and its sides: it melts into whatever place is behind it.
    const shown = Math.max(0, 1 - Math.max(0, along - 0.45) / 0.55) * Math.max(0, 1 - Math.max(0, side - 0.55) / 0.45);
    shade.push(near.r, near.g, near.b, shown);
  }
  surface.setAttribute('color', new Float32BufferAttribute(shade, 4));
  const floorOfLane = new Mesh(
    surface,
    new MeshBasicMaterial({ vertexColors: true, transparent: true, depthWrite: false, toneMapped: false }),
  );
  floorOfLane.renderOrder = -0.5;
  lane.add(floorOfLane);
  // Each bar twice: its own light with a hot core, and a wide faint light it throws on the lane round it.
  const cast = glows(900, { halo: 0.16, round: 1, core: 0 });
  const bars = glows(900, { halo: 0.05, round: 1, whiten: 0.3 });
  lane.add(cast.object, bars.object);
  const flares = glows(160, { facing: true, halo: 0.16, round: 1, whiten: 0.2 });
  const motes = sparks(900);
  group.add(flares.object, motes.object);
  // On the floor: a faint streak of each played key's light toward the person, as on dark glass.
  const floor = glows(200, { halo: 0.3, round: 0.05, core: 0.6 });
  floor.object.rotation.x = -Math.PI / 2;
  floor.object.position.set(0, -height + 0.005, 0);
  group.add(floor.object);
  let last = Number.NaN;
  let previous = Number.NaN;
  let now = 0;
  const draw = (note: { pitch: number; part: number; loud: number }, start: number, end: number) => {
    const x = keys.x(note.pitch);
    if (Number.isNaN(x)) return;
    const colour = colours[note.part % colours.length] as Color;
    const head = (start - now) * speed;
    // A gap before the next note on the same key, so each note reads as its own.
    const tail = (end - now) * speed - Math.min(0.14, 0.06 + 0.06 * (end - start) * speed);
    const from = Math.max(0, head);
    // Never past the lane's end: a note comes out of the dark there.
    const to = Math.min(tail, length * 0.92);
    if (to > from) {
      // Brightest as it lands, fading to nothing by the far end; a long note a little softer, so held notes never
      // pile into a wall of light.
      // Out of the dark: faded as the eye sees it, then made linear (the screen's curve lifts dim light a lot), so a
      // note is truly near black at the lane's far end.
      const along = Math.min(1, (from + to) / 2 / length);
      const seen = (1 - along) ** 1.5 * Math.min(1, (1 - along) / 0.35);
      const fade = seen ** 2.2 / (1 + (to - from) * 0.5);
      const wide = keys.keyWidth * (isBlack(note.pitch) ? 0.5 : 0.74);
      bars.add(x, (from + to) / 2, 0.004, wide, to - from, colour, 0.8 * fade);
      // A line with only a halo, so the light it throws has no sides.
      cast.add(x, (from + to) / 2, 0.002, 0.002, to - from, colour, 0.12 * fade);
    }
    if (now >= start && now < end) {
      // Bright at the strike, settling while held.
      const strike = Math.exp(-(now - start) * 5);
      const amount = 0.55 + 0.45 * strike;
      keys.light(note.pitch, colour, amount * (0.65 + 0.35 * note.loud));
      flares.add(x, 0.015, keys.back + 0.012, 0.02, 0.02, colour, strike * 0.35);
      floor.add(x, -0.3, 0, keys.keyWidth * 0.6, 0.4, colour, 0.08 * amount);
    }
    // A cloud of sparks where each note lands, on the frame it lands.
    if (!Number.isNaN(previous) && start > previous && start <= now)
      motes.emit(x, 0.01, keys.back + 0.015, colour, 5 + Math.round(8 * note.loud), {
        rise: 0.4,
        spread: keys.keyWidth * 0.5,
        life: 0.7,
        size: 0.004,
      });
  };
  return {
    group,
    move(time) {
      now = player.now();
      const delta = Number.isNaN(last) ? 0 : time - last;
      last = time;
      keys.begin();
      bars.begin();
      cast.begin();
      flares.begin();
      floor.begin();
      player.each(0.2, length / speed, draw);
      keys.end();
      bars.end();
      cast.end();
      flares.end();
      floor.end();
      motes.update(delta);
      previous = now;
    },
  };
}
