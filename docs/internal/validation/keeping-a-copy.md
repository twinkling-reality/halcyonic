# Keeping a copy of a project: git archive on this Mac (2026-11-11)

Verified for Show in Finder and Save a Copy (keeping what was made, F3) with the git the checkpoint
store runs, Xcode's `git version 2.50.1 (Apple Git-155)`, in scratch folders only.

## Verified

1. `git archive --format=zip -o <file> <tree>` on a SHA-256 bare repository writes a zip of exactly
   the tree's files: never `.git` (git never tracks it), never what the folder's `.gitignore` left
   out of the tree (a `node_modules`, a `dist`).
2. Git's pathspec globs are case-sensitive unless `icase` is given, and this Mac's disk is not
   (AUDIT2's probe: `:(exclude,glob)**/.env` let `.ENV` and `Server.PEM` into the zip). So the store
   no longer leaves files out by git's patterns. It lists the tree (`ls-tree -r -z --full-tree`),
   drops every path the one secret-file list names (`secretShapedPath`, lower-cased) or that lies
   in another bound project's folder, builds the rest into a scratch index (`read-tree --empty`,
   `update-index -z --index-info`), writes that tree and archives it with no pathspec. Run in a
   bare repository with no work tree. The test writes `deploy/.ENV`, `KEY.PEM`, `aws/credentials`,
   `config/Secrets.json`, `android/release.keystore`, `certs/app.JKS` and `home/id_dsa`: none is in
   the zip, and every one was with the old patterns (run against the old code, then restored).
3. `git ls-tree` refuses pathspec magic, so the count of what a copy holds is read from the zip's
   own central directory, folders left out.

## Not verified

- A folder with more than 65,535 files or over 4 GiB (a ZIP64 archive): `zipFileCount` reads the
  whole zip into memory and gives no count, and the copy fails "could not be read back". The
  checkpoint store's own limits keep a project well under that.
- `/usr/bin/open -R` on a folder outside the person's home.
