import { realpathSync, statSync } from 'node:fs';
import { isAbsolute, relative, sep } from 'node:path';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';

/**
 * Allows a directory only when it exists and its real path lies inside one of the configured
 * project roots. Resolving real paths defeats `..` segments and symbolic links that lead outside a
 * root.
 */
export function createDirectoryPolicy(roots: readonly string[]): DirectoryPolicy {
  const realRoots = roots.map((root) => realpathSync(root));
  return (path) => {
    if (!isAbsolute(path)) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${path} is not an absolute path.`,
      };
    }
    let real: string;
    try {
      real = realpathSync(path);
    } catch {
      return { ok: false, code: 'location_missing', message: `${path} does not exist.` };
    }
    if (statSync(real, { throwIfNoEntry: false })?.isDirectory() !== true) {
      return { ok: false, code: 'location_missing', message: `${path} is not a folder.` };
    }
    if (realRoots.length === 0) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: 'No project roots are configured on the control plane (HALCYONIC_PROJECT_ROOTS).',
      };
    }
    if (!realRoots.some((root) => contains(root, real))) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${path} is outside the project roots configured on the control plane.`,
      };
    }
    return { ok: true, directory: real };
  };
}

/** Whether `candidate` is `root` or lies below it; both must be real paths. */
export function contains(root: string, candidate: string): boolean {
  const path = relative(root, candidate);
  return path === '' || (path !== '..' && !path.startsWith(`..${sep}`) && !isAbsolute(path));
}
