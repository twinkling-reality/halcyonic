import { execFile } from 'node:child_process';
import { readDescendants, runsAppServer } from '../server-record.ts';

const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', LC_ALL: 'C' };

/** An internet socket's name as lsof prints it, between loopback addresses only. */
const LOOPBACK = /^(?:127\.0\.0\.1|\[::1\]):\d+(?:->(?:127\.0\.0\.1|\[::1\]):\d+)?(?: \(\w+\))?$/;

export interface NetworkProbe {
  /** Every socket beyond loopback a watched process held, as `<command> <pid> <name>`. */
  readonly beyondLoopback: readonly string[];
  /** Every loopback socket seen, by name, which shows the probe sees sockets at all. */
  readonly loopback: ReadonlySet<string>;
  /** The Codex servers seen, by pid. */
  readonly servers: ReadonlySet<number>;
  /** Samples taken while a server ran. */
  readonly samples: number;
  /** Samples in which lsof could not see a running server, and failures of the probe itself. */
  readonly blind: readonly string[];
  stop(): Promise<void>;
}

/**
 * Watches, every `intervalMs` from before anything is launched, the internet sockets of every
 * process below `root` (the test's own process): the Codex server the adapter launches, the
 * commands it runs in sessions of their own, the version check and the watchdog. Ollama's own
 * connections, and any other app's, are not below it, so they count neither way. A sample in which
 * lsof cannot see a server that is running, as without permission to inspect it, is recorded as
 * blind, so a probe that cannot see never passes for one that saw nothing.
 *
 * Limits, stated rather than hidden: a connection opened and closed between two samples is
 * missed, and a DNS lookup goes through mDNSResponder, not the process, so it is never seen.
 */
export function probeNetwork(root: number, binaryPath: string, intervalMs = 200): NetworkProbe {
  const beyondLoopback: string[] = [];
  const loopback = new Set<string>();
  const servers = new Set<number>();
  const blind: string[] = [];
  let samples = 0;
  let running = true;
  const loop = (async () => {
    while (running) {
      try {
        const tree = await readDescendants(root);
        const server = tree.find((process) => runsAppServer(process.command, binaryPath));
        if (tree.length > 0) {
          const sockets = await run('lsof', [
            '-nP',
            '-a',
            '-p',
            tree.map((process) => process.pid).join(','),
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
        if (server !== undefined) {
          servers.add(server.pid);
          // lsof lists a process's current folder whenever it can inspect the process at all.
          const visible = await run('lsof', [
            '-a',
            '-p',
            String(server.pid),
            '-d',
            'cwd',
            '-F',
            'p',
          ]);
          const stillRunning = (await readDescendants(root)).some((p) => p.pid === server.pid);
          if (stillRunning) {
            samples += 1;
            if (!visible.split('\n').includes(`p${server.pid}`)) {
              blind.push(`sample ${samples}: lsof could not see the server ${server.pid}`);
            }
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
    servers,
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

/** Runs a command; exit status 1 is lsof's when a process listed has exited, not a failure. */
function run(command: string, args: readonly string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(command, args, { env: PS_ENV, timeout: 5000 }, (error, stdout) => {
      if (error === null || error.code === 1) resolve(stdout);
      else reject(new Error(`${command} failed: ${error.message}`));
    });
  });
}
