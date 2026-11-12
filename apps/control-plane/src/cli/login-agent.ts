/**
 * Start at login (ADR 0024, "Launch at login"): one user LaunchAgent that starts the control plane
 * from this checkout when the person logs in, so nobody opens a Terminal to start it and no
 * window has to stay open.
 *
 *   pnpm mac-setup login on|off        the person's command: ~/Library/LaunchAgents, the default label
 *   node apps/control-plane/src/cli/login-agent.ts on|off <folder> [--label <label>] [--port <port>]
 *       [--data-dir <folder>]          the same against any folder and label, as a scratch check does
 *
 * On refuses while launchd already has the agent (turn it off, then on, to change it) and while a
 * Halcyonic started by hand answers on the port (the agent's starts would fail again and again).
 * It writes the agent's file and has launchd start it with `launchctl bootstrap` in the person's
 * own session; if launchd refuses the file, the file is removed, so nothing is left to start at
 * the next login. Off has launchd stop it (`launchctl bootout`, which sends the plane its stop
 * signal) and removes the file.
 *
 * The agent restarts the plane after any exit that is not a success: the setup page's Restart to
 * Apply (exit 75), a crash, a close that failed, and a start the plane refuses (settings it can't
 * read, its port taken), no more than once every THROTTLE_SECONDS. A stop the person asks for
 * exits 0 and stays stopped; launchd waits EXIT_TIMEOUT_SECONDS for that stop before it kills.
 *
 * Its environment is PATH (launchd's own is minimal; the PATH of the shell that turns it on is
 * kept, since a project's own check needs npm or pnpm there, AUDIT3's S4), without relative
 * folders, folders inside the checkout or a package's `node_modules/.bin` (which pnpm adds while a
 * script runs), and the two settings in SETTINGS, nothing else, so no secret is ever in it. On
 * names any other Halcyonic setting it leaves out.
 */
import { spawnSync } from 'node:child_process';
import {
  accessSync,
  constants,
  existsSync,
  lstatSync,
  mkdirSync,
  realpathSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { homedir } from 'node:os';
import { delimiter, dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defaultDataDir, loopbackListener } from '../config.ts';

/** The agent's launchd label, and so its file's name. */
export const LOGIN_AGENT_LABEL = 'com.halcyonic.control-plane';

/** The settings an agent may carry besides PATH: where it serves and where its data lives. */
const SETTINGS = ['HALCYONIC_PORT', 'HALCYONIC_DATA_DIR'] as const;

/** The least time between two starts launchd makes, so a refused start does not fill the log. */
export const THROTTLE_SECONDS = 30;

/** How long launchd waits for the plane's own stop (its checkpoint, its agents) before it kills. */
export const EXIT_TIMEOUT_SECONDS = 30;

/** How long On waits for the started plane to answer its health check. */
const STARTED_WITHIN_MS = 30_000;

/** A launchd label: reverse-domain words, nothing a file name or the property list could misread. */
const LABEL = /^[A-Za-z0-9]+(\.[A-Za-z0-9-]+)+$/;

type Setting = (typeof SETTINGS)[number];

export interface LoginAgent {
  readonly label: string;
  /** The absolute path of the node that runs the control plane. */
  readonly node: string;
  /** The absolute path of the control plane's entry. */
  readonly entry: string;
  /** The checkout the control plane runs from. */
  readonly workingDirectory: string;
  /** Where the control plane's output goes, since no window shows it. */
  readonly logFile: string;
  /** The data folder, made before launchd opens the log inside it. */
  readonly dataDir: string;
  readonly environment: Readonly<Record<string, string>>;
  /** The Halcyonic settings set where On ran that the agent does not carry, by name only. */
  readonly leftOut: readonly string[];
}

/** The checkout this file belongs to. */
export function checkoutOf(file: string = fileURLToPath(import.meta.url)): string {
  return resolve(dirname(file), '..', '..', '..', '..');
}

function inside(folder: string, root: string): boolean {
  const path = relative(root, folder);
  return path === '' || (!path.startsWith('..') && !isAbsolute(path));
}

/**
 * PATH as the agent keeps it: absolute folders only, and none inside the checkout or a package's
 * `node_modules/.bin`, which a package manager adds while it runs a script.
 */
export function keptPath(path: string | undefined, checkout: string): string | undefined {
  const folders = (path ?? '')
    .split(delimiter)
    .filter((folder) => folder !== '' && isAbsolute(folder))
    .filter((folder) => !inside(folder, checkout))
    .filter((folder) => !resolve(folder).endsWith(`${sep}node_modules${sep}.bin`));
  return folders.length === 0 ? undefined : folders.join(delimiter);
}

/**
 * The node the agent runs: the first `node` on PATH that is the very node running this command
 * (the same file once links are followed), so the link a package manager keeps, such as
 * Homebrew's /opt/homebrew/bin/node, is written rather than the versioned folder it points into
 * today. Where PATH names no such node, this node's own path. A version manager that puts a
 * per-shell or per-version folder on PATH (nvm, fnm) gives a path an upgrade or a closed shell
 * removes; the person sees the path On prints.
 */
export function nodeFor(
  path: string | undefined,
  own: string,
  sameFile: (found: string, own: string) => boolean,
): string {
  for (const folder of (path ?? '').split(delimiter)) {
    if (folder === '' || !isAbsolute(folder)) continue;
    const found = join(folder, 'node');
    if (sameFile(found, own)) return found;
  }
  return own;
}

function sameExecutable(found: string, own: string): boolean {
  try {
    accessSync(found, constants.X_OK);
    return realpathSync(found) === realpathSync(own);
  } catch {
    return false;
  }
}

/** The agent for a checkout: its node, the control plane's entry, PATH kept and the settings given. */
export function loginAgentFor(options: {
  readonly checkout: string;
  readonly node: string;
  readonly env: NodeJS.ProcessEnv;
  readonly label?: string | undefined;
  readonly settings?: { readonly [name in Setting]?: string | undefined };
}): LoginAgent {
  const environment: Record<string, string> = {};
  const path = keptPath(options.env.PATH, options.checkout);
  if (path !== undefined) environment.PATH = path;
  for (const name of SETTINGS) {
    const value = options.settings?.[name];
    if (value !== undefined && value !== '') environment[name] = value;
  }
  const dataDir = defaultDataDir({ HALCYONIC_DATA_DIR: environment.HALCYONIC_DATA_DIR });
  const leftOut = Object.keys(options.env)
    .filter((name) => name.startsWith('HALCYONIC_') && !(name in environment))
    .filter((name) => (options.env[name] ?? '') !== '')
    .sort();
  return {
    label: options.label ?? LOGIN_AGENT_LABEL,
    node: options.node,
    entry: join(options.checkout, 'apps', 'control-plane', 'src', 'main.ts'),
    workingDirectory: options.checkout,
    logFile: join(dataDir, 'control-plane.log'),
    dataDir,
    environment,
    leftOut,
  };
}

function escapeXml(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

function xmlString(text: string): string {
  return `<string>${escapeXml(text)}</string>`;
}

/**
 * The agent's property list (keys as `man launchd.plist` gives them): it starts at load, with
 * `--restartable` so the setup page can apply settings; KeepAlive with SuccessfulExit false
 * starts it again after any exit that is not a success, no more than once every
 * THROTTLE_SECONDS; a stop gets EXIT_TIMEOUT_SECONDS; its output goes to the log file. Refuses a
 * label launchd could misread, a relative path, and any environment name but PATH and SETTINGS.
 */
export function loginAgentPlist(agent: LoginAgent): string {
  if (!LABEL.test(agent.label)) throw new Error(`the label ${agent.label} is not a launchd label`);
  for (const [what, path] of [
    ['node', agent.node],
    ['entry', agent.entry],
    ['working directory', agent.workingDirectory],
    ['log file', agent.logFile],
  ] as const) {
    if (!isAbsolute(path)) throw new Error(`the ${what} ${path} is not an absolute path`);
  }
  const names = Object.keys(agent.environment).sort();
  for (const name of names) {
    if (name !== 'PATH' && !(SETTINGS as readonly string[]).includes(name)) {
      throw new Error(`${name} is not a setting an agent carries`);
    }
  }
  const environment = names.map(
    (name) => `\t\t<key>${name}</key>\n\t\t${xmlString(agent.environment[name] ?? '')}`,
  );
  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">',
    '<plist version="1.0">',
    '<dict>',
    '\t<key>Label</key>',
    `\t${xmlString(agent.label)}`,
    '\t<key>ProgramArguments</key>',
    '\t<array>',
    `\t\t${xmlString(agent.node)}`,
    `\t\t${xmlString(agent.entry)}`,
    `\t\t${xmlString('--restartable')}`,
    '\t</array>',
    '\t<key>WorkingDirectory</key>',
    `\t${xmlString(agent.workingDirectory)}`,
    '\t<key>EnvironmentVariables</key>',
    '\t<dict>',
    ...environment,
    '\t</dict>',
    '\t<key>RunAtLoad</key>',
    '\t<true/>',
    '\t<key>KeepAlive</key>',
    '\t<dict>',
    '\t\t<key>SuccessfulExit</key>',
    '\t\t<false/>',
    '\t</dict>',
    '\t<key>ThrottleInterval</key>',
    `\t<integer>${THROTTLE_SECONDS}</integer>`,
    '\t<key>ExitTimeOut</key>',
    `\t<integer>${EXIT_TIMEOUT_SECONDS}</integer>`,
    '\t<key>StandardOutPath</key>',
    `\t${xmlString(agent.logFile)}`,
    '\t<key>StandardErrorPath</key>',
    `\t${xmlString(agent.logFile)}`,
    '</dict>',
    '</plist>',
    '',
  ].join('\n');
}

/** Where launchd reads a person's own agents at login. */
export function launchAgentsFolder(home: string = homedir()): string {
  return join(home, 'Library', 'LaunchAgents');
}

/** What On and Off need from the Mac, given so a test can stand in for launchctl. */
export interface LoginIo {
  readonly print: (line: string) => void;
  /** Runs launchctl with these arguments; its exit status and its output. */
  readonly launchctl: (args: readonly string[]) => {
    readonly status: number;
    readonly output: string;
  };
  /** Whether Halcyonic's health check answers on this loopback port. */
  readonly answers: (port: number) => Promise<boolean>;
  readonly uid: number;
  readonly sleep: (ms: number) => Promise<void>;
}

export function localLoginIo(print: (line: string) => void): LoginIo {
  return {
    print,
    launchctl: (args) => {
      const run = spawnSync('/bin/launchctl', [...args], { encoding: 'utf8' });
      return { status: run.status ?? 1, output: `${run.stdout ?? ''}${run.stderr ?? ''}`.trim() };
    },
    answers: async (port) => {
      try {
        const response = await fetch(`http://127.0.0.1:${port}/api/health`, {
          signal: AbortSignal.timeout(1500),
        });
        return response.ok;
      } catch {
        return false;
      }
    },
    uid: process.getuid?.() ?? 0,
    sleep: (ms) => new Promise((done) => setTimeout(done, ms)),
  };
}

/** Writes the file whole or not at all: a new partial file beside it, then a rename over it. */
function writeAgentFile(file: string, text: string): void {
  mkdirSync(dirname(file), { recursive: true });
  const partial = `${file}.${process.pid}.partial`;
  writeFileSync(partial, text, { mode: 0o644, flag: 'wx' });
  renameSync(partial, file);
}

/** Turns the agent on in `folder`; the plane answers on `port` once launchd has started it. */
export async function turnOn(
  agent: LoginAgent,
  port: number,
  folder: string,
  io: LoginIo,
): Promise<number> {
  if (port < 1) {
    io.print('The agent needs a port of its own; 0 lets the system pick one nobody could find.');
    return 2;
  }
  const domain = `gui/${io.uid}`;
  if (io.launchctl(['print', `${domain}/${agent.label}`]).status === 0) {
    io.print(
      'Halcyonic already starts when you log in. To change how, turn it off, then on again.',
    );
    return 1;
  }
  if (await io.answers(port)) {
    io.print(
      `A Halcyonic you started yourself answers on port ${port}. Stop it first (Ctrl-C in its window), then turn this on, so the two never take the same port.`,
    );
    return 1;
  }
  const file = join(folder, `${agent.label}.plist`);
  const text = loginAgentPlist(agent);
  mkdirSync(agent.dataDir, { recursive: true, mode: 0o700 });
  writeAgentFile(file, text);
  const started = io.launchctl(['bootstrap', domain, file]);
  if (started.status !== 0) {
    rmSync(file, { force: true });
    io.print(
      `launchd did not take the agent (launchctl exit ${started.status}): ${started.output}. Its file is removed, so nothing starts at the next login.`,
    );
    return 1;
  }
  io.print(`Halcyonic now starts when you log in, with ${agent.node}. Its log: ${agent.logFile}.`);
  if (agent.leftOut.length > 0) {
    io.print(
      `Set here but not carried by the agent: ${agent.leftOut.join(', ')}. Halcyonic's own settings file still applies.`,
    );
  }
  for (let waited = 0; waited < STARTED_WITHIN_MS; waited += 1000) {
    if (await io.answers(port)) {
      io.print(`It is running on port ${port}.`);
      return 0;
    }
    await io.sleep(1000);
  }
  io.print(`Nothing answers on port ${port} yet. Its log says why: ${agent.logFile}.`);
  return 1;
}

/** Turns the agent off: launchd stops the plane, and the file is removed. */
export function turnOff(label: string, folder: string, io: LoginIo): number {
  if (!LABEL.test(label)) throw new Error(`the label ${label} is not a launchd label`);
  const stopped = io.launchctl(['bootout', `gui/${io.uid}/${label}`]);
  const file = join(folder, `${label}.plist`);
  const link = existsSync(file) && lstatSync(file).isSymbolicLink();
  const had = existsSync(file) && !link;
  if (had) rmSync(file);
  io.print(
    stopped.status === 0
      ? 'Halcyonic is stopped and no longer starts when you log in.'
      : had
        ? 'Halcyonic no longer starts when you log in. It was not running under launchd.'
        : 'Halcyonic was not set to start when you log in.',
  );
  if (stopped.status !== 0 && had) {
    io.print(`launchctl said (exit ${stopped.status}): ${stopped.output}`);
  }
  if (link) io.print(`${file} is a link, not the agent's file; it was left alone.`);
  return 0;
}

/** `pnpm mac-setup login on|off`: the person's own LaunchAgents folder and the default label. */
export function runLogin(
  state: 'on' | 'off',
  env: NodeJS.ProcessEnv,
  home: string,
  io: LoginIo,
): Promise<number> | number {
  const folder = launchAgentsFolder(home);
  if (state === 'off') return turnOff(LOGIN_AGENT_LABEL, folder, io);
  const port = loopbackListener(env).port;
  const agent = loginAgentFor({
    checkout: checkoutOf(),
    node: nodeFor(env.PATH, process.execPath, sameExecutable),
    env,
    settings: { HALCYONIC_PORT: env.HALCYONIC_PORT, HALCYONIC_DATA_DIR: env.HALCYONIC_DATA_DIR },
  });
  return turnOn(agent, port, folder, io);
}

const USAGE =
  'usage: node apps/control-plane/src/cli/login-agent.ts on|off <folder> [--label <label>] [--port <port>] [--data-dir <folder>]';

/** The command's words, strictly: every flag known, given once, with a value. */
export function parseLoginArgs(args: readonly string[]): {
  readonly state: 'on' | 'off';
  readonly folder: string;
  readonly flags: ReadonlyMap<string, string>;
} | null {
  const [state, folder, ...rest] = args;
  if ((state !== 'on' && state !== 'off') || folder === undefined || folder.startsWith('--'))
    return null;
  const flags = new Map<string, string>();
  for (let at = 0; at < rest.length; at += 2) {
    const name = rest[at];
    const value = rest[at + 1];
    if (name === undefined || !['--label', '--port', '--data-dir'].includes(name)) return null;
    if (value === undefined || value.startsWith('--') || flags.has(name)) return null;
    flags.set(name, value);
  }
  return { state, folder, flags };
}

export async function main(
  args: readonly string[],
  env: NodeJS.ProcessEnv,
  io: LoginIo,
): Promise<number> {
  const parsed = parseLoginArgs(args);
  if (parsed === null) {
    io.print(USAGE);
    return 2;
  }
  const folder = resolve(parsed.folder);
  const label = parsed.flags.get('--label') ?? LOGIN_AGENT_LABEL;
  if (parsed.state === 'off') return turnOff(label, folder, io);
  const dataDir = parsed.flags.get('--data-dir');
  const settings = {
    HALCYONIC_PORT: parsed.flags.get('--port') ?? env.HALCYONIC_PORT,
    HALCYONIC_DATA_DIR: dataDir === undefined ? env.HALCYONIC_DATA_DIR : resolve(dataDir),
  };
  const port = loopbackListener({ HALCYONIC_PORT: settings.HALCYONIC_PORT }).port;
  const agent = loginAgentFor({
    checkout: checkoutOf(),
    node: nodeFor(env.PATH, process.execPath, sameExecutable),
    env,
    label,
    settings,
  });
  return turnOn(agent, port, folder, io);
}

if (import.meta.main) {
  const io = localLoginIo((line) => process.stdout.write(`${line}\n`));
  main(process.argv.slice(2), process.env, io)
    .then((status) => process.exit(status))
    .catch((error: unknown) => {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
      process.exit(1);
    });
}
