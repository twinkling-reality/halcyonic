import { execFileSync } from 'node:child_process';
import { chmodSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A project folder whose origin is `https://github.com/owner/garden.git`, which the test's own git
 * settings (in `home`) point at a local bare repository, and a stand-in `gh` that records each call
 * and answers with a pull request's address, and with a Pages address when asked for one (none once a
 * file `pages-refused` is in `root`, as GitHub answers for a site it does not keep). Its https,
 * `git@` and `ssh://` forms all lead there, and any other github.com address to a path that is not
 * there: nothing reaches github.com, and no one's sign-in is used (ADR 0039, amended).
 */
export function standInGitHub(root: string) {
  const home = join(root, 'home');
  const projects = join(root, 'projects');
  const folder = join(projects, 'garden');
  const origin = join(root, 'origin.git');
  const ghLog = join(root, 'gh.log');
  const ghPath = join(root, 'bin', 'gh');
  const remote = 'https://github.com/owner/garden.git';
  for (const path of [home, folder, join(root, 'bin'), join(root, 'data')])
    mkdirSync(path, { recursive: true });
  writeFileSync(
    join(home, '.gitconfig'),
    [
      '[user]\n\tname = Test Person\n\temail = person@example.test',
      // Every form of the test repository's address leads to the local bare one (AUDIT3 S4)...
      `[url "file://${origin}"]`,
      `\tinsteadOf = ${remote}`,
      '\tinsteadOf = git@github.com:owner/garden.git',
      '\tinsteadOf = ssh://git@github.com/owner/garden.git',
      // ...and any other address on github.com to a path that is not there, so nothing leaves.
      `[url "file://${join(root, 'no-github')}/"]`,
      '\tinsteadOf = https://github.com/',
      '\tinsteadOf = git@github.com:',
      '\tinsteadOf = ssh://git@github.com/',
      '[init]\n\tdefaultBranch = main\n',
    ].join('\n'),
  );
  const git = (cwd: string, ...args: string[]) =>
    execFileSync('/usr/bin/git', args, {
      cwd,
      env: { PATH: '/usr/bin:/bin', HOME: home, GIT_CONFIG_NOSYSTEM: '1' },
    })
      .toString()
      .trim();
  git(root, 'init', '--bare', '-q', origin);
  git(folder, 'init', '-q');
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  git(folder, 'add', '-A');
  git(folder, 'commit', '-q', '-m', 'first');
  git(folder, 'remote', 'add', 'origin', remote);
  git(folder, 'push', '-q', 'origin', 'main');
  writeFileSync(
    ghPath,
    `#!/bin/sh\necho "$* token:\${GH_TOKEN:-none}" >> '${ghLog}'\nif [ "$1" = pr ]; then echo 'https://github.com/owner/garden/pull/7'; fi\nif [ "$1" = api ] && [ "$3" = '--jq' ]; then [ -e '${join(root, 'pages-refused')}' ] && exit 1; echo 'https://owner.github.io/garden/'; fi\nexit 0\n`,
  );
  chmodSync(ghPath, 0o755);
  return { home, projects, folder, origin, ghLog, ghPath, git };
}
