export {
  SalidiumClient,
  type SalidiumProvider,
  salidiumProviderFor,
  type UnderstandOptions,
} from './client.ts';
export { defaultSalidiumHome, type SalidiumOptions } from './connection.ts';
export {
  openSalidiumFeed,
  type SalidiumFeed,
  type SalidiumFeedEvent,
  type SalidiumFeedOptions,
} from './feed.ts';
export * from './understanding.ts';
