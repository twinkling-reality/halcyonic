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

## Not verified

- Whether Claude Code, Codex or OpenCode ask anything new for a folder they have not seen.
- Whether Salidium and Seorak follow an execution whose folder is a copy.
- Clone time for a folder with a real `node_modules` of 100,000 files (extrapolated, not measured).
- A volume that cannot clone (`cp -c` on a non-APFS volume).
