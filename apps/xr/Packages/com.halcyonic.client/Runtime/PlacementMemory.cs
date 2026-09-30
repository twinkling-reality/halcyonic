#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// Which saved spatial anchor keeps the stage's place in which room, so the characters are where
    /// the person left them the next time the app starts in that room. Room and anchor ids are
    /// opaque strings from the headset (its scene room and spatial anchor UUIDs).
    /// </summary>
    /// <remarks>
    /// It remembers the <see cref="MaxRooms"/> rooms used most recently, one anchor each. Whatever it
    /// stops remembering, a room's replaced anchor or the least recently used room's, is returned to
    /// the caller to erase from the headset, so saved anchors do not pile up. The saved form is plain
    /// text, one room per line, which needs no reflection to read in a player build.
    /// </remarks>
    public sealed class PlacementMemory
    {
        public const int MaxRooms = 8;

        private const string Header = "halcyonic-room-placements 1";

        /// <summary>Least recently used first.</summary>
        private readonly List<(string Room, string Anchor)> entries = new List<(string, string)>();

        /// <summary>The anchor that keeps the stage's place in a room, or null.</summary>
        public string? AnchorFor(string room) =>
            entries.Where(entry => entry.Room == room).Select(entry => entry.Anchor).FirstOrDefault();

        /// <summary>
        /// The anchor saved most recently in any room, for when the room cannot be read: an anchor
        /// from another room simply does not localize.
        /// </summary>
        public string? MostRecentAnchor => entries.Count == 0 ? null : entries[entries.Count - 1].Anchor;

        public int Count => entries.Count;

        /// <summary>
        /// Remembers the anchor for a room, as the most recently used. Returns the anchors no longer
        /// remembered, which the caller erases from the headset.
        /// </summary>
        public IReadOnlyList<string> Remember(string room, string anchor)
        {
            Validate(room, nameof(room));
            Validate(anchor, nameof(anchor));
            var dropped = new List<string>();
            var index = entries.FindIndex(entry => entry.Room == room);
            if (index >= 0)
            {
                if (entries[index].Anchor != anchor) dropped.Add(entries[index].Anchor);
                entries.RemoveAt(index);
            }
            // An anchor keeps one room's place; a stale record of it elsewhere goes.
            entries.RemoveAll(entry => entry.Anchor == anchor);
            entries.Add((room, anchor));
            while (entries.Count > MaxRooms)
            {
                dropped.Add(entries[0].Anchor);
                entries.RemoveAt(0);
            }
            return dropped;
        }

        /// <summary>Marks a room as used now, without changing its anchor.</summary>
        public void Touch(string room)
        {
            var index = entries.FindIndex(entry => entry.Room == room);
            if (index < 0 || index == entries.Count - 1) return;
            var entry = entries[index];
            entries.RemoveAt(index);
            entries.Add(entry);
        }

        /// <summary>Forgets an anchor that is gone or no longer suits its room. Returns whether it was remembered.</summary>
        public bool Forget(string anchor) => entries.RemoveAll(entry => entry.Anchor == anchor) > 0;

        public string Save()
        {
            var text = new StringBuilder(Header).Append('\n');
            foreach (var (room, anchor) in entries) text.Append(room).Append(' ').Append(anchor).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Reads what <see cref="Save"/> wrote. Anything else, including nothing, is an empty memory,
        /// and malformed lines are skipped: a lost placement is chosen again, never an error.
        /// </summary>
        public static PlacementMemory Load(string? saved)
        {
            var memory = new PlacementMemory();
            if (saved == null) return memory;
            var lines = saved.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count == 0 || lines[0] != Header) return memory;
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(' ');
                if (parts.Length != 2 || !IsId(parts[0]) || !IsId(parts[1])) continue;
                memory.Remember(parts[0], parts[1]);
            }
            return memory;
        }

        private static bool IsId(string value) => value.Length > 0 && value.Length <= 128 && !value.Any(char.IsWhiteSpace);

        private static void Validate(string value, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
            if (!IsId(value)) throw new ArgumentException("An id is non-empty, at most 128 characters, without white space.", name);
        }
    }
}
