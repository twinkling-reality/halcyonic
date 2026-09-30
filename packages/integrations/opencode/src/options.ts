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
  modelRef: string | null = null,
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
  let chosen: StartOptions['model'] = null;
  if (model !== undefined && model !== null) {
    if (modelRef !== null) {
      return fail('Choose the model either with model_ref or with the "model" option, not both.');
    }
    chosen = toModel(model);
    if (chosen === null) {
      return fail('Option "model" must name a configured model as "provider/model".');
    }
  }
  if (modelRef !== null) {
    chosen = toModel(modelRef);
    if (chosen === null) return fail(`${modelRef} is not a model OpenCode lists.`);
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
  return { ok: true, value: { directory: decision.directory, model: chosen } };
}

/** A model as OpenCode names it, `provider/model`, which is also the adapter's `model_ref`. */
export function toModel(value: unknown): StartOptions['model'] {
  if (typeof value !== 'string' || !/^[^/\s]+\/\S+$/.test(value)) return null;
  const slash = value.indexOf('/');
  return { providerID: value.slice(0, slash), id: value.slice(slash + 1) };
}

function fail(message: string): ParsedStartOptions {
  return { ok: false, message };
}
