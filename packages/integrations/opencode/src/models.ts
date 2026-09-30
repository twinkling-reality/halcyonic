import type { OpenCodeClient } from './client.ts';
import { isRecord } from './events.ts';

/**
 * One model as `GET /api/model` of OpenCode 2.0.18 lists it, reduced to the fields the adapter
 * reads. The listing also carries each model's settings (API keys among them), headers and
 * request body; none of those are kept.
 */
export interface OpenCodeModel {
  readonly providerID: string;
  /** The model's id within its provider, as a session's `model.id` names it. */
  readonly id: string;
  readonly name: string;
  /** `capabilities.tools`, or null when the listing does not say. */
  readonly tools: boolean | null;
  /** `limit.context`, or null when it is missing or not a positive integer. */
  readonly contextTokens: number | null;
  /** `settings.baseURL`, the endpoint OpenCode sends the model's requests to, when set. */
  readonly baseUrl: string | null;
}

export interface ModelRef {
  readonly providerID: string;
  readonly id: string;
}

/**
 * Reads the models OpenCode offers for a directory, whose own configuration may add or disable
 * models, or for the server's own location when `directory` is null. Each location settles
 * separately: right after the server starts, or after a directory is first used, the list may
 * be empty or lack the models OpenCode discovers from local servers such as Ollama.
 */
export async function readModels(
  client: OpenCodeClient,
  directory: string | null,
  timeoutMs: number,
): Promise<OpenCodeModel[]> {
  const response = await client.request('GET', `/api/model${location(directory)}`, { timeoutMs });
  if (response.status !== 200) throw new Error(`GET /api/model answered ${response.status}`);
  const data = isRecord(response.body) ? response.body.data : undefined;
  if (!Array.isArray(data)) throw new Error('GET /api/model answered without a list of models');
  return parseModels(data);
}

/** Reads the model a session without an explicit model uses in `directory`, or null for none yet. */
export async function readDefaultModel(
  client: OpenCodeClient,
  directory: string,
  timeoutMs: number,
): Promise<ModelRef | null> {
  const response = await client.request('GET', `/api/model/default${location(directory)}`, {
    timeoutMs,
  });
  if (response.status !== 200)
    throw new Error(`GET /api/model/default answered ${response.status}`);
  const data = isRecord(response.body) ? response.body.data : undefined;
  if (!isRecord(data) || typeof data.providerID !== 'string' || typeof data.id !== 'string') {
    return null;
  }
  return { providerID: data.providerID, id: data.id };
}

/** Keeps the listed models that are enabled and well formed, field by field. */
export function parseModels(data: readonly unknown[]): OpenCodeModel[] {
  const models: OpenCodeModel[] = [];
  for (const item of data) {
    if (!isRecord(item) || item.enabled === false) continue;
    const { providerID, id } = item;
    if (
      typeof providerID !== 'string' ||
      providerID === '' ||
      typeof id !== 'string' ||
      id === ''
    ) {
      continue;
    }
    const capabilities = isRecord(item.capabilities) ? item.capabilities : {};
    const limit = isRecord(item.limit) ? item.limit : {};
    const settings = isRecord(item.settings) ? item.settings : {};
    const context = limit.context;
    models.push({
      providerID,
      id,
      name: typeof item.name === 'string' && /\S/.test(item.name) ? item.name : id,
      tools: typeof capabilities.tools === 'boolean' ? capabilities.tools : null,
      contextTokens:
        typeof context === 'number' && Number.isSafeInteger(context) && context > 0
          ? context
          : null,
      baseUrl: typeof settings.baseURL === 'string' ? settings.baseURL : null,
    });
  }
  return models;
}

export function sameModel(model: OpenCodeModel | ModelRef, ref: ModelRef): boolean {
  return model.providerID === ref.providerID && model.id === ref.id;
}

/** The `location` query of OpenCode 2.0.18: a deep object, sent with its brackets escaped. */
function location(directory: string | null): string {
  return directory === null ? '' : `?location%5Bdirectory%5D=${encodeURIComponent(directory)}`;
}
