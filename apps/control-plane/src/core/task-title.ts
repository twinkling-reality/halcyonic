/**
 * A task's title, made from the person's words when they type no title of their own: short and
 * true (the lead's D, 2026-11-17). The headset makes the same title by the same rule
 * (NewWorkDraft.TitleOf in the client package), so the review shows the name the task gets; one
 * table of cases, fixtures/titles/cases.json, pins both.
 *
 * The rule, in order:
 * 1. The first line that has words. A line ends at a line feed, a carriage return, a vertical tab,
 *    a form feed, U+0085, U+2028 or U+2029, so no title spans lines.
 * 2. In it, control, format and unpaired surrogate code points are dropped, a tab and every space
 *    separator is a space, spaces run together are one, and none stands at either end.
 * 3. A link (a word holding "://") loses its query and its fragment, "?" or "#" on, which often
 *    hold a token.
 * 4. The first sentence, ending at ". ", "? " or "! ", never at the stop of "e.g.", "i.e.", "vs."
 *    or "cf.", is the title when it is 60 characters or fewer.
 * 5. Otherwise its first words up to 60 characters, cut only between words, with any comma,
 *    semicolon, colon or dash left at the end dropped; and a first word longer than 60 alone (a
 *    path or a link) keeps its end: the last 60 characters, from a "/" where one is among them.
 * 6. Text with no words at all is "New Task".
 *
 * A character is one the person sees: a code point with the marks and emoji skin tones that follow
 * it, or a pair of regional indicators (a flag), never split. Halcyonic adds no ellipsis.
 */

/** The most characters a title made from the person's words holds. */
export const MADE_TITLE_LENGTH = 60;

/** The title of a task whose words hold nothing to name it by. */
export const UNNAMED_TASK = 'New Task';

const LINE_BREAK = /[\n\r\v\f\u0085\u2028\u2029]/u;
const SPACE = /[\t\p{Zs}]/gu;
const HIDDEN = /[\p{Cc}\p{Cf}\p{Cs}]/gu;
const SENTENCE_STOP = new Set(['.', '?', '!']);
const NOT_AN_END = new Set(['e.g.', 'i.e.', 'vs.', 'cf.']);
const LEFT_AT_A_CUT = new Set([',', ';', ':', '-', '\u2013', '\u2014']);

/** The title Halcyonic gives a task made from `text`, the person's own words. */
export function taskTitleOf(text: string): string {
  const line = firstLine(text);
  if (line === null) return UNNAMED_TASK;
  const sentence = firstSentence(line.split(' ').map(withoutQuery).join(' '));
  if (charactersOf(sentence).length <= MADE_TITLE_LENGTH) return sentence;
  const words = sentence.split(' ');
  const first = words[0] as string;
  if (charactersOf(first).length > MADE_TITLE_LENGTH) return endOf(first);
  let title = first;
  for (const word of words.slice(1)) {
    const longer = `${title} ${word}`;
    if (charactersOf(longer).length > MADE_TITLE_LENGTH) break;
    title = longer;
  }
  return withoutLeftovers(title) || UNNAMED_TASK;
}

/** The characters of `text` as the person sees them, each whole. */
export function charactersOf(text: string): string[] {
  const characters: string[] = [];
  for (const point of text) {
    const code = point.codePointAt(0) as number;
    const last = characters.length - 1;
    const joins =
      last >= 0 &&
      (/\p{M}/u.test(point) ||
        (code >= 0x1f3fb && code <= 0x1f3ff) ||
        (isRegionalIndicator(code) &&
          [...(characters[last] as string)].length === 1 &&
          isRegionalIndicator((characters[last] as string).codePointAt(0) as number)));
    if (joins) characters[last] += point;
    else characters.push(point);
  }
  return characters;
}

function isRegionalIndicator(code: number): boolean {
  return code >= 0x1f1e6 && code <= 0x1f1ff;
}

function firstLine(text: string): string | null {
  for (const raw of text.split(LINE_BREAK)) {
    const line = raw.replace(SPACE, ' ').replace(HIDDEN, '').replace(/ +/g, ' ').trim();
    if (line !== '') return line;
  }
  return null;
}

function withoutQuery(word: string): string {
  if (!word.includes('://')) return word;
  const at = word.search(/[?#]/);
  return at === -1 ? word : word.slice(0, at);
}

function firstSentence(line: string): string {
  for (let at = 0; at < line.length - 1; at++) {
    if (!SENTENCE_STOP.has(line[at] as string) || line[at + 1] !== ' ') continue;
    const word = line.slice(line.lastIndexOf(' ', at) + 1, at + 1).toLowerCase();
    if (!NOT_AN_END.has(word)) return line.slice(0, at + 1);
  }
  return line;
}

/** A word longer than a title, by its end: from a "/" among its last characters, where one is. */
function endOf(word: string): string {
  const end = charactersOf(word).slice(-MADE_TITLE_LENGTH);
  const slash = end.indexOf('/');
  return (slash !== -1 && slash < end.length - 1 ? end.slice(slash) : end).join('');
}

function withoutLeftovers(title: string): string {
  let end = title.length;
  while (end > 0 && (LEFT_AT_A_CUT.has(title[end - 1] as string) || title[end - 1] === ' ')) end--;
  return title.slice(0, end);
}
