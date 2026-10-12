import Type from 'typebox';
import { Position, Text } from './primitives.ts';

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
} as const;
