import type { CheckFindings } from '@halcyonic/contracts';

/**
 * Sharp edges at or under which a check's page counts as having shown nothing at all, as the check's
 * own drawn rule reads them (`DRAWN_EDGES` in the control plane's check, held equal by a test).
 */
export const BLANK_PAGE_EDGES = 200;

export type FixStep = 'typecheck' | 'build' | 'page';

/**
 * The steps of a check that a try of the fix chain is sent for (ADR 0044), the one rule both the host's
 * sending and the fix view's `failed_steps` read: the type check or the build when it exited other than
 * 0 (one stopped at its limit gives no lines to send), and the page when it threw or showed nothing at
 * all. A page that shows only the starter's own scene is a game not made yet, not a fault, and is not
 * one of them.
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
  return steps;
}
