import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
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
import type { ProjectLocationChoice } from '@halcyonic/contracts';
import { createHostLocations } from './locations.ts';
import { HostStarters } from './starters.ts';
import { createTestControlPlane } from './testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-starters-')));
after(() => rmSync(base, { recursive: true, force: true }));

const LOCK = '{"name":"starter","lockfileVersion":3,"packages":{}}\n';

/** A tracked starter `vr` with a two-file template, and, when asked, its installed dependencies. */
function setup(name: string, options: { installed?: 'matching' | 'stale' | false } = {}) {
  const root = join(base, name);
  const tracked = join(root, 'tracked');
  const installed = join(root, 'installed');
  const projects = join(root, 'projects');
  mkdirSync(join(tracked, 'vr', 'template', 'src'), { recursive: true });
  mkdirSync(projects, { recursive: true });
  writeFileSync(
    join(tracked, 'vr', 'starter.json'),
    JSON.stringify({ format: 1, title: 'A VR app' }),
  );
  writeFileSync(join(tracked, 'vr', 'package-lock.json'), LOCK);
  writeFileSync(join(tracked, 'vr', 'template', 'index.html'), '<html></html>\n');
  writeFileSync(join(tracked, 'vr', 'template', 'src', 'index.ts'), 'export {};\n');
  if (options.installed) {
    mkdirSync(join(installed, 'vr', 'node_modules', 'dep'), { recursive: true });
    writeFileSync(join(installed, 'vr', 'node_modules', 'dep', 'index.js'), 'export {};\n');
    const lockfile =
      options.installed === 'matching'
        ? createHash('sha256').update(LOCK).digest('hex')
        : 'f'.repeat(64);
    writeFileSync(join(installed, 'vr', 'installed.json'), JSON.stringify({ format: 1, lockfile }));
  }
  const starters = new HostStarters({ trackedDir: tracked, installedDir: installed });
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
