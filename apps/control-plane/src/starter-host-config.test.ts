import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';

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
    assert.match(source, /plugins: \[confine\(\), iwsdkProject\(\), inputProfiles\(\)\]/);
  });

  test('keeps the page to its own origin', () => {
    assert.match(source, /"default-src 'self'"/);
    assert.match(
      source,
      /`connect-src 'self' data: ws:\/\/localhost:\$\{port\} ws:\/\/127\.0\.0\.1:\$\{port\}`/,
    );
  });
});

interface ListedFile {
  readonly path: string;
  readonly size: number;
  readonly sha256: string;
}
interface Listing {
  readonly format: 1;
  readonly notice: ListedFile;
  readonly files: readonly ListedFile[];
}
interface Answer {
  statusCode: number;
  readonly headers: Record<string, string>;
  body: Buffer | undefined;
  setHeader(name: string, value: string): void;
  end(body?: Buffer): void;
}
interface ProfilesModule {
  readonly PROFILES_PREFIX: string;
  isProfilesModule(path: string): boolean;
  rewriteProfilesModule(code: string): string;
  readManifest(path: string): Listing;
  readVerified(folder: string, file: ListedFile): Buffer | null;
  buildFiles(folder: string | null, listing: Listing): { fileName: string; source: Buffer }[];
  profilesMiddleware(
    folder: string | null,
    listing: Listing,
  ): (req: { url: string; method: string }, res: Answer) => void;
}

// The serving rules have no dependency beyond Node, so they run here as they run on the Mac.
const hostFolder = new URL('../../../starters/iwsdk-vr/host/', import.meta.url);
const profiles = (await import(new URL('input-profiles.mjs', hostFolder).href)) as ProfilesModule;
const listingPath = new URL('input-profiles.json', hostFolder).pathname;
const CDN =
  "const DEFAULT_PROFILES_PATH = 'https://cdn.jsdelivr.net/npm/@webxr-input-profiles/assets@1.0/dist/profiles';";

const sha256 = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');
const scratch = mkdtempSync(join(tmpdir(), 'halcyonic-input-profiles-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

/** A host folder holding one model and the notice, with a listing that names them. */
function hostWith(model: Buffer, listed: Buffer = model): { folder: string; listing: Listing } {
  const folder = mkdtempSync(join(scratch, 'host-'));
  mkdirSync(join(folder, 'dist/profiles/meta-quest-touch-plus'), { recursive: true });
  writeFileSync(join(folder, 'dist/profiles/meta-quest-touch-plus/left.glb'), model);
  writeFileSync(join(folder, 'LICENSE.md'), 'MIT');
  const notice = Buffer.from('MIT');
  return {
    folder,
    listing: {
      format: 1,
      notice: { path: 'LICENSE.md', size: notice.length, sha256: sha256(notice) },
      files: [
        {
          path: 'dist/profiles/meta-quest-touch-plus/left.glb',
          size: listed.length,
          sha256: sha256(listed),
        },
      ],
    },
  };
}

function ask(
  handler: ReturnType<ProfilesModule['profilesMiddleware']>,
  url: string,
  method = 'GET',
): Answer {
  const res: Answer = {
    statusCode: 200,
    headers: {},
    body: undefined,
    setHeader(name, value) {
      this.headers[name] = value;
    },
    end(body) {
      this.body = body;
    },
  };
  handler({ url, method }, res);
  return res;
}

describe("the IWSDK starter's controller models (from the host, never the CDN)", () => {
  test('the host config rewrites the module for the build and for the pre-bundled dev server, and serves the listing', () => {
    assert.match(source, /plugins: \[confine\(\), iwsdkProject\(\), inputProfiles\(\)\]/);
    assert.match(source, /esbuildOptions: \{ target: 'esnext', plugins: \[prebundledProfiles\] \}/);
    assert.match(source, /process\.env\.HALCYONIC_PREVIEW_PROFILES/);
    assert.match(
      source,
      /configureServer\(server\) \{\n\s+server\.middlewares\.use\(`\/\$\{PROFILES_PREFIX\}`/,
    );
    assert.match(
      source,
      /configurePreviewServer\(server\) \{\n\s+server\.middlewares\.use\(`\/\$\{PROFILES_PREFIX\}`/,
    );
  });

  test("rewrites only xr-input's profiles module, and refuses one whose constant changed", () => {
    assert.ok(
      profiles.isProfilesModule(
        '/x/node_modules/@iwsdk/xr-input/dist/gamepad/input-profiles.js?v=1',
      ),
    );
    assert.ok(!profiles.isProfilesModule('/x/node_modules/@iwsdk/core/dist/input-profiles.js'));
    assert.ok(!profiles.isProfilesModule('/project/src/input-profiles.js'));
    const rewritten = profiles.rewriteProfilesModule(`a;\n${CDN}\nb;`);
    assert.ok(!rewritten.includes('cdn.jsdelivr.net'));
    assert.ok(rewritten.includes(`'./${profiles.PROFILES_PREFIX}'`));
    assert.throws(() =>
      profiles.rewriteProfilesModule('const DEFAULT_PROFILES_PATH = "elsewhere";'),
    );
    assert.throws(() => profiles.rewriteProfilesModule(`${CDN}\n${CDN}`));
  });

  test('the tracked listing names the Quest controller and hand models and the notice, and nothing else', () => {
    const listing = profiles.readManifest(listingPath);
    assert.deepEqual(
      listing.files.map((file) => file.path),
      [
        'dist/profiles/meta-quest-touch-plus/left.glb',
        'dist/profiles/meta-quest-touch-plus/right.glb',
        'dist/profiles/meta-quest-touch-plus-v2/left.glb',
        'dist/profiles/meta-quest-touch-plus-v2/right.glb',
        'dist/profiles/generic-hand/left.glb',
        'dist/profiles/generic-hand/right.glb',
      ],
    );
    assert.equal(listing.notice.path, 'LICENSE.md');
    for (const bad of ['dist/profiles/../../x.glb', 'dist/profiles/a/b.js', '/etc/x.glb']) {
      const file = join(scratch, `listing-${sha256(Buffer.from(bad)).slice(0, 8)}.json`);
      writeFileSync(
        file,
        JSON.stringify({ ...listing, files: [{ path: bad, size: 1, sha256: '0'.repeat(64) }] }),
      );
      assert.throws(() => profiles.readManifest(file), /unknown input profiles listing/);
    }
  });

  test('reads a model only while it is a regular file of the listed size and SHA-256', () => {
    const model = Buffer.from('glTF model bytes');
    const good = hostWith(model);
    const file = good.listing.files[0] as ListedFile;
    assert.deepEqual(profiles.readVerified(good.folder, file), model);
    // Same size, one byte changed.
    const changed = hostWith(Buffer.from('glTF model bytez'), model);
    assert.equal(profiles.readVerified(changed.folder, file), null);
    const longer = hostWith(Buffer.from('glTF model bytes!'), model);
    assert.equal(profiles.readVerified(longer.folder, file), null);
    // A link in the model's place is never followed.
    const linked = hostWith(model);
    const target = join(linked.folder, 'elsewhere.glb');
    writeFileSync(target, model);
    rmSync(join(linked.folder, file.path));
    symlinkSync(target, join(linked.folder, file.path));
    assert.equal(profiles.readVerified(linked.folder, file), null);
    const folderInstead = hostWith(model);
    rmSync(join(folderInstead.folder, file.path));
    mkdirSync(join(folderInstead.folder, file.path));
    assert.equal(profiles.readVerified(folderInstead.folder, file), null);
  });

  test('serves a listed, verified model as a glTF binary, and answers 404 to everything else', () => {
    const model = Buffer.from('glTF model bytes');
    const { folder, listing } = hostWith(model);
    const serve = profiles.profilesMiddleware(folder, listing);
    const answer = ask(serve, '/meta-quest-touch-plus/left.glb?import');
    assert.equal(answer.statusCode, 200);
    assert.equal(answer.headers['Content-Type'], 'model/gltf-binary');
    assert.deepEqual(answer.body, model);
    for (const url of [
      '/meta-quest-touch-plus/right.glb',
      '/../LICENSE.md',
      '/meta-quest-touch-plus/../meta-quest-touch-plus/left.glb',
      '/dist/profiles/meta-quest-touch-plus/left.glb',
      '/meta-quest-touch-plus%2Fleft.glb',
    ]) {
      assert.equal(ask(serve, url).statusCode, 404, url);
    }
    assert.equal(ask(serve, '/meta-quest-touch-plus/left.glb', 'POST').statusCode, 404);
    assert.equal(
      ask(profiles.profilesMiddleware(null, listing), '/meta-quest-touch-plus/left.glb').statusCode,
      404,
    );
    const tampered = hostWith(Buffer.from('glTF model bytez'), model);
    assert.equal(
      ask(profiles.profilesMiddleware(tampered.folder, listing), '/meta-quest-touch-plus/left.glb')
        .statusCode,
      404,
    );
  });

  test('the build carries each verified model at the address the page asks for, with the notice beside them', () => {
    const model = Buffer.from('glTF model bytes');
    const { folder, listing } = hostWith(model);
    assert.deepEqual(
      profiles.buildFiles(folder, listing).map(({ fileName }) => fileName),
      [
        `${profiles.PROFILES_PREFIX}/meta-quest-touch-plus/left.glb`,
        `${profiles.PROFILES_PREFIX}/LICENSE.md`,
      ],
    );
    assert.deepEqual(profiles.buildFiles(null, listing), []);
    const tampered = hostWith(Buffer.from('glTF model bytez'), model);
    assert.deepEqual(profiles.buildFiles(tampered.folder, listing), []);
  });
});
