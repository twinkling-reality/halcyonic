import { lstat, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { FileChangeKind } from '@halcyonic/contracts';
import { NAMED_FILES, namedFile } from '../starters.ts';

/** The most bytes of a named file read to compare it: a page's markup, never a large file. */
const MOST_NAMED_BYTES = 1024 * 1024;

/**
 * A project folder's changes against its starter's template, without the host's own naming of the app (VRIFY,
 * 2026-11-17): a named file whose bytes are exactly the template's as the host applied it, `namedFile` with the
 * project's name, is no change the project made, so a fresh project still reads Starter Only. A named file that
 * differs in any other byte still counts, as does every other file; an added or removed one, a link or a file that
 * cannot be read is left as read. Null stays null: nothing is guessed.
 */
export async function withoutHostNaming(
  changes: readonly { path: string; change: FileChangeKind }[] | null,
  template: string,
  folder: string,
  name: string,
): Promise<{ path: string; change: FileChangeKind }[] | null> {
  if (changes === null) return null;
  const kept: { path: string; change: FileChangeKind }[] = [];
  for (const change of changes) {
    if (
      change.change !== 'changed' ||
      !NAMED_FILES.includes(change.path) ||
      !(await namedAsApplied(join(template, change.path), join(folder, change.path), name))
    )
      kept.push(change);
  }
  return kept;
}

/** Whether `inFolder` holds exactly `inTemplate`'s bytes named with `name`, both plain files, never through a link. */
async function namedAsApplied(
  inTemplate: string,
  inFolder: string,
  name: string,
): Promise<boolean> {
  const [base, made] = await Promise.all([plainBytes(inTemplate), plainBytes(inFolder)]);
  return base !== null && made !== null && namedFile(base, name).equals(made);
}

async function plainBytes(path: string): Promise<Buffer | null> {
  try {
    const stat = await lstat(path);
    if (!stat.isFile() || stat.size > MOST_NAMED_BYTES) return null;
    return await readFile(path);
  } catch {
    return null;
  }
}
