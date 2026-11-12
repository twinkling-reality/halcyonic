#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>One of the scene's lights as the headset mixes it: its layer, how strongly, and its tint in linear light.</summary>
    public sealed class WorldLightMix
    {
        public WorldLightMix(string light, WorldLightWhen when, double brightness, double red, double green, double blue)
        {
            Light = light;
            When = when;
            Brightness = brightness;
            Red = red;
            Green = green;
            Blue = blue;
        }

        /// <summary>The light's name in its scene's manifest, which names its layer.</summary>
        public string Light { get; }

        public WorldLightWhen When { get; }

        /// <summary>Against the world as made: 0 off, 1 as made, 4 four times.</summary>
        public double Brightness { get; }

        public double Red { get; }

        public double Green { get; }

        public double Blue { get; }

        /// <summary>How much of this light shows at a moment of the night (0 dusk, 1 night): day lights fade, night lights come.</summary>
        public double At(double night) => Brightness * WorldOf.Shining(When, night);
    }

    /// <summary>One of the scene's places as the headset lights it: its layer, and the project whose work lights it.</summary>
    public sealed class WorldPlaceMix
    {
        public WorldPlaceMix(string place, string projectId)
        {
            Place = place;
            ProjectId = projectId;
        }

        /// <summary>The place's name in its scene's manifest, which names its layer.</summary>
        public string Place { get; }

        public string ProjectId { get; }
    }

    /// <summary>What the headset takes from a person's world description: exactly what it mixes.</summary>
    public sealed class WorldMix
    {
        public WorldMix(string scene, double night, IReadOnlyList<WorldLightMix> lights, double hazeThickness, double hazeRed, double hazeGreen, double hazeBlue, IReadOnlyList<WorldPlaceMix> places)
        {
            Scene = scene;
            Night = night;
            Lights = lights;
            HazeThickness = hazeThickness;
            HazeRed = hazeRed;
            HazeGreen = hazeGreen;
            HazeBlue = hazeBlue;
            Places = places;
        }

        public string Scene { get; }

        /// <summary>How far night has come, from dusk (0) to night (1).</summary>
        public double Night { get; }

        /// <summary>Every light of the scene, in its manifest's order.</summary>
        public IReadOnlyList<WorldLightMix> Lights { get; }

        /// <summary>The air against the world as made: 0 none, 1 as made, 4 four times.</summary>
        public double HazeThickness { get; }

        public double HazeRed { get; }

        public double HazeGreen { get; }

        public double HazeBlue { get; }

        /// <summary>The places given to projects the headset knows, in the description's order: names that match no project are left out.</summary>
        public IReadOnlyList<WorldPlaceMix> Places { get; }
    }

    /// <summary>
    /// A person's world as the headset takes it from the Mac's reading (ADR 0049, format 2). The generated bindings
    /// enforce no range, pattern or cap, so every rule of the contract, and the rules its scene's manifest sets (each
    /// light once, only its places, each once), is checked again here before anything is mixed (AUDIT S2); a
    /// description that breaks one is not used, and the last one used stays. A place names its project by name: the
    /// name is only compared, by ordinal equality, with the names of projects the headset knows, and never drawn or
    /// used otherwise (AUDIT S1).
    /// </summary>
    public static class WorldOf
    {
        public const string StarterId = "world";

        public const int MaxPlaces = 8;

        public const int MaxLights = 8;

        private static readonly Regex Name = new Regex("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant);

        /// <summary>The person's world project in a snapshot: the one made from the world starter whose description the Mac read most recently, or null.</summary>
        public static ProjectView? Project(IEnumerable<ProjectView> projects)
        {
            ProjectView? newest = null;
            foreach (var project in projects)
            {
                if (project.Starter != StarterId || project.World?.Description == null) continue;
                if (newest == null || string.CompareOrdinal(project.World.ObservedAt, newest.World!.ObservedAt) > 0) newest = project;
            }
            return newest;
        }

        /// <summary>A scene's manifest as the build holds it beside its pictures, or null with why when it breaks a rule.</summary>
        public static WorldScene? Scene(string json, out string? why)
        {
            WorldScene? scene;
            try
            {
                scene = JsonConvert.DeserializeObject<WorldScene>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (JsonException)
            {
                why = "not a manifest";
                return null;
            }
            why = SceneProblem(scene);
            return why == null ? scene : null;
        }

        /// <summary>Why a manifest breaks the contract, or null when it keeps every rule.</summary>
        public static string? SceneProblem(WorldScene? s)
        {
            if (s == null) return "no manifest";
            if (s.Format != 1) return "/format";
            if (!IsName(s.Scene)) return "/scene";
            if (s.Lights == null || s.Lights.Count < 1 || s.Lights.Count > MaxLights) return "/lights";
            if (s.Places == null || s.Places.Count > MaxPlaces) return "/places";
            var lights = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < s.Lights.Count; i++)
            {
                if (s.Lights[i] == null || !IsName(s.Lights[i].Light) || !lights.Add(s.Lights[i].Light)) return "/lights/" + i + "/light";
            }
            var places = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < s.Places.Count; i++)
            {
                if (s.Places[i] == null || !IsName(s.Places[i].Place) || !places.Add(s.Places[i].Place)) return "/places/" + i + "/place";
            }
            return null;
        }

        /// <summary>The mix a description gives in its scene, or null with why when it breaks a rule.</summary>
        public static WorldMix? Read(WorldDescription? description, WorldScene scene, IEnumerable<ProjectView> projects, out string? why)
        {
            why = Problem(description, scene);
            if (why != null || description == null) return null;
            var lights = new List<WorldLightMix>();
            foreach (var light in scene.Lights)
            {
                foreach (var given in description.Lights)
                {
                    if (given.Light != light.Light) continue;
                    var (r, g, b) = Linear(given.Colour);
                    lights.Add(new WorldLightMix(light.Light, light.When, given.Brightness, r, g, b));
                    break;
                }
            }
            var places = new List<WorldPlaceMix>();
            foreach (var place in description.Places)
            {
                foreach (var project in projects)
                {
                    if (!string.Equals(project.Name, place.Project, StringComparison.Ordinal)) continue;
                    places.Add(new WorldPlaceMix(place.At, project.ProjectId));
                    break;
                }
            }
            var (hr, hg, hb) = Linear(description.Haze.Colour);
            return new WorldMix(description.Scene, description.Night, lights, description.Haze.Thickness, hr, hg, hb, places);
        }

        /// <summary>Why a description breaks the contract or its scene's manifest, or null when it keeps every rule.</summary>
        public static string? Problem(WorldDescription? d, WorldScene scene)
        {
            if (d == null) return "no description";
            if (d.Format != 2) return "/format";
            if (d.Lights == null || d.Haze == null || d.Places == null) return "a part is missing";
            if (!IsName(d.Scene) || d.Scene != scene.Scene) return "/scene";
            if (!In(d.Night, 0, 1)) return "/night";
            if (d.Lights.Count > MaxLights) return "/lights";
            var sceneLights = new HashSet<string>(StringComparer.Ordinal);
            foreach (var light in scene.Lights) sceneLights.Add(light.Light);
            var given = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < d.Lights.Count; i++)
            {
                var light = d.Lights[i];
                if (light == null || !IsName(light.Light) || !sceneLights.Contains(light.Light) || !given.Add(light.Light)) return "/lights/" + i + "/light";
                if (!In(light.Brightness, 0, 4)) return "/lights/" + i + "/brightness";
                if (!Colour(light.Colour)) return "/lights/" + i + "/colour";
            }
            if (given.Count != sceneLights.Count) return "/lights";
            if (!In(d.Haze.Thickness, 0, 4)) return "/haze/thickness";
            if (!Colour(d.Haze.Colour)) return "/haze/colour";
            if (d.Places.Count > MaxPlaces) return "/places";
            var scenePlaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var place in scene.Places) scenePlaces.Add(place.Place);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < d.Places.Count; i++)
            {
                var place = d.Places[i];
                if (place == null || string.IsNullOrWhiteSpace(place.Project) || place.Project.Length > 200) return "/places/" + i + "/project";
                if (!IsName(place.At) || !scenePlaces.Contains(place.At) || !taken.Add(place.At)) return "/places/" + i + "/at";
            }
            return null;
        }

        /// <summary>How much of a light shines at a moment of the night: night lights come with it, day lights fade by the square of what is left of dusk, so halfway reads as halfway.</summary>
        public static double Shining(WorldLightWhen when, double night) => when switch
        {
            WorldLightWhen.Day => (1 - night) * (1 - night),
            WorldLightWhen.Night => night,
            WorldLightWhen.All => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };

        private static bool IsName(string? name) => name != null && Name.IsMatch(name);

        private static bool In(double value, double low, double high) => !double.IsNaN(value) && value >= low && value <= high;

        private static bool Colour(string? hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            for (var i = 1; i < 7; i++)
            {
                var c = hex[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        /// <summary>A colour in linear light, each channel 0 to 1: <c>#FFFFFF</c> is 1, 1, 1.</summary>
        public static (double Red, double Green, double Blue) Linear(string hex)
        {
            double Channel(int at)
            {
                var c = int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return (Channel(1), Channel(3), Channel(5));
        }
    }
}
