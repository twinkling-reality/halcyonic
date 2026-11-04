import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';

// The starter's look as a headset wearer and the laptop camera see it. The template's three.js code runs only in a
// browser (the repository does not install the starter's dependencies), so its numbers and wiring are read here as
// the files say them; the host's check and the lane's frames show what they draw.
const template = new URL('../../../starters/iwsdk-vr/template/', import.meta.url);
const read = (path: string) => readFileSync(new URL(path, template), 'utf8');

interface Pose {
  readonly position: readonly [number, number, number];
  readonly lookAt: readonly [number, number, number];
}

const DEGREE = Math.PI / 180;

function laptopCamera(): Pose {
  const config = JSON.parse(read('iwsdk.config.json')) as { world: { render: { camera: Pose } } };
  return config.world.render.camera;
}

/** A `{ x: <n>, z: <n> }` constant of look.ts, by name. */
function islandPoint(name: string): { x: number; z: number } {
  const found = read('src/look.ts').match(
    new RegExp(`export const ${name} = \\{ x: (-?[\\d.]+), z: (-?[\\d.]+) \\} as const;`),
  );
  assert.ok(found, `look.ts exports ${name}`);
  return { x: Number(found[1]), z: Number(found[2]) };
}

describe("the starter's look faces the headset wearer (U1)", () => {
  test('the laptop camera stands just behind the wearer and looks the way they look, at the island', () => {
    // In VR the wearer stands at the XR origin, facing -z when the session starts; the laptop camera is the only
    // picture the check and the agent see, so it must look the same way or the headset shows the island's back.
    const { position, lookAt } = laptopCamera();
    const [x, y, z] = position;
    assert.ok(Math.abs(x) <= 0.5 && z > 0.5 && z <= 3.5, `behind the origin: ${position}`);
    assert.ok(y >= 1.4 && y <= 2.4, `about head height: ${y}`);
    const dx = lookAt[0] - x;
    const dy = lookAt[1] - y;
    const dz = lookAt[2] - z;
    const yaw = Math.atan2(dx, -dz);
    assert.ok(Math.abs(yaw) <= 5 * DEGREE, `facing -z within 5 degrees: ${yaw / DEGREE}`);
    const pitch = Math.atan2(-dy, Math.hypot(dx, dz));
    assert.ok(
      pitch >= 5 * DEGREE && pitch <= 25 * DEGREE,
      `looking down a little: ${pitch / DEGREE}`,
    );
    const centre = islandPoint('ISLAND_CENTRE');
    const radius = Number(read('src/look.ts').match(/export const ISLAND_RADIUS = ([\d.]+);/)?.[1]);
    assert.ok(
      Math.hypot(lookAt[0] - centre.x, lookAt[2] - centre.z) <= radius / 2,
      'at the middle of the island',
    );
  });

  test("the island keeps ART7's front toward the wearer: the stage is never turned", () => {
    const look = read('src/look.ts');
    assert.doesNotMatch(look, /stage\.rotation/);
    assert.doesNotMatch(look, /\(-4, 1\.5, -6\)/);
  });

  test('the agent is told where the player stands and which way things face', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /`PLAYER`/);
    assert.match(agents, /faces -z/);
    assert.match(agents, /toward \+z/);
    assert.doesNotMatch(agents, /in front of the camera/);
  });
});

describe("a game object stays where it is put, in the world's own frame (U3b)", () => {
  // `world.createTransformEntity(object)` with no parent moves the object to the level's root (@iwsdk/core
  // dist/ecs/world.js): an object put in an offset island group would jump by the offset when made interactive.
  test("the game's frame is the world's: the player at the origin, the island's centre in front", () => {
    assert.deepEqual(islandPoint('PLAYER'), { x: 0, z: 0 });
    const centre = islandPoint('ISLAND_CENTRE');
    assert.ok(centre.z < -1, 'the island is in front of the player');
  });

  test('gameIsland() is a group of its own on the scene, never moved or turned; the island meshes sit at its centre', () => {
    const look = read('src/look.ts');
    assert.match(
      look,
      /const play = new Group\(\);\n\s*world\.scene\.add\(play\);\n\s*built = play;/,
    );
    assert.doesNotMatch(look, /play\.(position|rotation|scale|quaternion)/);
    assert.match(look, /stage\.add\(ground\);/);
  });

  test("the decor keeps its place: it is laid out round the island's centre", () => {
    const decor = read('src/decor.ts');
    assert.match(decor, /decor\.position\.set\(ISLAND_CENTRE\.x, 0, ISLAND_CENTRE\.z\);/);
  });

  test('the agent works in world coordinates and is told what createTransformEntity does', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /world's own coordinates/);
    assert.match(agents, /`ISLAND_CENTRE`/);
    assert.match(
      agents,
      /`world\.createTransformEntity\(object\)` moves the object to the scene's root/,
    );
  });
});

describe("the starter's sky shows ART7's colours (U2)", () => {
  test('the sky shader converts its linear colours for the screen, after it mixes them', () => {
    // three.js holds the uniforms' colours linear; a ShaderMaterial writes what it computes unless it includes the
    // output conversion, and then ART7's #4FAEFF shows as #146CFF.
    const look = read('src/look.ts');
    const shader = look.slice(
      look.indexOf('fragmentShader'),
      look.indexOf('}),', look.indexOf('fragmentShader')),
    );
    const mixed = shader.indexOf('gl_FragColor = vec4(mix(horizon, top, t), 1.0);');
    const converted = shader.search(/^\s*'\s*#include <colorspace_fragment>',$/m);
    assert.ok(mixed > 0, 'the sky mixes horizon to top');
    assert.ok(
      converted > mixed,
      'then converts, on a line of its own (three.js includes only whole lines)',
    );
  });
});

describe('the world moves by itself from the first frame, by the time a system is given (U3)', () => {
  test('the look registers one system that moves the clouds, and anything else asks it', () => {
    const look = read('src/look.ts');
    assert.match(look, /class AmbientSystem extends createSystem\(\{\}\)/);
    assert.match(look, /update\(_delta: number, time: number\): void \{/);
    assert.match(look, /world\.registerSystem\(AmbientSystem\)/);
    assert.match(look, /export function ambientMotion\(/);
    assert.match(look, /drift\(time,/);
  });

  test('the decor sways in a breeze and brings butterflies, through the same system', () => {
    const decor = read('src/decor.ts');
    assert.match(decor, /ambientMotion\(/);
    assert.match(decor, /sway\(time,/);
    assert.match(decor, /flap\(time,/);
    assert.match(decor, /flight\(time,/);
    assert.match(decor, /butterflies = \d+/);
  });

  test('nothing in the look or the decor keeps a clock of its own, so the check can step it', () => {
    for (const file of ['src/look.ts', 'src/decor.ts', 'src/sound.ts', 'src/worlds.ts'])
      assert.doesNotMatch(
        read(file),
        /setTimeout|setInterval|Date\.now|performance\.now|requestAnimationFrame|new Clock/,
        file,
      );
  });
});

/** Every `[x, z, ...]` spot of decor.ts's LAYOUT, as (x, z) round the island's centre. */
function layoutSpots(): { x: number; z: number }[] {
  const decor = read('src/decor.ts');
  const start = decor.indexOf('const LAYOUT = {');
  const end = decor.indexOf('} as const;', start);
  assert.ok(start >= 0 && end > start, 'decor.ts has its LAYOUT');
  return [...decor.slice(start, end).matchAll(/\[(-?[\d.]+), (-?[\d.]+)/g)].map((found) => ({
    x: Number(found[1]),
    z: Number(found[2]),
  }));
}

describe('a fresh island comes decorated and costs a headset few draw calls (U4)', () => {
  test('a new game starts with the decor on', () => {
    const index = read('src/index.ts');
    assert.match(index, /decorate\(gameIsland\(\)\);/);
    assert.ok(index.indexOf('setupLook(world)') < index.indexOf('decorate(gameIsland())'));
  });

  test('the decor leaves the middle of the island clear for the game, 1 to 3 m in front of the player', () => {
    // Round the island's centre, the player stands at z = 1.8 (U3b): keep |x| < 0.8 and z from -1.2 to 1.2 free.
    const spots = layoutSpots();
    assert.ok(spots.length >= 20, `the layout's spots are read: ${spots.length}`);
    for (const { x, z } of spots)
      assert.ok(
        Math.abs(x) >= 0.8 || z <= -1.2 || z >= 1.2,
        `decor at (${x}, ${z}) is in the play area`,
      );
  });

  test('each prop is one mesh of merged parts, the grass one instanced mesh, each cloud one mesh', () => {
    const decor = read('src/decor.ts');
    assert.match(
      decor,
      /import \{ mergeGeometries \} from 'three\/addons\/utils\/BufferGeometryUtils\.js';/,
    );
    assert.match(decor, /new InstancedMesh\(/);
    assert.match(decor, /vertexColors = true/);
    assert.match(read('src/look.ts'), /mergeGeometries\(/);
  });

  test('the agent is told the island comes decorated and how to change it', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /comes decorated/);
    assert.match(agents, /decorate\(gameIsland\(\), \{ trees: 2, butterflies: 0 \}\)/);
  });
});

describe('the card names the game and says how to play it, nothing else (U5)', () => {
  test("a new game's card is its own: a title and a line the agent fills, never Meta's words", () => {
    const card = read('public/ui/welcome.uikitml');
    assert.doesNotMatch(card, /Hello, Immersive Web!|Build interactive browser/);
    assert.match(card, /<div id="card-title" class="heading">Untitled Game<\/div>/);
    assert.match(card, /<div id="card-line" class="sub-heading"><\/div>/);
    // The card's words still go as one, after 6 s (GQ1a).
    assert.match(card, /<div id="card-words" class="panel-content">/);
    assert.match(read('index.html'), /<title>Untitled Game<\/title>/);
  });

  test('the starter is a VR app, so its button says Enter VR', () => {
    const card = read('public/ui/welcome.uikitml');
    assert.match(card, /<ButtonLabel class="button-label">Enter VR<\/ButtonLabel>/);
    assert.doesNotMatch(card, /Enter XR/);
  });

  test('the agent names the game on its card and in its title, and adds nothing else there', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /`card-title`/);
    assert.match(agents, /`card-line`/);
    assert.match(agents, /`<title>`/);
    assert.match(agents, /nothing else on the card/);
  });
});

describe('a hit can celebrate and the score can be shown big, ready for any game (U6)', () => {
  test('confetti is one instanced mesh driven by the same system, and a score sign stands on the tier', () => {
    const decor = read('src/decor.ts');
    // Since U7 a hit takes its time from the game's clock itself.
    assert.match(
      decor,
      /export function celebrate\(at: \{ x: number; y: number; z: number \}\): void/,
    );
    assert.match(decor, /export function scoreSign\(/);
    assert.match(decor, /confettiPiece\(time - /);
    assert.match(decor, /pulse\(time - /);
    assert.match(decor, /texture\.colorSpace = SRGBColorSpace;/);
  });

  test('the agent is asked to celebrate a hit and show the score on the sign, with the time it is given', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /`celebrate\(/);
    assert.match(agents, /`scoreSign\(/);
    assert.match(agents, /`bonk` it/);
  });
});

describe('the page shows the sky while the game loads, never a white page (U6b)', () => {
  test("the page's own background is ART7's sky, behind the canvas", () => {
    const page = read('index.html');
    assert.match(page, /html,\s*body \{[^}]*height: 100%;/);
    assert.match(page, /background: linear-gradient\(#4FAEFF, #D8F0FF\);/);
    assert.match(read('src/palette.ts'), /sky: '#4FAEFF'/);
    assert.match(read('src/palette.ts'), /cloud: '#D8F0FF'/);
  });

  test("the canvas stays clear over it until the look's first frame, and then shows, once", () => {
    // The SDK draws its canvas black before setupLook has built anything; held clear, the page's sky shows instead.
    assert.match(read('index.html'), /#scene-container canvas \{\s*opacity: 0;\s*\}/);
    const look = read('src/look.ts');
    assert.match(look, /renderer\.domElement\.style\.opacity = '1';/);
    assert.ok(
      look.indexOf("style.opacity = '1'") < look.indexOf('world.registerSystem(AmbientSystem)'),
    );
  });
});

describe("a hit's time is the game's own, so a press handler cannot freeze it (U7)", () => {
  // S1's agent passed performance.now() to bonk, celebrate and the sign from an onSelect handler, which is given no
  // time: measured against the game's clock, its bonked moles never finished and no confetti ever showed.
  test('the look keeps the time its system was last given, for handlers, which are given none', () => {
    const look = read('src/look.ts');
    assert.match(look, /export function gameTime\(\): number/);
    assert.match(look, /latest = time;/);
  });

  test('confetti and the score sign take the time from the game, and ask for none', () => {
    const decor = read('src/decor.ts');
    assert.match(decor, /time: gameTime\(\)/);
    assert.match(decor, /show\(value: number \| string\): void/);
    assert.match(decor, /changedAt = gameTime\(\);/);
  });

  test('the agent is told to use gameTime() in handlers, never a clock of its own, and how big to make targets', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /`gameTime\(\)`/);
    assert.match(agents, /never\s+`performance\.now\(\)` or `Date\.now\(\)`/);
    assert.match(agents, /`celebrate\(position\)`/);
    assert.match(agents, /`score\.show\(points\)`/);
    assert.match(agents, /at least 30 cm across/);
    assert.match(agents, /anywhere in the project/);
  });
});

describe("a toy kit of friendly things, so a game's things read at a glance (U8)", () => {
  // The first two showcase rehearsals built their moles, stars and basket from bare spheres and cylinders.
  test('the kit makes a critter, a star, a balloon and a basket, each one mesh', () => {
    const decor = read('src/decor.ts');
    for (const name of ['critter', 'toyStar', 'balloon', 'basket'])
      assert.match(
        decor,
        new RegExp(
          `export function ${name}\\(colour: PaletteName \\| string = '\\w+'\\): Mesh \\{`,
        ),
        name,
      );
    // A critter has eyes with pupils and a nose: never a bare sphere.
    const critter = decor.slice(
      decor.indexOf('export function critter'),
      decor.indexOf('export function toyStar'),
    );
    assert.match(critter, /'ink'/);
    assert.match(critter, /'berry'/);
    // Extruded shapes have no index; the merge takes every piece to the same form.
    assert.match(decor, /toNonIndexed\(\)/);
  });

  test('the agent is asked to start from the kit, at about 30 cm, scaled whole', () => {
    const agents = read('AGENTS.md');
    for (const call of [
      "critter('cocoa')",
      "toyStar('sunny')",
      "balloon('berry')",
      "basket('tangerine')",
    ])
      assert.ok(agents.includes(`\`${call}\``), call);
    assert.match(agents, /toy kit/);
  });
});

describe('the agent writes every file with its file-editing tool (U9)', () => {
  test('AGENTS.md asks for every file to be written and changed with the file-editing tool, and claims nothing about a temporary folder', () => {
    const agents = read('AGENTS.md').replace(/\s+/g, ' ');
    const cannot = agents.slice(
      agents.indexOf('What you cannot do here:'),
      agents.indexOf('How the app is put together:'),
    );
    assert.match(
      cannot,
      /Write and change every file with your file-editing tool, never through the shell\./,
    );
    // A confined server has a temporary folder of its own (ADR 0041, CONFINED-TMP): no line says otherwise (AUDIT U9).
    assert.doesNotMatch(agents, /temporary folder|here.document|operation not permitted/);
  });
});

describe('a game can be set in a world of its own, so two games never read as one place (W1)', () => {
  // The three showcase games of 2026-11-04 all kept the starter's island, sky and decor, and read as one world.
  const worlds = read('src/worlds.ts');
  const names = ['meadow', 'garden', 'seaside', 'night'];

  test('the starter offers the meadow and three other worlds, each its own sky, light, ground, dressing and sound', () => {
    assert.match(worlds, /export const worlds = \{ meadow, garden, seaside, night \} as const;/);
    for (const name of names) {
      const start = worlds.indexOf(`const ${name}: WorldLook = {`);
      assert.ok(start >= 0, name);
      const body = worlds.slice(start, worlds.indexOf('\n};', start));
      for (const part of ['sky:', 'light:', 'ground:', `dressing: '${name}'`, 'sound:'])
        assert.ok(body.includes(part), `${name} has ${part}`);
    }
    // The meadow is the starter's own, so a game that keeps it draws the same picture as before (DRAWN's reference).
    const meadow = worlds.slice(
      worlds.indexOf('const meadow'),
      worlds.indexOf('\n};', worlds.indexOf('const meadow')),
    );
    assert.match(
      meadow,
      /sky: \{ top: palette\.sky, horizon: palette\.cloud, clouds: '#FFFFFF' \}/,
    );
    assert.match(meadow, /top: palette\.leaf,/);
    assert.doesNotMatch(meadow, /middle:|disc:|stars:|sea:|hills:/);
  });

  test('setupLook takes the world, the meadow when none is named, and decorate dresses the island for it', () => {
    const look = read('src/look.ts');
    assert.match(
      look,
      /export function setupLook\(world: World, setting: WorldName \| WorldLook = 'meadow'\): \{ island: Group \}/,
    );
    assert.match(look, /stage\.add\(plain \? meadowSky\(look\.sky\) : skyDome\(look\.sky\)\);/);
    assert.match(look, /startAmbience\(look\.sound, ambientMotion\);/);
    const decor = read('src/decor.ts');
    for (const dressing of ['garden', 'seaside', 'night'])
      assert.match(
        decor,
        new RegExp(`function ${dressing}Dressing\\(decor: Group, counts: DecorCounts\\): void`),
      );
    assert.match(decor, /const dressing = currentWorld\(\)\.dressing;/);
  });

  test("the new skies convert their colours for the screen after mixing, as the meadow's does", () => {
    const look = read('src/look.ts');
    const dome = look.slice(look.indexOf('function skyDome'), look.indexOf('function stars'));
    const mixed = dome.indexOf('gl_FragColor = vec4(c, 1.0);');
    assert.ok(mixed > 0 && dome.indexOf("'#include <colorspace_fragment>'") > mixed);
  });

  test('the sound waits for a press and needs no file', () => {
    const sound = read('src/sound.ts');
    assert.match(sound, /navigator\.userActivation\?\.hasBeenActive/);
    assert.doesNotMatch(sound, /fetch\(|new Audio\(|decodeAudioData|Math\.random/);
  });

  test('the agent is told to pick the world that fits, or make one, and the meadow is what every game shares', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /Set the game in a world of its own/);
    assert.match(agents, /`setupLook\(world, 'night'\)`/);
    for (const name of ['garden', 'seaside', 'night'])
      assert.match(agents, new RegExp(`- \`${name}\`: `));
    assert.match(agents, /make one of your own from another/);
    // The pristine starter keeps the meadow, so the check's reference picture is the starter's own.
    assert.match(read('src/index.ts'), / {2}setupLook\(world\);/);
  });
});

describe('a whole small game in AGENTS.md, to start from, importing from one place (V1)', () => {
  // A VR version's local model imported contactShadow from the wrong file and misused createSystem, failed its first
  // check, and ran a fix-up past 58 minutes (DEMO's rehearsal, 2026-11-04).
  test('the example registers its system by class and imports only names src/kit.ts gives', () => {
    const agents = read('AGENTS.md');
    const start = agents.indexOf('```ts\n');
    const example = agents.slice(start, agents.indexOf('\n```', start + 6));
    assert.ok(start > 0, 'AGENTS.md holds a ts example');
    assert.match(example, /class GameSystem extends createSystem\(\{\}\)/);
    assert.match(example, /world\.registerSystem\(GameSystem\);/);
    assert.doesNotMatch(example, /new GameSystem/);
    const imported = example.match(/import \{([^}]+)\} from '\.\/kit\.js';/);
    assert.ok(imported, "the example imports from './kit.js'");
    const kit = read('src/kit.ts');
    for (const name of (imported[1] ?? '')
      .split(',')
      .map((n) => n.trim())
      .filter((n) => n !== ''))
      assert.match(kit, new RegExp(`\\b${name}\\b`), name);
  });
});
