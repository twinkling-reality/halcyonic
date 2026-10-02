/**
 * Logs a Quest's frame rate, battery and temperature through a long session (M6), one row of
 * numbers every `--every` seconds (30) for `--minutes` minutes (60), to the terminal and to a CSV
 * file in `--out` (`.private/m6`, which git ignores). Halcyonic should be running on the headset;
 * the frame rate comes from the app's own `Halcyonic: device frames` line each minute and from the
 * compositor's VrApi line where the headset logs one. Nothing but numbers is written.
 *
 *   pnpm quest:session -- --minutes 60 --every 30
 *
 * See docs/internal/runbooks/XR_DEVELOPMENT.md, "Device measures on a Quest".
 */
import { appendFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { adb, follow, option, requireHeadset, sleep, textOption } from './adb.ts';
import {
  type CompositorReading,
  type FramesReading,
  readBattery,
  readCompositor,
  readFrames,
  readThermal,
} from './readings.ts';

const COLUMNS = [
  'minute',
  'battery_percent',
  'battery_c',
  'plugged',
  'thermal_status',
  'cpu_c',
  'gpu_c',
  'skin_c',
  'app_fps',
  'app_below_60',
  'app_slowest_ms',
  'compositor_fps',
  'compositor_target',
  'compositor_stale',
] as const;

await requireHeadset();
const minutes = option('minutes', 60);
const every = option('every', 30);
const folder = textOption('out', join(process.cwd(), '.private', 'm6'));
mkdirSync(folder, { recursive: true });
const file = join(folder, `session-${new Date().toISOString().replace(/[:.]/g, '-')}.csv`);
appendFileSync(file, `${COLUMNS.join(',')}\n`);

let frames: FramesReading | null = null;
let compositor: CompositorReading | null = null;
let fpsLow = Infinity;
let belowSixty = 0;
const stop = follow(['Unity:I', 'VrApi:I'], (line) => {
  const app = readFrames(line);
  if (app !== null) {
    frames = app;
    belowSixty += app.belowSixty;
    // A stretch cut short by a pause says little about the rate.
    if (app.seconds >= 10) fpsLow = Math.min(fpsLow, app.perSecond);
  }
  compositor = readCompositor(line) ?? compositor;
});

const cell = (value: string | number | boolean | null | undefined) =>
  value === null || value === undefined ? '' : typeof value === 'boolean' ? (value ? 1 : 0) : value;

const started = Date.now();
let first: number | null = null;
let last: number | null = null;
let hottest = -Infinity;
try {
  while (Date.now() - started < minutes * 60_000) {
    const battery = readBattery(await adb('shell', 'dumpsys', 'battery'));
    const thermal = readThermal(await adb('shell', 'dumpsys', 'thermalservice'));
    const app = frames as FramesReading | null;
    const shown = compositor as CompositorReading | null;
    const row = [
      ((Date.now() - started) / 60_000).toFixed(1),
      battery.levelPercent,
      battery.celsius,
      battery.plugged,
      thermal.status,
      thermal.hottestByType.get(0),
      thermal.hottestByType.get(1),
      thermal.hottestByType.get(3),
      app?.perSecond,
      app?.belowSixty,
      app?.slowestMs,
      shown?.fps,
      shown?.target,
      shown?.stale,
    ].map(cell);
    appendFileSync(file, `${row.join(',')}\n`);
    console.log(COLUMNS.map((name, index) => `${name}=${row[index]}`).join(' '));
    if (battery.levelPercent !== null) {
      first ??= battery.levelPercent;
      last = battery.levelPercent;
    }
    for (const value of thermal.hottestByType.values()) hottest = Math.max(hottest, value);
    await sleep(every * 1000);
  }
} finally {
  stop();
}

const hours = (Date.now() - started) / 3_600_000;
console.log(
  [
    `summary minutes=${(hours * 60).toFixed(1)}`,
    `battery_used_percent=${first === null || last === null ? '' : first - last}`,
    `battery_per_hour=${first === null || last === null || hours === 0 ? '' : ((first - last) / hours).toFixed(1)}`,
    `hottest_c=${Number.isFinite(hottest) ? hottest : ''}`,
    `lowest_app_fps=${Number.isFinite(fpsLow) ? fpsLow : ''}`,
    `app_frames_below_60=${belowSixty}`,
    `file=${file}`,
  ].join(' '),
);
