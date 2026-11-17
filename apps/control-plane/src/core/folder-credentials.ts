import { readFileSync, statSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';

/**
 * Whether the git settings of `folder` hold a credential in an address: an http, https or ftp URL with a user part
 * (`https://user:token@host/...`, or a token as the user), in a remote's url or pushurl or a url section's name
 * (AUDIT3 M3, 2026-11-17). Claude Code reads a folder's own `.git/config` without asking (claude-code-unasked.md), so
 * a task that works in the folder itself, not in a copy (which takes the user part out, ADR 0045), would put that
 * credential in its model's context. Follows a `.git` file to its git folder, and a linked worktree to its main one. Reads, never prints, what it finds;
 * false where there is no git folder or its settings cannot be read.
 */
export function folderHoldsAddressCredential(folder: string): boolean {
  const config = gitConfigPath(folder);
  if (config === null) return false;
  let text: string;
  try {
    text = readFileSync(config, 'utf8');
  } catch {
    return false;
  }
  return /\b(?:https?|ftps?):\/\/[^\s/@"'\]]+@/i.test(text);
}

function gitConfigPath(folder: string): string | null {
  const dotGit = join(folder, '.git');
  try {
    const stat = statSync(dotGit);
    if (stat.isDirectory()) return join(dotGit, 'config');
    if (!stat.isFile()) return null;
    const line = readFileSync(dotGit, 'utf8')
      .match(/^gitdir:\s*(.+)$/m)?.[1]
      ?.trim();
    if (line === undefined) return null;
    const gitDir = isAbsolute(line) ? line : resolve(folder, line);
    // A linked worktree keeps its remotes in the main repository's settings, named by its commondir.
    let common = gitDir;
    try {
      const named = readFileSync(join(gitDir, 'commondir'), 'utf8').trim();
      if (named !== '') common = isAbsolute(named) ? named : resolve(gitDir, named);
    } catch {
      // No commondir: the git folder's own settings.
    }
    return join(common, 'config');
  } catch {
    return null;
  }
}
