import { realpathSync } from 'node:fs';
import { sep } from 'node:path';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';

/** A policy for tests that allows `root` and what lies below it, answering with real paths. */
export function allowOnly(root: string): DirectoryPolicy {
  const allowed = realpathSync(root);
  return (path) => {
    let real: string;
    try {
      real = realpathSync(path);
    } catch {
      return { ok: false, message: `${path} does not exist.` };
    }
    return real === allowed || real.startsWith(`${allowed}${sep}`)
      ? { ok: true, directory: real }
      : { ok: false, message: `${path} is outside the directories this test allows.` };
  };
}
