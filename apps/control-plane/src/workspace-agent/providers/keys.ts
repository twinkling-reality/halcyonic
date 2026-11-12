import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import type { KeyState, ProviderKey } from '@halcyonic/runtime-core';
import type { KeyLookup } from './provider.ts';

const LONGEST_KEY = 4096;

/**
 * Reads a declared key at each use, so a key stored or removed on the setup page takes effect at the
 * next turn: only the private file in the data folder, the product's own entry, refused when other
 * users can read it (the rule runtimes.ts holds for the agent app's key). The environment is never
 * read for the assistant: a developer's shell or a test suite's plane that inherits a key variable
 * would otherwise send every request to a paid provider unasked (review of f0371a2a2). The value is
 * returned only inside `stored` and never put in an error or a message.
 */
export function declaredKeys(dataDir: string): KeyLookup {
  return (key: ProviderKey): KeyState => {
    const path = join(dataDir, key.file);
    let mode: number;
    try {
      mode = statSync(path).mode;
    } catch {
      return { kind: 'none' };
    }
    if ((mode & 0o077) !== 0)
      return {
        kind: 'unusable',
        code: 'key_file_open',
        message: 'The stored key can be read by other users, so it is not used.',
      };
    try {
      return checked(readFileSync(path, 'utf8').trim());
    } catch {
      return {
        kind: 'unusable',
        code: 'key_unreadable',
        message: 'The stored key could not be read.',
      };
    }
  };
}

function checked(value: string): KeyState {
  if (value === '') return { kind: 'none' };
  if (value.length > LONGEST_KEY || /\s/.test(value))
    return { kind: 'unusable', code: 'key_malformed', message: 'The stored key is not one key.' };
  return { kind: 'stored', value };
}
