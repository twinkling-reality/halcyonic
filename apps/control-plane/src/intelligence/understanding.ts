import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import type { UnderstandingFailure, UnderstandingResult } from '@halcyonic/contracts';
import {
  defaultSalidiumHome,
  SalidiumClient,
  salidiumProviderFor,
} from '@halcyonic/integration-salidium';

/** The file in the control plane's data directory that holds the Salidium consumer credential. */
export const SALIDIUM_CREDENTIAL_FILE = 'salidium-credential';

/**
 * Where the control plane reads what an understanding provider concluded about a runtime session.
 * The answer is read through on request and never journaled (ADR 0010).
 */
export interface UnderstandingSource {
  understand(runtimeKind: string, nativeId: string): Promise<UnderstandingResult>;
}

export interface SalidiumUnderstandingOptions {
  /** Salidium's state directory, where it publishes its discovery file. */
  readonly home: string;
  /** The file holding the consumer credential the owner created for Halcyonic. */
  readonly credentialPath: string;
}

/**
 * Salidium as the understanding source. The credential file is read on every request, so creating,
 * replacing or deleting it takes effect without a restart, and a file other users can read is
 * refused rather than used.
 */
export function salidiumUnderstanding(options: SalidiumUnderstandingOptions): UnderstandingSource {
  return {
    async understand(runtimeKind, nativeId) {
      // Sessions Salidium never observes need no credential to say so.
      if (salidiumProviderFor(runtimeKind) === null) {
        return new SalidiumClient({ home: options.home, credential: null }).understand(
          runtimeKind,
          nativeId,
        );
      }
      const credential = readCredential(options.credentialPath);
      if (typeof credential !== 'string') return credential;
      return new SalidiumClient({ home: options.home, credential }).understand(
        runtimeKind,
        nativeId,
      );
    },
  };
}

/** Salidium at its default location, with the credential kept in the control plane's data directory. */
export function salidiumUnderstandingFor(dataDir: string): UnderstandingSource {
  return salidiumUnderstanding({
    home: defaultSalidiumHome(),
    credentialPath: join(dataDir, SALIDIUM_CREDENTIAL_FILE),
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
