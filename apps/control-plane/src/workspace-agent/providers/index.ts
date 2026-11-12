import { anthropicProvider } from './anthropic/declaration.ts';
import { anthropicMessages } from './anthropic/messages.ts';
import { openAiChat } from './openai-compatible/chat.ts';
import type { HostedProvider, Protocol } from './provider.ts';

/** The protocols the host speaks to hosted providers, by the name a declaration gives. */
export const PROTOCOLS: ReadonlyMap<string, Protocol> = new Map([
  ['anthropic_messages', anthropicMessages],
  ['openai_chat', openAiChat],
]);

/** The hosted providers the product declares, in the order the assistant prefers a stored key. */
export const DECLARED_PROVIDERS: readonly HostedProvider[] = [anthropicProvider];
