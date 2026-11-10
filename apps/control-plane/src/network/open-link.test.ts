import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { OPEN_LINK_DIR, openLinkPrivately, sweepOpenLinks } from './open-link.ts';

const LINK = 'http://127.0.0.1:47802/?code=AbC_d-123';

test('a link with a code reaches the browser in a file only its owner reads, never in arguments', async (t) => {
  const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-open-link-'));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const opened: string[] = [];
  const path = openLinkPrivately(dataDir, LINK, { open: (file) => opened.push(file), keepMs: 200 });
  assert.deepEqual(opened, [path]);
  assert.doesNotMatch(path, /AbC/, 'the path tells nothing');
  assert.equal(statSync(path).mode & 0o777, 0o600);
  assert.equal(statSync(join(dataDir, OPEN_LINK_DIR)).mode & 0o777, 0o700);
  assert.match(
    readFileSync(path, 'utf8'),
    /content="0;url=http:\/\/127\.0\.0\.1:47802\/\?code=AbC_d-123"/,
  );
  await new Promise((resolve) => setTimeout(resolve, 400));
  assert.equal(existsSync(path), false, 'removed once the code has run out');
});

test('only a link to this computer is handed over, and old files go at the next call', async (t) => {
  const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-open-link-'));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  for (const link of [
    'https://example.com/?code=x',
    'http://localhost:47802/?code=x',
    'file:///etc/passwd',
  ]) {
    assert.throws(() => openLinkPrivately(dataDir, link, { open: () => {} }), link);
  }
  let now = 1_000_000;
  const first = openLinkPrivately(dataDir, LINK, {
    open: () => {},
    now: () => now,
    keepMs: 60_000,
  });
  now = Date.now() + 120_000;
  openLinkPrivately(dataDir, LINK, { open: () => {}, now: () => now, keepMs: 60_000 });
  assert.equal(existsSync(first), false);
  assert.equal(readdirSync(join(dataDir, OPEN_LINK_DIR)).length, 1);
});

test('the control plane sweeps a hand-over file whose code has run out, which pnpm pair left behind', (t) => {
  const dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-open-link-'));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const path = openLinkPrivately(dataDir, LINK, { open: () => {} });
  sweepOpenLinks(dataDir, { now: () => Date.now() + 30_000 });
  assert.equal(existsSync(path), true, 'kept while its code can be used');
  sweepOpenLinks(dataDir, { now: () => Date.now() + 61_000 });
  assert.equal(existsSync(path), false);
  sweepOpenLinks(join(dataDir, 'nowhere'));
});
