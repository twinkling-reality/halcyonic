import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  readlinkSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { ProjectLocationChoice } from '@halcyonic/contracts';
import { createHostLocations } from './locations.ts';
import {
  HostStarters,
  installedSet,
  originalFolderOf,
  originalRefusal,
  templateDigest,
  VR_STARTER,
} from './starters.ts';
import { createTestControlPlane } from './testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-starters-')));
after(() => rmSync(base, { recursive: true, force: true }));

const LOCK = '{"name":"starter","lockfileVersion":3,"packages":{}}\n';

/** A tracked starter `vr` with a two-file template, and, when asked, its installed dependencies. */
function setup(
  name: string,
  options: {
    installed?: 'matching' | 'stale' | false;
    id?: string;
    dataDir?: string;
    home?: string;
  } = {},
) {
  const id = options.id ?? 'vr';
  const root = join(base, name);
  const tracked = join(root, 'tracked');
  const installed = join(root, 'installed');
  const projects = join(root, 'projects');
  mkdirSync(join(tracked, id, 'template', 'src'), { recursive: true });
  mkdirSync(projects, { recursive: true });
  writeFileSync(
    join(tracked, id, 'starter.json'),
    JSON.stringify({ format: 1, title: 'A VR app' }),
  );
  writeFileSync(join(tracked, id, 'package-lock.json'), LOCK);
  writeFileSync(join(tracked, id, 'template', 'index.html'), '<html></html>\n');
  writeFileSync(join(tracked, id, 'template', 'src', 'index.ts'), 'export {};\n');
  if (options.installed) {
    mkdirSync(join(installed, id, 'node_modules', 'dep'), { recursive: true });
    writeFileSync(join(installed, id, 'node_modules', 'dep', 'index.js'), 'export {};\n');
    const lockfile =
      options.installed === 'matching'
        ? createHash('sha256').update(LOCK).digest('hex')
        : 'f'.repeat(64);
    writeFileSync(join(installed, id, 'installed.json'), JSON.stringify({ format: 1, lockfile }));
  }
  const starters = new HostStarters({
    trackedDir: tracked,
    installedDir: installed,
    dataDir: options.dataDir ?? join(root, 'data'),
    home: options.home ?? join(root, 'home'),
  });
  return { root, tracked, installed, projects, starters };
}

describe('starters on the host', () => {
  test('a tracked starter is listed, installed only while its dependencies match the lockfile', () => {
    assert.deepEqual(setup('none').starters.list(), [
      { starter_id: 'vr', title: 'A VR app', installed: false },
    ]);
    assert.equal(setup('stale', { installed: 'stale' }).starters.list()[0]?.installed, false);
    assert.equal(setup('match', { installed: 'matching' }).starters.list()[0]?.installed, true);
  });

  test('an installed set never matches a lockfile entry without a version of its own (AUDIT SETUP-3)', () => {
    const { tracked, installed } = setup('unpinned');
    writeFileSync(
      join(tracked, 'vr', 'package-lock.json'),
      JSON.stringify({
        lockfileVersion: 3,
        packages: {
          '': { name: 'starter' },
          'node_modules/kept': { version: '1.0.0' },
          'node_modules/unpinned': {},
        },
      }),
    );
    mkdirSync(join(installed, 'vr', 'node_modules', 'kept'), { recursive: true });
    writeFileSync(
      join(installed, 'vr', 'node_modules', 'kept', 'package.json'),
      '{"version":"1.0.0"}',
    );
    assert.deepEqual(installedSet(tracked, installed, 'vr'), {
      state: 'differs',
      differs: ['node_modules/unpinned'],
    });
  });

  test("the template's digest is the one applying records, and the installed lockfile's is read only while it matches (DRAWN)", () => {
    const { tracked, projects, starters } = setup('digest', { installed: 'matching' });
    const applied = starters.apply('vr', join(projects, 'digest-app'));
    assert.ok(applied.ok);
    assert.equal(templateDigest(join(tracked, 'vr', 'template')), applied.template);
    assert.equal(starters.lockfile('vr'), applied.lockfile);
    assert.equal(setup('digest-stale', { installed: 'stale' }).starters.lockfile('vr'), null);
    assert.equal(starters.lockfile('missing'), null);
    // A template holding a link has no digest, as it is never applied.
    symlinkSync(tracked, join(tracked, 'vr', 'template', 'link'));
    assert.equal(templateDigest(join(tracked, 'vr', 'template')), null);
  });

  test('applying copies the template and links the shared dependencies', () => {
    const { projects, installed, starters } = setup('apply', { installed: 'matching' });
    const folder = join(projects, 'garden');
    mkdirSync(folder);
    const applied = starters.apply('vr', folder);
    assert.ok(applied.ok);
    assert.equal(applied.files, 2);
    assert.match(applied.template, /^[0-9a-f]{64}$/);
    assert.equal(readFileSync(join(folder, 'src', 'index.ts'), 'utf8'), 'export {};\n');
    assert.equal(lstatSync(join(folder, 'node_modules')).isSymbolicLink(), true);
    assert.equal(readlinkSync(join(folder, 'node_modules')), join(installed, 'vr', 'node_modules'));
    const again = join(projects, 'again');
    mkdirSync(again);
    const second = starters.apply('vr', again);
    assert.equal(second.ok && second.template, applied.template, 'the same files hash the same');
  });

  test('a template holding a link is refused before anything is copied', () => {
    const { tracked, projects, starters } = setup('link', { installed: 'matching' });
    symlinkSync('/etc/hosts', join(tracked, 'vr', 'template', 'hosts'));
    const folder = join(projects, 'garden');
    mkdirSync(folder);
    const applied = starters.apply('vr', folder);
    assert.equal(applied.ok, false);
    assert.equal(existsSync(join(folder, 'index.html')), false);
  });

  test('an agent sandboxed in the project cannot write through the link to the shared dependencies', () => {
    const { projects, installed, starters } = setup('sandbox', { installed: 'matching' });
    const folder = join(projects, 'garden');
    mkdirSync(folder);
    assert.ok(starters.apply('vr', folder).ok);
    // The write rule of Halcyonic's agent sandbox (ADR 0028): writes only in the project roots.
    // Seatbelt judges the path a write resolves to, so a link out of the root is no way out.
    const profile = join(base, 'sandbox.sb');
    writeFileSync(
      profile,
      `(version 1)\n(allow default)\n(deny file-write*)\n(allow file-write* (subpath ${JSON.stringify(projects)}) (literal "/dev/null"))\n`,
    );
    const through = spawnSync(
      '/usr/bin/sandbox-exec',
      ['-f', profile, '/bin/sh', '-c', 'echo x > node_modules/dep/index.js'],
      { cwd: folder },
    );
    assert.notEqual(through.status, 0, 'the write through the link is refused');
    assert.equal(
      readFileSync(join(installed, 'vr', 'node_modules', 'dep', 'index.js'), 'utf8'),
      'export {};\n',
    );
    const inside = spawnSync(
      '/usr/bin/sandbox-exec',
      ['-f', profile, '/bin/sh', '-c', 'echo x > src/new.ts'],
      { cwd: folder },
    );
    assert.equal(inside.status, 0, 'a write in the project itself still works');
    execFileSync('/bin/test', ['-f', join(folder, 'src', 'new.ts')]);
  });
});

describe('beginning a project from a starter', () => {
  function plane(name: string, installed: 'matching' | false) {
    const fixture = setup(name, { installed });
    const harness = createTestControlPlane({
      locations: createHostLocations([fixture.projects]),
      starters: fixture.starters,
    });
    const submit = (location: ProjectLocationChoice) =>
      harness.controlPlane.commands.submit(
        harness.commands.createProject('Garden', location),
        'internal',
      );
    return { ...fixture, ...harness, submit };
  }

  test('makes the folder, puts the starter in it, and journals which starter', async () => {
    const { projects, controlPlane, journal, submit } = plane('create', 'matching');
    const outcome = submit({
      kind: 'new_from_starter',
      root: projects,
      folder_name: 'garden',
      starter_id: 'vr',
    });
    assert.equal(outcome.command?.status, 'completed');
    const result = outcome.command?.result;
    const projectId = result?.kind === 'project_created' ? result.project_id : '';
    assert.equal(controlPlane.projection.project(projectId)?.starter, 'vr');
    assert.equal(readFileSync(join(projects, 'garden', 'index.html'), 'utf8'), '<html></html>\n');
    const events = [...journal.readAll()].map((stored) => stored.event);
    assert.deepEqual(
      events.map((event) => event.event_type),
      ['command.accepted', 'project.created', 'project.starter_applied', 'command.completed'],
    );
    const applied = events[2];
    assert.equal(applied?.event_type === 'project.starter_applied' && applied.payload.files, 2);
    await controlPlane.close();
  });

  test('a starter not installed is refused, and nothing is made', async () => {
    const { projects, controlPlane, submit } = plane('refused', false);
    const outcome = submit({
      kind: 'new_from_starter',
      root: projects,
      folder_name: 'garden',
      starter_id: 'vr',
    });
    assert.equal(outcome.disposition, 'rejected');
    assert.equal(outcome.command?.rejection?.code, 'capability_unsupported');
    assert.equal(existsSync(join(projects, 'garden')), false);
    await controlPlane.close();
  });

  test('a starter cannot be bound to a project that already exists', async () => {
    const { projects, controlPlane, commands } = plane('rebind', 'matching');
    const created = controlPlane.commands.submit(commands.createProject('Plain', null), 'internal');
    const result = created.command?.result;
    const projectId = result?.kind === 'project_created' ? result.project_id : null;
    assert.ok(projectId);
    const refused = controlPlane.commands.submit(
      {
        ...commands.createProject('unused', null),
        command_type: 'project.set_location',
        payload: {
          project_id: projectId,
          location: {
            kind: 'new_from_starter',
            root: projects,
            folder_name: 'garden',
            starter_id: 'vr',
          },
        },
      },
      'internal',
    );
    assert.equal(refused.command?.rejection?.code, 'capability_unsupported');
    await controlPlane.close();
  });
});

describe('making a VR version of a project (M1.8c-1)', () => {
  function plane(
    name: string,
    installed: 'matching' | false,
    where: { dataDir?: string; home?: string } = {},
  ) {
    const fixture = setup(name, { installed, id: VR_STARTER, ...where });
    const harness = createTestControlPlane({
      locations: createHostLocations([fixture.projects]),
      starters: fixture.starters,
    });
    const created = (location: ProjectLocationChoice | null) => {
      const outcome = harness.controlPlane.commands.submit(
        harness.commands.createProject('Garden', location),
        'internal',
      );
      const result = outcome.command?.result;
      return result?.kind === 'project_created' ? result.project_id : '';
    };
    const make = (originalProjectId: string, folderName = 'garden-vr') =>
      harness.controlPlane.commands.submit(
        {
          ...harness.commands.createProject('unused', null),
          command_type: 'project.make_vr_version',
          payload: {
            original_project_id: originalProjectId as never,
            root: fixture.projects,
            folder_name: folderName,
          },
        },
        'internal',
      );
    return { ...fixture, ...harness, created, make };
  }

  test('makes a new starter project beside the original, records the original and the instruction, and changes nothing of the original', async () => {
    const { projects, controlPlane, journal, created, make } = plane('version', 'matching');
    mkdirSync(join(projects, 'garden'));
    writeFileSync(join(projects, 'garden', 'game.js'), 'game');
    const originalId = created({ kind: 'existing_folder', root: projects, folder_name: 'garden' });
    const before = [...journal.readAll()].length;
    const outcome = make(originalId);
    assert.equal(outcome.command?.status, 'completed', JSON.stringify(outcome.command));
    const result = outcome.command?.result;
    const projectId = result?.kind === 'project_created' ? result.project_id : '';
    const project = controlPlane.projection.project(projectId);
    assert.equal(project?.name, 'Garden VR');
    assert.equal(project?.starter, VR_STARTER);
    assert.equal(project?.location?.path, join(projects, 'garden-vr'));
    assert.equal(controlPlane.projection.madeFrom(projectId), originalId);
    const events = [...journal.readAll()].slice(before).map((stored) => stored.event);
    assert.deepEqual(
      events.map((event) => event.event_type),
      [
        'command.accepted',
        'project.created',
        'project.starter_applied',
        'project.made_from',
        'command.completed',
      ],
    );
    const madeFrom = events[3];
    const instruction =
      madeFrom?.event_type === 'project.made_from' ? madeFrom.payload.instruction : '';
    assert.ok(instruction.includes(join(projects, 'garden')), instruction);
    assert.match(instruction, /Do not change anything there/);
    assert.match(instruction, /src\/play-input\.ts/);
    // The S3 re-run's game kept the starter's "Untitled Game" card: the instruction asks for the app's own.
    assert.match(
      instruction,
      /Put the app's name and one line on what to do on its welcome card\./,
    );
    // V1: the main play only, from the starter's worked game, in a world that fits, type checked before it ends.
    assert.match(
      instruction,
      /If it is a game, build its main play: start from the whole small game at the end of this project's AGENTS\.md/,
    );
    // VRIFY: an app that is not a game brings its most striking view, as the starter's AGENTS.md says.
    assert.match(
      instruction,
      /If it is not a game, bring its one most striking view, the scene people open it for, as 'A view from another app' in this project's AGENTS\.md says/,
    );
    assert.match(instruction, /leave out title, end and pause screens, menus and best scores/);
    assert.match(instruction, /the world from src\/worlds\.ts that fits the original/);
    assert.match(instruction, /Run npx tsc --noEmit/);
    assert.deepEqual(readdirSync(join(projects, 'garden')), ['game.js']);
    assert.equal(readFileSync(join(projects, 'garden', 'game.js'), 'utf8'), 'game');
    await controlPlane.close();
  });

  test('the command refuses an original that is a root, or holds the data folder or the home folder, and makes nothing', async () => {
    const atRoot = plane('original-root', 'matching');
    const root = atRoot.created({
      kind: 'existing_folder',
      root: atRoot.projects,
      folder_name: null,
    });
    const refused = atRoot.make(root);
    assert.equal(
      refused.command?.rejection?.code,
      'location_not_allowed',
      JSON.stringify(refused.command),
    );
    assert.equal(existsSync(join(atRoot.projects, 'garden-vr')), false);
    await atRoot.controlPlane.close();
    for (const [name, where] of [
      ['original-data', { dataDir: 'garden/.halcyonic' }],
      ['original-home', { home: 'garden/me' }],
    ] as const) {
      // The fixture's projects folder is <base>/<name>/projects.
      const one = plane(name, 'matching', {
        ...('dataDir' in where ? { dataDir: join(base, name, 'projects', where.dataDir) } : {}),
        ...('home' in where ? { home: join(base, name, 'projects', where.home) } : {}),
      });
      mkdirSync(join(one.projects, 'garden'));
      const garden = one.created({
        kind: 'existing_folder',
        root: one.projects,
        folder_name: 'garden',
      });
      const outcome = one.make(garden);
      assert.equal(outcome.command?.rejection?.code, 'location_not_allowed', name);
      assert.equal(existsSync(join(one.projects, 'garden-vr')), false, name);
      await one.controlPlane.close();
    }
  });

  test('an original without a folder, a folder already there, or no starter installed is refused, and nothing is made', async () => {
    const one = plane('refusals', 'matching');
    const noFolder = one.make(one.created(null));
    assert.equal(noFolder.command?.rejection?.code, 'location_required');
    mkdirSync(join(one.projects, 'garden'));
    const originalId = one.created({
      kind: 'existing_folder',
      root: one.projects,
      folder_name: 'garden',
    });
    mkdirSync(join(one.projects, 'taken'));
    assert.equal(one.make(originalId, 'taken').disposition, 'rejected');
    assert.deepEqual(readdirSync(join(one.projects, 'taken')), []);
    await one.controlPlane.close();
    const two = plane('no-starter', false);
    mkdirSync(join(two.projects, 'garden'));
    const original = two.created({
      kind: 'existing_folder',
      root: two.projects,
      folder_name: 'garden',
    });
    const refused = two.make(original);
    assert.equal(refused.command?.rejection?.code, 'capability_unsupported');
    assert.equal(existsSync(join(two.projects, 'garden-vr')), false);
    await two.controlPlane.close();
  });
});

describe("which folder may be a VR version's original (AUDIT M18C1-M1, M18C1-S2)", () => {
  const host = {
    roots: ['/Users/someone/dev', '/Users/someone/work'],
    home: '/Users/someone',
    dataDir: '/Users/someone/.halcyonic',
    projects: [
      '/Users/someone/dev/garden',
      '/Users/someone/dev/garden-vr',
      '/Users/someone/work/shop',
    ],
  };

  test('a game in its own folder may be; a root, the home folder or above, or a folder holding the data or another project may not', () => {
    assert.equal(originalRefusal('/Users/someone/dev/garden', host), null);
    for (const folder of [
      '/Users/someone/dev',
      '/Users/someone/work',
      '/Users/someone',
      '/Users',
      '/',
    ]) {
      assert.match(originalRefusal(folder, host) ?? '', /./, folder);
    }
    assert.match(
      originalRefusal('/Users/someone/dev/garden', {
        ...host,
        dataDir: '/Users/someone/dev/garden/.halcyonic',
      }) ?? '',
      /data/,
    );
    assert.match(originalRefusal('/Users/someone/.halcyonic/starters', host) ?? '', /data/);
    // A folder above a projects folder is refused too, even when it holds no project yet.
    assert.match(
      originalRefusal('/Users/someone/dev', {
        ...host,
        roots: ['/Users/someone/dev/games'],
        projects: [],
      }) ?? '',
      /projects folder/,
    );
    assert.match(
      originalRefusal('/Users/someone/dev/garden', {
        ...host,
        projects: [...host.projects, '/Users/someone/dev/garden/inner'],
      }) ?? '',
      /another project/,
    );
    assert.match(
      originalRefusal('/Users/someone/dev/garden', {
        ...host,
        home: '/Users/someone/dev/garden/me',
      }) ?? '',
      /home/,
    );
  });

  test('the original a folder reads is found only when exactly one starter project has that folder, and only when allowed', () => {
    const projects = [
      { id: 'g', folder: '/w/garden', starter: null, madeFrom: null },
      { id: 'v', folder: '/w/garden-vr', starter: 'iwsdk-vr', madeFrom: 'g' },
    ];
    const allow = () => null;
    assert.equal(originalFolderOf('/w/garden-vr', projects, allow), '/w/garden');
    assert.equal(originalFolderOf('/w/garden', projects, allow), null);
    assert.equal(
      originalFolderOf('/w/garden-vr', projects, () => 'no'),
      null,
    );
    const shared = [
      ...projects,
      { id: 'v2', folder: '/w/garden-vr', starter: 'iwsdk-vr', madeFrom: null },
    ];
    assert.equal(originalFolderOf('/w/garden-vr', shared, allow), null);
  });

  test('the tracked iwsdk-vr template keeps its src/ clean and tells the agent to edit src/index.ts directly without scratch scripts (SC1)', () => {
    const template = fileURLToPath(new URL('../../../starters/iwsdk-vr/template', import.meta.url));
    const srcFiles = readdirSync(join(template, 'src')).sort();
    assert.deepEqual(srcFiles, [
      'assets.ts',
      'components.ts',
      'decor.ts',
      'index.ts',
      'kit.ts',
      'look.ts',
      'motion.ts',
      'palette.ts',
      'panel.ts',
      'play-input-core.ts',
      'play-input.ts',
      'sound.ts',
      'vite-env.d.ts',
      'worlds.ts',
    ]);
    const agents = readFileSync(join(template, 'AGENTS.md'), 'utf8');
    assert.match(agents, /Keep the game's systems and setup in `src\/index\.ts`/);
    assert.match(agents, /Edit `src\/index\.ts` directly/);
    assert.match(agents, /never write `\.py`, `\.txt`, `\.sh` or `\.patch` scripts/);
    assert.match(agents, /Run `npx tsc --noEmit` before ending your turn/);
  });
});
