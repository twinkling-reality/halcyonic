/**
 * Plays a harness that launches the control plane as its child, the way the C# tests do: the
 * control plane's stdin is a pipe that this process holds open and never writes to. It prints
 * `{"launched": n}` with the control plane's pid; the control plane inherits this process's
 * environment, stdout and stderr, so its log follows on the same streams. Tests kill this process
 * to check that the control plane does not outlive its launcher. It exits when its own stdin
 * ends, so it does not outlive its test either. Usage: `node launcher.ts`.
 */
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const MAIN = fileURLToPath(new URL('../main.ts', import.meta.url));

const controlPlane = spawn(process.execPath, [MAIN], { stdio: ['pipe', 'inherit', 'inherit'] });
process.stdout.write(`${JSON.stringify({ launched: controlPlane.pid })}\n`);

const exit = () => process.exit(0);
process.stdin.on('end', exit);
process.stdin.on('error', exit);
process.stdin.resume();
