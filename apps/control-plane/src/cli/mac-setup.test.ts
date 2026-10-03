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
import { loopbackProof, PROOF_CHALLENGE_HEADER, PROOF_HEADER } from '../http/security.ts';
import type { Pins } from '../pins.ts';
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
      /OpenCode uses your own OpenCode settings\. With OpenCode's defaults it runs every command without asking you/,
    );
    assert.match(text(machine), /pnpm mac-setup local-model qwen3\.6:35b-a3b-nvfp4/);
  });

  test("the words about Halcyonic's own OpenCode settings follow the permissions in them", async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    await machine.run();
    assert.match(
      text(machine),
      /Halcyonic's own OpenCode settings: it asks you before running a shell command, and it can't fetch from the web\./,
    );
    const path = join(machine.dataDir, 'opencode-config', 'opencode', 'opencode.json');
    const settings = JSON.parse(readFileSync(path, 'utf8'));
    settings.permissions.push({ action: '*', resource: '*', effect: 'allow' });
    writeFileSync(path, JSON.stringify(settings), { mode: 0o600 });
    await machine.run();
    assert.equal(status(machine, 'Agent apps'), 'Look at this');
    assert.match(
      text(machine),
      /it runs some or all shell commands without asking you, so they never reach the headset to approve, and it may fetch from the web\./,
    );
  });

  test('a rule for a pattern of actions counts for every action it matches', async (t) => {
    const machine = mac(t);
    machine.install('opencode');
    await machine.run('agent-apps');
    await machine.run('local-model', 'qwen3.6:35b-a3b-nvfp4');
    const path = join(machine.dataDir, 'opencode-config', 'opencode', 'opencode.json');
    const settings = JSON.parse(readFileSync(path, 'utf8'));
    settings.permissions.push({ action: 'web*', resource: '*', effect: 'allow' });
    writeFileSync(path, JSON.stringify(settings), { mode: 0o600 });
    await machine.run();
    assert.match(
      text(machine),
      /it asks you before running a shell command, and it may fetch from the web\./,
    );
    settings.permissions.push({ action: 'sh?ll', resource: '*', effect: 'allow' });
    writeFileSync(path, JSON.stringify(settings), { mode: 0o600 });
    await machine.run();
    assert.match(text(machine), /it runs some or all shell commands without asking you/);
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
    for (const file of ['seorak-credential', 'salidium-credential']) {
      writeFileSync(join(machine.dataDir, file), 'not a real credential\n', { mode: 0o000 });
    }
    await machine.run('--with-token');
    assert.equal(status(machine, 'Usage left'), 'Ready');
    assert.equal(status(machine, 'What changed and why'), 'Ready');
    for (const file of ['seorak-credential', 'salidium-credential']) {
      chmodSync(join(machine.dataDir, file), 0o644);
    }
    await machine.run('--with-token');
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
    assert.equal(await machine.run('--with-token'), 0, text(machine));
    assert.equal(status(machine, 'Where work goes, and what it costs'), 'Ready');
    assert.match(
      text(machine),
      /Codex: your own Codex settings name Ollama, so the headset lists Codex's model as running on this Mac\./,
    );
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
      /Codex: your own Codex settings name OpenAI, their default, so the headset lists Codex's models as running on a remote service\. Nothing starts on one without its second press/,
    );
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
