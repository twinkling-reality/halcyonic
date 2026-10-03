/**
 * Credentials taken out of a runtime's or a provider's error text before it is journaled. The
 * journal is append-only and replayed to every device, and such text can repeat what it was given:
 * a gateway's 401 that echoes the key, Codex's or OpenCode's error answer. Only credentials are
 * taken out; the rest stays word for word, so a person still reads what went wrong
 * ([logging-audit.md](../../../../docs/internal/validation/logging-audit.md)).
 */

export const REDACTED = '[redacted]';

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

/**
 * A credential after its scheme, as an Authorization header carries it: one with a digit or a
 * base64 sign in it, so a sentence such as "Basic authentication failed" stays as it is.
 */
const SCHEME_CREDENTIAL =
  /\b(Bearer|Basic|Token)(\s+)(?=[A-Za-z0-9._~+/=-]*[0-9+/=])[A-Za-z0-9._~+/=-]{8,}/gi;

/** A URL's user and password: `scheme://user:password@host`. */
const URL_USERINFO = /\b([a-z][a-z0-9+.-]{0,31}:\/\/)[^\s/@:]+(?::[^\s/@]*)?@/gi;

/**
 * A long run that reads as random: 32 or more letters, digits, `_` and `-`, with capitals, small
 * letters and digits all in it, as a base64url token is. Ids (small letters, digits and dashes)
 * and hashes in hex are left alone.
 */
const RANDOM_RUN = /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{32,}(?![A-Za-z0-9_-])/g;
const looksRandom = (run: string) => /[A-Z]/.test(run) && /[a-z]/.test(run) && /[0-9]/.test(run);

/**
 * `text` with every exact copy of a value in `secrets` replaced, longest first, then every
 * credential-shaped run: a scheme's credential, a URL's user and password, a well-known key shape,
 * and a long random run.
 */
export function redactSecrets(text: string, secrets: Iterable<string>): string {
  let out = text.length > READ ? text.slice(0, READ) : text;
  const exact = [...new Set([...secrets].map((secret) => secret.trim()))]
    .filter((secret) => secret.length >= SHORTEST_SECRET)
    .sort((a, b) => b.length - a.length);
  for (const secret of exact) out = out.split(secret).join(REDACTED);
  return out
    .replace(SCHEME_CREDENTIAL, `$1$2${REDACTED}`)
    .replace(URL_USERINFO, `$1${REDACTED}@`)
    .replace(KEY_SHAPES, REDACTED)
    .replace(RANDOM_RUN, (run) => (looksRandom(run) ? REDACTED : run));
}

/**
 * `text` within JOURNALED_TEXT characters, cut with an ellipsis, never splitting a surrogate pair:
 * after redaction, since "[redacted]" can be longer than what it replaced.
 */
export function withinLimit(text: string): string {
  if (text.length <= JOURNALED_TEXT) return text;
  const end = /[\uD800-\uDBFF]/.test(text.charAt(JOURNALED_TEXT - 2))
    ? JOURNALED_TEXT - 2
    : JOURNALED_TEXT - 1;
  return `${text.slice(0, end)}…`;
}
