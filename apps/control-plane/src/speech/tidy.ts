import { PROMPT_WORDS } from './whisper.ts';

/** Hesitation sounds a draft drops: whole words only, in any case; not "mm", a unit ("5 mm"). */
const FILLERS = new Set(['um', 'umm', 'uhm', 'uh', 'uhh', 'erm', 'hmm']);

/** Short words a stutter doubles that nobody means twice; "had had" or "in in" may be meant. */
const STUTTERED = new Set(['a', 'an', 'the', 'i', 'to', 'and', 'of', 'for', 'with', 'my', 'we']);

/** The most names a draft is tidied with, and the longest name taken. */
export const MAX_NAMES = 200;
export const MAX_NAME_LENGTH = 60;

/** A word: letters and digits joined by an apostrophe or a hyphen, so "uh-oh" is one word. */
const WORD = /[\p{L}\p{N}]+(?:['\u2019-][\p{L}\p{N}]+)*/gu;

/** What may stand beside a removed filler: spaces, pauses and a sentence's end, nothing else. */
const PAUSE_OR_END = /^[\s,.!?\u2026\u2013\u2014-]*$/u;

/**
 * Tidies what the engine heard before it becomes the person's draft (ADR 0021): drops hesitation
 * sounds with the pauses around them, says a stuttered short word once, and spells each of `names`
 * as written wherever the same letters were heard as whole words, in any case, with a space, a
 * hyphen or nothing between the name's parts. Returns '' when only hesitation was heard.
 */
export function tidySpeech(text: string, names: readonly string[]): string {
  const words: string[] = [];
  const gaps: string[] = [];
  let at = 0;
  for (const match of text.matchAll(WORD)) {
    gaps.push(text.slice(at, match.index));
    words.push(match[0]);
    at = match.index + match[0].length;
  }
  gaps.push(text.slice(at));

  let index = 0;
  while (index < words.length) {
    const word = words[index] as string;
    const before = gaps[index] as string;
    const after = gaps[index + 1] as string;
    if (
      !FILLERS.has(word.toLowerCase()) ||
      !PAUSE_OR_END.test(before) ||
      !PAUSE_OR_END.test(after)
    ) {
      index++;
      continue;
    }
    const first = index === 0;
    const last = index === words.length - 1;
    const end = sentenceEnd(before) ?? sentenceEnd(after);
    const gap = first ? '' : last ? (end ?? '') : end === null ? ' ' : `${end} `;
    words.splice(index, 1);
    gaps.splice(index, 2, gap);
    // A filler that began a sentence hands its capital to the word after it.
    const next = words[index];
    if (next !== undefined && /^\p{Lu}/u.test(word) && (first || end !== null)) {
      words[index] = next.charAt(0).toUpperCase() + next.slice(1);
    }
  }

  index = 1;
  while (index < words.length) {
    const word = (words[index] as string).toLowerCase();
    if (
      word === (words[index - 1] as string).toLowerCase() &&
      STUTTERED.has(word) &&
      /^\s+$/u.test(gaps[index] as string)
    ) {
      words.splice(index, 1);
      gaps.splice(index, 1);
    } else {
      index++;
    }
  }

  let tidied = gaps[0] as string;
  words.forEach((word, position) => {
    tidied += word + (gaps[position + 1] as string);
  });
  return spellNames(tidied.trim() === '' ? '' : tidied, names);
}

/** The sentence's end a gap holds ('.', '!' or '?'), or null; an ellipsis is a pause. */
function sentenceEnd(gap: string): string | null {
  return /[.!?]+/u.exec(gap.replace(/\.\.\.|\u2026/gu, ''))?.[0] ?? null;
}

/** Spells each name as written where its letters were heard as whole words; longest wins. */
function spellNames(text: string, names: readonly string[]): string {
  if (text === '' || names.length === 0) return text;
  const ordered = [...names].sort((a, b) => b.length - a.length);
  const pattern = ordered.map((name) => `(${namePattern(name)})`).join('|');
  const matcher = new RegExp(`(?<![\\p{L}\\p{N}])(?:${pattern})(?![\\p{L}\\p{N}])`, 'giu');
  return text.replace(matcher, (...groups: unknown[]) => {
    const found = groups.slice(1, ordered.length + 1).findIndex((group) => group !== undefined);
    return ordered[found] as string;
  });
}

/**
 * A name's words, split at spaces and hyphens, heard with a space, a hyphen or nothing between;
 * the parts of a word written joined (OpenCode, ToDo) heard joined or hyphenated, never spaced,
 * so "open code review" and "to do this" stay as said.
 */
function namePattern(name: string): string {
  return name
    .split(/[\s-]+/u)
    .filter((word) => word !== '')
    .map((word) =>
      word
        .split(/(?<=\p{Ll})(?=\p{Lu})/u)
        .map((part) => part.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'))
        .join('-?'),
    )
    .join('[\\s-]?');
}

/**
 * The names a draft is tidied with: Halcyonic's own words (the engine's prompt), then each
 * project's name, trimmed, once regardless of case, with letters, at most 60 characters, 200 in
 * all.
 */
export function spokenNames(projectNames: readonly string[]): string[] {
  const names: string[] = [];
  const seen = new Set<string>();
  for (const candidate of [...PROMPT_WORDS, ...projectNames]) {
    const name = candidate.trim();
    const key = name.toLowerCase();
    if (name.length > MAX_NAME_LENGTH || !/\p{L}/u.test(name) || seen.has(key)) continue;
    seen.add(key);
    names.push(name);
    if (names.length === MAX_NAMES) break;
  }
  return names;
}
