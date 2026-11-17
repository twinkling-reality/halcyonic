import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { StartExecutionRequest } from '@halcyonic/runtime-core';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { GIT_SIGN_IN_REFUSED } from './command-service.ts';
import { folderGitHoldsSignIn } from './folder-credentials.ts';

const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-folder-credentials-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
/** A dummy token, never a real one. */
const TOKEN = 'ghp_dummyHalcyonicTestToken0000000000';

function repo(name: string, config: string): string {
  const folder = join(ROOT, name);
  mkdirSync(join(folder, '.git'), { recursive: true });
  writeFileSync(join(folder, '.git', 'config'), config);
  return folder;
}

/** Each case on its own scratch repository, its value fake; nothing found is ever printed. */
test('a sign-in the copy would take out is found, wherever git keeps one (AUDIT3 M3, M3b, 2026-11-17)', async () => {
  const cases: [string, string][] = [
    ['userpass', `[remote "origin"]\n\turl = https://someone:${TOKEN}@github.com/o/r.git\n`],
    ['token-user', `[remote "o"]\n\tpushurl = https://${TOKEN}@host/r\n`],
    [
      'rewrite',
      `[url "https://x-access-token:${TOKEN}@github.com/"]\n\tinsteadOf = https://github.com/\n`,
    ],
    ['submodule', `[submodule "lib"]\n\turl = https://u:${TOKEN}@host/lib\n`],
    [
      'extraheader',
      `[http "https://github.com/"]\n\textraheader = AUTHORIZATION: basic ${TOKEN}\n`,
    ],
    ['inline-helper', `[credential]\n\thelper = "!f() { echo password=${TOKEN}; }; f"\n`],
    ['include', '[include]\n\tpath = ../secrets.gitconfig\n'],
    ['askpass', '[core]\n\taskPass = ./give-password.sh\n'],
    ['cookie', '[http]\n\tcookieFile = ./cookies.txt\n'],
    ['cookie-url', '[http "https://host/"]\n\tcookiefile = ./cookies.txt\n'],
    ['ssl-key', '[http]\n\tsslKey = ./client.key\n'],
    ['ssl-cert', '[http "https://host/"]\n\tsslCert = ./client.pem\n'],
  ];
  for (const [name, config] of cases)
    assert.equal(await folderGitHoldsSignIn(repo(name, config)), true, name);
});

test('a remote, a hook or an ssh address alone is no sign-in, nor is a folder without git', async () => {
  assert.equal(
    await folderGitHoldsSignIn(
      repo(
        'plain',
        '[remote "origin"]\n\turl = https://github.com/o/r.git\n[branch "main"]\n\tremote = origin\n',
      ),
    ),
    false,
  );
  assert.equal(
    await folderGitHoldsSignIn(
      repo(
        'ssh',
        '[remote "origin"]\n\turl = git@github.com:o/r.git\n\tpushurl = ssh://git@host/r\n[core]\n\thooksPath = /h\n',
      ),
    ),
    false,
  );
  const empty = join(ROOT, 'empty');
  mkdirSync(empty);
  assert.equal(await folderGitHoldsSignIn(empty), false);
});

test('settings git cannot read count as holding one', async () => {
  assert.equal(await folderGitHoldsSignIn(repo('broken', '[remote "origin"\n\turl = = =\n')), true);
});

test("a linked worktree's main settings and its own config.worktree are both read", async () => {
  const main = repo('main', '[remote "origin"]\n\turl = https://host/r\n');
  const gitDir = join(main, '.git', 'worktrees', 'side');
  mkdirSync(gitDir, { recursive: true });
  writeFileSync(join(gitDir, 'commondir'), '../..\n');
  const side = join(ROOT, 'side');
  mkdirSync(side);
  writeFileSync(join(side, '.git'), `gitdir: ${gitDir}\n`);
  assert.equal(await folderGitHoldsSignIn(side), false);
  writeFileSync(
    join(gitDir, 'config.worktree'),
    `[http]\n\textraHeader = Authorization: Bearer ${TOKEN}\n`,
  );
  assert.equal(await folderGitHoldsSignIn(side), true);
  rmSync(join(gitDir, 'config.worktree'));
  writeFileSync(
    join(main, '.git', 'config'),
    `[remote "origin"]\n\turl = https://u:${TOKEN}@host/r\n`,
  );
  assert.equal(await folderGitHoldsSignIn(side), true);
});

test('a task that would work in the folder itself is refused in words where its git settings hold a sign-in, nothing started (AUDIT3 M3)', async () => {
  const root = join(ROOT, 'connected');
  const app = join(root, 'app');
  mkdirSync(join(app, '.git'), { recursive: true });
  const config = join(app, '.git', 'config');
  writeFileSync(config, `[remote "origin"]\n\turl = https://someone:${TOKEN}@github.com/o/r.git\n`);
  const starts: StartExecutionRequest[] = [];
  const { controlPlane, commands, time } = createTestControlPlane({
    locations: createHostLocations([root]),
    adapters: (clock) => {
      const mock = new MockRuntimeAdapter({ scenarios: SCENARIOS, clock, scheduler: clock });
      // As an agent app that works in the project's folder does (Claude Code, Codex, OpenCode).
      (mock as { descriptor: typeof mock.descriptor }).descriptor = {
        ...mock.descriptor,
        uses_project_location: true,
      };
      const start = mock.startExecution.bind(mock);
      mock.startExecution = (request: StartExecutionRequest) => {
        starts.push(request);
        return start(request);
      };
      return [mock];
    },
  });
  const created = controlPlane.commands.submit(
    commands.createProject('Garden', { kind: 'existing_folder', root, folder_name: 'app' }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId, { title: 'Paint', objective: null } as never),
    'internal',
  );
  const workstreamId = (
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
  ) as WorkstreamId;
  const start = async () => {
    const sent = controlPlane.commands.submit(
      commands.startExecution(workstreamId, {
        title: 'Paint',
        objective: null,
        instruction: 'Paint the fence.',
        scenario: MockRuntimeAdapter.DEFAULT_SCENARIO,
      } as never),
      'internal',
    );
    // The check reads the folder's settings through a real git process: wait for it, then for the start.
    const id = sent.command?.command_id ?? '';
    for (let i = 0; i < 200 && controlPlane.projection.command(id)?.status === 'accepted'; i += 1) {
      await time.runUntilIdle();
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    await time.runUntilIdle();
    return controlPlane.projection.command(id);
  };
  const refused = await start();
  assert.deepEqual(
    { status: refused?.status, code: refused?.failure?.code, message: refused?.failure?.message },
    { status: 'failed', code: 'invalid_state', message: GIT_SIGN_IN_REFUSED },
  );
  assert.equal(starts.length, 0);
  const journal = JSON.stringify([...controlPlane.journal.readAll()]);
  assert.equal(journal.includes(TOKEN), false, 'the address is never recorded');
  assert.equal(
    [...controlPlane.journal.readAll()].some(
      ({ event }) => event.event_type === 'execution.created',
    ),
    false,
  );
  // Taken out of the address, the same task starts in the folder.
  writeFileSync(config, '[remote "origin"]\n\turl = https://github.com/o/r.git\n');
  assert.equal((await start())?.status, 'completed');
  assert.equal(starts.length, 1);
  assert.equal(starts[0]?.directory, app);
  await controlPlane.close();
});
