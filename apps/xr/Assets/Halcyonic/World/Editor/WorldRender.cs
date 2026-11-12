#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.World.Editor
{
    /// <summary>
    /// Renders a person's world as the eyes would see it, without a headset: views from where the scene was rendered, each
    /// mixed from a description as the headset mixes it (ADR 0049, decision 9), in an empty scene each, into
    /// <c>Builds/WorldRenders</c>. In batch mode, with graphics: <c>-executeMethod Halcyonic.XR.World.Editor.WorldRender.Check</c>,
    /// with <c>HALCYONIC_WORLD_SCENE</c> naming a folder a bake left; without it, the first scene the build holds. The
    /// descriptions are the world starter's own (<c>HALCYONIC_WORLD_STARTER</c> names its <c>world.json</c>) with one change
    /// each, as a person would speak it. <c>HALCYONIC_WORLD_ONLY</c> narrows to the views whose names hold it. A render is
    /// a picture of what is drawn, never proof of how it looks or runs on the headset.
    /// </summary>
    public static class WorldRender
    {
        private const int Width = 2000;
        private const int Height = 1800;

        private static readonly (string Name, float Pitch, float Yaw, Action<WorldDescription> Change, string[] Lit)[] Shots =
        {
            ("as-made-ahead", 8f, 0f, _ => { }, new string[0]),
            ("as-made-up", 35f, 0f, _ => { }, new string[0]),
            ("as-made-overhead", 86f, 0f, _ => { }, new string[0]),
            ("as-made-left", 6f, -80f, _ => { }, new string[0]),
            ("as-made-right", 6f, 80f, _ => { }, new string[0]),
            ("as-made-down", -35f, 0f, _ => { }, new string[0]),
            ("night-ahead", 8f, 0f, d => d.Night = 1, new string[0]),
            ("night-up", 35f, 0f, d => d.Night = 1, new string[0]),
            ("half-night", 8f, 0f, d => d.Night = 0.5, new string[0]),
            ("warmer-sunset", 8f, 0f, d => Light(d, "sunset", 2, "#FFB070"), new string[0]),
            ("suns-off", 35f, 0f, d => Light(d, "suns", 0, "#FFFFFF"), new string[0]),
            ("clear-air", 8f, 0f, d => d.Haze.Thickness = 0.3, new string[0]),
            ("thick-air", 8f, 0f, d => d.Haze.Thickness = 2, new string[0]),
            ("towns-bright-night", 8f, 0f, d => { d.Night = 1; Light(d, "towns", 3, "#FFFFFF"); }, new string[0]),
            ("places-lit", 8f, 0f, _ => { }, new[] { "*" }),
            ("places-lit-night", 8f, 0f, d => d.Night = 1, new[] { "*" }),
            ("places-lit-up", 35f, 0f, _ => { }, new[] { "*" }),
        };

        /// <summary>A light changed as a person would say it: its brightness times the starter's ("twice as bright"), its tint as given.</summary>
        private static void Light(WorldDescription d, string light, double times, string colour)
        {
            foreach (var l in d.Lights.Where(l => l.Light == light))
            {
                l.Brightness = System.Math.Min(4, l.Brightness * times);
                l.Colour = colour;
            }
        }

        [MenuItem("Halcyonic/Render the World")]
        public static void Menu() => Run(exitWhenDone: false);

        public static void Check() => Run(exitWhenDone: true);

        private static void Run(bool exitWhenDone)
        {
            var only = Environment.GetEnvironmentVariable("HALCYONIC_WORLD_ONLY");
            var from = Environment.GetEnvironmentVariable("HALCYONIC_WORLD_SCENE");
            var starterPath = Environment.GetEnvironmentVariable("HALCYONIC_WORLD_STARTER");
            if (string.IsNullOrEmpty(starterPath)) starterPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "starters", "world", "template", "world.json"));
            var failures = new List<string>();
            var rendered = 0;
            var made = new List<Texture2D>();
            try
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorldRenders"));
                Directory.CreateDirectory(folder);
                string? why;
                var pictures = string.IsNullOrEmpty(from)
                    ? WorldPictures.InBuild().Where(WorldPictures.PicturesInBuild).Select(scene => WorldPictures.FromBuild(scene, out _)).FirstOrDefault(p => p != null)
                    : WorldPictures.FromFolder(from, made, out why);
                if (pictures == null) throw new InvalidOperationException("no scene's pictures");
                var starting = File.ReadAllText(starterPath);
                foreach (var shot in Shots)
                {
                    if (!string.IsNullOrEmpty(only) && shot.Name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var description = JsonConvert.DeserializeObject<WorldDescription>(starting)!;
                    shot.Change(description);
                    var mix = WorldOf.Read(description, pictures.Scene, new List<ProjectView>(), out why);
                    if (mix == null)
                    {
                        failures.Add(shot.Name + ": its description is not one (" + why + ")");
                        continue;
                    }
                    var lit = shot.Lit.Contains("*") ? pictures.Scene.Places.Select(p => p.Place).ToArray() : shot.Lit;
                    var problem = RenderOne(shot.Name, shot.Pitch, shot.Yaw, folder, pictures, mix, lit);
                    if (problem != null) failures.Add(shot.Name + ": " + problem);
                    rendered++;
                }
                if (rendered == 0) failures.Add("no view was rendered");
                else Measure(pictures, starting);
            }
            catch (Exception exception)
            {
                failures.Add("the render stopped: " + exception.GetType().Name);
                Debug.LogException(exception);
            }
            finally
            {
                foreach (var texture in made) UnityEngine.Object.DestroyImmediate(texture);
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic world render: " + failure);
            Debug.Log("Halcyonic world render: " + rendered + " views, " + failures.Count + " failures.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count > 0 ? 1 : 0);
        }

        private static string? RenderOne(string name, float pitch, float yaw, string folder, WorldPictures pictures, WorldMix mix, IReadOnlyCollection<string> lit)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Halcyonic world");
            var eye = new GameObject("Eyes") { tag = "MainCamera" };
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            try
            {
                var world = root.AddComponent<WorldView>();
                world.BuildNow(pictures);
                world.MixNow(mix, lit, WorldView.Arrived);
                eye.transform.SetPositionAndRotation(new Vector3(0f, WorldView.Eyes, 0f), Quaternion.Euler(-pitch, yaw, 0f));
                var camera = eye.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = 90f;
                camera.aspect = (float)Width / Height;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 1000f;
                camera.allowHDR = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.enabled = false;
                camera.targetTexture = target;
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                var problem = Empty(image) ? "the frame is one flat colour" : null;
                UnityEngine.Object.DestroyImmediate(image);
                return problem;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(eye);
                UnityEngine.Object.DestroyImmediate(root);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// Times, on this Mac's GPU (never the headset's), one mix of every layer into the panorama, and one view drawn
        /// from the mixed panorama: each made to finish before the clock stops, by reading one pixel back. The headset
        /// pays the first only while a change eases in, the second every frame.
        /// </summary>
        private static void Measure(WorldPictures pictures, string starting)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Halcyonic world");
            var eye = new GameObject("Eyes") { tag = "MainCamera" };
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                var world = root.AddComponent<WorldView>();
                world.BuildNow(pictures);
                var mix = WorldOf.Read(JsonConvert.DeserializeObject<WorldDescription>(starting)!, pictures.Scene, new List<ProjectView>(), out _)!;
                var camera = eye.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 90f;
                camera.targetTexture = target;
                eye.transform.SetPositionAndRotation(new Vector3(0f, WorldView.Eyes, 0f), Quaternion.Euler(-35f, 0f, 0f));
                void Settle()
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    RenderTexture.active = previous;
                }
                double Time(Action step, int times)
                {
                    step();
                    Settle();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < times; i++) step();
                    Settle();
                    return clock.Elapsed.TotalMilliseconds / times;
                }
                var mixing = Time(() => world.MixNow(mix, new string[0], WorldView.Arrived), 10);
                var viewing = Time(() => camera.Render(), 20);
                var size = 0L;
                foreach (var texture in pictures.Lights.Values) size += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                Debug.Log("Halcyonic world render: on this Mac, a mix of every layer takes " + mixing.ToString("F2") + " ms and a view from the mixed picture " + viewing.ToString("F2")
                    + " ms (2000 x 1800); the light layers as loaded here take " + (size / 1048576) + " MB (uncompressed, not as the headset's build holds them).");
                camera.targetTexture = null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
                UnityEngine.Object.DestroyImmediate(eye);
                UnityEngine.Object.DestroyImmediate(root);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>Whether a frame shows nothing: a missing shader or an undrawn world gives one flat colour.</summary>
        private static bool Empty(Texture2D image)
        {
            var pixels = image.GetPixels32();
            var first = pixels[0];
            var differing = 0;
            for (var index = 0; index < pixels.Length; index += 997)
            {
                var pixel = pixels[index];
                if (Mathf.Abs(pixel.r - first.r) + Mathf.Abs(pixel.g - first.g) + Mathf.Abs(pixel.b - first.b) > 12) differing++;
            }
            return differing < 20;
        }
    }
}
