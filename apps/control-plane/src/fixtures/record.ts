import type { ExecutionId } from '@halcyonic/contracts';
import { loadScenarios, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createCommandFactory, type OperatorScript, type TracePlan } from '../demo-plan.ts';
import { createSeededRandom, createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { silentLogger } from '../logger.ts';
import { serializeTrace } from './trace.ts';

export const RECORDING_START = new Date('2026-09-26T09:00:00.000Z');
const APPROVAL_DELAY_MS = 2500;
const DENIAL_REASON = 'Not now: the reporting job still reads that table.';
const MID_TURN_INSTRUCTION = 'Keep the old session API working until the callers move.';

/**
 * Records a trace plan by driving the real control plane and mock runtime under virtual time with
 * seeded identifiers. The same code always produces the same trace, which is how tests detect a
 * fixture that no longer matches the contracts or the pipeline.
 */
export async function recordTrace(plan: TracePlan, scenariosDir: string): Promise<string> {
  const time = createVirtualTime(RECORDING_START);
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(plan.seed),
  });
  const commandIds = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(plan.seed + 1),
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
  const operators = new Map<string, OperatorScript>();
  const later = (delayMs: number, act: () => void) => {
    void time.sleep(delayMs).then(act);
  };

  try {
    // A scripted operator answers approval requests after a pause, as a person would.
    controlPlane.publisher.subscribe(({ event }) => {
      if (event.event_type !== 'runtime.approval.requested') return;
      const executionId: ExecutionId = event.execution_id;
      const approvalId = event.payload.approval_id;
      const denies = operators.get(event.workstream_id)?.approval === 'deny';
      later(APPROVAL_DELAY_MS, () => {
        const answer = denies
          ? commands.deny(executionId, approvalId, DENIAL_REASON)
          : commands.approve(executionId, approvalId);
        controlPlane.commands.submit(answer, 'internal');
      });
    });

    const project = controlPlane.commands.submit(
      commands.createProject(plan.projectName),
      'internal',
    );
    const projectResult = project.command?.result;
    if (projectResult?.kind !== 'project_created') throw new Error('the project was not created');

    for (const workstream of plan.workstreams) {
      await time.advance(250);
      const created = controlPlane.commands.submit(
        commands.createWorkstream(projectResult.project_id, workstream),
        'internal',
      );
      const result = created.command?.result;
      if (result?.kind !== 'workstream_created')
        throw new Error(`${workstream.title} was not created`);
      operators.set(result.workstream_id, workstream.operator);
      await time.advance(250);
      controlPlane.commands.submit(
        commands.startExecution(result.workstream_id, workstream),
        'internal',
      );

      const { instructAfterMs, interruptAfterMs } = workstream.operator;
      if (instructAfterMs === null && interruptAfterMs === null) continue;
      const executionId = controlPlane.projection.workstream(
        result.workstream_id,
      )?.current_execution_id;
      if (executionId === undefined || executionId === null) {
        throw new Error(`${workstream.title} has no execution to direct`);
      }
      if (instructAfterMs !== null) {
        later(instructAfterMs, () => {
          controlPlane.commands.submit(
            commands.instruct(executionId, MID_TURN_INSTRUCTION),
            'internal',
          );
        });
      }
      if (interruptAfterMs !== null) {
        later(interruptAfterMs, () => {
          controlPlane.commands.submit(commands.interrupt(executionId), 'internal');
        });
      }
    }

    await time.runUntilIdle();
    return serializeTrace([...journal.readAll()].map((stored) => stored.event));
  } finally {
    await controlPlane.close();
  }
}
