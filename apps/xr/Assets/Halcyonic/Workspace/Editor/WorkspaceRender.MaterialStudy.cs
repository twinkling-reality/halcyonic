#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        public static void InstrumentMaterialCheck()
        {
            var originalPalette = Glaze.Palette; var originalField = ViewField.Current;
            var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var found = new List<string>();
                    foreach (var palette in new[] { GlazePalette.Default, GlazePalette.InstrumentStudy })
                    foreach (var bright in new[] { false, true })
                    {
                        Glaze.SetPalette(palette);
                        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "InstrumentMaterialRenders", variant, palette.Key, bright ? "bright" : "dark"));
                        Directory.CreateDirectory(folder); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        ViewField.Current = FieldChecks.Quest3S;
                        found.AddRange(RenderInstrumentMotion(folder, bright));
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("material fixture threw " + error.GetBaseException().Message); }
            finally { Glaze.SetPalette(originalPalette); ViewField.Current = originalField; KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: instrument material: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: instrument material: every source geometry, actual scene and motion control passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderInstrumentMotion(string folder, bool bright)
        {
            var failures = new List<string>(); var root = new GameObject("Actual instrument motion study");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var work = Work.Running(); var journal = new JournalInfo { JournalId = "render-material", Origin = JournalOrigin.Live };
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(), Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution }, Runtimes = work.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time }); inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, texture);
                camera.backgroundColor = bright ? Color.white : new Color(0.04f, 0.04f, 0.04f);
                var commands = new CommandFactory(client);
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, InitialPlacementReady = () => true, Presentation = new WorkspacePresentationState(), Commands = commands,
                    State = () => session.State, Session = () => session, Connected = () => true,
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    File = (host, _) => new FileColumn(host, () => work.Present(live: true), commands, () => null, _ => null, () => ""),
                    Submit = (_, _) => throw new InvalidOperationException("Material study cannot submit work."),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                director.OpenFile(work.Workstream.WorkstreamId);
                for (var tick = 0; tick < 4; tick++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); }
                ForceMeshes(root);
                var physical = root.GetComponentsInChildren<Surface>(false).Where(surface => surface.MaterialDepth > 0f).ToArray();
                if (physical.Length < 2) failures.Add("production heading and body did not consume physical material");
                foreach (var surface in physical)
                {
                    var rim = surface.MaterialRim;
                    if (rim == null) { failures.Add("physical surface has no actual rim renderer"); continue; }
                    var mesh = rim.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == null) { failures.Add("material rim has no actual mesh"); continue; }
                    if (mesh.bounds.size.z < surface.MaterialDepth * 0.99f) failures.Add("material thickness is flat or absent");
                    foreach (var vertex in mesh.vertices)
                        if (Mathf.Abs(vertex.x) > 0.50001f || Mathf.Abs(vertex.y) > 0.50001f || vertex.z < -0.00001f || vertex.z > surface.MaterialDepth + 0.00001f)
                            failures.Add("material mesh escaped allocated face footprint/depth");
                    if (surface.Fill.a < 0.98f) failures.Add("reading material lost its controlled opacity");
                }
                var shown = director.Plane.Shown.Single().View;
                failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, "instrument centre"));
                failures.AddRange(GlazeChecks.TargetsLargeEnough(shown.Targets.Concat(director.Plane.Chrome.Targets), eyes, "instrument centre"));
                var positions = new[] { new Vector3(-0.09f, 0.02f, 0f), Vector3.zero, new Vector3(0.09f, -0.02f, 0f) };
                // MenuPlane is a coordinator root; its children are independently positioned.
                // Use the actual whole frame for field direction and the actual reading part for aim.
                var center = director.Plane.Chrome.FrameSurface.transform.position;
                var readingCenter = shown.Parts[0].position;
                if ((readingCenter - eyes).sqrMagnitude < 0.01f || Vector3.Dot(readingCenter - eyes, Vector3.forward) <= 0f)
                    failures.Add("actual reading part has no valid forward camera target");
                var rigid = physical.Select(surface => surface.transform.localToWorldMatrix).ToArray();
                Color32[]? lastMaterialPixels = null;
                for (var frame = 0; frame < positions.Length; frame++)
                {
                    camera.transform.position = eyes + positions[frame]; camera.transform.rotation = Quaternion.LookRotation(readingCenter - camera.transform.position, Vector3.up);
                    ForceMeshes(root);
                    foreach (var surface in physical)
                    {
                        var points = surface.MaterialRim!.GetComponent<MeshFilter>().sharedMesh.vertices.Select(vertex => surface.MaterialRim.transform.TransformPoint(vertex));
                        if (ViewField.Current is ViewField field)
                            failures.AddRange(GlazeChecks.InsideField("actual material rim " + frame, points, camera.transform.position, center, WorkspacePlacement.ReadingPitch(director.Plane.Composition!.Size, director.Plane.Direction.Elevation, field), field));
                    }
                    foreach (var support in root.GetComponentsInChildren<Surface>(false).Where(surface => surface.name == "Semantic text contrast support"))
                    {
                        var corners = new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }.Select(y => support.transform.TransformPoint(new Vector3(x, y, 0f))));
                        if (ViewField.Current is ViewField supportField)
                            failures.AddRange(GlazeChecks.InsideField("semantic text support " + frame, corners, camera.transform.position, center, WorkspacePlacement.ReadingPitch(director.Plane.Composition!.Size, director.Plane.Direction.Elevation, supportField), supportField));
                    }
                    failures.AddRange(GlazeChecks.TextAsSeen(root, camera.transform.position, "instrument motion " + frame));
                    var projected = camera.WorldToViewportPoint(readingCenter);
                    if (projected.z <= 0f || projected.x <= 0f || projected.x >= 1f || projected.y <= 0f || projected.y >= 1f)
                        failures.Add("actual reading centre is outside the positive camera image");
                    var image = Render(camera, texture);
                    var centrePixel = image.GetPixel(Mathf.Clamp(Mathf.RoundToInt(projected.x * (image.width - 1)), 0, image.width - 1),
                        Mathf.Clamp(Mathf.RoundToInt(projected.y * (image.height - 1)), 0, image.height - 1));
                    var background = image.GetPixel(0, 0); // Same rendered color pipeline, known empty corner.
                    if (Mathf.Abs(centrePixel.r - background.r) + Mathf.Abs(centrePixel.g - background.g) + Mathf.Abs(centrePixel.b - background.b) < 0.05f)
                        failures.Add("camera aimed at the reading part but captured only its background");
                    var critical = director.Plane.Chrome.GetComponentsInChildren<TMP_Text>(false)
                        .Concat(shown.GetComponentsInChildren<TMP_Text>(false).Where(word => word.name == "Identity kind" || word.name.StartsWith("Section ") && word.name.EndsWith(" words")))
                        .Where(word => !string.IsNullOrWhiteSpace(word.text)).Distinct().ToArray();
                    var renderers = critical.Select(word => word.GetComponent<MeshRenderer>()).Where(renderer => renderer != null && renderer.enabled).ToArray();
                    Texture2D withoutWords;
                    try { foreach (var renderer in renderers) renderer.enabled = false; withoutWords = Render(camera, texture); }
                    finally { foreach (var renderer in renderers) renderer.enabled = true; }
                    foreach (var word in critical)
                    {
                        var bounds = word.textBounds;
                        var corners = new[] { bounds.min.x, bounds.max.x }.SelectMany(x => new[] { bounds.min.y, bounds.max.y }.Select(y => camera.WorldToScreenPoint(word.transform.TransformPoint(new Vector3(x, y, 0f))))).ToArray();
                        var left = Mathf.Clamp(Mathf.FloorToInt(corners.Min(point => point.x)), 0, image.width - 1);
                        var right = Mathf.Clamp(Mathf.CeilToInt(corners.Max(point => point.x)), 0, image.width - 1);
                        var bottom = Mathf.Clamp(Mathf.FloorToInt(corners.Min(point => point.y)), 0, image.height - 1);
                        var top = Mathf.Clamp(Mathf.CeilToInt(corners.Max(point => point.y)), 0, image.height - 1);
                        var authored = (Color32)word.color;
                        var authoredColor = GlazeColor.Hex((uint)(authored.r << 16 | authored.g << 8 | authored.b));
                        double leastBacking = double.PositiveInfinity;
                        double bestInk = 1; var ink = 0;
                        for (var x = left; x <= right; x++) for (var y = bottom; y <= top; y++)
                        {
                            // Sample the projected text quadrilateral, not the empty wedges of
                            // its axis-aligned rectangle when the real camera leans.
                            var polygon = new[] { corners[0], corners[2], corners[3], corners[1] };
                            var signs = polygon.Select((point, index) => {
                                var next = polygon[(index + 1) % polygon.Length];
                                return (next.x - point.x) * (y + 0.5f - point.y) - (next.y - point.y) * (x + 0.5f - point.x);
                            }).ToArray();
                            if (signs.Any(sign => sign < 0f) && signs.Any(sign => sign > 0f)) continue;
                            var foreground = image.GetPixel(x, y); var backing = withoutWords.GetPixel(x, y);
                            var supportPixel = (Color32)backing;
                            var supportColor = GlazeColor.Hex((uint)(supportPixel.r << 16 | supportPixel.g << 8 | supportPixel.b));
                            leastBacking = Math.Min(leastBacking, GlazeColor.Contrast(authoredColor, supportColor));
                            if (Mathf.Abs(foreground.r - backing.r) + Mathf.Abs(foreground.g - backing.g) + Mathf.Abs(foreground.b - backing.b) < 0.08f) continue;
                            ink++;
                            var a = GlazeColor.Hex((uint)(Mathf.RoundToInt(foreground.r * 255) << 16 | Mathf.RoundToInt(foreground.g * 255) << 8 | Mathf.RoundToInt(foreground.b * 255)));
                            var b = GlazeColor.Hex((uint)(Mathf.RoundToInt(backing.r * 255) << 16 | Mathf.RoundToInt(backing.g * 255) << 8 | Mathf.RoundToInt(backing.b * 255)));
                            bestInk = Math.Max(bestInk, GlazeColor.Contrast(a, b));
                        }
                        if (authored.a != 255 || leastBacking < 4.5)
                            failures.Add("actual image backing does not support the whole text bounds at4.5:1 for " + word.name + ": minimum=" + leastBacking.ToString("0.00") + ", source alpha=" + authored.a);
                        if (ink < 2 || bestInk < 4.5) failures.Add("actual image lacks4.5:1 resolved ink against its rendered backing for " + word.name + ": pixels=" + ink + ", best=" + bestInk.ToString("0.00"));
                    }
                    UnityEngine.Object.DestroyImmediate(withoutWords);
                    lastMaterialPixels = image.GetPixels32(); File.WriteAllBytes(Path.Combine(folder, "motion-" + frame + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                    for (var index = 0; index < physical.Length; index++) if (physical[index].transform.localToWorldMatrix != rigid[index]) failures.Add("camera motion moved the material instead of revealing its real depth");
                }
                // Same real production frame, camera, palette and text; only the physical edge is removed.
                foreach (var surface in physical) surface.FlattenMaterialForRender();
                if (physical.Any(surface => surface.MaterialRim != null || surface.MaterialDepth != 0f)) failures.Add("flat negative control retained physical material");
                var flat = Render(camera, texture); var flatPixels = flat.GetPixels32();
                var changed = flatPixels.Where((pixel, index) => lastMaterialPixels != null &&
                    Math.Abs(pixel.r - lastMaterialPixels[index].r) + Math.Abs(pixel.g - lastMaterialPixels[index].g) + Math.Abs(pixel.b - lastMaterialPixels[index].b) > 12).Count();
                if (changed < 32) failures.Add("actual material produced no distinguishable pixels against its flat negative control");
                File.WriteAllBytes(Path.Combine(folder, "flat-control.png"), flat.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(flat);
                if (GlazeColor.Contrast(Glaze.TextSecondary, Glaze.Panel) < 4.5) failures.Add("candidate secondary text lacks opaque source contrast");
                // Linear compositor worst-case: the .98 substrate over ideal white. Do not use encoded byte interpolation.
                var composite = Glaze.Panel.Luminance * 0.98 + 0.02 + 0.018;
                if ((Glaze.TextSecondary.Luminance + 0.05) / (composite + 0.05) < 4.5) failures.Add("candidate secondary text lacks bright-background linear-composite contrast");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            return failures;
        }
    }
}
