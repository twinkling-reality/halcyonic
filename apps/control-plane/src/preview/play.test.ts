import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { after, describe, test } from 'node:test';
import { type ChromeRun, DevToolsPipe } from './chrome.ts';
import {
  BROWSER_FILES,
  browserProblem,
  HostPlays,
  installBrowser,
  playArguments,
  playInput,
  playPreferences,
  playProfile,
  readPlayRecords,
} from './play.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-play-')));
after(() => rmSync(base, { recursive: true, force: true }));

describe('the contained browser a game plays in on the Mac (Play on my laptop, P-A)', () => {
  const app = '/Users/someone/.halcyonic/browser/chrome-headless-shell';

  test("its profile is the check's, reads its own browser, and refuses the microphone and camera", () => {
    const folder = join(base, 'profile-test');
    const profile = playProfile({ home: '/Users/someone', folder, port: 47930, browser: app });
    assert.match(profile, /\(deny network-outbound\)/);
    assert.match(profile, /\(allow network-outbound \(remote ip "localhost:47930"\)\)/);
    assert.ok(profile.includes(`(allow file-read* (subpath "${app}"))`));
    assert.ok(profile.includes(`(allow file-read-metadata (path-ancestors "${app}"))`));
    // The check's services, and sound out: the audio services, never the microphone's.
    assert.match(profile, /\(deny mach-lookup\)\n\(deny iokit-open\)\n/);
    assert.ok(profile.includes('(allow mach-lookup (global-name "com.apple.audio.audiohald"))'));
    // Refused last, so nothing before can reopen them.
    const lines = profile.trim().split('\n');
    assert.deepEqual(lines.slice(-2), ['(deny device-microphone)', '(deny device-camera)']);
    assert.match(profile, /\(deny lsopen\)/);
  });

  test('it runs headless, never with a DevTools port, no WebGPU, no crash reports, sound allowed to start', () => {
    const args = playArguments(join(base, 'p'), 47930);
    for (const wanted of [
      '--headless=new',
      '--no-sandbox',
      '--remote-debugging-pipe',
      '--disable-crash-reporter',
    ])
      assert.ok(args.includes(wanted), wanted);
    assert.ok(args.some((arg) => arg.startsWith('--disable-features=') && arg.includes('WebGPU')));
    assert.ok(args.includes('--autoplay-policy=no-user-gesture-required'));
    assert.ok(!args.some((arg) => arg.startsWith('--remote-debugging-port')));
    assert.ok(args.includes('--proxy-bypass-list=127.0.0.1:47930'));
    assert.ok(args.includes(`--user-data-dir=${join(base, 'p', 'profile')}`));
  });

  test('its own settings block every permission a game could ask for, and printing', () => {
    const preferences = playPreferences(join(base, 'p')) as {
      profile: { default_content_setting_values: Record<string, number> };
      printing: { enabled: boolean };
      download: { prompt_for_download: boolean; default_directory: string };
    };
    const settings = preferences.profile.default_content_setting_values;
    for (const name of [
      'notifications',
      'geolocation',
      'media_stream_mic',
      'media_stream_camera',
      'clipboard',
      'usb_guard',
      'hid_guard',
      'serial_guard',
      'bluetooth_guard',
      'midi_sysex',
      'window_placement',
      'local_fonts',
      'idle_detection',
      'keyboard_lock',
      'file_system_write_guard',
    ])
      assert.equal(settings[name], 2, name);
    assert.equal(preferences.printing.enabled, false);
    assert.equal(preferences.download.prompt_for_download, false);
    assert.equal(preferences.download.default_directory, join(base, 'p', 'downloads'));
  });

  /** A folder with every file chrome-headless-shell is made of, its binary holding `binary`. */
  const shellFolder = (path: string, binary: string) => {
    mkdirSync(path, { recursive: true });
    for (const file of BROWSER_FILES)
      writeFileSync(join(path, file), file === 'chrome-headless-shell' ? binary : file);
    return path;
  };

  test('only the recorded chrome-headless-shell runs: all of its files, and its binary hash, at every start', () => {
    const copy = shellFolder(join(base, 'shell'), 'binary');
    const right = createHash('sha256').update('binary').digest('hex');
    assert.equal(browserProblem(copy, right), null);
    assert.match(browserProblem(copy, 'f'.repeat(64)) ?? '', /changed/);
    rmSync(join(copy, 'headless_lib_data.pak'));
    assert.match(browserProblem(copy, right) ?? '', /not chrome-headless-shell/);
    assert.match(browserProblem(join(base, 'missing'), right) ?? '', /no browser/);
  });

  test('the browser is copied into the data folder, links as links, once; the source is never what runs', async () => {
    const source = shellFolder(
      join(base, 'source', 'chrome-headless-shell-mac-arm64'),
      'build one',
    );
    symlinkSync('libEGL.dylib', join(source, 'libEGL-link.dylib'));
    const sha = createHash('sha256').update('build one').digest('hex');
    const dataDir = join(base, 'data-install');
    const copy = await installBrowser(source, sha, dataDir);
    assert.equal(copy, join(dataDir, 'browser', 'current'));
    assert.equal(browserProblem(copy, sha), null);
    assert.ok(lstatSync(join(copy, 'libEGL-link.dylib')).isSymbolicLink());
    // Once there, the copy is kept as it is: a changed source is not copied over it.
    writeFileSync(join(source, 'chrome-headless-shell'), 'build two');
    assert.equal(await installBrowser(source, sha, dataDir), copy);
    assert.equal(readFileSync(join(copy, 'chrome-headless-shell'), 'utf8'), 'build one');
    // A copy that changed is replaced only by a source that hashes as recorded; otherwise none is left.
    writeFileSync(join(copy, 'chrome-headless-shell'), 'tampered');
    await assert.rejects(installBrowser(source, sha, dataDir), /changed/);
    assert.ok(!existsSync(copy));
  });
});

describe("the viewer's input: only a mouse or a key for the game's page (P-A)", () => {
  test('a mouse move, press and release, and a key down and up, become the two input calls and nothing else', () => {
    assert.deepEqual(playInput({ type: 'mouse', action: 'down', x: 10, y: 20, button: 'left' }), {
      method: 'Input.dispatchMouseEvent',
      params: { type: 'mousePressed', x: 10, y: 20, button: 'left', buttons: 1, clickCount: 1 },
    });
    assert.deepEqual(playInput({ type: 'mouse', action: 'move', x: 1, y: 2 }), {
      method: 'Input.dispatchMouseEvent',
      params: { type: 'mouseMoved', x: 1, y: 2, button: 'none', buttons: 0 },
    });
    assert.deepEqual(playInput({ type: 'key', action: 'down', key: 'a', code: 'KeyA' }), {
      method: 'Input.dispatchKeyEvent',
      params: { type: 'keyDown', key: 'a', code: 'KeyA', text: 'a' },
    });
    assert.deepEqual(
      playInput({ type: 'key', action: 'up', key: 'ArrowLeft', code: 'ArrowLeft' }),
      {
        method: 'Input.dispatchKeyEvent',
        params: { type: 'keyUp', key: 'ArrowLeft', code: 'ArrowLeft' },
      },
    );
  });

  test('anything else is refused: another method, an extra field, a position off the page, a long key', () => {
    for (const message of [
      { method: 'Runtime.evaluate', params: { expression: '1' } },
      { type: 'mouse', action: 'down', x: 10, y: 20, button: 'left', extra: true },
      { type: 'mouse', action: 'down', x: -1, y: 20, button: 'left' },
      { type: 'mouse', action: 'down', x: 10, y: 99999, button: 'left' },
      { type: 'mouse', action: 'down', x: 10, y: 20, button: 'back' },
      { type: 'key', action: 'down', key: 'a'.repeat(40), code: 'KeyA' },
      { type: 'key', action: 'press', key: 'a', code: 'KeyA' },
      { type: 'touch', action: 'down', x: 1, y: 1 },
      'mouse',
      null,
    ])
      assert.equal(playInput(message), null, JSON.stringify(message));
  });
});

describe('a play left by a crash is found and stopped at start (P-A)', () => {
  test('records are read by project, and only those whose pid and folder still match are said to run', () => {
    const data = join(base, 'data');
    mkdirSync(join(data, 'play'), { recursive: true });
    writeFileSync(
      join(data, 'play', 'p1.json'),
      JSON.stringify({ pid: 4242, check_id: 'c', folder: '/x/p1' }),
    );
    writeFileSync(join(data, 'play', 'p2.json'), 'not json');
    const records = readPlayRecords(data);
    assert.deepEqual(records, [{ project: 'p1', pid: 4242, check_id: 'c', folder: '/x/p1' }]);
  });
});

describe('the host runs, streams, feeds and stops a play (P-A)', () => {
  /** A Chrome that answers every command, records what it was sent, and sends frames when asked. */
  const fakeChrome = (pid: number) => {
    const toChrome = new PassThrough();
    const fromChrome = new PassThrough();
    const sent: { method: string; params: Record<string, unknown> }[] = [];
    let stopped = 0;
    toChrome.setEncoding('utf8');
    toChrome.on('data', (chunk: string) => {
      for (const text of chunk.split('\0').filter((part) => part !== '')) {
        const { id, method, params } = JSON.parse(text) as {
          id: number;
          method: string;
          params: Record<string, unknown>;
        };
        sent.push({ method, params });
        fromChrome.write(`${JSON.stringify({ id, result: {} })}\0`);
      }
    });
    const frame = (data: string) =>
      fromChrome.write(
        `${JSON.stringify({ method: 'Page.screencastFrame', sessionId: 'page', params: { data, sessionId: 7, metadata: {} } })}\0`,
      );
    const run: ChromeRun = {
      pid,
      devtools: new DevToolsPipe(toChrome, fromChrome),
      session: 'page',
      stop: async () => {
        stopped += 1;
      },
    };
    return { run, sent, frame, stopped: () => stopped };
  };
  const host = (overrides: Partial<ConstructorParameters<typeof HostPlays>[0]> = {}) => {
    const dataDir = mkdtempSync(join(base, 'host-'));
    const chrome = fakeChrome(4242);
    const launched: Record<string, unknown>[] = [];
    const ended: [string, string, string][] = [];
    const plays = new HostPlays({
      dataDir,
      home: '/Users/someone',
      browser: { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
      browserProblem: () => null,
      launcher: () => '/fake/bin/halcyonic-disclaim',
      launch: async (options) => {
        launched.push(options as unknown as Record<string, unknown>);
        return chrome.run;
      },
      onEnded: (project, check, reason) => ended.push([project, check, reason]),
      viewerWaitMs: 60_000,
      viewerGraceMs: 20,
      audible: async () => false,
      ...overrides,
    });
    return { plays, dataDir, chrome, launched, ended };
  };

  test('it starts the browser through the launcher, and without one starts nothing', async () => {
    const { plays, launched } = host();
    await plays.start('p1', 'c1', 47930);
    assert.equal(launched[0]?.launcher, '/fake/bin/halcyonic-disclaim');
    assert.equal(launched[0]?.chrome, '/fake/chrome-headless-shell/chrome-headless-shell');
    await plays.close();
    const without = host({ launcher: () => null });
    await assert.rejects(without.plays.start('p1', 'c1', 47930), /launcher/);
    assert.equal(without.launched.length, 0);
  });

  test('a browser that fails its check starts nothing', async () => {
    const { plays, launched } = host({
      browserProblem: () => 'The browser has changed since it was set up.',
    });
    await assert.rejects(plays.start('p1', 'c1', 47930), /changed/);
    assert.equal(launched.length, 0);
  });

  test("it writes the play profile's settings and its record, opens the preview's page, and streams its frames", async () => {
    const { plays, dataDir, chrome, launched } = host();
    await plays.start('p1', 'c1', 47930);
    const folder = join(dataDir, 'play', 'p1');
    const preferences = JSON.parse(
      readFileSync(join(folder, 'profile', 'Default', 'Preferences'), 'utf8'),
    ) as {
      profile: { default_content_setting_values: Record<string, number> };
    };
    assert.equal(preferences.profile.default_content_setting_values.media_stream_mic, 2);
    assert.deepEqual(JSON.parse(readFileSync(join(dataDir, 'play', 'p1.json'), 'utf8')), {
      pid: 4242,
      check_id: 'c1',
      folder,
    });
    assert.match(String(launched[0]?.profileText), /\(deny device-microphone\)/);
    assert.ok(([launched[0]?.args].flat() as unknown[]).includes('--headless=new'));
    assert.ok(
      chrome.sent.some(
        (m) => m.method === 'Page.navigate' && m.params.url === 'http://127.0.0.1:47930/',
      ),
    );
    assert.ok(chrome.sent.some((m) => m.method === 'Page.startScreencast'));
    // Before the page opens, the browser refuses the preview's origin every permission a game could
    // ask for, the microphone and camera first, whatever its own settings say (S1).
    const denied = chrome.sent.filter((m) => m.method === 'Browser.setPermission');
    for (const m of denied) {
      assert.equal(m.params.setting, 'denied');
      assert.equal(m.params.origin, 'http://127.0.0.1:47930');
    }
    const names = denied.map((m) => (m.params.permission as { name: string }).name);
    assert.deepEqual(names.slice(0, 2), ['microphone', 'camera']);
    assert.ok(names.includes('geolocation') && names.includes('notifications'));
    const navigate = chrome.sent.findIndex((m) => m.method === 'Page.navigate');
    const lastDenied = chrome.sent.findLastIndex((m) => m.method === 'Browser.setPermission');
    assert.ok(lastDenied !== -1 && lastDenied < navigate, 'refused before the page opens');
    const frames: string[] = [];
    const off = plays.watch('p1', (frame) => frames.push(frame));
    chrome.frame('ZnJhbWU=');
    await new Promise((resolve) => setTimeout(resolve, 20));
    assert.deepEqual(frames, ['ZnJhbWU=']);
    assert.ok(chrome.sent.some((m) => m.method === 'Page.screencastFrameAck'));
    off();
    await plays.close();
  });

  test("its viewer, on 127.0.0.1, carries the browser's frames out and a key in, and the play ends when the viewer closes", async () => {
    const { plays, chrome, ended } = host();
    const { link } = await plays.start('p1', 'c1', 47930);
    const url = new URL(link);
    assert.equal(url.hostname, '127.0.0.1');
    const port = Number(url.port);
    assert.ok(port >= 47940 && port <= 47949, link);
    const signedIn = await fetch(link, {
      redirect: 'manual',
      headers: { 'sec-fetch-site': 'none' },
    });
    assert.equal(signedIn.status, 303);
    const cookie = (signedIn.headers.get('set-cookie') ?? '').split(';')[0] ?? '';
    const socket = new WebSocket(`ws://127.0.0.1:${port}/stream`, {
      headers: { cookie, origin: `http://127.0.0.1:${port}` },
    } as never);
    socket.binaryType = 'arraybuffer';
    const received: string[] = [];
    socket.addEventListener('message', (event) =>
      received.push(Buffer.from(event.data as ArrayBuffer).toString()),
    );
    await new Promise((resolve, reject) => {
      socket.addEventListener('open', resolve, { once: true });
      socket.addEventListener('error', reject, { once: true });
    });
    await new Promise((resolve) => setTimeout(resolve, 50));
    chrome.frame(Buffer.from('jpeg').toString('base64'));
    socket.send(JSON.stringify({ type: 'key', action: 'down', key: 'w', code: 'KeyW' }));
    socket.send(JSON.stringify({ method: 'Runtime.evaluate', params: { expression: '1' } }));
    await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(received, ['jpeg']);
    const keys = chrome.sent.filter((m) => m.method === 'Input.dispatchKeyEvent');
    assert.equal(keys.length, 1);
    assert.equal(keys[0]?.params.code, 'KeyW');
    assert.ok(!chrome.sent.some((m) => m.method === 'Runtime.evaluate'));
    socket.close();
    for (let i = 0; i < 100 && ended.length === 0; i += 1)
      await new Promise((resolve) => setTimeout(resolve, 20));
    assert.deepEqual(ended, [['p1', 'c1', 'closed']]);
    // The viewer closed with the play.
    await assert.rejects(fetch(`http://127.0.0.1:${port}/`));
  });

  test('only a mouse or a key reaches the page; anything else sends nothing', async () => {
    const { plays, chrome } = host();
    await plays.start('p1', 'c1', 47930);
    const before = chrome.sent.length;
    assert.equal(
      await plays.input('p1', { type: 'mouse', action: 'down', x: 5, y: 5, button: 'left' }),
      true,
    );
    assert.equal(
      await plays.input('p1', {
        method: 'Runtime.evaluate',
        params: { expression: 'document.cookie' },
      }),
      false,
    );
    assert.equal(
      await plays.input('p2', { type: 'key', action: 'down', key: 'a', code: 'KeyA' }),
      false,
    );
    const after = chrome.sent.slice(before).map((m) => m.method);
    assert.deepEqual(after, ['Input.dispatchMouseEvent']);
    await plays.close();
  });

  test('it stops when asked, when no viewer comes in time, and when the host closes, each saying why, its record gone', async () => {
    const asked = host();
    await asked.plays.start('p1', 'c1', 47930);
    await asked.plays.stop('p1', 'preview_stopped');
    assert.equal(asked.chrome.stopped(), 1);
    assert.equal(existsSync(join(asked.dataDir, 'play', 'p1.json')), false);
    assert.deepEqual(asked.ended, [['p1', 'c1', 'preview_stopped']]);
    await asked.plays.stop('p1', 'closed');
    assert.equal(asked.ended.length, 1, 'stopping what does not run says nothing');

    const unwatched = host({ viewerWaitMs: 50 });
    await unwatched.plays.start('p1', 'c1', 47930);
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.deepEqual(unwatched.ended, [['p1', 'c1', 'closed']]);

    const watched = host({ viewerWaitMs: 50 });
    await watched.plays.start('p1', 'c1', 47930);
    const off = watched.plays.watch('p1', () => undefined);
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.deepEqual(watched.ended, [], 'a viewer came');
    off();
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.deepEqual(watched.ended, [['p1', 'c1', 'closed']], 'the viewer left');

    const closing = host();
    await closing.plays.start('p1', 'c1', 47930);
    await closing.plays.close();
    assert.deepEqual(closing.ended, [['p1', 'c1', 'host_stopped']]);
  });

  test('a viewer that drops and comes back within the grace keeps the game, as a reload does; past the grace the game ends', async () => {
    const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
    const { plays, ended } = host({ viewerGraceMs: 300 });
    await plays.start('p1', 'c1', 47930);
    const first = plays.watch('p1', () => undefined);
    first();
    await sleep(100);
    const second = plays.watch('p1', () => undefined);
    await sleep(400);
    assert.deepEqual(ended, [], 'the viewer came back within the grace');
    second();
    await sleep(100);
    assert.deepEqual(ended, [], 'still within the grace');
    await sleep(400);
    assert.deepEqual(ended, [['p1', 'c1', 'closed']]);
  });

  test('a drag: moves while a button is held carry it, and stop carrying it once it is released', async () => {
    const { plays, chrome } = host();
    await plays.start('p1', 'c1', 47930);
    const off = plays.watch('p1', () => undefined);
    await plays.input('p1', { type: 'mouse', action: 'down', x: 10, y: 10, button: 'left' });
    await plays.input('p1', { type: 'mouse', action: 'move', x: 20, y: 10 });
    await plays.input('p1', { type: 'mouse', action: 'up', x: 20, y: 10, button: 'left' });
    await plays.input('p1', { type: 'mouse', action: 'move', x: 30, y: 10 });
    const moves = chrome.sent
      .filter((m) => m.method === 'Input.dispatchMouseEvent' && m.params.type === 'mouseMoved')
      .map((m) => [m.params.button, m.params.buttons]);
    assert.deepEqual(moves, [
      ['left', 1],
      ['none', 0],
    ]);
    off();
    await plays.close();
  });

  test("the game's sound is said as the computer shows it: none after a while, playing, then paused; unknown says nothing", async () => {
    const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
    let heard: boolean | null = false;
    const { plays } = host({
      audible: async () => heard,
      soundPollMs: 20,
      soundSilentMs: 150,
    });
    await plays.start('p1', 'c1', 47930);
    const off = plays.watch('p1', () => undefined);
    assert.equal(plays.sound('p1'), undefined, 'too soon to say');
    await sleep(300);
    assert.equal(plays.sound('p1'), 'none');
    heard = true;
    await sleep(100);
    assert.equal(plays.sound('p1'), 'playing');
    heard = false;
    await sleep(100);
    assert.equal(plays.sound('p1'), 'quiet');
    off();
    await plays.close();

    const unknown = host({ audible: async () => null, soundPollMs: 20, soundSilentMs: 50 });
    await unknown.plays.start('p1', 'c1', 47930);
    const offUnknown = unknown.plays.watch('p1', () => undefined);
    await sleep(200);
    assert.equal(
      unknown.plays.sound('p1'),
      undefined,
      'never "No Sound" when the computer could not say',
    );
    offUnknown();
    await unknown.plays.close();
  });

  test('a viewer is told why its game ended before its stream closes', async () => {
    const { plays } = host();
    const { link } = await plays.start('p1', 'c1', 47930);
    const port = Number(new URL(link).port);
    const signedIn = await fetch(link, {
      redirect: 'manual',
      headers: { 'sec-fetch-site': 'none' },
    });
    const cookie = (signedIn.headers.get('set-cookie') ?? '').split(';')[0] ?? '';
    const socket = new WebSocket(`ws://127.0.0.1:${port}/stream`, {
      headers: { cookie, origin: `http://127.0.0.1:${port}` },
    } as never);
    const texts: string[] = [];
    socket.addEventListener('message', (event) => {
      if (typeof event.data === 'string') texts.push(event.data);
    });
    const closed = new Promise((resolve) => socket.addEventListener('close', resolve));
    await new Promise((resolve, reject) => {
      socket.addEventListener('open', resolve, { once: true });
      socket.addEventListener('error', reject, { once: true });
    });
    await plays.stop('p1', 'preview_stopped');
    await closed;
    assert.deepEqual(
      texts.map((text) => JSON.parse(text)).filter((m) => m.type === 'ended'),
      [{ type: 'ended', reason: 'preview_stopped' }],
    );
  });

  test('at start, a play a crash left is stopped only when its pid still runs a command naming its folder', async () => {
    const dataDir = mkdtempSync(join(base, 'sweep-'));
    mkdirSync(join(dataDir, 'play'), { recursive: true });
    const folder = join(dataDir, 'play', 'p1');
    const left = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)', folder], {
      stdio: 'ignore',
    });
    const other = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)', '/elsewhere'], {
      stdio: 'ignore',
    });
    after(() => {
      left.kill('SIGKILL');
      other.kill('SIGKILL');
    });
    writeFileSync(
      join(dataDir, 'play', 'p1.json'),
      JSON.stringify({ pid: left.pid, check_id: 'c1', folder }),
    );
    writeFileSync(
      join(dataDir, 'play', 'p2.json'),
      JSON.stringify({ pid: other.pid, check_id: 'c2', folder: join(dataDir, 'play', 'p2') }),
    );
    const leftExit = new Promise((resolve) => left.once('exit', resolve));
    const plays = new HostPlays({
      dataDir,
      home: '/Users/someone',
      browser: null,
      browserProblem: () => null,
      launcher: () => null,
      onEnded: () => undefined,
    });
    assert.equal(await plays.sweep(), 1);
    await leftExit;
    assert.equal(other.exitCode, null, 'a process not naming its folder is left alone');
    assert.deepEqual(readPlayRecords(dataDir), []);
  });
});
