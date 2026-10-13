import type {
  Principal,
  ToolResultResponse,
  ToolResultsQuery,
  ToolResultsResponse,
} from '@halcyonic/contracts';
import type { ControlPlane } from './control-plane.ts';

/** Owner-journal reads only: no adapter invocation or source filesystem access. */
export class ToolResults {
  readonly plane: ControlPlane;
  constructor(plane: ControlPlane) {
    this.plane = plane;
  }

  current(executionId: string, scope: ToolResultsQuery, principal: Principal | null): boolean {
    if (
      !principal ||
      (principal.kind === 'device' &&
        this.plane.projection.device(principal.device_id)?.revoked_at !== null)
    )
      return false;
    const run = this.plane.projection.execution(executionId);
    const work = this.plane.projection.workstream(scope.workstream_id);
    return (
      scope.journal_id === this.plane.journal.info.journal_id &&
      Boolean(
        run &&
          work &&
          run.project_id === scope.project_id &&
          run.workstream_id === scope.workstream_id &&
          work.project_id === scope.project_id &&
          work.current_execution_id === executionId,
      )
    );
  }

  list(
    executionId: string,
    scope: ToolResultsQuery,
    principal: Principal | null,
  ): ToolResultsResponse | undefined {
    if (!this.current(executionId, scope, principal)) return undefined;
    const run = this.plane.projection.execution(executionId);
    if (!run) return undefined;
    const descriptor = this.plane.registry.get(run.runtime.runtime_id);
    const recorded = this.plane.projection.executionResultSummaries(executionId);
    return {
      ...scope,
      execution_id: run.execution_id,
      availability:
        descriptor === undefined
          ? 'unavailable'
          : descriptor.reports_tool_results === true
            ? 'available'
            : descriptor.reports_tool_results === false
              ? 'unsupported'
              : 'unknown',
      total: recorded.total,
      earlier: recorded.total - recorded.summaries.length,
      results: recorded.summaries,
    };
  }

  read(
    executionId: string,
    eventId: string,
    scope: ToolResultsQuery,
    principal: Principal | null,
  ): ToolResultResponse | undefined {
    if (!this.current(executionId, scope, principal)) return undefined;
    // The projection admits only correlated, observed, ordered results. A matching raw journal row
    // that it rejected is not evidence. Its lightweight index survives recent-list eviction.
    const summary = this.plane.projection.executionResultSummary(executionId, eventId);
    if (!summary) return undefined;
    const stored = this.plane.journal.event(eventId);
    if (!stored || stored.position !== summary.position) return undefined;
    const event = stored.event;
    if (
      event.event_type !== 'runtime.tool.result' ||
      event.provenance.epistemic !== 'observed' ||
      event.project_id !== scope.project_id ||
      event.workstream_id !== scope.workstream_id ||
      event.execution_id !== executionId ||
      event.source.kind !== 'runtime' ||
      event.source.runtime_id !== summary.runtime_id ||
      event.occurred_at !== summary.occurred_at ||
      event.provenance.native_type !== summary.native_type
    )
      return undefined;
    return {
      ...scope,
      execution_id: event.execution_id,
      summary,
      result: structuredClone(event.payload),
    };
  }
}
