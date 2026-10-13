import Type, { type Static } from 'typebox';
import {
  EventId,
  ExecutionId,
  JournalId,
  NativeId,
  Nullable,
  Position,
  ProjectId,
  RuntimeId,
  Text,
  Timestamp,
  WorkstreamId,
} from './primitives.ts';

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0, maximum: Number.MAX_SAFE_INTEGER });
export const TOOL_RESULT_SOURCE_BYTES = 65536;
export const TOOL_RESULT_BYTES = 65536;
export const TOOL_RESULT_BODY_CHARACTERS = 4096;
export const TOOL_RESULT_FILES = 32;
export const TOOL_RESULT_RECENT = 16;

/** Entire admitted native field retained after replacement; never complete stdout or current bytes. */
export const ToolResultBody = Type.Object(
  {
    text: Nullable(Text(TOOL_RESULT_BODY_CHARACTERS)),
    availability: Type.Union([
      Type.Literal('available'),
      Type.Literal('absent'),
      Type.Literal('source_over_limit'),
      Type.Literal('omitted_scope'),
      Type.Literal('malformed'),
    ]),
    complete: Type.Boolean(),
    truncation: Type.Union([
      Type.Literal('none'),
      Type.Literal('body_bound'),
      Type.Literal('aggregate_bound'),
      Type.Literal('source_limit'),
      Type.Literal('scope'),
      Type.Literal('absent'),
      Type.Literal('malformed'),
    ]),
  },
  strict,
);
export type ToolResultBody = Static<typeof ToolResultBody>;
export const ToolResultCompletion = Type.Union([
  Type.Literal('completed'),
  Type.Literal('failed'),
  Type.Literal('declined'),
  Type.Literal('interrupted'),
  Type.Literal('unknown'),
]);
export const ToolResultRedaction = Type.Object(
  { policy: Type.Literal('known_held_values'), replacements: Count },
  strict,
);
const common = {
  tool_call_id: NativeId,
  turn_id: Nullable(NativeId),
  completion: ToolResultCompletion,
  redaction: ToolResultRedaction,
};
export const ToolResultPathScope = Type.Union([
  Type.Literal('project_relative'),
  Type.Literal('outside_project'),
  Type.Literal('unknown'),
]);
export const ToolResultFile = Type.Object(
  {
    kind: Type.Union([Type.Literal('add'), Type.Literal('update'), Type.Literal('delete')]),
    path: Nullable(Text(1024)),
    path_scope: ToolResultPathScope,
    path_complete: Type.Boolean(),
    move_path: Nullable(Text(1024)),
    move_path_scope: Nullable(ToolResultPathScope),
    move_path_complete: Nullable(Type.Boolean()),
    patch: ToolResultBody,
  },
  strict,
);
export type ToolResultFile = Static<typeof ToolResultFile>;
export const ToolResult = Type.Union([
  Type.Object(
    {
      ...common,
      kind: Type.Literal('command'),
      exit_code: Nullable(Type.Integer({ minimum: -2147483648, maximum: 2147483647 })),
      output: ToolResultBody,
    },
    strict,
  ),
  Type.Object(
    {
      ...common,
      kind: Type.Literal('file_changes'),
      files: Type.Array(ToolResultFile, { maxItems: TOOL_RESULT_FILES }),
      files_total: Count,
      list_complete: Type.Boolean(),
    },
    strict,
  ),
]);
export type ToolResult = Static<typeof ToolResult>;

/** Current declaration/adapter availability is separate from durable recorded results. */
export const ToolResultsAvailability = Type.Union([
  Type.Literal('available'),
  Type.Literal('unsupported'),
  Type.Literal('unknown'),
  Type.Literal('unavailable'),
]);
export const ToolResultsQuery = Type.Object(
  { journal_id: JournalId, project_id: ProjectId, workstream_id: WorkstreamId },
  strict,
);
export type ToolResultsQuery = Static<typeof ToolResultsQuery>;
const scope = { ...ToolResultsQuery.properties, execution_id: ExecutionId };
export const ToolResultSummary = Type.Object(
  {
    event_id: EventId,
    position: Position,
    occurred_at: Timestamp,
    runtime_id: RuntimeId,
    epistemic: Type.Literal('observed'),
    native_type: Nullable(Text(128)),
    kind: Type.Union([Type.Literal('command'), Type.Literal('file_changes')]),
    completion: ToolResultCompletion,
    title: Nullable(Text(180)),
    exit_code: Nullable(Type.Integer({ minimum: -2147483648, maximum: 2147483647 })),
    files_total: Nullable(Count),
    body_available: Type.Boolean(),
    retained_complete: Type.Boolean(),
  },
  strict,
);
export type ToolResultSummary = Static<typeof ToolResultSummary>;
export const ToolResultsResponse = Type.Object(
  {
    ...scope,
    availability: ToolResultsAvailability,
    total: Count,
    earlier: Count,
    results: Type.Array(ToolResultSummary, { maxItems: TOOL_RESULT_RECENT }),
  },
  strict,
);
export type ToolResultsResponse = Static<typeof ToolResultsResponse>;
export const ToolResultResponse = Type.Object(
  { ...scope, summary: ToolResultSummary, result: ToolResult },
  strict,
);
export type ToolResultResponse = Static<typeof ToolResultResponse>;

/** Semantic bounds that JSON Schema cannot express; messages never quote outside content. */
export function toolResultProblem(result: ToolResult): string | null {
  if (new TextEncoder().encode(JSON.stringify(result)).length > TOOL_RESULT_BYTES)
    return 'result exceeds its aggregate byte bound';
  const bodies =
    result.kind === 'command' ? [result.output] : result.files.map((file) => file.patch);
  for (const body of bodies) {
    if ((body.availability === 'available') !== (body.text !== null))
      return 'body text and availability disagree';
    if (body.complete && (body.text === null || body.truncation !== 'none'))
      return 'body completeness is inconsistent';
    if (body.text !== null && new TextEncoder().encode(body.text).length > 16384)
      return 'body exceeds its byte bound';
  }
  if (result.kind === 'file_changes') {
    if (
      result.files_total < result.files.length ||
      (result.list_complete && result.files_total !== result.files.length)
    )
      return 'file-list coverage is inconsistent';
    for (const file of result.files) {
      if ((file.path_scope === 'project_relative') !== (file.path !== null))
        return 'file path scope is inconsistent';
      if (
        file.move_path_scope === null &&
        (file.move_path !== null || file.move_path_complete !== null)
      )
        return 'rename metadata is inconsistent';
      if (
        file.move_path_scope !== null &&
        ((file.move_path_scope === 'project_relative') !== (file.move_path !== null) ||
          file.move_path_complete === null)
      )
        return 'rename scope is inconsistent';
      if (
        (file.path_scope !== 'project_relative' ||
          (file.move_path_scope !== null && file.move_path_scope !== 'project_relative')) &&
        file.patch.text !== null
      )
        return 'patch outside its lexical scope was not omitted';
    }
  }
  return null;
}
