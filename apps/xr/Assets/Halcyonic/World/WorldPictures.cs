#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using UnityEngine;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// How a scene's pictures were stored, written by the bake beside them (<c>pictures.json</c>): each layer holds twelve
    /// stops below its scale, and each place's light has its own colour. Internal to the build, never sent.
    /// </summary>
    public sealed class WorldPicturesIndex
    {
        [JsonProperty("format", Required = Required.Always)]
        public int Format { get; set; }

        /// <summary>Each light's scale, by its name in the scene's manifest.</summary>
        [JsonProperty("lights", Required = Required.Always)]
        public Dictionary<string, float> Lights { get; set; } = new Dictionary<string, float>();

        [JsonProperty("air", Required = Required.Always)]
        public float Air { get; set; }

        /// <summary>Each place's scale, by its name in the manifest.</summary>
        [JsonProperty("places", Required = Required.Always)]
        public Dictionary<string, float> Places { get; set; } = new Dictionary<string, float>();

        /// <summary>Each place's light colour in linear light, by its name in the manifest.</summary>
        [JsonProperty("place_colours", Required = Required.Always)]
        public Dictionary<string, float[]> PlaceColours { get; set; } = new Dictionary<string, float[]>();
    }

    /// <summary>
    /// A scene's pictures (ADR 0049, decision 9), as the build holds them under <c>Resources/HalcyonicWorld/Scenes/&lt;scene&gt;</c>
    /// or as a bake left them in a folder: its manifest, one layer for each of its lights, its places' lights four to a
    /// picture, its air by day (with the air's amount) and by night, its view transform and its depth.
    /// </summary>
    public sealed class WorldPictures
    {
        public const string ScenesPath = "HalcyonicWorld/Scenes/";

        /// <summary>The depth's size: distances in meters, 32-bit floats, top row first.</summary>
        public const int DepthWidth = 512;

        public const int DepthHeight = 256;

        private WorldPictures(WorldScene scene, WorldPicturesIndex index, Dictionary<string, Texture> lights, Texture? places0, Texture? places1, Texture airDay, Texture airNight, Texture view, float[]? depth)
        {
            Scene = scene;
            Index = index;
            Lights = lights;
            Places0 = places0;
            Places1 = places1;
            AirDay = airDay;
            AirNight = airNight;
            View = view;
            Depth = depth;
        }

        public WorldScene Scene { get; }

        public WorldPicturesIndex Index { get; }

        public IReadOnlyDictionary<string, Texture> Lights { get; }

        public Texture? Places0 { get; }

        public Texture? Places1 { get; }

        public Texture AirDay { get; }

        public Texture AirNight { get; }

        public Texture View { get; }

        /// <summary>Distances from the eyes, row by row from the bottom, or null.</summary>
        public float[]? Depth { get; }

        /// <summary>The scenes the build holds, by name, in name order.</summary>
        public static IReadOnlyList<string> InBuild()
        {
            var names = new List<string>();
            foreach (var manifest in Resources.LoadAll<TextAsset>(ScenesPath))
            {
                if (manifest.name != "scene") continue;
                var scene = WorldOf.Scene(manifest.text, out _);
                if (scene != null && !names.Contains(scene.Scene)) names.Add(scene.Scene);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>Whether the build holds a scene's pictures at all, not only its manifest (which is tracked; the pictures are copied in).</summary>
        public static bool PicturesInBuild(string scene) => Resources.Load<TextAsset>(ScenesPath + scene + "/pictures") != null;

        /// <summary>A scene's pictures from the build, or null with why.</summary>
        public static WorldPictures? FromBuild(string scene, out string? why)
        {
            var at = ScenesPath + scene + "/";
            return Make(Resources.Load<TextAsset>(at + "scene")?.text, Resources.Load<TextAsset>(at + "pictures")?.text,
                name => Resources.Load<Texture2D>(at + name), Resources.Load<TextAsset>(at + "depth")?.bytes, out why);
        }

        /// <summary>A scene's pictures from a folder a bake left (the Editor's renders only), or null with why.</summary>
        public static WorldPictures? FromFolder(string folder, List<Texture2D> made, out string? why)
        {
            string? Text(string name) => File.Exists(Path.Combine(folder, name)) ? File.ReadAllText(Path.Combine(folder, name)) : null;
            Texture? Picture(string name)
            {
                var path = Path.Combine(folder, name + ".png");
                if (!File.Exists(path)) return null;
                // As the build imports them: every layer as stored, the view transform in sRGB.
                var linear = name != "view";
                var mips = name != "view";
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear)
                {
                    wrapModeU = name == "view" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
                    wrapModeV = TextureWrapMode.Clamp,
                    anisoLevel = name == "view" ? 0 : 4,
                    filterMode = name == "view" ? FilterMode.Bilinear : FilterMode.Trilinear,
                    hideFlags = HideFlags.DontSave,
                };
                texture.LoadImage(File.ReadAllBytes(path));
                made.Add(texture);
                return texture;
            }
            var depthPath = Path.Combine(folder, "depth.bytes");
            return Make(Text("scene.json"), Text("pictures.json"), Picture, File.Exists(depthPath) ? File.ReadAllBytes(depthPath) : null, out why);
        }

        private static WorldPictures? Make(string? manifest, string? indexText, Func<string, Texture?> picture, byte[]? depthBytes, out string? why)
        {
            if (manifest == null) { why = "no manifest"; return null; }
            var scene = WorldOf.Scene(manifest, out why);
            if (scene == null) return null;
            WorldPicturesIndex? index;
            try
            {
                index = indexText == null ? null : JsonConvert.DeserializeObject<WorldPicturesIndex>(indexText, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (JsonException)
            {
                index = null;
            }
            if (index == null || index.Format != 1) { why = "no index of its pictures"; return null; }
            var lights = new Dictionary<string, Texture>(StringComparer.Ordinal);
            foreach (var light in scene.Lights)
            {
                var texture = picture("light-" + light.Light);
                if (texture == null || !index.Lights.ContainsKey(light.Light)) { why = "no picture of a light"; return null; }
                lights[light.Light] = texture;
            }
            var airDay = picture("air-day");
            var airNight = picture("air-night");
            var view = picture("view");
            if (airDay == null || airNight == null || view == null) { why = "no air or view transform"; return null; }
            var places0 = picture("places-0");
            var places1 = picture("places-1");
            if (scene.Places.Count > 0 && places0 == null || scene.Places.Count > 4 && places1 == null) { why = "no picture of the places"; return null; }
            foreach (var place in scene.Places)
            {
                if (!index.PlaceColours.TryGetValue(place.Place, out var colour) || colour.Length != 3 || !index.Places.ContainsKey(place.Place)) { why = "no colour or scale of a place"; return null; }
            }
            why = null;
            return new WorldPictures(scene, index, lights, places0, places1, airDay, airNight, view, DepthOf(depthBytes));
        }

        private static float[]? DepthOf(byte[]? bytes)
        {
            if (bytes == null || bytes.Length != DepthWidth * DepthHeight * 4) return null;
            var top = new float[DepthWidth * DepthHeight];
            Buffer.BlockCopy(bytes, 0, top, 0, bytes.Length);
            var depth = new float[top.Length];
            for (var row = 0; row < DepthHeight; row++) Array.Copy(top, row * DepthWidth, depth, (DepthHeight - 1 - row) * DepthWidth, DepthWidth);
            return depth;
        }
    }
}
