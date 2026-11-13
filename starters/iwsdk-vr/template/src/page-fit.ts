/**
 * A page's words spread over as many pages as they need, never cut off (the lead, 2026-11-13T08:20Z). Nothing here
 * draws or needs a page: `pages()` gives it a canvas's own measure, and a test gives it one of its own.
 */

/** One line of words, and whether its paragraph ends with it. */
export interface Line {
  readonly line: string;
  readonly end: boolean;
}

/** The words broken into lines no wider than `width` by `measure`, keeping where each paragraph ends. */
export function wrapLines(text: string, width: number, measure: (text: string) => number): Line[] {
  const lines: Line[] = [];
  for (const paragraph of text.split(/\n+/)) {
    let line = '';
    for (const word of paragraph.split(/\s+/).filter((each) => each !== '')) {
      const next = line === '' ? word : `${line} ${word}`;
      if (measure(next) <= width || line === '') line = next;
      else {
        lines.push({ line, end: false });
        line = word;
      }
    }
    if (line !== '') lines.push({ line, end: true });
  }
  return lines;
}

/**
 * The words of one page as the texts of the pages they take: `first` lines on the first page (where its picture
 * is), `rest` on each after. One text, the same as given, when they fit. Paragraphs stay paragraphs.
 */
export function fitText(
  text: string,
  width: number,
  measure: (text: string) => number,
  first: number,
  rest: number,
): string[] {
  const lines = wrapLines(text, width, measure);
  if (lines.length <= first) return [text];
  const texts: string[] = [];
  let at = 0;
  let room = Math.max(0, first);
  while (at < lines.length) {
    const part = lines.slice(at, at + room);
    texts.push(
      part.map((each, i) => each.line + (i === part.length - 1 ? '' : each.end ? '\n' : ' ')).join(''),
    );
    at += room;
    room = Math.max(1, rest);
  }
  return texts;
}
