/**
 * Which names a project secret may not take (ADR 0057): those an agent's runtime, the shell, git,
 * Node or the system's loader read to decide how a program runs, and those Halcyonic sets or passes
 * itself. A secret is for the person's own keys, never to change how an agent or its sandbox runs.
 */
const RESERVED_NAMES = new Set([
  'PATH',
  'HOME',
  'USER',
  'LOGNAME',
  'SHELL',
  'PWD',
  'OLDPWD',
  'TMPDIR',
  'TMPPREFIX',
  'TERM',
  'LANG',
  'IFS',
  'ENV',
  'BASH_ENV',
  'ZDOTDIR',
  'PS4',
  'SSH_AUTH_SOCK',
  'EDITOR',
  'VISUAL',
  'PAGER',
  // Where a program connects and what it trusts: Claude Code gets the secrets in its whole process
  // environment, so one of these would send its API traffic elsewhere (AUDIT3 S1).
  'HTTP_PROXY',
  'HTTPS_PROXY',
  'ALL_PROXY',
  'NO_PROXY',
  'SSL_CERT_FILE',
  'SSL_CERT_DIR',
  'REQUESTS_CA_BUNDLE',
  'CURL_CA_BUNDLE',
]);

const RESERVED_PREFIXES = [
  'HALCYONIC_',
  'OPENCODE_',
  'CODEX_',
  'CLAUDE_',
  'ANTHROPIC_',
  'OLLAMA_',
  'XDG_',
  'LC_',
  'DYLD_',
  'LD_',
  'GIT_',
  'NODE_',
  'NPM_CONFIG_',
  'PYTHON',
  'BASH_FUNC_',
  'SANDBOX_',
  'BUN_', // the bundled Claude Code is a Bun build
];

/** Why `name` cannot be a project secret's name, in the product's words, or null when it can. */
export function reservedSecretName(name: string): string | null {
  if (RESERVED_NAMES.has(name) || RESERVED_PREFIXES.some((prefix) => name.startsWith(prefix))) {
    return `${name} is a name the Mac or an agent uses to run, so a secret can't take it. Choose another name.`;
  }
  return null;
}
