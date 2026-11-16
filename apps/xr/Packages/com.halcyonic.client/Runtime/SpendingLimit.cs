#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A task's spending limit as the headset says it, and the new limit a person may carry on under (SCREENS's spending-limit unit,
    /// 2026-11-16; AUDIT2's words 23:14Z; AUDIT3's S1 to S6). The host stops a task at its limit (ExecutionView.spending_cap); a task
    /// whose limit is reached, or whose spend is not known after a restart, takes no instruction without a new one
    /// (execution.send_instruction's spending_cap_usd), which counts from now. Every amount is the person's own press, never preset.
    /// </summary>
    public static class SpendingLimit
    {
        /// <summary>The label of a task whose limit is reached.</summary>
        public const string AtLimit = "At Spending Limit";

        /// <summary>The label of a task re-attached after a restart with its spend not known (CORE's SPEND_NOT_KNOWN): never a figure.</summary>
        public const string NotKnown = "Spending Not Known";

        /// <summary>The composer's question for a new limit: new money from now, never added to the old limit (AUDIT2, AUDIT3's L3).</summary>
        public const string SpendUpTo = "Spend Up To";

        /// <summary>Why Send waits in the composer of a task at its limit until an amount is chosen.</summary>
        public const string ChooseAmountFirst = "Choose an Amount First";

        /// <summary>Said beside the amounts: the new limit is new money from now, never added to the old one (AUDIT3's S4).</summary>
        public const string CountsFromNow = "Counts from now.";

        /// <summary>An offered amount's key on its press, and back: only an offered amount reads back, so no other value is ever chosen.</summary>
        public static string Key(double usd) => usd.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The offered amount a press's key names, or null for any other key.</summary>
        public static double? FromKey(string? key)
        {
            foreach (var amount in Amounts) if (Key(amount) == key) return amount;
            return null;
        }

        /// <summary>The amounts offered, in US dollars; none is chosen until the person presses one (S1).</summary>
        public static IReadOnlyList<double> Amounts { get; } = new[] { 1d, 5d, 10d };

        /// <summary>
        /// Whether a task takes no instruction without a new limit: its limit reached, or its spend not known after a restart. Null, or
        /// an open limit, is not.
        /// </summary>
        public static bool NeedsNewLimit(ExecutionView? execution) =>
            execution?.SpendingCap?.State is SpendingCapViewState.Reached or SpendingCapViewState.Waiting;

        /// <summary>
        /// The task's limit as its file says it, label apart from fact: reached "At Spending Limit" with "Spent $0.30 of $1.00" where
        /// the spend is known, else the limit alone; waiting "Spending Not Known" with "Limit $1.00", never a figure; open, no label and
        /// "Limit $1.00". Null for a task with no limit.
        /// </summary>
        public static (string? Label, string Fact)? Of(ExecutionView? execution)
        {
            if (execution?.SpendingCap is not SpendingCapView cap) return null;
            var limit = "Limit " + Dollars(cap.LimitUsd);
            return cap.State switch
            {
                SpendingCapViewState.Reached => (AtLimit, cap.SpentUsd is double spent ? "Spent " + Dollars(spent) + " of " + Dollars(cap.LimitUsd) : limit),
                SpendingCapViewState.Waiting => (NotKnown, limit),
                _ => (null, limit),
            };
        }

        /// <summary>A start's choice, where the chosen runtime offers a limit (AUDIT2, 2026-11-16T2324Z): the heading, and the true state while no amount is chosen.</summary>
        public const string StartHeading = "Spending Limit";
        public const string NoLimit = "No Limit";

        /// <summary>The key of No Limit on a start's choice; an amount's key is <see cref="Key"/>.</summary>
        public const string NoLimitKey = "none";

        /// <summary>Whether a start on <paramref name="runtime"/> may carry a limit: its descriptor offers one, never a practice run.</summary>
        public static bool Offered(RuntimeDescriptor? runtime) => runtime != null && !runtime.Synthetic && runtime.OffersSpendingCap == true;

        /// <summary>A start's amount as its choice says it: "$5", a first limit (the composer's "$5 More" is new money on top).</summary>
        public static string First(double usd) => More(usd).Substring(0, More(usd).Length - " More".Length);

        /// <summary>The review's line under the limit, the control plane's own (start.ts), whole.</summary>
        public static string StartLine(double usd) => "It stops near " + Dollars(usd) + " by the app's own estimate; it can go over on its last step";

        /// <summary>The host's own most for any amount of money (contracts' USAGE_USD_MAX): no typed amount passes it.</summary>
        public const double MostUsd = 1_000_000;

        /// <summary>
        /// A typed amount as plain dollars and cents, or null for anything else (AUDIT3's S2 and 23:3xZ terms): digits, then at most two
        /// decimals after one point, more than zero and no more than <see cref="MostUsd"/>. No sign, exponent, grouping, locale comma,
        /// space or currency mark is taken; the value read is the value sent, shown back by <see cref="Dollars"/>.
        /// </summary>
        public static double? Parse(string? typed)
        {
            if (typed == null || typed.Length == 0 || typed.Length > 10) return null;
            var point = typed.IndexOf('.');
            var whole = point < 0 ? typed : typed.Substring(0, point);
            var cents = point < 0 ? "" : typed.Substring(point + 1);
            if (whole.Length == 0 || (point >= 0 && (cents.Length == 0 || cents.Length > 2))) return null;
            foreach (var c in whole + cents) if (c < '0' || c > '9') return null;
            var value = double.Parse(typed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return value > 0 && value <= MostUsd ? value : (double?)null;
        }

        /// <summary>An amount whole in dollars and cents, as sent: "$5.00" (S2).</summary>
        public static string Dollars(double usd) => "$" + usd.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>A choice's words: "$5 More", new money from now (AUDIT2).</summary>
        public static string More(double usd) =>
            "$" + (usd == System.Math.Floor(usd) ? usd.ToString("0", CultureInfo.InvariantCulture) : usd.ToString("0.00", CultureInfo.InvariantCulture)) + " More";

        /// <summary>The confirm's words for an instruction with its new limit: "Send Instruction, Spend Up To $5.00 More" (AUDIT2).</summary>
        public static string Confirm(double usd) => "Send Instruction, " + SpendUpTo + " " + Dollars(usd) + " More";
    }
}
