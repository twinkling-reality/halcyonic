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
 * `dumpsys activity activities`: the first `topResumedActivity`, the top task's. On 2026-10-28 only
 * it told what was in front: `ResumedActivity` and `mFocusedApp` stayed on Home while Meta's
 * Guardian dialog was on top. A system dialog can show below it, so readSystemDialogs reads them all.
 */
export function readTopActivity(text: string): Activity | null {
  const match = /topResumedActivity=ActivityRecord\{\S+ u\d+ ([\w.]+)\/([\w.$]+)/.exec(text);
  if (match?.[1] === undefined || match[2] === undefined) return null;
  return { package: match[1], activity: match[2] };
}

const GUARDIAN = 'com.oculus.guardian';
/** Meta's alert service, whose Headset Power menu a power-button hold leaves open. */
const ALERTS = 'com.oculus.vralertservice';
/** Meta's first-time tips, such as "Customize your Library", which held a launch on 2026-11-03. */
const FIRST_TIME = 'com.oculus.firsttimenux';

/** Whether Meta's Guardian dialog is in front: the headset can't find its position in the room. */
export function isGuardianDialog(front: Activity | null): boolean {
  return front?.package === GUARDIAN && front.activity.endsWith('GuardianDialogActivity');
}

/**
 * `dumpsys activity activities`: every system dialog showing, the Guardian's, the alert service's or
 * a first-time tip, in the order of the tasks, then the one `ResumedActivity` names. A Quest resumes several
 * activities at once, one per task: on 2026-11-03 the Headset Power menu's task came first while
 * `ResumedActivity` named the Guardian dialog. Each kind has held a launch (2026-10-28, 2026-11-03).
 */
export function readSystemDialogs(text: string): Activity[] {
  const dialogs: Activity[] = [];
  for (const match of text.matchAll(
    /(?:topResumedActivity=|ResumedActivity: )ActivityRecord\{\S+ u\d+ ([\w.]+)\/([\w.$]+)/g,
  )) {
    const found = { package: match[1] ?? '', activity: match[2] ?? '' };
    const dialog = [GUARDIAN, ALERTS, FIRST_TIME].includes(found.package);
    const seen = dialogs.some(
      (kept) => kept.package === found.package && kept.activity === found.activity,
    );
    if (dialog && !seen) dialogs.push(found);
  }
  return dialogs;
}

/** One plain line about what is in front. */
export function describeFront(front: Activity | null, own: string): string {
  if (front === null) return 'Nothing is in front (the headset may be asleep).';
  if (isGuardianDialog(front)) {
    return "The headset can't find its position in the room, so nothing opens. Stand it facing the room, or wear it.";
  }
  if (front.package === ALERTS && front.activity.endsWith('ShutdownConfirmActivity')) {
    return "Meta's Headset Power menu is open (Sleep, Cancel, Power Options), so nothing opens until someone closes it in the headset.";
  }
  if (front.package === ALERTS) {
    return `A system alert is showing (${front.activity}), so nothing opens until someone closes it in the headset.`;
  }
  if (front.package === FIRST_TIME) {
    return "One of Meta's first-time tips is open (such as Customize your Library), so nothing opens until someone closes it in the headset.";
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

/**
 * Whether the log has the line for a launch a system dialog held, read by its words alone: on
 * 2026-11-03 the headset logged it under vrshell's own tag, `[SEO] SystemUXController`.
 */
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
