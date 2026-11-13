/**
 * No screen of the product names a sibling product (LANE-RULES 19 for anything a judge sees; the lead's
 * ruling of 2026-11-13 for every build): the headset says where an understanding or a measurement came
 * from in its own words, and a provider's id stays data in its records, never drawn. This guard reads
 * every string literal the headset's code can draw (the client package's runtime and the app's scripts
 * outside Editor folders; comments are not drawn and are skipped) and fails on any that holds a sibling
 * product's name, or a name this checkout keeps out of the repository where it keeps any. The sibling
 * products are named in AGENTS.md; the other names are read from the ignored file the recorded-run scan
 * reads, and never printed.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** The products beside this one, as AGENTS.md names them. */
const SIBLINGS = ['Salidium', 'Seorak'] as const;

/** The names kept out of the repository, one a line, as the recorded-run scan reads them. */
const KEPT_OUT = new URL('../.private/recorded-run-names', import.meta.url);

/** The code a headset build carries: the client package and the app's scripts, never an Editor folder. */
const ROOTS = ['apps/xr/Packages/com.halcyonic.client/Runtime', 'apps/xr/Assets/Halcyonic'];

function sources(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory())
      return entry.name === 'Editor' || entry.name === 'Tests' ? [] : sources(path);
    return entry.name.endsWith('.cs') ? [path] : [];
  });
}

/**
 * The text of every string literal in C# source, comments skipped: regular, verbatim and interpolated
 * strings, an interpolation's holes left out (they are code, not words).
 */
export function literals(source: string): string[] {
  const found: string[] = [];
  let index = 0;
  while (index < source.length) {
    const here = source[index];
    const next = source[index + 1];
    if (here === '/' && next === '/') {
      const end = source.indexOf('\n', index);
      index = end < 0 ? source.length : end;
    } else if (here === '/' && next === '*') {
      const end = source.indexOf('*/', index + 2);
      index = end < 0 ? source.length : end + 2;
    } else if (here === "'") {
      index += 1;
      while (index < source.length && source[index] !== "'")
        index += source[index] === '\\' ? 2 : 1;
      index += 1;
    } else if (
      here === '"' ||
      ((here === '@' || here === '$') && (next === '"' || next === '@' || next === '$'))
    ) {
      let verbatim = false;
      let interpolated = false;
      while (source[index] === '@' || source[index] === '$') {
        if (source[index] === '@') verbatim = true;
        else interpolated = true;
        index += 1;
      }
      index += 1;
      let text = '';
      let depth = 0;
      while (index < source.length) {
        const c = source[index];
        if (depth > 0) {
          if (c === '{') depth += 1;
          else if (c === '}') depth -= 1;
          index += 1;
          continue;
        }
        if (interpolated && c === '{') {
          if (source[index + 1] === '{') {
            text += '{';
            index += 2;
          } else {
            depth = 1;
            index += 1;
          }
          continue;
        }
        if (interpolated && c === '}' && source[index + 1] === '}') {
          text += '}';
          index += 2;
          continue;
        }
        if (verbatim && c === '"') {
          if (source[index + 1] === '"') {
            text += '"';
            index += 2;
            continue;
          }
          index += 1;
          break;
        }
        if (!verbatim && c === '\\') {
          text += source.slice(index, index + 2);
          index += 2;
          continue;
        }
        if (!verbatim && c === '"') {
          index += 1;
          break;
        }
        text += c;
        index += 1;
      }
      found.push(text);
    } else {
      index += 1;
    }
  }
  return found;
}

function keptOut(): string[] {
  let text: string;
  try {
    text = readFileSync(KEPT_OUT, 'utf8');
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw error;
  }
  return text
    .split('\n')
    .map((line) => line.replace(/#.*/, '').trim())
    .filter((line) => line !== '');
}

function naming(names: readonly string[]): { file: string; name: number }[] {
  const patterns = names.map(
    (name) => new RegExp(`(?<![a-z])${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`, 'i'),
  );
  const hits: { file: string; name: number }[] = [];
  for (const root of ROOTS) {
    for (const file of sources(join(ROOT, root))) {
      for (const literal of literals(readFileSync(file, 'utf8'))) {
        const name = patterns.findIndex((pattern) => pattern.test(literal));
        if (name >= 0) hits.push({ file: relative(ROOT, file), name });
      }
    }
  }
  return hits;
}

describe('no screen names a sibling product', () => {
  test('the scanner reads literals and skips comments and holes', () => {
    assert.deepEqual(
      literals(
        '// "Zarnoth"\n/* "Quillon" */ var a = "one \\" two"; var b = @"x ""y"""; var c = $"p {Zarnoth("q")} {{r}}"; var d = \'"\';',
      ),
      ['one \\" two', 'x "y"', 'p  {r}'],
    );
  });

  test("no literal the headset can draw holds a sibling product's name", () => {
    const hits = naming(SIBLINGS);
    assert.deepEqual(
      hits.map((hit) => `${hit.file} names ${SIBLINGS[hit.name]}`),
      [],
      "say where a source came from in the product's own words; a provider's id is data, never drawn",
    );
  });

  test('no literal the headset can draw holds a name kept out of the repository, where this checkout keeps any', (t) => {
    const names = keptOut();
    if (names.length === 0) {
      t.skip('this checkout keeps no names out of the repository');
      return;
    }
    // The file and how many hits only: the name itself is never printed.
    assert.deepEqual(
      naming(names).map((hit) => hit.file),
      [],
      'a literal the headset can draw holds a name kept out of the repository',
    );
  });
});
