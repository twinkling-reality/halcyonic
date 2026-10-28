/**
 * Reads what a Quest says about itself for the device tool (device.ts): whether it is awake, what
 * is in front, Meta's testing service, a launch a system dialog held, packages and their bytes,
 * and the capture folders. Each reader takes the text a command printed and keeps fixed words,
 * names and numbers only. The shapes are the ones recorded on a Quest 3 on Horizon OS v207
 * (validation/quest-headset-in-the-loop.md).
 */

/** `dumpsys power`: asleep or awake (lower case), or null when the line is missing. */
export function readWakefulness(text: string): string | null {
  return /^\s*mWakefulness=(\w+)\s*$/m.exec(text)?.[1]?.toLowerCase() ?? null;
}

export interface Activity {
  readonly package: string;
  readonly activity: string;
}

/**
 * `dumpsys activity activities`: the first `topResumedActivity`. Only it tells what is in front;
 * `ResumedActivity` and `mFocusedApp` stay on Home while Meta's Guardian dialog is on top.
 */
export function readTopActivity(text: string): Activity | null {
  const match = /topResumedActivity=ActivityRecord\{\S+ u\d+ ([\w.]+)\/([\w.$]+)/.exec(text);
  if (match?.[1] === undefined || match[2] === undefined) return null;
  return { package: match[1], activity: match[2] };
}

const GUARDIAN = 'com.oculus.guardian';

/** Whether Meta's Guardian dialog is in front: the headset can't find its position in the room. */
export function isGuardianDialog(front: Activity | null): boolean {
  return front?.package === GUARDIAN && front.activity.endsWith('GuardianDialogActivity');
}

/** One plain line about what is in front. */
export function describeFront(front: Activity | null, own: string): string {
  if (front === null) return 'Nothing is in front (the headset may be asleep).';
  if (isGuardianDialog(front)) {
    return "The headset can't find its position in the room, so nothing opens. Stand it facing the room, or wear it.";
  }
  if (front.package === GUARDIAN) return `A system dialog is in front: ${front.activity}.`;
  if (front.package === own) return `${own} is in front.`;
  return `Another app is in front: ${front.package}.`;
}

/** `content call --uri content://com.oculus.rc --method GET_PROPERTY`: each property as a boolean. */
export function readTestingProperties(text: string): Record<string, boolean> {
  const bundle = /Bundle\[\{([^}]*)\}\]/.exec(text)?.[1];
  const properties: Record<string, boolean> = {};
  if (bundle === undefined) return properties;
  for (const pair of bundle.split(',')) {
    const match = /^\s*(\w+)=(true|false)\s*$/.exec(pair);
    if (match?.[1] !== undefined) properties[match[1]] = match[2] === 'true';
  }
  return properties;
}

/** Whether the log has Android's line for a launch a system dialog held. */
export function isLaunchHeld(log: string): boolean {
  return /Launch is blocked because: .*Caching launch/.test(log);
}

/** Whether the capture service said it uploads captures to the account. */
export function isMediaSyncOn(log: string): boolean {
  return /media sync preference is enabled: true/.test(log);
}

/** `pm path <package>`: every APK path it lists. Installing over an app needs exactly one base.apk. */
export function readBasePaths(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => /^package:(\S+\.apk)$/.exec(line.trim())?.[1])
    .filter((path): path is string => path !== undefined);
}

/** A `sha256sum` line: its 64 hex digits, or null. */
export function readSha256(text: string): string | null {
  return /^([0-9a-f]{64})\s/m.exec(text)?.[1] ?? null;
}

/** `dumpsys package <package>`: the first install time, which `install -r` must keep. */
export function readFirstInstall(text: string): string | null {
  return /^\s*firstInstallTime=(.+?)\s*$/m.exec(text)?.[1] ?? null;
}

/** A capture's name: the foreground package, the date and the time, as Meta's capture service and this tool write them. */
export const captureName = /^[\w.]+-\d{8}-\d{6}(-\d+)?\.(jpg|mp4)$/;

/** `ls <folder>...`: the file names, without the folder headers and blank lines. */
export function readListedFiles(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '' && !line.endsWith(':'));
}

/** The capture names in `after` that were not in `before`: only these may be pulled and removed. */
export function newCaptures(before: readonly string[], after: readonly string[]): string[] {
  const had = new Set(before);
  return after.filter((name) => !had.has(name) && captureName.test(name));
}
