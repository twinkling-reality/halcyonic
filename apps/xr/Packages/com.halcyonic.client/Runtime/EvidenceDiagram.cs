#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The diagram's places, in the order the route reaches them (plan v2 row 2; Salidium's four brief moves between the ask and the verdict).</summary>
    public enum DiagramPlaceKind { Asked, Changes, How, Checks, LeftToDo, Verdict, Why }

    /// <summary>How the source knows a node or a link: Salidium's own five classes, never merged (its ADR 0006 and 0007); None for the person's own words.</summary>
    public enum DiagramClass { None, Observed, Reported, Inferred, Planned, Explained }

    /// <summary>One node in a place: its stable id (a field's own key), its words, its facts each on its own line, its class, its mark.</summary>
    public sealed class DiagramNode
    {
        public DiagramNode(string id, LineWords words, DiagramClass kind, IReadOnlyList<string>? lines = null, string? value = null, bool mono = false,
            RoomKind mark = RoomKind.None, bool stale = false) =>
            (Id, Words, Class, Lines, Value, Mono, Mark, Stale) = (id, words, kind, lines ?? new string[0], value, mono, mark, stale);

        /// <summary>Stable across reads (a file's path, a run's label, an item's text): a new id is a node the newest read brought.</summary>
        public string Id { get; }
        public LineWords Words { get; }
        public DiagramClass Class { get; }
        public IReadOnlyList<string> Lines { get; }
        public string? Value { get; }
        public bool Mono { get; }
        public RoomKind Mark { get; }
        /// <summary>A run with changes after it: drawn dashed with "Out of Date".</summary>
        public bool Stale { get; }
    }

    /// <summary>A line between two nodes, in the class the source gives that link.</summary>
    public sealed class DiagramLink
    {
        public DiagramLink(string from, string to, DiagramClass kind) => (From, To, Class) = (from, to, kind);

        public string From { get; }
        public string To { get; }
        public DiagramClass Class { get; }
    }

    /// <summary>A place: its head, its nodes, and where the read gave nothing for it, its state in words.</summary>
    public sealed class DiagramPlace
    {
        public DiagramPlace(DiagramPlaceKind kind, IReadOnlyList<DiagramNode> nodes, string? state = null, string? why = null, IReadOnlyList<(string Head, string State)>? apart = null) =>
            (Kind, Nodes, State, Why, Apart) = (kind, nodes, state, why, apart ?? new (string, string)[0]);

        public DiagramPlaceKind Kind { get; }
        public string Head => EvidenceDiagram.HeadOf(Kind);
        public IReadOnlyList<DiagramNode> Nodes { get; }
        public string? State { get; }
        public string? Why { get; }
        /// <summary>A part of the place not read yet, said in words (HOW's "What It Touches": "Not Read Yet").</summary>
        public IReadOnlyList<(string Head, string State)> Apart { get; }
    }

    /// <summary>
    /// Salidium's understanding of a run as the evidence view's dynamic diagram (the owner's 2026-11-18 02:24Z shape; plan v2,
    /// AUDIT's 9): YOU ASKED, then its four brief moves as places, CHANGES, HOW, CHECKS and LEFT TO DO, then the VERDICT, with WHY
    /// apart, joined by one route in that order. Every node is one field of the session report (or the person's own instruction
    /// from our journal), and every node and link carries the class Salidium gives it; nothing is drawn that no read gave. Ids are
    /// stable, so the view grows the diagram node by node as Salidium's evidence sequence grows (<see cref="Sequence"/>).
    /// </summary>
    public sealed class EvidenceDiagram
    {
        /// <summary>The diagram's words (AUDIT2's 02:22Z and 02:25Z).</summary>
        public static class Words
        {
            public const string YouAsked = "You Asked", Changes = "Changes", How = "How", Checks = "Checks", LeftToDo = "Left to Do", Verdict = "Verdict", Why = "Why";
            public const string WhatItTouches = "What It Touches", NotReadYet = "Not Read Yet", NotCheckedSince = "Not Checked Since", OutOfDate = "Out of Date";
            public const string Root = "Root", Commits = "Commits", Review = "Review", Open = "Open", Resolved = "Resolved";
            public const string NoExplanation = "No Explanation", Explaining = "Explaining", ExplanationsOff = "Explanations Off",
                ExplanationUnavailable = "Explanation Unavailable", ExplanationFailed = "Explanation Failed";
        }

        public static string HeadOf(DiagramPlaceKind kind) => kind switch
        {
            DiagramPlaceKind.Asked => Words.YouAsked,
            DiagramPlaceKind.Changes => Words.Changes,
            DiagramPlaceKind.How => Words.How,
            DiagramPlaceKind.Checks => Words.Checks,
            DiagramPlaceKind.LeftToDo => Words.LeftToDo,
            DiagramPlaceKind.Verdict => Words.Verdict,
            _ => Words.Why,
        };

        /// <summary>Each class's word, for the key and the labels: never a mark without its word (AUDIT2's 02:25Z).</summary>
        public static string? WordOf(DiagramClass kind) => kind switch
        {
            DiagramClass.Observed => "Observed",
            DiagramClass.Reported => "Reported",
            DiagramClass.Inferred => "Inferred",
            DiagramClass.Planned => "Planned",
            DiagramClass.Explained => "Explained",
            _ => null,
        };

        private EvidenceDiagram(IReadOnlyList<DiagramPlace> places, IReadOnlyList<DiagramLink> links, long? sequence, EvidencePage? state, bool explanationCurrent = false) =>
            (Places, Links, Sequence, Without, ExplanationCurrent) = (places, links, sequence, state, explanationCurrent);

        /// <summary>Whether the explanation (HOW's steps and WHY) covers the newest evidence; where not, the view dims them.</summary>
        public bool ExplanationCurrent { get; }

        /// <summary>The places in the route's order (WHY last, as it stands apart from the route).</summary>
        public IReadOnlyList<DiagramPlace> Places { get; }

        /// <summary>The lines between nodes in different places (a file to the run that verified it) and within a place (a step to the next).</summary>
        public IReadOnlyList<DiagramLink> Links { get; }

        /// <summary>Salidium's evidence sequence for this read; null without an answer.</summary>
        public long? Sequence { get; }

        /// <summary>Where the read gave no answer: the page's honest state and reason (<see cref="EvidencePage"/>); null for an answer.</summary>
        public EvidencePage? Without { get; }

        /// <summary>
        /// The diagram of <paramref name="response"/> for the task whose instruction was <paramref name="asked"/> (the person's
        /// own words, from our journal; null where none is recorded). Without an answer it is the ask alone and the honest state.
        /// </summary>
        public static EvidenceDiagram Of(string? asked, UnderstandingResponse? response)
        {
            var places = new List<DiagramPlace>();
            if (!string.IsNullOrWhiteSpace(asked))
                places.Add(new DiagramPlace(DiagramPlaceKind.Asked, new[] { new DiagramNode("asked", LineWords.Theirs(asked), DiagramClass.None) }));
            if (response?.Result is not AvailableUnderstanding { Understanding: Understanding understood } || understood.Verdict == null)
                return new EvidenceDiagram(places, new DiagramLink[0], null, EvidencePage.Of(response));
            var links = new List<DiagramLink>();
            var runs = understood.Verification?.LatestByMethod ?? new List<UnderstandingVerificationRun>();

            var changes = new List<DiagramNode>();
            foreach (var file in understood.Changes?.Files ?? new List<UnderstandingChangedFile>())
            {
                var lines = new List<string>();
                if (file.LinesAdded > 0) lines.Add("+" + Count(file.LinesAdded));
                if (file.LinesRemoved > 0) lines.Add("-" + Count(file.LinesRemoved));
                var verifiedBy = file.Coverage is { VerifiedAfter: true, By: string by } ? runs.FirstOrDefault(run => run.Label == by) : null;
                // A file no check covered says so; one the source says a run verified joins that run, in the class Salidium fixes
                // for coverage: inferred (consumer-contract schemas.ts:228).
                if (file.Coverage?.VerifiedAfter != true) lines.Add(Words.NotCheckedSince);
                else if (verifiedBy != null) links.Add(new DiagramLink("file:" + file.Path, "run:" + verifiedBy.Label, DiagramClass.Inferred));
                changes.Add(new DiagramNode("file:" + file.Path, LineWords.Theirs(file.Path), DiagramClass.Observed, lines, mono: true));
            }
            if (understood.Changes?.Commits is { Count: > 0 } commits)
                changes.Add(new DiagramNode("commits", LineWords.Ours(Words.Commits), DiagramClass.Observed, value: Count(commits.Count)));
            places.Add(new DiagramPlace(DiagramPlaceKind.Changes, changes));

            var explanation = understood.Explanation;
            var content = explanation?.Content;
            var how = new List<DiagramNode>();
            var why = new List<DiagramNode>();
            if (content != null)
            {
                string? last = null;
                void Chain(List<DiagramNode> into, string id, LineWords words, bool mono = false, IReadOnlyList<string>? lines = null)
                {
                    into.Add(new DiagramNode(id, words, DiagramClass.Explained, lines, mono: mono));
                    if (last != null) links.Add(new DiagramLink(last, id, DiagramClass.Explained));
                    last = id;
                }
                if (content.How?.Root is string root) Chain(how, "how:root", LineWords.Theirs(root), mono: true, lines: new[] { Words.Root });
                for (var step = 0; step < (content.How?.Steps.Count ?? 0); step++) Chain(how, "how:" + step, LineWords.Theirs(content.How!.Steps[step]));
                if (content.ApproachChange is UnderstandingExplanationContentApproachChange change)
                {
                    last = null;
                    Chain(how, "approach:from", LineWords.Theirs(change.From));
                    Chain(how, "approach:to", LineWords.Theirs(change.To), lines: new[] { change.Why });
                }
                foreach (var lane in content.Why?.Lanes ?? new List<UnderstandingExplanationLane>())
                {
                    last = null;
                    Chain(why, "lane:" + lane.Title, LineWords.Theirs(lane.Title));
                    for (var step = 0; step < lane.Steps.Count; step++) Chain(why, "lane:" + lane.Title + ":" + step, LineWords.Theirs(lane.Steps[step]));
                }
                last = null;
                for (var step = 0; step < (content.Why?.Chain.Count ?? 0); step++) Chain(why, "chain:" + step, LineWords.Theirs(content.Why!.Chain[step]));
            }
            var explained = content == null ? ExplanationState(explanation?.Status) : null;
            // The footprint (what the change touches) is Salidium's brief, which the plane does not read yet: said, never drawn.
            places.Add(new DiagramPlace(DiagramPlaceKind.How, how, explained, apart: new[] { (Words.WhatItTouches, Words.NotReadYet) }));

            var checks = runs.Select(run =>
            {
                var (word, mark) = run.Outcome switch
                {
                    UnderstandingVerificationRunOutcome.Pass => (RunUnderstanding.Words.Passed, RoomKind.Passed),
                    UnderstandingVerificationRunOutcome.Fail => (RunUnderstanding.Words.Failed, RoomKind.Failed),
                    UnderstandingVerificationRunOutcome.Partial => (RunUnderstanding.Words.PartlyPassed, RoomKind.None),
                    _ => (RunUnderstanding.Words.CantTell, RoomKind.None),
                };
                var lines = new List<string>();
                if (run.Stale) lines.Add(Words.OutOfDate);
                if (run.Counts?.Passed is long passed && run.Counts.Total is long total) lines.Add(Count(passed) + " of " + Count(total) + " Passed");
                else
                {
                    if (run.Counts?.Passed is long alone) lines.Add(Count(alone) + " Passed");
                    if (run.Counts?.Failed is long failed) lines.Add(Count(failed) + " Failed");
                }
                if (run.Exit?.Code is long code) lines.Add("Exit " + code.ToString(CultureInfo.InvariantCulture));
                return new DiagramNode("run:" + run.Label, LineWords.Theirs(run.Label), ClassOf(run.Epistemic), lines, word, mono: true, mark: mark, stale: run.Stale);
            }).ToList();
            places.Add(new DiagramPlace(DiagramPlaceKind.Checks, checks));

            var left = (understood.Remaining?.Items ?? new List<UnderstandingRemainingItem>()).Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => new DiagramNode("left:" + item.Text, LineWords.Theirs(item.Text), ClassOf(item.Epistemic))).ToList();
            if (understood.Review is UnderstandingReview review && review.Open + review.Resolved > 0)
                left.Add(new DiagramNode("review", LineWords.Ours(Words.Review), DiagramClass.Observed,
                    new[] { Count(review.Open) + " " + Words.Open, Count(review.Resolved) + " " + Words.Resolved }));
            places.Add(new DiagramPlace(DiagramPlaceKind.LeftToDo, left));

            var verdict = understood.Verdict;
            // The finding said once (AUDIT's 21:13Z): a reason that only repeats the headline or a run's own name is left to it.
            var because = verdict.Because == null || verdict.Because == verdict.Headline || runs.Any(run => run.Label == verdict.Because) ? null : verdict.Because;
            places.Add(new DiagramPlace(DiagramPlaceKind.Verdict, new[]
            {
                new DiagramNode("verdict", LineWords.Theirs(verdict.Headline), ClassOf(verdict.Epistemic), because == null ? null : new[] { because }),
            }));
            places.Add(new DiagramPlace(DiagramPlaceKind.Why, why, explained));
            return new EvidenceDiagram(places, links, understood.Source?.EvidenceSequence, null, explanation?.Current == true);
        }

        /// <summary>The ids in this diagram and not in <paramref name="before"/>: the nodes the newest read brought, drawn in one by one.</summary>
        public IReadOnlyList<string> NewSince(EvidenceDiagram? before)
        {
            var had = new HashSet<string>(before?.Places.SelectMany(place => place.Nodes).Select(node => node.Id) ?? Enumerable.Empty<string>());
            return Places.SelectMany(place => place.Nodes).Select(node => node.Id).Where(id => !had.Contains(id)).ToList();
        }

        private static DiagramClass ClassOf(UnderstandingEpistemic epistemic) => epistemic switch
        {
            UnderstandingEpistemic.Observed => DiagramClass.Observed,
            UnderstandingEpistemic.Reported => DiagramClass.Reported,
            UnderstandingEpistemic.Inferred => DiagramClass.Inferred,
            UnderstandingEpistemic.Planned => DiagramClass.Planned,
            _ => DiagramClass.Explained,
        };

        private static string? ExplanationState(UnderstandingExplanationStatus? status) => status switch
        {
            UnderstandingExplanationStatus.Generating => Words.Explaining,
            UnderstandingExplanationStatus.Disabled => Words.ExplanationsOff,
            UnderstandingExplanationStatus.Unavailable => Words.ExplanationUnavailable,
            UnderstandingExplanationStatus.Failed => Words.ExplanationFailed,
            // Generated with no content, none asked, or no explanation part at all: there is no explanation to show.
            UnderstandingExplanationStatus.Generated or UnderstandingExplanationStatus.None or null => Words.NoExplanation,
            _ => null,
        };

        private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
