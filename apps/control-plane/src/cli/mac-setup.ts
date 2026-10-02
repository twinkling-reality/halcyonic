/**
 * Checks this Mac's setup for Halcyonic step by step, in plain words, and writes the settings it
 * needs into `<data dir>/settings.json` (ADR 0024, docs/internal/runbooks/LOCAL_DEVELOPMENT.md,
 * "First run on a Mac"):
 *
 *   pnpm mac-setup                      check every step and say what to do next
 *   pnpm mac-setup allow [folder]       let agents use a folder; with none, ~/HalcyonicProjects
 *   pnpm mac-setup disallow <folder>    stop letting agents use a folder; nothing is deleted
 *   pnpm mac-setup agent-apps           record the pinned OpenCode and Codex, once checked
 *   pnpm mac-setup local-model <name>   give OpenCode Halcyonic's own settings on an Ollama model
 *   pnpm mac-setup voice                record the voice files, once checked
 *   pnpm mac-setup pairing on|off       let headsets pair over Wi-Fi, or stop it
 *
 * It never opens a credential: of the Seorak, Salidium and Anthropic files it reads only whether
 * they exist and who may read them. By default it reads no access token and asks the running
 * Halcyonic only its public health check; with --with-token it reads the token and sends it only
 * after the server proves it holds it. It never turns on an agent app that spends model credit,
 * never names a remote model, and downloads nothing: what needs a download it shows as a command
 * for the person to run.
 */
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { readFile } from 'node:fs/promises';
import { platform, totalmem, userInfo } from 'node:os';
import { delimiter, dirname, join, resolve, sep } from 'node:path';
import { createInterface } from 'node:readline/promises';
import type { DevicesResponse, LocationsResponse, Snapshot } from '@halcyonic/contracts';
import {
  ConfigError,
  DEFAULT_NETWORK_PORT,
  defaultDataDir,
  isLocalOllamaModel,
  loadConfig,
  loopbackListener,
  OPENCODE_SETTINGS_PATH,
  type OpenCodeSettings,
  readOpenCodeSettings,
} from '../config.ts';
import { assessFolder, type FolderContext, realOrSelf } from '../folder-safety.ts';
import {
  ACCESS_TOKEN_FILE,
  loopbackBases,
  provenBase,
  serverProvesToken,
} from '../http/security.ts';
import { SEORAK_CREDENTIAL_FILE } from '../intelligence/evaluation.ts';
import { SALIDIUM_CREDENTIAL_FILE } from '../intelligence/understanding.ts';
import { type PinnedFile, type Pins, pinsForThisMac, sha256File } from '../pins.ts';
import { ANTHROPIC_KEY_FILE } from '../runtimes.ts';
import {
  type HostSettings,
  readHostSettings,
  type SettingName,
  settingRoots,
  withSettings,
  writeHostSettings,
} from '../settings.ts';
import { printable } from './devices.ts';

export interface Firewall {
  readonly enabled: boolean;
  readonly blockAll: boolean;
}

/** whisper.cpp is built on the Mac from its pinned source, so its binary has no checksum. */
const WHISPER_BINARY = 'speech/whisper.cpp-1.9.4/bin/whisper-cli';

/** The folder `allow` makes and allows when given none. */
export const DEFAULT_PROJECTS_FOLDER = 'HalcyonicProjects';

/** The Ollama models Halcyonic was run with end to end, on a Mac with 64 GB (local-models.md). */
const CHECKED_MODELS = [
  { name: 'qwen3.6:35b-a3b-nvfp4', bytes: 23.6e9 },
  { name: 'qwen3.8:27b-nvfp4', bytes: 18.2e9 },
] as const;

/** The context Ollama is told to give a model, and OpenCode to expect (local-models.md). */
const OLLAMA_CONTEXT = 65_536;

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
}

type Status = 'ready' | 'todo' | 'optional' | 'look' | 'unknown';

const STATUS_WORDS: Record<Status, string> = {
  ready: 'Ready',
  todo: 'To do',
  optional: 'Optional',
  look: 'Look at this',
  unknown: "Can't tell yet",
};

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
    case 'pairing':
      return argument === 'on' || argument === 'off' ? setup.pairing(argument) : usage(io);
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
    '  pnpm mac-setup local-model <name>   give OpenCode settings of its own on an Ollama model',
    '  pnpm mac-setup voice                record the checked voice files',
    '  pnpm mac-setup pairing on|off       let headsets pair over Wi-Fi, or stop it',
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

  async check(): Promise<number> {
    const io = this.#io;
    const settings = this.#settings();
    const running = await this.#running();
    const ollama = await this.#ollama();
    const steps: Step[] = [];
    if ('error' in settings) {
      steps.push({
        title: "Halcyonic's settings",
        status: 'todo',
        lines: [`Halcyonic won't start with its settings as they are: ${settings.error}`],
      });
    }
    const values = 'error' in settings ? {} : settings.values;
    const env = withSettings(io.env, values);
    steps.push(this.#foldersStep(env));
    steps.push(await this.#agentAppsStep(env, ollama));
    steps.push(this.#modelsStep(ollama));
    steps.push(this.#costStep(env, ollama));
    steps.push(await this.#voiceStep(env));
    steps.push(this.#usageLeftStep(running));
    steps.push(this.#understandStep());
    steps.push(this.#runningStep(env, running));
    steps.push(this.#headsetStep(env, running));

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
    if (env.HALCYONIC_OPENCODE_BIN !== undefined && env.HALCYONIC_OPENCODE_BIN !== '') {
      const home = env.HALCYONIC_OPENCODE_CONFIG_HOME;
      let own: OpenCodeSettings | null = null;
      try {
        own = home === undefined || home === '' ? null : readOpenCodeSettings(home);
      } catch {
        own = null;
      }
      if (own === null) {
        look = true;
        lines.push(
          "OpenCode uses your own OpenCode settings. With OpenCode's defaults it runs every command without asking you, so nothing reaches the headset to approve, and it may fetch from the web. Halcyonic doesn't read your settings, so it can't tell whether yours ask first.",
        );
        next.push(`pnpm mac-setup local-model ${this.#suggestedModel(ollama) ?? '<model name>'}`);
      } else {
        const shell = effectOf(own, 'shell');
        const web = [effectOf(own, 'webfetch'), effectOf(own, 'websearch')];
        const asks = shell === 'ask' || shell === 'deny';
        const offline = web.every((effect) => effect === 'deny');
        if (!asks || !offline) look = true;
        lines.push(
          `OpenCode uses Halcyonic's own OpenCode settings: ${
            shell === 'ask'
              ? 'it asks you before running a shell command'
              : shell === 'deny'
                ? 'it never runs a shell command'
                : 'it runs some or all shell commands without asking you, so they never reach the headset to approve'
          }, and ${offline ? "it can't fetch from the web" : 'it may fetch from the web'}.`,
        );
        if (!asks || !offline) {
          next.push(`pnpm mac-setup local-model ${own.model.replace(/^ollama\//, '')}`);
        }
      }
      if (this.#io.which('rg') === null) {
        look = true;
        lines.push(
          "OpenCode searches files with ripgrep, which isn't on this PATH, so OpenCode would download it from GitHub the first time.",
        );
        next.push('brew install ripgrep');
      }
    }
    lines.push(
      "Claude Agent is set up only by hand, because every task on it runs on a remote service, Anthropic's, paid for with your API key. See “Run real agents” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.",
    );
    if (ready === 0) {
      lines.unshift(
        'No agent app is set up, so no task can start. The headset says “No agent app on your computer can start work right now.”',
      );
    }
    return { title, status: ready === 0 ? 'todo' : look ? 'look' : 'ready', lines, next };
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
      'Halcyonic was checked with qwen3.6:35b-a3b-nvfp4 (23.6 GB) and qwen3.8:27b-nvfp4 (18.2 GB), on a Mac with 64 GB of memory.',
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
      const provider = this.#codexProvider();
      if (provider === 'ollama') {
        lines.push(
          "Codex: your own Codex settings name Ollama, so the headset lists Codex's model as running on this Mac.",
        );
      } else {
        hosted = true;
        lines.push(
          `Codex: your own Codex settings name ${provider === null ? 'OpenAI, their default' : provider}, so the headset lists Codex's models as running on a remote service. Nothing starts on one without its second press; a task on one sends your code and instructions there, and may cost money. To run Codex on this Mac, see “Codex” in docs/internal/runbooks/LOCAL_DEVELOPMENT.md.`,
        );
      }
    }
    if (this.#io.env.HALCYONIC_CLAUDE_AGENT === '1') {
      hosted = true;
      lines.push(
        "Claude Agent is on. Its models run on a remote service, Anthropic's: nothing starts on one without its second press, and a task on one is paid for with your API key.",
      );
      const key = this.#fileState(ANTHROPIC_KEY_FILE);
      if (key === 'exposed')
        lines.push(
          `Other users can read ${this.#tilde(join(this.#dataDir, ANTHROPIC_KEY_FILE))}. Run chmod 600 on it.`,
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
        ],
      };
    }
    if (reading.availability === 'available')
      return { title, status: 'ready', lines: ['Usage left is set up.', about] };
    return { title, status: 'todo', lines: [usageLeftProblem(reading.code), about] };
  }

  #understandStep(): Step {
    const title = 'What changed and why';
    const path = join(this.#dataDir, SALIDIUM_CREDENTIAL_FILE);
    const about =
      "Salidium tells the headset's Understand tab what a task changed and why, for tasks on Claude Code and Codex.";
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
    return {
      title,
      status: 'ready',
      lines: [
        "A Salidium credential is saved. Each task's Understand tab says whether Salidium could answer.",
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
    const next = { ...settings, HALCYONIC_OPENCODE_CONFIG_HOME: home };
    this.#save(next);
    io.print(
      `${replaced ? "Replaced Halcyonic's own OpenCode settings" : 'OpenCode now has settings of its own for Halcyonic'}: ${model.name} on this Mac when no model is chosen, a question to you before any shell command, and no fetching from the web. Your own OpenCode settings are left as they are.`,
    );
    io.print(
      `Start Ollama with OLLAMA_CONTEXT_LENGTH=${OLLAMA_CONTEXT}, the context these settings tell OpenCode to expect.`,
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

  async pairing(state: 'on' | 'off'): Promise<number> {
    const io = this.#io;
    const settings = this.#settingsOrPrint();
    if (settings === null) return 1;
    const next: Record<string, string | undefined> = { ...settings };
    if (state === 'on') {
      const port = io.env.HALCYONIC_NETWORK_PORT ?? String(DEFAULT_NETWORK_PORT);
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
      loadConfig(withSettings(this.#io.env, values));
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
      loadConfig(withSettings(io.env, kept));
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
    const usageBody = usage?.body as
      | { availability?: string; reason?: { code?: string } }
      | undefined;
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
      usageLeft:
        usage?.status === 200 && typeof usageBody?.availability === 'string'
          ? { availability: usageBody.availability, code: usageBody.reason?.code ?? null }
          : null,
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
  async #ollama(): Promise<OllamaModel[] | null> {
    try {
      const response = await this.#io.fetch(`${this.#io.ollamaUrl}/api/tags`, {
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
        const remote =
          /[:-]cloud$/.test(model.name) ||
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

  /** The model provider in the top level of the person's Codex settings; only that line is read. */
  #codexProvider(): string | null {
    const codexHome = this.#io.env.CODEX_HOME ?? join(this.#io.home, '.codex');
    let text: string;
    try {
      if (statSync(join(codexHome, 'config.toml')).size > 1024 * 1024) return null;
      text = readFileSync(join(codexHome, 'config.toml'), 'utf8');
    } catch {
      return null;
    }
    for (const line of text.split('\n')) {
      if (/^\s*\[/.test(line)) break;
      const match = /^\s*model_provider\s*=\s*"([A-Za-z0-9._-]{1,64})"/.exec(line);
      if (match?.[1] !== undefined) return match[1];
    }
    return null;
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

/**
 * What OpenCode does with an action under these settings, for every resource: the last rule whose
 * action matches wins, as OpenCode applies them, a `*` in a rule's action matching any run of
 * characters (`web*` matches `webfetch`), and with no rule it allows. A rule that allows only some
 * resources makes the answer `some`.
 */
export function effectOf(
  settings: OpenCodeSettings,
  action: string,
): 'allow' | 'ask' | 'deny' | 'some' {
  let effect: 'allow' | 'ask' | 'deny' = 'allow';
  let someAllowed = false;
  for (const rule of settings.permissions) {
    if (!globMatches(rule.action, action)) continue;
    if (rule.resource === '*') {
      effect = rule.effect;
      someAllowed = false;
    } else if (rule.effect === 'allow') {
      someAllowed = true;
    }
  }
  return someAllowed && effect !== 'allow' ? 'some' : effect;
}

/** Whether a pattern with `*` (any run of characters) and `?` (one character) matches the whole name. */
function globMatches(pattern: string, name: string): boolean {
  const source = [...pattern]
    .map((character) =>
      character === '*'
        ? '.*'
        : character === '?'
          ? '.'
          : character.replace(/[.+^${}()|[\]\\]/g, '\\$&'),
    )
    .join('');
  return new RegExp(`^${source}$`, 's').test(name);
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
  runMacSetup(process.argv.slice(2), {
    env: process.env,
    home: userInfo().homedir,
    print: (line) => process.stdout.write(`${line}\n`),
    ask,
    fetch,
    ollamaUrl: 'http://127.0.0.1:11434',
    memoryBytes: totalmem(),
    which,
    firewall: platform() === 'darwin' ? readFirewall : () => null,
    pins: pinsForThisMac(),
  })
    .then((status) => process.exit(status))
    .catch((error: unknown) => {
      process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
      process.exit(1);
    });
}
