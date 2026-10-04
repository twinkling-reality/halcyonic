import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { describe, test } from 'node:test';

const RUNBOOKS = new URL('../../docs/internal/runbooks/', import.meta.url);

/** The commands in a runbook's shell blocks, one per line. */
function commands(markdown: string): string[] {
  const lines: string[] = [];
  let inside = false;
  for (const line of markdown.split(/\r?\n/)) {
    if (/^\s*```/.test(line)) {
      inside = !inside;
      continue;
    }
    if (inside) lines.push(line.trim());
  }
  return lines;
}

/**
 * adb joins the words it is given with spaces and does not quote them again, so a remote `sh -c`
 * given as separate words runs only its first command inside `run-as` (headset-token-storage.md,
 * 2026-10-04). Every one must be one quoted string, for the headset's shell to read.
 */
const SPLIT_REMOTE_SHELL = /\badb (?:shell|exec-in|exec-out) run-as\b.*\bsh -c\b/;

describe('the runbooks', () => {
  test('give every remote sh -c through run-as as one quoted string', () => {
    const files = readdirSync(RUNBOOKS).filter((name) => name.endsWith('.md'));
    assert.ok(files.includes('HEADSET_SESSION.md') && files.includes('XR_DEVELOPMENT.md'));
    let quoted = 0;
    for (const name of files) {
      for (const line of commands(readFileSync(new URL(name, RUNBOOKS), 'utf8'))) {
        assert.doesNotMatch(line, SPLIT_REMOTE_SHELL, `${name}: ${line.slice(0, 80)}`);
        if (/\badb (?:shell|exec-in) "run-as com\.halcyonic\.xr sh -c '/.test(line)) quoted++;
      }
    }
    assert.ok(quoted >= 8, `the token writes, each two quoted commands (${quoted})`);
  });

  test('the guard knows the form that broke', () => {
    assert.match(
      "adb exec-in run-as com.halcyonic.xr sh -c 'umask 077; cat > files/x.tmp' < token",
      SPLIT_REMOTE_SHELL,
    );
    assert.doesNotMatch(
      `adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; cat > files/x.tmp'" < token`,
      SPLIT_REMOTE_SHELL,
    );
    assert.doesNotMatch('adb shell run-as com.halcyonic.xr rm -f files/x', SPLIT_REMOTE_SHELL);
  });
});
