#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where a file's path may break across lines (the lead's finish item, 2026-11-16 06:09Z: "welcome.uikitm" / "l" is never
    /// drawn): only after a slash or a dot, never inside a name. The lines are laid greedily: as many whole pieces on a line as
    /// <c>fits</c> holds; a piece no line holds stands alone on its own line, whole, never cut (the view shrinks it, it does not
    /// split it). The Unity layer lays the lines; this only says where they break.
    /// </summary>
    public static class PathWrap
    {
        /// <summary>The path's pieces, each ending in its slash or dot where it has one: "src/", "welcome.", "uikitml".</summary>
        public static IReadOnlyList<string> Pieces(string path)
        {
            var pieces = new List<string>();
            if (string.IsNullOrEmpty(path)) return pieces;
            var start = 0;
            for (var index = 0; index < path.Length; index++)
            {
                // A dot that starts a name (".env") or stands alone is part of the name, never a place to break before it.
                if (path[index] == '/' || (path[index] == '.' && index > start && index + 1 < path.Length))
                {
                    pieces.Add(path.Substring(start, index + 1 - start));
                    start = index + 1;
                }
            }
            if (start < path.Length) pieces.Add(path.Substring(start));
            return pieces;
        }

        /// <summary>
        /// A piece no line holds even at the body minimum, in finer parts (AUDIT's condition, 2026-11-16 06:15Z; FIELD's view asks
        /// for them only then): after a hyphen or an underscore, or before a capital that follows a small letter
        /// ("welcomeScreen" to "welcome", "Screen"); never inside letters. A piece with none of them stays whole.
        /// </summary>
        public static IReadOnlyList<string> FinerPieces(string piece)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(piece)) return parts;
            var start = 0;
            for (var index = 1; index < piece.Length; index++)
            {
                var before = piece[index - 1];
                var breaksAfter = (before == '-' || before == '_') && index - 1 > start;
                var breaksBefore = char.IsUpper(piece[index]) && char.IsLower(before);
                if (breaksAfter || breaksBefore)
                {
                    parts.Add(piece.Substring(start, index - start));
                    start = index;
                }
            }
            parts.Add(piece.Substring(start));
            return parts;
        }

        /// <summary>
        /// The path in lines, each breaking only after a slash or a dot, as many pieces a line as <paramref name="fits"/> holds.
        /// A path that fits whole is one line.
        /// </summary>
        public static IReadOnlyList<string> Lines(string path, Func<string, bool> fits)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(path)) return lines;
            if (fits(path))
            {
                lines.Add(path);
                return lines;
            }
            var line = "";
            foreach (var piece in Pieces(path))
            {
                if (line.Length > 0 && !fits(line + piece))
                {
                    lines.Add(line);
                    line = "";
                }
                line += piece;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }
    }
}
