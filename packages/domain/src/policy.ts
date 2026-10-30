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
  // A new project from a starter beside the original, which it only reads; changes no existing file.
  'project.make_vr_version': 'low_consequence',
  // Writes over the project's files: the person reads what would change before confirming.
  'project.restore': 'review_required',
  // The host's own check and its serving of the checked copy on loopback; change no project file.
  'project.check': 'low_consequence',
  'project.preview.start': 'low_consequence',
  'project.preview.stop': 'low_consequence',
  // The checked copy, already run by the check, in the same contained browser shown in Halcyonic's
  // own viewer; it changes no project file and reaches nothing but the preview's port (ADR 0037).
  'project.open_on_mac': 'low_consequence',
  // The person's own link between a project and a catalog repository; changes no file.
  'project.link_catalog': 'low_consequence',
  'project.unlink_catalog': 'low_consequence',
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
