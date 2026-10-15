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
                var center = director.Plane.transform.position;
                var rigid = physical.Select(surface => surface.transform.localToWorldMatrix).ToArray();
                Color32[]? lastMaterialPixels = null;
                for (var frame = 0; frame < positions.Length; frame++)
                {
                    camera.transform.position = eyes + positions[frame]; camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
                    ForceMeshes(root);
                    foreach (var surface in physical)
                    {
                        var points = surface.MaterialRim!.GetComponent<MeshFilter>().sharedMesh.vertices.Select(vertex => surface.MaterialRim.transform.TransformPoint(vertex));
                        if (ViewField.Current is ViewField field)
                            failures.AddRange(GlazeChecks.InsideField("actual material rim " + frame, points, camera.transform.position, center, WorkspacePlacement.ReadingPitch(director.Plane.Composition!.Size, director.Plane.Direction.Elevation, field), field));
                    }
                    failures.AddRange(GlazeChecks.TextAsSeen(root, camera.transform.position, "instrument motion " + frame));
                    var image = Render(camera, texture); lastMaterialPixels = image.GetPixels32(); File.WriteAllBytes(Path.Combine(folder, "motion-" + frame + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
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
                var composite = Glaze.Panel.Luminance * 0.98 + 0.02;
                if ((Glaze.TextSecondary.Luminance + 0.05) / (composite + 0.05) < 4.5) failures.Add("candidate secondary text lacks bright-background linear-composite contrast");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            return failures;
        }
    }
}
