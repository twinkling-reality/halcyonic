import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs, {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { FakeSalidium } from '@halcyonic/integration-salidium/testing';
import { loadConfig } from '../config.ts';
import { loopbackProof, PROOF_CHALLENGE_HEADER, PROOF_HEADER } from '../http/security.ts';
import type { Pins } from '../pins.ts';
import { audioOutputFor } from '../preview/audio-output.ts';
import { launcherProblem } from '../preview/launcher.ts';
import { BROWSER_FILES, browserManifest, manifestDigest } from '../preview/play.ts';
import {
  readHostSettings,
  SETTINGS_FILE,
  settingRoots,
  withSettings,
  writeHostSettings,
} from '../settings.ts';
import { FOLDER_MEANING, type MacSetupIo, runMacSetup } from './mac-setup.ts';

/** The access token of the fake running Halcyonic. */
const TOKEN = 'a'.repeat(43);

/** Every line any run printed, held to the content guide at the end. */
const everything: string[] = [];

const CONTENT = {
  opencode: 'opencode 2.0.18, for tests\n',
  codex: 'codex 0.157.0, for tests\n',
  whisperModel: 'speech model, for tests\n',
  whisperVadModel: 'voice activity model, for tests\n',
  browser: 'chrome-headless-shell, for tests\n',
};

function sha256(text: string): string {
  return createHash('sha256').update(text).digest('hex');
}

const STARTER_LOCKFILE = new URL(
  '../../../../starters/iwsdk-vr/package-lock.json',
  import.meta.url,
);

/** A stand-in for the VR starter's installed set: each package its lockfile pins, as a package.json of that version. */
function installStarterSet(dataDir: string): string {
  const folder = join(dataDir, 'starters', 'iwsdk-vr');
  const lock = JSON.parse(readFileSync(STARTER_LOCKFILE, 'utf8')) as {
    packages: Record<string, { version?: string; optional?: boolean }>;
  };
  for (const [path, entry] of Object.entries(lock.packages)) {
    if (path === '' || entry.optional) continue;
    mkdirSync(join(folder, path), { recursive: true });
    writeFileSync(
      join(folder, path, 'package.json'),
      JSON.stringify({ name: path.replace(/^.*node_modules\//, ''), version: entry.version }),
    );
  }
  return folder;
}

/** A chrome-headless-shell folder: every file it is named by, its binary holding `binary`. */
function writeShell(folder: string, binary: string): string {
  fs.mkdirSync(folder, { recursive: true });
  for (const file of BROWSER_FILES)
    fs.writeFileSync(join(folder, file), file === 'chrome-headless-shell' ? binary : file);
  return folder;
}

/** The whole-folder hash of a stand-in browser holding `binary`, as the pin is (AUDIT M1). */
function shellHash(binary: string): string {
  const folder = fs.mkdtempSync(join(tmpdir(), 'halcyonic-shell-pin-'));
  try {
    return manifestDigest(browserManifest(writeShell(join(folder, 'shell'), binary)) as string[]);
  } finally {
    fs.rmSync(folder, { recursive: true, force: true });
  }
}

const PINS: Pins = {
  opencode: { path: 'runtimes/opencode/bin/opencode', sha256: sha256(CONTENT.opencode) },
  codex: { path: 'runtimes/codex/bin/codex', sha256: sha256(CONTENT.codex) },
  whisperModel: { path: 'speech/models/model.bin', sha256: sha256(CONTENT.whisperModel) },
  whisperVadModel: { path: 'speech/models/vad.bin', sha256: sha256(CONTENT.whisperVadModel) },
  browser: { path: 'browser/current', sha256: shellHash(CONTENT.browser) },
};

const OLLAMA_MODELS = [
  { name: 'qwen3.6:35b-a3b-nvfp4', size: 23_600_000_000, capabilities: ['completion', 'tools'] },
  { name: 'smollm2:135m', size: 270_000_000, capabilities: ['completion'] },
  {
    name: 'gpt-oss:120b-cloud',
    size: 384,
    capabilities: ['completion', 'tools'],
    remote_host: 'https://ollama.com:443',
    remote_model: 'gpt-oss:120b',
  },
];

/** The companion's own model, as Ollama lists it once pulled. */
const COMPANION = {
  name: 'qwen3.5:9b',
  size: 6_600_000_000,
  capabilities: ['completion', 'tools'],
};

type Answer = { readonly status: number; readonly body: unknown };

interface Mac {
  readonly root: string;
  readonly home: string;
  readonly dataDir: string;
  readonly lines: string[];
  readonly answers: string[];
  readonly requests: { url: string; authorization: string | null }[];
  /** Answers by `ollama` or `halcyonic` and path; a missing one means nothing answers. */
  readonly routes: Map<string, Answer>;
  firewall: { enabled: boolean; blockAll: boolean } | null;
  rg: boolean;
  /** Whether the fake running Halcyonic proves it holds the access token. */
  proves: boolean;
  /** How many more proofs it gives before it stops, as a control plane replaced mid-check would. */
  proofsLeft: number;
  interactive: boolean;
  env: NodeJS.ProcessEnv;
  run(...args: string[]): Promise<number>;
  settings(): ReturnType<typeof readHostSettings>;
  install(file: keyof Pins, content?: string): string;
}

function mac(t: TestContext): Mac {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-mac-setup-')));
  t.after(() => {
    chmodTree(root);
    rmSync(root, { recursive: true, force: true });
  });
  const home = join(root, 'home');
  mkdirSync(home);
  const dataDir = join(home, '.halcyonic');
  const routes = new Map<string, Answer>([
    ['ollama /api/tags', { status: 200, body: { models: OLLAMA_MODELS } }],
  ]);
  const machine: Mac = {
    root,
    home,
    dataDir,
    lines: [],
    answers: [],
    requests: [],
    routes,
    firewall: { enabled: true, blockAll: false },
    proves: true,
    proofsLeft: Number.POSITIVE_INFINITY,
    rg: true,
    interactive: true,
    env: { HOME: home, HALCYONIC_DATA_DIR: dataDir, HALCYONIC_PORT: '47999' },
    async run(...args: string[]) {
      machine.lines.length = 0;
      const io: MacSetupIo = {
        env: machine.env,
        home,
        print: (line) => {
          machine.lines.push(line);
          everything.push(line);
        },
        ask: machine.interactive
          ? async (question) => {
              machine.lines.push(question);
              everything.push(question);
              return machine.answers.shift() ?? '';
            }
          : null,
        fetch: (async (input: string | URL | Request, init?: RequestInit) => {
          const url = new URL(String(input));
          const authorization = new Headers(init?.headers).get('authorization');
          machine.requests.push({ url: url.href, authorization });
          // Ollama answers on its own port, as on the Mac; everything else is Halcyonic.
          const host = url.port === '11434' ? 'ollama' : 'halcyonic';
          const answer = routes.get(`${host} ${url.pathname}`);
          if (answer === undefined) throw new TypeError('fetch failed');
          const challenge = new Headers(init?.headers).get(PROOF_CHALLENGE_HEADER);
          const proof =
            host === 'halcyonic' &&
            url.pathname === '/api/health' &&
            machine.proves &&
            challenge !== null &&
            machine.proofsLeft-- > 0
              ? { [PROOF_HEADER]: loopbackProof(TOKEN, url.host, challenge) }
              : {};
          return new Response(JSON.stringify(answer.body), {
            status: answer.status,
            headers: proof,
          });
        }) as typeof fetch,
        ollamaUrl: 'http://127.0.0.1:11434',
        memoryBytes: 64 * 1024 ** 3,
        which: (command) => (command === 'rg' && machine.rg ? '/opt/homebrew/bin/rg' : null),
        firewall: () => machine.firewall,
        pins: PINS,
      };
      return runMacSetup(args, io);
    },
    settings: () => readHostSettings(dataDir),
    install(file, content = CONTENT[file]) {
      const path = join(dataDir, PINS[file].path);
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, content);
      return path;
    },
  };
  return machine;
}

/** Gives back what tests took away, so the directory can be removed. */
function chmodTree(path: string): void {
  try {
    chmodSync(path, 0o700);
  } catch {
    return;
  }
  for (const entry of ['home/.halcyonic', 'home/.halcyonic/access-token']) {
    try {
      chmodSync(join(path, entry), 0o700);
    } catch {}
  }
}

/** What it printed, with wrapped sentences joined again. */
function text(machine: Mac): string {
  return machine.lines.map((line) => line.trim()).join(' ');
}

/** The status a step reads, by its title. */
function status(machine: Mac, title: string): string | undefined {
  const line = machine.lines.find((entry) => /^\d+\. /.test(entry) && entry.includes(`${title}: `));
  return line?.slice(line.indexOf(`${title}: `) + title.length + 2);
}

/** A running Halcyonic answering with what it uses. */
function running(
  machine: Mac,
  uses: {
    roots?: string[];
    apps?: string[];
    devices?: unknown;
    usage?: unknown;
    companion?: unknown;
  } = {},
) {
  mkdirSync(machine.dataDir, { recursive: true, mode: 0o700 });
  writeFileSync(join(machine.dataDir, 'access-token'), TOKEN, { mode: 0o600 });
  machine.routes.set('halcyonic /api/health', { status: 200, body: { status: 'ok' } });
  machine.routes.set('halcyonic /api/locations', {
    status: 200,
    body: { roots: (uses.roots ?? []).map((path) => ({ path })) },
  });
  machine.routes.set('halcyonic /api/snapshot', {
    status: 200,
    body: {
      runtimes: [
        { runtime_id: 'mock', synthetic: true },
        ...(uses.apps ?? []).map((runtime_id) => ({ runtime_id, synthetic: false })),
      ],
    },
  });
  machine.routes.set('halcyonic /api/devices', {
    status: 200,
    body: uses.devices ?? { devices: [], connected: [] },
  });
  machine.routes.set('halcyonic /api/companion', {
    status: 200,
    body: uses.companion ?? {
      availability: 'unavailable',
      reason: { code: 'companion_not_set_up', message: 'x' },
    },
  });
  machine.routes.set('halcyonic /api/usage-limits', {
    status: 200,
    body: uses.usage ?? { availability: 'unauthorized', reason: { code: 'credential_missing' } },
  });
}

describe('pnpm mac-setup', () => {
  test('on a new Mac, it says what each step needs, and starts with the folders agents may use', async (t) => {
    const machine = mac(t);
    assert.equal(await machine.run(), 1);
    assert.equal(status(machine, 'Folders agents may use'), 'To do');
    assert.equal(status(machine, 'Agent apps'), 'To do');
    assert.equal(status(machine, 'Models on this Mac'), 'Ready');
    assert.equal(status(machine, 'Voice'), 'Optional');
    assert.equal(status(machine, 'Usage left'), 'Optional');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'To do');
    assert.equal(status(machine, 'Your headset'), "Can't tell yet");
    const output = text(machine);
    for (const sentence of FOLDER_MEANING) {
      assert.ok(output.includes(sentence), sentence);
    }
    assert.match(output, /pnpm mac-setup allow/);
    assert.match(
      output,
      /npm install --prefix ~\/\.halcyonic\/runtimes\/opencode-2\.0\.18 @opencode\/cli@2\.0\.18 --ignore-scripts/,
    );
    assert.match(output, /Claude Agent is off\./);
    assert.match(
      output,
      /gpt-oss:120b-cloud runs on a remote service of Ollama's, not on this Mac\./,
    );
    assert.match(output, /smollm2:135m \(0\.3 GB\) can't use tools/);
    assert.match(output, /plays a recorded demo, labelled as one/);
    assert.doesNotMatch(
      output,
      /Next: pnpm mac-setup pairing on/,
      'pairing is never the default next step',
    );
    assert.match(output, /Your computer doesn't allow any folder yet\./);
    assert.match(output, /Start with “Folders agents may use”\./);
    assert.equal(machine.lines.filter((line) => line.includes('--ignore-scripts')).length, 2);
    assert.equal(existsSync(machine.dataDir), false, 'checking writes nothing');
  });

  test("says whether npm and pnpm are where Halcyonic looks for a project's own check", async (t) => {
    const machine = mac(t);
    const tools = join(machine.home, 'tools');
    mkdirSync(tools);
    for (const name of ['npm', 'pnpm']) writeFileSync(join(tools, name), '', { mode: 0o755 });
    machine.env = { ...machine.env, PATH: ['relative', tools].join(delimiter) };
    await machine.run();
    assert.equal(status(machine, "Projects' own checks"), 'Ready');
    assert.match(
      text(machine),
      /npm and pnpm are where Halcyonic looks: ~\/tools\/npm and ~\/tools\/pnpm\./,
    );

    // A Halcyonic started without pnpm on its PATH, as launchd might start it (AUDIT3's S4).
    rmSync(join(tools, 'pnpm'));
    await machine.run();
    assert.equal(status(machine, "Projects' own checks"), 'Optional');
    const output = text(machine);
    assert.match(
      output,
      /pnpm isn't where Halcyonic looks, so a project that uses it offers no check of its own\./,
    );
    assert.match(output, /stop Halcyonic and start it again from a Terminal that finds them\./);
    // Where this Mac has it in a usual folder, the step says so; never a folder it did not look in.
    assert.equal(
      output.includes("pnpm is at /opt/homebrew/bin/pnpm, a folder that PATH doesn't name."),
      existsSync('/opt/homebrew/bin/pnpm'),
    );
    assert.doesNotMatch(output, /(?<!p)npm is at/, 'npm was found where Halcyonic looks');
  });

  test('allow makes ~/HalcyonicProjects once the person says yes, and only they can read the settings', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes');
    assert.equal(await machine.run('allow'), 0);
    const projects = join(machine.home, 'HalcyonicProjects');
    assert.ok(statSync(projects).isDirectory());
    assert.deepEqual(settingRoots(machine.settings()), [projects]);
    assert.equal(statSync(join(machine.dataDir, SETTINGS_FILE)).mode & 0o777, 0o600);
    // What allowing means is said before the question.
    const question = machine.lines.findIndex((line) =>
      line.startsWith('Allow agents to read and change everything in ~/HalcyonicProjects?'),
    );
    const meaning = machine.lines.findIndex((line) =>
      line.includes('Agents may read and change anything'),
    );
    assert.ok(meaning >= 0 && meaning < question, text(machine));
    assert.match(text(machine), /Halcyonic uses this the next time it starts/);

    assert.equal(await machine.run('allow'), 0);
    assert.match(text(machine), /already allowed/);
    assert.equal(await machine.run(), 1);
    assert.equal(status(machine, 'Folders agents may use'), 'Ready');
    assert.deepEqual(loadConfig(withSettings(machine.env, machine.settings())).projectRoots, [
      projects,
    ]);
  });

  test('allow changes nothing unless the person says yes', async (t) => {
    const machine = mac(t);
    machine.answers.push('no');
    assert.equal(await machine.run('allow'), 1);
    assert.match(text(machine), /Nothing changed\./);
    machine.interactive = false;
    assert.equal(await machine.run('allow'), 1);
    assert.match(text(machine), /run this in a terminal to answer, or add --yes/);
    assert.equal(existsSync(join(machine.home, 'HalcyonicProjects')), false);
    assert.equal(existsSync(join(machine.dataDir, SETTINGS_FILE)), false);
    assert.equal(await machine.run('allow', '--yes'), 0);
    assert.equal(settingRoots(machine.settings()).length, 1);
  });

  test('allow refuses the home folder, a folder holding it, Halcyonic data, app settings and a folder anyone can change', async (t) => {
    const machine = mac(t);
    mkdirSync(machine.dataDir, { mode: 0o700 });
    const shared = join(machine.home, 'shared');
    mkdirSync(shared);
    chmodSync(shared, 0o777);
    for (const folder of ['.ssh', 'Library', '.config/opencode']) {
      mkdirSync(join(machine.home, folder), { recursive: true });
    }
    for (const folder of [
      '~',
      machine.home,
      machine.root,
      machine.dataDir,
      '~/.ssh',
      '~/Library',
      '~/.config/opencode',
      shared,
    ]) {
      machine.answers.push('yes');
      assert.equal(await machine.run('allow', folder), 1, folder);
      assert.match(text(machine), /can't be allowed/, folder);
    }
    assert.equal(existsSync(join(machine.dataDir, SETTINGS_FILE)), false);
  });

  test('allow says Documents holds much more than projects before asking', async (t) => {
    const machine = mac(t);
    mkdirSync(join(machine.home, 'Documents'));
    machine.answers.push('yes');
    assert.equal(await machine.run('allow', '~/Documents'), 0);
    assert.match(text(machine), /holds much more than projects/);
    assert.match(text(machine), /also goes to iCloud/);
    assert.equal(await machine.run(), 1);
    assert.equal(status(machine, 'Folders agents may use'), 'Look at this');
  });

  test('allow makes only its own folder: another must be there already', async (t) => {
    const machine = mac(t);
    assert.equal(await machine.run('allow', '~/projects'), 1);
    assert.match(text(machine), /isn't there\. Make it first/);
    assert.equal(existsSync(join(machine.home, 'projects')), false);
  });

  test('disallow takes a folder out of the settings and deletes nothing', async (t) => {
    const machine = mac(t);
    const dev = join(machine.home, 'dev');
    mkdirSync(dev);
    writeFileSync(join(dev, 'keep.txt'), 'kept');
    machine.answers.push('yes', 'yes');
    await machine.run('allow');
    await machine.run('allow', '~/dev');
    assert.equal(settingRoots(machine.settings()).length, 2);
    assert.equal(await machine.run('disallow', '~/dev'), 0);
    assert.deepEqual(settingRoots(machine.settings()), [join(machine.home, 'HalcyonicProjects')]);
    assert.equal(readFileSync(join(dev, 'keep.txt'), 'utf8'), 'kept');
    assert.match(text(machine), /Nothing in it is deleted\./);
    assert.equal(await machine.run('disallow', '~/dev'), 1);
    assert.equal(await machine.run('disallow', '~/HalcyonicProjects'), 0);
    assert.equal(machine.settings().HALCYONIC_PROJECT_ROOTS, undefined);
  });

  test('agent-apps records only the copies Halcyonic was checked with', async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    machine.install('codex');
    await machine.run();
    assert.equal(
      machine.lines.filter((line) => line.trim() === 'pnpm mac-setup agent-apps').length,
      1,
      'one command for both apps',
    );
    const opencode = machine.install('opencode');
    machine.install('codex', 'another build\n');
    assert.equal(await machine.run('agent-apps'), 0);
    assert.equal(machine.settings().HALCYONIC_OPENCODE_BIN, opencode);
    assert.equal(machine.settings().HALCYONIC_CODEX_BIN, undefined);
    assert.match(text(machine), /Codex 0\.157\.0 at .* isn't the copy Halcyonic was checked with/);

    assert.equal(await machine.run(), 1);
    assert.equal(status(machine, 'Agent apps'), 'Look at this');
    assert.match(
      text(machine),
      /Halcyonic can't tell whether your own settings put OpenCode on a model this computer serves, and any plugins they name run inside OpenCode\./,
    );
    assert.match(text(machine), /pnpm mac-setup local-model qwen3\.6:35b-a3b-nvfp4/);
  });

  test('muse-glimmer:30b-mlx is a checked model, not the default: qwen3.6 stays first (GL1, the owner decides)', async (t) => {
    const machine = mac(t);
    const glimmer = {
      name: 'muse-glimmer:30b-mlx',
      size: 19_100_000_000,
      capabilities: ['completion', 'vision', 'tools', 'thinking'],
    };
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [...OLLAMA_MODELS, glimmer] },
    });
    // With the agent apps set up, the check suggests the model to give them: the default, while the Mac has it.
    machine.install('opencode');
    machine.install('codex');
    assert.equal(await machine.run('agent-apps'), 0);
    await machine.run();
    assert.match(text(machine), /pnpm mac-setup local-model qwen3\.6:35b-a3b-nvfp4/);
    assert.match(
      text(machine),
      /Halcyonic was checked with qwen3\.6:35b-a3b-nvfp4 \(23\.6 GB\), muse-glimmer:30b-mlx \(19\.1 GB\) and qwen3\.8:27b-nvfp4 \(18\.2 GB\)/,
    );
    // A Mac that has only Muse Glimmer of the checked models is offered it.
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [glimmer, ...OLLAMA_MODELS.filter((m) => !m.name.startsWith('qwen3.6'))] },
    });
    await machine.run();
    assert.match(text(machine), /pnpm mac-setup local-model muse-glimmer:30b-mlx/);
    // With no model that can do agent work, the one to download is the default.
    machine.routes.set('ollama /api/tags', { status: 200, body: { models: [] } });
    await machine.run();
    assert.match(text(machine), /ollama pull qwen3\.6:35b-a3b-nvfp4/);
  });

  test('local-model configures muse-glimmer:30b-mlx and local Meta Llama models cleanly', async (t) => {
    const machine = mac(t);
    const glimmer = {
      name: 'muse-glimmer:30b-mlx',
      size: 19_100_000_000,
      capabilities: ['completion', 'vision', 'tools', 'thinking'],
    };
    const llama = {
      name: 'llama3.3:70b',
      size: 42_500_000_000,
      capabilities: ['completion', 'tools'],
    };
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [glimmer, llama] },
    });
    machine.install('opencode');
    machine.install('codex');
    assert.equal(await machine.run('agent-apps'), 0);

    assert.equal(await machine.run('local-model', 'muse-glimmer:30b-mlx'), 0);
    assert.match(
      text(machine),
      /Codex now has settings of its own for Halcyonic, in a folder of its own: muse-glimmer:30b-mlx on this Mac\./,
    );

    assert.equal(await machine.run('local-model', 'llama3.3:70b'), 0);
    assert.match(
      text(machine),
      /Replaced Halcyonic's own Codex settings: llama3\.3:70b on this Mac\./,
    );
  });

  test("the check names Halcyonic's own OpenCode settings and its model, whatever permissions they hold", async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    await machine.run();
    assert.match(
      text(machine),
      /OpenCode uses Halcyonic's own OpenCode settings, on qwen3\.6:35b-a3b-nvfp4\./,
    );
    const before = status(machine, 'Agent apps');
    // The rules on each session Halcyonic opens outrank these, so loosening them changes nothing.
    const path = join(machine.dataDir, 'opencode-config', 'opencode', 'opencode.json');
    const settings = JSON.parse(readFileSync(path, 'utf8'));
    settings.permissions.push({ action: '*', resource: '*', effect: 'allow' });
    writeFileSync(path, JSON.stringify(settings), { mode: 0o600 });
    await machine.run();
    assert.equal(status(machine, 'Agent apps'), before);
    assert.doesNotMatch(text(machine), /without asking|fetch from the web/);
  });

  test("without ripgrep, the check says OpenCode can't search files", async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    machine.rg = false;
    await machine.run();
    assert.match(text(machine), /OpenCode can't search files until it's installed/);
    assert.match(text(machine), /brew install ripgrep/);
  });

  test("local-model gives OpenCode Halcyonic's own settings on a model of this Mac, never a remote one", async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    for (const [name, why] of [
      ['gpt-oss:120b-cloud', /runs on a remote service of Ollama's, not on this Mac/],
      ['smollm2:135m', /can't use tools/],
      ['qwen3.8:27b-nvfp4', /doesn't have qwen3\.8:27b-nvfp4/],
    ] as const) {
      assert.equal(await machine.run('local-model', name), 1, name);
      assert.match(text(machine), why, name);
    }
    assert.equal(machine.settings().HALCYONIC_OPENCODE_CONFIG_HOME, undefined);

    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 0);
    const home = machine.settings().HALCYONIC_OPENCODE_CONFIG_HOME;
    assert.equal(home, join(machine.dataDir, 'opencode-config'));
    const path = join(home, 'opencode', 'opencode.json');
    assert.equal(statSync(path).mode & 0o777, 0o600);
    const settings = JSON.parse(readFileSync(path, 'utf8'));
    assert.equal(settings.model, 'ollama/qwen3.6:35b-a3b-nvfp4');
    assert.equal(
      settings.small_model,
      'ollama/qwen3.6:35b-a3b-nvfp4',
      'OpenCode never picks a small model itself',
    );
    assert.deepEqual(settings.permissions, [
      { action: 'shell', resource: '*', effect: 'ask' },
      { action: 'webfetch', resource: '*', effect: 'deny' },
      { action: 'websearch', resource: '*', effect: 'deny' },
    ]);
    assert.deepEqual(Object.keys(settings.providers.ollama.models), ['qwen3.6:35b-a3b-nvfp4']);
    assert.equal(
      loadConfig(withSettings(machine.env, machine.settings())).opencodeConfigHome,
      home,
    );
    assert.match(text(machine), /Your own OpenCode settings are left as they are\./);

    machine.routes.delete('ollama /api/tags');
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    assert.match(text(machine), /Ollama isn't answering/);
  });

  test("local-model gives Codex Halcyonic's own settings in a folder of its own, never with a sign-in", async (t) => {
    const machine = mac(t);
    machine.install('codex');
    await machine.run('agent-apps');
    assert.equal(await machine.run('local-model', 'gpt-oss:120b-cloud'), 1);
    const home = join(machine.dataDir, 'codex-home');
    assert.equal(existsSync(join(home, 'config.toml')), false);

    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 0);
    assert.equal(statSync(home).mode & 0o777, 0o700);
    const path = join(home, 'config.toml');
    assert.equal(statSync(path).mode & 0o777, 0o600);
    const settings = readFileSync(path, 'utf8');
    assert.deepEqual(
      settings.split('\n').filter((line) => line !== '' && !line.startsWith('#')),
      [
        'model_provider = "ollama"',
        'model = "qwen3.6:35b-a3b-nvfp4"',
        'model_context_window = 65536',
        'model_auto_compact_token_limit = 52000',
      ],
    );
    assert.match(
      text(machine),
      /Codex now has settings of its own for Halcyonic, in a folder of its own: qwen3\.6:35b-a3b-nvfp4 on this Mac\. Your own Codex settings are left as they are\./,
    );
    assert.equal(
      existsSync(join(machine.home, '.codex')),
      false,
      "the person's own home is never made",
    );

    // A sign-in in Halcyonic's Codex home is refused, and nothing is written.
    writeFileSync(join(home, 'auth.json'), '{}', { mode: 0o600 });
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    assert.match(
      text(machine),
      /auth\.json is a Codex sign-in\. Halcyonic runs Codex only on models this Mac serves, without one/,
    );
    assert.equal(readFileSync(path, 'utf8'), settings);
  });

  test("local-model refuses a Codex home that is a link, another user's or open to others, and never changes its mode", async (t) => {
    const machine = mac(t);
    machine.install('codex');
    await machine.run('agent-apps');
    const home = join(machine.dataDir, 'codex-home');
    const elsewhere = join(machine.dataDir, 'elsewhere');
    mkdirSync(elsewhere, { mode: 0o700 });

    symlinkSync(elsewhere, home);
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    assert.match(
      text(machine),
      /codex-home is not a folder, or is a link\. Move it away, then try again\./,
    );
    assert.equal(existsSync(join(elsewhere, 'config.toml')), false, 'the link was followed');
    rmSync(home);

    mkdirSync(home, { mode: 0o700 });
    chmodSync(home, 0o750);
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    assert.match(
      text(machine),
      /Other users can open .*codex-home, so it may hold what they put there\. Move it away/,
    );
    assert.equal(statSync(home).mode & 0o777, 0o750, 'its mode was changed');
    assert.equal(existsSync(join(home, 'config.toml')), false);

    chmodSync(home, 0o705);
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    assert.equal(statSync(home).mode & 0o777, 0o705, 'its mode was changed');
    assert.equal(existsSync(join(home, 'config.toml')), false);

    chmodSync(home, 0o700);
    // Another user's folder: the folder's owner, as lstat reports it, is not this process's user.
    const lstat = fs.lstatSync;
    t.mock.method(fs, 'lstatSync', (path: string, options?: object) => {
      const stats = lstat(path, options as never);
      return path === home && stats !== undefined
        ? Object.assign(Object.create(Object.getPrototypeOf(stats)), stats, { uid: stats.uid + 1 })
        : stats;
    });
    syncBuiltinESMExports();
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 1);
    t.mock.restoreAll();
    syncBuiltinESMExports();
    assert.match(
      text(machine),
      /codex-home belongs to another user\. Move it away, then try again\./,
    );
    assert.equal(existsSync(join(home, 'config.toml')), false);
    assert.equal(await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4'), 0);
  });

  test('voice records its files once their checksums match', async (t) => {
    const machine = mac(t);
    assert.equal(await machine.run('voice'), 1);
    const binary = join(machine.dataDir, 'speech/whisper.cpp-1.9.4/bin/whisper-cli');
    mkdirSync(dirname(binary), { recursive: true });
    writeFileSync(binary, '');
    machine.install('whisperModel', 'a damaged download\n');
    machine.install('whisperVadModel');
    assert.equal(await machine.run('voice'), 1);
    machine.install('whisperModel');
    assert.equal(await machine.run('voice'), 0);
    assert.equal(machine.settings().HALCYONIC_WHISPER_BIN, binary);
    await machine.run();
    assert.equal(status(machine, 'Voice'), 'Ready');
    assert.match(text(machine), /Nothing you say leaves the Mac or is kept\./);
  });

  test('pairing on says what it opens and changes nothing unless the person says yes', async (t) => {
    const machine = mac(t);
    machine.answers.push('no');
    assert.equal(await machine.run('pairing', 'on'), 1);
    assert.match(
      text(machine),
      /opens a second listener, encrypted, on port 47801, to every device on your network/,
    );
    assert.match(text(machine), /Anyone on your network can try in that time/);
    assert.match(text(machine), /until you revoke it with pnpm devices revoke/);
    assert.match(text(machine), /macOS asks whether node may accept incoming connections/);
    assert.match(text(machine), /Nothing changed\./);
    assert.equal(existsSync(join(machine.dataDir, SETTINGS_FILE)), false);
    machine.interactive = false;
    assert.equal(await machine.run('pairing', 'on'), 1);
    assert.equal(existsSync(join(machine.dataDir, SETTINGS_FILE)), false);
  });

  test('pairing on and off write the listener setting', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes');
    assert.equal(await machine.run('pairing', 'on'), 0);
    assert.equal(machine.settings().HALCYONIC_NETWORK_HOST, '0.0.0.0');
    assert.match(text(machine), /Settings, Your computer, Pair with a computer/);
    machine.firewall = { enabled: true, blockAll: true };
    await machine.run();
    assert.equal(status(machine, 'Your headset'), 'To do');
    assert.match(text(machine), /firewall blocks all incoming connections/);
    assert.equal(await machine.run('pairing', 'off'), 0);
    assert.equal(machine.settings().HALCYONIC_NETWORK_HOST, undefined);
    assert.match(text(machine), /Paired headsets stay paired/);
  });

  test('it tells a running Halcyonic with other settings to restart, and what restarting stops', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes', 'yes');
    await machine.run('allow');
    running(machine, { roots: [] });
    assert.equal(await machine.run('pairing', 'on'), 0);
    assert.match(text(machine), /Halcyonic is running: restart it to use this\./);
    assert.match(
      text(machine),
      /Restarting stops any agent at work, and its task then shows Can't tell yet\./,
    );
    await machine.run('--with-token');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Look at this');
    running(machine, { roots: [join(machine.home, 'HalcyonicProjects')] });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Ready');
  });

  test('by default it reads no access token; with --with-token it sends it only to a server that proves it holds it', async (t) => {
    const machine = mac(t);
    running(machine);
    await machine.run();
    assert.match(text(machine), /use pnpm mac-setup --with-token/);
    assert.ok(machine.requests.every((request) => request.authorization === null));
    assert.ok(machine.requests.some((request) => request.url.endsWith('/api/health')));
    assert.ok(!machine.requests.some((request) => request.url.endsWith('/api/snapshot')));

    machine.requests.length = 0;
    await machine.run('--with-token');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Ready');
    assert.ok(machine.requests.some((request) => request.authorization === `Bearer ${TOKEN}`));

    // Something else listens on the port, as another account could while Halcyonic is stopped.
    machine.proves = false;
    machine.requests.length = 0;
    await machine.run('--with-token');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Look at this');
    assert.match(
      text(machine),
      /can't prove it holds this Mac's access token, so the token was not sent/,
    );
    assert.ok(machine.requests.every((request) => request.authorization === null));

    // Replaced after its first proof: every later request proves again first, so none carries it.
    machine.proves = true;
    machine.proofsLeft = 1;
    machine.requests.length = 0;
    await machine.run('--with-token');
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Look at this');
    assert.ok(machine.requests.every((request) => request.authorization === null));

    machine.proofsLeft = Number.POSITIVE_INFINITY;
    chmodSync(join(machine.dataDir, 'access-token'), 0o000);
    await machine.run('--with-token');
    assert.match(text(machine), /there is no access token/);
  });

  test('it never opens a credential: it checks only that other users cannot read it', async (t) => {
    const machine = mac(t);
    running(machine, { usage: { availability: 'available', limits: [] } });
    // Unreadable even to their owner: opening either would fail.
    for (const file of ['seorak-credential', 'salidium-credential', 'omnorium-credential']) {
      writeFileSync(join(machine.dataDir, file), 'not a real credential\n', { mode: 0o000 });
    }
    await machine.run('--with-token');
    assert.equal(status(machine, 'Usage left'), 'Ready');
    assert.equal(status(machine, 'What changed and why'), 'Ready');
    assert.equal(status(machine, 'Your other projects'), 'Ready');
    for (const file of ['seorak-credential', 'salidium-credential', 'omnorium-credential']) {
      chmodSync(join(machine.dataDir, file), 0o644);
    }
    await machine.run('--with-token');
    assert.equal(status(machine, 'Usage left'), 'To do');
    assert.equal(status(machine, 'What changed and why'), 'To do');
    assert.equal(status(machine, 'Your other projects'), 'To do');
    assert.match(text(machine), /chmod 600/);
  });

  test("What changed and why says whether the installed Salidium can read Halcyonic's own tasks, by its version", async (t) => {
    const machine = mac(t);
    await machine.run();
    assert.equal(status(machine, 'What changed and why'), 'Optional');
    const about =
      "Salidium tells the headset's Understand tab what a task changed and why. Halcyonic keeps its own Codex and OpenCode tasks in folders of its own, which Salidium reads only once it is told where they are.";
    assert.ok(text(machine).includes(about), 'said while not set up');
    mkdirSync(machine.dataDir, { recursive: true, mode: 0o700 });
    writeFileSync(join(machine.dataDir, 'salidium-credential'), 'x', { mode: 0o600 });
    machine.lines.length = 0;
    await machine.run();
    assert.equal(status(machine, 'What changed and why'), 'Ready');
    assert.ok(text(machine).includes(about), 'said once set up');
    assert.match(text(machine), /Salidium isn't answering on this Mac now/);
    const salidium = await FakeSalidium.start();
    t.after(() => salidium.close());
    machine.env.SALIDIUM_HOME = salidium.home;
    salidium.version = '0.8.3';
    salidium.writeDiscovery();
    machine.lines.length = 0;
    await machine.run();
    assert.match(
      text(machine),
      /Salidium 0\.8\.3, as installed, doesn't read Halcyonic's own Codex and OpenCode tasks: their Understand tab says so\. A Salidium after 0\.8\.3 can\./,
    );
    assert.doesNotMatch(text(machine), /SALIDIUM_EXTRA/);
    salidium.version = '0.9.0';
    salidium.writeDiscovery();
    machine.lines.length = 0;
    await machine.run();
    assert.match(
      text(machine),
      /Salidium 0\.9\.0 can read Halcyonic's own Codex and OpenCode tasks once it is told where they are/,
    );
    assert.match(text(machine), /This Mac can't see whether you ran them/);
    assert.ok(
      text(machine).includes(
        'export SALIDIUM_EXTRA_CODEX_HOMES="$HOME/.halcyonic/codex-home:$HOME/.halcyonic/codex-confined/*"',
      ),
    );
    assert.ok(
      text(machine).includes(
        'export SALIDIUM_EXTRA_OPENCODE_STORES="$HOME/.halcyonic/opencode-sandbox/data/opencode/opencode.db:$HOME/.halcyonic/opencode-sandbox-*/data/opencode/opencode.db"',
      ),
    );
    assert.ok(text(machine).includes('salidium service install'));
    assert.equal(
      salidium.requests.some((request) => request.headers.authorization !== undefined),
      false,
      'the version is read without the credential',
    );
  });

  test("Usage left gives the one line that lets Seorak read Halcyonic's own Codex tasks", async (t) => {
    const machine = mac(t);
    mkdirSync(machine.dataDir, { mode: 0o700 });
    writeFileSync(join(machine.dataDir, 'seorak-credential'), 'x', { mode: 0o600 });
    running(machine, { usage: { availability: 'available', limits: [] } });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Usage left'), 'Ready');
    assert.match(text(machine), /Seorak needs to be told where they are/);
    assert.match(text(machine), /Seorak doesn't read OpenCode tasks/);
    assert.ok(
      text(machine).includes(
        'SEORAK_CODEX_EXTRA_DIRS="$HOME/.halcyonic/codex-home/sessions:$HOME/.halcyonic/codex-confined/*/sessions" seorak setup',
      ),
    );
  });

  test('Usage left says what the Seorak credential lacks, from what the running Halcyonic answers', async (t) => {
    const machine = mac(t);
    mkdirSync(machine.dataDir, { mode: 0o700 });
    writeFileSync(join(machine.dataDir, 'seorak-credential'), 'x', { mode: 0o600 });
    running(machine, {
      usage: { availability: 'unauthorized', reason: { code: 'insufficient_scope', message: 'x' } },
    });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Usage left'), 'To do');
    assert.match(
      text(machine),
      /can't read usage limits\. Issue a new one with sessions:read, replay:read and limits:read/,
    );
  });

  test('it lists the headsets paired, and never the revoked ones', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes');
    await machine.run('pairing', 'on');
    const device = (id: string, label: string, revoked: string | null) => ({
      device_id: id,
      label,
      paired_at: '2026-10-02T09:00:00.000Z',
      certificate_sha256: 'a'.repeat(64),
      revoked_at: revoked,
    });
    running(machine, { devices: { devices: [], connected: [] } });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Your headset'), 'To do');
    assert.match(text(machine), /no headset is paired yet/);
    assert.match(text(machine), /pnpm pair/);
    running(machine, {
      devices: {
        devices: [
          device('dev-1', 'Quest 3', null),
          device('dev-2', 'Old Quest\u202e', '2026-10-01T09:00:00.000Z'),
        ],
        connected: ['dev-1'],
      },
    });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Your headset'), 'Ready');
    assert.match(
      text(machine),
      /"Quest 3" is paired and connected\. To stop it for good: pnpm devices revoke dev-1/,
    );
    assert.doesNotMatch(text(machine), /Old Quest/);
  });

  test('a Mac set up for work on this Mac is ready', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes', 'yes');
    await machine.run('allow');
    machine.install('opencode');
    machine.install('codex');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    await machine.run('pairing', 'on');
    running(machine, {
      roots: [join(machine.home, 'HalcyonicProjects')],
      apps: ['opencode', 'codex'],
      devices: {
        devices: [
          {
            device_id: 'dev-1',
            label: 'Quest 3',
            paired_at: '2026-10-02T09:00:00.000Z',
            certificate_sha256: 'a'.repeat(64),
            revoked_at: null,
          },
        ],
        connected: [],
      },
    });
    assert.equal(await machine.run('--with-token'), 0, text(machine));
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Ready');
    assert.match(
      text(machine),
      /Codex uses Halcyonic's own Codex settings, in a folder of its own: qwen3\.6:35b-a3b-nvfp4 on this Mac\./,
    );
    assert.match(text(machine), /Codex runs only on models this Mac serves, never on OpenAI's/);
    assert.match(
      text(machine),
      /OpenCode also offers free models that run on a remote service of its own, opencode\.ai/,
    );
    assert.match(text(machine), /Everything Halcyonic needs on this Mac is ready\./);
  });

  test('it says when work can go to a remote service: Claude Agent, never Codex', async (t) => {
    const machine = mac(t);
    machine.install('codex');
    await machine.run('agent-apps');
    // The person's own Codex settings, on OpenAI, are not Halcyonic's and change nothing.
    mkdirSync(join(machine.home, '.codex'));
    writeFileSync(join(machine.home, '.codex', 'config.toml'), 'model_provider = "openai"\n');
    await machine.run();
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Ready');
    assert.match(text(machine), /Codex runs only on models this Mac serves, never on OpenAI's/);
    assert.match(
      text(machine),
      /Halcyonic's own Codex settings don't name one yet, so Codex has no model to offer/,
    );
    assert.match(text(machine), /pnpm mac-setup local-model /);
    machine.env = { ...machine.env, HALCYONIC_CLAUDE_AGENT: '1' };
    await machine.run();
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Look at this');
    assert.match(
      text(machine),
      /Claude Agent is on\. Its models run on a remote service, Anthropic's: nothing starts on one without its second press/,
    );
  });

  test('a settings file it cannot use is the first thing it says', async (t) => {
    const machine = mac(t);
    mkdirSync(machine.dataDir, { mode: 0o700 });
    writeFileSync(
      join(machine.dataDir, SETTINGS_FILE),
      '{"format": 1, "HALCYONIC_CLAUDE_AGENT": "1"}',
      { mode: 0o600 },
    );
    assert.equal(await machine.run(), 1);
    assert.match(machine.lines[2] ?? '', /^1\. Halcyonic's settings: To do$/);
    assert.match(text(machine), /only the environment sets it/);
    assert.equal(await machine.run('allow', '--yes'), 1);
    assert.match(text(machine), /Halcyonic's settings can't be changed/);
  });

  test("the companion's step recommends a model of its own and says what it needs beside the agents'", async (t) => {
    const machine = mac(t);
    await machine.run();
    assert.equal(status(machine, 'Companion'), 'Optional');
    assert.match(
      text(machine),
      /“The companion isn't set up on your computer\. Type your idea, or answer a few fixed questions\.”/,
    );
    assert.match(text(machine), /OLLAMA_MAX_LOADED_MODELS=2/);
    assert.match(
      text(machine),
      /This Mac keeps nothing of what you tell it; the headset keeps the draft\./,
    );
    assert.match(text(machine), /ollama pull qwen3\.5:9b pnpm mac-setup companion qwen3\.5:9b/);
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [...OLLAMA_MODELS, COMPANION] },
    });
    await machine.run();
    assert.doesNotMatch(text(machine), /ollama pull qwen3\.5:9b/, 'already on this Mac');
    assert.match(text(machine), /pnpm mac-setup companion qwen3\.5:9b/);
  });

  test('an Ollama address with credentials in it is never printed or asked, from the environment or the settings', async (t) => {
    const secret = 'S3CRET-not-real';
    const machine = mac(t);
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [...OLLAMA_MODELS, COMPANION] },
    });
    machine.env.HALCYONIC_COMPANION_MODEL = 'qwen3.5:9b';
    for (const address of [
      `http://u:${secret}@127.0.0.1:11434`,
      `http://elsewhere.example:11434/?key=${secret}`,
    ]) {
      machine.env.HALCYONIC_COMPANION_OLLAMA_URL = address;
      machine.lines.length = 0;
      machine.requests.length = 0;
      await machine.run();
      assert.equal(status(machine, 'Companion'), 'To do', address);
      assert.match(
        text(machine),
        /must be http:\/\/ on a loopback address with a port and nothing else/,
      );
      assert.doesNotMatch(text(machine), new RegExp(secret), address);
      assert.ok(
        !machine.requests.some((request) => request.url.includes('elsewhere')),
        'nothing asks it',
      );
    }
    machine.env.HALCYONIC_COMPANION_MODEL = undefined;
    machine.env.HALCYONIC_COMPANION_OLLAMA_URL = undefined;

    writeHostSettings(machine.dataDir, {
      HALCYONIC_COMPANION_OLLAMA_URL: `http://u:${secret}@127.0.0.1:11434`,
    });
    machine.lines.length = 0;
    machine.requests.length = 0;
    assert.equal(await machine.run('companion', 'qwen3.5:9b'), 1);
    assert.match(text(machine), /settings\.json, then try again/);
    assert.doesNotMatch(text(machine), new RegExp(secret));
    assert.equal(machine.requests.length, 0, 'nothing asks it');
  });

  test('companion records a model this Mac serves, and refuses a remote or missing one', async (t) => {
    const machine = mac(t);
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: {
        models: [
          ...OLLAMA_MODELS,
          COMPANION,
          {
            name: 'shared:latest',
            size: 1,
            capabilities: ['completion'],
            remote_host: 'https://elsewhere:443',
          },
        ],
      },
    });
    for (const [name, why] of [
      ['gpt-oss:120b-cloud', /runs on a remote service of Ollama's/],
      ['qwen3:CLOUD', /runs on a remote service of Ollama's/],
      ['shared', /runs on a remote service of Ollama's/],
      [
        'qwen3.8:27b-nvfp4',
        /doesn't have qwen3\.8:27b-nvfp4\. Downloading it is for you to run: ollama pull qwen3\.8:27b-nvfp4/,
      ],
    ] as const) {
      assert.equal(await machine.run('companion', name), 1, name);
      assert.match(text(machine), why, name);
    }
    assert.equal(machine.settings().HALCYONIC_COMPANION_MODEL, undefined);

    assert.equal(await machine.run('companion', 'qwen3.5:9b'), 0);
    assert.equal(machine.settings().HALCYONIC_COMPANION_MODEL, 'qwen3.5:9b');
    assert.equal(
      loadConfig(withSettings(machine.env, machine.settings())).companion?.model,
      'qwen3.5:9b',
    );
    await machine.run();
    assert.equal(status(machine, 'Companion'), 'Ready');
    assert.match(text(machine), /The companion asks qwen3\.5:9b on this Mac\./);

    machine.routes.delete('ollama /api/tags');
    await machine.run();
    assert.equal(status(machine, 'Companion'), 'To do');
    assert.match(text(machine), /“The companion can't run on your computer right now\./);

    assert.equal(await machine.run('companion', 'off'), 0);
    assert.equal(machine.settings().HALCYONIC_COMPANION_MODEL, undefined);
    assert.match(text(machine), /Nothing is removed from Ollama\./);
  });

  test("a companion that shares the agents' model is told to wait for their steps", async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    assert.equal(await machine.run('companion', 'qwen3.6:35b-a3b-nvfp4'), 0);
    assert.match(text(machine), /is also the agents' model/);
    await machine.run();
    assert.equal(status(machine, 'Companion'), 'Look at this');
    assert.match(text(machine), /A model of its own, such as qwen3\.5:9b, answers sooner\./);
  });

  test("the companion's step says what the running Halcyonic answers about it", async (t) => {
    const machine = mac(t);
    machine.routes.set('ollama /api/tags', {
      status: 200,
      body: { models: [...OLLAMA_MODELS, COMPANION] },
    });
    await machine.run('companion', 'qwen3.5:9b');
    running(machine, {
      companion: {
        availability: 'unavailable',
        reason: { code: 'companion_not_set_up', message: 'x' },
      },
    });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Companion'), 'To do');
    assert.match(text(machine), /restart Halcyonic to use these settings/);
    running(machine, {
      companion: {
        availability: 'available',
        companion: { model: 'qwen3.5:9b' },
        max_questions: 4,
      },
    });
    await machine.run('--with-token');
    assert.equal(status(machine, 'Companion'), 'Ready');
  });

  test("starter, with nothing installed, says how to install the VR starter's pinned set, and records nothing", async (t) => {
    const machine = mac(t);
    assert.equal(await machine.run('starter'), 1);
    const said = text(machine);
    assert.match(said, /npm ci --prefix "\$HOME\/\.halcyonic\/starters\/iwsdk-vr"/);
    assert.match(said, /--ignore-scripts/);
    assert.match(said, /--registry https:\/\/registry\.npmjs\.org\//);
    assert.match(said, /--userconfig \/dev\/null --globalconfig \/dev\/null/);
    assert.match(said, /starters\/iwsdk-vr\/package-lock\.json/);
    assert.equal(
      existsSync(join(machine.dataDir, 'starters', 'iwsdk-vr', 'installed.json')),
      false,
    );
    machine.lines.length = 0;
    await machine.run();
    assert.match(text(machine), /VR starter: Optional/);
    assert.match(text(machine), /pnpm mac-setup starter/);
  });

  test('starter records a set whose every package is the version its lockfile pins, and the check reads it Ready', async (t) => {
    const machine = mac(t);
    const folder = installStarterSet(machine.dataDir);
    assert.equal(await machine.run('starter'), 0, text(machine));
    const record = JSON.parse(readFileSync(join(folder, 'installed.json'), 'utf8'));
    assert.deepEqual(record, {
      format: 1,
      lockfile: sha256(readFileSync(STARTER_LOCKFILE, 'utf8')),
    });
    machine.lines.length = 0;
    await machine.run();
    assert.match(text(machine), /VR starter: Ready/);
  });

  test('starter refuses a set with a package at another version, and names it', async (t) => {
    const machine = mac(t);
    const folder = installStarterSet(machine.dataDir);
    writeFileSync(
      join(folder, 'node_modules', 'three', 'package.json'),
      JSON.stringify({ name: 'three', version: '0.185.0' }),
    );
    assert.equal(await machine.run('starter'), 1);
    assert.match(text(machine), /node_modules\/three/);
    assert.equal(existsSync(join(folder, 'installed.json')), false);
  });

  test('starter writes its record as a new file of its own, never through a link at its name (AUDIT SETUP-1)', async (t) => {
    const machine = mac(t);
    const folder = installStarterSet(machine.dataDir);
    const elsewhere = join(machine.root, 'elsewhere.json');
    writeFileSync(elsewhere, 'kept\n');
    symlinkSync(elsewhere, join(folder, 'installed.json'));
    assert.equal(await machine.run('starter'), 0, text(machine));
    assert.equal(readFileSync(elsewhere, 'utf8'), 'kept\n');
    const record = fs.lstatSync(join(folder, 'installed.json'));
    assert.ok(record.isFile());
    assert.equal(record.mode & 0o777, 0o600);
  });

  test('starter refuses a set with anything its lockfile does not list, a link out of it, or a link for its folder (AUDIT SETUP-2)', async (t) => {
    const cases: [string, (folder: string, root: string) => void, RegExp][] = [
      [
        'a package the lockfile does not name',
        (folder) => {
          mkdirSync(join(folder, 'node_modules', 'stranger'));
          writeFileSync(
            join(folder, 'node_modules', 'stranger', 'package.json'),
            '{"version":"1.0.0"}',
          );
        },
        /node_modules\/stranger/,
      ],
      [
        'a package folder that is a link',
        (folder, root) => {
          fs.renameSync(join(folder, 'node_modules', 'three'), join(root, 'three-copy'));
          symlinkSync(join(root, 'three-copy'), join(folder, 'node_modules', 'three'));
        },
        /node_modules\/three/,
      ],
      [
        'a link inside a package, out of the set',
        (folder, root) => symlinkSync(root, join(folder, 'node_modules', 'three', 'out')),
        /node_modules\/three/,
      ],
      [
        'a named pipe for a package.json',
        (folder) => {
          fs.rmSync(join(folder, 'node_modules', 'vite', 'package.json'));
          execFileSync('/usr/bin/mkfifo', [join(folder, 'node_modules', 'vite', 'package.json')]);
        },
        /node_modules\/vite/,
      ],
    ];
    for (const [name, spoil, named] of cases) {
      const machine = mac(t);
      const folder = installStarterSet(machine.dataDir);
      spoil(folder, machine.root);
      assert.equal(await machine.run('starter'), 1, name);
      assert.match(text(machine), named, name);
      assert.equal(existsSync(join(folder, 'installed.json')), false, name);
    }
    // npm's own .bin links, to files inside the set, are the set's.
    const machine = mac(t);
    const folder = installStarterSet(machine.dataDir);
    mkdirSync(join(folder, 'node_modules', 'typescript', 'bin'), { recursive: true });
    writeFileSync(join(folder, 'node_modules', 'typescript', 'bin', 'tsc'), '');
    mkdirSync(join(folder, 'node_modules', '.bin'));
    symlinkSync('../typescript/bin/tsc', join(folder, 'node_modules', '.bin', 'tsc'));
    writeFileSync(join(folder, 'node_modules', '.package-lock.json'), '{}');
    assert.equal(await machine.run('starter'), 0, text(machine));
    // The starter's folder itself a link to a set elsewhere is refused.
    const other = mac(t);
    const real = installStarterSet(join(other.root, 'set-elsewhere'));
    mkdirSync(join(other.dataDir, 'starters'), { recursive: true });
    symlinkSync(real, join(other.dataDir, 'starters', 'iwsdk-vr'));
    assert.equal(await other.run('starter'), 1);
    assert.equal(existsSync(join(real, 'installed.json')), false);
  });

  test('the printed commands quote their paths, for a data folder with a space in its name (AUDIT SETUP-6)', async (t) => {
    const machine = mac(t);
    machine.env.HALCYONIC_DATA_DIR = join(machine.home, 'Halcyonic Data');
    assert.equal(await machine.run('starter'), 1);
    assert.match(text(machine), /mkdir -p "\$HOME\/Halcyonic Data\/starters\/iwsdk-vr"/);
    assert.match(text(machine), /npm ci --prefix "\$HOME\/Halcyonic Data\/starters\/iwsdk-vr" /);
  });

  test("undo records the program a link names, by its own path, and says when it is not the system's git (AUDIT SETUP-4)", async (t) => {
    const machine = mac(t);
    const git = join(machine.root, 'tools', 'git-2.50');
    mkdirSync(dirname(git), { recursive: true });
    writeFileSync(git, '#!/bin/sh\n');
    chmodSync(git, 0o755);
    const link = join(machine.root, 'git');
    symlinkSync(git, link);
    assert.equal(await machine.run('undo', link), 0, text(machine));
    assert.equal(machine.settings().HALCYONIC_GIT_BIN, git);
    assert.match(text(machine), /isn't the system's own git, \/usr\/bin\/git/);
  });

  test("the check's Undo step is Ready only for a program that can run, and names it with ~ (AUDIT SETUP-5)", async (t) => {
    const machine = mac(t);
    /** The Undo step's lines, joined. */
    const undo = () => {
      const start = machine.lines.findIndex((line) => /^\d+\. Undo: /.test(line));
      const end = machine.lines.findIndex((line, at) => at > start && /^\d+\. /.test(line));
      return machine.lines
        .slice(start, end === -1 ? undefined : end)
        .map((line) => line.trim())
        .join(' ');
    };
    // A folder, named in the environment (the settings file would refuse it).
    const folderGit = join(machine.home, 'git-folder');
    mkdirSync(folderGit);
    machine.env.HALCYONIC_GIT_BIN = folderGit;
    await machine.run();
    assert.equal(status(machine, 'Undo'), 'Look at this');
    assert.match(undo(), /~\/git-folder isn't a program this Mac can run/);
    assert.ok(!undo().includes(machine.home), undo());
    delete machine.env.HALCYONIC_GIT_BIN;
    // A file that cannot run, then the same file able to.
    const git = join(machine.home, 'bin', 'git');
    mkdirSync(dirname(git), { recursive: true });
    writeFileSync(git, '#!/bin/sh\n');
    chmodSync(git, 0o644);
    writeHostSettings(machine.dataDir, { HALCYONIC_GIT_BIN: git });
    await machine.run();
    assert.equal(status(machine, 'Undo'), 'Look at this');
    assert.match(undo(), /~\/bin\/git isn't a program this Mac can run/);
    chmodSync(git, 0o755);
    await machine.run();
    assert.equal(status(machine, 'Undo'), 'Ready');
    assert.match(undo(), /Undo uses ~\/bin\/git\./);
  });

  test("undo records the system's git, so undo is on from the next start; off takes it away", async (t) => {
    const machine = mac(t);
    const git = join(machine.root, 'bin', 'git');
    mkdirSync(dirname(git), { recursive: true });
    writeFileSync(git, '#!/bin/sh\n');
    chmodSync(git, 0o755);
    machine.lines.length = 0;
    await machine.run();
    assert.match(text(machine), /Undo: Optional/);
    assert.equal(await machine.run('undo', git), 0, text(machine));
    assert.equal(machine.settings().HALCYONIC_GIT_BIN, git);
    assert.equal(loadConfig(withSettings(machine.env, machine.settings()), PINS).gitBinary, git);
    machine.lines.length = 0;
    await machine.run();
    assert.match(text(machine), /Undo: Ready/);
    assert.equal(await machine.run('undo', 'off'), 0);
    assert.equal(machine.settings().HALCYONIC_GIT_BIN, undefined);
  });

  test('undo refuses a git that is not there, is a folder or cannot run, and records nothing', async (t) => {
    const machine = mac(t);
    const notRunnable = join(machine.root, 'git-text');
    writeFileSync(notRunnable, 'not a program\n');
    chmodSync(notRunnable, 0o644);
    for (const path of [
      join(machine.root, 'missing-git'),
      machine.root,
      notRunnable,
      'relative/git',
    ]) {
      assert.equal(await machine.run('undo', path), 1, path);
      assert.equal(machine.settings().HALCYONIC_GIT_BIN, undefined, path);
    }
  });

  test('unknown commands and flags print how to use it', async (t) => {
    const machine = mac(t);
    for (const args of [
      ['help'],
      ['allow', 'a', 'b'],
      ['pairing', 'maybe'],
      ['--force'],
      ['disallow'],
    ]) {
      assert.equal(await machine.run(...args), 2, args.join(' '));
      assert.match(text(machine), /^Usage:/);
    }
  });

  test('every line follows the content guide', () => {
    assert.ok(everything.length > 100);
    const prose = everything.filter(
      (line) => !/^ {5}\S/.test(line) && !line.startsWith('  pnpm mac-setup'),
    );
    for (const line of prose) {
      // Paths and commands are data, such as the runtimes folder the runbook installs into.
      const words = line
        .split(' ')
        .filter((word) => !word.includes('/'))
        .join(' ');
      assert.doesNotMatch(
        words,
        /control plane|workstream|execution|runtime|journal|scenario|projection|snapshot|principal|capabilit|hosted|needs you|\u2014|!/i,
        line,
      );
    }
  });
});

describe("the browser's launcher (S1)", {
  skip: process.platform !== 'darwin' || !fs.existsSync('/usr/bin/clang'),
}, () => {
  test('launcher builds it into the data folder; the check says To do while a browser is set and it is missing, Ready once built', async (t) => {
    const machine = mac(t);
    await machine.run();
    assert.equal(status(machine, "New apps' browser"), 'Optional', 'no browser is set');
    const browser = join(machine.root, 'chrome-headless-shell-mac-arm64');
    fs.mkdirSync(browser);
    machine.env = {
      ...machine.env,
      HALCYONIC_BROWSER: browser,
    };
    machine.lines.length = 0;
    await machine.run();
    assert.equal(status(machine, "New apps' browser"), 'To do');
    assert.match(text(machine), /pnpm mac-setup browser/);
    machine.lines.length = 0;
    assert.equal(await machine.run('launcher'), 0);
    assert.equal(launcherProblem(machine.dataDir), null);
    assert.notEqual(audioOutputFor(machine.dataDir), null, 'the sound helper is built with it');
    assert.match(text(machine), /launcher is built/i);
    machine.lines.length = 0;
    await machine.run();
    assert.equal(status(machine, "New apps' browser"), 'Ready');
  });
});

describe('the browser set up in one command (pnpm mac-setup browser)', {
  skip: process.platform !== 'darwin' || !fs.existsSync('/usr/bin/clang'),
}, () => {
  const shell = writeShell;
  const cache = (machine: Mac) => join(machine.home, 'Library', 'Caches', 'ms-playwright');

  test("it finds the pinned browser in Playwright's cache, copies it, records only its folder and builds the launcher; pnpm dev then needs no variables", async (t) => {
    const machine = mac(t);
    shell(
      join(cache(machine), 'chromium_headless_shell-1200', 'chrome-headless-shell-mac-arm64'),
      'another build\n',
    );
    const pinned = shell(
      join(cache(machine), 'chromium_headless_shell-1248', 'chrome-headless-shell-mac-arm64'),
      CONTENT.browser,
    );
    // A link to the pinned folder is passed over: the search follows none.
    fs.mkdirSync(join(cache(machine), 'chromium_headless_shell-1300'));
    fs.symlinkSync(
      pinned,
      join(cache(machine), 'chromium_headless_shell-1300', 'chrome-headless-shell-mac-arm64'),
    );
    assert.equal(await machine.run('browser'), 0, text(machine));
    assert.deepEqual(machine.settings().HALCYONIC_BROWSER, pinned);
    assert.equal(
      Object.hasOwn(machine.settings(), 'HALCYONIC_BROWSER_SHA256'),
      false,
      'never the hash',
    );
    assert.equal(
      fs.readFileSync(join(machine.dataDir, 'browser', 'current', 'chrome-headless-shell'), 'utf8'),
      CONTENT.browser,
    );
    assert.equal(launcherProblem(machine.dataDir), null);
    assert.notEqual(audioOutputFor(machine.dataDir), null);
    assert.doesNotMatch(text(machine), /\u2014/);
    // The start reads the folder from the settings and the hash from the pin.
    const config = loadConfig(
      withSettings({ HALCYONIC_DATA_DIR: machine.dataDir }, machine.settings()),
      PINS,
    );
    assert.deepEqual(config.browser, { folder: pinned, sha256: PINS.browser.sha256 });
    machine.lines.length = 0;
    await machine.run();
    assert.equal(status(machine, "New apps' browser"), 'Ready');
  });

  test('with no pinned browser it says where it looked and what is needed, and records and copies nothing', async (t) => {
    const machine = mac(t);
    shell(
      join(cache(machine), 'chromium_headless_shell-1200', 'chrome-headless-shell-mac-arm64'),
      'another build\n',
    );
    assert.equal(await machine.run('browser'), 1);
    assert.match(
      text(machine),
      new RegExp(cache(machine).replace(machine.home, '~').replace(/[.]/g, '\\.')),
    );
    assert.match(text(machine), /chrome-headless-shell 156\.0\.8078\.4/);
    assert.equal(machine.settings().HALCYONIC_BROWSER, undefined);
    assert.equal(fs.existsSync(join(machine.dataDir, 'browser')), false);
    // A folder named outright is held to the same pin.
    const other = shell(join(machine.root, 'elsewhere'), 'another build\n');
    machine.lines.length = 0;
    assert.equal(await machine.run('browser', other), 1);
    assert.equal(machine.settings().HALCYONIC_BROWSER, undefined);
    // The pinned build with one file more is not it.
    const extra = shell(join(machine.root, 'extra'), CONTENT.browser);
    fs.writeFileSync(join(extra, 'libvk_swiftshader.dylib'), 'added');
    machine.lines.length = 0;
    assert.equal(await machine.run('browser', extra), 1);
    assert.equal(machine.settings().HALCYONIC_BROWSER, undefined);
    const named = shell(join(machine.root, 'named'), CONTENT.browser);
    machine.lines.length = 0;
    assert.equal(await machine.run('browser', named), 0, text(machine));
    assert.equal(machine.settings().HALCYONIC_BROWSER, named);
  });
});
