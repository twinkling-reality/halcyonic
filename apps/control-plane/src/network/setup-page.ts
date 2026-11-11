import { spawn } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  CHECKED_MODELS,
  COMPANION_MODEL,
  type MacSetupIo,
  macSetupSteps,
  runMacSetup,
  type SetupStep,
} from '../cli/mac-setup.ts';
import { matchesPin, type Pins } from '../pins.ts';
import { plainLine } from '../preview/check-output.ts';
import { VR_STARTER } from '../starters.ts';
import type { PageRoutes } from './pairing-page.ts';

/**
 * The setup page (ADR 0053): a Setup view beside pairing on the pairing page's own server, so a
 * person who was handed a running copy sets up the Mac without a terminal. It shows every step
 * `pnpm mac-setup` checks, with a press for each action the terminal offers, run exactly as the
 * terminal runs it; downloads say first what they fetch, run one at a time with their progress and a
 * stop, and never start on their own. A changed setting takes effect at the next start, which the
 * page offers as Restart to Apply. It never sets the paid runtime switches, which the settings file
 * refuses anyway.
 */

const TRACKED_STARTERS = fileURLToPath(new URL('../../../../starters', import.meta.url));

/**
 * The exit code with which the control plane asks its launcher to start it again, after the setup
 * page's Restart to Apply: `Start Halcyonic.command` starts it again on this code and on no other.
 */
export const RESTART_EXIT_CODE = 75;

/** Lines of a download's output kept for the page. */
const JOB_LINES = 12;
/**
 * How long the steps as last checked stand. A check hashes the pinned files and asks the firewall,
 * so the page's poll reads the last check; a press, a download's end and Check Again check again.
 */
const STEPS_STAND_MS = 5 * 60 * 1000;
const JOB_LINE_CHARS = 200;

/** A download: what it fetches, said before the press, and how it runs. */
export interface SetupDownload {
  readonly id: string;
  readonly title: string;
  /** What, which version, how large, from where, in the page's words. */
  readonly fetches: string;
  /** The commands, each an exact argument list, run with no shell, one after another. */
  readonly commands: readonly (readonly string[])[];
  /** The setup action that records it once downloaded, as `pnpm mac-setup` takes it. */
  readonly record: readonly string[];
  /** Work before the commands, such as copying the starter's lockfile into place. */
  readonly prepare?: () => void;
  /**
   * Whether what it installed is the copy Halcyonic was checked with: true only on a pin match, null
   * where Halcyonic has no pin to check it by. Without it, the record's own exit says.
   */
  readonly verify?: () => Promise<boolean | null>;
}

/** A running command, as the page's runner starts it. */
export interface RunningCommand {
  readonly done: Promise<number>;
  stop(): void;
}

export type CommandRunner = (
  argv: readonly string[],
  options: { readonly cwd: string; readonly onLine: (line: string) => void },
) => RunningCommand;

export interface SetupPageDeps {
  readonly dataDir: string;
  readonly home: string;
  /** The Mac as setup sees it, printing to `print`. */
  readonly io: (print: (line: string) => void) => MacSetupIo;
  /** Runs a download's command; faked in tests, so nothing is downloaded there. */
  readonly run: CommandRunner;
  /**
   * The settings that take effect only at a start, as the running control plane started with them,
   * and as they are now: pairing's own setting applies live, so it is left out of both.
   */
  readonly appliedAtStart: string;
  readonly appliedNow: () => string;
  /** The tasks a restart would pause now, by their names. */
  readonly tasksThatPause: () => readonly string[];
  /**
   * Whether a launcher starts the control plane again when it closes for a restart: true only when
   * Start Halcyonic.command started it (`--restartable`).
   */
  readonly canRestart: boolean;
  /** Closes the control plane cleanly and has the launcher start it again. */
  readonly restart: () => void;
}

/** What the page shows. */
export interface SetupState {
  readonly steps: readonly {
    readonly id: string;
    readonly title: string;
    readonly status: SetupStep['status'];
    readonly lines: readonly string[];
    readonly actions: readonly SetupActionView[];
  }[];
  readonly job: {
    readonly id: string;
    readonly title: string;
    /** `not_set_up`: downloaded, but setup did not record it (a pin that does not match, say). */
    readonly state: 'running' | 'done' | 'not_set_up' | 'failed' | 'stopped';
    readonly lines: readonly string[];
    readonly seconds: number;
  } | null;
  /** The settings differ from those the control plane started with. */
  readonly restart_needed: boolean;
  /** The page can restart Halcyonic itself; otherwise the person restarts it. */
  readonly can_restart: boolean;
  /** Set when a press needs a second press, with what it will do. */
  readonly confirm: {
    readonly action: string;
    readonly arg: string | null;
    readonly text: string;
  } | null;
  /** What the last action printed, as setup prints it. */
  readonly said: readonly string[];
  readonly restarting: boolean;
}

export interface SetupActionView {
  readonly action: string;
  readonly arg: string | null;
  readonly label: string;
  /** For a download: what it fetches, said before the press. */
  readonly fetches?: string;
}

/** The actions each step offers, as `pnpm mac-setup` subcommands; downloads by their ids. */
const STEP_ACTIONS: Readonly<Record<string, readonly SetupActionView[]>> = {
  folders: [{ action: 'allow', arg: null, label: 'Allow ~/HalcyonicProjects' }],
  agent_apps: [
    { action: 'download', arg: 'opencode', label: 'Install OpenCode' },
    { action: 'download', arg: 'codex', label: 'Install Codex' },
    { action: 'agent_apps', arg: null, label: 'Check Agent Apps' },
  ],
  models: CHECKED_MODELS.map((model) => ({
    action: 'download',
    arg: `model:${model.name}`,
    label: `Get ${model.name}`,
  })),
  cost: CHECKED_MODELS.map((model) => ({
    action: 'local_model',
    arg: model.name,
    label: `Use ${model.name}`,
  })),
  voice: [{ action: 'voice', arg: null, label: 'Check Voice Files' }],
  launcher: [
    { action: 'browser', arg: null, label: 'Set Up Browser' },
    { action: 'launcher', arg: null, label: 'Build Launcher' },
  ],
  starter: [
    { action: 'download', arg: 'starter', label: 'Install VR Starter' },
    { action: 'starter', arg: null, label: 'Check VR Starter' },
  ],
  undo: [
    { action: 'undo', arg: null, label: 'Turn Undo On' },
    { action: 'undo', arg: 'off', label: 'Turn Undo Off' },
  ],
  companion: [
    {
      action: 'download',
      arg: `model:${COMPANION_MODEL.name}`,
      label: `Get ${COMPANION_MODEL.name}`,
    },
    { action: 'companion', arg: COMPANION_MODEL.name, label: 'Turn Companion On' },
    { action: 'companion', arg: 'off', label: 'Turn Companion Off' },
  ],
};

/** The setup actions a press may run, each as `pnpm mac-setup` takes it; nothing else. */
const ACTIONS: Readonly<Record<string, (arg: string | null) => readonly string[] | null>> = {
  allow: (arg) => (arg === null ? ['allow', '--yes'] : null),
  agent_apps: (arg) => (arg === null ? ['agent-apps'] : null),
  local_model: (arg) =>
    arg !== null && CHECKED_MODELS.some((model) => model.name === arg)
      ? ['local-model', arg]
      : null,
  voice: (arg) => (arg === null ? ['voice'] : null),
  browser: (arg) => (arg === null ? ['browser'] : null),
  launcher: (arg) => (arg === null ? ['launcher'] : null),
  starter: (arg) => (arg === null ? ['starter'] : null),
  undo: (arg) => (arg === null ? ['undo'] : arg === 'off' ? ['undo', 'off'] : null),
  companion: (arg) => (arg === COMPANION_MODEL.name || arg === 'off' ? ['companion', arg] : null),
};

/** The downloads the page can run, each exactly what `pnpm mac-setup` prints for it today. */
export function setupDownloads(
  dataDir: string,
  which: (command: string) => string | null,
  pins: Pins | null,
): SetupDownload[] {
  const pinned = (app: 'opencode' | 'codex') => () =>
    pins === null ? Promise.resolve(null) : matchesPin(join(dataDir, pins[app].path), pins[app]);
  const npm = which('npm') ?? 'npm';
  const ollama = which('ollama') ?? 'ollama';
  const starter = join(dataDir, 'starters', VR_STARTER);
  const models = [
    ...CHECKED_MODELS.map((m) => ({ name: m.name, size: `${(m.bytes / 1e9).toFixed(1)} GB` })),
    { name: COMPANION_MODEL.name, size: COMPANION_MODEL.download },
  ];
  return [
    {
      id: 'opencode',
      title: 'Install OpenCode',
      fetches:
        'OpenCode 2.0.18 (@opencode/cli), about 171 MB on disk, from the public npm registry, with no install scripts.',
      commands: [
        [
          npm,
          'install',
          '--prefix',
          join(dataDir, 'runtimes', 'opencode-2.0.18'),
          '@opencode/cli@2.0.18',
          '--ignore-scripts',
        ],
      ],
      record: ['agent-apps'],
      verify: pinned('opencode'),
    },
    {
      id: 'codex',
      title: 'Install Codex',
      fetches:
        'Codex 0.157.0 (@openai/codex), about 314 MB on disk, from the public npm registry, with no install scripts.',
      commands: [
        [
          npm,
          'install',
          '--prefix',
          join(dataDir, 'runtimes', 'codex-0.157.0'),
          '@openai/codex@0.157.0',
          '--ignore-scripts',
        ],
      ],
      record: ['agent-apps'],
      verify: pinned('codex'),
    },
    {
      id: 'starter',
      title: 'Install VR Starter',
      fetches:
        "The VR starter's 80 pinned packages, about 240 MB, from the public npm registry, with no install scripts and none of your own npm settings.",
      prepare: () => {
        mkdirSync(starter, { recursive: true });
        for (const name of ['package.json', 'package-lock.json']) {
          copyFileSync(join(TRACKED_STARTERS, VR_STARTER, name), join(starter, name));
        }
      },
      commands: [
        [
          npm,
          'ci',
          '--prefix',
          starter,
          '--ignore-scripts',
          '--no-audit',
          '--no-fund',
          '--userconfig',
          '/dev/null',
          '--globalconfig',
          '/dev/null',
          '--cache',
          '<fresh cache>',
          '--registry',
          'https://registry.npmjs.org/',
        ],
      ],
      record: ['starter'],
    },
    ...models.map((model) => ({
      id: `model:${model.name}`,
      title: `Get ${model.name}`,
      // By name and tag only: Halcyonic has no digest pinned for a model (mac-host-setup.md).
      fetches: `The model ${model.name}, ${model.size}, into Ollama on this Mac, from Ollama's own library, by its name: Halcyonic doesn't check which build of it arrives.`,
      commands: [[ollama, 'pull', model.name]],
      record: [],
    })),
  ];
}

/** A download under way or ended. */
interface SetupJob {
  readonly id: string;
  readonly title: string;
  state: 'running' | 'done' | 'not_set_up' | 'failed' | 'stopped';
  readonly lines: string[];
  readonly started: number;
  ended: number | null;
  running: RunningCommand | null;
  stopped: boolean;
}

/** What one session of the page has pressed and been told. */
interface Press {
  confirm: SetupState['confirm'];
  said: string[];
}

/** The most sessions whose presses are kept; the pairing page keeps fewer sessions than this. */
const MAX_PRESSES = 16;

/** The session key for a caller with no page session, as the tests drive the view. */
const ONE_SESSION = 'one';

/** The setup view: its state, its presses, and one download at a time. */
export class SetupView {
  readonly #deps: SetupPageDeps;
  readonly #downloads: Map<string, SetupDownload>;
  #job: SetupJob | null = null;
  /** Each session's pending press and last words, so a press is confirmed only where it was made. */
  readonly #presses = new Map<string, Press>();
  #restarting = false;
  #steps: { readonly at: number; readonly value: Promise<SetupStep[]> } | null = null;

  constructor(deps: SetupPageDeps, downloads: readonly SetupDownload[]) {
    this.#deps = deps;
    this.#downloads = new Map(downloads.map((download) => [download.id, download]));
  }

  /** The steps as last checked, checked again when they are old or `fresh` asks. */
  #checked(fresh = false): Promise<SetupStep[]> {
    const now = Date.now();
    if (fresh || this.#steps === null || now - this.#steps.at > STEPS_STAND_MS) {
      const value = macSetupSteps(this.#deps.io(() => undefined));
      this.#steps = { at: now, value };
      // A check that fails is checked again at the next read rather than kept.
      value.catch(() => {
        if (this.#steps?.value === value) this.#steps = null;
      });
    }
    return this.#steps.value;
  }

  /** A session's presses, made on its first use; the oldest session's are let go past the bound. */
  #press(session: string): Press {
    let press = this.#presses.get(session);
    if (press === undefined) {
      press = { confirm: null, said: [] };
      this.#presses.set(session, press);
      while (this.#presses.size > MAX_PRESSES) {
        const oldest = this.#presses.keys().next().value;
        if (oldest === undefined) break;
        this.#presses.delete(oldest);
      }
    }
    return press;
  }

  /** What the page shows to one session: the shared steps and download, its own pending press and words. */
  async state(session: string = ONE_SESSION): Promise<SetupState> {
    const here = this.#press(session);
    const steps = await this.#checked();
    const job = this.#job;
    return {
      steps: steps.map((step) => ({
        id: step.id,
        title: step.title,
        status: step.status,
        lines: step.lines.map((line) => this.#tilde(line)),
        actions: (STEP_ACTIONS[step.id] ?? []).map((view) => {
          const download =
            view.action === 'download' ? this.#downloads.get(view.arg ?? '') : undefined;
          return download === undefined ? view : { ...view, fetches: download.fetches };
        }),
      })),
      job:
        job === null
          ? null
          : {
              id: job.id,
              title: job.title,
              state: job.state,
              lines: job.lines,
              seconds: Math.round(((job.ended ?? Date.now()) - job.started) / 1000),
            },
      restart_needed: this.#deps.appliedNow() !== this.#deps.appliedAtStart,
      can_restart: this.#deps.canRestart,
      confirm: here.confirm,
      said: here.said,
      restarting: this.#restarting,
    };
  }

  /**
   * A press. The first asks for a second with what it will do; only the second, `confirmed`, acts.
   * Anything not offered is refused in words.
   */
  async act(
    action: unknown,
    arg: unknown,
    confirmed: unknown,
    session: string = ONE_SESSION,
  ): Promise<SetupState> {
    const here = this.#press(session);
    const given = typeof arg === 'string' ? arg : null;
    if (typeof action !== 'string')
      return this.#refused(here, session, 'That press is not one this page offers.');
    // A second press acts only on what the page last said it would do; any other is a first press.
    const pending = here.confirm;
    const second =
      confirmed === true && pending !== null && pending.action === action && pending.arg === given;
    if (action === 'check') {
      here.confirm = null;
      here.said = [];
      await this.#checked(true);
      return this.state(session);
    }
    if (action === 'restart') return this.#restart(here, session, second);
    if (action === 'download') return this.#download(here, session, given, second);
    const args = ACTIONS[action]?.(given) ?? null;
    if (args === null)
      return this.#refused(here, session, 'That press is not one this page offers.');
    if (!second) {
      here.confirm = {
        action,
        arg: given,
        text: `This runs "pnpm mac-setup ${args.join(' ')}" on this Mac. Go On runs it.`,
      };
      return this.state(session);
    }
    here.confirm = null;
    const printed: string[] = [];
    try {
      await runMacSetup(
        args,
        this.#deps.io((line) => printed.push(this.#tilde(line))),
      );
    } catch (error) {
      printed.push(this.#tilde(error instanceof Error ? error.message : String(error)));
    }
    here.said = printed.filter((line) => line.trim() !== '');
    await this.#checked(true);
    return this.state(session);
  }

  /** Stops the running download: its whole process group. */
  stop(session: string = ONE_SESSION): Promise<SetupState> {
    const job = this.#job;
    if (job !== null && job.state === 'running') {
      job.stopped = true;
      job.running?.stop();
    }
    return this.state(session);
  }

  async #download(
    here: Press,
    session: string,
    id: string | null,
    confirmed: boolean,
  ): Promise<SetupState> {
    const download = id === null ? undefined : this.#downloads.get(id);
    if (download === undefined)
      return this.#refused(here, session, 'That download is not one this page offers.');
    if (this.#job?.state === 'running')
      return this.#refused(here, session, 'Another download is running. Wait for it, or stop it.');
    if (!confirmed) {
      here.confirm = {
        action: 'download',
        arg: download.id,
        text: `${download.fetches} Go On downloads it.`,
      };
      return this.state(session);
    }
    here.confirm = null;
    const job: SetupJob = {
      id: download.id,
      title: download.title,
      state: 'running',
      lines: [],
      started: Date.now(),
      ended: null,
      running: null,
      stopped: false,
    };
    this.#job = job;
    void this.#runDownload(download, job);
    return this.state(session);
  }

  async #runDownload(download: SetupDownload, job: SetupJob): Promise<void> {
    const add = (line: string) => {
      // As a check's log is shown: no terminal sequences or unseen characters, tabs as spaces.
      const clean = this.#tilde(plainLine(line).replaceAll('\t', ' ')).slice(0, JOB_LINE_CHARS);
      if (clean.trim() === '') return;
      job.lines.push(clean);
      if (job.lines.length > JOB_LINES) job.lines.shift();
    };
    let cache: string | null = null;
    try {
      download.prepare?.();
      for (const command of download.commands) {
        if (job.stopped) break;
        const argv = command.map((part) => {
          if (part !== '<fresh cache>') return part;
          cache ??= mkdtempSync(join(tmpdir(), 'halcyonic-npm-cache-'));
          return cache;
        });
        const running = this.#deps.run(argv, { cwd: this.#deps.dataDir, onLine: add });
        job.running = running;
        const code = await running.done;
        job.running = null;
        if (code !== 0) {
          job.state = job.stopped ? 'stopped' : 'failed';
          job.ended = Date.now();
          return;
        }
      }
      if (job.stopped) {
        job.state = 'stopped';
        job.ended = Date.now();
        return;
      }
      // Recorded only by setup's own action, which checks the pins; a copy that does not match is
      // left out of the settings, setup's words say why, and the job says Not Set Up.
      const recorded =
        download.record.length === 0
          ? 0
          : await runMacSetup(
              download.record,
              this.#deps.io((line) => add(line)),
            );
      const verified = download.verify === undefined ? true : (await download.verify()) === true;
      job.state = recorded === 0 && verified ? 'done' : 'not_set_up';
    } catch (error) {
      add(error instanceof Error ? error.message : String(error));
      job.state = 'failed';
    } finally {
      job.ended ??= Date.now();
      if (cache !== null) rmSync(cache, { recursive: true, force: true });
      void this.#checked(true).catch(() => undefined);
    }
  }

  async #restart(here: Press, session: string, confirmed: boolean): Promise<SetupState> {
    if (!this.#deps.canRestart) {
      return this.#refused(
        here,
        session,
        'Halcyonic was not started from Start Halcyonic, so it cannot start itself again. Stop it and start it again to apply the settings.',
      );
    }
    if (this.#restarting) return this.state(session);
    if (!confirmed) {
      const tasks = this.#deps.tasksThatPause();
      here.confirm = {
        action: 'restart',
        arg: null,
        text:
          tasks.length === 0
            ? 'Halcyonic closes and starts again, with the new settings. No task is running. Go On restarts it.'
            : `Halcyonic closes and starts again, with the new settings. These tasks stop where they are and come back after the restart: ${tasks.join(', ')}. Go On restarts it.`,
      };
      return this.state(session);
    }
    here.confirm = null;
    this.#restarting = true;
    const state = await this.state(session);
    setImmediate(() => this.#deps.restart());
    return state;
  }

  async #refused(here: Press, session: string, text: string): Promise<SetupState> {
    here.confirm = null;
    here.said = [text];
    return this.state(session);
  }

  /** The home folder as ~, as setup says it. */
  #tilde(line: string): string {
    return this.#deps.home === '' ? line : line.split(this.#deps.home).join('~');
  }
}

/** The page's own files. */
function asset(name: string): Buffer {
  return readFileSync(new URL(`./setup-page/${name}`, import.meta.url));
}

/** The settings that take effect at a start, as one comparable text: all but pairing's, which applies live. */
export function settingsForRestart(values: Readonly<Record<string, string | undefined>>): string {
  return JSON.stringify(
    Object.entries(values)
      .filter(([name]) => name !== 'HALCYONIC_NETWORK_HOST')
      .sort(([a], [b]) => (a < b ? -1 : 1)),
  );
}

/** Adds the setup view to the pairing page's server (ADR 0053). */
export function mountSetup(view: SetupView): (routes: PageRoutes) => void {
  return (routes) => {
    routes.asset('/setup', 'text/html; charset=utf-8', asset('setup.html'));
    routes.asset('/setup.js', 'text/javascript; charset=utf-8', asset('setup.js'));
    routes.asset('/setup.css', 'text/css; charset=utf-8', asset('setup.css'));
    routes.get('/setup/state', (_request, session) => view.state(session));
    routes.post('/setup/act', (request, session) => {
      const body = (request.body ?? {}) as { action?: unknown; arg?: unknown; confirmed?: unknown };
      return view.act(body.action, body.arg, body.confirmed, session);
    });
    routes.post('/setup/stop', (_request, session) => view.stop(session));
  };
}

/** Runs a download's command in a process group of its own, with a small environment, no shell. */
export const runCommand: CommandRunner = (argv, options) => {
  const [file, ...args] = argv as [string, ...string[]];
  const child = spawn(file, args, {
    cwd: options.cwd,
    detached: true,
    stdio: ['ignore', 'pipe', 'pipe'],
    // Nothing of the control plane's own environment (its keys among it) reaches a download.
    env: {
      PATH: process.env.PATH ?? '/usr/bin:/bin',
      HOME: process.env.HOME ?? '',
      TMPDIR: process.env.TMPDIR ?? tmpdir(),
      LANG: process.env.LANG ?? 'en_US.UTF-8',
    },
  });
  const split = (stream: NodeJS.ReadableStream | null) => {
    let rest = '';
    stream?.on('data', (chunk: Buffer) => {
      const text = rest + chunk.toString('utf8');
      const parts = text.split(/\r?\n|\r/);
      rest = parts.pop() ?? '';
      for (const part of parts) options.onLine(part);
    });
    stream?.on('end', () => {
      if (rest !== '') options.onLine(rest);
    });
  };
  split(child.stdout);
  split(child.stderr);
  const done = new Promise<number>((resolve) => {
    child.on('error', () => resolve(-1));
    child.on('close', (code) => resolve(code ?? -1));
  });
  return {
    done,
    stop: () => {
      if (child.pid !== undefined) {
        try {
          process.kill(-child.pid, 'SIGTERM');
        } catch {
          // Already gone.
        }
      }
    },
  };
};
