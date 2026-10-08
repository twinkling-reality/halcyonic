#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>The highest the raised banner may reach above eye level before the coordinator decides again where it stands.</summary>
        private const float RaisedBannerMostDegrees = 20f;

        /// <summary>
        /// The demonstration's first visit (ADR 0026), from the eyes, drawn by the menu's own director: it
        /// opens closed, the recorded characters and the closed bar, as the recording begins and again once
        /// its directed task waits; then Projects, from the bar's Open; the stage's banner raised above the
        /// stage throughout, with the demonstration's lines alone. It fails if the first visit opens
        /// anything or asks the first question, if the closed bar doesn't say what waits, if the
        /// demonstration's lines would be missing or hang where the bar or the menu stands, if a live
        /// session's banner would show or rise with the menu open, if the lines come within a degree of a
        /// character's highest reach, a label, the bar or the plane, if they leave the field while the menu
        /// is read, or if they reach above <see cref="RaisedBannerMostDegrees"/>.
        /// </summary>
        private static IEnumerable<string> RenderDemoWelcome(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Demo welcome render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                var recording = DemonstrationRecording.Parse(asset.text);
                Resources.UnloadAsset(asset);
                // As the recording begins: its welcome and its beginning's snapshot, nothing played yet.
                var (state, _) = RecordedAt(recording, new int[0]);

                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                // The recording's tasks on the stage, in its order, as the judge walk places them.
                var tasks = state.Workstreams.Values.OrderBy(task => task.CreatedAt).ToList();
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var spread = CharacterStage.Spread(radius, surfaceDrop.HasValue ? -surfaceDrop.Value : CharacterStage.DefaultHeightFromEyes);
                var origin = eyes + Vector3.down * (surfaceDrop ?? 0f);
                var slots = new[] { -12f, 0f, 12f };
                for (var slot = 0; slot < tasks.Count && slot < slots.Length; slot++)
                {
                    var view = CharacterView.Create(root.transform, tasks[slot].WorkstreamId);
                    view.Show(CharacterPresenter.Present(tasks[slot], state, true));
                    var (height, scale) = CharacterStage.Stance(view, radius, surfaceDrop ?? 0f, surfaceDrop.HasValue ? (float?)null : CharacterStage.DefaultHeightFromEyes);
                    var level = Quaternion.Euler(0f, slots[slot] * spread, 0f) * Vector3.forward;
                    view.transform.SetPositionAndRotation(origin + level * radius + Vector3.up * height, Quaternion.LookRotation(-level, Vector3.up));
                    view.transform.localScale = Vector3.one * scale;
                    characters.Add((view, CharacterTarget.Attach(view, tasks[slot].WorkstreamId)));
                }
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Overview = () => WorkOverview.Of(state, new StageVisibility(), _ => true),
                    Comfort = comfort,
                    RecordedUsage = at => recording.UsageLimitsAt(at),
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null), _ => { }, _ => { }),
                    Connected = () => true,
                    Demonstration = () => true,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, targets, surface, false),
                    CharacterOf = task => targets.FirstOrDefault(target => target.View.WorkstreamId == task),
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => state.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou),
                });
                var navigator = director.Navigator;

                // The banner, where the stage stands it while the menu is open, reading away from the person as the arc does.
                var bannerRoot = new GameObject("Banner root").transform;
                bannerRoot.SetParent(root.transform, false);
                bannerRoot.localScale = Vector3.one * radius;
                var banner = StageBanner.Create(bannerRoot);
                var line = DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null);

                // The first visit, as the workspace's director takes it once the recording plays with nothing open:
                // the demonstration never asks the first question, and opens nothing.
                var visit = new FirstVisit();
                var asks = visit.Asks(live: true, demonstration: true, state.Journal?.JournalId, state.Workstreams.Count > 0);
                if (asks != false) failures.Add(name + ": the demonstration's first visit asks the first question.");
                director.BeforeFirstTask = asks == true;
                if (visit.Due(demonstration: true, somethingOpen: false, asks)) failures.Add(name + ": the demonstration's first visit opens the menu, not ambient.");
                director.DrawNow();

                void Shot(string step, bool open)
                {
                    var what = name + " demo welcome " + step;
                    if (open)
                    {
                        if (navigator.Place != MenuPlace.Projects || !navigator.IsOpen) failures.Add(what + ": the menu stands on " + navigator.Place + (navigator.IsOpen ? "" : ", closed") + ", not open on Projects.");
                        if (navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Subject != ProjectsText.Subject) failures.Add(what + ": Projects does not ask what to work on.");
                    }
                    else
                    {
                        if (navigator.IsOpen || navigator.Beside != null || director.Plane.Bar == null) failures.Add(what + ": the demonstration does not open ambient, the closed bar alone on the plane.");
                        var waits = state.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou);
                        var said = TasksColumn.Bar(navigator.Place, state).ClosedLine;
                        if (said != (waits ? "1 task is waiting for you" : "Nothing is waiting for you.")) failures.Add(what + ": the closed bar says \"" + said + "\", not what waits.");
                    }

                    // Where the stage would stand its banner now, from what covers its place as the director drew it.
                    var stand = BannerPlace.Of(demonstration: true, AmbientCover.PanelShowing, AmbientCover.PeekShowing);
                    if (stand != BannerStand.AboveTheStage)
                    {
                        failures.Add(what + ": in the demonstration, its lines would be "
                            + (stand == BannerStand.Hidden ? "missing" : "under the labels, where the " + (open ? "menu" : "closed bar") + " stands") + ".");
                    }
                    var live = BannerPlace.Of(demonstration: false, AmbientCover.PanelShowing, AmbientCover.PeekShowing);
                    if (open && live != BannerStand.Hidden)
                    {
                        failures.Add(what + ": with the menu open in a live session, the banner would " + (live == BannerStand.AboveTheStage ? "rise above the stage" : "show under the labels") + ".");
                    }
                    banner.Show(line, BannerKind.Practice, null);
                    if (banner.Line.text != LabelText.ForTextMeshPro(line) || banner.Waiting.gameObject.activeSelf || banner.NotShown != null || banner.StillOpen != null)
                    {
                        failures.Add(what + ": the raised banner does not say the demonstration's lines alone.");
                    }
                    // As the stage places it: raised, clear of the characters, then of the panel in its place, by the top the
                    // plane gives, which the closed bar alone doesn't give.
                    var panelTop = AmbientCover.PanelTop;
                    if (open && panelTop == null) failures.Add(what + ": the open menu gives no top edge for the raised banner to clear.");
                    var overCharacters = surface.HasValue
                        ? CharacterStage.BannerBottomOnSurface(radius, surfaceDrop!.Value) - surfaceDrop.Value
                        : CharacterStage.BannerBottomAbove(radius, CharacterStage.DefaultHeightFromEyes);
                    bannerRoot.position = eyes + new Vector3(0f, CharacterStage.RaisedBannerBottom(radius, overCharacters, panelTop), radius);
                    banner.transform.localPosition = new Vector3(0f, banner.Height, 0f);
                    banner.gameObject.SetActive(stand != BannerStand.Hidden);
                    ForceMeshes(root);

                    // A degree over every character's highest reach, risen and moving, and clear of its label and of the plane.
                    var plate = new GlazeChecks.PlaneShape("the demonstration's lines", banner.Plate.transform, new Vector2(banner.Width, banner.Height) * radius);
                    var others = new List<GlazeChecks.Extent>();
                    foreach (var (view, _) in characters)
                    {
                        others.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                        others.Add(Reach(view, eyes));
                    }
                    others.Add(GlazeChecks.Of(open ? "the menu's plane" : "the closed bar", eyes, director.Plane.gameObject));
                    foreach (var other in others)
                    {
                        var apart = GlazeChecks.OutlineApart(plate, eyes, other);
                        if (apart < GlazeChecks.GapDegrees - 0.01f) failures.Add(what + ": the demonstration's lines are " + GlazeChecks.Degrees(apart) + " degrees from " + other + "; a degree at least.");
                    }
                    var corners = new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                        .Select(y => plate.Root.position + plate.Root.right * (x * plate.Size.x) + plate.Root.up * (y * plate.Size.y))).ToList();
                    var elevations = corners.Select(corner => FieldChecks.ElevationOf(eyes, corner)).ToList();
                    var top = elevations.Max();
                    if (top > RaisedBannerMostDegrees) failures.Add(what + ": the demonstration's lines reach " + GlazeChecks.Degrees(top) + " degrees above eye level, over " + RaisedBannerMostDegrees.ToString(CultureInfo.InvariantCulture) + ".");
                    Debug.Log("Halcyonic: workspace render " + what + ": the demonstration's lines stand from " + GlazeChecks.Degrees(elevations.Min()) + " to "
                        + GlazeChecks.Degrees(top) + " degrees above eye level; the plane's top edge stands at " + (panelTop == null ? "none" : GlazeChecks.Degrees(panelTop.Value)) + ".");
                    // In the field while the person reads the menu, the head pitched as the plane's checks take it.
                    if (ViewField.Current is ViewField field && director.Plane.Composition is PlaneComposition composition)
                    {
                        var shapes = new List<GlazeChecks.PlaneShape>();
                        for (var c = 0; c < director.Plane.Shown.Count; c++)
                        {
                            var shown = director.Plane.Shown[c].View;
                            var placed = composition.Parts.Where(part => part.Column == c).ToList();
                            shapes.AddRange(shown.Parts.Select((part, index) => new GlazeChecks.PlaneShape(shown.name + " " + part.name, part,
                                new Vector2(placed[index].Width, placed[index].Height) * PlaneComposition.Distance)));
                        }
                        failures.AddRange(GlazeChecks.InsideField(what + ", the demonstration's lines", corners, eyes, GlazeChecks.CompositionCenter(shapes),
                            WorkspacePlacement.ReadingPitch(composition.Size, director.Plane.Direction.Elevation, field), field));
                    }
                    failures.AddRange(PlaneState(name + " demo welcome " + step, folder, camera, texture, director.Plane, characters, eyes, null, lightLine: false));
                }

                Shot("1 ambient", open: false);
                if (AmbientCover.PanelShowing) failures.Add(name + ": the closed bar covers the banner's place.");

                // The directed task comes to wait: nothing opens by itself, and the closed bar says so.
                var asked = RecordedAt(recording, new[] { 0 });
                state = asked.State;
                foreach (var (view, _) in characters) view.Show(CharacterPresenter.Present(state.Workstreams[view.WorkstreamId], state, true));
                navigator.Tick();
                director.DrawNow();
                Shot("2 a task waits", open: false);

                // Projects, a press from the bar: the menu open beside the stage, its lines raised over it, Tasks with the amber dot.
                director.Open(MenuPlace.Projects);
                director.DrawNow();
                if (!AmbientCover.PanelShowing) failures.Add(name + ": the open menu leaves the banner's place to it, so nothing raises the demonstration's lines.");
                if (navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Sections.SingleOrDefault(section => section.Waits)?.Words != "Tasks")
                {
                    failures.Add(name + ": with a task waiting, Tasks does not carry the amber dot beside Projects.");
                }
                Shot("3 projects", open: true);
            }
            finally
            {
                Object.DestroyImmediate(root);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>The most a character's body takes as the eyes see it, risen and moving: from a body's radius under its place up to its highest reach.</summary>
        private static GlazeChecks.Extent Reach(CharacterView view, Vector3 eyes)
        {
            var scale = view.transform.lossyScale.y;
            var bottom = -CharacterView.BodyRadius * scale;
            var top = CharacterView.HighestReach * scale;
            return GlazeChecks.Facing(view.WorkstreamId + "'s body, risen", eyes, view.transform.position + Vector3.up * ((top + bottom) / 2f),
                CharacterView.BodyRadius * scale, (top - bottom) / 2f);
        }
    }
}
