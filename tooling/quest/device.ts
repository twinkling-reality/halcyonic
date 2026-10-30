/**
 * Checks a Quest without anyone wearing it (HEADSET_SESSION.md, "Checking the headset without
 * wearing it"): what it shows, whether it is awake, installing an APK with the installed one saved
 * first, launching and stopping, a frame of what it shows, a recording, and Halcyonic's own log
 * lines. Every command goes to the headset's adb server through adb.ts.
 *
 *   pnpm quest:device -- status
 *   pnpm quest:device -- awake                       keeps it awake on the desk; give-back undoes it
 *   pnpm quest:device -- give-back                   the sensor back, stops what this tool started
 *   pnpm quest:device -- save [--package <p>]        pulls the installed APK and checks its bytes
 *   pnpm quest:device -- install <apk> --saved <saved apk> [--package <p>]
 *   pnpm quest:device -- restore <saved apk> [--package <p>]
 *   pnpm quest:device -- install <check apk> --package com.halcyonic.xr.check   beside the owner's app
 *   pnpm quest:device -- grant <permission> --package com.halcyonic.xr.check
 *   pnpm quest:device -- uninstall --package com.halcyonic.xr.check             the check build only
 *   pnpm quest:device -- launch [--package <p>]      fails when a system dialog holds the launch
 *     [--extra <key>=<text>] [--extra-bool <key>=true|false]   intent extras, each repeatable
 *   pnpm quest:device -- stop [--package <p>]
 *   pnpm quest:device -- shot [--out <dir>]          a frame of both eyes, through screenrecord
 *   pnpm quest:device -- shot --service --sync-is-off    Meta's capture service, one eye
 *   pnpm quest:device -- record --seconds <n> --sync-is-off [--out <dir>]
 *   pnpm quest:device -- record start --sync-is-off, later record stop [--out <dir>]
 *     a recording through Meta's capture service, pulled into --out with <clip>.sha256 beside it
 *   pnpm quest:device -- logs [--out <file>]         Halcyonic's own lines, its process only
 *   pnpm quest:device -- preview <project id>        the project's checked copy in Quest Browser
 *   pnpm quest:device -- preview stop                the reverse removed, the browser stopped if started
 *
 * `preview` opens only what preview.ts allows: the host's own server of the project's latest
 * check, passed, with every agent's reads confined to the project (`reads_confined`), at
 * `http://127.0.0.1:<port>/` over an `adb reverse` of that port. It refuses while anything on the
 * headset listens on 8720 (Meta XR Operator), when that cannot be read, when a system dialog is in
 * front, or when the headset already has someone else's reverse of the port. The reverse is made
 * with --no-rebind and recorded before it is made, so `preview stop` and `give-back` remove it even
 * after a cut-off run; Quest Browser is stopped only when this tool started it. The check holds
 * when the page opens, not after. No browser log, no DevTools.
 *
 * A check build (QuestBuild's test application id, `com.halcyonic.xr.check`) installs beside the
 * owner's app with data of its own, so it needs no saved copy, and it is the only package `grant`
 * and `uninstall` act on: `grant` gives it one of the permissions in GRANTABLE.
 *
 * Besides what adb.ts lists, it runs `input keyevent KEYCODE_WAKEUP` (no other input), the
 * vrpowermanager `prox_close` and `automation_disable` broadcasts, the testing service's
 * GET_PROPERTY, `pm path`, `sha256sum`, `pull`, `install -r`, `cmd package resolve-activity`,
 * `screenrecord` into /sdcard/Download, Meta's capture service, `ls` of the capture folders and
 * `rm` of the files it made, by name, after their pulled copy's sha256 matched. It never
 * uninstalls, clears data, changes a setting or the boundary, or touches a system dialog.
 *
 * Meta's capture service uploads every capture to the headset's account while the headset's
 * media sync is on (validation/quest-headset-in-the-loop.md), so it runs only with
 * `--sync-is-off`, which the headset's owner confirms, and never while the headset's log says
 * sync is on; for `screenrecord` no upload was seen.
 *
 * What it changed is kept in a state file (`HALCYONIC_QUEST_STATE`, `.private/quest/state.json`,
 * which git ignores): each app it installed over and the saved copy to restore, and each app it
 * started, so `status` and `give-back` say what is still to put back.
 */
import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { promisify } from 'node:util';
import {
  ACTIVITY,
  adb,
  adbSaid,
  option,
  PACKAGE,
  requireHeadset,
  sleep,
  textOption,
} from './adb.ts';
import {
  describeFront,
  isGuardianDialog,
  isLaunchHeld,
  isMediaSyncOn,
  newCaptures,
  readBasePaths,
  readFirstInstall,
  readListedFiles,
  readSha256,
  readTestingProperties,
  readTopActivity,
  readWakefulness,
} from './headset.ts';
import { controlPlaneGate, listensOn } from './preview.ts';
import { readBattery, readThermal } from './readings.ts';

const run = promisify(execFile);
const command = process.argv[2] ?? '';
/** Options that take a value, so the value is never taken for the command's own argument. */
const VALUED = new Set([
  '--package',
  '--saved',
  '--out',
  '--seconds',
  '--extra',
  '--extra-bool',
  '--state',
]);

/** The command's own argument, such as the APK or the permission: the first word that is neither an option nor an option's value. */
const positional = process.argv
  .slice(3)
  .find((word, at, words) => !word.startsWith('--') && !VALUED.has(words[at - 1] ?? ''));
const flag = (name: string) => process.argv.includes(`--${name}`);
const packageName = textOption('package', PACKAGE);
const out = textOption('out', '.private/quest');
const STATE = textOption('state', process.env.HALCYONIC_QUEST_STATE ?? '.private/quest/state.json');
const CHECK_PACKAGE = `${PACKAGE}.check`;

/** What `grant` may give the check build: the scene, and the microphone for hold to talk. */
const GRANTABLE = [
  'com.oculus.permission.USE_SCENE',
  'horizonos.permission.USE_SCENE',
  'android.permission.RECORD_AUDIO',
];

interface State {
  /** Each app installed over, with the saved copy that puts the owner's back. */
  installs: Record<string, { saved: string; savedSha256: string; installedSha256: string }>;
  /** The apps this tool started and has not stopped. */
  started: string[];
  /** A recording `record start` began and `record stop` has not ended: the capture folders' names before it. */
  recording?: { before: string[]; startedAt: string; stopSentAt?: string };
  /** The ports `preview` reversed (or was about to) and has not removed. */
  reverses: number[];
}

function readState(): State {
  try {
    const read = JSON.parse(readFileSync(STATE, 'utf8')) as Partial<State>;
    return {
      installs: read.installs ?? {},
      started: read.started ?? [],
      ...(read.recording ? { recording: read.recording } : {}),
      reverses: read.reverses ?? [],
    };
  } catch {
    return { installs: {}, started: [], reverses: [] };
  }
}

function writeState(change: (state: State) => void): void {
  const state = readState();
  change(state);
  mkdirSync(dirname(STATE), { recursive: true });
  writeFileSync(STATE, `${JSON.stringify(state, null, 2)}\n`);
}

function replaced(): void {
  for (const [name, install] of Object.entries(readState().installs)) {
    console.log(
      `The owner's ${name} is replaced; restore with: pnpm quest:device -- restore ${install.saved} --package ${name}`,
    );
  }
}

const SCREENSHOTS = '/sdcard/Oculus/Screenshots';
const VIDEOS = '/sdcard/Oculus/VideoShots';
const CLIP_WAIT_MS = 120_000;
const DOWNLOADS = '/sdcard/Download';
const CAPTURE_SERVICE = 'com.oculus.metacam/.capture.CaptureService';
const TESTING = ['content', 'call', '--uri', 'content://com.oculus.rc', '--method', 'GET_PROPERTY'];

function fail(line: string): never {
  console.error(line);
  process.exit(1);
}

const sha256File = (path: string) => createHash('sha256').update(readFileSync(path)).digest('hex');

const AAPT2 =
  process.env.HALCYONIC_AAPT2 ??
  '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/build-tools/36.0.0/aapt2';

/** The package an APK on the Mac declares, read with aapt2 before anything reaches the headset; null when unreadable. */
async function apkPackage(path: string): Promise<string | null> {
  try {
    const { stdout } = await run(AAPT2, ['dump', 'badging', path]);
    return /^package: name='([^']+)'/m.exec(stdout)?.[1] ?? null;
  } catch {
    return null;
  }
}

/** Stops unless the APK on the Mac declares exactly `name`. */
async function requirePackage(path: string, name: string): Promise<void> {
  const declared = await apkPackage(path);
  if (declared !== name) {
    fail(
      `${basename(path)} is ${declared ?? 'not an APK aapt2 can read'}, not ${name}; nothing was installed.`,
    );
  }
}

async function front() {
  return readTopActivity(await adb('shell', 'dumpsys', 'activity', 'activities'));
}

async function refuseSystemDialog(doing: string): Promise<void> {
  const shown = await front();
  if (isGuardianDialog(shown) || shown?.package === 'com.oculus.guardian') {
    fail(`Not ${doing}: ${describeFront(shown, packageName)}`);
  }
}

async function installed(name: string) {
  const paths = readBasePaths(await adbSaid('shell', 'pm', 'path', name));
  if (paths.length !== 1 || !paths[0]?.endsWith('/base.apk')) return null;
  const sum = readSha256(await adbSaid('shell', 'sha256sum', paths[0]));
  const first = readFirstInstall(await adbSaid('shell', 'dumpsys', 'package', name));
  return sum === null ? null : { path: paths[0], sha256: sum, firstInstall: first };
}

/** Pulls a file the tool made, checks its bytes against the headset's sha256, and only then removes it there. */
async function collect(remote: string, folder: string): Promise<string> {
  mkdirSync(folder, { recursive: true });
  const local = join(folder, basename(remote));
  const onDevice = readSha256(await adbSaid('shell', 'sha256sum', remote));
  await adb('pull', remote, local);
  if (onDevice === null || sha256File(local) !== onDevice) {
    throw new Error(
      `The pulled copy of ${basename(remote)} does not match the headset's; it was left there.`,
    );
  }
  await adb('shell', 'rm', remote);
  return local;
}

async function captureFolders(): Promise<string[]> {
  return readListedFiles(await adbSaid('shell', 'ls', SCREENSHOTS, VIDEOS));
}

async function requireSyncOff(): Promise<void> {
  if (!flag('sync-is-off')) {
    fail(
      "Meta's capture service uploads captures to the headset's account while its media sync is on. Run with --sync-is-off once the owner has turned sync off, or use `shot` without --service.",
    );
  }
  if ((await syncPreference()) === true) {
    fail("The headset's log says its media sync is on, so the capture service is not used.");
  }
}

/** The capture service's latest word on media sync: true, false, or null when its log holds none. */
async function syncPreference(): Promise<boolean | null> {
  const lines = (await adbSaid('logcat', '-d', '-s', 'SimpleMediaSyncUtils:I'))
    .split(/\r?\n/)
    .filter((line) => /media sync preference is enabled: (true|false)/.test(line));
  const latest = lines.at(-1);
  return latest === undefined ? null : isMediaSyncOn(latest);
}

async function warnIfSyncOn(): Promise<void> {
  if ((await syncPreference()) === true) {
    console.error(
      'Warning: the headset says media sync is on, so this capture was uploaded to its account.',
    );
  }
}

/** The proximity override as GET_PROPERTY reads it, after the broadcast has had time to land. */
async function proximityOverride(): Promise<boolean | undefined> {
  const given = process.env.HALCYONIC_QUEST_SETTLE_MS ?? '';
  await sleep(/^\d+$/.test(given) ? Number(given) : 5000);
  return readTestingProperties(await adbSaid('shell', ...TESTING)).set_proximity_close;
}

/**
 * Gives the proximity sensor back and reads the override twice. On the headset one
 * `automation_disable` once read off and later on again, with nothing else sent in between, and a
 * second one ended it (validation/quest-headset-in-the-loop.md), so it is sent again while either
 * reading says on.
 */
async function giveSensorBack(): Promise<boolean | undefined> {
  let on: boolean | undefined;
  for (let attempt = 0; attempt < 2; attempt++) {
    await adb('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.automation_disable');
    const first = await proximityOverride();
    const second = await proximityOverride();
    on = first === false && second === false ? false : (second ?? first);
    if (on === false) break;
  }
  return on;
}

function stamp(): string {
  const now = new Date();
  const two = (n: number) => String(n).padStart(2, '0');
  return `${now.getUTCFullYear()}${two(now.getUTCMonth() + 1)}${two(now.getUTCDate())}-${two(now.getUTCHours())}${two(now.getUTCMinutes())}${two(now.getUTCSeconds())}`;
}

async function leftEye(video: string): Promise<string | null> {
  const ffmpeg = process.env.HALCYONIC_FFMPEG ?? '/opt/homebrew/bin/ffmpeg';
  if (!existsSync(ffmpeg)) return null;
  const frame = video.replace(/\.mp4$/, '-left.png');
  await run(ffmpeg, [
    '-v',
    'error',
    '-sseof',
    '-0.3',
    '-i',
    video,
    '-frames:v',
    '1',
    '-vf',
    'crop=iw/2:ih:0:0',
    '-y',
    frame,
  ]);
  return frame;
}

async function status(): Promise<void> {
  const shown = await front();
  const properties = readTestingProperties(await adbSaid('shell', ...TESTING));
  const battery = readBattery(await adb('shell', 'dumpsys', 'battery'));
  const thermal = readThermal(await adb('shell', 'dumpsys', 'thermalservice'));
  const running = (await adbSaid('shell', 'pidof', PACKAGE)).trim() !== '';
  console.log(`awake: ${readWakefulness(await adb('shell', 'dumpsys', 'power')) ?? 'unknown'}`);
  console.log(`front: ${describeFront(shown, PACKAGE)}`);
  console.log(
    `battery: ${battery.levelPercent ?? '?'}% ${battery.plugged ? 'charging' : 'on battery'}, ${battery.celsius ?? '?'} C`,
  );
  console.log(`thermal status: ${thermal.status ?? 'unknown'}`);
  console.log(`proximity override: ${properties.set_proximity_close ?? 'unknown'}`);
  console.log(`Halcyonic running: ${running}`);
  console.log(`reverse rules: ${(await adbSaid('reverse', '--list')).trim() || 'none'}`);
  console.log(`forward rules: ${(await adbSaid('forward', '--list')).trim() || 'none'}`);
  const recording = readState().recording;
  if (recording?.stopSentAt)
    console.log(
      `A recording may still be going: a stop was sent at ${recording.stopSentAt} but no clip appeared. Run record stop again.`,
    );
  else if (recording)
    console.log(`A recording is in progress since ${recording.startedAt}; record stop ends it.`);
  replaced();
}

async function awake(): Promise<void> {
  await adb('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close');
  await adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP');
  const on = await proximityOverride();
  console.log(
    on === true
      ? 'Awake, and kept awake on the desk until give-back.'
      : on === false
        ? 'The proximity override did not take.'
        : 'The proximity override could not be read.',
  );
  console.log(`front: ${describeFront(await front(), PACKAGE)}`);
  if (on !== true) process.exit(1);
}

async function giveBack(): Promise<void> {
  // A recording a cut-off run left going keeps the microphone on until something stops it. Its trouble is reported
  // at the end, after the apps are stopped and the sensor is given back.
  let problem: string | null = null;
  if (readState().recording) {
    console.log('A recording is still in progress: stopping and collecting it first.');
    problem = await stopRecording().catch((error: unknown) => (error as Error).message);
  }
  await removeReverses();
  for (const name of readState().started) {
    await adb('shell', 'am', 'force-stop', name);
    console.log(`Stopped ${name}, which this tool started.`);
  }
  writeState((state) => {
    state.started = [];
  });
  const on = await giveSensorBack();
  // Seen on the headset (2026-10-29): while Meta's Guardian dialog holds it, the override reads on after the
  // release until the headset sleeps, and then reads off.
  const lost = on === true && isGuardianDialog(await front());
  console.log(
    on === false
      ? "The proximity sensor is the headset's again."
      : lost
        ? "The headset can't find its position in the room, so the proximity override reads on until the headset sleeps. The release was sent."
        : on === true
          ? 'The proximity override is still on.'
          : 'The proximity override could not be read, so it may still be on.',
  );
  const reverse = (await adbSaid('reverse', '--list')).trim();
  const forward = (await adbSaid('forward', '--list')).trim();
  if (reverse) console.log(`reverse rules left: ${reverse}`);
  if (forward) console.log(`forward rules left: ${forward}`);
  replaced();
  if (problem !== null)
    fail(`${problem} A clip may still be on the headset in ${VIDEOS}; collect it by name.`);
  if (on !== false && !lost) process.exit(1);
}

async function save(): Promise<void> {
  const now = await installed(packageName);
  if (now === null)
    fail(`${packageName} is not installed as a single base.apk, so it cannot be saved.`);
  mkdirSync(out, { recursive: true });
  const copy = join(out, `${packageName}-installed-${now.sha256.slice(0, 12)}.apk`);
  await adb('pull', now.path, copy);
  if (sha256File(copy) !== now.sha256)
    fail(`The pulled copy of ${packageName} does not match the headset's.`);
  console.log(copy);
}

async function install(): Promise<void> {
  if (positional === undefined || !existsSync(positional))
    fail('install needs the APK to install.');
  await requirePackage(positional, packageName);
  if (packageName === CHECK_PACKAGE) {
    await refuseSystemDialog('installing');
    const wanted = sha256File(positional);
    const owner = await installed(PACKAGE);
    await adb('install', '-r', positional);
    const after = await installed(CHECK_PACKAGE);
    if (after?.sha256 !== wanted)
      fail(`${CHECK_PACKAGE} on the headset is not ${basename(positional)} after install -r.`);
    if ((await installed(PACKAGE))?.sha256 !== owner?.sha256)
      fail(
        `The owner's ${PACKAGE} on the headset changed during the check install; restore it from its saved copy.`,
      );
    console.log(
      `Installed ${basename(positional)} (${wanted.slice(0, 12)}) as ${CHECK_PACKAGE}, beside the owner's app.`,
    );
    return;
  }
  const saved = textOption('saved', '');
  if (saved === '' || !existsSync(saved))
    fail(
      `install over ${packageName} needs --saved <its installed APK> (pnpm quest:device -- save).`,
    );
  await refuseSystemDialog('installing');
  const before = await installed(packageName);
  if (before === null)
    fail(`${packageName} is not installed as a single base.apk, so it cannot be put back after.`);
  if (sha256File(saved) !== before.sha256) {
    fail(`${saved} is not the installed ${packageName}; nothing was installed.`);
  }
  const wanted = sha256File(positional);
  writeState((state) => {
    state.installs[packageName] ??= { saved, savedSha256: before.sha256, installedSha256: wanted };
  });
  await adb('install', '-r', positional);
  const after = await installed(packageName);
  if (after?.sha256 !== wanted)
    fail(`${packageName} on the headset is not ${basename(positional)} after install -r.`);
  console.log(`Installed ${basename(positional)} (${wanted.slice(0, 12)}) over ${packageName}.`);
  if (after.firstInstall !== before.firstInstall)
    console.log('Warning: its first install time changed.');
  replaced();
}

async function restore(): Promise<void> {
  if (positional === undefined || !existsSync(positional)) fail('restore needs the saved APK.');
  await requirePackage(positional, packageName);
  await refuseSystemDialog('restoring');
  const before = await installed(packageName);
  const wanted = sha256File(positional);
  await adb('install', '-r', positional);
  const after = await installed(packageName);
  if (after?.sha256 !== wanted)
    fail(`${packageName} on the headset is not the saved copy after install -r.`);
  console.log(`Restored ${packageName} to ${wanted.slice(0, 12)}.`);
  writeState((state) => {
    if (state.installs[packageName]?.savedSha256 === wanted) delete state.installs[packageName];
  });
  if (before !== null && after.firstInstall !== before.firstInstall)
    console.log('Warning: its first install time changed.');
}

/**
 * The intent extras `launch` was given, as `am start` options: `--extra key=text` as a string,
 * `--extra-bool key=true|false` as a boolean. `adb shell` joins its words into one command for the
 * headset's shell, so a key or value may hold only letters, digits and `._:/-`.
 */
function launchExtras(argv: readonly string[]): string[] {
  const options: string[] = [];
  for (let at = 0; at < argv.length; at++) {
    const kind = argv[at];
    if (kind !== '--extra' && kind !== '--extra-bool') continue;
    const match = /^([A-Za-z0-9._]+)=([A-Za-z0-9._:/-]+)$/.exec(argv[at + 1] ?? '');
    const key = match?.[1];
    const value = match?.[2];
    if (key === undefined || value === undefined) {
      fail(`${kind} needs key=value, of letters, digits and ._:/- only.`);
    }
    if (kind === '--extra-bool' && value !== 'true' && value !== 'false') {
      fail(`--extra-bool ${key} needs true or false.`);
    }
    options.push(kind === '--extra' ? '--es' : '--ez', key, value);
    at++;
  }
  return options;
}

async function uninstall(): Promise<void> {
  if (packageName !== CHECK_PACKAGE) {
    fail(`uninstall acts only on the check build, ${CHECK_PACKAGE}; never on ${packageName}.`);
  }
  await adb('uninstall', CHECK_PACKAGE);
  writeState((state) => {
    state.started = state.started.filter((name) => name !== CHECK_PACKAGE);
  });
  console.log(`Uninstalled ${CHECK_PACKAGE}.`);
}

async function grant(): Promise<void> {
  if (packageName !== CHECK_PACKAGE) {
    fail(`grant acts only on the check build, ${CHECK_PACKAGE}; never on ${packageName}.`);
  }
  if (positional === undefined || !GRANTABLE.includes(positional)) {
    fail(`grant gives one of: ${GRANTABLE.join(', ')}.`);
  }
  await adb('shell', 'pm', 'grant', CHECK_PACKAGE, positional);
  console.log(`Granted ${positional} to ${CHECK_PACKAGE}.`);
}

async function launch(): Promise<void> {
  const extras = launchExtras(process.argv.slice(3));
  await refuseSystemDialog('launching');
  const activity =
    packageName === PACKAGE
      ? ACTIVITY
      : ((await adb('shell', 'cmd', 'package', 'resolve-activity', '--brief', packageName))
          .trim()
          .split(/\r?\n/)
          .pop() ?? '');
  if (!activity.startsWith(`${packageName}/`)) fail(`No launcher activity for ${packageName}.`);
  if (packageName === PACKAGE && readState().installs[PACKAGE] === undefined) {
    console.log(
      "This is the owner's Halcyonic: a launch rewrites its files/halcyonic-panel-motion.json. Never run it with a real runtime behind it.",
    );
  }
  const said = await adbSaid('shell', 'am', 'start', '-W', '-n', activity, ...extras);
  writeState((state) => {
    if (!state.started.includes(packageName)) state.started.push(packageName);
  });
  await sleep(3000);
  const shown = await front();
  const held = isLaunchHeld(
    await adbSaid('logcat', '-d', '-t', '200', '-s', 'ActivityTaskManager:I'),
  );
  if (held || isGuardianDialog(shown)) {
    await adb('shell', 'am', 'force-stop', packageName);
    fail(
      `The launch was held and ${packageName} was stopped. ${describeFront(shown, packageName)}`,
    );
  }
  console.log(
    /Status: ok/.test(said)
      ? `Started ${packageName}.`
      : `am start did not say ok for ${packageName}.`,
  );
  console.log(`front: ${describeFront(shown, packageName)}`);
}

async function stop(): Promise<void> {
  await adb('shell', 'am', 'force-stop', packageName);
  const left = (await adbSaid('shell', 'pidof', packageName)).trim() !== '';
  if (!left) {
    writeState((state) => {
      state.started = state.started.filter((name) => name !== packageName);
    });
  }
  console.log(left ? `${packageName} is still running.` : `${packageName} stopped.`);
  if (left) process.exit(1);
}

async function shot(): Promise<void> {
  if (flag('service')) {
    await requireSyncOff();
    const before = await captureFolders();
    await adb('shell', 'am', 'startservice', '-n', CAPTURE_SERVICE, '-a', 'TAKE_SCREENSHOT');
    let made: string[] = [];
    for (let i = 0; i < 10 && made.length === 0; i++) {
      await sleep(500);
      made = newCaptures(before, await captureFolders());
    }
    if (made.length !== 1 || made[0] === undefined)
      fail('The capture service made no single new screenshot.');
    console.log(await collect(`${SCREENSHOTS}/${made[0]}`, out));
    await warnIfSyncOn();
    return;
  }
  const remote = `${DOWNLOADS}/halcyonic.device-${stamp()}.mp4`;
  if (!/No such file/.test(await adbSaid('shell', 'ls', remote)))
    fail(`${remote} already exists on the headset.`);
  await adb('shell', 'screenrecord', '--time-limit', '1', remote);
  const local = await collect(remote, out);
  const frame = await leftEye(local);
  console.log(frame ?? local);
}

/** Where the clip and its <clip>.sha256 go: --out, else .private/quest. ffprobe's summary of it, when ffprobe is there. */
async function keepClip(remote: string): Promise<string> {
  const local = await collect(remote, out);
  writeFileSync(`${local}.sha256`, `${sha256File(local)}  ${basename(local)}\n`);
  const ffprobe = process.env.HALCYONIC_FFPROBE ?? '/opt/homebrew/bin/ffprobe';
  if (existsSync(ffprobe)) {
    const { stdout } = await run(ffprobe, [
      '-v',
      'error',
      '-show_entries',
      'format=duration:stream=codec_type,width,height,avg_frame_rate,sample_rate,channels',
      '-of',
      'compact',
      local,
    ]);
    console.log(stdout.trim());
  }
  return local;
}

async function recordStart(): Promise<void> {
  await requireSyncOff();
  if (readState().recording) fail('A recording is already in progress: record stop ends it.');
  console.log('Recording includes the headset microphone, mixed with the app sound.');
  const before = await captureFolders();
  await adb(
    'shell',
    'am',
    'startservice',
    '-n',
    CAPTURE_SERVICE,
    '-a',
    'START_INTERNAL_CAPTURE_TO_DISK',
  );
  writeState((state) => {
    state.recording = { before, startedAt: new Date().toISOString() };
  });
  console.log('Recording.');
}

/**
 * Stops the recording `record start` began and collects it: what went wrong, or null when the clip is in --out with
 * its sha256 and the headset said media sync is off. Never exits, so give-back can finish its own work after it.
 */
async function stopRecording(): Promise<string | null> {
  const recording = readState().recording;
  if (!recording) return 'No recording is in progress.';
  await adb(
    'shell',
    'am',
    'startservice',
    '-n',
    CAPTURE_SERVICE,
    '-a',
    'STOP_INTERNAL_CAPTURE_TO_DISK',
  );
  // A 200 s clip of 159 MB appeared within 10 s of its stop; the wait leaves room for longer clips.
  const given = process.env.HALCYONIC_QUEST_CLIP_WAIT_MS ?? '';
  const wait = /^\d+$/.test(given) ? Number(given) : CLIP_WAIT_MS;
  const until = Date.now() + wait;
  let made: string[] = [];
  do {
    await sleep(500);
    made = newCaptures(recording.before, await captureFolders()).filter((name) =>
      name.endsWith('.mp4'),
    );
  } while (made.length === 0 && Date.now() < until);
  if (made.length === 0) {
    // No clip means the stop may not have taken: the recording, and the microphone, may still be going.
    writeState((state) => {
      if (state.recording) state.recording.stopSentAt = new Date().toISOString();
    });
    return `No clip appeared within ${Math.ceil(wait / 1000)} s of the stop, so the recording may still be going. Run record stop again.`;
  }
  writeState((state) => {
    delete state.recording;
  });
  if (made.length !== 1 || made[0] === undefined)
    return 'The capture service made no single new recording.';
  try {
    console.log(await keepClip(`${VIDEOS}/${made[0]}`));
  } catch (error) {
    return (error as Error).message;
  }
  if ((await syncPreference()) === true) {
    return "The headset's media sync is on: this recording was uploaded to its account. Tell the owner.";
  }
  return null;
}

async function recordStop(): Promise<void> {
  const problem = await stopRecording();
  if (problem !== null) fail(problem);
}

async function record(): Promise<void> {
  if (positional === 'start') return recordStart();
  if (positional === 'stop') return recordStop();
  if (positional !== undefined) fail('record takes start, stop, or --seconds <n>.');
  const seconds = option('seconds', 10);
  await recordStart();
  await sleep(seconds * 1000);
  await recordStop();
}

/** Halcyonic's own lines, its OpenXR session's states and the compositor's frame line: its process only. */
const KEPT = /( Unity\s*: Halcyonic|OpenXR_SessionImpl: PostSessionStateChange| VrApi\s*: FPS=)/;

async function logs(): Promise<void> {
  const pid = (await adbSaid('shell', 'pidof', PACKAGE)).trim().split(/\s+/)[0] ?? '';
  if (!/^\d+$/.test(pid)) fail('Halcyonic is not running.');
  const lines = (await adbSaid('logcat', '-d', '-v', 'threadtime', `--pid=${pid}`))
    .split(/\r?\n/)
    .filter((line) => KEPT.test(line));
  const file = textOption('out', '');
  if (file === '') {
    for (const line of lines) console.log(line);
  } else {
    writeFileSync(file, `${lines.join('\n')}\n`);
    console.log(`${lines.length} lines in ${file}`);
  }
}

const BROWSER = 'com.oculus.browser';

/** Removes every reverse `preview` recorded, and forgets each one the headset no longer lists. */
/** The headset's reverse rules, one line each; throws when `reverse --list` fails. */
async function reverseList(): Promise<string[]> {
  return (await adb('reverse', '--list')).split(/\r?\n/).filter((line) => line.trim() !== '');
}

/** Whether a rule reverses the headset's own `port`, whoever made it and wherever it leads on the Mac. */
function reversesPort(rules: readonly string[], port: number): boolean {
  return rules.some((line) => line.trim().split(/\s+/)[1] === `tcp:${port}`);
}

async function removeReverses(): Promise<void> {
  for (const port of readState().reverses) {
    await adbSaid('reverse', '--remove', `tcp:${port}`);
    let rules: string[];
    try {
      rules = await reverseList();
    } catch {
      // Forgotten only once a list that was read shows it gone.
      console.log(
        `reverse --list could not be read, so the reverse of port ${port} stays recorded.`,
      );
      continue;
    }
    if (reversesPort(rules, port)) {
      console.log(`The reverse of port ${port} is still there.`);
      continue;
    }
    writeState((state) => {
      state.reverses = state.reverses.filter((kept) => kept !== port);
    });
    console.log(`Removed the reverse of port ${port}.`);
  }
}

async function preview(): Promise<void> {
  if (positional === 'stop') return previewStop();
  if (positional === undefined) fail('Say which project: preview <project id>, or preview stop.');
  const gate = await controlPlaneGate(positional);
  if (!gate.ok) fail(`Not opening the preview: ${gate.reason}`);
  // Fails closed: a listener on 8720 is ruled out only by both tables, read whole.
  const sockets = await adb('shell', 'cat', '/proc/net/tcp', '/proc/net/tcp6').catch(() => '');
  if ((sockets.match(/^\s*sl\s+local_address/gm) ?? []).length < 2) {
    fail(
      "Not opening the preview: the headset's /proc/net/tcp and tcp6 could not be read, so a listener on 8720 (Meta XR Operator) is not ruled out.",
    );
  }
  if (listensOn(sockets, 8720)) {
    fail('Not opening the preview: something on the headset listens on 8720 (Meta XR Operator).');
  }
  await refuseSystemDialog('opening the preview');
  const rules = await reverseList().catch(() =>
    fail("Not opening the preview: the headset's reverse rules could not be read."),
  );
  const ours = readState().reverses.includes(gate.port);
  if (reversesPort(rules, gate.port) && !ours) {
    fail(
      `Not opening the preview: the headset already has a reverse on port ${gate.port} that this tool did not make.`,
    );
  }
  const ran = (await adbSaid('shell', 'pidof', BROWSER)).trim() !== '';
  if (!reversesPort(rules, gate.port)) {
    // Recorded before it is made, so a cut-off run still removes it; never made over anyone's.
    writeState((state) => {
      if (!state.reverses.includes(gate.port)) state.reverses.push(gate.port);
    });
    try {
      await adb('reverse', '--no-rebind', `tcp:${gate.port}`, `tcp:${gate.port}`);
    } catch {
      if (!ours) {
        writeState((state) => {
          state.reverses = state.reverses.filter((kept) => kept !== gate.port);
        });
      }
      fail(`Not opening the preview: the reverse of port ${gate.port} could not be made.`);
    }
  }
  const said = await adbSaid(
    'shell',
    'am',
    'start',
    '-W',
    '-a',
    'android.intent.action.VIEW',
    '-d',
    gate.url,
    BROWSER,
  );
  if (!ran) {
    writeState((state) => {
      if (!state.started.includes(BROWSER)) state.started.push(BROWSER);
    });
  }
  console.log(
    /Status: ok/.test(said)
      ? `Opened ${gate.url} (check ${gate.checkId}) in Quest Browser.`
      : `am start did not say ok for ${gate.url}.`,
  );
  console.log(`front: ${describeFront(await front(), BROWSER)}`);
}

async function previewStop(): Promise<void> {
  await removeReverses();
  if (readState().started.includes(BROWSER)) {
    await adb('shell', 'am', 'force-stop', BROWSER);
    writeState((state) => {
      state.started = state.started.filter((name) => name !== BROWSER);
    });
    console.log('Stopped Quest Browser, which this tool started.');
  }
  if (readState().reverses.length > 0) process.exit(1);
}

const commands: Record<string, () => Promise<void>> = {
  status,
  awake,
  'give-back': giveBack,
  save,
  install,
  uninstall,
  grant,
  restore,
  launch,
  stop,
  shot,
  record,
  logs,
  preview,
};

const chosen = commands[command];
if (chosen === undefined) fail(`Say what to do: ${Object.keys(commands).join(', ')}.`);
await requireHeadset();
try {
  await chosen();
} catch (error) {
  // adb.ts's errors name the subcommand and how it ended, never what adb printed.
  fail((error as Error).message);
}
