import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, realpathSync, rmSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import type { CommandEnvelope, ProjectId } from '@halcyonic/contracts';
import { KEPT_FILES, MediaStore } from '../core/media-store.ts';
import { startTestServer } from '../testing/harness.ts';
import { MediaPage, mountMedia, partOf, RECEIVED_WORDS } from './media-page.ts';

const PORTS = [47912, 47919] as const;
const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-media-page-')));
after(() => rmSync(root, { recursive: true, force: true }));

interface Answer {
  readonly status: number;
  readonly headers: Record<string, string | string[] | undefined>;
  readonly body: string;
}

function call(
  port: number,
  method: string,
  path: string,
  headers: Record<string, string> = {},
  body?: Buffer | string,
): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port,
        path,
        method,
        // A connection of its own: a refused request's unread body never runs into the next one.
        agent: false,
        headers: { host: `127.0.0.1:${port}`, 'sec-fetch-site': 'none', ...headers },
      },
      (res) => {
        let text = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          text += chunk;
        });
        res.on('end', () =>
          resolve({ status: res.statusCode ?? 0, headers: res.headers, body: text }),
        );
      },
    );
    req.on('error', reject);
    if (body !== undefined) req.write(body);
    req.end();
  });
}

function box(type: string, payload: Buffer = Buffer.alloc(0)): Buffer {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(8 + payload.length, 0);
  head.write(type, 4, 'latin1');
  return Buffer.concat([head, payload]);
}
/** A made-up clip with a whole box structure: no frames, never anyone's footage. */
const clip = Buffer.concat([
  box('ftyp', Buffer.from('qt  \0\0\0\0qt  ', 'latin1')),
  box('moov', Buffer.alloc(16)),
  box('mdat', Buffer.alloc(32)),
]);

test('the Photos and Videos page takes a file only behind its session and checks, streams it to the store, and submits Add Media as a local press (ADR 0066)', async (t) => {
  const folder = join(root, 'store');
  const store = new MediaStore(folder);
  const submitted: CommandEnvelope[] = [];
  const project = { project_id: 'p1' as ProjectId, name: 'Camel Ride', folder: root };
  const page = new MediaPage({
    store,
    projects: () => [project],
    media: () => [],
    stills: () => undefined,
    command: () => undefined,
    pending: () => [],
    submit: (command) => {
      submitted.push(command);
      return { disposition: 'accepted', command: null };
    },
    protectedFolders: [],
    nextId: () => `id-${submitted.length}`,
    now: () => new Date('2026-11-16T04:00:00Z'),
  });
  const server = await startTestServer({ page: { ports: PORTS, mount: mountMedia(page) } });
  t.after(() => server.stop());
  assert.ok(server.page);
  const port = server.page.port;
  const link = new URL(server.page.link());
  const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
  const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
  const press = {
    cookie,
    origin: `http://127.0.0.1:${port}`,
    'sec-fetch-site': 'same-origin',
    'content-type': 'application/octet-stream',
    'content-length': String(clip.length),
  };
  const add = `/setup/media/add?project=p1`;

  // Refused before one byte is read: no session, a forged one, another origin, a cross-site request.
  for (const headers of [
    { ...press, cookie: '' },
    { ...press, cookie: 'page=forged' },
    { ...press, origin: 'http://127.0.0.1:1' },
    { ...press, 'sec-fetch-site': 'cross-site' },
  ]) {
    const answer = await call(port, 'POST', add, headers, clip);
    assert.equal(answer.status, 403, JSON.stringify(headers));
  }
  assert.deepEqual(
    readdirSync(root).includes('store') ? readdirSync(folder) : [],
    [],
    'no file was kept',
  );
  assert.equal(submitted.length, 0);
  // Only raw bytes, and never past the bound by its declared length.
  assert.equal(
    (await call(port, 'POST', add, { ...press, 'content-type': 'application/json' }, clip)).status,
    415,
  );
  assert.equal(
    (await call(port, 'POST', add, { ...press, 'content-length': '80000001' })).status,
    413,
  );

  const sent = await call(port, 'POST', add, press, clip);
  assert.equal(sent.status, 200);
  assert.deepEqual(JSON.parse(sent.body), { said: null });
  assert.equal(submitted.length, 1);
  assert.equal(submitted[0]?.command_type, 'project.media.add');
  assert.deepEqual(submitted[0]?.payload, {
    project_id: 'p1',
    media: readdirSync(folder)[0],
  });
  assert.match(String(readdirSync(folder)[0]), /^[0-9a-f]{64}$/);

  // Not a medium: refused in the page's words, nothing submitted.
  const words = await call(port, 'POST', add, { ...press, 'content-length': '5' }, 'words');
  assert.match(JSON.parse(words.body).said, /isn't a whole photo or video/);
  assert.equal(submitted.length, 1);

  const state = JSON.parse((await call(port, 'GET', '/setup/media/state', { cookie })).body);
  assert.equal(state.projects[0].name, 'Camel Ride');
  assert.match(state.projects[0].said, /isn't a whole photo or video/);
  assert.equal((await call(port, 'GET', '/setup/media/state')).status, 403);
  // A still that is not one of the project's is never read.
  assert.equal(
    (await call(port, 'GET', '/setup/media/still?project=p1&path=src%2Fanything.png', { cookie }))
      .status,
    404,
  );
});

test('every other page route keeps its 1 KiB body bound beside the media route (ADR 0066, C7)', async (t) => {
  const page = new MediaPage({
    store: new MediaStore(join(root, 'store-2')),
    projects: () => [],
    media: () => [],
    stills: () => undefined,
    command: () => undefined,
    pending: () => [],
    submit: () => ({ disposition: 'accepted', command: null }),
    protectedFolders: [],
    nextId: () => 'id',
    now: () => new Date(),
  });
  const server = await startTestServer({
    page: {
      ports: PORTS,
      mount: (routes) => {
        mountMedia(page)(routes);
        routes.post('/setup/echo', () => ({ ok: true }));
      },
    },
  });
  t.after(() => server.stop());
  assert.ok(server.page);
  const port = server.page.port;
  const link = new URL(server.page.link());
  const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
  const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
  const headers = {
    cookie,
    origin: `http://127.0.0.1:${port}`,
    'sec-fetch-site': 'same-origin',
    'content-type': 'application/json',
  };
  const big = JSON.stringify({ text: 'x'.repeat(2048) });
  assert.equal((await call(port, 'POST', '/setup/echo', headers, big)).status, 413);
  assert.equal((await call(port, 'POST', '/setup/echo', headers, '{}')).status, 200);
});

test('the page says how many files may wait from the store own bound, never a number typed by hand (ADR 0066)', () => {
  assert.equal(
    RECEIVED_WORDS.full,
    `${KEPT_FILES} files are waiting to be added already. Try again in a moment.`,
  );
  assert.match(String(RECEIVED_WORDS.too_large), /25 MB for a photo, 80 MB for a video/);
});

test('a video tile plays only the play copy the journal names, read without a link, never another file (ADR 0066)', async (t) => {
  const { mkdirSync, symlinkSync, writeFileSync } = await import('node:fs');
  const project = join(root, 'play-project');
  mkdirSync(join(project, 'media'), { recursive: true });
  const play = 'media/video-0123456789ab-play.mp4';
  writeFileSync(join(project, play), clip);
  writeFileSync(join(project, 'media', 'video-0123456789ab.mov'), clip);
  writeFileSync(join(root, 'outside.mp4'), Buffer.from('not the project'));
  symlinkSync(join(root, 'outside.mp4'), join(project, 'media', 'video-bbbbbbbbbbbb-play.mp4'));
  const media = (path: string) => ({
    path: path.replace('-play.mp4', '.mov'),
    media_type: 'video/quicktime' as const,
    size_bytes: clip.length,
    added_at: '2026-11-16T04:00:00.000Z' as never,
    still: null,
    play: path,
    duration_ms: 6000,
    prepared: true,
  });
  const page = new MediaPage({
    store: new MediaStore(join(root, 'store-3')),
    projects: () => [{ project_id: 'p1' as ProjectId, name: 'Camel Ride', folder: project }],
    media: () => [media(play), media('media/video-bbbbbbbbbbbb-play.mp4')],
    stills: () => [],
    command: () => undefined,
    pending: () => [],
    submit: () => ({ disposition: 'accepted', command: null }),
    protectedFolders: [],
    nextId: () => 'id',
    now: () => new Date(),
  });
  const server = await startTestServer({ page: { ports: PORTS, mount: mountMedia(page) } });
  t.after(() => server.stop());
  assert.ok(server.page);
  const port = server.page.port;
  const link = new URL(server.page.link());
  const opened = await call(port, 'GET', `${link.pathname}${link.search}`);
  const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
  const get = (path: string, headers: Record<string, string> = { cookie }) =>
    call(port, 'GET', `/setup/media/play?project=p1&path=${encodeURIComponent(path)}`, headers);
  const served = await get(play);
  assert.equal(served.status, 200);
  assert.equal(served.headers['content-type'], 'video/mp4');
  assert.equal(Number(served.headers['content-length']), clip.length);
  assert.equal((await get(play, {})).status, 403, 'behind the session');
  // A part, as Safari asks for a video: 206 with its place in the whole.
  const part = await get(play, { cookie, range: 'bytes=0-9' });
  assert.equal(part.status, 206);
  assert.equal(part.headers['content-range'], `bytes 0-9/${clip.length}`);
  assert.equal(Number(part.headers['content-length']), 10);
  assert.equal(
    (await get('media/video-0123456789ab.mov')).status,
    404,
    'only a play copy the journal names',
  );
  assert.equal(
    (await get('media/video-bbbbbbbbbbbb-play.mp4')).status,
    404,
    'never through a link',
  );
  assert.equal((await get('../outside.mp4')).status, 404);
});

test('a Range header is read as one part of the file or not at all (ADR 0066)', () => {
  assert.deepEqual(partOf('bytes=0-9', 100), { start: 0, end: 9 });
  assert.deepEqual(partOf('bytes=90-', 100), { start: 90, end: 99 });
  assert.deepEqual(partOf('bytes=-10', 100), { start: 90, end: 99 });
  assert.deepEqual(partOf('bytes=0-1000', 100), { start: 0, end: 99 });
  for (const bad of [
    'bytes=100-',
    'bytes=5-2',
    'bytes=0-1,5-6',
    'items=0-1',
    'bytes=-',
    undefined,
    7,
  ])
    assert.equal(partOf(bad, 100), null, String(bad));
});
