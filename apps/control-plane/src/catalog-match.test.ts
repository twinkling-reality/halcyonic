import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { LocationsResponse } from '@halcyonic/contracts';
import { matchFolders, originFullName, originUrl, readOrigin } from './catalog-match.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-catalog-match-')));
after(() => rmSync(base, { recursive: true, force: true }));
// Where a credential would sit in a remote's URL; not in any real credential's shape, so no scanner takes it for one.
const TOKEN = 'synthetic-credential-never-real';

/** A folder with a `.git` directory whose config is `config`. */
function repository(name: string, config: string): string {
  const folder = join(base, name);
  mkdirSync(join(folder, '.git'), { recursive: true });
  writeFileSync(join(folder, '.git', 'config'), config);
  return folder;
}

const origin = (url: string) =>
  `[core]\n\tbare = false\n[remote "origin"]\n\turl = ${url}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n`;

describe("a remote's URL to owner/name (AUDIT G2)", () => {
  test('https, ssh and scp forms on github.com, any credential in them dropped first', () => {
    for (const url of [
      'https://github.com/Acme/App.git',
      'https://github.com/acme/app',
      'https://github.com/acme/app/',
      `https://${TOKEN}@github.com/acme/app.git`,
      `https://user:${TOKEN}@github.com/acme/app.git`,
      'https://github.com:443/acme/app.git',
      'ssh://git@github.com/acme/app.git',
      `ssh://git:${TOKEN}@github.com:22/acme/app`,
      'git@github.com:acme/app.git',
      `${TOKEN}@github.com:acme/app`,
      'GitHub.com:acme/app',
    ])
      assert.equal(originFullName(url), 'acme/app', url);
  });

  test('anything else is no match, never a guess', () => {
    for (const url of [
      'http://github.com/acme/app.git',
      'https://github.com.evil.example/acme/app.git',
      'https://evil.example/github.com/acme/app.git',
      'https://gitlab.com/acme/app.git',
      'git://github.com/acme/app.git',
      'file:///Users/someone/app',
      '/Users/someone/app',
      '../app',
      'https://github.com/acme',
      'https://github.com/acme/app/extra',
      'https://github.com/-acme/app',
      `https://github.com/${'o'.repeat(40)}/app`,
      'https://github.com/acme/..',
      'https://github.com/acme/.',
      'https://github.com/acme/a%2Fb',
      'https://github.com/acme/app name',
      'github.com:acme/app:extra',
      'gh:acme/app',
      '',
    ])
      assert.equal(originFullName(url), null, url);
  });
});

describe('the origin in a .git/config (AUDIT G3)', () => {
  test('only [remote "origin"], with exactly one url', () => {
    assert.equal(
      originUrl(origin('https://github.com/acme/app.git')),
      'https://github.com/acme/app.git',
    );
    assert.equal(
      originUrl('[remote "origin"]\n\turl = "https://github.com/acme/app.git"\n'),
      'https://github.com/acme/app.git',
    );
    assert.equal(
      originUrl('[Remote "origin"]\nURL = https://github.com/acme/app.git\n'),
      'https://github.com/acme/app.git',
    );
    assert.equal(originUrl('[remote "upstream"]\n\turl = https://github.com/acme/app.git\n'), null);
    assert.equal(
      originUrl('[remote "Origin"]\n\turl = https://github.com/acme/app.git\n'),
      null,
      'a subsection is case-sensitive',
    );
    assert.equal(
      originUrl(
        '[remote "origin"]\n\turl = https://github.com/acme/app.git\n\turl = https://github.com/acme/other.git\n',
      ),
      null,
      'several urls are no match',
    );
    assert.equal(originUrl('[core]\n\tbare = false\n'), null);
  });

  test('include, includeIf and insteadOf are not applied', () => {
    const included = `[include]\n\tpath = /tmp/elsewhere\n[includeIf "gitdir:/x/"]\n\tpath = /tmp/other\n${origin('gh:acme/app')}[url "https://github.com/"]\n\tinsteadOf = gh:\n`;
    assert.equal(originUrl(included), 'gh:acme/app');
    assert.equal(originFullName(originUrl(included) ?? ''), null);
  });
});

describe('reading a folder (AUDIT G1)', () => {
  test('a real .git directory with a regular config', async () => {
    assert.equal(
      await readOrigin(repository('plain', origin(`https://${TOKEN}@github.com/acme/app.git`))),
      'acme/app',
    );
  });

  test('a .git file pointing elsewhere, a linked .git or config, or an oversized config is not read', async () => {
    const pointer = join(base, 'worktree');
    mkdirSync(pointer);
    writeFileSync(join(pointer, '.git'), 'gitdir: /somewhere/else\n');
    assert.equal(await readOrigin(pointer), null);

    const real = repository('real-for-link', origin('https://github.com/acme/app.git'));
    const linkedGit = join(base, 'linked-git');
    mkdirSync(linkedGit);
    symlinkSync(join(real, '.git'), join(linkedGit, '.git'));
    assert.equal(await readOrigin(linkedGit), null);

    const linkedConfig = join(base, 'linked-config');
    mkdirSync(join(linkedConfig, '.git'), { recursive: true });
    symlinkSync(join(real, '.git', 'config'), join(linkedConfig, '.git', 'config'));
    assert.equal(await readOrigin(linkedConfig), null);

    const big = repository(
      'big',
      `${origin('https://github.com/acme/app.git')}#${'x'.repeat(70 * 1024)}\n`,
    );
    assert.equal(await readOrigin(big), null);

    const fifo = join(base, 'no-git');
    mkdirSync(fifo);
    assert.equal(await readOrigin(fifo), null);
  });
});

describe('matching the listed folders', () => {
  function listing(...names: string[]): LocationsResponse {
    return {
      roots: [
        {
          path: base,
          name: 'base',
          status: 'available',
          repository: false,
          changed_at: null,
          used_by: [],
          folders_truncated: false,
          folders: names.map((name) => ({
            name,
            path: join(base, name),
            repository: true,
            changed_at: null,
            used_by: [],
          })),
        },
      ],
    };
  }

  test("finds each folder's owner/name, and keeps no URL anywhere", async () => {
    repository('chimes', origin(`https://${TOKEN}@github.com/Lantern-Lab/Wind-Chimes.git`));
    repository('kite', origin('git@github.com:lantern-lab/kite-tails.git'));
    repository('elsewhere', origin('https://gitlab.com/x/y.git'));
    const found = await matchFolders(listing('chimes', 'kite', 'elsewhere'), { protect: [] });
    assert.deepEqual(
      found.map((entry) => [entry.folderName, entry.fullName]),
      [
        ['chimes', 'lantern-lab/wind-chimes'],
        ['kite', 'lantern-lab/kite-tails'],
      ],
    );
    assert.doesNotMatch(JSON.stringify(found), /synthetic|https:|git@/);
  });

  test("skips a folder that holds Halcyonic's own data", async () => {
    const holder = repository('holder', origin('https://github.com/acme/holder.git'));
    mkdirSync(join(holder, 'halcyonic-data'), { recursive: true });
    const found = await matchFolders(listing('holder'), {
      protect: [join(holder, 'halcyonic-data')],
    });
    assert.deepEqual(found, []);
  });

  test('stops at its folder bound and its time budget (AUDIT G4)', async () => {
    for (let index = 0; index < 5; index++)
      repository(`many-${index}`, origin(`https://github.com/acme/many-${index}.git`));
    const names = [0, 1, 2, 3, 4].map((index) => `many-${index}`);
    assert.equal((await matchFolders(listing(...names), { protect: [], maxFolders: 2 })).length, 2);
    let clock = 0;
    const timed = await matchFolders(listing(...names), {
      protect: [],
      budgetMs: 10,
      now: () => (clock += 6),
    });
    assert.ok(timed.length < 5, `stopped after ${timed.length}`);
  });
});
