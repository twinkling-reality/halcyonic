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
        public IReadOnlyList<ProjectView> ContextProjects { get; set; } = Array.Empty<ProjectView>();
        public string? ContextSelectedProject { get; set; }
        public object? ContextBinding { get; set; }
        public event Action<string, object>? ContextProjectPressed;
        public event Action<MenuPlace, object>? ContextPlacePressed;
        public event Action<object>? ContextWorkspacePressed;
        public static float PortraitClearance => MenuFrameView.PortraitWidth * 0.75f;
        public IReadOnlyList<Vector3> WorkspaceCorners => VisibleCorners() ?? Array.Empty<Vector3>();
        public Vector3 WorkspaceCentre => PlaneLayout.PointOf(eyes, Direction, 0f, 0f);
        public static float IndependentTop => PlaneComposition.Units(18f);
        public static float IndependentEnvelope(float height) => 2f * Mathf.Max(IndependentTop, height - IndependentTop);
        private PlanePart contextAllocation;
        public PlanePart? ReaderAllocation { get; private set; }
        public PlanePart? ComposerAllocation { get; private set; }

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
                activityContext.IdentityMarks = SelectedMarks;
                var header = activityContext.SpatialSubjectHeight(ActivityFrame!.Subject, degrees, ActivityFrame.Pill != null);
                activityContext.Show(ActivityFrame, degrees, header, ActivityFrame.Pill != null);
                activityHeight = activityContext.Heights[0];
            }
            else activityContext.Hide();
            var heights = hasActivity ? new[] { activityHeight, primaryHeight } : new[] { primaryHeight };
            var stack = heights.Sum() + (heights.Length - 1) * PlaneComposition.Units(PlaneComposition.PartGapDegrees);
            var envelope = IndependentEnvelope(Mathf.Max(Context.Size.y, stack));
            Composition = new PlaneComposition(new[] { new PlaneColumn(Context.Size.x + PortraitClearance, envelope), new PlaneColumn(primary.Width, heights) }, zoom);
            Placed = PlaceWorkspace(all, looking, surfaceHeight, Composition.Size); Moved = moved; Direction = MenuDrag.Turned(Placed, moved);
            shown.Clear(); shown.Add((kind, primary));
            shownFileColumn = kind == MenuColumn.File ? fileColumn : null;
            MenuAside = menuFrame != null && fileFrame != null; FileAside = false;
            Chrome.Hide(); bar.Hide(); cover.SetActive(true);
            var left = Composition.Parts.First(part => part.Column == 0);
            contextAllocation = new PlanePart(0, 0, Context.Size.x * zoom, Context.Size.y * zoom, left.Right - PortraitClearance * zoom / 2f,
                IndependentTop * zoom - Context.Size.y * zoom / 2f);
            SlideTo(Context.transform, contextAllocation, zoom, null, immediately);
            var top = IndependentTop * zoom;
            var right = Composition.Parts.First(part => part.Column == 1);
            var gap = PlaneComposition.Units(PlaneComposition.PartGapDegrees) * zoom;
            ReaderAllocation = null; ComposerAllocation = null;
            if (hasActivity)
            {
                var reading = new PlanePart(1, 0, right.Width, activityHeight * zoom, right.Right, top - activityHeight * zoom / 2f);
                ReaderAllocation = reading;
                if (!activityWas || !was) activityContext.Open(false, frames);
                SlideTo(activityContext.Parts[0], reading, zoom, null, immediately || !activityWas);
                activityContext.Settle(reading, zoom);
                top -= activityHeight * zoom + gap;
            }
            var placed = new PlanePart(1, hasActivity ? 1 : 0, right.Width, primaryHeight * zoom, right.Right, top - primaryHeight * zoom / 2f);
            if (hasActivity) ComposerAllocation = placed; else ReaderAllocation = placed;
            if (!was || hasActivity != activityWas) primary.Open(false, frames);
            else if (Vector3.Distance(primary.Parts[0].position, PlaneLayout.PointOf(eyes, Direction, placed.Right, placed.Up)) > MovedMeters) primary.Unsettle();
            SlideTo(primary.Parts[0], placed, zoom, null, immediately || !was);
            primary.Settle(placed, zoom);
            lineTo = fileOf != null ? hasActivity ? activityContext : primary : null; lineColumn = lineTo == null ? -1 : 1;
            Advance(0f);
        }
    }
}
