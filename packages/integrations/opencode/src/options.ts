import { statSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import type { RuntimeOptions } from '@halcyonic/contracts';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';

export interface StartOptions {
  /** The project directory the OpenCode session works in: the real path the policy returned. */
  readonly directory: string;
  /** OpenCode's `Model.Ref`; null keeps the model OpenCode is configured with. */
  readonly model: { readonly providerID: string; readonly id: string } | null;
}

export type ParsedStartOptions =
  | { readonly ok: true; readonly value: StartOptions }
  | { readonly ok: false; readonly message: string };

const SUPPORTED = ['directory', 'model'];

/**
 * Checks the start options of an OpenCode execution, then asks the host's directory policy.
 * OpenCode 2.0.18 creates a session in a directory that does not exist without complaint (and then
 * answers 500 on some routes), so the directory is checked here as well.
 */
export function parseStartOptions(
  options: RuntimeOptions,
  policy: DirectoryPolicy,
): ParsedStartOptions {
  const unknown = Object.keys(options).filter((key) => !SUPPORTED.includes(key));
  if (unknown.length > 0) {
    return fail(
      `Unknown OpenCode runtime options: ${unknown.join(', ')}. Supported: "directory", "model".`,
    );
  }
  const { directory, model } = options;
  if (typeof directory !== 'string' || directory === '') {
    return fail('Option "directory" is required: the absolute path of an existing directory.');
  }
  if (!isAbsolute(directory)) {
    return fail(`Option "directory" must be an absolute path, got "${directory}".`);
  }
  let isDirectory: boolean;
  try {
    isDirectory = statSync(directory).isDirectory();
  } catch {
    return fail(`Option "directory" does not exist: ${directory}`);
  }
  if (!isDirectory) return fail(`Option "directory" is not a directory: ${directory}`);
  let modelRef: StartOptions['model'] = null;
  if (model !== undefined && model !== null) {
    if (typeof model !== 'string' || !/^[^/\s]+\/\S+$/.test(model)) {
      return fail('Option "model" must name a configured model as "provider/model".');
    }
    const slash = model.indexOf('/');
    modelRef = { providerID: model.slice(0, slash), id: model.slice(slash + 1) };
  }
  let decision: ReturnType<DirectoryPolicy>;
  try {
    decision = policy(directory);
  } catch (error) {
    return fail(
      `The directory policy could not decide on ${directory}: ${error instanceof Error ? error.message : String(error)}`,
    );
  }
  if (!decision.ok) return fail(decision.message);
  return { ok: true, value: { directory: decision.directory, model: modelRef } };
}

function fail(message: string): ParsedStartOptions {
  return { ok: false, message };
}
