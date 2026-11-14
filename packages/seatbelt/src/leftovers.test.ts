import assert from 'node:assert/strict';
import { test } from 'node:test';
import { endLeftovers, type LeftoverScope, type Run, runListedTool } from './leftovers.ts';

const scope: LeftoverScope = {
  folder: '/Users/someone/apps/race-times',
  launchedAtMs: Date.parse('Fri Nov 14 00:30:00 2026'),
};
const AFTER = 'Fri Nov 14 00:31:10 2026';

interface Proc {
  cwd: string;
  start: string;
  ppid: number;
  command: string;
  /** Whether macOS shows this process's environment to the user (false: it is sandboxed). */
  readable: boolean;
}

/** A fake system: lsof gives each process's working folder; ps its start, its parent and its command, with its environment where readable. */
function system(procs: Map<number, Proc>): Run {
  return async (file, args) => {
    if (file.endsWith('lsof'))
      return [...procs].map(([pid, proc]) => `p${pid}\nfcwd\nn${proc.cwd}`).join('\n');
    const pid = Number(args[args.length - 1]);
    const proc = procs.get(pid);
    if (proc === undefined) return '';
    if (args.includes('lstart=')) return `${proc.start}\n`;
    if (args.includes('ppid=')) return `${proc.ppid}\n`;
    return args.includes('-E') && proc.readable
      ? `${proc.command} HOME=/Users/someone PATH=/usr/bin\n`
      : `${proc.command}\n`;
  };
}

const quick = { wait: async () => {} };

test("only a confined server's own leftovers end: in its folder, after its launch, orphaned and sandboxed", async () => {
  const procs = new Map<number, Proc>([
    // The agent's dev server, left running in the folder when its shell exited.
    [
      501,
      {
        cwd: `${scope.folder}/site`,
        start: AFTER,
        ppid: 1,
        command: 'node dev.js',
        readable: false,
      },
    ],
    // The person's own dev server in the same folder, after the launch, orphaned too: not sandboxed.
    [502, { cwd: scope.folder, start: AFTER, ppid: 1, command: 'npm run dev', readable: true }],
    // Halcyonic's own confined preview of the app: sandboxed, but the control plane's child.
    [
      503,
      { cwd: scope.folder, start: AFTER, ppid: 4242, command: 'node preview.js', readable: false },
    ],
    // A sandboxed orphan in another folder, and one in a folder whose name only begins with this one's.
    [
      504,
      {
        cwd: '/Users/someone/apps/other',
        start: AFTER,
        ppid: 1,
        command: 'sleep 600',
        readable: false,
      },
    ],
    [
      505,
      { cwd: `${scope.folder}-old`, start: AFTER, ppid: 1, command: 'sleep 600', readable: false },
    ],
    // A sandboxed orphan in the folder from before this launch.
    [
      506,
      {
        cwd: scope.folder,
        start: 'Fri Nov 14 00:20:00 2026',
        ppid: 1,
        command: 'sleep 600',
        readable: false,
      },
    ],
  ]);
  const signals: [number, string][] = [];
  const ended = await endLeftovers(scope, {
    exec: system(procs),
    signal: (pid, name) => {
      signals.push([pid, name]);
      if (name === 'SIGTERM') procs.delete(pid);
    },
    ...quick,
  });
  assert.equal(ended, 1);
  assert.deepEqual(signals, [[501, 'SIGTERM']]);
});

test('a leftover that ignores TERM is killed after the grace; a pid reused meanwhile is never signalled', async () => {
  const procs = new Map<number, Proc>([
    [601, { cwd: scope.folder, start: AFTER, ppid: 1, command: 'stubborn', readable: false }],
    [602, { cwd: scope.folder, start: AFTER, ppid: 1, command: 'quick', readable: false }],
  ]);
  const signals: [number, string][] = [];
  let waits = 0;
  await endLeftovers(scope, {
    exec: system(procs),
    signal: (pid, name) => {
      signals.push([pid, name]);
      // 601 ends only on KILL.
      if (pid === 601 && name === 'SIGKILL') procs.delete(601);
    },
    // During the first grace 602 exits and its pid goes to the person's editor, started later and readable.
    wait: async () => {
      waits++;
      if (waits === 1)
        procs.set(602, {
          cwd: scope.folder,
          start: 'Fri Nov 14 00:40:00 2026',
          ppid: 1,
          command: 'vim',
          readable: true,
        });
    },
  });
  assert.deepEqual(signals, [
    [601, 'SIGTERM'],
    [602, 'SIGTERM'],
    [601, 'SIGKILL'],
  ]);
});

test('a command orphaned only after the server stopped is caught by the second sweep', async () => {
  const procs = new Map<number, Proc>([
    // Its shell is still its parent at the first sweep.
    [701, { cwd: scope.folder, start: AFTER, ppid: 700, command: 'sleep 600', readable: false }],
  ]);
  const signals: [number, string][] = [];
  const ended = await endLeftovers(scope, {
    exec: system(procs),
    signal: (pid, name) => {
      signals.push([pid, name]);
      if (name === 'SIGTERM') procs.delete(pid);
    },
    wait: async () => {
      const proc = procs.get(701);
      if (proc !== undefined) proc.ppid = 1;
    },
  });
  assert.equal(ended, 1);
  assert.deepEqual(signals, [[701, 'SIGTERM']]);
});

test('nothing in the folder, or a system that cannot be read: nothing is signalled', async () => {
  const signals: number[] = [];
  assert.equal(
    await endLeftovers(scope, {
      exec: async () => null,
      signal: (pid) => signals.push(pid),
      ...quick,
    }),
    0,
  );
  assert.equal(
    await endLeftovers(scope, {
      exec: system(new Map()),
      signal: (pid) => signals.push(pid),
      ...quick,
    }),
    0,
  );
  assert.deepEqual(signals, []);
});

test('the sweep runs only lsof and ps, by absolute path, never a shell or another file (AUDIT3 A2)', async () => {
  for (const [file, args] of [
    ['/bin/sh', ['-c', 'echo ran']],
    ['/usr/bin/env', ['echo', 'ran']],
    ['ps', ['-p', String(process.pid)]],
    ['/bin/ps/../sh', ['-c', 'echo ran']],
  ] as const)
    assert.equal(await runListedTool(file, args), null, file);
  const own = await runListedTool('/bin/ps', ['-o', 'pid=', '-p', String(process.pid)]);
  assert.equal(own?.trim(), String(process.pid));
});
