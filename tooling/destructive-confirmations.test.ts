import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * Every confirmation's Yes on the headset declares whether it is the destructive kind (AUDIT, 2026-11-13T17:59Z): one that
 * removes, forgets, discards or stops something the person made or has running is drawn in the kit's red with its cross;
 * the rest are not. A new confirmation must choose: a `PromptKind.Yes` prompt with no `destructive:` fails here, and each
 * known one is held to its declaration.
 */
const runtime = join(
  import.meta.dirname,
  '..',
  'apps',
  'xr',
  'Packages',
  'com.halcyonic.client',
  'Runtime',
);

const expected: Record<string, string> = {
  'FileScreens.cs Yes': 'armed == WorkspaceAction.Interrupt',
  'NewProjectScreens.cs ConfirmStartOver': 'true',
  'NewProjectScreens.cs ConfirmClear': 'true',
  'NewProjectScreens.cs ConfirmStart': 'false',
  'SettingsColumn.cs Yes': 'now.Destructive',
  'WorkspaceRequestColumn.cs Yes': 'false',
};

function confirmations(): { where: string; destructive: string | null }[] {
  const found: { where: string; destructive: string | null }[] = [];
  for (const file of readdirSync(runtime).filter((name) => name.endsWith('.cs'))) {
    const text = readFileSync(join(runtime, file), 'utf8');
    for (const match of text.matchAll(/new Prompt\((\w+),/g)) {
      // The prompt's arguments, to its closing parenthesis at the same depth.
      let depth = 0;
      let end = match.index;
      for (; end < text.length; end++) {
        if (text[end] === '(') depth++;
        else if (text[end] === ')' && --depth === 0) break;
      }
      const call = text.slice(match.index, end);
      if (!call.includes('PromptKind.Yes')) continue;
      const declared = /destructive:\s*([^,)]+?)\s*(?:[,)]|$)/.exec(call + ')')?.[1];
      found.push({
        where: `${file} ${match[1]}`,
        destructive: declared === undefined ? null : declared.trim(),
      });
    }
  }
  return found;
}

test('every confirmation declares whether it is destructive, as listed', () => {
  const all = confirmations();
  assert.deepEqual(
    all.filter((each) => each.destructive === null).map((each) => each.where),
    [],
    'a Yes with no destructive: declared',
  );
  for (const each of all)
    assert.equal(
      each.destructive,
      expected[each.where],
      `${each.where}: add it to the list with its declaration, or declare it as listed`,
    );
  assert.deepEqual(
    [...new Set(all.map((each) => each.where))].sort(),
    Object.keys(expected).sort(),
    'every listed confirmation still exists',
  );
});
