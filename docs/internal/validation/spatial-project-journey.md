# Spatial selected-project creation route

2026-10-15. `WorkspaceRender.SpatialProjectJourneyCheck` renders the actual MenuDirector with
`SpatialWorkspace=true` at both text sizes. It uses a synthetic current RealtimeSession and an
in-memory draft store. No transport starts, runtime command is allowed, or user preference changes.

The route is Projects → selected-project Tasks → the visible New task row → actual creation flow.
It checks the existing project binding, enters a multi-paragraph draft through the real keyboard
callback, opens the recap and runtime selector, and opens the mandatory boundary review. Every
review part is rendered; confirmation must stay unavailable before the last part. Cancel retains
the selected project and submits nothing. This validates review presentation and cancellation,
not successful runtime execution or a creation receipt.

Each state reuses SpatialState's actual text, target, caption, complete field and footer checks.
It does not apply the legacy separated-card gap expectation to the unified composition, and does
not weaken any typography, field or target threshold. Settings/Appearance/Comfort have their own
spatial route fixture; whole pending questions and Results are covered by SpatialWorkspaceInteractionCheck.

Client-core build and aggregate Editor source compilation passed. Actual Unity execution and
visual review remain required; source compilation is not an asmdef or scene PASS.

The first actual execution found a production sizing defect: nonfocused wizard review pages
used only field fit, allowing peripheral chrome below the existing 14 dp threshold. Spatial
MenuDirector page budgets now enforce the same apparent-text readable limit for every caller,
including wizard/review callers that do not pass focused:true. Legacy layout behavior is unchanged.
The fixture independently compares a nonfocused spatial page budget with the readable bound and
requires the field-only bound to be larger, so the former caller behavior fails this regression.
Actual rendered multi-page review and unchanged text thresholds remain required.
