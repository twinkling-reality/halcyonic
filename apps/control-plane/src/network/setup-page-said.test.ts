import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { createContext, runInContext } from 'node:vm';

/**
 * The setup page's own script, run in a context of its own with stand-ins for the browser, so its pure reading of what
 * setup printed is tested as the page runs it. Nothing is fetched: the stand-in fetch refuses.
 */
function setupScript(): {
  saidParts: (lines: string[]) => { text: string; kind: 'line' | 'quiet' | 'command' }[];
  restartView: (phase: string, reopens: boolean, usual: boolean) => string[];
  refusalMeans: (restartSeen: boolean, body: string) => string;
} {
  const element = () => ({
    addEventListener: () => undefined,
    hidden: false,
    dataset: {},
    setAttribute: () => undefined,
    replaceChildren: () => undefined,
    append: () => undefined,
  });
  const context = createContext({
    document: { getElementById: element, createElement: element, createElementNS: element },
    fetch: () => Promise.reject(new TypeError('no network in a test')),
    setInterval: () => 0,
    setTimeout: () => 0,
    navigator: {},
  });
  runInContext(readFileSync(new URL('./setup-page/setup.js', import.meta.url), 'utf8'), context);
  const page = context as unknown as Record<string, (...args: unknown[]) => unknown>;
  // Its answers are made in the page's own context: brought over as plain data to compare.
  const plain =
    (name: string) =>
    (...args: unknown[]) =>
      JSON.parse(JSON.stringify((page[name] as (...a: unknown[]) => unknown)(...args)));
  return {
    saidParts: plain('saidParts') as never,
    restartView: plain('restartView') as never,
    refusalMeans: plain('refusalMeans') as never,
  };
}

describe("the setup page's reading of what setup said (AUDIT2, 2026-11-11)", () => {
  test('each command setup prints stays one whole line, exactly as printed, never joined to the next', () => {
    const { saidParts } = setupScript();
    const commands = [
      'mkdir -p ~/.halcyonic/starters/iwsdk-vr',
      'cp /repo/starters/iwsdk-vr/package.json /repo/starters/iwsdk-vr/package-lock.json ~/.halcyonic/starters/iwsdk-vr',
      'npm ci --prefix ~/.halcyonic/starters/iwsdk-vr --ignore-scripts --no-audit --no-fund --registry https://registry.npmjs.org/',
      'pnpm mac-setup starter',
    ];
    const parts = saidParts([
      "The VR starter isn't set up on this Mac. Install its pinned set from the public npm registry, with no install scripts and none of your own npm settings, then run this again:",
      ...commands.map((command) => `     ${command}`),
    ]);
    assert.deepEqual(
      parts.map((part) => part.kind),
      ['line', 'command', 'command', 'command', 'command'],
    );
    assert.deepEqual(
      parts.filter((part) => part.kind === 'command').map((part) => part.text),
      commands,
    );
  });

  test("a sentence setup's wrap() broke is joined back once, at two spaces or three; a list stays a list", () => {
    const { saidParts } = setupScript();
    const parts = saidParts([
      'Allowing ~/HalcyonicProjects, a new empty folder:',
      '  A paired headset sees the names of the folders directly inside it, can make new empty folders',
      '  there with no limit on how many.',
      '1. Folders agents may use: To Do',
      '   No folder is allowed yet, so no agent can start work. The headset says it cannot',
      '   start yet.',
      '   Allow one folder you keep for projects.',
      '   Next:',
      '     pnpm mac-setup allow',
      '',
      "Ollama on this Mac doesn't have qwen3:9b. Download it with ollama pull qwen3:9b, or choose one it has:",
      '  qwen3.5:9b',
      '  muse-glimmer:30b-mlx',
    ]);
    assert.deepEqual(parts, [
      { text: 'Allowing ~/HalcyonicProjects, a new empty folder:', kind: 'line' },
      {
        text: 'A paired headset sees the names of the folders directly inside it, can make new empty folders there with no limit on how many.',
        kind: 'quiet',
      },
      { text: '1. Folders agents may use: To Do', kind: 'line' },
      {
        text: 'No folder is allowed yet, so no agent can start work. The headset says it cannot start yet.',
        kind: 'quiet',
      },
      { text: 'Allow one folder you keep for projects.', kind: 'quiet' },
      { text: 'Next:', kind: 'quiet' },
      { text: 'pnpm mac-setup allow', kind: 'command' },
      {
        text: "Ollama on this Mac doesn't have qwen3:9b. Download it with ollama pull qwen3:9b, or choose one it has:",
        kind: 'line',
      },
      { text: 'qwen3.5:9b', kind: 'quiet' },
      { text: 'muse-glimmer:30b-mlx', kind: 'quiet' },
    ]);
  });
});

describe("the setup page's words around a Restart to Apply (the owner, 2026-11-13T04:04Z)", () => {
  test("after a restart the page saw, its own page server's refusal means Halcyonic is running again; anything else is the closed page", () => {
    const { refusalMeans } = setupScript();
    const closed = 'This page has closed. Open pairing again on this computer.';
    assert.equal(refusalMeans(true, closed), 'started');
    assert.equal(refusalMeans(true, `${closed}\n`), 'started');
    assert.equal(
      refusalMeans(false, closed),
      'closed',
      'a page that saw no restart is the closed page, red',
    );
    assert.equal(
      refusalMeans(true, 'Forbidden'),
      'closed',
      'another server on the port is not taken for Halcyonic',
    );
    assert.equal(refusalMeans(true, ''), 'closed');
  });

  test('each phase is said calmly where it went right, and plainly where it may not have; the way back is named on the usual folder', () => {
    const { restartView } = setupScript();
    assert.deepEqual(restartView('restarting', false, true), [
      'working',
      'hourglass',
      'Restarting',
      'When Halcyonic is running again, run pnpm pair in Terminal to open it again.',
    ]);
    assert.deepEqual(
      restartView('restarting', false, false)[3],
      'When Halcyonic is running again, open this page again the way you opened it.',
    );
    assert.deepEqual(
      restartView('restarting', true, true)[3],
      'A new page opens by itself when Halcyonic is running again.',
    );
    assert.deepEqual(restartView('started', false, true), [
      'good',
      'tick',
      'Started Again',
      'Halcyonic is running again with your changes. Run pnpm pair in Terminal to open it again.',
    ]);
    assert.deepEqual(
      restartView('started', false, false)[3],
      'Halcyonic is running again with your changes. Open this page again the way you opened it.',
    );
    assert.deepEqual(
      restartView('started', true, true)[3],
      'Halcyonic is running again with your changes, and opens a new page by itself. Once that page is open, you can close this one.',
    );
    const notBack = restartView('not_back', false, true);
    assert.deepEqual(notBack.slice(0, 3), ['bad', 'alert', 'Not Back Yet']);
    assert.match(notBack[3] as string, /may not have started again/);
  });
});
