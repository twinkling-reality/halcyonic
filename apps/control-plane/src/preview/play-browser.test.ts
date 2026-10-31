import assert from 'node:assert/strict';
import { mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { launchChrome } from './chrome.ts';
import {
  HostPlays,
  installPlayBrowser,
  type PlayEnd,
  playArguments,
  playBinary,
  playProfile,
} from './play.ts';

/**
 * Opt-in, with the real Chrome for Testing (P-B): a game plays in the contained browser and a second
 * contained browser, standing for the person's, opens the viewer's link, as `open` would hand it over.
 */
const APP = process.env.HALCYONIC_TEST_PLAY_BROWSER ?? '';
const SHA256 = process.env.HALCYONIC_TEST_PLAY_BROWSER_SHA256 ?? '';
const SKIP =
  APP === '' || SHA256 === ''
    ? 'HALCYONIC_TEST_PLAY_BROWSER and HALCYONIC_TEST_PLAY_BROWSER_SHA256 are not set'
    : process.platform !== 'darwin' && 'Seatbelt is macOS only';

/**
 * The game: it moves every frame, asks for the microphone at once, reports keys and a drag to its
 * own server, and plays a short, quiet tone on S, never before the microphone was answered.
 */
const GAME = `<!doctype html><meta charset="utf-8"><title>Game</title>
<style>html,body{margin:0;background:#123}canvas{display:block}</style>
<canvas width="1280" height="720"></canvas>
<script>
const report = (name, value) => fetch('/report?' + name + '=' + encodeURIComponent(value));
const pen = document.querySelector('canvas').getContext('2d');
let t = 0;
(function draw() { t += 1; pen.fillStyle = 'hsl(' + (t % 360) + ' 60% 40%)'; pen.fillRect(0, 0, 1280, 720); requestAnimationFrame(draw); })();
let micAnswered = false;
const mic = navigator.mediaDevices.getUserMedia({ audio: true })
  .then((stream) => { stream.getTracks().forEach((track) => track.stop()); return 'granted'; }, (error) => error.name);
Promise.race([mic, new Promise((resolve) => setTimeout(() => resolve('no answer'), 5000))])
  .then((answer) => { micAnswered = true; report('mic', answer); });
let dragged = false;
addEventListener('pointermove', (event) => { if (event.buttons && !dragged) { dragged = true; report('drag', event.buttons); } });
addEventListener('keydown', (event) => {
  report('key', event.code);
  if (event.code !== 'KeyS' || !micAnswered) return;
  const context = new AudioContext();
  const tone = context.createOscillator();
  const gain = context.createGain();
  gain.gain.value = 0.01;
  tone.frequency.value = 440;
  tone.connect(gain).connect(context.destination);
  tone.start();
  setTimeout(() => { tone.stop(); context.close(); }, 3000);
});
</script>`;

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function until(check: () => boolean | Promise<boolean>, what: string, ms = 15_000) {
  const end = Date.now() + ms;
  while (!(await check())) {
    if (Date.now() > end) throw new Error(`timed out waiting for ${what}`);
    await sleep(100);
  }
}

describe('a game played on the Mac through its viewer, with the real browser (P-B)', {
  skip: SKIP,
}, () => {
  test('the link opens once, the microphone is refused before any sound, the viewer says the game is silent and then that it plays sound, input reaches the game, a reload keeps it, and leaving ends it', async (t: TestContext) => {
    const dataDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-play-e2e-')));
    t.after(() => rmSync(dataDir, { recursive: true, force: true }));
    const app = await installPlayBrowser(APP, SHA256, dataDir);

    const reports: string[] = [];
    const game = createServer((request, response) => {
      if (request.url?.startsWith('/report?') === true) {
        reports.push(decodeURIComponent(request.url.slice('/report?'.length)));
        response.end();
        return;
      }
      response.setHeader('content-type', 'text/html; charset=utf-8');
      response.end(GAME);
    });
    await new Promise<void>((resolve) => game.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise((resolve) => game.close(resolve)));
    const { port } = game.address() as { port: number };

    const ended: PlayEnd[] = [];
    const plays = new HostPlays({
      dataDir,
      home: homedir(),
      browser: { app, sha256: SHA256 },
      onEnded: (_project, _check, reason) => ended.push(reason),
      viewerWaitMs: 30_000,
      viewerGraceMs: 5000,
      soundPollMs: 500,
      soundSilentMs: 3000,
    });
    t.after(() => plays.close());
    const { link } = await plays.start('p1', 'c1', port);
    const viewerPort = Number(new URL(link).port);

    // The person's browser, contained as well, reaching nothing but the viewer's port.
    const folder = join(dataDir, 'person');
    const person = await launchChrome({
      chrome: playBinary(app),
      home: homedir(),
      folder,
      port: viewerPort,
      profileText: playProfile({ home: homedir(), folder, port: viewerPort, app }),
      args: playArguments(folder, viewerPort),
    });
    t.after(() => person.stop());
    const send = (method: string, params: Record<string, unknown> = {}) =>
      person.devtools.send(method, params, person.session);
    const read = async (expression: string): Promise<unknown> =>
      (
        (await send('Runtime.evaluate', { expression, returnByValue: true })) as {
          result: { value?: unknown };
        }
      ).result.value;
    const text = (id: string) => read(`document.getElementById('${id}')?.textContent ?? null`);
    const centre = async (id: string) => {
      const box = JSON.parse(
        String(
          await read(`JSON.stringify(document.getElementById('${id}').getBoundingClientRect())`),
        ),
      ) as { x: number; y: number; width: number; height: number };
      return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    };
    const mouse = (type: string, at: { x: number; y: number }, buttons: number) =>
      send('Input.dispatchMouseEvent', {
        type,
        x: at.x,
        y: at.y,
        button: type === 'mouseMoved' && buttons === 0 ? 'none' : 'left',
        buttons,
        clickCount: 1,
      });
    const key = async (key: string, code: string) => {
      await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code, text: key });
      await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code });
    };

    await send('Page.enable');
    await send('Page.navigate', { url: link });
    await until(async () => (await text('status')) === 'Ready', 'the viewer to connect');
    assert.equal(
      await read('location.href'),
      `http://127.0.0.1:${viewerPort}/`,
      'the code left the address',
    );
    assert.equal(await read('document.cookie'), '', 'the page cannot read its own cookie');
    await until(
      async () => String(await read("document.getElementById('screen').src")).startsWith('blob:'),
      'a frame of the game',
    );

    await until(
      () => reports.some((report) => report.startsWith('mic=')),
      'the game to ask for the microphone',
    );
    assert.deepEqual(
      reports.filter((report) => report.startsWith('mic=')),
      ['mic=NotAllowedError'],
      'the microphone is refused',
    );
    await until(
      async () => (await text('sound')) === 'No Sound · Silent Game',
      'the viewer to say the game is silent',
    );

    await mouse('mousePressed', await centre('start'), 1);
    await mouse('mouseReleased', await centre('start'), 0);
    await until(async () => (await text('status')) === 'Live', 'the viewer to take input');
    await key('w', 'KeyW');
    await until(() => reports.includes('key=KeyW'), 'the key to reach the game');
    const middle = await centre('screen');
    await mouse('mousePressed', middle, 1);
    await mouse('mouseMoved', { x: middle.x + 40, y: middle.y }, 1);
    await mouse('mouseReleased', { x: middle.x + 40, y: middle.y }, 0);
    await until(() => reports.includes('drag=1'), 'a drag to reach the game with its button held');

    await key('s', 'KeyS');
    await until(
      async () => (await text('sound')) === 'Sound On',
      'the viewer to say the game plays sound',
    );

    await send('Page.reload');
    await until(
      async () => (await text('status')) === 'Ready',
      'the viewer to come back after a reload',
    );
    await sleep(6000);
    assert.equal(plays.playing('p1'), 'c1', 'the reload came back within the grace');
    assert.deepEqual(ended, []);

    await send('Page.navigate', { url: link });
    await until(
      async () =>
        String(await read('document.body.innerText')).includes(
          'This link has been used or has run out',
        ),
      'the used link to say so',
    );
    await until(() => ended.length === 1, 'the game to end once its viewer had gone', 10_000);
    assert.deepEqual(ended, ['closed']);
  });
});
