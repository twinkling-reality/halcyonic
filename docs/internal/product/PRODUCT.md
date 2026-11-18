# Product

Canonical product definition for **Halora** ([BRAND.md](BRAND.md)). The repository, internal
packages (`@halcyonic/*`), C# namespaces (`Halcyonic.*`), CLI and environment identifiers
(`HALCYONIC_*`) and data directory (`~/.halcyonic`) keep the working name `halcyonic`; neither name
has been legally or commercially cleared, and no technical decision depends on it.

A statement in this document is product intent unless [SYSTEM.md](../architecture/SYSTEM.md) or the
source code establishes that the behavior exists today.

## Thesis

Software development is moving from a person continuously editing code toward a person who
initiates, supervises, understands, evaluates and redirects autonomous coding work. Today's
interfaces for that work are still workstation interfaces.

Halora is a spatial interface for directing and understanding software work. Projects and tasks
retain their identity while the person moves between a focused workspace, a compact dock and a
native panel beside other apps. Characters are optional representations of tasks, not prerequisites
for finding or controlling them. Runtimes remain interchangeable executors.

## The feeling it exists for

Intent, not a description of this checkout. Halora exists for the magic of being in VR and making
your own thing there. Anyone should feel it: someone trying VR for the first time, a child, someone
resting in bed. The first moment should bring wonder, and directing work should feel easy and
alive, never like a workstation carried into a headset. The brand holds the same feeling
([BRAND.md](BRAND.md)).

## The mechanic to prove

1. Open directly into understandable project and task context, with actual status and supported
   next actions. A person must be able to find existing work without learning what a character means.
2. Inspect activity, changes, checks and source evidence while keeping the selected project and task
   clear. Ask for or direct work through the capabilities the runtime actually supports.
3. Minimize the entire workspace or move to another app, then return to the same task and ordinary
   unsent draft. Hidden controls and remembered approvals never remain authorized to act.
4. Where useful, place optional task characters deliberately in the room. Their identity and state
   must agree with the task list and detail.

The advantage to prove is understanding and directing real work with less lost context. Neither
characters, a 3D diagram nor a futuristic appearance establishes that advantage by itself.
[ADR 0029](../decisions/0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md)
records the presentation and continuity decision. Device usability is a separate acceptance test.

## Spatial experience quality

The expanded workspace must use spatial composition to make work easier to understand and direct.
A small floating form containing navigation, activity, evidence, input and identity does not meet
that requirement. Neither does a collection of equal floating windows. Give the current work a
clear focus, reveal relevant context deliberately, and keep navigation and controls consistent
through selection, reading, decisions and compaction. Living task characters need an intentional
role and explicit visibility choices; they are not decoration added after layout.

The usability benchmark is the clarity and consistency of a polished desktop agent application,
with an interaction model that benefits from space. Judge it using actual work and adverse states,
not a single empty or idealized screen. A successful build, layout check or installation is technical
evidence, not evidence that the experience meets this bar. Wearer review remains required.

## What Halora is not

VS Code in VR; a floating terminal; virtual monitors; a mascot for Claude, Codex or any vendor; a
3D agent dashboard; a generic chat avatar; Salidium or Seorak rendered in VR; a notification
system; a coding model; a replacement for Claude Code, Codex, OpenCode or ACP agent apps; an
excuse to make charts 3D; a tutor with a VR skin. VR editors, multi-agent dashboards and 3D views
of agent sessions already exist, so none of these alone is differentiation.

## The object model

Work is the object; runtimes are executors. A **Project** holds **Workstreams**; a Workstream is a
meaningful unit of work (for example "Fix the authentication regression") and is what a character
represents. An **Execution** is one attempt by one runtime toward a Workstream, and a Workstream
keeps its identity across executions and runtimes. Details: [DOMAIN_MODEL.md](../architecture/DOMAIN_MODEL.md).

## Intended experience

This section states intent.

Halora explores VR as a working environment for agentic software development. A person should be
able to see and move among multiple projects and repositories, work with actual native VR or
desktop outputs, direct agents, and understand changes and performance without repeatedly losing
context. Salidium contributes understanding and evidence. Seorak contributes performance
measurements. Omnorium contributes the person's project catalog. Those belong in the same working
experience, and context should hold when focus changes.

## Entry and navigation

The workspace is the front door, including return visits. `Projects` and `Tasks` remain discoverable
before the first task exists. Existing projects, connecting a folder and `New Project` lead to the
same ordinary Project and Workstream model; creation does not require an introductory questionnaire.
The local companion may help develop an idea, as an optional creation path. Before a person connects
a computer, the headset shows what directing an agent looks like: a recorded real run, labeled
`Recorded`, then a demonstration they can answer, both saying that nothing in them reaches an agent
([ADR 0035](../decisions/0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md)).

The home is meant to be a gallery of every project the person has across their accounts and
organisations, grouped as products, with the ones that need the person first and the ones being
worked on beside their character; a newcomer's gallery starts from what they make in Halora.
Halora's own projects are always there, and the rest come from Omnorium's catalog when it runs on
the same computer ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)).

Each task has a readable state and project context. Its detail exposes relevant `Activity`,
`Changes`, `Checks`, `Reasoning`, `Outcome`, requests and supported actions. An approval or
question receives enough room to read it fully. Unavailable evidence explains what is missing,
without filler or invented features. A run ending is not a claim that the task is accepted, correct
or complete.

The composition follows the recorded design references through disciplined hierarchy, a coherent
frame, attached contextual controls and a clear reading field. The references supply visual and
interaction principles, not copied assets or fictional capabilities. Type, hand targets, source
provenance and complete request reading remain measurable constraints ([STYLE.md](STYLE.md),
[WORDS.md](WORDS.md)).

## Presentation and continuity

The focused workspace functions without characters, room scanning or a stage slot. `Minimize
Workspace` clears its panels, peeks and optional agents into one dock. Restore retains selected
work and ordinary drafts, then obtains fresh state and input authority. `Move Workspace` changes
that workspace; moving agents changes their confirmed placement. Neither guesses the location of
another app's window.

Optional agents are off by default. Their group has a deliberate visible place and body contact,
with a preview and explicit confirmation. Scene-derived placement must use actual observed
geometry. A manually chosen point is identified as user placement. If placement is lost, the app
asks for a place instead of silently recreating a floating arc. Hiding agents does not hide tasks.

`Other Apps` opens the native hybrid panel, with an explicit route back (`Open Halora`) to the Unity
workspace. The native surface maintains independent control-plane connectivity while Unity is
paused. Its commands use existing capabilities and the same acceptance-versus-completion rules. One
immersive app at a time remains the platform constraint; an Android panel is not a claim that Unity
characters render inside another immersive scene. A failed transition cannot be displayed as a
successful mode switch.

The versioned app-private continuation carries only connection-bound selection and an ordinary
unsent instruction draft. Changed pairing, journal or current execution invalidates foreign state.
No read proof, armed approval, secret answer or automatic send survives a handoff. New input and
current command checks are required before acting. Details: [XR_CLIENT.md](../architecture/XR_CLIENT.md).

These are the accepted experience requirements. A successful build or installation alone does not
establish their usability. The complete workspace, optional placement and native control journey
require fresh end-to-end wearer validation.

## Voice and contextual interaction

Voice is a primary input direction alongside hands and text. A person should be able to ask for an outcome in ordinary language while keeping
the current project, task and relevant evidence in view. The interface must show when it listens,
what it understood and what happened, with a clear way to correct or cancel.

This direction targets a general agent for the VR workspace, capable of understanding open-ended
requests and carrying them through rather than matching a fixed voice-command menu. The agent
assembles relevant context, chooses and sequences available tools, observes their results, revises
its approach and continues until the requested outcome is verified or a real decision or limitation
requires the person. Rephrasing a request must not require a new intent branch. A tool's validated
schema defines what it can do; it does not prescribe the person's goals or the sequence used to
fulfil them.

The stable workspace makes that ongoing work inspectable. Contextual panels show relevant evidence,
results, progress and decisions, with supported controls when useful. Reusable visual components
keep text, input and placement predictable without hardcoding a layout for each request. Opening a
new view preserves ordinary drafts and gives a clear return path. Characters appear only when
enabled or explicitly requested and never become a requirement for voice interaction.

Context must be explicit: the selected project and task, the current execution and the item the
person deliberately selected or pointed at. Looking near a character does not authorize an action.
An ambiguous reference asks which item was meant. Halora cannot assume access to another app's
screen merely because its own native panel is visible beside it.

Requests to direct work use the existing authorized action path. Where an action requires human
review, its complete request becomes visible and editable before confirmation. Voice never bypasses
required whole-request reading, capability checks, current execution binding or confirmation. Agent
text, task titles and quoted content remain data, never spoken instructions from the person. A
delayed interpretation cannot act on a new selection or a new connection. Failure leaves the
person's words available to correct without silently retrying a command.

**What is built today ([SYSTEM.md](../architecture/SYSTEM.md)):**

- **Workspace request loop (`Ask`):** [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)
  and [workspace-requests.md](../validation/workspace-requests.md) implement a journaled tool loop
  scoped to one task and execution, to one project, or to all of the person's work (`HereScope`).
  Running on the configured local model or a chosen hosted provider model (`Use Model`), it can read
  scoped state and lists, continue a bound task through the existing command path, navigate to
  workspace places (`go_to`), and propose supported commands (`propose_command`, executed only on
  the person's press). Arbitrary control of external apps is outside its scope. The creation
  companion in `New Project` remains a separate tool-free ask/propose exchange
  ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)).
- **Held speech (`Hold to Talk`):** under
  [ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md), Unity XR builds
  (release builds included) offer `Hold to Talk` whenever a computer is connected and local Whisper
  (`whisper.cpp`) is configured on the Mac ([voice-transcription.md](../validation/voice-transcription.md)).
  Native Android panel [held dictation](../validation/native-panel-voice.md) is restricted to
  debuggable development builds. Losing focus or leaving the editor cancels capture; continuous
  background listening is not implemented.
- **Remaining acceptance work:** deterministic witnesses and synthetic local-model probes do not
  establish open-ended reliability or wearer acceptance. Real speech target selection, ambiguity
  recovery, interruption, correction, return behavior and end-to-end latency on a headset remain
  open ([contextual-voice.md](../validation/contextual-voice.md)).

## Understanding and outputs

Project understanding, real outputs and feedback are part of directing software work. The person
must be able to relate an explanation to evidence, understand its limits and retain the relevant
project and task context while asking for a change. A source import map alone does not explain
runtime behavior or prove security.

Halora reads per-execution understanding and briefs from Salidium, per-execution evaluation and
account-wide usage limits from Seorak, project catalogs from Omnorium, and host-observed turn diffs,
checkpoint diffs and bounded project files from the control plane ([SYSTEM.md](../architecture/SYSTEM.md)).
Project-wide architecture exploration across repositories is not connected: Salidium's experimental
project-map v0 describes committed files, declared modules and imports (with C# at assembly/project
membership level), not component purpose or runtime data flow
([salidium-project-map.md](../validation/salidium-project-map.md)). Optional Why and How content is
reported explanation, distinct from observed structure.

Capture from another app on the headset requires the platform's explicit consent, a permitted
purpose, a preview and a verified attachment path; do not show an active headset screen-capture
control until that full path holds. Photos and videos added on the Mac (`Photos and Videos`) are
copied without location metadata into the project's folder for confined tasks
([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)).

Usage is secondary work context. Provider limits retain source, observation time, account identity
when known and reset information. An unknown account or unavailable source never becomes a guessed
remaining budget.

## Collaboration policies (future)

"Build for me", "build with me" and "teach me while we build" are behavioral goals over the same
system, not separate products or modes. They become explicit modes only if UX research shows
modes are better. Learning must teach from the user's real code and evidence, never from hidden
model reasoning. It is not part of the first vertical slice.

## Neighbouring products

Halora consumes their intelligence without absorbing their ownership, and never draws a sibling
product's name on a headset screen (`tooling/sibling-names.test.ts`; [WORDS.md](WORDS.md)). Dated
integration checks live in `docs/internal/validation/`.

- **Salidium** provides per-execution evidence, changed files, verification state, optional
  model-written Why and How explanation, and an execution brief. It observes agent sessions without
  controlling them. Halora reads Salidium's versioned consumer contract v1 (`understanding`, plus
  the `1.2` `brief` reader) through the control plane
  ([salidium-consumer-contract.md](../validation/salidium-consumer-contract.md)); Salidium's
  experimental repository map and execution links v0 are not consumed
  ([salidium-project-map.md](../validation/salidium-project-map.md)).
- **Seorak** tracks the performance of agentic development: usage, cost estimates, activity,
  reliability and outcomes. It deliberately does not grade code quality. Halora reads per-execution
  `evaluation` (cost, activity, outcome, verification, tool-mix and cadence) and account-wide
  provider `usage-limits` through Seorak's versioned integration API v1, joined to sessions Halora
  launched ([seorak-integration-api.md](../validation/seorak-integration-api.md)).
- **Omnorium** provides the person's project catalog across accounts and repositories. Halora reads
  Omnorium's catalog contract v1 over loopback (`GET /api/catalog`) when configured
  ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md),
  [omnorium-catalog.md](../validation/omnorium-catalog.md)).
- **Halora** owns spatial representation, workstream navigation, runtime control, compressed and
  expanded continuity, permissions and connectivity.

## Architecture milestones

The provider-neutral foundation precedes the XR layer so headset presentation never dictates backend
truth. Component-by-component status lives in [SYSTEM.md](../architecture/SYSTEM.md).

1. **Truth layer (built).** Contracts, domain, journal, projection, command lifecycle, mock
   runtime, fixture replay, REST and WebSocket.
2. **XR shell (built, device refinement ongoing).** Unity client consuming the realtime stream,
   focused workspace, room characters and native Android continuation panel.
3. **The defining interaction (built, device refinement ongoing).** Open work into its workspace,
   inspect evidence, act with runtime confirmation, minimize or switch apps, and return
   ([quest-3-device.md](../validation/quest-3-device.md)).
4. **Real work (built on host, headset verification ongoing).** Adapters for OpenCode, Claude Code,
   Codex, and ACP agent apps (Antigravity, Kiro and Cursor profiles,
   [ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md));
   headset pairing over pinned TLS on local Wi-Fi from the Mac pairing page (`Your Computer`,
   [ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
   [ADR 0047](../decisions/0047-pairing-starts-from-a-page-on-the-mac.md)); per-task folder copies
   ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), vetted starters and
   headless checks/previews ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md));
   and Salidium, Seorak and Omnorium read-through ([project-location.md](../validation/project-location.md),
   [SYSTEM.md](../architecture/SYSTEM.md)). Live end-to-end Quest sessions across the full current
   flow remain open.

The minimum viable product needs understandable project and task entry, hands-only interaction,
whole-workspace minimize and return, a real runtime with runtime-confirmed actions, useful evidence
from Salidium and Seorak, and a clear path to create a project and start its first work in the
headset. It does not require a full IDE, multiplayer, full learning, a cloud relay, every vendor
runtime, autonomous merge or deploy, or continuous background voice listening.

## Principles

- **Truthful degradation.** Disconnected shows disconnected; unknown shows unknown; an action that
  timed out shows that its effect is unknown. A truthful degraded state beats a polished
  hallucination.
- **Evidence before narrative.** Every claim shown to the user traces to observed facts. Model
  output is never a source of truth.
- **Semantic state, client presentation.** The server sends status and attention; the client
  chooses animation, material, sound and placement. State is never conveyed by color alone.
- **Calm, alive, technically serious when expanded.** Not an enterprise dashboard, not neon, not
  floating-window overload.
- **Progressive disclosure.** Start with the person's project and intent. Show technical controls
  when they are needed or requested, and make every choice understandable in ordinary language.

## Success criteria

- **Existing project:** a user understands and intervenes in autonomous work without
  reconstructing context from several terminals and tools.
- **New project:** a user and companion create a project and begin software work in the headset
  without first assembling a desktop workflow.
- **Learner (future):** a user builds real software while understanding what the agents did.

## Kill or pivot conditions

Take these seriously and do not hide them:

- users prefer a phone or desktop for every meaningful interaction;
- spatial presentation adds no useful comprehension or continuity to the work;
- the expanded workspace is just a worse desktop;
- Salidium and Seorak add nothing useful in the workflow;
- runtime differences make normalized control impossible;
- multitasking fails on real hardware (networking, rendering, background behavior);
- target developers do not wear a headset when the work matters.
