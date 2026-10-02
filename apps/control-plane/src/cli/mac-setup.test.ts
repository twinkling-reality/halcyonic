import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { loadConfig } from '../config.ts';
import { readHostSettings, SETTINGS_FILE, settingRoots, withSettings } from '../settings.ts';
import {
  assessFolder,
  FOLDER_MEANING,
  type MacSetupIo,
  type Pins,
  runMacSetup,
} from './mac-setup.ts';

/** Every line any run printed, held to the content guide at the end. */
const everything: string[] = [];

const CONTENT = {
  opencode: 'opencode 2.0.18, for tests\n',
  codex: 'codex 0.157.0, for tests\n',
  whisperModel: 'speech model, for tests\n',
  whisperVadModel: 'voice activity model, for tests\n',
};

function sha256(text: string): string {
  return createHash('sha256').update(text).digest('hex');
}

const PINS: Pins = {
  opencode: { path: 'runtimes/opencode/bin/opencode', sha256: sha256(CONTENT.opencode) },
  codex: { path: 'runtimes/codex/bin/codex', sha256: sha256(CONTENT.codex) },
  whisperModel: { path: 'speech/models/model.bin', sha256: sha256(CONTENT.whisperModel) },
  whisperVadModel: { path: 'speech/models/vad.bin', sha256: sha256(CONTENT.whisperVadModel) },
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
    rg: true,
    interactive: true,
    env: { HALCYONIC_DATA_DIR: dataDir, HALCYONIC_PORT: '47999' },
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
          const host = url.hostname === 'ollama.test' ? 'ollama' : 'halcyonic';
          const answer = routes.get(`${host} ${url.pathname}`);
          if (answer === undefined) throw new TypeError('fetch failed');
          return new Response(JSON.stringify(answer.body), { status: answer.status });
        }) as typeof fetch,
        ollamaUrl: 'http://ollama.test:11434',
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
  uses: { roots?: string[]; apps?: string[]; devices?: unknown; usage?: unknown } = {},
) {
  mkdirSync(machine.dataDir, { recursive: true, mode: 0o700 });
  writeFileSync(join(machine.dataDir, 'access-token'), 'a'.repeat(43), { mode: 0o600 });
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
    assert.equal(status(machine, 'Your headset'), 'To do');
    const output = text(machine);
    for (const sentence of FOLDER_MEANING) {
      assert.ok(output.includes(sentence), sentence);
    }
    assert.match(output, /Your Mac doesn't allow any folder yet\./);
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
    assert.match(output, /Start with “Folders agents may use”\./);
    assert.equal(machine.lines.filter((line) => line.includes('--ignore-scripts')).length, 2);
    assert.equal(existsSync(machine.dataDir), false, 'checking writes nothing');
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

  test('a folder holding macOS, its apps or other people is never allowed', () => {
    for (const path of [
      '/',
      '/Users',
      '/Volumes',
      '/System/Library',
      '/Library/Preferences',
      '/Applications',
      '/usr/local',
      '/private/tmp',
      '/tmp',
      '/opt',
    ]) {
      assert.equal(
        assessFolder(path, '/Users/someone', '/Users/someone/.halcyonic').verdict,
        'refused',
        path,
      );
    }
    assert.equal(
      assessFolder('/Volumes/Work/projects', '/Users/someone', '/Users/someone/.halcyonic').verdict,
      'fine',
    );
    assert.equal(
      assessFolder('/Users/someone/dev', '/Users/someone', '/Users/someone/.halcyonic').verdict,
      'fine',
    );
    assert.equal(
      assessFolder('/Users/someone/Documents', '/Users/someone', '/Users/someone/.halcyonic')
        .verdict,
      'broad',
    );
    assert.equal(
      assessFolder('/Users/someone/Documents/code', '/Users/someone', '/Users/someone/.halcyonic')
        .verdict,
      'fine',
    );
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
      /OpenCode uses your own OpenCode settings\. With OpenCode's defaults it runs every command without asking you/,
    );
    assert.match(text(machine), /pnpm mac-setup local-model qwen3\.6:35b-a3b-nvfp4/);
  });

  test('without ripgrep, the check says OpenCode would download it', async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    machine.rg = false;
    await machine.run();
    assert.match(text(machine), /OpenCode would download it from GitHub/);
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

  test('pairing on and off write the listener setting and say what each means', async (t) => {
    const machine = mac(t);
    assert.equal(await machine.run('pairing', 'on'), 0);
    assert.equal(machine.settings().HALCYONIC_NETWORK_HOST, '0.0.0.0');
    assert.match(text(machine), /Only a headset you pair can use it/);
    assert.match(text(machine), /macOS asks whether node may accept incoming connections/);
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
    machine.answers.push('yes');
    await machine.run('allow');
    running(machine, { roots: [] });
    assert.equal(await machine.run('pairing', 'on'), 0);
    assert.match(text(machine), /Halcyonic is running: restart it to use this\./);
    assert.match(
      text(machine),
      /Restarting stops any agent at work, and its task then shows Can't tell yet\./,
    );
    await machine.run();
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Look at this');
    running(machine, { roots: [join(machine.home, 'HalcyonicProjects')] });
    await machine.run();
    assert.equal(status(machine, 'Halcyonic running on this Mac'), 'Ready');
  });

  test('with --no-token it neither reads the access token nor sends one', async (t) => {
    const machine = mac(t);
    running(machine);
    chmodSync(join(machine.dataDir, 'access-token'), 0o000);
    await machine.run();
    assert.match(text(machine), /there is no access token/);
    machine.requests.length = 0;
    await machine.run('--no-token');
    assert.match(text(machine), /Checked without its access token/);
    assert.ok(machine.requests.every((request) => request.authorization === null));
    assert.ok(machine.requests.some((request) => request.url.endsWith('/api/health')));
    assert.ok(!machine.requests.some((request) => request.url.endsWith('/api/snapshot')));
  });

  test('it never opens a credential: it checks only that other users cannot read it', async (t) => {
    const machine = mac(t);
    running(machine, { usage: { availability: 'available', limits: [] } });
    // Unreadable even to their owner: opening either would fail.
    for (const file of ['seorak-credential', 'salidium-credential']) {
      writeFileSync(join(machine.dataDir, file), 'not a real credential\n', { mode: 0o000 });
    }
    await machine.run();
    assert.equal(status(machine, 'Usage left'), 'Ready');
    assert.equal(status(machine, 'What changed and why'), 'Ready');
    for (const file of ['seorak-credential', 'salidium-credential']) {
      chmodSync(join(machine.dataDir, file), 0o644);
    }
    await machine.run();
    assert.equal(status(machine, 'Usage left'), 'To do');
    assert.equal(status(machine, 'What changed and why'), 'To do');
    assert.match(text(machine), /chmod 600/);
  });

  test('Usage left says what the Seorak credential lacks, from what the running Halcyonic answers', async (t) => {
    const machine = mac(t);
    mkdirSync(machine.dataDir, { mode: 0o700 });
    writeFileSync(join(machine.dataDir, 'seorak-credential'), 'x', { mode: 0o600 });
    running(machine, {
      usage: { availability: 'unauthorized', reason: { code: 'insufficient_scope', message: 'x' } },
    });
    await machine.run();
    assert.equal(status(machine, 'Usage left'), 'To do');
    assert.match(
      text(machine),
      /can't read usage limits\. Issue a new one with sessions:read, replay:read and limits:read/,
    );
  });

  test('it lists the headsets paired, and never the revoked ones', async (t) => {
    const machine = mac(t);
    await machine.run('pairing', 'on');
    const device = (id: string, label: string, revoked: string | null) => ({
      device_id: id,
      label,
      paired_at: '2026-10-02T09:00:00.000Z',
      certificate_sha256: 'a'.repeat(64),
      revoked_at: revoked,
    });
    running(machine, { devices: { devices: [], connected: [] } });
    await machine.run();
    assert.equal(status(machine, 'Your headset'), 'To do');
    assert.match(text(machine), /no headset is paired yet/);
    assert.match(text(machine), /pnpm pair/);
    running(machine, {
      devices: {
        devices: [
          device('dev-1', 'Quest 3', null),
          device('dev-2', 'Old Quest‮', '2026-10-01T09:00:00.000Z'),
        ],
        connected: ['dev-1'],
      },
    });
    await machine.run();
    assert.equal(status(machine, 'Your headset'), 'Ready');
    assert.match(
      text(machine),
      /"Quest 3" is paired and connected\. To stop it for good: pnpm devices revoke dev-1/,
    );
    assert.doesNotMatch(text(machine), /Old Quest/);
  });

  test('a Mac set up for work on this Mac is ready', async (t) => {
    const machine = mac(t);
    machine.answers.push('yes');
    await machine.run('allow');
    machine.install('opencode');
    machine.install('codex');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    await machine.run('pairing', 'on');
    mkdirSync(join(machine.home, '.codex'));
    writeFileSync(
      join(machine.home, '.codex', 'config.toml'),
      'model_provider = "ollama"\nmodel = "qwen3.6:35b-a3b-nvfp4"\n',
    );
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
    assert.equal(await machine.run(), 0, text(machine));
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Ready');
    assert.match(text(machine), /Codex is set to Ollama on this Mac/);
    assert.match(
      text(machine),
      /OpenCode also offers free models that run on a remote service of its own, opencode\.ai/,
    );
    assert.match(text(machine), /Everything Halcyonic needs on this Mac is ready\./);
  });

  test('it says when work can go to a remote service: Codex on OpenAI, or Claude Agent', async (t) => {
    const machine = mac(t);
    machine.install('codex');
    await machine.run('agent-apps');
    machine.env = { ...machine.env, HALCYONIC_CLAUDE_AGENT: '1' };
    await machine.run();
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Look at this');
    assert.match(
      text(machine),
      /Codex uses your own Codex settings, which run it on a remote service \(OpenAI, its default\)/,
    );
    assert.match(text(machine), /Claude Agent is on: every task on it runs on a remote service/);
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
        /control plane|workstream|execution|runtime|journal|scenario|projection|snapshot|principal|capabilit|hosted|needs you|—|!/i,
        line,
      );
    }
  });
});
