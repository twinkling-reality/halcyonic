/**
 * Credentials taken out of a runtime's or a provider's text before a device sees it. The journal is
 * append-only and replayed to every device, and such text can repeat what it was given: a gateway's
 * 401 that echoes the key, Codex's or OpenCode's error answer, a command that sends a key. Error
 * text loses exact copies of what Halcyonic holds and every credential shape; what a person reads
 * to decide, such as the command they approve, loses only the exact copies. Each copy reads as
 * which secret it was; the rest stays as given, so a person still reads what went wrong or what is
 * asked ([logging-audit.md](../../../../docs/internal/validation/logging-audit.md)).
 */

/** What stands in for a credential found by its shape. */
export const REDACTED = '[redacted]';

/** A secret Halcyonic holds or passes to a runtime, with what a person reads in its place. */
export interface HeldSecret {
  /** What it is, as "Anthropic key" or a variable's name: it is replaced by "[redacted: what]". */
  readonly what: string;
  readonly value: string;
}

/** What stands in for an exact copy of a held secret: which one it was, never any of its value. */
export const redactedHeld = (what: string) => `[redacted: ${what}]`;

/** The most a journaled error text holds: the contracts' `Text(2000)`. */
export const JOURNALED_TEXT = 2000;

/**
 * What redaction reads of a text: what is journaled is at most JOURNALED_TEXT characters, cut after
 * redaction, so more is never read, however long a runtime's line is.
 */
const READ = 4096;

/** A value shorter than this is too likely to be an ordinary word to be replaced wherever it appears. */
const SHORTEST_SECRET = 8;

/** Well-known key and token shapes, whole. */
const KEY_SHAPES = new RegExp(
  [
    'sk-[A-Za-z0-9_-]{16,}', // OpenAI's and Anthropic's keys (sk-, sk-proj-, sk-ant-)
    'sk_(?:live|test)_[A-Za-z0-9]{16,}',
    'gh[pousr]_[A-Za-z0-9]{20,}', // GitHub's tokens
    'github_pat_[A-Za-z0-9_]{20,}',
    'glpat-[A-Za-z0-9_-]{20,}', // GitLab's personal access tokens
    'xox[abprs]-[A-Za-z0-9-]{10,}', // Slack's
    '(?:AKIA|ASIA)[0-9A-Z]{16}', // AWS access key ids
    'AIza[0-9A-Za-z_-]{35}', // Google's API keys
    'srkx_[A-Za-z0-9_-]{8,}', // Seorak's integration credential
    'hlcd_[A-Za-z0-9_-]{16,}', // a Halcyonic device credential
    'eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}', // a JSON Web Token
  ]
    .map((shape) => `(?<![A-Za-z0-9_-])${shape}`)
    .join('|'),
  'g',
);
const WHOLE_KEY_SHAPE = new RegExp(`^(?:${KEY_SHAPES.source})$`);

/**
 * A credential after its scheme, as an Authorization header carries it: one with a digit or a
 * base64 sign in it, so a sentence such as "Basic authentication failed" stays as it is, and never
 * a path: one that starts with `/`, `~` or `.`, as in "token /Users/me/.config/gh/hosts.yml", or
 * holds both `/` and a `.` before a letter or digit, as in "invalid token src/config/settings.json",
 * since base64 has no `.` and a JSON Web Token no `/`. One with `+` or `=` in it is base64 and so
 * never a path. It runs on across `=`, as in `abcdefgh=ijkl…`, but a `.` never ends it, so a full
 * stop after it, or after its padding, stays outside what is replaced, as in "…ZA==.Retry". A
 * parameter's name before it, with its quote, stays, as in `Token token="…"`.
 */
const SCHEME_CREDENTIAL = (() => {
  const run = '[A-Za-z0-9._~+/=-]';
  // A stretch with no `=` that neither starts nor ends with `.`.
  const piece = '[A-Za-z0-9_~+/-](?:[A-Za-z0-9._~+/-]*[A-Za-z0-9_~+/-])?';
  return new RegExp(
    [
      '\\b(Bearer|Basic|Token)(\\s+)',
      '((?:token|access_token|auth|key|api_key|credentials?|sig|signature|password|secret)="?)?',
      '(?![/~.])',
      '(?!(?=[A-Za-z0-9._~/-]*(?![A-Za-z0-9._~+/=-]))(?=[A-Za-z0-9._~/-]*\\/)[A-Za-z0-9._~/-]*\\.[A-Za-z0-9])',
      `(?=${run}*[0-9+/=])`,
      `(${piece}(?:=+${piece})*={0,2})`,
    ].join(''),
    'gi',
  );
})();

/**
 * A URL's user and password: `scheme://user:password@host`, up to the last `@` before the host,
 * since a password can hold a raw `@`, and a comma or an apostrophe as RFC 3986 allows. Whitespace,
 * a double quote, `<`, `>` or a comma before whitespace ends the URL, so an email address after it,
 * or a second URL, keeps its own `@`; one glued to it by a comma alone loses its host with it.
 */
const URL_USERINFO = /\b([a-z][a-z0-9+.-]{0,31}:\/\/)(?:[^\s/?#"<>,]|,(?!\s))+@/gi;

/**
 * A long run that reads as random: 32 or more letters, digits, `_` and `-`, with capitals, small
 * letters and digits all in it, as a base64url token is. Ids (small letters, digits and dashes)
 * and hashes in hex are left alone, and so are names built of words, such as a class, a test or a
 * branch. A random token switches between capitals, small letters and digits at least 0.4 times a
 * character, and has fewer than half its characters in runs of three or more small letters, where
 * a name's words put most of it. Measured over 50,000 random base64url tokens each, those that
 * stay, read as names or lacking one of the three, are 1.3 percent at 32 characters, 0.24 percent
 * at 43 (256 bits), 0.12 at 48 and 0.03 at 64; what Halcyonic holds is taken out exactly in any
 * case.
 */
const RANDOM_RUN = /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{32,}(?![A-Za-z0-9_-])/g;
const kind = (character: string) =>
  /[A-Z]/.test(character) ? 0 : /[a-z]/.test(character) ? 1 : /[0-9]/.test(character) ? 2 : 3;
function looksRandom(run: string): boolean {
  if (!(/[A-Z]/.test(run) && /[a-z]/.test(run) && /[0-9]/.test(run))) return false;
  let switches = 0;
  for (let at = 1; at < run.length; at++) {
    if (kind(run.charAt(at - 1)) !== kind(run.charAt(at))) switches++;
  }
  const inWords = [...run.matchAll(/[a-z]{3,}/g)].reduce((sum, word) => sum + word[0].length, 0);
  return switches / (run.length - 1) >= 0.4 && inWords / run.length < 0.5;
}

/**
 * `text` with every exact copy of a held secret replaced by which one it was, longest first, and
 * nothing else. A value held under two names reads as the first.
 */
export function redactHeld(text: string, secrets: Iterable<HeldSecret>): string {
  const exact = new Map<string, string>();
  for (const { what, value } of secrets) {
    const trimmed = value.trim();
    if (trimmed.length >= SHORTEST_SECRET && !exact.has(trimmed)) exact.set(trimmed, what);
  }
  let out = text;
  for (const [value, what] of [...exact].sort(([a], [b]) => b.length - a.length)) {
    out = out.split(value).join(redactedHeld(what));
  }
  return out;
}

/**
 * Whether a value reads as a credential by itself, whatever it is called: a well-known key shape,
 * 32 or more hex digits, or 16 or more token characters that read as random (as RANDOM_RUN).
 */
export function looksLikeCredential(value: string): boolean {
  const trimmed = value.trim();
  if (WHOLE_KEY_SHAPE.test(trimmed)) return true;
  if (/^[0-9A-Fa-f]{32,}$/.test(trimmed)) return true;
  return /^[A-Za-z0-9_+/=.-]{16,}$/.test(trimmed) && looksRandom(trimmed);
}

/**
 * `text` with every exact copy of a value in `secrets` replaced, longest first, then every
 * credential-shaped run: a scheme's credential, a URL's user and password, a well-known key shape,
 * and a long random run.
 */
export function redactSecrets(text: string, secrets: Iterable<HeldSecret>): string {
  return redactHeld(text.length > READ ? withoutHalfPair(text.slice(0, READ)) : text, secrets)
    .replace(
      SCHEME_CREDENTIAL,
      (all, scheme: string, space: string, name = '', credential: string) =>
        credential.length < SHORTEST_SECRET ? all : `${scheme}${space}${name}${REDACTED}`,
    )
    .replace(URL_USERINFO, `$1${REDACTED}@`)
    .replace(KEY_SHAPES, REDACTED)
    .replace(RANDOM_RUN, (run) => (looksRandom(run) ? REDACTED : run));
}

/** `text` without a high surrogate left alone at its end by a cut. */
function withoutHalfPair(text: string): string {
  return /[\uD800-\uDBFF]$/.test(text) ? text.slice(0, -1) : text;
}

/**
 * The first `count` characters of `text`, counted in code points as the contracts count them, so
 * a surrogate pair is never split; null when `text` has no more than that.
 */
function firstCharacters(text: string, count: number): string | null {
  if (text.length <= count) return null;
  let seen = 0;
  let end = 0;
  for (const character of text) {
    if (seen === count) return text.slice(0, end);
    seen += 1;
    end += character.length;
  }
  return null;
}

/** Whether `text` fits a contract's `limit`, counted in code points. */
export function fits(text: string, limit: number): boolean {
  return firstCharacters(text, limit) === null;
}

/**
 * Error text within `limit` characters, cut with an ellipsis: after redaction, since what stands in
 * for a credential can be longer than the credential.
 */
export function withinLimit(text: string, limit: number = JOURNALED_TEXT): string {
  if (fits(text, limit)) return text;
  return `${firstCharacters(text, limit - 1)}…`;
}

/** How a cut is marked in text a person reads as given, as the adapters mark theirs. */
export const TRUNCATED = ' [truncated]';

/**
 * Text a person reads as given within `limit` characters, the cut marked: after redaction, which
 * has to see a held secret whole to take it out, so nothing before the observation sink cuts it.
 */
export function fitted(text: string, limit: number): string {
  if (fits(text, limit)) return text;
  return `${firstCharacters(text, limit - TRUNCATED.length)}${TRUNCATED}`;
}

/**
 * How text from a runtime or a provider is cleaned before a device sees it, from what Halcyonic
 * holds when it is asked, since some of that changes (a server's password with each launch).
 */
export interface Redaction {
  /**
   * Error text, a runtime's or a provider's, which can echo a key Halcyonic never held: every exact
   * copy of what Halcyonic holds, then every credential shape.
   */
  errorText(text: string): string;
  /**
   * Text a person must read as given, such as the command they are asked to approve: only exact
   * copies of what Halcyonic holds go, so nothing that only looks like a credential is guessed
   * away. Whole, however long; `fitted` cuts it after.
   */
  held(text: string): string;
}

export function redaction(secrets: () => Iterable<HeldSecret> = () => []): Redaction {
  return {
    errorText: (text) => redactSecrets(text, secrets()),
    held: (text) => redactHeld(text, secrets()),
  };
}
