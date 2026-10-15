# Product

Canonical product definition. "Halcyonic" is a working name and has not been legally or
commercially cleared; no technical decision depends on it.

## Thesis

Software development is moving from a person continuously editing code toward a person who
initiates, supervises, understands, evaluates and redirects autonomous coding work. Today's
interfaces for that work are still workstation interfaces.

Halcyonic explores a different interaction model: **autonomous units of software work become
persistent, contextual spatial objects that move between a lightweight embodied form and a deep
work environment.**

## The mechanic to prove

1. Work exists in a **compressed** form: a small character (working term: "spirit") that shows,
   at a glance, that work exists, whether it is active, whether it needs you, and whether it
   finished or failed.
2. Opening it **expands** the same work into a focused workspace: objective, activity, code,
   diffs, tests, output, understanding, evaluation, and the controls to steer it.
3. The user understands, evaluates and acts, then collapses it back.

The transition must read as *the same work at a different level of detail*. Everything else is
secondary to proving that this mechanic is better than a desktop or phone for supervising agents.

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

## Intended experience

This section states intent.

Halcyonic explores VR as a working environment for agentic software development. A person should be
able to see and move among multiple projects and repositories, work with actual native VR or
desktop outputs, direct agents, and understand changes and performance without repeatedly losing
context. Salidium contributes understanding and evidence. Seorak contributes performance
measurements. Those belong in the same working experience. Context should hold when focus changes.

**Connect projects** and **Create a project** remain the intended entry. Connect projects chooses
existing projects and which work to show. Create a project makes an ordinary Project and its first
Workstream, from an idea the person brings or works out with a companion, and it does not add a
separate idea object or a separate shared-vision screen. The companion is a guide, not a
Workstream. A turn ending is not a claim that the project is correct. Which creation settings can
safely default remains open ([OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)).

## This checkout

[SYSTEM.md](../architecture/SYSTEM.md) and [XR_CLIENT.md](../architecture/XR_CLIENT.md) inventory
what this checkout's control plane and headset client do.

Projects are records in the journal. The Connect projects and Create a project entry has been
checked in editor renders and tests. A project binds to one folder the host approves
([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). An opened workspace
asks four questions: What is it doing?, Help me understand, What was checked?, and What do you need
from me?

The companion asks a local model on the computer and keeps the exchange on the headset
([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)).
[XR_CLIENT.md](../architecture/XR_CLIENT.md) describes the New project screens that show it.

Cross-repository navigation is not implemented. Salidium's experimental project-map v0 is not
consumed ([salidium-project-map.md](../validation/salidium-project-map.md)).

Usage left reads provider limits through Seorak. That read was live once, on 2026-09-30, from an
unreleased build, and does not establish a current daemon or headset rendering
([seorak-integration-api.md](../validation/seorak-integration-api.md#provider-usage-limits-2026-09-30)).
The fifth session in [quest-3-device.md](../validation/quest-3-device.md) read a limit and did not
confirm the headset's rendering with the wearer. A selected account's limit cannot be claimed until
Seorak can identify that account.

Whether a person can return hand focus from a desktop or media window without also triggering an
action remains open ([OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)). On 2026-09-29 the second Quest
session saw Virtual Display take focus, and Halcyonic stayed unfocused until it was brought forward
([quest-3-device.md](../validation/quest-3-device.md)).

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
   workspace, act, collapse it back. On 2026-09-29 a Quest session opened a workspace and completed
   an approval through the control plane
   ([quest-3-device.md](../validation/quest-3-device.md), second session). Later interface revisions
   have their own notes in that record. That session is dated evidence, and it is not proof the
   current build is finished.
4. **Real work (partly built).** OpenCode, Claude Code and Codex adapters exist; Salidium and
   Seorak read-through exists. A real Codex execution was read through both local daemons. The
   control plane can bind a project to one folder the host approves, and tests have started
   OpenCode and Codex on a local model in a new folder
   ([project-location.md](../validation/project-location.md)). A complete live start on a headset is
   not closed by the fifth session in [quest-3-device.md](../validation/quest-3-device.md), which
   that record calls partly.

The minimum viable product needs three characters, hands-only interaction, the compressed to
expanded transition, one real runtime, one real safe action with a runtime-confirmed result, and
real Salidium and Seorak value. It also needs a simple path to create a project and begin its first
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
- removing the characters changes nothing about comprehension or navigation;
- the expanded workspace is just a worse desktop;
- Salidium and Seorak add nothing useful in the workflow;
- runtime differences make normalized control impossible;
- multitasking fails on real hardware (networking, rendering, background behavior);
- target developers do not wear a headset when the work matters.
