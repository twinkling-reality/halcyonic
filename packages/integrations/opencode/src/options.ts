import type { RuntimeOptions } from '@halcyonic/contracts';

export interface StartOptions {
  /** OpenCode's `Model.Ref`; null keeps the model OpenCode is configured with. */
  readonly model: { readonly providerID: string; readonly id: string } | null;
}

export type ParsedStartOptions =
  | { readonly ok: true; readonly value: StartOptions }
  | { readonly ok: false; readonly message: string };

const SUPPORTED = ['model'];

/**
 * Checks the start options of an OpenCode execution. Where the session works is not an option:
 * it is the project's folder, which the host resolves and checks (`confirmProjectLocation`).
 */
export function parseStartOptions(
  options: RuntimeOptions,
  modelRef: string | null = null,
): ParsedStartOptions {
  const unknown = Object.keys(options).filter((key) => !SUPPORTED.includes(key));
  if (unknown.length > 0) {
    return fail(
      `Unknown OpenCode runtime options: ${unknown.join(', ')}. Supported: "model". The session works in the project's folder.`,
    );
  }
  const { model } = options;
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
  return { ok: true, value: { model: chosen } };
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
