#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Recorded output keeps LF and space indentation; other controls use the existing literal text rule.</summary>
    public static class RecordedResultText
    {
        public static LineWords Body(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                // Visible bookends prevent Exact's label trimming from losing indentation or trailing spaces.
                // They are removed only after the shared control/bidi/private-use policy has run.
                var shown = LabelText.Exact("x" + lines[index] + "x");
                lines[index] = shown.Substring(1, shown.Length - 2);
            }
            return LineWords.Shown(string.Join("\n", lines));
        }
    }
}
