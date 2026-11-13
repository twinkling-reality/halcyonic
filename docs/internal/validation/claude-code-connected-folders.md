# Claude Code on a connected folder: no key in its hands, the host's folders out of reach (VRIFY, 2026-11-13)

A Claude Agent task on a folder the person connected runs outside Halcyonic's closed profile (ADR 0048), with the
person's own access. Until this change its process held `ANTHROPIC_API_KEY`, the person's key itself, so the agent,
its commands and every hook passed held it (AUDIT3's finding, 2026-11-12, 18:16Z), and it could read the host's own
folders, the key's file and the journal among them. The lead's 18:21Z step 3, both parts, is built here.

## What is built

- (a) Every Claude Code process the adapter spawns outside the closed profile (starts, resumes and model listings)
  reaches its API through the same key gate confined runs use (`packages/seatbelt/src/key-gate.ts`): its environment
  holds the gate's address as `ANTHROPIC_BASE_URL` and a run credential of its own as `ANTHROPIC_API_KEY`
  (`halcyonic-gate-run-` and 64 hex digits, issued per process, revoked when it exits or fails to spawn), never the
  key; `ANTHROPIC_AUTH_TOKEN` is dropped (`openLaunch` in `confinement.ts`). The gate adds the key on the way out. A
  cloud provider switch (Bedrock, Vertex, Foundry) has no gate, as before (its hosts are not known ahead), and then
  the process is given no Anthropic key or token at all, since a cloud provider never takes it (AUDIT3 M1). The gate
  is used where the adapter spawns Claude Code itself (the SDK's own query); a test handing its own query opens none.
- (b) Each such process runs under a light seatbelt profile (`hostFoldersDeniedProfile` in
  `packages/seatbelt/src/profile.ts`): `(allow default)` and `(deny file-read* file-write*)` of the host's own folders,
  the plane's data folder and the task copies' folder (`hostFolders` from `claudeConfinement`), and of every
  `.halcyonic` or `.halcyonic-<name>` folder in the person's home (the owner's, the proving plane's). Nothing else is
  taken away: the network, the person's home and tools, their own folder, Salidium's and Seorak's homes. A start in
  one of those folders is refused in words. macOS only; elsewhere there is no seatbelt.
- The record stays `confined: false`: the light profile is not the closed one.
- The adapter declares the result for the key's card (`ClaudeAgentRuntimeAdapter.holdsKeyOutsideSandbox()`: false on
  this Mac), so the card stops saying a task on a connected folder holds the key, by itself.

## Seen (the bundled Claude Code 2.1.283; a fake API on loopback; a fake key; scratch folders)

`open-launch.e2e.test.ts`, failing first on main e6cbebaa7 (the command inside read the scratch data folder): an
unconfined run's own command, approved, could not read, list or write the scratch data folder (exit 1 each), nor
read a `.halcyonic-proving` home's file; it read the person's home and wrote its own folder; it held
`key=halcyonic-gate-run-<64 hex> token=none`; the gate asked without that credential answered 403; every request
reached the fake API through the gate with the fake key in `x-api-key` and no `Authorization`; no file of its folder
held the key. A hard link from the data folder into its project was refused, and reading through it too (AUDIT3
M2). A Salidium-shaped observation hook, passed as b114 passes them, wrote the key its environment held at each
prompt: a run credential each time. A second adapter resumed the session, and its process held a run credential too.
No file under the scratch HOME or Claude Code's configuration holds the key or any 16-character slice of it, after
the turn and after the resume (AUDIT3 M2); the same check on main e6cbebaa7 passed too, so the bundled Claude Code in
this non-interactive use wrote no fragment of the key into `~/.claude.json` before this change either (the test
failed there first on the data folder). The other Claude end to end tests (confined, plan, resume, credential, pictures, project-settings) pass
on it, their unconfined runs now through the gate. `profile.test.ts` shows the profile under `sandbox-exec` (the host
folders refused, a look-alike `.halcyonicnot` and the project not).

## Not verified, and what it does not close

- A cloud provider run is shown by the unit test of `openLaunch` (no key, no token in its environment), not by an end
  to end run: that would reach AWS, Google or Azure.
- The model listing's own process is covered by code (`#openSpawn` on every path), not by an end to end assertion.

- An unconfined agent still runs with the person's own user rights: it can spend through the gate while its process
  lives (its run credential), and read other processes' arguments (`process-info` is not in the profile). The plane
  keeps no key in its arguments, nor in its environment when the key is a stored file.
- A key given to the plane in its environment rather than stored: the plane process holds it, as before.
- Linux or another system: no profile, so the key's file is reachable; the card says so there.
