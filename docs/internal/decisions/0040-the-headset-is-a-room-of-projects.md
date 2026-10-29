# ADR 0040: The headset is a room of projects: a gallery home, one screen per project, and moments from the task's character

- Status: Accepted on 2026-10-29 by the owner from a storyboard of the wearer's view ("Yes, build it"); nothing built;
  acceptance on the headset pending
- Date: 2026-10-29
- Supersedes in part [ADR 0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md) (the menu
  on one plane as the headset's main presentation) and
  [ADR 0034](0034-the-workspace-is-one-continuous-gentle-curve-centred-on-the-eyes.md) (the curve as the workspace),
  and [ADR 0029](0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md) (characters off by
  default);
  complements [ADR 0039](0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)

## Context

On 2026-10-29 the owner rejected the headset's live build view as designed without references: equal cards in one
column, a pastel per step kind, a coloured stripe on every card. The bar is the clarity of the Cursor, Claude, OpenCode
and Codex desktop apps, with the spatial layer of a film or game interface, and the experience should "almost feel
magical". The owner then asked that the work not be fixed to the middle of the view, that the design be researched
rather than guessed, that the home show every project the way the Wii showed its channels and YouTube's VR player shows
one screen with its controls out of the way, and that it suit a newcomer as well as someone with hundreds of projects.

A reference sheet was built from sources read on 2026-10-29: Apple's visionOS guidelines (a window about 2 m away;
ornaments parallel to a window, slightly in front, never wider), Meta's Horizon guidelines (targets of 48 dp and 60 dp
for primary actions, 12 mm apart; ray-cast surfaces 0.8 to 3 m away; a leash for small global items, never a head
lock), YouTube VR's help (a click shows the controls, a click or a wait hides them), Material 3's motion tokens, Radix
Colors' step roles, the Claude desktop docs (tool calls collapsed to one line, a +12 -1 count opening the diff), and
games and films for qualities only: the Wii Menu (equal live tiles; Start off until something playable is in), Destiny's
Ghost, Dead Space, Mirror's Edge, Death Stranding, Star Citizen's mobiGlas, Her. No footage was timed; durations come
from the platforms' stated tokens. The sheet and a 13-frame storyboard at the Quest 3's field (110 by 96 degrees,
Meta's figure) are private evidence; the owner accepted the storyboard on 2026-10-29, and a truth read of it shaped
the rules below.

## Decision

The headset's main presentation is a room. Every rule below keeps the kit's floors: text at least 14 dp as the eyes see
it (Body 18 dp, 1.125 degrees), targets 3.75 degrees and 12 mm apart, every word at least 4.5:1 on an opaque carrier,
words never on the room, state never by colour alone, Keep things still landing every state at once.

1. **Home is a gallery of projects.** Equal tiles on shelves, at reading distance, facing the person.
   - A newcomer sees Halcyonic's own Projects only ([ADR 0020](0020-a-project-works-in-one-host-approved-folder.md)):
     what they made, and Make something new as the first, equal tile.
   - Someone with many sees Needs you first, then one shelf per group from the catalog
     ([ADR 0039](0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)), the rest counted ("+42 more"),
     with Find a project and Make something new always at the top.
   - A tile shows a picture only from the host's own check screenshot, taken in its sandboxed headless browser, with
     its time; until that check exists, and for every other project, the name, the state and a neutral mark. Halcyonic's own states carry a state dot; a catalog state carries the catalog's mark on its own tile,
     as reported, never observed. A catalog project linked to no Project here says so, never that it is not on this
     computer, which Halcyonic cannot know; a linked project shows both lines when both exist, Halcyonic's observed
     one and the catalog's reported one, each with its own mark, so a reported line never hides an observed one.
2. **A picked project comes forward as one screen**, about 2 m away, with one bar beneath it: parallel, slightly in
   front, edges aligned with the screen and never wider. The bar holds the name, the runtime and the state, one primary
   action, Try it only once a check passed, and quiet doors to Steps, Why and How it went. It folds to a handle while
   nothing is wanted and returns on a point or a press. The screen shows the latest check screenshot, labelled with
   its time, once the host's check takes one; until then the project's name, state and neutral mark. A project that
   is not VR yet offers one clear action, Make a VR version, which only asks the agent; ready comes only after the
   host's check opened the VR version, and that check may render the laptop version in the host's sandboxed headless
   browser, which is a check, never play. Playing a VR game on the laptop is not decided here: agent-written code
   runs only in the headset's browser ([ADR 0037](0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)).
3. **The task's character is its home in the room** (a Workstream, never a vendor session or a catalog project, ADR
   0039), and it is shown by default: the owner accepted the room with characters beside the projects that have a task
   going and moments coming from them. ADR 0029's lesson stays whole
   ([ADR 0029](0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md)): the room is
   complete without characters, no action ever needs one (every action lives in a moment or the bar, never in the
   character), and a person can turn them off, when a small task mark beside the screen takes the character's place
   and everything reads the same.
4. **Moments come out of the character one at a time**, in one fixed slot beside it, with their actions beneath: sent,
   the runtime's report of a step, an observed change, a question, a check, a failure, ready. A new moment replaces the
   last; earlier ones stack behind as one edge and "Earlier: {n} more", which counts only the stacked moments. A moment
   is colourless except a question (amber), a failure (red) and a passed check (green), each with its glyph and words.
5. **Reading unfolds on demand.** Read it turns the moment into a reading surface about 1 m ahead of where the person
   looks, and Fold turns it back; the screen dims behind it.
6. **What the reading surface may claim.** While a turn runs, code is the runtime's report: drawn as reported, on a
   dotted gutter, with no added or removed marks and no caret, and Whole file unavailable. When the turn ends, the Mac's
   own reading ([ADR 0038](0038-the-mac-observes-each-turns-file-changes-itself.md)) confirms the file itself, as
   "Changed, 41 to 54 lines, seen 10:42", never its lines. Added and removed lines are shown only from a checkpoint
   diff when undo is on ([ADR 0036](0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)).
7. **Who did a thing is said.** Checked by your computer only when the host's own check ran it; a check the agent ran
   is the agent's report and says so ("Codex ran"). An approval carries ADR 0033's line in the form the runtime declares,
   that it may run outside its sandbox or still runs inside one
   ([ADR 0033](0033-approvals-declare-whether-commands-leave-the-sandbox.md)). An accepted request says it was sent,
   never that it started.
8. **At most two heads-up items**, small and low at the sides, on a leash that turns with the body after a dead zone,
   fading while the person moves; never locked to the head.
9. **Compact** is Halcyonic's own small panel beside another app on the existing route (ADR 0029): the character, the
   task, its state and the two-position switch, which only asks.
10. **One system.** A cool graphite ramp of eight opaque steps by role, one accent (kingfisher) for the one primary
    action and live work, amber, red and green only where they mean something; one type scale (Identity 30, Heading 22,
    Body 18, Label 15, Mono 16 dp); motion only on events: a moment pops in 500 ms with a small overshoot, a surface
    unfolds in 500 ms and folds in 250 ms, a move takes 600 ms. [STYLE.md](../product/STYLE.md) holds the values;
    [WORDS.md](../product/WORDS.md) the words.

What stays from ADR 0026: one MenuDirector, guarded routes, the file as the authoritative reading object, the footer's
fixed places, StateLanguage's words, and its reading and input safeguards. What gives way: the menu and a file side by
side on one plane, and ADR 0034's continuous curve, as the main presentation.

## Alternatives considered

- **Keep the continuous curve with a better look** (ADR 0034). Rejected by the owner's question: a permanent instrument
  in the middle is the laptop's screen carried into the room, and it is where the rejected live build view lived.
- **A permanent middle panel with moments around it.** Two things compete for the centre; one thing at a time loses.
- **Head-locked heads-up items.** Meta and Apple both advise against anchoring content to the head; reading surfaces
  and persistent status on the head tire people and hide the room.
- **Moments scattered where each event happens.** Rejected with ADR 0026 alternative D: fixed places are what make a
  spatial interface learnable.
- **Showing observed lines.** The Mac reads the files but keeps only sizes, times, hashes and line counts; keeping
  and sending contents is what ADR 0038 refuses on purpose.

## Consequences

- The room's parts are drawn as kit components with Editor renders checked against the floors (tokens, the moment
  model, the screen and its bar, moments, the gallery, the reading surface, heads-up items and compact); the headset's
  placement code puts them where this decision says (the gallery as home, the screen at about 2 m, the slot beside the character, the reading surface
  at 1 m, the leash). STYLE.md's composition sections for the plane and the curve are rewritten as those units land.
- New words (WORDS.md) and four open questions (OPEN_QUESTIONS.md): italics under Meta's guidance, how edits arrive
  during a turn, the Unity type face, and playing a VR game on the laptop, which needs an amendment to ADR 0037 first.
- The game "in the room" is a standing card with the project's name and state, carrying the check's screenshot once the
  host's check takes one, and Try it opening the build in the headset's browser
  ([ADR 0037](0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)); Halcyonic draws nothing that claims to show
  the live game inside its room.
- A storyboard is not headset proof. Revisit if the wearer finds the 2 m screen and 1 m reading surface uncomfortable,
  if the leash distracts while walking, or if a newcomer cannot find Make something new without help.
