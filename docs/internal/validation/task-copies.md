# A task's own copy: git and the file system (2026-11-10)

Verified for [ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md) on this Mac with
the git the checkpoint store runs, Xcode's `git version 2.50.1 (Apple Git-155)`, resolved through
`xcrun` as `checkpoints/store.ts` does, in scratch folders only. Nothing here touched a real
project. The store side used a bare repository with the checkpoint store's attributes
(`* -text -filter -ident -working-tree-encoding -eol`) and throwaway indexes.

## Verified

1. `cp -c -R` (APFS clone) of a plain folder exits 0 and keeps an absolute symbolic link as a link
   to the same target, so a starter's `node_modules` still reaches the shared dependencies. 13,616
   files (this repository's sources with its `node_modules` links) cloned in 2.1 s.
2. A tree of a folder written through `GIT_DIR=<store> GIT_WORK_TREE=<folder>
   GIT_INDEX_FILE=<throwaway>` with `add -A` and `write-tree` writes nothing into the folder; no
   `.git` appears in a plain folder. The first such tree of about 12,000 files into a fresh store
   took 11.5 s, a second with the same index 0.07 s.
3. `git merge-tree --write-tree --merge-base=<base> <project> <task>` exits 0 and prints the merged
   tree when the two sides changed different places, including a file the task deleted, a new
   executable file and a binary file only the task changed.
4. With both sides changing the same line it exits 1; `--name-only` prints the merged tree, then
   the conflicted paths, then git's messages (`CONFLICT (content): Merge conflict in src/a.txt`);
   the merged tree holds that file with `<<<<<<<`, `=======` and `>>>>>>>` around both sides.
5. A binary file changed on both sides is a conflict: `warning: Cannot merge binary files`,
   `CONFLICT (content)`; the index lines of the output show stages 1, 2 and 3 for it.
6. Applying a clean merged tree to the folder with `read-tree -m -u <project tree> <merged tree>`,
   on an index built by `add -A` from the folder itself (so it holds the folder's stat data), exits
   0: the folder's tree afterwards equals the merged tree, the deleted file is gone, the new file
   is `-rwxr-xr-x`, the symbolic link is untouched and no `.git` is created. A fresh `read-tree`
   index without stat data is refused (`Entry 'img.bin' not uptodate. Cannot merge.`).
7. If a file is edited after that index was built and before the apply, `read-tree -m -u` exits 128
   (`Entry 'src/a.txt' not uptodate. Cannot merge.`) and the later edit is kept.
8. A git project's clone has its own `.git`: a commit in the clone left the original at 1 commit
   and the clone at 2; the original's uncommitted edit was in the clone; a file committed in the
   clone did not appear in the original.
9. A git worktree's folder, cloned, keeps its `.git` as a file reading `gitdir: <the original
   repository's worktree record>`, so git commands in the clone would act on the original.

10. `merge-tree --write-tree --name-only -z` prints the merged tree, the conflicted paths, an empty
    separator, then messages, each as a count, that many paths, a type and the message; the types seen
    were `CONFLICT (contents)` (also for an add/add), `CONFLICT (binary)` (beside a contents conflict for
    the same file), `CONFLICT (modify/delete)`, and `Auto-merging`. A modify/delete leaves the modified
    version in the merged tree.

## A real run (2026-11-10, 22:49 to 22:57Z, a scratch control plane, ollama/muse-glimmer:30b-mlx)

One git project with one commit, two tasks started at once, Codex 0.157.0 and OpenCode 2.0.18, each told
to write one file; one iwsdk-vr starter project, one Codex task told to add a comment line to
`src/index.ts` and run `npx tsc --noEmit`. All three were accepted at once, each got a copy of its own
(`workstream.copy_made` before `execution.created`), and all three completed with no approval and no
question asked by any runtime in a folder it had not seen.

- Each file was in its own task's copy only: `codex.txt` in Codex's, `opencode.txt` in OpenCode's,
  neither in the other's; the project's folder still held only `.git` and `README.md`, one commit.
- The starter's copy had the comment line, the project's `src/index.ts` was unchanged, and the copy's
  `node_modules` was still a link to the starter's shared dependencies.
- Codex ran the starter task confined (`confined: true`), the git project's unconfined, as Codex's
  confinement is for starter projects today. Inside its confinement `npx tsc --noEmit` first failed: Node
  could not read `/System/Library/OpenSSL/openssl.cnf` (`Operation not permitted`) and npm warned of the
  Node version on the agent's PATH; the agent's own `env -i ... OPENSSL_CONF= npx tsc --noEmit` then
  exited 0. Not checked in the project's own folder, so not known to be the copy's.

## Not verified

- Claude Code in a copy (it needs a hosted model, which no lane may use).
- Whether Salidium and Seorak follow an execution whose folder is a copy.
- Clone time for a folder with a real `node_modules` of 100,000 files (extrapolated, not measured).
- A volume that cannot clone (`cp -c` on a non-APFS volume).
