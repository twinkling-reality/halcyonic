import { execFileSync } from 'node:child_process';
import { chmodSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A project folder whose origin is `https://github.com/owner/garden.git`, which the test's own git
 * settings (in `home`) point at a local bare repository, and a stand-in `gh` that records each call
 * and answers with a pull request's address. Nothing reaches github.com, and no one's sign-in is
 * used (ADR 0039, amended).
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
    `[user]\n\tname = Test Person\n\temail = person@example.test\n[url "file://${origin}"]\n\tinsteadOf = ${remote}\n[init]\n\tdefaultBranch = main\n`,
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
    `#!/bin/sh\necho "$* token:\${GH_TOKEN:-none}" >> '${ghLog}'\nif [ "$1" = pr ]; then echo 'https://github.com/owner/garden/pull/7'; fi\nexit 0\n`,
  );
  chmodSync(ghPath, 0o755);
  return { home, projects, folder, origin, ghLog, ghPath, git };
}
