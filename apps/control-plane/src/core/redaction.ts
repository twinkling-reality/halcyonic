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
 * since base64 has no `.` and a JSON Web Token no `/`. A full stop after it is a sentence's, and
 * stays outside what is replaced.
 */
const SCHEME_CREDENTIAL =
  /\b(Bearer|Basic|Token)(\s+)(?![/~.])(?!(?=[A-Za-z0-9._~+/=-]*\/)[A-Za-z0-9._~+/=-]*\.[A-Za-z0-9])(?=[A-Za-z0-9._~+/=-]*[0-9+/=])[A-Za-z0-9._~+/=-]{7,}[A-Za-z0-9_~+/=-]/gi;

/** A URL's user and password: `scheme://user:password@host`. */
const URL_USERINFO = /\b([a-z][a-z0-9+.-]{0,31}:\/\/)[^\s/@:]+(?::[^\s/@]*)?@/gi;

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
  return redactHeld(text.length > READ ? text.slice(0, READ) : text, secrets)
    .replace(SCHEME_CREDENTIAL, `$1$2${REDACTED}`)
    .replace(URL_USERINFO, `$1${REDACTED}@`)
    .replace(KEY_SHAPES, REDACTED)
    .replace(RANDOM_RUN, (run) => (looksRandom(run) ? REDACTED : run));
}

/**
 * `text` within `limit` characters, cut with an ellipsis, never splitting a surrogate pair: after
 * redaction, since "[redacted]" can be longer than what it replaced.
 */
export function withinLimit(text: string, limit: number = JOURNALED_TEXT): string {
  if (text.length <= limit) return text;
  const end = /[\uD800-\uDBFF]/.test(text.charAt(limit - 2)) ? limit - 2 : limit - 1;
  return `${text.slice(0, end)}…`;
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
   * away. Cut to `limit` only when "[redacted]" made a text that fitted longer than that; one that
   * was already too long is left to fail validation, as it did before.
   */
  held(text: string, limit: number): string;
}

export function redaction(secrets: () => Iterable<HeldSecret> = () => []): Redaction {
  return {
    errorText: (text) => redactSecrets(text, secrets()),
    held: (text, limit) => {
      const out = redactHeld(text, secrets());
      return text.length <= limit ? withinLimit(out, limit) : out;
    },
  };
}
