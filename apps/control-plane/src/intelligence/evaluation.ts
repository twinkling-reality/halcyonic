import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import type { EvaluationFailure, EvaluationResult, UsageLimitsResponse } from '@halcyonic/contracts';
import { SEORAK_DEFAULT_PORT, SeorakClient, seorakAgentFor } from '@halcyonic/integration-seorak';

/** The file in the control plane's data directory that holds the Seorak integration credential. */
export const SEORAK_CREDENTIAL_FILE = 'seorak-credential';

/**
 * Where the control plane reads what an evaluation provider measured about a runtime session. The
 * answer is read through on request and never journaled (ADR 0010).
 */
export interface EvaluationSource {
  evaluate(runtimeKind: string, nativeId: string): Promise<EvaluationResult>;
  usageLimits?(): Promise<UsageLimitsResponse>;
}

export interface SeorakEvaluationOptions {
  /** The file holding the integration credential the owner issued for Halcyonic. */
  readonly credentialPath: string;
  /** The port of Seorak's local plane on 127.0.0.1. Defaults to 4317. */
  readonly port?: number;
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
  return {
    async usageLimits() {
      const credential = readCredential(options.credentialPath, `http://127.0.0.1:${port}`);
      if (typeof credential !== 'string')
        return { availability: 'unauthorized', reason: credential.reason };
      return client.usageLimits({ credential });
    },
    async evaluate(runtimeKind, nativeId) {
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
  return seorakEvaluation({ credentialPath: join(dataDir, SEORAK_CREDENTIAL_FILE) });
}

function readCredential(path: string, origin: string): string | EvaluationFailure {
  let mode: number;
  try {
    mode = statSync(path).mode;
  } catch {
    return unauthorized(
      'credential_missing',
      `No Seorak credential is configured. Issue one in Seorak's dashboard (${origin}/dashboard) for the audience ${origin}/api/v1 with the sessions:read and replay:read scopes, and save it to ${path} with mode 600.`,
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
