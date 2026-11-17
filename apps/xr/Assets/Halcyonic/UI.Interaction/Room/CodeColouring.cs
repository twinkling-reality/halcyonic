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
        /// A sheet colour as the glyph's vertices must carry it to be drawn as that colour: a glyph's vertex colour reaches the
        /// screen as a linear value in this linear project, so a sheet's sRGB colour written as is draws lifted toward white
        /// (keywords' #C9B8FF read #CFC7E6 in the parts' frames); converted, it draws as the sheet, under the row's own alpha.
        /// </summary>
        private static Color32 Sheet(byte r, byte g, byte b) => (Color32)((Color)new Color32(r, g, b, 0xFF)).linear;

        /// <summary>
        /// Halora (FIELD's code colours sheet, AUDIT's read, 2026-11-17): the brand's light, pale tints round the kingfisher:
        /// keywords violet #CFC1FF, types rose #F2BAD1, names cream #F3EBDC, strings sea glass #9EE6D8, numbers sky #A8D4FF,
        /// comments a steel #C3CAD4. Each is drawn at 4.7 to 1 or more on the glass in both rooms at a code row's own alpha
        /// (the sheet's keyword, type and comment lightened a little, their hue kept, to hold it). Every other run (punctuation
        /// and plain words) keeps the row's own white.
        /// </summary>
        public static readonly Palette Halora = kind => kind switch
        {
            CodeTokenKind.Keyword => Sheet(0xCF, 0xC1, 0xFF),
            CodeTokenKind.Type => Sheet(0xF2, 0xBA, 0xD1),
            CodeTokenKind.Name => Sheet(0xF3, 0xEB, 0xDC),
            CodeTokenKind.String => Sheet(0x9E, 0xE6, 0xD8),
            CodeTokenKind.Number => Sheet(0xA8, 0xD4, 0xFF),
            CodeTokenKind.Comment => Sheet(0xC3, 0xCA, 0xD4),
            _ => null,
        };

        /// <summary>
        /// High Contrast (the same sheet): brighter colours set further apart, each drawn at 4.7 to 1 or more on the glass in both
        /// rooms at a code row's own alpha: keywords #D3C0FF, types #FFB8E0, names white, strings #7FF2E0, numbers #99CFFF,
        /// comments #C9D0DA. Every other run keeps the row's own white.
        /// </summary>
        public static readonly Palette HighContrast = kind => kind switch
        {
            CodeTokenKind.Keyword => Sheet(0xD3, 0xC0, 0xFF),
            CodeTokenKind.Type => Sheet(0xFF, 0xB8, 0xE0),
            CodeTokenKind.Name => Sheet(0xFF, 0xFF, 0xFF),
            CodeTokenKind.String => Sheet(0x7F, 0xF2, 0xE0),
            CodeTokenKind.Number => Sheet(0x99, 0xCF, 0xFF),
            CodeTokenKind.Comment => Sheet(0xC9, 0xD0, 0xDA),
            _ => null,
        };

        /// <summary>Each code theme's palette (SCREENS' CodeThemes names the choice): Halora, Plain (today's white) and High Contrast.</summary>
        public static readonly Dictionary<CodeTheme, Palette> Palettes = new Dictionary<CodeTheme, Palette>
        {
            [CodeTheme.Halora] = Halora,
            [CodeTheme.Plain] = Plain,
            [CodeTheme.HighContrast] = HighContrast,
        };

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
