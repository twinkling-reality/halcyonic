#nullable enable
using System.Globalization;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the stage says while another window has focus and large panels are folded: how much work
    /// needs the person, counted across every project, hidden ones included, so it stays findable when
    /// a window covers the characters; beside a window, how much more work has no character; and the
    /// panel that was open, kept as it was until focus returns.
    /// </summary>
    public static class AmbientText
    {
        /// <summary>The most of a panel's name, which can be a task's title, said while it is kept.</summary>
        public const int StillOpenLimit = 32;

        /// <summary>How many workstreams need the person now: an approval waits, or one waits for them.</summary>
        public static int NeedsYou(ClientProjection state) =>
            state.Workstreams.Values.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou);

        /// <summary>"1 needs you", "3 need you", or null when nothing does.</summary>
        public static string? NeedsYouLine(int count) => count <= 0 ? null : EntryText.WaitingForYou(count);

        /// <summary>"1 more task not shown here", "3 more tasks not shown here": work with no character on the stage; null for none.</summary>
        public static string? NotShown(int count) =>
            count <= 0 ? null : count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " more task" : " more tasks") + " not shown here";

        /// <summary>
        /// "Still open: Create a project": the panel that was open when it folded, kept as it was. Its
        /// name can be text from outside, as a task's title, so it is cut short past <see cref="StillOpenLimit"/>.
        /// </summary>
        public static string StillOpen(string panel) => "Still open: " + IntelligenceText.Truncate(panel, StillOpenLimit);
    }

    /// <summary>Where the stage's banner stands (<see cref="BannerPlace"/>).</summary>
    public enum BannerStand
    {
        /// <summary>Something else stands where it goes, so it steps aside.</summary>
        Hidden,

        /// <summary>In its own place: under the labels in front of the person, under a window's lane beside one, above the characters over a surface.</summary>
        InPlace,

        /// <summary>Above the stage, saying only that the demonstration plays, while the menu stands in its place.</summary>
        AboveTheStage,
    }

    /// <summary>
    /// Where the stage's banner stands while the menu, a panel or the peek may be where it goes. The peek
    /// always hides it, and a panel, as the open menu, hides it in a live session, which the panel then
    /// speaks for. In the recorded demonstration a panel raises it above the stage instead, since the
    /// demonstration's lines stay in view whatever the menu shows (ADR 0012, ADR 0026).
    /// </summary>
    public static class BannerPlace
    {
        public static BannerStand Of(bool demonstration, bool panelCovers, bool peekCovers)
        {
            if (peekCovers) return BannerStand.Hidden;
            if (!panelCovers) return BannerStand.InPlace;
            return demonstration ? BannerStand.AboveTheStage : BannerStand.Hidden;
        }
    }
}
