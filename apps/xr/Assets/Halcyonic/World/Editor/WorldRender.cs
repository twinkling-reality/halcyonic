#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.World.Editor
{
    /// <summary>
    /// Renders a person's world as the eyes would see it, without a headset: views from where the picture was rendered,
    /// in an empty scene each, into <c>Builds/WorldRenders</c>. In batch mode, with graphics:
    /// <c>-executeMethod Halcyonic.XR.World.Editor.WorldRender.Check</c>, with <c>HALCYONIC_WORLD_PANORAMA</c> naming a
    /// folder of pictures (<c>dusk.png</c>, <c>night.png</c>, <c>dusk-place.png</c>, <c>night-place.png</c>,
    /// <c>depth-512x256.f32</c>); without it, the pictures the build holds under
    /// <c>Resources/HalcyonicWorld/Pictures</c>. <c>HALCYONIC_WORLD_ONLY</c> narrows to the views whose names hold it.
    /// A render is a picture of what is drawn, never proof of how it looks or runs on the headset.
    /// </summary>
    public static class WorldRender
    {
        private const int Width = 2000;
        private const int Height = 1800;

        private static readonly (string Name, float Pitch, float Yaw, float Night, float Place, Vector3 Head)[] Shots =
        {
            ("pano-ahead", 8f, 0f, 0f, 0f, Vector3.zero),
            ("pano-up", 35f, 0f, 0f, 0f, Vector3.zero),
            ("pano-overhead", 86f, 0f, 0f, 0f, Vector3.zero),
            ("pano-left", 6f, -80f, 0f, 0f, Vector3.zero),
            ("pano-down", -35f, 0f, 0f, 0f, Vector3.zero),
            ("pano-place", 8f, 0f, 0f, 1f, Vector3.zero),
            ("pano-turn", 8f, 0f, 0.5f, 0f, Vector3.zero),
            ("pano-night", 8f, 0f, 1f, 0f, Vector3.zero),
            ("pano-night-up", 35f, 0f, 1f, 1f, Vector3.zero),
            ("pano-moved", 8f, 0f, 0f, 0f, new Vector3(0.2f, 0.1f, 0.15f)),
        };

        [MenuItem("Halcyonic/Render the World")]
        public static void Menu() => Run(exitWhenDone: false);

        public static void Check() => Run(exitWhenDone: true);

        private static void Run(bool exitWhenDone)
        {
            var only = Environment.GetEnvironmentVariable("HALCYONIC_WORLD_ONLY");
            var from = Environment.GetEnvironmentVariable("HALCYONIC_WORLD_PANORAMA");
            var failures = new List<string>();
            var rendered = 0;
            var made = new List<Texture2D>();
            try
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorldRenders"));
                Directory.CreateDirectory(folder);
                Texture2D? Picture(string name)
                {
                    if (string.IsNullOrEmpty(from)) return Resources.Load<Texture2D>(WorldView.PicturesPath + name);
                    var path = Path.Combine(from, name + ".png");
                    if (!File.Exists(path)) return null;
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, anisoLevel = 4, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                    texture.LoadImage(File.ReadAllBytes(path));
                    made.Add(texture);
                    return texture;
                }
                var dusk = Picture("dusk");
                var night = Picture("night");
                if (dusk == null || night == null) throw new InvalidOperationException("no dusk and night pictures");
                var duskPlace = Picture("dusk-place");
                var nightPlace = Picture("night-place");
                var depth = Depth(from);
                foreach (var shot in Shots)
                {
                    if (!string.IsNullOrEmpty(only) && shot.Name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var problem = RenderOne(shot, folder, dusk, night, duskPlace, nightPlace, depth);
                    if (problem != null) failures.Add(shot.Name + ": " + problem);
                    rendered++;
                }
                if (rendered == 0) failures.Add("no view was rendered");
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

        private static float[]? Depth(string? from)
        {
            byte[]? bytes = null;
            if (string.IsNullOrEmpty(from))
            {
                var asset = Resources.Load<TextAsset>(WorldView.PicturesPath + "depth");
                if (asset != null) bytes = asset.bytes;
            }
            else
            {
                var path = Path.Combine(from, "depth-" + WorldView.DepthWidth + "x" + WorldView.DepthHeight + ".f32");
                if (File.Exists(path)) bytes = File.ReadAllBytes(path);
            }
            if (bytes == null || bytes.Length != WorldView.DepthWidth * WorldView.DepthHeight * 4) return null;
            var top = new float[WorldView.DepthWidth * WorldView.DepthHeight];
            Buffer.BlockCopy(bytes, 0, top, 0, bytes.Length);
            var depth = new float[top.Length];
            for (var row = 0; row < WorldView.DepthHeight; row++)
                Array.Copy(top, row * WorldView.DepthWidth, depth, (WorldView.DepthHeight - 1 - row) * WorldView.DepthWidth, WorldView.DepthWidth);
            return depth;
        }

        private static string? RenderOne((string Name, float Pitch, float Yaw, float Night, float Place, Vector3 Head) shot, string folder,
            Texture2D dusk, Texture2D night, Texture2D? duskPlace, Texture2D? nightPlace, float[]? depth)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Halcyonic world");
            var eye = new GameObject("Eyes") { tag = "MainCamera" };
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            try
            {
                var world = root.AddComponent<WorldView>();
                world.BuildPanoramaNow(dusk, night, duskPlace, nightPlace, depth, depth == null ? 0 : WorldView.DepthWidth, depth == null ? 0 : WorldView.DepthHeight);
                world.Apply(shot.Night, shot.Place, WorldView.Arrived);
                eye.transform.SetPositionAndRotation(new Vector3(0f, WorldView.Eyes, 0f) + shot.Head, Quaternion.Euler(-shot.Pitch, shot.Yaw, 0f));
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
                File.WriteAllBytes(Path.Combine(folder, shot.Name + ".png"), image.EncodeToPNG());
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
