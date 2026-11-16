#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One figure of what a task used: its label, its value and the notes under it, each Halcyonic's own words.</summary>
    public sealed class TaskUsageFigure
    {
        public TaskUsageFigure(string label, string value, IReadOnlyList<string> notes, bool known)
        {
            Label = label;
            Value = value;
            Notes = notes;
            Known = known;
        }

        public string Label { get; }
        public string Value { get; }

        /// <summary>"At least this much" first where the figure is short of the whole, then each reason.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>False where the value is a word for what is not known, never a number.</summary>
        public bool Known { get; }
    }

    /// <summary>
    /// What a task used, under its steps once the turn ended (shot 7; the lead's ruling of 2026-11-16 03:22Z): the tokens
    /// the agent app reported, and its own cost estimate only where it gave one. The same rules as the Mac's Usage page
    /// (apps/control-plane/src/network/usage-page.ts): nothing counted or priced on the headset; reasoning is inside
    /// output and never added again; a figure short of the whole says "At least this much" and why; unknown is said,
    /// never 0; money under a cent keeps its first figures, never $0.00. The usage is the domain's one sum for the task
    /// (WorkstreamView.usage, CORE's, at protocol 3), or one run's where only that is known, and the caller says which.
    /// </summary>
    public static class TaskUsage
    {
        public const string TokensUsed = "Tokens Used";
        public const string Cost = "Cost";
        public const string NotReportedYet = "Not Reported Yet";
        public const string NotReportedByThisApp = "Not Reported by This App";
        public const string NotKnown = "Not Known";
        public const string AtLeast = "At least this much";
        public const string PartsMissing = "Some parts not reported";

        /// <summary>Under the cost: whose figure it is (AUDIT2's words, 2026-11-16 02:02Z).</summary>
        public const string AgentsCount = "By the agent's count";

        public static string TurnsMissing(long turns, long ended) =>
            turns.ToString(CultureInfo.InvariantCulture) + " of " + ended.ToString(CultureInfo.InvariantCulture) + " turns reported";

        /// <summary>
        /// The tokens figure for <paramref name="usage"/>, where <paramref name="reportsUsage"/> says whether the task's agent
        /// app reports usage at all (RuntimeDescriptor.reports_usage; null where not known).
        /// </summary>
        public static TaskUsageFigure Tokens(RunUsage? usage, bool? reportsUsage)
        {
            if (usage == null)
                return new TaskUsageFigure(TokensUsed, reportsUsage == false ? NotReportedByThisApp : NotReportedYet, Array.Empty<string>(), false);
            var parts = new[] { usage.Tokens.Input, usage.Tokens.CacheRead, usage.Tokens.CacheWrite, usage.Tokens.Output };
            long sum = 0;
            var given = 0;
            foreach (var part in parts)
            {
                if (part is long value)
                {
                    sum += value;
                    given++;
                }
            }
            if (given == 0) return new TaskUsageFigure(TokensUsed, NotKnown, Array.Empty<string>(), false);
            var reasons = new List<string>();
            if (given < parts.Length) reasons.Add(PartsMissing);
            if (usage.Turns < usage.TurnsEnded) reasons.Add(TurnsMissing(usage.Turns, usage.TurnsEnded));
            var notes = reasons.Count == 0 ? (IReadOnlyList<string>)Array.Empty<string>() : new List<string> { AtLeast }.Concat(reasons);
            return new TaskUsageFigure(TokensUsed, sum.ToString("N0", CultureInfo.InvariantCulture), notes, true);
        }

        /// <summary>The agent's own cost estimate, or null where it gave none: the line is left out, never $0.</summary>
        public static TaskUsageFigure? CostOf(RunUsage? usage)
        {
            if (usage?.Cost is not UsageEstimate cost) return null;
            var notes = new List<string>();
            if (usage.Turns < usage.TurnsEnded) notes.Add(AtLeast);
            notes.Add(AgentsCount);
            return new TaskUsageFigure(Cost, Money(cost.EstimatedUsd), notes, true);
        }

        /// <summary>Money as the Mac's page says it: above zero but under a cent keeps its first two figures, never $0.00.</summary>
        public static string Money(double usd)
        {
            if (usd > 0 && usd < 0.01)
            {
                var digits = Math.Max(0, 1 - (int)Math.Floor(Math.Log10(usd)));
                return "$" + Math.Round(usd, digits, MidpointRounding.AwayFromZero).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
            }
            return "$" + usd.ToString("N2", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> Concat(this List<string> first, List<string> rest)
        {
            first.AddRange(rest);
            return first;
        }
    }
}
