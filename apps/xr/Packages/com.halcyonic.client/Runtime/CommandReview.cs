#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The host's review of one command (the lead's decision of 2026-11-12; ADR 0060; <see cref="ActionReview"/>, ADR 0051's
    /// digest made general) as the one card reads it: what was given is kept as the host wrote it; only what no card
    /// could show is left out (a warning with no line, words given that are empty).
    /// </summary>
    public static class CommandReview
    {
        /// <summary>The warnings that say something.</summary>
        public static IReadOnlyList<ActionReviewWarning> Warnings(ActionReview review) =>
            review.Warnings.Where(each => !string.IsNullOrWhiteSpace(each.Line)).ToList();

        /// <summary>The words given that go out with the command, in the payload's order, each with words to show.</summary>
        public static IReadOnlyList<ActionReviewSaid> Said(ActionReview review) => review.Said.Where(each => !string.IsNullOrEmpty(each.Words)).ToList();

        /// <summary>How many things the command touches in all: never fewer than the host listed.</summary>
        public static long Total(ActionReview review) => System.Math.Max(review.ItemsTotal, review.Items.Count);
    }

    /// <summary>
    /// The one table from the host's review kinds to the kit's marks (R3): a kind with no glyph of its own has no mark,
    /// never another thing's. A name a person types (a file, a folder, a repository, a branch, an address, a script) is
    /// set in mono. Every member is listed; a value outside the enum has no mark and is not set in mono.
    /// </summary>
    public static class ReviewKinds
    {
        public static RoomKind Mark(ReviewKind kind) => kind switch
        {
            ReviewKind.FileAdded => RoomKind.Added,
            ReviewKind.FileChanged => RoomKind.Changed,
            ReviewKind.FileRemoved => RoomKind.Removed,
            ReviewKind.Folder => RoomKind.Folder,
            ReviewKind.Repository => RoomKind.Project,
            ReviewKind.Task => RoomKind.Task,
            ReviewKind.Address => RoomKind.Link,
            ReviewKind.Branch or ReviewKind.Version or ReviewKind.Script or ReviewKind.Words or ReviewKind.Unknown => RoomKind.None,
            _ => RoomKind.None,
        };

        public static bool Mono(ReviewKind kind) => kind switch
        {
            ReviewKind.FileAdded or ReviewKind.FileChanged or ReviewKind.FileRemoved or ReviewKind.Folder or ReviewKind.Repository
                or ReviewKind.Branch or ReviewKind.Address or ReviewKind.Script => true,
            ReviewKind.Task or ReviewKind.Version or ReviewKind.Words or ReviewKind.Unknown => false,
            _ => false,
        };
    }
}
