import { execFile } from 'node:child_process';

const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', LC_ALL: 'C' };

/** An internet socket's name as lsof prints it, between loopback addresses only. */
const LOOPBACK = /^(?:127\.0\.0\.1|\[::1\]):\d+(?:->(?:127\.0\.0\.1|\[::1\]):\d+)?(?: \(\w+\))?$/;

export interface NetworkProbe {
  /** Every socket beyond loopback a watched process held, as `<command> <pid> <name>`. */
  readonly beyondLoopback: readonly string[];
  /** Every loopback socket seen, by name, which shows the probe sees sockets at all. */
  readonly loopback: ReadonlySet<string>;
  /** Samples taken while a server ran. */
  readonly samples: number;
  /** Samples in which lsof could not see a running server, and failures of the probe itself. */
  readonly blind: readonly string[];
  stop(): Promise<void>;
}

/**
 * Watches, every `intervalMs` from before anything is launched, the internet sockets of every
 * process below `root` (the test's own process), the OpenCode server the adapter launches and
 * whatever it starts among them. Other apps' connections are not below it, so they count neither
 * way. A sample in which lsof cannot see a server running the binary is recorded as blind, so a
 * probe that cannot see never passes for one that saw nothing. Limits: a connection shorter than
 * a sample is missed, and a DNS lookup, which mDNSResponder makes, is never seen.
 */
export function probeNetwork(root: number, binaryPath: string, intervalMs = 200): NetworkProbe {
  const beyondLoopback: string[] = [];
  const loopback = new Set<string>();
  const blind: string[] = [];
  let samples = 0;
  let running = true;
  const loop = (async () => {
    while (running) {
      try {
        const tree = await below(root);
        if (tree.length > 0) {
          const sockets = await run('lsof', [
            '-nP',
            '-a',
            '-p',
            tree.map((p) => p.pid).join(','),
            '-i',
          ]);
          for (const line of sockets.split('\n').slice(1)) {
            const fields = line.trim().split(/\s+/);
            const name = fields.slice(8).join(' ');
            if (name === '') continue;
            if (LOOPBACK.test(name)) loopback.add(name.replace(/ \(\w+\)$/, ''));
            else beyondLoopback.push(`${fields[0]} ${fields[1]} ${name}`);
          }
        }
        const server = tree.find((p) => p.command.startsWith(`${binaryPath} `));
        if (server !== undefined) {
          samples += 1;
          const visible = await run('lsof', [
            '-a',
            '-p',
            String(server.pid),
            '-d',
            'cwd',
            '-F',
            'p',
          ]);
          if (
            !visible.split('\n').includes(`p${server.pid}`) &&
            (await below(root)).some((p) => p.pid === server.pid)
          ) {
            blind.push(`sample ${samples}: lsof could not see the server ${server.pid}`);
          }
        }
      } catch (error) {
        blind.push(`the probe failed: ${error instanceof Error ? error.message : String(error)}`);
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, intervalMs));
    }
  })();
  return {
    beyondLoopback,
    loopback,
    blind,
    get samples() {
      return samples;
    },
    stop: async () => {
      running = false;
      await loop;
    },
  };
}

/** The processes below `root`, followed through their parents, with their command lines. */
async function below(root: number): Promise<{ pid: number; command: string }[]> {
  const rows = (await run('ps', ['-A', '-o', 'pid=,ppid=,args=']))
    .split('\n')
    .map((line) => /^\s*(\d+)\s+(\d+)\s+(.*)$/.exec(line))
    .filter((match): match is RegExpExecArray => match !== null)
    .map((match) => ({
      pid: Number(match[1]),
      parent: Number(match[2]),
      command: match[3] as string,
    }));
  const found = new Set([root]);
  const result: { pid: number; command: string }[] = [];
  for (let grew = true; grew; ) {
    grew = false;
    for (const row of rows) {
      if (found.has(row.parent) && !found.has(row.pid)) {
        found.add(row.pid);
        result.push({ pid: row.pid, command: row.command });
        grew = true;
      }
    }
  }
  return result;
}

/** Runs a command; exit status 1 is lsof's when a process listed has exited, not a failure. */
function run(command: string, args: readonly string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(
      command,
      args,
      { env: PS_ENV, timeout: 5000, maxBuffer: 16 * 1024 * 1024 },
      (error, stdout) => {
        if (error === null || error.code === 1) resolve(stdout);
        else reject(new Error(`${command} failed: ${error.message}`));
      },
    );
  });
}
