import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

/** Where macOS keeps the sandbox launcher; never looked up on PATH. */
export const SANDBOX_EXEC = '/usr/bin/sandbox-exec';

/**
 * The system's own paths a tool reads to run, and nothing of a person's, another app's or a drive's:
 * libraries, frameworks, time zones, the system's configuration and its standard devices, as the
 * profile of a confined agent allows them (Codex's minimal platform defaults, ADR 0041). The check's
 * build (AUDIT P1) and undo's git (U2) read no more of the Mac than this and their own folders.
 */
export const SYSTEM_READS: readonly string[] = [
  `(allow file-read* (literal "/") ${[
    '/Library/Apple',
    '/Library/Filesystems/NetFSPlugins',
    '/Library/Preferences',
    '/System/Library/Frameworks',
    '/System/Library/PrivateFrameworks',
    '/System/Library/SubFrameworks',
    '/System/iOSSupport/System/Library/Frameworks',
    '/System/iOSSupport/System/Library/PrivateFrameworks',
    '/System/iOSSupport/System/Library/SubFrameworks',
    '/usr/lib',
    '/usr/share',
    '/bin',
    '/sbin',
    '/usr/bin',
    '/usr/sbin',
    '/usr/libexec',
    '/etc',
    '/private/etc',
    '/private/var/db/timezone',
    '/opt/homebrew/lib',
    '/usr/local/lib',
    '/dev/fd',
  ]
    .map((path) => `(subpath "${path}")`)
    .join(' ')})`,
  `(allow file-read* ${[
    '/System/Library/CoreServices',
    '/System/Library/CoreServices/SystemVersion.plist',
    '/System/Library/CoreServices/.SystemVersionPlatform.plist',
    '/private/var/db/DarwinDirectory/local/recordStore.data',
    '/private/var/db/eligibilityd/eligibility.plist',
    '/dev/null',
    '/dev/zero',
    '/dev/random',
    '/dev/urandom',
    '/dev/autofs_nowait',
    '/dev/dtracehelper',
  ]
    .map((path) => `(literal "${path}")`)
    .join(' ')})`,
  '(allow file-read-metadata (literal "/etc") (literal "/tmp") (literal "/var") (literal "/dev") (regex #"^/dev/") (subpath "/var") (subpath "/private/var") (path-ancestors "/System/Volumes/Data/private"))',
  '(allow file-read-metadata (literal "/System/Volumes") (literal "/System/Volumes/Data") (literal "/System/Volumes/Data/Users"))',
];

/** Seatbelt filters for each path and everything below it, as the file system resolves the path. */
export function subpaths(paths: readonly string[]): string {
  return paths.map((path) => `(subpath ${JSON.stringify(resolved(path))})`).join(' ');
}

/** Seatbelt filters for the folders above each path, so the path resolves where reads are closed. */
export function ancestors(paths: readonly string[]): string {
  return paths.map((path) => `(path-ancestors ${JSON.stringify(resolved(path))})`).join(' ');
}

/**
 * The path as the file system resolves it, through its nearest folder that exists, so one not yet
 * made under `/var` reads `/private/var` as Seatbelt will see it once made.
 */
export function resolved(path: string): string {
  let existing = path;
  const rest: string[] = [];
  while (true) {
    try {
      return join(realpathSync.native(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
}
