# Entry journey render witnesses

- Date: 2026-10-13.
- Question: Do the empty-computer and recorded entry render journeys exercise ADR 0029's actual
  open Projects workspace, normal navigation and whole-workspace restore rather than the superseded
  automatic first question, mandatory character arc and raised stage banner?
- Method: Source comparison of `MenuDirector`, its production `MenuNavigator` construction,
  `WorkspacePresentationState`, the two Editor journeys and ADR 0029. No runtime, microphone,
  model, device or Unity job was run by this source-edit lane.
- Status: Source migration prepared; actual Unity render and full integration results are separate
  gates and are not claimed by this record.

`WorkspaceRender.EntryJourneysCheck` runs these same four journeys at both real font sizes in the
Quest 3S field and writes `Builds/EntryJourneyRenders`. It reports each journey's failures without
skipping the other cases, restores the field/font setup and fails the Editor process for any error.
The existing full render gate continues to run the same journeys separately.

`RenderFirstVisit` starts an explicitly synthetic empty live-origin projection in the real menu
director. It checks open Projects, offered folders, all four normal navigation sections and current
selection through Tasks, Usage and Settings. Actual header Minimize and dock Open workspace pointer
events suspend and restore the retained Settings column. A hidden previous menu frame cannot act.
The visible Projects New project prompt then opens the real creation wizard at Your idea with its
offered held input; this render never starts microphone capture. Opening or navigating emits no
command. The standalone first-question model and its unit tests remain unchanged; production
`MenuDirector` supplies no first-question factory, so that reusable wizard is not automatic entry.

`RenderDemoWelcome` retains the real demonstration recording and its fixture-origin journal.
It checks the actual visible header's Demo provenance, full Projects navigation and an explicit
New project entry. Recorded attention changes the Tasks marker without opening work or enabling
agents. Offered project/task rows open a real retained `FileColumn` with Demo provenance, without
character targets. Header Minimize and visible dock restoration retain its exact menu/task identity
and reject old hidden input. Projects and its real creation wizard remain explicitly reachable;
recorded creation offers no microphone and navigation/opening emits no command.

Both journeys pass every capture through the unchanged `PlaneState` checks, retaining the normal
target, typography, layout, outside-text, provenance and field geometry invariants. Their synthetic
character arcs and banners have been removed because neither is present in default production
entry under ADR 0029. Optional shelf/agent geometry has its separate production witnesses; this
does not remove those checks or relax any geometry tolerance. The witnesses assert that default
entry has no character targets or stage banners.

Input routes inspect `MenuPlane`'s actually drawn frame and call the guarded director route. They
do not regenerate navigator frames while observing them, which would change frame identity beneath
input. Pointer helpers explicitly advance only the button settling clock, so these witnesses do
not claim elapsed-time settling, hand comfort or real headset gesture behavior. Both placements and
both text sizes remain in the existing full render gate. No production code changes accompany this
fixture migration. Headset acceptance and complete render execution remain required.
