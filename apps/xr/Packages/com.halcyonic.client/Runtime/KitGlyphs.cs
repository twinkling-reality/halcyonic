#nullable enable
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The accented Latin letters every kit face holds beside printable ASCII (the lead, 2026-11-17T22:10Z): the Latin-1 Supplement's
    /// printable characters and Latin Extended-A, so a name like "Café", "señor" or "Łódź" draws in its own face, never by its code
    /// point. The atlas builders and the review's code point marks read this one list.
    /// </summary>
    public static class KitGlyphs
    {
        /// <summary>
        /// U+00A0 to U+00FF, then U+0100 to U+017F, without the soft hyphen, which draws nothing, and ŉ (U+0149), which Unicode
        /// deprecates and Inter SemiBold does not draw (the trial rebuild, 2026-11-17T22:19Z); it shows by its code point.
        /// </summary>
        public static readonly string Latin = new string(Enumerable.Range(0xA0, 0x180 - 0xA0).Where(HoldsLatin).Select(code => (char)code).ToArray());

        /// <summary>Whether a code point is one of <see cref="Latin"/>.</summary>
        public static bool HoldsLatin(int codePoint) => codePoint >= 0xA0 && codePoint < 0x180 && codePoint != 0xAD && codePoint != 0x149;
    }
}
