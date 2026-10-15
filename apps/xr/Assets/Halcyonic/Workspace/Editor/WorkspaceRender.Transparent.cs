#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public static partial class WorkspaceRender
    {
        /// <summary>Actual production views over a transparent RGBA eye target. This isolates host
        /// rendering; it cannot establish the headset compositor or wearer acceptance.</summary>
        public static void TransparentReadingCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "TransparentReadingRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var priorField = ViewField.Current;
                try { ViewField.Current = FieldChecks.Quest3S; return RenderTransparentReading(folder); }
                catch (Exception error) { return new List<string> { error.ToString() }; }
                finally { ViewField.Current = priorField; KeepFontAssetsAsCommitted(); }
            });
            foreach (var failure in failures) Debug.LogError("Halcyonic: transparent reading: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: transparent reading: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderTransparentReading(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Transparent actual production reading views");
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var work = Work.Running();
                var journal = new JournalInfo { JournalId = "transparent-render", Origin = JournalOrigin.Live };
                var snapshot = new Snapshot { Journal = journal, Position = 1,
                    Projects = work.State.Projects.Values.ToList(), Workstreams = new List<WorkstreamView> { work.Workstream },
                    Executions = new List<ExecutionView> { work.Execution }, Runtimes = work.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot });
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, target);
                camera.backgroundColor = Color.clear;
                var commands = new CommandFactory(client);
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, InitialPlacementReady = () => true, Presentation = new WorkspacePresentationState(), Commands = commands,
                    State = () => session.State, Session = () => session, Connected = () => true,
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    File = (host, _) => new FileColumn(host, () => work.Present(live: true), commands, () => null, _ => null, () => ""),
                    Submit = (_, _) => throw new InvalidOperationException("Render cannot submit."),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                static void Draw(MenuDirector shownDirector, GameObject sceneRoot)
                {
                    for (var tick = 0; tick < 4; tick++)
                    { shownDirector.UpdateForRender(); shownDirector.DrawNow(); shownDirector.Plane.Advance(MenuPlane.OpeningSeconds); }
                    ForceMeshes(sceneRoot);
                }
                void Capture(string name)
                {
                    Draw(director, root);
                    var face = director.Plane.Chrome.GetComponentsInChildren<Surface>(false).Single(surface => surface.name == "Coated work object" && surface.CoatedInstrument && surface.Size.x > 0f);
                    var image = Render(camera, target);
                    try
                    {
                        failures.AddRange(TransparentMetrics(name, camera, face, image));
                        failures.AddRange(TransparentContrast(name, camera, director.Plane.Shown.Single().View.SubjectShown.Title, image));
                        RecordRolledField(name, folder, camera, target, director.Plane);
                        File.WriteAllBytes(Path.Combine(folder, name + "-rgba.png"), image.EncodeToPNG());
                        var composite = CompositeRoom(image);
                        try { File.WriteAllBytes(Path.Combine(folder, name + "-known-room.png"), composite.EncodeToPNG()); }
                        finally { UnityEngine.Object.DestroyImmediate(composite); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                    // Deliberately fade the actual production face, preserving all overlaid labels.
                    // The opacity gate must detect transmitted room even where text remains opaque.
                    // Draw has fully settled; production face opacity is one.
                    try
                    {
                        face.Fade(0.15f);
                        var negative = Render(camera, target);
                        try
                        {
                            var rejected = TransparentMetrics(name + " negative alpha .15", camera, face, negative);
                            if (!rejected.Any(failure => failure.Contains("interior alpha"))) failures.Add(name + ": opacity negative control was accepted");
                            File.WriteAllBytes(Path.Combine(folder, name + "-negative-rgba.png"), negative.EncodeToPNG());
                        }
                        finally { UnityEngine.Object.DestroyImmediate(negative); }
                    }
                    finally { face.Fade(1f); }
                }
                Capture("projects");
                var project = director.Plane.Shown.Last(each => each.View.Frame.Lines.Any(line => line.Action == ProjectsScreens.ChooseProject));
                if (!director.PressForRender(project.Kind, ProjectsScreens.ChooseProject, work.Workstream.ProjectId, project.View.Frame, null)) failures.Add("actual Projects selection refused");
                Capture("task-list");
                director.OpenFile(work.Workstream.WorkstreamId);
                Capture("file");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            return failures;
        }

        private static List<string> TransparentMetrics(string name, Camera camera, Surface face, Texture2D image)
        {
            var failures = new List<string>();
            var alpha = new List<float>();
            // The unit quad has local [-.5,.5] bounds; stay well within rounded corners/rim.
            for (var row = 0; row < 9; row++) for (var column = 0; column < 9; column++)
            {
                var p = camera.WorldToScreenPoint(face.transform.TransformPoint(new Vector3(-0.35f + column * 0.0875f, -0.35f + row * 0.0875f, 0f)));
                if (p.z <= 0 || p.x < 0 || p.y < 0 || p.x >= image.width || p.y >= image.height) { failures.Add(name + ": interior witness outside eye target"); continue; }
                alpha.Add(image.GetPixel((int)p.x, (int)p.y).a);
            }
            var minimum = alpha.Count == 0 ? 0f : alpha.Min();
            if (minimum < 0.98f) failures.Add(name + ": interior alpha below .98: " + minimum);
            var exterior = new[] { image.GetPixel(3, 3).a, image.GetPixel(image.width - 4, 3).a,
                image.GetPixel(3, image.height - 4).a, image.GetPixel(image.width - 4, image.height - 4).a }.Max();
            if (exterior > 0.01f) failures.Add(name + ": exterior alpha exceeds .01: " + exterior);
            Debug.Log("Halcyonic: transparent reading metrics " + name + ": interior-min-alpha=" + minimum.ToString("F4") + ", exterior-max-alpha=" + exterior.ToString("F4") + ", witnesses=" + alpha.Count);
            return failures;
        }



        /// <summary>Characterise actual camera roll independently of the production forward-only
        /// placement model. Counts are diagnostic, not a claim of comfort or automatic recovery.</summary>
        private static void RecordRolledField(string name, string folder, Camera camera, RenderTexture target, MenuPlane plane)
        {
            var original = camera.transform.rotation;
            var field = ViewField.Current ?? MenuPage.Quest3S;
            try
            {
                foreach (var roll in new[] { -25f, -15f, 0f, 15f, 25f })
                {
                    camera.transform.rotation = original * Quaternion.AngleAxis(roll, Vector3.forward);
                    var outside = 0; var excess = 0f;
                    foreach (var x in new[] { -.5f, .5f }) foreach (var y in new[] { -.5f, .5f })
                    {
                        var corner = plane.Chrome.transform.TransformPoint(new Vector3(x * plane.Chrome.Size.x, y * plane.Chrome.Size.y, 0));
                        var relative = camera.transform.InverseTransformPoint(corner);
                        var yaw = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                        var elevation = Mathf.Atan2(relative.y, new Vector2(relative.x, relative.z).magnitude) * Mathf.Rad2Deg;
                        if (relative.z <= 0 || !field.Shows(yaw, elevation)) outside++;
                        // Flat-frustum vertical coordinate, matching ViewField.Shows.
                        var vertical = Mathf.Atan2(relative.y, relative.z) * Mathf.Rad2Deg;
                        excess = Mathf.Max(excess, (float)Math.Max(Math.Max(yaw - field.Right, -yaw - field.Left), Math.Max(vertical - field.Up, -vertical - field.Down)));
                    }
                    Debug.Log("Halcyonic: transparent reading rolled field " + name + ": roll=" + roll + ", outside-corners=" + outside + ", maximum-excess-degrees=" + excess.ToString("F3"));
                    var rolled = Render(camera, target);
                    try
                    {
                        var composite = CompositeRoom(rolled);
                        try { File.WriteAllBytes(Path.Combine(folder, name + "-roll-" + roll + "-known-room.png"), composite.EncodeToPNG()); }
                        finally { UnityEngine.Object.DestroyImmediate(composite); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(rolled); }
                }
            }
            finally { camera.transform.rotation = original; }
        }

        private static List<string> TransparentContrast(string name, Camera camera, TMP_Text label, Texture2D image)
        {
            var failures = new List<string>();
            label.ForceMeshUpdate();
            // TMP_Text.bounds is mesh-local; Renderer.bounds supplies the actual world-space raster footprint.
            var rect = ScreenBounds(camera, label.GetComponent<Renderer>().bounds);
            var low = 1f; var high = 0f; var count = 0;
            // Sample actual rasterised title stroke and its enclosed dark substrate. Anti-aliased
            // fringe is excluded by taking the brightest core pixel, not its mean with empty space.
            for (var y = Mathf.Max(0, Mathf.CeilToInt(rect.yMin)); y < Mathf.Min(image.height, Mathf.FloorToInt(rect.yMax)); y++)
            for (var x = Mathf.Max(0, Mathf.CeilToInt(rect.xMin)); x < Mathf.Min(image.width, Mathf.FloorToInt(rect.xMax)); x++)
            {
                var p = image.GetPixel(x, y);
                if (p.a < .98f) continue;
                var l = .2126f * p.r + .7152f * p.g + .0722f * p.b;
                low = Mathf.Min(low, l); high = Mathf.Max(high, l); count++;
            }
            var ratio = (high + .05f) / (low + .05f);
            Debug.Log("Halcyonic: transparent reading contrast " + name + ": title-core=" + ratio.ToString("F3") + ", linear-low=" + low.ToString("F4") + ", linear-high=" + high.ToString("F4") + ", opaque-pixels=" + count);
            if (count == 0 || ratio < 4.5f) failures.Add(name + ": actual rasterised title core contrast below 4.5: " + ratio);
            return failures;
        }

        // The shader emits premultiplied RGB (Blend One OneMinusSrcAlpha). Apply source-over once,
        // in explicitly linear target space; no unpremultiply. PNG output is converted to sRGB.
        private static Texture2D CompositeRoom(Texture2D eye)
        {
            var result = new Texture2D(eye.width, eye.height, TextureFormat.RGBA32, false);
            var pixels = eye.GetPixels();
            for (var y = 0; y < eye.height; y++) for (var x = 0; x < eye.width; x++)
            {
                var i = y * eye.width + x;
                var room = ((x / 48 + y / 48) & 1) == 0 ? new Color(.92f, .87f, .72f) : new Color(.12f, .22f, .3f);
                var src = pixels[i];
                room = room.linear;
                pixels[i] = new Color(src.r + room.r * (1f - src.a), src.g + room.g * (1f - src.a), src.b + room.b * (1f - src.a), 1f).gamma;
            }
            result.SetPixels(pixels); result.Apply(); return result;
        }
    }
}
