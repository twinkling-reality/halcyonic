#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a headset face (a tag, a card, a screen, speech, the picker) calls an agent app: what it is, never its maker's name
    /// (AGENTS.md: a character is a Workstream, never a vendor session; AUDIT2's rule 182 and its words, 2026-11-18T02:52Z). The
    /// runtime's own name and a model's own id stay one press deeper, on the agent's details (<see cref="DetailsName"/>).
    /// Each head is true of its adapter as INTEGRATIONS.md declares it; an app of a kind not named here is a "Coding Agent".
    /// </summary>
    public static class RuntimeFaces
    {
        /// <summary>The head for an agent app of a kind this does not name: what it is, and no more.</summary>
        public const string Unnamed = "Coding Agent";

        /// <summary>The face's head for <paramref name="kind"/>: a synthetic runtime (the mock adapter's) is the practice run whatever its kind.</summary>
        public static string Head(string? kind, bool synthetic = false) => synthetic ? "Practice Run" : kind switch
        {
            "claude-agent" => "Hosted Models",
            "codex" => "Local Models",
            "opencode" => "Your Model Setup",
            "antigravity" or "kiro" or "cursor" => "Signed-In App",
            _ => Unnamed,
        };

        /// <summary>The face's head for <paramref name="runtime"/>.</summary>
        public static string Head(RuntimeDescriptor runtime) => Head(runtime.Kind, runtime.Synthetic);

        /// <summary>The face's head for a run's runtime, as the run records it.</summary>
        public static string Head(RuntimeRef runtime) => Head(runtime.Kind, runtime.Synthetic);

        /// <summary>The picker's line under the head, one plain sentence each (AUDIT2's words); null where it has none.</summary>
        public static string? Line(string? kind, bool synthetic = false) => synthetic ? "Pretends to work; nothing real is done." : kind switch
        {
            "claude-agent" => "Runs on models its maker or your cloud provider serves, with your own key.",
            "codex" => "Runs only on models served on this computer.",
            "opencode" => "Runs on the models your own setup lists, on this computer or online.",
            "antigravity" or "kiro" or "cursor" => "Uses its own sign-in and its own models.",
            _ => null,
        };

        /// <summary>A second line where the record sets the app apart (AUDIT2's words); null where it does not.</summary>
        public static string? SecondLine(string? kind) => kind switch
        {
            "kiro" => "Always asks before it acts.",
            "antigravity" => "Asks before it reads outside the project.",
            _ => null,
        };

        /// <summary>The runtime's own name, in its own case as its adapter reports it: for the agent's details alone, one press deeper.</summary>
        public static string DetailsName(string displayName) => CaptureNames.AgentName(displayName);
    }
}
