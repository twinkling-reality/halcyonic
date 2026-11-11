#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What the headset takes from a person's world description: only what it draws now.</summary>
    public sealed class WorldMood
    {
        public WorldMood(double night, IReadOnlyList<string> placeProjectIds)
        {
            Night = night;
            PlaceProjectIds = placeProjectIds;
        }

        /// <summary>How far night has come, from day (0) to night (1), read from the sky the description gives.</summary>
        public double Night { get; }

        /// <summary>The projects the description gives a place to, by their ids, in its order: names that match no known project are left out.</summary>
        public IReadOnlyList<string> PlaceProjectIds { get; }
    }

    /// <summary>
    /// A person's world as the headset takes it from the Mac's reading (ADR 0049). The generated bindings enforce
    /// no range, pattern or cap, so every rule of the contract is checked again here before anything is drawn
    /// (AUDIT S2); a description that breaks one is not used, and the last one used stays. A place names its
    /// project by name: the name is only compared, by ordinal equality, with the names of projects the headset
    /// knows, and never drawn or used otherwise; the project's own name is what the headset shows (AUDIT S1).
    /// </summary>
    public static class WorldOf
    {
        public const string StarterId = "world";

        public const int MaxPlaces = 8;

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

        /// <summary>The mood a description gives, or null with why when it breaks a rule of the contract.</summary>
        public static WorldMood? Read(WorldDescription? description, IEnumerable<ProjectView> projects, out string? why)
        {
            why = Problem(description);
            if (why != null || description == null) return null;
            var ids = new List<string>();
            foreach (var place in description.Places)
            {
                foreach (var project in projects)
                {
                    if (!string.Equals(project.Name, place.Project, StringComparison.Ordinal)) continue;
                    if (!ids.Contains(project.ProjectId)) ids.Add(project.ProjectId);
                    break;
                }
            }
            return new WorldMood(NightOf(description), ids);
        }

        /// <summary>
        /// How far night has come: from the sky's own brightness straight up (a sky as bright as a dusk's is day for
        /// the world's pictures, one near black is night), and the sun or moon below the horizon pulls toward night.
        /// </summary>
        public static double NightOf(WorldDescription description)
        {
            var zenith = Luminance(description.Sky.Zenith);
            var horizon = Luminance(description.Sky.Horizon);
            var sky = 0.75 * zenith + 0.25 * horizon;
            var night = 1 - Smooth(0.006, 0.05, sky);
            if (description.Disc != null && description.Disc.Height < 0) night = Math.Max(night, Smooth(0, -10, description.Disc.Height));
            return Math.Round(night, 4);
        }

        /// <summary>Why a description breaks the contract, or null when it keeps every rule.</summary>
        public static string? Problem(WorldDescription? d)
        {
            if (d == null) return "no description";
            if (d.Format != 1) return "/format";
            if (d.Sky == null || d.Clouds == null || d.Mist == null || d.Water == null || d.Ground == null || d.Wind == null || d.Places == null) return "a part is missing";
            if (!Colour(d.Sky.Zenith)) return "/sky/zenith";
            if (d.Sky.Band != null && !Colour(d.Sky.Band)) return "/sky/band";
            if (!Colour(d.Sky.Horizon)) return "/sky/horizon";
            if (d.Disc != null)
            {
                if (!In(d.Disc.Bearing, -180, 180)) return "/disc/bearing";
                if (!In(d.Disc.Height, -10, 90)) return "/disc/height";
                if (!In(d.Disc.Size, 0.25, 15)) return "/disc/size";
                if (!Colour(d.Disc.Colour)) return "/disc/colour";
                if (!In(d.Disc.Glow, 0, 1)) return "/disc/glow";
            }
            if (!In(d.Stars, 0, 1)) return "/stars";
            if (!In(d.Clouds.Amount, 0, 1) || !Colour(d.Clouds.Colour)) return "/clouds";
            if (!In(d.Mist.Amount, 0, 1) || !Colour(d.Mist.Colour)) return "/mist";
            if (!Colour(d.Water.Near) || !Colour(d.Water.Far) || !In(d.Water.Ripple, 0, 1) || !In(d.Water.Shine, 0, 1)) return "/water";
            if (!Colour(d.Ground.Colour) || !In(d.Ground.Radius, 1.5, 12)) return "/ground";
            if (!In(d.Wind.Bearing, -180, 180) || !In(d.Wind.Strength, 0, 1)) return "/wind";
            if (d.Places.Count > MaxPlaces) return "/places";
            for (var i = 0; i < d.Places.Count; i++)
            {
                var place = d.Places[i];
                if (place == null || string.IsNullOrWhiteSpace(place.Project) || place.Project.Length > 200) return "/places/" + i + "/project";
                if (!In(place.Bearing, -180, 180)) return "/places/" + i + "/bearing";
                if (!In(place.Distance, 3, 80)) return "/places/" + i + "/distance";
            }
            return null;
        }

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

        /// <summary>A colour's relative luminance, in linear light.</summary>
        private static double Luminance(string hex)
        {
            double Channel(int at)
            {
                var c = int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }

        private static double Smooth(double edge0, double edge1, double x)
        {
            var t = Math.Max(0, Math.Min(1, (x - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }
    }
}
