import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
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
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import {
  checkoutOf,
  EXIT_TIMEOUT_SECONDS,
  folderIdentity,
  foldersProblem,
  keptPath,
  LOGIN_AGENT_LABEL,
  type LoginAgent,
  type LoginIo,
  launchAgentsFolder,
  loginAgentFor,
  loginAgentPlist,
  main,
  nodeFor,
  parseLoginArgs,
  runLogin,
  THROTTLE_SECONDS,
  turnOff,
  turnOn,
} from './login-agent.ts';

const checkout = '/Users/someone/halcyonic';

function folder(t: TestContext): string {
  const made = mkdtempSync(join(tmpdir(), 'login-agent-'));
  t.after(() => rmSync(made, { recursive: true, force: true }));
  return made;
}

/** An agent whose data and tasks folders are the test's own, never the person's. */
function agent(
  t: TestContext,
  env: NodeJS.ProcessEnv = { PATH: '/opt/homebrew/bin:/usr/bin:/bin' },
): LoginAgent {
  const own = folder(t);
  return loginAgentFor({
    checkout,
    node: '/opt/homebrew/bin/node',
    env,
    settings: { HALCYONIC_DATA_DIR: join(own, 'data'), HALCYONIC_TASKS_DIR: join(own, 'tasks') },
  });
}

/** The property list as plutil reads it, where this Mac has plutil. */
function read(t: TestContext, plist: string): Record<string, unknown> | null {
  if (!existsSync('/usr/bin/plutil')) return null;
  const file = join(folder(t), 'agent.plist');
  writeFileSync(file, plist);
  execFileSync('/usr/bin/plutil', ['-lint', file]);
  return JSON.parse(
    execFileSync('/usr/bin/plutil', ['-convert', 'json', '-o', '-', file], { encoding: 'utf8' }),
  );
}

/**
 * A Mac where every launchctl call is recorded: print finds the job only where `loaded`;
 * bootstrap exits with `bootstrap`; bootout finds a job only where `loaded`. The plane answers
 * already where `running`, else once `startsAfter` health checks have been asked.
 */
function mac(
  options: { bootstrap?: number; loaded?: boolean; running?: boolean; startsAfter?: number } = {},
) {
  const calls: string[][] = [];
  const lines: string[] = [];
  let asked = 0;
  const io: LoginIo = {
    print: (line) => lines.push(line),
    launchctl: (args) => {
      calls.push([...args]);
      if (args[0] === 'bootstrap') {
        const status = options.bootstrap ?? 0;
        return { status, output: status === 0 ? '' : 'Input/output error' };
      }
      return options.loaded === true
        ? { status: 0, output: '' }
        : { status: 113, output: 'Could not find service' };
    },
    answers: async () => {
      asked += 1;
      if (options.running === true) return true;
      return options.startsAfter !== undefined && asked > options.startsAfter;
    },
    uid: 501,
    sleep: async () => {},
  };
  return { io, calls, lines };
}

describe('the login agent', () => {
  test('starts the plane restartable at load, again after any failed exit, throttled, with time to stop', (t) => {
    const made = agent(t);
    assert.equal(made.label, LOGIN_AGENT_LABEL);
    assert.equal(made.entry, join(checkout, 'apps/control-plane/src/main.ts'));
    const plist = read(t, loginAgentPlist(made));
    if (plist === null) return t.skip('no plutil on this machine');
    assert.deepEqual(plist.ProgramArguments, [
      '/opt/homebrew/bin/node',
      join(checkout, 'apps/control-plane/src/main.ts'),
      '--restartable',
    ]);
    assert.equal(plist.WorkingDirectory, checkout);
    assert.equal(plist.RunAtLoad, true);
    assert.deepEqual(plist.KeepAlive, { SuccessfulExit: false });
    assert.equal(plist.ThrottleInterval, THROTTLE_SECONDS);
    assert.equal(plist.ExitTimeOut, EXIT_TIMEOUT_SECONDS);
    assert.equal(plist.StandardOutPath, made.logFile);
    assert.equal(plist.StandardErrorPath, made.logFile);
    assert.deepEqual(plist.EnvironmentVariables, {
      PATH: '/opt/homebrew/bin:/usr/bin:/bin',
      HALCYONIC_DATA_DIR: made.dataDir,
      HALCYONIC_TASKS_DIR: made.environment.HALCYONIC_TASKS_DIR,
    });
  });

  test('carries a scratch port and data folder, and logs into that folder', () => {
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home: '/Users/someone',
      label: 'com.halcyonic.control-plane.scratch',
      settings: { HALCYONIC_PORT: '47841', HALCYONIC_DATA_DIR: '/tmp/scratch-data' },
    });
    assert.deepEqual(made.environment, {
      HALCYONIC_PORT: '47841',
      HALCYONIC_DATA_DIR: '/tmp/scratch-data',
      HALCYONIC_TASKS_DIR: '/Users/someone/.halcyonic-tasks',
    });
    assert.equal(made.dataDir, '/tmp/scratch-data');
    assert.equal(made.logFile, '/tmp/scratch-data/control-plane.log');
  });

  test("keeps PATH's absolute folders, none inside the checkout or a package's node_modules/.bin", () => {
    assert.equal(
      keptPath(
        `.:node_modules/.bin::${checkout}/node_modules/.bin:/elsewhere/node_modules/.bin:${checkout}/tools:/usr/bin`,
        checkout,
      ),
      '/usr/bin',
    );
    assert.equal(keptPath('relative:', checkout), undefined);
    assert.equal(
      keptPath('/Users/someone/halcyonic-other/bin', checkout),
      '/Users/someone/halcyonic-other/bin',
    );
  });

  test('carries no other setting and nothing else of the environment, and names the settings it leaves out', (t) => {
    const made = agent(t, {
      PATH: '/usr/bin',
      HALCYONIC_PROJECT_ROOTS: '/x',
      HALCYONIC_HOST: '',
      ANTHROPIC_API_KEY: 'y',
    });
    assert.deepEqual(Object.keys(made.environment).sort(), [
      'HALCYONIC_DATA_DIR',
      'HALCYONIC_TASKS_DIR',
      'PATH',
    ]);
    assert.deepEqual(made.leftOut, ['HALCYONIC_PROJECT_ROOTS']);
    assert.throws(
      () => loginAgentPlist({ ...made, environment: { HALCYONIC_ACCESS_TOKEN: 'x' } }),
      /not a setting/,
    );
  });

  test('escapes what XML would misread, and refuses a relative path or a label launchd could misread', (t) => {
    const made = loginAgentFor({
      checkout: '/Users/a&b/<halcyonic>',
      node: '/usr/local/bin/node',
      env: {},
    });
    const plist = read(t, loginAgentPlist(made));
    if (plist !== null) assert.equal(plist.WorkingDirectory, '/Users/a&b/<halcyonic>');
    assert.throws(() => loginAgentPlist({ ...made, node: 'node' }), /not an absolute path/);
    assert.throws(() => loginAgentPlist({ ...made, label: 'halcyonic' }), /not a launchd label/);
    assert.throws(
      () => loginAgentPlist({ ...made, label: '../evil.plist' }),
      /not a launchd label/,
    );
  });

  test('runs the node on PATH that is this very node, so the stable link is kept, else this node', () => {
    const own = '/opt/homebrew/Cellar/node@24/24.15.0/bin/node';
    const sameFile = (found: string) => found === '/opt/homebrew/bin/node';
    assert.equal(
      nodeFor('relative:/usr/local/bin:/opt/homebrew/bin', own, sameFile),
      '/opt/homebrew/bin/node',
    );
    assert.equal(nodeFor('/usr/local/bin:/usr/bin', own, sameFile), own);
    assert.equal(nodeFor(undefined, own, sameFile), own);
  });

  test('takes every flag strictly: known, given once, with a value', () => {
    assert.deepEqual(
      parseLoginArgs(['on', 'x', '--port', '47841'])?.flags,
      new Map([['--port', '47841']]),
    );
    assert.equal(parseLoginArgs(['on', 'x', '--port', '--data-dir', 'y']), null);
    assert.equal(parseLoginArgs(['on', 'x', '--prot', '1']), null);
    assert.equal(parseLoginArgs(['on', 'x', '--port']), null);
    assert.equal(parseLoginArgs(['on', 'x', '--port', '1', '--port', '2']), null);
    assert.equal(parseLoginArgs(['write', 'x']), null);
    assert.equal(parseLoginArgs(['on', '--port', '1']), null);
  });

  test('refuses a port that is not one, port 0 and bad words, before anything is written or started', async (t) => {
    const into = folder(t);
    const data = join(folder(t), 'data');
    const { io, calls } = mac();
    await assert.rejects(
      main(
        ['on', into, '--port', 'abc', '--data-dir', data, '--tasks-dir', join(data, '..', 'tasks')],
        {},
        io,
      ),
      /HALCYONIC_PORT/,
    );
    assert.equal(
      await main(
        ['on', into, '--port', '0', '--data-dir', data, '--tasks-dir', join(data, '..', 'tasks')],
        {},
        io,
      ),
      2,
    );
    assert.equal(await main(['on'], {}, io), 2);
    assert.deepEqual(calls, []);
    assert.equal(existsSync(join(into, `${LOGIN_AGENT_LABEL}.plist`)), false);
    assert.equal(existsSync(data), false);
  });

  test('refuses a data folder of its own without a tasks folder of its own, before anything is written', async (t) => {
    const into = folder(t);
    const data = join(folder(t), 'data');
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      settings: { HALCYONIC_DATA_DIR: data },
    });
    const { io, calls, lines } = mac({ startsAfter: 1 });
    assert.equal(await turnOn(made, 47841, into, io), 2);
    assert.match(lines.join('\n'), /needs its own tasks folder.*would clear the task copies in/);
    assert.deepEqual(calls, []);
    assert.equal(existsSync(data), false);
    assert.equal(existsSync(join(into, `${LOGIN_AGENT_LABEL}.plist`)), false);
  });

  test("judges the folders by the account's own home, whatever HOME says, and by which tasks folder, not whether one is set", () => {
    const home = '/Users/someone';
    const made = (settings: { HALCYONIC_DATA_DIR?: string; HALCYONIC_TASKS_DIR?: string }) =>
      loginAgentFor({
        checkout,
        node: '/opt/homebrew/bin/node',
        env: { HOME: '/tmp/s' },
        settings,
        home,
      });
    assert.equal(foldersProblem(made({})), null, 'the usual folders, together');
    assert.equal(foldersProblem(made({ HALCYONIC_DATA_DIR: '/Users/someone/.halcyonic' })), null);
    assert.match(
      foldersProblem(made({ HALCYONIC_DATA_DIR: '/tmp/s/.halcyonic' })) ?? '',
      /would clear the task copies in \/Users\/someone\/\.halcyonic-tasks/,
    );
    assert.match(
      foldersProblem(
        made({
          HALCYONIC_DATA_DIR: '/tmp/s/data',
          HALCYONIC_TASKS_DIR: '/Users/someone/.halcyonic-tasks',
        }),
      ) ?? '',
      /needs its own tasks folder/,
    );
    assert.match(
      foldersProblem(
        made({ HALCYONIC_DATA_DIR: '/tmp/s/data', HALCYONIC_TASKS_DIR: '/tmp/s/data/tasks' }),
      ) ?? '',
      /beside the data folder/,
    );
    assert.equal(
      foldersProblem(
        made({ HALCYONIC_DATA_DIR: '/tmp/s/data', HALCYONIC_TASKS_DIR: '/tmp/s/tasks' }),
      ),
      null,
    );
  });

  test('takes a scratch tasks folder only beside a scratch data folder on its own words, never from the shell', async (t) => {
    const into = folder(t);
    const own = folder(t);
    const shell = { HALCYONIC_TASKS_DIR: '/Users/someone/.halcyonic-tasks' };
    const refused = mac({ startsAfter: 1 });
    assert.equal(
      await main(
        ['on', into, '--port', '47841', '--data-dir', join(own, 'data')],
        shell,
        refused.io,
      ),
      2,
    );
    assert.match(refused.lines.join('\n'), /--data-dir and --tasks-dir go together/);
    assert.deepEqual(refused.calls, []);
    const taken = mac({ startsAfter: 1 });
    const args = [
      'on',
      into,
      '--port',
      '47841',
      '--data-dir',
      join(own, 'data'),
      '--tasks-dir',
      join(own, 'tasks'),
    ];
    assert.equal(await main(args, shell, taken.io), 0);
    const plist = readFileSync(join(into, `${LOGIN_AGENT_LABEL}.plist`), 'utf8');
    assert.match(
      plist,
      new RegExp(`<key>HALCYONIC_TASKS_DIR</key>\\n\\t\\t<string>${join(own, 'tasks')}</string>`),
    );
  });

  test('knows the usual tasks folder by a link to it or another case of its name, and refuses either', (t) => {
    const home = folder(t);
    mkdirSync(join(home, '.halcyonic-tasks'));
    const link = join(folder(t), 'tasks-link');
    symlinkSync(join(home, '.halcyonic-tasks'), link);
    const made = (tasks: string) =>
      loginAgentFor({
        checkout,
        node: '/opt/homebrew/bin/node',
        env: {},
        home,
        settings: { HALCYONIC_DATA_DIR: join(folder(t), 'data'), HALCYONIC_TASKS_DIR: tasks },
      });
    assert.equal(
      (folderIdentity(link) as { folded: string }).folded,
      (folderIdentity(join(home, '.halcyonic-tasks')) as { folded: string }).folded,
    );
    assert.match(foldersProblem(made(link)) ?? '', /needs its own tasks folder/);
    assert.match(
      foldersProblem(made(join(home, '.HALCYONIC-tasks'))) ?? '',
      /needs its own tasks folder/,
    );
    assert.match(
      foldersProblem(made(join(home, 'x', '..', '.halcyonic-tasks'))) ?? '',
      /needs its own tasks folder/,
    );
    assert.equal(foldersProblem(made(join(folder(t), 'tasks'))), null);
  });

  test('knows the usual tasks folder before it exists: by a link to it, and by a path through a link to the home', (t) => {
    const home = folder(t);
    const usual = join(home, '.halcyonic-tasks');
    const elsewhere = folder(t);
    const dangling = join(elsewhere, 'tasks-link');
    symlinkSync(usual, dangling);
    const homeLink = join(elsewhere, 'home-link');
    symlinkSync(home, homeLink);
    const made = (tasks: string) =>
      loginAgentFor({
        checkout,
        node: '/opt/homebrew/bin/node',
        env: {},
        home,
        settings: { HALCYONIC_DATA_DIR: join(elsewhere, 'data'), HALCYONIC_TASKS_DIR: tasks },
      });
    assert.equal(
      existsSync(usual),
      false,
      'a fresh account: the usual tasks folder is not there yet',
    );
    assert.equal(folderIdentity(dangling), 'dangling');
    assert.equal(
      (folderIdentity(join(homeLink, '.halcyonic-tasks')) as { folded: string }).folded,
      (folderIdentity(usual) as { folded: string }).folded,
    );
    assert.match(foldersProblem(made(dangling)) ?? '', /a link to a folder that does not exist/);
    assert.match(
      foldersProblem(made(join(homeLink, '.halcyonic-tasks'))) ?? '',
      /needs its own tasks folder/,
    );
    assert.equal(foldersProblem(made(join(homeLink, 'scratch-tasks'))), null);
  });

  test("refuses a link whose relative target climbs out of the link's real folder to the usual tasks folder", (t) => {
    const home = folder(t);
    mkdirSync(join(home, 'a'));
    symlinkSync('../.halcyonic-tasks', join(home, 'a', 'dangling'));
    const scratch = folder(t);
    symlinkSync(join(home, 'a'), join(scratch, 'sub'));
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home,
      settings: {
        HALCYONIC_DATA_DIR: join(scratch, 'data'),
        HALCYONIC_TASKS_DIR: join(scratch, 'sub', 'dangling'),
      },
    });
    assert.match(foldersProblem(made) ?? '', /a link to a folder that does not exist/);
    mkdirSync(join(home, '.halcyonic-tasks'));
    assert.match(
      foldersProblem(made) ?? '',
      /needs its own tasks folder/,
      'once it exists, the kernel says where it leads',
    );
  });

  test("knows the usual tasks folder through the data volume's firmlink, as the kernel does", (t) => {
    const home = folder(t);
    mkdirSync(join(home, '.halcyonic-tasks'));
    const through = join('/System/Volumes/Data', realpathSync(home), '.halcyonic-tasks');
    if (!existsSync(through)) return t.skip('no data volume firmlink on this machine');
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home,
      settings: { HALCYONIC_DATA_DIR: join(folder(t), 'data'), HALCYONIC_TASKS_DIR: through },
    });
    assert.match(foldersProblem(made) ?? '', /needs its own tasks folder/);
  });

  test('takes the data folder as the usual one only when the file system says so, and refuses a folder inside the usual tasks', (t) => {
    const home = folder(t);
    mkdirSync(join(home, '.halcyonic'));
    mkdirSync(join(home, '.halcyonic-tasks'));
    const made = (settings: { HALCYONIC_DATA_DIR?: string; HALCYONIC_TASKS_DIR?: string }) =>
      loginAgentFor({ checkout, node: '/opt/homebrew/bin/node', env: {}, home, settings });
    assert.equal(foldersProblem(made({ HALCYONIC_DATA_DIR: join(home, '.halcyonic') })), null);
    assert.match(
      foldersProblem(
        made({
          HALCYONIC_DATA_DIR: join(home, 'other'),
          HALCYONIC_TASKS_DIR: join(home, '.halcyonic-tasks', 'p'),
        }),
      ) ?? '',
      /needs its own tasks folder/,
    );
  });

  test('always writes both folders as judged, even the usual ones, so nothing launchd holds can name others', () => {
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home: '/Users/someone',
    });
    assert.equal(made.environment.HALCYONIC_DATA_DIR, '/Users/someone/.halcyonic');
    assert.equal(made.environment.HALCYONIC_TASKS_DIR, '/Users/someone/.halcyonic-tasks');
    assert.match(
      loginAgentPlist(made),
      /<key>HALCYONIC_DATA_DIR<\/key>\n\t\t<string>\/Users\/someone\/\.halcyonic<\/string>/,
    );
  });

  test('takes another case of the usual data folder for another folder before it exists, so it is refused with the usual tasks', (t) => {
    const home = folder(t);
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home,
      settings: { HALCYONIC_DATA_DIR: join(home, '.HALCYONIC') },
    });
    assert.equal(existsSync(join(home, '.halcyonic')), false);
    assert.match(foldersProblem(made) ?? '', /needs its own tasks folder/);
  });

  test('refuses a tasks folder inside the usual one through the firmlink, and data and tasks differing only in case', (t) => {
    const home = folder(t);
    mkdirSync(join(home, '.halcyonic-tasks', 'P1'), { recursive: true });
    const firm = join('/System/Volumes/Data', realpathSync(home), '.halcyonic-tasks');
    const made = (settings: { HALCYONIC_DATA_DIR?: string; HALCYONIC_TASKS_DIR?: string }) =>
      loginAgentFor({ checkout, node: '/opt/homebrew/bin/node', env: {}, home, settings });
    const scratch = folder(t);
    assert.match(
      foldersProblem(
        made({ HALCYONIC_DATA_DIR: join(scratch, 'DD'), HALCYONIC_TASKS_DIR: join(scratch, 'dd') }),
      ) ?? '',
      /beside the data folder/,
    );
    if (!existsSync(firm)) return t.skip('no data volume firmlink on this machine');
    for (const child of ['P1', 'P2']) {
      assert.match(
        foldersProblem(
          made({
            HALCYONIC_DATA_DIR: join(scratch, 'data'),
            HALCYONIC_TASKS_DIR: join(firm, child),
          }),
        ) ?? '',
        /needs its own tasks folder/,
        child,
      );
    }
  });

  test('writes its folders absolute, as it judged them, since the plane would resolve a relative one against the checkout', () => {
    const made = loginAgentFor({
      checkout,
      node: '/opt/homebrew/bin/node',
      env: {},
      home: '/Users/someone',
      settings: {
        HALCYONIC_DATA_DIR: '.halcyonic',
        HALCYONIC_TASKS_DIR: 'scratch/tasks',
        HALCYONIC_PORT: '47841',
      },
    });
    assert.equal(made.environment.HALCYONIC_DATA_DIR, join(process.cwd(), '.halcyonic'));
    assert.equal(made.environment.HALCYONIC_TASKS_DIR, join(process.cwd(), 'scratch', 'tasks'));
    assert.equal(made.environment.HALCYONIC_PORT, '47841');
    assert.equal(made.dataDir, made.environment.HALCYONIC_DATA_DIR);
  });

  test('pnpm mac-setup login on: refuses a data folder of its own alone, carries both when both are set', async (t) => {
    const home = folder(t);
    const own = folder(t);
    const alone = mac({ startsAfter: 1 });
    assert.equal(
      await runLogin('on', { HALCYONIC_DATA_DIR: join(own, 'data') }, home, alone.io),
      2,
    );
    assert.deepEqual(alone.calls, []);
    const both = mac({ startsAfter: 1 });
    const env = { HALCYONIC_DATA_DIR: join(own, 'data'), HALCYONIC_TASKS_DIR: join(own, 'tasks') };
    assert.equal(await runLogin('on', env, home, both.io), 0);
    const plist = readFileSync(
      join(home, 'Library', 'LaunchAgents', `${LOGIN_AGENT_LABEL}.plist`),
      'utf8',
    );
    assert.ok(plist.includes(`<string>${join(own, 'tasks')}</string>`));
  });

  test('turns on: refuses while launchd has it already, and while a Halcyonic started by hand answers', async (t) => {
    const into = folder(t);
    const loaded = mac({ loaded: true });
    assert.equal(await turnOn(agent(t), 47841, into, loaded.io), 1);
    assert.deepEqual(loaded.calls, [['print', `gui/501/${LOGIN_AGENT_LABEL}`]]);
    assert.match(loaded.lines.join('\n'), /already starts when you log in.*turn it off, then on/);
    const byHand = mac({ running: true });
    assert.equal(await turnOn(agent(t), 47841, into, byHand.io), 1);
    assert.match(byHand.lines.join('\n'), /you started yourself answers on port 47841/);
    assert.equal(existsSync(join(into, `${LOGIN_AGENT_LABEL}.plist`)), false);
  });

  test('turns on: writes the file, starts it in the person session, waits for health, names what it leaves out', async (t) => {
    const into = folder(t);
    const made = agent(t, { PATH: '/usr/bin', HALCYONIC_PROJECT_ROOTS: '/x' });
    const { io, calls, lines } = mac({ startsAfter: 3 });
    assert.equal(await turnOn(made, 47841, into, io), 0);
    const file = join(into, `${LOGIN_AGENT_LABEL}.plist`);
    assert.equal(readFileSync(file, 'utf8'), loginAgentPlist(made));
    assert.equal(statSync(file).mode & 0o777, 0o644);
    assert.ok(
      existsSync(made.dataDir),
      'the data folder is made before launchd opens the log there',
    );
    assert.deepEqual(calls, [
      ['print', `gui/501/${LOGIN_AGENT_LABEL}`],
      ['bootstrap', 'gui/501', file],
    ]);
    assert.match(lines.join('\n'), /not carried by the agent: HALCYONIC_PROJECT_ROOTS\./);
    assert.match(lines.at(-1) ?? '', /running on port 47841/);
  });

  test('turns on: when launchd refuses, says what it answered and leaves no file to start at login', async (t) => {
    const into = folder(t);
    const refused = mac({ bootstrap: 5 });
    assert.equal(await turnOn(agent(t), 47841, into, refused.io), 1);
    assert.match(
      refused.lines.join('\n'),
      /launchctl exit 5\): Input\/output error\. Its file is removed/,
    );
    assert.equal(existsSync(join(into, `${LOGIN_AGENT_LABEL}.plist`)), false);
  });

  test('turns on and off around a link at the file place without writing through it', async (t) => {
    const into = folder(t);
    const elsewhere = join(folder(t), 'other.plist');
    writeFileSync(elsewhere, 'left alone');
    symlinkSync(elsewhere, join(into, `${LOGIN_AGENT_LABEL}.plist`));
    const off = mac();
    assert.equal(turnOff(LOGIN_AGENT_LABEL, into, off.io), 0);
    assert.match(off.lines.join('\n'), /is a link, not the agent's file; it was left alone/);
    assert.equal(await turnOn(agent(t), 47841, into, mac({ startsAfter: 1 }).io), 0);
    assert.equal(readFileSync(elsewhere, 'utf8'), 'left alone');
  });

  test('turns off: launchd stops it and the file goes; with nothing set up, it says so', (t) => {
    const into = folder(t);
    const file = join(into, `${LOGIN_AGENT_LABEL}.plist`);
    writeFileSync(file, loginAgentPlist(agent(t)));
    const loaded = mac({ loaded: true });
    assert.equal(turnOff(LOGIN_AGENT_LABEL, into, loaded.io), 0);
    assert.deepEqual(loaded.calls, [['bootout', `gui/501/${LOGIN_AGENT_LABEL}`]]);
    assert.equal(existsSync(file), false);
    assert.match(loaded.lines[0] ?? '', /stopped and no longer starts/);
    const none = mac();
    assert.equal(turnOff(LOGIN_AGENT_LABEL, into, none.io), 0);
    assert.match(none.lines[0] ?? '', /was not set to start/);
  });

  test("lives in the person's own LaunchAgents folder, and this file's checkout is the repository's", () => {
    assert.equal(launchAgentsFolder('/Users/someone'), '/Users/someone/Library/LaunchAgents');
    assert.ok(existsSync(join(checkoutOf(), 'apps', 'control-plane', 'src', 'main.ts')));
    assert.ok(
      readFileSync(join(checkoutOf(), 'package.json'), 'utf8').includes('"name": "halcyonic"'),
    );
  });
});
