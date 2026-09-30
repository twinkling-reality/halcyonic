import type { ModelServed, RuntimeModel } from '@halcyonic/contracts';
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

/** The `model_ref` of a model: OpenCode's own `provider/model`, which a session takes back. */
export function modelRefOf(model: ModelRef): string {
  return `${model.providerID}/${model.id}`;
}

/** What people call the providers OpenCode 2.0.18 discovers or ships; any other keeps its id. */
const PROVIDER_NAMES: Readonly<Record<string, string>> = {
  ollama: 'Ollama',
  lmstudio: 'LM Studio',
  vllm: 'vLLM',
  opencode: 'OpenCode Zen',
};

/**
 * A listed model as the contract describes it (ADR 0016). The display name names the provider as
 * well as the model, so a local model named after a hosted one, such as Ollama's `gpt-4o:latest`,
 * never reads as the hosted one. Where it runs is decided from the address OpenCode sends its
 * requests to, never from its name.
 */
export function toRuntimeModel(model: OpenCodeModel): RuntimeModel {
  const provider = PROVIDER_NAMES[model.providerID] ?? model.providerID;
  return {
    model_ref: modelRefOf(model),
    display_name: clipName(`${model.name} (${provider})`),
    served: servedBy(model),
    tool_calling:
      model.tools === true ? 'declared' : model.tools === false ? 'not_declared' : 'unknown',
    context_tokens: model.contextTokens,
  };
}

/**
 * `this_mac` when OpenCode reaches the model on a loopback address, `remote` when it reaches it
 * anywhere else, `unknown` without an address. Ollama serves the models it names with a `cloud`
 * tag from its own hosted service, whatever address OpenCode reaches Ollama at, so those are
 * `remote`; a cloud model copied to a name without that tag would still read as this Mac's, a gap
 * OpenCode's list gives no way to close.
 */
export function servedBy(model: OpenCodeModel): ModelServed {
  if (model.baseUrl === null) return 'unknown';
  let host: string;
  try {
    host = new URL(model.baseUrl).hostname;
  } catch {
    return 'unknown';
  }
  if (!isLoopback(host)) return 'remote';
  if (model.providerID === 'ollama' && /[:-]cloud$/.test(model.id)) return 'remote';
  return 'this_mac';
}

function isLoopback(host: string): boolean {
  const bare = host.replace(/^\[|\]$/g, '');
  return bare === 'localhost' || bare === '::1' || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(bare);
}

function clipName(name: string): string {
  const characters = Array.from(name.trim());
  return characters.length <= 200 ? name.trim() : `${characters.slice(0, 199).join('')}…`;
}

/** The `location` query of OpenCode 2.0.18: a deep object, sent with its brackets escaped. */
function location(directory: string | null): string {
  return directory === null ? '' : `?location%5Bdirectory%5D=${encodeURIComponent(directory)}`;
}
