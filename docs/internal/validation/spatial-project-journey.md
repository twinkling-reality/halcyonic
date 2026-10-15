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

Client-core build and aggregate Editor source compilation passed. Subsequent actual Unity
execution now passes the complete route at both reading sizes with a clean process exit. This
is synthetic renderer/interaction evidence; live project creation and wearer acceptance remain
separate requirements.

The first actual execution found a production sizing defect: nonfocused wizard review pages
used only field fit, allowing peripheral chrome below the existing 14 dp threshold. Spatial
MenuDirector page budgets now enforce the same apparent-text readable limit for every caller,
including wizard/review callers that do not pass focused:true. Legacy layout behavior is unchanged.
The actual rendered multi-page review and unchanged apparent-text thresholds reject the former
field-only behavior; no duplicate budget calculation substitutes for those scene checks.

The next actual run exposed a separate sizing error: Standard selected-project Tasks hid its
New task row because its budget modeled two full legacy columns. Spatial PageHeight now models
the actual narrow chooser rail and one reading body, or one standalone body for wizard/task
views, retaining sections only on standalone nonfocused pages. SpatialTop, SpatialBase, actual
Hero subject height and unified gaps are included. The same complete-field, readable-text and
occupied-character predicates still constrain the binary search. The visible Standard New task
route is the behavioral negative regression (the prior production caller failed it); no hidden
control dispatch or threshold change substitutes for the actual route and images.
