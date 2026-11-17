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

        /// <summary>Where leading folders are left out of a long path: the folders that remain follow it.</summary>
        public const string Elision = "\u2026/";

        /// <summary>
        /// A step's act and its file's path in at most <paramref name="mostLines"/> lines (the lead's 2026-11-17T02:03Z): the act
        /// (<paramref name="lead"/>, "Editing ") first, then the path breaking only after a slash; where it still takes more
        /// lines, its leading folders give way to <see cref="Elision"/> one at a time, before layout, and the file's name stays
        /// whole. A name too long for one line breaks only at its own word boundaries (a dot, a hyphen, an underscore, or a
        /// small letter before a capital), never inside a word. Returns the lines and the path as shown.
        /// </summary>
        public static (IReadOnlyList<string> Lines, string Shown) Fold(string lead, string path, Func<string, bool> fits, int mostLines = 3)
        {
            var folders = new List<string>();
            var start = 0;
            for (var index = 0; index < path.Length; index++)
            {
                if (path[index] != '/') continue;
                folders.Add(path.Substring(start, index + 1 - start));
                start = index + 1;
            }
            var name = path.Substring(start);
            var nameParts = name.Length == 0 || fits(name) ? new List<string> { name } : NameParts(name);
            (IReadOnlyList<string> Lines, string Shown) laid = (new string[0], path);
            for (var drop = 0; drop <= folders.Count; drop++)
            {
                var pieces = new List<string>();
                if (drop > 0) pieces.Add(Elision);
                for (var at = drop; at < folders.Count; at++) pieces.Add(folders[at]);
                pieces.AddRange(nameParts);
                laid = (Greedy(lead, pieces, fits), string.Concat(pieces));
                if (laid.Lines.Count <= mostLines) return laid;
            }
            return laid;
        }

        /// <summary>A file's name at its own word boundaries: after a dot that is not its first or last character, then <see cref="FinerPieces"/>.</summary>
        private static List<string> NameParts(string name)
        {
            var parts = new List<string>();
            var start = 0;
            for (var index = 1; index < name.Length - 1; index++)
            {
                if (name[index] != '.') continue;
                parts.AddRange(FinerPieces(name.Substring(start, index + 1 - start)));
                start = index + 1;
            }
            parts.AddRange(FinerPieces(name.Substring(start)));
            return parts;
        }

        /// <summary>The lead, then the pieces, as many a line as <paramref name="fits"/> holds; a piece never splits.</summary>
        private static IReadOnlyList<string> Greedy(string lead, IReadOnlyList<string> pieces, Func<string, bool> fits)
        {
            var lines = new List<string>();
            var line = lead;
            foreach (var piece in pieces)
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
