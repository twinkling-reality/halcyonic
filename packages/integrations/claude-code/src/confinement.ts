import { randomUUID } from 'node:crypto';
import {
  closeSync,
  mkdirSync,
  openSync,
  readdirSync,
  readSync,
  realpathSync,
  renameSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join } from 'node:path';
import type { SpawnOptions } from '@anthropic-ai/claude-agent-sdk';
import {
  type Confinement,
  confinedHomeEnvironment,
  confinedProfile,
  confinementKey,
  gatedEnvironment,
  type HostFoldersScope,
  hostFoldersDeniedProfile,
  type KeyGateDeclaration,
  SANDBOX_EXEC,
} from '@halcyonic/seatbelt';

/**
 * Halcyonic's closed sandbox for the Claude Code starts that ask for it (ADR 0048, BOX-4): a new
 * app's project. Everywhere else Claude Code runs as before, with the person's own settings and
 * hooks, so Salidium and Seorak observe it.
 */
export interface ClaudeConfinementOptions {
  /** Where each confined folder's own home, Claude Code configuration and temporary folder live. */
  readonly dataFolder: string;
  /** The folders of the tools a command needs, such as Node's installation, read only. */
  readonly toolFolders: readonly string[];
  /** Folders never read, even through a readable one: Halcyonic's data folder. */
  readonly hidden?: readonly string[];
  /**
   * For a start's folder: null to run it as before, unconfined; else what it also reads, never
   * writes: a starter's shared dependencies, the original a VR version is made from.
   */
  readonly forDirectory: (directory: string) => Pick<Confinement, 'readable' | 'originals'> | null;
  /**
   * The host's own folders, which no agent process reaches even outside the closed profile (the lead's 18:21Z step
   * 3b): its data folder, the task copies' folder; every `.halcyonic*` folder of the person's home besides. Absent,
   * a start outside the closed profile runs as before, with no profile.
   */
  readonly hostFolders?: readonly string[];
}

/** One confined folder's own place: its home, Claude Code's configuration in it, and temporary folder. */
export interface ConfinedFolder {
  readonly project: string;
  readonly root: string;
  readonly home: string;
  /** Claude Code's `CLAUDE_CONFIG_DIR`: its settings, login state and session transcripts. */
  readonly config: string;
  readonly temp: string;
  readonly reads: Pick<Confinement, 'readable' | 'originals'>;
}

/** The folder's own place under the data folder, by its key (`claude-sandbox-<key>`). */
export function confinedFolder(
  options: ClaudeConfinementOptions,
  project: string,
  reads: Pick<Confinement, 'readable' | 'originals'>,
): ConfinedFolder {
  const root = join(options.dataFolder, `claude-sandbox-${confinementKey(project)}`);
  const home = join(root, 'home');
  return { project, root, home, config: join(home, '.claude'), temp: join(root, 'tmp'), reads };
}

/** Makes the folder's own folders, readable by this user only. */
export function prepareConfinedFolder(folder: ConfinedFolder): void {
  for (const path of [folder.config, folder.temp]) {
    mkdirSync(path, { recursive: true, mode: 0o700 });
  }
}

/**
 * The launch the SDK asked for, inside the profile: `sandbox-exec -f <profile> <command> <args>`,
 * which becomes the program in place, keeping its pid and arguments for the process guard. The
 * profile is written whole under a new name, then moved into place beside the folder's own place.
 * Its only way out is the API gate's port; its environment points Claude Code's home, configuration
 * and temporary folders inside its own place, its API and its traffic at the gate, and holds no key.
 */
export function confinedLaunch(
  options: SpawnOptions,
  folder: ConfinedFolder,
  settings: Pick<ClaudeConfinementOptions, 'toolFolders' | 'hidden'>,
  gate: { readonly url: string; readonly port: number },
  declaration: KeyGateDeclaration,
  runCredential: string,
): SpawnOptions {
  const text = confinedProfile({
    project: folder.project,
    readable: folder.reads.readable,
    ...(folder.reads.originals === undefined ? {} : { originals: folder.reads.originals }),
    ...(settings.hidden === undefined ? {} : { hidden: settings.hidden }),
    home: folder.home,
    temp: folder.temp,
    // The executable's own folder: the bundled native binary, or the CLI package beside its script.
    runtimeFolder: dirname(realOrGiven(options.command)),
    toolFolders: settings.toolFolders,
    loopbackPorts: [gate.port],
  });
  const profile = `${folder.root}.sb`;
  const draft = `${profile}.${process.pid}.${randomUUID()}`;
  writeFileSync(draft, text, { mode: 0o600, flag: 'wx' });
  renameSync(draft, profile);
  const env: Record<string, string | undefined> = {
    ...options.env,
    ...confinedHomeEnvironment(folder.home),
    CLAUDE_CONFIG_DIR: folder.config,
    TMPDIR: `${folder.temp}/`,
    // Without it Claude Code makes its per-user folder in /tmp, which the profile refuses (BOX-4 probe).
    CLAUDE_CODE_TMPDIR: folder.temp,
    // zsh writes every here-document under TMPPREFIX, /tmp/zsh unless it is set.
    TMPPREFIX: join(folder.temp, 'zsh'),
    HTTP_PROXY: gate.url,
    HTTPS_PROXY: gate.url,
    http_proxy: gate.url,
    https_proxy: gate.url,
  };
  // The gate is its API (VRIFY, 2026-11-12): it adds the person's key on the way out, so the agent holds
  // only this run's credential from the gate, worthless anywhere else and dead when the run ends, and
  // no other credential.
  Object.assign(env, gatedEnvironment(declaration, gate.url, runCredential, env));
  for (const name of declaration.otherCredentials) delete env[name];
  // Nothing bypasses the gate: every request, loopback included, goes through it.
  delete env.NO_PROXY;
  delete env.no_proxy;
  return {
    ...options,
    command: SANDBOX_EXEC,
    args: ['-f', profile, options.command, ...options.args],
    env,
  };
}

/**
 * A launch outside the closed profile (VRIFY, 2026-11-13; the lead's 18:21Z step 3): through the key gate where
 * there is one, so the process holds this run's credential and the gate's address, never the person's key; and
 * under the light profile where the host names its folders, so the process and everything it runs never read or
 * write them. The person's own HOME, configuration and network stay as they are.
 */
export function openLaunch(
  options: SpawnOptions,
  gate: { readonly url: string } | null,
  declaration: KeyGateDeclaration,
  runCredential: string | null,
  profile: string | null,
): SpawnOptions {
  const env: Record<string, string | undefined> = { ...options.env };
  if (gate !== null && runCredential !== null) {
    Object.assign(env, gatedEnvironment(declaration, gate.url, runCredential, env));
  } else {
    // No gate: a cloud provider pays, which never takes the declared key, so the key is not given at all (AUDIT3 M1).
    delete env[declaration.keyVariable];
  }
  for (const name of declaration.otherCredentials) delete env[name];
  if (profile === null) return { ...options, env };
  return {
    ...options,
    command: SANDBOX_EXEC,
    args: ['-f', profile, options.command, ...options.args],
    env,
  };
}

/** Writes the light profile for `scope` at `path` whole, replacing any older one at once. */
export function writeHostFoldersProfile(path: string, scope: HostFoldersScope): string {
  const draft = `${path}.${process.pid}.${randomUUID()}`;
  writeFileSync(draft, hostFoldersDeniedProfile(scope), { mode: 0o600, flag: 'wx' });
  renameSync(draft, path);
  return path;
}

/**
 * Reads a confined session's own store (ADR 0046): its configuration folder holds that one
 * folder's sessions only, so the session's transcript is `projects/<any>/<id>.jsonl` there, and the
 * first entry that names a `cwd` says where it worked. Undefined when no transcript is there. Reads
 * at most the start of the transcript.
 */
export function findInConfinedStore(
  config: string,
  sessionId: string,
): { readonly cwd?: string } | undefined {
  let folders: string[];
  try {
    folders = readdirSync(join(config, 'projects'));
  } catch {
    return undefined;
  }
  for (const name of folders) {
    let fd: number;
    try {
      fd = openSync(join(config, 'projects', name, `${sessionId}.jsonl`), 'r');
    } catch {
      continue;
    }
    try {
      const buffer = Buffer.alloc(STORE_READ_BYTES);
      const read = readSync(fd, buffer, 0, buffer.length, 0);
      for (const line of buffer.subarray(0, read).toString('utf8').split('\n')) {
        try {
          const entry = JSON.parse(line) as { cwd?: unknown };
          if (typeof entry.cwd === 'string') return { cwd: entry.cwd };
        } catch {
          // A line cut at the end of the read, or one that is not an entry.
        }
      }
      return {};
    } finally {
      closeSync(fd);
    }
  }
  return undefined;
}

/** How much of a transcript's start the store reader reads for its first `cwd`. */
const STORE_READ_BYTES = 256 * 1024;

function realOrGiven(path: string): string {
  try {
    return realpathSync(path);
  } catch {
    return path;
  }
}
