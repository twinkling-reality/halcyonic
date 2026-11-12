# The live walk

- **Question:** How many presses does a person's own journey take in the headset, through only what the redesign
  draws, beside the same journey in a terminal agent session at a desk?
- **Date:** 2026-11-12.
- **Environment:** b74's candidate b8de8ae6f (the redesign in every build) on an Apple M5 Max with macOS 26.7; Unity
  6000.3.25f1 in batch mode; a scratch control plane on loopback with its own data, task and project folders and
  only the mock runtime, its default scenario replaced by the one with an approval. No headset.
- **Method:** `WorkspaceRender.LiveWalkCheck` ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "The live walk in
  the Editor"). Each step it takes words where a keyboard is open, a control that asks for words where none were
  given, else the screen's main action, else a drawn control or row it has not tried; where the redesign draws
  nothing it says what the frame of record offers.
- **Status:** Two journeys walked through what is drawn on the newcomer fix; seeing and keeping changes not reached.

## What it found (run live-5)

Starting a project took 6 presses and 1 typed sentence and did not start: New Project, Type Idea, the idea typed,
Use Idea, then on the recap Change, Rename and Done, and back on the recap nothing more to press. The recap draws
Change and Close only. Its frame of record holds Start Building as its main action, unavailable with the reason
"Choose Run Settings", and the redesign draws neither the action nor the reason. The computer offered one runtime,
and none was chosen for the person. At a desk the same is 2: make a folder and start the agent in it, or type the
sentence and press Enter where a session already runs.

The approval, the result, the changes and keeping them were not reached, so their counts are not measured yet. The
mock works in no folder: seeing and keeping changes needs a folder runtime on the local model.

## With the newcomer fix (run live-9, 2026-11-12)

On XR's fd277f8ee and FIELD's d9c84c646 together, not yet on main (the one agent app taken for the person; the
recap draws Start Building), the first full journey went through what is drawn, beside a desk's:

| Journey | Headset | Desk |
| --- | --- | --- |
| Start a project | 6 presses and 1 typed sentence: New Project, Type Idea, the idea, the forward step, Start Building, Next Part, Confirm | 2 |
| Answer an approval | 2: Approve, then Confirm behind its lock | 1 |
| Read the result | 0: the task's file stands open | 0 |
| See what changed | not reached: nothing drawn leads there, the walk opened it | 1 |
| Keep it | not offered: the mock changes no folder | 0 to 1 |

The review pages after Start Building (Next Part, Confirm) draw the older views beside the new ones, and the waiting
approval is reached by nothing drawn: the walk opened it.

## Consequences

- The journey a person starts every piece of work with cannot be finished by what the redesign draws: the start
  and why it waits are not drawn, and the one runtime there is is not chosen by itself.
- The walk counts every press, so a change that removes a step shows in its count, and one that adds a step does too.
