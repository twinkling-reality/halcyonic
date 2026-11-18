#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The story's places, in reading order.</summary>
    public enum StoryPlaceKind { Asked, Wrong, Why, Did, Worked, Left }

    /// <summary>
    /// The story's card forms, inspired by the Cluely ad (the owner's 2026-11-18: inspiration, not a copy): a short status pill; the
    /// person's ask in large bold words; the agent's account as lines that build, the older dim and the newest bright; and the checks'
    /// own result, a large plain answer.
    /// </summary>
    public enum StoryForm { Pill, Lead, Lines, Result }

    /// <summary>
    /// One place of the story: a plain question as its head, one sentence that answers it (the source's own words, or ours where
    /// we say what a check found), steps that follow one another where the answer has them, a line saying where the answer comes
    /// from, and one line in our words that teaches the idea behind it, so a person new to coding learns as they read.
    /// </summary>
    public sealed class StoryPlace
    {
        public StoryPlace(StoryPlaceKind kind, LineWords answer, IReadOnlyList<LineWords>? steps = null, string? from = null, string? note = null, LineWords? source = null, string? head = null) =>
            (Kind, Answer, Steps, From, Note, Source, heading) = (kind, answer, steps ?? new LineWords[0], from, note, source, head);

        private readonly string? heading;

        /// <summary>Where the answer is the agent's plain sentence, the source's own sentence for this place, kept for one press in; null otherwise.</summary>
        public LineWords? Source { get; }

        public StoryPlaceKind Kind { get; }
        /// <summary>The place's plain question, or a fan card's own heading ("Step 1").</summary>
        public string Head => heading ?? EvidenceStory.HeadOf(Kind);
        public LineWords Answer { get; }
        /// <summary>Steps that follow one another (a cause and its effect, what the AI did in order), drawn joined; empty for none.</summary>
        public IReadOnlyList<LineWords> Steps { get; }
        /// <summary>Where the answer comes from, in plain words ("The AI's own explanation", "Checked by running the tests"); null for the person's own words.</summary>
        public string? From { get; }
        /// <summary>A plain fact under the answer (how many tests, how many files); null for none.</summary>
        public string? Note { get; }
        public string Teach => EvidenceStory.TeachOf(Kind);
    }

    /// <summary>
    /// What the agent did, as a story anyone can follow (the owner's 2026-11-18 direction: "easy for a non technical person to
    /// understand what's happening, how, why ... so a non tech person can ease their way into coding"): You Asked, What Was Wrong,
    /// Why, What the AI Did, Did It Work?, What's Left. Every answer is a real field of the reads (Salidium's own sentences for the
    /// problem, its cause and its fix; its check runs for whether it worked; its remaining items), never made up; a place the read
    /// does not answer is left out. File paths, commands and the source's classes stay off the face. Our words are the heads, the
    /// teaching lines and the plain answer to whether it worked.
    /// </summary>
    public sealed class EvidenceStory
    {
        /// <summary>The story's words, ours.</summary>
        public static class Words
        {
            // AUDIT2's 03:02Z MUSTs: the places say what each holds (What It Did, Why, How It Works) and the check asks what it can answer.
            public const string YouAsked = "What You Asked", Wrong = "What It Did", Why = "Why", Did = "How It Works", Worked = "Did the Checks Pass?", Left = "What's Left";
            /// <summary>The agent's account's title, over its lines.</summary>
            public const string Account = "What the AI Did";
            /// <summary>The run's state as the status pill says it, plain.</summary>
            public const string Done = "Done", Working = "Working", Checking = "Checking", NeedsYou = "Needs You", Stopped = "Stopped", Starting = "Starting";
            public const string TeachAsked = "What you asked the AI to do, in your own words.";
            public const string TeachWrong = "Before it changes anything, the AI works out what the problem is.";
            public const string TeachWhy = "Finding the cause first keeps the fix small and right.";
            public const string TeachDid = "A change is lines added to or removed from the project's files.";
            public const string TeachWorked = "A test is a small program that checks the work does what it should.";
            public const string TeachLeft = "Work the AI noticed but has not done yet.";
            /// <summary>How each answer is known, in plain words (AUDIT2's 02:50Z for Salidium's five classes): never a code.</summary>
            public const string Seen = "Seen happen", AgentSays = "The AI Says", WorkedOut = "Worked out from what was seen", Planned = "Planned, not done yet",
                Summary = "Summary written afterwards", Practice = "Practice run, not real";
            public const string Yes = "Yes", No = "No", Partly = "Partly", NotChecked = "Not Checked Yet";
            public const string Changed = "It changed {0} files.", ChangedOne = "It changed 1 file.";
        }

        public static string HeadOf(StoryPlaceKind kind) => kind switch
        {
            StoryPlaceKind.Asked => Words.YouAsked,
            StoryPlaceKind.Wrong => Words.Wrong,
            StoryPlaceKind.Why => Words.Why,
            StoryPlaceKind.Did => Words.Did,
            StoryPlaceKind.Worked => Words.Worked,
            _ => Words.Left,
        };

        public static string TeachOf(StoryPlaceKind kind) => kind switch
        {
            StoryPlaceKind.Asked => Words.TeachAsked,
            StoryPlaceKind.Wrong => Words.TeachWrong,
            StoryPlaceKind.Why => Words.TeachWhy,
            StoryPlaceKind.Did => Words.TeachDid,
            StoryPlaceKind.Worked => Words.TeachWorked,
            _ => Words.TeachLeft,
        };

        private EvidenceStory(IReadOnlyList<StoryPlace> places, long? sequence, EvidencePage? without) => (Places, Sequence, Without) = (places, sequence, without);

        public IReadOnlyList<StoryPlace> Places { get; }

        /// <summary>Salidium's evidence sequence for this read: a higher one is a newer read; null without an answer.</summary>
        public long? Sequence { get; }

        /// <summary>Where the read gave no answer: its honest state and reason; null for an answer.</summary>
        public EvidencePage? Without { get; }

        /// <summary>The story of <paramref name="response"/> for the run whose instruction was <paramref name="asked"/> (the person's own words).</summary>
        public static EvidenceStory Of(string? asked, UnderstandingResponse? response, ClosingWords? closing = null)
        {
            var places = new List<StoryPlace>();
            if (!string.IsNullOrWhiteSpace(asked)) places.Add(new StoryPlace(StoryPlaceKind.Asked, LineWords.Theirs(asked)));
            if (response?.Result is not AvailableUnderstanding { Understanding: Understanding understood } || understood.Verdict == null)
                return new EvidenceStory(places, null, EvidencePage.Of(response));

            var content = understood.Explanation?.Content;
            if (content?.What?.Summary is string problem && !string.IsNullOrWhiteSpace(problem))
                places.Add(new StoryPlace(StoryPlaceKind.Wrong, LineWords.Theirs(problem), from: Summary(understood)));
            if (content?.Why is UnderstandingExplanationContentWhy why && (!string.IsNullOrWhiteSpace(why.Summary) || why.Chain.Count > 0))
                places.Add(new StoryPlace(StoryPlaceKind.Why, LineWords.Theirs(why.Summary), why.Chain.Where(step => !string.IsNullOrWhiteSpace(step)).Select(step => LineWords.Theirs(step)).ToList(), Summary(understood)));

            var files = understood.Changes?.Files.Count ?? 0;
            var changed = files == 0 ? null : files == 1 ? Words.ChangedOne : string.Format(CultureInfo.InvariantCulture, Words.Changed, files);
            if (content?.How is UnderstandingExplanationContentHow how && (!string.IsNullOrWhiteSpace(how.Summary) || how.Steps.Count > 0))
                places.Add(new StoryPlace(StoryPlaceKind.Did, LineWords.Theirs(how.Summary), how.Steps.Where(step => !string.IsNullOrWhiteSpace(step)).Select(step => LineWords.Theirs(step)).ToList(), Summary(understood), changed));
            else if (changed != null)
                places.Add(new StoryPlace(StoryPlaceKind.Did, LineWords.Ours(changed)));

            places.Add(Worked(understood));

            var left = (understood.Remaining?.Items ?? new List<UnderstandingRemainingItem>()).Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToList();
            if (left.Count > 0)
                places.Add(new StoryPlace(StoryPlaceKind.Left, LineWords.Theirs(left[0].Text), left.Skip(1).Select(item => LineWords.Theirs(item.Text)).ToList(), KnownBy(left[0].Epistemic, understood)));
            return new EvidenceStory(closing == null ? places : places.Select(place => Plain(place, closing)).ToList(), understood.Source?.EvidenceSequence, null);
        }

        /// <summary>
        /// The agent's own plain sentences for someone who has never coded, asked of it at the end of each run (the lead's closing
        /// request, 03:0xZ): one per place, null where it gave none. Never the ask (the person's own words), never the check.
        /// </summary>
        public sealed class ClosingWords
        {
            public ClosingWords(string? did, string? why, string? how, string? left) => (Did, Why, How, Left) = (did, why, how, left);
            /// <summary>What it did (the What It Did place).</summary>
            public string? Did { get; }
            /// <summary>Why it did it that way (Why).</summary>
            public string? Why { get; }
            /// <summary>How it works now (How It Works).</summary>
            public string? How { get; }
            /// <summary>What is left to do (What's Left).</summary>
            public string? Left { get; }
        }

        /// <summary>A place answered in the agent's plain sentence where it gave one, said as the agent's; else as the source said it.</summary>
        private static StoryPlace Plain(StoryPlace place, ClosingWords closing)
        {
            var plain = place.Kind switch
            {
                StoryPlaceKind.Wrong => closing.Did,
                StoryPlaceKind.Why => closing.Why,
                StoryPlaceKind.Did => closing.How,
                StoryPlaceKind.Left => closing.Left,
                StoryPlaceKind.Asked or StoryPlaceKind.Worked => null,
                _ => null,
            };
            return string.IsNullOrWhiteSpace(plain) ? place : new StoryPlace(place.Kind, LineWords.Theirs(plain), place.Steps, Words.AgentSays, place.Note, place.Answer);
        }

        /// <summary>
        /// Did it work: our plain answer from the source's own check runs, never a guess. A run that failed says not yet; one that
        /// passed and is not out of date says yes; partly where only some passed; with no run, not checked yet. The note is the
        /// run's own count of tests.
        /// </summary>
        private static StoryPlace Worked(Understanding understood)
        {
            var runs = (understood.Verification?.LatestByMethod ?? new List<UnderstandingVerificationRun>()).Where(run => !run.Stale).ToList();
            string answer;
            if (runs.Count == 0) answer = Words.NotChecked;
            else if (runs.Any(run => run.Outcome == UnderstandingVerificationRunOutcome.Fail)) answer = Words.No;
            else if (runs.All(run => run.Outcome == UnderstandingVerificationRunOutcome.Pass)) answer = Words.Yes;
            else if (runs.Any(run => run.Outcome is UnderstandingVerificationRunOutcome.Pass or UnderstandingVerificationRunOutcome.Partial)) answer = Words.Partly;
            else answer = Words.NotChecked;
            var passed = runs.Sum(run => run.Counts?.Passed ?? 0);
            var failed = runs.Sum(run => run.Counts?.Failed ?? 0);
            var counted = runs.Any(run => run.Counts?.Passed != null || run.Counts?.Failed != null);
            string? note = null;
            if (counted && failed > 0) note = Count(passed) + " tests passed, " + Count(failed) + " failed.";
            else if (counted && passed > 0) note = Count(passed) + (passed == 1 ? " test passed." : " tests passed.");
                        // How the answer is known: the class the source gives the runs, the surest first.
            var known = runs.Count == 0 ? null : KnownBy(runs.Select(run => run.Epistemic).OrderBy(Rank).First(), understood);
            return new StoryPlace(StoryPlaceKind.Worked, LineWords.Ours(answer), from: known, note: note);
        }

        private static int Rank(UnderstandingEpistemic epistemic) => epistemic switch
        {
            UnderstandingEpistemic.Observed => 0,
            UnderstandingEpistemic.Inferred => 1,
            UnderstandingEpistemic.Reported => 2,
            UnderstandingEpistemic.Planned => 3,
            UnderstandingEpistemic.Explained => 4,
            _ => 5,
        };

        /// <summary>How a value is known, in plain words; a stand-in's source says it is practice, whatever the class.</summary>
        private static string KnownBy(UnderstandingEpistemic epistemic, Understanding understood) => understood.Source?.Synthetic == true ? Words.Practice : epistemic switch
        {
            UnderstandingEpistemic.Observed => Words.Seen,
            UnderstandingEpistemic.Reported => Words.AgentSays,
            UnderstandingEpistemic.Inferred => Words.WorkedOut,
            UnderstandingEpistemic.Planned => Words.Planned,
            UnderstandingEpistemic.Explained => Words.Summary,
            _ => Words.Summary,
        };

        /// <summary>The explanation's own class: a summary written afterwards, or practice where the source is a stand-in.</summary>
        private static string Summary(Understanding understood) => KnownBy(UnderstandingEpistemic.Explained, understood);

        private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
