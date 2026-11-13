#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// What the headset draws for an agent app, a model and the host while a development build's capture extra asks for
    /// footage free of product names (the competition's rules; lead 11-04, DEMO's shot list). Where an understanding or a
    /// measurement came from is not this switch's: the headset names it in its own words in every build
    /// (<see cref="SourceWords"/>; the lead's ruling, 2026-11-13).
    /// Off by default. Only the development build's capture presets turn it on: <c>UseGeneric</c> is compiled into
    /// development builds, the editor and the test build alone (<c>WorkspaceRender.CapturePresetsCheck</c> proves a release
    /// build's client carries no such switch), so a release build always draws the real names. It changes presentation
    /// alone: ids, model references and commands keep the real values.
    /// </summary>
    public static class CaptureNames
    {
        /// <summary>The runtime's display name, and an agent app's, under the extra.</summary>
        public const string Agent = "Coding Agent";

        /// <summary>A model's name or reference under the extra.</summary>
        public const string Model = "Local Model";

        /// <summary>The host, where the live build names it "This Mac", under the extra.</summary>
        public const string Host = "This Computer";

        /// <summary>Whether the extra is on; always false in a release build.</summary>
        public static bool Generic { get; private set; }

#if DEVELOPMENT_BUILD || UNITY_EDITOR || HALCYONIC_DEVELOPMENT
        /// <summary>Turns the extra on or off. Development builds, the editor and tests only.</summary>
        public static void UseGeneric(bool generic) => Generic = generic;
#endif

        /// <summary>An agent app's name as the headset draws it.</summary>
        public static string AgentName(string name) => Generic ? Agent : name;

        /// <summary>A model's name or reference as the headset draws it.</summary>
        public static string ModelName(string name) => Generic ? Model : name;

        /// <summary>A model's name or reference as the headset draws it, or null where there is none.</summary>
        public static string? ModelNameOrNull(string? name) => name == null ? null : ModelName(name);

        /// <summary>
        /// An approval's request as the headset draws it. Under the extra, a line that names its folder by a host path
        /// ("in /Users/someone/star-catcher", as Codex's adapter adds it) names the folder by its own name, so the home
        /// folder's name never stands on camera; the command itself reads as the agent gave it.
        /// </summary>
        public static string Request(string summary)
        {
            if (!Generic || summary.IndexOf("\nin /", System.StringComparison.Ordinal) < 0) return summary;
            var lines = summary.Split('\n');
            for (var index = 1; index < lines.Length; index++)
            {
                if (lines[index].StartsWith("in /", System.StringComparison.Ordinal)) lines[index] = "in " + FolderName(lines[index].Substring(3));
            }
            return string.Join("\n", lines);
        }

        /// <summary>A host path's last folder, its own name.</summary>
        public static string FolderName(string path)
        {
            var parts = path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? path : parts[parts.Length - 1];
        }
    }
}
