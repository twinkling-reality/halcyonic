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
  readonly parts: readonly ('typecheck' | 'build')[];
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
  const steps = [
    { part: 'typecheck' as const, name: 'type check', step: input.typecheck },
    { part: 'build' as const, name: 'build', step: input.build },
  ].filter(({ step }) => step.exit !== null && step.exit !== 0);
  if (steps.length === 0) return null;
  // Longest first, so a folder inside another is named before the one holding it.
  const names: [string, string][] = (
    [
      [input.projectFolder, '.'],
      [relative(input.cacheFolder, input.projectFolder), '.'],
      [input.dataDir, "<Halcyonic's data>"],
      [input.home, '~'],
    ] as [string, string][]
  ).sort((a, b) => b[0].length - a[0].length);
  const clean = (line: string) => {
    let out = line
      .replace(ESCAPES, '')
      .replace(BREAKS, ' ')
      .replace(UNSEEN, (char) => (char === '\t' ? char : ''));
    for (const [path, name] of names) if (path !== '') out = out.split(path).join(name);
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
