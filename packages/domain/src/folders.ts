/**
 * Whether two absolute folders are one, or one holds the other, so that work in one reaches the
 * other (FOLDER, ADR 0045).
 */
export function foldersOverlap(a: string, b: string): boolean {
  const left = a.endsWith('/') ? a : `${a}/`;
  const right = b.endsWith('/') ? b : `${b}/`;
  return left.startsWith(right) || right.startsWith(left);
}
