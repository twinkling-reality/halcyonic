import type { HostedProvider } from '../provider.ts';

/**
 * Meta's Model API, as its documentation states it on 2026-11-12 (validation/meta-models.md): the
 * OpenAI-compatible Chat Completions shape at api.meta.ai/v1, the key as a Bearer token, and its
 * Standard-mode models only. The `-contributor` ids, which let Meta train on the content, are never
 * declared. Muse Wonder thinks at `max` unless told otherwise, and its output budget counts that
 * thinking, so the declaration asks for `low` and names the budget field Meta documents. The key is
 * the one Meta's coding agent and OpenCode's built-in provider read: META_API_KEY, kept in
 * `<data dir>/meta-api-key`.
 */
export const metaProvider: HostedProvider = {
  id: 'meta',
  name: 'Meta',
  protocol: 'openai_chat',
  upstream: 'https://api.meta.ai/v1',
  key: {
    variable: 'META_API_KEY',
    file: 'meta-api-key',
    header: { name: 'authorization', scheme: 'Bearer' },
  },
  models: ['muse-wonder-1.0', 'muse-spark-1.3'],
  effort: 'low',
  outputField: 'max_completion_tokens',
};
