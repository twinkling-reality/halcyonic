import type { CommandType, PolicyCategory } from '@halcyonic/contracts';

/**
 * The consequence category of every command, recorded with each accepted command for audit.
 *
 * Categories tell clients which commands need an explicit confirmation (`review_required`,
 * `high_consequence`). They do not restrict which authenticated principal may act. Local and
 * device principals are admitted alike, except device-management routes that stay on loopback.
 * No `high_consequence` command exists (see docs/internal/architecture/SECURITY.md).
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
  'workspace.request.submit': 'low_consequence',
  'workspace.request.resume': 'low_consequence',
  'workspace.request.cancel': 'low_consequence',
};
