#nullable enable
using System;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// The agent's own words on the faded band low in the view (DESIGN's BUILD-SPEC-v4 1.6): the band's numbers, the words
    /// made ready to lay, and their paging. The band holds only what the agent said, or its question's prompt, in the one type
    /// size, at most <see cref="LinesAPart"/> lines a part; longer words page by parts, and the way on is the product's
    /// "Next Part · 2 of 3". The lines are counted where they are laid (the view lays them at the measure and at the person's
    /// text size); this says which of them a part holds. Lengths are Meta's dp, 16 a degree at the eye.
    /// </summary>
    public sealed class RoomWords
    {
        /// <summary>A part's lines at most.</summary>
        public const int LinesAPart = 3;

        /// <summary>The band's width, 55 degrees, and the centred measure its words are laid in.</summary>
        public const float WidthDp = 880f;

        public const float MeasureDp = 600f;

        /// <summary>The room above the first line and under the last.</summary>
        public const float PadDp = 26f;

        /// <summary>The ground fades to nothing over this much at each side, and over <see cref="EdgeFadeDp"/> at its top and foot.</summary>
        public const float SideFadeDp = 120f;

        public const float EdgeFadeDp = 22f;

        /// <summary>The band's top edge, a degree under the Hold to Talk row's feet; it grows downward.</summary>
        public const float TopPitch = -24.5f;

        /// <summary>The ground's colour, #121416, and its alpha as the page mixes it, in sRGB.</summary>
        public const int GroundColour = 0x121416;

        public const float GroundAlpha = 0.72f;

        /// <summary>Every word's tracking, in hundredths of an em: -0.6 percent (BUILD-SPEC-v4 1.3).</summary>
        public const float Tracking = -0.6f;

        /// <summary>Every character that ends a line: the line feed and carriage return, the vertical tab, the form feed, the next line, and the line and paragraph separators.</summary>
        private static readonly char[] LineBreaks = { '\n', '\r', (char)0x0B, (char)0x0C, (char)0x85, (char)0x2028, (char)0x2029 };

        private RoomWords(int lines, int part)
        {
            Lines = Math.Max(0, lines);
            Parts = (Lines + LinesAPart - 1) / LinesAPart;
            Part = Math.Max(0, Math.Min(part, Parts - 1));
        }

        /// <summary>The lines the words take as laid, every part's together.</summary>
        public int Lines { get; }

        /// <summary>How many parts the lines make, three lines each; none for no words.</summary>
        public int Parts { get; }

        /// <summary>The part that shows, from 0.</summary>
        public int Part { get; }

        /// <summary>The part's first line among the laid ones, and how many lines it holds, one to three.</summary>
        public int First => Part * LinesAPart;

        public int Count => Math.Max(0, Math.Min(LinesAPart, Lines - First));

        /// <summary>Whether a later part waits.</summary>
        public bool More => Part + 1 < Parts;

        /// <summary>The way to the next part in the product's words, "Next Part · 2 of 3", or null on the last part.</summary>
        public string? Next => More ? EntryText.NextPart(Part + 2, Parts) : null;

        /// <summary>The paging of <paramref name="lines"/> laid lines at <paramref name="part"/>, held inside the parts there are.</summary>
        public static RoomWords Of(int lines, int part = 0) => new RoomWords(lines, part);

        /// <summary>The next part, or this one where it is the last.</summary>
        public RoomWords Following() => More ? new RoomWords(Lines, Part + 1) : this;

        /// <summary>
        /// The band's height with <paramref name="lines"/> lines at the person's text size: the room above and below and a 28 dp
        /// line each, every one times <paramref name="textScale"/>; widths stay. Three lines stand 136 dp tall.
        /// </summary>
        public static float HeightDp(int lines, float textScale = 1f) => (2f * PadDp + Math.Max(0, lines) * RoomGeometry.LineDp) * textScale;

        /// <summary>
        /// What someone else wrote, ready for a label that reads no markup: each of the text's own lines by the one rule for
        /// outside text (<see cref="LabelText.ForTextMeshPro"/>: exactly what it says, white space as one space, a backslash
        /// shown as itself), the text's own line breaks kept, and lines of nothing left out so a part never spends a line on air.
        /// </summary>
        public static string Laid(string? words)
        {
            if (string.IsNullOrEmpty(words)) return "";
            var result = new StringBuilder(words!.Length);
            foreach (var line in words.Split(LineBreaks))
            {
                var plain = LabelText.ForTextMeshPro(line);
                if (plain.Length == 0) continue;
                if (result.Length > 0) result.Append('\n');
                result.Append(plain);
            }
            return result.ToString();
        }

        /// <summary>
        /// How much of the ground stands at a point <paramref name="x"/> from the band's left and <paramref name="y"/> from its
        /// top, 0 to 1: rising in a straight line over the outer 120 dp at each side and over <paramref name="edgeFade"/> at the
        /// top and foot, flat in the middle so every word sits on the full ground.
        /// </summary>
        public static float Ground(float x, float y, float width, float height, float edgeFade = EdgeFadeDp)
        {
            var across = Clamp01(Math.Min(x, width - x) / SideFadeDp);
            var down = Clamp01(Math.Min(y, height - y) / Math.Max(edgeFade, 1e-4f));
            return across * down;
        }

        /// <summary>
        /// The alpha that shows over a white wall, blended in linear light as the headset blends, what the page shows there
        /// with <paramref name="pageAlpha"/> mixed in sRGB: the binding case (BUILD-SPEC-v4 1.2). <paramref name="ground"/> is
        /// the ground's value in sRGB, 0 to 1. The page's 0.72 on #121416 comes to about 0.91.
        /// </summary>
        public static float LinearAlpha(float pageAlpha, float ground)
        {
            var mixed = 1f + (ground - 1f) * Clamp01(pageAlpha);
            return (1f - Decode(mixed)) / Math.Max(1f - Decode(ground), 1e-4f);
        }

        /// <summary>An sRGB value as linear light.</summary>
        private static float Decode(float value) => value <= 0.04045f ? value / 12.92f : (float)Math.Pow((value + 0.055f) / 1.055f, 2.4);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
