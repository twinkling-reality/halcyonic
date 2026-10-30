import { closeSync, constants, fstatSync, openSync, readSync } from 'node:fs';
import { join } from 'node:path';
import type {
  CatalogRepositoryRef,
  CatalogResponse,
  LocationsResponse,
} from '@halcyonic/contracts';
import {
  defaultPhatPorcupineHome,
  PhatPorcupineClient,
} from '@halcyonic/integration-phat-porcupine';
import { type FolderOrigin, matchFolders } from '../catalog-match.ts';
import type { CatalogLinks } from '../core/command-service.ts';
import type { CatalogSource } from '../http/routes.ts';

/** The file in the control plane's data directory that holds the Phat Porcupine consumer credential. */
export const PHAT_PORCUPINE_CREDENTIAL_FILE = 'phat-porcupine-credential';

type Unauthorized = Extract<CatalogResponse, { availability: 'unauthorized' }>;

export interface PhatPorcupineCatalogOptions {
  /** Phat Porcupine's home, where it publishes its discovery file. */
  readonly home: string;
  /** The file holding the consumer credential the person created for Halcyonic. */
  readonly credentialPath: string;
  /** Halcyonic's own data, which no folder match ever reads. */
  readonly protect: readonly string[];
  readonly timeoutMs?: number;
}

/**
 * The person's catalog from Phat Porcupine (ADR 0039), read through on request and never journaled.
 * The credential is read from its file on every request, used only when its owner alone can read it,
 * and never logged, journaled, sent to a client or put in an agent's environment. The last catalog
 * read is kept, only in memory, so a link can be checked against what the person was shown.
 */
export class PhatPorcupineCatalog implements CatalogSource, CatalogLinks {
  readonly #client: PhatPorcupineClient;
  readonly #protect: readonly string[];
  #last: Extract<CatalogResponse, { availability: 'available' }> | null = null;

  constructor(options: PhatPorcupineCatalogOptions) {
    this.#protect = options.protect;
    this.#client = new PhatPorcupineClient({
      home: options.home,
      credential: () => readCredential(options.credentialPath),
      ...(options.timeoutMs === undefined ? {} : { timeoutMs: options.timeoutMs }),
    });
  }

  async read(): Promise<CatalogResponse> {
    const answer = await this.#client.catalog();
    if (answer.availability === 'available') this.#last = answer;
    return answer;
  }

  match(listing: LocationsResponse): Promise<FolderOrigin[]> {
    return matchFolders(listing, { protect: this.#protect });
  }

  /**
   * A link names a repository the last catalog read held, under the same owner/name, and that catalog
   * was the person's own, not a sample.
   */
  check(
    ref: CatalogRepositoryRef,
  ): { readonly ok: true } | { readonly ok: false; readonly message: string } {
    const last = this.#last;
    if (last === null)
      return {
        ok: false,
        message: 'No catalog has been read yet. Open your projects, then link it.',
      };
    if (last.source.sample)
      return { ok: false, message: 'A sample catalog is never linked to your projects.' };
    const known = last.projects.some((project) =>
      project.repositories.some(
        (repository) =>
          repository.repository_id === ref.repository_id && repository.full_name === ref.full_name,
      ),
    );
    return known
      ? { ok: true }
      : {
          ok: false,
          message:
            "That repository isn't in the catalog this computer last read. Open your projects again, then link it.",
        };
  }
}

/** Phat Porcupine at its default home, with the credential kept in the control plane's data directory. */
export function phatPorcupineCatalogFor(dataDir: string): PhatPorcupineCatalog {
  return new PhatPorcupineCatalog({
    home: defaultPhatPorcupineHome(),
    credentialPath: join(dataDir, PHAT_PORCUPINE_CREDENTIAL_FILE),
    protect: [dataDir],
  });
}

/** The most a credential file may hold: a `ppc_` token is 47 bytes. */
const CREDENTIAL_MAX_BYTES = 1024;

/**
 * The credential, or why it isn't used. Its contents are never part of a message. The file is opened
 * without following a link and without waiting on a pipe, and checked through the handle: a regular
 * file of at most 1 KiB that only its owner can read.
 */
function readCredential(path: string): string | Unauthorized {
  let fd: number;
  try {
    fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT')
      return unauthorized(
        'credential_missing',
        `No Phat Porcupine credential is set up: save it as ${PHAT_PORCUPINE_CREDENTIAL_FILE} in Halcyonic's data directory with mode 600.`,
      );
    if (code === 'ELOOP')
      return unauthorized(
        'credential_not_a_file',
        `${PHAT_PORCUPINE_CREDENTIAL_FILE} is a link, so it is not used. Save the credential as a file.`,
      );
    return unauthorized(
      'credential_unreadable',
      `${PHAT_PORCUPINE_CREDENTIAL_FILE} could not be read.`,
    );
  }
  try {
    const file = fstatSync(fd);
    if (!file.isFile())
      return unauthorized(
        'credential_not_a_file',
        `${PHAT_PORCUPINE_CREDENTIAL_FILE} is not a file, so it is not used. Save the credential as a file.`,
      );
    if ((file.mode & 0o077) !== 0)
      return unauthorized(
        'credential_file_exposed',
        `${PHAT_PORCUPINE_CREDENTIAL_FILE} can be read by other users, so it is not used. Run chmod 600 on it.`,
      );
    const buffer = Buffer.alloc(CREDENTIAL_MAX_BYTES + 1);
    const read = readSync(fd, buffer, 0, buffer.length, 0);
    if (read > CREDENTIAL_MAX_BYTES)
      return unauthorized(
        'credential_unreadable',
        `${PHAT_PORCUPINE_CREDENTIAL_FILE} holds more than a credential, so it is not used.`,
      );
    return buffer.subarray(0, read).toString('utf8').trim();
  } catch {
    return unauthorized(
      'credential_unreadable',
      `${PHAT_PORCUPINE_CREDENTIAL_FILE} could not be read.`,
    );
  } finally {
    closeSync(fd);
  }
}

function unauthorized(code: string, message: string): Unauthorized {
  return { availability: 'unauthorized', reason: { code, message } };
}
