import type { KeyReader } from '@halcyonic/runtime-core';

/** The id this adapter's descriptor carries unless its options name another. */
export const CLAUDE_AGENT_RUNTIME_ID = 'claude-agent';

/**
 * Claude Agent as a reader of a declared provider's key (ADR 0061): it reads Anthropic's key when the host starts it,
 * and a stored key turns it on. The provider's own declaration says where the key is kept and what the person sees.
 */
export const CLAUDE_AGENT_READER: KeyReader = {
  name: 'Claude Agent',
  providers: ['anthropic'],
  needs: ['messages'],
  reads: 'at_start',
  turnsOn: true,
};
