#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Where a stat kind stands in the person's choice of the strip.</summary>
    public enum EvidenceChoiceState
    {
        /// <summary>The read holds it and it stands on the strip.</summary>
        Chosen,

        /// <summary>The read holds it and it does not stand; a press chooses it.</summary>
        Open,

        /// <summary>The read lacks it: shown so the person knows it exists, never pressable.</summary>
        NotMeasured,
    }

    /// <summary>One row of Choose: the stat's key, its head (the strip's own word), a kind beside it, and where it stands.</summary>
    public sealed class EvidenceChoiceRow
    {
        public EvidenceChoiceRow(string key, string head, string? kind, EvidenceChoiceState state) => (Key, Head, Kind, State) = (key, head, kind, state);

        /// <summary>The stat's key in <see cref="EvidenceStrip.Stat.Key"/>'s terms; for the Checks row and a kind the read lacks, the kind's own key (<see cref="EvidenceChoice.ChecksKey"/>).</summary>
        public string Key { get; }

        public string Head { get; }

        /// <summary><see cref="EvidenceChoice.Words.NotMeasured"/> where the read lacks the kind; null otherwise, a check row's too: its head alone stands (AUDIT's pass).</summary>
        public string? Kind { get; }

        public EvidenceChoiceState State { get; }
    }

    /// <summary>
    /// Choose for the evidence view's strip: the person picks which of the strip's stats stand (<see cref="EvidenceStrip.Shown"/>),
    /// and the choice is kept on their headset across runs. Every kind the strip knows is a row in the strip's order, so a kind a
    /// run did not measure still shows, as Not Measured, never as 0 and never pressable. A choice keeps a kind this run lacks, so
    /// it stands again on the runs that measure it.
    /// </summary>
    public static class EvidenceChoice
    {
        /// <summary>Choose's own words (AUDIT2's): the heads are the strip's.</summary>
        public static class Words
        {
            public const string Title = "Choose Stats", NotMeasured = "Not Measured", Done = "Done";
        }

        /// <summary>Where the choice is kept on the headset.</summary>
        public const string PreferenceKey = "evidence.strip.chosen";

        /// <summary>The prefix of a check kind's key: one stat a check label, "checks:test".</summary>
        public const string ChecksPrefix = "checks:";

        /// <summary>
        /// The one Checks row's key: every kind of check the read holds is one row (AUDIT's pass), chosen and unchosen together. The
        /// choice itself keeps the kinds' own keys, which is what <see cref="EvidenceStrip.Shown"/> reads.
        /// </summary>
        public const string ChecksKey = "checks";

        /// <summary>
        /// Every kind the strip can show, in its order, each its key and the strip's own head. <see cref="EvidenceStrip"/> keeps its keys
        /// inline where it builds each stat, so the catalogue of keys is here, once, beside the words it takes from the strip.
        /// </summary>
        public static readonly IReadOnlyList<(string Key, string Head)> Kinds = new[]
        {
            ("lines-added", EvidenceStrip.Words.LinesAdded),
            ("lines-removed", EvidenceStrip.Words.LinesRemoved),
            (ChecksKey, EvidenceStrip.Words.Checks),
            ("time", EvidenceStrip.Words.Time),
            ("cost", EvidenceStrip.Words.Cost),
            ("lines-kept", EvidenceStrip.Words.LinesKept),
            ("tool-calls", EvidenceStrip.Words.ToolCalls),
            ("commits", EvidenceStrip.Words.Commits),
            ("errors", EvidenceStrip.Words.Errors),
        };

        /// <summary>
        /// Choose's rows for <paramref name="strip"/> under <paramref name="chosen"/> (null: no choice made, the strip's default stands):
        /// one a kind in the strip's order, every kind of check one Checks row (<see cref="ChecksKey"/>), chosen where any kind of check the
        /// read holds is chosen; with no strip, every kind not measured.
        /// </summary>
        public static IReadOnlyList<EvidenceChoiceRow> Rows(EvidenceStrip? strip, IReadOnlyList<string>? chosen)
        {
            var rows = new List<EvidenceChoiceRow>();
            var effective = new HashSet<string>(strip == null ? new string[0] : Effective(chosen, strip), StringComparer.Ordinal);
            foreach (var (key, head) in Kinds)
            {
                var measured = strip == null ? new List<EvidenceStrip.Stat>()
                    : key == ChecksKey ? CheckStats(strip)
                    : strip.All.Where(stat => stat.Key == key).ToList();
                if (measured.Count == 0)
                {
                    rows.Add(new EvidenceChoiceRow(key, head, Words.NotMeasured, EvidenceChoiceState.NotMeasured));
                    continue;
                }
                rows.Add(new EvidenceChoiceRow(key, head, null, measured.Any(stat => effective.Contains(stat.Key)) ? EvidenceChoiceState.Chosen : EvidenceChoiceState.Open));
            }
            return rows;
        }

        /// <summary>
        /// The choice after a press of the row <paramref name="key"/>, starting from the effective choice: an open stat joins at the end, a
        /// chosen one leaves, and a key no row of this read has (a kind it lacks, or one kind of check alone) leaves the choice as it was.
        /// The Checks row (<see cref="ChecksKey"/>) chosen leaves with every kind of check, kept from other runs too; open, it joins with
        /// every kind the read holds, in the strip's order, where the first kind of check already kept stands, else at the end.
        /// </summary>
        public static IReadOnlyList<string> Toggle(IReadOnlyList<string>? chosen, string key, EvidenceStrip strip)
        {
            if (strip == null) throw new ArgumentNullException(nameof(strip));
            var effective = Effective(chosen, strip).ToList();
            if (key == ChecksKey)
            {
                var checks = CheckStats(strip).Select(stat => stat.Key).ToList();
                if (checks.Count == 0) return effective;
                if (checks.Any(effective.Contains))
                {
                    effective.RemoveAll(IsCheck);
                    return effective;
                }
                var first = effective.FindIndex(IsCheck);
                effective.InsertRange(first >= 0 ? first : effective.Count, checks);
                return effective;
            }
            if (IsCheck(key) || !strip.All.Any(stat => stat.Key == key)) return effective;
            if (effective.Remove(key))
            {
                // A key kept twice leaves whole.
                effective.RemoveAll(each => each == key);
                return effective;
            }
            effective.Add(key);
            return effective;
        }

        /// <summary>The choice kept on this headset; null where none is.</summary>
        public static IReadOnlyList<string>? Load(IDevicePreferences prefs)
        {
            if (prefs == null) throw new ArgumentNullException(nameof(prefs));
            if (!(prefs.Get(PreferenceKey) is string kept)) return null;
            // Keys never hold a newline, so a newline parts them; an empty string is the choice of nothing.
            return kept.Split('\n').Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>Keeps <paramref name="chosen"/> on this headset.</summary>
        public static void Save(IDevicePreferences prefs, IReadOnlyList<string> chosen)
        {
            if (prefs == null) throw new ArgumentNullException(nameof(prefs));
            if (chosen == null) throw new ArgumentNullException(nameof(chosen));
            prefs.Set(PreferenceKey, string.Join("\n", chosen.Where(key => !string.IsNullOrEmpty(key) && key.IndexOf('\n') < 0)));
        }

        private static bool IsCheck(string key) => key.StartsWith(ChecksPrefix, StringComparison.Ordinal);

        /// <summary>Every kind of check the read holds, in the strip's order.</summary>
        private static List<EvidenceStrip.Stat> CheckStats(EvidenceStrip strip) => strip.All.Where(stat => IsCheck(stat.Key)).ToList();

        /// <summary>The person's choice, or with none made, the keys the strip shows by default.</summary>
        private static IReadOnlyList<string> Effective(IReadOnlyList<string>? chosen, EvidenceStrip strip) =>
            chosen ?? strip.Shown(null).Select(stat => stat.Key).ToList();
    }
}
