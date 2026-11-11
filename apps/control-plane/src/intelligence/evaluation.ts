import { readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type {
  EvaluationFailure,
  EvaluationResult,
  UsageLimitsResponse,
} from '@halcyonic/contracts';
import { SEORAK_DEFAULT_PORT, SeorakClient, seorakAgentFor } from '@halcyonic/integration-seorak';
import { CODEX_CONFINED_FOLDER, CODEX_HOME_FOLDER, inHalcyonicsCodexHome } from './codex-home.ts';

/** The file in the control plane's data directory that holds the Seorak integration credential. */
export const SEORAK_CREDENTIAL_FILE = 'seorak-credential';

/**
 * Where the control plane reads what an evaluation provider measured about a runtime session. The
 * answer is read through on request and never journaled (ADR 0010).
 */
export interface EvaluationSource {
  /** `startedAt` is when the execution started, as journaled, or when it was created. */
  evaluate(runtimeKind: string, nativeId: string, startedAt: string): Promise<EvaluationResult>;
  usageLimits?(): Promise<UsageLimitsResponse>;
}

export interface SeorakEvaluationOptions {
  /** The file holding the integration credential the owner issued for Halcyonic. */
  readonly credentialPath: string;
  /** The port of Seorak's local plane on 127.0.0.1. Defaults to 4317. */
  readonly port?: number;
  /** Halcyonic's own Codex home, whose sessions only a Seorak told about it reads (unit 4b). */
  readonly codexHome: string;
  /** Folder holding confined Codex homes (`<data>/codex-confined/<key>`), defaulting next to `codexHome`. */
  readonly confinedCodexHomes?: string;
}

/**
 * Seorak as the evaluation source. One client serves every request, so the credential's request
 * budget (60 a minute, three per evaluation) holds across them. The credential file is read on
 * every request, so issuing, replacing or deleting it takes effect without a restart, and a file
 * other users can read is refused rather than used.
 */
export function seorakEvaluation(options: SeorakEvaluationOptions): EvaluationSource {
  const port = options.port ?? SEORAK_DEFAULT_PORT;
  const client = new SeorakClient({ port });
  const confinedHomes =
    options.confinedCodexHomes ?? join(dirname(options.codexHome), CODEX_CONFINED_FOLDER);
  return {
    async usageLimits() {
      const credential = readCredential(options.credentialPath, `http://127.0.0.1:${port}`);
      if (typeof credential !== 'string')
        return { availability: 'unauthorized', reason: credential.reason };
      return client.usageLimits({ credential });
    },
    async evaluate(runtimeKind, nativeId, startedAt) {
      if (
        await inHalcyonicsCodexHome(
          options.codexHome,
          runtimeKind,
          nativeId,
          startedAt,
          confinedHomes,
        )
      ) {
        // Seorak names no version a client can read, so a session in Halcyonic's own Codex home is
        // asked about; a Seorak that has not captured it may not have been told where Halcyonic keeps
        // them yet (SEORAK_CODEX_EXTRA_DIRS, a Seorak after e3aa039e), or not captured it yet: never
        // a fault (unit 4b).
        const credential = readCredential(options.credentialPath, `http://127.0.0.1:${port}`);
        if (typeof credential !== 'string') return credential;
        const result = await client.evaluate(runtimeKind, nativeId, { credential });
        if (result.availability === 'not_found' && result.reason.code === 'not_captured')
          return {
            availability: 'not_found',
            reason: {
              code: 'own_sources_not_set_up',
              message:
                "Seorak has not captured this session, which is in Halcyonic's own Codex home: it may not have been told where Halcyonic keeps them yet (Setup shows the line), or not captured it yet.",
            },
          };
        return result;
      }
      // Sessions Seorak never observes need no credential to say so.
      if (seorakAgentFor(runtimeKind) === null)
        return client.evaluate(runtimeKind, nativeId, { credential: null });
      const credential = readCredential(options.credentialPath, `http://127.0.0.1:${port}`);
      if (typeof credential !== 'string') return credential;
      return client.evaluate(runtimeKind, nativeId, { credential });
    },
  };
}

/** Seorak's local plane on its default port, with the credential in the control plane's data directory. */
export function seorakEvaluationFor(dataDir: string): EvaluationSource {
  return seorakEvaluation({
    credentialPath: join(dataDir, SEORAK_CREDENTIAL_FILE),
    codexHome: join(dataDir, CODEX_HOME_FOLDER),
    confinedCodexHomes: join(dataDir, CODEX_CONFINED_FOLDER),
  });
}

function readCredential(path: string, origin: string): string | EvaluationFailure {
  let mode: number;
  try {
    mode = statSync(path).mode;
  } catch {
    return unauthorized(
      'credential_missing',
      `No Seorak credential is configured. Issue one in Seorak's dashboard (${origin}/dashboard) for the audience ${origin}/api/v1 with the sessions:read, replay:read and limits:read scopes, and save it to ${path} with mode 600.`,
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

function unauthorized(code: string, message: string): EvaluationFailure {
  return { availability: 'unauthorized', reason: { code, message } };
}
