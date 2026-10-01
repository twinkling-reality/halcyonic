#nullable enable
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the stage says while another window has focus and large panels are folded: how much work
    /// needs the person, counted across every project, hidden ones included, so it stays findable when
    /// a window covers the characters.
    /// </summary>
    public static class AmbientText
    {
        /// <summary>How many workstreams need the person now: an approval waits, or one waits for them.</summary>
        public static int NeedsYou(ClientProjection state) =>
            state.Workstreams.Values.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou);

        /// <summary>"1 needs you", "3 need you", or null when nothing does.</summary>
        public static string? NeedsYouLine(int count) => count <= 0 ? null : EntryText.WaitingForYou(count);
    }
}
