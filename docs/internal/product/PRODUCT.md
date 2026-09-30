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

- **Connect projects.** Bring in existing projects and work already running on the user's machines.
  Choose which projects and work are visible, and how they appear. Meaningful work appears as
  characters.
- **Create a project.** A person and an AI companion develop a shared vision together in the
  headset. The person can arrive with an idea and parameters, or explore possibilities with the
  companion. The companion asks useful questions, offers choices and makes its current
  interpretation visible and correctable. The person can start building on the spot once the
  vision is clear enough, then continue refining it while work progresses. The result is an
  ordinary Project with Workstreams, not a separate "idea" architecture.

The default path uses language about the thing being made. Runtime, model and working-directory
choices must not be a prerequisite to expressing an idea; people who want those controls can
reach them. How the companion appears, how the shared vision is represented, and which safe
defaults can start real work remain open design questions.

## Collaboration policies (future)

"Build for me", "build with me" and "teach me while we build" are behavioral goals over the same
system, not separate products or modes. They become explicit modes only if UX research shows
modes are better. Learning must teach from the user's real code and evidence, never from hidden
model reasoning. It is not part of the first vertical slice.

## Neighbouring products

Halcyonic consumes their intelligence without absorbing their ownership. What each actually
provides was audited on 2026-09-26; see the validation records.

- **Salidium** understands and visualizes what agent work did: evidence-linked reports, changed
  files, verification state, rewind, and model-written Why and How diagrams. It observes Claude
  Code and Codex; it does not control them. Its local API is private and unversioned, so no
  integration is built yet ([audit](../validation/salidium-integration-audit.md)).
- **Seorak** tracks the performance of agentic development: usage, cost estimates, reliability
  and outcomes. It deliberately does not grade quality. Halcyonic reads it through Seorak's
  versioned integration API, joined to the sessions Halcyonic launched.
- **Halcyonic** owns spatial representation, workstream navigation, runtime control, the
  compressed and expanded interaction, permissions and connectivity.

## First milestones

The foundation (the provider-neutral truth layer) comes before anything visual, so that XR
prototypes cannot dictate the backend.

1. **Truth layer (built).** Contracts, domain, journal, projection, command lifecycle, mock
   runtime, fixture replay, REST and WebSocket.
2. **XR shell (next).** A Unity client consumes the realtime stream and renders three workstream
   characters that react to status and attention, hands first.
3. **The defining interaction.** Open a character into its workspace, act, collapse it back.
4. **Real work.** One real runtime end to end, one real Salidium capability and one real Seorak
   capability, each only after its integration contract is settled
   ([OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)).

The minimum viable product needs three characters, hands-only interaction, the compressed to
expanded transition, one real runtime, one real safe action with a runtime-confirmed result, and
real Salidium and Seorak value. It also needs a small, credible new-project loop from a shared
vision to first work. This does not require open-ended idea generation, a full IDE, multiplayer,
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
- **New project:** a user and companion form a visible, revisable shared vision and turn it into
  active software work without first assembling a desktop workflow.
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
