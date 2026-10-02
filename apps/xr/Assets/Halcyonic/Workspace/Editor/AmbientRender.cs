#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the stage beside a window-sized plate, as a browser video or a Mac's Virtual Display
    /// would stand in front of the person, in each of the three arrangements (<see cref="StageArrangement"/>),
    /// and the large panels folded and restored as focus goes and comes back. It counts how many
    /// characters' bodies and labels the window covers in each, and checks that turned aside
    /// (<see cref="CharacterStage.AsideDegrees"/>) it covers fewer bodies than in front, and that beside
    /// a window it covers none: every body and badge a degree or more outside the window's lane, and
    /// the banner, with what waits, what is not shown and what is still open, under the lane and
    /// clear of the window. It logs how far out the outermost label reaches. It checks that a folded
    /// panel leaves nothing of itself, that the banner can name it as still open, and that it comes
    /// back pixel for pixel as it was. The window is a plate of a typical size, 1.4 by 0.79 m at
    /// 1.6 m, centered at eye level: Halcyonic cannot see a real window, so this shows what each
    /// arrangement can do, not what a headset will show. It saves each render in
    /// apps/xr/Builds/AmbientRenders, which git ignores. In the editor: Halcyonic > Render the Stage
    /// Beside a Window. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1
    /// when a check fails.
    /// </summary>
    public static class AmbientRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const float WindowDistance = 1.6f;
        private static readonly Vector2 WindowSize = new Vector2(1.4f, 0.79f);

        [MenuItem("Halcyonic/Render the Stage Beside a Window")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Ambient render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <param name="variant">A folder of its own for the renders of a pass, such as the larger text's; empty for the standard pass.</param>
        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "AmbientRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(BesideAWindow(folder));
                failures.AddRange(FoldAndRestore(folder));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                FocusGuard.FoldForRender(null);
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: ambient render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: ambient render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>The characters 2.4 m away, the stage's own geometry, in each arrangement, behind a window.</summary>
        private static IEnumerable<string> BesideAWindow(string folder)
        {
            var failures = new List<string>();
            var covered = new Dictionary<StageArrangement, int>();
            foreach (StageArrangement arrangement in Enum.GetValues(typeof(StageArrangement)))
            {
                var name = arrangement switch
                {
                    StageArrangement.TurnedAside => "aside",
                    StageArrangement.BesideAWindow => "beside",
                    _ => "front",
                };
                var root = new GameObject("Ambient render " + name);
                var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                try
                {
                    var eyes = new Vector3(0f, EyeHeight, 0f);
                    var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                    var beside = arrangement == StageArrangement.BesideAWindow;
                    var work = beside ? WindowWork() : null;
                    var characters = beside
                        ? WorkspaceRender.Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null, (_, slot) => work!.Shown[slot], besideWindow: true).ConvertAll(character => character.Target)
                        : Characters(root.transform, eyes, arrangement == StageArrangement.TurnedAside ? CharacterStage.AsideDegrees : 0f);
                    var banner = beside ? Strip(root.transform, eyes, work!) : null;
                    var window = Window(root.transform, eyes);
                    WorkspaceRender.ForceMeshes(root);
                    var render = WorkspaceRender.Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, "window-" + name + ".png"), render.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(render);
                    var outline = Outline(camera, window.transform);
                    var count = 0;
                    var labels = 0;
                    var outermost = 0f;
                    foreach (var target in characters)
                    {
                        var body = camera.WorldToScreenPoint(target.BodyPosition);
                        if (Inside(outline, new Vector2(body.x, body.y))) count++;
                        if (Behind(outline, WorkspaceRender.LabelRect(camera, target.View))) labels++;
                        outermost = Mathf.Max(outermost, OutermostYaw(eyes, target.View));
                    }
                    covered[arrangement] = count;
                    Debug.Log("Halcyonic: ambient render: " + (beside ? "beside a window" : arrangement == StageArrangement.TurnedAside ? "with the lineup turned aside" : "with the lineup in front")
                        + ", a window covers " + count + " of " + characters.Count + " characters' bodies and " + labels + " of their labels; the outermost label reaches "
                        + WorkspaceRender.Degrees(outermost) + " degrees from straight ahead.");
                    if (beside)
                    {
                        if (count > 0 || labels > 0) failures.Add("beside a window, the window covers " + count + " bodies and " + labels + " labels.");
                        failures.AddRange(LaneClear(eyes, characters));
                        LogAlike(characters);
                        failures.AddRange(StripUnderTheLane(eyes, banner!, outline, camera, characters));
                        failures.AddRange(StripCounts(banner!, work!));
                        failures.AddRange(RailUnderTheBanner(root.transform, eyes, banner!, work!));
                        failures.AddRange(NothingTouches(eyes, characters));
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            if (covered[StageArrangement.TurnedAside] >= covered[StageArrangement.InFront]) failures.Add("turning the lineup aside does not uncover any character.");
            return failures;
        }

        /// <summary>The work beside a window: every task, the four the stage's lineup stands there, and what each shows.</summary>
        private sealed class Beside
        {
            public Beside(ClientProjection state, CharacterLineup lineup, List<CharacterPresentation> shown)
            {
                State = state;
                Lineup = lineup;
                Shown = shown;
            }

            public ClientProjection State { get; }

            public CharacterLineup Lineup { get; }

            /// <summary>What each slot's character shows, from the person's left, as the stage presents it.</summary>
            public List<CharacterPresentation> Shown { get; }
        }

        /// <summary>
        /// The portfolio's ten tasks with three waiting for the person, stood as the stage stands them
        /// beside a window: its four-slot lineup, which fills the middle first, and its presenter. Two
        /// waiting tasks take the upper places and the third stands under one, risen as high as it rises:
        /// the case where a body comes nearest the badge above it.
        /// </summary>
        private static Beside WindowWork()
        {
            var state = EntryRender.Portfolio(hostile: false, needsYouNow: true);
            var third = state.Workstreams["w-c"];
            third.Status = WorkstreamStatus.WaitingForHuman;
            third.Attention = new Attention { Level = AttentionLevel.ActionRequired, Reasons = new List<AttentionReason>() };
            var lineup = new CharacterLineup(CharacterStage.WindowCapacity);
            lineup.Update(state.Workstreams.Values);
            // Two tasks whose titles start alike and cut to the same words beside a window, which the log names.
            var slots = lineup.Slots.Where(id => id != null).ToList();
            state.Workstreams[slots[0]!].Title = "Add rate limiting to the sign-in endpoint";
            state.Workstreams[slots[1]!].Title = "Add rate limiting to the sign-up form";
            var shown = lineup.Slots.Select(id => CharacterPresenter.Present(state.Workstreams[id!], state, live: true)).ToList();
            return new Beside(state, lineup, shown);
        }

        /// <summary>
        /// The stage's banner where the stage hangs it beside a window (<see cref="CharacterStage.BannerTopBesideWindow"/>),
        /// saying what it says there while another window keeps focus, from the work as the stage counts
        /// it: live, what waits across every task, how many tasks have no character, and the panel still open.
        /// </summary>
        private static StageBanner Strip(Transform parent, Vector3 eyes, Beside work)
        {
            var radius = CharacterStage.DefaultDistance;
            var holder = new GameObject("Banner").transform;
            holder.SetParent(parent, false);
            holder.SetPositionAndRotation(eyes + new Vector3(0f, CharacterStage.BannerTopBesideWindow(radius), radius), Quaternion.identity);
            holder.localScale = Vector3.one * radius;
            var banner = StageBanner.Create(holder);
            banner.Show("Connected to " + HostText.Your, BannerKind.Live, AmbientText.NeedsYouLine(AmbientText.NeedsYou(work.State)), null,
                AmbientText.NotShown(work.State.Workstreams.Count - work.Shown.Count), AmbientText.StillOpen("Add rate limiting to the sign-in endpoint"));
            return banner;
        }

        /// <summary>
        /// The banner's count of what waits is the badges that say Waiting for you plus the waiting
        /// tasks with no character, and its count of what is not shown is the tasks with no character.
        /// </summary>
        private static IEnumerable<string> StripCounts(StageBanner banner, Beside work)
        {
            var waitingShown = work.Shown.Count(shown => StateLanguage.StateOf(shown.Activity, shown.Attention) == WorkState.WaitingForYou);
            var offStage = work.State.Workstreams.Values.Where(workstream => work.Lineup.SlotOf(workstream.WorkstreamId) < 0).ToList();
            var waitingOff = offStage.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou);
            var counted = AmbientText.NeedsYou(work.State);
            Debug.Log("Halcyonic: ambient render: beside a window, " + waitingShown + " badges say Waiting for you, " + waitingOff + " waiting tasks have no character, "
                + offStage.Count + " tasks are not shown, and the banner counts " + counted + " waiting.");
            if (counted != waitingShown + waitingOff)
            {
                yield return "beside a window, the banner counts " + counted + " waiting, but " + waitingShown + " badges say Waiting for you and " + waitingOff + " waiting tasks have no character.";
            }
            if (banner.Waiting.text != LabelText.ForTextMeshPro(AmbientText.NeedsYouLine(counted)!)) yield return "beside a window, the banner says \"" + banner.Waiting.text + "\" for " + counted + " waiting.";
            var notShown = AmbientText.NotShown(offStage.Count);
            if (notShown == null ? banner.NotShown != null : banner.NotShown == null || banner.NotShown.text != LabelText.ForTextMeshPro(notShown))
            {
                yield return "beside a window, the banner does not say that " + offStage.Count + " tasks are not shown.";
            }
        }

        /// <summary>
        /// Beside a window, the short titles that read the same as laid out, cut to the same words:
        /// named in the log, not solved, since the peek tells them apart.
        /// </summary>
        private static void LogAlike(List<CharacterTarget> characters)
        {
            var shown = characters.Select(target => (target.View.WorkstreamId, Words: Visible(target.View.Label.ShortTitle))).ToList();
            foreach (var same in shown.GroupBy(each => each.Words).Where(group => group.Count() > 1))
            {
                Debug.Log("Halcyonic: ambient render: beside a window, " + same.Count() + " titles cut to the same words, \"" + same.Key + "\": "
                    + string.Join(", ", same.Select(each => each.WorkstreamId)) + ".");
            }
        }

        /// <summary>The characters a label shows, the ellipsis included.</summary>
        private static string Visible(TMPro.TMP_Text label)
        {
            label.ForceMeshUpdate();
            var info = label.textInfo;
            var last = -1;
            for (var index = 0; index < info.characterCount; index++)
            {
                if (info.characterInfo[index].isVisible) last = index;
            }
            var text = new System.Text.StringBuilder();
            for (var index = 0; index <= last; index++) text.Append(info.characterInfo[index].character);
            return text.ToString();
        }

        /// <summary>
        /// Beside a window, every character's body and label stands a degree or more outside the
        /// window's lane, as the eyes see it: its sides at <see cref="CharacterStage.WindowLaneHalfWidthDegrees"/>.
        /// </summary>
        private static IEnumerable<string> LaneClear(Vector3 eyes, List<CharacterTarget> characters)
        {
            var lane = CharacterStage.WindowLaneHalfWidthDegrees + GlazeChecks.GapDegrees;
            foreach (var target in characters)
            {
                var view = target.View;
                var toward = target.BodyPosition - eyes;
                var yaw = Mathf.Abs(Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg);
                var bodyHalf = Mathf.Atan2(CharacterView.BodyExtent * target.Scale, toward.magnitude) * Mathf.Rad2Deg;
                if (yaw - bodyHalf < lane) yield return "beside a window, " + view.WorkstreamId + "'s body reaches " + WorkspaceRender.Degrees(yaw - bodyHalf) + " degrees from straight ahead, inside the lane and its degree.";
                var inner = InnermostYaw(eyes, view);
                if (inner < lane) yield return "beside a window, " + view.WorkstreamId + "'s label reaches " + WorkspaceRender.Degrees(inner) + " degrees from straight ahead, inside the lane and its degree.";
                if (view.Label.Title.gameObject.activeSelf) yield return "beside a window, " + view.WorkstreamId + " shows its whole title; only one short line belongs there.";
                var shortTitle = view.Label.ShortTitle;
                if (!shortTitle.gameObject.activeSelf || string.IsNullOrEmpty(shortTitle.text))
                {
                    yield return "beside a window, " + view.WorkstreamId + " shows no short title, so it can't be told from the others.";
                    continue;
                }
                shortTitle.ForceMeshUpdate();
                if (shortTitle.textInfo.lineCount != 1) yield return "beside a window, " + view.WorkstreamId + "'s short title takes " + shortTitle.textInfo.lineCount + " lines.";
                // As designed, in units of the distance from the eyes, as the widest plate's 10.5 degrees are.
                var wide = GlazeTokens.DegreesOf(view.Label.Plate.Size.x);
                if (wide > CharacterLabelView.MaxWidthDegrees + 0.01f)
                {
                    yield return "beside a window, " + view.WorkstreamId + "'s short title is " + WorkspaceRender.Degrees(wide) + " degrees wide, over "
                        + WorkspaceRender.Degrees(CharacterLabelView.MaxWidthDegrees) + ".";
                }
            }
        }

        /// <summary>
        /// With a field of view so short that it lifts the rail to its ceiling
        /// (<see cref="ProjectRail.HighestBelowDegrees"/>), the rail stays a degree or more below the
        /// banner, at its tallest beside a window: four lines.
        /// </summary>
        private static IEnumerable<string> RailUnderTheBanner(Transform parent, Vector3 eyes, StageBanner banner, Beside work)
        {
            var kept = ViewField.Current;
            try
            {
                ViewField.Current = new ViewField(40, 40, 30, 20);
                var overview = WorkOverview.Of(work.State, new StageVisibility(), id => work.Lineup.SlotOf(id) >= 0);
                var rail = ProjectRail.ForRender(parent, overview, null);
                rail.ResetPosition();
                WorkspaceRender.ForceMeshes(rail.gameObject);
                var bannerExtent = GlazeChecks.Of("the banner", eyes, banner.gameObject);
                var near = rail.Shown.Select(button => GlazeChecks.Of("the rail's " + button.name, eyes, button.gameObject)).ToList();
                var railTop = near.Max(extent => extent.Top);
                Debug.Log("Halcyonic: ambient render: beside a window, the banner reaches " + WorkspaceRender.Degrees(-bannerExtent.Bottom)
                    + " degrees below eye level, and the rail at its highest (" + WorkspaceRender.Degrees(ProjectRail.Below(ViewField.Current))
                    + " down) has its top at " + WorkspaceRender.Degrees(-railTop) + ".");
                near.Add(bannerExtent);
                var failures = GlazeChecks.Apart(near).Where(failure => failure.Contains("the banner")).Select(failure => "beside a window, with the rail at its highest: " + failure).ToList();
                UnityEngine.Object.DestroyImmediate(rail.gameObject);
                return failures;
            }
            finally
            {
                ViewField.Current = kept;
            }
        }

        /// <summary>
        /// The banner hangs a degree or more under the lane and the window, clear of every label, says
        /// all it was given whole but the panel's name, and only says: nothing on it takes a press.
        /// </summary>
        private static IEnumerable<string> StripUnderTheLane(Vector3 eyes, StageBanner banner, Vector2[] window, Camera camera, List<CharacterTarget> characters)
        {
            var failures = new List<string>();
            var top = banner.transform.position;
            var toward = top - eyes;
            var elevation = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
            Debug.Log("Halcyonic: ambient render: beside a window, the banner's top is " + WorkspaceRender.Degrees(-elevation) + " degrees below eye level.");
            if (elevation > -(CharacterStage.WindowLaneHalfHeightDegrees + GlazeChecks.GapDegrees) + 0.01f) failures.Add("beside a window, the banner reaches into the lane.");
            var plate = EntryRender.ScreenBounds(camera, banner.Plate.GetComponent<Renderer>().bounds);
            for (var x = 0; x <= 2; x++)
            {
                if (Inside(window, new Vector2(Mathf.Lerp(plate.xMin, plate.xMax, x / 2f), plate.yMax))) failures.Add("beside a window, the window covers the banner.");
            }
            foreach (var target in characters)
            {
                if (EntryRender.Overlap(plate, WorkspaceRender.LabelRect(camera, target.View))) failures.Add("beside a window, the banner covers " + target.View.WorkstreamId + "'s label.");
            }
            if (banner.Waiting == null || !banner.Waiting.gameObject.activeSelf || banner.NotShown == null || banner.StillOpen == null)
            {
                failures.Add("beside a window, the banner does not say what waits, what is not shown and what is still open.");
            }
            failures.AddRange(GlazeChecks.NothingCut(new TMPro.TMP_Text[] { banner.Line, banner.Waiting!, banner.NotShown! }, "ambient render beside a window"));
            if (banner.GetComponentsInChildren<PointerTarget>(true).Length > 0) failures.Add("beside a window, something on the banner takes a press.");
            return failures;
        }

        /// <summary>
        /// Beside a window, no character's body or label comes within a degree of another character's,
        /// a risen body included, as the eyes see them. A body and its own label stand as the stage
        /// stands every character.
        /// </summary>
        private static IEnumerable<string> NothingTouches(Vector3 eyes, List<CharacterTarget> characters)
        {
            var parts = characters.ConvertAll(target => new[]
            {
                GlazeChecks.Of(target.View.WorkstreamId + "'s label", eyes, target.View.Label.gameObject),
                WorkspaceRender.BodyExtent(target.View, eyes),
            });
            var failures = new List<string>();
            for (var a = 0; a < parts.Count; a++)
            {
                for (var b = a + 1; b < parts.Count; b++)
                {
                    foreach (var mine in parts[a])
                    {
                        foreach (var theirs in parts[b]) failures.AddRange(GlazeChecks.Apart(new[] { mine, theirs }).Select(failure => "beside a window: " + failure));
                    }
                }
            }
            return failures;
        }

        /// <summary>How far from straight ahead a character's label reaches, at its outer edge, in degrees.</summary>
        private static float OutermostYaw(Vector3 eyes, CharacterView view) => Mathf.Max(Mathf.Abs(EdgeYaw(eyes, view, -1f)), Mathf.Abs(EdgeYaw(eyes, view, 1f)));

        /// <summary>How near straight ahead a character's label reaches, at its inner edge, in degrees.</summary>
        private static float InnermostYaw(Vector3 eyes, CharacterView view) => Mathf.Min(Mathf.Abs(EdgeYaw(eyes, view, -1f)), Mathf.Abs(EdgeYaw(eyes, view, 1f)));

        private static float EdgeYaw(Vector3 eyes, CharacterView view, float side)
        {
            var edge = view.transform.TransformPoint(new Vector3(side * view.LabelHalfWidth, view.LabelBottom / 2f, 0f)) - eyes;
            return Mathf.Atan2(edge.x, edge.z) * Mathf.Rad2Deg;
        }

        /// <summary>The entry panel and the Usage left panel, open, then folded, then restored.</summary>
        private static IEnumerable<string> FoldAndRestore(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Ambient render fold");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var characters = Characters(root.transform, eyes, 0f);
                var state = EntryRender.Portfolio(hostile: false, needsYouNow: false);
                var visibility = new StageVisibility();
                var lineup = new CharacterLineup(6);
                lineup.Update(state.Workstreams.Values);
                var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);

                var entry = EntryPanel.ForRender(root.transform, state, overview, characters, null);
                entry.ShowForRender(EntryPanel.Screen.Connect);
                failures.AddRange(Fold("entry", folder, root, camera, texture, entry.Root.gameObject, entry.ApplyFold, entry.Frame.Shown?.Title));
                // Closed, as the person would, before Usage left opens: one foreground panel at a time.
                entry.PressForRender(PanelModel.Close);

                var rail = ProjectRail.ForRender(root.transform, overview, null);
                rail.ResetPosition();
                var glance = UsageLeftGlance.ForRender(rail);
                glance.ShowForRender(UsageLeftPresenter.Message(UsageLeftPresenter.NotSetUp), characters, null);
                rail.Root.gameObject.SetActive(false);
                failures.AddRange(Fold("usage-left", folder, root, camera, texture, glance.Panel.gameObject, glance.ApplyFold, UsageLeftPresenter.Title));
            }
            finally
            {
                FocusGuard.FoldForRender(null);
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <param name="openAs">The name the banner says the folded panel is still open as.</param>
        private static IEnumerable<string> Fold(string name, string folder, GameObject root, Camera camera, RenderTexture texture, GameObject panel, Action apply, string? openAs)
        {
            var failures = new List<string>();
            FocusGuard.FoldForRender(false);
            apply();
            WorkspaceRender.ForceMeshes(root);
            var open = WorkspaceRender.Render(camera, texture);
            FocusGuard.FoldForRender(true);
            apply();
            var folded = WorkspaceRender.Render(camera, texture);
            if (panel.activeInHierarchy) failures.Add(name + ": the panel still shows while folded.");
            // Folded, it no longer covers the stage's banner, which then says what waits for the person and that it is still open.
            if (AmbientCover.Any) failures.Add(name + ": something still covers the stage's banner while the panel is folded.");
            if (AmbientCover.OpenPanel != openAs) failures.Add(name + ": the banner would say \"" + AmbientCover.OpenPanel + "\" is still open, not \"" + openAs + "\".");
            FocusGuard.FoldForRender(false);
            apply();
            WorkspaceRender.ForceMeshes(root);
            var restored = WorkspaceRender.Render(camera, texture);
            var whole = new RectInt(0, 0, Size, Size);
            var (gone, _) = WorkspaceRender.Compare(open, folded, whole);
            var (moved, largest) = WorkspaceRender.Compare(open, restored, whole);
            File.WriteAllBytes(Path.Combine(folder, name + "-open.png"), open.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, name + "-folded.png"), folded.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, name + "-restored.png"), restored.EncodeToPNG());
            if (gone == 0) failures.Add(name + ": folding changed nothing on the render.");
            if (moved > 0) failures.Add(name + ": " + moved + " pixels differ after restoring (largest " + largest.ToString("0.000", CultureInfo.InvariantCulture) + ").");
            Debug.Log("Halcyonic: ambient render: " + name + " folds away " + gone + " pixels and comes back " + (moved == 0 ? "exactly." : "with " + moved + " pixels changed."));
            UnityEngine.Object.DestroyImmediate(open);
            UnityEngine.Object.DestroyImmediate(folded);
            UnityEngine.Object.DestroyImmediate(restored);
            return failures;
        }

        /// <summary>Six characters on the stage's arc, at the stage's default distance and height, as CharacterStage stands them.</summary>
        private static List<CharacterTarget> Characters(Transform parent, Vector3 eyes, float turn) =>
            WorkspaceRender.Lineup(parent, eyes, CharacterStage.DefaultDistance, null, WorkspaceRender.Presentation, turn).ConvertAll(character => character.Target);

        /// <summary>
        /// The window: an opaque quad of <see cref="WindowSize"/> straight ahead at eye level, drawn
        /// over everything Halcyonic draws, as the system draws a window over the app.
        /// </summary>
        private static GameObject Window(Transform parent, Vector3 eyes)
        {
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "Window";
            UnityEngine.Object.DestroyImmediate(window.GetComponent<Collider>());
            window.transform.SetParent(parent, false);
            window.transform.SetPositionAndRotation(eyes + Vector3.forward * WindowDistance, Quaternion.identity);
            window.transform.localScale = new Vector3(WindowSize.x, WindowSize.y, 1f);
            var material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.18f, 0.2f, 0.24f, 1f) };
            var renderer = window.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 100;
            return window;
        }

        /// <summary>The window's outline on the render, a trapezoid, its corners counterclockwise.</summary>
        private static Vector2[] Outline(Camera camera, Transform window)
        {
            var unit = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            var corners = new Vector2[4];
            for (var index = 0; index < 4; index++) corners[index] = camera.WorldToScreenPoint(window.TransformPoint(unit[index]));
            return corners;
        }

        /// <summary>Whether a point of the render falls inside the window's outline.</summary>
        private static bool Inside(Vector2[] outline, Vector2 point)
        {
            for (var edge = 0; edge < outline.Length; edge++)
            {
                var from = outline[edge];
                var to = outline[(edge + 1) % outline.Length];
                if ((to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x) < 0f) return false;
            }
            return true;
        }

        /// <summary>Whether any part of <paramref name="label"/>, a rectangle on the render, falls inside the window's outline, sampled at its corners, edges and middle.</summary>
        private static bool Behind(Vector2[] outline, Rect label)
        {
            for (var x = 0; x <= 2; x++)
            {
                for (var y = 0; y <= 2; y++)
                {
                    if (Inside(outline, new Vector2(Mathf.Lerp(label.xMin, label.xMax, x / 2f), Mathf.Lerp(label.yMin, label.yMax, y / 2f)))) return true;
                }
            }
            return false;
        }
    }
}
