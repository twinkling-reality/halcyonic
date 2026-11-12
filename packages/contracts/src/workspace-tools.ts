import Type from 'typebox';
import { EventId, Position, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;
/** Actual scoped coordinator tools. Target identifiers are supplied by the host, never the model. */
export const WORKSPACE_TOOL_ARGUMENTS = {
  read_workspace: Type.Object({}, strict),
  read_events: Type.Object({ after: Position }, strict),
  read_understanding: Type.Object({}, strict),
  read_evaluation: Type.Object({}, strict),
  continue_task: Type.Object({ text: Text(32000) }, strict),
  observe_task: Type.Object({}, strict),
  ask_person: Type.Object({ message: Text(4000) }, strict),
  read_results: Type.Object({ before: Type.Optional(Position) }, strict),
  read_project: Type.Object({}, strict),
  read_projects: Type.Object({}, strict),
  read_project_at: Type.Object({ index: Type.Integer({ minimum: 0, maximum: 63 }) }, strict),
  read_task: Type.Object({ index: Type.Integer({ minimum: 0, maximum: 63 }) }, strict),
  /**
   * Names one entry of what can be done here (ADR 0060) and what it still needs, by each need's name: words, a yes
   * or no, a count, or the index (or indices) of rows `read_choices` listed. Never an id: the host fills those.
   */
  propose_command: Type.Object(
    {
      command_type: Text(128),
      values: Type.Record(
        Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' }),
        Type.Union([
          Text(32000),
          Type.Integer({ minimum: 0, maximum: 1_000_000 }),
          Type.Boolean(),
          Type.Array(Type.Integer({ minimum: 0, maximum: 31 }), { maxItems: 8 }),
        ]),
        { maxProperties: 16 },
      ),
    },
    strict,
  ),
  /**
   * Reads one list the host holds, by the name an entry opens or a need's choices name (ADR 0060): rows by index, in
   * the host's words.
   */
  read_choices: Type.Object({ list: Text(128) }, strict),
  /** Picks one of the places the client said it can go, by index; the client goes there (ADR 0060). */
  go_to: Type.Object({ index: Type.Integer({ minimum: 0, maximum: 15 }) }, strict),
  read_result: Type.Object(
    {
      event_id: EventId,
      file_index: Type.Optional(Type.Integer({ minimum: 0, maximum: 31 })),
      part: Type.Optional(Type.Integer({ minimum: 0, maximum: 127 })),
    },
    strict,
  ),
} as const;
