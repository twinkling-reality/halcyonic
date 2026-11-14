import type { CheckFindings, CheckReading } from '@halcyonic/contracts';

/**
 * Sharp edges at or under which a check's page counts as having shown nothing at all, as the check's
 * own drawn rule reads them (`DRAWN_EDGES` in the control plane's check, held equal by a test).
 */
export const BLANK_PAGE_EDGES = 200;

export type FixStep = 'typecheck' | 'build' | 'page' | 'reading';

/**
 * The steps of a check that a try of the fix chain is sent for (ADR 0044), the one rule both the host's
 * sending and the fix view's `failed_steps` read: the type check or the build when it exited other than
 * 0 (one stopped at its limit gives no lines to send), and the page when it threw or showed nothing at
 * all. A page that shows only the starter's own scene is a game not made yet, not a fault, and is not
 * one of them. And the laptop reading when it was measured under a floor (`readingUnder`), whether the check
 * passed or not: the second trigger of ADR 0044, under the same cap.
 */
export function fixSteps(findings: CheckFindings): FixStep[] {
  const steps: FixStep[] = [];
  if (findings.tsc.exit !== null && findings.tsc.exit !== 0) steps.push('typecheck');
  if (findings.build.exit !== null && findings.build.exit !== 0) steps.push('build');
  const page = findings.page;
  if (
    page !== null &&
    (page.exceptions.length > 0 ||
      Math.max(page.edges_at_3s ?? 0, page.edges_at_10s ?? 0) <= BLANK_PAGE_EDGES)
  )
    steps.push('page');
  if (readingUnder(findings) !== null) steps.push('reading');
  return steps;
}

/**
 * Whether the laptop reading lets the laptop step be ready (VRIFY, AUDIT3 R3: it never changes whether the check
 * passed). Only a measured failure holds it back: the smallest words measured under the floor, or, on a reading that
 * could not be measured, the project's last measured reading still under it (`last_measured_px`). A reading not known
 * holds nothing back by itself, and neither does one absent on a check recorded before it.
 */
export function readingPasses(reading: CheckReading | undefined): boolean {
  return readingFailure(reading) === null;
}

/** The words' height that fails the floor, measured now or last measured and still held, or null. */
function readingFailure(reading: CheckReading | undefined): number | null {
  if (reading === undefined) return null;
  if (reading.state === 'measured')
    return reading.smallest_px !== null && reading.smallest_px < reading.floor_px
      ? reading.smallest_px
      : null;
  return reading.last_measured_px;
}

/**
 * The laptop reading when it is a measured failure, the one case an agent can act on: its words measured under the
 * floor, now or last time with no measurement since. A reading merely not known is not sent: no agent is known to have
 * made it so.
 */
export function readingUnder(findings: CheckFindings): CheckReading | null {
  const reading = findings.laptop?.reading;
  return readingFailure(reading) === null ? null : (reading ?? null);
}

/**
 * The reading in words for the agent, which may name the kit's parts a person is never told of: how high the words are
 * drawn, the floor a person reads at, and what to change. Null when the reading gives an agent nothing to act on.
 */
export function readingFix(findings: CheckFindings): string | null {
  const reading = readingUnder(findings);
  const failing = readingFailure(reading ?? undefined);
  if (reading === null || failing === null) return null;
  // Cut to one place, never rounded up onto a floor.
  const px = Math.floor(failing * 10 + 1e-9) / 10;
  const change =
    'Bring the pages nearer the laptop camera or make the words larger (TYPE in src/pages.ts), so the laptop reads as the headset does.';
  if (reading.state === 'measured')
    return `Words are drawn ${px} px high at a laptop's 1280 by 800 view; a person reads at least ${reading.floor_px} px. ${change}`;
  const why =
    reading.state === 'changed'
      ? "a starter kit file that measures them (src/pages.ts, src/decor.ts, src/look.ts, src/legible.ts or the input layer) differs from the starter's own; put it back"
      : 'no text drawn with the kit (pages() or the score sign) was in view';
  return `Words were drawn ${px} px high at a laptop's 1280 by 800 view when last measured; a person reads at least ${reading.floor_px} px. The check can no longer measure them: ${why}. ${change}`;
}

export type CheckVerdict = NonNullable<CheckFindings['verdict']>;

/**
 * A starter's check's whole answer, recorded beside `passed` (VRIFY, 2026-11-13): a reference the product could not make
 * is the product's failure, never the app's. `failed` on any failure the check measured or saw, whatever else is not
 * known: a type check or build that did not exit 0, a page not opened, an uncaught exception, nothing drawn by its
 * edges or of its own, a refusal of its starter, or the laptop reading measured under its floor. `not_known` where
 * nothing failed and only whether the page draws its own look is not known (`drawn` null). `passed` only for a whole
 * seen pass.
 */
export function checkVerdict(findings: CheckFindings): CheckVerdict {
  const page = findings.page;
  if (
    findings.tsc.exit !== 0 ||
    findings.build.exit !== 0 ||
    page === null ||
    page.exception_count > 0 ||
    page.drawn === false ||
    (findings.refusals?.length ?? 0) > 0 ||
    readingUnder(findings) !== null
  )
    return 'failed';
  if (page.drawn === null) return 'not_known';
  return findings.passed ? 'passed' : 'failed';
}

/**
 * Whether a check's built copy may be served and played on this computer: a pass, or a check whose only unknown is
 * whether the page draws its own look (`not_known`), which refuses nothing by itself. Sharing and publishing still
 * read `passed` alone.
 */
export function checkServable(findings: CheckFindings): boolean {
  return findings.passed || findings.verdict === 'not_known';
}
