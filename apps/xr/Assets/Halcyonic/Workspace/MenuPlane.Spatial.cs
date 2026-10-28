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

        private PlanePart kitPart;
        private (MenuColumn Kind, MenuFrame Frame)? pendingKitDraw;

        /// <summary>The kit takes a press only where the file's view would: settled, still, standing, and input not suspended.</summary>
        private bool TakesKit() => !FocusGuard.InputSuspended && !Repositioning && !Sliding && (KitShows || KitCompactShows) && Kit.Settled;

        private void CreateKit(Transform root)
        {
            Kit = KitWorkspaceHost.Create(root);
            Kit.Accepting = TakesKit;
            Kit.Acted += (kind, action, key, frame, side) => { if (TakesKit()) Acted?.Invoke(kind, action, key, frame, side); };
            Kit.HoldStarted += (kind, prompt, frame, side) => { if (TakesKit()) HoldStarted?.Invoke(kind, prompt, frame, side); };
            Kit.HoldEnded += (kind, prompt, letGo) => HoldEnded?.Invoke(kind, prompt, letGo);
            Kit.ContextRow += (key, owner) => { if (TakesKit()) KitContextPressed?.Invoke(key, owner); };
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
        private bool ShowKitWorkspace(MenuFrame? fileFrame, SidePanel? panel, object? fileColumn, IReadOnlyList<CharacterTarget> all,
            Vector3 looking, float? surfaceHeight, (float Yaw, float Elevation) moved, bool immediately)
        {
            if (!KitWorkspaceHost.Enabled || panel != null || fileFrame == null || ContextBinding == null
                || KitBinding is not KitFileBinding binding || !ReferenceEquals(binding.Frame, fileFrame) || !Kit.Holds(binding))
                return false;
            var zoom = GlazeText.Scale;
            var kitWas = KitShows;
            foreach (var view in frameViews) view.Hide();
            Context.Hide();
            // The file's view stays the column of record (Showing, identity), laid for this frame but never shown.
            file.SpatialWorkspace = true; file.IndependentSurface = true; file.PassiveActivity = false;
            file.OrganizationControls = false; file.ExternalPlaces = false; file.HeadingOverride = null;
            file.Show(fileFrame, Glaze.Menu.FileColumnDegrees, 0f, false);
            file.Hide();
            Kit.Show(binding, MenuColumn.File, ContextBinding);
            // As the presence at Compact: a switch never told the whole workspace stands there at once.
            if (Kit.View.Spine.Switch.ConfirmedCompact) Kit.ShowConfirmed(false, immediately: true);
            KitOwner = ContextBinding;
            Composition = new PlaneComposition(new[] { new PlaneColumn(Kit.Size.x, Kit.Size.y) }, zoom);
            Placed = PlaceWorkspace(all, looking, surfaceHeight, Composition.Size); Moved = moved; Direction = MenuDrag.Turned(Placed, moved);
            shown.Clear(); shown.Add((MenuColumn.File, file));
            shownFileColumn = fileColumn;
            MenuAside = false; FileAside = false;
            Chrome.Hide(); bar.Hide(); cover.SetActive(true);
            kitPart = Composition.Parts.First();
            SlideTo(Kit.Part, kitPart, zoom, null, immediately || !kitWas);
            // The kit's bands are its spine's, its slate's and its rail's, about its centre at the standard size.
            TextBand At(TextBand band) => new TextBand(kitPart.Right + band.Left * zoom, kitPart.Right + band.Right * zoom,
                kitPart.Up + band.Bottom * zoom, kitPart.Up + band.Top * zoom, band.EmDegrees);
            var bands = Kit.TextBands.Select(At).ToArray();
            ContextTextBands = bands.Take(1).ToArray();
            ReaderTextBands = bands.Skip(1).ToArray();
            ComposerTextBands = bands.Skip(1).Take(1).ToArray();
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

        private void ShowIndependentWorkspace(MenuBar menuBar, MenuFrame? menuFrame, MenuFrame? fileFrame, SidePanel? panel,
            object? fileColumn, IReadOnlyList<CharacterTarget> all, Vector3 looking, float? surfaceHeight,
            (float Yaw, float Elevation) moved, bool immediately)
        {
            if (ShowKitWorkspace(fileFrame, panel, fileColumn, all, looking, surfaceHeight, moved, immediately)) return;
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
