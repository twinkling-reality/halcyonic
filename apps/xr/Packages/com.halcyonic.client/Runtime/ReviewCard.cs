#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The ONE review card (the lead's decision of 2026-11-12, R1): any command's <see cref="ActionReview"/> drawn from the
    /// kit's parts, with one press. Nothing here reads which command it reviews. It reads in parts:
    /// <list type="bullet">
    /// <item>Where words given go out with the command (<see cref="ActionReview.Said"/>), those words first, whole: the entry's title
    /// over them, the kit's paged description four lines a part, as many parts as the view makes of them (the 04:45Z
    /// amendment: every value in the model's words shown whole).</item>
    /// <item>Then what happens: the entry's title from the computer's list, the host's line under it, "Check First" where the
    /// host warns, its facts as slots, what it touches as the kit's list five a part, and on the last part how many the
    /// host did not list.</item>
    /// </list>
    /// The actions: the host's Confirm (the main action, one press, carrying the digest), Next Part and Previous Part while
    /// there are more, then Cancel. Confirm stands on every part, so paging is never on the way to it, except where a model
    /// wrote what goes out: then it stands on the last part only, once those words were in view.
    /// </summary>
    public static class ReviewCard
    {
        /// <summary>The one press: sends the command with the review's digest.</summary>
        public const string Confirm = "room-review-confirm";

        public const string Cancel = "room-review-cancel";
        public const string NextPart = "room-review-next";
        public const string PreviousPart = "room-review-previous";

        public const string NextPartWords = "Next Part";
        public const string PreviousPartWords = "Previous Part";

        /// <summary>The parts the list of what it touches turns through: the kit's list, <see cref="RoomCard.ListAPage"/> a part.</summary>
        public static int ListParts(ActionReview review) => Math.Max(1, (review.Items.Count + RoomCard.ListAPage - 1) / RoomCard.ListAPage);

        /// <summary>
        /// Every part: the model's words in <paramref name="saidPages"/> (as many as the card's view made of them, shown with
        /// part 0; none where nothing was said), then the list's.
        /// </summary>
        public static int Parts(ActionReview review, int saidPages = 1) => SaidParts(review, saidPages) + ListParts(review);

        /// <summary>The card at <paramref name="part"/> (from 0, held to the parts there are), titled by the list's entry.</summary>
        public static RoomCard Card(ActionReview review, string title, int part = 0, int saidPages = 1)
        {
            if (review == null) throw new ArgumentNullException(nameof(review));
            var parts = Parts(review, saidPages);
            var at = Math.Max(0, Math.Min(part, parts - 1));
            var said = SaidParts(review, saidPages);
            if (at < said)
                return new RoomCard(Host(title), SaidWords(review), more: LineWords.Ours(RoomReview.PartOf(at, parts)), descriptionPage: at);

            var page = at - said;
            var slots = review.Facts.Take(RoomCard.MostSlots)
                .Select(fact => new RoomSlot(ReviewKinds.Mark(fact.Kind), Words(fact.Value, fact.Theirs), mono: ReviewKinds.Mono(fact.Kind))).ToList();
            var rows = review.Items.Skip(page * RoomCard.ListAPage).Take(RoomCard.ListAPage)
                .Select(item => new RoomListRow(ReviewKinds.Mark(item.Kind), Words(item.Name, item.Theirs), item.Note, mono: ReviewKinds.Mono(item.Kind))).ToList();
            var warnings = CommandReview.Warnings(review);
            RoomCardStatus? status = warnings.Count == 0 ? (RoomCardStatus?)null
                // Each warning its own line, in the host's order, never joined into one paragraph (a fresh reader, 2026-11-14T00:20Z: the joined
                // warnings buried that the person's requests leave this computer).
                : RoomCardStatus.Apart(RoomReview.CheckTitle, warnings.Select(each => each.Line), RoomTone.Attention, RoomKind.Failed);
            // What happens first, the warnings after (AUDIT2's 2026-11-13T22:37Z), except where a warning is a consent the press gives:
            // then it stands under the name, read before the press (AUDIT2's 22:38Z).
            var after = status != null && !warnings.Any(each => ConsentFirst.Contains(each.Code));
            return new RoomCard(Host(title), review.WhatHappens.Trim().Length > 0 ? Host(review.WhatHappens) : null, status: status, slots: slots, list: rows,
                more: Foot(review, at, parts), statusAfter: after);
        }

        /// <summary>
        /// The host's warning codes that are a consent the person gives by pressing (it never asks, it reaches past its folder, it
        /// cannot be brought back, history is left, it becomes public, it replaces what was published, it costs money or its cost is not
        /// known: AUDIT2's 22:42Z, the money said before the press, the lead's 1111-b123): a review carrying any keeps its
        /// warnings first, under its name. A new consent code joins here in the same change that adds it to the host.
        /// </summary>
        public static readonly IReadOnlyCollection<string> ConsentFirst = new HashSet<string>(StringComparer.Ordinal)
        {
            "never_asks", "unconfined", "cannot_undo", "history_left", "public", "replaces", "costs_money", "cost_unknown",
        };

        /// <summary>The actions at <paramref name="part"/>: Confirm (main, see the class), Next Part and Previous Part where they turn, then Cancel.</summary>
        public static IReadOnlyList<ReviewAction> Actions(ActionReview review, int part = 0, int saidPages = 1)
        {
            var parts = Parts(review, saidPages);
            var at = Math.Max(0, Math.Min(part, parts - 1));
            var actions = new List<ReviewAction>();
            if (CommandReview.Said(review).Count == 0 || at == parts - 1) actions.Add(new ReviewAction(Confirm, review.ConfirmWords, main: true));
            if (at + 1 < parts) actions.Add(new ReviewAction(NextPart, NextPartWords, main: actions.Count == 0));
            if (at > 0) actions.Add(new ReviewAction(PreviousPart, PreviousPartWords));
            actions.Add(new ReviewAction(Cancel, EntryText.Cancel));
            return actions;
        }

        /// <summary>
        /// The words that go out, as the card shows them: whole and exactly, character for character (the lead's 12:42Z: what
        /// will run or be sent is never trimmed, unquoted or collapsed), spelled by the start's review's own rule
        /// (<see cref="NewWorkReview.Safe"/>). One value stands alone under the entry's title, which already says what it is;
        /// where there are several, each follows the host's label for it, so none is taken for another.
        /// </summary>
        public static LineWords? SaidWords(ActionReview review)
        {
            var said = CommandReview.Said(review);
            if (said.Count == 1) return LineWords.Shown(NewWorkReview.Safe(said[0].Words));
            LineWords? words = null;
            foreach (var each in said)
            {
                var one = LineWords.Ours((words == null ? "" : " ") + LabelText.Plain(each.Label) + ":") + LineWords.Shown(NewWorkReview.Safe(each.Words));
                words = words == null ? one : words + one;
            }
            return words;
        }

        private static int SaidParts(ActionReview review, int saidPages) => CommandReview.Said(review).Count == 0 ? 0 : Math.Max(1, saidPages);

        /// <summary>The foot: the part's place while the card turns, and on its last part how many the host did not list.</summary>
        private static LineWords? Foot(ActionReview review, int part, int parts)
        {
            var unlisted = CommandReview.Total(review) - review.Items.Count;
            if (part == parts - 1 && unlisted > 0) return LineWords.Ours(HistoryText.AndMore(unlisted));
            return parts > 1 ? LineWords.Ours(RoomReview.PartOf(part, parts)) : null;
        }

        /// <summary>A fact's value or an item's name: someone's own name shown exactly, as what the command acts on; else the host's words.</summary>
        private static LineWords Words(string text, bool theirs) => theirs ? LineWords.Shown(NewWorkReview.Safe(text)) : Host(text);

        /// <summary>The host's own words (a title, its line of what happens, a fact it names): upright, by the rule every label is shown by.</summary>
        private static LineWords Host(string text) => LineWords.Written(LabelText.Plain(text));
    }

    /// <summary>An action of the review card: its id, its words, and whether it is the main one.</summary>
    public readonly struct ReviewAction
    {
        public ReviewAction(string id, string words, bool main = false)
        {
            Id = id;
            Words = words;
            Main = main;
        }

        public string Id { get; }

        public string Words { get; }

        public bool Main { get; }
    }
}
