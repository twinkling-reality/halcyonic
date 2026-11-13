import { spawn } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  COMMAND_SCHEMA_VERSION,
  type CommandEnvelope,
  type CommandId,
  type Principal,
  type ProjectId,
  type ReceivedVia,
  type SecretName,
  type Timestamp,
} from '@halcyonic/contracts';
import {
  CHECKED_MODELS,
  COMPANION_MODEL,
  type MacSetupIo,
  macSetupSteps,
  runMacSetup,
  type SetupStep,
  tunnelProgram,
} from '../cli/mac-setup.ts';
import { type KeyView, LONGEST_KEY } from '../keys/provider-keys.ts';
import { matchesPin, type Pins } from '../pins.ts';
import { plainLine } from '../preview/check-output.ts';
import { isPinnedTunnel, TUNNEL_PIN } from '../preview/share.ts';
import { submitSecretSet } from '../secrets/set.ts';
import { LONGEST_VALUE, nameProblem, type SecretStore } from '../secrets/store.ts';
import { VR_STARTER } from '../starters.ts';
import type { PageRoutes } from './pairing-page.ts';

/**
 * The setup page (ADR 0053): a Setup view beside pairing on the pairing page's own server, so a
 * person who was handed a running copy sets up the Mac without a terminal. It shows every step
 * `pnpm mac-setup` checks, with a press for each action the terminal offers, run exactly as the
 * terminal runs it; downloads say first what they fetch, run one at a time with their progress and a
 * stop, and never start on their own. A changed setting takes effect at the next start, which the
 * page offers as Restart to Apply. It never sets the paid runtime switches, which the settings file
 * refuses anyway; only the person's own key, added under Agent apps for an app that declares one, turns
 * that app on at the next start (ADR 0053, amendment of 2026-11-12).
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
/** The most a secret's set may send: the longest value, escaped as JSON escapes it, and its names. */
const SECRET_BODY_LIMIT = LONGEST_VALUE * 6 + 1024;

/** A key's body: the longest key, escaped at worst, and its agent app's id. */
const KEY_BODY_LIMIT = LONGEST_KEY * 6 + 1024;

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
  /** Each game shared now, link or Wi-Fi (ADR 0054); none when sharing is off. */
  readonly shared?: () => readonly SharedNow[];
  /**
   * Stops a project's share by the person's press: `stopped` once the stop is accepted, `not_shared`
   * when it is not shared, `refused` when the stop was not accepted (the share ending just then, say).
   */
  readonly stopShare?: (projectId: string) => 'stopped' | 'not_shared' | 'refused';
  /** Each project's secrets by name, with Set and Remove (ADR 0057); absent where none are kept. */
  readonly secrets?: PageSecrets;
  /** The person's own keys for the declared providers something here reads (ADR 0061); absent where none is. */
  readonly keys?: PageKeys;
  /** The data folder, as the person reads it, where it is not the usual one; absent or null where it is. */
  readonly dataFolder?: string | null;
}

/** The agent apps' keys as the page reaches them: whether each is stored and when, never a key (AgentKeys). */
export interface PageKeys {
  list(): readonly KeyView[];
  /** The refusal's words, or null once kept. */
  store(id: unknown, value: unknown): string | null;
  /** The refusal's words, or null once removed. */
  remove(id: unknown): string | null;
}

/** A project as the page's secrets section lists it: its name and its secrets' names, never a value. */
export interface SecretsProject {
  readonly project_id: string;
  readonly name: string;
  readonly secrets: readonly string[];
}

/** The page's secrets (ADR 0057): what each set or remove says when it is refused, or null. */
export interface PageSecrets {
  readonly projects: () => readonly SecretsProject[];
  /** The agents here, by their own names, that do not declare `secret_environment`: they start without. */
  readonly without: () => readonly string[];
  /**
   * The agents here, by their own names, that can run outside Halcyonic's sandbox (Claude Code on a
   * folder the person connected), so their commands can read every project's secrets here (AUDIT3).
   */
  readonly outside: () => readonly string[];
  readonly set: (projectId: unknown, name: unknown, value: unknown) => string | null;
  readonly remove: (projectId: unknown, name: unknown) => string | null;
}

/**
 * The person's Set and Remove from the page (ADR 0057), submitted as what they are: a local press
 * over HTTP on this Mac. A set stages the value in the store under the command's id, then submits
 * the command with the name alone; a set refused or failed leaves nothing kept.
 */
export function pageSecrets(deps: {
  readonly submit: (
    command: CommandEnvelope,
    via: ReceivedVia,
    principal: Principal | null,
  ) => {
    readonly disposition: string;
    readonly command: {
      readonly status: string;
      readonly rejection?: { readonly message: string } | null;
      readonly failure?: { readonly message: string } | null;
    } | null;
  };
  readonly store: SecretStore;
  readonly projects: () => readonly SecretsProject[];
  readonly without: () => readonly string[];
  readonly outside: () => readonly string[];
  readonly nextId: () => string;
  readonly now: () => Date;
}): PageSecrets {
  /** What a submit's answer says to the person: its refusal or failure, or nothing once done. */
  const said = (sent: ReturnType<typeof deps.submit>): string | null => {
    if (sent.disposition !== 'accepted')
      return sent.command?.rejection?.message ?? "That couldn't be done. Try again.";
    if (sent.command?.status === 'failed')
      return sent.command.failure?.message ?? "That couldn't be done. Try again.";
    return null;
  };
  const known = (projectId: unknown): projectId is ProjectId =>
    typeof projectId === 'string' &&
    deps.projects().some((project) => project.project_id === projectId);
  const send = (
    commandId: string,
    command:
      | {
          readonly command_type: 'project.secret.set';
          readonly payload: { project_id: ProjectId; name: SecretName };
        }
      | {
          readonly command_type: 'project.secret.remove';
          readonly payload: { project_id: ProjectId; name: SecretName };
        },
  ): string | null => {
    const sent = deps.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId as CommandId,
        issued_at: deps.now().toISOString() as Timestamp,
        client: { name: 'halcyonic-setup-page', version: null, device_label: null },
        ...command,
      } as CommandEnvelope,
      'http',
      { kind: 'local' },
    );
    return said(sent);
  };
  return {
    projects: deps.projects,
    without: deps.without,
    outside: deps.outside,
    set: (projectId, name, value) => {
      if (!known(projectId)) return 'That project is not on this Mac.';
      const named = deps.store.nameProblem(name);
      if (named !== null) return named;
      const set = submitSecretSet(
        {
          store: deps.store,
          submit: (command, via, principal) => deps.submit(command, via, principal),
        },
        {
          schema_version: COMMAND_SCHEMA_VERSION,
          command_id: deps.nextId() as CommandId,
          issued_at: deps.now().toISOString() as Timestamp,
          client: { name: 'halcyonic-setup-page', version: null, device_label: null },
          command_type: 'project.secret.set',
          payload: { project_id: projectId, name: name as SecretName },
        },
        value,
        'http',
        { kind: 'local' },
      );
      if (!set.ok) return set.problem;
      return said(set.outcome);
    },
    remove: (projectId, name) => {
      if (!known(projectId)) return 'That project is not on this Mac.';
      const problem = nameProblem(name);
      if (problem !== null) return 'That project has no secret by that name.';
      return send(deps.nextId(), {
        command_type: 'project.secret.remove',
        payload: { project_id: projectId, name: name as SecretName },
      });
    },
  };
}

/**
 * The person's Stop from the page, submitted as what it is: a local press over HTTP on this Mac,
 * never a host-made stop (AUDIT2 S1). `stopped` once accepted.
 */
export function pageStopShare(deps: {
  readonly submit: (
    command: CommandEnvelope,
    via: ReceivedVia,
    principal: Principal | null,
  ) => { readonly disposition: string };
  readonly isShared: (projectId: string) => boolean;
  readonly nextId: () => string;
  readonly now: () => Date;
}): (projectId: string) => 'stopped' | 'not_shared' | 'refused' {
  return (projectId) => {
    if (!deps.isShared(projectId)) return 'not_shared';
    const sent = deps.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: deps.nextId() as CommandId,
        issued_at: deps.now().toISOString() as Timestamp,
        client: { name: 'halcyonic-setup-page', version: null, device_label: null },
        command_type: 'project.share.stop',
        payload: { project_id: projectId as ProjectId },
      },
      'http',
      { kind: 'local' },
    );
    return sent.disposition === 'accepted' ? 'stopped' : 'refused';
  };
}

/** A game shared now, as the control plane's view has it. */
export interface SharedNow {
  readonly project_id: string;
  readonly name: string;
  readonly way: 'link' | 'wifi';
  readonly url: string;
}

/** What the page shows. */
export interface SetupState {
  readonly steps: readonly {
    readonly id: string;
    readonly title: string;
    readonly status: SetupStep['status'];
    readonly lines: readonly string[];
    /** Commands for the person to run in Terminal, shown as text and never run by the page. */
    readonly next: readonly string[];
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
  /** The data folder this Halcyonic keeps its data in, where it is not the usual one; null where it is. */
  readonly data_folder: string | null;
  /**
   * Each game shared now (ADR 0054), for Copy and Stop. `address_text` is the Wi-Fi address to show,
   * null for a link: a link's address carries the tunnel service's name and is never shown, only
   * copied (`url`).
   */
  readonly shared: readonly {
    readonly project_id: string;
    readonly name: string;
    readonly way: 'link' | 'wifi';
    readonly address_text: string | null;
    readonly url: string;
  }[];
  /**
   * Each project's secrets by name (ADR 0057); null where none are kept. A value is never in the
   * page's state.
   */
  readonly secrets: {
    readonly projects: readonly SecretsProject[];
    /** The agents that start without them, by name. */
    readonly without: readonly string[];
    /** The agents that run outside the sandbox on a connected folder and can read every project's. */
    readonly outside: readonly string[];
    /** What this session's last set or remove was refused with, said in that project's card. */
    readonly problem: { readonly project_id: string; readonly text: string } | null;
    /** A Remove waiting for its second press: which secret, and what it will do. */
    readonly confirm: {
      readonly project_id: string;
      readonly name: string;
      readonly text: string;
    } | null;
  } | null;
  /**
   * Each declared provider something here reads, in its declaration's words, with what reads it, whether a key is
   * stored and since when; null where none is declared. A key is never in the page's state.
   */
  readonly keys: {
    readonly apps: readonly KeyView[];
    /** What this session's last add or remove was refused with, said in that key's card. */
    readonly problem: { readonly id: string; readonly text: string } | null;
    /** A Remove Key waiting for its second press: which app, and what it will do. */
    readonly confirm: { readonly id: string; readonly text: string } | null;
  } | null;
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
  sharing: [
    { action: 'download', arg: 'tunnel', label: 'Get Link Sharing' },
    { action: 'tunnel', arg: 'off', label: 'Turn Link Sharing Off' },
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
  tunnel: (arg) => (arg === 'off' ? ['tunnel', 'off'] : null),
  companion: (arg) => (arg === COMPANION_MODEL.name || arg === 'off' ? ['companion', arg] : null),
};

/** The checked tunnel release's archive, by its address and SHA-256 (validation/sharing-a-link.md). */
export const TUNNEL_ARCHIVE_URL = `https://github.com/cloudflare/cloudflared/releases/download/${TUNNEL_PIN.version}/cloudflared-darwin-arm64.tgz`;
export const TUNNEL_ARCHIVE_SHA256 =
  '3586ccf397e98e5f437e60e7c307ca6ac463e110580503af8bb8e6f9ebf831bd';

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
  const tunnelFolder = dirname(tunnelProgram(dataDir));
  const tunnelArchive = join(tunnelFolder, 'cloudflared-darwin-arm64.tgz');
  const tunnelSum = `${tunnelArchive}.sha256`;
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
      // The tunnel a shared link runs (ADR 0054): the release's archive, checked by its SHA-256
      // before it is unpacked, and recorded only when the program inside is the checked one.
      id: 'tunnel',
      title: 'Get Link Sharing',
      fetches: `Cloudflare's tunnel program cloudflared ${TUNNEL_PIN.version} for Apple silicon, an 18.9 MB archive, from its GitHub release, checked by its SHA-256 before it is unpacked. Using it means Cloudflare's terms for tunnels without an account.`,
      prepare: () => {
        mkdirSync(tunnelFolder, { recursive: true, mode: 0o700 });
        rmSync(tunnelArchive, { force: true });
        writeFileSync(tunnelSum, `${TUNNEL_ARCHIVE_SHA256}  ${tunnelArchive}\n`);
      },
      commands: [
        [
          '/usr/bin/curl',
          // First, so none of the person's own curl settings (~/.curlrc) apply (AUDIT2 S1).
          '-q',
          '--fail',
          '--location',
          '--silent',
          '--show-error',
          '--proto',
          '=https',
          '--proto-redir',
          '=https',
          '--tlsv1.2',
          // About one and a half times the archive: a redirect to anything larger stops (AUDIT2 S2).
          '--max-filesize',
          '30000000',
          '--output',
          tunnelArchive,
          TUNNEL_ARCHIVE_URL,
        ],
        ['/usr/bin/shasum', '-a', '256', '-c', tunnelSum],
        ['/usr/bin/tar', '-xzf', tunnelArchive, '-C', tunnelFolder, 'cloudflared'],
      ],
      record: ['tunnel'],
      verify: () => isPinnedTunnel(tunnelProgram(dataDir)),
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
          // An empty file each, the run's own: npm 11 refuses /dev/null for both ("double-loading config").
          '--userconfig',
          '<empty user config>',
          '--globalconfig',
          '<empty global config>',
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
  /** The last refusal of a secret's set or remove, said in its project's card (LINK's critic). */
  secretProblem: { project_id: string; text: string } | null;
  /** A Remove asked once, acting only on a second press of the same secret. */
  secretConfirm: { project_id: string; name: string; text: string } | null;
  /** The last refusal of a key's add or remove, said in that key's card. */
  keyProblem: { id: string; text: string } | null;
  /** A Remove Key asked once, acting only on a second press for the same app. */
  keyConfirm: { id: string; text: string } | null;
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
      press = {
        confirm: null,
        said: [],
        secretProblem: null,
        secretConfirm: null,
        keyProblem: null,
        keyConfirm: null,
      };
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
        // The commands the terminal prints under the step, as it prints them (already written for a
        // shell, so never turned to ~), for the person to run themselves.
        next: [...new Set(step.next ?? [])],
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
      data_folder: this.#deps.dataFolder ?? null,
      shared: (this.#deps.shared?.() ?? []).map((share) => ({
        project_id: share.project_id,
        name: share.name,
        way: share.way,
        address_text: share.way === 'wifi' ? share.url.replace(/^http:\/\//, '') : null,
        url: share.url,
      })),
      secrets:
        this.#deps.secrets === undefined
          ? null
          : {
              projects: this.#deps.secrets.projects(),
              without: this.#deps.secrets.without(),
              outside: this.#deps.secrets.outside(),
              problem: here.secretProblem,
              confirm: here.secretConfirm,
            },
      keys:
        this.#deps.keys === undefined
          ? null
          : {
              apps: this.#deps.keys.list(),
              problem: here.keyProblem,
              confirm: here.keyConfirm,
            },
    };
  }

  /**
   * Keeps the person's key for a declared provider by their press: one press, the key typed or pasted for it, never
   * returned. A refusal is said in that key's card. A kept key takes effect as its readers read it: at the next start
   * where one reads it then (the page offers Restart to Apply), else at once.
   */
  async setKey(id: unknown, value: unknown, session: string = ONE_SESSION): Promise<SetupState> {
    const here = this.#press(session);
    // One question stands at a time: a press here forgets any other asked first.
    here.confirm = null;
    here.secretConfirm = null;
    here.keyConfirm = null;
    const keys = this.#deps.keys;
    const problem = keys === undefined ? 'Nothing here takes a key.' : keys.store(id, value);
    // The steps say whether a key is stored, so they are checked again at once.
    if (problem === null) this.#steps = null;
    here.keyProblem =
      problem === null ? null : { id: typeof id === 'string' ? id : '', text: problem };
    return this.state(session);
  }

  /**
   * Removes a declared provider's key on the second press for the same key, as a secret's Remove asks first: a
   * removed key can never be seen or put back. The first press says what it will do, by each reader's timing.
   */
  async removeKey(
    id: unknown,
    confirmed: unknown,
    session: string = ONE_SESSION,
  ): Promise<SetupState> {
    const here = this.#press(session);
    // One question stands at a time: a press here forgets any other asked first.
    here.confirm = null;
    here.secretConfirm = null;
    here.keyProblem = null;
    const keys = this.#deps.keys;
    const card = keys?.list().find((each) => each.id === id);
    if (typeof id !== 'string' || keys === undefined || card === undefined) {
      here.keyConfirm = null;
      return this.state(session);
    }
    if (confirmed !== true || here.keyConfirm?.id !== id) {
      here.keyConfirm = {
        id,
        text: removeWords(card),
      };
      return this.state(session);
    }
    here.keyConfirm = null;
    const problem = keys.remove(id);
    if (problem === null) this.#steps = null;
    here.keyProblem = problem === null ? null : { id, text: problem };
    return this.state(session);
  }

  /** Forgets a Remove Key asked once, by the person's Cancel. */
  async cancelKey(session: string = ONE_SESSION): Promise<SetupState> {
    this.#press(session).keyConfirm = null;
    return this.state(session);
  }

  /**
   * Sets a project secret by the person's press (ADR 0057): one press, the value typed for it. A refusal
   * is said in that project's card, where the person is looking, not in What Setup Said.
   */
  async setSecret(
    projectId: unknown,
    name: unknown,
    value: unknown,
    session: string = ONE_SESSION,
  ): Promise<SetupState> {
    const here = this.#press(session);
    here.keyConfirm = null;
    here.secretConfirm = null;
    const secrets = this.#deps.secrets;
    const problem =
      secrets === undefined
        ? "This computer doesn't keep secrets."
        : secrets.set(projectId, name, value);
    here.secretProblem =
      problem === null
        ? null
        : { project_id: typeof projectId === 'string' ? projectId : '', text: problem };
    return this.state(session);
  }

  /**
   * Removes a project secret on the second press of the same secret, as Forget and Turn Off ask first:
   * a removed value can never be seen or put back. The first press says what it will do.
   */
  async removeSecret(
    projectId: unknown,
    name: unknown,
    confirmed: unknown,
    session: string = ONE_SESSION,
  ): Promise<SetupState> {
    const here = this.#press(session);
    here.keyConfirm = null;
    here.secretProblem = null;
    const secrets = this.#deps.secrets;
    if (typeof projectId !== 'string' || typeof name !== 'string' || secrets === undefined) {
      here.secretConfirm = null;
      return this.state(session);
    }
    const pending = here.secretConfirm;
    if (confirmed !== true || pending?.project_id !== projectId || pending.name !== name) {
      here.secretConfirm = {
        project_id: projectId,
        name,
        text: `Tasks started after this won't get ${name}, and its value can't be seen or put back.`,
      };
      return this.state(session);
    }
    here.secretConfirm = null;
    const problem = secrets.remove(projectId, name);
    here.secretProblem = problem === null ? null : { project_id: projectId, text: problem };
    return this.state(session);
  }

  /** Forgets a Remove asked once, by the person's Cancel. */
  async cancelSecret(session: string = ONE_SESSION): Promise<SetupState> {
    this.#press(session).secretConfirm = null;
    return this.state(session);
  }

  /** Stops a shared game by the person's press: one press, since stopping is undone by sharing again. */
  async stopShare(projectId: unknown, session: string = ONE_SESSION): Promise<SetupState> {
    const here = this.#press(session);
    const stopped = typeof projectId === 'string' ? this.#deps.stopShare?.(projectId) : undefined;
    if (stopped === 'refused')
      return this.#refused(here, session, "This game couldn't be stopped. Try again.");
    if (stopped !== 'stopped') return this.#refused(here, session, 'This game is not shared.');
    here.confirm = null;
    here.said = [];
    return this.state(session);
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
    here.keyConfirm = null;
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
    let configs: string | null = null;
    try {
      // A fresh cache, and an empty settings file each for npm's user and global settings, the run's own.
      const freshPart = (part: string): string => {
        if (part === '<fresh cache>') {
          cache ??= mkdtempSync(join(tmpdir(), 'halcyonic-npm-cache-'));
          return cache;
        }
        if (part !== '<empty user config>' && part !== '<empty global config>') return part;
        if (configs === null) {
          configs = mkdtempSync(join(tmpdir(), 'halcyonic-npm-configs-'));
          writeFileSync(join(configs, 'user'), '');
          writeFileSync(join(configs, 'global'), '');
        }
        return join(configs, part === '<empty user config>' ? 'user' : 'global');
      };
      download.prepare?.();
      for (const command of download.commands) {
        if (job.stopped) break;
        const argv = command.map(freshPart);
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
      if (configs !== null) rmSync(configs, { recursive: true, force: true });
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
            ? 'Halcyonic closes and starts again, with your changes. No task is running. Go On restarts it.'
            : `Halcyonic closes and starts again, with your changes. These tasks stop where they are and come back after the restart: ${tasks.join(', ')}. Go On restarts it.`,
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
    routes.post('/setup/shares/stop', (request, session) => {
      const body = (request.body ?? {}) as { project_id?: unknown };
      return view.stopShare(body.project_id, session);
    });
    // Room for a value as long as the store keeps, and no more (ADR 0057).
    routes.post(
      '/setup/secrets/set',
      (request, session) => {
        const body = (request.body ?? {}) as {
          project_id?: unknown;
          name?: unknown;
          value?: unknown;
        };
        return view.setSecret(body.project_id, body.name, body.value, session);
      },
      { bodyLimit: SECRET_BODY_LIMIT },
    );
    routes.post('/setup/secrets/remove', (request, session) => {
      const body = (request.body ?? {}) as {
        project_id?: unknown;
        name?: unknown;
        confirmed?: unknown;
      };
      return view.removeSecret(body.project_id, body.name, body.confirmed, session);
    });
    routes.post('/setup/secrets/cancel', (_request, session) => view.cancelSecret(session));
    routes.post(
      '/setup/keys/set',
      (request, session) => {
        const body = (request.body ?? {}) as { id?: unknown; value?: unknown };
        return view.setKey(body.id, body.value, session);
      },
      { bodyLimit: KEY_BODY_LIMIT },
    );
    routes.post('/setup/keys/remove', (request, session) => {
      const body = (request.body ?? {}) as { id?: unknown; confirmed?: unknown };
      return view.removeKey(body.id, body.confirmed, session);
    });
    routes.post('/setup/keys/cancel', (_request, session) => view.cancelKey(session));
  };
}

/**
 * What Remove Key will do, said before its second press, from each reader's own name and timing: one that reads the
 * key at its start needs a key again from the next start, one that reads it at each use stops using it at once.
 */
export function removeWords(card: KeyView): string {
  const names = (reads: 'at_start' | 'at_each_use') =>
    card.readers.filter((reader) => reader.reads === reads).map((reader) => reader.name);
  const listed = (all: readonly string[]) =>
    all.length <= 2 ? all.join(' and ') : `${all.slice(0, -1).join(', ')} and ${all.at(-1)}`;
  const atStart = names('at_start');
  const atOnce = names('at_each_use');
  const parts = [
    ...(atStart.length === 0
      ? []
      : [
          `${listed(atStart)} ${atStart.length === 1 ? 'needs' : 'need'} a key again from the next start`,
        ]),
    ...(atOnce.length === 0
      ? []
      : [`${listed(atOnce)} ${atOnce.length === 1 ? 'stops' : 'stop'} using it at once`]),
  ];
  return `Halcyonic deletes its copy.${parts.length === 0 ? '' : ` ${parts.join(', and ')}.`}`;
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
