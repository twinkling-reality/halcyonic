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
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class StageRender
    {
        /// <summary>Current optional one-agent shelf: all identities, animation, contact and exact stillness, at both text sizes.</summary>
        public static void CharacterLifeCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CharacterLifeRenders", variant));
                Directory.CreateDirectory(folder);
                var result = new List<string>();
                var previousField = ViewField.Current;
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    result.AddRange(CharacterProjectionEdges());
                    result.AddRange(CharacterShelfCases(folder, features: true));
                    result.AddRange(CharacterBlinks(folder));
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { ViewField.Current = previousField; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: character life: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: character life: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static MenuPlane CharacterConsole(Transform root, Vector3 eyes)
        {
            var plane = MenuPlane.Create(root);
            var frame = ProjectsScreens.Projects(new ProjectsScreens.State
            {
                Demonstration = true, TextSize = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard,
            });
            plane.Show(new MenuBar(MenuPlace.Projects, "Projects"), frame, null, null, Array.Empty<CharacterTarget>(),
                eyes, Vector3.forward, null, immediately: true);
            for (var frameIndex = 0; frameIndex < 10; frameIndex++) plane.FrameForRender(0.05f);
            WorkspaceRender.ForceMeshes(root.gameObject);
            return plane;
        }

        private static List<string> CharacterShelfCases(string folder, bool features)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var ids = features
                    ? (from BodyShape shape in Enum.GetValues(typeof(BodyShape))
                       from CharacterFeature feature in Enum.GetValues(typeof(CharacterFeature))
                       select Enumerable.Range(0, 10000).Select(n => "feature-" + shape + "-" + feature + "-" + n.ToString(CultureInfo.InvariantCulture))
                         .First(id => CharacterIdentity.Of(id).Shape == shape && CharacterIdentity.Of(id).Feature == feature)).ToArray()
                    : new[] { "shelf-state-witness" };
                foreach (var id in ids)
                {
                    var identity = CharacterIdentity.Of(id);
                    var name = features ? identity.Shape + "-" + identity.Feature : "all-states";
                    var root = new GameObject("Current character shelf " + name);
                    var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    try
                    {
                        var eyes = new Vector3(0f, EyeHeight, 0f);
                        var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                        var plane = CharacterConsole(root.transform, eyes);
                        if (root.GetComponentsInChildren<CharacterTarget>(false).Length != 0)
                            failures.Add(name + ": character-free console created a character target.");
                        var original = plane.Direction;
                        var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), failures, name);
                        var view = character.View;
                        if (root.GetComponentsInChildren<CharacterTarget>(false).Length != 1)
                            failures.Add(name + ": confirmed shelf does not contain exactly one supported target.");
                        var characters = new List<(CharacterView View, CharacterTarget Target)> { character };
                        failures.AddRange(CharacterEnvelopes(name, eyes, characters));
                        view.Show(Shown(id, CharacterActivity.Idle));
                        view.PlaceShelfLabel(eyes);
                        Advance(view, 1f);
                        WorkspaceRender.ForceMeshes(root);
                        var image = WorkspaceRender.Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        failures.AddRange(CharacterFeatureShows(name, camera, texture, view));
                        failures.AddRange(KeptStill(name, folder, camera, texture, characters));
                        if (!features)
                        {
                            foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                            {
                                view.Show(Shown(id, activity));
                                view.PlaceShelfLabel(eyes);
                                failures.AddRange(CharacterStateStill(name + "-" + activity, folder, camera, texture, view));
                            }
                        }
                        if (plane.Direction.Yaw != original.Yaw || plane.Direction.Elevation != original.Elevation)
                            failures.Add(name + ": the optional shelf moved the independent console.");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                        texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
            finally { GlazeMotion.Still = wasStill; }
            return failures;
        }

        private static IEnumerable<string> CharacterStateStill(string name, string folder, Camera camera, RenderTexture texture, CharacterView view)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            Texture2D? first = null;
            Texture2D? later = null;
            try
            {
                GlazeMotion.Still = true;
                Advance(view, 3f);
                var before = CharacterFrameValues(new[] { view });
                first = WorkspaceRender.Render(camera, texture);
                Advance(view, 1f);
                later = WorkspaceRender.Render(camera, texture);
                SaveCharacterFrameDifferences(folder, name, before, CharacterFrameValues(new[] { view }));
                File.WriteAllBytes(Path.Combine(folder, name + "-still-before.png"), first.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-still-after.png"), later.EncodeToPNG());
                var difference = WorkspaceRender.Compare(first, later, new RectInt(0, 0, Size, Size));
                if (difference.Changed != 0) failures.Add(name + ": kept-still state changes " + difference.Changed + " pixels by up to "
                    + (difference.Largest * 255f).ToString("0", CultureInfo.InvariantCulture) + " of 255.");
            }
            finally
            {
                GlazeMotion.Still = wasStill;
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (later != null) UnityEngine.Object.DestroyImmediate(later);
            }
            return failures;
        }

        private static IEnumerable<string> CharacterFeatureShows(string name, Camera camera, RenderTexture texture, CharacterView view)
        {
            var failures = new List<string>();
            var identity = CharacterIdentity.Of(view.WorkstreamId);
            var (feature, color) = view.FeatureForRender;
            if ((feature != null) != (identity.Feature != CharacterFeature.None))
                failures.Add(name + ": feature presence differs from the declared identity.");
            if (feature == null) return failures;
            Color.RGBToHSV(color, out var hue, out var saturation, out _);
            foreach (var stateHue in CharacterIdentity.StateHues)
                if (saturation > 0.1f && Mathf.Abs(Mathf.DeltaAngle(hue * 360f, (float)stateHue)) < 25f)
                    failures.Add(name + ": feature hue resembles a state cue.");
            if (Mathf.Abs(Mathf.DeltaAngle(hue * 360f, (float)identity.Hue)) > 20f)
                failures.Add(name + ": feature does not retain the body's hue.");
            var with = WorkspaceRender.Render(camera, texture);
            Texture2D? without = null;
            try
            {
                feature.enabled = false;
                without = WorkspaceRender.Render(camera, texture);
                if (WorkspaceRender.Compare(with, without, new RectInt(0, 0, Size, Size)).Changed == 0)
                    failures.Add(name + ": disabling only the feature changes no rendered pixels.");
            }
            finally
            {
                feature.enabled = true;
                UnityEngine.Object.DestroyImmediate(with);
                if (without != null) UnityEngine.Object.DestroyImmediate(without);
            }
            return failures;
        }

        /// <summary>Exact production values, retained alongside both images before any stillness repair.</summary>
        private static SortedDictionary<string, string> CharacterFrameValues(IEnumerable<CharacterView> views)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var block = new MaterialPropertyBlock();
            foreach (var view in views)
            {
                var prefix = view.WorkstreamId + "/";
                values[prefix + "clock"] = view.ClockForRender.ToString("R", CultureInfo.InvariantCulture);
                foreach (var part in view.GetComponentsInChildren<Transform>(true))
                {
                    var key = prefix + part.name + "#" + part.GetInstanceID().ToString(CultureInfo.InvariantCulture) + "/";
                    values[key + "position"] = part.localPosition.ToString("R");
                    values[key + "rotation"] = part.localRotation.ToString("R");
                    values[key + "scale"] = part.localScale.ToString("R");
                    if (!part.TryGetComponent<Renderer>(out var renderer)) continue;
                    values[key + "enabled"] = renderer.enabled.ToString();
                    renderer.GetPropertyBlock(block);
                    foreach (var property in new[] { "_Clock", "_Flow", "_FlowSpeed", "_Crack", "_Fog", "_Ghost", "_Saturation", "_EyeKind", "_EyeOpen", "_EyeUneven", "_EyeInk" })
                        values[key + property] = block.GetFloat(property).ToString("R", CultureInfo.InvariantCulture);
                    foreach (var property in new[] { "_BodyColor", "_Look", "_EyeLayout", "_Color" })
                        values[key + property] = block.GetVector(property).ToString("R");
                }
            }
            return values;
        }

        private static void SaveCharacterFrameDifferences(string folder, string name, SortedDictionary<string, string> before, SortedDictionary<string, string> after)
        {
            var differences = before.Where(pair => !after.TryGetValue(pair.Key, out var value) || value != pair.Value)
                .Select(pair => pair.Key + ": " + pair.Value + " -> " + (after.TryGetValue(pair.Key, out var value) ? value : "missing")).ToArray();
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-values-before.txt"), before.Select(pair => pair.Key + ": " + pair.Value));
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-values-after.txt"), after.Select(pair => pair.Key + ": " + pair.Value));
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-differences.txt"), differences);
            Debug.Log("Halcyonic: " + name + ": kept-still value differences: " + string.Join("; ", differences));
        }

        private static IEnumerable<string> CharacterProjectionEdges()
        {
            var root = new GameObject("Character projection edge witness");
            var mesh = new Mesh();
            try
            {
                // Endpoints reach only 35.264 degrees; the edge's middle reaches 45 degrees.
                // This fails a vertices-only implementation as well as the old box-corner witness.
                mesh.vertices = new[] { new Vector3(-1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 1f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                var extent = GlazeChecks.MeshOutline("edge witness", Vector3.zero, new[] { renderer });
                if (Mathf.Abs(extent.Top - 45f) > 1e-4f || Mathf.Abs(extent.Bottom) > 1e-4f)
                    return new[] { "Character triangle projection misses a vertical edge extremum." };
                // Rotation makes box corners phantom points beyond the actual triangle.
                root.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                extent = GlazeChecks.MeshOutline("rotated edge witness", Vector3.zero, new[] { renderer });
                var box = GlazeChecks.Of("rotated box", Vector3.zero, new[] { renderer });
                if (box.Right - extent.Right < 10f)
                    return new[] { "Character triangle projection includes a phantom bounding-box corner." };
                return Array.Empty<string>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// Regression samples of actual shell/feature vertices, every shape and feature, in moving
        /// and held state poses. These samples supplement the conservative scale bound; they do not find all extrema.
        /// </summary>
        private static IEnumerable<string> CharacterEnvelopes(string name, Vector3 eyes, List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            var block = new MaterialPropertyBlock();
            try
            {
                foreach (var still in new[] { false, true })
                {
                    GlazeMotion.Still = still;
                    foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                    {
                        foreach (var (view, _) in characters)
                        {
                            view.Show(new CharacterPresentation(view.WorkstreamId, "A task", activity, CharacterPresenter.LabelOf(activity), AttentionLevel.None,
                                Array.Empty<string>(), 0, false, false, false));
                        }
                        foreach (var seconds in new[] { 0.017f, 0.13f, 0.5f, 1.5f })
                        {
                            foreach (var (view, _) in characters)
                            {
                                view.AdvanceForRender(seconds);
                                view.PlaceShelfLabel(eyes);
                                WorkspaceRender.ForceMeshes(view.gameObject);
                                var pose = new Pose(view.transform.parent.position, view.transform.parent.rotation);
                                var corners = new Vector3[8];
                                CharacterStage.WriteShelfCorners(pose, AgentShelfDraft.Width(1), eyes, corners);
                                var envelope = new Bounds(Quaternion.Inverse(pose.rotation) * (corners[0] - pose.position), Vector3.zero);
                                foreach (var corner in corners) envelope.Encapsulate(Quaternion.Inverse(pose.rotation) * (corner - pose.position));
                                foreach (var filter in view.GetComponentsInChildren<MeshFilter>(false))
                                    foreach (var vertex in filter.sharedMesh.vertices)
                                        if (!envelope.Contains(Quaternion.Inverse(pose.rotation) * (filter.transform.TransformPoint(vertex) - pose.position)))
                                        { failures.Add(name + ": actual " + filter.name + " escapes the confirmed shelf envelope in " + activity + "."); break; }
                                var shellAt = view.Body.Find("Shell");
                                var bottom = shellAt.GetComponent<MeshFilter>().sharedMesh.vertices.Min(vertex => shellAt.TransformPoint(vertex).y);
                                if (!view.Grounded || Mathf.Abs(bottom - pose.position.y) > 0.0001f)
                                    failures.Add(name + ": actual shell loses grounded contact in " + activity + ".");
                                failures.AddRange(GlazeChecks.TextAsSeen(view.Label.gameObject, eyes, name + " " + activity));
                                var what = name + " " + activity + (still ? " kept still" : " moving");
                                var shell = view.Body.Find("Shell");
                                foreach (var mesh in shell.GetComponentsInChildren<MeshFilter>())
                                {
                                    foreach (var vertex in mesh.sharedMesh.vertices)
                                    {
                                        var local = view.Body.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                                        if (local.magnitude > CharacterView.BodyExtent + 1e-5f)
                                        {
                                            failures.Add(what + ": " + mesh.name + " escapes the visual/hand-target envelope.");
                                            break;
                                        }
                                    }
                                }
                                var body = WorkspaceRender.BodyExtent(view, eyes);
                                var label = GlazeChecks.Of(view.WorkstreamId + " label", eyes, view.Label.gameObject);
                                if (label.Apart(body) < OwnLabelGapDegrees) failures.Add(what + ": shell/feature touches its label.");
                                if (view.FeatureForRender.Renderer is MeshRenderer feature)
                                {
                                    feature.GetPropertyBlock(block);
                                    if (block.GetFloat("_EyeInk") != 0f) failures.Add(what + ": a feature inherited eyes.");
                                    foreach (var key in new[] { "_Flow", "_Crack", "_Fog", "_Ghost", "_Saturation" })
                                    {
                                        var own = block.GetFloat(key);
                                        shell.GetComponent<Renderer>().GetPropertyBlock(block);
                                        if (Mathf.Abs(own - block.GetFloat(key)) > 1e-6f) failures.Add(what + ": feature's " + key + " differs from its task's surface state.");
                                        feature.GetPropertyBlock(block);
                                    }
                                    var mesh = feature.GetComponent<MeshFilter>().sharedMesh;
                                    if (mesh.vertexCount > 300 || mesh.GetIndexCount(0) / 3 > 588)
                                        failures.Add(what + ": feature exceeds the declared geometry budget.");
                                    if (feature.sharedMaterials.Length != 1 || feature.sharedMaterial.passCount != 1)
                                        failures.Add(what + ": feature uses more than one material pass.");
                                }
                            }
                            for (var first = 0; first < characters.Count; first++)
                            {
                                for (var other = first + 1; other < characters.Count; other++)
                                {
                                    var a = characters[first].Target;
                                    var b = characters[other].Target;
                                    if (Vector3.Distance(a.BodyPosition, b.BodyPosition) <= CharacterView.BodyRadius * (a.Scale + b.Scale))
                                        failures.Add(name + ": neighboring hand/gaze target spheres overlap in " + activity + ".");
                                }
                            }
                        }
                    }
                }
            }
            finally { GlazeMotion.Still = wasStill; }
            return failures;
        }

        /// <summary>
        /// Two fixed identities run through the actual animation clock and frame path. Their own intervals,
        /// the blink's end, and the absence of blinks when still, closed or not live are checked in pose and pixels.
        /// </summary>
        private static IEnumerable<string> CharacterBlinks(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Character blink witnesses");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var wasStill = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var ids = new[] { "01a0dcf1-5a80-7000-8000-000000000001", "01a0dcf1-5a80-7000-8000-000000000002" };
                var plane = CharacterConsole(root.transform, eyes);
                var views = new List<CharacterView>();
                foreach (var id in ids)
                {
                    var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), failures, "blink " + id);
                    character.View.transform.parent.gameObject.SetActive(false);
                    views.Add(character.View);
                }
                if (Math.Abs(CharacterIdentity.Of(ids[0]).BlinkEverySeconds - CharacterIdentity.Of(ids[1]).BlinkEverySeconds) < 1)
                    failures.Add("blink witnesses do not have distinct intervals.");
                foreach (var view in views)
                {
                    view.transform.parent.gameObject.SetActive(true);
                    var period = (float)CharacterIdentity.Of(view.WorkstreamId).BlinkEverySeconds;
                    // Begin safely after one blink. Then advance to the next, using the identity's
                    // declared period rather than setting private animation state for the witness.
                    var after = (Mathf.Floor(view.ClockForRender / period) + 1) * period + 0.3f;
                    Advance(view, after - view.ClockForRender);
                    var openPose = view.PoseForRender.Open;
                    UnityEngine.Object.DestroyImmediate(Capture(view, "open"));
                    var firstBlink = (Mathf.Floor(view.ClockForRender / period) + 1) * period + Glaze.BlinkSeconds / 2f;
                    Advance(view, firstBlink - view.ClockForRender);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": eyes do not blink at their identity's interval.");
                    var blink = Capture(view, "blink");
                    var shell = view.Body.Find("Shell").GetComponent<Renderer>();
                    var original = new MaterialPropertyBlock();
                    var opened = new MaterialPropertyBlock();
                    shell.GetPropertyBlock(original);
                    shell.GetPropertyBlock(opened);
                    Texture2D? openAtBlink = null;
                    try
                    {
                        // Same production frame, same clock, transforms, surface and gaze. Only the
                        // shell's EyeOpen changes, so breathing or eye wandering cannot satisfy this witness.
                        opened.SetFloat("_EyeOpen", openPose);
                        shell.SetPropertyBlock(opened);
                        openAtBlink = Capture(view, "open-at-blink");
                        if (WorkspaceRender.Compare(blink, openAtBlink, new RectInt(0, 0, Size, Size)).Changed == 0)
                            failures.Add(view.WorkstreamId + ": changing only _EyeOpen at the actual blink changes no rendered eye pixels.");
                    }
                    finally
                    {
                        shell.SetPropertyBlock(original);
                        UnityEngine.Object.DestroyImmediate(blink);
                        if (openAtBlink != null) UnityEngine.Object.DestroyImmediate(openAtBlink);
                    }
                    Advance(view, Glaze.BlinkSeconds / 2f - 0.01f);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": blink ended before the shared duration.");
                    Advance(view, 0.16f);
                    if (view.PoseForRender.Open < 0.85f) failures.Add(view.WorkstreamId + ": blink fails to reopen after the shared duration.");
                    var nextBlink = firstBlink + period;
                    Advance(view, nextBlink - view.ClockForRender);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": next blink fails to repeat at its declared interval.");

                    GlazeMotion.Still = true;
                    Advance(view, period);
                    if (view.PoseForRender.Open < 0.85f) failures.Add(view.WorkstreamId + ": Keep things still does not suppress blinks.");
                    var held = Capture(view, "still");
                    Advance(view, period);
                    var later = Capture(view, "still-later");
                    if (WorkspaceRender.Compare(held, later, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": kept-still character changes pixels across a blink interval.");
                    UnityEngine.Object.DestroyImmediate(held);
                    UnityEngine.Object.DestroyImmediate(later);
                    GlazeMotion.Still = false;

                    // Enter while a different eye-kind transition is still in flight.
                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Failed));
                    Advance(view, 0.04f);
                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Idle, stale: true));
                    var frozenClock = view.ClockForRender;
                    var stale = Capture(view, "stale");
                    var frozenPosition = view.Body.localPosition;
                    var frozenRotation = view.Body.localRotation;
                    var frozenPose = view.PoseForRender;
                    var frozenValues = CharacterFrameValues(new[] { view });
                    Advance(view, 1f);
                    var settled = Capture(view, "stale-one-second");
                    if (WorkspaceRender.Compare(stale, settled, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": last-known appearance continues settling after Show.");
                    UnityEngine.Object.DestroyImmediate(settled);
                    Advance(view, period);
                    if (view.ClockForRender != frozenClock) failures.Add(view.WorkstreamId + ": last-known animation clock advances.");
                    if (view.Body.localPosition != frozenPosition || view.Body.localRotation != frozenRotation || view.PoseForRender != frozenPose)
                        failures.Add(view.WorkstreamId + ": last-known pose continues settling after Show.");
                    var staleLater = Capture(view, "stale-later");
                    if (WorkspaceRender.Compare(stale, staleLater, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": last-known character blinks or otherwise changes rendered state.");
                    UnityEngine.Object.DestroyImmediate(stale);
                    UnityEngine.Object.DestroyImmediate(staleLater);

                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Working));
                    Advance(view, 0.13f);
                    var resumedValues = CharacterFrameValues(new[] { view });
                    if (view.ClockForRender <= frozenClock || !frozenValues.Any(pair => !pair.Key.EndsWith("clock", StringComparison.Ordinal)
                        && !pair.Key.EndsWith("_Clock", StringComparison.Ordinal) && resumedValues[pair.Key] != pair.Value))
                        failures.Add(view.WorkstreamId + ": a live presentation fails to resume animation after last known.");

                    foreach (var activity in new[] { CharacterActivity.TurnFinished, CharacterActivity.Failed, CharacterActivity.Interrupted })
                    {
                        view.Show(Shown(view.WorkstreamId, activity));
                        Advance(view, 1);
                        var kind = view.Body.Find("Shell").GetComponent<Renderer>();
                        var block = new MaterialPropertyBlock();
                        kind.GetPropertyBlock(block);
                        var before = block.GetFloat("_EyeKind");
                        var beforeClock = view.ClockForRender;
                        var beforeValues = CharacterFrameValues(new[] { view });
                        Texture2D? beforePixels = activity == CharacterActivity.Interrupted ? Capture(view, "interrupted") : null;
                        Advance(view, period);
                        kind.GetPropertyBlock(block);
                        if (before == 0 || block.GetFloat("_EyeKind") != before) failures.Add(view.WorkstreamId + ": " + activity + " gains open blinking eyes.");
                        if (beforePixels != null)
                        {
                            var afterPixels = Capture(view, "interrupted-later");
                            try
                            {
                                var afterValues = CharacterFrameValues(new[] { view });
                                if (view.ClockForRender != beforeClock || beforeValues.Any(pair => afterValues[pair.Key] != pair.Value))
                                    failures.Add(view.WorkstreamId + ": Interrupted continues changing production values.");
                                if (WorkspaceRender.Compare(beforePixels, afterPixels, new RectInt(0, 0, Size, Size)).Changed != 0)
                                    failures.Add(view.WorkstreamId + ": Interrupted continues changing rendered pixels.");
                            }
                            finally { UnityEngine.Object.DestroyImmediate(beforePixels); UnityEngine.Object.DestroyImmediate(afterPixels); }
                        }
                    }
                    view.transform.parent.gameObject.SetActive(false);
                }

                Texture2D Capture(CharacterView view, string moment)
                {
                    WorkspaceRender.ForceMeshes(root);
                    var image = CloseUp(camera, texture, view.Body.position, out _);
                    File.WriteAllBytes(Path.Combine(folder, "blink-" + Array.IndexOf(ids, view.WorkstreamId).ToString(CultureInfo.InvariantCulture) + "-" + moment + ".png"), image.EncodeToPNG());
                    return image;
                }
            }
            finally
            {
                GlazeMotion.Still = wasStill;
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        private static void Advance(CharacterView view, float seconds)
        {
            while (seconds > 0.000001f)
            {
                var frame = Mathf.Min(seconds, 1f / 72f);
                view.AdvanceForRender(frame);
                seconds -= frame;
            }
        }

        private static CharacterPresentation Shown(string id, CharacterActivity activity, bool stale = false) =>
            new CharacterPresentation(id, "A task", activity, CharacterPresenter.LabelOf(activity), AttentionLevel.None, Array.Empty<string>(), 0, false, false, stale);
    }
}
