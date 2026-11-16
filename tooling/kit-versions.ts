// The released versions of the VR starter's kit files (VRIFY, 2026-11-16; AUDIT3 K1 to K5): the SHA-256 of each kit
// file the laptop check compares, as main held them after every commit that changed one, oldest first. The host reads
// starters/iwsdk-vr/kit-versions.json so an untouched project made from an earlier kit still reads as the kit's own.
//   node tooling/kit-versions.ts --seed   rebuild the list from git history (read only; no network)
//   node tooling/kit-versions.ts --add    append the template's current set when it is not already the last
// Every kit change adds its set in the same commit (--add); tooling/kit-versions.test.ts fails when it was forgotten.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The kit files the laptop check compares (laptop-probe.ts READING_FILES, which holds the layer's and the fit's). */
export const KIT_FILES = [
  'play-input.ts',
  'play-input-core.ts',
  'legible.ts',
  'pages.ts',
  'rows.ts',
  'decor.ts',
  'look.ts',
] as const;

export interface KitVersion {
  /** The main commit after which the kit stood so, short. */
  readonly from: string;
  readonly sha256: Readonly<Record<string, string>>;
}

export interface KitVersions {
  readonly files: readonly string[];
  readonly versions: readonly KitVersion[];
}

const ROOT = new URL('..', import.meta.url).pathname;
const TEMPLATE = 'starters/iwsdk-vr/template/src';
export const LIST = join(ROOT, 'starters/iwsdk-vr/kit-versions.json');

const sha = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');
const git = (args: string[]) =>
  execFileSync('git', args, {
    cwd: ROOT,
    maxBuffer: 64 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'ignore'],
  });

/** The template's kit set as it stands in the working tree. */
export function currentSet(): Record<string, string> {
  return Object.fromEntries(
    KIT_FILES.map((file) => [file, sha(readFileSync(join(ROOT, TEMPLATE, file)))]),
  );
}

/**
 * Every set main held after a commit changing a kit file, oldest first. A file the kit did not have yet is absent from
 * that version: a comparison of files a version lacks never matches it (laptop-probe.ts).
 */
export function seedFromHistory(ref = 'main'): KitVersion[] {
  const paths = KIT_FILES.map((file) => `${TEMPLATE}/${file}`);
  const commits = git(['log', '--reverse', '--format=%h', ref, '--', ...paths])
    .toString()
    .trim()
    .split('\n');
  const versions: KitVersion[] = [];
  for (const commit of commits) {
    const set: Record<string, string> = {};
    for (const file of KIT_FILES) {
      try {
        set[file] = sha(git(['show', `${commit}:${TEMPLATE}/${file}`]));
      } catch {
        // Not in the kit yet at this commit.
      }
    }
    if (Object.keys(set).length === 0) continue;
    const last = versions.at(-1);
    if (last !== undefined && KIT_FILES.every((file) => last.sha256[file] === set[file])) continue;
    versions.push({ from: commit, sha256: set });
  }
  return versions;
}

function write(versions: readonly KitVersion[]): void {
  writeFileSync(LIST, `${JSON.stringify({ files: KIT_FILES, versions }, null, 2)}\n`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const mode = process.argv[2];
  if (mode === '--seed') {
    const versions = seedFromHistory();
    const now = currentSet();
    const last = versions.at(-1);
    if (last === undefined || !KIT_FILES.every((file) => last.sha256[file] === now[file]))
      versions.push({ from: 'working tree', sha256: now });
    write(versions);
    console.log(`kit-versions: ${versions.length} versions written`);
  } else if (mode === '--add') {
    const list = JSON.parse(readFileSync(LIST, 'utf8')) as KitVersions;
    const now = currentSet();
    const last = list.versions.at(-1);
    if (last !== undefined && KIT_FILES.every((file) => last.sha256[file] === now[file])) {
      console.log('kit-versions: the current set is already the last');
    } else {
      const head = git(['rev-parse', '--short', 'HEAD']).toString().trim();
      write([...list.versions, { from: `after ${head}`, sha256: now }]);
      console.log(`kit-versions: added the current set (${list.versions.length + 1} versions)`);
    }
  } else {
    console.log('usage: node tooling/kit-versions.ts --seed | --add');
    process.exit(1);
  }
}
