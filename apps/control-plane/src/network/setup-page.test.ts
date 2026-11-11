import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { type MacSetupIo, tunnelProgram } from '../cli/mac-setup.ts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { APPLE_SILICON_PINS, type Pins } from '../pins.ts';
import { readHostSettings, rememberNetworkListener, writeHostSettings } from '../settings.ts';
import { capturingLogger, createTestControlPlane, startTestServer } from '../testing/harness.ts';
import {
  type CommandRunner,
  mountSetup,
  pageStopShare,
  RESTART_EXIT_CODE,
  runCommand,
  type SetupState,
  SetupView,
  type SharedNow,
  settingsForRestart,
  setupDownloads,
  TUNNEL_ARCHIVE_SHA256,
  TUNNEL_ARCHIVE_URL,
} from './setup-page.ts';

/** CORE's scratch block; the page takes the first free port in it. */
const PORTS = [47900, 47909] as const;
const STARTER = fileURLToPath(new URL('../../../../starters/iwsdk-vr', import.meta.url));

interface Mac {
  readonly home: string;
  readonly dataDir: string;
  readonly io: (print: (line: string) => void) => MacSetupIo;
}

/** A Mac of its own in a scratch folder, offline: nothing it checks reaches a network. */
function mac(t: TestContext, pins: Pins | null = null): Mac {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-setup-page-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const home = join(root, 'home');
  const dataDir = join(home, '.halcyonic');
  mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  return {
    home,
    dataDir,
    io: (print) => ({
      env: { HOME: home, HALCYONIC_DATA_DIR: dataDir, HALCYONIC_PORT: '47909' },
      home,
      print,
      ask: null,
      fetch: (async () => {
        throw new TypeError('fetch failed');
      }) as typeof fetch,
      ollamaUrl: 'http://127.0.0.1:47908',
      memoryBytes: 64 * 1024 ** 3,
      which: () => null,
      firewall: () => null,
      pins,
    }),
  };
}

interface Run {
  readonly argv: string[];
  readonly cwd: string;
  readonly line: (line: string) => void;
  readonly finish: (code: number) => void;
  stopped: boolean;
}

/** A runner that runs nothing: each command waits until the test finishes it. Nothing is downloaded. */
function fakeRunner(): { readonly run: CommandRunner; readonly runs: Run[] } {
  const runs: Run[] = [];
  const run: CommandRunner = (argv, options) => {
    let finish: (code: number) => void = () => undefined;
    const done = new Promise<number>((resolve) => {
      finish = resolve;
    });
    const entry: Run = {
      argv: [...argv],
      cwd: options.cwd,
      line: options.onLine,
      finish,
      stopped: false,
    };
    runs.push(entry);
    return {
      done,
      stop: () => {
        entry.stopped = true;
        finish(143);
      },
    };
  };
  return { run, runs };
}

interface Setup {
  readonly view: SetupView;
  readonly runs: Run[];
  readonly restarts: { count: number };
  readonly mac: Mac;
}

function setup(
  t: TestContext,
  options: {
    canRestart?: boolean;
    tasks?: readonly string[];
    pins?: Pins;
    shared?: SharedNow[];
    stopped?: string[];
    refuseStop?: boolean;
  } = {},
): Setup {
  const machine = mac(t, options.pins ?? null);
  const { run, runs } = fakeRunner();
  const restarts = { count: 0 };
  const applied = () => settingsForRestart(readHostSettings(machine.dataDir));
  const view = new SetupView(
    {
      dataDir: machine.dataDir,
      home: machine.home,
      io: machine.io,
      run,
      appliedAtStart: applied(),
      appliedNow: applied,
      canRestart: options.canRestart ?? true,
      tasksThatPause: () => options.tasks ?? [],
      restart: () => {
        restarts.count += 1;
      },
      shared: () => options.shared ?? [],
      stopShare: (projectId) => {
        if (options.refuseStop === true) return 'refused';
        const shared = options.shared ?? [];
        const at = shared.findIndex((share) => share.project_id === projectId);
        if (at < 0) return 'not_shared';
        shared.splice(at, 1);
        options.stopped?.push(projectId);
        return 'stopped';
      },
    },
    setupDownloads(machine.dataDir, () => null, options.pins ?? null),
  );
  return { view, runs, restarts, mac: machine };
}

/** The state once the running download has ended. */
async function ended(view: SetupView): Promise<SetupState> {
  for (let tries = 0; tries < 500; tries += 1) {
    const state = await view.state();
    if (state.job?.state !== 'running') return state;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error('the download never ended');
}

function step(state: SetupState, id: string) {
  const found = state.steps.find((entry) => entry.id === id);
  assert.ok(found, `the page shows the ${id} step`);
  return found;
}

describe('the setup view', () => {
  test('shows every setup step with its presses, and what each download fetches before it', async (t) => {
    const { view, runs } = setup(t);
    const state = await view.state();
    for (const id of ['folders', 'agent_apps', 'models', 'cost', 'starter', 'companion']) {
      step(state, id);
    }
    assert.equal(state.job, null);
    assert.equal(state.restart_needed, false, 'nothing has changed since the start');
    assert.equal(state.can_restart, true);
    const opencode = step(state, 'agent_apps').actions.find((action) => action.arg === 'opencode');
    assert.match(
      opencode?.fetches ?? '',
      /OpenCode 2\.0\.18 .*about 171 MB.*public npm registry, with no install scripts/,
    );
    for (const action of step(state, 'models').actions) {
      assert.match(action.fetches ?? '', /GB, into Ollama on this Mac/);
    }
    assert.doesNotMatch(
      JSON.stringify(state.steps.flatMap((entry) => entry.actions)),
      /claude|anthropic|AGENT_ENV/i,
      'no press turns on the paid runtime: its words say it is set up by hand only',
    );
    assert.equal(runs.length, 0, 'showing the page runs nothing');
    assert.ok(
      state.steps.every((entry) => Array.isArray(entry.next)),
      'every step carries the commands setup prints under it, none run by the page',
    );
  });

  test('a press acts only on its second press, on what the page said it would do', async (t) => {
    const { view, mac: machine } = setup(t);
    const projects = join(machine.home, 'HalcyonicProjects');
    const forged = await view.act('allow', null, true);
    assert.match(forged.confirm?.text ?? '', /pnpm mac-setup allow --yes/);
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_PROJECT_ROOTS, undefined);
    const other = await view.act('undo', null, true);
    assert.equal(
      other.confirm?.action,
      'undo',
      'a second press of another action is a first press',
    );
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_PROJECT_ROOTS, undefined);
    await view.act('allow', null, false);
    assert.equal(existsSync(projects), false, 'the first press changes nothing');
    const done = await view.act('allow', null, true);
    assert.equal(done.confirm, null);
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_PROJECT_ROOTS, projects);
    assert.ok(done.said.length > 0, 'the page says what setup said');
    assert.ok(
      done.said.every((line) => !line.includes(machine.home)),
      'and names the home folder as ~',
    );
    assert.equal(done.restart_needed, true, 'a new folder applies at the next start');
  });

  test("each session's press is confirmed only in that session", async (t) => {
    const { view, mac: machine } = setup(t);
    const asked = await view.act('allow', null, false, 'first');
    assert.match(asked.confirm?.text ?? '', /allow --yes/);
    const elsewhere = await view.act('allow', null, true, 'second');
    assert.equal(elsewhere.confirm?.action, 'allow', 'the other session gets its own first press');
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_PROJECT_ROOTS, undefined);
    assert.equal((await view.state('first')).confirm?.action, 'allow', 'the first keeps its own');
    assert.equal((await view.state('third')).confirm, null);
    const done = await view.act('allow', null, true, 'first');
    assert.equal(done.confirm, null);
    assert.ok(readHostSettings(machine.dataDir).HALCYONIC_PROJECT_ROOTS);
    assert.deepEqual(
      (await view.state('third')).said,
      [],
      'what setup said is shown where it was pressed',
    );
  });

  test('refuses every press it does not offer', async (t) => {
    const { view, runs, mac: machine } = setup(t);
    for (const [action, arg] of [
      ['claude_agent', null],
      ['local_model', 'qwen3-coder:480b-cloud'],
      ['companion', 'llama3:70b'],
      ['undo', 'sideways'],
      ['download', 'left-pad'],
      [42, null],
    ] as const) {
      await view.act(action, arg, false);
      const state = await view.act(action, arg, true);
      assert.equal(state.confirm, null, `${String(action)} asks nothing`);
      assert.match(state.said.join(' '), /not one this page offers/);
    }
    assert.equal(runs.length, 0);
    assert.deepEqual(readHostSettings(machine.dataDir), {});
  });

  test('runs one download at a time, as setup prints it, shows its progress and stops it', async (t) => {
    const { view, runs, mac: machine } = setup(t);
    const asked = await view.act('download', 'opencode', false);
    assert.match(asked.confirm?.text ?? '', /OpenCode 2\.0\.18 .* Go On downloads it\./);
    assert.equal(runs.length, 0, 'the first press downloads nothing');
    const started = await view.act('download', 'opencode', true);
    assert.equal(started.job?.state, 'running');
    assert.deepEqual(runs[0]?.argv, [
      'npm',
      'install',
      '--prefix',
      join(machine.dataDir, 'runtimes', 'opencode-2.0.18'),
      '@opencode/cli@2.0.18',
      '--ignore-scripts',
    ]);
    const second = await view.act('download', 'codex', false);
    assert.match(second.said.join(' '), /Another download is running/);
    assert.equal(second.confirm, null);
    assert.equal(runs.length, 1, 'never two at once');
    const run = runs[0] as Run;
    run.line(`\u001b[32madded\u001b[0m 1 package in ${machine.home}/x\u0007​`);
    for (let index = 0; index < 20; index += 1) run.line(`line ${index}`);
    run.line('x'.repeat(500));
    run.line('   ');
    const shown = (await view.state()).job;
    assert.equal(shown?.lines.length, 12, 'the last 12 lines');
    assert.equal(shown?.lines.at(-1), 'x'.repeat(200), 'each at most 200 characters');
    assert.equal(shown?.lines[0], 'line 9');
    const fresh = setup(t);
    await fresh.view.act('download', 'opencode', false);
    await fresh.view.act('download', 'opencode', true);
    fresh.runs[0]?.line(`\u001b[32madded\u001b[0m 1 package in ${fresh.mac.home}/x\u0007​`);
    assert.deepEqual((await fresh.view.state()).job?.lines, ['added 1 package in ~/x']);
    await view.stop();
    assert.equal(run.stopped, true);
    assert.equal((await ended(view)).job?.state, 'stopped');
    await view.act('download', 'codex', false);
    await view.act('download', 'codex', true);
    assert.equal(runs.length, 2, 'once stopped, another can start');
    assert.ok(runs[1]?.argv.includes('@openai/codex@0.157.0'));
    runs[1]?.finish(1);
    assert.equal((await ended(view)).job?.state, 'failed');
    fresh.runs[0]?.finish(0);
    const unchecked = await ended(fresh.view);
    assert.equal(
      unchecked.job?.state,
      'not_set_up',
      'with no pins to check it by, nothing is recorded',
    );
    assert.match(unchecked.job?.lines.at(-1) ?? '', /checksums for Apple silicon Macs only/);
  });

  test('link sharing downloads the checked tunnel release, checks the archive before unpacking it, and is recorded only when the program inside is the checked one (ADR 0054)', async (t) => {
    const { view, runs, mac: machine } = setup(t);
    const before = await view.state();
    const sharing = step(before, 'sharing');
    assert.equal(sharing.status, 'optional', 'off until the person turns it on');
    assert.deepEqual(
      sharing.actions.map((action) => [action.action, action.arg]),
      [
        ['download', 'tunnel'],
        ['tunnel', 'off'],
      ],
    );
    const asked = await view.act('download', 'tunnel', false);
    assert.match(
      asked.confirm?.text ?? '',
      /cloudflared 2026\.10\.7 .*18\.9 MB.* Go On downloads it\./,
    );
    assert.equal(runs.length, 0, 'the first press downloads nothing');
    await view.act('download', 'tunnel', true);
    const program = tunnelProgram(machine.dataDir);
    const folder = join(program, '..');
    const archive = join(folder, 'cloudflared-darwin-arm64.tgz');
    assert.deepEqual(runs[0]?.argv, [
      '/usr/bin/curl',
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
      '--max-filesize',
      '30000000',
      '--output',
      archive,
      TUNNEL_ARCHIVE_URL,
    ]);
    assert.equal(runs[0]?.argv[1], '-q', "first, so the person's own curl settings never apply");
    assert.equal(
      readFileSync(`${archive}.sha256`, 'utf8'),
      `${TUNNEL_ARCHIVE_SHA256}  ${archive}\n`,
      'the release archive by its pin',
    );
    const next = async (count: number) => {
      for (let tries = 0; tries < 500 && runs.length < count; tries += 1)
        await new Promise((resolve) => setTimeout(resolve, 10));
      return runs[count - 1] as Run;
    };
    (runs[0] as Run).finish(0);
    assert.deepEqual((await next(2)).argv, [
      '/usr/bin/shasum',
      '-a',
      '256',
      '-c',
      `${archive}.sha256`,
    ]);
    (runs[1] as Run).finish(0);
    assert.deepEqual((await next(3)).argv, [
      '/usr/bin/tar',
      '-xzf',
      archive,
      '-C',
      folder,
      'cloudflared',
    ]);
    // A program that is not the checked release, as a tampered archive would unpack.
    writeFileSync(program, 'not the checked program');
    (runs[2] as Run).finish(0);
    const done = await ended(view);
    assert.equal(done.job?.state, 'not_set_up');
    assert.match(done.job?.lines.join(' ') ?? '', /isn't the checked tunnel program 2026\.10\.7/);
    assert.equal(
      readHostSettings(machine.dataDir).HALCYONIC_TUNNEL_BIN,
      undefined,
      'never recorded',
    );
    // An archive whose SHA-256 is not the pin is never unpacked.
    const fresh = setup(t);
    await fresh.view.act('download', 'tunnel', false);
    await fresh.view.act('download', 'tunnel', true);
    (fresh.runs[0] as Run).finish(0);
    for (let tries = 0; tries < 500 && fresh.runs.length < 2; tries += 1)
      await new Promise((resolve) => setTimeout(resolve, 10));
    (fresh.runs[1] as Run).finish(1);
    assert.equal((await ended(fresh.view)).job?.state, 'failed');
    assert.equal(fresh.runs.length, 2, 'tar never ran');
  });

  test('a recorded tunnel program that is not the checked one is named, and link sharing turns off', async (t) => {
    const { view, mac: machine } = setup(t);
    const program = tunnelProgram(machine.dataDir);
    mkdirSync(join(program, '..'), { recursive: true });
    writeFileSync(program, 'not the checked program');
    writeHostSettings(machine.dataDir, { HALCYONIC_TUNNEL_BIN: program });
    assert.equal(step(await view.act('check', null, false), 'sharing').status, 'look');
    await view.act('tunnel', 'off', false);
    await view.act('tunnel', 'off', true);
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_TUNNEL_BIN, undefined);
    assert.equal(step(await view.act('check', null, false), 'sharing').status, 'optional');
  });

  test('a downloaded agent app is set up only when it is the copy Halcyonic was checked with', async (t) => {
    const binary = 'runtimes/opencode-2.0.18/node_modules/.bin/opencode';
    const checked = 'the copy Halcyonic was checked with';
    const pins: Pins = {
      ...APPLE_SILICON_PINS,
      opencode: { path: binary, sha256: createHash('sha256').update(checked).digest('hex') },
    };
    const { view, runs, mac: machine } = setup(t, { pins });
    const install = async (content: string) => {
      await view.act('download', 'opencode', false);
      await view.act('download', 'opencode', true);
      const path = join(machine.dataDir, binary);
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, content);
      runs.at(-1)?.finish(0);
      return ended(view);
    };
    const changed = await install('a copy someone changed');
    assert.equal(changed.job?.state, 'not_set_up');
    assert.match(changed.job?.lines.join(' ') ?? '', /isn't the copy Halcyonic was checked with/);
    assert.equal(readHostSettings(machine.dataDir).HALCYONIC_OPENCODE_BIN, undefined);
    const good = await install(checked);
    assert.equal(good.job?.state, 'done');
    assert.equal(
      readHostSettings(machine.dataDir).HALCYONIC_OPENCODE_BIN,
      join(machine.dataDir, binary),
    );
    const again = await install('changed after it was set up');
    assert.equal(
      again.job?.state,
      'not_set_up',
      'a setting from before does not make a changed copy Done',
    );
  });

  test('the starter uses its pinned lockfile, none of the person npm settings, and a fresh cache', async (t) => {
    const { view, runs, mac: machine } = setup(t);
    await view.act('download', 'starter', false);
    await view.act('download', 'starter', true);
    const argv = runs[0]?.argv ?? [];
    assert.equal(argv[1], 'ci');
    for (const pair of [
      ['--userconfig', '/dev/null'],
      ['--globalconfig', '/dev/null'],
      ['--registry', 'https://registry.npmjs.org/'],
    ] as const) {
      assert.equal(argv[argv.indexOf(pair[0]) + 1], pair[1]);
    }
    assert.ok(argv.includes('--ignore-scripts'));
    const cache = argv[argv.indexOf('--cache') + 1] ?? '';
    assert.ok(existsSync(cache) && cache.includes('halcyonic-npm-cache-'), 'a cache of its own');
    const copied = join(machine.dataDir, 'starters', 'iwsdk-vr', 'package-lock.json');
    assert.equal(
      readFileSync(copied, 'utf8'),
      readFileSync(join(STARTER, 'package-lock.json'), 'utf8'),
    );
    runs[0]?.finish(0);
    await ended(view);
    assert.equal(existsSync(cache), false, 'the cache is removed after');
  });

  test('a model is pulled through Ollama on this Mac', async (t) => {
    const { view, runs } = setup(t);
    const arg = step(await view.state(), 'models').actions[0]?.arg ?? '';
    assert.ok(arg.startsWith('model:'));
    await view.act('download', arg, false);
    await view.act('download', arg, true);
    assert.deepEqual(runs[0]?.argv, ['ollama', 'pull', arg.slice('model:'.length)]);
  });

  test('a restart says first which tasks pause, and happens only on the second press', async (t) => {
    const { view, restarts } = setup(t, { tasks: ['Build the menu', 'Fix the score'] });
    const asked = await view.act('restart', null, false);
    assert.match(asked.confirm?.text ?? '', /Build the menu, Fix the score/);
    assert.equal(restarts.count, 0);
    const going = await view.act('restart', null, true);
    assert.equal(going.restarting, true);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(restarts.count, 1);
    await view.act('restart', null, false);
    await view.act('restart', null, true);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(restarts.count, 1, 'once');
    const idle = setup(t);
    assert.match(
      (await idle.view.act('restart', null, false)).confirm?.text ?? '',
      /No task is running/,
    );
  });

  test('without the launcher the page cannot restart, and says how', async (t) => {
    const { view, restarts } = setup(t, { canRestart: false });
    assert.equal((await view.state()).can_restart, false);
    await view.act('restart', null, false);
    const state = await view.act('restart', null, true);
    assert.match(state.said.join(' '), /cannot start itself again/);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(restarts.count, 0);
  });

  test('lists each game shared now with its way, never a link as text, and stops one by one press (ADR 0054)', async (t) => {
    const LINK = 'https://bright-quiet-garden.trycloudflare.com';
    const shared: SharedNow[] = [
      { project_id: 'p-link', name: 'Kite Run', way: 'link', url: LINK },
      { project_id: 'p-wifi', name: 'Tail', way: 'wifi', url: 'http://192.168.1.20:47970' },
    ];
    const stopped: string[] = [];
    const { view } = setup(t, { shared, stopped });
    const state = await view.state();
    assert.deepEqual(state.shared, [
      { project_id: 'p-link', name: 'Kite Run', way: 'link', address_text: null, url: LINK },
      {
        project_id: 'p-wifi',
        name: 'Tail',
        way: 'wifi',
        address_text: '192.168.1.20:47970',
        url: 'http://192.168.1.20:47970',
      },
    ]);
    const after = await view.stopShare('p-wifi');
    assert.deepEqual(stopped, ['p-wifi'], 'one press stops it');
    assert.deepEqual(
      after.shared.map((share) => share.project_id),
      ['p-link'],
    );
    const unknown = await view.stopShare('p-none');
    assert.deepEqual(unknown.said, ['This game is not shared.']);
    assert.deepEqual((await view.stopShare(42)).said, ['This game is not shared.']);
    assert.deepEqual(stopped, ['p-wifi']);
    // A stop the control plane did not accept says so, never that the game is not shared (CORE).
    const refusing = setup(t, { shared: [...shared], refuseStop: true });
    assert.deepEqual((await refusing.view.stopShare('p-link')).said, [
      "This game couldn't be stopped. Try again.",
    ]);
  });

  test("the person's Stop is recorded as theirs, a local press over HTTP, never a host-made stop (AUDIT2)", () => {
    const { controlPlane } = createTestControlPlane({ logger: capturingLogger().logger });
    const project = '01a19000-0000-7000-8000-00000000aa31';
    const check = '01a19000-0000-7000-8000-00000000cc31';
    const scope = { project_id: project, workstream_id: null, execution_id: null };
    const record = (type: string, payload: unknown) =>
      controlPlane.recorder.record(
        controlPlaneDraft(
          type as never,
          scope as never,
          payload as never,
          '2026-11-11T10:00:00.000Z' as never,
          NO_CAUSE,
        ),
      );
    record('project.created', { name: 'Tail', location: null });
    record('project.preview.started', { check_id: check, port: 47931 });
    record('project.share.started', {
      check_id: check,
      url: 'http://192.168.1.20:47970',
      way: 'wifi',
    });
    let n = 0;
    const stop = pageStopShare({
      submit: (command, via, principal) => controlPlane.commands.submit(command, via, principal),
      isShared: (id) => controlPlane.projection.share(id) !== undefined,
      nextId: () => {
        n += 1;
        return `01a19000-0000-7000-8000-${String(n).padStart(12, '0')}`;
      },
      now: () => new Date('2026-11-11T10:00:01.000Z'),
    });
    assert.equal(stop('01a19000-0000-7000-8000-00000000aa99'), 'not_shared');
    assert.equal(stop(project), 'stopped');
    const accepted = [...controlPlane.journal.readAll()]
      .map((stored) => stored.event)
      .find(
        (event) =>
          event.event_type === 'command.accepted' &&
          event.payload.command.command_type === 'project.share.stop',
      );
    assert.ok(accepted?.event_type === 'command.accepted');
    assert.equal(accepted.payload.received_via, 'http');
    assert.deepEqual(accepted.payload.principal, { kind: 'local' });
  });

  test('pairing applies live, so turning it on or off asks for no restart', async (t) => {
    const { view, mac: machine } = setup(t);
    rememberNetworkListener(machine.dataDir, true);
    assert.equal((await view.state()).restart_needed, false);
    rememberNetworkListener(machine.dataDir, false);
    assert.equal((await view.state()).restart_needed, false);
    writeHostSettings(machine.dataDir, { HALCYONIC_GIT_BIN: '/usr/bin/git' });
    assert.equal((await view.state()).restart_needed, true);
    assert.equal(
      settingsForRestart({ HALCYONIC_NETWORK_HOST: '0.0.0.0', B: '2', A: '1' }),
      settingsForRestart({ A: '1', B: '2' }),
    );
    assert.equal(RESTART_EXIT_CODE, 75, 'the code Start Halcyonic.command starts again on');
  });
});

interface Answer {
  readonly status: number;
  readonly headers: Record<string, string | string[] | undefined>;
  readonly body: string;
}

function call(
  port: number,
  method: string,
  path: string,
  headers: Record<string, string> = {},
  body?: string,
): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port,
        path,
        method,
        headers: { host: `127.0.0.1:${port}`, 'sec-fetch-site': 'none', ...headers },
      },
      (res) => {
        let text = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          text += chunk;
        });
        res.on('end', () =>
          resolve({ status: res.statusCode ?? 0, headers: res.headers, body: text }),
        );
      },
    );
    req.on('error', reject);
    if (body !== undefined) req.write(body);
    req.end();
  });
}

describe('the setup view on the pairing page', () => {
  test('serves only behind the page session and its origin checks', async (t) => {
    const { view, runs } = setup(t);
    const server = await startTestServer({ page: { ports: PORTS, mount: mountSetup(view) } });
    t.after(() => server.stop());
    const page = server.page;
    assert.ok(page);
    const port = page.port;
    for (const path of ['/setup', '/setup.js', '/setup.css', '/setup/state']) {
      assert.equal((await call(port, 'GET', path)).status, 403, `${path} needs the session`);
    }
    const link = new URL(page.link());
    const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
    const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
    const shown = await call(port, 'GET', '/setup', { cookie });
    assert.equal(shown.status, 200);
    assert.match(shown.body, /<h1>Set Up This Computer<\/h1>/);
    assert.ok(shown.headers['content-security-policy'], 'under the page CSP');
    const state = await call(port, 'GET', '/setup/state', {
      cookie,
      'sec-fetch-site': 'same-origin',
    });
    assert.equal(state.status, 200);
    assert.ok((JSON.parse(state.body) as SetupState).steps.length > 5);
    const pressHeaders = {
      cookie,
      origin: `http://127.0.0.1:${port}`,
      'sec-fetch-site': 'same-origin',
      'content-type': 'application/json',
    };
    const press = JSON.stringify({ action: 'download', arg: 'opencode', confirmed: false });
    assert.equal(
      (
        await call(
          port,
          'POST',
          '/setup/act',
          { ...pressHeaders, 'sec-fetch-site': 'cross-site' },
          press,
        )
      ).status,
      403,
    );
    assert.equal(
      (
        await call(
          port,
          'POST',
          '/setup/act',
          { ...pressHeaders, origin: 'http://127.0.0.1:1' },
          press,
        )
      ).status,
      403,
    );
    assert.equal(
      (await call(port, 'POST', '/setup/act', { ...pressHeaders, cookie: 'page=forged' }, press))
        .status,
      403,
    );
    const asked = await call(port, 'POST', '/setup/act', pressHeaders, press);
    assert.equal(asked.status, 200, asked.body);
    assert.match((JSON.parse(asked.body) as SetupState).confirm?.text ?? '', /OpenCode/);
    const other = new URL(page.link());
    const otherCookie =
      String(
        (await call(port, 'GET', `${other.pathname}${other.search}`)).headers['set-cookie'],
      ).split(';')[0] ?? '';
    const confirmedElsewhere = await call(
      port,
      'POST',
      '/setup/act',
      { ...pressHeaders, cookie: otherCookie },
      JSON.stringify({ action: 'download', arg: 'opencode', confirmed: true }),
    );
    assert.equal(confirmedElsewhere.status, 200);
    assert.equal(
      (JSON.parse(confirmedElsewhere.body) as SetupState).job,
      null,
      'a press confirmed from another session is a first press there',
    );
    assert.equal(runs.length, 0);
  });

  test('a shared game is stopped from the page only behind its session and origin', async (t) => {
    const shared: SharedNow[] = [
      { project_id: 'p-wifi', name: 'Tail', way: 'wifi', url: 'http://192.168.1.20:47970' },
    ];
    const stopped: string[] = [];
    const { view } = setup(t, { shared, stopped });
    const server = await startTestServer({ page: { ports: PORTS, mount: mountSetup(view) } });
    t.after(() => server.stop());
    const page = server.page;
    assert.ok(page);
    const port = page.port;
    const link = new URL(page.link());
    const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
    const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
    const headers = {
      cookie,
      origin: `http://127.0.0.1:${port}`,
      'sec-fetch-site': 'same-origin',
      'content-type': 'application/json',
    };
    const body = JSON.stringify({ project_id: 'p-wifi' });
    assert.equal(
      (await call(port, 'POST', '/setup/shares/stop', { ...headers, cookie: 'page=forged' }, body))
        .status,
      403,
    );
    assert.equal(
      (
        await call(
          port,
          'POST',
          '/setup/shares/stop',
          { ...headers, 'sec-fetch-site': 'cross-site' },
          body,
        )
      ).status,
      403,
    );
    assert.deepEqual(stopped, [], 'nothing stopped without the session and origin');
    const done = await call(port, 'POST', '/setup/shares/stop', headers, body);
    assert.equal(done.status, 200, done.body);
    assert.deepEqual((JSON.parse(done.body) as SetupState).shared, []);
    assert.deepEqual(stopped, ['p-wifi']);
    const shown = await call(port, 'GET', '/setup', { cookie });
    assert.match(shown.body, /<h2>Shared Now<\/h2>/);
    assert.ok(
      shown.body.indexOf('id="shared"') < shown.body.indexOf('id="steps"'),
      'a live share stands above the steps',
    );
  });

  test('a mounted view may add nothing outside /setup', async () => {
    await assert.rejects(
      startTestServer({
        page: { ports: PORTS, mount: (routes) => routes.get('/state-of-things', () => ({})) },
      }),
      /only paths under \/setup/,
    );
  });
});

describe('the download runner', () => {
  test('runs with no shell and a small environment, line by line', async (t) => {
    process.env.HALCYONIC_SETUP_PAGE_PROBE = 'kept from downloads';
    t.after(() => delete process.env.HALCYONIC_SETUP_PAGE_PROBE);
    const lines: string[] = [];
    const running = runCommand(
      [
        process.execPath,
        '-e',
        "console.log('one'); process.stderr.write('two\\r'); console.log(Object.keys(process.env).sort().join(','));",
      ],
      { cwd: tmpdir(), onLine: (line) => lines.push(line) },
    );
    assert.equal(await running.done, 0);
    assert.ok(lines.includes('one') && lines.includes('two'));
    const names = lines.find((line) => line.includes('PATH'))?.split(',') ?? [];
    // macOS adds its text encoding to every process it starts; nothing else is there.
    const allowed = ['HOME', 'LANG', 'PATH', 'TMPDIR', '__CF_USER_TEXT_ENCODING'];
    assert.ok(
      names.every((name) => allowed.includes(name)),
      names.join(),
    );
  });

  test('stop ends the command and its process group', async () => {
    const running = runCommand([process.execPath, '-e', 'setInterval(() => {}, 1000)'], {
      cwd: tmpdir(),
      onLine: () => undefined,
    });
    await new Promise((resolve) => setTimeout(resolve, 100));
    running.stop();
    assert.notEqual(await running.done, 0);
  });
});
