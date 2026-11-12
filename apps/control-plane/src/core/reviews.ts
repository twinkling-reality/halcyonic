import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type {
  ActionReview,
  ActionReviewItem,
  ActionReviewWarning,
  ChangedFiles,
  CheckpointId,
  CommandEnvelope,
  PathList,
  ProjectId,
  PublishReviewRequest,
  PublishReviewResponse,
  ReviewKind,
  SendReviewRequest,
  SendReviewResponse,
} from '@halcyonic/contracts';
import { COMMAND_FIELDS } from '@halcyonic/domain';
import type { HostedProvider } from '@halcyonic/runtime-core';
import { declarationProblem } from '../providers.ts';
import { sendTitle } from '../sending/github.ts';
import type { ControlPlane } from './control-plane.ts';
import { readRules, rulesReview } from './project-rules.ts';
import { locationReview, reviewDigest } from './review.ts';

/**
 * Send's review (ADR 0039, amended): what a send would do with the values the press will send, read
 * before it: the title as it goes (the person's, else the host's own), who can see a repository a
 * first send would make, and its name. The same read the button's route answers and the one review
 * card is built from.
 */
export async function sendReviewOf(
  controlPlane: ControlPlane,
  projectId: ProjectId,
  values: SendReviewRequest = {},
): Promise<SendReviewResponse> {
  const unavailable = (code: string, message: string): SendReviewResponse => ({
    availability: 'unavailable',
    reason: { code, message },
  });
  const folder = controlPlane.projection.project(projectId)?.location?.path;
  if (folder === undefined)
    return unavailable('location_required', 'The project has no folder on this computer.');
  const github = controlPlane.github;
  if (github === null)
    return unavailable('unavailable', "Sending to GitHub isn't set up on this computer.");
  const others = controlPlane.projection
    .projects()
    .filter((project) => project.project_id !== projectId)
    .flatMap((project) => (project.location === null ? [] : [project.location.path]));
  try {
    const title = sendTitle(
      values.title,
      controlPlane.projection.project(projectId),
      controlPlane.projection.workstreams(projectId),
    );
    const reviewed = await github.review(projectId, folder, others, title, {
      visibility: values.visibility ?? 'private',
      name: values.repository_name,
      made: controlPlane.projection.madeRepository(projectId),
    });
    return reviewed.ok
      ? { availability: 'available', send: reviewed.send }
      : unavailable(reviewed.code, reviewed.message);
  } catch {
    return unavailable('send_failed', "The project's files could not be read.");
  }
}

/** Publish's review (ADR 0039, amended), as its route answers it, with whether the press makes it public. */
export async function publishReviewOf(
  controlPlane: ControlPlane,
  projectId: ProjectId,
  values: PublishReviewRequest = {},
): Promise<PublishReviewResponse> {
  const unavailable = (code: string, message: string): PublishReviewResponse => ({
    availability: 'unavailable',
    reason: { code, message },
  });
  const project = controlPlane.projection.project(projectId);
  const folder = project?.location?.path;
  if (project === undefined || folder === undefined)
    return unavailable('location_required', 'The project has no folder on this computer.');
  const github = controlPlane.github;
  if (github === null)
    return unavailable('unavailable', "Publishing to GitHub isn't set up on this computer.");
  const check = controlPlane.projection.latestCheck(projectId);
  if (check === undefined || !check.findings.passed || check.files !== 'current')
    return unavailable(
      'invalid_state',
      'Only a passed check can be published. Check the game first.',
    );
  if (!check.findings.reads_confined)
    return unavailable(
      'invalid_state',
      'Not every agent run in this project was confined, so it is not published.',
    );
  try {
    const reviewed = await github.publishReview(
      projectId,
      folder,
      check.check_id,
      controlPlane.previews?.builtCopy(projectId, check.check_id) ?? null,
      project.published?.commit ?? null,
      {
        made: controlPlane.projection.madeRepository(projectId),
        makePublic: values.make_public === true,
      },
    );
    return reviewed.ok
      ? { availability: 'available', publish: reviewed.publish }
      : unavailable(reviewed.code, reviewed.message);
  } catch {
    return unavailable('publish_failed', "The checked game's built files could not be read.");
  }
}

export type BuiltReview =
  | { readonly ok: true; readonly review: ActionReview }
  | { readonly ok: false; readonly code: string; readonly message: string };

const CHANGE_KIND: Record<'added' | 'changed' | 'removed', ReviewKind> = {
  added: 'file_added',
  changed: 'file_changed',
  removed: 'file_removed',
};

const files = (changed: ChangedFiles): { items: ActionReviewItem[]; total: number } => ({
  items: changed.files.slice(0, 200).map((file) => ({
    kind: CHANGE_KIND[file.change],
    name: file.path,
    note: null,
    theirs: true,
  })),
  total: changed.total,
});

/** A path that is a hosted provider's declaration, as the host reads them (ADR 0061). */
const DECLARATION = /^(?:.*\/)?providers\/([a-z][a-z0-9_-]{0,31})\/provider\.json$/;

/** A declaration's contents at `path`, or null where there is none or it is not one the host would read. */
function declarationAt(path: string, id: string): HostedProvider | null {
  try {
    const raw: unknown = JSON.parse(readFileSync(path, 'utf8'));
    return declarationProblem(id, raw) === null ? (raw as HostedProvider) : null;
  } catch {
    return null;
  }
}

const hostOf = (upstream: string): string => {
  try {
    return new URL(upstream).host;
  } catch {
    return upstream;
  }
};

/**
 * What adding a task's files would do to where a stored key goes (AUDIT2 N1): each changed file that is a
 * provider's declaration says so in plain words, above all a new address or a key kept or sent another way.
 * A host that reads the project's folder as its own declarations sends keys by it at its next start.
 */
export function providerWarnings(
  changed: ChangedFiles,
  copy: string,
  project: string | null,
): ActionReviewWarning[] {
  const warnings: ActionReviewWarning[] = [];
  for (const file of changed.files) {
    const id = DECLARATION.exec(file.path)?.[1];
    if (id === undefined) continue;
    const after = file.change === 'removed' ? null : declarationAt(join(copy, file.path), id);
    const before = project === null ? null : declarationAt(join(project, file.path), id);
    if (after === null && before === null) continue;
    const line = (words: string) => ({
      code: 'provider_declaration',
      line: `${words} (${file.path})`.slice(0, 200),
    });
    if (before === null && after !== null)
      warnings.push(line(`Adds where a key goes: ${after.name} at ${hostOf(after.upstream)}`));
    else if (after === null && before !== null)
      warnings.push(line(`Removes the ${before.name} provider`));
    else if (before !== null && after !== null) {
      if (after.upstream !== before.upstream)
        warnings.push(
          line(
            `Sends the ${after.name} key to ${hostOf(after.upstream)}, not ${hostOf(before.upstream)}`,
          ),
        );
      else if (JSON.stringify(after.key) !== JSON.stringify(before.key))
        warnings.push(line(`Changes how the ${after.name} key is kept or sent`));
      else warnings.push(line(`Changes the ${after.name} provider`));
    }
  }
  if (changed.total > changed.files.length)
    warnings.push({
      code: 'provider_declaration_unread',
      line: 'More files changed than were listed, so a provider among them is not looked for',
    });
  return warnings;
}

const paths = (list: PathList, kind: ReviewKind): ActionReviewItem[] =>
  list.paths.map((path) => ({ kind, name: path, note: null, theirs: true }));

/** The words given for a command's text needs, by their labels, exactly as the payload holds them. */
export function saidIn(command: CommandEnvelope): ActionReview['said'] {
  const fields = COMMAND_FIELDS[command.command_type as keyof typeof COMMAND_FIELDS];
  if (fields === undefined) return [];
  const payload = command.payload as Record<string, unknown>;
  return Object.entries(fields).flatMap(([name, field]) =>
    field.from === 'person' && field.need === 'text' && typeof payload[name] === 'string'
      ? [{ label: field.label, words: payload[name] as string }]
      : [],
  );
}

/** "1 File", "3 Files": the host's count for a button's words. */
const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * The one review of a prepared command (ADR 0060; ADR 0051 made general): one reader per command
 * type for its facts, every word the host's, and the digest the command carries back, or the host
 * holds and checks again at the press, computed exactly as the button's own review computes it.
 * A command whose confirm is not a review has none.
 */
export async function buildReview(
  controlPlane: ControlPlane,
  command: CommandEnvelope,
): Promise<BuiltReview> {
  const no = (code: string, message: string): BuiltReview => ({ ok: false, code, message });
  const review = (body: Omit<ActionReview, 'command_type' | 'said'>): BuiltReview => ({
    ok: true,
    review: { command_type: command.command_type, ...body, said: saidIn(command) },
  });
  const { projection } = controlPlane;
  switch (command.command_type) {
    case 'project.send': {
      const { title, visibility, repository_name } = command.payload;
      const read = await sendReviewOf(controlPlane, command.payload.project_id, {
        ...(title === undefined ? {} : { title }),
        ...(visibility === undefined ? {} : { visibility }),
        ...(repository_name === undefined ? {} : { repository_name }),
      });
      if (read.availability !== 'available') return no(read.reason.code, read.reason.message);
      const { send } = read;
      const listed = files(send.files);
      // A first send makes the person's repository: its name and who can see it are what the press does.
      if (send.creates !== null)
        return review({
          digest: send.review,
          confirm_words: `Send ${count(send.files.total, 'File', 'Files')}`,
          what_happens:
            send.creates.visibility === 'public'
              ? 'A new public repository of yours holds the project, and the folder becomes its clone'
              : 'A new private repository of yours holds the project, and the folder becomes its clone',
          facts: [
            { kind: 'repository', value: send.creates.name, theirs: true },
            { kind: 'branch', value: send.new_branch, theirs: false },
            { kind: 'words', value: send.title, theirs: true },
          ],
          items: listed.items,
          items_total: listed.total,
          warnings:
            send.creates.visibility === 'public'
              ? [{ code: 'public', line: 'Anyone can see its code and history' }]
              : [],
        });
      return review({
        digest: send.review,
        confirm_words: `Send ${count(send.files.total, 'File', 'Files')}`,
        what_happens: "The project's work goes to its repository as a new branch",
        facts: [
          ...(send.remote === null
            ? []
            : [
                {
                  kind: 'repository' as const,
                  value: `${send.remote.host}/${send.remote.path}`,
                  theirs: true,
                },
              ]),
          { kind: 'branch', value: send.new_branch, theirs: false },
          { kind: 'branch', value: send.branch, theirs: true },
          { kind: 'words', value: send.title, theirs: true },
        ],
        items: listed.items,
        items_total: listed.total,
        warnings: send.review_request
          ? []
          : [{ code: 'no_review_request', line: 'No review request is opened, only the branch' }],
      });
    }
    case 'project.publish': {
      const read = await publishReviewOf(controlPlane, command.payload.project_id, {
        ...(command.payload.make_public === undefined
          ? {}
          : { make_public: command.payload.make_public }),
      });
      if (read.availability !== 'available') return no(read.reason.code, read.reason.message);
      const { publish } = read;
      return review({
        digest: publish.review,
        confirm_words: 'Publish',
        what_happens: 'The checked game goes public at its own address',
        facts: [
          { kind: 'repository', value: publish.repository, theirs: true },
          { kind: 'address', value: publish.address, theirs: true },
        ],
        items: [],
        items_total: publish.files,
        warnings: [
          { code: 'public', line: 'Anyone with its address can play it' },
          ...(publish.make_public
            ? [
                {
                  code: 'made_public',
                  line: 'The repository becomes public first: anyone can see its code and history',
                },
              ]
            : publish.visibility === 'private'
              ? [
                  {
                    code: 'private_repository',
                    line: 'GitHub Free serves no page from a private repository',
                  },
                ]
              : []),
          ...(publish.replaces === null
            ? []
            : [{ code: 'replaces', line: 'It replaces the game published before' }]),
        ],
      });
    }
    case 'project.set_location': {
      const project = projection.project(command.payload.project_id);
      if (project === undefined) return no('project_not_found', 'The project is not there.');
      const next = command.payload.location;
      return review({
        digest: locationReview(project.project_id, next, project.location),
        confirm_words: 'Change Folder',
        what_happens: "The project's later work runs in the folder chosen",
        facts: [
          ...(project.location === null
            ? []
            : [{ kind: 'folder' as const, value: project.location.path, theirs: true }]),
          {
            kind: 'folder',
            value:
              'folder_name' in next && next.folder_name !== null
                ? `${next.root}/${next.folder_name}`
                : next.root,
            theirs: true,
          },
        ],
        items: [],
        items_total: 0,
        warnings: [],
      });
    }
    case 'project.rules.set': {
      const project = projection.project(command.payload.project_id);
      const folder = project?.location?.path ?? null;
      if (project === undefined || folder === null)
        return no('location_required', 'The project has no folder on this computer.');
      if (!command.payload.on)
        return review({
          digest: reviewDigest(
            'project.rules.set',
            { project_id: project.project_id, on: false },
            {},
          ),
          confirm_words: 'Stop Rules',
          what_happens: "The project's rules no longer go to its tasks' agents",
          facts: [],
          items: [],
          items_total: 0,
          warnings: [],
        });
      const read = readRules(folder);
      if (read.state !== 'ok') return no('invalid_state', 'The project has no rules file to use.');
      return review({
        digest: rulesReview(project.project_id, read.file, read.sha256),
        confirm_words: 'Use Rules',
        what_happens: "These words go before each task's first instruction, as the project's own",
        facts: [{ kind: 'words', value: read.file, theirs: false }],
        items: [{ kind: 'words', name: read.text.slice(0, 400), note: null, theirs: true }],
        items_total: 1,
        warnings: [],
      });
    }
    case 'project.restore': {
      const checkpoints = controlPlane.checkpoints;
      if (checkpoints === null)
        return no('undo_not_set_up', 'Undo is not set up on this computer.');
      const previewed = await checkpoints.preview(
        command.payload.project_id,
        command.payload.checkpoint_id as CheckpointId,
      );
      if (!previewed.ok) return no(previewed.code, previewed.message);
      const { preview } = previewed;
      const items = [
        ...paths(preview.changed_back, 'file_changed'),
        ...paths(preview.removed, 'file_removed'),
        ...paths(preview.returned, 'file_added'),
      ].slice(0, 200);
      const total = preview.changed_back.total + preview.removed.total + preview.returned.total;
      return review({
        digest: previewed.review,
        confirm_words: 'Restore',
        what_happens: "Every file of the project's folder goes back as this version holds it",
        facts: [],
        items,
        items_total: total,
        warnings: [
          {
            code: 'kept_first',
            line: 'The folder as it is now is kept first, so this can be undone',
          },
        ],
      });
    }
    case 'workstream.add_to_project': {
      const copy = projection.workstream(command.payload.workstream_id)?.copy;
      if (copy?.state !== 'own' || copy.to_add_review === null || copy.to_add_files === null)
        return no('review_required', "Read the task's changes first.");
      const listed = files(copy.to_add_files);
      return review({
        digest: copy.to_add_review,
        confirm_words: `Add ${count(copy.to_add_files.total, 'File', 'Files')}`,
        what_happens: "The task's changes go into the project's folder",
        facts: [],
        items: listed.items,
        items_total: listed.total,
        warnings: providerWarnings(
          copy.to_add_files,
          copy.directory,
          projection.project(projection.workstream(command.payload.workstream_id)?.project_id ?? '')
            ?.location?.path ?? null,
        ),
      });
    }
    case 'project.share.start': {
      const check = projection.latestCheck(command.payload.project_id);
      if (check === undefined) return no('invalid_state', 'Only a passed check can be shared.');
      const link = command.payload.way === 'link';
      return review({
        digest: reviewDigest(
          'project.share.start',
          { project_id: command.payload.project_id, way: command.payload.way },
          { check_id: check.check_id },
        ),
        confirm_words: 'Share',
        what_happens: link
          ? 'The checked game opens at an address anyone with it can reach'
          : 'The checked game opens for devices on this Wi-Fi',
        facts: [],
        items: [],
        items_total: 0,
        warnings: link ? [{ code: 'public', line: 'Anyone with the link can play it' }] : [],
      });
    }
    case 'project.secret.remove': {
      // Gone for good, and the person may not have the value anywhere else (the lead, 05:49Z).
      const held = projection.project(command.payload.project_id)?.secrets ?? [];
      if (!held.includes(command.payload.name))
        return no('invalid_state', `This project has no secret named ${command.payload.name}.`);
      return review({
        digest: reviewDigest(
          'project.secret.remove',
          { project_id: command.payload.project_id, name: command.payload.name },
          { held: true },
        ),
        confirm_words: 'Remove Secret',
        what_happens: "The secret's value is deleted, and later tasks no longer get it",
        facts: [{ kind: 'words', value: command.payload.name, theirs: true }],
        items: [],
        items_total: 0,
        warnings: [{ code: 'cannot_undo', line: 'Its value cannot be brought back' }],
      });
    }
    case 'project.own_check.choose':
      return review({
        digest: reviewDigest(
          'project.own_check.choose',
          { project_id: command.payload.project_id },
          { scripts: command.payload.scripts },
        ),
        confirm_words: 'Choose Check',
        what_happens: "These commands of the project's own become its check, run confined",
        facts: [],
        items: command.payload.scripts.map((script) => ({
          kind: 'script' as const,
          name: script.name,
          note: null,
          theirs: true,
        })),
        items_total: command.payload.scripts.length,
        warnings: [],
      });
    case 'assistant.model.choose': {
      // Where the assistant will run, read before the one press (the lead's D1, 2026-11-12): a hosted model is paid
      // with the person's key, and what each request reads goes to its provider.
      const chosen = command.payload.model;
      return review({
        digest: reviewDigest('assistant.model.choose', {}, { model: chosen }),
        confirm_words: chosen === null ? "Use This Computer's Model" : 'Use This Model',
        what_happens:
          chosen === null
            ? "The assistant runs on this computer's model, and nothing it reads leaves this computer"
            : 'The assistant runs on this hosted model, paid with your key, and what each request reads goes to it',
        facts:
          chosen === null
            ? []
            : [
                { kind: 'words', value: chosen.model, theirs: true },
                { kind: 'address', value: chosen.provider_id, theirs: true },
              ],
        items: [],
        items_total: 0,
        warnings:
          chosen === null ? [] : [{ code: 'paid', line: 'Each request is paid with your key' }],
      });
    }
    default:
      return no('invalid_state', 'This command has no review: one press does it.');
  }
}
