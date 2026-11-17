#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Code's colours on a drawn row (the lead's 2026-11-17T03:36Z; AUDIT3's C1 to C5): the shared highlighter names the
    /// row's runs and the palette gives each kind its colour, painted on the row's vertices through GlazeRuns, never a tag
    /// and never a change to the text. A row is coloured again only when its text, language or palette changes; a palette
    /// switch repaints without laying the words again.
    /// </summary>
    public static class CodeColouring
    {
        /// <summary>A palette: each kind's colour, or null where the run keeps the row's own (Plain keeps every run's own).</summary>
        public delegate Color32? Palette(CodeTokenKind kind);

        /// <summary>Plain: today's look, every run in the row's own colour.</summary>
        public static readonly Palette Plain = _ => null;

        private sealed class Laid
        {
            public string Text = "";
            public CodeLanguage Language;
            public Palette? Palette;
            public CodeCarry CarryIn;
            public CodeCarry CarryOut;
            public IReadOnlyList<(int Start, int End, Color32 Colour)> Ranges = Array.Empty<(int, int, Color32)>();
        }

        /// <summary>
        /// Each code theme's palette (SCREENS' CodeThemes names the choice; the colours are FIELD's, who adds Halora and High
        /// Contrast here). A theme with no palette yet draws Plain, never a guess.
        /// </summary>
        public static readonly Dictionary<CodeTheme, Palette> Palettes = new Dictionary<CodeTheme, Palette> { [CodeTheme.Plain] = Plain };

        /// <summary>The palette in force: the person's choice (CodeThemes.Current); renders and checks may set another.</summary>
        public static Func<Palette> Now { get; set; } = () => Palettes.TryGetValue(CodeThemes.Current, out var palette) ? palette : Plain;

        /// <summary>Held weakly, so a destroyed or pooled-away row is never kept alive here (AUDIT3's CC-b).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Laid> laid = new System.Runtime.CompilerServices.ConditionalWeakTable<TMP_Text, Laid>();

        /// <summary>
        /// Colours <paramref name="row"/>, whose shown text is <paramref name="text"/>, in <paramref name="language"/> by
        /// <paramref name="palette"/>; <paramref name="carry"/> is what the row before left open and comes back as this
        /// row's. Nothing is worked out again for a row whose text, language, carry and palette are as they were.
        /// </summary>
        public static void Colour(TMP_Text row, string text, CodeLanguage language, Palette palette, ref CodeCarry carry)
        {
            if (laid.TryGetValue(row, out var was) && was.Text == text && was.Language == language && was.Palette == palette && was.CarryIn == carry
                && ReferenceEquals(GlazeRuns.Of(row).Painted, was.Ranges))
            {
                carry = was.CarryOut;
                return;
            }
            var carryIn = carry;
            var runs = CodeTokens.Of(text, language, ref carry);
            var ranges = new List<(int Start, int End, Color32 Colour)>(runs.Count);
            foreach (var run in runs)
                if (palette(run.Kind) is Color32 colour) ranges.Add((run.Start, run.Start + run.Length, colour));
            GlazeRuns.Of(row).Paint(ranges);
            if (was == null) laid.Add(row, was = new Laid());
            was.Text = text;
            was.Language = language;
            was.Palette = palette;
            was.CarryIn = carryIn;
            was.CarryOut = carry;
            was.Ranges = ranges;
        }

        /// <summary>Forgets a row's colours, so a reused label is worked out afresh.</summary>
        public static void Forget(TMP_Text row) => laid.Remove(row);
    }
}
