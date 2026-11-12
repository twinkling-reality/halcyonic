#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// One rule for every choice a step asks (the lead, 2026-11-12, from JUDGE's live walk): a choice with exactly one ready
    /// answer is never asked, it is taken, shown as chosen and stays changeable; with two or more, the one last used here, else
    /// the one last used anywhere, is taken; only with none of those is the person asked. The ready answers are the host's
    /// list, never a name the client knows.
    /// </summary>
    public static class Preselection
    {
        /// <summary>The answer to take from <paramref name="ready"/>, or the default where the person must choose.</summary>
        /// <param name="lastHere">Answers last used in this place (a project), newest first.</param>
        /// <param name="lastAnywhere">Answers last used anywhere, newest first.</param>
        public static T? Pick<T>(IReadOnlyList<T> ready, IEnumerable<T> lastHere, IEnumerable<T> lastAnywhere, IEqualityComparer<T>? same = null) where T : class
        {
            if (ready.Count == 1) return ready[0];
            if (ready.Count == 0) return null;
            var comparer = same ?? EqualityComparer<T>.Default;
            return lastHere.FirstOrDefault(each => ready.Contains(each, comparer)) is T here ? ready.First(each => comparer.Equals(each, here))
                : lastAnywhere.FirstOrDefault(each => ready.Contains(each, comparer)) is T anywhere ? ready.First(each => comparer.Equals(each, anywhere))
                : null;
        }
    }
}
