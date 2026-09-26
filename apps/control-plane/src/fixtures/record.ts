import type { ExecutionId } from '@halcyonic/contracts';
import { loadScenarios, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createCommandFactory, DEMO_PROJECT_NAME, DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createSeededRandom, createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { silentLogger } from '../logger.ts';
import { serializeTrace } from './trace.ts';

export const RECORDING_START = new Date('2026-09-26T09:00:00.000Z');
const APPROVAL_DELAY_MS = 2500;

/**
 * Records the demo plan into a trace by driving the real control plane and mock runtime under
 * virtual time with seeded identifiers. The same code always produces the same trace, which is
 * how tests detect a fixture that no longer matches the contracts or the pipeline.
 */
export async function recordDemoTrace(scenariosDir: string): Promise<string> {
  const time = createVirtualTime(RECORDING_START);
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(1),
  });
  const commandIds = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(2),
  });
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids });
  const controlPlane = new ControlPlane({
    journal,
    adapters: [
      new MockRuntimeAdapter({
        scenarios: loadScenarios(scenariosDir),
        clock: time,
        scheduler: time,
      }),
    ],
    ids,
    clock: time,
    scheduler: time,
    logger: silentLogger,
    commandTimeoutMs: 30_000,
  });
  const commands = createCommandFactory(commandIds, time, {
    name: 'halcyonic-fixture-recorder',
    version: null,
    device_label: null,
  });

  try {
    // A scripted operator approves every approval request after a pause, as a person would.
    controlPlane.publisher.subscribe(({ event }) => {
      if (event.event_type !== 'runtime.approval.requested') return;
      const executionId: ExecutionId = event.execution_id;
      const approvalId = event.payload.approval_id;
      void time.sleep(APPROVAL_DELAY_MS).then(() => {
        controlPlane.commands.submit(commands.approve(executionId, approvalId), 'internal');
      });
    });

    const project = controlPlane.commands.submit(
      commands.createProject(DEMO_PROJECT_NAME),
      'internal',
    );
    const projectResult = project.command?.result;
    if (projectResult?.kind !== 'project_created') throw new Error('demo project was not created');

    for (const workstream of DEMO_WORKSTREAMS) {
      await time.advance(250);
      const created = controlPlane.commands.submit(
        commands.createWorkstream(projectResult.project_id, workstream),
        'internal',
      );
      const result = created.command?.result;
      if (result?.kind !== 'workstream_created')
        throw new Error(`${workstream.title} was not created`);
      await time.advance(250);
      controlPlane.commands.submit(
        commands.startExecution(result.workstream_id, workstream),
        'internal',
      );
    }

    await time.runUntilIdle();
    return serializeTrace([...journal.readAll()].map((stored) => stored.event));
  } finally {
    await controlPlane.close();
  }
}
