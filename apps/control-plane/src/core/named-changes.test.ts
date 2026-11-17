import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { withoutHostNaming } from './named-changes.ts';

const CARD = '<text class="card-title">Untitled Game</text>\n';
const TITLE = '<title>Untitled Game</title>\n';

function folders(t: { after: (f: () => void) => void }) {
  const root = mkdtempSync(join(tmpdir(), 'halcyonic-named-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const template = join(root, 'template');
  const folder = join(root, 'project');
  for (const where of [template, folder])
    mkdirSync(join(where, 'public', 'ui'), { recursive: true });
  writeFileSync(join(template, 'index.html'), TITLE);
  writeFileSync(join(template, 'public', 'ui', 'welcome.uikitml'), CARD);
  return { template, folder };
}

const both = [
  { path: 'index.html', change: 'changed' as const },
  { path: 'public/ui/welcome.uikitml', change: 'changed' as const },
];

test("a fresh project's two named files, the host's own naming, are no change of the project's", async (t) => {
  const { template, folder } = folders(t);
  writeFileSync(join(folder, 'index.html'), '<title>Critter Row</title>\n');
  writeFileSync(
    join(folder, 'public', 'ui', 'welcome.uikitml'),
    CARD.replace('Untitled Game', 'Critter Row'),
  );
  assert.deepEqual(await withoutHostNaming(both, template, folder, 'Critter Row'), []);
});

test('a named file the agent changed beyond the name still counts, as does every other file', async (t) => {
  const { template, folder } = folders(t);
  writeFileSync(
    join(folder, 'index.html'),
    '<title>Critter Row</title>\n<script src="music.js"></script>\n',
  );
  writeFileSync(
    join(folder, 'public', 'ui', 'welcome.uikitml'),
    CARD.replace('Untitled Game', 'Critter Row'),
  );
  const changes = [...both, { path: 'src/score.ts', change: 'changed' as const }];
  assert.deepEqual(await withoutHostNaming(changes, template, folder, 'Critter Row'), [
    { path: 'index.html', change: 'changed' },
    { path: 'src/score.ts', change: 'changed' },
  ]);
});

test('a project named before naming, a name with markup, an added or removed file, and a link all stay as read', async (t) => {
  const { template, folder } = folders(t);
  // Escaped as namedFile writes it: the comparison is with the template as the host applied it.
  writeFileSync(join(folder, 'index.html'), '<title>Tom &amp; Jerry</title>\n');
  symlinkSync(
    join(template, 'public', 'ui', 'welcome.uikitml'),
    join(folder, 'public', 'ui', 'welcome.uikitml'),
  );
  assert.deepEqual(await withoutHostNaming(both, template, folder, 'Tom & Jerry'), [
    { path: 'public/ui/welcome.uikitml', change: 'changed' },
  ]);
  const kinds = [
    { path: 'index.html', change: 'added' as const },
    { path: 'public/ui/welcome.uikitml', change: 'removed' as const },
  ];
  assert.deepEqual(await withoutHostNaming(kinds, template, folder, 'Tom & Jerry'), kinds);
  assert.equal(await withoutHostNaming(null, template, folder, 'Tom & Jerry'), null);
});
