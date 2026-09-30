/**
 * Smoke test against the real Claude Code CLI bundled with the SDK. It calls the Anthropic API and
 * spends credit, so it runs only when asked:
 *
 *   HALCYONIC_CLAUDE_SMOKE=1 ANTHROPIC_API_KEY=... \
 *     node --test packages/integrations/claude-code/src/smoke.test.ts
 *
 * HALCYONIC_CLAUDE_SMOKE_MODEL optionally chooses the model, for example a small one to limit cost.
 * Like every execution Halcyonic starts, the session uses your HOME and Claude Code configuration:
 * your hooks run and its transcript is kept with your other Claude Code sessions.
 */
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { type Options, query } from '@anthropic-ai/claude-agent-sdk';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

const enabled =
  process.env.HALCYONIC_CLAUDE_SMOKE === '1' && (process.env.ANTHROPIC_API_KEY ?? '') !== '';

const execution: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000303' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000302' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000301' as ProjectId,
};

const TURN_ENDS = new Set([
  'runtime.turn.completed',
  'runtime.turn.failed',
  'runtime.turn.interrupted',
]);

describe('Claude Agent against the real Claude Code CLI', {
  skip: enabled
    ? false
    : 'set HALCYONIC_CLAUDE_SMOKE=1 and ANTHROPIC_API_KEY to run; it spends API credit',
}, () => {
  test('a session keeps the chosen id, waits for approval, takes a second turn and can be interrupted', {
    timeout: 600_000,
  }, async () => {
    const workdir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-smoke-')));
    const records = mkdtempSync(join(tmpdir(), 'halcyonic-claude-smoke-records-'));
    let launched: Options | undefined;
    const adapter = new ClaudeAgentRuntimeAdapter({
      directoryPolicy: (path: string) => ({ ok: true, directory: path }),
      processRecordFile: join(records, 'claude-agent-processes.json'),
      query: (params) => {
        launched = params.options;
        return query(params);
      },
    });
    const observed: RuntimeObservation[] = [];
    const answered = new Set<string>();
    const turnEnds = () => observed.filter((observation) => TURN_ENDS.has(observation.type));
    const seen = () => observed.map((observation) => observation.type).join(', ');
    /** Waits for the given number of turn ends, approving every request while `approve` is set. */
    const waitForTurnEnds = async (count: number, approve: boolean) => {
      const deadline = Date.now() + 240_000;
      while (turnEnds().length < count) {
        if (Date.now() > deadline) {
          throw new Error(`timed out waiting for turn ${count} to end; observed ${seen()}`);
        }
        for (const observation of observed) {
          if (observation.type !== 'runtime.approval.requested' || !approve) continue;
          const approvalId = observation.payload.approval_id;
          if (answered.has(approvalId)) continue;
          answered.add(approvalId);
          await adapter.respondToApproval({
            execution,
            approval_id: approvalId,
            decision: 'approve',
            message: null,
          });
        }
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
    };
    const model = process.env.HALCYONIC_CLAUDE_SMOKE_MODEL;
    try {
      const { native_id } = await adapter.startExecution({
        execution,
        instruction:
          'Use the Bash tool to run `touch smoke-marker.txt` in the current directory, then reply with the single word done.',
        options: { ...(model !== undefined && { model }) },
        model_ref: null,
        directory: workdir,
        emit: (observation) => observed.push(observation),
      });
      // The CLI honors the session id Halcyonic chose at launch.
      assert.equal(native_id, launched?.sessionId);
      assert.deepEqual(observed[0]?.payload, { native_id });

      await waitForTurnEnds(1, true);
      const first = turnEnds()[0];
      // A failed turn names its cause, for example an API key the API refuses.
      assert.equal(
        first?.type,
        'runtime.turn.completed',
        `the first turn ended with ${JSON.stringify(first?.payload)}`,
      );
      assert.ok(answered.size >= 1, 'Claude Code asked for approval before changing files');
      assert.ok(existsSync(join(workdir, 'smoke-marker.txt')));
      assert.ok(observed.some((observation) => observation.type === 'runtime.tool.completed'));

      await adapter.sendInstruction({ execution, text: 'Reply with the single word again.' });
      await waitForTurnEnds(2, true);
      assert.equal(turnEnds()[1]?.type, 'runtime.turn.completed');

      await adapter.sendInstruction({
        execution,
        // A command that writes a file always needs approval; `sleep` alone may not.
        text: 'Use the Bash tool to run `touch interrupt-marker.txt && sleep 120`, then reply with the single word finished.',
      });
      const deadline = Date.now() + 240_000;
      while (
        !observed.some(
          (observation) =>
            observation.type === 'runtime.approval.requested' &&
            !answered.has(observation.payload.approval_id),
        )
      ) {
        if (Date.now() > deadline) {
          throw new Error(`timed out waiting for the third approval; observed ${seen()}`);
        }
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
      await adapter.interrupt({ execution });
      await waitForTurnEnds(3, false);
      assert.equal(turnEnds()[2]?.type, 'runtime.turn.interrupted');
    } finally {
      await adapter.close();
      rmSync(workdir, { recursive: true, force: true });
      rmSync(records, { recursive: true, force: true });
    }
  });
});
