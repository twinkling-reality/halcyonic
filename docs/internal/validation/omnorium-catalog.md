# Omnorium catalog contract v1

- **Question:** Can Halcyonic read the person's catalog from Omnorium through its published,
  read-only catalog contract, proving the instance before it sends a credential, without depending
  on Omnorium's source ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md))?
- **Date:** 2026-10-29.
- **Versions:** contract `omnorium.catalog` 1.0 (originally published as `phat-porcupine.catalog` 1.0
  on 2026-10-29): `shared/contract/consumer.ts` (sha256 `40579eb9a77490cacc6b21a1c42ab649290c926d2cb91496b1c49326e92f472a`)
  and its four fixtures under `shared/consumer/fixtures/v1` (`catalog-empty.json` `1c6d6dc0…`,
  `catalog-sample-small.json` `cc917532…`, `error-not-ready-503.json` `c158f4ea…`,
  `error-unauthorized-401.json` `f07e902c…`), read from the owner's local checkout. No git command
  was run in it, and nothing of it is copied into this repository.
- **Method:** Halcyonic's client (`packages/integrations/omnorium`) was written from the
  contract's types with its own tolerant TypeBox readers, and tested against a stand-in it owns
  (`src/testing/fake-omnorium.ts`) and fixtures Halcyonic wrote by hand (`fixtures/v1`). Its
  reader was then run once over Omnorium's four published fixtures, in place.
- **Status:** Source-verified and fixture-verified. Not read live: that needs a consumer credential,
  which waits for the owner's yes. The control plane does not use the client yet.

## Findings

- **Discovery.** `consumer.json` in `$OMNORIUM_HOME` or `~/.omnorium` (and `$PHAT_PORCUPINE_HOME` or `~/.phat-porcupine`), written at every
  start (directory 0700, file 0600, replaced by rename) and removed at a clean stop, names the
  instance, a 32-byte secret in base64url, the sample and stand-in flags, and each contract with its
  base URL, always exactly `http://127.0.0.1:<port>/consumer/v1`.
- **Proof.** `GET /consumer/v1/discovery?challenge=<32 bytes, base64url>` answers without a
  credential with the file less its secret and `proof`, the HMAC-SHA256 of the challenge's 32 bytes
  keyed with the secret's 32 bytes, in base64url. A missing or malformed challenge is 400
  `bad_request`.
- **Catalog.** `GET /consumer/v1/catalog` with `Authorization: Bearer omc_…` (or `ppc_…`) answers the user's
  collection: projects with their repositories, primary first; groups; each project's derived state
  and up to 5 attention facts; the last syncs and their errors; `truncated`, always present, null or
  the counts left out; an ETag, and 304 for a matching If-None-Match, compared weakly. Errors are
  `{ error: { code, message } }` with the codes `bad_request`, `unauthorized`, `origin_not_allowed`,
  `not_found`, `method_not_allowed`, `host_not_allowed`, `rate_limited`, `internal` and `not_ready`.
- **Bounds.** Names 100 characters, descriptions 350, pull request titles 256, 2000 projects, 100
  repositories a project and 5000 in all, 5 attention facts a project, 8 MiB an answer.
- **The published fixtures through Halcyonic's reader:** the empty collection read as available with
  no projects; the small sample as available with 38 projects, marked a sample; the 503 as
  `unavailable` (`not_ready`); the 401 as `unauthorized`. Every result matched Halcyonic's contract.

## What Halcyonic does

- It reads the discovery file only when it is a regular file, not a link, owned by its own user and
  writable by nobody else, opened without following a link and checked again through the open handle.
- It sends a fresh challenge every time it connects, checks the instance id and the proof, compared in
  constant time, and only then sends the credential, only in its `omc_` or `ppc_` form.
- It reaches the instance over `node:http` on 127.0.0.1 at the named port: no proxy from the
  environment, no redirect followed, a timeout per request and the contract's 8 MiB cap on an answer.
- It maps the catalog to Halcyonic's `CatalogResponse`, `reported`: the sample flag from the catalog
  or the instance, the age from the syncs, and Omnorium's states and attention kinds as open
  words. Pull request links, accounts, the user and Omnorium's own repository ids do not cross.
  An answer that does not fit, such as a repository name that is not GitHub's, is `incompatible` as a
  whole. A refusal is said in Halcyonic's words by its code; Omnorium's message is not passed on.

## Not verified

- A live read against a running Omnorium (`OMNORIUM_CREDENTIAL_FILE` runs
  `src/live-omnorium.test.ts`), and so the control plane's use of the client, its credential
  file and the folder match against the person's real catalog and folders. They are tested against
  the stand-in and temporary folders only (`apps/control-plane/src/intelligence/catalog.test.ts`,
  `catalog-match.test.ts`, `http/catalog-route.test.ts`).

## Omnorium rename and dual contract support (2026-10-31)

- **What changed upstream:** On 2026-10-31 Omnorium (at `5c6d983`) accepted its ADR 0021
  (product rename to Omnorium) and ADR 0022 (consumer contract rename with one release of dual
  support): `shared/contract/consumer.ts` (sha256
  `dc1389f72d0c1cfb24e6b5a4ea64bf98339cfb7e792428c50e58c917ad4d775b`) now defines
  `CATALOG_CONTRACT` as `omnorium.catalog` 1.0 alongside `LEGACY_CATALOG_CONTRACT`
  (`phat-porcupine.catalog` 1.0) at the same `/consumer/v1` base URL; `consumer:create` issues
  `omc_` + 32 bytes in base64url while the server accepts both `omc_` and `ppc_` tokens; and
  discovery writes `consumer.json` to `OMNORIUM_HOME` if set, else `PHAT_PORCUPINE_HOME` if set,
  else both `~/.omnorium/consumer.json` and `~/.phat-porcupine/consumer.json`.
- **What Halcyonic now does:**
  - `defaultCatalogHomes` returns `[OMNORIUM_HOME]` when set, else `[PHAT_PORCUPINE_HOME]` when set,
    else `[~/.omnorium, ~/.phat-porcupine]`. `connect` tries the homes in order and moves to the
    next only when a home has no discovery file (`ENOENT`); an unsafe, unreadable or invalid
    discovery file stops the read with its own reason and never falls through to a later home.
  - `readDiscoveryFile` prefers `omnorium.catalog` major 1 and still accepts
    `phat-porcupine.catalog` major 1.
  - `checkCredential` accepts both `omc_` and `ppc_` prefixes followed by 32 bytes in canonical
    base64url.
  - The credential file name in Halcyonic's data directory is `omnorium-credential`, the
    contract literal is `source.system: 'omnorium'`, the control plane's reason messages name Omnorium,
    and the headset's on-screen source name (`GalleryText.Source`) is `"Omnorium"`.
- **What was run:** Stand-in tests only (`packages/integrations/omnorium/src/connection.test.ts`,
  `src/client.test.ts`, `src/wire.test.ts`, `apps/control-plane/src/intelligence/catalog.test.ts`,
  `apps/control-plane/src/http/catalog-route.test.ts`). Not read live: a live read still waits for
  the owner's yes to issue a consumer credential.

## On this Mac (2026-11-03)

- **Not running:** no Omnorium process; `~/.omnorium` and `~/.phat-porcupine` hold no discovery
  file, and `~/.halcyonic` holds no `omnorium-credential`. A control plane on this Mac answers
  `GET /api/catalog` `unauthorized` (`credential_missing`), checked on a scratch control plane; with
  a credential saved but Omnorium stopped the client answers `unavailable` (`not_running`), tested
  against the stand-in only (`packages/integrations/omnorium/src/client.test.ts`).
- **Upstream, read in place:** the owner's checkout at `5c6d983`; `shared/contract/consumer.ts`
  unchanged (sha256 `dc1389f7...`, as on 2026-10-31); of the four fixtures, `catalog-empty.json`,
  `catalog-sample-small.json` and `error-unauthorized-401.json` are unchanged, and
  `error-not-ready-503.json` (now `59c03d58...`) changed only in its message, which renames the
  product (`f085d6a`); Halcyonic never passes that message on.
- **The whole path, on Omnorium's own answers:** an opt-in test,
  `apps/control-plane/src/intelligence/omnorium-published.test.ts` (`OMNORIUM_CHECKOUT` names the
  checkout; the fixtures are read there, never copied), has the stand-in instance serve each
  published fixture and reads it as a headset's gallery does, through discovery and its proof, the
  credential, the client, the control plane's catalog source and `GET /api/catalog`. The small
  sample answered `available`, `reported`, marked a sample, with 38 projects, none linked and no
  folder suggested, and `project.link_catalog` to one of its repositories was rejected, "A sample
  catalog is never linked to your projects."; the empty collection answered `available` with no
  projects; the 503 `unavailable` (`not_ready`) and the 401 `unauthorized`, neither passing on
  Omnorium's message. Run on 2026-11-03, 3 of 3.
- **To read it live**, the owner runs Omnorium in its checkout (`pnpm dev`, signed in to GitHub
  and synced; `pnpm dev:sample` serves a labelled sample, which Halcyonic shows as one and never
  links) and saves a consumer credential as the runbook says
  ([LOCAL_DEVELOPMENT.md](../runbooks/LOCAL_DEVELOPMENT.md#connect-omnorium)). Halcyonic creates none.
