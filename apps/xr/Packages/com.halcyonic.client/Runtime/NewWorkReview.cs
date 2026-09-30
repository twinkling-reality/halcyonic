#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

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

        public NewWorkReview(string project, string runtime, string model, string modelFacts, string modelRef, string objective)
        {
            var lines = new List<string>();
            Add(lines, "Project: " + Safe(project));
            Add(lines, "Runtime: " + Safe(runtime));
            Add(lines, "Model: " + Safe(model));
            Add(lines, "Model location: " + Safe(modelFacts));
            Add(lines, "Model reference: " + Safe(modelRef));
            Add(lines, "Objective:");
            Add(lines, Safe(objective));
            for (var index = 0; index < lines.Count; index += PageLines)
            {
                pages.Add(string.Join("\n", lines.Skip(index).Take(PageLines)));
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

        private static string Safe(string value) => LabelText.Plain(value
            .Replace("\r", " ‹carriage return› ")
            .Replace("\n", " ‹line break› ")
            .Replace("\t", " ‹tab› "));

        private static void Add(List<string> lines, string text)
        {
            if (text.Length == 0)
            {
                lines.Add("");
                return;
            }
            for (var index = 0; index < text.Length;)
            {
                var length = Math.Min(LineCharacters, text.Length - index);
                if (index + length < text.Length && char.IsHighSurrogate(text[index + length - 1])) length--;
                lines.Add(text.Substring(index, length));
                index += length;
            }
        }
    }
}
