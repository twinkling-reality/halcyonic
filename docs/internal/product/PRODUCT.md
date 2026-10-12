# Product

Canonical product definition. "Halcyonic" is a working name and has not been legally or
commercially cleared; no technical decision depends on it.

## Thesis

Software development is moving from a person continuously editing code toward a person who
initiates, supervises, understands, evaluates and redirects autonomous coding work. Today's
interfaces for that work are still workstation interfaces.

Halcyonic is a spatial interface for directing and understanding software work. Projects and tasks
retain their identity while the person moves between a focused workspace, a compact dock and a
native panel beside other apps. Characters are optional representations of tasks, not prerequisites
for finding or controlling them. Runtimes remain interchangeable executors.

## The interaction to prove

1. Open directly into understandable project and task context, with actual status and supported
   next actions. A person must be able to find existing work without learning what a character means.
2. Inspect activity, changes, tests and source evidence while keeping the selected project and task
   clear. Ask for or direct work through the capabilities the runtime actually supports.
3. Minimize the entire workspace or move to another app, then return to the same task and ordinary
   unsent draft. Hidden controls and remembered approvals never remain authorized to act.
4. Where useful, place optional task characters deliberately in the room. Their identity and state
   must agree with the task list and detail.

The advantage to prove is understanding and directing real work with less lost context. Neither
characters, a 3D diagram nor a futuristic appearance establishes that advantage by itself.
[ADR 0029](../decisions/0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md)
records the presentation and continuity decision. Device usability is a separate acceptance test.

## What Halcyonic is not

VS Code in VR; a floating terminal; virtual monitors; a mascot for Claude, Codex or any vendor; a
3D agent dashboard; a generic chat avatar; Salidium or Seorak rendered in VR; a notification
system; a coding model; a replacement for Claude Code, Codex, OpenCode or similar runtimes; an
excuse to make charts 3D; a tutor with a VR skin. VR editors, multi-agent dashboards and 3D views
of agent sessions already exist, so none of these alone is differentiation.

## The object model

Work is the object; runtimes are executors. A **Project** holds **Workstreams**; a Workstream is a
meaningful unit of work (for example "Fix the authentication regression") and is what a character
represents. An **Execution** is one attempt by one runtime toward a Workstream, and a Workstream
keeps its identity across executions and runtimes. Details: [DOMAIN_MODEL.md](../architecture/DOMAIN_MODEL.md).

## Entry and navigation

The workspace is the front door, including return visits. Projects and Tasks remain discoverable
before the first task exists. Existing projects, Connect project and New project lead to the same
ordinary Project and Workstream model; creation does not require an introductory questionnaire.
The local companion may help develop an idea, but is an optional creation path.

Each task has a readable state and project context. Its detail exposes relevant Activity, Changes,
Tests, requests and supported actions. An approval or question receives enough room to read it
fully. Provider names identify evidence inside the relevant view; they are not the primary menu.
Unavailable evidence explains what is missing, without filler or invented features. A run ending
is not a claim that the task is accepted, correct or complete.

The composition follows the recorded design references through disciplined hierarchy, a coherent
frame, attached contextual controls and a clear reading field. The references supply visual and
interaction principles, not copied assets or fictional capabilities. Type, hand targets, source
provenance and complete request reading remain measurable constraints.

## Presentation and continuity

The focused workspace functions without characters, room scanning or a stage slot. Minimize
workspace clears its panels, peeks and optional agents into one dock. Restore retains selected
work and ordinary drafts, then obtains fresh state and input authority. Move workspace changes
that workspace; moving agents changes their confirmed placement. Neither guesses the location of
another app's window.

Optional agents are off by default. Their group has a deliberate visible place and body contact,
with a preview and explicit confirmation. Scene-derived placement must use actual observed
geometry. A manually chosen point is identified as user placement. If placement is lost, the app
asks for a place instead of silently recreating a floating arc. Hiding agents does not hide tasks.

Other apps opens the native hybrid panel, with an explicit route back to the Unity workspace.
The native surface needs independent control-plane connectivity while Unity is paused. Its commands
use existing capabilities and the same acceptance-versus-completion rules. One immersive app at a
time remains the platform constraint; an Android panel is not a claim that Unity characters render
inside another immersive scene. A failed transition cannot be displayed as a successful mode switch.

The versioned app-private continuation carries only connection-bound selection and an ordinary
unsent instruction draft. Changed pairing, journal or current execution invalidates foreign state.
No read proof, armed approval, secret answer or automatic send survives a handoff. New input and
current command checks are required before acting. Details: [XR_CLIENT.md](../architecture/XR_CLIENT.md).

These are the accepted experience requirements. A successful build or installation alone does not
establish their usability. The installed character-first build was rejected in use; the complete
workspace, optional placement and native control journey require fresh end-to-end validation.

## Understanding and outputs

Project understanding, real outputs and feedback are part of directing software work. The person
must be able to relate an explanation to evidence, understand its limits and retain the relevant
project/task context while asking for a change. A source import map alone does not explain runtime
behavior or prove security.

Halcyonic currently reads per-execution understanding and evaluation. Project architecture
exploration is not yet connected. Salidium's experimental project-map v0 describes committed files,
declared modules and imports, with C# at assembly/project membership level; it does not describe
component purpose or data flow ([source validation](../validation/salidium-project-map.md)). Optional
Why and How content is explanation, distinct from observed structure. Multi-repository explanation,
full source/diff inspection and output capture/feedback require separate source and access proof.

Capture from another app requires the platform's explicit consent, a permitted purpose, a preview
and an actual supported attachment path. A screenshot is not proof of source identity. Do not show
an active share/capture control until its full permission, review, delivery and return path works.

Usage is secondary work context. Provider limits retain source, observation time, account identity
when known and reset information. An unknown account or unavailable source cannot become a guessed
remaining budget.

## Collaboration policies (future)

"Build for me", "build with me" and "teach me while we build" are behavioral goals over the same
system, not separate products or modes. They become explicit modes only if UX research shows
modes are better. Learning must teach from the user's real code and evidence, never from hidden
model reasoning. It is not part of the first vertical slice.

## Neighbouring products

Halcyonic consumes their intelligence without absorbing their ownership. The dated integration
facts and checks are in the validation records.

- **Salidium** provides per-execution evidence, changed files, verification state and optional
  model-written Why and How explanation. It observes Claude Code and Codex; it does not control
  them. Halcyonic reads its versioned consumer contract v1 through the control plane
  ([validation](../validation/salidium-consumer-contract.md)). Its separate experimental map and
  execution links describe committed repository structure and exact-path change relationships;
  Halcyonic does not consume them, and they do not establish application behavior or security.
- **Seorak** tracks the performance of agentic development: usage, cost estimates, reliability
  and outcomes. It deliberately does not grade quality. Halcyonic reads per-execution cost,
  outcome and verification through Seorak's versioned integration API, joined to sessions
  Halcyonic launched. Its provider usage limits read was observed live once on 2026-09-30 from an
  unreleased build; that historical check does not establish current service availability or
  headset rendering ([validation](../validation/seorak-integration-api.md#provider-usage-limits-2026-09-30)).
- **Halcyonic** owns spatial representation, workstream navigation, runtime control, the
  compressed and expanded interaction, permissions and connectivity.

## First milestones

The foundation was built before the XR prototype, so headset presentation does not dictate the
provider-neutral backend.

1. **Truth layer (built).** Contracts, domain, journal, projection, command lifecycle, mock
   runtime, fixture replay, REST and WebSocket.
2. **XR shell (built, device refinement ongoing).** A Unity client consumes the realtime stream
   and renders workstream characters that react to status and attention, hands first.
3. **The defining interaction (built, device refinement ongoing).** Open a character into its
   workspace, act, collapse it back. A real approval has been observed on Quest.
4. **Real work (partly built).** OpenCode, Claude Code and Codex adapters exist; Salidium and
   Seorak read-through exists. A real Codex execution was read through both local daemons. Each
   project binds to one folder the host approves, and a client that never names a path has started
   real OpenCode and Codex work on a local model in a new folder through the control plane
   ([project-location.md](../validation/project-location.md)). The headset still needs to choose a
   folder and a complete live-start walkthrough.

The minimum viable product needs understandable project/task entry, hands-only interaction,
whole-workspace minimize/return, one real runtime, one supported action with a runtime-confirmed
result, and useful evidence from Salidium and Seorak. It also needs a simple path to create a project and begin its first
work in the headset. This does not require open-ended idea generation, a full IDE, multiplayer,
full learning, a cloud relay, every runtime, elaborate customization, autonomous merge or deploy,
or a complex voice assistant. How much of the new-project path fits in the first competition
release remains open.

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
