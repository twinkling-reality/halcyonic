#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuPlane
    {
        private MenuFrameView activityContext = null!;
        public WorkspaceContextView Context { get; private set; } = null!;
        public MenuFrameView? ActivityContext => activityContext.gameObject.activeSelf ? activityContext : null;
        public bool SeparateWorkspace { get; set; }
        public MenuFrame? ActivityFrame { get; set; }
        public bool ComposerFocused { get; set; }
        public IReadOnlyList<ProjectView> ContextProjects { get; set; } = Array.Empty<ProjectView>();
        public string? ContextSelectedProject { get; set; }
        public object? ContextBinding { get; set; }
        public event Action<string, object>? ContextProjectPressed;

        /// <summary>
        /// The spatial kit standing in for the task's file in the independent workspace (D3.4), while
        /// <see cref="KitWorkspaceHost.Enabled"/> and the kit can draw the file's frame whole; the file's own views otherwise.
        /// </summary>
        public KitWorkspaceHost Kit { get; private set; } = null!;

        /// <summary>The kit bound to the active file's frame, set by the director before each draw; null where the kit can't stand in.</summary>
        public KitFileBinding? KitBinding { get; set; }

        /// <summary>Whether the kit draws the task's file now, in place of its views.</summary>
        public bool KitShows => Kit.gameObject.activeSelf && !Kit.ShowsCompact;

        /// <summary>Whether the kit's compact presence stands where the closed bar would (D3.5).</summary>
        public bool KitCompactShows => Kit.ShowsCompact;

        /// <summary>The context owner the kit was last shown with, carried back on its context rows.</summary>
        public object? KitOwner { get; private set; }

        /// <summary>A context row of the kit's spine pressed, by its key, with the owner it was shown for.</summary>
        public event Action<string, object>? KitContextPressed;

        /// <summary>The kit has drawn the file's frame and every control has settled: the frame's draw, as a view's Drawn is.</summary>
        public event Action<MenuColumn, MenuFrame>? KitDrawn;

        /// <summary>The kit composer's environment selector pill pressed while the kit stands settled.</summary>
        public event Action? KitEnvironmentPressed;

        /// <summary>The kit composer's project selector pill pressed while the kit stands settled.</summary>
        public event Action? KitProjectPressed;

        /// <summary>The kit composer's agent selector pill pressed while the kit stands settled.</summary>
        public event Action? KitAgentPressed;

        private PlanePart kitPart;
        private (MenuColumn Kind, MenuFrame Frame)? pendingKitDraw;

        /// <summary>The kit takes a press only where the file's view would: settled, still, standing, and input not suspended.</summary>
        private bool TakesKit() => !FocusGuard.InputSuspended && !Repositioning && !Sliding && (KitShows || KitCompactShows) && Kit.Settled;

        private void CreateKit(Transform root)
        {
            Kit = KitWorkspaceHost.Create(root);
            Kit.Accepting = TakesKit;
            Kit.Voice = () => Voice?.Stage ?? VoiceStage.Idle;
            Kit.MicLevel = () => HoldToTalk.CurrentLevel;
            Kit.Acted += (kind, action, key, frame, side) => { if (TakesKit()) Acted?.Invoke(kind, action, key, frame, side); };
            Kit.HoldStarted += (kind, prompt, frame, side) => { if (TakesKit()) HoldStarted?.Invoke(kind, prompt, frame, side); };
            Kit.HoldEnded += (kind, prompt, letGo) => HoldEnded?.Invoke(kind, prompt, letGo);
            Kit.ContextRow += (key, owner) => { if (TakesKit()) KitContextPressed?.Invoke(key, owner); };
            Kit.EnvironmentPressed += () => { if (TakesKit()) KitEnvironmentPressed?.Invoke(); };
            Kit.ProjectPressed += () => { if (TakesKit()) KitProjectPressed?.Invoke(); };
            Kit.AgentPressed += () => { if (TakesKit()) KitAgentPressed?.Invoke(); };
            Kit.Drawn += ForwardKitDrawn;
            // The connected switch asks for the existing routes only: Compact as a frame view's Compact, Workspace as the closed bar's Open.
            Kit.CompactPressed += (kind, frame) => { if (TakesKit() && KitShows) CompactPressed?.Invoke(kind, frame, null); };
            Kit.Opened += () => { if (TakesKit() && KitCompactShows) Opened?.Invoke(); };
        }

        /// <summary>
        /// Shows the kit's compact presence in the closed bar's place, naming what the bar would (<paramref name="identity"/>, or
        /// the place's closed line, the task's state, the tasks waiting), while the kit is switched on in the spatial workspace and
        /// the bar would show no portrait; returns false, showing nothing, where the bar stands instead. A presence newly shown
        /// whose switch was never told the presentation (as at start) stands at Compact at once; otherwise the thumb has
        /// already been sent there by the confirmed presentation (<see cref="KitWorkspaceHost.ShowConfirmed"/>).
        /// </summary>
        private bool ShowKitCompact(MenuBar menuBar, LineWords? identity, StateBadge? state, int waiting)
        {
            if (!KitWorkspaceHost.Enabled || !SpatialWorkspace || SelectedPortrait != null) return false;
            Kit.ShowCompact(KitPresenter.PresenceOf(identity ?? LineWords.Ours(menuBar.ClosedLine), state, waiting));
            if (!Kit.Presence.Switch.ConfirmedCompact) Kit.ShowConfirmed(true, immediately: true);
            KitOwner = null;
            return true;
        }

        /// <summary>The compact presence's own band, about the bar's placed part, as the plane's reading bands.</summary>
        private void KitCompactBands(PlanePart placed, float zoom)
        {
            ContextTextBands = Array.Empty<TextBand>();
            ReaderTextBands = Kit.TextBands.Select(band => new TextBand(placed.Right + band.Left * zoom, placed.Right + band.Right * zoom,
                placed.Up + band.Bottom * zoom, placed.Up + band.Top * zoom, band.EmDegrees)).ToArray();
            ComposerTextBands = Array.Empty<TextBand>();
        }

        /// <summary>The kit's draw, held while the plane moves and passed on once it stands, for the frame the kit still shows.</summary>
        private void ForwardKitDrawn(MenuColumn kind, MenuFrame frame)
        {
            if (Repositioning || Sliding) { pendingKitDraw = (kind, frame); return; }
            if (KitShows && ReferenceEquals(KitBinding?.Frame, frame)) KitDrawn?.Invoke(kind, frame);
        }

        private void FlushKitDraw()
        {
            if (pendingKitDraw is not (MenuColumn kind, MenuFrame frame) || Repositioning || Sliding) return;
            pendingKitDraw = null;
            ForwardKitDrawn(kind, frame);
        }

        /// <summary>
        /// Draws the active file through the kit, where the kit can draw its frame whole: the kit in one column of its size,
        /// the file's view kept as the column of record, laid for its frame and hidden, the context column and passive
        /// Activity hidden, the readability bands the kit's own. Returns false, drawing nothing, where it can't.
        /// </summary>
        private bool ShowKitWorkspace(MenuFrame? fileFrame, MenuColumn kind, SidePanel? panel, object? fileColumn, IReadOnlyList<CharacterTarget> all,
            Vector3 looking, float? surfaceHeight, (float Yaw, float Elevation) moved, bool immediately)
        {
            // Under the redesign a side panel is part of the frame its reader draws (RoomBar.General); the older kit declines it.
            if (!KitWorkspaceHost.Enabled || panel != null && !RoomDesign.Enabled || fileFrame == null || ContextBinding == null
                || KitBinding is not KitFileBinding binding || !ReferenceEquals(binding.Frame, fileFrame) || !Kit.Holds(binding))
                return false;
            var zoom = GlazeText.Scale;
            var kitWas = KitShows;
            foreach (var view in frameViews) view.Hide();
            Context.Hide();
            // The file's view stays the column of record (Showing, identity), laid for this frame but never shown; for the menu's
            // own frame under the redesign, the menu's view is.
            var record = kind == MenuColumn.Menu ? menu : file;
            record.SpatialWorkspace = true; record.IndependentSurface = true; record.PassiveActivity = false;
            record.OrganizationControls = false; record.ExternalPlaces = false; record.HeadingOverride = null;
            record.Show(fileFrame, Glaze.Menu.FileColumnDegrees, 0f, false);
            record.Hide();
            Kit.Show(binding, kind, ContextBinding);
            // As the presence at Compact: a switch never told the whole workspace stands there at once.
            if (Kit.View.Spine.Switch.ConfirmedCompact) Kit.ShowConfirmed(false, immediately: true);
            KitOwner = ContextBinding;
            Composition = new PlaneComposition(new[] { new PlaneColumn(Kit.Size.x, Kit.Size.y) }, zoom);
            Placed = PlaceWorkspace(all, looking, surfaceHeight, Composition.Size);
            if (RoomDesign.Enabled)
            {
                // The redesign stands by its foot line, not where the head pitched (BUILD-SPEC-v4 1.1): the plane faces the person
                // the way they look, tipped so the line the pair stands on is at DESIGN's pitch, the pair growing upward from it
                // and the row and the band hanging under it. The person's own move still turns it from there.
                var footed = new PanelDirection(Placed.Yaw, RoomGeometry.FeetPitch - SpatialKit.DegreesTo(Kit.FootLine * zoom), true, false);
                Placed = new PanelDirection(footed.Yaw, footed.Elevation, WorkspacePlacement.Clears(footed, bodies, Composition.Size), false);
            }
            Moved = moved; Direction = MenuDrag.Turned(Placed, moved);
            shown.Clear(); shown.Add((kind, record));
            shownFileColumn = fileColumn;
            MenuAside = false; FileAside = false;
            Chrome.Hide(); bar.Hide(); cover.SetActive(true);
            kitPart = Composition.Parts.First();
            SlideTo(Kit.Part, kitPart, zoom, null, immediately || !kitWas);
            // The kit's bands are its spine's, its slate's and its rail's, about its centre at the standard size.
            TextBand At(TextBand band) => new TextBand(kitPart.Right + band.Left * zoom, kitPart.Right + band.Right * zoom,
                kitPart.Up + band.Bottom * zoom, kitPart.Up + band.Top * zoom, band.EmDegrees);
            var bands = Kit.TextBands.Select(At).ToArray();
            // Under the redesign the bar's one band is what is read and written in; there is no spine (ON-1).
            ContextTextBands = RoomDesign.Enabled ? Array.Empty<TextBand>() : bands.Take(1).ToArray();
            ReaderTextBands = RoomDesign.Enabled ? bands : bands.Skip(1).ToArray();
            ComposerTextBands = RoomDesign.Enabled ? bands : bands.Skip(1).Take(1).ToArray();
            contextAllocation = kitPart; ReaderAllocation = kitPart; ComposerAllocation = null;
            lineTo = null; lineColumn = -1;
            Advance(0f);
            return true;
        }
        public event Action<MenuPlace, object>? ContextPlacePressed;
        public event Action<object>? ContextWorkspacePressed;
        public static float PortraitClearance => MenuFrameView.PortraitWidth * 0.75f;
        public IReadOnlyList<Vector3> WorkspaceCorners => VisibleCorners() ?? Array.Empty<Vector3>();
        public Vector3 WorkspaceCentre => PlaneLayout.PointOf(eyes, Direction, 0f, 0f);
        public static float IndependentTop => PlaneComposition.Units(18f);
        public static float IndependentEnvelope(float height, float subject = 0f, bool portrait = false)
        {
            var portraitOverhang = portrait ? Mathf.Max(0f, (MenuFrameView.PortraitHeight - subject) / 2f) : 0f;
            return 2f * Mathf.Max(IndependentTop + portraitOverhang, height - IndependentTop);
        }
        private PlanePart contextAllocation;
        public PlanePart? ReaderAllocation { get; private set; }
        public PlanePart? ComposerAllocation { get; private set; }

        /// <summary>Where the context column's text can stand, as the independent workspace was last laid (<see cref="IndependentTextBands"/>).</summary>
        public IReadOnlyList<TextBand> ContextTextBands { get; private set; } = Array.Empty<TextBand>();

        /// <summary>Where the reading part's text can stand, Activity's beside a composer, as last laid.</summary>
        public IReadOnlyList<TextBand> ReaderTextBands { get; private set; } = Array.Empty<TextBand>();

        /// <summary>Where the composer's text can stand, as last laid; none when no composer was invoked.</summary>
        public IReadOnlyList<TextBand> ComposerTextBands { get; private set; } = Array.Empty<TextBand>();

        /// <summary>
        /// The independent workspace's actual parts on <paramref name="composition"/>: the context
        /// column at the left column's left, its top at <see cref="IndependentTop"/>, and the reading
        /// parts down the right column from the same top, a part gap apart. The plane lays exactly
        /// these, and the reading model measures text in them.
        /// </summary>
        public static (PlanePart Context, PlanePart[] Reading) IndependentParts(PlaneComposition composition, Vector2 context, IReadOnlyList<float> heights)
        {
            var zoom = composition.Zoom;
            var left = composition.Parts.First(part => part.Column == 0);
            var right = composition.Parts.First(part => part.Column == 1);
            var contextPart = new PlanePart(0, 0, context.x * zoom, context.y * zoom, left.Right - PortraitClearance * zoom / 2f,
                IndependentTop * zoom - context.y * zoom / 2f);
            var top = IndependentTop * zoom;
            var gap = PlaneComposition.Units(PlaneComposition.PartGapDegrees) * zoom;
            var reading = new PlanePart[heights.Count];
            for (var index = 0; index < heights.Count; index++)
            {
                reading[index] = new PlanePart(1, index, right.Width, heights[index] * zoom, right.Right, top - heights[index] * zoom / 2f);
                top -= heights[index] * zoom + gap;
            }
            return (contextPart, reading);
        }

        /// <summary>
        /// Where the independent workspace's text can stand, for the reading model
        /// (<see cref="MenuPage.LeastEm"/>), at body size unless said: the context column whole;
        /// in each reading part, within its content padding, words from the part's top down to its
        /// foot, or, over a footer, down to the 12 mm above the footer's lowest row; and that row's
        /// action words at <see cref="FooterView.WrappedActionDegrees"/>, from their row's middle, out
        /// to the prompt margin. Each reaches a little past its words' boxes for ink: a twentieth of
        /// an em at either side for side bearings, a tenth under the action row for descenders.
        /// Empty envelope and portrait corners hold no text, so they are no band; the field and
        /// clearance checks still take them.
        /// </summary>
        public static TextBand[] IndependentTextBands(PlanePart context, IReadOnlyList<(PlanePart Part, bool Footer)> reading, float zoom) =>
            new[] { ContextTextBand(context) }.Concat(reading.SelectMany(each => PartTextBands(each.Part, each.Footer, zoom))).ToArray();

        /// <summary>The context column's text, conservatively anywhere on it, at body size.</summary>
        public static TextBand ContextTextBand(PlanePart context) =>
            new TextBand(context.Left, context.Left + context.Width, context.Bottom, context.Top, Glaze.Menu.BodyDegrees);

        /// <summary>One reading part's text bands (<see cref="IndependentTextBands"/>), with or without its footer.</summary>
        public static TextBand[] PartTextBands(PlanePart part, bool footer, float zoom)
        {
            // PlanePart.Right is its centre's offset; its right edge is Left + Width.
            var edge = part.Left + part.Width;
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees) * zoom - 0.05f * GlazeTokens.Units(Glaze.Menu.BodyDegrees) * zoom;
            if (!footer) return new[] { new TextBand(part.Left + padding, edge - padding, part.Bottom, part.Top, Glaze.Menu.BodyDegrees) };
            var action = GlazeTokens.Units(FooterView.WrappedActionDegrees) * zoom;
            var reach = (GlazeTokens.Units(Glaze.Menu.PaddingDegrees) - GlazeTokens.Units(Glaze.Menu.PromptMarginDegrees)) * zoom - 0.05f * action;
            var room = GlazeTokens.Units(1f) * zoom;
            var row = FooterView.Height * zoom;
            var wordsFoot = Mathf.Min(part.Top, part.Bottom + room + row + MenuPage.TargetGap * zoom);
            var actionsFoot = Mathf.Min(wordsFoot, part.Bottom + room + row / 2f - 0.6f * action);
            return new[]
            {
                new TextBand(part.Left + padding, edge - padding, wordsFoot, part.Top, Glaze.Menu.BodyDegrees),
                new TextBand(part.Left + reach, edge - reach, actionsFoot, wordsFoot, FooterView.WrappedActionDegrees),
            };
        }

        private MenuFrameView? pendingIndependentDraw;
        private MenuFrame? pendingIndependentFrame;
        private SidePanel? pendingIndependentSide;
        private MenuColumn pendingIndependentKind;

        private void ForwardDrawn(MenuColumn kind, MenuFrameView view)
        {
            if (SeparateWorkspace && Context.gameObject.activeSelf)
            {
                if (Repositioning || !Contains(shown, view) || !view.gameObject.activeInHierarchy || view.Opening || view.Closing) return;
                if (Sliding)
                {
                    pendingIndependentDraw = view; pendingIndependentFrame = view.Frame;
                    pendingIndependentSide = view.Side; pendingIndependentKind = kind; return;
                }
            }
            Drawn?.Invoke(kind, view);
        }

        private void FlushIndependentDraw()
        {
            if (pendingIndependentDraw == null || Sliding) return;
            var view = pendingIndependentDraw; pendingIndependentDraw = null;
            if (!SeparateWorkspace || !Context.gameObject.activeSelf || Repositioning || !Contains(shown, view)
                || !view.gameObject.activeInHierarchy || view.Opening || view.Closing
                || !ReferenceEquals(view.Frame, pendingIndependentFrame) || !ReferenceEquals(view.Side, pendingIndependentSide)) return;
            Drawn?.Invoke(pendingIndependentKind, view);
        }

        private readonly PlaneWidthRule widthRule = new PlaneWidthRule();

        /// <summary>The frame a placing ended on, while the plane waits for the shelf it placed; -1 otherwise.</summary>
        private int placingEndedFrame = -1;
        private (float Scale, string Shelf, int Characters, int Yaw, int Pitch, bool Placing)? formSituation;

        /// <summary>The plane's panel width now, in dp, held between events (the lead's 2026-11-13T17:16Z and 19:34Z rulings).</summary>
        public float WidthDp => widthRule.WidthDp;

        /// <summary>
        /// Decides the plane's form again, only where the situation changed by an event: the text size, the placed shelf, the characters
        /// coming or going, or the plane placed or moved by the person (to the degree). A character moving on its shelf is no event: the
        /// shelf's envelope already holds the room it moves in. The widest measured width at or above the floor that clears the shelf
        /// inside the field wins (PlaneWidthRule); the kit is told before any reader measures, and each change is said once with its cause.
        /// </summary>
        /// <param name="placing">A local overlay places something in the room now (<see cref="IPlacesInRoom"/>): the floor's width meanwhile.</param>
        public void ConsiderForm(Vector3[]? shelf, Vector3 viewer, int characters, bool placing = false)
        {
            if (!RoomDesign.Enabled || !KitWorkspaceHost.Enabled) { Kit.Form = RoomForm.Full; ShelfAsideDegrees = 0f; return; }
            var floor = PlaneWidth.FloorDp(GlazeText.Scale);
            var shelfKey = shelf == null ? "" : string.Join(";", shelf.Select(corner => $"{Mathf.RoundToInt(corner.x * 100)},{Mathf.RoundToInt(corner.y * 100)},{Mathf.RoundToInt(corner.z * 100)}"));
            var now = (GlazeText.Scale, shelfKey, characters, Mathf.RoundToInt(Direction.Yaw), Mathf.RoundToInt(Direction.Elevation), placing);
            if (formSituation is { } was && was == now) return;
            // Placing just ended and the shelf placed is not standing yet (it stands a frame later): wait those frames, or the plane would
            // widen for a room with no shelf and narrow again, two changes for one placing (JUDGE's charR); never longer, and never over
            // another change, so a cancelled placing is decided at once (AUDIT's 22:30Z).
            if (formSituation is { } placed && placed.Placing && !placing)
            {
                if (placingEndedFrame < 0) placingEndedFrame = Time.frameCount;
                var otherChange = !Mathf.Approximately(placed.Scale, now.Item1) || placed.Characters != characters
                    || placed.Yaw != now.Item4 || placed.Pitch != now.Item5;
                if (PlaneWidthRule.AwaitsPlacedShelf(true, false, shelf != null, otherChange, Time.frameCount - placingEndedFrame)) return;
            }
            placingEndedFrame = -1;
            PlaneFormEvent happened = formSituation is not { } before ? PlaneFormEvent.PlaneMoved
                : !Mathf.Approximately(before.Scale, now.Item1) ? PlaneFormEvent.TextSizeChanged
                : before.Shelf != shelfKey || before.Placing != placing ? PlaneFormEvent.ShelfChanged
                : before.Characters != characters ? PlaneFormEvent.CharactersChanged
                : PlaneFormEvent.PlaneMoved;
            formSituation = now;
            if (widthRule.Reconsider(happened, width => !(placing && width > floor + 0.5f) && WidthClears(width, shelf, viewer), floor))
                Debug.Log("Halcyonic: the plane lays its panel " + widthRule.WidthDp.ToString("0") + " dp wide (" + happened + ").");
            Kit.Form = RoomForm.Full.AtWidth(widthRule.WidthDp);
            ShelfAsideDegrees = placing || shelf == null ? 0f : AsideFor(shelf, viewer, widthRule.WidthDp);
        }

        /// <summary>
        /// How far the placed shelf stands aside about the eyes while the plane is open (ShelfAside; the lead's 19:34Z GO, pick B), decided
        /// with the width on events only, from the shelf's own place; 0 where the plane at its width clears it.
        /// </summary>
        public float ShelfAsideDegrees { get; private set; }

        /// <summary>
        /// The turn that clears the plane's largest footprint at <paramref name="widthDp"/> for the shelf at <paramref name="shelf"/>, along
        /// its own arc, thirty degrees first and as far as clears out to the person's side, each said once; 0 where none clears (a
        /// finding, said once: the shelf stays, never put behind the person).
        /// </summary>
        private float AsideFor(Vector3[] shelf, Vector3 viewer, float widthDp)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (var point in shelf)
            {
                var offset = point - viewer;
                var yaw = Direction.Yaw + Mathf.DeltaAngle(Direction.Yaw, Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg);
                var elevation = Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
                left = Mathf.Min(left, yaw); right = Mathf.Max(right, yaw); bottom = Mathf.Min(bottom, elevation); top = Mathf.Max(top, elevation);
            }
            var aside = ShelfAside.For((left + right) / 2f, (right - left) / 2f, bottom, top, LargestExtent(widthDp), Direction.Yaw, Direction.Yaw,
                AgentShelfSearch.SideDegrees);
            if (!aside.Clears) Debug.Log("Halcyonic: no turn out to the person's side clears the plane for the shelf; it stays in its own place.");
            else if (!Mathf.Approximately(aside.Moved, 0f))
                Debug.Log("Halcyonic: the shelf stands aside " + Mathf.Abs(aside.Moved).ToString("0") + " degrees while the plane is open"
                    + (aside.PastThirty ? ", past thirty, as far as clears." : "."));
            return aside.Clears ? aside.Moved : 0f;
        }

        /// <summary>
        /// Where the largest bar laid with its panel <paramref name="widthDp"/> wide would stand, in degrees from the eyes toward where the
        /// plane faces: the width about the plane's heading, from its foot line up (FIELD's measured record, RoomBarView.LargestAt, in
        /// DESIGN units), the foot where the person's drag left it.
        /// </summary>
        private AngularExtent LargestExtent(float widthDp)
        {
            var largest = RoomBarView.LargestAt(widthDp);
            return AngularExtent.OfBar(largest.x, largest.y, RoomGeometry.FeetPitch, Placed.Elevation, Direction.Elevation);
        }

        /// <summary>
        /// What a shelf being placed keeps clear of (the lead's GO of 2026-11-13T19:34Z; AUDIT's 20:24Z): the largest bar at the floor's
        /// width, the plane as it stands while a shelf is placed (IPlacesInRoom). The plane widens again only where that clears every
        /// shelf (PlaneWidthRule), and a placed shelf the plane later meets stands aside (ShelfAside), so placing always finds a place.
        /// The shelf search and its Apply ask the same extent, so a place offered is a place kept.
        /// </summary>
        private AngularExtent KeepOutExtent() => LargestExtent(PlaneWidth.FloorDp(GlazeText.Scale));

        /// <summary>Whether the largest bar at <paramref name="widthDp"/> stands inside the field and clear of the shelf by the clearance kept between things.</summary>
        private bool WidthClears(float widthDp, Vector3[]? shelf, Vector3 viewer)
        {
            var plane = LargestExtent(widthDp);
            var measured = ViewField.Current ?? MenuPage.Quest3S;
            var field = new ViewField(measured.Left - ViewField.EdgeMarginDegrees, measured.Right - ViewField.EdgeMarginDegrees,
                measured.Up - ViewField.EdgeMarginDegrees, measured.Down - ViewField.EdgeMarginDegrees);
            if (!field.Shows(plane.Left, plane.Bottom) || !field.Shows(plane.Right, plane.Top)) return false;
            if (shelf == null) return true;
            float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (var point in shelf)
            {
                var offset = point - viewer;
                var yaw = Mathf.DeltaAngle(Direction.Yaw, Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg);
                var elevation = Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
                left = Mathf.Min(left, yaw); right = Mathf.Max(right, yaw); bottom = Mathf.Min(bottom, elevation); top = Mathf.Max(top, elevation);
            }
            return plane.Apart(new AngularExtent(left, right, bottom, top)) >= WorkspacePlacement.ClearanceDegrees;
        }

        private object? olderViewSaid;

        /// <summary>
        /// Under the redesign a whole screen drawn by the older view is a fault (the lead, 2026-11-13: the room bar always has a reader):
        /// said once per frame, by its column alone (no words of the frame are logged), so the walks and the census hear it.
        /// </summary>
        private void OlderViewDrawn(MenuFrame? frame)
        {
            if (frame == null || ReferenceEquals(olderViewSaid, frame)) return;
            olderViewSaid = frame;
            Debug.LogWarning("Halcyonic: older view drew a whole screen under the redesign (" + (frame.Side != null ? "side panel" : "frame")
                + (KitBinding is KitFileBinding binding && ReferenceEquals(binding.Frame, frame) ? ", the kit did not hold it" : ", no reader bound it") + ").");
        }

        private void ShowIndependentWorkspace(MenuBar menuBar, MenuFrame? menuFrame, MenuFrame? fileFrame, SidePanel? panel,
            object? fileColumn, IReadOnlyList<CharacterTarget> all, Vector3 looking, float? surfaceHeight,
            (float Yaw, float Elevation) moved, bool immediately)
        {
            // Under the redesign, with nothing beside the menu, the menu's own frame (Projects, Settings), its side panel included,
            // is the kit's to draw.
            var kitMenu = fileFrame == null && RoomDesign.Enabled;
            if (ShowKitWorkspace(kitMenu ? menuFrame : fileFrame, kitMenu ? MenuColumn.Menu : MenuColumn.File, panel, fileColumn, all, looking, surfaceHeight, moved, immediately)) return;
            if (RoomDesign.Enabled && KitWorkspaceHost.Enabled) OlderViewDrawn(kitMenu ? menuFrame : fileFrame);
            Kit.Hide();
            var zoom = GlazeText.Scale;
            var kind = panel != null ? MenuColumn.Side : fileFrame != null ? MenuColumn.File : MenuColumn.Menu;
            var primary = kind == MenuColumn.Side ? side : kind == MenuColumn.File ? file : menu;
            var was = shown.Count == 1 && shown[0].View == primary && ReferenceEquals(shownFileColumn, fileColumn);
            var hasActivity = kind == MenuColumn.File && panel == null && ActivityFrame != null;
            var activityWas = activityContext.gameObject.activeSelf;
            foreach (var other in frameViews) if (other != primary && other != activityContext) other.Hide();
            Context.Show(ContextProjects, ContextSelectedProject, ContextBinding!, ConnectionState);
            primary.SpatialWorkspace = true; primary.IndependentSurface = true; primary.PassiveActivity = false;
            primary.OrganizationControls = false; primary.ExternalPlaces = kind == MenuColumn.Menu;
            primary.HeadingOverride = hasActivity ? "" : null;
            primary.TaskPortrait = hasActivity ? null : SelectedPortrait; primary.TaskPortraitHue = SelectedPortraitHue;
            primary.IdentityMarks = hasActivity ? Array.Empty<WorkMark>() : SelectedMarks;
            primary.IdentityContext = ComposerFocused ? "Editing instruction" : null;
            var degrees = Glaze.Menu.FileColumnDegrees;
            var subject = hasActivity ? 0f : primary.SpatialSubjectHeight(panel?.Subject ?? Front!.Subject, degrees, Front!.Pill != null);
            if (panel != null) primary.Show(panel, degrees, subject, Front!.Pill != null, Front.Pill, Front);
            else primary.Show(Front!, degrees, subject, !hasActivity && Front!.Pill != null, stateInHeader: hasActivity);
            var primaryHeight = primary.Heights[0];
            var activityHeight = 0f;
            if (hasActivity)
            {
                activityContext.SpatialWorkspace = true; activityContext.IndependentSurface = true; activityContext.PassiveActivity = true;
                activityContext.OrganizationControls = false; activityContext.ExternalPlaces = false;
                activityContext.TaskPortrait = SelectedPortrait; activityContext.TaskPortraitHue = SelectedPortraitHue;
                activityContext.IdentityMarks = SelectedMarks; activityContext.IdentityContext = null;
                var header = activityContext.SpatialSubjectHeight(ActivityFrame!.Subject, degrees, ActivityFrame.Pill != null);
                activityContext.Show(ActivityFrame, degrees, header, ActivityFrame.Pill != null);
                activityHeight = activityContext.Heights[0];
            }
            else activityContext.Hide();
            var heights = hasActivity ? new[] { activityHeight, primaryHeight } : new[] { primaryHeight };
            var stack = heights.Sum() + (heights.Length - 1) * PlaneComposition.Units(PlaneComposition.PartGapDegrees);
            var identityView = hasActivity ? activityContext : primary;
            var envelope = IndependentEnvelope(Mathf.Max(Context.Size.y, stack), identityView.SubjectSize.y, identityView.TaskPortrait != null);
            Composition = new PlaneComposition(new[] { new PlaneColumn(Context.Size.x + PortraitClearance, envelope), new PlaneColumn(primary.Width, heights) }, zoom);
            Placed = PlaceWorkspace(all, looking, surfaceHeight, Composition.Size); Moved = moved; Direction = MenuDrag.Turned(Placed, moved);
            shown.Clear(); shown.Add((kind, primary));
            shownFileColumn = kind == MenuColumn.File ? fileColumn : null;
            MenuAside = menuFrame != null && fileFrame != null; FileAside = false;
            Chrome.Hide(); bar.Hide(); cover.SetActive(true);
            var parts = IndependentParts(Composition, Context.Size, heights);
            contextAllocation = parts.Context;
            SlideTo(Context.transform, contextAllocation, zoom, null, immediately);
            ReaderAllocation = null; ComposerAllocation = null;
            if (hasActivity)
            {
                var reading = parts.Reading[0];
                ReaderAllocation = reading;
                if (!activityWas || !was) activityContext.Open(false, frames);
                SlideTo(activityContext.Parts[0], reading, zoom, null, immediately || !activityWas);
                activityContext.Settle(reading, zoom);
            }
            var placed = parts.Reading[parts.Reading.Length - 1];
            if (hasActivity || ComposerFocused) ComposerAllocation = placed;
            if (!hasActivity) ReaderAllocation = placed;
            ContextTextBands = new[] { ContextTextBand(contextAllocation) };
            ReaderTextBands = ReaderAllocation is PlanePart reader ? PartTextBands(reader, !hasActivity, zoom) : Array.Empty<TextBand>();
            ComposerTextBands = ComposerAllocation is PlanePart composer ? PartTextBands(composer, true, zoom) : Array.Empty<TextBand>();
            if (!was || hasActivity != activityWas) primary.Open(false, frames);
            else if (Vector3.Distance(primary.Parts[0].position, PlaneLayout.PointOf(eyes, Direction, placed.Right, placed.Up)) > MovedMeters) primary.Unsettle();
            SlideTo(primary.Parts[0], placed, zoom, null, immediately || !was);
            primary.Settle(placed, zoom);
            lineTo = fileOf != null ? hasActivity ? activityContext : primary : null; lineColumn = lineTo == null ? -1 : 1;
            Advance(0f);
        }
    }
}
