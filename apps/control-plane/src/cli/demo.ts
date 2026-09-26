/**
 * Drives the demo plan against a running control plane through the realtime protocol and prints
 * what a client sees: state changes streaming in, an approval round trip, and the final state.
 * Start the control plane first with `pnpm dev`.
 */
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { ExecutionView, ServerMessage, WorkstreamView } from '@halcyonic/contracts';
import { systemClock } from '@halcyonic/runtime-core';
import { RealtimeClient } from '../client/realtime-client.ts';
import { loadConfig } from '../config.ts';
import { createCommandFactory, DEMO_PROJECT_NAME, DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { ACCESS_TOKEN_FILE } from '../http/security.ts';
import { createUuidV7Generator } from '../ids.ts';

const APPROVAL_DELAY_MS = 3000;
const OVERALL_TIMEOUT_MS = 90_000;
const AT_REST = new Set(['completed', 'failed', 'interrupted', 'unknown']);

async function main(): Promise<void> {
  const config = loadConfig();
  const token = (await readFile(join(config.dataDir, ACCESS_TOKEN_FILE), 'utf8')).trim();
  const host = config.host === '::1' ? '[::1]' : config.host;
  const client = await RealtimeClient.connect(`ws://${host}:${config.port}/realtime`, token);
  const clientInfo = { name: 'halcyonic-demo', version: null, device_label: null };
  client.hello(clientInfo);

  const welcome = await client.waitFor((message) => message.type === 'welcome');
  if (welcome.type === 'welcome' && welcome.journal.origin === 'fixture') {
    print('The connected control plane serves a fixture journal; run the demo against `pnpm dev`.');
  }

  const titles = new Map<string, string>();
  const executions = new Map<string, ExecutionView>();
  const approvalsSent = new Set<string>();
  const commands = createCommandFactory(createUuidV7Generator(), systemClock, clientInfo);

  client.onMessage((message) => {
    if (message.type !== 'event') return;
    for (const workstream of message.changes.workstreams)
      printWorkstream(workstream, message, titles);
    for (const execution of message.changes.executions) {
      executions.set(execution.execution_id, execution);
      for (const approval of execution.pending_approvals) {
        if (approvalsSent.has(approval.approval_id)) continue;
        approvalsSent.add(approval.approval_id);
        print(`  approval requested: ${approval.subject.summary}`);
        print(`  (the demo approves it in ${APPROVAL_DELAY_MS / 1000}s, as a person would)`);
        setTimeout(() => {
          print(`  sending approval for ${approval.approval_id}`);
          client.command(commands.approve(execution.execution_id, approval.approval_id));
        }, APPROVAL_DELAY_MS);
      }
    }
  });

  const submit = async (command: Parameters<RealtimeClient['command']>[0]) => {
    client.command(command);
    const ack = await client.waitFor(
      (message) => message.type === 'command_ack' && message.command_id === command.command_id,
    );
    if (ack.type !== 'command_ack' || ack.disposition !== 'accepted' || ack.command === null) {
      throw new Error(`command ${command.command_type} was not accepted: ${JSON.stringify(ack)}`);
    }
    return ack.command;
  };

  print(
    `Creating "${DEMO_PROJECT_NAME}" with ${DEMO_WORKSTREAMS.length} workstreams on the mock runtime.`,
  );
  const project = await submit(commands.createProject(DEMO_PROJECT_NAME));
  if (project.result?.kind !== 'project_created') throw new Error('project was not created');
  const started: string[] = [];
  for (const workstream of DEMO_WORKSTREAMS) {
    const created = await submit(commands.createWorkstream(project.result.project_id, workstream));
    if (created.result?.kind !== 'workstream_created')
      throw new Error('workstream was not created');
    titles.set(created.result.workstream_id, workstream.title);
    started.push(created.result.workstream_id);
    await submit(commands.startExecution(created.result.workstream_id, workstream));
  }

  const deadline = Date.now() + OVERALL_TIMEOUT_MS;
  while (Date.now() < deadline) {
    const views = [...executions.values()].filter((view) => started.includes(view.workstream_id));
    if (views.length === started.length && views.every((view) => AT_REST.has(view.status))) break;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  print('\nFinal state (synthetic: produced by the mock runtime):');
  for (const view of executions.values()) {
    if (!started.includes(view.workstream_id)) continue;
    const test =
      view.last_test_run === null ? 'no test run' : `last test run ${view.last_test_run.outcome}`;
    print(
      `  ${titles.get(view.workstream_id)}: ${view.status}, ${test}, ${view.turn_count} turn(s)`,
    );
  }
  if (client.invalid.length > 0) {
    print(`\n${client.invalid.length} server message(s) did not match the contract.`);
    process.exitCode = 1;
  }
  await client.close();
}

function printWorkstream(
  workstream: WorkstreamView,
  message: Extract<ServerMessage, { type: 'event' }>,
  titles: Map<string, string>,
): void {
  const title = titles.get(workstream.workstream_id) ?? workstream.title;
  const attention =
    workstream.attention.level === 'none'
      ? ''
      : ` [${workstream.attention.level}: ${workstream.attention.reasons.map((r) => r.kind).join(', ')}]`;
  print(
    `#${String(message.position).padStart(4)} ${title.padEnd(34)} ${workstream.status.padEnd(18)} ${message.event.event_type}${attention}`,
  );
}

function print(line: string): void {
  process.stdout.write(`${line}\n`);
}

main().catch((error: unknown) => {
  process.stderr.write(`demo failed: ${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
