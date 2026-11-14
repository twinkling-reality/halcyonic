/**
 * Enforces the dependency rules in docs/internal/architecture/SYSTEM.md. The core packages may
 * depend only on what is listed here: no runtime vendor, no storage, no transport, no framework.
 * Adding to an allowlist is an architectural change and needs the matching documentation change.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

interface Rule {
  /** Declared dependencies allowed in package.json. */
  readonly dependencies: readonly string[];
  /** Module specifiers production source may import, besides relative paths. */
  readonly imports: readonly string[];
}

const RULES: Readonly<Record<string, Rule>> = {
  'packages/contracts': { dependencies: ['typebox'], imports: ['typebox', 'typebox/schema'] },
  'packages/domain': { dependencies: ['@halcyonic/contracts'], imports: ['@halcyonic/contracts'] },
  'packages/runtime-core': {
    dependencies: ['@halcyonic/contracts'],
    imports: ['@halcyonic/contracts', 'node:timers/promises'],
  },
  // ADR 0048: the closed sandbox profile every runtime shares holds no runtime and no framework; the
  // secret-shaped files' rules it shares with the project reads come from runtime-core.
  'packages/seatbelt': {
    dependencies: ['@halcyonic/runtime-core'],
    // node:http, node:https and node:net for the key gate alone: the sandbox's one way out, holding the
    // person's key for every confined agent app (ADR 0048, amended 2026-11-12). node:child_process for the
    // leftovers sweep alone: lsof and ps by absolute path, no shell (SECURITY.md, what a command leaves running).
    imports: [
      '@halcyonic/runtime-core',
      'node:child_process',
      'node:crypto',
      'node:fs',
      'node:http',
      'node:https',
      'node:net',
      'node:path',
    ],
  },
};

const IMPORT =
  /(?:^|\s)(?:import|export)\s(?:[^'"]*?\sfrom\s)?['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)/g;

function productionSources(directory: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name !== 'testing' && entry.name !== 'node_modules')
        files.push(...productionSources(path));
    } else if (entry.name.endsWith('.ts') && !entry.name.endsWith('.test.ts')) {
      files.push(path);
    }
  }
  return files;
}

function specifiers(source: string): string[] {
  return [...source.matchAll(IMPORT)].map((match) => match[1] ?? match[2] ?? '');
}

describe('core package boundaries', () => {
  for (const [pkg, rule] of Object.entries(RULES)) {
    test(`${pkg} declares only its allowed dependencies`, () => {
      const manifest = JSON.parse(readFileSync(join(ROOT, pkg, 'package.json'), 'utf8')) as {
        dependencies?: Record<string, string>;
        devDependencies?: Record<string, string>;
      };
      assert.deepEqual(
        Object.keys(manifest.dependencies ?? {}).sort(),
        [...rule.dependencies].sort(),
      );
      assert.equal(manifest.devDependencies, undefined, 'core packages have no dev dependencies');
    });

    test(`${pkg} imports only its allowed modules`, () => {
      const violations: string[] = [];
      for (const file of productionSources(join(ROOT, pkg, 'src'))) {
        for (const specifier of specifiers(readFileSync(file, 'utf8'))) {
          if (specifier.startsWith('.') || rule.imports.includes(specifier)) continue;
          violations.push(`${file.slice(ROOT.length)} imports ${specifier}`);
        }
      }
      assert.deepEqual(violations, []);
    });
  }

  // AUDIT3 A1 (2026-11-14): seatbelt runs a process only to end a run's leftovers, from one file.
  test('packages/seatbelt runs a process only from its leftovers sweep', () => {
    const running = productionSources(join(ROOT, 'packages/seatbelt/src'))
      .filter((file) => specifiers(readFileSync(file, 'utf8')).includes('node:child_process'))
      .map((file) => file.slice(ROOT.length));
    assert.deepEqual(running, ['packages/seatbelt/src/leftovers.ts']);
  });

  // ADR 0061: no vendor's name in the core. Each declared provider's id and shown name stay out of every core
  // package, read from the declarations themselves, so a third provider is scanned with no edit here; and every
  // agent app's integration name stays out of runtime-core, the adapters' and providers' shared shape.
  const providerNames = readdirSync(join(ROOT, 'providers'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .flatMap((entry) => {
      const declared = JSON.parse(
        readFileSync(join(ROOT, 'providers', entry.name, 'provider.json'), 'utf8'),
      ) as { id: string; name: string };
      return [declared.id, declared.name];
    });
  const integrationNames = readdirSync(join(ROOT, 'packages/integrations'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name !== 'mock')
    .flatMap((entry) => [entry.name, entry.name.split('-')[0] ?? entry.name]);
  const named = (names: readonly string[], pkg: string): string[] => {
    const words = [...new Set(names.map((name) => name.toLowerCase()))];
    const found: string[] = [];
    for (const file of productionSources(join(ROOT, pkg, 'src'))) {
      const source = readFileSync(file, 'utf8').toLowerCase();
      for (const word of words)
        if (new RegExp(`(?<![a-z0-9])${word.replace(/[-.]/g, '\\$&')}(?![a-z0-9])`).test(source))
          found.push(`${file.slice(ROOT.length)} names ${word}`);
    }
    return found;
  };
  for (const pkg of Object.keys(RULES)) {
    test(`${pkg} names no declared provider`, () => {
      assert.ok(providerNames.length > 0, 'the declarations are read');
      assert.deepEqual(named(providerNames, pkg), []);
    });
  }
  test('packages/runtime-core names no agent app', () => {
    assert.ok(integrationNames.includes('opencode'), 'the integrations are read');
    assert.deepEqual(named(integrationNames, 'packages/runtime-core'), []);
  });

  test('the import scanner sees real imports', () => {
    const found = specifiers(readFileSync(join(ROOT, 'packages/domain/src/projection.ts'), 'utf8'));
    assert.ok(found.includes('@halcyonic/contracts'));
    assert.ok(found.includes('./attention.ts'));
  });
});
