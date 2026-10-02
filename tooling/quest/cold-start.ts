/**
 * Times Halcyonic's cold start on a Quest, `--runs` times (5): stops the app, waits, starts it,
 * and measures on the headset's own clock from the start command to Android's launch measure, the
 * app's first frame, and the stage having something to show (the demonstration's first play, or a
 * live control plane). The headset must be awake and worn, or its proximity sensor overridden, or
 * nothing renders. Prints numbers only.
 *
 *   pnpm quest:cold-start -- --runs 5
 *
 * See docs/internal/runbooks/XR_DEVELOPMENT.md, "Device measures on a Quest".
 */
import { ACTIVITY, adb, follow, option, PACKAGE, requireHeadset, sleep } from './adb.ts';
import {
  deviceMillis,
  epochMillis,
  isStageReady,
  median,
  readFirstFrame,
  readLaunch,
} from './readings.ts';

const runs = option('runs', 5);

const patience = option('timeout', 60) * 1000;

await requireHeadset();

const launches: number[] = [];
const firstFrames: number[] = [];
const ready: number[] = [];

for (let run = 1; run <= runs; run++) {
  await adb('shell', 'am', 'force-stop', PACKAGE);
  await sleep(4000);
  const startedOnDevice = deviceMillis(await adb('shell', 'date', '+%s.%N'));
  let frame: number | null = null;
  let stage: number | null = null;
  const stop = follow(['Unity:I'], (line) => {
    const at = epochMillis(line);
    if (at === null || at < startedOnDevice) return;
    if (frame === null && readFirstFrame(line) !== null) frame = at - startedOnDevice;
    if (stage === null && isStageReady(line)) stage = at - startedOnDevice;
  });
  const launch = readLaunch(await adb('shell', 'am', 'start', '-W', '-n', ACTIVITY));
  const until = Date.now() + patience;
  while (stage === null && Date.now() < until) await sleep(100);
  stop();
  if (launch.totalMs !== null) launches.push(launch.totalMs);
  if (frame !== null) firstFrames.push(frame);
  if (stage !== null) ready.push(stage);
  console.log(
    `run=${run} launch_ms=${launch.totalMs ?? ''} first_frame_ms=${frame ?? ''} stage_ready_ms=${stage ?? ''}`,
  );
}

console.log(
  `median launch_ms=${median(launches) ?? ''} first_frame_ms=${median(firstFrames) ?? ''} stage_ready_ms=${median(ready) ?? ''} runs=${runs} ready_runs=${ready.length}`,
);
