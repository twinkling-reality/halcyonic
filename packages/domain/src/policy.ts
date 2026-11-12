import type { CommandType, ConfirmKind, PolicyCategory } from '@halcyonic/contracts';

/**
 * The consequence category of every command, recorded with each accepted command for audit.
 *
 * Categories tell clients which commands need an explicit confirmation (`review_required`,
 * `high_consequence`). They do not restrict which authenticated principal may act, except that a paired
 * device's restore and rebinding carry the digest of the review the person read (ADR 0051). Local and
 * device principals are admitted alike, except device-management routes that stay on loopback.
 * No `high_consequence` command exists (see docs/internal/architecture/SECURITY.md).
 */
export const COMMAND_POLICY: Readonly<Record<CommandType, PolicyCategory>> = {
  'project.create': 'low_consequence',
  // Rebinding changes where a project's later work runs: the person reads the review first (ADR 0051).
  'project.set_location': 'review_required',
  // The project's own words given to every task's agent from now on: the person reads them first (E5).
  'project.rules.set': 'review_required',
  // A new project from a starter beside the original, which it only reads; changes no existing file.
  'project.make_vr_version': 'low_consequence',
  // Writes over the project's files: the person reads what would change before confirming.
  'project.restore': 'review_required',
  // The host's own check and its serving of the checked copy on loopback; change no project file. A
  // project's own check runs the commands the person already chose, confined as an agent (ADR 0055).
  'project.check': 'low_consequence',
  // Which of the project's own commands the host may run as its check: the person reads them first.
  'project.own_check.choose': 'review_required',
  'project.preview.start': 'low_consequence',
  'project.preview.stop': 'low_consequence',
  // The checked copy, already run by the check, in the same contained browser shown in Halcyonic's
  // own viewer; it changes no project file and reaches nothing but the preview's port (ADR 0037).
  'project.open_on_mac': 'low_consequence',
  // Opening a Finder window on the Mac changes nothing; a saved copy writes a file there.
  'project.show_in_finder': 'low_consequence',
  // A copy of the project's files written beside the person's own, never over one: done on the press (R1).
  'project.save_copy': 'low_consequence',
  // A shared link lets anyone with it open the game: a deliberate press; stopping it is not.
  'project.share.start': 'review_required',
  'project.share.stop': 'low_consequence',
  'project.send': 'review_required',
  'project.publish': 'review_required',
  'project.unpublish': 'review_required',
  // The way back after a publish made the repository public: it narrows who can see it, as stopping a
  // share does; only the repository Halcyonic made and made public, one press (ADR 0039, amended).
  'project.make_private': 'low_consequence',
  // Gives the project's later tasks a key their agents can use and repeat (ADR 0057): a deliberate
  // press on this Mac's own page, which holds the value; removing one gives them nothing.
  'project.secret.set': 'review_required',
  // The value is gone for good and the person may not have it elsewhere: reviewed, then one press (R1).
  'project.secret.remove': 'review_required',
  // The person's own link between a project and a catalog repository; changes no file.
  'project.link_catalog': 'low_consequence',
  'project.unlink_catalog': 'low_consequence',
  'workstream.create': 'low_consequence',
  'workstream.add_to_project': 'review_required',
  'workstream.fit_to_project': 'low_consequence',
  'execution.start': 'low_consequence',
  'execution.send_instruction': 'low_consequence',
  'execution.respond_to_approval': 'review_required',
  'execution.answer_question': 'low_consequence',
  'execution.interrupt': 'review_required',
  // Dropping an instruction before it is sent changes nothing but the queue (ADR 0056).
  'execution.drop_queued_instruction': 'low_consequence',
  'workspace.request.submit': 'low_consequence',
  'workspace.request.resume': 'low_consequence',
  'workspace.request.cancel': 'low_consequence',
};

/**
 * What each command is called and does, in the host's own words, how it is confirmed, and whether the
 * assistant may propose it (ADR 0060; the lead's decision of 2026-11-12, R1 and R6). A proposal is data
 * until the person's press. Never proposed: turning a share on, answering a runtime's approval or
 * question (the person's alone), keeping a secret (its value is typed, never said), and the workspace
 * request's own commands (the assistant is that request). Pairing, a device's rights and deleting a
 * project are no commands here.
 */
export interface CommandSaying {
  /** The button's words: 1 to 3 words, Title Case, no brand name (WORDS.md). */
  readonly title: string;
  /** One sentence, in the host's words, of what the command does; at most 160 characters. */
  readonly description: string;
  /** Whether the assistant may propose it. */
  readonly sayable: boolean;
  readonly confirm: ConfirmKind;
  /** The command that undoes it, for a command confirmed with an Undo. */
  readonly undo?: CommandType;
}

export const COMMAND_SAYING: Readonly<Record<CommandType, CommandSaying>> = {
  'project.create': {
    title: 'New Project',
    description: 'Make a new project, in a folder on this computer or none.',
    sayable: true,
    confirm: 'none',
  },
  'project.set_location': {
    title: 'Change Folder',
    description: "Move the project's later work to another folder on this computer.",
    sayable: true,
    confirm: 'review',
  },
  'project.rules.set': {
    title: 'Project Rules',
    description: "Turn the project's own rules file on or off for its tasks' agents.",
    sayable: true,
    confirm: 'review',
  },
  'project.make_vr_version': {
    title: 'Make VR Version',
    description:
      'Make a VR version of the project in a new folder beside it, leaving the original as it is.',
    sayable: true,
    confirm: 'none',
  },
  'project.restore': {
    title: 'Restore',
    description:
      "Put every file of the project's folder back as one of its saved versions holds it.",
    sayable: true,
    confirm: 'review',
  },
  'project.check': {
    title: 'Check',
    description: 'Check the project now: build it and try it, or run the check it was given.',
    sayable: true,
    confirm: 'none',
  },
  'project.own_check.choose': {
    title: 'Choose Check',
    description: "Choose which of the project's own commands are its check.",
    sayable: true,
    confirm: 'review',
  },
  'project.preview.start': {
    title: 'Play',
    description: "Start serving the project's last passed check so it can be played.",
    sayable: true,
    confirm: 'undo',
    undo: 'project.preview.stop',
  },
  'project.preview.stop': {
    title: 'Stop Playing',
    description: "Stop serving the project's preview.",
    sayable: true,
    confirm: 'none',
  },
  'project.open_on_mac': {
    title: 'Play on Computer',
    description: "Play the project's last passed check on this computer.",
    sayable: true,
    confirm: 'none',
  },
  'project.show_in_finder': {
    title: 'Show Folder',
    description: "Show the project's folder in a window on this computer.",
    sayable: true,
    confirm: 'none',
  },
  'project.save_copy': {
    title: 'Save a Copy',
    description: "Save a copy of the project's files as a zip in this computer's Downloads folder.",
    sayable: true,
    confirm: 'none',
  },
  'project.share.start': {
    title: 'Share',
    description: "Share the project's checked game at an address others can open.",
    sayable: false,
    confirm: 'review',
  },
  'project.share.stop': {
    title: 'Stop Sharing',
    description: "Stop sharing the project's game.",
    sayable: true,
    confirm: 'none',
  },
  'project.secret.set': {
    title: 'Add Secret',
    description: 'Keep a secret for the project, given to its tasks when they start.',
    sayable: false,
    confirm: 'none',
  },
  'project.secret.remove': {
    title: 'Remove Secret',
    description: "Remove one of the project's secrets, so later tasks no longer get it.",
    sayable: true,
    confirm: 'review',
  },
  'project.send': {
    title: 'Send for Review',
    description:
      "Send the project's work to the person's own repository as a new branch for review.",
    sayable: true,
    confirm: 'review',
  },
  'project.publish': {
    title: 'Publish',
    description:
      "Publish the project's last passed check as a public page under the person's own account.",
    sayable: true,
    confirm: 'review',
  },
  'project.unpublish': {
    title: 'Take Down',
    description: "Take the project's published page down.",
    sayable: true,
    confirm: 'none',
  },
  'project.make_private': {
    title: 'Make Private',
    description:
      'Make the repository Halcyonic made for the project and made public private again.',
    sayable: true,
    confirm: 'none',
  },
  'project.link_catalog': {
    title: 'Link Repository',
    description: "Link the project to one of the repositories in the person's catalog.",
    sayable: true,
    confirm: 'none',
  },
  'project.unlink_catalog': {
    title: 'Unlink Repository',
    description: "Remove the project's link to its catalog repository.",
    sayable: true,
    confirm: 'none',
  },
  'workstream.create': {
    title: 'New Task',
    description: 'Make a new task in the project.',
    sayable: true,
    confirm: 'none',
  },
  'workstream.add_to_project': {
    title: 'Add to Project',
    description: "Add what a task changed in its own copy to the project's folder.",
    sayable: true,
    confirm: 'review',
  },
  'workstream.fit_to_project': {
    title: 'Fit to Project',
    description:
      "Ask a task's agent to fit its work to the project's changes where both changed the same places.",
    sayable: true,
    confirm: 'none',
  },
  'execution.start': {
    title: 'Start',
    description: "Start a task's agent on an instruction.",
    sayable: true,
    confirm: 'none',
  },
  'execution.send_instruction': {
    title: 'Send',
    description: "Give a task's agent an instruction.",
    sayable: true,
    confirm: 'none',
  },
  'execution.respond_to_approval': {
    title: 'Approve',
    description: "Approve or deny what a task's agent asked to do.",
    sayable: false,
    confirm: 'none',
  },
  'execution.answer_question': {
    title: 'Answer',
    description: "Answer a question a task's agent asked.",
    sayable: false,
    confirm: 'none',
  },
  'execution.interrupt': {
    title: 'Stop',
    description: "Stop a task's agent in its turn.",
    sayable: true,
    confirm: 'none',
  },
  'execution.drop_queued_instruction': {
    title: 'Drop',
    description: 'Drop an instruction that is waiting for the turn to end, before it is sent.',
    sayable: true,
    confirm: 'none',
  },
  'workspace.request.submit': {
    title: 'Ask',
    description: 'Ask the assistant something.',
    sayable: false,
    confirm: 'none',
  },
  'workspace.request.resume': {
    title: 'Answer',
    description: 'Answer the assistant so it carries on.',
    sayable: false,
    confirm: 'none',
  },
  'workspace.request.cancel': {
    title: 'Cancel',
    description: 'Stop a request to the assistant.',
    sayable: false,
    confirm: 'none',
  },
};
