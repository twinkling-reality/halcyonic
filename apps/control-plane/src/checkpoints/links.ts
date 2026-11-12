import { posix } from 'node:path';

/**
 * Whether a link at `path` (relative to a project's folder, with `/` between names) whose target
 * text is `target` points outside that folder: an absolute target, or a relative one that climbs out
 * of it. One rule for every place a project's files leave their folder or reach it from a task's
 * copy: Send, Publish and Add. A link inside the folder is the project's own.
 */
export function linkLeavesFolder(path: string, target: string): boolean {
  if (target.startsWith('/')) return true;
  const resolved = posix.normalize(posix.join(posix.dirname(path), target));
  return resolved === '..' || resolved.startsWith('../');
}
