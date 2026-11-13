import type { KeyReader } from '@halcyonic/runtime-core';

/**
 * Codex as a reader of a declared provider's key (ADR 0061; VRIFY, 2026-11-13): every declared provider that serves
 * the Responses shape, the only wire Codex 0.157.0 speaks to a custom provider (`wire_api = "chat"` fails to load).
 * It takes a provider's key when its server starts, only for a confined server and only through the key gate, and a
 * stored key never turns Codex on.
 */
export const CODEX_KEY_READER: KeyReader = {
  name: 'Codex',
  providers: 'all',
  needs: ['responses'],
  reads: 'at_start',
  turnsOn: false,
};

/** The prefix of the provider id Codex knows a gated provider by: never a built-in id, never one of the person's. */
export const GATED_PROVIDER_PREFIX = 'halcyonic_';

/** The variable a gated provider's run credential is in, in the server's environment alone. */
export function gatedCredentialVariable(providerId: string): string {
  return `HALCYONIC_GATE_${providerId.toUpperCase().replace(/[^A-Z0-9]/g, '_')}_TOKEN`;
}

/** The fields of a provider entry that would add to what a request carries, and so never sit on a gated one. */
const ADDING_FIELDS = [
  'http_headers',
  'env_http_headers',
  'query_params',
  'experimental_bearer_token',
];

/**
 * Why Codex's effective configuration does not hold the gated providers exactly as the adapter passed them, or null
 * (AUDIT3's read, addition A): every `halcyonic_*` entry is one the adapter made for this server, at its gate, with
 * its credential's variable and the Responses wire, and nothing that adds headers, query parameters or a bearer
 * token; no other provider names a gate's variable; and every gated entry the adapter passed is there.
 */
export function gatedEntriesProblem(
  config: Readonly<Record<string, unknown>>,
  gated: ReadonlyMap<string, string>,
): string | null {
  const raw = config.model_providers;
  const providers =
    typeof raw === 'object' && raw !== null && !Array.isArray(raw)
      ? (raw as Record<string, unknown>)
      : {};
  for (const [id, value] of Object.entries(providers)) {
    const entry =
      typeof value === 'object' && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : {};
    if (!id.startsWith(GATED_PROVIDER_PREFIX)) {
      if (typeof entry.env_key === 'string' && entry.env_key.startsWith('HALCYONIC_GATE_'))
        return `the provider ${id} names ${entry.env_key}, a key gate's credential`;
      continue;
    }
    const url = gated.get(id);
    if (url === undefined) return `the provider ${id} is not one Halcyonic made for this server`;
    if (entry.base_url !== url) return `the provider ${id} does not point at its key gate`;
    if (entry.env_key !== gatedCredentialVariable(id.slice(GATED_PROVIDER_PREFIX.length)))
      return `the provider ${id} names another credential`;
    if (entry.wire_api !== 'responses') return `the provider ${id} does not speak Responses`;
    const adding = ADDING_FIELDS.filter((field) => {
      const held = entry[field];
      return (
        held !== undefined &&
        held !== null &&
        !(typeof held === 'object' && Object.keys(held as object).length === 0)
      );
    });
    if (adding.length > 0) return `the provider ${id} adds ${adding.join(', ')}`;
  }
  for (const id of gated.keys())
    if (providers[id] === undefined) return `Codex did not apply the provider ${id}`;
  return null;
}
