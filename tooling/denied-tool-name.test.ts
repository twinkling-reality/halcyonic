/**
 * A guard for AUDIT3 F2.2 (ADR 0065): the tool name beside a request the journal could not record
 * (`execution.approval.denied_by_host`'s `tool_name`) is the agent app's own report, kept for display
 * only, so nothing may decide by it. The scan reads every source file that ships, TypeScript and C#,
 * and a file that names the event and a tool name together must be one listed here with why: the
 * contract that defines it, the host that writes it, and the projection and views that may show it.
 * It reads names, not data flow, so a new file it flags needs a person to look; it proves nothing
 * about a file it passes.
 */
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

const ROOTS = [
  'apps/control-plane/src',
  'packages',
  'apps/xr/Assets',
  'apps/xr/Packages',
  'apps/xr/dotnet',
];

/** The event, as TypeScript and the generated C# name it. */
const EVENT = /denied_by_host|DeniedByHost/;
/** A tool name, as either names it. */
const TOOL_NAME = /\btool_name\b|\bToolName\b/;

/** The files that may name both, and why. */
const ALLOWED: Readonly<Record<string, string>> = {
  'packages/contracts/src/events.ts': 'the contract that defines the field',
  'apps/control-plane/src/core/host-denials.ts':
    "the host that writes it, from the refused request's own tool name, and decides nothing by it",
  'packages/domain/src/projection.ts': 'the projection, which may show it',
  'packages/contracts/src/views.ts': 'the views, which may show it',
};

const SKIPPED = new Set(['node_modules', 'Library', 'Temp', 'obj', 'bin', 'Builds', 'Logs']);

function files(directory: string): string[] {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return SKIPPED.has(entry.name) ? [] : files(path);
    const shipped =
      (path.endsWith('.ts') && !path.endsWith('.test.ts') && !path.includes('/testing/')) ||
      (path.endsWith('.cs') && !path.endsWith('.g.cs'));
    return shipped ? [path] : [];
  });
}

function both(path: string): boolean {
  const text = readFileSync(path, 'utf8');
  return EVENT.test(text) && TOOL_NAME.test(text);
}

describe('the tool name beside a request not recorded is for display only (AUDIT3 F2.2)', () => {
  test('no file outside the contract, the host that writes it, the projection and the views names both', () => {
    const named = ROOTS.flatMap((root) => files(join(ROOT, root)))
      .filter(both)
      .map((path) => relative(ROOT, path))
      .filter((path) => ALLOWED[path] === undefined);
    assert.deepEqual(named, [], 'read it only for display, then list the file in ALLOWED with why');
  });

  test('the scan finds what it looks for', () => {
    // The contract and the host name both today; a scan that finds neither reads nothing.
    for (const path of [
      'packages/contracts/src/events.ts',
      'apps/control-plane/src/core/host-denials.ts',
    ])
      assert.ok(both(join(ROOT, path)), path);
    assert.ok(files(join(ROOT, 'apps/xr/Assets')).some((path) => path.endsWith('.cs')));
  });
});
