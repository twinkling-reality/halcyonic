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

  test('the import scanner sees real imports', () => {
    const found = specifiers(readFileSync(join(ROOT, 'packages/domain/src/projection.ts'), 'utf8'));
    assert.ok(found.includes('@halcyonic/contracts'));
    assert.ok(found.includes('./attention.ts'));
  });
});
