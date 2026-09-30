#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>
    /// The complete request shown before the headset creates or starts work. Every page has a fixed
    /// number of short lines, so the Unity label has no reason to shorten one with an ellipsis.
    /// </summary>
    public sealed class NewWorkReview
    {
        public const int LineCharacters = 24;
        private const int PageLines = 12;
        private readonly List<string> pages = new List<string>();
        private readonly int lineCharacters;

        /// <param name="lineCharacters">The longest line, in ASCII characters, that the panel draws in full.</param>
        /// <param name="pageLines">How many lines a page holds.</param>
        /// <param name="folder">Where the project's files will live, in words, or null when it is not part of the request.</param>
        /// <param name="folderBefore">
        /// An existing project's folder now, when the request moves it to <paramref name="folder"/>:
        /// both show, since all later work in the project runs in the new one.
        /// </param>
        public NewWorkReview(string project, string title, string runtime, string model, string modelFacts, string modelRef, string objective,
            int lineCharacters = LineCharacters, int pageLines = PageLines, string? folder = null, string? folderBefore = null)
        {
            if (lineCharacters < 12) throw new ArgumentOutOfRangeException(nameof(lineCharacters), lineCharacters, "A line holds at least 12 characters.");
            if (pageLines < 1) throw new ArgumentOutOfRangeException(nameof(pageLines), pageLines, "A page holds at least one line.");
            this.lineCharacters = lineCharacters;
            var lines = new List<string>();
            Add(lines, "Project: " + Safe(project));
            if (folderBefore != null && folder != null)
            {
                Add(lines, "Folder now: " + Safe(folderBefore));
                Add(lines, "Folder from now on: " + Safe(folder));
            }
            else if (folder != null) Add(lines, "Where its files live: " + Safe(folder));
            Add(lines, "Workstream title: " + Safe(title));
            Add(lines, "Runtime: " + Safe(runtime));
            Add(lines, "Model: " + Safe(model));
            Add(lines, "Model location: " + Safe(modelFacts));
            Add(lines, "Model reference: " + Safe(modelRef));
            Add(lines, "Objective:");
            Add(lines, Safe(objective));
            for (var index = 0; index < lines.Count; index += pageLines)
            {
                pages.Add(string.Join("\n", lines.Skip(index).Take(pageLines)));
            }
        }

        public int Page { get; private set; }

        public int PageCount => pages.Count;

        public string Text => pages[Page];

        public bool CanConfirm => Page == PageCount - 1;

        public void Next()
        {
            if (Page + 1 < PageCount) Page++;
        }

        public void Previous()
        {
            if (Page > 0) Page--;
        }

        public IReadOnlyList<string> Pages => pages;

        // The bundled TMP font cannot show every character. Spell each non-ASCII code point and
        // control character in ASCII, and double a literal backslash so a typed marker differs.
        private static string Safe(string value)
        {
            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var unit = value[index];
                var paired = char.IsHighSurrogate(unit) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
                var codePoint = paired ? char.ConvertToUtf32(unit, value[index + 1]) : unit;
                if (unit == '\\') result.Append("\\\\");
                else if (codePoint >= 0x20 && codePoint <= 0x7E) result.Append(unit);
                else result.Append("\\u{").Append(codePoint.ToString("X", CultureInfo.InvariantCulture)).Append('}');
                if (paired) index++;
            }
            return result.ToString();
        }

        private void Add(List<string> lines, string text)
        {
            if (text.Length == 0)
            {
                lines.Add("");
                return;
            }
            var line = new StringBuilder(lineCharacters);
            for (var index = 0; index < text.Length;)
            {
                var length = 1;
                if (text[index] == '\\' && index + 1 < text.Length)
                {
                    if (text[index + 1] == '\\') length = 2;
                    else if (text[index + 1] == 'u' && index + 2 < text.Length && text[index + 2] == '{')
                    {
                        var end = text.IndexOf('}', index + 3);
                        if (end >= 0) length = end + 1 - index;
                    }
                }
                if (line.Length + length > lineCharacters)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                line.Append(text, index, length);
                index += length;
            }
            if (line.Length > 0) lines.Add(line.ToString());
        }
    }
}
