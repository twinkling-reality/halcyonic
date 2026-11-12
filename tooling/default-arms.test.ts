/**
 * A guard for the headset's words ([default-arms.md](../docs/internal/validation/default-arms.md)): a switch
 * expression's discard arm (`_ =>`, `_ when`) on the headset side must not answer with a named thing
 * for a value it does not list, since a person is then told something nobody observed (a project's own
 * check once read "Page Drawn" this way). An arm passes when it names nothing (null, a refusal, an empty
 * value, or a word that says unknown or none), or when it stands for exactly one enum member no other
 * arm lists, which is checked here, so a new member fails the day it arrives. Every other named arm is
 * one a person reviewed and listed with why it is safe; a new or changed one fails until it is. It reads
 * code, not behaviour: it narrows what a review must look at and proves nothing about an arm it passes.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** The code that ships to the headset: the client and the Unity layer, outside Editor folders. */
const ROOTS = ['apps/xr/Packages/com.halcyonic.client/Runtime', 'apps/xr/Assets/Halcyonic'];

/** Where enums are declared that these switches read: the same code and the generated contracts. */
const ENUM_ROOTS = [...ROOTS, 'packages/contracts/csharp/Runtime'];

/** Answers that name nothing. */
const HONEST =
  /^(null\b|throw\b|false\b|default\b|\(\s*[\w?.<>]+\s*\)\s*null\b|""|string\.Empty\b|0[fdm]?\b|new\s*\w*\s*\[\s*0\s*\]|Array\.Empty|Enumerable\.Empty)/;
const UNKNOWNISH = /Unknown|\bNone\b|NotKnown|Unnamed|Unavailable/i;

/** Reviewed named arms: the file, the arm's answer as the scan reads it (spaces collapsed), and why. */
interface Reviewed {
  readonly file: string;
  readonly answer: string;
  readonly count: number;
  readonly why: string;
}

const REVIEWED: readonly Reviewed[] = JSON.parse(
  readFileSync(join(ROOT, 'tooling/default-arms.reviewed.json'), 'utf8'),
) as Reviewed[];

function sources(roots: readonly string[]): string[] {
  const found: string[] = [];
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== 'Editor') walk(path);
      } else if (entry.name.endsWith('.cs')) found.push(path);
    }
  };
  for (const root of roots) walk(join(ROOT, root));
  return found.sort();
}

/** Comments and string contents blanked, lengths and newlines kept, so braces in them are not counted. */
export function blank(text: string): string {
  const out = text.split('');
  let i = 0;
  const n = text.length;
  while (i < n) {
    if (text.startsWith('//', i)) {
      let j = text.indexOf('\n', i);
      if (j < 0) j = n;
      for (let k = i; k < j; k++) out[k] = ' ';
      i = j;
    } else if (text.startsWith('/*', i)) {
      let j = text.indexOf('*/', i + 2);
      j = j < 0 ? n : j + 2;
      for (let k = i; k < j; k++) if (out[k] !== '\n') out[k] = ' ';
      i = j;
    } else if (
      text[i] === '"' ||
      (/[$@]/.test(text[i] ?? '') && text[i + 1] === '"') ||
      /^(\$@|@\$)"/.test(text.slice(i, i + 3))
    ) {
      const start = i;
      const verbatim = text[i] === '@' || text.startsWith('$@', i) || text.startsWith('@$', i);
      while (text[i] !== '"') i++;
      i++;
      while (i < n) {
        if (verbatim && text.startsWith('""', i)) {
          i += 2;
          continue;
        }
        if (!verbatim && text[i] === '\\') {
          i += 2;
          continue;
        }
        if (text[i] === '"') {
          i++;
          break;
        }
        i++;
      }
      for (let k = start + 1; k < i - 1; k++) if (out[k] !== '\n') out[k] = 'x';
    } else if (text[i] === "'" && i + 2 < n) {
      let j = i + 1;
      if (text[j] === '\\') j++;
      j++;
      if (text[j] === "'") {
        for (let k = i + 1; k < j; k++) out[k] = 'x';
        i = j + 1;
      } else i++;
    } else i++;
  }
  return out.join('');
}

/** Every enum's members by name; one name may be declared in several namespaces. */
function enums(): Map<string, Set<string>[]> {
  const table = new Map<string, Set<string>[]>();
  for (const path of sources(ENUM_ROOTS)) {
    const text = blank(readFileSync(path, 'utf8'));
    for (const m of text.matchAll(/\benum\s+(\w+)[^{;]*\{([^}]*)\}/g)) {
      const members = new Set<string>();
      for (const part of (m[2] ?? '').split(',')) {
        const name = /(\w+)/.exec(part.replace(/\[[^\]]*\]/g, '').trim());
        if (name?.[1]) members.add(name[1]);
      }
      const name = m[1] as string;
      table.set(name, [...(table.get(name) ?? []), members]);
    }
  }
  return table;
}

/** The members of the one enum a switch's arms name that no arm lists; undefined where it is not one known enum. */
function uncovered(
  lefts: readonly string[],
  table: Map<string, Set<string>[]>,
): string[] | undefined {
  const kinds = new Set<string>();
  const names = new Set<string>();
  for (const left of lefts)
    for (const m of left.matchAll(/\b(\w+)\.(\w+)\b/g))
      if (table.has(m[1] as string)) {
        kinds.add(m[1] as string);
        names.add(m[2] as string);
      }
  if (kinds.size !== 1) return undefined;
  const fits = (table.get([...kinds][0] as string) ?? []).filter((members) =>
    [...names].every((name) => members.has(name)),
  );
  if (fits.length === 0) return undefined;
  const smallest = fits.reduce((a, b) => (b.size < a.size ? b : a));
  return [...smallest].filter((member) => !names.has(member)).sort();
}

interface Arm {
  readonly file: string;
  readonly line: number;
  readonly answer: string;
  readonly kind: 'honest' | 'one' | 'named';
}

export function arms(): Arm[] {
  const table = enums();
  const found: Arm[] = [];
  for (const path of sources(ROOTS)) {
    const file = relative(ROOT, path);
    const text = readFileSync(path, 'utf8');
    const clean = blank(text);
    for (const m of clean.matchAll(/\bswitch\s*\{/g)) {
      const open = (m.index ?? 0) + m[0].length - 1;
      let depth = 0;
      let close = open;
      for (; close < clean.length; close++) {
        if (clean[close] === '{') depth++;
        else if (clean[close] === '}' && --depth === 0) break;
      }
      const body = clean.slice(open + 1, close);
      const parts: { start: number; text: string }[] = [];
      let level = 0;
      let start = 0;
      for (let i = 0; i < body.length; i++) {
        const c = body[i] as string;
        if ('([{'.includes(c)) level++;
        else if (')]}'.includes(c)) level--;
        else if (c === ',' && level === 0) {
          parts.push({ start, text: body.slice(start, i) });
          start = i + 1;
        }
      }
      if (body.slice(start).trim()) parts.push({ start, text: body.slice(start) });
      const lefts = parts
        .filter((part) => part.text.includes('=>'))
        .map((part) => part.text.split('=>')[0] as string);
      for (const part of parts) {
        const head = part.text.trimStart();
        if (!/^_\s*(=>|when\b)/.test(head)) continue;
        const at = open + 1 + part.start + (part.text.length - head.length);
        const raw = text.slice(open + 1 + part.start, open + 1 + part.start + part.text.length);
        const answer = raw.includes('=>')
          ? raw
              .slice(raw.indexOf('=>') + 2)
              .split(/\s+/)
              .join(' ')
              .trim()
          : '';
        const line = text.slice(0, at).split('\n').length;
        const rest = uncovered(lefts, table);
        const kind =
          HONEST.test(answer) || UNKNOWNISH.test(answer)
            ? 'honest'
            : rest !== undefined && rest.length <= 1
              ? 'one'
              : 'named';
        found.push({ file, line, answer, kind });
      }
    }
  }
  return found;
}

describe('default arms on the headset side', () => {
  const found = arms();
  const named = found.filter((arm) => arm.kind === 'named');

  test('the scan reads the headset code it guards', () => {
    assert.ok(
      found.length > 100,
      `only ${found.length} discard arms found: the scan no longer reads the code`,
    );
  });

  test('every discard arm that names a thing for several values is reviewed', () => {
    const counts = new Map<string, number>();
    for (const arm of named)
      counts.set(
        `${arm.file} | ${arm.answer}`,
        (counts.get(`${arm.file} | ${arm.answer}`) ?? 0) + 1,
      );
    const allowed = new Map(
      REVIEWED.map((entry) => [`${entry.file} | ${entry.answer}`, entry.count]),
    );
    const unreviewed = named
      .filter(
        (arm) =>
          (counts.get(`${arm.file} | ${arm.answer}`) ?? 0) >
          (allowed.get(`${arm.file} | ${arm.answer}`) ?? 0),
      )
      .map((arm) => `${arm.file}:${arm.line}  _ => ${arm.answer.slice(0, 160)}`);
    assert.deepEqual(
      [...new Set(unreviewed)],
      [],
      'a discard arm answers with a named thing for values it does not list: answer unknown or null, list every member, or review it in tooling/default-arms.reviewed.json',
    );
  });

  test('every reviewed arm still exists, so the list never holds a stale pass', () => {
    const present = new Set(named.map((arm) => `${arm.file} | ${arm.answer}`));
    const stale = REVIEWED.filter((entry) => !present.has(`${entry.file} | ${entry.answer}`)).map(
      (entry) => `${entry.file} | ${entry.answer}`,
    );
    assert.deepEqual(stale, [], 'remove these from tooling/default-arms.reviewed.json');
  });

  test('every review says why', () => {
    for (const entry of REVIEWED)
      assert.ok(entry.why.trim().length >= 12, `${entry.file}: say why "${entry.answer}" is safe`);
  });
});
