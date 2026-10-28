#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Where a drawn point may stand from where the curve puts it: 0.05 mm, a sixth of a pixel at 0.46 m.</summary>
        private const float CurveTolerance = 0.00005f;

        /// <summary>Glyphs of leaned outside words checked on the curve in this pass: the order witness must meet some.</summary>
        private static int curveLeanedGlyphs;

        /// <summary>
        /// The Spatial journey's three production routes with the plane drawn on the workspace's one gentle curve
        /// (ADR 0034), at both reading sizes. Each shot is drawn on the curve, checked and rendered as
        /// <c>&lt;shot&gt;-curve.png</c>, then drawn flat again, the fallback, and checked and rendered as the Spatial
        /// journey does, so its flat renders can be compared with that journey's.
        /// </summary>
        public static void SpatialCurveCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpatialCurveRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                spatialProductGaps.Clear();
                curveLeanedGlyphs = 0;
                var found = new List<string>();
                try
                {
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Running(), false, curved: true));
                    var multiple = Path.Combine(folder, "multiple-projects"); Directory.CreateDirectory(multiple);
                    found.AddRange(RenderSpatialWorkspace(multiple, Work.Running(), false, multipleProjects: true, curved: true));
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Approval("Run make migrate"), true, curved: true));
                }
                finally { CurvedDrawing.Enabled = false; ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                if (curveLeanedGlyphs == 0) found.Add("curve: no leaned outside words were drawn on the curve, so the lean-then-bend order was never witnessed");
                else Debug.Log("Halcyonic: curve: " + curveLeanedGlyphs + " leaned outside-word glyphs kept their lean on the curve.");
                foreach (var failure in found) Debug.LogError("Halcyonic: spatial curve: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: spatial curve: every check passed; " + folder
                    + (spatialProductGaps.Count == 0 ? "" : "; product gaps, not passes: " + string.Join("; ", spatialProductGaps)));
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>
        /// One shot on the curve: the switch on, the plane drawn and checked where the curve puts it, its counts recorded
        /// and its render saved; then what moved or changed bent again, and the switch off, every mesh flat again.
        /// </summary>
        private static IEnumerable<string> CurveState(string name, string folder, GameObject root, Camera camera, RenderTexture texture, MenuDirector director)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            var curve = plane.Curved;
            // Counted once every label's mesh is current, as the curve's counts are.
            ForceMeshes(root);
            var flat = curve.Counts(plane.CurvedBodies);
            var before = FlatMeshes(plane);
            CurvedDrawing.Enabled = true;
            try
            {
                plane.Advance(0f); ForceMeshes(root); plane.Advance(0f);
                if (!curve.On) { failures.Add(name + ": the switch did not put the plane on the curve"); return failures; }
                failures.AddRange(OnTheCurve(name, plane, record: true));
                var counts = curve.Counts(plane.CurvedBodies);
                Debug.Log("Halcyonic: curve: " + name + ": " + counts.Surfaces + " surfaces with " + counts.SurfaceVertices + " vertices (" + flat.SurfaceVertices
                    + " flat), " + counts.Labels + " labels, " + counts.Glyphs + " glyphs.");
                if (counts.Surfaces != flat.Surfaces || counts.Labels != flat.Labels || counts.Glyphs != flat.Glyphs)
                    failures.Add(name + ": the curve changed what is drawn: " + flat + " flat against " + counts);
                camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + "-curve.png"), image.EncodeToPNG()); Object.DestroyImmediate(image);
                failures.AddRange(RebuildsOnTheCurve(name, plane, root));
            }
            finally { CurvedDrawing.Enabled = false; plane.Advance(0f); }
            ForceMeshes(root);
            var after = curve.Counts(plane.CurvedBodies);
            if (after != flat) failures.Add(name + ": drawn flat again, the plane draws " + after + ", not the " + flat + " it drew before the curve");
            var again = FlatMeshes(plane);
            foreach (var (what, drawn) in before)
                if (!again.TryGetValue(what, out var now) || !now.SequenceEqual(drawn))
                { failures.Add(name + ": drawn flat again, " + what + " is not the mesh it drew before the curve"); break; }
            foreach (var surface in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<Surface>(false)))
                if (surface.OnCurve) { failures.Add(name + ": " + PathOf(surface.transform) + " stayed on the curve after the switch went off"); break; }
            foreach (var label in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<TMP_Text>(false)))
                if (label.GetComponent<GlazeRuns>() is GlazeRuns runs && runs.Bent) { failures.Add(name + ": " + PathOf(label.transform) + " stayed bent after the switch went off"); break; }
            return failures;
        }

        /// <summary>
        /// What the plane draws flat, exactly: each shape's and rim's mesh, by identity, and each label's drawn
        /// vertices in its main mesh, keyed by where each stands.
        /// </summary>
        private static Dictionary<string, object[]> FlatMeshes(MenuPlane plane)
        {
            var drawn = new Dictionary<string, object[]>();
            foreach (var surface in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<Surface>(false)))
            {
                var rim = surface.MaterialRim;
                drawn[PathOf(surface.transform) + " #" + surface.GetInstanceID()] = new object[]
                    { surface.GetComponent<MeshFilter>().sharedMesh, rim != null && rim.gameObject.activeSelf ? rim.GetComponent<MeshFilter>().sharedMesh : (object)"no rim" };
            }
            // A label's mesh keeps room for its longest text so far; only the vertices it draws now count.
            foreach (var label in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<TMP_Text>(false)))
                if (label is TextMeshPro text && text.mesh != null)
                    drawn[PathOf(label.transform) + " #" + label.GetInstanceID()] =
                        text.mesh.vertices.Take(label.textInfo?.meshInfo[0].vertexCount ?? 0).Cast<object>().ToArray();
            return drawn;
        }

        /// <summary>The pages under <paramref name="plane"/>: everything shown but the bodies standing on it.</summary>
        private static IEnumerable<T> Pages<T>(Transform plane, IReadOnlyList<Transform> bodies, IEnumerable<T> all) where T : Component =>
            all.Where(each => !bodies.Any(body => body != null && each.transform.IsChildOf(body)));

        /// <summary>
        /// Everything on the plane stands where the curve puts it: every shape a strip of columns no wider than a
        /// degree, each vertex where the curve puts its laid-out point and square to the eyes across; every rim with
        /// columns along its long edges; every glyph's drawn corners where the curve puts its flat, leaned corners,
        /// and back again by the inverse; leaned outside words still leaned; every label's em, as the eyes see it
        /// where drawn, at least 14 dp and never smaller than flat.
        /// </summary>
        private static List<string> OnTheCurve(string name, MenuPlane plane, bool record)
        {
            var failures = new List<string>();
            var curve = plane.Curved;
            var eyes = curve.Eyes;
            var axis = curve.Facing * Vector3.up;
            // Where the curve puts a laid-out point, from the client core's mapping itself, not the drawing's own.
            Vector3 Expected(Vector3 laid)
            {
                var local = Quaternion.Inverse(curve.Facing) * (laid - eyes);
                var bent = curve.Curve.Bend(new System.Numerics.Vector3(local.x, local.y, local.z));
                return eyes + curve.Facing * new Vector3(bent.X, bent.Y, bent.Z);
            }
            static string Millimetres(float meters) => (meters * 1000f).ToString("0.000", CultureInfo.InvariantCulture);
            foreach (var surface in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<Surface>(false)))
            {
                var t = surface.transform;
                var scale = t.lossyScale;
                if (Mathf.Abs(scale.x) < 1e-6f || Mathf.Abs(scale.y) < 1e-6f) continue;
                if (!surface.OnCurve) { failures.Add(name + ": " + PathOf(t) + " is drawn flat on the curve"); continue; }
                var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                var worst = 0f;
                for (var index = 0; index < vertices.Length; index++)
                {
                    var drawn = t.TransformPoint(vertices[index]);
                    var laid = t.TransformPoint(new Vector3(uvs[index].x - 0.5f, uvs[index].y - 0.5f, 0f));
                    worst = Mathf.Max(worst, Vector3.Distance(drawn, Expected(laid)), Vector3.Distance(curve.Straighten(drawn), laid));
                }
                if (worst > CurveTolerance) failures.Add(name + ": " + PathOf(t) + " stands " + Millimetres(worst) + " mm from where the curve puts it");
                var columns = vertices.Length / 2 - 1;
                var widest = curve.Curve.SpreadDegrees(Mathf.Abs(scale.x)) / columns;
                if (widest > CurvedDrawing.ColumnDegrees + 1e-3f) failures.Add(name + ": " + PathOf(t) + " has columns " + widest.ToString("0.000", CultureInfo.InvariantCulture) + " degrees wide");
                for (var column = 0; column < columns; column++)
                {
                    var bottom = t.TransformPoint(vertices[2 * column]);
                    var along = t.TransformPoint(vertices[2 * column + 2]) - bottom;
                    var up = t.TransformPoint(vertices[2 * column + 1]) - bottom;
                    var normal = Vector3.Cross(up.normalized, along.normalized);
                    var toward = eyes - (bottom + along / 2f);
                    var across = Vector3.Angle(Vector3.ProjectOnPlane(normal, axis), Vector3.ProjectOnPlane(toward, axis));
                    if (across >= 0.5f) { failures.Add(name + ": " + PathOf(t) + " meets the eyes " + across.ToString("0.00", CultureInfo.InvariantCulture) + " degrees off square across"); break; }
                }
                if (surface.MaterialRim is MeshRenderer rim && rim.gameObject.activeInHierarchy)
                {
                    var outline = rim.GetComponent<MeshFilter>().sharedMesh.vertices;
                    var ring = outline.Length / 3;
                    var step = 0f;
                    for (var index = 0; index < ring; index++)
                    {
                        Vector3 Local(int at) => Quaternion.Inverse(curve.Facing) * (rim.transform.TransformPoint(outline[at]) - eyes);
                        var a = Local(index); var b = Local((index + 1) % ring);
                        step = Mathf.Max(step, Mathf.Abs(Mathf.Atan2(a.x, a.z) - Mathf.Atan2(b.x, b.z)) * Mathf.Rad2Deg);
                    }
                    if (step > CurvedDrawing.ColumnDegrees + 1e-3f) failures.Add(name + ": " + PathOf(t) + "'s rim runs " + step.ToString("0.000", CultureInfo.InvariantCulture) + " degrees between points along its edge");
                }
            }
            foreach (var label in Pages(plane.transform, plane.CurvedBodies, plane.transform.GetComponentsInChildren<TMP_Text>(false)))
            {
                if (!label.isActiveAndEnabled || string.IsNullOrEmpty(label.text) || label.textInfo == null || label.textInfo.characterCount == 0) continue;
                var runs = GlazeRuns.Of(label);
                if (!runs.Bent) { failures.Add(name + ": " + PathOf(label.transform) + " is drawn flat on the curve"); continue; }
                var info = label.textInfo;
                var t = label.transform;
                var worst = 0f;
                for (var material = 0; material < info.materialCount; material++)
                for (var index = 0; index < info.meshInfo[material].vertexCount; index++)
                {
                    var drawn = t.TransformPoint(info.meshInfo[material].vertices[index]);
                    var laid = t.TransformPoint(runs.FlatVertex(material, index));
                    worst = Mathf.Max(worst, Vector3.Distance(drawn, Expected(laid)), Vector3.Distance(curve.Straighten(drawn), laid));
                }
                if (label is TextMeshPro text && text.mesh != null && info.meshInfo[0].vertexCount > 0)
                {
                    var uploaded = text.mesh.vertices;
                    for (var index = 0; index < info.meshInfo[0].vertexCount && index < uploaded.Length; index++)
                        worst = Mathf.Max(worst, Vector3.Distance(t.TransformPoint(uploaded[index]), t.TransformPoint(info.meshInfo[0].vertices[index])));
                }
                if (worst > CurveTolerance) failures.Add(name + ": a glyph of " + PathOf(t) + " stands " + Millimetres(worst) + " mm from where the curve puts its laid-out corner");
                // TextMeshPro writes each glyph bottom left, top left, top right, bottom right; a leaned one's top sits right of its bottom.
                if (runs.Theirs.Count > 0 && !runs.IsNamedIdentity && !runs.IsAttributedProse)
                    for (var index = 0; index < info.characterCount; index++)
                    {
                        var glyph = info.characterInfo[index];
                        if (!glyph.isVisible || !GlazeText.InRun(info, index, runs.Theirs)) continue;
                        var corner = glyph.vertexIndex;
                        var lean = runs.FlatVertex(glyph.materialReferenceIndex, corner + 1).x - runs.FlatVertex(glyph.materialReferenceIndex, corner).x;
                        if (!(lean > 1e-6f)) { failures.Add(name + ": an outside-word glyph of " + PathOf(t) + " lost its lean on the curve"); break; }
                        if (record) curveLeanedGlyphs++;
                    }
                if (GlazeIcons.IsIcon(label)) continue;
                var em = label.fontSize * 0.1f * t.lossyScale.y;
                var middle = t.TransformPoint(label.textBounds.center);
                var half = t.up * (em / 2f);
                var seen = Between(Expected(middle + half) - eyes, Expected(middle - half) - eyes);
                var laidOut = Between(middle + half - eyes, middle - half - eyes);
                if (seen < Glaze.MinimumTextDegrees - 0.0005f)
                    failures.Add(name + ": " + PathOf(t) + " reads " + seen.ToString("0.000", CultureInfo.InvariantCulture) + " degrees where drawn on the curve, under 14 dp");
                if (seen < laidOut - 1e-4f)
                    failures.Add(name + ": " + PathOf(t) + " reads smaller on the curve (" + seen.ToString("0.000", CultureInfo.InvariantCulture) + ") than flat (" + laidOut.ToString("0.000", CultureInfo.InvariantCulture) + ")");
            }
            return failures;
        }

        /// <summary>The angle between two directions, in degrees, by atan2 in double precision: acos loses about 0.02 degrees at a degree.</summary>
        private static float Between(Vector3 a, Vector3 b)
        {
            double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return (float)(System.Math.Atan2(System.Math.Sqrt(cx * cx + cy * cy + cz * cz), ax * bx + ay * by + az * bz) * 180.0 / System.Math.PI);
        }

        /// <summary>
        /// What moved or changed is bent again where it now stands, and put back as it was: a part moved across (a
        /// slide), a shape's rectangle grown, the whole plane turned about the eyes with its curve (Move), and a
        /// label's words changed.
        /// </summary>
        private static List<string> RebuildsOnTheCurve(string name, MenuPlane plane, GameObject root)
        {
            var failures = new List<string>();
            var curve = plane.Curved;
            void Check(string what)
            {
                curve.Refresh(plane.CurvedBodies);
                failures.AddRange(OnTheCurve(name + " (" + what + ")", plane, record: false));
            }
            var view = plane.Shown[0].View;
            var part = view.Parts[0];
            // Put back by its local place, exactly: a world place set back is recomputed and can differ in its last bit.
            var was = part.localPosition;
            part.position += part.right * 0.02f;
            Check("a part slid 20 mm across");
            part.localPosition = was;
            Check("the part slid back");
            var surface = view.ReadingSurface;
            var size = surface.transform.localScale;
            surface.transform.localScale = new Vector3(size.x * 1.25f, size.y, size.z);
            Check("a shape grown a quarter wider");
            surface.transform.localScale = size;
            Check("the shape back to its size");
            var (eyes, facing) = (curve.Eyes, curve.Facing);
            var turn = Quaternion.AngleAxis(12f, facing * Vector3.up);
            var (rootAt, rootTurned) = (plane.transform.localPosition, plane.transform.localRotation);
            plane.transform.SetPositionAndRotation(eyes + turn * (plane.transform.position - eyes), turn * plane.transform.rotation);
            curve.Place(eyes, turn * facing);
            Check("the plane moved 12 degrees about the eyes");
            plane.transform.localPosition = rootAt;
            plane.transform.localRotation = rootTurned;
            curve.Place(eyes, facing);
            Check("the plane moved back");
            var label = Pages(plane.transform, plane.CurvedBodies, view.GetComponentsInChildren<TMP_Text>(false))
                .FirstOrDefault(each => !string.IsNullOrEmpty(each.text) && !GlazeIcons.IsIcon(each));
            if (label == null) { failures.Add(name + ": no label to change on the curve"); return failures; }
            var words = label.text;
            label.text = words + " and more";
            label.ForceMeshUpdate();
            Check("a label's words changed");
            label.text = words;
            label.ForceMeshUpdate();
            Check("the label's words back");
            // A line of outside words the person did not write, set the way production sets one, so the lean-then-bend
            // order is always witnessed, whatever the shot shows; made after the render and removed again.
            var witness = GlazeText.Create(part, "Leaned outside words witness", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 0);
            try
            {
                GlazeRuns.Of(witness).Set(LineWords.Ours("Agent says ") + LineWords.Theirs("the rate limit test passes"));
                // Laid in a box half the column wide at the part's centre, as a reading line is, not TextMeshPro's default box.
                GlazeText.Lay(witness, view.Width / 2f, 2);
                witness.ForceMeshUpdate();
                curve.Refresh(plane.CurvedBodies);
                failures.AddRange(OnTheCurve(name + " (leaned outside words)", plane, record: true));
            }
            finally { Object.DestroyImmediate(witness.gameObject); }
            ForceMeshes(root);
            return failures;
        }
    }
}
