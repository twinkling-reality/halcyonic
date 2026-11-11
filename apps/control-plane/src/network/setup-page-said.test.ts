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
  const inPage = (context as { saidParts: (lines: string[]) => unknown }).saidParts;
  // Its answer is made in the page's own context: brought over as plain data to compare.
  return { saidParts: (lines) => JSON.parse(JSON.stringify(inPage(lines))) };
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
