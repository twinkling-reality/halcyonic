// Halcyonic's host-owned Vite config for a project made from the IWSDK starter (M1.1 trial, MAKE lane).
// The project's own files never configure Vite: this file is passed with --config, lives beside the vetted
// runtime set, and loads no plugin from the project. It serves on loopback only, plain HTTP, with headers
// that keep the page to its own origin.
//
// Environment: HALCYONIC_PREVIEW_ROOT (the project folder), HALCYONIC_PREVIEW_PORT, HALCYONIC_PREVIEW_CACHE
// (a folder outside the project for Vite's cache), HALCYONIC_PREVIEW_DEPS (the host's own path to the shared
// dependencies; folders separated by `:`), and optionally HALCYONIC_PREVIEW_PROFILES (the host's folder of
// controller models listed in input-profiles.json; without it a controller shows no model).
import { existsSync, readFileSync, realpathSync } from 'node:fs';
import { isAbsolute, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assertValidIwsdkProjectManifest } from '@iwsdk/core/project';
import { defineConfig } from 'vite';
import {
  buildFiles,
  isProfilesModule,
  PROFILES_PREFIX,
  profilesMiddleware,
  readManifest,
  rewriteProfilesModule,
} from './input-profiles.mjs';
import { freshOrigin } from './fresh-origin.mjs';
import { refuseServiceWorkers } from './service-workers.mjs';

const root = process.env.HALCYONIC_PREVIEW_ROOT;
const port = Number(process.env.HALCYONIC_PREVIEW_PORT);
const cacheDir = process.env.HALCYONIC_PREVIEW_CACHE;
// The shared dependencies as the host installed them (folders separated by `:`), named by the host, never read
// from the project's own node_modules link.
const deps = (process.env.HALCYONIC_PREVIEW_DEPS ?? '').split(':').filter((path) => path !== '');
if (!root || !Number.isInteger(port) || !cacheDir || deps.length === 0)
  throw new Error('preview root, port, cache and dependencies are required');
const allowed = [root, ...deps, cacheDir].map((path) => realpathSync(path));
const profiles = process.env.HALCYONIC_PREVIEW_PROFILES || null;
const profilesListing = readManifest(fileURLToPath(new URL('./input-profiles.json', import.meta.url)));

const csp = [
  "default-src 'self'",
  "script-src 'self' 'wasm-unsafe-eval'",
  // data: for yoga-layout's WASM, which the SDK's panels inline and fetch as a data: URL; no network.
  `connect-src 'self' data: ws://localhost:${port} ws://127.0.0.1:${port}`,
  "img-src 'self' data: blob:",
  "media-src 'self' blob:",
  "worker-src 'self' blob:",
  "style-src 'self' 'unsafe-inline'",
  "form-action 'none'",
  "frame-ancestors 'none'",
].join('; ');

const PROJECT_ID = 'virtual:iwsdk-project';
const RESOLVED = '\0virtual:iwsdk-project';

/** The project's World options from its iwsdk.config.json, as Meta's dev plugin builds them. */
function iwsdkProject() {
  return {
    name: 'halcyonic-iwsdk-project',
    resolveId(id) {
      return id === PROJECT_ID ? RESOLVED : null;
    },
    load(id) {
      if (id !== RESOLVED) return null;
      const manifest = JSON.parse(readFileSync(join(root, 'iwsdk.config.json'), 'utf8'));
      assertValidIwsdkProjectManifest(manifest);
      const module = (declared) => {
        const base = join(root, declared.module.replace(/^\.\//, ''));
        const found = ['.ts', '.js'].map((ext) => base + ext).filter((path) => existsSync(path));
        if (found.length !== 1) throw new Error(`expected one source for ${declared.module}`);
        return `/${found[0].slice(root.length + 1)}`;
      };
      const bindings = ['level'];
      const imports = [`import { normalizeProjectWorldOptions } from '@iwsdk/core/project';`];
      if (manifest.assets) {
        imports.push(`import assets from ${JSON.stringify(module(manifest.assets))};`);
        bindings.push('assets');
      }
      if (manifest.components) {
        imports.push(`import components from ${JSON.stringify(module(manifest.components))};`);
        bindings.push('components');
      }
      return [
        ...imports,
        `const manifest = ${JSON.stringify(manifest)};`,
        'const normalized = normalizeProjectWorldOptions(manifest);',
        "const level = import.meta.env.BASE_URL + normalized.level.replace(/^\\.\\//, '');",
        `export default { ...normalized, ${bindings.join(', ')} };`,
      ].join('\n');
    },
  };
}

/**
 * Refuses, for the build and the dev server alike, any file whose real path is outside the project, the shared
 * dependencies and the cache: an import like `../../.ssh/id_rsa?raw` would otherwise put any readable file into
 * the page, and the build does not apply the dev server's own file rules.
 */
function confine() {
  return {
    name: 'halcyonic-confine',
    enforce: 'pre',
    load(id) {
      if (id.startsWith('\0') || id.startsWith('virtual:')) return null;
      const file = id.split('?')[0];
      if (!isAbsolute(file)) return null;
      let real;
      try {
        real = realpathSync(file);
      } catch {
        return null;
      }
      if (allowed.some((base) => real === base || real.startsWith(base + sep))) return null;
      throw new Error('The app may only load files from its own folder and its dependencies.');
    },
  };
}

/**
 * Controller models from the preview's own origin, never the CDN: the one module naming cdn.jsdelivr.net is
 * rewritten (by this plugin for the build, by the esbuild plugin below for the dev server's pre-bundling), and only
 * the listed, verified files are served or added to the build.
 */
function inputProfiles() {
  return {
    name: 'halcyonic-input-profiles',
    transform(code, id) {
      return isProfilesModule(id) ? { code: rewriteProfilesModule(code), map: null } : null;
    },
    configureServer(server) {
      server.middlewares.use(freshOrigin);
      server.middlewares.use(refuseServiceWorkers);
      server.middlewares.use(`/${PROFILES_PREFIX}`, profilesMiddleware(profiles, profilesListing));
    },
    configurePreviewServer(server) {
      server.middlewares.use(freshOrigin);
      server.middlewares.use(refuseServiceWorkers);
      server.middlewares.use(`/${PROFILES_PREFIX}`, profilesMiddleware(profiles, profilesListing));
    },
    generateBundle() {
      for (const { fileName, source } of buildFiles(profiles, profilesListing)) {
        this.emitFile({ type: 'asset', fileName, source });
      }
    },
  };
}

const prebundledProfiles = {
  name: 'halcyonic-input-profiles',
  setup(build) {
    build.onLoad({ filter: /input-profiles\.js$/ }, (args) =>
      isProfilesModule(args.path)
        ? { contents: rewriteProfilesModule(readFileSync(args.path, 'utf8')), loader: 'js' }
        : undefined,
    );
  },
};

export default defineConfig({
  root,
  cacheDir,
  configFile: false,
  envDir: cacheDir,
  publicDir: 'public',
  base: './',
  plugins: [confine(), iwsdkProject(), inputProfiles()],
  // Inline, so Vite never looks for the project's own postcss.config.*, which would run on the Mac.
  css: { postcss: { plugins: [] } },
  server: {
    host: '127.0.0.1',
    port,
    strictPort: true,
    open: false,
    headers: { 'Content-Security-Policy': csp, 'Cache-Control': 'no-store' },
    // The project's own files, and the shared dependencies the host names.
    fs: { strict: true, allow: [root, ...deps] },
  },
  preview: { host: '127.0.0.1', port, strictPort: true, headers: { 'Content-Security-Policy': csp, 'Cache-Control': 'no-store' } },
  build: { target: 'esnext', sourcemap: false, rollupOptions: { input: join(root, 'index.html') } },
  esbuild: { target: 'esnext' },
  resolve: { dedupe: ['three', '@pmndrs/uikit', '@pmndrs/uikit-horizon', '@pmndrs/uikit-lucide'] },
  optimizeDeps: {
    exclude: ['@babylonjs/havok'],
    include: ['three', '@pmndrs/uikit', '@pmndrs/uikit-horizon', '@pmndrs/uikit-lucide', '@drawcall/uikitml'],
    esbuildOptions: { target: 'esnext', plugins: [prebundledProfiles] },
  },
});
