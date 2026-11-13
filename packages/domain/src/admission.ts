import type {
  CommandEnvelope,
  CommandRejection,
  CommandType,
  ExecutionId,
  ExecutionStatus,
  PolicyCategory,
  ProjectId,
  ProjectView,
  QuestionAnswer,
  QuestionPrompt,
  RejectionCode,
  RuntimeCapabilities,
  RuntimeDescriptor,
  WorkstreamId,
  WorkstreamView,
} from '@halcyonic/contracts';
import {
  MAX_PROJECT_SECRETS,
  QUEUED_INSTRUCTIONS_MAX,
  WORLD_STARTER_ID,
} from '@halcyonic/contracts';
import { foldersOverlap } from './folders.ts';
import { COMMAND_POLICY } from './policy.ts';
import type { ExecutionFacts, Projection } from './projection.ts';
import { reservedSecretName } from './secrets.ts';
import type { StarterCatalog } from './starter-declarations.ts';

/** Why a spending limit is refused where the agent app keeps none. */
const NO_SPENDING_CAP = 'This app cannot stop a task at a spending limit, so none was set.';

export interface CommandScope {
  project_id: ProjectId | null;
  workstream_id: WorkstreamId | null;
  execution_id: ExecutionId | null;
}

export type Admission =
  | {
      admitted: true;
      policy: PolicyCategory;
      scope: CommandScope;
      /** The runtime the command will be dispatched to, when it targets one. */
      runtime: RuntimeDescriptor | null;
      /**
       * An instruction to a runtime that cannot steer, admitted to wait for the turn to end (ADR
       * 0056): the host holds it and sends it after a turn that completed.
       */
      queued?: true;
    }
  | { admitted: false; rejection: CommandRejection; scope: CommandScope };

export interface RuntimeCatalog {
  get(runtimeId: string): RuntimeDescriptor | undefined;
  /**
   * Whether the host holds instructions sent during a turn to a runtime that cannot steer, and
   * sends them after it (ADR 0056): only where its instruction queue runs, on the person's own
   * journal. Elsewhere, as in a recording, such an instruction is refused as the runtime declares.
   */
  readonly queuesInstructions?: boolean;
  /** What each starter declares (R2), so no rule compares a starter's id. */
  readonly starters?: StarterCatalog;
  /**
   * Whether a runtime here reads `name` for itself, as its adapter says, so a project's secret may
   * not take it (ADR 0057). Absent: only the names every program runs by are refused.
   */
  ownsEnvironmentName?(name: string): boolean;
}

const NO_SCOPE: CommandScope = { project_id: null, workstream_id: null, execution_id: null };

/**
 * Decides whether a well-formed command may be carried out now. Pure: it reads the projection
 * and the declared runtime capabilities and changes nothing. Runtime-specific option checks
 * happen afterwards in the adapter, because only the adapter knows its options.
 */
export function admitCommand(
  command: CommandEnvelope,
  projection: Projection,
  runtimes: RuntimeCatalog,
): Admission {
  return admit(command, projection, runtimes, false);
}

/**
 * Whether `command` could be carried out here now, for the list of what can be done here (ADR 0060):
 * its payload holds only what the scope binds and the host fills, so every check of a value the person
 * still gives is left to the whole admission the command meets once it is filled.
 */
export function admitHere(
  command: CommandEnvelope,
  projection: Projection,
  runtimes: RuntimeCatalog,
): Admission {
  return admit(command, projection, runtimes, true);
}

/**
 * The one admission, run whole for a command, or as a probe for what can be done here (ADR 0060):
 * a probe's payload holds only what the scope binds, so a check of a value the person still gives
 * (a runtime, a version, an answer) is left to the whole run, and one of a pending thing (an approval,
 * a question, a waiting instruction) asks whether there is any. Every check of the state is the same
 * line of code in both, so the list never offers what admission would refuse for the state.
 */
function admit(
  command: CommandEnvelope,
  projection: Projection,
  runtimes: RuntimeCatalog,
  probe: boolean,
): Admission {
  const policy = COMMAND_POLICY[command.command_type];
  const off = offList(command, projection);
  if (off !== undefined) return off;
  switch (command.command_type) {
    case 'workspace.request.submit': {
      const { project_id, workstream_id, execution_id } = command.payload;
      const scope = { project_id, workstream_id, execution_id };
      // A request about all of the person's work binds nothing (ADR 0030, amended 2026-11-10).
      if (project_id === null) {
        if (workstream_id === null && execution_id === null)
          return { admitted: true, policy, scope, runtime: null };
        return reject(
          NO_SCOPE,
          'invalid_state',
          'A request about all work names no task; one about a task names its project too.',
        );
      }
      if (!projection.project(project_id))
        return reject(scope, 'project_not_found', 'The selected project does not exist.');
      // A request about the whole project binds no task (ADR 0030, amended 2026-11-10).
      if (workstream_id === null && execution_id === null)
        return { admitted: true, policy, scope, runtime: null };
      if (workstream_id === null || execution_id === null)
        return reject(
          { project_id, workstream_id: null, execution_id: null },
          'invalid_state',
          "A request names both a task and the task's current execution, or neither.",
        );
      const work = workstream_id === null ? undefined : projection.workstream(workstream_id),
        run = execution_id === null ? undefined : projection.execution(execution_id);
      if (
        !work ||
        work.project_id !== project_id ||
        work.current_execution_id !== execution_id ||
        !run ||
        run.project_id !== project_id ||
        run.workstream_id !== workstream_id
      )
        return reject(
          scope,
          'invalid_state',
          'The request no longer matches the selected task and current execution.',
        );
      return { admitted: true, policy, scope, runtime: null };
    }
    case 'workspace.request.resume':
    case 'workspace.request.cancel': {
      const request = projection.workspaceRequest(command.payload.request_id);
      if (!request)
        return reject(NO_SCOPE, 'request_not_found', 'The workspace request does not exist.');
      const scope = {
        project_id: request.project_id,
        workstream_id: request.workstream_id,
        execution_id: request.execution_id,
      };
      if (
        command.command_type === 'workspace.request.resume' &&
        request.workstream_id !== null &&
        projection.workstream(request.workstream_id)?.current_execution_id !== request.execution_id
      )
        return reject(
          scope,
          'invalid_state',
          'The task has another execution. Start a new request for it.',
        );
      return { admitted: true, policy, scope, runtime: null };
    }
    case 'project.create':
      return { admitted: true, policy, scope: NO_SCOPE, runtime: null };

    case 'project.set_location': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      return {
        admitted: true,
        policy,
        scope: { ...NO_SCOPE, project_id: projectId },
        runtime: null,
      };
    }

    case 'project.rules.set': {
      // The project's own rules come from its folder (E5); whether the file still reads as reviewed is
      // the host's to check.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      if (command.payload.on && project.location === null)
        return reject(
          scope,
          'location_required',
          "A project's rules come from its folder, and the project has none. Choose a folder for the project first.",
        );
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.make_vr_version': {
      const projectId = command.payload.original_project_id;
      const original = projection.project(projectId);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (original === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      if (original.location === null)
        return reject(
          scope,
          'location_required',
          'The game has no folder to make a VR version from.',
        );
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.show_in_finder':
    case 'project.save_copy':
    case 'project.send': {
      // Keeping what was made (F3): the project's own folder, shown or saved by the host.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (project.location === null)
        return reject(scope, 'location_required', 'The project has no folder on this computer.');
      if (command.command_type !== 'project.show_in_finder') {
        // A copy taken, or work sent, while the folder is being written would hold half of it.
        const after =
          command.command_type === 'project.save_copy'
            ? 'Save a copy once it has finished.'
            : 'Send it once it has finished.';
        const writing = projection
          .pendingCommands()
          .find(
            (pending) =>
              pending.project_id === projectId &&
              (pending.command_type === 'project.restore' ||
                pending.command_type === 'workstream.add_to_project'),
          );
        if (writing !== undefined)
          return reject(
            scope,
            'invalid_state',
            `${commandInWords(writing.command_type)} in this project is still under way. ${after}`,
          );
        // A turn at work may leave a file half written (AUDIT2 S1), as a restore waits for it.
        const busy = projection.projectBusy(projectId);
        if (busy !== null) return reject(scope, 'invalid_state', `${busy} ${after}`);
      }
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.secret.set':
    case 'project.secret.remove': {
      // Names only (ADR 0057): the setup page put the value in the store before it submitted this.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      const name = command.payload.name;
      const held = project.secrets ?? [];
      // A probe has no name yet: one is there to remove, or there is room for one more.
      if (probe) {
        if (command.command_type === 'project.secret.remove' && held.length === 0)
          return reject(scope, 'invalid_state', 'This project has no secrets.');
        if (command.command_type === 'project.secret.set' && held.length >= MAX_PROJECT_SECRETS)
          return reject(
            scope,
            'invalid_state',
            `A project keeps at most ${MAX_PROJECT_SECRETS} secrets. Remove one first.`,
          );
        return { admitted: true, policy, scope, runtime: null };
      }
      if (command.command_type === 'project.secret.remove')
        return held.includes(name)
          ? { admitted: true, policy, scope, runtime: null }
          : reject(scope, 'invalid_state', `This project has no secret named ${name}.`);
      const reserved = reservedSecretName(
        name,
        (taken) => runtimes.ownsEnvironmentName?.(taken) === true,
      );
      if (reserved !== null) return reject(scope, 'invalid_state', reserved);
      if (!held.includes(name) && held.length >= MAX_PROJECT_SECRETS)
        return reject(
          scope,
          'invalid_state',
          `A project keeps at most ${MAX_PROJECT_SECRETS} secrets. Remove one first.`,
        );
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.link_catalog':
    case 'project.unlink_catalog': {
      const projectId = command.payload.project_id;
      const link = projection.catalogLink(projectId);
      if (link === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (command.command_type === 'project.unlink_catalog' && link === null)
        return reject(scope, 'invalid_state', 'The project is not linked to a catalog repository.');
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.restore': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (
        probe
          ? projection.checkpoints(projectId).length === 0
          : projection.checkpoint(projectId, command.payload.checkpoint_id) === undefined
      ) {
        return reject(
          scope,
          'checkpoint_not_found',
          probe
            ? 'The project has no saved version to put back.'
            : `Checkpoint ${command.payload.checkpoint_id} is not one of this project's.`,
        );
      }
      // Files the runtime is still writing cannot be put back, and a restore must not race a start.
      const busy = projection.projectBusy(projectId);
      if (busy !== null) {
        return reject(scope, 'invalid_state', `${busy} Stop it before putting files back.`);
      }
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.check':
    case 'project.preview.start':
    case 'project.preview.stop': {
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (command.command_type === 'project.check') {
        if (project.location === null)
          return reject(scope, 'invalid_state', 'This project has no folder to check.');
        // A project not made from a starter runs the check the person chose (ADR 0055).
        if (project.starter === null && projection.ownCheck(projectId) === undefined) {
          return reject(
            scope,
            'invalid_state',
            'Choose a check for this project first: its own commands, from its package.json.',
          );
        }
        // A world is a description, read and validated when it changes (ADR 0049): it has no build.
        // A starter that says it has no build is not checked (R2). The host's catalog says a starter it
        // cannot read has none (AUDIT2 S1). Without a catalog the domain cannot say, and no host checks
        // without one: a check needs the previews, which a host has only beside its starters (main.ts).
        if (
          project.starter !== null &&
          runtimes.starters?.declares(project.starter).check === 'none'
        ) {
          return reject(scope, 'invalid_state', 'A world has no build to check.');
        }
        const taskId = command.payload.workstream_id;
        if (taskId !== undefined) {
          // A check of one task's own copy (ADR 0045) waits for that task alone, and for the
          // project's own checks and restores; other tasks work in copies of their own.
          const task = projection.workstream(taskId);
          if (task === undefined || task.project_id !== projectId)
            return reject(
              scope,
              'workstream_not_found',
              `Workstream ${taskId} is not this project's.`,
            );
          const taskScope = { ...scope, workstream_id: task.workstream_id };
          if (task.copy?.state !== 'own')
            return reject(
              taskScope,
              'invalid_state',
              "This task works in the project's own folder: check the project.",
            );
          const busy = taskInUse(projection, task.workstream_id, null);
          if (busy?.kind === 'active')
            return reject(
              taskScope,
              'invalid_state',
              `This task is ${inWords(busy.status)}. Check it once it stops.`,
            );
          if (busy?.kind === 'pending')
            return reject(
              taskScope,
              'invalid_state',
              `${commandInWords(busy.commandType)} for this task is still under way. Check it once that has finished.`,
            );
          const other = projection
            .pendingCommands()
            .find(
              (pending) =>
                pending.project_id === projectId &&
                (pending.command_type === 'project.check' ||
                  pending.command_type === 'project.own_check.choose' ||
                  pending.command_type === 'project.restore' ||
                  pending.command_type === 'project.preview.start'),
            );
          if (other !== undefined)
            return reject(
              taskScope,
              'invalid_state',
              `${commandInWords(other.command_type)} in this project is still under way. Check it once that has finished.`,
            );
          return { admitted: true, policy, scope: taskScope, runtime: null };
        }
        // A check of files a runtime is still writing would describe neither before nor after.
        const busy = projection.projectBusy(projectId);
        if (busy !== null) return reject(scope, 'invalid_state', `${busy} Check it once it stops.`);
        return { admitted: true, policy, scope, runtime: null };
      }
      const preview = projection.preview(projectId);
      if (command.command_type === 'project.preview.stop') {
        if (preview === undefined)
          return reject(scope, 'invalid_state', 'The project has no preview running.');
        return { admitted: true, policy, scope, runtime: null };
      }
      if (preview !== undefined)
        return reject(scope, 'invalid_state', 'The project already has a preview running.');
      const check = projection.latestCheck(projectId);
      if (check === undefined) {
        return reject(
          scope,
          'invalid_state',
          'Only the copy of a passed check is served. Check the project first.',
        );
      }
      if (check.files === 'changed') {
        return reject(
          scope,
          'invalid_state',
          "The project's files changed since its last check. Check the project again first.",
        );
      }
      if (check.files !== 'current') {
        return reject(
          scope,
          'invalid_state',
          "The project's files may have changed since its last check. Check the project again first.",
        );
      }
      if (!check.findings.passed) {
        return reject(
          scope,
          'invalid_state',
          'Only the copy of a passed check is served. Check the project first.',
        );
      }
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.open_on_mac': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      // Only the checked copy a person could already play: current, passed, ready on a laptop, every
      // agent run confined, and served by the preview right now (ADR 0037, T2).
      const check = projection.latestCheck(projectId);
      const findings = check?.findings;
      if (check === undefined || findings === undefined)
        return reject(
          scope,
          'invalid_state',
          'Only a passed check can be played. Check the game first.',
        );
      if (check.files === 'changed')
        return reject(
          scope,
          'invalid_state',
          "The game's files changed since its last check. Check the game again first.",
        );
      if (check.files !== 'current')
        return reject(
          scope,
          'invalid_state',
          "The game's files may have changed since its last check. Check the game again first.",
        );
      if (!findings.passed)
        return reject(
          scope,
          'invalid_state',
          'Only a passed check can be played. Check the game first.',
        );
      if (findings.laptop?.ready !== true)
        return reject(
          scope,
          'invalid_state',
          "The check didn't see the game work with a mouse and keyboard.",
        );
      if (!findings.reads_confined)
        return reject(
          scope,
          'invalid_state',
          'Not every agent run in this project was confined, so it is not played here.',
        );
      const preview = projection.preview(projectId);
      if (preview?.check_id !== check.check_id || preview.files !== 'current')
        return reject(scope, 'invalid_state', 'Start the preview of the latest check first.');
      if (projection.macPlay(projectId) !== undefined)
        return reject(scope, 'invalid_state', 'This game is already playing on the Mac.');
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.publish':
    case 'project.unpublish': {
      // Publish (ADR 0039, amended): only a passed, current, confined check's built copy, as a share.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (project.location === null)
        return reject(scope, 'location_required', 'The project has no folder on this computer.');
      if (command.command_type === 'project.unpublish') {
        if (project.published === undefined)
          return reject(scope, 'invalid_state', 'This game is not published.');
        // What the person was shown is what comes down, from any client (AUDIT3 S1).
        if (!probe && project.published.commit !== command.payload.commit)
          return reject(
            scope,
            'review_stale',
            'The game was published again since it was shown. Look at it again, then take it down.',
          );
        return { admitted: true, policy, scope, runtime: null };
      }
      const check = projection.latestCheck(projectId);
      if (check === undefined || !check.findings.passed)
        return reject(
          scope,
          'invalid_state',
          'Only a passed check can be published. Check the game first.',
        );
      if (check.files !== 'current')
        return reject(
          scope,
          'invalid_state',
          "The game's files may have changed since its last check. Check the game again first.",
        );
      if (!check.findings.reads_confined)
        return reject(
          scope,
          'invalid_state',
          'Not every agent run in this project was confined, so it is not published.',
        );
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.make_private': {
      // The way back after Make Public (ADR 0039, amended): only the repository this project's first Send
      // made, while this Mac's journal says Halcyonic made it public, and with no game published from it.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (project.location === null)
        return reject(scope, 'location_required', 'The project has no folder on this computer.');
      if (projection.madeRepository(projectId)?.visibility !== 'public')
        return reject(
          scope,
          'invalid_state',
          'Halcyonic made no repository of this project public, so there is nothing to make private.',
        );
      if (project.published !== undefined)
        return reject(scope, 'invalid_state', 'The game is published from it. Take it down first.');
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.share.start':
    case 'project.share.stop': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      const share = projection.share(projectId);
      if (command.command_type === 'project.share.stop')
        return share === undefined
          ? reject(scope, 'invalid_state', 'This game is not shared.')
          : { admitted: true, policy, scope, runtime: null };
      if (share !== undefined)
        return reject(scope, 'invalid_state', 'This game is already shared.');
      // Only the checked build the preview serves right now, current and passed (F1).
      const check = projection.latestCheck(projectId);
      if (check === undefined || !check.findings.passed)
        return reject(
          scope,
          'invalid_state',
          'Only a passed check can be shared. Check the game first.',
        );
      if (check.files !== 'current')
        return reject(
          scope,
          'invalid_state',
          "The game's files may have changed since its last check. Check the game again first.",
        );
      // An agent that could read outside its folder may have put the person's files in the build.
      if (!check.findings.reads_confined)
        return reject(
          scope,
          'invalid_state',
          'Not every agent run in this project was confined, so it is not shared.',
        );
      const preview = projection.preview(projectId);
      if (preview?.check_id !== check.check_id || preview.files !== 'current')
        return reject(scope, 'invalid_state', 'Start the preview of the latest check first.');
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'workstream.create': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      // A task from a version starts from one of this project's own (ADR 0045).
      const from = command.payload.from_checkpoint;
      // A world changes in place as the person speaks, with no copy for a version to be made into.
      if (from !== undefined && projection.project(projectId)?.starter === WORLD_STARTER_ID)
        return reject(
          { ...NO_SCOPE, project_id: projectId },
          'invalid_state',
          "A world changes as you speak to it, so a task can't start from an older version of it.",
        );
      const version =
        from === undefined
          ? undefined
          : projection
              .checkpoints(projectId)
              .find((checkpoint) => checkpoint.checkpoint_id === from);
      if (from !== undefined && version === undefined) {
        // One the host let go by its pruning rule says so, as a start from it would (AUDIT3 S1).
        const pruned = projection
          .history(projectId)
          .some(
            (entry) =>
              entry.kind === 'version' &&
              entry.checkpoint.checkpoint_id === from &&
              entry.pruned_at !== undefined,
          );
        return reject(
          { ...NO_SCOPE, project_id: projectId },
          'checkpoint_not_found',
          pruned
            ? 'That version is no longer kept. Choose another one.'
            : "That version is not one of this project's.",
        );
      }
      // A version of another task's own copy holds that task's work; unless that work was added to
      // the project since, a task started on it would carry work the project never took (AUDIT3 S2).
      if (version !== undefined && version.execution_id !== null) {
        const execution = projection.execution(version.execution_id);
        const copy =
          execution === undefined
            ? undefined
            : projection.workstream(execution.workstream_id)?.copy;
        if (
          copy?.state === 'own' &&
          execution?.directory === copy.directory &&
          (copy.last_added_at === null || copy.last_added_at < version.recorded_at)
        )
          return reject(
            { ...NO_SCOPE, project_id: projectId },
            'invalid_state',
            "That version holds a task's work that was never added to the project. Choose another version, or add that task's work first.",
          );
      }
      return {
        admitted: true,
        policy,
        scope: { ...NO_SCOPE, project_id: projectId },
        runtime: null,
      };
    }

    case 'workstream.add_to_project':
    case 'workstream.fit_to_project': {
      const workstream = projection.workstream(command.payload.workstream_id);
      if (workstream === undefined) {
        return reject(
          NO_SCOPE,
          'workstream_not_found',
          `Workstream ${command.payload.workstream_id} does not exist.`,
        );
      }
      const scope: CommandScope = {
        project_id: workstream.project_id,
        workstream_id: workstream.workstream_id,
        execution_id: null,
      };
      const adding = command.command_type === 'workstream.add_to_project';
      const copy = workstream.copy;
      if (copy?.state !== 'own') {
        return reject(
          scope,
          'invalid_state',
          copy?.state === 'refused'
            ? "This task works in the project's own folder, so its changes are already there."
            : 'This task has no copy of its own yet: it gets one when it starts.',
        );
      }
      if (!adding && copy.conflict === null) {
        return reject(
          scope,
          'invalid_state',
          "Nothing to fit: the task's work and the project's did not meet at its last add.",
        );
      }
      // Its copy must be still while it is read or written (ADR 0045).
      const busy = taskInUse(projection, workstream.workstream_id, null);
      if (busy?.kind === 'active') {
        return reject(
          scope,
          'invalid_state',
          `This task is ${inWords(busy.status)}. ${untilFree(busy.status)}`,
        );
      }
      if (busy?.kind === 'pending') {
        return reject(
          scope,
          'invalid_state',
          `${commandInWords(busy.commandType)} for this task is still under way. Wait for its outcome first.`,
        );
      }
      if (projection.restoreInProgress(workstream.project_id)) {
        return reject(
          scope,
          'invalid_state',
          "The project's files are being put back. Try again once that has finished.",
        );
      }
      const location = projection.project(workstream.project_id)?.location ?? null;
      if (location === null) {
        return reject(scope, 'location_required', 'The project has no folder to add the work to.');
      }
      // The project's folder takes one writer, and a fit reads it whole: no add, check or move of
      // it under way, and, for an add, no task working in it directly.
      const writing = projection
        .pendingCommands()
        .find(
          (pending) =>
            pending.project_id === workstream.project_id &&
            PROJECT_FOLDER_COMMANDS.has(pending.command_type),
        );
      if (writing !== undefined) {
        return reject(
          scope,
          'invalid_state',
          `${commandInWords(writing.command_type)} in this project is still under way. Wait for its outcome, then try again.`,
        );
      }
      if (!adding) return { admitted: true, policy, scope, runtime: null };
      const refusal = folderRefusal(projection, location.path, workstream.workstream_id, scope);
      if (refusal !== undefined) return refusal;
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'execution.start': {
      const workstream = projection.workstream(command.payload.workstream_id);
      if (workstream === undefined) {
        return reject(
          NO_SCOPE,
          'workstream_not_found',
          `Workstream ${command.payload.workstream_id} does not exist.`,
        );
      }
      const scope: CommandScope = {
        project_id: workstream.project_id,
        workstream_id: workstream.workstream_id,
        execution_id: null,
      };
      // The runtime is the person's to choose: a probe leaves every check of it to the whole run.
      const chosen = probe ? undefined : runtimes.get(command.payload.runtime_id);
      if (!probe && chosen === undefined) {
        return reject(
          scope,
          'runtime_not_found',
          `Runtime ${command.payload.runtime_id} is not registered.`,
        );
      }
      const refused = chosen === undefined ? undefined : startRefusal(chosen, command, scope);
      if (refused !== undefined) return refused;
      // One execution at a time in a task: a start beside an active one, or before a command to one
      // has its outcome, would put two agents to work in the task's folder (ADMIT).
      const busy = taskInUse(projection, workstream.workstream_id, null);
      if (busy?.kind === 'active') {
        return reject(
          scope,
          'invalid_state',
          `This task is ${inWords(busy.status)}. Stop it first, or start another task.`,
        );
      }
      if (busy?.kind === 'pending') {
        return reject(
          scope,
          'invalid_state',
          `${commandInWords(busy.commandType)} for this task is still under way. Wait for its outcome, or start another task.`,
        );
      }
      if (projection.restoreInProgress(workstream.project_id)) {
        return reject(
          scope,
          'invalid_state',
          "The project's files are being put back. Start the task once that has finished.",
        );
      }
      if (chosen === undefined) return { admitted: true, policy, scope, runtime: null };
      const runtime = chosen;
      const location = projection.project(workstream.project_id)?.location ?? null;
      if (runtime.uses_project_location && location === null) {
        return reject(
          scope,
          'location_required',
          `Runtime ${runtime.runtime_id} works in the project's folder, and the project has none. Choose a folder for the project first.`,
        );
      }
      if (fromTheFolder(command.payload.attachments) && location === null) {
        return reject(scope, 'location_required', PICTURES_NEED_A_FOLDER);
      }
      // A task with a copy of its own works there; one without works in the project's folder
      // until the host makes it one (ADR 0045).
      const folder = taskFolder(projection, workstream);
      if (runtime.uses_project_location && folder !== null) {
        const refusal = folderRefusal(projection, folder, workstream.workstream_id, scope);
        if (refusal !== undefined) return refusal;
      }
      return { admitted: true, policy, scope, runtime };
    }
    case 'execution.send_instruction': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      if (!facts.hasNativeSession) {
        return reject(
          scope,
          'invalid_state',
          'The execution never started, so it cannot be instructed.',
        );
      }
      const pictures = picturesRefusal(runtime, command.payload.attachments, scope);
      if (pictures !== undefined) return pictures;
      if (
        fromTheFolder(command.payload.attachments) &&
        (projection.project(facts.projectId)?.location ?? null) === null
      ) {
        return reject(scope, 'location_required', PICTURES_NEED_A_FOLDER);
      }
      if (projection.restoreInProgress(facts.projectId)) {
        return reject(
          scope,
          'invalid_state',
          "The project's files are being put back. Send the instruction once that has finished.",
        );
      }
      // Carrying a plan out (PLAN): only a plan can be left, once, on a runtime that offers plans.
      if (command.payload.leave_plan === true) {
        if (runtime.offers_plan !== true) {
          return reject(
            scope,
            'capability_unsupported',
            `Runtime ${runtime.runtime_id} does not offer planning, so there is no plan to carry out.`,
          );
        }
        if (!facts.inPlan) {
          return reject(
            scope,
            'invalid_state',
            'This task is not a plan now, so there is no plan to carry out. Send the instruction without it.',
          );
        }
        // Only at rest: switched while a turn runs or waits for the person, the steered words could
        // still be answered on the plan (AUDIT3 4a S2).
        if (!['completed', 'failed', 'interrupted'].includes(facts.status)) {
          return reject(scope, 'invalid_state', 'Carry the plan out once it stops.');
        }
      }
      // A task at its limit, or waiting with its spending not known, carries on only under a limit
      // raised now (lead's ruling, 2026-11-13); every sender comes through here, the host's own too.
      if (command.payload.spending_cap_usd !== undefined) {
        if (runtime.offers_spending_cap !== true) {
          return reject(scope, 'capability_unsupported', NO_SPENDING_CAP);
        }
        if (facts.spendingCap !== 'reached' && facts.spendingCap !== 'waiting') {
          return reject(
            scope,
            'invalid_state',
            'A limit is raised only for a task that is at its limit or waiting for one. Send the instruction without it.',
          );
        }
      } else if (facts.spendingCap === 'reached') {
        return reject(
          scope,
          'invalid_state',
          'This task is at its spending limit. Raise it to carry on.',
        );
      } else if (facts.spendingCap === 'waiting') {
        return reject(
          scope,
          'invalid_state',
          'What this task spent before the restart is not fully known. Give it a limit that counts from now to carry on.',
        );
      }
      switch (facts.status) {
        case 'completed':
        case 'failed':
        case 'interrupted': {
          const missing = requireCapability(runtime, 'instruct_at_rest', scope);
          if (missing !== undefined) return missing;
          // An instruction sets a resting execution going, so not beside another of its task that
          // is active or about to be: two agents would work in one folder (ADMIT-2).
          const busy = taskInUse(projection, facts.workstreamId, facts.executionId);
          if (busy?.kind === 'active') {
            return reject(
              scope,
              'invalid_state',
              `This task is ${inWords(busy.status)} in another execution. Stop it first, or instruct that one.`,
            );
          }
          if (busy?.kind === 'pending') {
            return reject(
              scope,
              'invalid_state',
              `${commandInWords(busy.commandType)} for this task is still under way. Wait for its outcome first.`,
            );
          }
          if (facts.directory !== null) {
            const refusal = folderRefusal(projection, facts.directory, facts.workstreamId, scope);
            if (refusal !== undefined) return refusal;
          }
          return { admitted: true, policy, scope, runtime };
        }
        case 'running':
        case 'verifying':
        case 'waiting_for_human': {
          if (runtime.capabilities.instruct_while_running || runtimes.queuesInstructions !== true) {
            const missing = requireCapability(runtime, 'instruct_while_running', scope);
            return missing ?? { admitted: true, policy, scope, runtime };
          }
          // A runtime that cannot steer: the host holds the instruction until the turn ends, then
          // sends it as an instruction at rest (ADR 0056). Not where nothing could send it then.
          const missing = requireCapability(runtime, 'instruct_at_rest', scope);
          if (missing !== undefined) return missing;
          const waiting = projection.execution(facts.executionId)?.queued_instructions?.length ?? 0;
          if (waiting >= QUEUED_INSTRUCTIONS_MAX) {
            return reject(
              scope,
              'invalid_state',
              `${QUEUED_INSTRUCTIONS_MAX} instructions are already waiting for this turn to end. Wait for it, or drop one.`,
            );
          }
          return { admitted: true, policy, scope, runtime, queued: true };
        }
        case 'starting':
        case 'unknown':
          return reject(
            scope,
            'invalid_state',
            `The execution is ${facts.status}; instructions cannot be delivered now.`,
          );
        default: {
          const unhandled: never = facts.status;
          throw new Error(`unhandled status ${String(unhandled)}`);
        }
      }
    }

    case 'execution.respond_to_approval': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'respond_to_approval', scope);
      if (missing !== undefined) return missing;
      const approval = probe
        ? facts.pendingApprovals[0]
        : facts.pendingApprovals.find(
            (pending) => pending.approval_id === command.payload.approval_id,
          );
      if (approval === undefined) {
        return reject(
          scope,
          'approval_not_found',
          probe
            ? 'Nothing is waiting for an approval on this execution.'
            : `Approval ${command.payload.approval_id} is not pending on this execution.`,
        );
      }
      if (facts.status !== 'waiting_for_human') {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; the approval cannot be answered now.`,
        );
      }
      // What the person could not read whole may be refused, never approved. After the status, so
      // the deny this suggests is not refused for it.
      if (!approval.approvable && command.payload.decision === 'approve') {
        return reject(
          scope,
          'approval_not_whole',
          'This request was never shown whole, so it can only be denied.',
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    case 'execution.answer_question': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'answer_question', scope);
      if (missing !== undefined) return missing;
      const question = probe
        ? facts.pendingQuestions[0]
        : facts.pendingQuestions.find(
            (pending) => pending.question_id === command.payload.question_id,
          );
      if (question === undefined) {
        return reject(
          scope,
          'question_not_found',
          probe
            ? 'No question is waiting for an answer on this execution.'
            : `Question ${command.payload.question_id} is not shown for an answer on this execution: it was resolved, or waits behind the questions shown.`,
        );
      }
      // A secret is never carried, whatever the adapter said (ADR 0022).
      if (question.prompts.some((prompt) => prompt.secret)) {
        return reject(
          scope,
          'capability_unsupported',
          'This question asks for something secret, which Halcyonic never sends. Stop the turn to go on.',
        );
      }
      if (!question.answerable) {
        return reject(
          scope,
          'capability_unsupported',
          'This question cannot be answered through Halcyonic. Stop the turn to go on.',
        );
      }
      if (facts.status !== 'waiting_for_human') {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; the question cannot be answered now.`,
        );
      }
      const problem = probe ? null : answerProblem(question.prompts, command.payload.answers);
      if (problem !== null) return reject(scope, 'invalid_answer', problem);
      return { admitted: true, policy, scope, runtime };
    }

    case 'execution.drop_queued_instruction': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { scope, runtime } = target;
      const waiting = projection.execution(command.payload.execution_id)?.queued_instructions ?? [];
      if (
        probe
          ? waiting.length === 0
          : !waiting.some((queued) => queued.command_id === command.payload.command_id)
      ) {
        return reject(
          scope,
          'invalid_state',
          'That instruction is not waiting: it was sent already, or never queued.',
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    case 'execution.interrupt': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'interrupt', scope);
      if (missing !== undefined) return missing;
      if (
        facts.status !== 'running' &&
        facts.status !== 'verifying' &&
        facts.status !== 'waiting_for_human'
      ) {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; there is no running turn to interrupt.`,
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    case 'project.own_check.choose': {
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      if (project.starter !== null)
        return reject(
          scope,
          'invalid_state',
          'A project made from a starter is checked by your computer itself.',
        );
      if (project.location === null)
        return reject(scope, 'invalid_state', 'This project has no folder to check.');
      // Choosing runs the check once: never over files a runtime is still writing.
      const busy = projection.projectBusy(projectId);
      if (busy !== null)
        return reject(scope, 'invalid_state', `${busy} Choose its check once it stops.`);
      return { admitted: true, policy, scope, runtime: null };
    }

    case 'project.remove_from_list': {
      // Taking a project off the list deletes nothing and touches no file (ADR 0063); it waits for every
      // thing of the project's that is at work or live, so nothing goes on unseen.
      const projectId = command.payload.project_id;
      const project = projection.project(projectId);
      if (project === undefined)
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      const scope = { ...NO_SCOPE, project_id: projectId };
      const refusal = removalRefusal(projection, project);
      return refusal === null
        ? { admitted: true, policy, scope, runtime: null }
        : reject(scope, 'invalid_state', refusal);
    }

    case 'assistant.model.choose':
      // Which hosted models exist and whose key is stored is the host's to say (CommandService's host check): the
      // domain knows no provider. The choice belongs to no project.
      return { admitted: true, policy, scope: NO_SCOPE, runtime: null };

    default: {
      const unhandled: never = command;
      throw new Error(
        `unhandled command ${String((unhandled as { command_type?: unknown }).command_type)}`,
      );
    }
  }
}

/** The words a command naming a project taken off the list is refused with (ADR 0063). */
export const OFF_LIST_WORDS =
  'This project was taken off the list. Add its folder again to bring it back.';

/** The project a command names, through its project, a task or an execution of it, or a request about it. */
function namedProject(command: CommandEnvelope, projection: Projection): ProjectId | null {
  const payload = command.payload as Record<string, unknown>;
  const text = (value: unknown) => (typeof value === 'string' ? value : null);
  const project = text(payload.project_id) ?? text(payload.original_project_id);
  if (project !== null) return project as ProjectId;
  const workstream = text(payload.workstream_id);
  if (workstream !== null) return projection.workstream(workstream)?.project_id ?? null;
  const execution = text(payload.execution_id);
  if (execution !== null) return projection.execution(execution)?.project_id ?? null;
  const request = text(payload.request_id);
  if (request !== null) return projection.workspaceRequest(request)?.project_id ?? null;
  return null;
}

/**
 * The one refusal of every command naming a project taken off the list, or one being taken off (ADR 0063),
 * before any command's own checks. Ending a request stays open, so nothing about the project is left running.
 */
function offList(
  command: CommandEnvelope,
  projection: Projection,
): (Admission & { admitted: false }) | undefined {
  if (command.command_type === 'workspace.request.cancel') return undefined;
  const projectId = namedProject(command, projection);
  if (projectId === null || projection.project(projectId) === undefined) return undefined;
  if (!projection.listed(projectId)) return reject(NO_SCOPE, 'project_not_found', OFF_LIST_WORDS);
  const removing = projection
    .pendingCommands()
    .some(
      (pending) =>
        pending.command_type === 'project.remove_from_list' &&
        pending.project_id === projectId &&
        pending.command_id !== command.command_id,
    );
  if (removing)
    return reject(
      { ...NO_SCOPE, project_id: projectId },
      'invalid_state',
      'This project is being taken off the list. Try again in a moment.',
    );
  return undefined;
}

/**
 * Why the project cannot be taken off the list now, in words, or null (ADR 0063). `except` is the removal's
 * own command, which the host's reading again before it records leaves out.
 */
export function removalRefusal(
  projection: Projection,
  project: ProjectView,
  except?: string,
): string | null {
  // A request of the assistant's about it ends itself once the project is off the list, its press included.
  const busy = projection.projectBusy(project.project_id, except, false);
  if (busy !== null)
    // A task has a Stop; a command still under way has none, and its name is not a person's word.
    return busy.startsWith('A task')
      ? `${busy} Stop it, then remove the project.`
      : 'Something in this project is still under way. Wait for it to finish, then remove the project.';
  if (project.share !== undefined) return 'This game is shared now. Stop sharing it first.';
  if (project.mac_play !== undefined) return 'This game is playing on the Mac now. Stop it first.';
  if (project.preview !== undefined) return 'This game is previewing now. Stop the preview first.';
  if (project.location === null && (project.secrets?.length ?? 0) > 0)
    return 'Remove its secrets first. It has no folder, so it could not be brought back to remove them later.';
  return null;
}

/** What refuses a start on the chosen runtime: its capabilities and the options the person chose. */
function startRefusal(
  runtime: RuntimeDescriptor,
  command: Extract<CommandEnvelope, { command_type: 'execution.start' }>,
  scope: CommandScope,
): Admission | undefined {
  const missing = requireCapability(runtime, 'start_execution', scope);
  if (missing !== undefined) return missing;
  if (command.payload.model_ref !== null && runtime.model_choice !== 'listed') {
    return reject(
      scope,
      'capability_unsupported',
      `Runtime ${runtime.runtime_id} does not offer a choice of model.`,
    );
  }
  // Never asking is a level a runtime offers once verified, never one admission assumes (ADR 0043).
  if (command.payload.approval_level === 'never_ask' && runtime.offers_never_ask !== true) {
    return reject(
      scope,
      'capability_unsupported',
      `Runtime ${runtime.runtime_id} does not offer working without asking.`,
    );
  }
  // A plan is the runtime's own mode, offered once verified, never one admission assumes (PLAN).
  if (command.payload.plan === true && runtime.offers_plan !== true) {
    return reject(
      scope,
      'capability_unsupported',
      `Runtime ${runtime.runtime_id} does not offer planning without changing a file.`,
    );
  }
  // A spending limit is the runtime's own stop, offered once verified; the host never stops a task
  // on its own sums.
  if (command.payload.spending_cap_usd !== undefined && runtime.offers_spending_cap !== true) {
    return reject(scope, 'capability_unsupported', NO_SPENDING_CAP);
  }
  const pictures = picturesRefusal(runtime, command.payload.attachments, scope);
  if (pictures !== undefined) return pictures;
  // Planning asks before every command that could change a file, so it never goes unasked.
  if (command.payload.plan === true && command.payload.approval_level === 'never_ask') {
    return reject(
      scope,
      'invalid_runtime_options',
      'A plan asks before every command, so it is not started with working without asking.',
    );
  }
  // A runtime that lists its models never chooses one by itself, where it could pick a
  // hosted model nobody chose.
  if (command.payload.model_ref === null && runtime.model_choice === 'listed') {
    return reject(
      scope,
      'model_required',
      'Choose a model: this runtime lists the models it can use.',
    );
  }
  return undefined;
}

/**
 * Why answers do not fit the questions they answer, or null when they do: one answer per question,
 * each choosing only offered labels, one unless several are allowed, and typed text only where the
 * question takes it.
 */
/**
 * A rejected command as it is journaled. The journal is the audit record and goes to every client,
 * so a refused answer keeps only the keys it named: what was chosen or typed may be what the
 * question should never have received, such as a secret.
 */
export function journaledRejection(command: CommandEnvelope): CommandEnvelope {
  if (command.command_type !== 'execution.answer_question') return command;
  return {
    ...command,
    payload: {
      ...command.payload,
      answers: command.payload.answers.map((answer) => ({
        key: answer.key,
        selected: [],
        text: null,
      })),
    },
  };
}

export function answerProblem(
  prompts: readonly QuestionPrompt[],
  answers: readonly QuestionAnswer[],
): string | null {
  const byKey = new Map(prompts.map((prompt) => [prompt.key, prompt]));
  const answered = new Set<string>();
  for (const answer of answers) {
    const prompt = byKey.get(answer.key);
    if (prompt === undefined) return 'An answer names a question this request does not ask.';
    if (answered.has(answer.key)) return 'A question is answered twice.';
    answered.add(answer.key);
    const labels = new Set(prompt.options.map((option) => option.label));
    if (answer.selected.some((label) => !labels.has(label))) {
      return 'An answer chooses an option the question does not offer.';
    }
    if (new Set(answer.selected).size !== answer.selected.length) {
      return 'An answer chooses the same option twice.';
    }
    if (!prompt.multiple && answer.selected.length > 1) {
      return 'An answer chooses several options for a question that takes one.';
    }
    if (answer.text !== null && !prompt.free_text) {
      return 'An answer types text for a question that takes only its options.';
    }
    if (answer.selected.length === 0 && answer.text === null) {
      return 'An answer leaves a question unanswered.';
    }
    if (!prompt.multiple && answer.selected.length === 1 && answer.text !== null) {
      return 'An answer both chooses an option and types text for a question that takes one answer.';
    }
  }
  if (answered.size !== prompts.length) return 'Every question of the request must be answered.';
  return null;
}

type ResolvedExecution =
  | { found: true; facts: ExecutionFacts; runtime: RuntimeDescriptor; scope: CommandScope }
  | { found: false; rejection: Admission & { admitted: false } };

function resolveExecution(
  executionId: string,
  projection: Projection,
  runtimes: RuntimeCatalog,
): ResolvedExecution {
  const facts = projection.executionFacts(executionId);
  if (facts === undefined) {
    return {
      found: false,
      rejection: reject(
        NO_SCOPE,
        'execution_not_found',
        `Execution ${executionId} does not exist.`,
      ),
    };
  }
  const scope: CommandScope = {
    project_id: facts.projectId,
    workstream_id: facts.workstreamId,
    execution_id: facts.executionId,
  };
  const runtime = runtimes.get(facts.runtimeId);
  if (runtime === undefined) {
    return {
      found: false,
      rejection: reject(
        scope,
        'runtime_not_found',
        `Runtime ${facts.runtimeId} for this execution is not registered.`,
      ),
    };
  }
  return { found: true, facts, runtime, scope };
}

/** Whether any of an instruction's pictures comes from the project's folder; a given one needs none. */
function fromTheFolder(attachments: readonly { readonly kind: string }[] | undefined): boolean {
  return attachments?.some((attachment) => attachment.kind === 'image') === true;
}

const PICTURES_NEED_A_FOLDER =
  "Pictures come from the project's folder, and the project has none. Choose a folder for the project first.";

/**
 * An instruction's pictures (A4) go only to a runtime that declares it takes them; nothing is
 * emulated, so the words are never sent without them.
 */
function picturesRefusal(
  runtime: RuntimeDescriptor,
  attachments: readonly unknown[] | undefined,
  scope: CommandScope,
): Admission | undefined {
  if (attachments === undefined || runtime.takes_pictures === true) return undefined;
  return reject(
    scope,
    'capability_unsupported',
    `Runtime ${runtime.runtime_id} does not take pictures with an instruction. Send the words without them, or choose another runtime.`,
  );
}

function requireCapability(
  runtime: RuntimeDescriptor,
  capability: keyof RuntimeCapabilities,
  scope: CommandScope,
): (Admission & { admitted: false }) | undefined {
  if (runtime.capabilities[capability]) return undefined;
  return reject(
    scope,
    'capability_unsupported',
    `Runtime ${runtime.runtime_id} does not support ${capability}.`,
  );
}

function reject(
  scope: CommandScope,
  code: RejectionCode,
  message: string,
): Admission & { admitted: false } {
  return { admitted: false, rejection: { code, message }, scope };
}

/** An execution's status as a person reads it, as the headset's state words say it. */
function inWords(status: ExecutionStatus): string {
  switch (status) {
    case 'verifying':
      return 'checking its work';
    case 'waiting_for_human':
      return 'waiting for you';
    default:
      return status;
  }
}

/** What frees a task's folder, as a person can act on it: a starting task has no Stop yet. */
function untilFree(status: ExecutionStatus): string {
  return status === 'starting'
    ? 'Wait for it to start, then try again.'
    : 'Wait for it to finish or stop it, then try again.';
}

/** A command for a task as a person would say it, at the start of a refusal. */
function commandInWords(type: CommandType): string {
  switch (type) {
    case 'execution.start':
      return 'A start';
    case 'execution.send_instruction':
      return 'An instruction';
    case 'execution.respond_to_approval':
      return 'An answer to an approval';
    case 'execution.answer_question':
      return 'An answer to a question';
    case 'execution.interrupt':
      return 'A stop';
    case 'workstream.add_to_project':
      return 'Adding to the project';
    case 'workstream.fit_to_project':
      return 'Fitting to the project';
    case 'project.restore':
      return 'Putting files back';
    case 'project.check':
      return 'A check';
    case 'project.own_check.choose':
      return 'Choosing a check';
    default:
      return 'A command';
  }
}

/**
 * What keeps a task from setting an execution going now: one of its executions is active, or an
 * execution command for it has no outcome yet. `instructed` is the execution an instruction is for,
 * which is neither, or null for a start, which nothing of the task may be beside (ADMIT, ADMIT-2).
 */
function taskInUse(
  projection: Projection,
  workstreamId: WorkstreamId,
  instructed: ExecutionId | null,
):
  | { readonly kind: 'active'; readonly status: ExecutionStatus }
  | { readonly kind: 'pending'; readonly commandType: CommandType }
  | null {
  const active = projection
    .activeExecutions()
    .find((facts) => facts.workstreamId === workstreamId && facts.executionId !== instructed);
  if (active !== undefined) return { kind: 'active', status: active.status };
  const pending = projection
    .pendingCommands()
    .find(
      (command) =>
        command.workstream_id === workstreamId &&
        (command.command_type.startsWith('execution.') ||
          command.command_type.startsWith('workstream.')) &&
        (instructed === null || command.execution_id !== instructed),
    );
  if (pending !== undefined) return { kind: 'pending', commandType: pending.command_type };
  return null;
}

/**
 * Why setting an agent going in `folder` for a task must wait: another task's execution works in
 * that folder, or in one inside it or around it, and is active, or a start or an instruction for
 * another task there has no outcome yet. Two agents in one tree would write the same files and see
 * each other's unfinished changes (FOLDER, OPEN_QUESTIONS). Undefined when the folder is free.
 */
function folderRefusal(
  projection: Projection,
  folder: string,
  workstreamId: WorkstreamId,
  scope: CommandScope,
): (Admission & { admitted: false }) | undefined {
  const active = projection
    .executionsAtWork()
    .find(
      (facts) =>
        facts.workstreamId !== workstreamId &&
        facts.directory !== null &&
        foldersOverlap(facts.directory, folder),
    );
  if (active !== undefined) {
    return reject(
      scope,
      'invalid_state',
      active.status === 'unknown'
        ? 'Another task in this folder may still be running. Wait for it, then try again.'
        : `Another task is ${inWords(active.status)} in this folder. ${untilFree(active.status)}`,
    );
  }
  const pending = projection.pendingCommands().find((command) => {
    if (command.workstream_id === null || command.workstream_id === workstreamId) return false;
    const other = projection.workstream(command.workstream_id);
    // A start of a task the host has not refused a copy works in a copy it is making (ADR 0045);
    // should the copy fail, the host asks `folderInUse` again before it falls back to this folder.
    const where =
      command.command_type === 'execution.start'
        ? other === undefined || other.copy === undefined
          ? null
          : taskFolder(projection, other)
        : command.command_type === 'execution.send_instruction' && command.execution_id !== null
          ? (projection.execution(command.execution_id)?.directory ?? null)
          : null;
    return where !== null && foldersOverlap(where, folder);
  });
  if (pending !== undefined) {
    return reject(
      scope,
      'invalid_state',
      `${commandInWords(pending.command_type)} for another task in this folder is still under way. Wait for its outcome, then try again.`,
    );
  }
  // A project's own check runs in place (ADR 0055), so no turn starts in the folder it reads and writes while it
  // runs, whichever task's (AUDIT3 5).
  const checking = projection.pendingCommands().find((command) => {
    if (
      command.command_type !== 'project.check' &&
      command.command_type !== 'project.own_check.choose'
    )
      return false;
    const project =
      command.project_id === null ? undefined : projection.project(command.project_id);
    if (project === undefined || project.starter !== null || project.location === null)
      return false;
    const copy =
      command.workstream_id === null
        ? undefined
        : projection.workstream(command.workstream_id)?.copy;
    const where = copy?.state === 'own' ? copy.directory : project.location.path;
    return foldersOverlap(where, folder);
  });
  if (checking !== undefined)
    return reject(
      scope,
      'invalid_state',
      "This project's own check is running in this folder. Wait for its outcome, then try again.",
    );
  return undefined;
}

/**
 * Why a task may not work in `folder` now, as admission would refuse it (FOLDER), or null when the
 * folder is free: what the host asks before a start whose copy was refused works in the project's
 * own folder (ADR 0045).
 */
export function folderInUse(
  projection: Projection,
  folder: string,
  workstreamId: WorkstreamId,
): CommandRejection | null {
  const refusal = folderRefusal(projection, folder, workstreamId, NO_SCOPE);
  return refusal === undefined ? null : refusal.rejection;
}

/** Commands that read or write a project's own folder, which an add must not run beside. */
const PROJECT_FOLDER_COMMANDS: ReadonlySet<CommandType> = new Set<CommandType>([
  'workstream.add_to_project',
  'project.restore',
  'project.check',
  'project.own_check.choose',
  'project.set_location',
  'project.make_vr_version',
]);

/**
 * The folder a task's next start works in, as admission knows it: its own copy, else the project's
 * folder (ADR 0045), or null when the project has none.
 */
function taskFolder(projection: Projection, workstream: WorkstreamView): string | null {
  if (workstream.copy?.state === 'own') return workstream.copy.directory;
  return projection.project(workstream.project_id)?.location?.path ?? null;
}
