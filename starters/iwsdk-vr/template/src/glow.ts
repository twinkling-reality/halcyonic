import {
  AdditiveBlending,
  BoxGeometry,
  BufferGeometry,
  Color,
  DoubleSide,
  DynamicDrawUsage,
  Float32BufferAttribute,
  Group,
  InstancedBufferAttribute,
  InstancedMesh,
  Mesh,
  MeshBasicMaterial,
  PlaneGeometry,
  ShaderMaterial,
  Vector2,
} from 'three';
import { pageMusic, type Player } from './instruments.js';
import { isBlack, keyDown, RING_SECONDS, ringRadius, ringStrength, roundColumn, swell } from './score.js';

/**
 * Light for apps of music and light: soft glowing shapes, sparks, and a keyboard of light. Each kind is ONE instanced
 * mesh (one draw call) whose glow is drawn in its own shader, so there is no full-screen bloom pass to pay for in the
 * headset; each frame writes into arrays made once, so nothing new is made per frame. Light adds up where it
 * overlaps, as light does: keep a dark place behind it (`src/places.ts`).
 */

const VERTEX = /* glsl */ `
  uniform float facing;
  uniform float halo;
  uniform float stretch;
  uniform float narrow;
  uniform float level;
  uniform float lift;
  varying vec2 vPoint;
  varying vec2 vHalf;
  varying vec3 vColour;
  varying float vNear;
  varying float vHigh;
  void main() {
    vHigh = 1e4;
    // How near the water a reflected light is: a light high above it is seen faintly in it.
    vNear = lift > 0.0 ? exp(-max(0.0, instanceMatrix[3].y - level) / lift) : 1.0;
    vec3 x = instanceMatrix[0].xyz;
    vec3 y = instanceMatrix[1].xyz;
    // A reflection's length differs light to light (its own wind on the water), never one shape for all.
    float own = lift > 0.0 ? 0.7 + 0.6 * fract(sin(dot(instanceMatrix[3].xz, vec2(12.9898, 78.233))) * 43758.5453) : 1.0;
    vec2 size = vec2(length(x) * narrow, length(y) * stretch * own);
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
      // Its right to the eye's right, so the quad faces the eye and is never culled as a back face.
      vec3 side = normalize(cross(centre - cameraPosition, along) + vec3(1e-5, 0.0, 0.0));
      vec3 world = centre + side * vPoint.x + along * vPoint.y;
      vHigh = world.y;
      at = viewMatrix * vec4(world, 1.0);
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
  uniform vec2 fade;
  uniform float dim;
  uniform float ripple;
  uniform float time;
  uniform vec2 under;
  varying float vNear;
  varying float vHigh;
  varying vec2 vPoint;
  varying vec2 vHalf;
  varying vec3 vColour;
  void main() {
    // Distance in metres from the rounded shape's edge: below zero inside.
    float r = min(corner, min(vHalf.x, vHalf.y));
    // On rippled water a reflection is broken into bands that sway sideways and come and go, never a still streak.
    vec2 at = vPoint;
    float band = 1.0;
    if (ripple > 0.0) {
      float w = vPoint.y / max(vHalf.y, 1e-4) * ripple;
      float seed = vColour.g * 31.0 + vColour.r * 17.0;
      at.x += (sin(w * 0.61 + time * 0.9 + seed) + 0.6 * sin(w * 1.37 - time * 1.3 + seed * 2.0)) * vHalf.x * 0.3;
      // Two slow waves of different lengths: the light comes and goes along the reflection, never in even rungs.
      float wave = 0.5 + 0.3 * sin(w + time * 1.1 + seed) + 0.2 * sin(w * 1.7 - time * 0.7 + seed * 3.0);
      band = 0.72 + 0.28 * wave;
    }
    vec2 q = abs(at) - vHalf + r;
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
    vec3 light = (hot * inside * core * (0.45 + 0.55 * deep) + vColour * glow * 0.5) * dim * vNear * band;
    // A ramp, when asked: full at the shape's -y end, falling to the ramp's share at its +y end.
    if (ramp >= 0.0) light *= pow(mix(1.0, pow(ramp, 0.333), clamp(vPoint.y / max(vHalf.y, 1e-4) * 0.5 + 0.5, 0.0, 1.0)), 3.0);
    // A fade, when asked: from nothing at its -y end (fade.x) and its +y end (fade.y) to full that share of its length
    // in from each, so a shape has no end at all.
    float fromFoot = clamp(0.5 + 0.5 * vPoint.y / max(vHalf.y + halo, 1e-4), 0.0, 1.0);
    if (fade.x > 0.0) light *= smoothstep(0.0, fade.x, fromFoot);
    if (fade.y > 0.0) light *= smoothstep(0.0, fade.y, 1.0 - fromFoot);
    // Below a polished floor, when asked: dimmer, as the floor's reflection of it.
    if (under.y > 0.0) light *= mix(under.y, 1.0, smoothstep(under.x - 0.25, under.x + 0.1, vHigh));
    if (dot(light, vec3(1.0)) < 0.0004) discard;
    gl_FragColor = vec4(light, 1.0);
    #include <colorspace_fragment>
    // A little noise below one step of the screen's colour, so soft falloffs never show as rings.
    gl_FragColor.rgb += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;
  }
`;

/** A set of soft glowing shapes drawn as one: rounded bars, dots and discs, each with a halo of light round it. */
export interface Glows {
  /** What to add to the scene: the shapes, and their reflection when they have one. */
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
 * that share at its +y end (a drop brightest at its foot); `fade` fades it from nothing at both ends to full that share
 * of its whole length in from each (a shaft of light with no end), or a pair, its -y end's share and its +y end's;
 * with `under` (upright shapes), whatever lies below the world height `level` is `strength` as bright: a shaft running
 * on into a polished floor as its reflection. With `mirror`, the shapes are also seen reflected in a
 * surface at height `level` of the object's frame, `strength` as bright, `stretch` times as tall (water stretches a
 * light's reflection downward) and `narrow` times as wide, fading the higher a shape hangs over it (`lift` metres
 * to fall to a third), broken into `ripple` bands that sway (0 for dark glass, about 9 for a lake at dusk). Add
 * `object` to the scene.
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
    fade?: number | readonly [number, number];
    under?: { level: number; strength: number };
    mirror?: { level: number; strength?: number; stretch?: number; narrow?: number; lift?: number; ripple?: number };
  } = {},
): Glows {
  const uniforms = (dim: number, stretch: number) => ({
    facing: { value: options.upright === true ? 2 : options.facing === true ? 1 : 0 },
    halo: { value: options.halo ?? 0.03 },
    corner: { value: options.round ?? 0.01 },
    core: { value: options.core ?? 1 },
    whiten: { value: options.whiten ?? 0 },
    dim: { value: dim },
    ramp: { value: options.ramp ?? -1 },
    under: { value: new Vector2(options.under?.level ?? 0, options.under?.strength ?? 0) },
    fade: { value: typeof options.fade === 'number' ? new Vector2(options.fade, options.fade) : new Vector2(...(options.fade ?? [0, 0])) },
    stretch: { value: stretch },
    narrow: { value: 1 },
    level: { value: 0 },
    lift: { value: 0 },
    ripple: { value: 0 },
    time: { value: 0 },
  });
  const material = new ShaderMaterial({
    uniforms: uniforms(1, 1),
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
  // Drawn over what is near them: after everything else that is see-through, the keys of light included.
  if (options.over === true) mesh.renderOrder = OVER;
  mesh.count = 0;
  const object = new Group();
  object.add(mesh);
  let reflection: InstancedMesh | null = null;
  if (options.mirror !== undefined) {
    // The same shapes drawn again upside down about the surface: their arrays are shared, so it costs one draw call.
    const mirrored = material.clone();
    mirrored.uniforms = uniforms(options.mirror.strength ?? 0.35, options.mirror.stretch ?? 1);
    // A reflection has no hot core of its own: only its glow, softened by the water.
    mirrored.uniforms.core = { value: 0 };
    // Narrower, with a wider halo: water draws a light softer than the light, never a copy of its shape.
    mirrored.uniforms.narrow = { value: options.mirror.narrow ?? 0.6 };
    mirrored.uniforms.ripple = { value: options.mirror.ripple ?? 0 };
    // Seen in still water, a light fades the higher it hangs over it.
    mirrored.uniforms.level = { value: options.mirror.level };
    mirrored.uniforms.lift = { value: options.mirror.lift ?? 0 };
    mirrored.uniforms.halo = { value: (options.halo ?? 0.03) * 1.4 };
    mirrored.side = DoubleSide;
    // Seen only where nothing stands in front of the mirror: a key or a hand hides its reflection.
    mirrored.depthTest = true;
    reflection = new InstancedMesh(geometry, mirrored, capacity);
    reflection.instanceMatrix = mesh.instanceMatrix;
    reflection.instanceColor = mesh.instanceColor;
    reflection.frustumCulled = false;
    reflection.count = 0;
    reflection.scale.y = -1;
    reflection.position.y = 2 * options.mirror.level;
    // The ripples move on the page's own clock, whatever the app's time is doing.
    const clock = mirrored.uniforms.time as { value: number };
    reflection.onBeforeRender = () => {
      clock.value = (performance.now() / 1000) % 1000;
    };
    object.add(reflection);
  }
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
      if (reflection !== null) reflection.count = used;
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
  return key.length * (key.black ? LANDS.black : LANDS.white);
}

/** When the keys of light are drawn among the see-through things, and when the light drawn over what is near it is. */
const KEYS = 0.5;
const OVER = 1;

/** `landing`'s shares of a key's length. */
const LANDS = { white: 0.14, black: 0.5 };

/** A keyboard of light: keys of glass lit from within, pale white keys and smoky black ones, lit in a colour when played. */
export interface LightKeys {
  readonly group: Group;
  /** Where a key is, or undefined for a key this keyboard does not have. */
  spot(midi: number): KeySpot | undefined;
  /** Every key, low to high. */
  readonly spots: readonly KeySpot[];
  /** A white key's width. */
  readonly keyWidth: number;
  /** With `glass`, the height of the dark glass the keys stand on, where light near it is seen mirrored. */
  readonly glass: number | undefined;
  /** Lights `midi` at `amount` (0 to 1) in `colour` for this frame; call between `begin` and `end` each frame. */
  light(midi: number, colour: Color, amount: number): void;
  begin(): void;
  end(): void;
}

const KEY_VERTEX = /* glsl */ `
  uniform float height;
  uniform float curve;
  uniform float dip;
  attribute vec4 tone;
  attribute float press;
  attribute vec4 lit;
  attribute vec3 near;
  varying vec3 vLocal;
  varying vec3 vHalf;
  varying vec4 vTone;
  varying vec4 vLit;
  varying vec3 vNear;
  varying vec3 vNormal;
  varying vec3 vView;
  void main() {
    // The face's normal and the way to the eye, in the world, for the glass's sheen.
    vec4 world = modelMatrix * instanceMatrix * vec4(position, 1.0);
    vNormal = normalize(mat3(modelMatrix) * mat3(instanceMatrix) * normal);
    vView = normalize(cameraPosition - world.xyz);
    // In metres in the key's own frame: x across it, y up, z from its back to its front.
    vec3 size = vec3(length(instanceMatrix[0].xyz), length(instanceMatrix[1].xyz), length(instanceMatrix[2].xyz));
    vLocal = position * size;
    vHalf = vec3(size.x, size.y * height, size.z) * 0.5;
    vTone = tone;
    vLit = lit;
    vNear = near;
    // On a curve each key widens toward its back with the arc, as a wedge, so neighbours meet along their whole length
    // instead of parting toward the back.
    vec3 shaped = position;
    if (curve > 0.0) shaped.x *= 1.0 + (0.5 - position.z) * size.z / curve;
    // Played, the key goes down as a real key does: turning on its back edge, its front dipping by dip metres.
    shaped.y -= press * dip * (position.z + 0.5);
    gl_Position = projectionMatrix * modelViewMatrix * instanceMatrix * vec4(shaped, 1.0);
  }
`;

const KEY_FRAGMENT = /* glsl */ `
  uniform float land;
  uniform vec3 edgeColour;
  uniform float dim;
  varying vec3 vLocal;
  varying vec3 vHalf;
  varying vec4 vTone;
  varying vec4 vLit;
  varying vec3 vNear;
  varying vec3 vNormal;
  varying vec3 vView;
  void main() {
    // How far in from each pair of faces: the least is the face this point is on, the middle one its nearest edge.
    vec3 e = max(vHalf - abs(vLocal), 0.0);
    float lo = min(e.x, min(e.y, e.z));
    float hi = max(e.x, max(e.y, e.z));
    float edge = e.x + e.y + e.z - lo - hi;
    float top = (e.y <= lo + 1e-5 && vLocal.y > 0.0) ? 1.0 : 0.0;
    float front = (top < 0.5 && e.z <= lo + 1e-5 && vLocal.z > 0.0) ? 1.0 : 0.0;
    float face = top + 0.8 * front + 0.35 * (1.0 - top - front);
    float span = 2.0 * vHalf.z;
    // From the key's front edge back along it.
    float along = vHalf.z - vLocal.z;
    // Glass lit from within: a soft light in its body, brightest toward its front and deeper in its middle than at
    // its sides, as light gathers in thick glass.
    float across = clamp(e.x / max(vHalf.x, 1e-4), 0.0, 1.0);
    vec3 c = vTone.rgb * face * (0.4 + 0.45 * exp(-along / (0.45 * span))) * (0.75 + 0.35 * across);
    // A sheen where the glass is seen at a glancing angle, the light of the place caught on its polish.
    float glance = pow(1.0 - clamp(abs(dot(normalize(vNormal), normalize(vView))), 0.0, 1.0), 4.0);
    c += edgeColour * glance * 0.55 * vTone.a;
    // Its edges bright, as the edges of lit glass are: a fine line over a softer one.
    float line = exp(-edge / 0.0011) + 0.3 * exp(-edge / 0.004);
    c += edgeColour * line * vTone.a;
    // Played, the note's light fills the key from inside: hottest where the note landed, spreading along the key's
    // length as the strike fades, and running bright along its edges.
    float d = abs(along - land * span);
    float spread = exp(-d / (span * (0.06 + 0.75 * vLit.a)));
    vec3 hot = mix(vLit.rgb, vec3(max(vLit.r, max(vLit.g, vLit.b))), 0.45 * spread * spread);
    c += hot * spread * (0.35 + face) * 3.4 + vLit.rgb * line * 2.0;
    // A neighbour's light caught on this key's edges.
    c += vNear * line;
    // Toward the keyboard's ends the keys grow clear as well as dim, so it ends in the place's own light, never in a
    // dark block.
    gl_FragColor = vec4(c * dim, vTone.a * vTone.a);
    #include <colorspace_fragment>
    gl_FragColor.rgb += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;
  }
`;

/**
 * A keyboard from `low` to `high` (MIDI numbers, widened to white keys at both ends), its keys' tops at y = 0 in its
 * own group. With `arc`, it curves round the group's origin, its front edge `arc` metres from it, low keys to the left
 * and high to the right of -z, its middle straight ahead: stand a person at the origin and they stand inside it. Without, it runs straight
 * along x, its front edge along z = 0. Each key is glass lit from within with bright edges and a sheen where it is
 * seen at a glancing angle: the white keys pale in `idle`, the black keys smoky and darker, resting on them short of
 * their back edge; on a curve each key widens toward its back, so the keyboard is one piece with no gaps opening
 * behind. A played key goes down, turning on its back edge as a piano's does, and fills with its note's light from
 * where the note lands along its whole length, and its neighbours' edges catch it. Toward both ends the keys grow dim
 * and clear, so the keyboard ends in the place's own light. Two meshes for the keys, and with `glass` one for the shelf they stand on.
 */
export function lightKeys(
  low: number,
  high: number,
  options: { keyWidth?: number; arc?: number; idle?: Color; dark?: Color; glass?: boolean } = {},
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
  const raised = depth * 0.55;
  const idle = options.idle ?? new Color('#40508A');
  // Smoky glass: far darker than the white keys, so the two kinds read apart at a glance.
  const blackIdle = idle.clone().multiplyScalar(0.12);
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
    // A black key ends a little short of the white keys' back edge, never past it.
    const place = at(s, black ? whiteLength * 0.9 - blackLength : 0);
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
      width: black ? keyWidth * 0.55 : keyWidth * 0.96,
      length: black ? blackLength : whiteLength,
    });
  }
  const byKey = new Map(spots.map((spot, i) => [spot.midi, i]));
  // The glass fades into the place's own dark (`dark`) and grows clear toward both ends of the keyboard, so it has no
  // cut end and no dark block.
  const dark = options.dark ?? new Color('#070B1C');
  const ends = (key: KeySpot) => {
    const i = spots.indexOf(key);
    return Math.min(1, Math.min(i, spots.length - 1 - i) / 12) ** 1.5;
  };
  const whiteKeys = spots.filter((spot) => !spot.black);
  const blackKeys = spots.filter((spot) => spot.black);
  // A white key stands from y = -depth to its top at 0; a black key rises from the same foot, between its neighbours,
  // to stand `raised` over them, so it grows out of the keyboard rather than lying on it.
  const whiteTall = depth;
  // A black key rests on the white keys' tops, sunk a hair into them, never reaching down between them.
  const sunk = depth * 0.04;
  const blackTall = sunk + raised;
  const whiteMiddle = -depth / 2;
  const blackMiddle = (raised - sunk) / 2;
  const make = (keys: KeySpot[], tall: number, middle: number, base: Color, edgeColour: Color, land: number, dip: number) => {
    const n = Math.max(1, keys.length);
    const geometry = new BoxGeometry(1, tall, 1);
    const tones = new Float32Array(n * 4);
    keys.forEach((key, i) => {
      const fade = ends(key);
      const tint = dark.clone().lerp(base, fade);
      tones.set([tint.r, tint.g, tint.b, fade], i * 4);
    });
    geometry.setAttribute('tone', new InstancedBufferAttribute(tones, 4));
    const lit = new InstancedBufferAttribute(new Float32Array(n * 4), 4);
    lit.setUsage(DynamicDrawUsage);
    geometry.setAttribute('lit', lit);
    const near = new InstancedBufferAttribute(new Float32Array(n * 3), 3);
    near.setUsage(DynamicDrawUsage);
    geometry.setAttribute('near', near);
    const press = new InstancedBufferAttribute(new Float32Array(n), 1);
    press.setUsage(DynamicDrawUsage);
    geometry.setAttribute('press', press);
    const material = new ShaderMaterial({
      uniforms: { height: { value: tall }, curve: { value: arc ?? 0 }, dip: { value: dip }, land: { value: land }, edgeColour: { value: edgeColour }, dim: { value: 1 } },
      vertexShader: KEY_VERTEX,
      fragmentShader: KEY_FRAGMENT,
      transparent: true,
      toneMapped: false,
    });
    const mesh = new InstancedMesh(geometry, material, n);
    // See-through toward its ends, so drawn after the light behind it (which then shows through) and before the light
    // drawn over it.
    mesh.renderOrder = KEYS;
    mesh.count = keys.length;
    const m = mesh.instanceMatrix.array as Float32Array;
    keys.forEach((key, i) => {
      // Turned to face the middle: its x along the keyboard, its z from back to front.
      const cx = key.x + (key.ox * key.length) / 2;
      const cz = key.z + (key.oz * key.length) / 2;
      m.set([key.tx * key.width, 0, key.tz * key.width, 0, 0, 1, 0, 0, -key.ox * key.length, 0, -key.oz * key.length, 0, cx, middle, cz, 1], i * 16);
    });
    return { mesh, lit: lit.array as Float32Array, near: near.array as Float32Array, press: press.array as Float32Array, litAttribute: lit, nearAttribute: near, pressAttribute: press };
  };
  const pale = new Color('#C4D2FF').multiplyScalar(0.75);
  const smoky = new Color('#7476C0').multiplyScalar(0.6);
  // How far a played key's front goes down: a white key a third of its width, nearly twice a piano's, so it is seen
  // to go down from a step away; a black key less, so it never sinks below the white keys it rests on.
  const whiteSet = make(whiteKeys, whiteTall, whiteMiddle, idle, pale, LANDS.white, keyWidth * 0.33);
  const blackSet = make(blackKeys, blackTall, blackMiddle, blackIdle, smoky, LANDS.black, raised * 0.9);
  const white = whiteSet.mesh;
  const black = blackSet.mesh;
  // Where each key's light is written: its set and its place in it.
  const slots = spots.map((spot) => (spot.black ? { set: blackSet, i: blackKeys.indexOf(spot) } : { set: whiteSet, i: whiteKeys.indexOf(spot) }));
  // With `glass`, the keys stand on a shelf of dark glass that mirrors them and the light near it, fading with height.
  const glass = options.glass === true ? -depth - 0.002 : undefined;
  const group = new Group();
  group.add(white, black);
  if (glass !== undefined) {
    // The shelf: a faint sheen of the keys' own colour, widest just under them and fading out on every side, so it
    // has no edge; it writes no depth, so what is mirrored under it shows through.
    const along = 64;
    const across = 8;
    const sheen: number[] = [];
    const shade: number[] = [];
    const inner = -0.4;
    const outer = whiteLength + 0.3;
    for (let i = 0; i <= along; i++) {
      for (let j = 0; j <= across; j++) {
        const u = i / along;
        const v = j / across;
        const p = at(-width / 2 + u * width, inner + v * (outer - inner));
        sheen.push(p.x, glass, p.z);
        const out = inner + v * (outer - inner);
        const k = Math.sin(Math.PI * u) ** 1.2 * Math.exp(-(((out - whiteLength * 0.4) / 0.28) ** 2)) * 0.07;
        shade.push(idle.r * k, idle.g * k, idle.b * k);
      }
    }
    const order: number[] = [];
    for (let i = 0; i < along; i++) {
      for (let j = 0; j < across; j++) {
        const a = i * (across + 1) + j;
        order.push(a, a + 1, a + across + 1, a + 1, a + across + 2, a + across + 1);
      }
    }
    const shelf = new BufferGeometry();
    shelf.setAttribute('position', new Float32BufferAttribute(sheen, 3));
    shelf.setAttribute('color', new Float32BufferAttribute(shade, 3));
    shelf.setIndex(order);
    group.add(
      new Mesh(
        shelf,
        new MeshBasicMaterial({
          vertexColors: true,
          toneMapped: false,
          transparent: true,
          blending: AdditiveBlending,
          depthWrite: false,
          side: DoubleSide,
        }),
      ),
    );
    // The keys' own boxes are not mirrored: seen just under a key, a turned-over box reads as a second box inside it.
  }
  // Each key's light this frame, the last frame's, and its strike's peak: how far a strike's light has spread along
  // its key is read from how far it has faded from that peak, so it needs no clock of its own.
  const level = new Float32Array(spots.length);
  const was = new Float32Array(spots.length);
  const peak = new Float32Array(spots.length);
  const hue = spots.map(() => new Color());
  const sets = [whiteSet, blackSet];
  return {
    group,
    spot: (midi) => {
      const i = byKey.get(midi);
      return i === undefined ? undefined : spots[i];
    },
    spots,
    keyWidth,
    glass,
    light(midi, colour, amount) {
      const k = byKey.get(midi);
      if (k === undefined || amount <= (level[k] as number)) return;
      level[k] = amount;
      hue[k]?.copy(colour);
    },
    begin() {
      level.fill(0);
    },
    end() {
      for (let k = 0; k < spots.length; k++) {
        const amount = Math.min(1, level[k] as number);
        if (amount <= 0) peak[k] = 0;
        else if (amount > (was[k] as number) * 1.1 + 0.02) peak[k] = amount;
        else peak[k] = Math.max(peak[k] as number, amount);
        was[k] = amount;
      }
      for (let k = 0; k < spots.length; k++) {
        const { set, i } = slots[k] as (typeof slots)[number];
        const amount = Math.min(1, level[k] as number);
        const c = hue[k] as Color;
        set.lit[i * 4] = c.r * amount;
        set.lit[i * 4 + 1] = c.g * amount;
        set.lit[i * 4 + 2] = c.b * amount;
        set.lit[i * 4 + 3] = amount > 0 ? 1 - amount / Math.max(peak[k] as number, 1e-4) : 0;
        // The light of the keys either side, caught on this one's edges.
        let r = 0;
        let g = 0;
        let b = 0;
        for (let s = 0; s < NEIGHBOURS.length; s += 2) {
          const n = k + (NEIGHBOURS[s] as number);
          const share = NEIGHBOURS[s + 1] as number;
          if (n < 0 || n >= spots.length) continue;
          const a = Math.min(1, level[n] as number) * share;
          if (a <= 0) continue;
          const h = hue[n] as Color;
          r += h.r * a;
          g += h.g * a;
          b += h.b * a;
        }
        set.near[i * 3] = r;
        set.near[i * 3 + 1] = g;
        set.near[i * 3 + 2] = b;
        // A played key is down while its light holds, and comes back up as its light dies away after it is let go.
        set.press[i] = keyDown(amount);
      }
      for (const set of sets) {
        set.litAttribute.needsUpdate = true;
        set.nearAttribute.needsUpdate = true;
        set.pressAttribute.needsUpdate = true;
      }
    },
  };
}

/** How much of a key's light its neighbours' edges catch: how many keys away, then the share, for each. */
const NEIGHBOURS = [-2, 0.12, -1, 0.35, 1, 0.35, 2, 0.12];

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
 * (`player.now()`); the key fills with its light from where it lands along its length and stays lit while the note is
 * held, a column of light rises from it, and a few sparks rise. Each note's light is coloured by where it lies in the music, low to high, moved a little by how
 * hard it is played (`colour` to choose another). `seconds` is how long a note is seen falling,
 * `fall` how far, in metres; `reach` how far off the hall answers each note with its colour, all round the person.
 * With `floor`, the floor's height in the keys' group's frame, each note's light pools on the floor under its key and
 * a ring of it runs out across the floor from there. With `bloom`, the moment in play seconds the hall blooms (the
 * `rise` given to `lightHall`), the bloom comes out of the keyboard: every key flares in its own colour, a column of
 * light rises from each, and (with `floor`) a warm ring runs out across the floor from the keys. Built inside
 * `keys.group`, so place that group.
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
    floor?: number;
    echo?: boolean;
    bloom?: number;
  } = {},
): FallingNotes {
  const fall = options.fall ?? 1.7;
  const reach = options.reach ?? 14;
  // On keys standing on glass, every key's flare and rising column are also seen in it; the falling drops are not, so
  // a note never reads as falling on through the glass, nor with a second head under its own.
  const mirror = keys.glass === undefined ? undefined : { level: keys.glass, strength: 0.4, lift: 0.35 };
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
  // A column of light rising from a struck key while its note sounds: a soft beam as wide as a third of the key, its
  // foot on the key, fading to nothing at its top.
  const blooms = glows(160, { upright: true, halo: 0.022, round: 0.004, core: 0.35, whiten: 0.3, ramp: 0, over: true, mirror });
  // The strike itself: a small hot point where the note lands, its light soft all round, never a disc of its own.
  const flares = glows(160, { facing: true, halo: 0.06, round: 1, core: 0.6, whiten: 0.4, over: true, mirror });
  const motes = sparks(700, { over: true });
  // The key fills with its note's light itself (`lightKeys`); on glass the light spreads on over the glass in front
  // of it, and, given `floor` (the floor's height in the keys' group's frame), it pools on the floor beneath it.
  const pools = glows(480, { halo: 0.35, round: 0.25, core: 0 });
  group.add(pools.object);
  // Given `floor`, each landing also sends a ring of its light out across the floor from under its key, so every note
  // is seen to land on the place and not only on the keyboard: at most eight at a time.
  const ripples = options.floor === undefined ? undefined : floorRings(RIPPLES + 1);
  // How far the keyboard's front edge is from its middle: where the bloom's wave starts out across the floor.
  const middleKey = keys.spots[Math.floor(keys.spots.length / 2)];
  const reachOut = middleKey === undefined ? 1 : Math.hypot(middleKey.x, middleKey.z);
  const bloomColour = new Color('#FFB070');
  if (ripples !== undefined) group.add(ripples.mesh);
  // The place answers: each note lights the far haze of the hall in its colour, low along the horizon where its key
  // points, to both sides and behind the person, so the music is all round them: a wide soft band of light in the
  // haze, never a disc hanging in the sky. Upright, so it stays level however the head tilts.
  // Each answer stays wholly above the floor (its halo smaller than its height), so the floor never meets it.
  // Rounded fully at their ends, so no band ever shows a cut edge.
  const echoes = glows(480, { upright: true, halo: 1.4, round: 1, core: 0, over: true });
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
    const to = Math.min(from + 0.15, fall);
    if (from >= 0 && to > from) {
      // Out of the dark overhead, brightest as it lands; faded as the eye sees it, then made linear.
      const up = (from + to) / 2 / fall;
      const seen = (1 - up) ** 1.4 * Math.min(1, (1 - up) / 0.3);
      const [px, py, pz] = paths.get(note.pitch) as [number, number, number];
      const mid = (from + to) / 2;
      const strength = 0.7 * seen ** 2.2;
      drops.place(lx + px * mid, spot.y + 0.004 + py * mid, lz + pz * mid, 1, 0, 0, px, py, pz, spot.width * 0.3, to - from, colour, strength);
      heads.add(lx + px * from, spot.y + 0.006 + py * from, lz + pz * from, 0.014, 0.014, colour, 0.9 * seen ** 2.2);
    }
    if (now >= start && now < end + 0.45) {
      const since = now - start;
      const strike = Math.exp(-since * 5);
      // Bright at the strike, then only a low glow while the note is held, and dying away for a moment after it is
      // let go, so even the shortest note is seen to light its key: nothing stands on a key waiting.
      const after = now > end ? Math.exp(-(now - end) * 7) : 1;
      const amount = (0.3 + 0.9 * strike) * after;
      keys.light(note.pitch, colour, amount * (0.65 + 0.35 * note.loud));
      flares.add(lx, spot.y + 0.006, lz, 0.007, 0.007, colour, strike * 0.6);
      if (keys.glass !== undefined) {
        // On glass, a wide soft pool of the note's colour spreads in front of its key.
        pools.place(lx - spot.ox * 0.12, keys.glass + 0.001, lz - spot.oz * 0.12, spot.tx, 0, spot.tz, spot.ox, 0, spot.oz, spot.width * 7, 0.36, colour, 0.1 * amount);
        // And each strike sends a pulse of its light spreading out over the glass, fading as it goes.
        if (since < 0.7) {
          const out = 0.08 + since * 1.4;
          const left = 1 - since / 0.7;
          pools.place(lx - spot.ox * 0.1, keys.glass + 0.0015, lz - spot.oz * 0.1, spot.tx, 0, spot.tz, spot.ox, 0, spot.oz, out * 2, out * 1.2, colour, 0.24 * left * left * (0.6 + 0.4 * note.loud));
        }
      }
      if (options.floor !== undefined) {
        pools.place(lx + spot.ox * 0.15, options.floor + 0.004, lz + spot.oz * 0.15, 1, 0, 0, 0, 0, 1, 0.34, 0.34, colour, 0.3 * amount * (0.6 + 0.4 * note.loud));
      }
      // A column of light rises from the key where it landed, quickly to its height, and stands while the note
      // sounds, dying away with it.
      const rise = 0.08 + 0.42 * (1 - Math.exp(-since * 4));
      blooms.add(lx, spot.y + rise / 2, lz, spot.width * 0.32, rise, colour, 0.5 * amount);
    }
    const landed = now - start;
    if (ripples !== undefined && options.floor !== undefined)
      ripples.add(lx, options.floor + 0.006, lz, ringRadius(landed), colour, ringStrength(landed, note.loud));
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
    if (spot === undefined || since < 0 || since > 2) return;
    const a = Math.atan2(spot.x, -spot.z);
    // Higher notes answer higher up, each key at its own distance, so the hall's light never lines up in a row.
    const up = (note.pitch - low) / Math.max(1, high - low);
    const far = reach * (0.8 + 0.4 * (((note.pitch * 7) % 11) / 10));
    // Each answer comes as the note sounds and is gone within about a second, so it is seen to answer that note.
    const strength = 0.3 * (0.4 + note.loud) * Math.exp(-since * 2.2) * Math.min(1, since / 0.05);
    const c = tinted(note);
    // Where its key points, and three more a third of a turn apart round the whole hall, their bearing a hash of the
    // note's key and the moment it sounds, so the hall is lit as brightly beside and behind as ahead, one note's
    // answers never step from the last note's by a steady angle (which reads as the hall turning), and its light
    // never falls into one pattern; heights vary with the note and the moment.
    echoes.add(Math.sin(a) * far, 0.6 + 0.4 * up, -Math.cos(a) * far, 2.2, 0.05, c, 0.6 * strength);
    const h = Math.sin(start * 12.9898 + note.pitch * 78.233) * 43758.5453;
    const b = (h - Math.floor(h)) * Math.PI * 2;
    for (let k = 0; k < 3; k++) {
      const bk = b + (k * Math.PI * 2) / 3;
      const g = Math.sin(start * 39.3467 + k * 11.135 + note.pitch * 3.7) * 24634.6345;
      const rise = 0.55 + 0.4 * up + 0.3 * (g - Math.floor(g));
      echoes.add(Math.sin(bk) * far, rise, -Math.cos(bk) * far, 2.2, 0.05, c, 1.1 * strength);
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
      pools.begin();
      ripples?.begin();
      player.each(RING_SECONDS, fall / speed, draw);
      // The bloom comes out of the keyboard: every key flares in its own colour, a column of light rises from each,
      // and one warm ring runs out across the floor from the keys.
      if (options.bloom !== undefined) {
        const peak = swell(now, options.bloom).bloom;
        if (peak > 0.01) {
          for (const spot of keys.spots) {
            const colour = colours.get(spot.midi) as Color;
            keys.light(spot.midi, colour, 0.95 * peak);
            const into = landing(spot);
            const tall = 0.4 + 2.6 * peak;
            blooms.add(spot.x + spot.ox * into, spot.y + tall / 2, spot.z + spot.oz * into, spot.width * 0.4, tall, colour, 0.45 * peak);
          }
          const since = now - options.bloom;
          if (ripples !== undefined && options.floor !== undefined && since < 3)
            ripples.add(0, options.floor + 0.008, 0, reachOut + since * 4, bloomColour, 0.9 * (1 - since / 3) ** 1.5);
        }
      }
      echoes.begin();
      if (options.echo !== false) player.each(2, 0, echo);
      echoes.end();
      keys.end();
      drops.end();
      heads.end();
      blooms.end();
      flares.end();
      pools.end();
      ripples?.end();
      motes.update(delta);
      previous = now;
    },
  };
}

/** How many straight pieces draw each of the piano hall's ribs. */
const RIB_PARTS = 14;

/** The bloom's burst at each column: the height, how many sparks, their size, how far they rise, how long they live. */
const BURST: readonly (readonly [number, number, number, number, number])[] = [
  [1.2, 16, 0.03, 1.6, 5],
  [3.0, 12, 0.05, 2.2, 6],
  [5.0, 8, 0.08, 2.8, 7],
  [7.5, 5, 0.13, 3.4, 8],
];

/** How many landings' rings are drawn at once at most. */
const RIPPLES = 8;

const RING_VERTEX = /* glsl */ `
  attribute vec4 ring;
  attribute vec3 hue;
  varying vec2 vAt;
  varying vec4 vRing;
  varying vec3 vHue;
  void main() {
    // ring: its middle's x and z, its square's half-width, its floor's y. A square lying on the floor, as wide as the
    // ring and its glow; vAt in metres from the ring's middle.
    vAt = position.xz * ring.z;
    vRing = ring;
    vHue = hue;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(ring.x + vAt.x, ring.w, ring.y + vAt.y, 1.0);
  }
`;

const RING_FRAGMENT = /* glsl */ `
  uniform float width;
  varying vec2 vAt;
  varying vec4 vRing;
  varying vec3 vHue;
  void main() {
    // A fine bright line on the ring with a softer glow either side of it: one even circle, never pieces.
    float d = abs(length(vAt) - (vRing.z - width * 12.0));
    // The glow ends well inside the square drawn for it, so no edge of the square ever shows.
    float a = exp(-(d * d) / (width * width)) + 0.35 * exp(-d / (width * 3.0));
    a *= 1.0 - smoothstep(width * 5.0, width * 11.0, d);
    if (a < 0.004) discard;
    gl_FragColor = vec4(vHue * a, 1.0);
    #include <colorspace_fragment>
  }
`;

/** Rings of light lying on a floor, at most `count` a frame, in one draw call: what `fallingNotes` sends from a landing. */
function floorRings(count: number) {
  const geometry = new PlaneGeometry(2, 2).rotateX(-Math.PI / 2);
  const ring = new InstancedBufferAttribute(new Float32Array(count * 4), 4);
  ring.setUsage(DynamicDrawUsage);
  const hue = new InstancedBufferAttribute(new Float32Array(count * 3), 3);
  hue.setUsage(DynamicDrawUsage);
  geometry.setAttribute('ring', ring);
  geometry.setAttribute('hue', hue);
  const width = 0.025;
  const material = new ShaderMaterial({
    uniforms: { width: { value: width } },
    vertexShader: RING_VERTEX,
    fragmentShader: RING_FRAGMENT,
    transparent: true,
    blending: AdditiveBlending,
    depthWrite: false,
    toneMapped: false,
  });
  const mesh = new InstancedMesh(geometry, material, count);
  mesh.frustumCulled = false;
  mesh.count = 0;
  const rings = ring.array as Float32Array;
  const hues = hue.array as Float32Array;
  let n = 0;
  return {
    mesh,
    begin() {
      n = 0;
    },
    /** A ring of `radius` metres round (x, y, z), lying level, in `colour` at `strength`. */
    add(x: number, y: number, z: number, radius: number, colour: Color, strength: number) {
      if (n >= count || strength <= 0) return;
      // Its square reaches past the ring by its glow.
      rings[n * 4] = x;
      rings[n * 4 + 1] = z;
      rings[n * 4 + 2] = radius + width * 12;
      rings[n * 4 + 3] = y;
      hues[n * 3] = colour.r * strength;
      hues[n * 3 + 1] = colour.g * strength;
      hues[n * 3 + 2] = colour.b * strength;
      n += 1;
    },
    end() {
      mesh.count = n;
      ring.needsUpdate = true;
      hue.needsUpdate = true;
    },
  };
}

/** A hall of light round the person, answering a score: what `lightHall` makes. */
export interface LightHall {
  /** The hall's light, round the group's origin, its floor at the group's own height 0: stand it where the person stands. */
  readonly group: Group;
  /** Draws a frame from the player's time; pass it to `ambientMotion`. */
  move(time: number): void;
}

/** The hall's colours round the person: deep blue ahead, sea blue and violet to the sides, rose behind. */
const AROUND = [new Color('#2F45D8'), new Color('#2EA7D0'), new Color('#7A6EF0'), new Color('#E06AB8')];

/**
 * Makes a hall of light round the person that answers a score (`playScore`) and builds with it: `columns` columns of
 * light stand round the person about `radius` metres out, each at its own distance and width, rising from the floor and
 * leaning in to meet in a warm crown of light just beyond the ring in front of the person, about 18 degrees above
 * their eye, so the keys and the crown are seen together with the head level, each faintly its own colour (deep blue ahead, sea blue and violet
 * beside, rose behind). Each note's light falls down one column, climbs back up it, and climbs, fainter, the column
 * facing it too (each octave runs once round the ring, C ahead and rising to the right, so a scale stands on every side
 * and a run circles the person); when it reaches the top, it lights a point of a slowly turning galaxy overhead, which
 * grows from its middle outward as the music goes on. Over the first `rise` seconds of the music the columns and the
 * galaxy grow brighter; for its last three seconds the hall dims, holding its breath, and at `rise` it blooms: every
 * column flares and sheds sparks along its height, a wave runs up them, the whole galaxy lights at once and a ring of
 * light runs out across the floor, and (unless `sound` is false) a soft pad swells into it under the music and bells
 * run up the chord sounding then; then it settles to a steady glow and goes on answering. Two bands of warm amber light
 * circle the columns, right to left, once every fourteen seconds of the music, coming in as the hall builds. Read from
 * the player's time, so it lands with the sound. Add `group` where the person stands, its height 0 on the floor.
 */
export function lightHall(
  player: Player,
  options: {
    radius?: number;
    height?: number;
    columns?: number;
    rise?: number;
    colour?: (midi: number) => Color;
    quality?: 'auto' | 'high' | 'low';
    sound?: boolean;
  } = {},
): LightHall {
  const radius = options.radius ?? 6.5;
  const height = options.height ?? 10;
  const count = options.columns ?? 24;
  const rise = options.rise ?? 20;
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const note of player.score.notes) {
    low = Math.min(low, note.pitch);
    high = Math.max(high, note.pitch);
  }
  // Each column at its own distance, height and width (a fixed hash of its place), so the ring is a hall of light
  // with depth, never an even curtain.
  const own = (i: number, seed: number) => {
    const h = Math.sin(i * 12.9898 + seed * 78.233) * 43758.5453;
    return h - Math.floor(h);
  };
  // Where the columns meet: a crown of light in front of the person, just beyond the ring, about 18 degrees up, so the
  // head level holds the keys and the crown's heart in one view and the bloom is seen to open from the music.
  const crownZ = -radius * 1.25;
  const apex = 1.6 - crownZ * Math.tan((18 * Math.PI) / 180);
  // A point at height h on a column's line from its foot at (x, z) to the crown.
  const lineX = (x: number, h: number) => x * (1 - h / apex);
  const lineZ = (z: number, h: number) => z + (crownZ - z) * (h / apex);
  const homes = Array.from({ length: count }, (_, i) => {
    const a = (i / count) * Math.PI * 2;
    // 0 ahead, 1 behind, either way round, changing evenly with the bearing.
    const t = Math.min(a, Math.PI * 2 - a) / Math.PI;
    const at = t * (AROUND.length - 1);
    const k = Math.min(AROUND.length - 2, Math.floor(at));
    const near = own(i, 1);
    const out = radius * (0.78 + 0.55 * near);
    const x = Math.sin(a) * out;
    const z = -Math.cos(a) * out;
    const reach = Math.hypot(x, apex, crownZ - z);
    return {
      a,
      x,
      z,
      // The way up the column: leaning in from its foot toward the crown over the person.
      dx: -x / reach,
      dy: apex / reach,
      dz: (crownZ - z) / reach,
      // Nearer columns brighter, farther ones fainter in the haze.
      bright: 1.25 - 0.6 * near,
      wide: (0.08 + 0.12 * own(i, 3)) * (1.3 - 0.5 * near),
      colour: (AROUND[k] as Color).clone().lerp(AROUND[k + 1] as Color, at - k),
    };
  });
  // How bright the light circling the hall makes a column at bearing `a` this frame: a band of light that runs once
  // round the person every fourteen seconds of the music, right to left, so whichever way they turn, light is moving
  // round them.
  let sweepAt = 0;
  let sweepGain = 0;
  // Two bands, opposite each other, so one is always somewhere ahead of a person turning.
  const sweeping = (a: number) => {
    const d = Math.atan2(Math.sin(a - sweepAt), Math.cos(a - sweepAt));
    const e = Math.atan2(Math.sin(a - sweepAt - Math.PI), Math.cos(a - sweepAt - Math.PI));
    return sweepGain * (Math.exp(-((d / 0.45) ** 2)) + Math.exp(-((e / 0.45) ** 2)));
  };
  // The circling light is warm, dawn amber among the blues, so it is seen to travel.
  const amber = new Color('#FF9640');
  // The light overhead: a slowly turning galaxy of fine points over the person, three arms winding out from its
  // middle, its middle a little higher than its rim; warm at its heart, violet along its arms, deep blue at its rim.
  // Points of many sizes and brightnesses, nearest the middle first, so it can grow outward.
  const petals = 1400;
  const crown = height * 0.6;
  const span = radius * 0.95;
  // How far the galaxy's rim dips toward the floor: it comes down round the person like a dome, its rim in view at eye
  // height.
  const dip = 2.6;
  let seed = 7;
  const random = () => {
    seed = (seed * 16807) % 2147483647;
    return seed / 2147483647;
  };
  const heart = new Color('#FFC8E6');
  const galaxy = Array.from({ length: petals }, (_, k) => {
    const u = random() ** 0.75;
    const r = 0.15 + u * span;
    const arm = (k % 3) * ((Math.PI * 2) / 3);
    // Scattered about each arm, more widely near the middle, and a few between the arms.
    const off = (random() + random() + random() - 1.5) * (random() < 0.15 ? 1.6 : 0.45);
    const a = arm + 1.9 * Math.log(1 + r) + off;
    const t = Math.min(1, r / span);
    const colour = t < 0.5 ? heart.clone().lerp(AROUND[2] as Color, t * 2) : (AROUND[2] as Color).clone().lerp(AROUND[0] as Color, (t - 0.5) * 2);
    const size = 0.005 + 0.028 * random() ** 4;
    const own = 0.35 + 0.65 * random() ** 2;
    return { r, a, y: crown - dip * t ** 2 + (random() - 0.5) * 1.2, colour, size, own, twinkle: 0.6 + random() * 2.2, phase: random() * 6.28 };
  }).sort((p, q) => p.r - q.r);
  // The arms' own soft light: wide faint glows along each arm's middle.
  const veils = Array.from({ length: 96 }, (_, k) => {
    const r = 0.3 + ((k / 3) / 32) * span;
    const a = (k % 3) * ((Math.PI * 2) / 3) + 1.9 * Math.log(1 + r);
    const t = Math.min(1, r / span);
    return { r, a, y: crown - dip * t ** 2, colour: (galaxy[Math.min(petals - 1, Math.floor(t * t * petals))] as (typeof galaxy)[number]).colour };
  });
  const group = new Group();
  // The columns: soft shafts of light, a faint bright thread down each middle, fading to nothing at the top, so each
  // rises out of the floor into the dark.
  // All of it is also seen in the polished floor (`darkHall`'s), so each column runs on down into its reflection.
  const polish = { level: 0, strength: 0.55, narrow: 1 };
  // Beyond them a second ring, twice as far, of fainter and finer columns, so the hall has depth all round.
  const outer = Math.round(count * 1.5);
  const beyond = Array.from({ length: outer }, (_, i) => {
    const a = ((i + 0.5) / outer) * Math.PI * 2;
    const n = Math.round((i / outer) * count) % count;
    return { a, x: Math.sin(a) * radius * 2.3, z: -Math.cos(a) * radius * 2.3, colour: (homes[n] as (typeof homes)[number]).colour };
  });
  // Each runs on through the polished floor as its own reflection, one light with no join, dimmer below the floor
  // and fading out under it.
  const pillars = glows(count + outer, { upright: true, halo: 0.55, round: 0.05, core: 0.18, whiten: 0.3, fade: [0.18, 0.3], under: { level: 0, strength: 0.4 } });
  // A note's light climbing its column: a long soft glow, brightest at its head, its tail fading to nothing, so notes
  // following each other up a column run together as one light, never as stacked bars.
  const climbs = glows(200, { upright: true, halo: 0.18, round: 0.02, core: 0.6, whiten: 0.6, ramp: 0, fade: 0.12, mirror: polish });
  // Ribs of light: from each column's foot a fine bright line arches up and in to the crown, so the hall has a shape,
  // a vault over the person; each note lights its rib, and the bloom runs up all of them from the floor to the crown.
  const ribs = glows(count * RIB_PARTS, { upright: true, halo: 0.06, round: 0.01, core: 0.7, whiten: 0.5 });
  // A point `t` (0 at the foot, 1 at the crown) along rib `i`: rising steeply from the floor, bowing out a little, and
  // curving in to the crown.
  const ribAt = (i: number, t: number, into: [number, number, number]) => {
    const home = homes[i] as (typeof homes)[number];
    const u = 1 - t;
    const cx = home.x * 1.08;
    const cy = apex * 0.95;
    const cz = home.z * 1.08 + (crownZ - home.z) * 0.15;
    into[0] = u * u * home.x + 2 * u * t * cx + t * t * 0;
    into[1] = 2 * u * t * cy + t * t * apex;
    into[2] = u * u * home.z + 2 * u * t * cz + t * t * crownZ;
  };
  const ribFrom: [number, number, number] = [0, 0, 0];
  const ribTo: [number, number, number] = [0, 0, 0];
  // A note's light falling down its column: brightest at its foot, which meets its own reflection where it lands.
  const falls = glows(48, { upright: true, halo: 0.18, round: 0.02, core: 0.6, whiten: 0.6, ramp: 0, mirror: polish });
  // The bloom's light overhead: one wide soft glow over the person, under the galaxy.
  const crownLight = glows(1, { facing: true, halo: 4, round: 1, core: 0 });
  // The crown itself: a small, hot, warm eye of light where every column meets, the place a look up arrives at.
  const oculus = glows(2, { facing: true, halo: 1.2, round: 1, core: 0.6, whiten: 1 });
  // The galaxy's points, the floor's light at each column's foot, and the bloom's ring running out across the floor.
  const points = glows(petals, { facing: true, halo: 0.025, round: 1, core: 0.9, whiten: 0.5, mirror: { level: 0, strength: 0.22, narrow: 1 } });
  const arms = glows(veils.length, { facing: true, halo: 0.7, round: 1, core: 0 });
  const floor = glows(count + 96 * 3 + 20 * 24, { halo: 0.5, round: 1, core: 0 });
  // Light drifting near the person, 1.5 m to 5 m away all round, so the hall has something close as well as far.
  const nearby = Array.from({ length: 140 }, () => {
    const a = random() * Math.PI * 2;
    const r = 1.6 + random() * 3.4;
    return { x: Math.sin(a) * r, y: 0.4 + random() * 3.2, z: -Math.cos(a) * r, phase: random() * 6.28, size: 0.006 + 0.012 * random() ** 2, n: Math.floor((a / (Math.PI * 2)) * count) % count };
  });
  const near = glows(nearby.length, { facing: true, halo: 0.03, round: 1, core: 0.8, whiten: 0.4 });
  // Sparks rising from a column's foot as each note starts up it.
  const embers = sparks(1400);
  group.add(floor.object, pillars.object, ribs.object, climbs.object, falls.object, arms.object, points.object, crownLight.object, oculus.object, embers.object, near.object);
  const climb = 4;
  const top = height / climb;
  const shade = new Color();
  const tinted = (note: { pitch: number; loud: number }) =>
    options.colour?.(note.pitch) ?? registerColour(note.pitch + (note.loud - 0.45) * 0.6 * (high - low), low, high, shade);
  // How lit each column and each point of the galaxy is this frame by the notes, summed over the notes.
  const lit = new Float32Array(count);
  const flash = new Float32Array(petals);
  const flashColour = Array.from({ length: petals }, () => new Color());
  const litColour = Array.from({ length: count }, () => new Color());
  const newest = new Float32Array(count);
  const newestLoud = new Float32Array(count);
  let built = 0;
  let now = 0;
  let rings = 0;
  let burst = false;
  // The keys sounding now, for the peak's chord; filled from the player without making anything per frame.
  const chord = new Set<number>();
  let swelled = false;
  let rang = false;
  let prepared = false;
  const peakSound = (since: number) => {
    const music = pageMusic();
    if (music === null) return;
    // Well before it, the pad's and the bells' keys are made, a few across the range: any other is played retuned
    // from the nearest one made.
    if (!prepared && since >= -14) {
      prepared = true;
      void music.voices.pad.prepare([36, 40, 43, 48, 52, 55, 60, 64, 67]).catch(() => {});
      void music.voices.bell.prepare([72, 76, 79, 84, 88, 91, 96]).catch(() => {});
    }
    if (!swelled && since >= -5 && since < -4) {
      chord.clear();
      player.each(0.6, 0.6, (note) => {
        if (chord.size < 5) chord.add(note.pitch);
      });
      if (chord.size === 0) return;
      swelled = true;
      const at = music.context.currentTime + 0.05;
      // Under the music, soft at first: the pad's own slow rise carries it to the bloom and past it.
      for (const k of chord) music.voices.pad.play(k - 12, at, 9 - (since + 5), 0.32);
    }
    if (!rang && since >= -0.25 && since < 0.5) {
      rang = true;
      const at = music.context.currentTime + Math.max(0, -since);
      let i = 0;
      for (const step of [12, 24])
        for (const k of chord) {
          music.voices.bell.play(k + step, at + i * 0.07, 2.5, 0.42 - i * 0.02);
          i += 1;
        }
    }
  };
  let drops = 0;
  const falling = 1.1;
  let waves = 0;
  let wave = 0;
  let previous = Number.POSITIVE_INFINITY;
  let lastTime = Number.NaN;
  // Under 'auto', a stretch of slow frames lowers the detail a step, at most once every three seconds: first every
  // other point of the galaxy, then its arms' soft light, the far ring of columns and what the floor mirrors too.
  const quality = options.quality ?? 'auto';
  let detail = quality === 'low' ? 0 : 1;
  let slow = 0;
  let lastFrame = Number.NaN;
  let lastStep = Number.NEGATIVE_INFINITY;
  // The last step: nothing mirrored in the floor.
  const unmirror = () => {
    for (const set of [pillars, climbs, falls, points])
      for (const child of set.object.children) if (child !== set.mesh) child.visible = false;
  };
  if (detail === 0) unmirror();
  const visit = (note: { pitch: number; loud: number }, start: number) => {
    const since = now - start;
    const c = tinted(note);
    const i = roundColumn(note.pitch, count);
    const home = homes[i] as (typeof homes)[number];
    const loud = 0.45 + note.loud;
    // Before it sounds, the note's light falls down its column out of the dark, landing on the floor at its foot as
    // it is heard; it splashes there and climbs back up.
    if (since < 0) {
      if (since > -falling && drops < 40) {
        const y = -since * (height * 0.8) / falling;
        const seen = Math.min(1, (falling + since) / 0.4);
        const h = y + 0.4;
        falls.place(lineX(home.x, h), h, lineZ(home.z, h), 1, 0, 0, home.dx, home.dy, home.dz, 0.03, 0.8 / home.dy, c, 0.7 * loud * seen * (0.5 + 0.5 * built));
        drops += 1;
      }
      return;
    }
    // Where it starts up, a ring of its light runs out across the floor round the column's foot.
    if (since < 1.4 && rings < 20) {
      const r = 0.2 + since * 1.8;
      const fade = (1 - since / 1.4) ** 2 * 0.9 * loud * (0.5 + 0.5 * built);
      for (let k = 0; k < 24; k++) {
        const a = (k / 24) * Math.PI * 2;
        const s = Math.sin(a);
        const o = Math.cos(a);
        floor.place(home.x + s * r, 0.008, home.z + o * r, o, 0, -s, s, 0, o, (2 * Math.PI * r) / 20, 0.2, c, fade);
      }
      rings += 1;
    }
    // A low note sends a ring of its light out across the floor from the keyboard, lighting each column as it passes.
    if (note.pitch < low + (high - low) / 3 && since < 2.6 && waves < 3) {
      const r = 1.2 + since * 3.2;
      const fade = (1 - since / 2.6) ** 1.5 * 0.5 * loud * (0.4 + 0.6 * built);
      for (let k = 0; k < 96; k++) {
        const a = (k / 96) * Math.PI * 2;
        const sa = Math.sin(a);
        const ca = -Math.cos(a);
        floor.place(sa * r, 0.01, ca * r, -ca, 0, sa, sa, 0, ca, (2 * Math.PI * r) / 80, 0.25, c, 0.6 * fade);
      }
      const passing = Math.exp(-(((r - radius) / 0.8) ** 2)) * fade * 2;
      wave = Math.max(wave, passing);
      waves += 1;
    }
    if (start > previous && start <= now)
      embers.emit(home.x, 0.05, home.z, c, 8 + Math.round(8 * note.loud), { rise: 0.8, spread: 0.6, life: 2.2, size: 0.022 });
    if (since < top) {
      const fade = Math.exp(-since * 0.35);
      // Its column, and fainter the one facing it across the hall, so every side answers whatever key the music is in.
      for (let side = 0; side < 2; side++) {
        const j = side === 0 ? i : (i + count / 2) % count;
        const share = side === 0 ? 1 : 0.55;
        lit[j] = (lit[j] ?? 0) + 0.5 * loud * fade * share;
        // Only the newest note climbing a column is drawn, so lights following each other never pile up.
        if (since < (newest[j] as number)) {
          newest[j] = since;
          newestLoud[j] = loud * share;
          (litColour[j] as Color).copy(c);
        }
      }
    }
    // At the top it lights a point of the galaxy, which glows and fades over some seconds; while the hall is still
    // building, only the galaxy's middle is grown, so it fills outward with the music.
    const after = since - top;
    if (after >= 0 && after < 9) {
      const h = Math.sin(start * 12.9898 + note.pitch * 78.233) * 43758.5453;
      const reach = Math.max(13, Math.floor(petals * (0.15 + 0.85 * built)));
      const k = Math.floor((h - Math.floor(h)) * reach);
      const glow = 0.9 * loud * Math.min(1, after / 0.08) * Math.exp(-after * 0.45);
      if (glow > (flash[k] ?? 0)) (flashColour[k] as Color).copy(c);
      flash[k] = Math.max(flash[k] ?? 0, glow);
    }
  };
  const ring = new Color();
  return {
    group,
    move(time) {
      now = player.now();
      if (quality === 'auto' && detail > 0) {
        const dt = time - lastFrame;
        if (dt > 0 && dt < 1) slow = slow * 0.95 + (dt > 1 / 50 ? 0.05 : 0);
        if (slow > 0.6 && time - lastStep > 3) {
          detail = Math.max(0, detail - 0.5);
          lastStep = time;
          slow = 0;
          if (detail === 0) unmirror();
        }
      }
      lastFrame = time;
      // How far the hall has built, 0 before the music to 1 at its bloom; and the bloom itself, sudden and long fading.
      const swelling = swell(now, rise);
      built = swelling.built;
      const since = now - rise;
      // The bloom heard: a pad swelling under the music for its last seconds, and bells running up at the moment.
      if (options.sound !== false) peakSound(since);
      // The bloom seen all round: every column sheds a burst of sparks all along its height at once, many fine ones low
      // and fewer, larger, slower ones higher up, so the burst has depth and never reads as even confetti.
      if (since >= 0 && !burst) {
        burst = true;
        for (const home of homes)
          BURST.forEach(([y, count, size, rise, life]) =>
            embers.emit(lineX(home.x, y), y, lineZ(home.z, y), home.colour, count, { rise, spread: 1.4, life, size }),
          );
      }
      if (since < 0) burst = false;
      const bloom = swelling.bloom;
      // Held just before the peak: the hall dims, so the bloom lands against it.
      const held = 1 - 0.93 * swelling.hush;
      // After the bloom the hall keeps a steady glow a little under its peak.
      const rest = 0.05 + 0.3 * built - (since > 0 ? 0.08 * Math.min(1, since / 6) : 0);
      lit.fill(0);
      flash.fill(0);
      newest.fill(Number.POSITIVE_INFINITY);
      rings = 0;
      drops = 0;
      waves = 0;
      wave = 0;
      floor.begin();
      climbs.begin();
      falls.begin();
      player.each(top + 9, falling, visit);
      // The circling light comes in as the hall builds, and runs on at its full after the bloom.
      sweepAt = -(Math.max(0, now) / 14) * Math.PI * 2;
      // Faint while the hall builds, so the warm light comes with the bloom and circles on after it.
      sweepGain = since >= 0 ? 1.1 : 0.2 * built;
      pillars.begin();
      for (let i = 0; i < count; i++) {
        const home = homes[i] as (typeof homes)[number];
        const notes = Math.min(1, lit[i] ?? 0);
        const swept = sweeping(home.a);
        shade.copy(home.colour).lerp(litColour[i] as Color, notes > 0 ? 0.6 : 0).lerp(amber, Math.min(0.95, swept));
        // The bloom brightens the columns well short of white, so their colour holds at the peak.
        const strength = held * (rest + 1.1 * notes + swept) + 0.45 * bloom + wave;
        const going = newest[i] as number;
        if (going < top) {
          // Climbing, its head at y and its tail below, fading as it goes.
          const y = going * climb;
          const tail = Math.min(y, 3);
          const fade = Math.exp(-going * 0.35);
          const h = Math.min(apex - 0.3, y - tail / 2);
          climbs.place(lineX(home.x, h), h, lineZ(home.z, h), 1, 0, 0, -home.dx, -home.dy, -home.dz, 0.05, tail / home.dy, litColour[i] as Color, 1.5 * (newestLoud[i] as number) * fade * (0.5 + 0.5 * built));
        }
        // From under the floor (its reflection) up to the crown, along its lean.
        const foot = -3;
        const crest = apex - 0.4;
        const mid = (foot + crest) / 2;
        pillars.place(lineX(home.x, mid), mid, lineZ(home.z, mid), 1, 0, 0, home.dx, home.dy, home.dz, home.wide, (crest - foot) / home.dy, shade, 1.4 * home.bright * strength);
        // The bloom runs up the columns from their feet into the light overhead, the ones ahead first and round to
        // those behind within half a second, so the hall is seen lighting up in turn round the person.
        const after = since - 0.5 * (Math.min(home.a, Math.PI * 2 - home.a) / Math.PI);
        if (after >= 0 && after < 2.5) {
          const y = after * 5;
          const tail = Math.min(y, 7);
          const h = Math.min(apex - 0.3, y - tail / 2);
          climbs.place(lineX(home.x, h), h, lineZ(home.z, h), 1, 0, 0, -home.dx, -home.dy, -home.dz, 0.14, tail / home.dy, home.colour, 0.9 * (1 - after / 2.5));
        }
      }
      for (const far of detail > 0 ? beyond : []) {
        const tall = height * 2.4;
        // Far off, fainter and lost in the haze, so the ring has a near and a far.
        pillars.add(far.x, height * 0.8, far.z, 0.08, tall, far.colour, held * (0.55 * rest + 0.35 * sweeping(far.a)) + 1.2 * bloom);
      }
      // The bloom's ring runs out across the floor from the person's feet past the columns.
      if (since >= 0 && since < 3) {
        const r = 0.8 + since * 4;
        const fade = (1 - since / 3) ** 2;
        for (let k = 0; k < 96; k++) {
          const a = (k / 96) * Math.PI * 2;
          const s = Math.sin(a);
          const c = -Math.cos(a);
          const n = Math.round(((a / (Math.PI * 2)) * count) % count) % count;
          ring.copy((homes[n] as (typeof homes)[number]).colour);
          floor.place(s * r, 0.012, c * r, -c, 0, s, s, 0, c, (2 * Math.PI * r) / 60, 0.5, ring, 0.6 * fade);
        }
      }
      pillars.end();
      // The ribs: faint while the hall builds, lit by their notes, and at the bloom lit from the floor up to the
      // crown by a front that climbs them in about a second and a half, ahead first and round to behind.
      ribs.begin();
      for (let i = 0; i < count; i++) {
        const home = homes[i] as (typeof homes)[number];
        const notes = Math.min(1, lit[i] ?? 0);
        const turn = since - 0.5 * (Math.min(home.a, Math.PI * 2 - home.a) / Math.PI);
        const front = turn < 0 ? -1 : Math.min(1.2, turn / 1.5);
        for (let k = 0; k < RIB_PARTS; k++) {
          const t0 = k / RIB_PARTS;
          const t1 = (k + 1) / RIB_PARTS;
          ribAt(i, t0, ribFrom);
          ribAt(i, t1, ribTo);
          const dx = ribTo[0] - ribFrom[0];
          const dy = ribTo[1] - ribFrom[1];
          const dz = ribTo[2] - ribFrom[2];
          const length = Math.hypot(dx, dy, dz);
          const mid = (t0 + t1) / 2;
          // The bloom's front: bright as it passes, then a steady glow behind it.
          const passed = front >= mid ? 0.5 * bloom + 0.9 * Math.exp(-(((front - mid) / 0.12) ** 2)) * Math.min(1, bloom * 2) : 0;
          const strength = held * (0.06 + 0.12 * built + 0.5 * notes) + passed;
          if (strength <= 0.003) continue;
          ribs.place(
            (ribFrom[0] + ribTo[0]) / 2,
            (ribFrom[1] + ribTo[1]) / 2,
            (ribFrom[2] + ribTo[2]) / 2,
            1,
            0,
            0,
            dx / length,
            dy / length,
            dz / length,
            0.03,
            length * 1.04,
            home.colour,
            strength * home.bright,
          );
        }
      }
      ribs.end();
      climbs.end();
      falls.end();
      near.begin();
      for (const m of nearby) {
        const drift = time * 0.05 + m.phase;
        const c = (homes[m.n] as (typeof homes)[number]).colour;
        near.add(m.x + Math.sin(drift) * 0.3, m.y + Math.sin(drift * 0.7) * 0.2, m.z + Math.cos(drift * 0.8) * 0.3, m.size, m.size, c, (0.25 + 0.5 * built + 1.2 * bloom) * (0.6 + 0.4 * Math.sin(time * 0.9 + m.phase * 3)));
      }
      near.end();
      embers.update(Number.isNaN(lastTime) ? 0 : time - lastTime);
      lastTime = time;
      previous = now;
      floor.end();
      points.begin();
      const turn = now * 0.015;
      const grown = petals * (0.12 + 0.88 * built);
      const every = detail < 1 ? 2 : 1;
      for (let k = 0; k < petals; k += every) {
        const p = galaxy[k] as (typeof galaxy)[number];
        // Points beyond the grown middle wait in the dark, each coming in softly as the galaxy reaches it.
        const shown = Math.min(1, Math.max(0, grown - k) / 40);
        const lit = flash[k] ?? 0;
        const glint = 0.7 + 0.3 * Math.sin(time * p.twinkle + p.phase);
        const strength = p.own * glint * ((1 - 0.85 * swelling.hush) * shown * (0.3 + 0.5 * built) + 0.6 * bloom) + lit;
        if (strength <= 0.002) continue;
        const a = p.a + turn;
        shade.copy(p.colour);
        if (lit > 0) shade.lerp(flashColour[k] as Color, Math.min(1, lit * 2));
        const size = p.size * (1 + 1.5 * Math.min(1, lit) + 0.3 * bloom);
        points.add(Math.sin(a) * p.r, p.y, -Math.cos(a) * p.r, size, size, shade, strength);
      }
      points.end();
      arms.begin();
      for (const v of detail > 0 ? veils : []) {
        const shown = Math.min(1, Math.max(0, grown / petals - v.r / span + 0.1) * 5);
        const a = v.a + turn;
        arms.add(Math.sin(a) * v.r, v.y, -Math.cos(a) * v.r, 0.2, 0.2, v.colour, shown * (0.015 + 0.03 * built) + 0.03 * bloom);
      }
      arms.end();
      crownLight.begin();
      ring.copy(AROUND[2] as Color).lerp(AROUND[3] as Color, 0.4);
      crownLight.add(0, crown + 0.5, 0, 1, 1, ring, 0.04 * built + 0.25 * bloom);
      crownLight.end();
      oculus.begin();
      // The warm eye: a hot white heart in a broad warm halo, so it reads as the light itself, never a bead or a
      // dull ball; at the peak the heart is the brightest thing in view.
      oculus.add(0, apex, crownZ, 0.5, 0.5, amber, (1 - 0.7 * swelling.hush) * 0.35 * built + 0.8 * bloom);
      oculus.add(0, apex, crownZ, 3.2, 3.2, amber, (1 - 0.9 * swelling.hush) * 0.05 * built + 0.25 * bloom);
      oculus.end();
    },
  };
}
