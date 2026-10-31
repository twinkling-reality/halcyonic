import { createHash } from 'node:crypto';
import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

/** Where macOS keeps the sandbox launcher; never looked up on PATH. */
export const SANDBOX_EXEC = '/usr/bin/sandbox-exec';

/**
 * A project whose agent reads and writes only inside it (ADR 0041): a new app made from a starter.
 * Codex's own sandbox reads the whole disk and cannot run inside another, so the server for such a
 * project runs inside a Seatbelt profile of Halcyonic's and its turns use Codex's
 * `externalSandbox` policy.
 */
export interface Confinement {
  /** The project's folder, as the host's directory policy returned it. */
  readonly project: string;
  /** Folders the agent may read but not change, such as the starter's shared dependencies. */
  readonly readable: readonly string[];
  /**
   * Other projects' folders the agent may read but not change, such as the original a VR version is
   * made from (M1.8c-1); their version control and secret-shaped files are refused.
   */
  readonly originals?: readonly string[];
  /**
   * Folders never readable, such as Halcyonic's data folder (AUDIT M18C1-M1), even through an original:
   * refused after every allowance, then the scope's own folders are allowed again.
   */
  readonly hidden?: readonly string[];
}

/** Everything one confined server may touch; nothing else is readable, writable or reachable. */
export interface ConfinedScope extends Confinement {
  /** The server's own Codex home (`CODEX_HOME`), read and written; never the open server's. */
  readonly codexHome: string;
  /** The server's temporary folder, read and written. */
  readonly temp: string;
  /** The folder holding the pinned binary and the tools Codex runs from beside it, read only. */
  readonly runtimeFolder: string;
  /** Folders of the tools a project's commands need, such as Node's installation, read only. */
  readonly toolFolders: readonly string[];
  /** The only loopback ports it may connect to: the model's. */
  readonly loopbackPorts: readonly number[];
}

/**
 * Codex 0.157.0's own Seatbelt base policy and its minimal macOS platform defaults, as the pinned
 * binary composes them (codex-rs/sandboxing, Apache-2.0, OpenAI): closed by default; process,
 * sysctl, terminal and system-path rules; and the mach services it allows, which leave out
 * LaunchServices, the pasteboard and Apple events. Its preferences rules are left out, as Codex
 * leaves them out when reads are restricted. Nothing here grants the network or any folder of the
 * person's.
 */
const CODEX_BASE = `(version 1)

; inspired by Chrome's sandbox policy:
; https://source.chromium.org/chromium/chromium/src/+/main:sandbox/policy/mac/common.sb;l=273-319;drc=7b3962fe2e5fc9e2ee58000dc8fbf3429d84d3bd
; https://source.chromium.org/chromium/chromium/src/+/main:sandbox/policy/mac/renderer.sb;l=64;drc=7b3962fe2e5fc9e2ee58000dc8fbf3429d84d3bd

; start with closed-by-default
(deny default)

; child processes inherit the policy of their parent
(allow process-exec)
(allow process-fork)
(allow signal (target same-sandbox))

; process-info
(allow process-info* (target same-sandbox))

(allow file-write-data
  (require-all
    (path "/dev/null")
    (vnode-type CHARACTER-DEVICE)))

; sysctls permitted.
(allow sysctl-read
  (sysctl-name "hw.activecpu")
  (sysctl-name "hw.busfrequency_compat")
  (sysctl-name "hw.byteorder")
  (sysctl-name "hw.cacheconfig")
  (sysctl-name "hw.cachelinesize_compat")
  (sysctl-name "hw.cpufamily")
  (sysctl-name "hw.cpufrequency_compat")
  (sysctl-name "hw.cputype")
  (sysctl-name "hw.l1dcachesize_compat")
  (sysctl-name "hw.l1icachesize_compat")
  (sysctl-name "hw.l2cachesize_compat")
  (sysctl-name "hw.l3cachesize_compat")
  (sysctl-name "hw.logicalcpu_max")
  (sysctl-name "hw.machine")
  (sysctl-name "hw.model")
  (sysctl-name "hw.memsize")
  (sysctl-name "hw.ncpu")
  (sysctl-name "hw.nperflevels")
  ; Chrome locks these CPU feature detection down a bit more tightly,
  ; but mostly for fingerprinting concerns which isn't an issue for codex.
  (sysctl-name-prefix "hw.optional.arm.")
  (sysctl-name-prefix "hw.optional.armv8_")
  (sysctl-name "hw.packages")
  (sysctl-name "hw.pagesize_compat")
  (sysctl-name "hw.pagesize")
  (sysctl-name "hw.physicalcpu")
  (sysctl-name "hw.physicalcpu_max")
  (sysctl-name "hw.logicalcpu")
  (sysctl-name "hw.cpufrequency")
  (sysctl-name "hw.tbfrequency_compat")
  (sysctl-name "hw.vectorunit")
  (sysctl-name "machdep.cpu.brand_string")
  (sysctl-name "kern.argmax")
  (sysctl-name "kern.hostname")
  (sysctl-name "kern.maxfilesperproc")
  (sysctl-name "kern.maxproc")
  (sysctl-name "kern.osproductversion")
  (sysctl-name "kern.osrelease")
  (sysctl-name "kern.ostype")
  (sysctl-name "kern.osvariant_status")
  (sysctl-name "kern.osversion")
  (sysctl-name "kern.secure_kernel")
  ; Python's ProcessPoolExecutor queries this through sysconf(_SC_SEM_NSEMS_MAX).
  (sysctl-name "kern.sysv.semmns")
  (sysctl-name "kern.usrstack64")
  (sysctl-name "kern.version")
  (sysctl-name "sysctl.proc_cputype")
  (sysctl-name "vm.loadavg")
  (sysctl-name-prefix "hw.perflevel")
  (sysctl-name-prefix "kern.proc.pgrp.")
  (sysctl-name-prefix "kern.proc.pid.")
  (sysctl-name-prefix "net.routetable.")
)

; Allow Java to read some CPU info. This is misclassified as a "write" because
; userspace passes a memory buffer to the sysctl, but conceptually it is a read.
(allow sysctl-write
  (sysctl-name "kern.grade_cputype"))

; IOKit
(allow iokit-open
  (iokit-registry-entry-class "RootDomainUserClient")
)

; needed to look up user info, see https://crbug.com/792228
(allow mach-lookup
  (global-name "com.apple.system.opendirectoryd.libinfo")
)

; Needed for python multiprocessing on MacOS for the SemLock
(allow ipc-posix-sem)

; Needed for PyTorch/libomp on macOS to register OpenMP runtimes.
(allow ipc-posix-shm-read-data
  ipc-posix-shm-write-create
  ipc-posix-shm-write-unlink
  (ipc-posix-name-regex #"^/__KMP_REGISTERED_LIB_[0-9]+$"))

(allow mach-lookup
  (global-name "com.apple.PowerManagement.control")
)

; allow openpty()
(allow pseudo-tty)
(allow file-read* file-write* file-ioctl (literal "/dev/ptmx"))
(allow file-read* file-write*
  (require-all
    (regex #"^/dev/ttys[0-9]+")
    (extension "com.apple.sandbox.pty")))
; PTYs created before entering seatbelt may lack the extension; allow ioctl
; on those slave ttys so interactive shells detect a TTY and remain functional.
(allow file-ioctl (regex #"^/dev/ttys[0-9]+"))
; macOS platform defaults included when a split filesystem policy requests ":minimal".

; Read access to standard system paths
(allow file-read* file-test-existence
  (subpath "/Library/Apple")
  (subpath "/Library/Filesystems/NetFSPlugins")
  (subpath "/Library/Preferences/Logging")
  (subpath "/private/var/db/DarwinDirectory/local/recordStore.data")
  (subpath "/private/var/db/timezone")
  (subpath "/usr/lib")
  (subpath "/usr/share")
  (subpath "/Library/Preferences")
  (subpath "/var/db")
  (subpath "/private/var/db"))

; Map system frameworks + dylibs for loader.
(allow file-map-executable
  (subpath "/Library/Apple/System/Library/Frameworks")
  (subpath "/Library/Apple/System/Library/PrivateFrameworks")
  (subpath "/Library/Apple/usr/lib")
  (subpath "/System/Library/Extensions")
  (subpath "/System/Library/Frameworks")
  (subpath "/System/Library/PrivateFrameworks")
  (subpath "/System/Library/SubFrameworks")
  (subpath "/System/iOSSupport/System/Library/Frameworks")
  (subpath "/System/iOSSupport/System/Library/PrivateFrameworks")
  (subpath "/System/iOSSupport/System/Library/SubFrameworks")
  (subpath "/usr/lib"))

; System Framework and AppKit resources
(allow file-read* file-test-existence
  (subpath "/Library/Apple/System/Library/Frameworks")
  (subpath "/Library/Apple/System/Library/PrivateFrameworks")
  (subpath "/Library/Apple/usr/lib")
  (subpath "/System/Library/Frameworks")
  (subpath "/System/Library/PrivateFrameworks")
  (subpath "/System/Library/SubFrameworks")
  (subpath "/System/iOSSupport/System/Library/Frameworks")
  (subpath "/System/iOSSupport/System/Library/PrivateFrameworks")
  (subpath "/System/iOSSupport/System/Library/SubFrameworks")
  (subpath "/usr/lib"))

; Allow guarded vnodes.
(allow system-mac-syscall (mac-policy-name "vnguard"))

; Determine whether a container is expected.
(allow system-mac-syscall
  (require-all
    (mac-policy-name "Sandbox")
    (mac-syscall-number 67)))

; Allow resolution of standard system symlinks.
(allow file-read-metadata file-test-existence
  (literal "/etc")
  (literal "/tmp")
  (literal "/var")
  (literal "/private/etc/localtime"))

; Allow stat'ing of firmlink parent path components.
(allow file-read-metadata file-test-existence
  (path-ancestors "/System/Volumes/Data/private"))

; Allow processes to get their current working directory.
(allow file-read* file-test-existence
  (literal "/"))

; Allow FSIOC_CAS_BSDFLAGS as alternate chflags.
(allow system-fsctl (fsctl-command FSIOC_CAS_BSDFLAGS))

; Allow access to standard special files.
(allow file-read* file-test-existence
  (literal "/dev/autofs_nowait")
  (literal "/dev/random")
  (literal "/dev/urandom")
  (literal "/private/etc/master.passwd")
  (literal "/private/etc/passwd")
  (literal "/private/etc/protocols")
  (literal "/private/etc/services"))

; Allow null/zero read/write.
(allow file-read* file-test-existence file-write-data
  (literal "/dev/null")
  (literal "/dev/zero"))

; Allow read/write access to the file descriptors.
(allow file-read-data file-test-existence file-write-data
  (subpath "/dev/fd"))

; Provide access to debugger helpers.
(allow file-read* file-test-existence file-write-data file-ioctl
  (literal "/dev/dtracehelper"))

; Allow reading standard config directories.
(allow file-read* (subpath "/etc"))
(allow file-read* (subpath "/private/etc"))

(allow file-read* file-test-existence
  (literal "/System/Library/CoreServices")
  (literal "/System/Library/CoreServices/.SystemVersionPlatform.plist")
  (literal "/System/Library/CoreServices/SystemVersion.plist"))

; Some processes read /var metadata during startup.
(allow file-read-metadata (subpath "/var"))
(allow file-read-metadata (subpath "/private/var"))

; IOKit access for root domain services.
(allow iokit-open
  (iokit-registry-entry-class "RootDomainUserClient"))

; macOS Standard library queries opendirectoryd at startup
(allow mach-lookup (global-name "com.apple.system.opendirectoryd.libinfo"))

; Allow IPC to analytics, logging, trust, and other system agents.
(allow mach-lookup
  (global-name "com.apple.analyticsd")
  (global-name "com.apple.analyticsd.messagetracer")
  (global-name "com.apple.appsleep")
  (global-name "com.apple.bsd.dirhelper")
  (global-name "com.apple.diagnosticd")
  (global-name "com.apple.dt.automationmode.reader")
  (global-name "com.apple.espd")
  (global-name "com.apple.logd")
  (global-name "com.apple.logd.events")
  (global-name "com.apple.secinitd")
  (global-name "com.apple.system.DirectoryService.libinfo_v1")
  (global-name "com.apple.system.logger")
  (global-name "com.apple.system.notification_center")
  (global-name "com.apple.system.opendirectoryd.membership")
  (global-name "com.apple.trustd")
  (global-name "com.apple.trustd.agent")
  (global-name "com.apple.xpc.activity.unmanaged"))

; Allow IPC to the syslog socket for logging.
(allow network-outbound (literal "/private/var/run/syslog"))

; macOS Notifications
(allow ipc-posix-shm-read*
  (ipc-posix-name "apple.shm.notification_center"))

; Regulatory domain support.
(allow file-read*
  (literal "/private/var/db/eligibilityd/eligibility.plist"))

; Audio and power management services.
(allow mach-lookup (global-name "com.apple.audio.audiohald"))
(allow mach-lookup (global-name "com.apple.audio.AudioComponentRegistrar"))
(allow mach-lookup (global-name "com.apple.PowerManagement.control"))

; Allow reading the minimum system runtime so exec works.
(allow file-read-data (subpath "/bin"))
(allow file-read-metadata (subpath "/bin"))
(allow file-read-data (subpath "/sbin"))
(allow file-read-metadata (subpath "/sbin"))
(allow file-read-data (subpath "/usr/bin"))
(allow file-read-metadata (subpath "/usr/bin"))
(allow file-read-data (subpath "/usr/sbin"))
(allow file-read-metadata (subpath "/usr/sbin"))
(allow file-read-data (subpath "/usr/libexec"))
(allow file-read-metadata (subpath "/usr/libexec"))

(allow file-read* (subpath "/Library/Preferences"))
(allow file-read* (subpath "/opt/homebrew/lib"))
(allow file-read* (subpath "/usr/local/lib"))

; Terminal basics and device handles.
(allow file-read* (regex "^/dev/fd/(0|1|2)$"))
(allow file-write* (regex "^/dev/fd/(1|2)$"))
(allow file-read* file-write* (literal "/dev/null"))
(allow file-read* file-write* (literal "/dev/tty"))
(allow file-read-metadata (literal "/dev"))
(allow file-read-metadata (regex "^/dev/.*$"))
(allow file-read-metadata (literal "/dev/stdin"))
(allow file-read-metadata (literal "/dev/stdout"))
(allow file-read-metadata (literal "/dev/stderr"))
(allow file-read-metadata (regex "^/dev/tty[^/]*$"))
(allow file-read-metadata (regex "^/dev/pty[^/]*$"))
(allow file-read* file-write* (regex "^/dev/ttys[0-9]+$"))
(allow file-read* file-write* (literal "/dev/ptmx"))
(allow file-ioctl (regex "^/dev/ttys[0-9]+$"))

; Allow metadata traversal for firmlink parents.
(allow file-read-metadata (literal "/System/Volumes") (vnode-type DIRECTORY))
(allow file-read-metadata (literal "/System/Volumes/Data") (vnode-type DIRECTORY))
(allow file-read-metadata (literal "/System/Volumes/Data/Users") (vnode-type DIRECTORY))

; App sandbox extensions
(allow file-read* (extension "com.apple.app-sandbox.read"))
(allow file-read* file-write* (extension "com.apple.app-sandbox.read-write"))
(allow file-read* (subpath "/Applications"))`;

/**
 * Seatbelt rules for the version control and secret-shaped files anywhere under an original's
 * folder (M1.8c-1): `.git`, `.env*`, keys and certificates' private parts, SSH keys, package and
 * network credentials, and files named credentials or secrets. Written case-insensitively, as the
 * Mac's file names usually are. A path Seatbelt's regex cannot quote is refused outright.
 */
export function secretsUnder(path: string): string {
  const root = resolved(path);
  if (/["\n\r]/.test(root))
    throw new Error(`a folder Halcyonic cannot name in its profile: ${root}`);
  const literal = root.replace(/[.^$*+?()[\]{}|\\]/g, '\\$&');
  const anyCase = (word: string) =>
    [...word].map((c) => (/[a-z]/.test(c) ? `[${c}${c.toUpperCase()}]` : c)).join('');
  const under = `^${literal}/(.*/)?`;
  return [
    `${under}\\.${anyCase('git')}(/|$)`,
    `${under}\\.${anyCase('env')}[^/]*$`,
    `${under}[^/]*\\.(${['pem', 'key', 'p12', 'pfx', 'keystore', 'jks'].map(anyCase).join('|')})$`,
    `${under}${anyCase('id_')}(${['rsa', 'dsa', 'ecdsa', 'ed25519'].map(anyCase).join('|')})[^/]*$`,
    `${under}\\.(${['npmrc', 'netrc', 'pypirc'].map(anyCase).join('|')})$`,
    `${under}(${['credentials', 'secrets'].map(anyCase).join('|')})(\\.[^/]*)?$`,
  ]
    .map((pattern) => `(regex #"${pattern}")`)
    .join(' ');
}

/**
 * The folders a project's commands need to run an executable such as Node, read only: the package
 * parts of Homebrew's or /usr/local's prefix when it lives there (its libraries sit beside it), else
 * the folder two levels above the resolved executable.
 */
export function toolFoldersFor(executable: string): string[] {
  const real = resolved(executable);
  for (const prefix of TOOL_PREFIXES) {
    if (real.startsWith(`${prefix}/`)) {
      return [
        ...TOOL_SUBFOLDERS.map((name) => join(prefix, name)),
        // The one file of etc a Homebrew Node needs to start: OpenSSL's configuration.
        join(prefix, 'etc', 'openssl@3', 'openssl.cnf'),
      ];
    }
  }
  return [dirname(dirname(real))];
}

/** Package managers' prefixes whose tools load their libraries from beside them. */
export const TOOL_PREFIXES = ['/opt/homebrew', '/usr/local'] as const;

/**
 * The parts of such a prefix a tool needs: its installed packages, their links, libraries and
 * shared files. Never `var` (databases such as PostgreSQL's and MongoDB's live there) or `etc`
 * (their configuration), but for OpenSSL's configuration file.
 */
const TOOL_SUBFOLDERS = ['Cellar', 'opt', 'lib', 'bin', 'libexec', 'share'] as const;

/** macOS's Perl library: nothing of the person's, read so the system Perl runs. */
const SYSTEM_PERL = '/System/Library/Perl';

/** A short, stable name for a project's server: its Codex home and record file carry it. */
export function confinementKey(project: string): string {
  return createHash('sha256').update(project).digest('hex').slice(0, 16);
}

/**
 * The Seatbelt profile for one confined server: Codex's closed base, then reads of the scope's
 * folders only (and the metadata of the folders above them, so their paths resolve), writes in the
 * project, the server's own Codex home and its temporary folder only, and connections to the given
 * loopback ports only. No bind and no inbound connection is allowed. Paths are given as the file
 * system resolves them.
 */
export function confinedProfile(scope: ConfinedScope): string {
  const quote = (path: string) => JSON.stringify(resolved(path));
  const writable = [scope.project, scope.codexHome, scope.temp];
  const own = [
    ...writable,
    scope.runtimeFolder,
    ...scope.readable,
    ...scope.toolFolders,
    // macOS's own Perl library, so /usr/bin/perl runs (the launch check binds with it).
    SYSTEM_PERL,
  ];
  const originals = scope.originals ?? [];
  const hidden = scope.hidden ?? [];
  // The scope's own folders are allowed again after the hidden ones are refused, so none of them may
  // be or hold a hidden folder, or the re-allow would reopen it (AUDIT M18C1-A-S1).
  for (const folder of own) {
    const mine = resolved(folder);
    for (const path of hidden) {
      const kept = resolved(path);
      if (kept === mine || kept.startsWith(mine.endsWith('/') ? mine : `${mine}/`))
        throw new Error(`the confinement's own folder ${mine} is or holds the hidden ${kept}`);
    }
  }
  const readable = [...own, ...originals];
  const each = (filter: string, paths: readonly string[]) =>
    paths.map((path) => `(${filter} ${quote(path)})`).join(' ');
  return [
    CODEX_BASE,
    '',
    "; Halcyonic's confinement of one project (ADR 0041).",
    `(allow file-read* file-test-existence ${each('subpath', readable)})`,
    `(allow file-read-metadata ${each('path-ancestors', readable)})`,
    `(allow file-write* ${each('subpath', writable)})`,
    ...scope.loopbackPorts.map(
      (port) => `(allow network-outbound (remote ip "localhost:${port}"))`,
    ),
    // Later rules win: an original's secret shapes, then the hidden folders, are refused after the
    // allowances above; the scope's own folders are allowed again last, so nothing of theirs is lost.
    ...(originals.length === 0
      ? []
      : [`(deny file-read* file-test-existence ${originals.map(secretsUnder).join(' ')})`]),
    ...(hidden.length === 0
      ? []
      : [
          `(deny file-read* file-test-existence ${each('subpath', hidden)})`,
          `(allow file-read* file-test-existence ${each('subpath', own)})`,
          // Each folder between a hidden one and an own folder in it may be found and its metadata read, never
          // listed or read, so the own folder's path resolves (Codex canonicalizes its home at start; CX1).
          ...(between(hidden, own).length === 0
            ? []
            : [
                `(allow file-read-metadata file-test-existence ${each('literal', between(hidden, own))})`,
              ]),
        ]),
    // Closed by default already; said again so no later allowance can reopen them: opening another
    // app through LaunchServices, the pasteboard, and Apple events to other apps.
    '(deny lsopen)',
    '(deny mach-lookup (global-name-regex #"^com\\.apple\\.pasteboard\\."))',
    '(deny appleevent-send)',
    '',
  ].join('\n');
}

/**
 * The folders from each hidden folder down to each own folder inside it, the hidden folder included
 * and the own folder not, leaving out any that is itself inside an own folder, resolved and sorted.
 */
function between(hidden: readonly string[], own: readonly string[]): string[] {
  const inside = (path: string, folder: string) =>
    path === folder || path.startsWith(folder.endsWith('/') ? folder : `${folder}/`);
  const mine = own.map(resolved);
  const found = new Set<string>();
  for (const top of hidden.map(resolved)) {
    for (const folder of mine) {
      if (folder === top || !inside(folder, top)) continue;
      for (let path = dirname(folder); inside(path, top); path = dirname(path)) {
        if (!mine.some((other) => inside(path, other))) found.add(path);
        if (path === top) break;
      }
    }
  }
  return [...found].sort();
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
      // The native call gives the spelling on disk, its letter case and Unicode form included.
      return join(realpathSync.native(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
}
