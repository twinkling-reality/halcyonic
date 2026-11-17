import { execFileSync } from 'node:child_process';
import { readFileSync, statSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';
import { configEntries, scrubPlan, scrubTakesSecret } from '../checkpoints/task-copies.ts';

/**
 * Whether the git settings of `folder` hold a sign-in its agent could read unasked (AUDIT3 M3 and M3b, 2026-11-17):
 * wherever a task's copy would take one out (`scrubPlan`, the one list the copy's scrub reads too), never for a remote
 * or a hook alone, which carry none. Claude Code reads a folder's own `.git/config` without asking
 * (claude-code-unasked.md), so a task that works in the folder itself, not in a copy, would put it in its model's
 * context. Reads the git folder's `config` and a linked worktree's own `config.worktree`, following a `.git` file to
 * its git folder and a worktree to its main settings, through git with no other settings (as the scrub does), in step. A file
 * git cannot read counts as holding one. Never prints what it finds.
 */
export function folderGitHoldsSignIn(folder: string, git = '/usr/bin/git'): boolean {
  for (const place of gitConfigPaths(folder)) {
    if (!isFile(place)) continue;
    const listed = listConfig(git, place);
    if (listed === null) return true;
    if (scrubTakesSecret(scrubPlan(configEntries(listed)))) return true;
  }
  return false;
}

/**
 * `git config --list` of one settings file, read in step with the start that asks (one small file, a few
 * milliseconds), with no other settings, as the copy's scrub reads it. Null where git cannot read it.
 */
function listConfig(git: string, file: string): string | null {
  try {
    return execFileSync(git, ['config', '--file', file, '--null', '--list'], {
      env: {
        PATH: '/usr/bin:/bin',
        LC_ALL: 'C',
        GIT_CONFIG_NOSYSTEM: '1',
        GIT_CONFIG_GLOBAL: '/dev/null',
        HOME: '/dev/null',
      },
      timeout: 10_000,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    });
  } catch {
    return null;
  }
}

function isFile(path: string): boolean {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
}

/** The settings files git reads for `folder`'s repository: its own `config`, and a linked worktree's too. */
function gitConfigPaths(folder: string): string[] {
  const dotGit = join(folder, '.git');
  try {
    const stat = statSync(dotGit);
    if (stat.isDirectory()) return [join(dotGit, 'config'), join(dotGit, 'config.worktree')];
    if (!stat.isFile()) return [];
    const line = readFileSync(dotGit, 'utf8')
      .match(/^gitdir:\s*(.+)$/m)?.[1]
      ?.trim();
    if (line === undefined) return [];
    const gitDir = isAbsolute(line) ? line : resolve(folder, line);
    // A linked worktree keeps its remotes in the main repository's settings, named by its commondir.
    let common = gitDir;
    try {
      const named = readFileSync(join(gitDir, 'commondir'), 'utf8').trim();
      if (named !== '') common = isAbsolute(named) ? named : resolve(gitDir, named);
    } catch {
      // No commondir: the git folder's own settings.
    }
    return [join(common, 'config'), join(gitDir, 'config.worktree')];
  } catch {
    return [];
  }
}
