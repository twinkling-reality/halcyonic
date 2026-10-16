#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Renders the spatial kit, a candidate no production view draws yet, in its first three states, the focused work
    /// and the overview of three projects, each on one gentle curve and on its fallback plane, the compact presence,
    /// and a specimen of its controls, from the eyes with a Quest 3S's field at about 22 pixels a degree at the centre,
    /// at both text sizes, as the eye buffer and laid over a dark room and a bright synthetic room as the compositor
    /// lays it over passthrough. It checks what a render can: every word reads at 14 dp or more as the eyes see it,
    /// every target is a whole hand target and 12 mm from the next, each instrument stays inside the field, every face
    /// that carries words is opaque in the pixels, every word holds 4.5 to 1 in the pixels over the bright room, the
    /// agent's report and the person's draft show whole and keep their provenance, and the connected switch moves only
    /// after the existing Minimize and Restore routes of a real <see cref="MenuDirector"/> confirm the presentation.
    /// Beside the images it writes each region's measures as the eyes meet it, for comparing the arrangements. Every
    /// project, task, message and event is synthetic, and every image says so. None of this is evidence of comfort, of
    /// readability through a headset's optics or passthrough, or of design acceptance.
    /// In the editor: Halcyonic > Render the Spatial Kit. In batch mode, with graphics: -executeMethod
    /// Halcyonic.XR.Workspace.Editor.KitRender.Check; it exits 1 when a check fails. Images go to
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
                    foreach (var state in new[] { "1-focused-work", "1-focused-work-plane", "2-overview", "2-overview-plane", "3-compact-presence", "4-control-kit" })
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

            /// <summary>The instrument whose regions are measured, its own frame the one the measures use.</summary>
            public Transform? Instrument;

            /// <summary>Whether the instrument is drawn on one gentle curve centred on the eyes rather than its plane.</summary>
            public bool Curved;

            /// <summary>Where the eyes see a point of the instrument as laid out: itself on the plane, bent onto the curve.</summary>
            public Func<Vector3, Vector3> Seen = point => point;

            /// <summary>Meshes the curve made, destroyed with the shot.</summary>
            public List<Mesh> Meshes = new List<Mesh>();

            /// <summary>Each region of the instrument, with the targets it carries, for the per-region measures.</summary>
            public List<(string Name, KitCarrier Carrier, IEnumerable<KitPress> Targets)> Regions = new List<(string, KitCarrier, IEnumerable<KitPress>)>();

            /// <summary>A bare point of a face and the colour it is drawn in: the eye buffer must hold it as the headset stores it.</summary>
            public (Surface Face, Vector3 Local, GlazeColor Colour)? Witness;
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
            // Eight bits a channel stored as sRGB, as a headset's eye buffers are in a linear project: stored linear,
            // the dark carriers would keep only a few steps and lose the recess shade.
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            Shot? made = null;
            try
            {
                var shot = made = state switch
                {
                    // The kit is drawn on one gentle curve; its plane, the fallback, beside it.
                    "1-focused-work" => FocusedWork(root.transform, eyes, curved: true),
                    "1-focused-work-plane" => FocusedWork(root.transform, eyes, curved: false),
                    "2-overview" => Overview(root.transform, eyes, curved: true),
                    "2-overview-plane" => Overview(root.transform, eyes, curved: false),
                    "3-compact-presence" => CompactPresence(root.transform, eyes),
                    _ => ControlKit(root.transform, eyes),
                };
                failures.AddRange(shot.Failures.Select(failure => name + ": " + failure));
                SetLayer(root, KitLayer);
                ForceMeshes(root);
                // The curve is drawn by bending the laid-out plane's meshes round the eyes; everything after measures
                // where the eyes see each point.
                if (shot.Curved) shot.Meshes.AddRange(BendMeshes(root, shot.Seen));
                failures.AddRange(shot.Curved ? TextAsSeenWhereDrawn(root, eyes, name, shot.Seen) : GlazeChecks.TextAsSeen(root, eyes, name));
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
                    failures.AddRange(Opaque(shot.Faces, camera, eye, name, shot.Seen));
                    if (shot.Witness is (Surface witnessFace, Vector3 witnessLocal, GlazeColor witnessColour))
                        failures.AddRange(StoredAsTheHeadsetStores(witnessFace, witnessLocal, witnessColour, camera, eye, name, shot.Seen));
                    bright = Composite(image, BrightRoom(Width, Height, camera), Color.white);
                    dark = Composite(image, null, DarkRoom);
                    failures.AddRange(WordsHoldContrast(root, camera, bright.GetPixels32(), name + " over the bright room", shot.Seen));
                    failures.AddRange(WordsHoldContrast(root, camera, dark.GetPixels32(), name + " over the dark room", shot.Seen));
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
                        failures.AddRange(Opaque(shot.Faces, camera, glance.GetPixels32(), name + " glance", shot.Seen));
                        glanceBright = Composite(glance, BrightRoom(Width, Height, camera), Color.white);
                        glanceDark = Composite(glance, null, DarkRoom);
                        failures.AddRange(WordsHoldContrast(root, camera, glanceBright.GetPixels32(), name + " glance over the bright room", shot.Seen, glance: true));
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
                        if (!Opaque(new[] { negative }, camera, faded.GetPixels32(), name + " translucent witness", shot.Seen).Any())
                            failures.Add(name + ": the opacity check accepted a face drawn at 15 percent");
                        else Debug.Log("Halcyonic: spatial kit: " + name + ": the opacity check refused the translucent witness, as it must.");
                    }
                    finally
                    {
                        negative.Fade(1f);
                        UnityEngine.Object.DestroyImmediate(faded);
                    }
                }
                Measure(shot, eyes, name, folder, state);
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
                if (made != null) foreach (var mesh in made.Meshes) UnityEngine.Object.DestroyImmediate(mesh);
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

        /// <summary>
        /// <see cref="WorkspacePlacement.ReadingPitch"/>'s rule for an outline that is not one flat plate, as the curve's:
        /// the head tips as far as the lowest corners need to stand inside the field less its margin, at most
        /// <see cref="WorkspacePlacement.MostReadingPitchDegrees"/>, with the head turned toward <paramref name="toward"/>.
        /// Where no tip that far is enough it returns the most, and the field check then fails.
        /// </summary>
        private static float ReadingPitchOf(IReadOnlyList<Vector3> corners, Vector3 eyes, Vector3 toward)
        {
            var margin = ViewField.EdgeMarginDegrees - 0.01;
            var shrunk = new ViewField(Quest3S.Left - margin, Quest3S.Right - margin, Quest3S.Up - margin, Quest3S.Down - margin);
            var flat = new Vector3(toward.x - eyes.x, 0f, toward.z - eyes.z);
            var yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            bool Fits(float pitch)
            {
                var head = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0f));
                foreach (var corner in corners)
                {
                    var local = head * (corner - eyes);
                    var across = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    var up = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                    if (!shrunk.Shows(across, up)) return false;
                }
                return true;
            }
            if (Fits(0f)) return 0f;
            var most = WorkspacePlacement.MostReadingPitchDegrees;
            if (!Fits(most)) return most;
            float low = 0f, high = most;
            for (var step = 0; step < 30; step++)
            {
                var middle = (low + high) / 2f;
                if (Fits(middle)) high = middle;
                else low = middle;
            }
            return high;
        }

        private static Shot FocusedWork(Transform root, Vector3 eyes, bool curved)
        {
            var shot = new Shot { Caption = curved ? "Focused work on one gentle curve, synthetic data" : "Focused work on one plane, the fallback, synthetic data", Curved = curved };
            var view = KitWorkView.Create(root);
            var model = WorkModel();
            view.Show(model);
            shot.Elevation = CentreFor(view.Size.y);
            shot.Centre = Place(view.transform, eyes, 0f, shot.Elevation);
            Stand(shot, view.transform, eyes, view.Carriers, view.Size);
            shot.Glance = TurnTo(shot.Seen(view.Rail.transform.TransformPoint(new Vector3(KitType.Units(SpatialKit.InstrumentRadiusDegrees) / 2f, 0f, 0f))), eyes);
            // The target checks hold on the curve as on the plane: each target's tangent plane stands at its plane's
            // distance, and the arcs between targets are their distances on the plane (chords shorter by under 0.01 mm).
            shot.Targets.Add(("instrument", view.transform, view.Targets));
            shot.Regions.Add(("context spine", view.SpineCarrier, view.SpineTargets));
            shot.Regions.Add(("reading slate", view.Slate, view.SlateTargets));
            shot.Regions.Add(("evidence rail", view.Rail, view.RailTargets));
            shot.Witness = (view.Slate.Face, new Vector3(-0.48f, 0f, 0f), SpatialKit.Slate);
            shot.Faces.AddRange(view.Carriers.Select(carrier => carrier.Face));
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
            FacesTheEyes(shot, view.transform, view.Carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: focused work " + (curved ? "on one gentle curve " : "on one plane ") + Spread(shot, eyes) + "; report " + view.Message.Lines
                + " lines; trail shows " + view.Trail.Shown.Count + " of " + model.Trail.Count + " events, " + view.Trail.Earlier + " earlier.");
            return shot;
        }

        private static Shot Overview(Transform root, Vector3 eyes, bool curved)
        {
            var shot = new Shot { Caption = curved ? "Overview of three projects on one gentle curve, synthetic data" : "Overview of three projects on one plane, the fallback, synthetic data", Curved = curved };
            var view = KitOverviewView.Create(root);
            var model = OverviewModel();
            view.Show(model);
            shot.Elevation = CentreFor(view.Size.y);
            shot.Centre = Place(view.transform, eyes, 0f, shot.Elevation);
            Stand(shot, view.transform, eyes, view.Carriers, view.Size);
            shot.Targets.Add(("instrument", view.transform, view.Targets));
            shot.Regions.Add(("context spine", view.SpineCarrier, view.SpineTargets));
            shot.Regions.Add(("board", view.Board, view.BoardTargets));
            shot.Witness = (view.Board.Face, new Vector3(-0.48f, 0f, 0f), SpatialKit.Slate);
            shot.Faces.AddRange(view.Carriers.Select(carrier => carrier.Face));
            var rows = view.TaskRows.ToList();
            if (rows.Count != model.Projects.Sum(project => project.Tasks.Count)) shot.Failures.Add("the board shows " + rows.Count + " task rows");
            foreach (var row in rows)
            {
                var task = row.Model!;
                if (task.Icon == null || task.Detail == null) shot.Failures.Add("a task row says its state by colour alone: " + task.Key);
            }
            FacesTheEyes(shot, view.transform, view.Carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: overview " + (curved ? "on one gentle curve " : "on one plane ") + Spread(shot, eyes) + ", " + rows.Count + " task rows.");
            return shot;
        }

        /// <summary>
        /// Stands a laid-out instrument for its shot, its outline (corners and points along every edge) kept for the
        /// field check and its spread: on its plane, the head tipped by the reading pitch of a flat plate; or on one
        /// gentle curve centred on the eyes, its outline bent with it and the head tipped as far as that outline's lowest
        /// points need (<see cref="ReadingPitchOf"/>).
        /// </summary>
        private static void Stand(Shot shot, Transform view, Vector3 eyes, IEnumerable<KitCarrier> carriers, Vector2 size)
        {
            shot.Instrument = view;
            if (shot.Curved) shot.Seen = CurveAbout(view, eyes);
            foreach (var carrier in carriers)
            {
                var half = carrier.Size / 2f;
                for (var step = 0; step <= 12; step++)
                {
                    var t = -1f + step / 6f;
                    foreach (var local in new[] { new Vector3(t * half.x, half.y, 0f), new Vector3(t * half.x, -half.y, 0f), new Vector3(half.x, t * half.y, 0f), new Vector3(-half.x, t * half.y, 0f) })
                        shot.Corners.Add(shot.Seen(carrier.transform.TransformPoint(local)));
                }
            }
            shot.PitchDown = shot.Curved ? ReadingPitchOf(shot.Corners, eyes, shot.Centre) : PitchFor(size, shot.Elevation);
        }

        /// <summary>
        /// One gentle curve centred on the eyes, about the instrument's own up: a point the plane lays out at an arc's
        /// length across from its centre stands that arc round the eyes at the same distance, so every column of the
        /// instrument faces the eyes across and keeps its words' size; the recessed regions curve the same way a
        /// little farther out.
        /// </summary>
        private static Func<Vector3, Vector3> CurveAbout(Transform view, Vector3 eyes)
        {
            var eye = view.InverseTransformPoint(eyes);
            return world =>
            {
                var from = view.InverseTransformPoint(world) - eye;
                var angle = from.x / from.z;
                return view.TransformPoint(eye + new Vector3(from.z * Mathf.Sin(angle), from.y, from.z * Mathf.Cos(angle)));
            };
        }

        /// <summary>
        /// Draws the laid-out instrument on its curve: every surface's quad becomes a strip of columns half a degree
        /// wide, its UVs kept so the shape draws the same, and every glyph of every label is moved where the curve puts
        /// it. Returns the meshes it made.
        /// </summary>
        private static List<Mesh> BendMeshes(GameObject root, Func<Vector3, Vector3> bend)
        {
            var made = new List<Mesh>();
            var column = Mathf.Tan(0.5f * Mathf.Deg2Rad) * PlaneComposition.Distance;
            foreach (var surface in root.GetComponentsInChildren<Surface>(true))
            {
                var filter = surface.GetComponent<MeshFilter>();
                var t = surface.transform;
                var scale = t.lossyScale;
                if (filter == null || Mathf.Abs(scale.x) < 1e-6f || Mathf.Abs(scale.y) < 1e-6f) continue;
                var columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(scale.x) / column), 1, 400);
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                for (var i = 0; i <= columns; i++)
                {
                    var u = (float)i / columns;
                    foreach (var v in new[] { 0f, 1f })
                    {
                        vertices.Add(t.InverseTransformPoint(bend(t.TransformPoint(new Vector3(u - 0.5f, v - 0.5f, 0f)))));
                        uvs.Add(new Vector2(u, v));
                    }
                    // As the shared quad winds: bottom left, top right, bottom right; bottom left, top left, top right.
                    if (i < columns) triangles.AddRange(new[] { 2 * i, 2 * i + 3, 2 * i + 2, 2 * i, 2 * i + 1, 2 * i + 3 });
                }
                var mesh = new Mesh { name = "Glaze surface on the curve" };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
                made.Add(mesh);
            }
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.textInfo == null) continue;
                var t = label.transform;
                foreach (var info in label.textInfo.meshInfo)
                {
                    if (info.vertices == null) continue;
                    for (var k = 0; k < info.vertexCount; k++) info.vertices[k] = t.InverseTransformPoint(bend(t.TransformPoint(info.vertices[k])));
                }
                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
                if (label is TextMeshPro text && text.mesh != null) text.mesh.RecalculateBounds();
                foreach (var sub in label.GetComponentsInChildren<TMP_SubMesh>(true)) if (sub.mesh != null) sub.mesh.RecalculateBounds();
            }
            return made;
        }

        /// <summary>
        /// The instrument faces the eyes: on its plane, square to the line of sight at its centre; on the curve, every
        /// column across square to the line of sight to it, within half a degree, as STYLE holds a plane.
        /// </summary>
        private static void FacesTheEyes(Shot shot, Transform view, IEnumerable<KitCarrier> carriers, Vector3 eyes)
        {
            if (!shot.Curved)
            {
                var off = Vector3.Angle(view.forward, view.position - eyes);
                if (off >= 0.5f || carriers.Any(carrier => carrier.transform.forward != view.forward))
                    shot.Failures.Add("the instrument does not face the eyes at its centre (" + Degrees(off) + " degrees)");
                return;
            }
            foreach (var carrier in carriers)
            {
                var t = carrier.transform;
                var half = carrier.Size / 2f;
                for (var step = 0; step <= 8; step++)
                {
                    var local = new Vector3(-half.x + carrier.Size.x * step / 8f, 0f, 0f);
                    var normal = NormalAt(shot.Seen, t, local);
                    var toward = eyes - shot.Seen(t.TransformPoint(local));
                    var across = Vector3.Angle(Vector3.ProjectOnPlane(normal, view.up), Vector3.ProjectOnPlane(toward, view.up));
                    if (across >= 0.5f) { shot.Failures.Add(carrier.name + " meets the eyes " + Degrees(across) + " off square across on the curve."); break; }
                }
            }
        }

        /// <summary>The surface's normal toward the eyes where the eyes see a point of it, from its drawn neighbours.</summary>
        private static Vector3 NormalAt(Func<Vector3, Vector3> seen, Transform surface, Vector3 local)
        {
            var step = 0.002f;
            var right = seen(surface.TransformPoint(local + Vector3.right * step)) - seen(surface.TransformPoint(local - Vector3.right * step));
            var up = seen(surface.TransformPoint(local + Vector3.up * step)) - seen(surface.TransformPoint(local - Vector3.up * step));
            // Content faces -Z: the normal toward the eyes is up crossed with right in Unity's left-handed frame. Each is
            // made unit first: their raw cross product, a square millimetre or so, is under what normalized keeps.
            return Vector3.Cross(up.normalized, right.normalized).normalized;
        }

        /// <summary>The instrument's spread as the eyes see it, in its own frame, from its outline.</summary>
        private static string Spread(Shot shot, Vector3 eyes)
        {
            var frame = shot.Instrument!;
            var eye = frame.InverseTransformPoint(eyes);
            var points = shot.Corners.Select(point => frame.InverseTransformPoint(point) - eye).ToList();
            var across = points.Max(v => Mathf.Atan2(v.x, v.z)) - points.Min(v => Mathf.Atan2(v.x, v.z));
            var tall = points.Max(v => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude)) - points.Min(v => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude));
            return Degrees(across * Mathf.Rad2Deg) + " by " + Degrees(tall * Mathf.Rad2Deg) + " degrees";
        }

        /// <summary>
        /// <see cref="GlazeChecks.TextAsSeen"/> for a drawing that is not the laid-out plane: every label's em measured
        /// the same way, at its text's middle, between where the eyes see its top and its bottom, against the same
        /// 14 dp floor.
        /// </summary>
        private static IEnumerable<string> TextAsSeenWhereDrawn(GameObject root, Vector3 eyes, string what, Func<Vector3, Vector3> seen)
        {
            var labels = SeenText(root, eyes, seen);
            if (labels.Count > 0) Debug.Log("Halcyonic: " + what + ": as the eyes see it where drawn, the smallest text is " + labels[0].Name + " at " + labels[0].Degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees.");
            foreach (var (name, degrees) in labels)
            {
                if (degrees >= Glaze.MinimumTextDegrees - 0.0005f) break;
                yield return what + ": " + name + " is " + degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees as the eyes see it where drawn, under 14 dp, " + Glaze.MinimumTextDegrees.ToString("0.000", CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>Every label of words under <paramref name="root"/>, smallest first, its em as <see cref="GlazeChecks.TextSeen"/> takes it, at the points the eyes see.</summary>
        private static List<(string Name, float Degrees)> SeenText(GameObject root, Vector3 eyes, Func<Vector3, Vector3> seen)
        {
            var labels = new List<(string Name, float Degrees)>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label) || label.textInfo == null || label.textInfo.characterCount == 0) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var middle = label.transform.TransformPoint(label.textBounds.center);
                var half = label.transform.up * (em / 2f);
                labels.Add((label.name, Vector3.Angle(seen(middle + half) - eyes, seen(middle - half) - eyes)));
            }
            labels.Sort((a, b) => a.Degrees.CompareTo(b.Degrees));
            return labels;
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

        /// <summary>The head's yaw and elevation, level, that look straight at <paramref name="point"/>: a glance at one region.</summary>
        private static (float Yaw, float Elevation) TurnTo(Vector3 point, Vector3 eyes)
        {
            var toward = point - eyes;
            return (Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// The eye buffer holds a bare point of a face as the headset's eight-bit sRGB buffers would: within three
        /// steps of its colour. A linear buffer, or a conversion on reading back, would show here.
        /// </summary>
        private static IEnumerable<string> StoredAsTheHeadsetStores(Surface face, Vector3 local, GlazeColor colour, Camera camera, Color32[] eye, string what, Func<Vector3, Vector3> seen)
        {
            var screen = camera.WorldToScreenPoint(seen(face.transform.TransformPoint(local)));
            var x = Mathf.RoundToInt(screen.x);
            var y = Mathf.RoundToInt(screen.y);
            if (screen.z <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) { yield return what + ": the colour witness is out of the frame."; yield break; }
            var stored = eye[y * Width + x];
            var expected = KitType.Colour(colour);
            var r = Mathf.RoundToInt(expected.r * 255f);
            var g = Mathf.RoundToInt(expected.g * 255f);
            var b = Mathf.RoundToInt(expected.b * 255f);
            if (Mathf.Abs(stored.r - r) > 3 || Mathf.Abs(stored.g - g) > 3 || Mathf.Abs(stored.b - b) > 3 || stored.a < 254)
                yield return what + ": the eye buffer holds (" + stored.r + ", " + stored.g + ", " + stored.b + ", " + stored.a + ") where the face is ("
                    + r + ", " + g + ", " + b + "): not stored as a headset stores it.";
            else Debug.Log("Halcyonic: spatial kit: " + what + ": the eye buffer holds the face as (" + stored.r + ", " + stored.g + ", " + stored.b + "), drawn ("
                + r + ", " + g + ", " + b + ").");
        }

        /// <summary>
        /// Measures each region as the eyes meet it and writes the numbers beside the images
        /// (<c>state-measures.json</c>): the least label as the eyes see it, a Body em at the region's outer corners, the
        /// least target as the eyes see it beside the size the target check takes at its plane's distance, where the
        /// region stands and how slanted the eyes meet its far edge, and the instrument's spread. Positions are in the
        /// instrument's own frame from the eyes, in metres: what a top-down drawing of the arrangement needs. These are
        /// evidence for comparing arrangements; they pass and fail nothing.
        /// </summary>
        private static void Measure(Shot shot, Vector3 eyes, string name, string folder, string state)
        {
            if (shot.Instrument is not Transform frame || shot.Regions.Count == 0) return;
            var seen = shot.Seen;
            var eyeLocal = frame.InverseTransformPoint(eyes);
            var metres = frame.lossyScale.x;
            Vector3 FromEyes(Vector3 world) => (frame.InverseTransformPoint(world) - eyeLocal) * metres;
            float YawOf(Vector3 world) { var v = FromEyes(world); return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
            float UpOf(Vector3 world) { var v = FromEyes(world); return Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg; }
            float AngleAt(Vector3 a, Vector3 b) => Vector3.Angle(seen(a) - eyes, seen(b) - eyes);
            string F(float value, string format = "0.000") => value.ToString(format, CultureInfo.InvariantCulture);
            string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string Point(Vector3 world) { var v = FromEyes(world); return "[" + F(v.x) + ", " + F(v.z) + "]"; }
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var em = KitType.Units(SpatialKit.Degrees(SpatialKit.BodyDp) * GlazeText.Scale);
            var json = new StringBuilder();
            json.Append("{\"state\": ").Append(Quote(state)).Append(", \"arrangement\": ").Append(Quote(shot.Curved ? "curve" : "plane"))
                .Append(", \"textScale\": ").Append(F(GlazeText.Scale)).Append(", \"regions\": [");
            var leftmost = float.MaxValue;
            var rightmost = float.MinValue;
            var nearest = float.MaxValue;
            var farthest = 0f;
            var first = true;
            foreach (var (regionName, carrier, targets) in shot.Regions)
            {
                var t = carrier.transform;
                var half = carrier.Size / 2f;
                var leastTall = float.MaxValue;
                var leastWide = float.MaxValue;
                foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                {
                    var corner = new Vector3(sx * (half.x - pad), sy * (half.y - pad), 0f);
                    leastTall = Mathf.Min(leastTall, AngleAt(t.TransformPoint(corner + Vector3.up * em / 2f), t.TransformPoint(corner - Vector3.up * em / 2f)));
                    leastWide = Mathf.Min(leastWide, AngleAt(t.TransformPoint(corner + Vector3.right * em / 2f), t.TransformPoint(corner - Vector3.right * em / 2f)));
                    farthest = Mathf.Max(farthest, Vector3.Distance(seen(t.TransformPoint(new Vector3(sx * half.x, sy * half.y, 0f))), eyes));
                }
                var labels = SeenText(carrier.gameObject, eyes, seen);
                var leastTarget = float.MaxValue;
                var leastGate = float.MaxValue;
                var leastTargetName = "";
                var leastTargetMm = float.MaxValue;
                foreach (var press in targets)
                {
                    if (!press.gameObject.activeInHierarchy) continue;
                    var h = press.Size / 2f;
                    var p = press.transform;
                    var wide = AngleAt(p.TransformPoint(new Vector3(-h.x, 0f, 0f)), p.TransformPoint(new Vector3(h.x, 0f, 0f)));
                    var tall = AngleAt(p.TransformPoint(new Vector3(0f, -h.y, 0f)), p.TransformPoint(new Vector3(0f, h.y, 0f)));
                    var distance = GlazeChecks.PlaneDistance(eyes, p);
                    var scale = p.lossyScale;
                    var gate = Mathf.Min(Glaze.DegreesOf(press.Size.y * scale.y, distance), Glaze.DegreesOf(press.Size.x * scale.x, distance));
                    leastGate = Mathf.Min(leastGate, gate);
                    if (Mathf.Min(wide, tall) < leastTarget)
                    {
                        leastTarget = Mathf.Min(wide, tall);
                        leastTargetName = press.name;
                        leastTargetMm = Mathf.Min(press.Size.x * scale.x, press.Size.y * scale.y) * 1000f;
                    }
                }
                var leftLocal = new Vector3(-half.x, 0f, 0f);
                var rightLocal = new Vector3(half.x, 0f, 0f);
                var leftEdge = seen(t.TransformPoint(leftLocal));
                var rightEdge = seen(t.TransformPoint(rightLocal));
                var outerLocal = Mathf.Abs(YawOf(leftEdge)) > Mathf.Abs(YawOf(rightEdge)) ? leftLocal : rightLocal;
                var outer = seen(t.TransformPoint(outerLocal));
                var slant = Vector3.Angle(NormalAt(seen, t, outerLocal), eyes - outer);
                var centre = seen(t.position);
                leftmost = Mathf.Min(leftmost, YawOf(leftEdge));
                rightmost = Mathf.Max(rightmost, YawOf(rightEdge));
                nearest = Mathf.Min(nearest, Vector3.Distance(centre, eyes));
                var path = string.Join(", ", Enumerable.Range(0, 13).Select(step => Point(seen(t.TransformPoint(new Vector3(-half.x + carrier.Size.x * step / 12f, 0f, 0f))))));
                Debug.Log("Halcyonic: spatial kit: measures " + name + " " + regionName + ": centre " + Degrees(YawOf(centre)) + " across at " + F(Vector3.Distance(centre, eyes), "0.000")
                    + " m; far edge " + Degrees(YawOf(outer)) + " across, met " + Degrees(slant) + " off square; least label " + (labels.Count > 0 ? F(labels[0].Degrees) + " (" + labels[0].Name + ")" : "none")
                    + "; Body at its corners " + F(leastTall) + " tall, " + F(leastWide) + " wide; least target " + (leastTarget < float.MaxValue ? F(leastTarget, "0.00") + " seen, " + F(leastGate, "0.00") + " at its plane's distance, " + F(leastTargetMm, "0.0") + " mm (" + leastTargetName + ")" : "none") + ".");
                if (!first) json.Append(", ");
                first = false;
                json.Append("{\"name\": ").Append(Quote(regionName))
                    .Append(", \"centreYaw\": ").Append(F(YawOf(centre))).Append(", \"centreUp\": ").Append(F(UpOf(centre)))
                    .Append(", \"distance\": ").Append(F(Vector3.Distance(centre, eyes)))
                    .Append(", \"left\": ").Append(Point(leftEdge)).Append(", \"right\": ").Append(Point(rightEdge))
                    .Append(", \"centre\": ").Append(Point(centre)).Append(", \"path\": [").Append(path).Append("]")
                    .Append(", \"leftYaw\": ").Append(F(YawOf(leftEdge))).Append(", \"rightYaw\": ").Append(F(YawOf(rightEdge)))
                    .Append(", \"farSlant\": ").Append(F(slant))
                    .Append(", \"leastLabel\": ").Append(labels.Count > 0 ? F(labels[0].Degrees) : "null")
                    .Append(", \"leastLabelName\": ").Append(Quote(labels.Count > 0 ? labels[0].Name : ""))
                    .Append(", \"bodyCornerTall\": ").Append(F(leastTall)).Append(", \"bodyCornerWide\": ").Append(F(leastWide))
                    .Append(", \"leastTarget\": ").Append(leastTarget < float.MaxValue ? F(leastTarget) : "null")
                    .Append(", \"leastTargetAtPlaneDistance\": ").Append(leastGate < float.MaxValue ? F(leastGate) : "null")
                    .Append(", \"leastTargetMm\": ").Append(leastTargetMm < float.MaxValue ? F(leastTargetMm, "0.0") : "null")
                    .Append(", \"leastTargetName\": ").Append(Quote(leastTargetName)).Append("}");
            }
            var forward = shot.Regions.Select(region => region.Carrier).OrderByDescending(carrier => carrier.Size.x * carrier.transform.lossyScale.x).First();
            var top = UpOf(seen(forward.transform.TransformPoint(new Vector3(0f, forward.Size.y / 2f, 0f))));
            var bottom = UpOf(seen(forward.transform.TransformPoint(new Vector3(0f, -forward.Size.y / 2f, 0f))));
            json.Append("], \"spread\": {\"left\": ").Append(F(leftmost)).Append(", \"right\": ").Append(F(rightmost))
                .Append(", \"across\": ").Append(F(rightmost - leftmost)).Append(", \"tall\": ").Append(F(top - bottom))
                .Append(", \"nearest\": ").Append(F(nearest)).Append(", \"farthest\": ").Append(F(farthest))
                .Append(", \"readingPitch\": ").Append(F(shot.PitchDown))
                .Append(", \"pitchForCorners\": ").Append(F(ReadingPitchOf(shot.Corners, eyes, shot.Centre)))
                .Append(", \"centreElevation\": ").Append(F(shot.Elevation)).Append("}}");
            File.WriteAllText(Path.Combine(folder, state + "-measures.json"), json.ToString());
            Debug.Log("Halcyonic: spatial kit: measures " + name + ": spread " + Degrees(rightmost - leftmost) + " by " + Degrees(top - bottom) + " degrees, "
                + Degrees(leftmost) + " to " + Degrees(rightmost) + " across; regions " + F(nearest, "0.000") + " to " + F(farthest, "0.000") + " m from the eyes.");
        }

        /// <summary>Every face that carries words is opaque in the eye buffer: no room shows through it.</summary>
        private static IEnumerable<string> Opaque(IEnumerable<Surface> faces, Camera camera, Color32[] eye, string what, Func<Vector3, Vector3> seen)
        {
            foreach (var face in faces)
            {
                if (!face.gameObject.activeInHierarchy) continue;
                var least = 255;
                for (var i = 1; i < 12; i++)
                for (var j = 1; j < 12; j++)
                {
                    var local = new Vector3(-0.46f + 0.92f * i / 12f, -0.46f + 0.92f * j / 12f, 0f);
                    var screen = camera.WorldToScreenPoint(seen(face.transform.TransformPoint(local)));
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
        /// most common colour inside the word's own box, which its letters leave mostly bare. A word whose box leaves
        /// the frame cannot be measured there: in a <paramref name="glance"/> it is left to the main view, which must
        /// hold every word whole and fails any that leaves it.
        /// </summary>
        private static IEnumerable<string> WordsHoldContrast(GameObject root, Camera camera, Color32[] pixels, string what, Func<Vector3, Vector3> seen, bool glance = false)
        {
            var weakest = float.MaxValue;
            var weakestName = "";
            var outOfFrame = 0;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label) || label.textInfo == null || label.textInfo.characterCount == 0) continue;
                var bounds = label.textBounds;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var x in new[] { bounds.min.x, bounds.max.x })
                foreach (var y in new[] { bounds.min.y, bounds.max.y })
                {
                    var screen = camera.WorldToScreenPoint(seen(label.transform.TransformPoint(new Vector3(x, y, 0f))));
                    min = Vector2.Min(min, screen);
                    max = Vector2.Max(max, screen);
                }
                if (min.x < 0f || min.y < 0f || max.x > Width || max.y > Height)
                {
                    if (glance) { outOfFrame++; continue; }
                    yield return what + ": " + label.name + " \"" + Clip(label.text) + "\" leaves the frame.";
                    continue;
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
            if (weakest < float.MaxValue) Debug.Log("Halcyonic: spatial kit: " + what + ": the weakest words are " + weakestName + " at " + weakest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1"
                + (outOfFrame > 0 ? "; " + outOfFrame + " labels past this view's edge, held in the main view." : "."));
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
            GlazeText.SetLiteral(label, "Editor render of the D1 spatial kit, not a headset capture · " + words
                + " · the frame is a Quest 3S's whole 96 by 90 degree field, everything at true size");
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
