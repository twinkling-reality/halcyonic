#nullable enable
using System.Globalization;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// The one rule for showing text Halcyonic did not write, so a label shows it literally and
    /// completely: workstream titles and objectives, anything an agent or a tool wrote (messages,
    /// approval requests, activity), refusals and failures, setup problems carrying exception text,
    /// what the understanding and evaluation sources say, and names that come from runtimes. Nothing
    /// in such text is interpreted, and nothing in it is hidden.
    /// </summary>
    /// <remarks>
    /// TextMeshPro interprets text even with rich text off
    /// (docs/internal/validation/workspace-interaction.md): it turns a backslash, "u" and four hex
    /// digits, or "U" and eight, into that character whatever its settings, and a backslash with n,
    /// r, t, v or another backslash into a control character while its escape parsing is on; the
    /// end of text character U+0003, typed or escaped, ends the label there, so everything after it
    /// is dropped without an ellipsis; a carriage return goes back to the start of the line, so what
    /// follows is drawn over it; line feeds, vertical tabs and the line and paragraph separators
    /// break the line; and zero width, joiner and bidirectional control characters draw nothing, or
    /// a mark over their neighbours, with no reordering. Unity's TextMesh parses no escapes, but
    /// breaks and tabs the same way. So:
    /// <list type="bullet">
    /// <item>
    /// Whitespace that holds a line break, a tab or any other white space character than the space
    /// collapses into one space; spaces alone stay as written; none at either end.
    /// </item>
    /// <item>
    /// Every other control character, every format character, every character Unicode says to
    /// display as nothing (default ignorable: a zero width space, a bidirectional override, a
    /// variation selector, a tag character), and every surrogate without its pair shows as its code
    /// point, for example ‹U+202E›: what cannot be seen is still read, and what makes two commands
    /// differ always shows.
    /// </item>
    /// <item>
    /// Every character of the Private Use Areas shows as its code point too, as ‹U+E769›: they mean
    /// only what a font makes of them, and Halcyonic's own icons are drawn from them (ADR 0023), so
    /// text from outside can never draw one of Halcyonic's icons among its words.
    /// </item>
    /// <item>
    /// Everything else shows as it is, markup and backslashes included: labels never interpret
    /// markup, and <see cref="ForTextMeshPro"/> doubles every backslash for a label with escape
    /// parsing on, which shows a doubled backslash as one.
    /// </item>
    /// </list>
    /// Which characters show by code is fixed here, from Unicode 17.0's control and format characters
    /// and default ignorable code points, so the headset and the tests decide alike whatever Unicode
    /// version their runtime knows. Characters that merely look alike, such as a Cyrillic letter for
    /// a Latin one, or a no-break space for a space, show as they look.
    /// </remarks>
    public static class LabelText
    {
        /// <summary>Unicode's White_Space characters, as ranges of first and last code point.</summary>
        private static readonly int[] WhiteSpace =
        {
            0x0009, 0x000D,
            0x0020, 0x0020,
            0x0085, 0x0085,
            0x00A0, 0x00A0,
            0x1680, 0x1680,
            0x2000, 0x200A,
            0x2028, 0x2029,
            0x202F, 0x202F,
            0x205F, 0x205F,
            0x3000, 0x3000,
        };

        /// <summary>
        /// Control characters that are not white space, format characters, default ignorable code points
        /// (Unicode 17.0), the braille blank, which draws nothing, and noncharacters, which nothing draws and
        /// normalizing can refuse, as ranges of
        /// first and last code point; planes 15 and 16 are in <see cref="PrivateUse"/>.
        /// </summary>
        private static readonly int[] ShownByCode =
        {
            0x0000, 0x0008,
            0x000E, 0x001F,
            0x007F, 0x0084,
            0x0086, 0x009F,
            0x00AD, 0x00AD,
            0x034F, 0x034F,
            0x0600, 0x0605,
            0x061C, 0x061C,
            0x06DD, 0x06DD,
            0x070F, 0x070F,
            0x0890, 0x0891,
            0x08E2, 0x08E2,
            0x115F, 0x1160,
            0x17B4, 0x17B5,
            0x180B, 0x180F,
            0x200B, 0x200F,
            0x202A, 0x202E,
            0x2060, 0x206F,
            0x2800, 0x2800,
            0x3164, 0x3164,
            0xFDD0, 0xFDEF,
            0xFE00, 0xFE0F,
            0xFEFF, 0xFEFF,
            0xFFA0, 0xFFA0,
            0xFFF0, 0xFFFB,
            0xFFFE, 0xFFFF,
            0x110BD, 0x110BD,
            0x110CD, 0x110CD,
            0x13430, 0x1343F,
            0x1BCA0, 0x1BCA3,
            0x1D173, 0x1D17A,
            0x1FFFE, 0x1FFFF,
            0x2FFFE, 0x2FFFF,
            0x3FFFE, 0x3FFFF,
            0x4FFFE, 0x4FFFF,
            0x5FFFE, 0x5FFFF,
            0x6FFFE, 0x6FFFF,
            0x7FFFE, 0x7FFFF,
            0x8FFFE, 0x8FFFF,
            0x9FFFE, 0x9FFFF,
            0xAFFFE, 0xAFFFF,
            0xBFFFE, 0xBFFFF,
            0xCFFFE, 0xCFFFF,
            0xDFFFE, 0xDFFFF,
            0xE0000, 0xE0FFF,
            0xEFFFE, 0xEFFFF,
        };

        /// <summary>
        /// The Private Use Areas, as ranges of first and last code point: the one in the Basic
        /// Multilingual Plane, and planes 15 and 16 whole, the noncharacters at their ends included.
        /// </summary>
        private static readonly int[] PrivateUse =
        {
            0xE000, 0xF8FF,
            0xF0000, 0x10FFFF,
        };

        /// <summary>
        /// Text as one line of exactly what it says: line breaks, tabs and other white space as one
        /// space with the whitespace around them, spaces as written, none at either end, and every
        /// character that would not show as itself, or could show as one of Halcyonic's icons, as its
        /// code point, such as ‹U+200B›. Applying it again changes nothing. A label of several lines
        /// shows each line through it.
        /// </summary>
        public static string Plain(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var result = new StringBuilder(text!.Length);
            // Whitespace waiting for what follows it: how much, and whether it is more than spaces.
            var spaces = 0;
            var collapse = false;
            for (var index = 0; index < text.Length; index++)
            {
                var unit = text[index];
                var paired = char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
                var codePoint = paired ? char.ConvertToUtf32(unit, text[index + 1]) : unit;
                if (In(WhiteSpace, codePoint))
                {
                    if (result.Length == 0) continue;
                    spaces++;
                    collapse |= unit != ' ';
                    continue;
                }
                if (spaces > 0) result.Append(' ', collapse ? 1 : spaces);
                spaces = 0;
                collapse = false;
                if (In(ShownByCode, codePoint) || In(PrivateUse, codePoint) || (!paired && char.IsSurrogate(unit)))
                {
                    result.Append("‹U+").Append(codePoint.ToString("X4", CultureInfo.InvariantCulture)).Append('›');
                }
                else
                {
                    result.Append(text, index, paired ? 2 : 1);
                }
                if (paired) index++;
            }
            return result.ToString();
        }

        /// <summary>
        /// A name from outside, as a folder's or a project's, by <see cref="Plain"/>'s rule, which never
        /// leaves it empty: a name of nothing but white space shows each of its characters as its code
        /// point, such as ‹U+0020›, so a blank name is still something to see and to choose, and never
        /// mistaken for another. Null or empty stays empty.
        /// </summary>
        public static string Name(string? text)
        {
            var plain = Plain(text);
            if (plain.Length > 0 || string.IsNullOrEmpty(text)) return plain;
            var result = new StringBuilder();
            for (var index = 0; index < text!.Length; index++)
            {
                var unit = text[index];
                var paired = char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
                var codePoint = paired ? char.ConvertToUtf32(unit, text[index + 1]) : unit;
                result.Append("‹U+").Append(codePoint.ToString("X4", CultureInfo.InvariantCulture)).Append('›');
                if (paired) index++;
            }
            return result.ToString();
        }

        /// <summary>
        /// Text whose every character can change what happens, such as a command a person approves,
        /// by <see cref="Plain"/>'s rule, except that no white space but the space collapses: a line
        /// break, a tab or any other white space character shows as its code point, such as ‹U+000A›,
        /// so two commands on two lines never read as one, and a tab or a no-break space never reads
        /// as a space. Applying it again changes nothing.
        /// </summary>
        public static string Exact(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var result = new StringBuilder(text!.Length);
            foreach (var unit in text)
            {
                if (unit != ' ' && In(WhiteSpace, unit))
                {
                    result.Append("‹U+").Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture)).Append('›');
                }
                else
                {
                    result.Append(unit);
                }
            }
            return Plain(result.ToString());
        }

        /// <summary>
        /// <see cref="Plain"/> text for a TextMeshPro label with rich text off and escape parsing on:
        /// every backslash doubled, since such a label shows a doubled backslash as one and turns
        /// every other backslash sequence into another character. Set it on the label exactly once.
        /// </summary>
        public static string ForTextMeshPro(string? text) => Plain(text).Replace("\\", "\\\\");

        private static bool In(int[] ranges, int codePoint)
        {
            for (var index = 0; index < ranges.Length; index += 2)
            {
                if (codePoint < ranges[index]) return false;
                if (codePoint <= ranges[index + 1]) return true;
            }
            return false;
        }
    }
}
