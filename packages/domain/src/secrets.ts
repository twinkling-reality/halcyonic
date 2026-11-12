/**
 * Which names a project secret may not take (ADR 0057): those the shell, git, Node or the system's
 * loader read to decide how a program runs, and those Halcyonic sets or passes itself. A secret is
 * for the person's own keys, never to change how an agent or its sandbox runs. Each runtime names
 * its own on top of these (`RuntimeCatalog.ownsEnvironmentName`, from its adapter), so a new
 * runtime brings its names with it.
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
  'OLLAMA_', // the local models' service, which every runtime here may call
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
];

/**
 * Why `name` cannot be a project secret's name, in the product's words, or null when it can:
 * `owned` says whether a runtime here reads it for itself.
 */
export function reservedSecretName(
  name: string,
  owned: (name: string) => boolean = () => false,
): string | null {
  if (
    RESERVED_NAMES.has(name) ||
    RESERVED_PREFIXES.some((prefix) => name.startsWith(prefix)) ||
    owned(name)
  ) {
    return `${name} is a name the Mac or an agent uses to run, so a secret can't take it. Choose another name.`;
  }
  return null;
}
