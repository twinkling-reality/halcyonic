import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';

// The host config runs with a starter's installed dependencies, which the repository does not have, so it
// cannot be imported here; its source is checked instead (ADR 0037, AUDIT S3).
const source = readFileSync(
  new URL('../../../starters/iwsdk-vr/host/vite.host.config.mjs', import.meta.url),
  'utf8',
);

describe("the IWSDK starter's host config", () => {
  test("keeps PostCSS inline, so a project's own postcss.config never runs on the Mac", () => {
    assert.match(source, /css: \{ postcss: \{ plugins: \[\] \} \}/);
  });

  test('never loads a config file of the project', () => {
    assert.match(source, /configFile: false/);
    assert.match(source, /envDir: cacheDir/);
  });

  test("serves the project and the dependencies the host names, never where the project's link leads", () => {
    assert.match(source, /process\.env\.HALCYONIC_PREVIEW_DEPS/);
    assert.match(source, /fs: \{ strict: true, allow: \[root, \.\.\.deps\] \}/);
    // The agent controls the project's node_modules link; following it would serve any folder it points at.
    assert.doesNotMatch(source, /join\(root, 'node_modules'\)/);
  });

  test('refuses, for the build and the server, any file outside the project, the dependencies and the cache', () => {
    assert.match(
      source,
      /const allowed = \[root, \.\.\.deps, cacheDir\]\.map\(\(path\) => realpathSync\(path\)\)/,
    );
    assert.match(source, /enforce: 'pre'/);
    assert.match(source, /plugins: \[confine\(\), iwsdkProject\(\)\]/);
  });

  test('keeps the page to its own origin', () => {
    assert.match(source, /"default-src 'self'"/);
    assert.match(
      source,
      /`connect-src 'self' ws:\/\/localhost:\$\{port\} ws:\/\/127\.0\.0\.1:\$\{port\}`/,
    );
  });
});
