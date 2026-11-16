import { constants, readFileSync, realpathSync } from 'node:fs';
import { type FileHandle, open } from 'node:fs/promises';
import { join } from 'node:path';
import {
  COMMAND_SCHEMA_VERSION,
  type CommandEnvelope,
  type CommandId,
  type CommandView,
  MEDIA_PHOTO_BYTES,
  MEDIA_VIDEO_BYTES,
  type Principal,
  type ProjectId,
  type ProjectMediaView,
  type ReceivedVia,
  type Timestamp,
} from '@halcyonic/contracts';
import { KEPT_FILES, type MediaStore } from '../core/media-store.ts';
import { readPictures } from '../core/pictures.ts';
import { NO_LINK } from '../observer/file-observer.ts';
import type { PageRoutes } from './pairing-page.ts';

/**
 * The Photos and Videos page (ADR 0066), on the pairing page's server beside setup and under its
 * paths, behind the same session and checks: each project with a folder, its photos and videos as
 * the journal has them, drawn by their stills, and Add Media. A file the person chooses or drops is
 * streamed to the host's store, then `project.media.add` is submitted as a local press; the page
 * reads how it went from the journal. Nothing of the file but its digest leaves the store.
 */

export interface MediaPageProject {
  readonly project_id: ProjectId;
  readonly name: string;
  readonly folder: string;
}

export interface MediaPageDeps {
  readonly store: MediaStore;
  readonly projects: () => readonly MediaPageProject[];
  readonly media: (projectId: ProjectId) => readonly ProjectMediaView[];
  readonly stills: (projectId: ProjectId, path: string) => readonly string[] | undefined;
  readonly command: (commandId: string) => CommandView | undefined;
  readonly pending: () => readonly CommandView[];
  readonly submit: (
    command: CommandEnvelope,
    via: ReceivedVia,
    principal: Principal | null,
  ) => {
    readonly disposition: string;
    readonly command: { readonly rejection?: { readonly message: string } | null } | null;
  };
  readonly protectedFolders: readonly string[];
  readonly nextId: () => string;
  readonly now: () => Date;
}

/** What the page shows of one project. */
export interface MediaPageProjectState {
  readonly project_id: ProjectId;
  readonly name: string;
  readonly adding: boolean;
  /** The words of the last add this session pressed here that was refused or failed, else null. */
  readonly said: string | null;
  readonly media: readonly {
    readonly path: string;
    readonly video: boolean;
    readonly still: string | null;
    readonly play: string | null;
    readonly seconds: number | null;
    readonly prepared: boolean;
  }[];
}

export const RECEIVED_WORDS: Readonly<Record<string, string>> = {
  busy: 'Another file is still arriving. Add this one once it has.',
  full: `${KEPT_FILES} files are waiting to be added already. Try again in a moment.`,
  too_large: `That file is larger than this computer takes: ${MEDIA_PHOTO_BYTES / 1_000_000} MB for a photo, ${MEDIA_VIDEO_BYTES / 1_000_000} MB for a video.`,
  not_a_media:
    "That file isn't a whole photo or video this computer takes: PNG, JPEG, WebP, HEIC, MOV or MP4.",
  failed: "That file couldn't be received. Try again.",
};

export class MediaPage {
  readonly #deps: MediaPageDeps;
  /** Each session's last add here, by project, so its refusal is shown to that session only. */
  readonly #pressed = new Map<string, Map<string, string>>();
  /** Words for a session's add that never became a command (the store refused the file). */
  readonly #refused = new Map<string, Map<string, string>>();

  constructor(deps: MediaPageDeps) {
    this.#deps = deps;
  }

  state(session: string): { readonly projects: readonly MediaPageProjectState[] } {
    const pending = this.#deps.pending();
    return {
      projects: this.#deps.projects().map((project) => {
        const commandId = this.#pressed.get(session)?.get(project.project_id) ?? null;
        const command = commandId === null ? undefined : this.#deps.command(commandId);
        const failed = command?.status === 'failed' ? (command.failure?.message ?? null) : null;
        const rejected =
          command?.status === 'rejected' ? (command.rejection?.message ?? null) : null;
        const said = this.#refused.get(session)?.get(project.project_id) ?? failed ?? rejected;
        return {
          project_id: project.project_id,
          name: project.name,
          adding: pending.some(
            (each) =>
              each.command_type === 'project.media.add' && each.project_id === project.project_id,
          ),
          said,
          media: this.#deps.media(project.project_id).map((medium) => ({
            path: medium.path,
            video: medium.media_type === 'video/mp4' || medium.media_type === 'video/quicktime',
            still: medium.still,
            play: medium.play,
            seconds:
              medium.duration_ms === null
                ? null
                : Math.max(1, Math.round(medium.duration_ms / 1000)),
            prepared: medium.prepared,
          })),
        };
      }),
    };
  }

  /** Receives the person's file for a project, then submits its add as their local press. */
  async add(
    projectId: unknown,
    body: AsyncIterable<Uint8Array>,
    declared: number,
    session: string,
  ): Promise<{ readonly said: string | null }> {
    const project = this.#deps.projects().find((each) => each.project_id === projectId);
    if (project === undefined) {
      // The body is never read for a project that is not here.
      return { said: "That project isn't on this computer, or has no folder." };
    }
    const refused = this.#refused.get(session) ?? new Map<string, string>();
    this.#refused.set(session, refused);
    refused.delete(project.project_id);
    const received = await this.#deps.store.receive(body, declared);
    if (!received.ok) {
      const words = RECEIVED_WORDS[received.reason] ?? RECEIVED_WORDS.failed ?? '';
      refused.set(project.project_id, words);
      return { said: words };
    }
    const commandId = this.#deps.nextId();
    const pressed = this.#pressed.get(session) ?? new Map<string, string>();
    this.#pressed.set(session, pressed);
    pressed.set(project.project_id, commandId);
    const sent = this.#deps.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId as CommandId,
        issued_at: this.#deps.now().toISOString() as Timestamp,
        client: { name: 'halcyonic-setup-page', version: null, device_label: null },
        command_type: 'project.media.add',
        payload: { project_id: project.project_id, media: received.id },
      } as CommandEnvelope,
      'http',
      { kind: 'local' },
    );
    if (sent.disposition !== 'accepted') {
      const words = sent.command?.rejection?.message ?? "That couldn't be done. Try again.";
      refused.set(project.project_id, words);
      await this.#deps.store.remove(received.id);
      return { said: words };
    }
    return { said: null };
  }

  /**
   * The play copy of one of the project's videos (ADR 0066), for the tile to play muted: only a path the journal
   * names as a play copy, opened once without a link anywhere in its path inside the project's real folder, a regular
   * file no larger than MEDIA_VIDEO_BYTES, streamed from that one handle.
   */
  async play(
    projectId: unknown,
    path: unknown,
    asked: unknown = undefined,
  ): Promise<{
    readonly type: string;
    readonly body: NodeJS.ReadableStream;
    readonly length: number;
    readonly range?: { readonly start: number; readonly end: number; readonly size: number };
  } | null> {
    const project = this.#deps.projects().find((each) => each.project_id === projectId);
    if (project === undefined || typeof path !== 'string') return null;
    const ours = this.#deps.media(project.project_id).some((medium) => medium.play === path);
    if (!ours) return null;
    let handle: FileHandle;
    try {
      handle = await open(join(realpathSync(project.folder), path), constants.O_RDONLY | NO_LINK);
    } catch {
      return null;
    }
    const info = await handle.stat().catch(() => null);
    if (info === null || !info.isFile() || info.size > MEDIA_VIDEO_BYTES) {
      await handle.close();
      return null;
    }
    // One part, as a browser asks for a video (Safari plays none without it); anything else is the whole file.
    const range = partOf(asked, info.size);
    if (range === null)
      return { type: 'video/mp4', body: handle.createReadStream(), length: info.size };
    return {
      type: 'video/mp4',
      body: handle.createReadStream({ start: range.start, end: range.end }),
      length: range.end - range.start + 1,
      range: { ...range, size: info.size },
    };
  }

  /** A still of one of the project's photos or videos, read as an instruction's picture is. */
  async still(
    projectId: unknown,
    path: unknown,
  ): Promise<{ readonly type: string; readonly body: Buffer } | null> {
    const project = this.#deps.projects().find((each) => each.project_id === projectId);
    if (project === undefined || typeof path !== 'string') return null;
    const ours = this.#deps
      .media(project.project_id)
      .some((medium) => (this.#deps.stills(project.project_id, medium.path) ?? []).includes(path));
    if (!ours) return null;
    const read = await readPictures(
      project.folder,
      [{ kind: 'image', path }],
      this.#deps.protectedFolders,
    ).catch(() => null);
    const picture = read?.ok === true ? read.pictures[0] : undefined;
    if (picture === undefined) return null;
    return { type: picture.media_type, body: Buffer.from(picture.bytes) };
  }
}

/** A single byte range of a file of `size`, from a Range header (`bytes=a-b`, `bytes=a-`, `bytes=-n`), or null. */
export function partOf(
  header: unknown,
  size: number,
): { readonly start: number; readonly end: number } | null {
  if (typeof header !== 'string' || size === 0) return null;
  const match = /^bytes=(\d*)-(\d*)$/.exec(header.trim());
  if (match === null || (match[1] === '' && match[2] === '')) return null;
  let start: number;
  let end: number;
  if (match[1] === '') {
    start = Math.max(0, size - Number(match[2]));
    end = size - 1;
  } else {
    start = Number(match[1]);
    end = match[2] === '' ? size - 1 : Math.min(Number(match[2]), size - 1);
  }
  if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start > end || start >= size)
    return null;
  return { start, end };
}

function asset(name: string): Buffer {
  return readFileSync(new URL(`./media-page/${name}`, import.meta.url));
}

/** The page's paths under `/setup`, each behind the page's session and checks. */
export function mountMedia(page: MediaPage): (routes: PageRoutes) => void {
  return (routes) => {
    routes.asset('/setup/media', 'text/html; charset=utf-8', asset('media.html'));
    routes.asset('/setup/media.js', 'text/javascript; charset=utf-8', asset('media.js'));
    routes.asset('/setup/media.css', 'text/css; charset=utf-8', asset('media.css'));
    routes.get('/setup/media/state', (_request, session) => page.state(session));
    routes.file('/setup/media/play', async (request) => {
      const query = request.query as { project?: unknown; path?: unknown };
      return page.play(query.project, query.path, request.headers.range);
    });
    routes.file('/setup/media/still', async (request) => {
      const query = request.query as { project?: unknown; path?: unknown };
      return page.still(query.project, query.path);
    });
    routes.upload(
      '/setup/media/add',
      MEDIA_VIDEO_BYTES,
      async (request, body, declared, session) => {
        const query = request.query as { project?: unknown };
        return page.add(query.project, body, declared, session);
      },
    );
  };
}
