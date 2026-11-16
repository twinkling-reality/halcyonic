import {
  AdditiveBlending,
  BackSide,
  BufferGeometry,
  CircleGeometry,
  Color,
  CylinderGeometry,
  DataTexture,
  Float32BufferAttribute,
  Group,
  LinearFilter,
  LinearMipmapLinearFilter,
  Mesh,
  MeshBasicMaterial,
  RepeatWrapping,
  RGBAFormat,
  ShaderMaterial,
  SphereGeometry,
  SRGBColorSpace,
  Vector3,
} from 'three';
import { glows } from './glow.js';
import { landShape } from './memory.js';

/**
 * Places, not islands: dark or dim surroundings for an app of music and light, built in the group `setupScene` returns
 * (`src/look.ts`), where light from `src/glow.ts` shows best. Each returns its group and a `move(time)` for its own slow
 * life; pass that to `ambientMotion`.
 */
export interface Place {
  readonly group: Group;
  /** The colour at the place's horizon, for anything that should fade into the distance (a lane, a far shore). */
  readonly horizon: Color;
  move(time: number): void;
}

/**
 * A sky as a sphere round the person, from `top` overhead through `horizon` to `below` under the floor; with
 * `middle`, it passes through that colour a third of the way up (a sunset's rose).
 */
function sky(top: Color, horizon: Color, below: Color, radius: number, middle?: Color, rows = 24): Mesh {
  const geometry = new SphereGeometry(radius, rows * 2, rows);
  const position = geometry.getAttribute('position');
  const colours: number[] = [];
  const c = new Color();
  for (let i = 0; i < position.count; i++) {
    const up = position.getY(i) / radius;
    if (up >= 0 && middle !== undefined) {
      const k = Math.min(1, up ** 0.5);
      if (k < 0.45) c.copy(horizon).lerp(middle, k / 0.45);
      else c.copy(middle).lerp(top, (k - 0.45) / 0.55);
    } else if (up >= 0) c.copy(horizon).lerp(top, Math.min(1, up ** 0.4));
    else c.copy(horizon).lerp(below, Math.min(1, (-up) ** 0.5));
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  const mesh = new Mesh(
    geometry,
    new MeshBasicMaterial({ vertexColors: true, side: BackSide, depthWrite: false, fog: false, toneMapped: false, dithering: true }),
  );
  // After setupScene's flat backdrop (-1), whichever was made first, and before a lake's water (-0.9).
  mesh.renderOrder = -0.95;
  return mesh;
}

/**
 * A floor disc `radius` metres round the person, `centre` under them fading to `edge`, with `mist` added toward its
 * far part (from a quarter of the way out, full from `misted` metres to its edge), so haze over the horizon carries on
 * down it; give the sky's horizon the same mist, so the two meet in one colour.
 */
function floor(centre: Color, edge: Color, radius: number, mist?: Color, misted = radius): Mesh {
  const geometry = new CircleGeometry(radius, 64, 0, Math.PI * 2);
  geometry.rotateX(-Math.PI / 2);
  const position = geometry.getAttribute('position');
  const colours: number[] = [];
  const c = new Color();
  for (let i = 0; i < position.count; i++) {
    const out = Math.hypot(position.getX(i), position.getZ(i)) / radius;
    c.copy(centre).lerp(edge, Math.min(1, out ** 0.5));
    if (mist !== undefined) {
      const k = Math.min(1, Math.max(0, (out * radius - radius * 0.25) / (misted - radius * 0.25)));
      const m = k * k * (3 - 2 * k);
      c.setRGB(c.r + mist.r * m, c.g + mist.g * m, c.b + mist.b * m);
    }
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  return new Mesh(geometry, new MeshBasicMaterial({ vertexColors: true, toneMapped: false, dithering: true }));
}

/** A fixed sequence, so a place is the same on every load. */
function seeded(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state * 16807) % 2147483647;
    return state / 2147483647;
  };
}

/**
 * A dark hall: a floor of deep blue-black glass round the person, a night that has no edge, and a few motes of dust
 * drifting in the air, catching no light but their own. For a concert of light: every light in it is the app's own.
 * `tint` colours the dark a little (default a deep blue).
 */
export function darkHall(options: { tint?: Color; motes?: number } = {}): Place {
  const tint = options.tint ?? new Color('#1A2448');
  const group = new Group();
  const black = new Color('#010103');
  // The floor fades into the horizon's colour and the sky rises from it, so no line marks where they meet.
  const horizon = tint.clone().multiplyScalar(0.16);
  const mist = tint.clone().multiplyScalar(0.05);
  // The sky's horizon carries the floor's mist, so where the floor ends the sky begins in the same colour.
  const misted = horizon.clone().add(mist);
  group.add(sky(black, misted, misted, 120));
  // The haze ring (below) stands 44 m out; the floor carries its low glow on toward the person, with no seam.
  group.add(floor(tint.clone().multiplyScalar(0.1), horizon, 60, mist, 44));
  const count = options.motes ?? 160;
  const motes = glows(count, { facing: true, halo: 0.01, round: 1 });
  group.add(motes.object);
  const random = seeded(4242);
  const home = Array.from({ length: count }, () => [
    (random() - 0.5) * 16,
    0.3 + random() * 4.5,
    -0.5 - random() * 14,
    random() * 6.28,
    0.003 + random() * 0.005,
  ]);
  const dust = new Color('#8FA6FF');
  // Haze far off, low over the floor, so the dark has depth and is not a wall: one soft ring of the tint all round,
  // rising softly out of the floor with no edge, brightest three metres up and fading to nothing above, with no crease
  // anywhere in it (a single smooth curve, never two joined at their peak).
  const ring = new CylinderGeometry(44, 44, 12, 96, 24, true);
  ring.translate(0, 6, 0);
  const at = ring.getAttribute('position');
  const glow: number[] = [];
  for (let i = 0; i < at.count; i++) {
    const y = at.getY(i);
    const t = Math.min(1, Math.max(0, y / 12));
    const k = ((t * (1 - t) ** 3) / 0.1055) * 0.16;
    glow.push(tint.r * k, tint.g * k, tint.b * k);
  }
  ring.setAttribute('color', new Float32BufferAttribute(glow, 3));
  const haze = new Mesh(
    ring,
    new MeshBasicMaterial({
      vertexColors: true,
      side: BackSide,
      transparent: true,
      blending: AdditiveBlending,
      depthWrite: false,
      toneMapped: false,
      dithering: true,
    }),
  );
  group.add(haze);
  return {
    group,
    horizon,
    move(time) {
      motes.begin();
      for (const [x, y, z, phase, size] of home as [number, number, number, number, number][]) {
        const drift = time * 0.03 + phase;
        motes.add(
          x + Math.sin(drift) * 0.4,
          y + Math.sin(drift * 0.7) * 0.25,
          z + Math.cos(drift * 0.8) * 0.4,
          size,
          size,
          dust,
          0.18 + 0.12 * Math.sin(time * 0.5 + phase * 3),
        );
      }
      motes.end();
    },
  };
}

const SKY_VERTEX = /* glsl */ `
  varying vec3 vDir;
  void main() {
    vDir = normalize((modelMatrix * vec4(position, 0.0)).xyz);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`;

const SKY_FRAGMENT = /* glsl */ `
  uniform vec3 zenith;
  uniform vec3 middle;
  uniform vec3 horizonColour;
  uniform vec3 below;
  uniform vec3 sunColour;
  uniform vec3 sunDir;
  uniform float time;
  uniform float cloud;
  uniform float bloom;
  varying vec3 vDir;
  float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
  float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
  }
  float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) {
      v += a * noise(p);
      p = p * 2.03 + vec2(1.7, 9.2);
      a *= 0.5;
    }
    return v;
  }
  void main() {
    vec3 d = normalize(vDir);
    float up = d.y;
    float h = max(up, 0.0);
    // The sky's own colours by height: the horizon's glow, the middle, the deep overhead.
    float k = pow(h, 0.5);
    vec3 colour = k < 0.45 ? mix(horizonColour, middle, k / 0.45) : mix(middle, zenith, (k - 0.45) / 0.55);
    if (up < 0.0) colour = mix(horizonColour, below, clamp(-up * 14.0, 0.0, 1.0));
    float mu = dot(d, sunDir);
    float toward = max(mu, 0.0);
    // The air thickest along the horizon, lit warm on the sun's side (the light scattered in the low air).
    float low = exp(-h * 8.0);
    colour += sunColour * (0.06 + 0.9 * toward * toward) * low * (1.0 + 0.8 * bloom);
    // Opposite the sun, low: the pink band of the last light over the blue of the earth's own shadow.
    float away = max(-mu, 0.0);
    vec3 pink = mix(sunColour, vec3(0.9, 0.45, 0.6), 0.6);
    colour = mix(colour, pink * 0.85 + middle * 0.2, away * smoothstep(0.03, 0.09, h) * (1.0 - smoothstep(0.1, 0.24, h)) * (0.55 + 0.3 * bloom));
    colour = mix(colour, zenith * 1.3 + vec3(0.02, 0.03, 0.08), away * (1.0 - smoothstep(0.0, 0.05, h)) * 0.45);
    // The forward glow round the sun, wide and soft, as fine dust and haze scatter it.
    float g = 0.82;
    float glow = (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * mu, 1.5);
    colour += sunColour * 0.018 * glow * (0.5 + 0.5 * low) * (1.0 + 0.7 * bloom);
    // Thin high cloud drifting slowly, lit on its edges toward the sun.
    vec2 at = d.xz / (h + 0.12) * 1.4 + vec2(time * 0.006, time * 0.002);
    float c = fbm(at * 1.2);
    c = smoothstep(0.5, 0.8, c) * smoothstep(0.015, 0.1, up) * (1.0 - smoothstep(0.3, 0.65, up)) * cloud;
    // Each cloud soft, its underside and its side toward the sun gold, cooling to a soft violet away from it.
    float behind = smoothstep(0.5, 0.8, fbm(at * 1.2 - sunDir.xz * 0.3));
    float edge = clamp((c - behind) * 1.5 + 0.55, 0.0, 1.0);
    vec3 cool = mix(middle, horizonColour, 0.3) * 0.95;
    vec3 lit = mix(cool, sunColour * (1.6 + 1.2 * bloom) + vec3(0.08, 0.04, 0.0), edge * (0.25 + 0.75 * pow(toward, 1.2)));
    colour = mix(colour, lit, c * 0.7);
    // The sun's disc, its edge darker than its middle, cut by nothing but the land.
    float disc = smoothstep(0.9992, 0.99965, mu);
    float limb = 0.75 + 0.25 * smoothstep(0.9992, 0.99995, mu);
    vec3 core = mix(sunColour, vec3(1.0, 0.93, 0.78), 0.65);
    colour = mix(colour, core * 1.9 * limb, disc);
    // Its bloom: a tight bright halo, then a wider warm one.
    colour += (core * pow(toward, 900.0) * 1.4 + sunColour * pow(toward, 70.0) * 0.5 + sunColour * pow(toward, 9.0) * 0.18) * (1.0 + 0.4 * bloom);
    // A little noise below one step of the screen's colour, so the soft sky never shows rings.
    colour += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;
    gl_FragColor = vec4(colour, 1.0);
    #include <colorspace_fragment>
  }
`;

/**
 * A sky worked out at every pixel: the colours given from the horizon up, the low air thick and warm toward the sun,
 * a wide soft glow round the sun as haze scatters it, its disc with a darker edge, and thin high cloud drifting
 * slowly, lit on the sun's side. `sunDir` is a unit direction toward the sun. Returns the dome and a `move(time)` for
 * the cloud's drift.
 */
function airSky(
  colours: { top: Color; middle: Color; horizon: Color; below: Color; sun: Color },
  sunDir: Vector3,
  radius: number,
  options: { cloud?: number } = {},
): { mesh: Mesh; move(time: number): void; cloud(amount: number): void; bloom(amount: number): void } {
  const material = new ShaderMaterial({
    uniforms: {
      zenith: { value: colours.top },
      middle: { value: colours.middle },
      horizonColour: { value: colours.horizon },
      below: { value: colours.below },
      sunColour: { value: colours.sun },
      sunDir: { value: sunDir },
      time: { value: 0 },
      cloud: { value: options.cloud ?? 1 },
      bloom: { value: 0 },
    },
    vertexShader: SKY_VERTEX,
    fragmentShader: SKY_FRAGMENT,
    side: BackSide,
    depthWrite: false,
    toneMapped: false,
  });
  const mesh = new Mesh(new SphereGeometry(radius, 64, 32), material);
  // After setupScene's flat backdrop (-1), before anything of the place.
  mesh.renderOrder = -0.95;
  const uniforms = material.uniforms as { time: { value: number }; cloud: { value: number }; bloom: { value: number } };
  return {
    mesh,
    move(time) {
      uniforms.time.value = time;
    },
    cloud(amount) {
      uniforms.cloud.value = amount;
    },
    bloom(amount) {
      uniforms.bloom.value = amount;
    },
  };
}

/**
 * A picture read smoothly between its cells (a cubic B-spline from four linear reads): read linearly, a sharp edge of
 * light or shadow, or a dune's crest in a picture of slopes, shows the cells' square steps. Under low detail one read.
 */
const SAND_CUBIC = `
vec4 sandCubic(sampler2D map, vec2 uv, float size, vec2 dx, vec2 dy) {
  if (sandDetail < 0.25) return textureGrad(map, uv, dx, dy);
  vec2 st = uv * size - 0.5;
  vec2 i = floor(st);
  vec2 f = st - i;
  vec2 f2 = f * f;
  vec2 f3 = f2 * f;
  vec2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0;
  vec2 w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
  vec2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0;
  vec2 w3 = f3 / 6.0;
  vec2 g0 = w0 + w1;
  vec2 g1 = w2 + w3;
  vec2 a = (i - 1.0 + w1 / g0 + 0.5) / size;
  vec2 b = (i + 1.0 + w3 / g1 + 0.5) / size;
  return g0.y * (g0.x * textureGrad(map, a, dx, dy) + g1.x * textureGrad(map, vec2(b.x, a.y), dx, dy))
    + g1.y * (g0.x * textureGrad(map, vec2(a.x, b.y), dx, dy) + g1.x * textureGrad(map, b, dx, dy));
}
`;

/** A place's colours as `#RRGGBB`: a sky from `top` through `middle` to `horizon`, a ground from `far` to `near`. */
export interface LandColours {
  readonly top: string;
  readonly middle: string;
  readonly horizon: string;
  readonly far: string;
  readonly near: string;
  /** The low sun's colour, which lights the faces of the ground turned to it. */
  readonly light: string;
}

/**
 * Open land to the horizon round the person, in the colours given (a person's own picture's, from
 * `coloursOfPicture` in `src/picture.ts`, or chosen). The person stands on the crest of a dune, level at their feet,
 * the sand falling away along the wind into a field of dunes (`crest` 1, sharp crests) or soft hills (`crest` 0) below
 * them, `swell` metres high near them, rising far off into a hazed line of distant dunes along the horizon. A low sun
 * of the `light`'s colour, warmed, stands over the horizon (`sun` its direction in radians, 0 ahead of the person and
 * positive to their right; 0.8, ahead and to the right, by default, so dunes ahead are lit from the side and the sun's glow stands at the view's edge); the sand is warm
 * on faces turned to it and cool in their lee, with wind ripples and grain near the person; the sky warms low round
 * the sun, with a few soft bands of high cloud. With `grains`, that many grains of sand blowing low (none unless
 * asked); with `stars`, that many stars, on the side of the sky away from the sun (give them only for a place after
 * sunset). `light(...)` and `shade(...)` let something near the sand (a window floating over it) cast its own light
 * and a soft shade on it. It opens from black, lifting over its first second of `move`, and over its first twenty
 * seconds its low light and the sun's glow build to a peak and settle (`peak: false` keeps them still). One mesh for
 * the ground, one for the sky.
 */
export function openLand(
  colours: LandColours,
  options: {
    swell?: number;
    crest?: number;
    sun?: number;
    grains?: number;
    peak?: boolean;
    stars?: number;
    seed?: number;
    quality?: 'auto' | 'high' | 'low';
  } = {},
): Place & {
  height(x: number, z: number): number;
  light(x: number, y: number, z: number, colour: Color, strength: number): void;
  shade(x: number, z: number, depth: number): void;
  castShadow(x: number, z: number, bottom: number, top: number, width: number, depth: number): void;
} {
  const group = new Group();
  const top = new Color(colours.top);
  const middle = new Color(colours.middle);
  const horizon = new Color(colours.horizon);
  const far = new Color(colours.far);
  const near = new Color(colours.near);
  // A low sun is warm: the picture's light, pushed toward orange.
  const sunColour = new Color(colours.light).lerp(new Color('#FF8040'), 0.6);
  const radius = 110;

  const random = seeded(options.seed ?? 7);
  const sunAt = options.sun ?? 0.8;
  // How much the place draws: 'high' all of it, 'low' the least, 'auto' all of it until frames come slow.
  const quality = options.quality ?? 'auto';
  const land = landShape({ ...options, sun: sunAt });
  const height = land.height;
  const [wx, wz] = land.wind;

  // The sun lights the ground from 7 degrees up and shows itself a little lower, just clear of the far crests.
  const lightUp = 0.075;
  const sun = { x: Math.sin(sunAt) * Math.cos(lightUp), y: Math.sin(lightUp), z: -Math.cos(sunAt) * Math.cos(lightUp) };
  const sunLight3 = new Vector3(sun.x, sun.y, sun.z);
  const lit = new Color();
  const shadow = new Color();
  // The grade: gold for the sun's light, blue-violet for the sky's light in shade.
  const gold = sunColour.clone().lerp(new Color('#FFC27A'), 0.5);
  const shadeSky = top.clone().lerp(middle, 0.35).lerp(new Color('#4A45A8'), 0.5);
  const farShade = shadeSky.clone().multiplyScalar(0.75);
  const c = new Color();
  const step = 0.35;
  // The colour of the sand at (x, z): lit by the low sun on the faces turned to it, in the long shadow of a swell
  // between it and the sun, the lee lit only by the sky, and far off going into the haze of the horizon.
  // The sky is warmer low round the sun, as just after sunset: how much, by how far toward the sun a bearing is
  // (toward, -1 to 1) and how high (up, 0 at the horizon).
  const afterglow = sunColour.clone().lerp(horizon, 0.35);
  const warmAt = (toward: number, up: number) =>
    (0.06 + 0.94 * Math.max(0, toward) ** 1.5) * Math.max(0, 1 - Math.max(0, up) / 0.3) ** 2 * 0.8;
  const hazeColour = new Color();
  const sandAt = (x: number, z: number, into: Color): Color => {
    const y = height(x, z);
    const r = Math.hypot(x, z);
    const nx = height(x - step, z) - height(x + step, z);
    const nz = height(x, z - step) - height(x, z + step);
    const ny = 2 * step;
    const facing = Math.max(0, (nx * sun.x + ny * sun.y + nz * sun.z) / Math.hypot(nx, ny, nz));
    into.copy(near).lerp(far, Math.min(1, (r / radius) ** 0.6));
    // In shade the sand still has the sky's light and a little warmth bounced from the lit slopes round it.
    // Graded as dusk light is: faces toward the sun gold, faces away from it cool blue-violet, lit by the sky.
    shadow.copy(into).multiplyScalar(0.26).lerp(shadeSky, 0.5);
    lit.copy(into).lerp(gold, 0.55).multiplyScalar(1.9);
    let shade = 0;
    // No march where the shadow would fade to nothing anyway (past 44 m): most of the land's rings are out there.
    for (let s = 1; s <= 14 && shade < 1 && r < 44; s++) {
      const d = s * 0.8 + s * s * 0.22;
      const over = height(x + sun.x * d, z + sun.z * d) - (y + sun.y * d);
      if (over > 0) shade = Math.max(shade, Math.min(1, over / 0.7));
    }
    // A soft turn from light to shade across each slope, never a line drawn across the sand.
    // Far off a shadow softens into the haze, so distant slopes never step.
    // Far off only the slopes' own turn from the sun shades them: shadows marched there, between rings a metre or
    // more apart, would stripe the far dunes.
    shade *= 1 - Math.min(1, Math.max(0, (r - 24) / 20));
    const k = Math.min(1, Math.max(0, (facing + 0.12) / 0.5)) * (1 - 0.85 * shade);
    // Sand seen toward the low sun from where the person stands is lit through the air and the glowing sky: its
    // shade is filled a little warm, yet stays dark, so dunes against the sun stand as shapes edged in light.
    const bearing = r > 0.5 ? Math.max(0, (x * Math.sin(sunAt) - z * Math.cos(sunAt)) / r) : 0;
    const fill = bearing * bearing * 0.07;
    // Broad soft changes over the shaded faces, as wind leaves them, so no face is one flat colour.
    const wander = 1 + 0.09 * Math.sin(x * 0.21 + z * 0.13) * Math.sin(z * 0.17 - x * 0.07 + 1.3);
    shadow.lerp(lit, fill).multiplyScalar(wander);
    // Far off light and shade draw together, as distance and haze soften them: a far slip face never reads as a
    // dark ruled line under its crest.
    const kk = k * k * (3 - 2 * k);
    into.copy(shadow).lerp(lit, kk + (0.55 - kk) * 0.45 * Math.min(1, Math.max(0, (r - 22) / 45)));
    // A warm rim along a crest turned toward the sun: where the ground bends over (its heights round it lower than
    // it), lit, and facing the sun's side.
    const bend = (4 * y - height(x + 1, z) - height(x - 1, z) - height(x, z + 1) - height(x, z - 1)) / 4;
    const rim = Math.min(1, Math.max(0, bend * 3)) * Math.min(1, facing / 0.25) * (1 - shade);
    into.lerp(sunColour, rim * 0.5);
    // Only far off does the ground go into the haze, so far crests still stand against the sky.
    // Far off the sand goes into the haze, until at its edge it is exactly the sky behind it at that bearing, so the
    // land's edge never shows as a line.
    const toward = r > 0 ? (x * Math.sin(sunAt) - z * Math.cos(sunAt)) / r : 0;
    hazeColour.copy(horizon).lerp(afterglow, warmAt(toward, 0));
    return into.lerp(hazeColour, Math.min(1, Math.max(0, (r - 40) / 70)) ** 2 * 0.25);
  };
  // Rings closer together near the person, where the ground's shape shows most; fine round, so far crests are
  // smooth lines, not steps.
  const rings = 120;
  const around = 384;
  const positions: number[] = [];
  const tints: number[] = [];
  const rim = new Color(0, 0, 0);
  for (let i = 0; i <= rings; i++) {
    const r = i === 0 ? 0 : radius * (i / rings) ** 1.8;
    for (let j = 0; j < around; j++) {
      const a = (j / around) * Math.PI * 2;
      const x = Math.sin(a) * r;
      const z = -Math.cos(a) * r;
      // Far off the ground rises into a line of distant dunes along the horizon, a darker silhouette against the
      // bright sky, so the land meets the sky in hills, never in a ruled line, and no sky shows under it.
      const edge = Math.min(1, Math.max(0, (r - 80) / 30)) ** 2;
      const skyline = 2.2 + 2.6 * (0.5 + 0.5 * Math.sin(a * 3 + 1.1)) * (0.55 + 0.45 * Math.sin(a * 7 + 2.4)) * (0.8 + 0.2 * Math.sin(a * 17));
      const y = height(x, z);
      positions.push(x, y + (skyline - y) * edge, z);
      sandAt(x, z, c);
      c.lerp(farShade, edge * 0.5);
      if (i === rings) rim.add(c);
      tints.push(c.r, c.g, c.b);
    }
  }
  // Near the person the sand's light is a picture of it made once, about 38 cm a step, read in the ground's shader at every
  // pixel: the ring of vertices alone smears light and shadow into streaks round the person.
  const span = 36;
  const cells = quality === 'low' ? 128 : 192;
  let light = new Float32Array(cells * cells * 3);
  for (let row = 0; row < cells; row++) {
    for (let col = 0; col < cells; col++) {
      sandAt(((col + 0.5) / cells) * 2 * span - span, ((row + 0.5) / cells) * 2 * span - span, c);
      light.set([c.r, c.g, c.b], (row * cells + col) * 3);
    }
  }
  // Softened once over its neighbours (about a quarter of a metre), so no edge of light or shadow steps from cell to
  // cell.
  for (let pass = 0; pass < 1; pass++) {
    const soft = new Float32Array(light.length);
    for (let row = 0; row < cells; row++) {
      for (let col = 0; col < cells; col++) {
        for (let k = 0; k < 3; k++) {
          let sum = 0;
          let n = 0;
          for (let dr = -1; dr <= 1; dr++) {
            for (let dc = -1; dc <= 1; dc++) {
              const rr = row + dr;
              const cc = col + dc;
              if (rr < 0 || rr >= cells || cc < 0 || cc >= cells) continue;
              sum += light[(rr * cells + cc) * 3 + k] as number;
              n++;
            }
          }
          soft[(row * cells + col) * 3 + k] = sum / n;
        }
      }
    }
    light = soft;
  }
  const nearLight = new Uint8Array(cells * cells * 4);
  const srgb = { r: 0, g: 0, b: 0 };
  for (let i = 0; i < cells * cells; i++) {
    c.setRGB(light[i * 3] as number, light[i * 3 + 1] as number, light[i * 3 + 2] as number);
    c.getRGB(srgb, SRGBColorSpace);
    nearLight[i * 4] = Math.round(Math.min(1, srgb.r) * 255);
    nearLight[i * 4 + 1] = Math.round(Math.min(1, srgb.g) * 255);
    nearLight[i * 4 + 2] = Math.round(Math.min(1, srgb.b) * 255);
    nearLight[i * 4 + 3] = 255;
  }
  const nearPicture = new DataTexture(nearLight, cells, cells, RGBAFormat);
  nearPicture.colorSpace = SRGBColorSpace;
  nearPicture.magFilter = LinearFilter;
  nearPicture.minFilter = LinearMipmapLinearFilter;
  nearPicture.generateMipmaps = true;
  // Seen at a slant, as the sand round the person always is, it stays sharp rather than smearing toward them.
  nearPicture.anisotropy = 8;
  nearPicture.needsUpdate = true;
  // Wind ripples: a small tile of how steeply the sand's ripples slope, repeated over the ground across the wind and
  // mip-mapped, so far off it averages to plain sand and never shimmers into lines.
  const rippleCells = 128;
  const rippleSlopes = new Uint8Array(rippleCells * rippleCells * 4);
  // Crests that bend gently along their length (a strong bend over a short span draws chevrons, not sand).
  const rippleAt = (u: number, v: number) =>
    Math.sin(Math.PI * 2 * (9 * u + 0.12 * Math.sin(Math.PI * 2 * v) + 0.08 * Math.sin(Math.PI * 2 * (2 * v + u)))) *
    (0.75 + 0.25 * Math.sin(Math.PI * 2 * (2 * v - u)));
  for (let row = 0; row < rippleCells; row++) {
    for (let col = 0; col < rippleCells; col++) {
      const u = col / rippleCells;
      const v = row / rippleCells;
      const e = 1 / rippleCells;
      const du = (rippleAt(u + e, v) - rippleAt(u - e, v)) / 2;
      const dv = (rippleAt(u, v + e) - rippleAt(u, v - e)) / 2;
      const o = (row * rippleCells + col) * 4;
      rippleSlopes[o] = Math.round(Math.min(1, Math.max(0, 0.5 + du * 2.2)) * 255);
      rippleSlopes[o + 1] = Math.round(Math.min(1, Math.max(0, 0.5 + dv * 2.2)) * 255);
      rippleSlopes[o + 2] = 128;
      rippleSlopes[o + 3] = 255;
    }
  }
  const ripples = new DataTexture(rippleSlopes, rippleCells, rippleCells, RGBAFormat);
  ripples.wrapS = RepeatWrapping;
  ripples.wrapT = RepeatWrapping;
  ripples.magFilter = LinearFilter;
  ripples.minFilter = LinearMipmapLinearFilter;
  ripples.generateMipmaps = true;
  // Seen at a slant at the feet, a ripple stays crisp along its length rather than smearing.
  ripples.anisotropy = 8;
  ripples.needsUpdate = true;
  // The whole land's slopes, made once: far off, where the rings of vertices are a metre or more apart, the sand is
  // lit at every pixel from these, so far crests keep a sharp edge between their lit and shaded sides.
  const normalCells = quality === 'low' ? 128 : 256;
  const normalSlopes = new Uint8Array(normalCells * normalCells * 4);
  const reachOut = radius;
  for (let row = 0; row < normalCells; row++) {
    for (let col = 0; col < normalCells; col++) {
      const x = ((col + 0.5) / normalCells) * 2 * reachOut - reachOut;
      const z = ((row + 0.5) / normalCells) * 2 * reachOut - reachOut;
      // Forward differences: three heights a cell, not four (the land is built on the headset as the app opens).
      const e = 0.6;
      const here = height(x, z);
      const nx = here - height(x + e, z);
      const nz = here - height(x, z + e);
      const ny = e;
      const n = Math.hypot(nx, ny, nz);
      const o = (row * normalCells + col) * 4;
      normalSlopes[o] = Math.round((0.5 + 0.5 * (nx / n)) * 255);
      normalSlopes[o + 1] = Math.round((0.5 + 0.5 * (nz / n)) * 255);
      normalSlopes[o + 2] = Math.round((ny / n) * 255);
      normalSlopes[o + 3] = 255;
    }
  }
  const landNormals = new DataTexture(normalSlopes, normalCells, normalCells, RGBAFormat);
  landNormals.magFilter = LinearFilter;
  landNormals.minFilter = LinearMipmapLinearFilter;
  landNormals.generateMipmaps = true;
  landNormals.anisotropy = 8;
  landNormals.needsUpdate = true;
  // How much fine detail the sand draws (1 all of it, 0 the least), lowered by itself under 'auto' when frames come
  // slower than 50 a second, so a headset short of its frame rate steps down instead of stuttering.
  const detail = { value: quality === 'low' ? 0 : 1 };
  // How far the place is into its peak, 0 to 1 (see `move`).
  const bloom = { value: 0 };
  const index: number[] = [];
  for (let i = 0; i < rings; i++) {
    for (let j = 0; j < around; j++) {
      const a = i * around + j;
      const b = i * around + ((j + 1) % around);
      const d = (i + 1) * around + j;
      const e = (i + 1) * around + ((j + 1) % around);
      // Counter-clockwise seen from above, so the ground faces up.
      if (i > 0) index.push(a, b, d);
      index.push(b, e, d);
    }
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setAttribute('color', new Float32BufferAttribute(tints, 3));
  geometry.setIndex(index);
  const sand = new MeshBasicMaterial({ vertexColors: true, toneMapped: false, dithering: true });
  // Where a light near the sand is this frame, and its colour times its strength (none until one is given).
  const lampAt = new Vector3(0, -1000, 0);
  const lampColour = new Vector3(0, 0, 0);
  // Where something floats over the sand (x, z) and how dark the shade under it is.
  const underAt = new Vector3(0, 0, 0);
  // A long shadow cast by something standing over the sand: where it starts and ends along the sand away from the
  // sun, its half width and how dark (none until one is cast).
  const castFrom = new Vector3(0, 0, 0);
  const castBand = new Vector3(0, 0, 0);
  // The near sand's picture, a fine grain and a sheen toward the low sun, in the ground's own shader (no extra draw
  // call).
  sand.onBeforeCompile = (shader) => {
    shader.uniforms.sandNear = { value: nearPicture };
    shader.uniforms.sandRipples = { value: ripples };
    shader.uniforms.sandWind = { value: [wx, wz] };
    shader.uniforms.sandNormals = { value: landNormals };
    // Each picture's cells across, for reading it smoothly between its cells.
    shader.uniforms.sandSizes = { value: [cells, normalCells] };
    shader.uniforms.sandReach = { value: reachOut };
    shader.uniforms.sandDetail = detail;
    shader.uniforms.sandBaseNear = { value: near };
    shader.uniforms.sandBaseFar = { value: far };
    shader.uniforms.sandGold = { value: gold };
    shader.uniforms.sandShadeSky = { value: shadeSky };
    shader.uniforms.sandSun3 = { value: sunLight3 };
    shader.uniforms.sandLamp = { value: lampAt };
    shader.uniforms.sandLampColour = { value: lampColour };
    shader.uniforms.sandUnder = { value: underAt };
    shader.uniforms.sandCastFrom = { value: castFrom };
    shader.uniforms.sandCastBand = { value: castBand };
    shader.uniforms.sandSpan = { value: span };
    shader.uniforms.sandSun = { value: [Math.sin(sunAt), -Math.cos(sunAt)] };
    shader.uniforms.sandSheen = { value: [sunColour.r, sunColour.g, sunColour.b] };
    shader.uniforms.sandFog = { value: horizon };
    shader.uniforms.sandBloom = bloom;
    shader.vertexShader = shader.vertexShader
      .replace('void main() {', 'varying vec3 vSand;\nvoid main() {')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n  vSand = (modelMatrix * vec4(transformed, 1.0)).xyz;');
    shader.fragmentShader = shader.fragmentShader
      .replace('void main() {', 'uniform sampler2D sandNear;\nuniform sampler2D sandNormals;\nuniform float sandReach;\nuniform float sandDetail;\nuniform vec3 sandBaseNear;\nuniform vec3 sandBaseFar;\nuniform vec3 sandGold;\nuniform vec3 sandShadeSky;\nuniform vec3 sandSun3;\nuniform sampler2D sandRipples;\nuniform vec2 sandWind;\nuniform vec3 sandLamp;\nuniform vec3 sandLampColour;\nuniform vec3 sandUnder;\nuniform vec3 sandCastFrom;\nuniform vec3 sandCastBand;\nuniform float sandSpan;\nuniform vec2 sandSun;\nuniform vec3 sandSheen;\nuniform vec3 sandFog;\nuniform float sandBloom;\nuniform vec2 sandSizes;\nvarying vec3 vSand;\n' + SAND_CUBIC + 'void main() {')
      .replace(
        '#include <opaque_fragment>',
        `{
    vec2 p = vSand.xz;
    // How much sand one pixel covers here, from its distance and how slanting the view is: smooth across every
    // triangle of the ground (screen derivatives jump where two triangles meet, and would draw the mesh as lines).
    vec3 look = vSand - cameraPosition;
    float dist = length(look);
    vec3 viewDir = look / max(dist, 0.001);
    float across = dist * 0.0011;
    float footprint = across / max(abs(viewDir.y), 0.08);
    float grad = sqrt(across * footprint);
    // Far off, the sand lit at every pixel from the land's own slopes: gold toward the sun, blue-violet in shade.
    float farOff = smoothstep(24.0, 38.0, length(p));
    if (farOff > 0.0) {
      vec2 nCell = p / (2.0 * sandReach) + 0.5;
      vec4 ns = sandCubic(sandNormals, nCell, sandSizes.y, vec2(grad / (2.0 * sandReach), 0.0), vec2(0.0, grad / (2.0 * sandReach)));
      vec3 n = normalize(vec3(ns.r * 2.0 - 1.0, ns.b, ns.g * 2.0 - 1.0));
      float faceSun = max(0.0, dot(n, sandSun3));
      vec3 base = mix(sandBaseNear, sandBaseFar, pow(min(1.0, length(p) / sandReach), 0.6));
      vec3 shadeC = mix(base * 0.24, sandShadeSky, 0.6);
      vec3 litC = mix(base, sandGold, 0.55) * 1.9;
      float k = clamp((faceSun + 0.12) / 0.5, 0.0, 1.0);
      vec3 farLight = mix(shadeC, litC, k * k * (3.0 - 2.0 * k));
      // A crest seen toward the low sun catches a bright rim where its slope turns away from the eye.
      vec3 toEye = normalize(cameraPosition - vSand);
      float edgeOn = pow(1.0 - max(dot(n, toEye), 0.0), 3.0);
      float towardSun = max(0.0, dot(normalize(-toEye.xz), sandSun));
      farLight += sandGold * edgeOn * towardSun * towardSun * 0.9;
      outgoingLight = mix(outgoingLight, farLight, farOff);
    }
    // Near the person, the sand's light from its picture, blending into the rings' own colour by its edge.
    vec2 cell = p / (2.0 * sandSpan) + 0.5;
    // Blended out in a circle well inside the picture's square, so its edge never shows as a straight line.
    float inside = 1.0 - smoothstep(0.55, 0.92, length(p) / sandSpan);
    if (inside > 0.0) outgoingLight = mix(outgoingLight, sandCubic(sandNear, cell, sandSizes.x, vec2(grad / (2.0 * sandSpan), 0.0), vec2(0.0, grad / (2.0 * sandSpan))).rgb, inside);
    // Ripples across the wind, a tile 2.4 m across: lighter on the side of each that faces the sun, darker on the
    // other, faintly in shade as well; strongest at the feet and gone by 30 m.
    // Ripples from the tile, across the wind (near the feet the exact ripples below take over).
    vec2 q = vec2(dot(p, sandWind), dot(p, vec2(-sandWind.y, sandWind.x))) / 2.4;
    // One tile only, read at the slanted footprint's full length: two tiles at different angles over each other
    // draw a herringbone moire, and a finer read shimmers.
    vec2 slope = (textureGrad(sandRipples, q, vec2(footprint / 2.4, 0.0), vec2(0.0, footprint / 2.4)).rg - 0.5) * 1.0;
    // And a finer one at the feet, so the sand close by stays crisp.
    vec2 q3 = vec2(q.x * 0.9 - q.y * 0.43, q.y * 0.9 + q.x * 0.43) * 2.3 + vec2(0.13, 0.52);
    slope += (textureGrad(sandRipples, q3, vec2(footprint * 0.96, 0.0), vec2(0.0, footprint * 0.96)).rg - 0.5) * 0.8 * (1.0 - smoothstep(1.5, 5.0, length(p)));
    float sunAlong = dot(sandSun, sandWind);
    float sunAcross = dot(sandSun, vec2(-sandWind.y, sandWind.x));
    float rippleNear = 1.0 - smoothstep(2.5, 12.0, length(p));
    float atFeet = 1.0 - smoothstep(2.0, 7.0, length(p));
    // Close to the person, ripples worked out exactly at every pixel (no texture to blur at a slant): sharp-crested,
    // bending across their length, faded only where one would be finer than a couple of pixels.
    float ra = dot(p, sandWind);
    float rc = dot(p, vec2(-sandWind.y, sandWind.x));
    // Sinuous as wind ripples are: about 16 cm apart, bending at several scales and warped by a smooth noise, so
    // no two crests run parallel and none reads as a straight line.
    // A smooth warp from sums of sines at unrelated angles (a noise on a square grid would crease along its cells).
    float warp = 0.5 + 0.22 * sin(p.x * 1.7 + p.y * 0.6) + 0.18 * sin(p.y * 2.3 - p.x * 1.1 + 1.3)
      + 0.1 * sin(p.x * 4.1 + p.y * 3.3 + 0.7);
    // Spacing that drifts between about 13 and 19 cm across the sand, and two fields of ripples at different
    // spacings blended by a slow mask: where they part, a crest forks or dies out, as real ripples do.
    float drift = ra * 40.0 + 9.0 * sin(ra * 1.1 + rc * 0.5) + 2.6 * sin(rc * 5.0 + ra * 1.2) + 1.3 * sin(rc * 11.0 - ra * 2.1) + 6.0 * warp;
    float other = ra * 53.0 + 6.0 * sin(ra * 0.9 - rc * 0.7 + 2.0) + 2.2 * sin(rc * 6.1 - ra * 0.8) + 5.0 * warp;
    float blend = smoothstep(0.25, 0.75, 0.5 + 0.3 * sin(rc * 4.6 + ra * 2.1 + 1.0) + 0.2 * sin(ra * 5.3 - rc * 3.7));
    float phase1 = mix(drift, other, blend);
    float crisp = (1.0 - smoothstep(0.25, 0.7, footprint * 66.0)) * (1.0 - smoothstep(1.0, 7.0, length(p)));
    // A ripple's face: a long slope lit as it rises into the wind, then a sharp crest and a short steep side in
    // shade, as wind-blown sand lies (never a smooth wave).
    float s1 = fract(phase1 / 6.2832);
    // The crest's drop about two pixels wide: narrower would alias, and an aliased crest draws as straight lines
    // fanning across the sand.
    float w1 = clamp(footprint * 66.0 / 6.2832 * 2.2, 0.03, 0.35);
    // One continuous profile round the whole period: up the lit face, a sharp drop at the crest, then a short rise in
    // shade back to where the next face begins (so there is one line per crest, never two).
    float lee1 = mix(-0.9, -0.55, clamp((s1 - 0.78) / 0.22, 0.0, 1.0));
    float face1 = mix(-0.55 + 1.3 * s1 / 0.78, lee1, smoothstep(0.78 - w1, 0.78 + w1, s1));
    // The lit windward face brighter and the short lee face darker, as the low sun shows them.
    face1 = face1 > 0.0 ? face1 * 1.25 : face1 * 1.15;
    // Each ripple stronger here and fainter there, so crests fade out and come back as real ones do.
    float strength1 = 0.5 + 0.5 * (0.5 + 0.5 * sin(warp * 9.0 + ra * 0.9 + rc * 0.4));
    float facingSun = 0.55 + 0.45 * sunAlong;
    outgoingLight *= 1.0 + sandDetail * crisp * 0.26 * face1 * strength1 * facingSun;
    float litFace = crisp > 0.0 ? smoothstep(0.1, 0.5, face1) : 1.0;
    // The soft ripples from the tile only where the exact ripples below have faded, so no crest is drawn twice.
    outgoingLight *= 1.0 + rippleNear * (1.0 - 0.92 * crisp) * (0.2 * (slope.x * sunAlong + slope.y * sunAcross) + 0.06 * slope.x);
    // Fine grain, only where a grain is still bigger than a pixel.
    // Smooth grain (value noise, never square cells), at two sizes.
    vec2 g = p * 30.0;
    vec2 gi = floor(g);
    vec2 gf = g - gi;
    gf = gf * gf * (3.0 - 2.0 * gf);
    float h00 = fract(sin(dot(gi, vec2(12.9898, 78.233))) * 43758.5453);
    float h10 = fract(sin(dot(gi + vec2(1.0, 0.0), vec2(12.9898, 78.233))) * 43758.5453);
    float h01 = fract(sin(dot(gi + vec2(0.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    float h11 = fract(sin(dot(gi + vec2(1.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    float grain = mix(mix(h00, h10, gf.x), mix(h01, h11, gf.x), gf.y) - 0.5;
    outgoingLight *= 1.0 + 0.06 * grain * (1.0 - smoothstep(0.4, 1.0, footprint * 30.0));
    // A finer grain, a centimetre across, seen only near the feet.
    vec2 fg = p * 110.0;
    vec2 fi = floor(fg);
    vec2 ff = fg - fi;
    ff = ff * ff * (3.0 - 2.0 * ff);
    float f00 = fract(sin(dot(fi, vec2(27.17, 91.31))) * 41231.27);
    float f10 = fract(sin(dot(fi + vec2(1.0, 0.0), vec2(27.17, 91.31))) * 41231.27);
    float f01 = fract(sin(dot(fi + vec2(0.0, 1.0), vec2(27.17, 91.31))) * 41231.27);
    float f11 = fract(sin(dot(fi + vec2(1.0, 1.0), vec2(27.17, 91.31))) * 41231.27);
    float fine = mix(mix(f00, f10, ff.x), mix(f01, f11, ff.x), ff.y) - 0.5;
    outgoingLight *= 1.0 + 0.1 * fine * (1.0 - smoothstep(0.4, 1.0, footprint * 110.0));
    // A soft shade on the sand under something floating over it (a window's own).
    float under = length(p - sandUnder.xy);
    outgoingLight *= 1.0 - sandUnder.z * exp(-under * under / 1.8);
    // A long soft shadow cast away from the low sun by something standing over the sand.
    vec2 away = -sandSun;
    vec2 fromFoot = p - sandCastFrom.xy;
    float along2 = dot(fromFoot, away);
    float across2 = abs(dot(fromFoot, vec2(-away.y, away.x)));
    float inShadow = smoothstep(sandCastBand.x - 0.6, sandCastBand.x + 0.6, along2) * (1.0 - smoothstep(sandCastBand.y - 2.0, sandCastBand.y + 1.0, along2))
      * (1.0 - smoothstep(sandCastFrom.z * 0.7, sandCastFrom.z * 1.3 + 0.25, across2));
    outgoingLight *= 1.0 - sandCastBand.z * inShadow * (1.0 - 0.6 * clamp((along2 - sandCastBand.x) / max(1.0, sandCastBand.y - sandCastBand.x), 0.0, 1.0));
    // Sand looked at toward a low sun shines along it, as backlit dunes do at sunset.
    float toward = max(0.0, dot(normalize(look.xz), sandSun));
    float graze = 1.0 - abs(normalize(look).y);
    outgoingLight += sandSheen * pow(toward, 3.0) * graze * graze * 0.22;
    // A light near the sand (a window's own), warming it in a pool that is gone within a few metres.
    float lampDistance = length(sandLamp - vSand);
    outgoingLight += sandLampColour * (0.5 / (1.0 + lampDistance * lampDistance * 1.2)) * (1.0 - smoothstep(1.2, 3.0, lampDistance));
    // The air between the person and the sand: thicker low down and far off, its colour the sky's own at the
    // horizon, warmer and brighter toward the sun, so the land melts into the sky with no edge.
    float sunward = max(0.0, dot(normalize(viewDir.xz), sandSun));
    float density = 0.005 * exp(-max(vSand.y + 3.0, 0.0) * 0.12) * smoothstep(25.0, 90.0, dist) + 0.0008;
    float thick = 1.0 - exp(-dist * density);
    // At the place's peak the low light warms the sand and the air toward the sun.
    outgoingLight *= 1.0 + sandBloom * vec3(0.14, 0.05, -0.03);
    vec3 airColour = sandFog + sandSheen * (0.06 + (0.5 + 0.35 * sandBloom) * sunward * sunward);
    outgoingLight = mix(outgoingLight, airColour, thick);
    // Grains catching the low sun: tiny glints on the sand seen toward it, only where a grain is bigger than a pixel.
    // Each glint a tiny round point in its own cell, there only from some places the eye stands, so it comes and
    // goes as the head moves.
    vec2 gp = p * 70.0;
    vec2 cellG = floor(gp);
    float pick = fract(sin(dot(cellG, vec2(41.31, 289.13))) * 15731.743);
    float seen = fract(sin(dot(cellG + floor(cameraPosition.xz * 9.0), vec2(17.7, 63.3))) * 9173.1);
    vec2 inCell = fract(gp) - 0.5;
    float round1 = 1.0 - smoothstep(0.05, 0.14, length(inCell));
    float glintNear = 1.0 - smoothstep(0.3, 0.8, footprint * 70.0);
    outgoingLight += sandSheen * step(0.993, pick) * step(0.5, seen) * round1 * glintNear * pow(sunward, 2.0) * 1.2 * litFace * sandDetail;
  }
  #include <opaque_fragment>`,
      );
  };
  group.add(new Mesh(geometry, sand));

  // The sky, worked out at every pixel; its under side the ground's own colour at its rim, so nothing shows where the
  // ground ends. The sun stands 5 degrees up, just clear of the far dunes.
  rim.multiplyScalar(1 / around);
  const sunUp = 0.1;
  const sunDir = new Vector3(Math.sin(sunAt) * Math.cos(sunUp), Math.sin(sunUp), -Math.cos(sunAt) * Math.cos(sunUp));
  const air = airSky({ top, middle, horizon, below: rim, sun: sunColour }, sunDir, 120, { cloud: detail.value });
  let lastFrame = 0;
  let lastStep = 0;
  let slow = 0;
  group.add(air.mesh);

  // With `grains`, that many grains of sand blowing low on the wind round the person.
  const grainCount = options.grains ?? 0;
  const grains = glows(Math.max(1, grainCount), { facing: true, halo: 0.002, round: 1, core: 1 });
  group.add(grains.object);
  const grain = Float32Array.from({ length: grainCount * 4 }, () => random());
  const grainColour = near.clone().lerp(sunColour, 0.6);

  // Stars come out first on the side of the sky away from the sun.
  const starCount = options.stars ?? 0;
  const stars = glows(Math.max(1, starCount), { facing: true, halo: 0.2, round: 1 });
  group.add(stars.object);
  // Each star: x, y, z, its twinkle's phase and its brightness, in one typed array read by index each frame.
  const home = new Float32Array(starCount * 5);
  for (let i = 0; i < starCount; i++) {
    const a = random() * Math.PI * 2;
    const up = 0.3 + random() * 0.7;
    const fromSun = Math.sin(a) * Math.sin(sunAt) + Math.cos(a) * Math.cos(sunAt);
    const bright = (up - 0.25) * Math.min(1, Math.max(0, 0.5 - fromSun) / 1.2) * 0.35;
    home.set([Math.sin(a) * 90 * Math.cos(up), 90 * Math.sin(up), -Math.cos(a) * 90 * Math.cos(up), random() * 6.28, bright], i * 5);
  }
  const starColour = new Color('#DCE3FF');
  // The place opens from black: a dark veil round the person, drawn over everything, lifts over its first second.
  const veil = new Mesh(
    new SphereGeometry(3, 16, 8),
    new MeshBasicMaterial({ color: 0x000000, side: BackSide, transparent: true, depthTest: false, depthWrite: false }),
  );
  veil.renderOrder = 1000;
  veil.frustumCulled = false;
  group.add(veil);
  let opened = Number.NaN;

  return {
    group,
    horizon,
    // The ground's height at any point, to stand things on it (a window on a crest).
    height,
    // One light near the sand for this frame (a window's own): it warms the sand round (x, y, z).
    light(x, y, z, colour, strength) {
      lampAt.set(x, y, z);
      lampColour.set(colour.r * strength, colour.g * strength, colour.b * strength);
    },
    // A soft shade on the sand round (x, z) for this frame, under something floating over it, `depth` 0 to 1.
    shade(x, z, depth) {
      underAt.set(x, z, Math.min(1, Math.max(0, depth)));
    },
    // The long shadow of something `width` metres wide standing at (x, z) from `bottom` to `top` metres over the sand,
    // cast away from the low sun, `depth` 0 to 1: it starts and ends where the sun's rays past its bottom and its top
    // meet the sand.
    castShadow(x, z, bottom, top, width, depth) {
      const run = 1 / Math.tan(lightUp);
      castFrom.set(x, z, width / 2);
      // One shadow, joined to the sand under the thing so it reads as its own, and kept within 10 m.
      castBand.set(Math.min(bottom * run, 0.3), Math.min(10, top * run), Math.min(1, Math.max(0, depth)));
    },
    move(time) {
      air.move(time);
      if (Number.isNaN(opened)) opened = time;
      // Its first twenty seconds build to a peak: the low light and the sun's glow rise to their fullest about 14 s
      // in, then settle to a calmer glow that stays.
      if (options.peak !== false) {
        const since = time - opened;
        const rise = Math.min(1, Math.max(0, (since - 2) / 11));
        const settle = Math.min(1, Math.max(0, (since - 15) / 9));
        bloom.value = rise * rise * (3 - 2 * rise) * (1 - 0.6 * settle * settle * (3 - 2 * settle));
        air.bloom(bloom.value);
      }
      if (veil.visible) {
        const k = Math.min(1, Math.max(0, (time - opened - 0.1) / 0.8));
        (veil.material as MeshBasicMaterial).opacity = 1 - k * k * (3 - 2 * k);
        if (k >= 1) veil.visible = false;
      }
      // Under 'auto', a stretch of slow frames lowers the detail a step, at most once every three seconds.
      if (quality === 'auto' && detail.value > 0) {
        const dt = time - lastFrame;
        if (dt > 0 && dt < 1) slow = slow * 0.95 + (dt > 1 / 50 ? 0.05 : 0);
        if (slow > 0.6 && time - lastStep > 3) {
          detail.value = Math.max(0, detail.value - 0.5);
          air.cloud(detail.value);
          lastStep = time;
          slow = 0;
        }
      }
      lastFrame = time;
      grains.begin();
      for (let i = 0; i < grainCount; i++) {
        const k = i * 4;
        // Each grain runs downwind 14 m and comes round again, rising and falling a little as it goes.
        const run = (((grain[k] * 14 + time * (1.2 + grain[k + 1] * 1.4)) % 14) + 14) % 14 - 7;
        const side = (grain[k + 2] - 0.5) * 14;
        const x = run * wx - side * wz;
        const z = run * wz + side * wx;
        const y = height(x, z) + 0.03 + grain[k + 3] * 0.22 + 0.04 * Math.sin(time * 3 + i);
        const fade = 1 - (Math.abs(run) / 7) ** 4;
        grains.add(x, y, z, 0.007, 0.007, grainColour, 0.16 * fade);
      }
      grains.end();
      stars.begin();
      for (let i = 0; i < starCount; i++) {
        const k = i * 5;
        const twinkle = 0.6 + 0.4 * Math.sin(time * 1.3 + (home[k + 3] as number) * 5);
        stars.add(home[k] as number, home[k + 1] as number, home[k + 2] as number, 0.24, 0.24, starColour, (home[k + 4] as number) * twinkle);
      }
      stars.end();
    },
  };
}
