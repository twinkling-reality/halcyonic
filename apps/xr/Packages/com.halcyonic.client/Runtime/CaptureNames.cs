#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// What the headset draws for an agent app, a model, the host and the sibling sources while a development build's
    /// capture extra asks for footage free of product names (the competition's rules; lead 11-04, DEMO's shot list).
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

        /// <summary>The source of an explanation (Understand, the evidence doors) under the extra.</summary>
        public const string Explanation = "Explanation";

        /// <summary>The source of a measurement (Checks, usage) under the extra.</summary>
        public const string Measurement = "Measurement";

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
    }
}
