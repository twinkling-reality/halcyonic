# ADR 0013: Characters are bots whose eyes, motion and light carry state

- Status: Accepted (by the owner, 2026-09-29); amended by the owner's picks on 2026-10-08
- Date: 2026-09-29, amended 2026-10-08 (a feature on top, eyelids, a ring and rim in place of the
  halo, places that mean something, labels that grow when looked at, and a stage for what is
  current; then life: looking back, homes by project, thinking and doing, the agent's words, and
  turning to each other only over what two tasks share)

## Context

A workstream's compressed form is a small character that shows at a glance that work exists,
whether it is active, whether it needs its person, and whether it finished or failed
([PRODUCT.md](../product/PRODUCT.md)). State is never conveyed by color alone. The first headset
run used placeholder spheres whose color and bobbing carried state
([quest-3-device.md](../validation/quest-3-device.md)).

On 2026-09-29 the owner compared two directions in a lookbook, a single page that drew each
direction in all eight states and six at once in the headset's arc. It stays outside the
repository. Both directions were drawn from scratch; public bot avatars and an agent orb study
inspired them, and nothing was copied.

- **Bots:** a glossy, rounded body with two eyes and no mouth. Shape and hue are the character's
  own; the eyes, the motion and the light carry the state. While it works, its surface flows like
  the orb's.
- **Orbs:** one faceless sphere for every workstream. Color, flow and texture carry the state, the
  way a voice assistant's orb does.

The characters render on a Meta Quest 3 at 72 Hz, six at once, beside system windows that open
within about 2 m of the person ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)).
The competition's rules, which the owner shared the same day, add four constraints: keep essential
interface and interactions within a comfortable, narrower field of view that adapts across devices,
since a Quest 3S and Meta's glasses see less than a Quest 3; run at 60 frames a second or more;
show no brand names, logos or recognizable branded products; and work for a seated person, within
about 2 ft, without standing or reaching far.

On 2026-10-07 the owner shared references for the feel of the whole app
([headset-redesign-research.md](../validation/headset-redesign-research.md), "The owner's references
for the feel of the whole app"), for qualities only, never features. Among them was a reel of small AI
characters, each with its own design, that float, fly, go to places and change expression. Beside it,
the owner said Halcyonic's characters have "a weird blank aura", which lane V read as the wide glow
behind each. Old tasks also crowded the stage in the sixth headset session (2026-10-04,
[quest-3-device.md](../validation/quest-3-device.md)): 4 of the journal's 11 tasks were `unknown`
from earlier sessions, and one, named like a new project, read as if that project had failed. Lane
V's proposal "One kit, four forms" (2026-10-07, outside the repository) answered both, and on
2026-10-08 the owner picked its recommended options. The amendments below are those picks. The
proposal's own choices, such as the six features, the two ages and the 0.5 m/s, are lane V's, not
taken from any reference.

The same day the owner asked for more: in the reel, the characters "weren't just cool because of their
looks (which I saw you improved here) but because they felt like i.e. they could move around, they
could look at you and look away and talk to each other like real characters etc". Halcyonic's agents
never talk to each other, so lane V proposed the closest true things as the proposal's item 6, and
the owner agreed to its recommended set on 2026-10-08. Its choices are lane V's.

## Decision

The owner chose bots.

- A character is a glossy, rounded body with two eyes, no mouth, and at most one small feature on
  top: an antenna, two ears, a sprout, a crest or two horns.
- Identity is shape, feature and hue, derived only from the workstream id (`CharacterIdentity`), so
  a workstream looks the same in every session: one of eight outlines, one of six features (none
  among them), and one of eight hues in a deep or a light tone, 768 in all. The hues run from teal to
  pink and stay at least 25 degrees from the state colors: amber for needs you, red for failed, green
  for a finished turn. No feature looks like a state: nothing is cracked, crossed, fogged, ringed or
  amber.
- State lives in the eyes, motion, light and where the character is, and the written status stays
  under every character: its state's word always, with Practice, Demo or Last known beside it where
  they apply, and its title once the person looks at it. `CharacterCues` in the client core maps a
  `CharacterPresentation` to these cues; the Unity layer renders them and derives nothing.
- The eyes have lids. Open eyes blink now and then ([ADR 0027](0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md)'s
  Blink), and the lids say the rest of the state with the table below: lowered a little over the
  work, uneven when the state is unknown, heavy when nothing has been heard for a day.

| State | Eyes | Body | Light and surface |
| --- | --- | --- | --- |
| Working | Lids lowered a little, down on the task | Floats slowly above its ring on a thread of light, leaning toward the work | The orb's satin flow moves across it; its ring and rim in the holo tone |
| Running tests | Scan from side to side | Hovers above its ring | A ring sweeps around it; its ring and rim in the holo tone |
| Needs you | Wide, on the person | Comes once to the front of the desk, turns to the person, rises toward their eye level, then breathes | An amber ring and rim |
| Answered | As its state | One turn about its own axis when the agent confirms it has what the person sent (an answer, a decision or an instruction), then its next state | As its state |
| Turn finished | Closed | Settles on its ring, no celebration | A green ring and rim, red when its tests failed |
| Failed | Crossed out | Slumps on its ring | Cracks; a red ring and rim |
| State unknown | Lids uneven, looking around | Drifts a little above its ring | Fogged over; a dashed grey ring and a grey rim |
| Not heard from for a day, brought back from Earlier | Lids heavy | Still | Ghosted into a halftone of dots, as last known; no ring |
| Stopped | Flat | Frozen mid-motion | Dulled |
| Last known (not live) | As they were | Frozen | Ghosted into a halftone of dots |
| Not started, starting | Open; down on the task | Breathes; a quick bob at home | None |

- The characters stand on an arc of fixed slots 2.4 m away, beyond the system windows. By default
  60 degrees lie between the outermost, so every character and its labels stay within about 36
  degrees of where the person faced. The person looks at them and points at them from the seat;
  nothing needs them to stand or reach.
- Homes go by project: a project's characters share a stretch of the arc, as a team shares a table,
  and a new task takes the free slot nearest its project-mates. Homes never shuffle while a task is on
  the stage, since moving without a reason says nothing true; gaps close when the stage is next
  placed. A character that needs the person comes to the front of the desk rather than standing in the
  middle, as the next bullet says. Beside a window the groups split either side.
- Each slot is a character's home, marked by its ring. A character leaves home only for a place its
  state names: the front of the desk while something waits for the person, the side of its open
  file while the file is open, and the bar when it arrives on the stage or leaves it. Characters an
  open file would hide step aside along the desk and come back when it closes
  ([ADR 0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)). A
  character travels on a short arc at no more than 0.5 m/s, never across an open file, never nearer
  the eyes than the front of the desk and never past the edge of the field; under Keep things still
  it fades out and in instead (ADR 0027).
- Movement always means something true. A character works only while the agent reports work, comes
  toward the person only while a request or question waits, turns once only when the agent confirms
  what the person sent, and never moves faster or more as a sign of progress, which Halcyonic cannot
  know.
- A character looks back when the person looks at it. After the peek's dwell its eyes come to the
  person, then its body turns and lifts a little; when the look leaves it holds for Glaze's linger,
  then goes back to its work. Only a live character with open eyes answers: never one that couldn't
  finish, was stopped, can't be seen, is last known or is days old, since answering would suggest a
  live task; a finished one opens its eyes while looked at. Only a character that waits for the person
  looks at them unasked, so a character's look always means one of two true things: it needs the
  person, or the person is looking at it.
- While one of its agent's tool calls runs, as the task's view reports, a working character leans in,
  eyes down on the work; between tool calls it sits up and looks up to one side. Each pose holds at
  least 1.5 s, so a burst of calls never makes it flicker, and it never moves faster for more work.
  The view does not report a running tool call yet; that is a contract change of its own.
- Its glance shows, for working and finished work, the agent's last whole message, leaning on a ground
  with Agent says before it, at most 2 lines, cut first. A new message replaces it with Appear's fade,
  never as streamed or partial text, since the contract carries whole messages.
- Characters turn to each other only over something two tasks really share, because Halcyonic's agents
  never talk to each other. As one comes forward because it needs the person, its project-mates look at
  it once; as a task joins a project, its project-mates look at it once as it lands. Once a capability
  reports the files each task changed, two tasks of one project that changed the same file turn to each
  other, joined by a light line that names the file when the person looks at either; where an agent app
  can't report its files, its character never turns for one. Nothing chatters: no character faces
  another and moves as if talking with nothing behind it.
- The stage is for now: what waits for the person, what works, and what ended or went quiet
  recently. Work that ended leaves the stage 8 hours later, and work Halcyonic hasn't heard from 24
  hours after it last heard; what waits for the person never leaves. Tasks keeps both under Earlier,
  and the person can bring one back. In the demonstration nothing ages, since its clock is the
  recording's.
- Until the person first opens a file from a character on a headset, a character's glance carries a
  prompt at its foot: a key cap holding the pinch icon, then Open. It replaces the first-time hint's
  drawn fingers and "Look, then pinch" (lane V and the coordinator, 2026-10-08).
- Six characters are built to take little of a 72 Hz frame, above the rules' 60 frames a second:
  noise is baked into a texture instead of computed per pixel, each body is one pass, and nothing
  allocates per frame. The cost is estimated, not yet measured on a headset
  ([character-rendering.md](../validation/character-rendering.md)).
- Nothing on a character shows a brand: shapes, hues and eyes are drawn in code.
- A finished turn gets no celebration, because it proves nothing about correctness.
- Everything is code: meshes generated in C#, two shaders, and a noise texture baked at startup. No
  rigging, and no purchased or generated assets, so everything stays under the project's license.

## Alternatives considered

- **Orbs.** Every character has the same shape, so identity would rest on color alone, and state
  on color, flow and texture, against the rule that state is never conveyed by color alone. An orb
  has no gaze, and eyes that turn to the person are the clearest attention cue in a headset. The
  orb's flowing surface survives as the bots' cue for work in progress.
- **Keeping the placeholder spheres.** Color and bobbing speed told the states apart, and nothing
  told six workstreams apart except their titles.
- **Rigged or purchased character art.** Costs rigging work and licensing, and puts assets outside
  the project's license, for cues that procedural shapes and shaders already carry.
- **The halo, as it was or at half its size** (2026-10-08). A glow 4.4 body radii wide behind each
  character, which the owner called a blank aura; halving it keeps the aura.
- **Stations** (2026-10-08). Every motion in place, with no travel: calm, but a character that needs
  the person could only rise where it stood, and nothing could arrive or leave.
- **Today's 128 identities** (2026-10-08). In about a quarter of stages, two of six characters
  shared a shape and a hue.
- **Glancing at the person while working** (2026-10-08). Livelier, but an unasked look is how a
  character says it needs the person, so it would cry wolf.
- **Wandering about the desk, or chattering with each other** (2026-10-08). Both show something that
  isn't happening, and wandering pulls the eye at the edge of the view while the person works.
- **Homes by state, a float alone, the agent's words only in the file, a shared file only, or never
  turning to each other** (2026-10-08). The other options of item 6; each is calmer and says less.
- **Only the state's mark at rest, or the full label** (2026-10-08). The mark alone drops the written
  status this ADR keeps. The full label, the state and a title on two rows, put three lines under
  every character, 18 of the 48 pieces of text lane V counted in one view of a task opened on a
  desk.

## Consequences

- The cues are testable without Unity: no two activities differ only in color, a stale state keeps
  its cues and stops moving, a finished turn never celebrates, a turn comes only with the agent's
  confirmation, and a character leaves home only for a place its state names.
- Identities can repeat. Before the features there were 128, and among six time-ordered ids two
  shared a shape and a hue in about a quarter of stages. With a feature, two of six share a shape, a
  feature and a hue in about 1 stage in 25, if the hash spreads ids as evenly; a test over real ids
  checks it once the feature is derived. Their titles still tell them apart when the person looks.
- At rest, characters are almost still; working ones float slowly; only one that needs the person
  comes toward them. Whether that stays calm over a long session, in the corner of the person's eye
  while they code, is open ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- The halo's wide transparent quad gives way to a ring and a rim. They should cover less of the
  frame than the halo did, but the rim is one more transparent pass a character, and features,
  threads and trails add draw calls: about 20 more for six characters by lane V's estimate, not a
  count. Lane U's measure renders count them, and the headset session measures the frame.
- As amended on 2026-10-08, none of this is built yet. Lane U builds it in steps, each with its
  renders. The speeds, the two ages and the ring's cost are judged on the headset, where they may
  change.
- Characters with more life, moving around, looking at the person and looking away, and talking to
  each other like real characters, were asked for by the owner on 2026-10-08, and the owner agreed to
  lane V's recommended set the same day. Thinking and doing waits for the task's view to report a
  running tool call, and the shared file for a capability each agent app declares; both are contract
  changes with their own records, and the shared file may miss the competition build.
- Looks between characters, the two working poses and the look back add motion at the edge of the
  person's view, which the open question on calm over a long session now covers too.
- The arc does not adapt to a device's field of view at run time. Its default fits a Quest 3 and a
  Quest 3S; another device needs its span and distance tuned.
- Custom shaders must ship through materials that the build includes
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md)).
- Revisit if hardware sessions show that the characters are decoration, a kill condition, or that
  identities do not help people tell workstreams apart.
