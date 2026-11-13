import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

/**
 * `path` as the file system resolves it: its longest existing part through `realpath` (so a link out of a folder
 * reads as where it leads), the rest as written, with `.` and `..` taken out first.
 */
export function resolvedPath(path: string): string {
  const plain = join(path);
  const rest: string[] = [];
  let at = plain;
  for (;;) {
    try {
      return join(realpathSync(at), ...rest.reverse());
    } catch {
      const parent = dirname(at);
      if (parent === at) return plain;
      rest.push(basename(at));
      at = parent;
    }
  }
}
