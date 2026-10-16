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
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the spatial kit, a candidate no production view draws yet, in its first three states, the focused
    /// work, the overview of three projects and the compact presence, and a specimen of its controls, from the eyes
    /// with a Quest 3S's field at about 22 pixels a degree at the centre, at both text sizes, as the eye buffer and
    /// laid over a dark room and a bright synthetic room as the compositor lays it over passthrough. It checks what a
    /// render can: every word reads at 14 dp or more as the eyes see it, every target is a whole hand target and
    /// 12 mm from the next, each instrument stays inside the field, every face that carries words is opaque in the
    /// pixels, every word holds 4.5 to 1 in the pixels over the bright room, the agent's report and the person's
    /// draft show whole and keep their provenance, and the connected switch moves only after the existing Minimize and
    /// Restore routes of a real <see cref="MenuDirector"/> confirm the presentation. Every project, task, message and
    /// event is synthetic, and every image says so. None of this is evidence of comfort, of readability through a
    /// headset's optics or passthrough, or of design acceptance.
    /// In the editor: Halcyonic > Render the Spatial Kit. In batch mode, with graphics:
    /// -executeMethod Halcyonic.XR.Workspace.Editor.KitRender.Check; it exits 1 when a check fails. Images go to
    /// apps/xr/Builds/KitRenders, which git ignores.
    /// </summary>
    public static partial class KitRender
    {
        private const int Width = 2800;
        private const int Height = 2520;
        private const float VerticalFieldOfView = 90f;
        private const float EyeHeight = 1.2f;
        private const int KitLayer = 30;

        private static readonly ViewField Quest3S = new ViewField(48, 48, 45, 45);

        [MenuItem("Halcyonic/Render the Spatial Kit")]
        public static void Menu() => Run(exitWhenDone: false);

        /// <summary>The fixture's one entry point in batch mode: renders and checks everything, then exits 1 on any failure.</summary>
        public static void Check() => Run(exitWhenDone: true);

        private static void Run(bool exitWhenDone)
        {
            var failures = new List<string>();
            var previousField = ViewField.Current;
            var previousStill = GlazeMotion.Still;
            try
            {
                failures.AddRange(ThumbMotion());
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var size = variant.Length == 0 ? "Standard" : variant;
                    var found = new List<string>();
                    foreach (var state in new[] { "1-focused-work", "2-overview", "3-compact-presence", "4-control-kit" })
                    {
                        try { found.AddRange(RenderState(state, size)); }
                        catch (Exception error) { found.Add(state + " threw " + error); }
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("the kit render threw " + error); }
            finally
            {
                ViewField.Current = previousField;
                GlazeMotion.Still = previousStill;
                KeepFontsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: spatial kit: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: spatial kit: every check passed.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>What one state's render needs: its root, where the head turns, and what to check.</summary>
        private sealed class Shot
        {
            public Transform Root = null!;
            public float Yaw;
            public float Elevation;
            public Vector3 Centre;
            public float PitchDown;
            public List<(string Plane, Transform Space, IEnumerable<KitPress> Targets)> Targets = new List<(string, Transform, IEnumerable<KitPress>)>();
            public List<Surface> Faces = new List<Surface>();
            public List<Vector3> Corners = new List<Vector3>();
            public List<string> Failures = new List<string>();
            public Action<List<string>>? AfterRender;
            public string Caption = "";

            /// <summary>Objects the shot made outside its root, as the existing director, destroyed with it.</summary>
            public List<GameObject> Extras = new List<GameObject>();

            /// <summary>A face drawn translucent once after the shot's renders, which the opacity check must refuse.</summary>
            public Surface? OpacityNegative;

            /// <summary>A second view, the head turned to glance at what the first shows at its side.</summary>
            public (float Yaw, float Elevation)? Glance;
        }

        private static List<string> RenderState(string state, string size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ViewField.Current = Quest3S;
            GlazeMotion.Still = true;
            var name = state + " (" + size + ")";
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "KitRenders", size));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("Spatial kit: " + state);
            var fixture = new GameObject("Fixture camera and caption");
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            Shot? made = null;
            try
            {
                var shot = made = state switch
                {
                    "1-focused-work" => FocusedWork(root.transform, eyes),
                    "2-overview" => Overview(root.transform, eyes),
                    "3-compact-presence" => CompactPresence(root.transform, eyes),
                    _ => ControlKit(root.transform, eyes),
                };
                failures.AddRange(shot.Failures.Select(failure => name + ": " + failure));
                SetLayer(root, KitLayer);
                ForceMeshes(root);
                failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                foreach (var (plane, space, targets) in shot.Targets)
                {
                    var shown = targets.Where(press => press.gameObject.activeInHierarchy).ToList();
                    failures.AddRange(TargetsWhole(shown, eyes, name + " " + plane));
                    failures.AddRange(TargetsApart(shown, space, name + " " + plane));
                }
                if (shot.Corners.Count > 0) failures.AddRange(GlazeChecks.InsideField(name, shot.Corners, eyes, shot.Centre, shot.PitchDown, Quest3S));

                var camera = MakeCamera(fixture.transform, eyes, target, shot.Yaw, shot.Elevation);
                Caption(fixture.transform, camera, shot.Caption + " · " + size + " text");
                SetLayer(fixture, KitLayer);
                ForceMeshes(fixture);
                var image = Render(camera, target);
                Texture2D? bright = null, dark = null;
                try
                {
                    var eye = image.GetPixels32();
                    failures.AddRange(Opaque(shot.Faces, camera, eye, name));
                    bright = Composite(image, BrightRoom(Width, Height, camera), Color.white);
                    dark = Composite(image, null, DarkRoom);
                    failures.AddRange(WordsHoldContrast(root, camera, bright.GetPixels32(), name + " over the bright room"));
                    failures.AddRange(WordsHoldContrast(root, camera, dark.GetPixels32(), name + " over the dark room"));
                    File.WriteAllBytes(Path.Combine(folder, state + "-rgba.png"), image.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-bright.png"), bright.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-dark.png"), dark.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(image);
                    if (bright != null) UnityEngine.Object.DestroyImmediate(bright);
                    if (dark != null) UnityEngine.Object.DestroyImmediate(dark);
                }
                if (shot.Glance is (float glanceYaw, float glanceElevation))
                {
                    // The same scene with the head turned to it: what a glance reads, square to the eyes.
                    camera.transform.rotation = Quaternion.Euler(-glanceElevation, glanceYaw, 0f);
                    var glance = Render(camera, target);
                    Texture2D? glanceBright = null, glanceDark = null;
                    try
                    {
                        failures.AddRange(Opaque(shot.Faces, camera, glance.GetPixels32(), name + " glance"));
                        glanceBright = Composite(glance, BrightRoom(Width, Height, camera), Color.white);
                        glanceDark = Composite(glance, null, DarkRoom);
                        failures.AddRange(WordsHoldContrast(root, camera, glanceBright.GetPixels32(), name + " glance over the bright room"));
                        File.WriteAllBytes(Path.Combine(folder, state + "-glance-bright.png"), glanceBright.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(folder, state + "-glance-dark.png"), glanceDark.EncodeToPNG());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(glance);
                        if (glanceBright != null) UnityEngine.Object.DestroyImmediate(glanceBright);
                        if (glanceDark != null) UnityEngine.Object.DestroyImmediate(glanceDark);
                    }
                    camera.transform.rotation = Quaternion.Euler(-shot.Elevation, shot.Yaw, 0f);
                }
                if (shot.OpacityNegative is Surface negative)
                {
                    // The opacity check's own witness: the same face drawn translucent must fail it.
                    negative.Fade(0.15f);
                    var faded = Render(camera, target);
                    try
                    {
                        if (!Opaque(new[] { negative }, camera, faded.GetPixels32(), name + " translucent witness").Any())
                            failures.Add(name + ": the opacity check accepted a face drawn at 15 percent");
                        else Debug.Log("Halcyonic: spatial kit: " + name + ": the opacity check refused the translucent witness, as it must.");
                    }
                    finally
                    {
                        negative.Fade(1f);
                        UnityEngine.Object.DestroyImmediate(faded);
                    }
                }
                var after = new List<string>();
                shot.AfterRender?.Invoke(after);
                failures.AddRange(after.Select(failure => name + ": " + failure));
                Debug.Log("Halcyonic: spatial kit: " + name + " rendered " + Width + " by " + Height + ", head turned to "
                    + Degrees(shot.Yaw) + " across and " + Degrees(shot.Elevation) + " up.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(fixture);
                if (made != null) foreach (var extra in made.Extras) UnityEngine.Object.DestroyImmediate(extra);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            return failures;
        }

        /// <summary>Stands a view centred <paramref name="yaw"/> across and <paramref name="elevation"/> up, facing the eyes there, at the plane's distance.</summary>
        private static Vector3 Place(Transform view, Vector3 eyes, float yaw, float elevation)
        {
            var direction = new PanelDirection(yaw, elevation, true, false);
            view.SetPositionAndRotation(PlaneLayout.PointOf(eyes, direction, 0f, 0f), PlaneLayout.Facing(direction));
            view.localScale = Vector3.one * PlaneComposition.Distance;
            return view.position;
        }

        /// <summary>The elevation of an instrument's centre whose top edge rests at <see cref="SpatialKit.RestingTopDegrees"/>.</summary>
        private static float CentreFor(float heightUnits) => SpatialKit.RestingTopDegrees - SpatialKit.DegreesTo(heightUnits / 2f);

        private static float PitchFor(Vector2 sizeUnits, float elevation) => WorkspacePlacement.ReadingPitch(
            new PanelSize(PlaneComposition.Distance, sizeUnits.x / 2f * PlaneComposition.Distance, sizeUnits.y / 2f * PlaneComposition.Distance), elevation, Quest3S);

        private static Shot FocusedWork(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "Focused work, synthetic data" };
            var view = KitWorkView.Create(root);
            var model = WorkModel();
            view.Show(model);
            shot.Elevation = CentreFor(view.Size.y);
            shot.Centre = Place(view.transform, eyes, 0f, shot.Elevation);
            shot.PitchDown = PitchFor(view.Size, shot.Elevation);
            shot.Targets.Add(("instrument", view.transform, view.Targets));
            shot.Faces.AddRange(view.Carriers.Select(carrier => carrier.Face));
            shot.Corners.AddRange(view.Corners());
            shot.OpacityNegative = view.Slate.Face;
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            Require(view.Message.Complete, "the agent's report shows " + view.Message.ShownLines + " of its " + view.Message.Lines + " lines");
            Require(GlazeRuns.Of(view.Message.Prose).IsAttributedProse && !GlazeRuns.Of(view.Message.Prose).IsNamedIdentity, "the report lost its attributed-prose treatment");
            Require(view.Message.Theirs.Count > 0 && view.Message.Prose.richText == false && view.Message.Prose.parseCtrlCharacters, "the report lost its outside ranges or literal rendering");
            Require(view.Message.Chip.gameObject.activeInHierarchy && view.Message.Chip.Words.text == KitMessage.Source, "the report lost its Agent says chip");
            Require(GlazeRuns.Of(view.Title).IsNamedIdentity, "the task's name is not shown as a named identity");
            Require(view.Composer.ShownDraftLines == view.Composer.DraftLines, "the draft shows " + view.Composer.ShownDraftLines + " of its " + view.Composer.DraftLines + " lines");
            Require(!view.Spine.Switch.ConfirmedCompact && view.Spine.Switch.ThumbPosition == 0f, "the switch's thumb is not on Workspace in the whole workspace");
            Require(view.Trail.Shown.All(entry => entry.Entry.Kind != ActivityKind.Message), "the evidence rail shows an agent message");
            Require(view.Trail.Shown.Count > 0, "the evidence rail shows no recorded event");
            var facing = Vector3.Angle(view.Slate.transform.forward, view.Slate.transform.position - eyes);
            Require(view.Slate.transform.forward == view.transform.forward && Mathf.Abs(Vector3.Angle(view.transform.forward, view.transform.position - eyes)) < 0.5f,
                "the instrument does not face the eyes at its centre (" + Degrees(facing) + " degrees at the slate)");
            Debug.Log("Halcyonic: spatial kit: focused work " + Degrees(SpatialKit.DegreesTo(view.Size.x / 2f) * 2f) + " by "
                + Degrees(SpatialKit.DegreesTo(view.Size.y / 2f) * 2f) + " degrees; report " + view.Message.Lines + " lines; trail shows "
                + view.Trail.Shown.Count + " of " + model.Trail.Count + " events, " + view.Trail.Earlier + " earlier.");
            return shot;
        }

        private static Shot Overview(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "Overview of three projects, synthetic data" };
            var view = KitOverviewView.Create(root);
            var model = OverviewModel();
            view.Show(model);
            shot.Elevation = CentreFor(view.Size.y);
            shot.Centre = Place(view.transform, eyes, 0f, shot.Elevation);
            shot.PitchDown = PitchFor(view.Size, shot.Elevation);
            shot.Targets.Add(("instrument", view.transform, view.Targets));
            shot.Faces.AddRange(view.Carriers.Select(carrier => carrier.Face));
            shot.Corners.AddRange(view.Corners());
            var rows = view.TaskRows.ToList();
            if (rows.Count != model.Projects.Sum(project => project.Tasks.Count)) shot.Failures.Add("the board shows " + rows.Count + " task rows");
            foreach (var row in rows)
            {
                var task = row.Model!;
                if (task.Icon == null || task.Detail == null) shot.Failures.Add("a task row says its state by colour alone: " + task.Key);
            }
            Debug.Log("Halcyonic: spatial kit: overview " + Degrees(SpatialKit.DegreesTo(view.Size.x / 2f) * 2f) + " by "
                + Degrees(SpatialKit.DegreesTo(view.Size.y / 2f) * 2f) + " degrees, " + rows.Count + " task rows.");
            return shot;
        }

        /// <summary>
        /// The compact presence, reached the way a person reaches it: from the whole workspace, by the switch's Compact
        /// position, through a real director's existing <see cref="MenuDirector.MinimizeWorkspace"/>. The fixture plays
        /// the switch's owner: it forwards the requests and shows whichever view the confirmed presentation calls for.
        /// </summary>
        private static Shot CompactPresence(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "Compact presence beside other work, synthetic data" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var presentation = new WorkspacePresentationState();
            // Outside the kit's root and layer: its own views are never measured or rendered here.
            var directorRoot = new GameObject("Existing director, never rendered").transform;
            shot.Extras.Add(directorRoot.gameObject);
            var client = new ClientInfo { Name = "kit-render", Version = "kit-render", DeviceLabel = "kit-render" };
            var director = MenuDirector.Create(directorRoot, new MenuDirector.Setup
            {
                SpatialWorkspace = true, Presentation = presentation, InitialPlacementReady = () => true,
                Commands = new CommandFactory(client),
            });

            var work = KitWorkView.Create(root);
            work.Show(WorkModel());
            var workElevation = CentreFor(work.Size.y);
            Place(work.transform, eyes, 0f, workElevation);
            var presence = KitPresenceView.Create(root);
            presence.Show(PresenceModel());
            shot.Centre = Place(presence.transform, eyes, SpatialKit.PresenceYawDegrees, SpatialKit.PresenceElevationDegrees);
            presence.gameObject.SetActive(false);
            var requests = 0;
            void Confirmed()
            {
                var compact = presentation.Minimized;
                work.gameObject.SetActive(!compact);
                presence.gameObject.SetActive(compact);
                work.Spine.Switch.ShowConfirmed(compact);
                presence.Switch.ShowConfirmed(compact);
            }
            presentation.Changed += Confirmed;
            work.Spine.Switch.CompactRequested += () => { requests++; director.MinimizeWorkspace(); };
            work.Spine.Switch.WorkspaceRequested += () => { requests++; director.RestoreWorkspace(); };
            presence.Switch.CompactRequested += () => { requests++; director.MinimizeWorkspace(); };
            presence.Switch.WorkspaceRequested += () => { requests++; director.RestoreWorkspace(); };
            var workSwitch = work.Spine.Switch;
            foreach (var press in new[] { workSwitch.WorkspaceTarget, workSwitch.CompactTarget, presence.Switch.WorkspaceTarget, presence.Switch.CompactTarget })
                press.SettleForRender();

            // The current position is a fact, not an action.
            workSwitch.WorkspaceTarget.PressForRender(301);
            Require(requests == 0 && !presentation.Minimized, "pressing the current Workspace position asked for something");
            // A switch whose host takes no presses asks nothing, and its thumb never moves on a press alone.
            workSwitch.CompactTarget.Accepting = () => false;
            workSwitch.CompactTarget.PressForRender(302);
            Require(requests == 0 && !presentation.Minimized && !workSwitch.ConfirmedCompact && workSwitch.ThumbPosition == 0f,
                "a press the host refused moved the thumb or reached the route");
            workSwitch.CompactTarget.Accepting = () => true;
            // The real route: Compact asks, the director's existing Minimize suspends and minimizes, the thumb follows.
            workSwitch.CompactTarget.PressForRender(303);
            Require(requests == 1 && presentation.Minimized, "Compact did not reach the existing Minimize route");
            Require(director.IsCompact, "the existing Minimize route's suspension did not compact the retained views");
            Require(presence.gameObject.activeSelf && !work.gameObject.activeSelf, "the confirmed presentation did not show the compact presence");
            Require(presence.Switch.ConfirmedCompact && presence.Switch.ThumbPosition == 1f, "the compact presence's thumb is not on Compact");

            // The person looks at their other work, the monitor ahead and to the right; the presence waits low at the left.
            shot.Yaw = 0f;
            shot.Elevation = -18f;
            shot.Glance = (SpatialKit.PresenceYawDegrees, SpatialKit.PresenceElevationDegrees);
            shot.PitchDown = 0f;
            shot.Targets.Add(("compact presence", presence.transform, presence.Targets));
            shot.Faces.Add(presence.Carrier.Face);
            shot.Corners.AddRange(presence.Corners());
            shot.AfterRender = failures =>
            {
                // Back again: Workspace forwards to the existing Restore route; the retained views expand again.
                presence.Switch.WorkspaceTarget.SettleForRender();
                presence.Switch.WorkspaceTarget.PressForRender(304);
                if (presentation.Minimized || requests != 2) failures.Add("Workspace did not reach the existing Restore route");
                if (!work.gameObject.activeSelf || work.Spine.Switch.ConfirmedCompact) failures.Add("the confirmed restore did not return the whole workspace with its thumb on Workspace");
                if (director.IsCompact) failures.Add("the existing Restore route left the retained views compact");
                Debug.Log("Halcyonic: spatial kit: the switch reached Minimize and Restore through a real director: requests "
                    + requests + ", minimized " + presentation.Minimized + ", compact views " + director.IsCompact + ".");
            };
            Debug.Log("Halcyonic: spatial kit: compact presence " + Degrees(SpatialKit.DegreesTo(presence.Size.x / 2f) * 2f) + " by "
                + Degrees(SpatialKit.DegreesTo(presence.Size.y / 2f) * 2f) + " degrees at " + Degrees(SpatialKit.PresenceYawDegrees) + " across, "
                + Degrees(SpatialKit.PresenceElevationDegrees) + " up.");
            return shot;
        }

        /// <summary>
        /// The thumb's own motion, checked without a render: it travels only after a confirmation, stretches toward
        /// where it goes mid-move, settles there in <see cref="SpatialKit.SwitchSeconds"/>, and under Keep things still
        /// is there at once.
        /// </summary>
        private static List<string> ThumbMotion()
        {
            var failures = new List<string>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Thumb motion");
            try
            {
                var view = KitModeSwitch.Create(root.transform);
                view.Layout(view.NarrowestWidth());
                GlazeMotion.Still = false;
                view.ShowConfirmed(true);
                if (!view.Moving || view.ThumbPosition != 0f) failures.Add("the thumb jumped before moving");
                view.Advance(SpatialKit.SwitchSeconds / 2f);
                var middle = view.ThumbPosition;
                var stretched = view.Thumb.Size.y > KitType.Units(SpatialKit.SwitchCellDegrees) * 1.05f;
                view.Advance(SpatialKit.SwitchSeconds);
                if (!(middle > 0.2f && middle < 0.8f) || !stretched) failures.Add("the thumb did not travel as one stretched shape (middle " + middle + ")");
                if (view.Moving || view.ThumbPosition != 1f) failures.Add("the thumb did not settle on Compact");
                GlazeMotion.Still = true;
                view.ShowConfirmed(false);
                if (view.Moving || view.ThumbPosition != 0f) failures.Add("under Keep things still the thumb did not move at once");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                GlazeMotion.Still = true;
            }
            return failures;
        }

        /// <summary>Every target is a whole ordinary hand target as the eyes see it: 3.75 degrees, as GlazeChecks.TargetsLargeEnough allows for a press.</summary>
        private static IEnumerable<string> TargetsWhole(IReadOnlyList<KitPress> targets, Vector3 eyes, string what)
        {
            var least = float.MaxValue;
            foreach (var press in targets)
            {
                var distance = GlazeChecks.PlaneDistance(eyes, press.transform);
                var scale = press.transform.lossyScale;
                var tall = Glaze.DegreesOf(press.Size.y * scale.y, distance);
                var wide = Glaze.DegreesOf(press.Size.x * scale.x, distance);
                least = Mathf.Min(least, Mathf.Min(tall, wide));
                if (Mathf.Min(tall, wide) < SpatialKit.TargetDegrees * 0.96f - 0.01f)
                    yield return what + ": " + press.name + " is " + Degrees(wide) + " by " + Degrees(tall) + " degrees, under " + Degrees(SpatialKit.TargetDegrees) + ".";
            }
            if (targets.Count > 0) Debug.Log("Halcyonic: spatial kit: " + what + ": " + targets.Count + " targets, the least " + least.ToString("0.00", CultureInfo.InvariantCulture) + " degrees.");
        }

        /// <summary>Every two targets on one plane stand at least 12 mm apart, edge to edge.</summary>
        private static IEnumerable<string> TargetsApart(IReadOnlyList<KitPress> targets, Transform space, string what)
        {
            var rects = targets.Select(press =>
            {
                var half = press.Size / 2f;
                var a = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(-half.x, -half.y, 0f)));
                var b = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(half.x, half.y, 0f)));
                return (press, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }).ToList();
            var metres = space.lossyScale.x;
            var nearest = float.MaxValue;
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var (first, a) = rects[i];
                var (second, b) = rects[j];
                var dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
                var dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
                var apart = Mathf.Sqrt(dx * dx + dy * dy) * metres;
                nearest = Mathf.Min(nearest, apart);
                if (apart < Glaze.TargetGapMeters - 0.0003f)
                    yield return what + ": " + first.name + " and " + second.name + " are " + (apart * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart, under 12.";
            }
            if (rects.Count > 1) Debug.Log("Halcyonic: spatial kit: " + what + ": the nearest two targets are " + (nearest * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart.");
        }

        /// <summary>Every face that carries words is opaque in the eye buffer: no room shows through it.</summary>
        private static IEnumerable<string> Opaque(IEnumerable<Surface> faces, Camera camera, Color32[] eye, string what)
        {
            foreach (var face in faces)
            {
                if (!face.gameObject.activeInHierarchy) continue;
                var least = 255;
                for (var i = 1; i < 12; i++)
                for (var j = 1; j < 12; j++)
                {
                    var local = new Vector3(-0.46f + 0.92f * i / 12f, -0.46f + 0.92f * j / 12f, 0f);
                    var screen = camera.WorldToScreenPoint(face.transform.TransformPoint(local));
                    var x = Mathf.RoundToInt(screen.x);
                    var y = Mathf.RoundToInt(screen.y);
                    if (screen.z <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    least = Mathf.Min(least, eye[y * Width + x].a);
                }
                if (least < 254) yield return what + ": " + face.transform.parent.name + "'s face lets the room through (alpha " + least + " of 255).";
            }
        }

        /// <summary>
        /// Every word's strongest pixel holds 4.5 to 1 against the ground it stands on, as rendered: the ground is the
        /// most common colour inside the word's own box, which its letters leave mostly bare.
        /// </summary>
        private static IEnumerable<string> WordsHoldContrast(GameObject root, Camera camera, Color32[] pixels, string what)
        {
            var weakest = float.MaxValue;
            var weakestName = "";
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label) || label.textInfo == null || label.textInfo.characterCount == 0) continue;
                var bounds = label.textBounds;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var x in new[] { bounds.min.x, bounds.max.x })
                foreach (var y in new[] { bounds.min.y, bounds.max.y })
                {
                    var screen = camera.WorldToScreenPoint(label.transform.TransformPoint(new Vector3(x, y, 0f)));
                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                }
                var rect = new RectInt(Mathf.Max(0, Mathf.FloorToInt(min.x)), Mathf.Max(0, Mathf.FloorToInt(min.y)), 0, 0);
                rect.xMax = Mathf.Min(Width, Mathf.CeilToInt(max.x));
                rect.yMax = Mathf.Min(Height, Mathf.CeilToInt(max.y));
                if (rect.width < 2 || rect.height < 2) continue;
                var ground = MostCommon(pixels, rect);
                var strongest = 1.0;
                var groundLuminance = Luminance(ground);
                for (var y = rect.yMin; y < rect.yMax; y++)
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    var l = Luminance(pixels[y * Width + x]);
                    strongest = Math.Max(strongest, (Math.Max(l, groundLuminance) + 0.05) / (Math.Min(l, groundLuminance) + 0.05));
                }
                if (strongest < weakest) { weakest = (float)strongest; weakestName = label.name + " \"" + Clip(label.text) + "\""; }
                if (strongest < 4.5) yield return what + ": " + label.name + " \"" + Clip(label.text) + "\" reaches " + strongest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1.";
            }
            if (weakest < float.MaxValue) Debug.Log("Halcyonic: spatial kit: " + what + ": the weakest words are " + weakestName + " at " + weakest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1.");
        }

        private static string Clip(string text) => text.Length <= 40 ? text : text.Substring(0, 40) + "…";

        private static Color32 MostCommon(Color32[] pixels, RectInt rect)
        {
            var counts = new Dictionary<int, (int Count, Color32 Colour)>();
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
            {
                var c = pixels[y * Width + x];
                var key = (c.r >> 2) << 12 | (c.g >> 2) << 6 | (c.b >> 2);
                counts[key] = counts.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Colour) : (1, c);
            }
            return counts.Values.OrderByDescending(entry => entry.Count).First().Colour;
        }

        private static double Luminance(Color32 colour) => new GlazeColor(colour.r, colour.g, colour.b).Luminance;

        /// <summary>The person's eyes: a Quest 3S's 96 by 90 degree field, the head turned to <paramref name="yaw"/> and <paramref name="elevation"/>.</summary>
        private static Camera MakeCamera(Transform parent, Vector3 eyes, RenderTexture texture, float yaw, float elevation)
        {
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.Euler(-elevation, yaw, 0f));
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.fieldOfView = VerticalFieldOfView;
            camera.aspect = (float)Width / Height;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.cullingMask = 1 << KitLayer;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            return camera;
        }

        /// <summary>The image's own caption, part of the fixture and never of the kit: what this is and that its data is synthetic.</summary>
        private static void Caption(Transform parent, Camera camera, string words)
        {
            var holder = new GameObject("Caption");
            holder.transform.SetParent(camera.transform, false);
            // A metre out, near the frame's top left, square to the view.
            var halfHeight = Mathf.Tan(VerticalFieldOfView / 2f * Mathf.Deg2Rad);
            var halfWidth = halfHeight * camera.aspect;
            holder.transform.localPosition = new Vector3(-halfWidth * 0.97f, halfHeight * 0.965f, 1f);
            var label = GlazeText.Create(holder.transform, "Caption words", GlazeType.Body, new Color(0.93f, 0.95f, 0.97f), TextAlignmentOptions.TopLeft, 40);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(1.0f));
            GlazeText.SetLiteral(label, "Editor render of the D1 spatial kit · " + words + " · not a headset capture");
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var (_, width) = GlazeText.Lay(label, 3f, 1);
            var plate = Surface.Create(holder.transform, "Caption plate", 39);
            var pad = GlazeTokens.Units(0.4f);
            var height = GlazeText.LineHeight(label);
            plate.Draw(new Vector2(width + 2f * pad, height + pad), pad, new Color(0.05f, 0.06f, 0.08f, 1f));
            plate.transform.localPosition = new Vector3(width / 2f, -height / 2f, 0.001f);
            label.transform.localPosition = new Vector3(0f, -pad / 4f, 0f);
        }

        private static Texture2D Render(Camera camera, RenderTexture texture)
        {
            var before = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = before;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        private static void SetLayer(GameObject root, int layer)
        {
            foreach (var each in root.GetComponentsInChildren<Transform>(true)) each.gameObject.layer = layer;
        }

        private static void ForceMeshes(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        /// <summary>Drawing text in the editor upgrades the committed font assets in memory; the editor must not save that.</summary>
        private static void KeepFontsAsCommitted()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!AssetDatabase.Contains(font)) continue;
                EditorUtility.ClearDirty(font);
                if (font.material != null) EditorUtility.ClearDirty(font.material);
                if (font.atlasTextures == null) continue;
                foreach (var atlas in font.atlasTextures) if (atlas != null) EditorUtility.ClearDirty(atlas);
            }
        }

        private static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
