import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

// The contracts' generated C# and the client package are compiled together in Unity, and a file that
// imports both namespaces cannot name a type both declare: CS0104 stops every Unity check and build
// (1111-b41, `ProjectFolder`). So no generated contract type may share a simple name with a public
// type of the client package.

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const GENERATED = join(ROOT, 'packages/contracts/csharp/Runtime/HalcyonicContracts.g.cs');
const CLIENT = join(ROOT, 'apps/xr/Packages/com.halcyonic.client/Runtime');

/**
 * The public types a C# source declares at namespace level: in a block namespace (four spaces in) or
 * a file-scoped one (none). Nested types, further in, cannot clash across namespaces.
 */
export function namespaceTypes(source: string): string[] {
  const declaration =
    /^(?: {4})?public\s+(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe|new)\s+)*(?:class|struct|enum|interface|record(?:\s+(?:class|struct))?)\s+([A-Za-z_][A-Za-z0-9_]*)/gm;
  return [...source.matchAll(declaration)].map((match) => match[1] as string);
}

function csharpFiles(folder: string): string[] {
  return readdirSync(folder, { recursive: true, encoding: 'utf8' })
    .filter((name) => name.endsWith('.cs'))
    .map((name) => join(folder, name));
}

/** The names both sources declare, sorted. */
export function clashes(generated: string, client: readonly string[]): string[] {
  const contract = new Set(namespaceTypes(generated));
  return [...new Set(client.flatMap(namespaceTypes))].filter((name) => contract.has(name)).sort();
}

test('the reader finds namespace-level types, and a clash by name', () => {
  const generated = [
    'namespace Halcyonic.Contracts',
    '{',
    '    public sealed class ProjectFolder : HistoryFolder',
    '    {',
    '        public sealed class Nested {}',
    '    }',
    '    public enum Kind { A }',
    '}',
  ].join('\n');
  const client = [
    'namespace Halcyonic.Client',
    '{',
    '    public static class ProjectFolder {}',
    '    internal sealed class Kind {}',
    '    public sealed class Other',
    '    {',
    '        public sealed class Nested {}',
    '    }',
    '}',
  ].join('\n');
  assert.deepEqual(namespaceTypes(generated), ['ProjectFolder', 'Kind']);
  assert.deepEqual(clashes(generated, [client]), ['ProjectFolder']);
});

test('no generated contract type shares a name with a public type of the client package', () => {
  const generated = readFileSync(GENERATED, 'utf8');
  const client = csharpFiles(CLIENT).map((file) => readFileSync(file, 'utf8'));
  assert.ok(namespaceTypes(generated).length > 100, 'the generated types are read');
  assert.ok(client.length > 50, 'the client package is read');
  assert.deepEqual(
    clashes(generated, client),
    [],
    'rename the generated type (a prefix in packages/contracts/src/csharp.ts UNIONS) or the client one',
  );
});
