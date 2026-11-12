import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { KeyReader, WireShape } from '@halcyonic/runtime-core';
import { PROVIDERS_DIR } from '../providers.ts';

/** An assistant-like reader of every provider's key, at each use, said to be a test's: no reader of the product. */
export const TEST_ASSISTANT_READER: KeyReader = {
  name: 'Test Assistant',
  providers: 'all',
  needs: ['messages', 'chat_completions'],
  reads: 'at_each_use',
  turnsOn: false,
};

/**
 * A folder of provider declarations for a test, removed after it: the repository's own Anthropic declaration, so
 * Claude Agent's reader reaches what it reaches in the product, and each test provider as one more file (ADR 0061).
 */
export function providersFolder(t: { after: (done: () => void) => void }): {
  readonly dir: string;
  /** Declares one more provider by its one file, as a third provider is. */
  declare(id: string, name: string, speaks?: readonly WireShape[]): void;
} {
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-declared-providers-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  mkdirSync(join(dir, 'anthropic'));
  copyFileSync(
    join(PROVIDERS_DIR, 'anthropic', 'provider.json'),
    join(dir, 'anthropic', 'provider.json'),
  );
  return {
    dir,
    declare(id, name, speaks = ['chat_completions']) {
      mkdirSync(join(dir, id), { recursive: true });
      writeFileSync(
        join(dir, id, 'provider.json'),
        JSON.stringify({
          format: 1,
          id,
          name,
          upstream: 'http://127.0.0.1:1/v1',
          speaks,
          key: {
            file: `${id}-api-key`,
            variable: `${id.toUpperCase()}_API_KEY`,
            field: `${name} API Key`,
            header: { name: 'Authorization', scheme: 'Bearer' },
          },
          models: [`${id}-1`],
        }),
      );
    },
  };
}
