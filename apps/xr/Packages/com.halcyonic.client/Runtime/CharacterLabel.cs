#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a character's label shows, in three parts kept apart (ADR 0023): the task's title on its
    /// plate, the state badge on the plate's edge, and the marks beside the badge. The reason a task
    /// needs attention is not on the label; it shows in the peek (<see cref="PeekCard"/>).
    /// </summary>
    public sealed class CharacterLabel
    {
        private CharacterLabel(string title, StateBadge badge, IReadOnlyList<WorkMark> marks)
        {
            Title = title;
            Badge = badge;
            Marks = marks;
        }

        /// <summary>The task's title, as <see cref="LabelText.Plain"/> shows it.</summary>
        public string Title { get; }

        public StateBadge Badge { get; }

        public IReadOnlyList<WorkMark> Marks { get; }

        public static CharacterLabel Of(CharacterPresentation presentation)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            return new CharacterLabel(presentation.Title, StateLanguage.BadgeOf(presentation), StateLanguage.MarksOf(presentation));
        }
    }

    /// <summary>
    /// The peek: a small card beside a character while the person looks at it or points at it. It
    /// shows the state badge, the reason in a sentence or two, what opening it is for, and what its
    /// marks mean, so the stage itself needs no reason written under each character.
    /// </summary>
    public sealed class PeekCard : IEquatable<PeekCard>
    {
        /// <summary>The peek wraps its reason to this many lines and ends a longer one in an ellipsis.</summary>
        public const int ReasonLines = 2;

        /// <summary>Said before the reason while the state is only the last known one.</summary>
        public const string LastKnown = "Last known";

        private PeekCard(StateBadge badge, IReadOnlyList<WorkMark> marks, string reason, int moreReasons, string? next)
        {
            Badge = badge;
            Marks = marks;
            Reason = reason;
            MoreReasons = moreReasons;
            Next = next;
        }

        public StateBadge Badge { get; }

        public IReadOnlyList<WorkMark> Marks { get; }

        /// <summary>
        /// Why it needs the person, without what the badge already says; else what it did last; else
        /// empty. Text from outside, as <see cref="LabelText.Plain"/> shows it.
        /// </summary>
        public string Reason { get; }

        /// <summary>How many more reasons wait behind the first; 0 for one or none.</summary>
        public int MoreReasons { get; }

        /// <summary>What opening the character is for, in Halcyonic's words, or null when there is nothing to do.</summary>
        public string? Next { get; }

        /// <summary>
        /// The reason as the card shows it: with how many more wait, "… (+1 more)", and after
        /// "Last known" while the session is not live, so the words say what the ghosted badge shows.
        /// Empty when the badge says it all.
        /// </summary>
        public string ReasonLine
        {
            get
            {
                var line = MoreReasons > 0 ? Reason + " (+" + MoreReasons + " more)" : Reason;
                if (!Badge.LastKnown) return line;
                return line.Length == 0 ? LastKnown + "." : LastKnown + ": " + line;
            }
        }

        /// <summary>What the marks mean, in a sentence, or null for work that is neither practice nor recorded.</summary>
        public string? MarkLine => Marks.Count == 0 ? null : MeaningOf(Marks[0]);

        public static PeekCard Of(WorkspacePresentation workspace)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            var character = workspace.Character;
            var badge = StateLanguage.BadgeOf(character);
            var reason = "";
            var more = 0;
            if (character.AttentionNotes.Count > 0)
            {
                // The first reason with more to say than the state, and every other reason behind it.
                // When none has, as for a failure that gave no reason, the state says it all.
                var shown = character.AttentionDetails.FirstOrDefault(detail => detail.Length > 0);
                if (shown != null)
                {
                    reason = shown;
                    more = character.AttentionNotes.Count - 1;
                }
            }
            else
            {
                var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
                if (latest != null) reason = WorkspaceText.Describe(latest);
            }
            return new PeekCard(badge, StateLanguage.MarksOf(character), reason, more, NextOf(badge));
        }

        /// <summary>
        /// What opening is for, by state: answering, seeing why it went wrong, or what changed. Nothing
        /// while the state is only the last known one, since nothing can be sent until it is live.
        /// </summary>
        public static string? NextOf(StateBadge badge)
        {
            if (badge.LastKnown) return null;
            return badge.State switch
            {
                WorkState.WaitingForYou => "Open it to answer.",
                WorkState.CouldNotFinish => "Open it to see why.",
                WorkState.ChecksFailed => "Open it to see what failed.",
                WorkState.FinishedThisRound => "Open it to see what changed.",
                _ => null,
            };
        }

        /// <summary>A mark's meaning: practice builds nothing, and neither a demo nor a recording reaches an agent.</summary>
        public static string MeaningOf(WorkMark mark) => mark.Word switch
        {
            StateLanguage.Practice => "Practice run: nothing is built.",
            StateLanguage.Demo => "Demo: recorded, nothing reaches an agent.",
            StateLanguage.Recorded => "Recorded: a replay, not live work.",
            _ => throw new ArgumentOutOfRangeException(nameof(mark), mark.Word, "Unhandled mark."),
        };

        public bool Equals(PeekCard? other) =>
            other != null
            && other.Badge.State == Badge.State
            && other.Badge.Text == Badge.Text
            && other.Badge.LastKnown == Badge.LastKnown
            && other.Reason == Reason
            && other.MoreReasons == MoreReasons
            && other.Next == Next
            && other.Marks.Select(mark => mark.Word).SequenceEqual(Marks.Select(mark => mark.Word));

        public override bool Equals(object? obj) => Equals(obj as PeekCard);

        public override int GetHashCode() => HashCode.Combine(Badge.State, Badge.Text, Reason, MoreReasons, Next);
    }
}
