import { readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { UnderstandingFailure, UnderstandingResult } from '@halcyonic/contracts';
import { OPENCODE_SANDBOX_FOLDER } from '@halcyonic/integration-opencode';
import {
  defaultSalidiumHome,
  readsOtherToolsSources,
  SalidiumClient,
} from '@halcyonic/integration-salidium';
import { CODEX_CONFINED_FOLDER, CODEX_HOME_FOLDER, inHalcyonicsCodexHome } from './codex-home.ts';
import { inHalcyonicsOpenCodeStore } from './opencode-store.ts';

/** The file in the control plane's data directory that holds the Salidium consumer credential. */
export const SALIDIUM_CREDENTIAL_FILE = 'salidium-credential';

/**
 * Where the control plane reads what an understanding provider concluded about a runtime session.
 * The answer is read through on request and never journaled (ADR 0010).
 */
export interface UnderstandingSource {
  /** `startedAt` is when the execution started, as journaled, or when it was created. */
  understand(
    runtimeKind: string,
    nativeId: string,
    startedAt: string,
  ): Promise<UnderstandingResult>;
}

export interface SalidiumUnderstandingOptions {
  /** Salidium's state directory, where it publishes its discovery file. */
  readonly home: string;
  /** The file holding the consumer credential the owner created for Halcyonic. */
  readonly credentialPath: string;
  /** Halcyonic's own Codex home, whose sessions Salidium does not read. */
  readonly codexHome: string;
  /** Folder holding confined Codex homes (`<data>/codex-confined/<key>`), defaulting next to `codexHome`. */
  readonly confinedCodexHomes?: string;
  /** The sandboxed OpenCode server's folder (`<data>/opencode-sandbox`), defaulting next to `codexHome`. */
  readonly openCodeSandbox?: string;
}

/**
 * Salidium as the understanding source. The credential file is read again on every read, so
 * creating, replacing or deleting it takes effect without a restart, and a file other users can
 * read is refused rather than used. For Claude Code and Codex it is read before Salidium is found,
 * so a missing credential is said first; for OpenCode, only once Salidium says it observes it.
 * Sessions Salidium does not observe need no credential to say so: among them the Codex threads
 * whose rollout is in Halcyonic's own Codex home or a confined Codex home (`codex-home.ts`), and the
 * OpenCode sessions in the sandboxed server's own store (`opencode-store.ts`), none of which
 * Salidium reads, and the credential is sent only to the instance that wrote the discovery file.
 */
export function salidiumUnderstanding(options: SalidiumUnderstandingOptions): UnderstandingSource {
  const confinedHomes =
    options.confinedCodexHomes ?? join(dirname(options.codexHome), CODEX_CONFINED_FOLDER);
  const openCodeSandbox =
    options.openCodeSandbox ?? join(dirname(options.codexHome), OPENCODE_SANDBOX_FOLDER);
  return {
    async understand(runtimeKind, nativeId, startedAt) {
      const client = new SalidiumClient({
        home: options.home,
        credential: () => readCredential(options.credentialPath),
      });
      const own = (await inHalcyonicsCodexHome(
        options.codexHome,
        runtimeKind,
        nativeId,
        startedAt,
        confinedHomes,
      ))
        ? 'Codex home'
        : (await inHalcyonicsOpenCodeStore(openCodeSandbox, runtimeKind, startedAt))
          ? 'OpenCode store'
          : null;
      if (own !== null) {
        // Only a Salidium that can read another tool's folders is asked about Halcyonic's own; one
        // that cannot answers what it always did, without a credential read (unit 4b).
        const version = await client.version();
        if (version === null || !readsOtherToolsSources(version)) {
          return {
            availability: 'unavailable',
            reason: {
              code: 'runtime_not_observed',
              message: `Salidium, as installed, does not read Halcyonic's own ${own}, where this session is kept.`,
            },
          };
        }
        const result = await client.understand(runtimeKind, nativeId);
        // A Salidium that could read them but has not seen the session has not been told where
        // they are yet (SALIDIUM_EXTRA_CODEX_HOMES, SALIDIUM_EXTRA_OPENCODE_STORES), or has not
        // seen it yet: never a fault.
        if (result.availability === 'not_found' && result.reason.code === 'not_observed')
          return {
            availability: 'not_found',
            reason: {
              code: 'own_sources_not_set_up',
              message: `Salidium ${version} can read Halcyonic's own ${own}, but has not seen this session: it may not have been told where Halcyonic keeps them yet (Setup shows the two lines), or not seen it yet.`,
            },
          };
        return result;
      }
      return client.understand(runtimeKind, nativeId);
    },
  };
}

/** Salidium at its default location, with the credential kept in the control plane's data directory. */
export function salidiumUnderstandingFor(dataDir: string): UnderstandingSource {
  return salidiumUnderstanding({
    home: defaultSalidiumHome(),
    credentialPath: join(dataDir, SALIDIUM_CREDENTIAL_FILE),
    codexHome: join(dataDir, CODEX_HOME_FOLDER),
    confinedCodexHomes: join(dataDir, CODEX_CONFINED_FOLDER),
    openCodeSandbox: join(dataDir, OPENCODE_SANDBOX_FOLDER),
  });
}

function readCredential(path: string): string | UnderstandingFailure {
  let mode: number;
  try {
    mode = statSync(path).mode;
  } catch {
    return unauthorized(
      'credential_missing',
      `No Salidium credential is configured. Create one with \`salidium consumer create halcyonic\` and save it to ${path} with mode 600.`,
    );
  }
  if ((mode & 0o077) !== 0) {
    return unauthorized(
      'credential_file_exposed',
      `${path} can be read by other users, so it is not used. Run chmod 600 on it.`,
    );
  }
  try {
    return readFileSync(path, 'utf8').trim();
  } catch {
    return unauthorized('credential_unreadable', `${path} could not be read.`);
  }
}

function unauthorized(code: string, message: string): UnderstandingFailure {
  return { availability: 'unauthorized', reason: { code, message } };
}
