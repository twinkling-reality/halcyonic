#nullable enable
using System;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// The agent's own words on the faded band low in the view (DESIGN's BUILD-SPEC-v4 1.6): the band's numbers, the words
    /// made ready to lay, and their paging. The band holds only what the agent said, or its question's prompt, in the one type
    /// size, at most <see cref="LinesAPart"/> lines a part; longer words page by parts, each but the last ending at a sentence's
    /// end where one falls in its lines (AUDIT 2026-11-16 11:29Z), the way on "Next Part" alone, and where the person is, "Part 1
    /// of 2", the band's own quiet foot (AUDIT2's M2, 12:22Z). The lines are counted where they are laid (the view lays them at
    /// the measure and at the person's text size, and says where each part starts); this says which of them a part holds.
    /// Lengths are Meta's dp, 16 a degree at the eye.
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

        /// <summary>The ground's colour and its alpha as the page mixes it, in sRGB: the kit's one glass, <see cref="AskGlass"/>, #0C0E16 at 0.72.</summary>
        public const int GroundColour = AskGlass.PageColour;

        public const float GroundAlpha = AskGlass.PageAlpha;

        /// <summary>Every word's tracking, in hundredths of an em: -0.6 percent (BUILD-SPEC-v4 1.3).</summary>
        public const float Tracking = -0.6f;

        /// <summary>Every character that ends a line: the line feed and carriage return, the vertical tab, the form feed, the next line, and the line and paragraph separators.</summary>
        private static readonly char[] LineBreaks = { '\n', '\r', (char)0x0B, (char)0x0C, (char)0x85, (char)0x2028, (char)0x2029 };

        /// <summary>
        /// The foot's count of where the person is, "Part 1 of 2": its type and its line, in dp. Two steps under the band's
        /// words, and no smaller: laid low on the band it is seen foreshortened, and 15 dp read 0.754 degrees there (XR's kit on
        /// e15bb4c2f), under 14 dp; 18 holds it.
        /// </summary>
        public const float CountTypeDp = 18f;

        public const float CountLineDp = 23f;

        private readonly int[] starts;

        private RoomWords(int lines, int part, int[]? partStarts)
        {
            Lines = Math.Max(0, lines);
            starts = partStarts != null && partStarts.Length > 0 && Lines > 0 ? partStarts : EveryThree(Lines);
            Parts = Lines == 0 ? 0 : starts.Length;
            Part = Math.Max(0, Math.Min(part, Parts - 1));
        }

        private static int[] EveryThree(int lines)
        {
            var parts = (lines + LinesAPart - 1) / LinesAPart;
            var result = new int[parts];
            for (var index = 0; index < parts; index++) result[index] = index * LinesAPart;
            return result;
        }

        /// <summary>The lines the words take as laid, every part's together.</summary>
        public int Lines { get; }

        /// <summary>How many parts the lines make, three lines each; none for no words.</summary>
        public int Parts { get; }

        /// <summary>The part that shows, from 0.</summary>
        public int Part { get; }

        /// <summary>The part's first line among the laid ones, and how many lines it holds, one to three.</summary>
        public int First => Parts == 0 ? 0 : starts[Part];

        public int Count => Parts == 0 ? 0 : Math.Max(0, Math.Min(LinesAPart, (Part + 1 < Parts ? starts[Part + 1] : Lines) - First));

        /// <summary>Whether a later part waits.</summary>
        public bool More => Part + 1 < Parts;

        /// <summary>
        /// The way on, alone in its words, its place kept on every part: "Next Part", and on the last part "First Part", back to
        /// the start; null where the words are one part.
        /// </summary>
        public string? Next => Parts < 2 ? null : More ? EntryText.NextPartLabel : FirstPartLabel;

        /// <summary>The way back to the first part, on the last.</summary>
        public const string FirstPartLabel = "First Part";

        /// <summary>
        /// Where the person is, the band's own quiet foot (AUDIT2's M2: "2 of 2" under Next Part read as already showing): "Part 1
        /// of 2", or null where the words are one part. The way on carries no count.
        /// </summary>
        public string? Where => Parts < 2 ? null : "Part " + EntryText.PartOf(Part + 1, Parts);

        /// <summary>
        /// The paging of <paramref name="lines"/> laid lines at <paramref name="part"/>, held inside the parts there are: three
        /// lines a part, or each part from the line <paramref name="partStarts"/> gives, as the view laid them at sentence ends.
        /// </summary>
        public static RoomWords Of(int lines, int part = 0, int[]? partStarts = null) => new RoomWords(lines, part, partStarts);

        /// <summary>The next part, or this one where it is the last.</summary>
        public RoomWords Following() => More ? new RoomWords(Lines, Part + 1, starts) : this;

        /// <summary>The first part again, as the last part's way on turns to.</summary>
        public RoomWords Start() => new RoomWords(Lines, 0, starts);

        /// <summary>
        /// Where sentences end in <paramref name="text"/>: the place just after each stop, question mark or exclamation mark
        /// that a space or the text's end follows, past any closing quote or bracket after it (AUDIT2 2026-11-16 11:29Z). A stop
        /// inside a word never ends one (index.html, 1.2.3, src/game/kite.js), and neither does a short abbreviation's (e.g.,
        /// i.e., vs., Dr.).
        /// </summary>
        public static int[] SentenceEnds(string text)
        {
            var ends = new System.Collections.Generic.List<int>();
            for (var index = 0; index < text.Length; index++)
            {
                var mark = text[index];
                if (mark != '.' && mark != '?' && mark != '!') continue;
                var after = index + 1;
                while (after < text.Length && (text[after] == '"' || text[after] == '\'' || text[after] == ')' || text[after] == ']'
                    || text[after] == (char)0x201D || text[after] == (char)0x2019)) after++;
                if (after < text.Length && !char.IsWhiteSpace(text[after])) continue;
                if (mark == '.' && Abbreviated(text, index)) continue;
                ends.Add(after);
            }
            return ends.ToArray();
        }

        private static readonly string[] Abbreviations = { "e.g", "i.e", "vs", "cf", "dr", "mr", "mrs", "ms", "approx", "fig" };

        /// <summary>Whether the stop at <paramref name="stop"/> closes a short abbreviation rather than a sentence.</summary>
        private static bool Abbreviated(string text, int stop)
        {
            var start = stop;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] != '(') start--;
            var word = text.Substring(start, stop - start).ToLowerInvariant();
            foreach (var each in Abbreviations) if (word == each) return true;
            // "No. 5" is a number's abbreviation; "The answer was no." ends a sentence (AUDIT2 2026-11-16 12:50Z).
            if (word == "no")
            {
                var next = stop + 1;
                while (next < text.Length && text[next] == ' ') next++;
                return next < text.Length && char.IsDigit(text[next]);
            }
            return false;
        }

        /// <summary>
        /// The band's height with <paramref name="lines"/> lines at the person's text size: the room above and below and a 28 dp
        /// line each, every one times <paramref name="textScale"/>; widths stay. Three lines stand 136 dp tall.
        /// </summary>
        public static float HeightDp(int lines, float textScale = 1f) => (2f * PadDp + Math.Max(0, lines) * RoomGeometry.LineDp) * textScale;

        /// <summary>
        /// How far the band reaches under the pair's foot line at its tallest, in dp at <paramref name="textScale"/>: from the foot
        /// line down to its top (<see cref="TopPitch"/>, where it always hangs) and then a part's three lines with their room. It
        /// stands <see cref="WidthDp"/> wide, centred under the pair. What the plane's footprint holds under the bar, so a shelf
        /// never stands under the band (JUDGE's charR, 2026-11-13T2228Z: the bar's record alone missed it).
        /// </summary>
        public static float UnderFootMostDp(float textScale = 1f) => (RoomGeometry.FeetPitch - TopPitch) * RoomGeometry.DpPerDegree + MostHeightDp(textScale);

        /// <summary>
        /// The band's height at its tallest: a part's three lines with their room, and the count's foot line where the last line
        /// of a part would run under the count (AUDIT 2026-11-16 12:23Z: the band never grows between parts).
        /// </summary>
        public static float MostHeightDp(float textScale = 1f) => HeightDp(LinesAPart, textScale) + CountLineDp * textScale;

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
        /// the ground's value in sRGB, 0 to 1. The page's 0.72 on #0C0E16 comes to about 0.92.
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
