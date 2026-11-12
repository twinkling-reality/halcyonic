import type { KeyReader } from '@halcyonic/runtime-core';

/**
 * OpenCode as a reader of hosted providers' keys (ADR 0061, VRIFY 2026-11-12): any declared provider that serves
 * Chat Completions, the shape OpenCode's OpenAI-compatible provider speaks, reached inside the sandbox through a key
 * gate of its own. A key stored reaches the next server launch, so it counts as read at start; it never turns
 * OpenCode on by itself.
 */
export const OPENCODE_KEY_READER: KeyReader = {
  name: 'OpenCode',
  providers: 'all',
  needs: ['chat_completions'],
  reads: 'at_start',
  turnsOn: false,
};
