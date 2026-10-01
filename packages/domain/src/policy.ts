import type { CommandType, PolicyCategory } from '@halcyonic/contracts';

/**
 * The consequence category of every command, recorded with each accepted command for audit.
 *
 * Authorization today is a single local principal (see docs/internal/architecture/SECURITY.md),
 * so the category does not yet gate who may act. It tells clients which commands need an
 * explicit, deliberate confirmation, and it is where per-device policy will attach.
 */
export const COMMAND_POLICY: Readonly<Record<CommandType, PolicyCategory>> = {
  'project.create': 'low_consequence',
  'project.set_location': 'low_consequence',
  'workstream.create': 'low_consequence',
  'execution.start': 'low_consequence',
  'execution.send_instruction': 'low_consequence',
  'execution.respond_to_approval': 'review_required',
  'execution.answer_question': 'low_consequence',
  'execution.interrupt': 'review_required',
};
