/**
 * The files that may hold secrets, by their names: one list for every place that keeps them away,
 * so the places cannot drift apart. A sandbox keeps an agent from them, the headset's reads of a
 * project never read or diff them, and a project's export leaves them out. By name only: a secret
 * written into a file of another name (a cluster's config, a deployment's state) is not found here.
 * Matched case-insensitively, as the Mac's file names usually are.
 */
export const SECRET_FILE_RULES = {
  /** A folder of version control, anywhere in the path. */
  folders: ['.git'],
  /** File names starting so: environment files of every kind. */
  prefixes: ['.env'],
  /** File name endings: private keys, certificates' private parts and key stores. */
  extensions: ['pem', 'key', 'p12', 'pfx', 'keystore', 'jks'],
  /** SSH private keys and their public halves, by their usual names. */
  sshKeys: ['rsa', 'dsa', 'ecdsa', 'ed25519'],
  /** Package and network credentials, whole names. */
  names: ['.npmrc', '.netrc', '.pypirc'],
  /** Files named so, with or without one extension. */
  stems: ['credentials', 'secrets'],
} as const;

/** Whether a path, relative to a project's folder with `/` between its parts, names a secret-shaped file. */
export function secretShapedPath(path: string): boolean {
  const parts = path.toLowerCase().split('/');
  const name = parts.at(-1) ?? '';
  const rules = SECRET_FILE_RULES;
  return (
    parts.some((part) => (rules.folders as readonly string[]).includes(part)) ||
    rules.prefixes.some((prefix) => name.startsWith(prefix)) ||
    rules.extensions.some((extension) => name.endsWith(`.${extension}`)) ||
    rules.sshKeys.some((kind) => name.startsWith(`id_${kind}`)) ||
    (rules.names as readonly string[]).includes(name) ||
    rules.stems.some((stem) => name === stem || name.startsWith(`${stem}.`))
  );
}
