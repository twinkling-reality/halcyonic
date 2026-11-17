#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// How code is coloured in every panel that shows it (the owner's ask, the lead's 2026-11-17T03:36Z): Halora, the default, soft
    /// colours for each kind of code; Plain, all code in white; High Contrast, stronger colours. The person's
    /// choice on Settings › Appearance › Code Colours, kept on the device beside the appearance; it changes no work, so no journal event.
    /// The colours themselves are FIELD's palette, drawn by the shared highlighter; this names the choice.
    /// </summary>
    public enum CodeTheme
    {
        Halora,
        Plain,
        HighContrast,
    }

    /// <summary>The code theme in force, read by every code view; set at start from the kept choice, while previewing, and on apply.</summary>
    public static class CodeThemes
    {
        /// <summary>The page's and the row's name (AUDIT2's 03:36Z): British "Colours", as "Character Colour".</summary>
        public const string Title = "Code Colours";

        public const string PreviewWords = "Preview Colours";

        /// <summary>The preview card's label over the theme's name (the lead's 18:47Z; AUDIT2's 18:52Z: one word of ours over the choice).</summary>
        public const string PreviewLabel = "Preview";
        public const string ApplyWords = "Apply Colours";

        public static readonly CodeTheme[] All = { CodeTheme.Halora, CodeTheme.Plain, CodeTheme.HighContrast };

        public static CodeTheme Current { get; private set; } = CodeTheme.Halora;

        /// <summary>Raised once per change, never for the theme already in force: code views recolour without laying their rows again.</summary>
        public static event Action<CodeTheme>? Changed;

        public static void Set(CodeTheme theme)
        {
            if (Current == theme) return;
            Current = theme;
            Changed?.Invoke(theme);
        }

        /// <summary>Each choice's name (AUDIT2's 03:36Z).</summary>
        public static string Words(CodeTheme theme) => theme switch
        {
            CodeTheme.Halora => "Halora",
            CodeTheme.Plain => "Plain",
            CodeTheme.HighContrast => "High Contrast",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "a code theme of no known kind"),
        };

        /// <summary>The plain line under each choice (AUDIT2's 03:36Z).</summary>
        public static string Line(CodeTheme theme) => theme switch
        {
            CodeTheme.Halora => "Soft colours for each kind of code.",
            CodeTheme.Plain => "All code in white.",
            CodeTheme.HighContrast => "Brighter colours, set further apart.",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "a code theme of no known kind"),
        };

        /// <summary>The kept key, exact: "halora", "plain" or "high-contrast".</summary>
        public static string Key(CodeTheme theme) => theme switch
        {
            CodeTheme.Halora => "halora",
            CodeTheme.Plain => "plain",
            CodeTheme.HighContrast => "high-contrast",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "a code theme of no known kind"),
        };

        /// <summary>The theme a kept key names; null for any other text, which the caller reads as the default.</summary>
        public static CodeTheme? Find(string? key) => key switch
        {
            "halora" => CodeTheme.Halora,
            "plain" => CodeTheme.Plain,
            "high-contrast" => CodeTheme.HighContrast,
            _ => null,
        };

        /// <summary>
        /// The example the preview draws in the chosen colours (AUDIT2's 03:36Z): real TypeScript, a few lines, with a keyword, a
        /// string, a number and a comment, and no claim about the product in its comment.
        /// </summary>
        public static readonly string[] Example =
        {
            "// Show at most three tasks",
            "const shown = tasks.slice(0, 3);",
            "console.log(\"Shown:\", shown);",
        };
    }
}
