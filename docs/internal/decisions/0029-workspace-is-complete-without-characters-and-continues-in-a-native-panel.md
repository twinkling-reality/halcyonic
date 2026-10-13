# ADR 0029: The workspace is complete without characters and continues in a native panel

- Status: Accepted; implementation and device acceptance are separate checks
- Date: 2026-10-12
- Supersedes the mandatory character entry and closed-on-return behavior of ADR 0026, and the
  automatic whole-stage placement and floating fallback of ADR 0015. Their unrelated reading,
  input, room-permission and anchor constraints remain.

## Context

The installed character-first interface passed source, rendering and installation checks but was
rejected in use as confusing. Source inspection explains several causes: returning visits began
closed; some outer task routes requested and waited for a character; normal project/task
navigation was hidden before the first task; Compact retained only one composition; and the
window arrangements assumed an app's location without observing it. Desk placement grounded a
label rather than the character's body. These are interaction failures, not evidence that more
character decoration would make the workflow understandable.

The established [design references](../validation/headset-redesign-research.md) support a coherent
personal instrument, disciplined hierarchy, content attached to its context and a clear center of
view. They do not establish that characters must mediate every action or that fictional control
labels are usable. Quest permits one immersive scene at a time; a native hybrid panel is a
different surface, not a way to keep arbitrary Unity characters over another immersive app.

## Decision

The full workspace opens directly and works without any character, target or stage slot. It
exposes Projects, Tasks, current status, evidence and supported actions. Selected project/task
identity remains clear through detail, requests, minimize and platform transitions. Familiar
labels replace forced euphemisms; a run ending still never means that the task is accepted or the
project is correct. Live, demonstration, disconnected, reported and unavailable remain distinct.

Minimize applies to the entire workspace. Before hiding panels, peeks, input and optional agents,
one shared presentation state suspends action authority, keyboard/voice callbacks and armed
confirmations. A single dock restores the retained selection, views and ordinary drafts. Restore
does not restore read or confirmation authority. Work on the computer continues independently.
Closing a task view and minimizing the workspace are distinct operations.

Characters are optional representations of selected Workstreams. They are off by default and
appear only after deliberate placement. A visible shelf and actual body contact give the group a
comprehensible place. A room-derived proposal names only an observed surface; a user-placed point
does not become a detected desk. Lost placement does not silently create a floating arc. Enabling
agents or hiding them does not change domain work, runtime access or the selected projects.

Other apps invokes a real native panel in the same APK through the supported hybrid activity
route. That panel reads the control plane and issues only existing, declared commands through
the same authorization boundary. It must work while Unity is paused, preserve project/task
context and provide an explicit return. Launch failure remains a failure. Neither a fixed window
layout nor a read-only status list is acceptance of this interaction.

The surfaces exchange a versioned `WorkspaceContinuation` through bounded, atomic app-private
storage. Its schema is defined once in TypeBox and generates JSON Schema and C# bindings. A
private digest binds it to the connection and credential; a current snapshot validates journal,
project, workstream and execution relationships. It may contain an ordinary unsent instruction,
never credentials, question answers, armed approval, read proof or automatic send. Intent extras contain no
credential or draft. Runtime command acceptance remains distinct from confirmed effect.

## Alternatives considered

- Relabeling Compact and window arrangements retains their incorrect scope and assumptions.
- Making the stage prettier leaves navigation dependent on a representation the user may not want.
- Hiding projects to hide characters changes selected work and is not a presentation preference.
- A generic WebView or unverified overlay adds a new input/security surface without proving the
  required native lifecycle. The panel uses native controls and the existing command protocol.
- Keeping only live Unity objects cannot recover selected context after process recreation.
  Persisting all UI authority would recreate stale actions. The continuation deliberately carries
  view data, with fresh authorization required after recovery.

## Consequences

There are two presentations of the same work, with independent foreground lifetimes. Shared
schemas and cross-language fixtures must guard their agreement; native pinning, credential
handling and command gates require independent review. Optional stage placement and full-menu
geometry need new renders rather than tests that preserve the rejected composition.

Acceptance requires real tasks and platform transitions: fresh and returning launch, no-character
navigation, multiple retained drafts, whole-workspace minimize, deliberate placement and recovery,
native commands, another app and return. Capturing another app requires a separately verified
consent, permitted purpose, preview and supported attachment path. Source tests and screenshots
alone do not establish comfort, comprehension, capture permission or superiority to desktop work.

## Local installed-app links (2026-10-13)

The native panel may retain one explicitly chosen installed launcher component for a project on
this headset. This is local presentation metadata bound to the connection, journal and project;
it is not a domain execution, artifact, repository-to-build association or evidence of software
completion. Linking alone launches nothing. Open requires a fresh explicit action, the current
original selection and installed component, and successful private draft preservation. The launch
intent carries no project context, words, credential, attachment or command. It does not clear
another app's task. Failed or uncertain launches are not retried automatically.

The chooser queries only matching MAIN/LAUNCHER activities on demand, using a corresponding intent
visibility declaration. It requests neither all-package visibility nor installation authority.
Only the chosen component is retained; display labels and the catalog remain transient. Unavailable
discovery or launch leaves the existing Home route available. The installed build's correspondence
to source edits is always unknown in this scope. Implementation and headset acceptance remain
separate checks; see [native-project-output.md](../validation/native-project-output.md).
