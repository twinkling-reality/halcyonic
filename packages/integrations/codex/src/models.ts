import type { ModelServed, RuntimeModel } from '@halcyonic/contracts';
import { isRecord } from './events.ts';
import { toModelRef } from './options.ts';

/** What people call Codex 0.157.0's built-in providers; one the configuration defines keeps its own name. */
const BUILT_IN_NAMES: Readonly<Record<string, string>> = {
  openai: 'OpenAI',
  ollama: 'Ollama',
  lmstudio: 'LM Studio',
  'amazon-bedrock': 'Amazon Bedrock',
};

/** Codex's built-in providers for models served on this machine, by port unless configured. */
const LOCAL_BUILT_INS: ReadonlySet<string> = new Set(['ollama', 'lmstudio']);

/** The built-in providers whose configured entry Codex 0.157.0 applies: Amazon Bedrock's. */
const BEDROCK: ReadonlySet<string> = new Set(['amazon-bedrock', 'amazon-bedrock-runtime']);

/** The built-in providers whose configured entry Codex 0.157.0 ignores. */
const NOT_OVERRIDABLE: ReadonlySet<string> = new Set(['openai', ...LOCAL_BUILT_INS]);

/**
 * The models Codex can run a thread on, from what `config/read` and `model/list` report, as the
 * contract describes them (ADR 0016).
 *
 * Codex 0.157.0 accepts any model name and cannot list the models a provider serves: `model/list`
 * returns a catalog, the one built into the binary (OpenAI's models) unless the configuration
 * names its own (`model_catalog_json`, or a provider's `model_catalog_url`). So the list holds the
 * catalog's models only when the catalog is the configured provider's, and the configured model
 * in any case; each under the configured provider, the one a thread runs on unless its start names
 * another.
 */
export function modelsFromCodex(
  config: Readonly<Record<string, unknown>>,
  catalog: readonly unknown[],
  environment: Readonly<Record<string, string>>,
): RuntimeModel[] {
  const provider = nonBlank(config.model_provider) ?? 'openai';
  const defined = definedProvider(config, provider);
  const label = BUILT_IN_NAMES[provider] ?? nonBlank(defined?.name) ?? provider;
  const served = servedBy(config, provider, environment);
  const catalogIsProviders =
    nonBlank(config.model_catalog_json) !== null ||
    nonBlank(defined?.model_catalog_url) !== null ||
    (provider === 'openai' && defined === null);
  const models = new Map<string, RuntimeModel>();
  const add = (model: string, name: string, contextTokens: number | null) => {
    const modelRef = toModelRef(provider, model);
    if (models.has(modelRef) || !/^\S{1,256}$/.test(modelRef)) return;
    models.set(modelRef, {
      model_ref: modelRef,
      display_name: clipName(`${name} (${label})`),
      served,
      // Neither list says whether a model calls tools.
      tool_calling: 'unknown',
      context_tokens: contextTokens,
    });
  };
  if (catalogIsProviders) {
    for (const entry of catalog) {
      if (!isRecord(entry) || entry.hidden === true) continue;
      const model = nonBlank(entry.model);
      if (model === null) continue;
      add(model, nonBlank(entry.displayName) ?? model, null);
    }
  }
  const configured = nonBlank(config.model);
  if (configured !== null) add(configured, configured, positive(config.model_context_window));
  return [...models.values()];
}

/**
 * Where the provider serves its models: `this_mac` for a loopback address, `remote` for any other,
 * `unknown` when neither the configuration nor Codex's built-in defaults say. Codex 0.157.0 ignores
 * a configured entry under a built-in provider's id, except Amazon Bedrock's
 * (`merge_configured_model_providers`, codex-rs/model-provider-info/src/lib.rs at rust-v0.157.0),
 * so `openai` is judged by `openai_base_url` alone, and the built-in `ollama` and `lmstudio`
 * providers are on this machine unless `CODEX_OSS_BASE_URL` in Codex's environment says otherwise.
 */
export function servedBy(
  config: Readonly<Record<string, unknown>>,
  provider: string,
  environment: Readonly<Record<string, string>>,
): ModelServed {
  if (provider === 'openai') {
    const base = nonBlank(config.openai_base_url);
    return base === null ? 'remote' : servedAt(base);
  }
  if (LOCAL_BUILT_INS.has(provider)) {
    const base = environment.CODEX_OSS_BASE_URL;
    return base === undefined || base === '' ? 'this_mac' : servedAt(base);
  }
  const defined = definedProvider(config, provider);
  if (BEDROCK.has(provider)) {
    const base = nonBlank(defined?.base_url);
    return base === null ? 'remote' : servedAt(base);
  }
  if (defined !== null) return servedAt(nonBlank(defined.base_url));
  return 'unknown';
}

function servedAt(baseUrl: string | null): ModelServed {
  if (baseUrl === null) return 'unknown';
  let host: string;
  try {
    host = new URL(baseUrl).hostname.replace(/^\[|\]$/g, '');
  } catch {
    return 'unknown';
  }
  return host === 'localhost' || host === '::1' || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(host)
    ? 'this_mac'
    : 'remote';
}

/** The provider's entry in the configuration, as Codex applies it: none for a built-in it ignores. */
function definedProvider(
  config: Readonly<Record<string, unknown>>,
  provider: string,
): Readonly<Record<string, unknown>> | null {
  if (NOT_OVERRIDABLE.has(provider)) return null;
  const providers = isRecord(config.model_providers) ? config.model_providers : {};
  const defined = providers[provider];
  return isRecord(defined) ? defined : null;
}

function positive(value: unknown): number | null {
  const number = typeof value === 'bigint' ? Number(value) : value;
  return typeof number === 'number' && Number.isSafeInteger(number) && number > 0 ? number : null;
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && /\S/.test(value) ? value : null;
}

function clipName(name: string): string {
  const characters = Array.from(name.trim());
  return characters.length <= 200 ? name.trim() : `${characters.slice(0, 199).join('')}…`;
}
