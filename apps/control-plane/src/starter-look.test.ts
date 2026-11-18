import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { READING_HIDDEN_POINTS } from '@halcyonic/contracts';
import {
  arcSlot,
  DRIFT,
  driftPose,
  FACE_POINTS,
  FAR_OFF,
  firstOf,
  HEADSET_PAGES,
  IN_FRONT,
  inView,
  kitPanels,
  LAPTOP_PAGES,
  PAGE_HEIGHT,
  PAGE_WIDTH,
  PAGES_DISTANCE,
  PAGES_EYE,
  PRESS_FORWARD,
  PRESS_GROW,
  pagesAcross,
  panelOn,
  ROW_GAP,
  ROW_HEIGHT,
  ROWS_FACE,
  ROWS_TOP,
  screenBox,
  segmentPixels,
  VIEW_AIM,
  VIEW_BACK,
  VIEW_CAMERA,
  ViewDrift,
  wordedMeshes,
  wordsOn,
} from '../../../starters/iwsdk-vr/template/src/legible.ts';
import { fitText } from '../../../starters/iwsdk-vr/template/src/page-fit.ts';
import { PAGE_MS } from './preview/check.ts';
import { TWIN_FRAMES } from './preview/laptop-probe.ts';

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
    const mixed = shader.indexOf('vec3 c = mix(horizon, top, t);');
    const written = shader.indexOf('gl_FragColor = vec4(c, 1.0);');
    const converted = shader.search(/^\s*'\s*#include <colorspace_fragment>',$/m);
    assert.ok(mixed > 0, 'the sky mixes horizon to top');
    assert.ok(written > mixed, 'and writes that mix, its haze and dither added');
    assert.ok(
      converted > written,
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
      /import \{ mergeGeometries, mergeVertices \} from 'three\/addons\/utils\/BufferGeometryUtils\.js';/,
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

  test("the agent keeps the project's name on its card and in its title, and adds nothing else there", () => {
    // The host writes the project's name into both when it makes the project (starters.ts namedFile); an agent told to
    // name the app would put its own name over the person's (AUDIT2, 2026-11-17).
    const agents = read('AGENTS.md').replace(/\s+/g, ' ');
    assert.match(
      agents,
      /`card-title` \(`public\/ui\/welcome\.uikitml`\) and `index\.html`'s `<title>` already hold the project's name, as the person gave it\. Keep that name; change it only when the person asks for a new one\./,
    );
    assert.match(agents, /`card-line`, with nothing else on the card/);
    assert.doesNotMatch(agents, /name it on its welcome card|Name the app on its welcome card/);
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
    // The meadow is the starter's own: ART7's sky and grass, with no sun disc, stars, sea or sunset band; since the lead's
    // starter look (2026-11-18) it stands on land with far hills, grounded as every land world is.
    const meadow = worlds.slice(
      worlds.indexOf('const meadow'),
      worlds.indexOf('\n};', worlds.indexOf('const meadow')),
    );
    assert.match(
      meadow,
      /sky: \{ top: palette\.sky, horizon: palette\.cloud, clouds: '#FFFFFF' \}/,
    );
    assert.match(meadow, /top: palette\.leaf,/);
    assert.doesNotMatch(meadow, /middle:|disc:|stars:|sea:/);
    assert.match(meadow, /plain: \[/);
    assert.match(meadow, /hills: \[/);
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

describe("a view from another app keeps its own look, not the island's (VRIFY)", () => {
  // VRIFY: a VR version of an app that is not a game (a sky with planets, a terrain model) was asked for a game on
  // the toy island, the only look the starter could set up.
  const look = () => read('src/look.ts');
  const setupScene = () => {
    const text = look();
    const start = text.indexOf('export function setupScene(');
    assert.ok(start > 0, 'look.ts exports setupScene');
    return text.slice(start, text.indexOf('\n}\n', start));
  };

  test('setupScene shows the canvas and keeps the clock, with no island, sky, lights or sound of the starter', () => {
    const body = setupScene();
    assert.match(body, /built = root;/, 'gameIsland() returns its group');
    assert.match(
      body,
      /world\.registerSystem\(AmbientSystem\);/,
      'gameTime() and motion keep their clock',
    );
    assert.match(
      body,
      /renderer\.domElement\.style\.opacity = '1';/,
      'the canvas shows from the first frame',
    );
    // The SDK's environment system clears scene.background (VRIFY's first lab check showed the page behind): a sphere.
    assert.doesNotMatch(body, /scene\.background =/);
    assert.match(body, /new SphereGeometry\(180,/);
    assert.match(body, /side: BackSide/);
    for (const starterPart of [
      'island()',
      'skyDome',
      'meadowSky',
      'Light(',
      'startAmbience',
      'cloud(',
    ])
      assert.ok(!body.includes(starterPart), `no ${starterPart}`);
    assert.match(read('src/kit.ts'), /\bsetupScene,/);
  });

  test("the agent is told when to use it, to rebuild the original's own look, and to give it one press that answers", () => {
    const agents = read('AGENTS.md');
    const start = agents.indexOf('A view from another app, not a game:');
    assert.ok(start > 0, 'AGENTS.md has the section');
    const section = agents.slice(start, agents.indexOf('\n\n', start));
    assert.match(section, /`setupScene\(world, '#000000'\)`/);
    assert.match(section, /instead of `setupLook` and\s+`decorate`/);
    // VRIFY's first run on a big astronomy app read the 700 MB original for 75 minutes, looking for a game, and wrote nothing.
    assert.match(section, /Find the view fast, and do not read the whole original/);
    assert.match(section, /rg -l "from 'three'"/);
    assert.match(section, /Rebuild the look from the original's own code/);
    assert.match(section, /Never import from the original's folder/);
    assert.match(section, /within 150 m/);
    assert.match(section, /one thing to press that answers at once: `onSelect`/);
    // The laptop camera's far plane is 200 m: a sky sphere past it would not draw.
    const config = JSON.parse(read('iwsdk.config.json')) as { world: { render: { far: number } } };
    assert.ok(config.world.render.far > 150, `far plane ${config.world.render.far}`);
  });
});

describe('a whole small view in AGENTS.md, to start a view from another app from (VRIFY)', () => {
  // VRIFY's re-run on that app passed the check with a sky on a lit material and no light: the frame was a blue dot on
  // black. The worked view shows a picture sky that needs no light and a lit subject with its own picture.
  const view = () => {
    const agents = read('AGENTS.md');
    const start = agents.indexOf('```ts\n', agents.indexOf('A whole small view, to start from'));
    assert.ok(start > 0, 'AGENTS.md holds a view example');
    return agents.slice(start, agents.indexOf('\n```', start + 6));
  };

  test('it sets up the scene, a picture sky with no light, a lit subject, and one press that answers', () => {
    const example = view();
    assert.match(example, /setupScene\(world, '#[0-9a-f]{6}'\);/);
    assert.doesNotMatch(example, /setupLook|decorate|scoreSign/);
    assert.match(example, /new MeshBasicMaterial\(\{ map: skyPicture, side: BackSide \}\)/);
    assert.match(example, /new DirectionalLight\(/);
    assert.match(example, /new MeshStandardMaterial\(\{ map: surface/);
    assert.match(example, /onSelect\(this\.world\.createTransformEntity\(this\.planet\)/);
    assert.match(example, /world\.registerSystem\(ViewSystem\);/);
  });

  test('it imports only names src/kit.ts gives', () => {
    const imported = view().match(/import \{([^}]+)\} from '\.\/kit\.js';/);
    assert.ok(imported, "the example imports from './kit.js'");
    const kit = read('src/kit.ts');
    for (const name of (imported[1] ?? '')
      .split(',')
      .map((n) => n.trim())
      .filter((n) => n !== ''))
      assert.match(kit, new RegExp(`\\b${name}\\b`), name);
  });
});

describe('land comes into VR as a relief table from its own heights (VRIFY)', () => {
  // VRIFY's run on a map app passed the check with its ground as a flat picture on a card turning edge-on: the planet of the
  // worked view does not fit land. relief() raises real ground from the original's own height picture.
  test('relief reads a grey or Terrarium height picture, raises the ground, and colours it by height without a picture', () => {
    const relief = read('src/relief.ts');
    assert.match(
      relief,
      /export function relief\(heights: Texture \| HeightGrid, options: ReliefOptions = \{\}\): Group/,
    );
    assert.match(relief, /r \* 256 \+ g \+ b \/ 256 - 32768/, "Terrarium's metres");
    assert.match(
      relief,
      /position\.setY\(i, 0\.004 \+ t \* height\)/,
      'just over the base, never fighting it',
    );
    // VRIFY's second run on it took a picture of elevation bands for heights; its own are a float32 grid.
    assert.match(relief, /export interface HeightGrid \{/);
    assert.match(relief, /computeVertexNormals\(\)/);
    assert.match(relief, /vertexColors: picture === null/);
    // No clock of its own and nothing from another site: the check steps it, and the preview is offline.
    assert.doesNotMatch(relief, /performance\.now|Date\.now|https?:/);
    assert.match(read('src/kit.ts'), /\brelief \} from '\.\/relief\.js';/);
  });

  test('the agent is told to use it for land, with the encoding its original names, and no picture that does not fit', () => {
    const agents = read('AGENTS.md');
    const start = agents.indexOf('A view from another app, not a game:');
    const section = agents.slice(start, agents.indexOf('\n\n', start));
    assert.match(
      section,
      /For land .* the subject is a relief table: `relief\(heights, options\)`/s,
    );
    assert.match(section, /`'terrarium'` \(R \* 256 \+ G \+ B \/ 256 - 32768\)/);
    assert.match(
      section,
      /follow its code to\s+that file and its metadata, never a picture of bands or classes/,
    );
    assert.match(section, /new Float32Array\(await \(await fetch\(url\)\)\.arrayBuffer\(\)\)/);
    // The same run draped a land-cover map, whose colours are class codes, and the ground read as blue noise.
    assert.match(section, /never a data image whose\s+colours are codes/);
  });
});

describe("a web page's own cards come into VR as pages of their own (VRIFY unit 2)", () => {
  // A lesson, steps or a deck of cards is not one striking view: the worked view showed a share picture on a card.
  // pages() lays the original's own cards out as readable panels in front of the person, and a press answers.
  test('pages draws each card, its title, its words wrapped and its picture, on a canvas texture of its own', () => {
    const pages = read('src/pages.ts');
    assert.match(
      pages,
      /export function pages\(list: readonly Page\[\], options: PagesOptions = \{\}\): Pages/,
    );
    assert.match(pages, /export interface Page \{/);
    assert.match(pages, /readonly title: string;/);
    assert.match(pages, /readonly text: string;/);
    assert.match(pages, /readonly picture\?: /);
    assert.match(pages, /new CanvasTexture\(/);
    assert.match(pages, /colorSpace = SRGBColorSpace/);
    assert.match(pages, /needsUpdate = true/);
    assert.match(pages, /measureText\(/, 'the words are wrapped to the panel');
    assert.match(pages, /drawImage\(/, 'a picture is drawn on its page');
  });

  test('a press answers on the game clock: show(i) brings that page forward and lights it, with no timer of its own', () => {
    const pages = read('src/pages.ts');
    assert.match(pages, /show\(index: number\): void;/);
    assert.match(pages, /ambientMotion\(/);
    assert.match(pages, /gameTime\(\)/);
    assert.doesNotMatch(
      pages,
      /setTimeout|setInterval|Date\.now|performance\.now|requestAnimationFrame/,
    );
  });

  test('the kit lays out at most six pages at a time, so the arc stays readable (AUDIT pages SHOULD), and turns to the rest', () => {
    const pages = read('src/pages.ts');
    assert.equal(HEADSET_PAGES, 6);
    assert.match(pages, /export const MAX_PAGES = HEADSET_PAGES;/);
    assert.match(pages, /const panels = Array\.from\(\{ length: MAX_PAGES \}/);
    assert.match(pages, /const slot = arcSlot\(index, first, across, items\.length, distance\);/);
    assert.match(
      pages,
      /const page = slot\.page === null \? undefined : items\[slot\.page\]\?\.page;/,
    );
    assert.match(read('AGENTS.md'), /six stand at a time in the headset, three on a laptop/);
  });

  test('the kit gives pages, and the agent is told when a web page becomes pages and how to press one', () => {
    assert.match(
      read('src/kit.ts'),
      /export \{ type Page, type Pages, type PagesOptions, MAX_PAGES, pages, TYPE \} from '\.\/pages\.js';/,
    );
    const agents = read('AGENTS.md');
    assert.match(agents, /pages\(list/);
    assert.match(agents, /onSelect\(world\.createTransformEntity\(panel\)/);
    assert.match(agents, /show\(i\)/);
  });
});

describe("an app's use comes whole, read to its end and readable (the lead, 2026-11-13T08:20Z)", () => {
  // A hosted Make VR Version of a field guide carried its use but dropped its search and three of a sighting's five
  // fields, cut its log at six rows with "and N more", drew small words, and said none of it where a person reads.
  test('the kit pages a long list six at a time and draws its words on its own type scale', () => {
    const pages = read('src/pages.ts');
    assert.match(pages, /export const TYPE = \{ title: 0\.04, words: 0\.026 \} as const;/);
    assert.match(pages, /turn\(by: 1 \| -1\): boolean;/);
    assert.match(pages, /readonly first: number;/);
    assert.match(pages, /readonly count: number;/);
    // A search or a filter shows what it leaves in the same row; a hidden panel takes no press.
    assert.match(pages, /set\(list: readonly Page\[\]\): void;/);
    assert.match(pages, /panel\.scale\.setScalar\(panel\.visible \? 1 \+ GROW \* near : 0\)/);
    // The last, partial group stands centred, and the row turns by as many as stand at a time.
    assert.deepEqual(
      [0, 1, 2].map((index) => arcSlot(index, 12, 6, 14, PAGES_DISTANCE)),
      [
        { page: 12, angle: -(PAGE_WIDTH + 0.06) / PAGES_DISTANCE / 2 },
        { page: 13, angle: (PAGE_WIDTH + 0.06) / PAGES_DISTANCE / 2 },
        { page: null, angle: (1.5 * (PAGE_WIDTH + 0.06)) / PAGES_DISTANCE },
      ],
    );
    assert.match(pages, /const next = first \+ by \* across;/);
    // The pages draw on that scale, never sizes of their own.
    assert.match(pages, /Math\.round\(TYPE\.title \* PIXELS\)/);
    assert.match(pages, /Math\.round\(TYPE\.words \* PIXELS\)/);
    assert.doesNotMatch(pages, /\d+px \$\{font\}/);
    assert.doesNotMatch(pages, /pass the ones that matter most/);
    assert.match(read('src/kit.ts'), /MAX_PAGES, pages, TYPE \} from '\.\/pages\.js';/);
  });

  test('AGENTS.md says how each part of the use works with a hand, and that what is left out is said on the card', () => {
    const agents = read('AGENTS.md');
    const has = (pattern: RegExp) => assert.match(agents, pattern);
    has(/An app's use, not a game/);
    has(/Nothing of the use is dropped without a word/);
    // The owner rejected two VR versions built as the same cards and an on-screen keyboard (2026-11-18): no keyboard.
    has(
      /Never show a keyboard of letters: a field that needs typed words is named among what this\s+version leaves out/,
    );
    assert.doesNotMatch(agents, /keyboard of letter buttons/);
    has(/write a short plan headed "What this app is in VR"/);
    has(/Cards or a row of panels are never the main layout/);
    has(/Each VR version looks and feels like its own app/);
    has(/Never fix such a field to a sample value or to now without saying so/);
    // A words critic: long text, files and sign-ins would push an agent to build editors or fake a form.
    has(
      /Long text to write \(steps, an article\)\s+and files to upload are named among what this version leaves out/,
    );
    has(/An account, a sign-in or a server is never rebuilt, not even its form/);
    has(/`turn\(1\)` and `turn\(-1\)`/);
    has(/`set\(list\)` shows what a search or\s+a filter leaves/);
    has(/never cut it with "and N more" and no way there/);
    has(/A card's words that do not fit go on to the\s+next card, never cut off/);
    has(
      /Words in the scene are read at a distance: draw them no smaller than `TYPE` from `\.\/kit\.js`/,
    );
    has(/a title never runs into the words beside\s+or below it/);
    // What is not carried yet is honest but out of the main view, in a panel the person opens (the lead, 2026-11-18).
    has(
      /When this version leaves something out, what it leaves out goes in a small panel the person opens with a press\s+labelled "Not Here Yet", never in the main view, and in your closing words; a code comment is not saying it\. When it\s+carries all of the app, show no such press\. Never call anything in it a stand-in or a demo\./,
    );
    has(/pass all of them/);
    assert.doesNotMatch(agents, /pass up to six/);
  });
});

describe("a page's words go on to the next page, never cut off (AUDIT2, 2026-11-13)", () => {
  // AUDIT2 blocked the words unit: the words promised it while the pages cut a card's words with " ...".
  // Every character ten units wide: a line of 100 holds ten characters.
  const measure = (text: string) => text.length * 10;

  test('one long text becomes two pages, the words in order and none lost, paragraphs kept', () => {
    const text = 'aaaa bbbb cccc dddd\neeee ffff gggg hhhh';
    const pages = fitText(text, 100, measure, 2, 3);
    assert.deepEqual(pages, ['aaaa bbbb cccc dddd', 'eeee ffff gggg hhhh']);
    assert.deepEqual(fitText('aaaa bbbb cccc dddd eeee ffff', 100, measure, 1, 1), [
      'aaaa bbbb',
      'cccc dddd',
      'eeee ffff',
    ]);
    // Words that fit stay one page, the very text given.
    assert.deepEqual(fitText(text, 100, measure, 4, 4), [text]);
    // A first page with no room for words (its picture takes it) leaves them all to the next.
    assert.deepEqual(fitText('aaaa bbbb', 100, measure, 0, 4), ['', 'aaaa bbbb']);
  });

  test('pages() lays out what the words take and draws no ellipsis', () => {
    const pages = read('src/pages.ts');
    assert.match(pages, /fitText\(/);
    assert.doesNotMatch(pages, / \.\.\.`/);
    assert.doesNotMatch(pages, /stop with an ellipsis/);
    assert.match(pages, /items = laid\(next\);/);
    // A press maps back to the agent's own entry, which a long entry's pages share (AUDIT2 S1).
    assert.match(pages, /source\(index: number\): number \| undefined;/);
    assert.match(
      pages,
      /return panels\[index\]\?\.visible \? items\[first \+ index\]\?\.source : undefined;/,
    );
    assert.match(
      read('AGENTS.md'),
      /Map a pressed panel back to your own entry with `book\.source\(i\)`, never\s+`first \+ i`/,
    );
    // A title past two lines is marked as cut (AUDIT2 S3).
    assert.match(pages, /if \(all\.length > 2\) titles\[1\] =/);
    // Cards an agent draws itself split their words the same way.
    assert.match(read('src/kit.ts'), /export \{ fitText \} from '\.\/page-fit\.js';/);
    assert.match(read('AGENTS.md'), /for cards you draw, `fitText` from `\.\/kit\.js` splits/);
  });
});

describe('what Make VR Version builds reads on a laptop, and the kit says what it draws (VRIFY, 2026-11-13)', () => {
  // The laptop camera of iwsdk.config.json stands behind the player for a game; a view's pages, 1.6 m from the person,
  // were seen from 3.8 m by it, and their words came to about 6 CSS pixels at the check's 1280 by 800.
  const VIEW = { width: 1280, height: 800 } as const;
  /** IWSDK 1.0.1's field of view when the config names none (`cameraFov` in its world initializer). */
  const DEFAULT_FOV = 50;
  type V = readonly [number, number, number];
  const sub = (a: V, b: V): V => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
  const dot = (a: V, b: V) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const cross = (a: V, b: V): V => [
    a[1] * b[2] - a[2] * b[1],
    a[2] * b[0] - a[0] * b[2],
    a[0] * b[1] - a[1] * b[0],
  ];
  const unit = (a: V): V => {
    const n = Math.hypot(...a);
    return [a[0] / n, a[1] / n, a[2] / n];
  };
  /** A point in normalized device coordinates, as three.js projects it through a camera at `pose` (z left out). */
  const project = (pose: Pose, point: V) => {
    const forward = unit(sub(pose.lookAt as V, pose.position as V));
    const right = unit(cross(forward, [0, 1, 0]));
    const up = cross(right, forward);
    const v = sub(point, pose.position as V);
    const depth = dot(v, forward);
    const tan = Math.tan(((DEFAULT_FOV / 2) * Math.PI) / 180);
    return {
      x: dot(v, right) / (depth * tan * (VIEW.width / VIEW.height)),
      y: dot(v, up) / (depth * tan),
      z: depth > 0 ? 0 : 2,
    };
  };
  /** How high words `height` metres tall at `at` stand on the laptop's screen, as the kit's hook measures them. */
  const wordPixels = (pose: Pose, at: V, height: number) => {
    const foot = project(pose, at);
    assert.ok(inView(foot), `in view: ${JSON.stringify(foot)}`);
    return segmentPixels(foot.y, project(pose, [at[0], at[1] + height, at[2]]).y, VIEW.height);
  };
  const words = () =>
    Number(read('src/pages.ts').match(/TYPE = \{ title: [\d.]+, words: ([\d.]+) \}/)?.[1]);
  const middlePage: V = [0, PAGES_EYE, -PAGES_DISTANCE];
  /** The camera a view is seen from on a laptop: the one setupScene places, else the config's. */
  const viewCamera = (): Pose => {
    const look = read('src/look.ts');
    const body = look.slice(look.indexOf('export function setupScene('));
    const placed =
      body.includes('world.camera.position.set(...VIEW_CAMERA.position);') &&
      body.includes('world.camera.lookAt(...VIEW_CAMERA.lookAt);');
    return placed ? VIEW_CAMERA : laptopCamera();
  };

  test("the config names no field of view, so IWSDK's 50 degrees is the laptop's", () => {
    const config = JSON.parse(read('iwsdk.config.json')) as { world: { render: { fov?: number } } };
    assert.equal(config.world.render.fov, undefined);
  });

  test("a view's pages, seen from the game's camera behind the player, read under 10 px: the cause", () => {
    const px = wordPixels(laptopCamera(), middlePage, words());
    assert.ok(px < 7 && px > 5, `about 6 px: ${px}`);
  });

  test("a view is seen on a laptop from a step behind the person's place, so the words of the pages read at 10 px and over (K4)", () => {
    assert.deepEqual(VIEW_CAMERA, {
      position: [0, 1.6, VIEW_BACK],
      lookAt: [0, VIEW_AIM, -PAGES_DISTANCE],
    });
    assert.deepEqual([VIEW_BACK, VIEW_AIM], [0.4, 1.2]);
    const px = wordPixels(viewCamera(), middlePage, words());
    assert.ok(px >= 10 && px < 12, `${px} px`);
    // Each page a laptop shows reads as well, the farthest from the camera the smallest, and the rows' words too.
    for (const count of [1, 2, 3])
      for (let index = 0; index < count; index += 1) {
        const { angle } = arcSlot(index, 0, LAPTOP_PAGES, count, PAGES_DISTANCE);
        const at: V = [
          Math.sin(angle) * PAGES_DISTANCE,
          PAGES_EYE,
          -Math.cos(angle) * PAGES_DISTANCE,
        ];
        assert.ok(wordPixels(viewCamera(), at, words()) >= 10, `${count}: ${angle}`);
      }
    for (let row = 0; row < 5; row += 1) {
      const { centre, up } = rowAt(row);
      const foot = project(viewCamera(), centre);
      const head = project(viewCamera(), [
        centre[0],
        centre[1] + up[1] * 0.04,
        centre[2] + up[2] * 0.04,
      ]);
      const label = segmentPixels(foot.y, head.y, VIEW.height);
      assert.ok(label >= 10, `row ${row}: ${label} px`);
    }
    assert.match(read('src/rows.ts'), /const LABEL_PX = Math\.round\(TYPE\.title \* PIXELS\);/);
    assert.match(read('src/rows.ts'), /wordsOn\(mesh, TYPE\.title, 'row'\);/);
  });

  /** Row `row` of `rows()`: its centre, at the pages' distance, and its own up, tipped back to face the person's eye. */
  const rowAt = (row: number): { centre: V; up: V } => {
    const y = ROWS_TOP - ROW_HEIGHT / 2 - row * (ROW_HEIGHT + ROW_GAP);
    const tilt = Math.atan2(ROWS_FACE - y, PAGES_DISTANCE);
    return { centre: [0, y, -PAGES_DISTANCE], up: [0, Math.cos(tilt), -Math.sin(tilt)] };
  };
  /** The box on the 1280 by 800 screen round corners at `centre`, `w` by `h` metres along `right` and `up`. */
  const boxAt = (centre: V, right: V, up: V, w: number, h: number, pose: Pose = viewCamera()) =>
    screenBox(
      [
        [-1, -1],
        [1, -1],
        [1, 1],
        [-1, 1],
      ].map(([sx = 0, sy = 0]) =>
        project(pose, [
          centre[0] + (right[0] * sx * w) / 2 + (up[0] * sy * h) / 2,
          centre[1] + (right[1] * sx * w) / 2 + (up[1] * sy * h) / 2,
          centre[2] + (right[2] * sx * w) / 2 + (up[2] * sy * h) / 2,
        ]),
      ),
      0,
      0,
      VIEW.width,
      VIEW.height,
    );
  /** Page `index` of a row of `count` on a laptop, at rest or pressed: nearer by `PRESS_FORWARD` and bigger. */
  const pageBox = (index: number, count: number, pressed: boolean, pose: Pose = viewCamera()) => {
    const { angle } = arcSlot(index, 0, LAPTOP_PAGES, count, PAGES_DISTANCE);
    const near = pressed ? PRESS_FORWARD : 0;
    const scale = pressed ? 1 + PRESS_GROW : 1;
    const centre: V = [
      Math.sin(angle) * (PAGES_DISTANCE - near),
      PAGES_EYE,
      -Math.cos(angle) * (PAGES_DISTANCE - near),
    ];
    return boxAt(
      centre,
      [Math.cos(angle), 0, Math.sin(angle)],
      [0, 1, 0],
      PAGE_WIDTH * scale,
      PAGE_HEIGHT * scale,
      pose,
    );
  };
  /** Inside the view, as the check reads it: no more than 2 px past any edge. */
  const inside = (box: { x: number; y: number; w: number; h: number }) =>
    box.x >= -2 &&
    box.y >= -2 &&
    box.x + box.w <= VIEW.width + 2 &&
    box.y + box.h <= VIEW.height + 2;
  /** A keyboard's row of ten letters at the narrowest button: about as wide as an app's rows come. */
  const KEYS_WIDE = 10 * 0.1 + 9 * 0.02;

  test("the starter's pages, a pressed page and five rows under them sit inside the laptop's 1280 by 800 view", () => {
    for (const count of [1, 2, 3])
      for (let index = 0; index < count; index += 1) {
        assert.ok(inside(pageBox(index, count, false)), `page ${index} of ${count}`);
        assert.ok(inside(pageBox(index, count, true)), `pressed page ${index} of ${count}`);
      }
    // The three at rest stand clear of each other, as the check's 4 px reads it.
    for (const [a, b] of [
      [0, 1],
      [1, 2],
    ] as const)
      assert.ok(
        pageBox(a, 3, false).x + pageBox(a, 3, false).w < pageBox(b, 3, false).x,
        `${a} ${b}`,
      );
    // The rows: under a pressed page's reach, clear of each other, and inside the view, five of them, a keyboard wide.
    const reach = Math.max(
      ...[0, 1, 2].map((index) => pageBox(index, 3, true).y + pageBox(index, 3, true).h),
    );
    let above = reach;
    for (let row = 0; row < 5; row += 1) {
      const { centre, up } = rowAt(row);
      const box = boxAt(centre, [1, 0, 0], up, KEYS_WIDE, ROW_HEIGHT);
      assert.ok(inside(box), `row ${row}: ${JSON.stringify(box)}`);
      assert.ok(box.y > above + 4, `row ${row} clear of what is above it: ${box.y} after ${above}`);
      above = box.y + box.h;
    }
    // A sixth row would go past the bottom: the kit says five fit.
    const { centre, up } = rowAt(5);
    assert.equal(inside(boxAt(centre, [1, 0, 0], up, KEYS_WIDE, ROW_HEIGHT)), false);
    assert.match(read('AGENTS.md'), /Five rows fit\./);
  });

  // The laptop view's drift (SHOW, 2026-11-16): a slow arc round the pages and a small dolly back on game time, on a
  // laptop only, stopped for good by the person's first drag, key or press and never moving in a headset.
  const forwardOf = (pose: Pose): V => unit(sub(pose.lookAt as V, pose.position as V));
  const angleBetween = (a: V, b: V) => Math.acos(Math.min(1, Math.max(-1, dot(a, b))));
  /** Every 10 ms of the drift's own seconds, its end included. */
  const driftTimes = () =>
    Array.from({ length: Math.round(DRIFT.seconds * 100) + 1 }, (_, i) => i / 100);
  /** Where the drift stands farthest from VIEW_CAMERA: the far end of its arc, half way through. */
  const farEnd = () => driftPose(DRIFT.seconds / 2);
  /** What the check takes at rest from `pose`: the words' heights of three pages and five rows, and their boxes. */
  const reports = (pose: Pose) => {
    const heights: number[] = [];
    const boxes: ReturnType<typeof screenBox>[] = [];
    for (let index = 0; index < 3; index += 1) {
      const { angle } = arcSlot(index, 0, LAPTOP_PAGES, 3, PAGES_DISTANCE);
      const at: V = [
        Math.sin(angle) * PAGES_DISTANCE,
        PAGES_EYE,
        -Math.cos(angle) * PAGES_DISTANCE,
      ];
      heights.push(wordPixels(pose, at, words()));
      boxes.push(pageBox(index, 3, false, pose));
    }
    for (let row = 0; row < 5; row += 1) {
      const { centre, up } = rowAt(row);
      const foot = project(pose, centre);
      const head = project(pose, [centre[0], centre[1] + up[1] * 0.04, centre[2] + up[2] * 0.04]);
      heights.push(segmentPixels(foot.y, head.y, VIEW.height));
      boxes.push(boxAt(centre, [1, 0, 0], up, KEYS_WIDE, ROW_HEIGHT, pose));
    }
    return { heights, boxes };
  };

  test('the drift starts exactly at VIEW_CAMERA and comes back to it, still, before the check looks for a still screen', () => {
    assert.deepEqual(driftPose(0), VIEW_CAMERA);
    assert.deepEqual(driftPose(DRIFT.seconds), VIEW_CAMERA);
    assert.deepEqual(driftPose(DRIFT.seconds + 100), VIEW_CAMERA);
    // Back before the twin loads' first frame (60 a second) and long before the laptop step's still control pair.
    assert.ok(DRIFT.seconds < TWIN_FRAMES[0] / 60 - 0.5, `${DRIFT.seconds} s`);
    assert.ok(DRIFT.seconds < PAGE_MS / 1000 / 2);
  });

  test('the drift turns at most 3 degrees a second with no roll, a few degrees of arc and a small dolly', () => {
    let fastest = 0;
    let widest = 0;
    let farthest = 0;
    const home = forwardOf(VIEW_CAMERA);
    const times = driftTimes();
    for (let i = 1; i < times.length; i += 1) {
      const before = times[i - 1] ?? 0;
      const now = times[i] ?? 0;
      const turn = angleBetween(forwardOf(driftPose(before)), forwardOf(driftPose(now)));
      fastest = Math.max(fastest, turn / DEGREE / (now - before));
      widest = Math.max(widest, angleBetween(home, forwardOf(driftPose(now))) / DEGREE);
      farthest = Math.max(
        farthest,
        Math.hypot(...sub(driftPose(now).position as V, VIEW_CAMERA.position)),
      );
      // It always looks at the same point, the camera's up the world's, so it never rolls.
      assert.deepEqual(driftPose(now).lookAt, VIEW_CAMERA.lookAt);
    }
    assert.ok(fastest <= 3, `${fastest} degrees a second`);
    assert.ok(widest >= 1 && widest <= 4, `${widest} degrees`);
    assert.ok(farthest <= 0.15, `${farthest} m`);
    const dolly =
      Math.hypot(...sub(farEnd().position as V, farEnd().lookAt as V)) -
      Math.hypot(...sub(VIEW_CAMERA.position, VIEW_CAMERA.lookAt));
    assert.ok(dolly > 0 && dolly <= 0.1, `back ${dolly} m`);
    assert.equal(DRIFT.arc, 3);
    assert.ok(DRIFT.back <= 0.1);
  });

  test("at the arc's far end, and every frame of it, the pages, a pressed page and five rows stay inside the view and read at 10 px", () => {
    for (const pose of [farEnd(), ...driftTimes().map((t) => driftPose(t))]) {
      for (const count of [1, 2, 3])
        for (let index = 0; index < count; index += 1) {
          assert.ok(inside(pageBox(index, count, false, pose)), `page ${index} of ${count}`);
          assert.ok(inside(pageBox(index, count, true, pose)), `pressed page ${index} of ${count}`);
        }
      const { heights, boxes } = reports(pose);
      for (const px of heights) assert.ok(px >= 10, `${px} px`);
      for (const box of boxes) assert.ok(inside(box), JSON.stringify(box));
    }
  });

  test("the check's reading and fit are the same with and without the drift: taken at VIEW_CAMERA while it moves (D1)", () => {
    const still = reports(VIEW_CAMERA);
    const drift = new ViewDrift();
    // Never stepped (a game, or before the first frame): read through the camera as it stands.
    assert.equal(drift.restPose(), null);
    for (const t of driftTimes()) {
      const drawn = drift.step(t, false);
      assert.ok(drawn !== null);
      if (t < DRIFT.seconds) assert.deepEqual(drift.restPose(), VIEW_CAMERA);
      assert.deepEqual(reports(drift.restPose() ?? drawn), still, `at ${t} s`);
    }
    // The drawn frame is not VIEW_CAMERA half way, so what keeps the reports the same is the rest pose.
    assert.notDeepEqual(reports(farEnd()), still);
    // The kit's hook reads and boxes through the camera put at the rest pose, then put back as it stood.
    const layer = read('src/play-input.ts');
    const readingsBody = layer.slice(layer.indexOf('function readings('));
    assert.match(readingsBody, /^[\s\S]*?\{\n\s+return atRest\(w, \(\) => \{/);
    const panelsBody = layer.slice(layer.indexOf('function panels('));
    assert.match(panelsBody, /^[\s\S]*?\{\n\s+return atRest\(w, \(\) => \{/);
    assert.match(
      layer,
      /const pose = viewDrift\.restPose\(\);\n\s+if \(pose === null\) return read\(\);/,
    );
    assert.match(
      layer,
      /camera\.position\.set\(\.\.\.pose\.position\);\n\s+camera\.lookAt\(\.\.\.pose\.lookAt\);/,
    );
    assert.match(
      layer,
      /camera\.position\.copy\(standing\);\n\s+camera\.quaternion\.copy\(facing\);/,
    );
  });

  test('the drift never moves a camera the game placed itself: placed away from VIEW_CAMERA, it stands down for good', () => {
    const placed = new ViewDrift();
    assert.equal(placed.step(0, false, { position: [0, 1.3, 0.2], lookAt: [0, 1.05, -1.1] }), null);
    for (let t = 0.1; t < 10; t += 0.5) assert.equal(placed.step(t, false), null, `${t}`);
    assert.equal(placed.restPose(), null, 'read through the camera the game placed');
    const kept = new ViewDrift();
    assert.deepEqual(
      kept.step(0, false, VIEW_CAMERA),
      VIEW_CAMERA,
      'a camera still at VIEW_CAMERA drifts',
    );
  });

  test('after a stop the drift never moves the camera again, and once back home it lets the camera be', () => {
    const drift = new ViewDrift();
    assert.deepEqual(
      drift.step(10, false),
      VIEW_CAMERA,
      'its first frame is VIEW_CAMERA, whatever the game time',
    );
    const moved = drift.step(10 + DRIFT.seconds / 4, false);
    assert.ok(moved !== null);
    assert.notDeepEqual(moved, VIEW_CAMERA);
    drift.stop();
    for (let t = 10.5; t < 40; t += 0.25) assert.equal(drift.step(t, false), null, `${t}`);
    assert.equal(drift.restPose(), null, 'read through the camera where it stopped');
    drift.stop();
    assert.equal(drift.step(50, false), null);
    const ended = new ViewDrift();
    ended.step(0, false);
    assert.deepEqual(
      ended.step(DRIFT.seconds + 0.01, false),
      VIEW_CAMERA,
      'its last frame lands home',
    );
    for (let t = DRIFT.seconds + 0.02; t < 30; t += 0.5) assert.equal(ended.step(t, false), null);
    // The look moves the camera only from the drift's own pose, on the game's time, and only while it gives one.
    const look = read('src/look.ts');
    const body = look.slice(look.indexOf('export function setupScene('));
    assert.match(
      body,
      /ambientMotion\(\(time\) => \{[\s\S]*?\n\s+const pose = viewDrift\.step\(time, inHeadset\(\) \|\| renderer\.xr\.isPresenting, placed\);\n\s+if \(pose === null\) return;\n\s+world\.camera\.position\.set\(\.\.\.pose\.position\);\n\s+world\.camera\.lookAt\(\.\.\.pose\.lookAt\);/,
    );
    // A game keeps its camera: only a view drifts.
    assert.equal(look.split('viewDrift.').length, 2);
    assert.doesNotMatch(
      look.slice(
        look.indexOf('export function setupLook('),
        look.indexOf('export function setupScene('),
      ),
      /viewDrift/,
    );
  });

  test('in a presenting session nothing moves, and never again after one began', () => {
    const drift = new ViewDrift();
    drift.step(0, false);
    assert.equal(drift.step(0.5, true), null);
    for (let t = 0.6; t < 20; t += 0.3) assert.equal(drift.step(t, false), null, `${t}`);
    assert.equal(drift.restPose(), null);
    const headset = new ViewDrift();
    assert.equal(headset.step(0, true), null, 'a session from the first frame never drifts');
    assert.equal(headset.step(1, false), null);
  });

  test("from the old camera, at the person's own place, the side pages already reached past the edge: the cause", () => {
    const old: Pose = { position: [0, 1.6, 0], lookAt: [0, PAGES_EYE, -PAGES_DISTANCE] };
    const { angle } = arcSlot(0, 0, 3, 3, PAGES_DISTANCE);
    const corner = project(old, [
      Math.sin(angle) * PAGES_DISTANCE - Math.cos(angle) * (PAGE_WIDTH / 2),
      PAGES_EYE + PAGE_HEIGHT / 2,
      -Math.cos(angle) * PAGES_DISTANCE - Math.sin(angle) * (PAGE_WIDTH / 2),
    ]);
    const px = ((corner.x + 1) / 2) * VIEW.width;
    assert.ok(px < -2, `${px} px`);
    // And six of them, as it laid out, reach far past both edges.
    assert.ok(Math.abs(arcSlot(0, 0, 6, 6, PAGES_DISTANCE).angle) > (40 * Math.PI) / 180);
  });

  test('six pages on a laptop show three, in a headset six; the row turns by as many and keeps the page in view', () => {
    assert.deepEqual([pagesAcross(false), pagesAcross(true)], [LAPTOP_PAGES, HEADSET_PAGES]);
    assert.deepEqual([LAPTOP_PAGES, HEADSET_PAGES], [3, 6]);
    const shows = (headset: boolean) =>
      Array.from(
        { length: HEADSET_PAGES },
        (_, index) => arcSlot(index, 0, pagesAcross(headset), 6, PAGES_DISTANCE).page,
      );
    assert.deepEqual(shows(false), [0, 1, 2, null, null, null]);
    assert.deepEqual(shows(true), [0, 1, 2, 3, 4, 5]);
    // Leaving the headset on pages 6 to 11 stands the laptop's three at 6; entering on 9 stands the six at 6.
    assert.equal(firstOf(6, 3), 6);
    assert.equal(firstOf(9, 6), 6);
    assert.equal(firstOf(4, 3), 3);
    const pages = read('src/pages.ts');
    assert.match(
      pages,
      /onHeadset\(\(headset\) => \{\n\s+across = pagesAcross\(headset\);\n\s+first = firstOf\(first, across\);\n\s+lay\(\);/,
    );
    assert.match(pages, /get across\(\) \{\n\s+return across;/);
    // The session is the SDK's own: the world's visibility, as mode() reads it, followed on entering and leaving.
    const look = read('src/look.ts');
    assert.match(look, /session\.visibilityState\.subscribe\(/);
    assert.match(look, /state !== VisibilityState\.NonImmersive/);
    assert.match(look.slice(look.indexOf('export function setupScene(')), /session = world;/);
  });

  test('each kit panel says where it stands, and the hook boxes them, sealed with the rest (K5)', () => {
    const pages = read('src/pages.ts');
    assert.match(
      pages,
      /panelOn\(panel, \{ kind: 'page', label: page\?\.title \?\? null, width: WIDTH, height: HEIGHT \}\);/,
    );
    assert.match(pages, /const WIDTH = PAGE_WIDTH;/);
    assert.match(pages, /const FORWARD = PRESS_FORWARD;/);
    assert.match(pages, /const GROW = PRESS_GROW;/);
    const rows = read('src/rows.ts');
    assert.match(
      rows,
      /panelOn\(anchor, \{ kind: 'row', label: words\[0\] \?\? null, width: whole, height: ROW_HEIGHT \}\);/,
    );
    assert.match(
      rows,
      /onSelect\(world\.createTransformEntity\(mesh\), \(\) => button\.onSelect\(\)\);/,
    );
    assert.match(rows, /const tilt = -Math\.atan2\(ROWS_FACE - y, PAGES_DISTANCE\);/);
    assert.doesNotMatch(rows, /setTimeout|setInterval|Date\.now|performance\.now|addEventListener/);
    assert.match(
      read('src/kit.ts'),
      /\nexport \{ type Button, type Rows, type RowsOptions, rows \} from '\.\/rows\.js';\n$/,
    );
    const layer = read('src/play-input.ts');
    const hook = layer.slice(layer.indexOf("Object.defineProperty(globalThis, '__playCheck'"));
    assert.match(
      hook,
      /^[\s\S]*?readings: \(\) => readings\(w\),\n\s+panels: \(\) => panels\(w\),\n\s+\}\),\n\s+writable: false,\n\s+configurable: false,/,
    );
    assert.match(layer, /for \(const \[held, panel\] of kitPanels\(\)\)/);
    assert.match(layer, /screenBox\(corners, rect\.left, rect\.top, rect\.width, rect\.height\)/);
    const registered = {};
    panelOn(registered, { kind: 'row', label: 'Back', width: 1, height: 0.08 });
    assert.deepEqual(kitPanels().get(registered), {
      kind: 'row',
      label: 'Back',
      width: 1,
      height: 0.08,
    });
  });

  test('a box round a panel the camera cannot see whole is far off the screen, never drawn through it', () => {
    const at = (x: number, y: number, z = 0) => ({ x, y, z });
    assert.deepEqual(screenBox([at(-1, 1), at(1, 1), at(1, -1), at(-1, -1)], 0, 0, 1280, 800), {
      x: 0,
      y: 0,
      w: 1280,
      h: 800,
    });
    assert.deepEqual(screenBox([at(0, 0), at(0.5, 0, 1.5)], 0, 0, 1280, 800), {
      x: -FAR_OFF,
      y: -FAR_OFF,
      w: FAR_OFF,
      h: FAR_OFF,
    });
    const huge = screenBox([at(-100, 0), at(100, 0)], 0, 0, 1280, 800);
    assert.ok(huge.x === -FAR_OFF && huge.w === FAR_OFF, JSON.stringify(huge));
  });

  test('the agent places every row through rows() and is told the laptop view is checked', () => {
    const agents = read('AGENTS.md').replace(/\s+/g, ' ');
    assert.ok(
      agents.includes(
        "- Every row of buttons, tabs, a pager or a keyboard goes through `rows(...)` from `./kit.js`: it places the rows under the pages, spaced by the kit, inside what a laptop shows and clear of a page while it is pressed. Never place a row of your own. The person's computer checks the laptop view at 1280 by 800 and holds it back when part of a panel is off its screen or covers another.",
      ),
    );
    assert.ok(
      agents.includes(
        '`pages()` lays out as many as the view holds (six in the headset, three on a laptop): `turn(1)` and `turn(-1)` turn by that many, and `count` and `first` say where the person is; read them',
      ),
    );
    assert.ok(agents.includes('never work it out from six'));
    assert.ok(agents.includes('`MAX_PAGES` is the most it lays out, in the headset'));
    assert.doesNotMatch(agents, /`MAX_PAGES` at a time|lays out six at a time|7 to 12 of 14/);
    // The rows' place comes after the pages' list, as the lead asked.
    assert.ok(
      agents.indexOf('- Every row of buttons') >
        agents.indexOf('- Every list can be read to its end'),
    );
  });

  test("setupScene places the laptop camera after the config's; setupLook keeps the camera", () => {
    const look = read('src/look.ts');
    const scene = look.slice(look.indexOf('export function setupScene('));
    assert.match(scene, /world\.camera\.position\.set\(\.\.\.VIEW_CAMERA\.position\);/);
    assert.match(scene, /world\.camera\.lookAt\(\.\.\.VIEW_CAMERA\.lookAt\);/);
    const game = look.slice(
      look.indexOf('export function setupLook('),
      look.indexOf('export function setupScene('),
    );
    assert.doesNotMatch(game, /world\.camera/);
    // The game's camera stays the config's, just behind the player.
    assert.deepEqual(laptopCamera(), { position: [0, 1.85, 2.2], lookAt: [0, 0.8, -1.8] });
  });

  test('the pages stand where the camera looks, and each panel says its words are TYPE.words high', () => {
    const pages = read('src/pages.ts');
    assert.match(pages, /const EYE = PAGES_EYE;/);
    assert.match(pages, /const distance = options\.distance \?\? PAGES_DISTANCE;/);
    assert.match(pages, /wordsOn\(panel, TYPE\.words, 'page'\);/);
    // The score sign's label: 54 px on its 512 px canvas, 1.2 m wide.
    const decor = read('src/decor.ts');
    assert.match(decor, /context\.font = `700 54px \$\{font\}`;/);
    assert.match(decor, /canvas\.width = 512;/);
    assert.match(decor, /new PlaneGeometry\(1\.2, 0\.6\)/);
    assert.match(decor, /wordsOn\(face, \(54 \* 1\.2\) \/ 512, 'sign'\);/);
  });

  test('the registry keeps what the kit drew, and needs no SDK', () => {
    assert.doesNotMatch(read('src/legible.ts'), /^import /m);
    const mesh = {};
    wordsOn(mesh, 0.026, 'sign');
    assert.deepEqual(wordedMeshes().get(mesh), { height: 0.026, kind: 'sign' });
    assert.equal(inView({ x: 0, y: 0, z: 0.5 }), true);
    assert.equal(inView({ x: 1.01, y: 0, z: 0.5 }), false);
    assert.equal(inView({ x: 0, y: 0, z: 1.5 }), false, 'behind the camera');
    assert.equal(segmentPixels(-0.01, 0.01, 800), 8);
  });

  test("the check hook reports the words' heights through the laptop camera", () => {
    const layer = read('src/play-input.ts');
    assert.match(layer, /readings: \(\) => readings\(w\),/);
    assert.match(layer, /for \(const \[mesh, \{ height, kind \}\] of wordedMeshes\(\)\)/);
    assert.match(layer, /if \(!inView\(foot\)\) continue;/);
    assert.match(layer, /\.project\(w\.camera\)/);
    assert.match(layer, /return \{ texts \};/);
  });

  test('the hook also says how many of five points on each text stand behind something, from the same camera (AUDIT3 H1, H5)', () => {
    const layer = read('src/play-input.ts');
    assert.match(layer, /kind, hidden: hiddenPoints\(w, object\) \}/);
    assert.match(layer, /w\.camera\.getWorldPosition\(eye\);/);
    assert.match(layer, /for \(const \[sx, sy\] of FACE_POINTS\)/);
    // Only a shown mesh other than the text's own covers it; a panel under the camera (the controls card) never does.
    assert.match(
      layer,
      /\(hit\.object as Mesh\)\.isMesh === true &&\s+shown\(hit\.object\) &&\s+!inside\(hit\.object, text\) &&\s+!inside\(hit\.object, w\.camera\)/,
    );
    assert.equal(
      FACE_POINTS.length,
      READING_HIDDEN_POINTS,
      'the host counts the points the kit casts',
    );
    assert.ok(IN_FRONT > 0 && IN_FRONT < 0.02, 'the score sign stands 2 cm proud of its frame');
    for (const [file, kind] of [
      ['src/pages.ts', 'page'],
      ['src/rows.ts', 'row'],
      ['src/decor.ts', 'sign'],
    ] as const)
      assert.match(read(file), new RegExp(`wordsOn\\(.*, '${kind}'\\);`), file);
  });
});

describe('a whole small piano of light in AGENTS.md, to start a piece of music and light from (SHOW)', () => {
  // SHOW, 2026-11-14: an agent asked for music and light finds every part, in a place that is not the island.
  const piano = () => {
    const agents = read('AGENTS.md');
    const start = agents.indexOf(
      '```ts\n',
      agents.indexOf('A whole small piano of light, to start'),
    );
    assert.ok(start > 0, 'AGENTS.md holds a piano example');
    return agents.slice(start, agents.indexOf('\n```', start + 6));
  };

  test('it plays a score from the kit onto a keyboard of light in a place, never the island', () => {
    const example = piano();
    assert.match(example, /setupScene\(world, '#[0-9a-f]{6}'\);/);
    assert.doesNotMatch(example, /setupLook|decorate|scoreSign/);
    assert.match(example, /darkHall\(\)/);
    assert.match(example, /playScore\(programme, ambientMotion/);
    assert.match(example, /fallingNotes\(player, keys/);
  });

  test('the hall round the person answers the notes and builds to a bloom, in place of the far haze (SHOW)', () => {
    const example = piano();
    assert.match(example, /const rise = 20;/);
    assert.match(example, /const around = lightHall\(player, \{ rise \}\);/);
    assert.match(example, /root\.add\(around\.group\);/);
    assert.match(example, /ambientMotion\(around\.move\);/);
    assert.match(
      example,
      /fallingNotes\(player, keys, \{ from: 'around', echo: false, floor: -1\.15, bloom: rise \}\)/,
    );
    // The floor it stands on is polished: light under it is seen in it.
    assert.match(
      read('src/places.ts'),
      /ground\.material as MeshBasicMaterial\)\.depthWrite = false;/,
    );
  });

  test('it imports only names src/kit.ts gives', () => {
    const imported = piano().match(/import \{([^}]+)\} from '\.\/kit\.js';/);
    assert.ok(imported, "the example imports from './kit.js'");
    const kit = read('src/kit.ts');
    for (const name of (imported[1] ?? '')
      .split(',')
      .map((n) => n.trim())
      .filter((n) => n !== ''))
      assert.match(kit, new RegExp(`\\b${name}\\b`), name);
  });
});

describe('tasks add to one app in parts of their own (VRIFY)', () => {
  // Three tasks on one project each wrote their thing into src/index.ts, and no two of them could be added together
  // (Critter Row's swarm, 2026-11-17). A part is a file of its own in src/parts/ that the build finds by itself.
  test('the build finds every file in src/parts/ and runs each with the world, after the setup', () => {
    const parts = read('src/parts.ts');
    assert.match(parts, /import\.meta\.glob<[^>]+>\('\.\/parts\/\*\.ts', \{ eager: true \}\)/);
    assert.match(parts, /Object\.keys\(found\)\.sort\(\)/);
    assert.match(parts, /must export by default a function that takes the world/);
    assert.match(read('src/kit.ts'), /export \{ addParts, type Part \} from '\.\/parts\.js';/);
    const index = read('src/index.ts');
    assert.match(
      index,
      /addParts\(world\);\n\}\);\n$/,
      "the starter's own setup runs the parts last",
    );
  });

  test('every whole example in AGENTS.md runs the parts last, and the part example changes no other file', () => {
    const agents = read('AGENTS.md');
    const blocks = [...agents.matchAll(/```ts\n([\s\S]*?)\n```/g)].map((m) => m[1] ?? '');
    const wholes = blocks.filter((block) => block.includes('World.create('));
    assert.equal(wholes.length, 3, 'the game, the view and the piano of light');
    for (const whole of wholes) {
      assert.match(whole, /addParts\(world\);\n\}\);$/);
      assert.match(whole, /import \{[^}]*\baddParts\b[^}]*\} from '\.\/kit\.js';/);
    }
    const part = blocks.find((block) => block.includes('export default function'));
    assert.ok(part, 'AGENTS.md holds a part example');
    assert.doesNotMatch(part, /World\.create\(|from '\.\/kit\.js'/);
    assert.match(part, /export default function \w+\(world: World\): void \{/);
    const imported = part.match(/import \{([^}]+)\} from '\.\.\/kit\.js';/);
    assert.ok(imported, "a part imports from '../kit.js'");
    const kit = read('src/kit.ts');
    for (const name of (imported[1] ?? '')
      .split(',')
      .map((n) => n.trim())
      .filter((n) => n !== ''))
      assert.match(kit, new RegExp(`\\b${name}\\b`), name);
    assert.match(agents, /Never import a part from `src\/index\.ts` or list it anywhere\./);
    // A part that names a time of day asked the person whether to change the whole app's world (Critter Row 4's
    // flowers, "at night", 2026-11-17): the notes now say to choose that world itself, and never to rename the app.
    assert.match(
      agents.replace(/\s+/g, ' '),
      /When the person's words name a time of day or a world for what you add \(at night, by the sea\), choose that world in `src\/index\.ts` yourself as the whole app's look, .*; never rename the app for it\./,
    );
    // The loading colours combine only when every task writes the same ones: the world's own top and horizon (AUDIT2).
    const night = read('src/worlds.ts');
    assert.match(night, /top: '#0D1240'/);
    assert.match(night, /horizon: '#3E3A86'/);
    assert.match(agents, /`linear-gradient\(#0D1240, #3E3A86\)` for `night`/);
    assert.match(
      agents.replace(/\s+/g, ' '),
      /A part added to a game leaves the name, the card and the title as they are\./,
    );
  });
});

describe('?seat shows the laptop view as the headset will see it (VRIFY)', () => {
  // The owner judged a piano from the app's own laptop camera, a step behind the bench, and it did not feel like VR
  // (2026-11-18). ?seat on the preview's address puts the laptop view at the person's own place, over the app's own camera.
  test('the seat is the person standing at the origin, 1.6 m up, facing -z; only ?seat asks for it, never in a headset', async () => {
    const { SEAT, seatPose, ViewDrift } = await import(
      '../../../starters/iwsdk-vr/template/src/legible.ts'
    );
    assert.deepEqual(SEAT, { position: [0, 1.6, 0], lookAt: [0, 1.6, -1] });
    assert.deepEqual(seatPose('?seat', false), SEAT);
    assert.deepEqual(seatPose('?check&seat=1', false), SEAT);
    assert.equal(seatPose('', false), null);
    assert.equal(seatPose('?seats', false), null);
    assert.equal(
      seatPose('?seat', true),
      null,
      "a presenting session's head pose is the headset's own",
    );
    // Placed at the seat before the drift's first frame, the drift stands down there, as for any app's own camera.
    assert.equal(new ViewDrift().step(0, false, SEAT), null);
  });

  test("both setups move the camera to the seat on the first frame, after the app's own setup and before the drift", () => {
    const look = read('src/look.ts');
    const hook = look.slice(
      look.indexOf('function seatOnFirstFrame('),
      look.indexOf('\n}\n', look.indexOf('function seatOnFirstFrame(')),
    );
    assert.match(
      hook,
      /ambientMotion\(/,
      'it runs on a frame, so after the app placed its own camera',
    );
    assert.match(hook, /seatPose\(\s*typeof location === 'undefined' \? '' : location\.search,/);
    assert.match(
      hook,
      /world\.camera\.position\.set\(\.\.\.pose\.position\);\s*world\.camera\.lookAt\(\.\.\.pose\.lookAt\);/,
    );
    const scene = look.slice(look.indexOf('export function setupScene('));
    assert.ok(scene.indexOf('seatOnFirstFrame(world);') > 0, 'setupScene asks for the seat');
    assert.ok(
      scene.indexOf('seatOnFirstFrame(world);') < scene.indexOf('let first = true;'),
      "the seat's hook runs before the drift's first frame, so the drift stands down at the seat",
    );
    const lookSetup = look.slice(
      look.indexOf('export function setupLook('),
      look.indexOf('export function setupScene('),
    );
    assert.ok(lookSetup.includes('seatOnFirstFrame(world);'), 'setupLook asks for the seat');
  });
});

describe("the starter's worlds are lit and finished like a shipped scene (the lead's starter look, 2026-11-18)", () => {
  // JUDGE's bar (.private references, not shipped): light with a key, a tone-mapped image and sky light on materials;
  // depth from haze; finish with no bare toy primitive and no flat water. Read from the files, as above.
  const look = () => read('src/look.ts');
  const setup = () =>
    look().slice(
      look().indexOf('export function setupLook'),
      look().indexOf('export function setupScene'),
    );

  test("setupLook tone-maps like film, lights every material by the world's own sky, and hazes the horizon", () => {
    const body = setup();
    assert.match(body, /renderer\.toneMapping = NeutralToneMapping;/);
    assert.match(body, /renderer\.toneMappingExposure = look\.light\.exposure \?\? 1;/);
    assert.match(body, /world\.scene\.environment = skyLight\(renderer, look\);/);
    assert.match(body, /new FogExp2\(new Color\(look\.sky\.horizon\), haze\)/);
    const light = look().slice(
      look().indexOf('function skyLight'),
      look().indexOf('export function setupLook'),
    );
    assert.match(light, /new PMREMGenerator\(renderer\)/);
    assert.match(light, /pmrem\.fromScene\(scene, /, 'the sky is blurred once, at setup');
    assert.match(
      light,
      /look\.light\.sunAt/,
      'the sun in the sky light is where the key light shines from',
    );
  });

  test('setupScene keeps a view from another app as it is: no tone mapping, sky light or haze of the starter', () => {
    const scene = look().slice(look().indexOf('export function setupScene'));
    assert.match(scene, /renderer\.toneMapping = NoToneMapping;/);
    assert.match(scene, /world\.scene\.environment = null;/);
    assert.match(scene, /world\.scene\.fog = null;/);
  });

  test('the sea is water that ripples and mirrors the sky, and takes no shadow', () => {
    const sea = look().slice(look().indexOf('function sea('), look().indexOf('function hills('));
    assert.match(sea, /waterMaterial\(/);
    assert.match(sea, /ambientMotion\(\(time\) => surfaceMaterial\.move\(time\)\)/);
    assert.match(sea, /surface\.receiveShadow = false;/);
    const surface = read('src/surface.ts');
    assert.match(surface, /roughness: 0\.05/);
    assert.match(surface, /normalMap: map/);
  });

  test('the grass worlds grow grass, the seaside none, as one instanced mesh moved by the GPU', () => {
    const worlds = read('src/worlds.ts');
    for (const name of ['meadow', 'garden', 'night']) {
      const world = worlds.slice(
        worlds.indexOf(`name: '${name}'`),
        worlds.indexOf('dressing:', worlds.indexOf(`name: '${name}'`)),
      );
      assert.match(
        world,
        /grass: \{ colour: '#[0-9A-F]{6}', tip: '#[0-9A-F]{6}', dry: '#[0-9A-F]{6}' \}/,
        name,
      );
    }
    const seaside = worlds.slice(
      worlds.indexOf("name: 'seaside'"),
      worlds.indexOf('dressing:', worlds.indexOf("name: 'seaside'")),
    );
    assert.doesNotMatch(seaside, /grass:/);
    assert.match(look(), /if \(ground\.grass !== undefined\) \{\n\s+const carpet = grassCarpet\(/);
    const grass = read('src/grass.ts');
    assert.match(grass, /new InstancedMesh\(grassBlade\(\), material, options\.count\)/);
    assert.match(grass, /uniform float grassTime;/, 'the wind is in the vertex shader');
    const move = grass.slice(
      grass.indexOf('move(t: number): void {'),
      grass.indexOf('},', grass.indexOf('move(t: number): void {')),
    );
    assert.doesNotMatch(move, /setMatrixAt/, 'nothing per blade on the CPU each frame');
  });

  test('every land world is grounded: land to the horizon fading into its haze, hills rising out of it, a turfed tier', () => {
    const worlds = read('src/worlds.ts');
    for (const name of ['meadow', 'garden', 'night']) {
      const world = worlds.slice(
        worlds.indexOf(`name: '${name}'`),
        worlds.indexOf('dressing:', worlds.indexOf(`name: '${name}'`)),
      );
      assert.match(world, /plain: \['#[0-9A-F]{6}', '#[0-9A-F]{6}'\]/, name);
      assert.match(world, /hills: \['#[0-9A-F]{6}', '#[0-9A-F]{6}'\]/, name);
    }
    const land = look().slice(
      look().indexOf('function plainLand('),
      look().indexOf('function hills('),
    );
    assert.match(
      land,
      /\.lerp\(far, /,
      'far off, the land takes the horizon colour, so it meets the sky in haze',
    );
    assert.match(
      look(),
      /hill\.translate\(Math\.cos\(a\) \* d, PLAIN_LEVEL - 0\.9, Math\.sin\(a\) \* d\);/,
    );
    assert.doesNotMatch(
      look(),
      /lipOfTurf/,
      'the tier has no dish lip; its bank rounds over into the lawn',
    );
    const fields = look().slice(
      look().indexOf('function plainLand('),
      look().indexOf('function farClumps('),
    );
    assert.match(
      fields,
      /new RingGeometry\(0\.5, 38, 160, 120\)/,
      'a dense mesh, so the fields are drawn where they lie',
    );
    assert.match(
      fields,
      /if \(r > 4\.5 && edge < 0\.75\) c\.lerp\(b, 0\.6\)\.multiplyScalar\(0\.55\);/,
      'hedgerows between the fields',
    );
  });

  test('the land is one ground: flush with the island, mottled, hazed toward the horizon, with far clumps on it', () => {
    assert.match(look(), /const PLAIN_LEVEL = -0\.035;/);
    const land = look().slice(
      look().indexOf('function plainLand('),
      look().indexOf('function farClumps('),
    );
    assert.match(
      land,
      /c\.lerp\(far, Math\.min\(1, Math\.max\(0, \(r - 10\) \/ 40\)\) \*\* 1\.1\);/,
    );
    const clumps = look().slice(
      look().indexOf('function farClumps('),
      look().indexOf('function hills('),
    );
    assert.match(
      clumps,
      /new InstancedMesh\(tree, material, count \+ 30\)/,
      'far clumps cost one draw call',
    );
    assert.match(
      clumps,
      /new CylinderGeometry\(0\.08, 0\.12, 0\.7, 6\)/,
      'each clump stands on a trunk',
    );
    assert.doesNotMatch(
      look(),
      /new CircleGeometry\(0\.9, 48\)/,
      'no pale worn mat in front of the player',
    );
    const decor = read('src/decor.ts');
    assert.match(
      decor,
      /const bark = new Mesh\(trunk, withGrain\(toyMaterial\('#FFFFFF', \{ ground: true/,
      'bark, not a tube',
    );
    assert.match(
      decor,
      /\{ natural: true, roughness: 0\.78 \}\);/,
      'a starfish is matte with relief',
    );
    assert.match(
      look(),
      /c \+= glowColour \* glowStrength \* \(0\.55 \* exp\(-a \/ \(discSize \* 3\.5\)\) \+ 0\.16 \* exp\(-a \/ 0\.4\)\)/,
      'the sun has a halo as the moon does',
    );
  });

  test('clouds are soft cards lit by the sky, and the night is lit by its moon over little fill', () => {
    const clouds = look().slice(
      look().indexOf('function cloud('),
      look().indexOf('/** A small deterministic'),
    );
    assert.match(clouds, /new Sprite\(/);
    assert.match(clouds, /fog: true/, 'far clouds take the haze like the land');
    assert.doesNotMatch(clouds, /SphereGeometry/, 'no ball clusters');
    const night = read('src/worlds.ts').slice(read('src/worlds.ts').indexOf("name: 'night'"));
    assert.match(night, /fill: 0\.5,/);
    assert.match(night, /exposure: 0\.72,/);
  });

  test('flowers, mushrooms and crystals are natural forms: petals, gilled caps and shards, no beads or gems', () => {
    const decor = read('src/decor.ts');
    assert.match(decor, /part\(petal\(0\.05, 0\.03\), colour/);
    assert.match(decor, /part\(capShape\(0\.055, 0\.042\), cap/);
    assert.doesNotMatch(decor, /new OctahedronGeometry\(0\.08, 0\), 'grape'/);
    assert.match(decor, /part\(shard\(0\.045, 0\.28\)/);
    const night = decor.slice(
      decor.indexOf('function nightDressing'),
      decor.indexOf('\n}\n', decor.indexOf('function nightDressing')),
    );
    assert.doesNotMatch(
      night,
      /new SphereGeometry\(0\.009, 8, 6\), '#FFFFFF'/,
      'no white dots on the caps',
    );
  });

  test('trees and bushes are leafy crowns and the tufts are blades: no bare sphere or cone of foliage', () => {
    const decor = read('src/decor.ts');
    assert.doesNotMatch(decor, /part\(new SphereGeometry\(r, \d+, \d+\), '#47B04A'/);
    assert.match(decor, /part\(foliage\(r, /);
    assert.doesNotMatch(decor, /new ConeGeometry\(0\.025, 0\.12, 6\)/);
    assert.match(decor, /part\(grassBlade\(0\.02\)/);
  });

  test("the agent is told the light is the starter's, that water is water(...), and how natural things take relief", () => {
    const agents = read('AGENTS.md');
    assert.match(
      agents,
      /Leave `renderer\.toneMapping`,\n\s+`scene\.environment` and `scene\.fog` to `setupLook`/,
    );
    assert.match(
      agents,
      /only a picture all\n\s+round \(a sky, stars\) stays on `MeshBasicMaterial`/,
    );
    assert.match(agents, /a disc 1\.2 m\n\s+in radius/);
    assert.match(agents, /`const lake = water\(1\.2\)` from `\.\/kit\.js`/);
    assert.match(agents, /Never a flat coloured disc\./);
    assert.match(
      read('src/kit.ts'),
      /export \{ surfaceGrain, water, waterMaterial, withGrain \} from '\.\/surface\.js';/,
    );
  });
});
