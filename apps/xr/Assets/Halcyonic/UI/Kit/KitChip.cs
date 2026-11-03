#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A chip of the spatial kit: a small raised pill holding one thing a line is about, an icon of Halcyonic's
    /// before it. As evidence it holds what was recorded, a file's path, a command or a test run's summary, as
    /// written; as provenance it holds Halcyonic's own words for where a claim comes from, such as Agent says.
    /// The chip is the ground under outside text, which keeps its run by the shared rule (<see cref="GlazeRuns"/>), upright in the room's faces, stays
    /// literal, wraps to at most <c>maxLines</c> and is cut inside itself, never Halcyonic's words. As evidence that wraps (a
    /// path above all), its rows can follow the reader's rule instead of TextMeshPro's, which breaks beside a hyphen ("rate-" /
    /// "limit.ts"): at its spaces, else after a "/", "?", "&", "=", ",", ";" or ":", one label a row (<see cref="KitReading"/>).
    /// A name longer than a row keeps TextMeshPro's wrap, beside its own hyphen, never cut inside its letters ("sign-in.test.t" /
    /// "s"). A chip takes no press. Centred on its transform.
    /// </summary>
    public sealed class KitChip : MonoBehaviour
    {
        private const float PadDegrees = 0.55f;
        private const float GapDegrees = 0.35f;
        private const float RiseDegrees = 0.3f;

        private Surface plate = null!;
        private TextMeshPro icon = null!;
        private TextMeshPro words = null!;
        private readonly List<TextMeshPro> rows = new List<TextMeshPro>();
        private int order;
        private bool roomFaces;
        private Vector2 size;
        private IReadOnlyList<(int Start, int End)> theirs = new (int, int)[0];

        public Vector2 Size => size;

        public TextMeshPro Words => words;

        /// <summary>Where outside text stands in the chip's words as shown.</summary>
        public IReadOnlyList<(int Start, int End)> Theirs => theirs;

        public static KitChip Create(Transform parent, string name, int order = KitType.Order.Chip, bool room = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var chip = go.AddComponent<KitChip>();
            chip.plate = Surface.Create(go.transform, "Chip", order);
            chip.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Secondary, order + 2);
            chip.words = KitType.Create(go.transform, "Words", KitRole.Body, KitType.Text, order + 2, room: room);
            chip.order = order;
            chip.roomFaces = room;
            return chip;
        }

        /// <summary>
        /// Shows <paramref name="text"/> with <paramref name="glyph"/> before it in <paramref name="glyphColour"/>,
        /// at most <paramref name="maxWidth"/> wide, and returns the chip's size. <paramref name="byRows"/> lays outside text that
        /// wraps by the reader's rule, a label a row, where its rows fit <paramref name="maxLines"/>.
        /// </summary>
        public Vector2 Show(LineWords text, GlazeIcon? glyph, Color glyphColour, float maxWidth, int maxLines = 2, bool byRows = false)
        {
            KitType.Size(words, KitRole.Body);
            var pad = KitType.Scaled(PadDegrees);
            var rise = KitType.Scaled(RiseDegrees);
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            var iconRoom = glyph.HasValue ? iconEm + KitType.Scaled(GapDegrees) : 0f;
            icon.gameObject.SetActive(glyph.HasValue);
            if (glyph is GlazeIcon shownIcon)
            {
                icon.fontSize = GlazeTokens.FontSize(iconEm);
                icon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                icon.color = glyphColour;
                GlazeIcons.Show(icon, shownIcon);
            }
            var room = Mathf.Max(KitType.Units(2f), maxWidth - 2f * pad - iconRoom);
            foreach (var extra in rows) extra.gameObject.SetActive(false);
            theirs = GlazeRuns.Of(words).Set(text);
            var (lines, width, height) = KitType.Lay(words, room, maxLines);
            var pitch = KitType.LinePitch(words);
            var shownRows = 1;
            if (byRows && lines > 1 && text.AllTheirs)
            {
                var reading = new KitReading(text.Text, (row, _) =>
                {
                    GlazeRuns.Of(words).Set(row);
                    return words.GetPreferredValues(words.text).x <= room + 1e-6f;
                });
                if (reading.Rows.Count <= maxLines && BreaksClean(reading))
                {
                    shownRows = reading.Rows.Count;
                    width = 0f;
                    for (var row = 0; row < shownRows; row++)
                    {
                        var label = row == 0 ? words : RowAt(row - 1);
                        label.gameObject.SetActive(true);
                        KitType.Size(label, KitRole.Body);
                        GlazeRuns.Of(label).Set(reading.RowWords(row));
                        var (_, rowWidth, _) = KitType.Lay(label, room, 1);
                        width = Mathf.Max(width, rowWidth);
                    }
                    theirs = GlazeRuns.Of(words).Theirs;
                    lines = shownRows;
                    height = shownRows * pitch;
                }
                else
                {
                    // More rows than the chip holds, or a name longer than a row, which the reader's rule would cut inside its
                    // letters: TextMeshPro's own wrap, which breaks beside a hyphen only there, cut inside their text as before.
                    theirs = GlazeRuns.Of(words).Set(text);
                    (lines, width, height) = KitType.Lay(words, room, maxLines);
                }
            }
            size = new Vector2(Mathf.Min(maxWidth, 2f * pad + iconRoom + width + KitType.Units(0.1f)), height + 2f * rise);
            var left = -size.x / 2f + pad;
            if (glyph.HasValue) icon.transform.localPosition = new Vector3(left + iconEm / 2f, size.y / 2f - rise - KitType.LinePitch(words) / 2f, -0.0005f);
            words.transform.localPosition = new Vector3(left + iconRoom, size.y / 2f - rise, -0.0005f);
            for (var row = 1; row < shownRows; row++) rows[row - 1].transform.localPosition = new Vector3(left + iconRoom, size.y / 2f - rise - row * pitch, -0.0005f);
            var radius = lines > 1 ? KitType.Units(SpatialKit.ControlRadiusDegrees) : size.y / 2f;
            plate.Draw(size, radius, KitType.Colour(SpatialKit.Raised));
            return size;
        }

        /// <summary>Whether every row of <paramref name="reading"/> but a line's last ends at a space or a mark, never inside a name.</summary>
        private static bool BreaksClean(KitReading reading)
        {
            for (var row = 0; row + 1 < reading.Rows.Count; row++)
            {
                if (!reading.Rows[row + 1].Continued) continue;
                var text = reading.Rows[row].Text;
                if (text.Length == 0 || " /?&=,;:".IndexOf(text[text.Length - 1]) < 0) return false;
            }
            return true;
        }

        /// <summary>A row label after the first, in the chip's words' look; made hidden.</summary>
        private TextMeshPro RowAt(int index)
        {
            while (rows.Count <= index)
            {
                var made = KitType.Create(transform, "Row " + (rows.Count + 1), KitRole.Body, KitType.Text, order + 2, room: roomFaces);
                made.gameObject.SetActive(false);
                rows.Add(made);
            }
            return rows[index];
        }
    }
}
