import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

/** Where macOS keeps the sandbox launcher; never looked up on PATH. */
export const SANDBOX_EXEC = '/usr/bin/sandbox-exec';

/**
 * What the OpenCode server, and everything it starts, may reach under Halcyonic's sandbox
 * (ADR 0028): the loopback ports it may connect to, folders it may write in, and files and
 * folders it may not read.
 */
export interface SandboxScope {
  /**
   * The only ports on loopback it may connect to: the model's (Ollama's) and the server's own. No
   * other program listening on this Mac, a debugger's port, a database or an app's own, is reached.
   */
  readonly loopbackPorts: readonly number[];
  /** The project roots a task may work in, OpenCode's own folders and the temporary folder. */
  readonly writable: readonly string[];
  /** Credentials and secrets: Halcyonic's and the person's. */
  readonly unreadable: readonly string[];
  /** Paths inside an unreadable folder that stay readable, such as OpenCode's settings. */
  readonly readable?: readonly string[];
}

/**
 * The folders OpenCode 2.0.18 writes its own data in for this environment: its data, state and
 * cache folders under the XDG directories, or their defaults in the home folder
 * (util/src/global-roots.ts), and the temporary folder.
 */
export function openCodeFolders(environment: Readonly<Record<string, string>>): string[] {
  const home = environment.HOME ?? '';
  const xdg = (name: string, fallback: string) =>
    environment[name] !== undefined && environment[name] !== ''
      ? environment[name]
      : join(home, fallback);
  return [
    join(xdg('XDG_DATA_HOME', '.local/share'), 'opencode'),
    join(xdg('XDG_STATE_HOME', '.local/state'), 'opencode'),
    join(xdg('XDG_CACHE_HOME', '.cache'), 'opencode'),
    ...(environment.TMPDIR !== undefined && environment.TMPDIR !== '' ? [environment.TMPDIR] : []),
  ];
}

/**
 * The credentials of the person's own that no agent needs: SSH keys, cloud command lines' keys,
 * the login keychains, and package registries' tokens.
 */
export function personalSecrets(home: string): string[] {
  return [
    '.ssh',
    '.aws',
    '.config/gcloud',
    '.azure',
    '.kube',
    '.docker/config.json',
    '.npmrc',
    '.pypirc',
    '.netrc',
    '.gnupg',
    'Library/Keychains',
  ].map((path) => join(home, path));
}

/**
 * A Seatbelt profile (ADR 0028): everything is allowed except what follows. Outbound network only
 * to the given loopback ports, so the model on this Mac answers and nothing else does, on this Mac
 * or beyond it; local sockets of other programs neither. Writes only in the scope's
 * writable folders and the devices a shell needs. Reads of every unreadable path refused, but of
 * the readable paths inside them. Paths are
 * given as the file system resolves them, since Seatbelt matches the resolved path (`/var` is
 * `/private/var` on macOS), through their nearest existing folder when they do not exist yet.
 */
export function sandboxProfile(scope: SandboxScope): string {
  const quote = (path: string) => JSON.stringify(resolved(path));
  const writable = scope.writable.map((path) => `(subpath ${quote(path)})`).join(' ');
  const unreadable = scope.unreadable
    .map((path) => `(subpath ${quote(path)}) (literal ${quote(path)})`)
    .join(' ');
  const readable = (scope.readable ?? []).map((path) => `(subpath ${quote(path)})`).join(' ');
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    ...scope.loopbackPorts.map(
      (port) => `(allow network-outbound (remote ip "localhost:${port}"))`,
    ),
    '(deny file-write*)',
    `(allow file-write* ${writable} (literal "/dev/null") (literal "/dev/zero") (regex #"^/dev/tty") (regex #"^/dev/fd/"))`,
    ...(unreadable === '' ? [] : [`(deny file-read* ${unreadable})`]),
    // Later rules win: what is readable inside an unreadable folder is said after it.
    ...(readable === '' ? [] : [`(allow file-read* ${readable})`]),
    '',
  ].join('\n');
}

/**
 * The path as the file system resolves it, through its nearest folder that exists, so one not yet
 * made under `/var` reads `/private/var` as Seatbelt will see it once made.
 */
function resolved(path: string): string {
  let existing = path;
  const rest: string[] = [];
  while (true) {
    try {
      return join(realpathSync(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
}
