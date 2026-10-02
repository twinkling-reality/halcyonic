#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// What the headset calls the computer it works with, the host that runs the control plane and
    /// the agents: one word everywhere, live and in the demo, so it can change in one place. It is
    /// "computer", never a product name, because the competition rules forbid brand names in what
    /// judges see.
    /// </summary>
    public static class HostText
    {
        /// <summary>Inside a sentence: "Waiting for your computer."</summary>
        public const string Your = "your computer";

        /// <summary>At the start of a sentence: "Your computer refused it, so it didn't happen."</summary>
        public const string YourStart = "Your computer";

        /// <summary>The word alone, as in "Pair with a computer" or "Forget this computer".</summary>
        public const string Noun = "computer";
    }
}
