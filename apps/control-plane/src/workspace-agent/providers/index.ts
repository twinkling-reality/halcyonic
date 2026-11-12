import type { KeyReader, WireShape } from '@halcyonic/runtime-core';
import { anthropicMessages } from './anthropic/messages.ts';
import { openAiChat } from './openai-compatible/chat.ts';
import type { Protocol } from './provider.ts';

/** The wire shapes the assistant speaks, each implemented once by the host (ADR 0061); a provider names its shapes. */
export const PROTOCOLS: ReadonlyMap<WireShape, Protocol> = new Map<WireShape, Protocol>([
  ['chat_completions', openAiChat],
  ['messages', anthropicMessages],
]);

/**
 * The assistant as a reader of providers' keys (ADR 0061): every declared provider that speaks a shape it implements,
 * read at each use, and never turned on by a stored key (the lead's D1): the person chooses with Use Model.
 */
export const ASSISTANT_READER: KeyReader = {
  name: 'The Assistant',
  providers: 'all',
  needs: [...PROTOCOLS.keys()],
  reads: 'at_each_use',
  turnsOn: false,
};
