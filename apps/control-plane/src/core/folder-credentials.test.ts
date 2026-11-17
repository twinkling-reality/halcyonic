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
import { ADDRESS_CREDENTIAL_REFUSED } from './command-service.ts';
import { folderHoldsAddressCredential } from './folder-credentials.ts';

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

test("a credential in a remote's address is found, in every place git keeps one (AUDIT3 M3, 2026-11-17)", () => {
  assert.equal(
    folderHoldsAddressCredential(
      repo('userpass', `[remote "origin"]\n\turl = https://someone:${TOKEN}@github.com/o/r.git\n`),
    ),
    true,
  );
  assert.equal(
    folderHoldsAddressCredential(
      repo('token-user', `[remote "o"]\n\tpushurl = https://${TOKEN}@host/r\n`),
    ),
    true,
  );
  assert.equal(
    folderHoldsAddressCredential(
      repo(
        'instead',
        `[url "https://x-access-token:${TOKEN}@github.com/"]\n\tinsteadOf = https://github.com/\n`,
      ),
    ),
    true,
  );
  assert.equal(
    folderHoldsAddressCredential(
      repo('upper', `[remote "o"]\n\turl = HTTPS://u:${TOKEN}@host/r\n`),
    ),
    true,
  );
});

test('an address without one, an ssh address, or no git folder at all is not a credential', () => {
  assert.equal(
    folderHoldsAddressCredential(
      repo('plain', '[remote "origin"]\n\turl = https://github.com/o/r.git\n'),
    ),
    false,
  );
  assert.equal(
    folderHoldsAddressCredential(
      repo(
        'ssh',
        '[remote "origin"]\n\turl = git@github.com:o/r.git\n\tpushurl = ssh://git@host/r\n',
      ),
    ),
    false,
  );
  assert.equal(folderHoldsAddressCredential(repo('none', '[core]\n\tbare = false\n')), false);
  const empty = join(ROOT, 'empty');
  mkdirSync(empty);
  assert.equal(folderHoldsAddressCredential(empty), false);
});

test('a .git file is followed to its git folder, and a linked worktree to its main settings', () => {
  const main = repo('main', `[remote "origin"]\n\turl = https://u:${TOKEN}@host/r\n`);
  const gitDir = join(main, '.git', 'worktrees', 'side');
  mkdirSync(gitDir, { recursive: true });
  writeFileSync(join(gitDir, 'commondir'), '../..\n');
  const side = join(ROOT, 'side');
  mkdirSync(side);
  writeFileSync(join(side, '.git'), `gitdir: ${gitDir}\n`);
  assert.equal(folderHoldsAddressCredential(side), true);
  const sub = join(ROOT, 'sub');
  mkdirSync(sub);
  const subGit = join(ROOT, 'modules-sub');
  mkdirSync(subGit);
  writeFileSync(join(subGit, 'config'), '[remote "origin"]\n\turl = https://host/r\n');
  writeFileSync(join(sub, '.git'), 'gitdir: ../modules-sub\n');
  assert.equal(folderHoldsAddressCredential(sub), false);
});

test('a task that would work in the folder itself is refused in words where its address holds a credential, nothing started (AUDIT3 M3)', async () => {
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
    await time.runUntilIdle();
    return controlPlane.projection.command(sent.command?.command_id ?? '');
  };
  const refused = await start();
  assert.deepEqual(
    { status: refused?.status, code: refused?.failure?.code, message: refused?.failure?.message },
    { status: 'failed', code: 'invalid_state', message: ADDRESS_CREDENTIAL_REFUSED },
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
