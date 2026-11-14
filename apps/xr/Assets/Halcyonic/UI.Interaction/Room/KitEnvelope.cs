#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The most the kit's bar lays, measured, never typed (the lead's 2026-11-13T1936Z ruling): for each text size and panel
    /// width, the widest and the tallest any PartsCheck state laid, in dp, kept in Resources/HalcyonicKitEnvelope.json. PartsCheck
    /// records what it lays and fails a state that passes the recorded most (so a kit that grows says so, and the record is
    /// changed on purpose, with the run's measured table); the plane keeps a shelf and the characters clear of it. Where no
    /// width or size is recorded, the nearest recorded entry at or above it stands, else the widest at that size.
    /// </summary>
    public static class KitEnvelope
    {
        public const string ResourceName = "HalcyonicKitEnvelope";

        [Serializable]
        public sealed class Entry
        {
            public float scale;
            public float panelDp;
            public float widthDp;
            public float heightDp;

            /// <summary>
            /// The plane at REST (the lead's 2026-11-14T01:13Z ruling 1): the most a bar draws with no page open, a shelf's placing
            /// page counting as rest (XR's 01:15Z), at this size; panelDp is then the width it was laid at. Never read by Most.
            /// </summary>
            public bool resting;
        }

        [Serializable]
        private sealed class Record
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static List<Entry>? entries;

        /// <summary>Every recorded entry, read once.</summary>
        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (entries != null) return entries;
                var text = Resources.Load<TextAsset>(ResourceName);
                entries = text == null ? new List<Entry>() : JsonUtility.FromJson<Record>(text.text)?.entries ?? new List<Entry>();
                return entries;
            }
        }

        /// <summary>For checks that write a new record: forget what was read, so the next read takes the file again.</summary>
        public static void Forget() => entries = null;

        /// <summary>
        /// The most laid at text scale <paramref name="scale"/> with a panel <paramref name="panelDp"/> wide, in dp; null where
        /// nothing at that size is recorded (the caller then has no measured envelope and says so).
        /// </summary>
        public static Vector2? Most(float scale, float panelDp)
        {
            var atSize = Entries.Where(entry => !entry.resting && Mathf.Abs(entry.scale - scale) < 0.01f).ToList();
            if (atSize.Count == 0) return null;
            var atOrAbove = atSize.Where(entry => entry.panelDp >= panelDp - 0.5f).OrderBy(entry => entry.panelDp).FirstOrDefault()
                ?? atSize.OrderByDescending(entry => entry.panelDp).First();
            return new Vector2(atOrAbove.widthDp, atOrAbove.heightDp);
        }

        /// <summary>
        /// The most drawn at REST at text scale <paramref name="scale"/>, in dp (see <see cref="Entry.resting"/>); null where no resting
        /// entry is recorded at that size (the caller then falls back to the largest, as RoomBarView.RestingAt does).
        /// </summary>
        public static Vector2? Resting(float scale)
        {
            var entry = Entries.Where(each => each.resting && Mathf.Abs(each.scale - scale) < 0.01f).OrderByDescending(each => each.heightDp).FirstOrDefault();
            return entry == null ? (Vector2?)null : new Vector2(entry.widthDp, entry.heightDp);
        }

        /// <summary>The record as JSON, from measured entries: what a run writes beside its renders for a person to commit.</summary>
        public static string ToJson(IEnumerable<Entry> measured) =>
            JsonUtility.ToJson(new Record { entries = measured.OrderBy(entry => entry.scale).ThenByDescending(entry => entry.panelDp).ToList() }, true);
    }
}
