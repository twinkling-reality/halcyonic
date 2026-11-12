import type { HostedProvider } from '../provider.ts';

/**
 * Anthropic, as its documentation states it on 2026-11-12 (validation/hosted-assistant.md): the
 * Messages API at api.anthropic.com, the key in `x-api-key`, and the current model ids. The key is
 * the one the Claude Agent runtime reads: ANTHROPIC_API_KEY, else `<data dir>/anthropic-api-key`.
 */
export const anthropicProvider: HostedProvider = {
  id: 'anthropic',
  name: 'Anthropic',
  protocol: 'anthropic_messages',
  upstream: 'https://api.anthropic.com',
  key: {
    variable: 'ANTHROPIC_API_KEY',
    file: 'anthropic-api-key',
    header: { name: 'x-api-key', scheme: null },
  },
  models: ['claude-opus-5-5', 'claude-sonnet-5-5', 'claude-haiku-5-5', 'claude-fable-5-2'],
};
