/// <reference types="vite/client" />

// Halcyonic's starter: the World options its host config builds from iwsdk.config.json.
declare module 'virtual:iwsdk-project' {
  import type { WorldOptions } from '@iwsdk/core';
  const projectOptions: WorldOptions;
  export default projectOptions;
}
