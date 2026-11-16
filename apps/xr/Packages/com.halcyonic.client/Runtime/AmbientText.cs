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
        /// <summary>How many workstreams need the person now: an approval waits, or one waits for them.</summary>
        public static int NeedsYou(ClientProjection state) =>
            state.Workstreams.Values.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou);

        /// <summary>"1 needs you", "3 need you", or null when nothing does.</summary>
        public static string? NeedsYouLine(int count) => count <= 0 ? null : EntryText.WaitingForYou(count);

        /// <summary>"1 more task not shown here", "3 more tasks not shown here": work with no character on the stage; null for none.</summary>
        public static string? NotShown(int count) =>
            count <= 0 ? null : count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " more task not shown here" : " more tasks not shown here");

        /// <summary>The label over the panel that was open when the work folded (AUDIT2, 2026-11-16 04:52Z).</summary>
        public const string StillOpenLabel = "Still Open";

        /// <summary>
        /// "Still Open: Create a project": the panel that was open when it folded, kept as it was, its name whole (never cut:
        /// the owner's bar and AUDIT's MUST, 2026-11-16; the banner wraps it). The name can be text from outside, as a task's
        /// title, so it is made plain first. Where the banner lays label and name apart, <see cref="StillOpenLabel"/> and
        /// <see cref="StillOpenName"/> give them.
        /// </summary>
        /// <remarks>As runs: "Still Open: " upright, the panel's name leaning on Halcyonic's ground, last.</remarks>
        public static LineWords StillOpen(string panel) => LineWords.Ours(StillOpenLabel + ": ") + StillOpenName(panel);

        /// <summary>The panel's name alone, plain and whole, as an outside name.</summary>
        public static LineWords StillOpenName(string panel) => LineWords.TheirName(LabelText.Plain(panel));
    }

    /// <summary>Where the stage's banner stands (<see cref="BannerPlace"/>).</summary>
    public enum BannerStand
    {
        /// <summary>Something else stands where it goes, so it steps aside.</summary>
        Hidden,

        /// <summary>In its own place: under the labels in front of the person, under a window's lane beside one, above the characters over a surface.</summary>
        InPlace,

        /// <summary>Above the stage, saying only that the demonstration plays, whatever stands in its place: the closed bar or the menu.</summary>
        AboveTheStage,
    }

    /// <summary>
    /// Where the stage's banner stands while the menu, a panel or the peek may be where it goes. The peek
    /// always hides it, and a panel, as the open menu, hides it in a live session, which the panel then
    /// speaks for. In the recorded demonstration it always stands raised above the stage instead, the
    /// closed bar or the menu where it would hang, since the demonstration's lines stay in view whatever
    /// the menu shows (ADR 0012, ADR 0026).
    /// </summary>
    public static class BannerPlace
    {
        public static BannerStand Of(bool demonstration, bool panelCovers, bool peekCovers)
        {
            if (peekCovers) return BannerStand.Hidden;
            if (demonstration) return BannerStand.AboveTheStage;
            return panelCovers ? BannerStand.Hidden : BannerStand.InPlace;
        }
    }
}
