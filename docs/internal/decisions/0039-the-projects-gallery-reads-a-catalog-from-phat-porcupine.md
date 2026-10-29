# ADR 0039: The projects gallery reads the person's catalog from Phat Porcupine, and Halcyonic never signs in to GitHub

- Status: Accepted; contract, link, gallery core, route and client built; live read not wired
- Date: 2026-10-29
- Complements ADR 0010 and ADR 0020

## Context

The owner, 2026-10-29: Halcyonic should be agentic development made enjoyable and easy for anyone with a
headset, children and people with no technical experience included, with everything organised. A person
with many projects across several GitHub accounts and organisations should see all of them in one place,
the ones being worked on and the ones that need them first; a newcomer with no GitHub starts from what
they make.

Halcyonic knows only the Projects created in it, each bound to one folder the host approves
([ADR 0020](0020-a-project-works-in-one-host-approved-folder.md)). It holds no GitHub credential and
makes no request to GitHub.

Phat Porcupine is the owner's separate GitHub portfolio app. From its source, read on 2026-10-29 without
running it: an instance registers its own GitHub App asking for read-only Metadata, Pull requests, Checks
and Commit statuses, never Contents, installable on every account and organisation; it syncs every
installation's repositories; a project holds several repositories with one primary, and projects sit in
named groups; one pure function derives each project's state (for example awaiting input, checks failing,
review requested, agent running, inactive), listing the signals it could not read rather than reading
them as zero; a fictional sample dataset is served from its own database and labelled. Its Node server
listens on 127.0.0.1 with a Host header guard. It has no API for a program other than its own web app.

## Decision

- **The gallery is the home.** It shows every project the person has, grouped as products, those that need
  the person first and those being worked on with their character. This states intent; the headset's
  composition is decided separately ([ADR 0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)
  and its amendments).
- **Halcyonic's own Projects are its floor.** The gallery always opens and always holds them, from the
  journal's projection. Nothing about it requires GitHub or Phat Porcupine, so a newcomer's gallery is
  what they made in Halcyonic.
- **Phat Porcupine widens it**, through a versioned, read-only consumer contract that Phat Porcupine
  publishes for programs on the same computer: a credential the person creates for Halcyonic and can
  revoke, scoped to one Phat Porcupine user; loopback only; a discovery file in the person's home whose
  instance id must match the served one before the credential is sent, as with Salidium's consumer
  contract; GET only. Halcyonic proposed the contract to Phat Porcupine; Phat Porcupine owns it.
- **The catalog is read through and never journaled**, as [ADR 0010](0010-external-intelligence-is-read-through.md)
  decides for Salidium and Seorak. Everything in it is `reported` by Phat Porcupine, never `observed` by
  the Mac, keeps Phat Porcupine's own wording of unknown, and never feeds a Workstream's status or
  attention. Every answer says whether Phat Porcupine served its sample data, and a sample is labelled
  wherever it appears and never mixed with real projects.
- **The person links a catalog project to a Halcyonic Project**, and that link is journaled, because it is
  the person's own fact. The link names GitHub's numeric repository id, its stable identity, beside the
  name Phat Porcupine reported at that moment. The host may suggest a folder for a catalog project by a
  named rule; a suggestion is `inferred` and is never applied without the person.
- **A character stands only for a Halcyonic Workstream.** Agent sessions Phat Porcupine reports are shown as
  its report and never animate a character, so one run is not drawn twice.
- **The headset reads the catalog only from the control plane**, never from Phat Porcupine, and treats
  every name, description and title in it as untrusted text.
- **One way.** Halcyonic reads Phat Porcupine and never writes to it.
- When Phat Porcupine is absent, stopped, refuses the credential or speaks an incompatible version, the
  gallery shows Halcyonic's own projects and one line saying which.

## Alternatives considered

- **Halcyonic signs in to GitHub itself**, with an OAuth device flow or a GitHub App of its own. It
  duplicates what Phat Porcupine already does (app registration, sealed and rotating tokens, sync across
  installations, product grouping, derived states), puts a GitHub credential in the control plane, which
  today holds only credentials for products on the same Mac, and makes the control plane call the
  internet.
- **Halcyonic runs the `gh` command line tool** with the person's existing sign-in. That sign-in usually
  carries the full `repo` scope, read and write access to code, far beyond a gallery's need, and gives no
  grouping or state.
- **Halcyonic reads Phat Porcupine's database.** Integrations never read another product's database
  ([INTEGRATIONS.md](../architecture/INTEGRATIONS.md)); there would be no consent, revocation or version.
- **Journal the catalog.** It would keep a second, stale copy of another product's facts and replay them
  as if they were Halcyonic's.

## Consequences

- The full catalog exists only while Phat Porcupine runs on the same Mac and the person has given
  Halcyonic a credential. Nothing of it is kept on disk by Halcyonic beyond the links the person made.
- Halcyonic gains a contract, a client package and a control plane read for the catalog, specified
  before Phat Porcupine publishes; until it does, they are tested against a stand-in only, and the
  live check is recorded in `docs/internal/validation/` when it runs.
- Suggesting a folder from a repository's git remote would be a new read of one file inside each listed
  folder; the listing reads no file's contents today ([SECURITY.md](../architecture/SECURITY.md)). It
  needs its own security review before it is built.
- The competition video and the judge build show a labelled, invented catalog stand-in, never a real
  person's project names unless the owner picks them, and never the names GitHub or Phat Porcupine.
- Getting a project that is not on the Mac onto it (a clone) is not part of this decision: it uses the
  network and the person's own git credentials and needs a decision of its own.
- Revisit if Phat Porcupine's real collection moves to a hosted instance: reading it would make the
  control plane call the internet with a per-person credential, which this decision does not cover.
