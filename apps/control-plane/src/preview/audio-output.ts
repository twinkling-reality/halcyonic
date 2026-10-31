import { execFile } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { installProgram, type NativeProgram, programFor } from './launcher.ts';

/**
 * Whether a game played on the Mac sends sound out (S1): read by a small program of Halcyonic's
 * (`native/audio-output.c`) from CoreAudio's own record of each client process, for the play's
 * browser and every process it started, never from anything the game's page reports. "Sends sound
 * out" is CoreAudio's: audio IO running with an active output stream, which may be quiet. Built and
 * checked like the launcher; when it is missing, changed, or CoreAudio cannot be read, nothing is
 * said about the game's sound.
 */
export const AUDIO_OUTPUT: NativeProgram = {
  name: 'halcyonic-audio-output',
  source: fileURLToPath(new URL('../../native/audio-output.c', import.meta.url)),
  frameworks: ['CoreAudio'],
};

/** The helper's path when it may be used now, else null. */
export function audioOutputFor(dataDir: string): string | null {
  return programFor(dataDir, AUDIO_OUTPUT);
}

/** Builds the helper; resolves with its hash. */
export function installAudioOutput(options: {
  readonly dataDir: string;
  readonly clang: string;
}): Promise<string> {
  return installProgram(AUDIO_OUTPUT, options);
}

/** `pid` and every process under it, from the table `ps -A -o pid=,ppid=` prints. */
export function pidTree(processes: string, pid: number): Set<number> {
  const children = new Map<number, number[]>();
  for (const line of processes.split('\n')) {
    const [child, parent] = line.trim().split(/\s+/).map(Number);
    if (child === undefined || parent === undefined || Number.isNaN(child) || Number.isNaN(parent))
      continue;
    children.set(parent, [...(children.get(parent) ?? []), child]);
  }
  const tree = new Set<number>([pid]);
  const queue = [pid];
  for (let next = queue.pop(); next !== undefined; next = queue.pop())
    for (const child of children.get(next) ?? [])
      if (!tree.has(child)) {
        tree.add(child);
        queue.push(child);
      }
  return tree;
}

/**
 * Whether `pid` or a process under it sends sound out now, by `helper`; null when there is no helper
 * or the answer cannot be read.
 */
export async function outputInTree(helper: string | null, pid: number): Promise<boolean | null> {
  if (helper === null) return null;
  const run = (file: string, args: string[]) =>
    new Promise<string | null>((resolve) =>
      execFile(file, args, { encoding: 'utf8', timeout: 5000 }, (error, out) =>
        resolve(error === null ? out : null),
      ),
    );
  const processes = await run('/bin/ps', ['-A', '-o', 'pid=,ppid=']);
  if (processes === null) return null;
  const pids = [...pidTree(processes, pid)].slice(0, 4096).map(String);
  const answer = (await run(helper, pids))?.trim();
  return answer === '1' ? true : answer === '0' ? false : null;
}
