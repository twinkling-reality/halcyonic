import { relative } from 'node:path';

/** The most lines of each failed step given to the agent, the longest line, and the most bytes a step takes. */
export const CHECK_OUTPUT_LINES = 40;
export const CHECK_OUTPUT_LINE_CHARS = 300;
export const CHECK_OUTPUT_PART_BYTES = 4096;

/** One step of a check as its own folder holds it: its exit, null when it did not run, and its output, null when unread. */
export interface CheckStepOutput {
  readonly exit: number | null;
  readonly log: string | null;
}

/** What the agent is given after the person's words, and what is recorded of it. */
export interface CheckOutput {
  readonly text: string;
  readonly parts: readonly ('typecheck' | 'build' | 'own_check')[];
  readonly lines: number;
  readonly bytes: number;
}

const HEADER = "[The Mac's own check of this project, not the person's words]";
const FOOTER = "[End of the Mac's check]";
/**
 * Each line the check printed starts with this, so none can stand as the block's own lines or as
 * the person's words (AUDIT FX1-M1): the build runs the project's own code, which writes its log.
 */
const MARK = '| ';
const ESC = String.fromCharCode(27);
const BEL = String.fromCharCode(7);
/**
 * A terminal sequence: an operating system command (ESC ], to BEL or ESC \), a control sequence (ESC [, its
 * parameters, a final byte), or ESC and one more character; built so the source holds no control character.
 */
const ESCAPES = new RegExp(
  `${ESC}\\][^${BEL}${ESC}]*(?:${BEL}|${ESC}\\\\)?|${ESC}\\[[0-9;?]*[ -/]*[@-~]|${ESC}.?`,
  'gs',
);
/** Control characters but tab, and format characters (bidirectional, zero width, invisible). */
const UNSEEN = /[\p{Cc}\p{Cf}]/gu;
/** The Unicode line and paragraph separators, which a model may read as a line break (AUDIT FX1-C1). */
const BREAKS = /[\p{Zl}\p{Zp}]/gu;

/** Where a check's log names the host: the project, its check folder, Halcyonic's data, the home folder. */
export interface CheckLogPlaces {
  readonly projectFolder: string;
  readonly cacheFolder: string;
  readonly dataDir: string;
  readonly home: string;
}

/**
 * A check step's log as lines a person reads (the parity map's gap 4): the same cleaning the agent's
 * copy gets (no terminal sequences, no unseen characters, no host paths), without its mark, each line
 * at most `lineChars`, at most `maxLines` of the non-empty lines.
 */
export function checkLogLines(
  log: string,
  places: CheckLogPlaces,
  maxLines: number,
  lineChars: number,
): { readonly lines: string[]; readonly total: number } {
  const names = placeNames(places);
  const all = log
    .split('\n')
    .map((line) => line.trimEnd())
    .filter((line) => line !== '');
  const lines = all.slice(0, maxLines).map((line) => {
    const out = cleanLine(line, names);
    return out.length <= lineChars ? out : `${out.slice(0, lineChars - 3)}...`;
  });
  return { lines, total: all.length };
}

/** Longest first, so a folder inside another is named before the one holding it. */
function placeNames(places: CheckLogPlaces): [string, string][] {
  return (
    [
      [places.projectFolder, '.'],
      [relative(places.cacheFolder, places.projectFolder), '.'],
      [places.dataDir, "<Halcyonic's data>"],
      [places.home, '~'],
    ] as [string, string][]
  ).sort((a, b) => b[0].length - a[0].length);
}

/**
 * A line a program printed as plain text: no terminal sequences, no unseen characters but tab, and
 * line separators as spaces. The setup page's downloads show their lines so too (ADR 0053).
 */
export function plainLine(line: string): string {
  return line
    .replace(ESCAPES, '')
    .replace(BREAKS, ' ')
    .replace(UNSEEN, (char) => (char === '\t' ? char : ''));
}

function cleanLine(line: string, names: readonly [string, string][]): string {
  let out = plainLine(line);
  for (const [path, name] of names) if (path !== '') out = out.split(path).join(name);
  return out;
}

/**
 * The first lines of a failed check's type check and build, for the agent with the person's next
 * instruction (FX1): marked as the Mac's own check, bounded, and naming no host path. The project's
 * folder reads as `.`, Halcyonic's data folder and the home folder by name. Null when neither step
 * failed.
 */
export function checkOutput(input: {
  readonly typecheck: CheckStepOutput;
  readonly build: CheckStepOutput;
  readonly projectFolder: string;
  readonly cacheFolder: string;
  readonly dataDir: string;
  readonly home: string;
}): CheckOutput | null {
  return stepsOutput(
    [
      { part: 'typecheck' as const, name: 'type check', step: input.typecheck },
      { part: 'build' as const, name: 'build', step: input.build },
    ],
    input,
  );
}

/**
 * The first lines of a project's own check's failed script (ADR 0055), as `checkOutput` gives a
 * starter's: marked as the Mac's own check, bounded, naming no host path. The script's output is the
 * project's own code's, so each line is marked as the check's. Null when it did not fail.
 */
export function ownCheckOutput(
  input: {
    readonly script: string;
    readonly exit: number | null;
    readonly log: string | null;
  } & CheckLogPlaces,
): CheckOutput | null {
  return stepsOutput(
    [
      {
        part: 'own_check' as const,
        name: `project's ${input.script} script`,
        step: { exit: input.exit, log: input.log },
      },
    ],
    input,
  );
}

function stepsOutput(
  all: readonly {
    readonly part: CheckOutput['parts'][number];
    readonly name: string;
    readonly step: CheckStepOutput;
  }[],
  input: CheckLogPlaces,
): CheckOutput | null {
  const steps = all.filter(({ step }) => step.exit !== null && step.exit !== 0);
  if (steps.length === 0) return null;
  const names = placeNames(input);
  const clean = (line: string) => {
    const out = cleanLine(line, names);
    const room = CHECK_OUTPUT_LINE_CHARS - MARK.length;
    return MARK + (out.length <= room ? out : `${out.slice(0, room - 3)}...`);
  };
  const text = [HEADER];
  let lines = 0;
  for (const { name, step } of steps) {
    text.push(`The ${name} failed (exit ${step.exit}). Its first lines:`);
    if (step.log === null) {
      text.push('Its output could not be read.');
      continue;
    }
    let bytes = 0;
    const given = step.log
      .split('\n')
      .map((line) => line.trimEnd())
      .filter((line) => line !== '')
      .slice(0, CHECK_OUTPUT_LINES)
      .map(clean);
    for (const line of given) {
      bytes += Buffer.byteLength(line) + 1;
      if (bytes > CHECK_OUTPUT_PART_BYTES) break;
      text.push(line);
      lines += 1;
    }
  }
  text.push(FOOTER);
  const joined = text.join('\n');
  return {
    text: joined,
    parts: steps.map(({ part }) => part),
    lines,
    bytes: Buffer.byteLength(joined),
  };
}
