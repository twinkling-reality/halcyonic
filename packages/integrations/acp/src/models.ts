/**
 * The models an agent app lists, read from its own list (validation/acp-agents.md): a session option's choices in
 * `session/new`'s answer (`configOptions`, ACP's `model` category), or the app's own listing command's JSON (Kiro's
 * `chat --list-models --format json`). Only what the list says is kept; anything the list leaves out is unknown.
 */

/** One model as the app's own list names it. */
export interface ListedModel {
  /** What the app takes back as the model's value. */
  readonly id: string;
  readonly name: string;
  /** The context the app's list states for it, in tokens, or null where it states none. */
  readonly contextTokens: number | null;
}

/** At most this many of an app's models are passed on; a longer list is cut. */
export const MAX_MODELS = 200;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

/** A model's value the host can hand back whole: one word of at most 256 characters, as `ModelRef` allows. */
function usableId(value: unknown): string | null {
  return typeof value === 'string' && /^\S{1,256}$/.test(value) ? value : null;
}

function named(value: unknown, id: string): string {
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : id;
}

function kept(models: readonly ListedModel[]): readonly ListedModel[] {
  const seen = new Set<string>();
  const out: ListedModel[] = [];
  for (const model of models) {
    if (seen.has(model.id)) continue;
    seen.add(model.id);
    out.push(model);
    if (out.length === MAX_MODELS) break;
  }
  return out;
}

/**
 * The choices of the session option `optionId` (`configOptions` of `session/new`'s answer), the one the app has now
 * first, as a later start takes them through `session/set_config_option`. None where the app lists no such option.
 */
export function modelsFromOptions(
  configOptions: unknown,
  optionId: string,
): readonly ListedModel[] {
  if (!Array.isArray(configOptions)) return [];
  const option = configOptions.map(record).find((entry) => entry?.id === optionId);
  if (option === undefined || option === null || !Array.isArray(option.options)) return [];
  const current = usableId(option.currentValue);
  const models: ListedModel[] = [];
  for (const choice of option.options.map(record)) {
    const id = usableId(choice?.value);
    if (id === null) continue;
    models.push({ id, name: named(choice?.name, id), contextTokens: null });
  }
  models.sort((a, b) => Number(b.id === current) - Number(a.id === current));
  return kept(models);
}

/**
 * Kiro's `chat --list-models --format json` (3.4.0, seen 2026-11-17): `{models: [{model_id, model_name,
 * context_window_tokens, ...}], default_model}`, the default first where it names one. Throws where the output is
 * not that shape, so a changed listing is said, never read as no models.
 */
export function modelsFromKiroList(stdout: string): readonly ListedModel[] {
  const parsed = record(JSON.parse(stdout));
  if (parsed === null || !Array.isArray(parsed.models)) throw new Error('not a model list');
  const fallback = usableId(parsed.default_model);
  const models: ListedModel[] = [];
  for (const entry of parsed.models.map(record)) {
    const id = usableId(entry?.model_id);
    if (id === null) continue;
    const tokens = entry?.context_window_tokens;
    models.push({
      id,
      name: named(entry?.model_name, id),
      contextTokens:
        Number.isSafeInteger(tokens) && (tokens as number) > 0 ? (tokens as number) : null,
    });
  }
  models.sort((a, b) => Number(b.id === fallback) - Number(a.id === fallback));
  return kept(models);
}

/** A name as `RuntimeModel.display_name` holds it: at most 200 characters, a longer one cut with an ellipsis. */
export function clipName(name: string): string {
  const characters = Array.from(name.trim());
  return characters.length <= 200 ? name.trim() : `${characters.slice(0, 199).join('')}…`;
}
