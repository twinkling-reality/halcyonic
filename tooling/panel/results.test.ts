import assert from 'node:assert/strict';
import { execFile, execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { startTestServer } from '../../apps/control-plane/src/testing/harness.ts';
import { EventBuilder } from '../../packages/domain/src/testing/events.ts';

const root = fileURLToPath(new URL('../../', import.meta.url));
const panel = join(root, 'apps/xr/Android/panel');
const android = '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer';
const bin = process.env.JAVA_HOME
  ? join(process.env.JAVA_HOME, 'bin')
  : join(android, 'OpenJDK/bin');
test('native recorded results consume strict scoped metadata, exact text paging and revocable view lifetime', async () => {
  const classes = mkdtempSync(join(tmpdir(), 'halcyonic-results-'));
  try {
    const src = join(panel, 'src/com/halcyonic/panel');
    execFileSync(join(bin, 'javac'), [
      '--release',
      '11',
      '-cp',
      process.env.ANDROID_JAR ?? join(android, 'SDK/platforms/android-36/android.jar'),
      '-d',
      classes,
      ...readdirSync(src)
        .filter((name) => name.endsWith('.java'))
        .map((name) => join(src, name)),
      join(panel, 'test/com/halcyonic/panel/PanelResultsSelfTest.java'),
    ]);
    for (const name of ['parser', 'text', 'lease'])
      assert.equal(
        execFileSync(
          join(bin, 'java'),
          ['-cp', classes, 'com.halcyonic.panel.PanelResultsSelfTest', name],
          { encoding: 'utf8' },
        ),
        'passed',
      );
    const server = await startTestServer();
    try {
      const b = new EventBuilder(),
        project = b.project(),
        work = b.workstream(project.projectId),
        run = b.execution(project.projectId, work.workstreamId);
      for (const stored of [project.event, work.event, run.event])
        server.controlPlane.recorder.import(stored.event);
      const scope = {
        projectId: project.projectId,
        workstreamId: work.workstreamId,
        executionId: run.executionId,
      };
      for (const stored of [
        b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native' }),
        b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 'turn' }),
        b.runtimeEvent(scope, 'runtime.tool.started', {
          tool_call_id: 'tool',
          tool_name: 'command',
          title: 'synthetic offline command',
        }),
        b.runtimeEvent(scope, 'runtime.tool.result', {
          kind: 'command',
          tool_call_id: 'tool',
          turn_id: 'turn',
          completion: 'completed',
          exit_code: 0,
          output: {
            text: 'native producer output\n',
            availability: 'available',
            complete: true,
            truncation: 'none',
          },
          redaction: { policy: 'known_held_values', replacements: 0 },
        }),
      ])
        server.controlPlane.recorder.import(stored.event);
      const query = {
        journal_id: server.journal.info.journal_id,
        project_id: project.projectId,
        workstream_id: work.workstreamId,
      };
      const path = `/api/executions/${run.executionId}/results?${new URLSearchParams(query)}`;
      assert.equal((await fetch(server.baseUrl + path)).status, 401);
      assert.equal(
        (
          await fetch(server.baseUrl + path + '&extra=field', {
            headers: { authorization: `Bearer ${server.token}` },
          })
        ).status,
        400,
      );
      assert.equal(
        (
          await fetch(server.baseUrl + path.replace(query.journal_id, b.id()), {
            headers: { authorization: `Bearer ${server.token}` },
          })
        ).status,
        404,
      );
      const read = await promisify(execFile)(join(bin, 'java'), [
        '-cp',
        classes,
        'com.halcyonic.panel.PanelResultsSelfTest',
        'transport',
        JSON.stringify({
          port: server.port,
          token: server.token,
          ...query,
          execution_id: run.executionId,
          runtime_id: 'mock',
        }),
      ]);
      assert.equal(read.stdout, 'passed');
    } finally {
      await server.stop();
    }
  } finally {
    rmSync(classes, { recursive: true, force: true });
  }
});
