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
    if (!isAbsolute(path)) return { ok: false, message: `${path} is not an absolute path.` };
    let real: string;
    try {
      real = realpathSync(path);
    } catch {
      return { ok: false, message: `${path} does not exist.` };
    }
    if (!statSync(real).isDirectory()) return { ok: false, message: `${path} is not a directory.` };
    if (realRoots.length === 0) {
      return {
        ok: false,
        message: 'No project roots are configured on the control plane (HALCYONIC_PROJECT_ROOTS).',
      };
    }
    if (!realRoots.some((root) => contains(root, real))) {
      return {
        ok: false,
        message: `${path} is outside the project roots configured on the control plane.`,
      };
    }
    return { ok: true, directory: real };
  };
}

function contains(root: string, candidate: string): boolean {
  const path = relative(root, candidate);
  return path === '' || (path !== '..' && !path.startsWith(`..${sep}`) && !isAbsolute(path));
}
