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

const runbooks = () =>
  readdirSync(RUNBOOKS)
    .filter((name) => name.endsWith('.md'))
    .map((name) => ({ name, lines: commands(readFileSync(new URL(name, RUNBOOKS), 'utf8')) }));

/**
 * `adb shell` joins the words it is given with spaces and does not quote them again, so a remote
 * `sh -c` given as separate words runs only its first command inside `run-as`
 * (headset-token-storage.md, 2026-10-04). Every one must be one quoted string, for the headset's
 * shell to read. (`adb exec-in` quotes each word after the first.)
 */
const SPLIT_REMOTE_SHELL = /\badb shell run-as\b.*\bsh -c\b/;

/** Where `text` has `first` before `then`. */
const before = (text: string, first: string, then: string) =>
  text.includes(first) && text.includes(then) && text.indexOf(first) < text.indexOf(then);

describe('the runbooks', () => {
  test('give every remote sh -c through adb shell as one quoted string', () => {
    const books = runbooks();
    assert.ok(books.some(({ name }) => name === 'HEADSET_SESSION.md'));
    assert.ok(books.some(({ name }) => name === 'XR_DEVELOPMENT.md'));
    for (const { name, lines } of books) {
      for (const line of lines) {
        assert.doesNotMatch(line, SPLIT_REMOTE_SHELL, `${name}: ${line.slice(0, 80)}`);
      }
    }
  });

  test("write each token private, whole and only in the control plane's own form, never a stale one", () => {
    let writes = 0;
    for (const { name, lines } of runbooks()) {
      lines.forEach((line, at) => {
        const sent = /cat > files\/([a-z-]+)\.tmp/.exec(line);
        if (sent?.[1] === undefined) return;
        const file = `files/${sent[1]}`;
        const where = `${name}, ${file}`;
        writes++;
        // What an earlier write left is removed first, by a command that returns once it has.
        assert.equal(
          lines[at - 1],
          `adb shell run-as com.halcyonic.xr rm -f ${file}.tmp`,
          `${where}: the leftover removed first`,
        );
        assert.match(line, /^adb exec-in "run-as com\.halcyonic\.xr sh -c '/, where);
        assert.ok(before(line, 'umask 077', `cat > ${file}.tmp`), `${where}: umask 077 before cat`);
        assert.ok(before(line, `rm -f ${file}.tmp`, `cat > ${file}.tmp`), `${where}: a new file`);
        assert.match(line, /< ~\/\.halcyonic\/access-token$/, where);
        const move = lines[at + 1] ?? '';
        assert.match(move, /^adb shell "run-as com\.halcyonic\.xr sh -c '/, where);
        const sized = `test \\"\\$(stat -c %s ${file}.tmp 2>/dev/null)\\" = 44 && chmod 600`;
        assert.ok(
          move.includes(`if ${sized} ${file}.tmp && mv -f ${file}.tmp ${file}; then`),
          where,
        );
        assert.ok(before(move, 'chmod 600', 'mv -f'), `${where}: private before it is moved`);
        assert.ok(
          move.includes(
            `else rm -f ${file}.tmp; echo not written: the token file is not the 44 bytes`,
          ),
          `${where}: a write that never landed is removed, and says so with the size`,
        );
        assert.doesNotMatch(move.slice(move.indexOf('echo not written')), /;[^;]*;[^;]*fi'"$/);
      });
    }
    assert.equal(writes, 4, "the app's and the glance's token, in two runbooks");
  });

  test('the guard knows the form that broke', () => {
    assert.match(
      "adb shell run-as com.halcyonic.xr sh -c 'umask 077; cat > files/x.tmp'",
      SPLIT_REMOTE_SHELL,
    );
    assert.doesNotMatch(
      `adb shell "run-as com.halcyonic.xr sh -c 'umask 077; cat > files/x.tmp'"`,
      SPLIT_REMOTE_SHELL,
    );
    assert.doesNotMatch('adb shell run-as com.halcyonic.xr rm -f files/x', SPLIT_REMOTE_SHELL);
  });
});
