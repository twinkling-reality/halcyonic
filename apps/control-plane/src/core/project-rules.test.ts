import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { StartExecutionRequest } from '@halcyonic/runtime-core';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { ProjectReads } from './project-reads.ts';
import { readRules, rulesReview, withRules } from './project-rules.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-rules-')));
after(() => rmSync(base, { recursive: true, force: true }));
const sha = (text: string) => createHash('sha256').update(text).digest('hex');
const RULES = 'Use tabs.\nRun the tests before you finish.\n';

function folder(name: string) {
  const dir = join(base, name);
  mkdirSync(dir, { recursive: true });
  return dir;
}

describe("a project's own rules (E5)", () => {
  test('the AGENTS.md at the top of the folder, else its CLAUDE.md, read whole with its digest', () => {
    const both = folder('both');
    writeFileSync(join(both, 'AGENTS.md'), RULES);
    writeFileSync(join(both, 'CLAUDE.md'), 'Not these.\n');
    assert.deepEqual(readRules(both), {
      state: 'ok',
      file: 'AGENTS.md',
      text: RULES,
      sha256: sha(RULES),
      size: Buffer.byteLength(RULES),
    });
    const claude = folder('claude');
    writeFileSync(join(claude, 'CLAUDE.md'), RULES);
    assert.equal((readRules(claude) as { file: string }).file, 'CLAUDE.md');
    assert.deepEqual(readRules(folder('none')), { state: 'none' });
  });

  test('one that cannot be passed on stops there: never a CLAUDE.md in place of an AGENTS.md', () => {
    const large = folder('large');
    writeFileSync(join(large, 'AGENTS.md'), 'x'.repeat(32_769));
    writeFileSync(join(large, 'CLAUDE.md'), RULES);
    assert.deepEqual(readRules(large), { state: 'too_large', file: 'AGENTS.md', size: 32_769 });
    const binary = folder('binary');
    writeFileSync(join(binary, 'AGENTS.md'), Buffer.from([0x61, 0, 0x62]));
    assert.equal(readRules(binary).state, 'unreadable');
    const invalid = folder('invalid');
    writeFileSync(join(invalid, 'AGENTS.md'), Buffer.from([0xff, 0xfe, 0x41]));
    assert.equal(readRules(invalid).state, 'unreadable');
    // Never through a link.
    const linked = folder('linked');
    const outside = folder('outside');
    writeFileSync(join(outside, 'AGENTS.md'), 'Somewhere else.\n');
    symlinkSync(join(outside, 'AGENTS.md'), join(linked, 'AGENTS.md'));
    assert.equal(readRules(linked).state, 'unreadable');
    const dir = folder('dir');
    mkdirSync(join(dir, 'AGENTS.md'));
    assert.equal(readRules(dir).state, 'unreadable');
  });

  test("the rules go before the person's words, marked as the project's own", () => {
    const sent = withRules('Fix the build.', 'AGENTS.md', `${RULES}\n\n`);
    assert.equal(
      sent,
      [
        `These are the project's own rules, from its AGENTS.md. The person read them and chose to pass them on; they are the project's words, not the person's. They begin after this line and end at "End of the project's rules."`,
        '',
        'Use tabs.\nRun the tests before you finish.',
        '',
        "End of the project's rules.",
        '',
        'Fix the build.',
      ].join('\n'),
    );
    assert.notEqual(
      rulesReview('p', 'AGENTS.md', sha(RULES)),
      rulesReview('p', 'CLAUDE.md', sha(RULES)),
    );
  });
});

describe("a project's own rules through the control plane (E5)", () => {
  function plane(name: string) {
    const root = folder(name);
    const app = join(root, 'app');
    mkdirSync(app);
    writeFileSync(join(app, 'AGENTS.md'), RULES);
    const starts: StartExecutionRequest[] = [];
    const { controlPlane, commands, time } = createTestControlPlane({
      locations: createHostLocations([root]),
      adapters: (clock) => {
        const mock = new MockRuntimeAdapter({ scenarios: SCENARIOS, clock, scheduler: clock });
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
    const set = (on: boolean, review?: string) => {
      const command = commands.setLocation(projectId, {
        kind: 'existing_folder',
        root,
        folder_name: 'app',
      });
      return controlPlane.commands.submit(
        {
          ...command,
          command_type: 'project.rules.set',
          payload: { project_id: projectId, on, ...(review !== undefined && { review }) },
        } as never,
        'internal',
      );
    };
    const start = () =>
      controlPlane.commands.submit(
        commands.startExecution(workstreamId, {
          title: 'Paint',
          objective: null,
          instruction: 'Paint the fence.',
          scenario: MockRuntimeAdapter.DEFAULT_SCENARIO,
        } as never),
        'internal',
      );
    const events = (type: string) =>
      [...controlPlane.journal.readAll()].map((s) => s.event).filter((e) => e.event_type === type);
    const reads = new ProjectReads(controlPlane);
    const read = () =>
      reads.rules(
        projectId,
        { journal_id: controlPlane.journal.info.journal_id },
        { kind: 'local' },
      );
    return { controlPlane, time, app, projectId, starts, set, start, events, read };
  }

  test("read first, chosen with its review, then put before each task's first instruction, journaled by digest only", async (t) => {
    const { controlPlane, time, app, projectId, starts, set, start, events, read } = plane('flow');
    t.after(() => controlPlane.close());
    const shown = read();
    assert.equal(shown?.state, 'off');
    assert.equal(shown?.text, RULES);
    assert.equal(shown?.file, 'AGENTS.md');
    // Never without the review of what the person read, and never with a stale one.
    assert.equal(set(true).command?.rejection?.code, 'review_required');
    assert.equal(set(true, 'f'.repeat(64)).command?.rejection?.code, 'review_stale');
    assert.equal(set(true, shown?.review ?? undefined).disposition, 'accepted');
    await time.runUntilIdle();
    assert.deepEqual(controlPlane.projection.project(projectId)?.rules, {
      file: 'AGENTS.md',
      sha256: sha(RULES),
    });
    assert.equal(read()?.state, 'on');

    start();
    await time.runUntilIdle();
    assert.equal(starts.length, 1);
    assert.equal(starts[0]?.instruction, withRules('Paint the fence.', 'AGENTS.md', RULES));
    assert.deepEqual(
      events('execution.rules_given').map((e) => e.payload),
      [{ file: 'AGENTS.md', sha256: sha(RULES) }],
    );
    // The person's own words are what the journal keeps; the rules only by digest.
    assert.equal(JSON.stringify([...controlPlane.journal.readAll()]).includes('Use tabs.'), false);

    // A changed file is off until read again: the next task starts without it.
    writeFileSync(join(app, 'AGENTS.md'), 'Use spaces.\n');
    assert.equal(read()?.state, 'changed');
    start();
    await time.runUntilIdle();
    assert.equal(starts.at(-1)?.instruction, 'Paint the fence.');
    assert.equal(controlPlane.projection.project(projectId)?.rules, undefined);
    assert.deepEqual(events('project.rules.updated').at(-1)?.payload, {
      on: false,
      file: 'AGENTS.md',
      sha256: sha('Use spaces.\n'),
      changed: true,
    });
    assert.equal(read()?.state, 'off');
    // Off needs no review.
    assert.equal(set(false).disposition, 'accepted');
  });

  test('a project without a rules file has none to turn on', async (t) => {
    const { controlPlane, app, set, read } = plane('missing');
    t.after(() => controlPlane.close());
    rmSync(join(app, 'AGENTS.md'));
    assert.equal(read()?.state, 'none');
    assert.equal(read()?.review, null);
    const refused = set(true, 'a'.repeat(64));
    assert.equal(refused.command?.rejection?.code, 'invalid_state');
    assert.match(refused.command?.rejection?.message ?? '', /no AGENTS.md or CLAUDE.md/);
  });
});
