// Halcyonic's host-owned Vite config for a project made from the IWSDK starter (M1.1 trial, MAKE lane).
// The project's own files never configure Vite: this file is passed with --config, lives beside the vetted
// runtime set, and loads no plugin from the project. It serves on loopback only, plain HTTP, with headers
// that keep the page to its own origin.
//
// Environment: HALCYONIC_PREVIEW_ROOT (the project folder), HALCYONIC_PREVIEW_PORT, HALCYONIC_PREVIEW_CACHE
// (a folder outside the project for Vite's cache).
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { assertValidIwsdkProjectManifest } from '@iwsdk/core/project';
import { defineConfig } from 'vite';

const root = process.env.HALCYONIC_PREVIEW_ROOT;
const port = Number(process.env.HALCYONIC_PREVIEW_PORT);
const cacheDir = process.env.HALCYONIC_PREVIEW_CACHE;
if (!root || !Number.isInteger(port) || !cacheDir) throw new Error('preview root, port and cache are required');

const csp = [
  "default-src 'self'",
  "script-src 'self' 'wasm-unsafe-eval'",
  `connect-src 'self' ws://localhost:${port} ws://127.0.0.1:${port}`,
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

export default defineConfig({
  root,
  cacheDir,
  configFile: false,
  envDir: cacheDir,
  publicDir: 'public',
  base: './',
  plugins: [iwsdkProject()],
  server: {
    host: '127.0.0.1',
    port,
    strictPort: true,
    open: false,
    headers: { 'Content-Security-Policy': csp },
    fs: { strict: true, allow: [root, join(import.meta.dirname, '..', 'node_modules')] },
  },
  preview: { host: '127.0.0.1', port, strictPort: true, headers: { 'Content-Security-Policy': csp } },
  build: { target: 'esnext', sourcemap: false, rollupOptions: { input: join(root, 'index.html') } },
  esbuild: { target: 'esnext' },
  resolve: { dedupe: ['three', '@pmndrs/uikit', '@pmndrs/uikit-horizon', '@pmndrs/uikit-lucide'] },
  optimizeDeps: {
    exclude: ['@babylonjs/havok'],
    include: ['three', '@pmndrs/uikit', '@pmndrs/uikit-horizon', '@pmndrs/uikit-lucide', '@drawcall/uikitml'],
    esbuildOptions: { target: 'esnext' },
  },
});
