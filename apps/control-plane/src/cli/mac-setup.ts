/**
 * Checks this Mac's setup for Halcyonic step by step, in plain words, and writes the settings it
 * needs into `<data dir>/settings.json` (ADR 0024, docs/internal/runbooks/LOCAL_DEVELOPMENT.md,
 * "First run on a Mac"):
 *
 *   pnpm mac-setup                      check every step and say what to do next
 *   pnpm mac-setup allow [folder]       let agents use a folder; with none, ~/HalcyonicProjects
 *   pnpm mac-setup disallow <folder>    stop letting agents use a folder; nothing is deleted
 *   pnpm mac-setup agent-apps           record the pinned OpenCode and Codex, once checked
 *   pnpm mac-setup local-model <name>   give OpenCode and Codex Halcyonic's own settings on an Ollama model
 *   pnpm mac-setup voice                record the voice files, once checked
 *   pnpm mac-setup companion <name|off> let Create's companion ask an Ollama model on this Mac
 *   pnpm mac-setup pairing on|off       let headsets pair over Wi-Fi, or stop it
 *   pnpm mac-setup login on|off         start Halcyonic whenever you log in, or stop that (login-agent.ts)
 *
 * It never opens a credential: of the Seorak, Salidium, Omnorium and Anthropic files it reads only whether
 * they exist and who may read them. By default it reads no access token and asks the running
 * Halcyonic only its public health check; with --with-token it reads the token and sends it only
 * after the server proves it holds it. It never turns on an agent app that spends model credit,
 * never names a remote model, and downloads nothing: what needs a download it shows as a command
 * for the person to run.
 */
import { execFileSync } from 'node:child_process';
import {
  accessSync,
  chmodSync,
  constants,
  type Dirent,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  type Stats,
  statSync,
  writeFileSync,
} from 'node:fs';
import { readFile } from 'node:fs/promises';
import { platform, totalmem, userInfo } from 'node:os';
import { delimiter, dirname, isAbsolute, join, resolve, sep } from 'node:path';
import { createInterface } from 'node:readline/promises';
import { fileURLToPath } from 'node:url';
import type { DevicesResponse, LocationsResponse, Snapshot } from '@halcyonic/contracts';
import {
  CLAUDE_AGENT_RUNTIME_ID,
  cloudProvider,
  type PersonalSetupUse,
} from '@halcyonic/integration-claude-code';
import { OPENCODE_SANDBOX_FOLDER, sandboxedSessionStore } from '@halcyonic/integration-opencode';
import {
  defaultSalidiumHome,
  LAST_OWN_SOURCES_ONLY,
  readsOtherToolsSources,
  SalidiumClient,
} from '@halcyonic/integration-salidium';
import { isCloudName } from '../companion/ollama.ts';
import {
  ConfigError,
  companionOllamaAddress,
  DEFAULT_PORT,
  defaultDataDir,
  isLocalOllamaModel,
  loadConfig,
  loopbackListener,
  OPENCODE_SETTINGS_PATH,
  type OpenCodeSettings,
  portFamily,
  readOpenCodeSettings,
} from '../config.ts';
import { assessFolder, type FolderContext, realOrSelf } from '../folder-safety.ts';
import {
  ACCESS_TOKEN_FILE,
  loopbackBases,
  provenBase,
  serverProvesToken,
} from '../http/security.ts';
import { OMNORIUM_CREDENTIAL_FILE } from '../intelligence/catalog.ts';
import { CODEX_CONFINED_FOLDER } from '../intelligence/codex-home.ts';
import { SEORAK_CREDENTIAL_FILE } from '../intelligence/evaluation.ts';
import { SALIDIUM_CREDENTIAL_FILE } from '../intelligence/understanding.ts';
import { keyFile } from '../keys/provider-keys.ts';
import { managersOn } from '../own-check/own-check.ts';
import { type PinnedFile, type Pins, pinsForThisMac, sha256File } from '../pins.ts';
import { AUDIO_OUTPUT, installAudioOutput } from '../preview/audio-output.ts';
import {
  installLauncher,
  launcherPath,
  launcherProblem,
  programProblem,
} from '../preview/launcher.ts';
import { browserProblem, installBrowser } from '../preview/play.ts';
import { isPinnedTunnel, TUNNEL_PIN } from '../preview/share.ts';
import { HostProviders } from '../providers.ts';
import {
  AGENT_KEY_READERS,
  CODEX_HOME_FOLDER,
  KEYED_AGENT_APPS,
  providersReadBy,
} from '../runtimes.ts';
import {
  type HostSettings,
  readHostSettings,
  SETTINGS_FILE,
  type SettingName,
  settingRoots,
  withSettings,
  writeHostSettings,
} from '../settings.ts';
import { HostStarters, installedSet, recordInstalled, VR_STARTER } from '../starters.ts';
import { printable } from './devices.ts';
import { localLoginIo, runLogin } from './login-agent.ts';

/** The repository's tracked starters (ADR 0037). */
const TRACKED_STARTERS = fileURLToPath(new URL('../../../../starters', import.meta.url));

/** The git undo runs when none is named: the system's own, never one found on the PATH (ADR 0036). */
const SYSTEM_GIT = '/usr/bin/git';

export interface Firewall {
  readonly enabled: boolean;
  readonly blockAll: boolean;
}

/** whisper.cpp is built on the Mac from its pinned source, so its binary has no checksum. */
const WHISPER_BINARY = 'speech/whisper.cpp-1.9.4/bin/whisper-cli';

/** The folder `allow` makes and allows when given none. */
export const DEFAULT_PROJECTS_FOLDER = 'HalcyonicProjects';

/**
 * The Ollama models Halcyonic was run with end to end, on a Mac with 64 GB (local-models.md), the
 * default first. Muse Glimmer is checked, not the default: Apache 2.0 by a card under the
 * meta-models organisation, shown as Meta Inc., its verification not seen, which also says the
 * model is not meant for anyone under 18, so the default is the owner's decision; it was clean in
 * 3 of 3 runs of a kid's game where qwen3.6 was clean in 1 of 4.
 */
export const CHECKED_MODELS = [
  { name: 'qwen3.6:35b-a3b-nvfp4', bytes: 23.6e9 },
  { name: 'muse-glimmer:30b-mlx', bytes: 19.1e9 },
  { name: 'qwen3.8:27b-nvfp4', bytes: 18.2e9 },
] as const;

/**
 * The companion's own model, the owner's choice after lane C's trial (companion-model.md): a model
 * of its own answers beside a task on the agents' model, where sharing theirs waits for each step.
 */
export const COMPANION_MODEL = {
  name: 'qwen3.5:9b',
  download: '6.6 GB',
  memory: 'about 5.7 GB',
} as const;

/** The context Ollama is told to give a model, and OpenCode and Codex to expect (local-models.md). */
const OLLAMA_CONTEXT = 65_536;

/** Where Codex compacts a thread's context, safely below OLLAMA_CONTEXT (local-models.md). */
const CODEX_AUTO_COMPACT = 52_000;

/** Model names Codex's settings may hold, as the Codex adapter takes them. */
const CODEX_MODEL_NAME = /^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,127}$/;

export interface MacSetupIo {
  readonly env: NodeJS.ProcessEnv;
  readonly home: string;
  readonly print: (line: string) => void;
  /** Asks the person and resolves with the answer; null when nobody can answer, as without a terminal. */
  readonly ask: ((question: string) => Promise<string>) | null;
  readonly fetch: typeof fetch;
  readonly ollamaUrl: string;
  readonly memoryBytes: number;
  /** Where a command is on the PATH, or null. */
  readonly which: (command: string) => string | null;
  /** The macOS firewall's state, or null when it can't be read. */
  readonly firewall: () => Firewall | null;
  /** Null where Halcyonic has no checksums, as on an Intel Mac. */
  readonly pins: Pins | null;
  /**
   * The agent apps that run on the person's own key which the running Halcyonic started with, by runtime id. Set only
   * where the running control plane asks (its setup page), so a key added or removed since can be said to wait for
   * the next start; absent, as in Terminal, setup says what the next start does.
   */
  readonly startedWith?: readonly string[];
  /**
   * The host's one list of declared providers, where the running control plane passes its own (ADR 0061); read when
   * setup first needs it, so a declaration that does not hold is said as a step.
   */
  readonly providers?: () => HostProviders;
}

/** Whether `variable` holds a key in `env`: a value of spaces alone is none, as the start reads it. */
function keyIn(env: NodeJS.ProcessEnv, variable: string): boolean {
  return (env[variable] ?? '').trim() !== '';
}

/** An agent app that runs on the person's own key, as setup sees it now and as the next start will have it. */
interface KeyedApp {
  /** The agent app by its runtime id and name, and the declared provider key it reads, by that provider's name. */
  readonly key: {
    readonly runtimeId: string;
    readonly app: string;
    readonly declared: string;
    readonly variable: string;
    readonly file: string;
    /** What of the person's own set-up a task outside the sandbox uses, as the adapter declares; null for none. */
    readonly personal: (() => PersonalSetupUse) | null;
  };
  readonly stored: boolean;
  /** A file is where its key would be, but the app would refuse it (others can read it, or it is empty). */
  readonly unusable: boolean;
  /** On at the next start: by its stored key, or its own switch in the environment. */
  readonly next: boolean;
  /** On now, where setup knows what the running Halcyonic started with; else as `next`. */
  readonly now: boolean;
  /** The running Halcyonic is set to differ from it at the next start. */
  readonly changes: boolean;
  /** The cloud provider a switch in the environment selects for it, by name, or null. */
  readonly provider: string | null;
  /**
   * Why the next start stops on it: its switch is on in the environment with no key there, and its key file can't be
   * used (`unusable`), or there is no key and no cloud provider at all (`no_credentials`); null where it starts.
   */
  readonly stops: 'unusable' | 'no_credentials' | null;
}

type Status = 'ready' | 'todo' | 'optional' | 'look' | 'unknown';

const STATUS_WORDS: Record<Status, string> = {
  ready: 'Ready',
  todo: 'To do',
  optional: 'Optional',
  look: 'Look at this',
  unknown: "Can't tell yet",
};

/** Which step a state is, for a page that offers each step's action (ADR 0053). */
export type SetupStepId =
  | 'settings'
  | 'folders'
  | 'agent_apps'
  | 'models'
  | 'cost'
  | 'voice'
  | 'launcher'
  | 'starter'
  | 'own_checks'
  | 'undo'
  | 'sharing'
  | 'companion'
  | 'usage_left'
  | 'understand'
  | 'catalog'
  | 'running'
  | 'headset';

/** A step of the check as data: what the terminal prints for it. */
export interface SetupStep extends Step {
  readonly id: SetupStepId;
}

export type SetupStatus = Status;

/** Every step's state as data, as `pnpm mac-setup` prints it, for the setup page (ADR 0053). Changes nothing. */
export function macSetupSteps(
  io: MacSetupIo,
  options: { readonly useToken?: boolean } = {},
): Promise<SetupStep[]> {
  return new MacSetup(io, { useToken: options.useToken === true, yes: false }).steps();
}

interface Step {
  readonly title: string;
  readonly status: Status;
  /** Sentences, wrapped when printed. */
  readonly lines: readonly string[];
  /** Commands for the person to run, each on a line of its own. */
  readonly next?: readonly string[];
}

/** What a folder you allow lets agents and a paired headset do, said wherever folders are. */
export const FOLDER_MEANING = [
  'Agents may read and change anything in a folder you allow, and in every folder inside it.',
  'A paired headset sees the names of the folders directly inside it, can make new empty folders there with no limit on how many, and can move any project to another folder there: its later tasks then work in that folder.',
  'Allow one folder you keep for projects, such as ~/HalcyonicProjects, never your whole home folder.',
];

const RESTART_WARNING =
  "Restarting stops any agent at work, and its task then shows Can't tell yet.";

/** Runs one command; returns the process exit status. */
export async function runMacSetup(args: readonly string[], io: MacSetupIo): Promise<number> {
  const flags = new Set(args.filter((arg) => arg.startsWith('--')));
  const [command = 'check', argument, ...rest] = args.filter((arg) => !arg.startsWith('--'));
  const unknownFlag = [...flags].find(
    (flag) => flag !== '--yes' && flag !== '--no-token' && flag !== '--with-token',
  );
  if (unknownFlag !== undefined || rest.length > 0) return usage(io);
  const setup = new MacSetup(io, {
    useToken: flags.has('--with-token') && !flags.has('--no-token'),
    yes: flags.has('--yes'),
  });
  switch (command) {
    case 'check':
      return argument === undefined ? setup.check() : usage(io);
    case 'allow':
      return setup.allow(argument);
    case 'disallow':
      return argument === undefined ? usage(io) : setup.disallow(argument);
    case 'agent-apps':
      return argument === undefined ? setup.agentApps() : usage(io);
    case 'local-model':
      return argument === undefined ? usage(io) : setup.localModel(argument);
    case 'voice':
      return argument === undefined ? setup.voice() : usage(io);
    case 'companion':
      return argument === undefined ? usage(io) : setup.companion(argument);
    case 'pairing':
      return argument === 'on' || argument === 'off' ? setup.pairing(argument) : usage(io);
    case 'launcher':
      return argument === undefined ? setup.launcher() : usage(io);
    case 'browser':
      return setup.browser(argument);
    case 'starter':
      return argument === undefined ? setup.starter() : usage(io);
    case 'undo':
      return setup.undo(argument);
    case 'tunnel':
      return setup.tunnel(argument);
    case 'login':
      return argument === 'on' || argument === 'off'
        ? runLogin(argument, io.env, io.home, localLoginIo(io.print))
        : usage(io);
    default:
      return usage(io);
  }
}

function usage(io: MacSetupIo): number {
  for (const line of [
    'Usage:',
    '  pnpm mac-setup                      check every step and say what to do next',
    '  pnpm mac-setup allow [folder]       let agents use a folder; with none, ~/HalcyonicProjects',
    '  pnpm mac-setup disallow <folder>    stop letting agents use a folder; nothing is deleted',
    '  pnpm mac-setup agent-apps           record the checked copies of OpenCode and Codex',
    '  pnpm mac-setup local-model <name>   give OpenCode and Codex settings of their own on an Ollama model',
    '  pnpm mac-setup voice                record the checked voice files',
    "  pnpm mac-setup companion <name|off> let Create's companion ask an Ollama model on this Mac",
    '  pnpm mac-setup pairing on|off       let headsets pair over Wi-Fi, or stop it',
    "  pnpm mac-setup browser [folder]     set up new apps' browser once: find it, copy it, build its launcher",
    "  pnpm mac-setup launcher             build what starts new apps' browser without this app's permissions",
    "  pnpm mac-setup starter              record the VR starter's pinned set, or say how to install it",
    '  pnpm mac-setup undo [git|off]       let Halcyonic undo a task, with /usr/bin/git or the git you name',
    '  pnpm mac-setup tunnel [path|off]    record the checked tunnel program that shares a game at a link',
    '  pnpm mac-setup login on|off         start Halcyonic whenever you log in, or stop that',
    'Add --with-token to ask the running Halcyonic what it uses: the check then reads its access',
    'token, and sends it only after Halcyonic proves it holds it.',
  ]) {
    io.print(line);
  }
  return 2;
}

interface Running {
  /** Null when nothing answers on the port. */
  readonly answering: boolean;
  /** What the running Halcyonic says it uses, when it could be asked. */
  readonly roots: readonly string[] | null;
  readonly agentApps: readonly string[] | null;
  readonly devices: DevicesResponse | null;
  readonly usageLeft: { readonly availability: string; readonly code: string | null } | null;
  /** Whether the running Halcyonic can ask the companion now, as `GET /api/companion` says. */
  readonly companion: { readonly availability: string; readonly code: string | null } | null;
  /** No access token could be read, or what answers could not prove it holds it. */
  readonly tokenProblem: 'missing' | 'unproved' | null;
}

interface OllamaModel {
  readonly name: string;
  readonly bytes: number;
  readonly local: boolean;
  readonly tools: boolean;
}

class MacSetup {
  readonly #io: MacSetupIo;
  readonly #useToken: boolean;
  readonly #yes: boolean;
  readonly #dataDir: string;
  readonly #hashes = new Map<string, Promise<string | null>>();

  constructor(io: MacSetupIo, options: { useToken: boolean; yes: boolean }) {
    this.#io = io;
    this.#useToken = options.useToken;
    this.#yes = options.yes;
    this.#dataDir = defaultDataDir(io.env);
  }

  // The check

  /** Every step's state, as the check prints it, each with its id; changes nothing. */
  async steps(): Promise<SetupStep[]> {
    const io = this.#io;
    const settings = this.#settings();
    const running = await this.#running();
    const ollama = await this.#ollama();
    const steps: SetupStep[] = [];
    if ('error' in settings) {
      steps.push({
        id: 'settings',
        title: "Halcyonic's settings",
        status: 'todo',
        lines: [`Halcyonic won't start with its settings as they are: ${settings.error}`],
      });
    }
    const values = 'error' in settings ? {} : settings.values;
    const env = withSettings(io.env, values);
    const add = (id: SetupStepId, step: Step) => steps.push({ id, ...step });
    add('folders', this.#foldersStep(env));
    add('agent_apps', await this.#agentAppsStep(env, ollama));
    add('models', this.#modelsStep(ollama));
    add('cost', this.#costStep(env, ollama));
    add('voice', await this.#voiceStep(env));
    add('launcher', this.#launcherStep(env));
    add('starter', this.#starterStep());
    add('own_checks', this.#ownChecksStep());
    add('undo', this.#undoStep(env));
    add('sharing', await this.#sharingStep(env));
    add('companion', await this.#companionStep(env, ollama, running));
    add('usage_left', this.#usageLeftStep(running));
    add('understand', await this.#understandStep());
    add('catalog', this.#catalogStep());
    add('running', this.#runningStep(env, running));
    add('headset', this.#headsetStep(env, running));
    return steps;
  }

  async check(): Promise<number> {
    const io = this.#io;
    const steps = await this.steps();

    io.print('Halcyonic on this Mac');
    io.print('');
    steps.forEach((step, index) => {
      io.print(`${index + 1}. ${step.title}: ${STATUS_WORDS[step.status]}`);
      for (const line of step.lines) for (const wrapped of wrap(line, 3)) io.print(wrapped);
      if (step.next !== undefined && step.next.length > 0) {
        io.print('   Next:');
        for (const command of new Set(step.next)) io.print(`     ${command}`);
      }
      io.print('');
    });
    const first = steps.find((step) => step.status === 'todo');
    if (first === undefined) {
      io.print('Everything Halcyonic needs on this Mac is ready.');
      return 0;
    }
    const count = steps.filter((step) => step.status === 'todo').length;
    io.print(
      `${count === 1 ? '1 step is' : `${count} steps are`} still to do. Start with “${first.title}”.`,
    );
    return 1;
  }

  #foldersStep(env: NodeJS.ProcessEnv): Step {
    const title = 'Folders agents may use';
    const fromEnvironment = this.#io.env.HALCYONIC_PROJECT_ROOTS !== undefined;
    const roots = (env.HALCYONIC_PROJECT_ROOTS ?? '')
      .split(delimiter)
      .filter((root) => root !== '');
    const source = fromEnvironment
      ? [
          "These come from HALCYONIC_PROJECT_ROOTS in the environment, which wins over Halcyonic's settings.",
        ]
      : [];
    if (roots.length === 0) {
      return {
        title,
        status: 'todo',
        lines: [
          "No folder is allowed yet, so no agent can start work. The headset says “Your computer doesn't allow any folder yet.”",
          ...FOLDER_MEANING,
          ...source,
        ],
        next: [
          `pnpm mac-setup allow    (makes ~/${DEFAULT_PROJECTS_FOLDER} if it isn't there, then asks you)`,
        ],
      };
    }
    const lines: string[] = [];
    let status: Status = 'ready';
    for (const root of roots) {
      const shown = this.#tilde(root);
      if (!isDirectory(root)) {
        status = 'todo';
        lines.push(
          `${shown} isn't there, and Halcyonic won't start until it is back or you stop allowing it.`,
        );
        continue;
      }
      const assessment = assessFolder(realOrSelf(root), this.#folders());
      if (assessment.verdict === 'refused') {
        status = 'todo';
        lines.push(`${shown} should not be allowed. ${assessment.reason}`);
      } else if (assessment.verdict === 'broad') {
        if (status === 'ready') status = 'look';
        lines.push(`${shown} is allowed. ${assessment.reason}`);
      } else {
        lines.push(`${shown} is allowed.`);
      }
    }
    const disallow = roots
      .filter((root) => {
        if (!isDirectory(root)) return true;
        return assessFolder(realOrSelf(root), this.#folders()).verdict !== 'fine';
      })
      .map((root) => `pnpm mac-setup disallow ${quote(this.#tilde(root))}`);
    return {
      title,
      status,
      lines: [...lines, ...FOLDER_MEANING.slice(0, 2), ...source],
      next: fromEnvironment ? [] : disallow,
    };
  }

  async #agentAppsStep(env: NodeJS.ProcessEnv, ollama: OllamaModel[] | null): Promise<Step> {
    const title = 'Agent apps';
    const lines: string[] = [];
    const next: string[] = [];
    let ready = 0;
    let look = false;
    for (const app of ['opencode', 'codex'] as const) {
      const report = await this.#agentApp(app, env);
      lines.push(report.line);
      if (report.next !== undefined) next.push(report.next);
      if (report.state === 'ready') ready += 1;
      if (report.state === 'look') look = true;
    }
    if (present(env.HALCYONIC_CODEX_BIN)) {
      const codex = this.#codexSettings();
      if (codex?.provider === 'ollama' && codex.model !== null) {
        lines.push(
          `Codex uses Halcyonic's own Codex settings, in a folder of its own: ${codex.model} on this Mac. It runs only on models this Mac serves, never signed in, with its plugins and update check off; your own Codex settings are left as they are.`,
        );
      } else {
        look = true;
        lines.push(
          "Codex runs only on models this Mac serves, and Halcyonic's own Codex settings don't name one yet, so Codex has no model to offer.",
        );
        next.push(`pnpm mac-setup local-model ${this.#suggestedModel(ollama) ?? '<model name>'}`);
      }
    }
    if (env.HALCYONIC_OPENCODE_BIN !== undefined && env.HALCYONIC_OPENCODE_BIN !== '') {
      const home = env.HALCYONIC_OPENCODE_CONFIG_HOME;
      let own: OpenCodeSettings | null = null;
      try {
        own = home === undefined || home === '' ? null : readOpenCodeSettings(home);
      } catch {
        own = null;
      }
      // What a task may do is set by the rules on each session Halcyonic opens, which outrank any
      // settings (opencode-permissions.md); the settings choose the model and its limits.
      if (own === null) {
        look = true;
        lines.push(
          "Halcyonic can't tell whether your own settings put OpenCode on a model this computer serves, and any plugins they name run inside OpenCode.",
        );
        next.push(`pnpm mac-setup local-model ${this.#suggestedModel(ollama) ?? '<model name>'}`);
      } else {
        lines.push(
          `OpenCode uses Halcyonic's own OpenCode settings, on ${own.model.replace(/^ollama\//, '')}.`,
        );
      }
      if (this.#io.which('rg') === null) {
        look = true;
        lines.push(
          "OpenCode searches files with ripgrep, which isn't on this PATH, and its sandbox won't let it download one, so OpenCode can't search files until it's installed.",
        );
        next.push('brew install ripgrep');
      }
    }
    // Each agent app that reads a declared provider's key (ADR 0061), and so runs on the person's own key.
    // What the running Halcyonic started with is said as it is; a key added or removed since waits for the next start.
    const declarations = this.#providerList().problem;
    if (declarations !== null) {
      look = true;
      lines.push(
        `A provider's declaration doesn't hold, so Halcyonic won't start until it is fixed: ${this.#tilde(declarations)}`,
      );
    }
    const said = new Set<string>();
    for (const keyed of this.#keyedApps(env)) {
      const { key } = keyed;
      // Said as of the next start where nothing tells setup what the running Halcyonic started with (Terminal).
      const known = this.#io.startedWith !== undefined;
      // In Terminal, an app whose key file will stop the start can start no work.
      if (keyed.now && !(keyed.stops !== null && !known)) ready += 1;
      const onWords = known ? `${key.app} is on` : `${key.app} is on from Halcyonic's next start`;
      const byEnvironment = keyIn(env, key.variable);
      const paid = byEnvironment
        ? `Every task on it is paid for with the ${key.variable} in Halcyonic's environment${keyed.stored ? ', not the stored key' : ''}.`
        : keyed.stored
          ? `Every task on it is paid for with that key.`
          : `Every task on it is paid for through your ${keyed.provider ?? 'cloud provider'} account, set in Halcyonic's environment.`;
      const its = `its ${key.declared} key`;
      const addIt = known ? `Add ${its} below.` : `Add ${its} under Agent Apps on the setup page.`;
      // While the start stops, the setup page is not there: in Terminal, the way to it is a start without the switch.
      const fixIt = known
        ? `adding ${its} below replaces it.`
        : `start Halcyonic once without HALCYONIC_CLAUDE_AGENT=1, then add ${its} under Agent Apps on the setup page.`;
      const sentence = (words: string) => `${words.charAt(0).toUpperCase()}${words.slice(1)}`;
      if (keyed.stops === 'unusable') {
        look = true;
        lines.push(
          `${key.app} is switched on in Halcyonic's environment, but ${this.#tilde(join(this.#dataDir, key.file))} can be read by other users or is empty, so Halcyonic won't start${known ? ' again' : ''} until it is fixed. If it holds the key, run chmod 600 on it. Otherwise, ${fixIt}`,
        );
        continue;
      }
      if (keyed.stops === 'no_credentials') {
        look = true;
        lines.push(
          `${key.app} is switched on in Halcyonic's environment, but it has no key and no cloud provider, so Halcyonic won't start${known ? ' again' : ''} until it has one. ${known ? addIt : sentence(fixIt)}`,
        );
        continue;
      }
      if (keyed.changes)
        lines.push(
          keyed.next
            ? `${key.app} turns on when Halcyonic starts again${keyed.stored ? ', with your stored key' : ''}. ${paid}`
            : `${key.app} turns off when Halcyonic starts again, since ${keyed.unusable ? "its key file can't be used" : 'its key was removed'}.`,
        );
      else if (keyed.now)
        lines.push(
          keyed.stored && !byEnvironment
            ? `${onWords}, with your key stored on this computer. ${paid}`
            : `${onWords}. ${paid}`,
        );
      else lines.push(`${key.app} runs on your own key, which pays for every task on it. ${addIt}`);
      // What of the person's own set-up for it a task outside the sandbox uses, read from what the adapter declares:
      // once per app, however many providers it reads.
      if (key.personal !== null && !said.has(key.runtimeId)) {
        said.add(key.runtimeId);
        const used = key.personal();
        const and = (names: readonly string[]) =>
          names.length <= 2
            ? names.join(' and ')
            : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
        // Claude Agent also runs outside the sandbox on an app made here while a cloud provider is switched on.
        const where =
          key.runtimeId === CLAUDE_AGENT_RUNTIME_ID
            ? 'On a folder you connected, and anywhere while a cloud provider is switched on'
            : 'On a folder you connected';
        // Only hooks as each integration's current installer writes them are passed (personal-settings.ts).
        const hooks =
          used.hooksOf.length === 0
            ? ''
            : ` and ${and(used.hooksOf.map((name) => `${name}'s`))} hooks there, as their current installers write them`;
        const folder =
          key.runtimeId === CLAUDE_AGENT_RUNTIME_ID
            ? 'your own Claude Code settings folder'
            : 'your own settings folder';
        lines.push(
          `${where}, a task on ${key.app} uses the ${used.instructions} in ${folder}${hooks}${used.restUsed ? '' : ', and nothing else of your own settings'}.`,
        );
      }
      if (keyed.unusable) {
        look = true;
        lines.push(
          `${this.#tilde(join(this.#dataDir, key.file))} can be read by other users or is empty, so ${key.app} doesn't use it. If it holds the key, run chmod 600 on it. Otherwise, adding its key again replaces it.`,
        );
      }
    }
    if (ready === 0) {
      lines.unshift(
        'No agent app is set up, so no task can start. The headset says “No agent app on your computer can start work right now.”',
      );
    }
    return { title, status: ready === 0 ? 'todo' : look ? 'look' : 'ready', lines, next };
  }

  #providers: { readonly providers: HostProviders | null; readonly problem: string | null } | null =
    null;

  /**
   * The host's one list of declared providers: the running control plane's own where it passes it, else read once per
   * run. A declaration that does not hold is said in the Agent apps step, as the start would stop on it, rather than
   * ending setup.
   */
  #providerList(): { readonly providers: HostProviders | null; readonly problem: string | null } {
    if (this.#providers === null) {
      try {
        this.#providers = {
          providers:
            this.#io.providers?.() ??
            new HostProviders({ dataDir: this.#dataDir, readers: AGENT_KEY_READERS }),
          problem: null,
        };
      } catch (error) {
        if (!(error instanceof ConfigError)) throw error;
        this.#providers = { providers: null, problem: error.message };
      }
    }
    return this.#providers;
  }

  /** Each declared keyed agent app: stored or not, on at the next start or not, and on now where that is known. */
  #keyedApps(env: NodeJS.ProcessEnv): KeyedApp[] {
    const { providers } = this.#providerList();
    if (providers === null) return [];
    const declared = KEYED_AGENT_APPS.flatMap(({ runtimeId, reader, personalSetup }) =>
      providersReadBy(providers, runtimeId).map((provider) => ({
        runtimeId,
        app: reader.name,
        declared: provider.name,
        variable: provider.key.variable,
        file: provider.key.file,
        personal: personalSetup,
      })),
    );
    return declared.map((key) => {
      const file = keyFile(this.#dataDir, key);
      const stored = file.state === 'stored';
      // Claude Agent alone also has a switch of its own in the environment (HALCYONIC_CLAUDE_AGENT).
      const byEnv = key.runtimeId === CLAUDE_AGENT_RUNTIME_ID && env.HALCYONIC_CLAUDE_AGENT === '1';
      const next = stored || byEnv;
      const started = this.#io.startedWith;
      const now = started === undefined ? next : started.includes(key.runtimeId);
      const unusable = file.state === 'unusable';
      // As the start authenticates it: a key in the environment, else the stored key, else a cloud provider's switch.
      const provider =
        key.runtimeId === CLAUDE_AGENT_RUNTIME_ID
          ? cloudProvider(env as Readonly<Record<string, string>>)
          : null;
      const stops =
        !byEnv || keyIn(env, key.variable)
          ? null
          : unusable
            ? 'unusable'
            : !stored && provider === null
              ? 'no_credentials'
              : null;
      return { key, stored, unusable, next, now, changes: now !== next, provider, stops };
    });
  }

  async #agentApp(
    app: 'opencode' | 'codex',
    env: NodeJS.ProcessEnv,
  ): Promise<{ state: 'ready' | 'todo' | 'look' | 'none'; line: string; next?: string }> {
    const name = app === 'opencode' ? 'OpenCode 2.0.18' : 'Codex 0.157.0';
    const variable = app === 'opencode' ? 'HALCYONIC_OPENCODE_BIN' : 'HALCYONIC_CODEX_BIN';
    const pin = this.#io.pins?.[app] ?? null;
    const configured = env[variable];
    if (configured !== undefined && configured !== '') {
      if (!existsSync(configured)) {
        return {
          state: 'todo',
          line: `${name} isn't at ${this.#tilde(configured)} any more, so Halcyonic won't start.`,
        };
      }
      const verdict = await this.#matches(configured, pin);
      if (verdict === 'matches')
        return { state: 'ready', line: `${name} is set up: the copy Halcyonic was checked with.` };
      if (verdict === 'unknown') {
        return {
          state: 'look',
          line: `${name} is set up, but Halcyonic has no checksum for this Mac's processor, so it can't check the copy.`,
        };
      }
      return {
        state: 'look',
        line: `${name} at ${this.#tilde(configured)} isn't the copy Halcyonic was checked with. Halcyonic still runs it if it reports version ${name.split(' ')[1]}.`,
      };
    }
    const standard = pin === null ? null : join(this.#dataDir, pin.path);
    if (standard !== null && existsSync(standard)) {
      const verdict = await this.#matches(standard, pin);
      if (verdict === 'matches') {
        return {
          state: 'none',
          line: `${name} is installed and checked, but not set up yet.`,
          next: 'pnpm mac-setup agent-apps',
        };
      }
      return {
        state: 'none',
        line: `${name} at ${this.#tilde(standard)} isn't the copy Halcyonic was checked with, so it isn't set up.`,
      };
    }
    const folder = quote(this.#tilde(this.#dataDir));
    const install =
      app === 'opencode'
        ? `npm install --prefix ${folder}/runtimes/opencode-2.0.18 @opencode/cli@2.0.18 --ignore-scripts`
        : `npm install --prefix ${folder}/runtimes/codex-0.157.0 @openai/codex@0.157.0 --ignore-scripts`;
    return {
      state: 'none',
      line: `${name} isn't installed for Halcyonic. Installing it downloads it from npm; then run pnpm mac-setup agent-apps.`,
      next: install,
    };
  }

  #modelsStep(ollama: OllamaModel[] | null): Step {
    const title = 'Models on this Mac';
    const memory = gigabytes(this.#io.memoryBytes);
    if (ollama === null) {
      return {
        title,
        status: 'todo',
        lines: [
          "Ollama isn't answering on this Mac, so no model can run here. Without it, agents can only use models on a remote service: your code and instructions go there, and some cost money.",
          `Install Ollama, start it with OLLAMA_CONTEXT_LENGTH=${OLLAMA_CONTEXT}, then download a model. See “Local models through Ollama” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        ],
      };
    }
    const usable = ollama.filter((model) => model.local && model.tools);
    const lines: string[] = [];
    for (const model of ollama) {
      const size = `${gigabytes(model.bytes)} GB`;
      if (!model.local) {
        lines.push(`${model.name} runs on a remote service of Ollama's, not on this Mac.`);
      } else if (!model.tools) {
        lines.push(`${model.name} (${size}) can't use tools, so agents can't work with it.`);
      } else if (model.bytes > this.#io.memoryBytes * 0.6) {
        lines.push(
          `${model.name} (${size}) may not fit beside your other apps in this Mac's ${memory} GB of memory.`,
        );
      } else {
        lines.push(`${model.name} (${size}) runs on this Mac.`);
      }
    }
    lines.push(
      'Halcyonic was checked with qwen3.6:35b-a3b-nvfp4 (23.6 GB), muse-glimmer:30b-mlx (19.1 GB) and qwen3.8:27b-nvfp4 (18.2 GB), on a Mac with 64 GB of memory.',
    );
    if (usable.length > 0) return { title, status: 'ready', lines };
    const suggestion = this.#suggestedModel(null);
    lines.unshift('No model on this Mac can do agent work yet.');
    if (suggestion === null) {
      lines.push(
        `This Mac has ${memory} GB of memory, too little for the models Halcyonic was checked with. A smaller model may work, but none was checked.`,
      );
      return { title, status: 'todo', lines };
    }
    return {
      title,
      status: 'todo',
      lines: [...lines, 'Downloading a model takes a while and the space it says.'],
      next: [`ollama pull ${suggestion}`],
    };
  }

  /** A model this Mac has, or a checked one that fits its memory, for a suggested command. */
  #suggestedModel(ollama: OllamaModel[] | null): string | null {
    const usable = ollama?.filter((model) => model.local && model.tools) ?? [];
    for (const checked of CHECKED_MODELS) {
      if (usable.some((model) => model.name === checked.name)) return checked.name;
    }
    if (usable[0] !== undefined) return usable[0].name;
    const fits = CHECKED_MODELS.find((model) => model.bytes <= this.#io.memoryBytes * 0.6);
    return fits?.name ?? null;
  }

  #costStep(env: NodeJS.ProcessEnv, ollama: OllamaModel[] | null): Step {
    const title = 'Where work goes, and what it costs';
    const lines = [
      'Halcyonic never picks a model by itself: each task runs on the model chosen for it. The headset lists the models that run on this Mac first, and a model that runs on a remote service takes a second press.',
    ];
    let hosted = false;
    if (present(env.HALCYONIC_OPENCODE_BIN)) {
      lines.push(
        'OpenCode also offers free models that run on a remote service of its own, opencode.ai. A task on one sends your code and instructions there.',
      );
    }
    if (present(env.HALCYONIC_CODEX_BIN)) {
      lines.push(
        "Codex runs only on models this Mac serves, never on OpenAI's: the headset lists no remote model for it.",
      );
    }
    const claude = this.#keyedApps(this.#io.env).find(
      (keyed) => keyed.key.runtimeId === CLAUDE_AGENT_RUNTIME_ID,
    );
    const again = this.#io.startedWith === undefined ? '' : ' again';
    if (claude?.stops === 'unusable') {
      hosted = true;
      lines.push(
        `Claude Agent is switched on, but its key file can't be used, so Halcyonic won't start${again} until it is fixed.`,
      );
    } else if (claude?.stops === 'no_credentials') {
      hosted = true;
      lines.push(
        `Claude Agent is switched on, but it has no key and no cloud provider, so Halcyonic won't start${again} until it has one.`,
      );
    } else if (claude?.changes === true && !claude.next) {
      // Still on until the restart, so still a paid service to look at.
      hosted = claude.now;
      lines.push('Claude Agent turns off when Halcyonic starts again.');
    } else if (claude?.now === true || claude?.next === true) {
      hosted = true;
      const on = claude.changes
        ? 'Claude Agent turns on when Halcyonic starts again.'
        : this.#io.startedWith === undefined
          ? "Claude Agent is on from Halcyonic's next start."
          : 'Claude Agent is on.';
      // Paid through the cloud provider only where no key is there to pay; with a key, Anthropic's service and the key.
      const keyed = claude.stored || keyIn(this.#io.env, claude.key.variable);
      lines.push(
        keyed || claude.provider === null
          ? `${on} Its models run on a remote service, Anthropic's: nothing starts on one without its second press, and a task on one is paid for with your API key.`
          : `${on} Its models run on a remote service, ${claude.provider}: nothing starts on one without its second press, and a task on one is paid for through your ${claude.provider} account.`,
      );
      const key = this.#fileState(claude.key.file);
      if (key === 'exposed')
        lines.push(
          `Other users can read ${this.#tilde(join(this.#dataDir, claude.key.file))}. Run chmod 600 on it.`,
        );
    } else {
      lines.push('Claude Agent is off.');
    }
    const cloud = ollama?.filter((model) => !model.local) ?? [];
    if (cloud.length > 0) {
      lines.push(
        `Ollama lists ${cloud.length === 1 ? '1 model that runs' : `${cloud.length} models that run`} on a remote service of its own, and the headset says so. Starting Ollama with OLLAMA_NO_CLOUD=1 hides them.`,
      );
    }
    return { title, status: hosted ? 'look' : 'ready', lines };
  }

  #launcherStep(env: NodeJS.ProcessEnv): Step {
    const title = "New apps' browser";
    const about =
      "A new app's check, and a game played on this Mac, run in a browser that starts through Halcyonic's launcher, so it has none of the macOS permissions given to the app that started Halcyonic.";
    const problem = launcherProblem(this.#dataDir) ?? programProblem(this.#dataDir, AUDIO_OUTPUT);
    if (!present(env.HALCYONIC_BROWSER))
      return {
        title,
        status: 'optional',
        lines: ['No browser for new apps is set up.', about],
        next: ['pnpm mac-setup browser'],
      };
    if (problem === null)
      return { title, status: 'ready', lines: ['The browser and its launcher are set up.', about] };
    return {
      title,
      status: 'todo',
      lines: [
        `${problem} Until it is built, checks run without it and games are not played on this Mac.`,
        about,
      ],
      next: ['pnpm mac-setup browser'],
    };
  }

  #starterStep(): Step {
    const title = 'VR starter';
    const about =
      'A new VR app begins from the starter: its files, and a pinned set of packages installed once on this Mac.';
    const starters = new HostStarters({
      trackedDir: TRACKED_STARTERS,
      installedDir: join(this.#dataDir, 'starters'),
    });
    if (starters.check(VR_STARTER).ok)
      return { title, status: 'ready', lines: ["The starter's pinned set is installed.", about] };
    return {
      title,
      status: 'optional',
      lines: ["The starter's pinned set isn't installed, so no new VR app can begin here.", about],
      next: ['pnpm mac-setup starter'],
    };
  }

  /**
   * Whether npm and pnpm are where the host finds a project's package manager: on the PATH it was
   * started with, read once at its start (`managersOn`, ADR 0055). On the setup page that PATH is
   * the running Halcyonic's own; in a Terminal it is the Terminal's.
   */
  #ownChecksStep(): Step {
    const title = "Projects' own checks";
    const about =
      'A project not made from a starter can run its own checks after each task, with the package manager it names. Halcyonic looks for npm and pnpm once, when it starts, on the PATH it was started with.';
    const found = managersOn(this.#io.env.PATH);
    if (found.npm !== null && found.pnpm !== null)
      return {
        title,
        status: 'ready',
        lines: [
          `npm and pnpm are where Halcyonic looks: ${this.#tilde(found.npm)} and ${this.#tilde(found.pnpm)}.`,
          about,
        ],
      };
    const missing = (['npm', 'pnpm'] as const).filter((name) => found[name] === null);
    const named = missing.join(' and ');
    const elsewhere = managersOn(['/opt/homebrew/bin', '/usr/local/bin'].join(delimiter));
    const there = missing.flatMap((name) => {
      const path = elsewhere[name];
      return path === null ? [] : [`${name} is at ${path}, a folder that PATH doesn't name.`];
    });
    return {
      title,
      status: 'optional',
      lines: [
        `${named} ${missing.length === 1 ? "isn't" : "aren't"} where Halcyonic looks, so a project that uses ${missing.length === 1 ? 'it' : 'them'} offers no check of its own.`,
        about,
        ...there,
        'The setup page looks where the running Halcyonic looks; a Terminal may find more. To use them, stop Halcyonic and start it again from a Terminal that finds them.',
      ],
      next: ['pnpm start'],
    };
  }

  #undoStep(env: NodeJS.ProcessEnv): Step {
    const title = 'Undo';
    const about =
      "With undo on, Halcyonic keeps checkpoints of a project's folder before each task and after each turn, so a task's changes can be put back.";
    const git = env.HALCYONIC_GIT_BIN;
    if (!present(git))
      return {
        title,
        status: 'optional',
        lines: ['Undo is off.', about],
        next: ['pnpm mac-setup undo'],
      };
    if (runnableProgram(git) === null)
      return {
        title,
        status: 'look',
        lines: [
          `${this.#tilde(git)} isn't a program this Mac can run, so undo can't use it.`,
          about,
        ],
        next: ['pnpm mac-setup undo'],
      };
    const named =
      git === SYSTEM_GIT
        ? `Undo uses ${this.#tilde(git)}.`
        : `Undo uses ${this.#tilde(git)}. It isn't the system's own git, ${SYSTEM_GIT}.`;
    return { title, status: 'ready', lines: [named, about] };
  }

  async #sharingStep(env: NodeJS.ProcessEnv): Promise<Step> {
    const title = 'Share a Link';
    const about =
      'With it, a checked game can be shared at a link anyone can open, through a temporary tunnel. The link reaches only the checked build, and stops when you stop it.';
    const tunnel = env.HALCYONIC_TUNNEL_BIN;
    if (!present(tunnel))
      return {
        title,
        status: 'optional',
        lines: ['Sharing a link is off.', about],
        next: ['pnpm mac-setup tunnel'],
      };
    if (!(await isPinnedTunnel(tunnel)))
      return {
        title,
        status: 'look',
        lines: [
          `${this.#tilde(tunnel)} isn't the checked tunnel program ${TUNNEL_PIN.version}, so sharing a link stays off.`,
          about,
        ],
        next: ['pnpm mac-setup tunnel'],
      };
    return {
      title,
      status: 'ready',
      lines: [`Sharing a link uses the checked tunnel program ${TUNNEL_PIN.version}.`, about],
    };
  }

  /**
   * Records the tunnel program a shared link runs (ADR 0054), only when its bytes are the checked
   * release's, by default where the setup page downloads it; `off` stops sharing a link. Nothing is
   * downloaded here.
   */
  async tunnel(argument: string | undefined): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    if (argument === 'off') {
      const next = { ...settings, HALCYONIC_TUNNEL_BIN: undefined };
      this.#save(next);
      io.print("Sharing a link is off from Halcyonic's next start.");
      await this.#afterChange(next);
      return 0;
    }
    const program = argument ?? tunnelProgram(this.#dataDir);
    if (!isAbsolute(program)) {
      io.print('Name the tunnel program by its full path.');
      return 1;
    }
    if (!(await isPinnedTunnel(program))) {
      io.print(
        `${this.#tilde(program)} isn't the checked tunnel program ${TUNNEL_PIN.version}, so sharing a link stays off. Download it from the setup page.`,
      );
      return 1;
    }
    const next = { ...settings, HALCYONIC_TUNNEL_BIN: program };
    this.#save(next);
    io.print(
      `Sharing a link is on from Halcyonic's next start, with the checked tunnel program ${TUNNEL_PIN.version}. A game is shared only when you press Share, and only its checked build.`,
    );
    await this.#afterChange(next);
    return 0;
  }

  /**
   * Sets up new apps' browser once: finds the checked chrome-headless-shell in Playwright's cache
   * (or in `folder`), reading only and following no link, copies it into the data folder, records
   * its folder in the settings (never its hash, which is always the repository's pin) and builds
   * the launcher and the sound helper. Nothing is downloaded, and nothing is recorded or copied
   * unless the binary matches the pin.
   */
  async browser(folder: string | undefined): Promise<number> {
    const io = this.#io;
    if (process.platform !== 'darwin') {
      io.print("New apps' browser is for macOS only.");
      return 1;
    }
    const pin = io.pins?.browser ?? null;
    if (pin === null) {
      io.print('There is no checked browser for this Mac, so none is set up.');
      return 1;
    }
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const searched = join(io.home, 'Library', 'Caches', 'ms-playwright');
    const candidates =
      folder === undefined ? playwrightShells(searched) : [resolve(expandHome(folder, io.home))];
    let found: string | null = null;
    for (const candidate of candidates) {
      if (isPinnedShell(candidate, pin.sha256)) {
        found = candidate;
        break;
      }
    }
    if (found === null) {
      io.print(
        folder === undefined
          ? `There is no checked browser in ${this.#tilde(searched)}.`
          : `${this.#tilde(candidates[0] ?? folder)} doesn't hold the checked browser.`,
      );
      io.print(
        'Halcyonic needs chrome-headless-shell 156.0.8078.4 for Apple silicon, the build Playwright keeps in a folder named chromium_headless_shell-1248. Install it with Playwright yourself, then run pnpm mac-setup browser again. Nothing was set up.',
      );
      return 1;
    }
    try {
      await installBrowser(found, pin.sha256, this.#dataDir);
    } catch {
      io.print(
        "The browser couldn't be copied into Halcyonic's data folder. Nothing was recorded.",
      );
      return 1;
    }
    const next = { ...settings, HALCYONIC_BROWSER: found };
    this.#save(next);
    io.print(
      `The browser is set up from ${this.#tilde(found)}, copied into ${this.#tilde(join(this.#dataDir, 'browser', 'current'))}. pnpm dev needs no variables for it.`,
    );
    const built = await this.launcher();
    if (built !== 0) return built;
    await this.#afterChange(next);
    return 0;
  }

  /** Builds the launcher from its tracked source with Apple's compiler (S1). */
  async launcher(): Promise<number> {
    const io = this.#io;
    if (process.platform !== 'darwin') {
      io.print('The launcher is for macOS only.');
      return 1;
    }
    try {
      const sha256 = await installLauncher({ dataDir: this.#dataDir, clang: '/usr/bin/clang' });
      const sound = await installAudioOutput({ dataDir: this.#dataDir, clang: '/usr/bin/clang' });
      io.print(
        `The launcher is built in ${this.#tilde(launcherPath(this.#dataDir))} (SHA-256 ${sha256.slice(0, 12)}), with the helper that tells whether a game sends sound out (SHA-256 ${sound.slice(0, 12)}). From Halcyonic's next start, new apps' browser runs without this app's macOS permissions.`,
      );
      return 0;
    } catch {
      io.print(
        "The launcher didn't build. It needs Apple's command line tools: run xcode-select --install, then try again.",
      );
      return 1;
    }
  }

  /**
   * Records the VR starter's installed set once every package in it is the version the tracked
   * lockfile pins (ADR 0037), or says how to install it: the person runs npm, from the public registry
   * only, with no install scripts and no npm settings of theirs; Halcyonic downloads nothing.
   */
  async starter(): Promise<number> {
    const io = this.#io;
    const installed = join(this.#dataDir, 'starters');
    const folder = join(installed, VR_STARTER);
    const set = installedSet(TRACKED_STARTERS, installed, VR_STARTER);
    if (set.state === 'matches') {
      recordInstalled(installed, VR_STARTER, set.lockfile);
      io.print(
        `The VR starter is set up: ${set.packages} packages in ${this.#tilde(folder)}, each the version its lockfile pins. New VR apps can begin from it now.`,
      );
      return 0;
    }
    const lock = join(TRACKED_STARTERS, VR_STARTER, 'package-lock.json');
    if (set.state === 'missing')
      io.print(
        "The VR starter isn't set up on this Mac. Install its pinned set from the public npm registry, with no install scripts and none of your own npm settings, then run this again:",
      );
    else if (set.state === 'not_a_folder')
      io.print(
        `${this.#tilde(folder)} is a link or a file, not a folder of its own, so the VR starter isn't set up from it. Move it away, install the set again, then run this again:`,
      );
    else
      io.print(
        `The VR starter's packages in ${this.#tilde(folder)} aren't the ones its lockfile pins (${set.differs.slice(0, 5).join(', ')}${set.differs.length > 5 ? ` and ${set.differs.length - 5} more` : ''}). Install them again, then run this again:`,
      );
    const at = (path: string) => shellPath(path, io.home);
    for (const command of [
      `mkdir -p ${at(folder)}`,
      `cp ${at(join(TRACKED_STARTERS, VR_STARTER, 'package.json'))} ${at(lock)} ${at(folder)}`,
      `npm ci --prefix ${at(folder)} --ignore-scripts --no-audit --no-fund --userconfig /dev/null --globalconfig /dev/null --cache "$(mktemp -d)" --registry https://registry.npmjs.org/`,
      'pnpm mac-setup starter',
    ])
      io.print(`     ${command}`);
    return 1;
  }

  /** Turns undo on with the system's git or the one named, off with `off` (ADR 0036). */
  async undo(argument: string | undefined): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    if (argument === 'off') {
      const next: Record<string, string | undefined> = {
        ...settings,
        HALCYONIC_GIT_BIN: undefined,
      };
      this.#save(next);
      io.print(
        `Undo is off from Halcyonic's next start. The checkpoints it kept stay in ${this.#tilde(join(this.#dataDir, 'checkpoints'))}.`,
      );
      await this.#afterChange(next);
      return 0;
    }
    const git = argument ?? SYSTEM_GIT;
    if (!isAbsolute(git)) {
      io.print(`Name git by its full path, such as ${SYSTEM_GIT}.`);
      return 1;
    }
    const program = runnableProgram(git);
    if (program === null) {
      io.print(
        `${this.#tilde(git)} isn't a program this Mac can run, so undo stays off. On a Mac, ${SYSTEM_GIT} comes with Apple's command line tools: xcode-select --install.`,
      );
      return 1;
    }
    // The program itself, after every link, so a link pointed elsewhere later changes nothing (AUDIT SETUP-4).
    const next = { ...settings, HALCYONIC_GIT_BIN: program };
    this.#save(next);
    io.print(
      `Undo is on from Halcyonic's next start, with ${this.#tilde(program)}: it keeps checkpoints of a project's folder in ${this.#tilde(join(this.#dataDir, 'checkpoints'))}, before each task and after each turn, and can put the folder back as one holds it.`,
    );
    if (program !== SYSTEM_GIT)
      io.print(
        `${this.#tilde(program)} isn't the system's own git, ${SYSTEM_GIT}. Undo runs it for every checkpoint, so use it only if you trust it.`,
      );
    await this.#afterChange(next);
    return 0;
  }

  async #voiceStep(env: NodeJS.ProcessEnv): Promise<Step> {
    const title = 'Voice';
    const configured = [
      env.HALCYONIC_WHISPER_BIN,
      env.HALCYONIC_WHISPER_MODEL,
      env.HALCYONIC_WHISPER_VAD_MODEL,
    ];
    const about =
      "With voice, the headset's Hold to talk turns speech into text on this Mac, as a draft you check before it is sent. Nothing you say leaves the Mac or is kept.";
    if (configured.every(present)) {
      const pins = this.#io.pins;
      const model = await this.#matches(configured[1] as string, pins?.whisperModel ?? null);
      const vad = await this.#matches(configured[2] as string, pins?.whisperVadModel ?? null);
      if (model === 'differs' || vad === 'differs') {
        return {
          title,
          status: 'look',
          lines: [
            "Voice is set up, but its model files aren't the ones Halcyonic was checked with.",
            about,
          ],
        };
      }
      return { title, status: 'ready', lines: ['Voice is set up.', about] };
    }
    const standard = await this.#standardVoiceFiles();
    return {
      title,
      status: 'optional',
      lines: [
        "Voice isn't set up. The headset then offers typing only, and says “Voice isn't set up on your computer.”",
        about,
        ...(standard === null
          ? [
              'Setting it up builds whisper.cpp and downloads two model files (548 MB): see “Turn on voice” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.',
            ]
          : []),
      ],
      ...(standard === null ? {} : { next: ['pnpm mac-setup voice'] }),
    };
  }

  async #companionStep(
    env: NodeJS.ProcessEnv,
    ollama: OllamaModel[] | null,
    running: Running,
  ): Promise<Step> {
    const title = 'Companion';
    const about =
      "The companion helps shape an idea into a project and its first task in Create, on a model this Mac's Ollama serves. This Mac keeps nothing of what you tell it; the headset keeps the draft.";
    const alongside = `Start Ollama with OLLAMA_MAX_LOADED_MODELS=2, so the companion's model can stay loaded beside the agents' and answer while a task works; with one, they unload each other and the headset says “The companion took too long. Your computer's model may be busy with a task. Try again, or go on without it.” This check can't read Ollama's own settings, so it only recommends this.`;
    const proxy =
      present(this.#io.env.http_proxy) || present(this.#io.env.HTTP_PROXY)
        ? [
            "A proxy is set in this environment. If Node's environment proxy is on (NODE_USE_ENV_PROXY), name Ollama's address in NO_PROXY, or Halcyonic won't start with the companion.",
          ]
        : [];
    const named = env.HALCYONIC_COMPANION_MODEL;
    if (!present(named)) {
      const pulled =
        ollama?.some((model) => model.local && model.name === COMPANION_MODEL.name) === true;
      return {
        title,
        status: 'optional',
        lines: [
          "The companion isn't set up. The headset then offers typing your idea or a few fixed questions, and says “The companion isn't set up on your computer. Type your idea, or answer a few fixed questions.”",
          about,
          `Halcyonic's companion was tried with ${COMPANION_MODEL.name}, a model of its own: a ${COMPANION_MODEL.download} download that uses ${COMPANION_MODEL.memory} of memory while it is loaded.`,
          alongside,
          ...proxy,
        ],
        next: [
          ...(pulled ? [] : [`ollama pull ${COMPANION_MODEL.name}`]),
          `pnpm mac-setup companion ${COMPANION_MODEL.name}`,
        ],
      };
    }
    const cantRun =
      "The headset says “The companion can't run on your computer right now. Type your idea, or answer a few fixed questions.”";
    // Checked before anything asks it or prints it: an address with credentials in it would print them.
    let address: string;
    try {
      address = companionOllamaAddress(env.HALCYONIC_COMPANION_OLLAMA_URL).origin;
    } catch (error) {
      if (!(error instanceof ConfigError)) throw error;
      return { title, status: 'todo', lines: [`${error.message} ${cantRun}`, about] };
    }
    const listed =
      address === this.#io.ollamaUrl.replace(/\/$/, '') ? ollama : await this.#ollama(address);
    const wanted = named.includes(':') ? [named] : [named, `${named}:latest`];
    const model = listed?.find((candidate) => wanted.includes(candidate.name));
    const lines: string[] = [];
    let status: Status = 'ready';
    let next: string[] = [];
    if (listed === null) {
      status = 'todo';
      lines.push(`Ollama isn't answering at ${address}, so the companion can't run. ${cantRun}`);
    } else if (model === undefined) {
      status = 'todo';
      lines.push(
        `Ollama on this Mac doesn't have ${named}, so the companion can't run. ${cantRun}`,
      );
      next = [`ollama pull ${named}`];
    } else if (!model.local) {
      status = 'todo';
      lines.push(
        `${named} runs on a remote service of Ollama's, not on this Mac, so the companion won't use it. ${cantRun}`,
      );
      next = [`pnpm mac-setup companion ${COMPANION_MODEL.name}`];
    } else {
      lines.push(`The companion asks ${named} on this Mac.`);
    }
    const agents = this.#agentsModel(env);
    if (agents !== null && wanted.includes(agents)) {
      if (status === 'ready') status = 'look';
      lines.push(
        `${named} is also the agents' model, so the companion waits for each step of a task that works on it. A model of its own, such as ${COMPANION_MODEL.name}, answers sooner.`,
      );
    }
    const reading = running.companion;
    if (reading !== null && reading.availability !== 'available' && status === 'ready') {
      status = 'todo';
      lines.push(
        `Halcyonic as it runs says the companion can't be asked: ${companionProblem(reading.code)}`,
      );
    }
    lines.push(about, alongside, ...proxy);
    return { title, status, lines, next };
  }

  /** The Ollama model Halcyonic's own OpenCode settings name as the agents' default, if any. */
  #agentsModel(env: NodeJS.ProcessEnv): string | null {
    const home = env.HALCYONIC_OPENCODE_CONFIG_HOME;
    if (!present(home)) return null;
    try {
      return readOpenCodeSettings(home).model.replace(/^ollama\//, '');
    } catch {
      return null;
    }
  }

  #usageLeftStep(running: Running): Step {
    const title = 'Usage left';
    const path = join(this.#dataDir, SEORAK_CREDENTIAL_FILE);
    const about =
      "Usage left shows on the headset how much of your providers' usage limits is left, as Seorak last saw it on this Mac.";
    const state = this.#fileState(SEORAK_CREDENTIAL_FILE);
    if (state === 'missing') {
      return {
        title,
        status: 'optional',
        lines: [
          "Usage left isn't set up. The headset says “Usage left isn't set up on your computer yet.”",
          about,
          `It needs Seorak running on this Mac and a credential you issue in Seorak's dashboard, saved as ${this.#tilde(path)} with mode 600. Move it as a file, never through the clipboard or a chat: see “Connect Seorak” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        ],
      };
    }
    if (state === 'exposed') {
      return {
        title,
        status: 'todo',
        lines: [`Other users can read ${this.#tilde(path)}, so Halcyonic won't use it.`],
        next: [`chmod 600 ${quote(this.#tilde(path))}`],
      };
    }
    const reading = running.usageLeft;
    if (reading === null) {
      return {
        title,
        status: 'unknown',
        lines: [
          'A Seorak credential is saved. Whether Seorak accepts it shows when Halcyonic runs and you check with pnpm mac-setup --with-token.',
          about,
          ...this.#seorakOwnSessions(),
        ],
        next: this.#seorakOwnSessionsNext(),
      };
    }
    if (reading.availability === 'available')
      return {
        title,
        status: 'ready',
        lines: ['Usage left is set up.', about, ...this.#seorakOwnSessions()],
        next: this.#seorakOwnSessionsNext(),
      };
    return { title, status: 'todo', lines: [usageLeftProblem(reading.code), about] };
  }

  /**
   * What Seorak needs to read Halcyonic's own Codex tasks for their Checks: one setting, in a Seorak
   * that has it (not released on 2026-11-11). Seorak publishes no version to tell by, and whether it
   * was set this Mac cannot see. Seorak reads no OpenCode tasks.
   */
  #seorakOwnSessions(): string[] {
    return [
      "For Halcyonic's own Codex tasks, Seorak needs to be told where they are: run the line below in Terminal, with a Seorak that has this setting. It lets Seorak read those sessions beside its own. This Mac can't see whether you ran it: until you do, those tasks' Checks say they aren't set up yet. Seorak doesn't read OpenCode tasks.",
    ];
  }

  #seorakOwnSessionsNext(): string[] {
    const data = this.#forShell(this.#dataDir);
    return [
      `SEORAK_CODEX_EXTRA_DIRS="${data}/${CODEX_HOME_FOLDER}/sessions:${data}/${CODEX_CONFINED_FOLDER}/*/sessions" seorak setup`,
    ];
  }

  async #understandStep(): Promise<Step> {
    const title = 'What changed and why';
    const path = join(this.#dataDir, SALIDIUM_CREDENTIAL_FILE);
    const about =
      "Salidium tells the headset's Understand tab what a task changed and why. Halcyonic keeps its own Codex and OpenCode tasks in folders of its own, which Salidium reads only once it is told where they are.";
    const state = this.#fileState(SALIDIUM_CREDENTIAL_FILE);
    if (state === 'missing') {
      return {
        title,
        status: 'optional',
        lines: [
          'Not set up.',
          about,
          `It needs Salidium running on this Mac and a credential saved as ${this.#tilde(path)} with mode 600: see “Connect Salidium” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        ],
      };
    }
    if (state === 'exposed') {
      return {
        title,
        status: 'todo',
        lines: [`Other users can read ${this.#tilde(path)}, so Halcyonic won't use it.`],
        next: [`chmod 600 ${quote(this.#tilde(path))}`],
      };
    }
    const own = await this.#salidiumOwnSessions();
    return {
      title,
      status: 'ready',
      lines: [
        "A Salidium credential is saved. Each task's Understand tab says whether Salidium could answer.",
        about,
        ...own.lines,
      ],
      ...(own.next === undefined ? {} : { next: own.next }),
    };
  }

  /**
   * Whether the installed Salidium can read Halcyonic's own Codex and OpenCode tasks, by its version
   * in the file it publishes (read without the credential): only a Salidium after 0.8.3 can, and only
   * once told where they are, by the commands given. Whether it was told this Mac cannot see; a task's
   * Understand tab says "not set up yet" until it is.
   */
  async #salidiumOwnSessions(): Promise<{ lines: string[]; next?: string[] }> {
    const version = await new SalidiumClient({
      home: defaultSalidiumHome(this.#io.env, this.#io.home),
      credential: null,
      timeoutMs: 2_000,
      budgetMs: 3_000,
    })
      .version()
      .catch(() => null);
    if (version === null)
      return {
        lines: [
          "Salidium isn't answering on this Mac now, so whether it reads Halcyonic's own Codex and OpenCode tasks can't be told.",
        ],
      };
    if (!readsOtherToolsSources(version))
      return {
        lines: [
          `Salidium ${version}, as installed, doesn't read Halcyonic's own Codex and OpenCode tasks: their Understand tab says so. A Salidium after ${LAST_OWN_SOURCES_ONLY} can.`,
        ],
      };
    const data = this.#forShell(this.#dataDir);
    return {
      lines: [
        `Salidium ${version} can read Halcyonic's own Codex and OpenCode tasks once it is told where they are: run the lines below in Terminal. They let it read those tasks' sessions, read only, beside its own; OpenCode's only while Salidium's OpenCode provider is on.`,
        "This Mac can't see whether you ran them: until you do, those tasks' Understand tab says it isn't set up yet.",
      ],
      next: [
        `export SALIDIUM_EXTRA_CODEX_HOMES="${data}/${CODEX_HOME_FOLDER}:${data}/${CODEX_CONFINED_FOLDER}/*"`,
        `export SALIDIUM_EXTRA_OPENCODE_STORES="${sandboxedSessionStore(`${data}/${OPENCODE_SANDBOX_FOLDER}`)}:${sandboxedSessionStore(`${data}/${OPENCODE_SANDBOX_FOLDER}-*`)}"`,
        'salidium service install',
      ],
    };
  }

  /** A folder as a shell line names it: under the home folder as `$HOME/...`, quoted by the caller. */
  #forShell(path: string): string {
    const home = this.#io.home;
    return path === home || path.startsWith(`${home}${sep}`)
      ? `$HOME${path.slice(home.length)}`
      : path;
  }

  #catalogStep(): Step {
    const title = 'Your other projects';
    const path = join(this.#dataDir, OMNORIUM_CREDENTIAL_FILE);
    const about =
      'Omnorium shows the headset every project you have across your accounts, beside the ones Halcyonic made.';
    const state = this.#fileState(OMNORIUM_CREDENTIAL_FILE);
    if (state === 'missing') {
      return {
        title,
        status: 'optional',
        lines: [
          'Not set up.',
          about,
          `It needs Omnorium running on this Mac and a credential saved as ${this.#tilde(path)} with mode 600: see “Connect Omnorium” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        ],
      };
    }
    if (state === 'exposed') {
      return {
        title,
        status: 'todo',
        lines: [`Other users can read ${this.#tilde(path)}, so Halcyonic won't use it.`],
        next: [`chmod 600 ${quote(this.#tilde(path))}`],
      };
    }
    return {
      title,
      status: 'ready',
      lines: [
        'An Omnorium credential is saved. The headset says whether Omnorium could answer when you open your projects.',
        about,
      ],
    };
  }

  #runningStep(env: NodeJS.ProcessEnv, running: Running): Step {
    const title = 'Halcyonic running on this Mac';
    if (!running.answering) {
      return {
        title,
        status: 'todo',
        lines: [
          "Halcyonic isn't running. Start it, and leave that window open while you use the headset. The headset says “Can't reach your computer” until it runs.",
        ],
        next: ['pnpm start'],
      };
    }
    if (running.tokenProblem !== null) {
      return {
        title,
        status: 'look',
        lines: [
          running.tokenProblem === 'missing'
            ? `Something answers on Halcyonic's port, but there is no access token in ${this.#tilde(this.#dataDir)}. Another copy of Halcyonic, with other data, may be running.`
            : "Something answers on Halcyonic's port but can't prove it holds this Mac's access token, so the token was not sent. Another copy of Halcyonic with other data, or another program or account on this Mac, may be listening there.",
        ],
      };
    }
    if (running.roots === null) {
      return {
        title,
        status: 'ready',
        lines: [
          'Halcyonic is running. To check which folders and agent apps it runs with, use pnpm mac-setup --with-token: it reads the access token and sends it only after Halcyonic proves it holds it.',
        ],
      };
    }
    const configuredRoots = (env.HALCYONIC_PROJECT_ROOTS ?? '')
      .split(delimiter)
      .filter((root) => root !== '')
      .map(realOrSelf)
      .sort();
    const runningRoots = [...running.roots].sort();
    const configuredApps = [
      ...(present(env.HALCYONIC_OPENCODE_BIN) ? ['opencode'] : []),
      ...(present(env.HALCYONIC_CODEX_BIN) ? ['codex'] : []),
    ];
    const runningApps = (running.agentApps ?? []).filter(
      (app) => app === 'opencode' || app === 'codex',
    );
    const same =
      sameList(configuredRoots, runningRoots) &&
      sameList(configuredApps.sort(), [...runningApps].sort());
    if (same)
      return { title, status: 'ready', lines: ['Halcyonic is running with these settings.'] };
    return {
      title,
      status: 'look',
      lines: [
        'Halcyonic is running with other folders or agent apps than these settings. Restart it to use them: stop it where it runs with Ctrl-C, then start it again.',
        RESTART_WARNING,
      ],
      next: ['pnpm start'],
    };
  }

  #headsetStep(env: NodeJS.ProcessEnv, running: Running): Step {
    const title = 'Your headset';
    const demo =
      'Until it reaches this Mac, the headset plays a recorded demo, labelled as one; nothing in it reaches an agent.';
    const usb =
      'A development build can also connect over USB instead: see “Install and connect” in docs/internal/runbooks/XR_DEVELOPMENT.md.';
    if (!present(env.HALCYONIC_NETWORK_HOST)) {
      return {
        title,
        status: 'unknown',
        lines: [
          "This check can't tell whether a headset is connected over USB, as a development build can be: see “Install and connect” in docs/internal/runbooks/XR_DEVELOPMENT.md.",
          'Pairing over Wi-Fi is off. Turning it on is your choice: pnpm mac-setup pairing on says what it opens, and asks before it changes anything.',
          demo,
        ],
      };
    }
    const lines: string[] = [];
    let status: Status = 'unknown';
    const firewall = this.#io.firewall();
    if (firewall?.blockAll === true) {
      status = 'todo';
      lines.push(
        "This Mac's firewall blocks all incoming connections, so no headset can reach it. Allowing them is in System Settings, Network, Firewall, Options.",
      );
    } else if (firewall?.enabled === true) {
      lines.push(
        'The first time Halcyonic listens, macOS asks whether node may accept incoming connections: allow it.',
      );
    }
    const devices = running.devices;
    if (devices === null) {
      lines.push(
        'Pairing over Wi-Fi is on. Whether a headset is paired shows when Halcyonic runs and you check with pnpm mac-setup --with-token, or with pnpm devices.',
      );
    } else {
      const kept = devices.devices.filter((device) => device.revoked_at === null);
      if (kept.length === 0) {
        if (status !== 'todo') status = 'todo';
        lines.push(
          'Pairing over Wi-Fi is on, and no headset is paired yet. Pair one: in the headset, Settings, Your computer, Pair with a computer, then enter the address and code pnpm pair shows.',
        );
      } else {
        if (status !== 'todo') status = 'ready';
        for (const device of kept) {
          const connected = devices.connected.includes(device.device_id);
          lines.push(
            `${printable(device.label)} is paired${connected ? ' and connected' : ''}. To stop it for good: pnpm devices revoke ${device.device_id}`,
          );
        }
      }
    }
    lines.push(usb, demo);
    return {
      title,
      status,
      lines,
      ...(status === 'todo' && devices !== null ? { next: ['pnpm pair'] } : {}),
    };
  }

  // The actions

  async allow(folder: string | undefined): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const isDefault = folder === undefined;
    const target = isDefault
      ? join(io.home, DEFAULT_PROJECTS_FOLDER)
      : resolve(expandHome(folder, io.home));
    if (target.includes(delimiter)) {
      io.print(
        `${target} has a “${delimiter}” in its name, which Halcyonic's settings can't hold. Choose another folder.`,
      );
      return 1;
    }
    const exists = existsSync(target);
    if (exists && !isDirectory(target)) {
      io.print(`${this.#tilde(target)} isn't a folder.`);
      return 1;
    }
    if (!exists && !isDefault) {
      io.print(
        `${this.#tilde(target)} isn't there. Make it first, or run pnpm mac-setup allow with no folder to make ~/${DEFAULT_PROJECTS_FOLDER}.`,
      );
      return 1;
    }
    if (!exists && !isDirectory(dirname(target))) {
      io.print(
        `${this.#tilde(dirname(target))} isn't there, so ${this.#tilde(target)} can't be made.`,
      );
      return 1;
    }
    const real = exists
      ? realpathSync.native(target)
      : join(realpathSync.native(dirname(target)), DEFAULT_PROJECTS_FOLDER);
    const assessment = assessFolder(real, this.#folders());
    if (assessment.verdict === 'refused') {
      io.print(`${this.#tilde(real)} can't be allowed. ${assessment.reason}`);
      io.print('Allow a folder you keep for projects instead, such as ~/HalcyonicProjects.');
      return 1;
    }
    const roots = settingRoots(settings);
    if (roots.some((root) => realOrSelf(root) === real)) {
      io.print(`${this.#tilde(real)} is already allowed.`);
      return 0;
    }
    io.print(`Allowing ${this.#tilde(real)}${exists ? '' : ', a new empty folder'}:`);
    for (const line of FOLDER_MEANING.slice(0, 2))
      for (const wrapped of wrap(line, 2)) io.print(wrapped);
    if (assessment.verdict === 'broad')
      for (const wrapped of wrap(assessment.reason, 2)) io.print(wrapped);
    if (
      !(await this.#confirm(`Allow agents to read and change everything in ${this.#tilde(real)}?`))
    )
      return 1;
    if (!exists) mkdirSync(real);
    const next = { ...settings, HALCYONIC_PROJECT_ROOTS: [...roots, real].join(delimiter) };
    this.#save(next);
    io.print(`Allowed ${this.#tilde(real)}.`);
    if (io.env.HALCYONIC_PROJECT_ROOTS !== undefined) {
      io.print(
        'HALCYONIC_PROJECT_ROOTS in the environment still wins over this setting while it is set.',
      );
    }
    await this.#afterChange(next);
    return 0;
  }

  async disallow(folder: string): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const target = resolve(expandHome(folder, io.home));
    const real = realOrSelf(target);
    const roots = settingRoots(settings);
    const kept = roots.filter((root) => root !== target && realOrSelf(root) !== real);
    if (kept.length === roots.length) {
      io.print(`${this.#tilde(target)} isn't one of the folders Halcyonic's settings allow.`);
      return 1;
    }
    const next: Record<string, string | undefined> = { ...settings };
    next.HALCYONIC_PROJECT_ROOTS = kept.length === 0 ? undefined : kept.join(delimiter);
    this.#save(next);
    io.print(
      `Agents may no longer use ${this.#tilde(target)} once Halcyonic restarts. Nothing in it is deleted. A project there keeps its folder, but can't start work until you allow the folder again.`,
    );
    await this.#afterChange(next);
    return 0;
  }

  async agentApps(): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const pins = io.pins;
    if (pins === null) {
      io.print(
        "Halcyonic has checksums for Apple silicon Macs only, so it can't check agent apps on this Mac.",
      );
      return 1;
    }
    const next: Record<string, string | undefined> = { ...settings };
    let recorded = 0;
    for (const [app, name, variable] of [
      ['opencode', 'OpenCode 2.0.18', 'HALCYONIC_OPENCODE_BIN'],
      ['codex', 'Codex 0.157.0', 'HALCYONIC_CODEX_BIN'],
    ] as const) {
      const pin = pins[app];
      const path = join(this.#dataDir, pin.path);
      if (!existsSync(path)) {
        io.print(
          `${name} isn't installed for Halcyonic: see “Run real agents” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        );
        continue;
      }
      if ((await this.#matches(path, pin)) !== 'matches') {
        io.print(
          `${name} at ${this.#tilde(path)} isn't the copy Halcyonic was checked with, so it isn't set up. Install it again.`,
        );
        continue;
      }
      next[variable] = path;
      recorded += 1;
      io.print(`${name} is set up: the copy Halcyonic was checked with.`);
    }
    if (recorded === 0) return 1;
    this.#save(next);
    await this.#afterChange(next);
    return 0;
  }

  async localModel(name: string): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const ollama = await this.#ollama();
    if (ollama === null) {
      io.print(
        "Ollama isn't answering on this Mac, so Halcyonic can't check the model. Start Ollama, then try again.",
      );
      return 1;
    }
    const model =
      ollama.find((candidate) => candidate.name === name) ??
      (name.includes(':')
        ? undefined
        : ollama.find((candidate) => candidate.name === `${name}:latest`));
    if (model === undefined) {
      io.print(
        `Ollama on this Mac doesn't have ${name}. Download it with ollama pull ${name}, or choose one it has:`,
      );
      for (const candidate of ollama.filter((entry) => entry.local && entry.tools))
        io.print(`  ${candidate.name}`);
      return 1;
    }
    if (!model.local || !isLocalOllamaModel(`ollama/${model.name}`)) {
      io.print(
        `${model.name} runs on a remote service of Ollama's, not on this Mac, so it can't be OpenCode's default here.`,
      );
      return 1;
    }
    if (!model.tools) {
      io.print(
        `${model.name} can't use tools, so agents can't work with it. Choose another model.`,
      );
      return 1;
    }
    if (!CODEX_MODEL_NAME.test(model.name)) {
      io.print(`${model.name} isn't a model name Codex can take. Choose another model.`);
      return 1;
    }
    const codexHome = join(this.#dataDir, CODEX_HOME_FOLDER);
    const codexProblem = codexHomeProblem(codexHome);
    if (codexProblem !== null) {
      io.print(codexProblem);
      return 1;
    }
    const home = join(this.#dataDir, 'opencode-config');
    const folder = join(home, 'opencode');
    for (const other of ['opencode.jsonc', 'config.json']) {
      if (existsSync(join(folder, other))) {
        io.print(
          `${this.#tilde(join(folder, other))} would also be read by OpenCode. Move it away, then try again.`,
        );
        return 1;
      }
    }
    const replaced = existsSync(join(home, OPENCODE_SETTINGS_PATH));
    const limits: Record<string, unknown> = {};
    for (const local of ollama.filter((entry) => entry.local && entry.tools)) {
      limits[local.name] = { limit: { context: OLLAMA_CONTEXT, output: 16_384 } };
    }
    const document = {
      model: `ollama/${model.name}`,
      small_model: `ollama/${model.name}`,
      permissions: [
        { action: 'shell', resource: '*', effect: 'ask' },
        { action: 'webfetch', resource: '*', effect: 'deny' },
        { action: 'websearch', resource: '*', effect: 'deny' },
      ],
      providers: { ollama: { models: limits } },
    };
    mkdirSync(folder, { recursive: true, mode: 0o700 });
    chmodSync(home, 0o700);
    chmodSync(folder, 0o700);
    writePrivate(join(home, OPENCODE_SETTINGS_PATH), `${JSON.stringify(document, null, 2)}\n`);
    const codexReplaced = existsSync(join(codexHome, 'config.toml'));
    if (lstatOrNull(codexHome) === null) {
      mkdirSync(this.#dataDir, { recursive: true, mode: 0o700 });
      mkdirSync(codexHome, { mode: 0o700 });
    }
    writePrivate(join(codexHome, 'config.toml'), codexConfig(model.name));
    const next = { ...settings, HALCYONIC_OPENCODE_CONFIG_HOME: home };
    this.#save(next);
    io.print(
      `${replaced ? "Replaced Halcyonic's own OpenCode settings" : 'OpenCode now has settings of its own for Halcyonic'}: ${model.name} on this Mac when no model is chosen, and no fetching from the web. Your own OpenCode settings are left as they are.`,
    );
    io.print(
      `${codexReplaced ? "Replaced Halcyonic's own Codex settings" : 'Codex now has settings of its own for Halcyonic, in a folder of its own'}: ${model.name} on this Mac. Your own Codex settings are left as they are.`,
    );
    io.print(
      `Start Ollama with OLLAMA_CONTEXT_LENGTH=${OLLAMA_CONTEXT}, the context these settings tell OpenCode and Codex to expect.`,
    );
    if (model.bytes > io.memoryBytes * 0.6) {
      io.print(
        `${model.name} is ${gigabytes(model.bytes)} GB and may not fit beside your other apps in this Mac's ${gigabytes(io.memoryBytes)} GB of memory.`,
      );
    }
    await this.#afterChange(next);
    return 0;
  }

  async voice(): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const files = await this.#standardVoiceFiles();
    if (files === null) {
      io.print(
        "Voice's files aren't all in place and checked: see “Turn on voice” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.",
      );
      return 1;
    }
    const next = {
      ...settings,
      HALCYONIC_WHISPER_BIN: files.binary,
      HALCYONIC_WHISPER_MODEL: files.model,
      HALCYONIC_WHISPER_VAD_MODEL: files.vadModel,
    };
    this.#save(next);
    io.print(
      'Voice is set up: Hold to talk turns speech into text on this Mac, as a draft you check before it is sent.',
    );
    await this.#afterChange(next);
    return 0;
  }

  async companion(name: string): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const next: Record<string, string | undefined> = { ...settings };
    if (name === 'off') {
      next.HALCYONIC_COMPANION_MODEL = undefined;
      next.HALCYONIC_COMPANION_OLLAMA_URL = undefined;
      this.#save(next);
      io.print(
        'The companion turns off when Halcyonic restarts. The headset then offers typing your idea or a few fixed questions. Nothing is removed from Ollama.',
      );
      await this.#afterChange(next);
      return 0;
    }
    if (isCloudName(name)) {
      io.print(
        `${name} runs on a remote service of Ollama's, not on this Mac, so the companion can't use it.`,
      );
      return 1;
    }
    // Checked before anything asks it or prints it: an address with credentials in it would print them.
    let address: string;
    try {
      address = companionOllamaAddress(settings.HALCYONIC_COMPANION_OLLAMA_URL).origin;
    } catch (error) {
      if (!(error instanceof ConfigError)) throw error;
      io.print(
        `${error.message} Change it in ${this.#tilde(join(this.#dataDir, SETTINGS_FILE))}, then try again.`,
      );
      return 1;
    }
    const ollama = await this.#ollama(address);
    if (ollama === null) {
      io.print(
        `Ollama isn't answering at ${address}, so Halcyonic can't check the model. Start Ollama, then try again.`,
      );
      return 1;
    }
    const wanted = name.includes(':') ? [name] : [name, `${name}:latest`];
    const model = ollama.find((candidate) => wanted.includes(candidate.name));
    if (model === undefined) {
      io.print(
        `Ollama on this Mac doesn't have ${name}. Downloading it is for you to run: ollama pull ${name}`,
      );
      return 1;
    }
    if (!model.local) {
      io.print(
        `${model.name} runs on a remote service of Ollama's, not on this Mac, so the companion can't use it.`,
      );
      return 1;
    }
    next.HALCYONIC_COMPANION_MODEL = model.name;
    this.#save(next);
    for (const line of [
      `The companion asks ${model.name} on this Mac once Halcyonic restarts. This Mac keeps nothing of what you tell it; the headset keeps the draft.`,
      "Start Ollama with OLLAMA_MAX_LOADED_MODELS=2, so the companion's model can stay loaded beside the agents' and answer while a task works.",
      ...(model.name !== COMPANION_MODEL.name
        ? [
            `Halcyonic's companion was tried with ${COMPANION_MODEL.name}; another model may answer differently.`,
          ]
        : []),
    ]) {
      for (const wrapped of wrap(line, 0)) io.print(wrapped);
    }
    const agents = this.#agentsModel(withSettings(io.env, next as HostSettings));
    if (agents !== null && agents === model.name) {
      io.print(
        `${model.name} is also the agents' model, so the companion waits for each step of a task that works on it.`,
      );
    }
    await this.#afterChange(next);
    return 0;
  }

  async pairing(state: 'on' | 'off'): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const next: Record<string, string | undefined> = { ...settings };
    if (state === 'on') {
      // The listener's port follows the control plane's own (ADR 0047, amended): 47801 beside 47800.
      const own = Number.parseInt(io.env.HALCYONIC_PORT ?? '', 10);
      const port =
        io.env.HALCYONIC_NETWORK_PORT ??
        String(portFamily(Number.isInteger(own) && own > 0 ? own : DEFAULT_PORT).network);
      io.print('Pairing over Wi-Fi is your choice. Turning it on:');
      const firewall = io.firewall();
      for (const line of [
        `opens a second listener, encrypted, on port ${port}, to every device on your network, from the next time Halcyonic starts. Without pairing, anything that reaches it gets no further than a health check.`,
        'lets a device try to pair only while pnpm pair runs, for five minutes, with the eight-digit code it shows; three wrong codes close it. Anyone on your network can try in that time, so pair where you trust the network.',
        'lets a paired headset start and steer agents in the folders you allow, until you revoke it with pnpm devices revoke.',
        ...(firewall?.blockAll === true
          ? [
              "This Mac's firewall blocks all incoming connections, so no headset can reach it until you allow them in System Settings, Network, Firewall, Options.",
            ]
          : firewall?.enabled === true
            ? [
                'The first time Halcyonic listens, macOS asks whether node may accept incoming connections: allow it, or no headset can reach it.',
              ]
            : []),
      ]) {
        for (const wrapped of wrap(line, 2)) io.print(wrapped);
      }
      if (!(await this.#confirm('Turn pairing over Wi-Fi on?'))) return 1;
      next.HALCYONIC_NETWORK_HOST = '0.0.0.0';
      this.#save(next);
      for (const line of [
        'Pairing over Wi-Fi turns on when Halcyonic restarts.',
        'Then run pnpm pair, and in the headset choose Settings, Your computer, Pair with a computer, and enter the address and code it shows.',
      ]) {
        for (const wrapped of wrap(line, 0)) io.print(wrapped);
      }
    } else {
      next.HALCYONIC_NETWORK_HOST = undefined;
      this.#save(next);
      io.print(
        'Halcyonic stops listening on your network when it restarts. Paired headsets stay paired and work over Wi-Fi again when you turn pairing back on. To stop one for good: pnpm devices revoke <device id>; pnpm devices lists them.',
      );
    }
    if (io.env.HALCYONIC_NETWORK_HOST !== undefined) {
      io.print(
        'HALCYONIC_NETWORK_HOST in the environment still wins over this setting while it is set.',
      );
    }
    await this.#afterChange(next);
    return 0;
  }

  // Helpers

  #settings(): { values: HostSettings } | { error: string } {
    try {
      const values = readHostSettings(this.#dataDir);
      loadConfig(withSettings(this.#io.env, values), this.#io.pins);
      return { values };
    } catch (error) {
      if (error instanceof ConfigError) return { error: error.message };
      throw error;
    }
  }

  /** The settings as the file holds them, or null after saying why they can't be changed. */
  #settingsOrPrint(): HostSettings | null {
    try {
      return readHostSettings(this.#dataDir);
    } catch (error) {
      if (!(error instanceof ConfigError)) throw error;
      this.#io.print(`Halcyonic's settings can't be changed: ${error.message}`);
      return null;
    }
  }

  #save(settings: Partial<Record<SettingName, string | undefined>>): void {
    const kept: Partial<Record<SettingName, string>> = {};
    for (const [name, value] of Object.entries(settings)) {
      if (value !== undefined) kept[name as SettingName] = value;
    }
    writeHostSettings(this.#dataDir, kept);
  }

  /** Says whether Halcyonic would start with the new settings, and whether it needs a restart. */
  async #afterChange(settings: Partial<Record<SettingName, string | undefined>>): Promise<void> {
    const io = this.#io;
    try {
      const kept: Partial<Record<SettingName, string>> = {};
      for (const [name, value] of Object.entries(settings)) {
        if (value !== undefined) kept[name as SettingName] = value;
      }
      loadConfig(withSettings(io.env, kept), io.pins);
    } catch (error) {
      if (!(error instanceof ConfigError)) throw error;
      io.print(`Halcyonic won't start with its settings yet: ${error.message}`);
      return;
    }
    if (!(await this.#answers())) {
      io.print('Halcyonic uses this the next time it starts: pnpm start');
    } else {
      io.print(
        'Halcyonic is running: restart it to use this. Stop it where it runs with Ctrl-C, then run pnpm start.',
      );
      io.print(RESTART_WARNING);
    }
  }

  async #confirm(question: string): Promise<boolean> {
    const io = this.#io;
    if (this.#yes) return true;
    if (io.ask === null) {
      io.print('Nothing changed: run this in a terminal to answer, or add --yes.');
      return false;
    }
    const answer = (await io.ask(`${question} Type yes to go ahead: `)).trim().toLowerCase();
    if (answer === 'yes') return true;
    io.print('Nothing changed.');
    return false;
  }

  async #running(): Promise<Running> {
    const none: Running = {
      answering: false,
      roots: null,
      agentApps: null,
      devices: null,
      usageLeft: null,
      companion: null,
      tokenProblem: null,
    };
    if (!(await this.#answers())) return none;
    const answering = { ...none, answering: true };
    if (!this.#useToken) return answering;
    let token: string;
    try {
      token = (await readFile(join(this.#dataDir, ACCESS_TOKEN_FILE), 'utf8')).trim();
    } catch {
      return { ...answering, tokenProblem: 'missing' };
    }
    // Another account could listen on the port while Halcyonic is stopped: the token goes only to
    // a server that first proves it holds it.
    const proven = await provenBase(this.#io.fetch, this.#bases(), token);
    if (typeof proven !== 'object') return { ...answering, tokenProblem: 'unproved' };
    // Each request proves the server again first: one that stopped since gets no token.
    const get = async (path: string) =>
      (await serverProvesToken(this.#io.fetch, proven.base, token)) === 'proved'
        ? this.#get(proven.base, path, token)
        : null;
    const locations = await get('/api/locations');
    if (locations === null || locations.status !== 200) {
      return { ...answering, tokenProblem: 'unproved' };
    }
    const snapshot = await get('/api/snapshot');
    const devices = await get('/api/devices');
    const usage = await get('/api/usage-limits');
    const companion = await get('/api/companion');
    const availabilityOf = (answer: { status: number; body: unknown } | null) => {
      const body = answer?.body as
        | { availability?: string; reason?: { code?: string } }
        | undefined;
      return answer?.status === 200 && typeof body?.availability === 'string'
        ? { availability: body.availability, code: body.reason?.code ?? null }
        : null;
    };
    return {
      answering: true,
      tokenProblem: null,
      roots: (locations.body as LocationsResponse).roots.map((root) => root.path),
      agentApps:
        snapshot?.status === 200
          ? (snapshot.body as Snapshot).runtimes
              .filter((runtime) => !runtime.synthetic)
              .map((runtime) => runtime.runtime_id)
          : null,
      devices: devices?.status === 200 ? (devices.body as DevicesResponse) : null,
      usageLeft: availabilityOf(usage),
      companion: availabilityOf(companion),
    };
  }

  /** Whether Halcyonic's public health check answers at any loopback address it may listen on. */
  async #answers(): Promise<boolean> {
    for (const base of this.#bases()) {
      if ((await this.#get(base, '/api/health', null))?.status === 200) return true;
    }
    return false;
  }

  async #get(
    base: string,
    path: string,
    token: string | null,
  ): Promise<{ status: number; body: unknown } | null> {
    try {
      const response = await this.#io.fetch(`${base}${path}`, {
        headers: token === null ? {} : { authorization: `Bearer ${token}` },
        // A redirect would carry the token on without a proof before it.
        redirect: 'error',
        signal: AbortSignal.timeout(3000),
      });
      const text = await response.text();
      let body: unknown = null;
      try {
        body = text === '' ? null : JSON.parse(text);
      } catch {
        body = null;
      }
      return { status: response.status, body };
    } catch {
      return null;
    }
  }

  /** Where Halcyonic listens on loopback, by literal address, as the control plane validates it. */
  #bases(): string[] {
    try {
      const { host, port } = loopbackListener(this.#io.env);
      return loopbackBases(host, port);
    } catch {
      return [];
    }
  }

  #folders(): FolderContext {
    return { home: this.#io.home, dataDir: this.#dataDir, uid: process.getuid?.() };
  }

  /** The models Ollama lists, or null when it doesn't answer on this Mac. */
  async #ollama(url: string = this.#io.ollamaUrl): Promise<OllamaModel[] | null> {
    try {
      const response = await this.#io.fetch(`${url.replace(/\/$/, '')}/api/tags`, {
        signal: AbortSignal.timeout(3000),
      });
      if (!response.ok) return null;
      const body = (await response.json()) as { models?: unknown };
      if (!Array.isArray(body.models)) return null;
      return body.models.flatMap((entry: unknown) => {
        const model = entry as {
          name?: unknown;
          size?: unknown;
          capabilities?: unknown;
          remote_host?: unknown;
          remote_model?: unknown;
        };
        if (typeof model.name !== 'string' || !/^[\w.:/-]{1,200}$/.test(model.name)) return [];
        // The companion's own rule (companion/ollama.ts): a cloud tag, or a host it passes requests to.
        const remote =
          isCloudName(model.name) ||
          model.remote_host !== undefined ||
          model.remote_model !== undefined;
        return [
          {
            name: model.name,
            bytes: typeof model.size === 'number' ? model.size : 0,
            local: !remote,
            tools: Array.isArray(model.capabilities) && model.capabilities.includes('tools'),
          },
        ];
      });
    } catch {
      return null;
    }
  }

  /**
   * The model provider and model in the top level of Halcyonic's own Codex settings, in
   * `<data dir>/codex-home/config.toml`; only those two lines are read. Never the person's own.
   */
  #codexSettings(): { provider: string | null; model: string | null } | null {
    const path = join(this.#dataDir, CODEX_HOME_FOLDER, 'config.toml');
    let text: string;
    try {
      if (statSync(path).size > 1024 * 1024) return null;
      text = readFileSync(path, 'utf8');
    } catch {
      return null;
    }
    const found: { provider: string | null; model: string | null } = {
      provider: null,
      model: null,
    };
    for (const line of text.split('\n')) {
      if (/^\s*\[/.test(line)) break;
      const provider = /^\s*model_provider\s*=\s*"([A-Za-z0-9._-]{1,64})"/.exec(line);
      if (provider?.[1] !== undefined) found.provider = provider[1];
      const model = /^\s*model\s*=\s*"([^"\\]{1,128})"/.exec(line);
      if (model?.[1] !== undefined && CODEX_MODEL_NAME.test(model[1])) found.model = model[1];
    }
    return found;
  }

  /** Whether a credential file in the data directory exists and is kept from other users; never opened. */
  #fileState(file: string): 'missing' | 'exposed' | 'private' {
    try {
      const stats = statSync(join(this.#dataDir, file));
      return (stats.mode & 0o077) === 0 ? 'private' : 'exposed';
    } catch {
      return 'missing';
    }
  }

  async #standardVoiceFiles(): Promise<{ binary: string; model: string; vadModel: string } | null> {
    const pins = this.#io.pins;
    if (pins === null) return null;
    const binary = join(this.#dataDir, WHISPER_BINARY);
    const model = join(this.#dataDir, pins.whisperModel.path);
    const vadModel = join(this.#dataDir, pins.whisperVadModel.path);
    if (![binary, model, vadModel].every((path) => existsSync(path))) return null;
    if ((await this.#matches(model, pins.whisperModel)) !== 'matches') return null;
    if ((await this.#matches(vadModel, pins.whisperVadModel)) !== 'matches') return null;
    return { binary, model, vadModel };
  }

  async #matches(path: string, pin: PinnedFile | null): Promise<'matches' | 'differs' | 'unknown'> {
    if (pin === null) return 'unknown';
    let hash = this.#hashes.get(path);
    if (hash === undefined) {
      hash = sha256File(path);
      this.#hashes.set(path, hash);
    }
    const value = await hash;
    if (value === null) return 'differs';
    return value === pin.sha256 ? 'matches' : 'differs';
  }

  #tilde(path: string): string {
    const home = this.#io.home;
    if (path === home) return '~';
    return path.startsWith(`${home}${sep}`) ? `~${path.slice(home.length)}` : path;
  }
}

function companionProblem(code: string | null): string {
  switch (code) {
    case 'companion_not_running':
      return "its model isn't running on this Mac. Start Ollama.";
    case 'companion_model_missing':
      return 'its model is not on this Mac.';
    case 'companion_model_not_local':
      return "its model would run on another computer, so it isn't used.";
    case 'companion_not_set_up':
      return 'Halcyonic runs without it: restart Halcyonic to use these settings.';
    default:
      return `it can't be asked right now${code === null ? '' : ` (${code})`}.`;
  }
}

function usageLeftProblem(code: string | null): string {
  switch (code) {
    case 'insufficient_scope':
      return "The Seorak credential can't read usage limits. Issue a new one with sessions:read, replay:read and limits:read, replace the file, then revoke the old one in Seorak's dashboard.";
    case 'outside_credential_restriction':
      return 'The Seorak credential is limited to a project or to dates. Issue one without limits.';
    case 'credential_rejected':
      return 'Seorak refused the credential: it may have expired or been revoked. Issue a new one.';
    case 'limits_not_served':
      return "This Seorak can't read usage limits yet. Update Seorak and restart it.";
    case 'not_running':
    case 'unreachable':
      return "Seorak isn't answering on this Mac. Start it.";
    case 'not_captured':
      return 'Seorak has no usage reading yet. It gets one once you use Codex.';
    case 'credential_file_exposed':
      return "Other users can read the Seorak credential, so Halcyonic won't use it. Run chmod 600 on it.";
    default:
      return `Usage left can't be read right now${code === null ? '' : ` (${code})`}.`;
  }
}

/**
 * Halcyonic's own Codex settings on an Ollama model of this Mac. The settings that keep Codex off
 * the network are not here: the Codex adapter passes them on every launch, above this file.
 */
function codexConfig(model: string): string {
  return [
    "# Halcyonic's own Codex settings, written by pnpm mac-setup local-model. Halcyonic runs Codex",
    '# only on models this Mac serves, and turns off its plugins, update check, analytics and web',
    '# search whenever it starts it, whatever this file says.',
    'model_provider = "ollama"',
    `model = ${JSON.stringify(model)}`,
    `model_context_window = ${OLLAMA_CONTEXT}`,
    `model_auto_compact_token_limit = ${CODEX_AUTO_COMPACT}`,
    '',
  ].join('\n');
}

/**
 * Why Halcyonic's Codex home can't take its settings, as the Codex adapter would refuse it, or
 * null when it can: a link or not a folder, another user's, one others can open, or holding a
 * sign-in. Its mode is never changed: what others could reach while it was open can't be told.
 */
function codexHomeProblem(home: string): string | null {
  const stats = lstatOrNull(home);
  if (stats === null) return null;
  if (!stats.isDirectory())
    return `${home} is not a folder, or is a link. Move it away, then try again.`;
  if (stats.uid !== process.getuid?.())
    return `${home} belongs to another user. Move it away, then try again.`;
  if ((stats.mode & 0o077) !== 0)
    return `Other users can open ${home}, so it may hold what they put there. Move it away, then try again.`;
  if (lstatOrNull(join(home, 'auth.json')) !== null) {
    return `${join(home, 'auth.json')} is a Codex sign-in. Halcyonic runs Codex only on models this Mac serves, without one: move it away, then try again.`;
  }
  return null;
}

/** The chrome-headless-shell folders Playwright's cache holds, as plain folders only; links are passed over. */
function playwrightShells(cache: string): string[] {
  const stats = lstatOrNull(cache);
  if (stats === null || !stats.isDirectory()) return [];
  let entries: Dirent[];
  try {
    entries = readdirSync(cache, { withFileTypes: true });
  } catch {
    return [];
  }
  return entries
    .filter((entry) => entry.isDirectory() && entry.name.startsWith('chromium_headless_shell-'))
    .map((entry) => join(cache, entry.name, 'chrome-headless-shell-mac-arm64'))
    .sort()
    .reverse();
}

/**
 * Whether `folder` is a plain folder whose whole contents, every file and folder, hash to the pin,
 * with nothing in it a link (browserProblem). Nothing is followed through a link.
 */
function isPinnedShell(folder: string, sha256: string): boolean {
  return lstatOrNull(folder)?.isDirectory() === true && browserProblem(folder, sha256) === null;
}

function lstatOrNull(path: string): Stats | null {
  try {
    return lstatSync(path);
  } catch {
    return null;
  }
}

/** Writes a file mode 600 through a new file renamed into place. */
function writePrivate(path: string, text: string): void {
  const temporary = `${path}.${process.pid}.tmp`;
  rmSync(temporary, { force: true });
  writeFileSync(temporary, text, { mode: 0o600, flag: 'wx' });
  renameSync(temporary, path);
}

function isDirectory(path: string): boolean {
  try {
    return statSync(path).isDirectory();
  } catch {
    return false;
  }
}

function present(value: string | undefined): value is string {
  return value !== undefined && value !== '';
}

function sameList(first: readonly string[], second: readonly string[]): boolean {
  return first.length === second.length && first.every((entry, index) => entry === second[index]);
}

function expandHome(path: string, home: string): string {
  if (path === '~') return home;
  return path.startsWith('~/') ? join(home, path.slice(2)) : path;
}

function quote(path: string): string {
  return /^[\w./~-]+$/.test(path) ? path : `'${path.replaceAll("'", "'\\''")}'`;
}

/**
 * A path for a printed command that a space in it cannot split: under the home folder as
 * "$HOME/...", else bare when plain and in single quotes when not (AUDIT SETUP-6).
 */
function shellPath(path: string, home: string): string {
  const rest = path.startsWith(`${home}${sep}`) ? path.slice(home.length + 1) : null;
  if (rest !== null && /^[^"$`\\!]*$/.test(rest)) return `"$HOME/${rest}"`;
  return /^[\w./-]+$/.test(path) ? path : `'${path.replaceAll("'", "'\\''")}'`;
}

/** The real path of a regular file this user may run, after every link, or null (undo's git). */
function runnableProgram(path: string): string | null {
  try {
    accessSync(path, constants.X_OK);
    const real = realpathSync(path);
    return statSync(real).isFile() ? real : null;
  } catch {
    return null;
  }
}

function gigabytes(bytes: number): string {
  return (bytes / 1e9).toFixed(1).replace(/\.0$/, '');
}

/** Wraps a sentence at 96 columns, indented. */
export function wrap(text: string, indent: number): string[] {
  const width = 96 - indent;
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(' ')) {
    if (line !== '' && line.length + 1 + word.length > width) {
      lines.push(line);
      line = word;
    } else {
      line = line === '' ? word : `${line} ${word}`;
    }
  }
  if (line !== '') lines.push(line);
  return lines.map((entry) => `${' '.repeat(indent)}${entry}`);
}

function readFirewall(): Firewall | null {
  const tool = '/usr/libexec/ApplicationFirewall/socketfilterfw';
  try {
    const state = execFileSync(tool, ['--getglobalstate'], { encoding: 'utf8', timeout: 5000 });
    const blockAll = execFileSync(tool, ['--getblockall'], { encoding: 'utf8', timeout: 5000 });
    return { enabled: /enabled/i.test(state), blockAll: /enabled/i.test(blockAll) };
  } catch {
    return null;
  }
}

function which(command: string): string | null {
  for (const folder of (process.env.PATH ?? '').split(':')) {
    if (folder === '') continue;
    const path = join(folder, command);
    try {
      if (statSync(path).isFile()) return path;
    } catch {}
  }
  return null;
}

/** This Mac as setup sees it: its environment, home, Ollama, tools, firewall and pins. */
export function localMacSetupIo(
  print: (line: string) => void,
  ask: MacSetupIo['ask'] = null,
): MacSetupIo {
  return {
    env: process.env,
    home: userInfo().homedir,
    print,
    ask,
    fetch,
    ollamaUrl: 'http://127.0.0.1:11434',
    memoryBytes: totalmem(),
    which,
    firewall: platform() === 'darwin' ? readFirewall : () => null,
    pins: pinsForThisMac(),
  };
}

if (import.meta.main) {
  const terminal = process.stdin.isTTY === true;
  const ask = terminal
    ? async (question: string) => {
        const prompt = createInterface({ input: process.stdin, output: process.stdout });
        try {
          return await prompt.question(question);
        } finally {
          prompt.close();
        }
      }
    : null;
  runMacSetup(
    process.argv.slice(2),
    localMacSetupIo((line) => process.stdout.write(`${line}\n`), ask),
  )
    .then((status) => process.exit(status))
    .catch((error: unknown) => {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
      process.exit(1);
    });
}

/** Where the setup page puts the checked tunnel program (ADR 0054). */
export function tunnelProgram(dataDir: string): string {
  return join(dataDir, 'tools', `cloudflared-${TUNNEL_PIN.version}`, 'cloudflared');
}
