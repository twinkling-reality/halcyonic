# Product

Canonical product definition. "Halcyonic" is a working name and has not been legally or
commercially cleared; no technical decision depends on it.

## In one sentence

Build software with AI agents in VR.

The sentence names the category, not the difference. The difference has to be demonstrated.

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

## Entry paths

The intended first choice is simple: **Connect projects** or **Create a project**. These are
product goals, not a description of the current headset build.

- **Connect projects.** Choose existing projects, which active work to show and how to show it.
  The chosen work appears as characters.
- **Create a project.** Make a project in the headset with an AI companion. Bring an idea and any
  parameters already in mind, or work them out together through conversation and simple choices.
  Start building on the spot when ready. This creates an ordinary Project and its first Workstream;
  there is no separate "idea" object.

Both paths lead to the same place: the person's projects and their work, represented by characters
that can be opened and directed. Creation should ask for the details needed to start real work in
ordinary language, with technical controls available to people who want them. It does not require
a separate "shared vision" screen or artifact. Which settings can safely default remains open.

## Intended experience

The first live visit needs a short greeting after the Mac host is connected. **Connect projects**
chooses which existing projects and work to show. **Create a project** lets a person describe a
specific idea or work it out with a companion, edit a short recap, then start its first real task.
The companion is a guide, not a Workstream character. These actions share one living space:
existing work stays visible and continues while a project is being created. On return visits the
space resumes selected projects without replaying onboarding.

The person's next move is usually to open a character and ask **What is it doing?**, **Help me
understand**, **What was checked?**, or **What do you need from me?** One clear next action follows
the answer. Salidium and Seorak remain named as sources of evidence inside the answer; they are
not the first navigation choices. Opening and closing keep the same Workstream identity. A turn
ending is not a claim that the project is complete or correct.

The primary ambient setting is passthrough with Halcyonic's full 3D characters in the room beside
an ordinary 2D media or Mac window. The person can watch or work in that window while the Mac's
agent work continues. When the window has input focus, Halcyonic's controls do not take hand
input. The person deliberately returns focus to open a character and act. The exact video-window
composition and hand focus return still need a Quest test. Another immersive app replaces
Halcyonic's 3D scene; a small 2D companion there is a later, separately validated capability.

An optional **Usage left** glance can show provider-reported limits through Seorak with the
window, reset and observation time. It is neither a Workstream status nor a required creation
step. A selected account's limit cannot be claimed until Seorak can identify that account.
Project architecture exploration is a later Salidium capability: its current consumer contract
covers an execution, not a codebase graph. Its existing Why and How explanation may support a
navigable execution flow when one was generated, clearly labeled as an explanation.

These paragraphs are target behavior. The detailed screen states, current implementation gaps,
and acceptance checks are in the owner's private experience blueprint. The headset now has a
first version of this entry: a low project rail, a welcome, Connect projects over the projects
Halcyonic's journal knows, More work, Create a project from a typed idea or a few fixed guided
questions with an editable recap, choosing where its files live from the folders the Mac allows,
and the four questions in an opened workspace ([XR_CLIENT.md](../architecture/XR_CLIENT.md)). It is
checked in editor renders and tests, not yet on a headset, and it has no discovery or attach. The
companion runs on a local model on the computer and keeps its exchange on the headset
([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md));
the control plane and the client core have it, and its headset screens wait for the redesign of
the headset's interface.

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
  ([validation](../validation/salidium-consumer-contract.md)). It does not yet provide a
  project-wide architecture graph.
- **Seorak** tracks the performance of agentic development: usage, cost estimates, reliability
  and outcomes. It deliberately does not grade quality. Halcyonic reads per-execution cost,
  outcome and verification through Seorak's versioned integration API, joined to sessions
  Halcyonic launched. Its provider usage limits read is built against Seorak's unreleased format
  and not yet observed live.
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
