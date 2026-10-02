/**
 * Reads what a Quest reports about itself during a device session (M6): the battery, the thermal
 * service, the compositor's once-a-second VrApi line, and Halcyonic's own `Halcyonic: device`
 * lines. Every reading is numbers only; nothing else from the headset's log is kept.
 */

/** `adb shell dumpsys battery`: level in percent, temperature in degrees Celsius, whether it charges. */
export interface BatteryReading {
  readonly levelPercent: number | null;
  readonly celsius: number | null;
  readonly plugged: boolean | null;
}

export function readBattery(text: string): BatteryReading {
  const field = (name: string): number | null => {
    const match = new RegExp(`^\\s*${name}:\\s*(-?\\d+)\\s*$`, 'm').exec(text);
    return match?.[1] === undefined ? null : Number(match[1]);
  };
  const level = field('level');
  const scale = field('scale');
  const tenths = field('temperature');
  const sources = ['AC powered', 'USB powered', 'Wireless powered', 'Dock powered'].map((name) => {
    const match = new RegExp(`^\\s*${name}:\\s*(true|false)\\s*$`, 'm').exec(text);
    return match?.[1] === undefined ? null : match[1] === 'true';
  });
  const known = sources.filter((source) => source !== null);
  return {
    levelPercent: level === null ? null : scale ? Math.round((level * 100) / scale) : level,
    celsius: tenths === null ? null : tenths / 10,
    plugged: known.length === 0 ? null : known.some(Boolean),
  };
}

/**
 * `adb shell dumpsys thermalservice`: the thermal status (0 none to 6 shutdown, Android's
 * PowerManager codes) and the hottest temperature of each sensor type (0 CPU, 1 GPU, 2 battery,
 * 3 skin, and so on), from the HAL's current temperatures, else the cached ones.
 */
export interface ThermalReading {
  readonly status: number | null;
  readonly hottestByType: ReadonlyMap<number, number>;
}

export function readThermal(text: string): ThermalReading {
  const status = /^\s*Thermal Status:\s*(\d+)/m.exec(text)?.[1];
  const current = text.split(/Current temperatures from HAL:/)[1];
  const section = current ?? text;
  const hottestByType = new Map<number, number>();
  for (const match of section.matchAll(
    /Temperature\{mValue=(-?[\d.]+), mType=(-?\d+), mName=[^,}]*, mStatus=\d+\}/g,
  )) {
    const value = Number(match[1]);
    const type = Number(match[2]);
    if (!Number.isFinite(value)) continue;
    hottestByType.set(type, Math.max(hottestByType.get(type) ?? -Infinity, value));
  }
  return { status: status === undefined ? null : Number(status), hottestByType };
}

/** The compositor's line, `VrApi: FPS=72/72,Prd=…,Stale=0,…,Temp=34.0C/0.0C,…`, where present. */
export interface CompositorReading {
  readonly fps: number;
  readonly target: number;
  readonly stale: number | null;
  readonly celsius: number | null;
}

export function readCompositor(line: string): CompositorReading | null {
  const fps = /\bFPS=(\d+)\/(\d+)/.exec(line);
  if (fps?.[1] === undefined || fps[2] === undefined) return null;
  const stale = /\bStale=(\d+)/.exec(line)?.[1];
  const temp = /\bTemp=(-?[\d.]+)C/.exec(line)?.[1];
  return {
    fps: Number(fps[1]),
    target: Number(fps[2]),
    stale: stale === undefined ? null : Number(stale),
    celsius: temp === undefined ? null : Number(temp),
  };
}

/** `Halcyonic: device frames 4320 in 60.0 s, 72.0 a second at 72 Hz, slowest 18.4 ms, 3 below 60, 5 missed`. */
export interface FramesReading {
  readonly frames: number;
  readonly seconds: number;
  readonly perSecond: number;
  readonly hertz: number | null;
  readonly slowestMs: number;
  readonly belowSixty: number;
  readonly missed: number | null;
}

export function readFrames(line: string): FramesReading | null {
  const match =
    /Halcyonic: device frames (\d+) in ([\d.]+) s, ([\d.]+) a second(?: at (\d+) Hz)?, slowest ([\d.]+) ms, (\d+) below 60(?:, (\d+) missed)?/.exec(
      line,
    );
  if (match === null) return null;
  const number = (index: number): number | null =>
    match[index] === undefined ? null : Number(match[index]);
  return {
    frames: number(1) ?? 0,
    seconds: number(2) ?? 0,
    perSecond: number(3) ?? 0,
    hertz: number(4),
    slowestMs: number(5) ?? 0,
    belowSixty: number(6) ?? 0,
    missed: number(7),
  };
}

/** `Halcyonic: device first frame 2345 ms after start`. */
export function readFirstFrame(line: string): number | null {
  const match = /Halcyonic: device first frame (\d+) ms after start/.exec(line);
  return match?.[1] === undefined ? null : Number(match[1]);
}

/** `Halcyonic: device view field left eye left 52.0 right 43.0 up 48.0 down 50.0, right eye …, both 104.0 across 96.0 tall`. */
export function readViewField(line: string): { across: number; tall: number } | null {
  const match = /Halcyonic: device view field .* both ([\d.]+) across ([\d.]+) tall/.exec(line);
  return match?.[1] === undefined || match[2] === undefined
    ? null
    : { across: Number(match[1]), tall: Number(match[2]) };
}

/** `Halcyonic: demonstration read 1373 KiB, loaded in 40 ms on the main thread, parsed in 900 ms on another`. */
export function readDemonstrationRead(
  line: string,
): { kib: number; loadedMs: number; parsedMs: number } | null {
  const match =
    /Halcyonic: demonstration read (\d+) KiB, loaded in (\d+) ms on the main thread, parsed in (\d+) ms/.exec(
      line,
    );
  return match?.[1] === undefined || match[2] === undefined || match[3] === undefined
    ? null
    : { kib: Number(match[1]), loadedMs: Number(match[2]), parsedMs: Number(match[3]) };
}

/**
 * Whether a Halcyonic line says the stage has something to show: the demonstration's first play,
 * or a live control plane.
 */
export function isStageReady(line: string): boolean {
  return /Halcyonic: (demonstration plays from its beginning \(1\)|connection Live)/.test(line);
}

/** The device's own time of a `logcat -v epoch` line, in milliseconds, or null. */
export function epochMillis(line: string): number | null {
  const match = /^\s*(\d{9,})\.(\d{3})/.exec(line);
  return match?.[1] === undefined || match[2] === undefined
    ? null
    : Number(match[1]) * 1000 + Number(match[2]);
}

/** `am start -W`: Android's own launch measure, `TotalTime: 1234`. */
export function readLaunch(text: string): { totalMs: number | null; waitMs: number | null } {
  const total = /^\s*TotalTime:\s*(\d+)/m.exec(text)?.[1];
  const wait = /^\s*WaitTime:\s*(\d+)/m.exec(text)?.[1];
  return {
    totalMs: total === undefined ? null : Number(total),
    waitMs: wait === undefined ? null : Number(wait),
  };
}

/** The middle value, or the mean of the two middle values; null for none. */
export function median(values: readonly number[]): number | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  const upper = sorted[middle] ?? 0;
  return sorted.length % 2 === 1 ? upper : ((sorted[middle - 1] ?? 0) + upper) / 2;
}

/** The headset's clock from `date +%s.%N`, or from whole seconds where it has no %N. */
export function deviceMillis(text: string): number {
  const [seconds, fraction] = text.trim().split('.');
  const whole = Number(seconds);
  if (!Number.isFinite(whole)) throw new Error('the headset did not tell its time');
  const part = fraction !== undefined && /^\d+$/.test(fraction) ? Number(`0.${fraction}`) : 0;
  return Math.floor((whole + part) * 1000);
}
