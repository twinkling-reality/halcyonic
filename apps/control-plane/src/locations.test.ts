import assert from 'node:assert/strict';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  symlinkSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  compileValidator,
  LocationsResponse,
  MAX_LOCATION_FOLDERS,
  type ProjectId,
} from '@halcyonic/contracts';
import { createHostLocations, MAX_SCANNED_ENTRIES, rootLabels } from './locations.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-locations-')));
after(() => rmSync(base, { recursive: true, force: true }));
const validLocations = compileValidator(LocationsResponse);

/** A project root with an app folder in it, and a private folder beside the root. */
function layout(name: string) {
  const top = join(base, name);
  const root = join(top, 'projects');
  const app = join(root, 'app');
  const outside = join(top, 'private');
  mkdirSync(app, { recursive: true });
  mkdirSync(outside, { recursive: true });
  return { root, app, outside, locations: createHostLocations([root]) };
}

describe('listing where projects may live', () => {
  test('lists each root with the visible folders directly inside it, sorted, and nothing else', () => {
    const { root, outside, locations } = layout('list');
    for (const folder of ['beta', 'Alpha', 'item10', 'item9', '.hidden', 'nested/inner']) {
      mkdirSync(join(root, folder), { recursive: true });
    }
    writeFileSync(join(root, 'a-file'), 'x');
    symlinkSync(outside, join(root, 'escape'));
    symlinkSync(join(root, 'beta'), join(root, 'alias'));
    const listed = locations.list();
    assert.ok(validLocations(listed).ok);
    assert.equal(listed.roots.length, 1);
    const [only] = listed.roots;
    assert.equal(only?.path, root);
    assert.equal(only?.name, 'projects');
    assert.equal(only?.status, 'available');
    assert.equal(only?.folders_truncated, false);
    assert.deepEqual(
      only?.folders.map((folder) => folder.name),
      ['Alpha', 'app', 'beta', 'item9', 'item10', 'nested'],
      'no hidden folder, file or symbolic link, and nothing deeper',
    );
    assert.equal(only?.folders.find((folder) => folder.name === 'app')?.path, join(root, 'app'));
  });

  test('a root with more folders than listed says so', () => {
    const { root, locations } = layout('many');
    for (let index = 0; index <= MAX_LOCATION_FOLDERS; index += 1) {
      mkdirSync(join(root, `folder-${String(index).padStart(3, '0')}`));
    }
    const [only] = locations.list().roots;
    assert.equal(only?.folders.length, MAX_LOCATION_FOLDERS);
    assert.equal(only?.folders_truncated, true);
  });

  test('a root holding more entries than are read is listed as truncated', () => {
    const { root, locations } = layout('crowded');
    for (let index = 0; index < MAX_SCANNED_ENTRIES; index += 1) {
      writeFileSync(join(root, `file-${index}`), '');
    }
    const [only] = locations.list().roots;
    assert.equal(only?.status, 'available');
    assert.equal(
      only?.folders_truncated,
      true,
      'the app folder and 10,000 files are 10,001 entries',
    );
    assert.ok((only?.folders.length ?? 0) <= 1);
  });

  test('a root that has gone is listed as missing, with nothing in it', () => {
    const { root, locations } = layout('gone');
    rmSync(root, { recursive: true });
    assert.deepEqual(locations.list().roots[0]?.status, 'missing');
    assert.deepEqual(locations.list().roots[0]?.folders, []);
  });

  test('with no roots nothing is listed', () => {
    assert.deepEqual(createHostLocations([]).list(), { roots: [] });
  });
});

const PROJECT_A = '01900000-0000-7000-8000-00000000000a' as ProjectId;
const PROJECT_B = '01900000-0000-7000-8000-00000000000b' as ProjectId;
const at = (iso: string) => new Date(iso);

describe('what the listing tells about each folder', () => {
  test('a folder with a .git folder or file is a repository; one without, or with a link by that name, is not', () => {
    const { root, outside, locations } = layout('repositories');
    mkdirSync(join(root, 'cloned', '.git'), { recursive: true });
    mkdirSync(join(root, 'worktree'));
    writeFileSync(join(root, 'worktree', '.git'), 'gitdir: elsewhere\n');
    mkdirSync(join(root, 'linked'));
    symlinkSync(outside, join(root, 'linked', '.git'));
    const listed = locations.list();
    assert.ok(validLocations(listed).ok);
    const facts = Object.fromEntries(
      (listed.roots[0]?.folders ?? []).map((folder) => [folder.name, folder.repository]),
    );
    assert.deepEqual(facts, { app: false, cloned: true, linked: false, worktree: true });
  });

  test("changed_at is the folder's own time, or for a repository the newer of it and .git's", () => {
    const { root, locations } = layout('changed');
    const plain = join(root, 'plain');
    const cloned = join(root, 'cloned');
    mkdirSync(plain);
    mkdirSync(join(cloned, '.git'), { recursive: true });
    utimesSync(plain, at('2026-09-01T10:00:00.000Z'), at('2026-09-01T10:00:00.000Z'));
    utimesSync(
      join(cloned, '.git'),
      at('2026-09-20T08:30:00.000Z'),
      at('2026-09-20T08:30:00.000Z'),
    );
    utimesSync(cloned, at('2026-09-02T00:00:00.000Z'), at('2026-09-02T00:00:00.000Z'));
    const folders = locations.list().roots[0]?.folders ?? [];
    const changed = (name: string) => folders.find((folder) => folder.name === name)?.changed_at;
    assert.equal(changed('plain'), '2026-09-01T10:00:00.000Z');
    assert.equal(changed('cloned'), '2026-09-20T08:30:00.000Z', '.git is newer');
    utimesSync(cloned, at('2026-09-30T00:00:00.000Z'), at('2026-09-30T00:00:00.000Z'));
    const again = locations.list().roots[0]?.folders.find((folder) => folder.name === 'cloned');
    assert.equal(again?.changed_at, '2026-09-30T00:00:00.000Z', 'the folder itself is newer');
  });

  test('a folder that cannot be searched is listed with its facts unknown', (t) => {
    const { root, locations } = layout('closed');
    const closed = join(root, 'closed');
    mkdirSync(join(closed, '.git'), { recursive: true });
    chmodSync(closed, 0o000);
    t.after(() => chmodSync(closed, 0o755));
    const folder = locations.list().roots[0]?.folders.find((entry) => entry.name === 'closed');
    assert.deepEqual(
      { repository: folder?.repository, changed_at: folder?.changed_at },
      { repository: null, changed_at: null },
    );
  });

  test('each folder names the projects bound to it, as a start would find them', (t) => {
    const { root, app, outside, locations } = layout('used');
    mkdirSync(join(root, 'free'));
    symlinkSync(app, join(outside, 'to-app'));
    const listed = locations.list([
      { project_id: PROJECT_A, path: app },
      { project_id: PROJECT_B, path: join(outside, 'to-app') },
    ]);
    assert.ok(validLocations(listed).ok);
    const usedBy = (name: string) =>
      listed.roots[0]?.folders.find((folder) => folder.name === name)?.used_by;
    assert.deepEqual(usedBy('app'), [PROJECT_A], 'a path that is a link names no folder');
    assert.deepEqual(usedBy('free'), []);
    assert.deepEqual(listed.roots[0]?.used_by, []);
    if (!existsSync(join(root, 'APP'))) {
      t.skip('this volume is case-sensitive');
      return;
    }
    // A bound path is the real path the host recorded, so another spelling only arises when the
    // folder was renamed since; every start in that project is then refused as moved.
    const spelt = locations.list([{ project_id: PROJECT_B, path: join(root, 'APP') }]);
    assert.deepEqual(
      spelt.roots[0]?.folders.find((folder) => folder.name === 'app')?.used_by,
      [],
      'a project that cannot start there does not hold the folder',
    );
  });

  test('a project bound to the root names the root, and a folder put in a bound place is the one in use', () => {
    const { root, app, locations } = layout('replaced');
    renameSync(app, join(root, 'old-app'));
    mkdirSync(app);
    const listed = locations.list([
      { project_id: PROJECT_A, path: root },
      { project_id: PROJECT_B, path: app },
    ]);
    assert.deepEqual(listed.roots[0]?.used_by, [PROJECT_A]);
    const usedBy = (name: string) =>
      listed.roots[0]?.folders.find((folder) => folder.name === name)?.used_by;
    assert.deepEqual(usedBy('app'), [PROJECT_B], 'what is at the bound path now');
    assert.deepEqual(usedBy('old-app'), [], 'the moved folder is no longer at the bound path');
  });

  test('a project whose path now leads through a link on the way names no folder', () => {
    const top = join(base, 'linked-on-the-way');
    const first = join(top, 'first');
    const second = join(top, 'second');
    mkdirSync(join(first, 'foo'), { recursive: true });
    mkdirSync(join(second, 'foo'), { recursive: true });
    const locations = createHostLocations([first, second]);
    renameSync(first, join(top, 'first-moved'));
    symlinkSync(second, first);
    const listed = locations.list([{ project_id: PROJECT_A, path: join(first, 'foo') }]);
    const secondRoot = listed.roots.find((root) => root.path === second);
    assert.deepEqual(
      secondRoot?.folders.find((folder) => folder.name === 'foo')?.used_by,
      [],
      'a start in that project is refused, so the folder it leads to is free',
    );
  });

  test('a folder names at most 100 projects, the oldest first, reading its path once', () => {
    const { app, locations } = layout('crowded-users');
    const ids = Array.from(
      { length: 150 },
      (_, index) => `01900000-0000-7000-8000-${String(index).padStart(12, '0')}` as ProjectId,
    );
    const listed = locations.list(ids.map((project_id) => ({ project_id, path: app })));
    assert.deepEqual(
      listed.roots[0]?.folders.find((folder) => folder.name === 'app')?.used_by,
      ids.slice(0, 100),
    );
  });

  test('a missing root tells nothing about itself', () => {
    const { root, locations } = layout('gone-facts');
    rmSync(root, { recursive: true });
    const [only] = locations.list([{ project_id: PROJECT_A, path: root }]).roots;
    assert.deepEqual(
      { repository: only?.repository, changed_at: only?.changed_at, used_by: only?.used_by },
      { repository: null, changed_at: null, used_by: [] },
    );
  });
});

describe('naming the roots for people', () => {
  test('a root is named by its own folder, and only roots sharing a name gain the folder above', () => {
    const labels = rootLabels([
      '/Users/person/Projects',
      '/Volumes/Work/Projects',
      '/Users/person/Code',
    ]);
    assert.deepEqual(Object.fromEntries(labels), {
      '/Users/person/Projects': 'Projects (person)',
      '/Volumes/Work/Projects': 'Projects (Work)',
      '/Users/person/Code': 'Code',
    });
  });

  test('a level further up is named only where the one below still leaves them alike', () => {
    const labels = rootLabels(['/a/shared/Projects', '/b/shared/Projects', '/c/other/Projects']);
    assert.deepEqual(Object.fromEntries(labels), {
      '/a/shared/Projects': 'Projects (a)',
      '/b/shared/Projects': 'Projects (b)',
      '/c/other/Projects': 'Projects (other)',
    });
  });

  test('names alike but for case or compatibility forms count as the same name', () => {
    const labels = rootLabels([
      '/Users/one/Projects',
      '/Users/two/PROJECTS',
      '/Users/three/Ｐrojects',
    ]);
    assert.deepEqual(Object.fromEntries(labels), {
      '/Users/one/Projects': 'Projects (one)',
      '/Users/two/PROJECTS': 'PROJECTS (two)',
      '/Users/three/Ｐrojects': 'Ｐrojects (three)',
    });
  });

  test('a drive says so, and a root with nothing above it is named by itself', () => {
    const labels = rootLabels([
      '/Volumes/Work',
      '/Users/person/Work',
      '/Projects',
      '/Users/person/Projects',
    ]);
    assert.deepEqual(Object.fromEntries(labels), {
      '/Volumes/Work': 'Work (drive)',
      '/Users/person/Work': 'Work (person)',
      '/Projects': 'Projects',
      '/Users/person/Projects': 'Projects (person)',
    });
  });

  test('a root configured twice is one root with one name, and no name grows past the contract', () => {
    assert.deepEqual(
      Object.fromEntries(rootLabels(['/Users/person/Projects', '/Users/person/Projects'])),
      {
        '/Users/person/Projects': 'Projects',
      },
    );
    const long = 'p'.repeat(200);
    const labels = rootLabels([`/${'a'.repeat(250)}/${long}`, `/${'b'.repeat(250)}/${long}`]);
    for (const label of labels.values()) {
      assert.ok(label.length <= 255, `${label.length} characters`);
      assert.ok(label.startsWith(`${long} (`));
    }
    assert.equal(new Set(labels.values()).size, 2);
  });

  test('no two labels read alike, even where one folder is named like another root label', () => {
    const labels = rootLabels([
      '/Users/glen/Personal/Projects',
      '/Users/glen/Personal/Work/Projects',
      '/Users/glen/Projects (old)',
      '/Users/glen/old/Projects',
      `/x/${'n'.repeat(253)}`,
      `/y/${'n'.repeat(253)}`,
    ]);
    const values = [...labels.values()];
    assert.equal(
      new Set(values.map((label) => label.normalize('NFKC').toLowerCase())).size,
      values.length,
      values.join(' | '),
    );
    assert.ok(
      values.every((label) => label.length > 0 && label.length <= 255 && !label.includes('/')),
    );
  });

  test('any set of distinct real paths gets labels that never read alike', () => {
    const names = [
      'Projects',
      'projects',
      'Work',
      'Projects (old)',
      'old',
      'drive',
      'Ｐrojects',
      '🙂',
      'a',
    ];
    let seed = 7;
    const next = () => {
      seed = (seed * 1103515245 + 12345) % 2147483648;
      return seed;
    };
    for (let trial = 0; trial < 500; trial += 1) {
      const count = 2 + (next() % 7);
      const paths = new Set<string>();
      while (paths.size < count) {
        const depth = 1 + (next() % 4);
        const parts = Array.from({ length: depth }, () => names[next() % names.length]);
        paths.add(`/${next() % 3 === 0 ? 'Volumes/' : ''}${parts.join('/')}`);
      }
      const labels = rootLabels([...paths]);
      assert.equal(labels.size, paths.size);
      const values = [...labels.values()];
      assert.equal(
        new Set(values.map((label) => label.normalize('NFKC').toLowerCase())).size,
        values.length,
        `${[...paths].join(', ')} gave ${values.join(' | ')}`,
      );
      assert.ok(
        values.every((label) => label.length > 0 && label.length <= 255 && !label.includes('/')),
      );
    }
  });

  test('a long name is cut on a whole character', () => {
    const emoji = '\u{1F642}'.repeat(130);
    for (const label of rootLabels([`/a/${emoji}`, `/b/${emoji}`]).values()) {
      assert.ok(label.length <= 255);
      assert.ok(!/[\uD800-\uDBFF](?![\uDC00-\uDFFF])/.test(label), 'no lone high surrogate');
    }
  });

  test('a root configured twice is listed once', () => {
    const root = join(base, 'twice', 'Projects');
    mkdirSync(join(root, 'app'), { recursive: true });
    const listed = createHostLocations([root, root]).list();
    assert.equal(listed.roots.length, 1);
    assert.equal(listed.roots[0]?.label, 'Projects');
  });

  test('the listing names two roots that share a folder name apart, and never by a path', () => {
    const top = join(base, 'labels');
    const first = join(top, 'home', 'Projects');
    const second = join(top, 'work', 'Projects');
    mkdirSync(join(first, 'app'), { recursive: true });
    mkdirSync(join(second, 'site'), { recursive: true });
    const listed = createHostLocations([first, second]).list();
    assert.ok(validLocations(listed).ok);
    assert.deepEqual(
      listed.roots.map((root) => [root.name, root.label]),
      [
        ['Projects', 'Projects (home)'],
        ['Projects', 'Projects (work)'],
      ],
      'the name stays the folder own name; the label tells them apart',
    );
    assert.ok(listed.roots.every((root) => !root.label.includes('/')));
  });
});

describe('binding a project to an existing folder', () => {
  test('a folder directly inside a root, or the root itself, is bound by its real path', () => {
    const { root, app, locations } = layout('existing');
    const choice = { kind: 'existing_folder', root, folder_name: 'app' } as const;
    assert.deepEqual(locations.check(choice), { ok: true });
    assert.deepEqual(locations.bind(choice), {
      ok: true,
      location: { path: app, name: 'app', created: false },
    });
    assert.deepEqual(locations.bind({ kind: 'existing_folder', root, folder_name: null }), {
      ok: true,
      location: { path: root, name: 'projects', created: false },
    });
  });

  test('a folder named in another case is bound by the one spelling the file system has', (t) => {
    const { root, app, locations } = layout('case');
    if (!existsSync(join(root, 'APP'))) {
      t.skip('this volume is case-sensitive');
      return;
    }
    const upper = locations.bind({ kind: 'existing_folder', root, folder_name: 'APP' });
    const lower = locations.bind({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.deepEqual(upper, lower);
    assert.deepEqual(upper, { ok: true, location: { path: app, name: 'app', created: false } });
    // The same folder by a new name is taken, whatever its case.
    const taken = locations.check({ kind: 'new_folder', root, folder_name: 'App' });
    assert.equal(taken.ok ? 'ok' : taken.code, 'location_exists');
  });

  test('a root is named as the host lists it, or as it was configured', () => {
    const top = join(base, 'configured');
    const real = join(top, 'real');
    mkdirSync(join(real, 'app'), { recursive: true });
    const link = join(top, 'link');
    symlinkSync(real, link);
    const locations = createHostLocations([link]);
    assert.equal(locations.list().roots[0]?.path, real);
    for (const root of [real, link]) {
      assert.equal(locations.check({ kind: 'existing_folder', root, folder_name: 'app' }).ok, true);
    }
  });

  test('what is not one of the host roots, or not directly inside one, is not allowed', () => {
    const { root, outside, locations } = layout('not-allowed');
    symlinkSync(outside, join(root, 'escape'));
    const refusals: [Parameters<typeof locations.check>[0], string][] = [
      [{ kind: 'existing_folder', root: outside, folder_name: null }, 'location_not_allowed'],
      [
        { kind: 'existing_folder', root: join(root, 'app'), folder_name: null },
        'location_not_allowed',
      ],
      [{ kind: 'existing_folder', root, folder_name: 'escape' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: '..' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: '.hidden' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: 'app/../..' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: 'missing' }, 'location_missing'],
    ];
    writeFileSync(join(root, 'a-file'), 'x');
    refusals.push([{ kind: 'existing_folder', root, folder_name: 'a-file' }, 'location_missing']);
    for (const [choice, code] of refusals) {
      const checked = locations.check(choice);
      assert.equal(checked.ok ? 'ok' : checked.code, code, JSON.stringify(choice));
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, code, JSON.stringify(choice));
      assert.ok(!bound.ok && bound.message.length > 0);
    }
  });

  test('with no roots configured nothing is allowed, and the refusal says the owner must allow one', () => {
    const { root } = layout('no-roots');
    const checked = createHostLocations([]).check({
      kind: 'existing_folder',
      root,
      folder_name: 'app',
    });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    assert.match(checked.ok ? '' : checked.message, /HALCYONIC_PROJECT_ROOTS/);
  });

  test('a root that became unreadable lists as missing and refuses, and nothing throws', (t) => {
    const top = join(base, 'unreadable');
    const root = join(top, 'projects');
    mkdirSync(join(root, 'app'), { recursive: true });
    const locations = createHostLocations([root]);
    // Without search permission on its parent, every look at the root fails with EACCES.
    chmodSync(top, 0o000);
    t.after(() => chmodSync(top, 0o755));
    assert.deepEqual(locations.list().roots[0]?.status, 'missing');
    for (const choice of [
      { kind: 'existing_folder', root, folder_name: 'app' },
      { kind: 'new_folder', root, folder_name: 'fresh' },
    ] as const) {
      const checked = locations.check(choice);
      assert.equal(checked.ok ? 'ok' : checked.code, 'location_missing', choice.kind);
      assert.match(checked.ok ? '' : checked.message, /cannot be read \(EACCES\)/);
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_missing', choice.kind);
    }
    const decision = locations.policy(join(root, 'app'));
    assert.equal(decision.ok ? 'ok' : decision.code, 'location_missing');
  });

  test('a refusal quotes at most the start of a very long root', () => {
    const { locations } = layout('long-root');
    const checked = locations.check({
      kind: 'existing_folder',
      root: `/${'a'.repeat(4000)}`,
      folder_name: null,
    });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    assert.ok(!checked.ok && checked.message.length < 400);
  });

  test('a root that has gone refuses its folders as missing', () => {
    const { root, locations } = layout('root-gone');
    rmSync(root, { recursive: true });
    const checked = locations.check({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_missing');
  });
});

describe('binding a project to a new folder', () => {
  test('the host makes the folder directly inside the root', () => {
    const { root, locations } = layout('new');
    const choice = { kind: 'new_folder', root, folder_name: 'storefront' } as const;
    assert.deepEqual(locations.check(choice), { ok: true });
    assert.equal(existsSync(join(root, 'storefront')), false, 'checking makes nothing');
    assert.deepEqual(locations.bind(choice), {
      ok: true,
      location: { path: join(root, 'storefront'), name: 'storefront', created: true },
    });
    assert.ok(statSync(join(root, 'storefront')).isDirectory());
  });

  test('a name already taken, by a folder, a file or a link, is refused and nothing is made', () => {
    const { root, outside, locations } = layout('taken');
    writeFileSync(join(root, 'notes'), 'x');
    symlinkSync(join(outside, 'nowhere'), join(root, 'dangling'));
    for (const name of ['app', 'notes', 'dangling']) {
      const bound = locations.bind({ kind: 'new_folder', root, folder_name: name });
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_exists', name);
    }
    assert.equal(existsSync(join(outside, 'nowhere')), false, 'the link was not followed');
  });

  test("a name that is not a plain folder name, or a root that is not the host's, is not allowed", () => {
    const { root, outside, locations } = layout('bad-name');
    for (const [folderName, parent] of [
      ['..', root],
      ['.hidden', root],
      ['a/b', root],
      ['with space', root],
      ['fine', outside],
      ['fine', join(root, 'app')],
    ] as const) {
      const checked = locations.check({
        kind: 'new_folder',
        root: parent,
        folder_name: folderName,
      });
      assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed', folderName);
    }
    assert.equal(existsSync(join(outside, 'fine')), false);
  });

  test('a root replaced by a symbolic link after startup is refused, and nothing is made through it', () => {
    const { root, outside, locations } = layout('root-linked');
    renameSync(root, `${root}-moved`);
    symlinkSync(outside, root);
    for (const choice of [
      { kind: 'new_folder', root, folder_name: 'escape' },
      { kind: 'existing_folder', root, folder_name: null },
    ] as const) {
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_not_allowed', choice.kind);
    }
    assert.equal(existsSync(join(outside, 'escape')), false, 'no folder outside the roots');
    assert.equal(locations.list().roots[0]?.status, 'missing');
    assert.deepEqual(locations.list().roots[0]?.folders, []);
  });

  test('a root replaced by another folder at the same path is refused until the control plane restarts', () => {
    const { root, locations } = layout('root-replaced');
    renameSync(root, `${root}-old`);
    mkdirSync(join(root, 'app'), { recursive: true });
    const checked = locations.check({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    // A control plane started now takes the folder that is there.
    assert.equal(
      createHostLocations([root]).check({ kind: 'existing_folder', root, folder_name: 'app' }).ok,
      true,
    );
  });

  test('a folder made but not usable is reported with an unknown effect and its real path', () => {
    const { root, outside } = layout('made-unusable');
    // Stands in for a root swapped for a link between its check and the mkdir: once made, the
    // folder is reached through a link to where it really is, and the policy refuses it.
    const locations = createHostLocations([root], (path) => {
      renameSync(path, join(outside, 'storefront'));
      symlinkSync(join(outside, 'storefront'), path);
      return { ok: false, code: 'location_not_allowed', message: `${path} is not allowed now.` };
    });
    const bound = locations.bind({ kind: 'new_folder', root, folder_name: 'storefront' });
    assert.equal(bound.ok ? 'ok' : bound.code, 'location_not_created');
    assert.equal(!bound.ok && 'effect' in bound ? bound.effect : null, 'unknown');
    assert.ok(
      !bound.ok && bound.message.startsWith(`A folder was made at ${join(outside, 'storefront')},`),
      'the message names where the folder really is',
    );
  });

  test('after a crash between making the folder and recording the project, the folder is chosen as existing', () => {
    const { root, locations } = layout('crash');
    const fresh = { kind: 'new_folder', root, folder_name: 'storefront' } as const;
    // The first attempt made the folder, then the control plane died before recording anything.
    assert.equal(locations.bind(fresh).ok, true);
    const retried = locations.check(fresh);
    assert.equal(retried.ok ? 'ok' : retried.code, 'location_exists');
    assert.match(retried.ok ? '' : retried.message, /Choose it as an existing folder/);
    assert.deepEqual(locations.bind({ kind: 'existing_folder', root, folder_name: 'storefront' }), {
      ok: true,
      location: { path: join(root, 'storefront'), name: 'storefront', created: false },
    });
  });
});
