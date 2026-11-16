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

/** A floor disc `radius` metres round the person, `centre` under them fading to `edge`. */
function floor(centre: Color, edge: Color, radius: number): Mesh {
  const geometry = new CircleGeometry(radius, 64, 0, Math.PI * 2);
  geometry.rotateX(-Math.PI / 2);
  const position = geometry.getAttribute('position');
  const colours: number[] = [];
  const c = new Color();
  for (let i = 0; i < position.count; i++) {
    const out = Math.hypot(position.getX(i), position.getZ(i)) / radius;
    c.copy(centre).lerp(edge, Math.min(1, out ** 0.5));
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
  group.add(sky(black, horizon, horizon, 120));
  group.add(floor(tint.clone().multiplyScalar(0.1), horizon, 60));
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
  // rising softly out of the floor with no edge, brightest three metres up and fading to nothing above.
  const ring = new CylinderGeometry(44, 44, 12, 96, 24, true);
  ring.translate(0, 6, 0);
  const at = ring.getAttribute('position');
  const glow: number[] = [];
  for (let i = 0; i < at.count; i++) {
    const y = at.getY(i);
    const k = (y < 3 ? (y / 3) ** 2 : Math.max(0, 1 - (y - 3) / 9) ** 2) * 0.16;
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
 * positive to their right; 1.25, to the right, by default, so every dune ahead is lit from the side); the sand is warm
 * on faces turned to it and cool in their lee, with wind ripples and grain near the person; the sky warms low round
 * the sun, with a few soft bands of high cloud. With `grains`, that many grains of sand blowing low (none unless
 * asked); with `stars`, that many stars, on the side of the sky away from the sun (give them only for a place after
 * sunset). `light(...)` and `shade(...)` let something near the sand (a window floating over it) cast its own light
 * and a soft shade on it. One mesh for the ground, one for the sky.
 */
export function openLand(
  colours: LandColours,
  options: { swell?: number; crest?: number; sun?: number; grains?: number; stars?: number; seed?: number } = {},
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
  const sunAt = options.sun ?? 1.25;
  const land = landShape({ ...options, sun: sunAt });
  const height = land.height;
  const [wx, wz] = land.wind;

  // The sun lights the ground from 7 degrees up and shows itself a little lower, just clear of the far crests.
  const lightUp = 0.12;
  const sun = { x: Math.sin(sunAt) * Math.cos(lightUp), y: Math.sin(lightUp), z: -Math.cos(sunAt) * Math.cos(lightUp) };
  const lit = new Color();
  const shadow = new Color();
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
    shadow.copy(into).multiplyScalar(0.58).lerp(middle, 0.26);
    lit.copy(into).lerp(sunColour, 0.4).multiplyScalar(1.45);
    let shade = 0;
    for (let s = 1; s <= 18 && shade < 1; s++) {
      const d = s * 0.7 + s * s * 0.16;
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
    // shade is filled warm, so the slopes under the sun never read as lit from somewhere else.
    const bearing = r > 0.5 ? Math.max(0, (x * Math.sin(sunAt) - z * Math.cos(sunAt)) / r) : 0;
    const fill = bearing * bearing * 0.5;
    // Broad soft changes over the shaded faces, as wind leaves them, so no face is one flat colour.
    const wander = 1 + 0.09 * Math.sin(x * 0.21 + z * 0.13) * Math.sin(z * 0.17 - x * 0.07 + 1.3);
    shadow.lerp(lit, fill).multiplyScalar(wander);
    // Far off light and shade draw together, as distance and haze soften them: a far slip face never reads as a
    // dark ruled line under its crest.
    const kk = k * k * (3 - 2 * k);
    into.copy(shadow).lerp(lit, kk + (0.55 - kk) * 0.65 * Math.min(1, Math.max(0, (r - 22) / 45)));
    // A warm rim along a crest turned toward the sun: where the ground bends over (its heights round it lower than
    // it), lit, and facing the sun's side.
    const bend = (4 * y - height(x + 1, z) - height(x - 1, z) - height(x, z + 1) - height(x, z - 1)) / 4;
    const rim = Math.min(1, Math.max(0, bend * 3)) * Math.min(1, facing / 0.25) * (1 - shade);
    into.lerp(sunColour, rim * 0.35);
    // Only far off does the ground go into the haze, so far crests still stand against the sky.
    // Far off the sand goes into the haze, until at its edge it is exactly the sky behind it at that bearing, so the
    // land's edge never shows as a line.
    const toward = r > 0 ? (x * Math.sin(sunAt) - z * Math.cos(sunAt)) / r : 0;
    hazeColour.copy(horizon).lerp(afterglow, warmAt(toward, 0));
    return into.lerp(hazeColour, Math.min(1, Math.max(0, (r - 40) / 70)) ** 2 * 0.8);
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
      // Far off the ground rises to the eye's own height, so it meets the sky exactly at the horizon all round.
      // Far off the ground rises into a soft line of distant dunes along the horizon, hazed into the sky, so the land
      // meets the sky in hills, never in a ruled line, and no sky shows under it.
      const edge = Math.min(1, Math.max(0, (r - 80) / 30)) ** 2;
      const skyline = 2.2 + 2.6 * (0.5 + 0.5 * Math.sin(a * 3 + 1.1)) * (0.55 + 0.45 * Math.sin(a * 7 + 2.4)) * (0.8 + 0.2 * Math.sin(a * 17));
      const y = height(x, z);
      positions.push(x, y + (skyline - y) * edge, z);
      sandAt(x, z, c);
      if (i === rings) rim.add(c);
      tints.push(c.r, c.g, c.b);
    }
  }
  // Near the person the sand's light is a picture of it made once, 28 cm a step, read in the ground's shader at every
  // pixel: the ring of vertices alone smears light and shadow into streaks round the person.
  const span = 36;
  const cells = 256;
  let light = new Float32Array(cells * cells * 3);
  for (let row = 0; row < cells; row++) {
    for (let col = 0; col < cells; col++) {
      sandAt(((col + 0.5) / cells) * 2 * span - span, ((row + 0.5) / cells) * 2 * span - span, c);
      light.set([c.r, c.g, c.b], (row * cells + col) * 3);
    }
  }
  // Softened twice over its neighbours (about half a metre), so no edge of light or shadow steps from cell to cell.
  for (let pass = 0; pass < 2; pass++) {
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
  const rippleAt = (u: number, v: number) =>
    Math.sin(Math.PI * 2 * (9 * u + 0.5 * Math.sin(Math.PI * 2 * v) + 0.22 * Math.sin(Math.PI * 2 * (3 * v + u)))) *
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
    shader.uniforms.sandLamp = { value: lampAt };
    shader.uniforms.sandLampColour = { value: lampColour };
    shader.uniforms.sandUnder = { value: underAt };
    shader.uniforms.sandCastFrom = { value: castFrom };
    shader.uniforms.sandCastBand = { value: castBand };
    shader.uniforms.sandSpan = { value: span };
    shader.uniforms.sandSun = { value: [Math.sin(sunAt), -Math.cos(sunAt)] };
    shader.uniforms.sandSheen = { value: [sunColour.r, sunColour.g, sunColour.b] };
    shader.vertexShader = shader.vertexShader
      .replace('void main() {', 'varying vec3 vSand;\nvoid main() {')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n  vSand = (modelMatrix * vec4(transformed, 1.0)).xyz;');
    shader.fragmentShader = shader.fragmentShader
      .replace('void main() {', 'uniform sampler2D sandNear;\nuniform sampler2D sandRipples;\nuniform vec2 sandWind;\nuniform vec3 sandLamp;\nuniform vec3 sandLampColour;\nuniform vec3 sandUnder;\nuniform vec3 sandCastFrom;\nuniform vec3 sandCastBand;\nuniform float sandSpan;\nuniform vec2 sandSun;\nuniform vec3 sandSheen;\nvarying vec3 vSand;\nvoid main() {')
      .replace(
        '#include <opaque_fragment>',
        `{
    vec2 p = vSand.xz;
    // Near the person, the sand's light from its picture, blending into the rings' own colour by its edge.
    vec2 cell = p / (2.0 * sandSpan) + 0.5;
    // Blended out in a circle well inside the picture's square, so its edge never shows as a straight line.
    float inside = 1.0 - smoothstep(0.55, 0.92, length(p) / sandSpan);
    if (inside > 0.0) outgoingLight = mix(outgoingLight, texture2D(sandNear, vec2(cell.x, cell.y)).rgb, inside);
    // Ripples across the wind, a tile 2.4 m across: lighter on the side of each that faces the sun, darker on the
    // other, faintly in shade as well; strongest at the feet and gone by 30 m.
    // Two sizes of ripple at slightly different angles, so they never repeat like a pattern.
    vec2 q = vec2(dot(p, sandWind), dot(p, vec2(-sandWind.y, sandWind.x))) / 2.4;
    vec2 q2 = vec2(q.x * 0.96 + q.y * 0.28, q.y * 0.96 - q.x * 0.28) * 0.62 + vec2(0.37, 0.71);
    vec2 slope = (texture2D(sandRipples, q).rg - 0.5) * 1.3 + (texture2D(sandRipples, q2).rg - 0.5) * 0.9;
    // And a finer one at the feet, so the sand close by stays crisp.
    vec2 q3 = vec2(q.x * 0.9 - q.y * 0.43, q.y * 0.9 + q.x * 0.43) * 2.3 + vec2(0.13, 0.52);
    slope += (texture2D(sandRipples, q3).rg - 0.5) * 0.8 * (1.0 - smoothstep(1.5, 5.0, length(p)));
    float sunAlong = dot(sandSun, sandWind);
    float sunAcross = dot(sandSun, vec2(-sandWind.y, sandWind.x));
    float rippleNear = 1.0 - smoothstep(2.5, 16.0, length(p));
    float atFeet = 1.0 - smoothstep(2.0, 7.0, length(p));
    outgoingLight *= 1.0 + rippleNear * ((0.2 + 0.12 * atFeet) * (slope.x * sunAlong + slope.y * sunAcross) + (0.06 + 0.06 * atFeet) * slope.x);
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
    outgoingLight *= 1.0 + 0.12 * grain * (1.0 - smoothstep(0.4, 1.0, fwidth(p.x * 30.0)));
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
    vec3 look = vSand - cameraPosition;
    float toward = max(0.0, dot(normalize(look.xz), sandSun));
    float graze = 1.0 - abs(normalize(look).y);
    outgoingLight += sandSheen * pow(toward, 3.0) * graze * graze * 0.22;
    // A light near the sand (a window's own), warming it in a pool that is gone within a few metres.
    float lampDistance = length(sandLamp - vSand);
    outgoingLight += sandLampColour * (0.5 / (1.0 + lampDistance * lampDistance * 1.2)) * (1.0 - smoothstep(1.2, 3.0, lampDistance));
  }
  #include <opaque_fragment>`,
      );
  };
  group.add(new Mesh(geometry, sand));

  // The sky, its under side the ground's own colour at its rim, so nothing shows where the ground ends; warmer low
  // round the sun, as a sky is just after sunset, in its own colours rather than a shape laid over it.
  rim.multiplyScalar(1 / around);
  // Fine rows, so the warm band round the sun has no steps.
  const dome = sky(top, horizon, rim, 120, middle, 72);
  const domeAt = dome.geometry.getAttribute('position');
  const domeColour = dome.geometry.getAttribute('color');
  for (let i = 0; i < domeAt.count; i++) {
    const dx = domeAt.getX(i);
    const dy = domeAt.getY(i) / 120;
    const dz = domeAt.getZ(i);
    if (dy < -0.05) continue;
    const toward = (dx * Math.sin(sunAt) - dz * Math.cos(sunAt)) / Math.max(1e-6, Math.hypot(dx, dz));
    // A wide warm band along the horizon, strongest round the sun.
    const warm = warmAt(toward, dy);
    c.setRGB(domeColour.getX(i), domeColour.getY(i), domeColour.getZ(i)).lerp(afterglow, warm);
    // A few long soft bands of high cloud, lit warm on the sun's side and dim away from it.
    const a = Math.atan2(dx, -dz);
    const band = Math.max(0, Math.sin(dy * 19 + Math.sin(a * 2 + 0.7) * 1.4)) ** 3 * Math.max(0, 1 - Math.abs(dy - 0.24) / 0.22);
    const streak = band * (0.55 + 0.45 * Math.sin(a * 5 + dy * 9)) * (0.25 + 0.75 * Math.max(0, toward));
    c.lerp(afterglow, Math.min(0.6, streak * 0.75));
    domeColour.setXYZ(i, c.r, c.g, c.b);
  }
  group.add(dome);

  // The sun itself, just touching the horizon behind the far dunes.
  const glow = glows(1, { facing: true, halo: 9, round: 3.2, core: 1 });
  group.add(glow.object);
  const sx = Math.sin(sunAt) * 100;
  const sy = 8.5;
  const sz = -Math.cos(sunAt) * 100;

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
      glow.begin();
      glow.add(sx, sy, sz, 6.4, 6.4, sunColour, 0.6 + 0.03 * Math.sin(time * 0.4));
      glow.end();
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
