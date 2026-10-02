#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What Help me understand answers, one at a time.</summary>
    public enum UnderstandPrompt
    {
        /// <summary>Each changed file, how it changed and by how many lines, as the source observed.</summary>
        WhatChanged,

        /// <summary>The reasons the agent gave before each change, quoted and attributed.</summary>
        WhyChanged,

        /// <summary>The source's explanation of the work, stepped through, when one was written; else why not, and the evidence.</summary>
        HowBuilt,
    }

    /// <summary>
    /// Turns what the understanding source concluded about an execution into the answers of Help me
    /// understand. Every claim keeps the source's epistemic class, the agent's words stay a quote, a
    /// model's explanation is said to be one and whether it covers the latest evidence, a value the
    /// source does not have is said to be unknown, and every text from the source is plain text
    /// (<see cref="LabelText.Plain"/>). Nothing here asks for an explanation to be written.
    /// </summary>
    public static class UnderstandingPresenter
    {
        public static SectionPresentation Present(UnderstandPrompt prompt, IntelligenceFeed<UnderstandingResponse> feed, DateTimeOffset now, TimeZoneInfo zone,
            AnswerRoom? room = null) =>
            Present(prompt, feed.ExecutionId, feed.Last, feed.Loading, feed.Error, now, zone, room);

        /// <param name="now">This device's time; a recorded answer is described as of when it was recorded.</param>
        /// <param name="room">The rows a page holds: a list keeps its most telling lines and counts the rest; a flow pages. Unlimited when null.</param>
        public static SectionPresentation Present(
            UnderstandPrompt prompt,
            string? executionId,
            IntelligenceRead<UnderstandingResponse>? read,
            bool loading,
            string? error,
            DateTimeOffset now,
            TimeZoneInfo zone,
            AnswerRoom? room = null)
        {
            room ??= AnswerRoom.Unlimited;
            if (executionId == null) return IntelligenceText.Empty(SectionKind.Understanding, "Nothing to understand until work starts.");
            if (read == null) return IntelligenceText.Waiting(SectionKind.Understanding, loading, error);
            var status = IntelligenceText.ReadingStatus(loading, error);
            if (!(read.Response.Result is AvailableUnderstanding available)) return Failure(SectionKind.Understanding, read.Response.Result, status);
            var understanding = available.Understanding;
            var source = understanding.Source;
            var provenance = IntelligenceText.UnderstandingProvenance(read, source, now, zone) + status;
            var tone = source.Synthetic ? SectionTone.Attention : SectionTone.Secondary;
            var lines = prompt switch
            {
                UnderstandPrompt.WhatChanged => WhatChanged(understanding, room),
                UnderstandPrompt.WhyChanged => WhyChanged(understanding, room),
                _ => HowBuilt(understanding, zone, room),
            };
            var steps = prompt == UnderstandPrompt.HowBuilt && Flow(understanding.Explanation) != null;
            return new SectionPresentation(SectionKind.Understanding, provenance, tone, lines, source.Synthetic, steps);
        }

        /// <summary>The section an understanding that is not available becomes: why there is none, said in words.</summary>
        internal static SectionPresentation Failure(SectionKind kind, UnderstandingResult result, string status) => result switch
        {
            NotFoundUnderstanding notFound => IntelligenceText.Failure(kind, IntelligenceText.FromSalidium, "No understanding yet: ", notFound.Reason, SectionTone.Secondary, status),
            UnavailableUnderstanding unavailable => IntelligenceText.Failure(kind, IntelligenceText.FromSalidium, "Understanding unavailable: ", unavailable.Reason, SectionTone.Secondary, status),
            IncompatibleUnderstanding incompatible => IntelligenceText.Failure(kind, IntelligenceText.FromSalidium, "Understanding unreadable: ", incompatible.Reason, SectionTone.Problem, status),
            UnauthorizedUnderstanding unauthorized => IntelligenceText.Failure(kind, IntelligenceText.FromSalidium, "Understanding not allowed: ", unauthorized.Reason, SectionTone.Attention, status),
            _ => IntelligenceText.Empty(kind, "The understanding came back in a form this app does not know."),
        };

        /// <summary>
        /// What changed?: how many files changed and how, then each file with how it changed and its
        /// lines, most recently changed first, as many as fit and the rest counted; whether a check
        /// ran after the changes; then, where there is room, the commits and what is not done yet.
        /// </summary>
        private static IReadOnlyList<SectionLine> WhatChanged(Understanding understanding, AnswerRoom room)
        {
            var changes = understanding.Changes;
            var page = room.Page();
            // The source observes paths, counts and kinds.
            page.Add(new SectionLine("observed", Count(changes), SectionTone.Normal));
            if (Revision(understanding.Revision) is string revision) page.Add(new SectionLine("observed", revision, SectionTone.Secondary));
            var shown = Shown(changes.Files);
            var files = changes.Files.Select((file, index) =>
                new SectionLine("observed", KindOf(file.Kinds) + ": " + shown[index] + " " + LinesOf(file), SectionTone.Normal)).ToList();
            var coverage = Coverage(understanding);
            page.AddCounted(files, more => new SectionLine("", "And " + IntelligenceText.Plural(more, "more file"), SectionTone.Secondary),
                reserve: coverage);
            if (coverage != null) page.Add(coverage);
            if (changes.Commits.Count > 0)
            {
                page.AddIfRoom(new SectionLine("observed", IntelligenceText.Plural(changes.Commits.Count, "commit") + ": "
                    + string.Join(", ", changes.Commits.Select(commit => ShortSha(commit.Sha))), SectionTone.Normal));
            }
            foreach (var item in understanding.Remaining.Items) page.AddIfRoom(Remaining(item));
            return page.Lines;
        }

        /// <summary>
        /// Why did it change?: what the agent said before each change, quoted, in the order it said
        /// it, with the files it changed next, as many as fit and the rest counted. The source binds a
        /// reason to a file by time, so the files are said to be changed after it, never that the
        /// reason is theirs.
        /// </summary>
        private static IReadOnlyList<SectionLine> WhyChanged(Understanding understanding, AnswerRoom room)
        {
            var files = understanding.Changes.Files;
            var page = room.Page();
            if (files.Count == 0)
            {
                page.Add(new SectionLine("observed", "No files changed, so there are no reasons to show.", SectionTone.Secondary));
                return page.Lines;
            }
            var reasons = files
                .Where(file => file.Reason != null)
                .GroupBy(file => (file.Reason!.Text, file.Reason.At, file.Reason.Author, file.Reason.Epistemic))
                .OrderBy(group => group.Key.At == null ? DateTimeOffset.MaxValue : At(group.Key.At))
                .Select(reason =>
                {
                    var statement = reason.First().Reason!;
                    return (IReadOnlyList<SectionLine>)new[]
                    {
                        new SectionLine(Word(statement.Epistemic), Quote(statement), SectionTone.Claim, rows: 2),
                        new SectionLine("", "Said before it changed " + Names(reason.Select(file => file.Path)), SectionTone.Secondary, detail: true, rows: 1),
                    };
                })
                .ToList();
            var unexplained = files.Where(file => file.Reason == null).Select(file => file.Path).ToList();
            var none = unexplained.Count == 0
                ? null
                : new SectionLine("", "No reason given before it changed " + Names(unexplained), SectionTone.Secondary, rows: 2);
            page.AddCounted(reasons, more => new SectionLine("", "And " + IntelligenceText.Plural(more, "more reason") + " it gave", SectionTone.Secondary),
                reserve: none);
            if (none != null) page.Add(none);
            return page.Lines;
        }

        /// <summary>
        /// How it was built: the source's explanation as a flow, a page for each step: what the work is
        /// about, why, each lane of the why, how, a change of approach, then the evidence. Every step
        /// says it is a model's explanation and whether it covers the latest evidence. Without one, why
        /// there is none, and the evidence.
        /// </summary>
        private static IReadOnlyList<SectionLine> HowBuilt(Understanding understanding, TimeZoneInfo zone, AnswerRoom room)
        {
            var explanation = understanding.Explanation;
            var content = Flow(explanation);
            var lines = new List<SectionLine>();
            if (content == null)
            {
                var page = room.Page();
                page.Add(new SectionLine("explained", NoFlow(explanation.Status), SectionTone.Secondary, rows: 2));
                page.Add(new SectionLine("", "The evidence", SectionTone.Normal));
                Evidence(page, understanding, zone);
                return page.Lines;
            }
            // Each step's heading says what it is, that a model wrote it, and whether it covers the
            // latest evidence, in one line that heads every page of the step.
            var note = explanation.Current ? " · explained by a model, up to date" : " · explained by a model before the latest evidence";
            var noteTone = explanation.Current ? SectionTone.Secondary : SectionTone.Attention;
            void Step(string title, string? theirs, IEnumerable<SectionLine> body)
            {
                lines.Add(new SectionLine("", title + note, noteTone, startsPage: true, repeats: true));
                if (theirs != null) lines.Add(new SectionLine("explained", IntelligenceText.Plain(theirs), SectionTone.Claim, repeats: true));
                lines.AddRange(body);
            }
            SectionLine Claim(string text, int rows = 2) => new SectionLine("explained", text, SectionTone.Claim, rows: rows);
            IEnumerable<SectionLine> Numbered(IReadOnlyList<string> steps) =>
                steps.Select((step, index) => Claim((index + 1).ToString(CultureInfo.InvariantCulture) + ". " + IntelligenceText.Plain(step), rows: 1));

            var what = new List<SectionLine> { Claim(IntelligenceText.Plain(content.What.Summary)) };
            if (content.What.Currently != null) what.Add(Claim("Now: " + IntelligenceText.Plain(content.What.Currently)));
            Step("What it is about", null, what);

            var why = new List<SectionLine> { Claim(IntelligenceText.Plain(content.Why.Summary)) };
            why.AddRange(Numbered(content.Why.Chain));
            Step("Why", null, why);
            foreach (var lane in content.Why.Lanes)
            {
                Step("Why, part by part", lane.Title, Numbered(lane.Steps));
            }

            var how = new List<SectionLine> { Claim(IntelligenceText.Plain(content.How.Summary)) };
            if (content.How.Root != null) how.Add(Claim("Starts at " + IntelligenceText.Plain(content.How.Root), rows: 1));
            how.AddRange(Numbered(content.How.Steps));
            Step("How", null, how);

            var change = content.ApproachChange;
            if (change != null)
            {
                Step("A change of approach", null, new[]
                {
                    Claim("At first: " + IntelligenceText.Plain(change.From)),
                    Claim("Then: " + IntelligenceText.Plain(change.To)),
                    Claim("Because: " + IntelligenceText.Plain(change.Why), rows: 3),
                });
            }

            var evidence = room.Page();
            evidence.Add(new SectionLine("", "The evidence", SectionTone.Normal, startsPage: true, repeats: true));
            Evidence(evidence, understanding, zone);
            lines.AddRange(evidence.Lines);
            return lines;
        }

        /// <summary>The explanation's content when there is a flow to step through, else null.</summary>
        private static UnderstandingExplanationContent? Flow(UnderstandingExplanation explanation) =>
            explanation.Status == UnderstandingExplanationStatus.Generated ? explanation.Content : null;

        /// <summary>Why there is no flow, in words. No explanation is ever asked for from here.</summary>
        private static string NoFlow(UnderstandingExplanationStatus status) => status switch
        {
            UnderstandingExplanationStatus.Generating => "An explanation is being written. Until it is, here is the evidence.",
            UnderstandingExplanationStatus.Disabled => "Explanations are turned off on " + HostText.Your + ", so there's none for this work.",
            UnderstandingExplanationStatus.Failed => "The explanation could not be written.",
            UnderstandingExplanationStatus.None => "No explanation was written for this work.",
            _ => "No explanation can be written now.",
        };

        /// <summary>
        /// What was observed, set beside an explanation and never as proof of it, on one page: the
        /// changes, whether a check ran after them, and the checks, as many as fit and the rest counted.
        /// </summary>
        private static void Evidence(AnswerPage page, Understanding understanding, TimeZoneInfo zone)
        {
            page.Add(new SectionLine("observed", Count(understanding.Changes), SectionTone.Normal));
            var coverage = Coverage(understanding);
            if (coverage != null) page.Add(coverage);
            var runs = Runs(understanding);
            if (runs.Count == 0) page.Add(new SectionLine("", IntelligenceText.Plain(understanding.Verification.Summary), SectionTone.Secondary));
            page.AddCounted(runs.Select(run => Run(run, zone)).ToList(),
                more => new SectionLine("", "And " + IntelligenceText.Plural(more, "more check"), SectionTone.Secondary));
        }

        /// <summary>"3 files changed: 1 new, 2 edited", or the source's own words when none did.</summary>
        internal static string Count(UnderstandingChanges changes)
        {
            if (changes.Files.Count == 0) return IntelligenceText.Plain(changes.Summary);
            var kinds = changes.Files
                .GroupBy(file => KindOf(file.Kinds))
                .OrderBy(group => KindOrder(group.Key))
                .Select(group => group.Count().ToString(CultureInfo.InvariantCulture) + " " + Lower(group.Key));
            return IntelligenceText.Plural(changes.Files.Count, "file") + " changed: " + string.Join(", ", kinds);
        }

        /// <summary>
        /// How a file changed over the work, in a word: New, Edited, Removed or Moved, Moved and
        /// edited, or each way it changed when it changed in more than one.
        /// </summary>
        public static string KindOf(IReadOnlyCollection<UnderstandingChangeKind> kinds)
        {
            var add = kinds.Contains(UnderstandingChangeKind.Add);
            var update = kinds.Contains(UnderstandingChangeKind.Update);
            var delete = kinds.Contains(UnderstandingChangeKind.Delete);
            var move = kinds.Contains(UnderstandingChangeKind.Move);
            if (!add && !update && !delete && !move) return "Changed";
            // A file made in this work and then edited is still new; one edited then removed is removed.
            if (add && !delete && !move) return "New";
            if (delete && !add && !move) return "Removed";
            if (move && !add && !delete) return update ? "Moved and edited" : "Moved";
            if (update && !add && !delete && !move) return "Edited";
            var words = new List<string>();
            if (add) words.Add("new");
            if (move) words.Add("moved");
            if (update) words.Add("edited");
            if (delete) words.Add("removed");
            var joined = string.Join(", ", words);
            return char.ToUpperInvariant(joined[0]) + joined.Substring(1);
        }

        private static int KindOrder(string kind) => kind switch
        {
            "New" => 0,
            "Edited" => 1,
            "Moved and edited" => 2,
            "Moved" => 3,
            "Removed" => 4,
            _ => 5,
        };

        private static string Lower(string word) => word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word.Substring(1);

        /// <summary>A file's lines, "(+38 −9)", with the minus sign the source's summaries use.</summary>
        /// <summary>
        /// A file's lines, "(+38 −9)", with the minus sign the source's summaries use; "(+38 −9 or
        /// more)" where the source saw a change replace the file without what it held.
        /// </summary>
        private static string LinesOf(UnderstandingChangedFile file) =>
            "(+" + file.LinesAdded.ToString(CultureInfo.InvariantCulture) + " −" + file.LinesRemoved.ToString(CultureInfo.InvariantCulture)
            + (file.LinesRemovedExact == false ? " or more" : "") + ")";

        /// <summary>
        /// Each changed file as the person reads it: by its path in its repository where the source
        /// resolved one, else by its name with as many folders as tell it apart from the others.
        /// </summary>
        private static IReadOnlyList<string> Shown(IReadOnlyList<UnderstandingChangedFile> files)
        {
            var tails = PathTails(files.Select(file => file.Path).ToList());
            return files.Select((file, index) => file.RepositoryPath is string inRepository ? IntelligenceText.Plain(inRepository) : tails[index]).ToList();
        }

        /// <summary>
        /// Which commit the work started from and stands at, as the source saw it at the session's
        /// boundaries: "From commit 3f9a2c1 to 8b1e4d7 on main". Null when the source saw neither.
        /// </summary>
        internal static string? Revision(UnderstandingRevision revision)
        {
            var start = revision.AtStart;
            var latest = revision.AtLatestTurnEnd;
            string Commit(UnderstandingRevisionAnchor anchor) => anchor.Head == null ? "a repository with no commits yet" : "commit " + ShortSha(anchor.Head);
            string On(UnderstandingRevisionAnchor anchor) => anchor.Branch == null ? "" : " on " + IntelligenceText.Plain(anchor.Branch);
            // A repository with no commits yet has no commit to stand at: "In a repository with no
            // commits yet, on main".
            string Empty(UnderstandingRevisionAnchor anchor) =>
                "a repository with no commits yet" + (anchor.Branch == null ? "" : ", on " + IntelligenceText.Plain(anchor.Branch));
            if (start != null && latest != null)
            {
                if (start.Head == latest.Head)
                {
                    return latest.Head == null ? "In " + Empty(latest) : "At " + Commit(latest) + On(latest) + ", where it started";
                }
                if (latest.Head == null) return "In " + Empty(latest);
                var startBranch = start.Branch == latest.Branch ? "" : On(start);
                return "From " + Commit(start) + startBranch + " to " + ShortSha(latest.Head) + On(latest);
            }
            if (latest != null) return latest.Head == null ? "In " + Empty(latest) : "At " + Commit(latest) + On(latest);
            if (start != null) return start.Head == null ? "Started in " + Empty(start) : "Started at " + Commit(start) + On(start);
            return null;
        }

        /// <summary>
        /// Each path by its file name, with as many of its folders as tell it apart from the others',
        /// so two files of one name never read as one.
        /// </summary>
        internal static IReadOnlyList<string> PathTails(IReadOnlyList<string> paths)
        {
            var parts = paths.Select(path => IntelligenceText.Plain(path).Replace('\\', '/').Trim('/').Split('/')).ToList();
            var depth = Enumerable.Repeat(1, parts.Count).ToArray();
            string Tail(int index) => string.Join("/", parts[index].Skip(Math.Max(0, parts[index].Length - depth[index])));
            for (var round = 0; round < 64; round++)
            {
                var clashes = Enumerable.Range(0, parts.Count)
                    .GroupBy(Tail)
                    .Where(group => group.Count() > 1)
                    .SelectMany(group => group)
                    .Where(index => depth[index] < parts[index].Length)
                    .ToList();
                if (clashes.Count == 0) break;
                foreach (var index in clashes) depth[index]++;
            }
            return Enumerable.Range(0, parts.Count).Select(Tail).ToList();
        }

        /// <summary>Files by name, the first few, then how many more.</summary>
        internal static string Names(IEnumerable<string> paths)
        {
            var tails = PathTails(paths.ToList());
            const int Shown = 3;
            if (tails.Count <= Shown) return string.Join(", ", tails.Take(tails.Count - 1)) + (tails.Count > 1 ? " and " : "") + tails[tails.Count - 1];
            return string.Join(", ", tails.Take(Shown)) + " and " + IntelligenceText.Plural(tails.Count - Shown, "more file");
        }

        private static string ShortSha(string sha)
        {
            var plain = IntelligenceText.Plain(sha);
            return plain.Length > 7 ? plain.Substring(0, 7) : plain;
        }

        /// <summary>Whether a check ran after every change, which the source infers; null when nothing changed.</summary>
        internal static SectionLine? Coverage(Understanding understanding)
        {
            var files = understanding.Changes.Files;
            if (files.Count == 0) return null;
            var unverified = understanding.Verification.UnverifiedFiles;
            var by = files.Select(file => file.Coverage.By).FirstOrDefault(value => value != null);
            var text = unverified.Count == 0
                ? "Every changed file was checked after its last change" + (by == null ? "" : ", by " + IntelligenceText.Plain(by))
                : IntelligenceText.Plural(unverified.Count, "file") + " not checked after the last change: " + Names(unverified);
            // Coverage is always the source's inference.
            return new SectionLine("inferred", text, unverified.Count == 0 ? SectionTone.Normal : SectionTone.Attention);
        }

        /// <summary>The latest run of each kind of check, the earliest first.</summary>
        internal static IReadOnlyList<UnderstandingVerificationRun> Runs(Understanding understanding) =>
            understanding.Verification.LatestByMethod.OrderBy(run => At(run.At)).ToList();

        /// <summary>
        /// A check run: what ran, how it went, when, and the source's words for it; then which change it
        /// ran after, or which files changed since, from the times the source observed.
        /// </summary>
        internal static IEnumerable<SectionLine> Run(UnderstandingVerificationRun run, IReadOnlyList<UnderstandingChangedFile> files, TimeZoneInfo zone)
        {
            yield return Run(run, zone);
            var at = At(run.At);
            var since = files.Where(file => At(file.LastChangedAt) > at).Select(file => file.Path).ToList();
            var before = files.Where(file => At(file.LastChangedAt) <= at).OrderByDescending(file => At(file.LastChangedAt)).FirstOrDefault();
            string after;
            var tone = SectionTone.Secondary;
            if (since.Count > 0)
            {
                after = "Then " + Names(since) + " changed, so it no longer covers " + (since.Count == 1 ? "it" : "them");
                tone = SectionTone.Attention;
            }
            else if (run.Stale)
            {
                after = "Files changed after it ran, so it no longer covers them";
                tone = SectionTone.Attention;
            }
            else if (before != null)
            {
                after = "Ran after the last change, to " + PathTails(new[] { before.Path })[0] + " at "
                    + IntelligenceText.Clock(At(before.LastChangedAt), zone, seconds: false);
            }
            else
            {
                after = "Ran before any file changed";
            }
            if (run.LaterUnreadable > 0) after += "; " + IntelligenceText.Plural(run.LaterUnreadable, "later run") + " couldn't be read";
            yield return new SectionLine("", after, tone, detail: true);
        }

        private static SectionLine Run(UnderstandingVerificationRun run, TimeZoneInfo zone)
        {
            var method = run.Method switch
            {
                UnderstandingVerificationRunMethod.Test => "Tests",
                UnderstandingVerificationRunMethod.Typecheck => "Type check",
                UnderstandingVerificationRunMethod.Lint => "Lint",
                UnderstandingVerificationRunMethod.Build => "Build",
                _ => "Check",
            };
            var (outcome, tone) = run.Outcome switch
            {
                UnderstandingVerificationRunOutcome.Pass => ("passed", SectionTone.Good),
                UnderstandingVerificationRunOutcome.Fail => ("failed", SectionTone.Problem),
                UnderstandingVerificationRunOutcome.Partial => ("partly passed", SectionTone.Attention),
                _ => ("outcome unknown", SectionTone.Secondary),
            };
            var when = IntelligenceText.TryParse(run.At, out var at) ? " at " + IntelligenceText.Clock(at, zone, seconds: false) : "";
            return new SectionLine(Word(run.Epistemic), method + " " + outcome + when + ": " + IntelligenceText.Plain(run.Label), tone);
        }

        internal static SectionLine Remaining(UnderstandingRemainingItem item)
        {
            var text = IntelligenceText.Plain(item.Text);
            return item.Status switch
            {
                UnderstandingRemainingItemStatus.Reported => new SectionLine(Word(item.Epistemic), "Still to do, the agent says: “" + text + "”", SectionTone.Claim),
                UnderstandingRemainingItemStatus.Failing => new SectionLine(Word(item.Epistemic), "Failing: " + text, SectionTone.Problem),
                UnderstandingRemainingItemStatus.InProgress => new SectionLine(Word(item.Epistemic), "In progress: " + text, SectionTone.Normal),
                _ => new SectionLine(Word(item.Epistemic),
                    (item.Source == UnderstandingRemainingItemSource.Plan ? "Planned, not done: " : "To do: ") + text, SectionTone.Normal),
            };
        }

        internal static string Quote(UnderstandingStatement statement)
        {
            var who = statement.Author switch
            {
                UnderstandingStatementAuthor.Agent => "Agent says",
                UnderstandingStatementAuthor.Subagent => "Subagent says",
                _ => "Quoted, author unknown",
            };
            return who + ": “" + IntelligenceText.Plain(statement.Text) + "”";
        }

        /// <summary>An instant the contract guarantees is a timestamp; one that does not read sorts last.</summary>
        internal static DateTimeOffset At(string timestamp) => IntelligenceText.TryParse(timestamp, out var at) ? at : DateTimeOffset.MaxValue;

        /// <summary>The source's own word for how it knows a claim.</summary>
        public static string Word(UnderstandingEpistemic epistemic) => epistemic switch
        {
            UnderstandingEpistemic.Observed => "observed",
            UnderstandingEpistemic.Reported => "reported",
            UnderstandingEpistemic.Inferred => "inferred",
            UnderstandingEpistemic.Planned => "planned",
            _ => "explained",
        };
    }

    /// <summary>
    /// What was checked?: each check that ran, how it went, when, and after which change, as the
    /// understanding source observed them; what it says needs a look; then what the evaluation source
    /// measured, under its own provenance line. The two sources stay apart: nothing of one is said in
    /// the other's words, and neither is combined into a score.
    /// </summary>
    public static class CheckedPresenter
    {
        public static SectionPresentation Present(
            IntelligenceFeed<UnderstandingResponse> understanding,
            IntelligenceFeed<EvaluationResponse> evaluation,
            DateTimeOffset now,
            TimeZoneInfo zone,
            AnswerRoom? room = null) =>
            Present(understanding.ExecutionId, understanding.Last, understanding.Loading, understanding.Error,
                evaluation.Last, evaluation.Loading, evaluation.Error, now, zone, room);

        /// <param name="now">This device's time; a recorded answer is described as of when it was recorded.</param>
        /// <param name="room">
        /// The rows a page holds: the checks the understanding source saw keep the most telling and
        /// count the rest on the first page; the measurement starts a page of its own and pages on.
        /// Unlimited when null.
        /// </param>
        public static SectionPresentation Present(
            string? executionId,
            IntelligenceRead<UnderstandingResponse>? understanding,
            bool understandingLoading,
            string? understandingError,
            IntelligenceRead<EvaluationResponse>? evaluation,
            bool evaluationLoading,
            string? evaluationError,
            DateTimeOffset now,
            TimeZoneInfo zone,
            AnswerRoom? room = null)
        {
            room ??= AnswerRoom.Unlimited;
            if (executionId == null) return IntelligenceText.Empty(SectionKind.Checked, "Nothing is checked until work starts.");
            var measurement = EvaluationPresenter.Measurement(evaluation, evaluationLoading, evaluationError, now, zone);
            var simulated = evaluation?.Response.Result is AvailableEvaluation measured && measured.Evaluation.Source.Synthetic;
            SectionPresentation runs;
            if (understanding == null) runs = IntelligenceText.Waiting(SectionKind.Checked, understandingLoading, understandingError);
            else
            {
                var status = IntelligenceText.ReadingStatus(understandingLoading, understandingError);
                runs = understanding.Response.Result is AvailableUnderstanding available
                    ? Runs(available.Understanding, understanding, status, now, zone, room)
                    : UnderstandingPresenter.Failure(SectionKind.Checked, understanding.Response.Result, status);
            }
            // The measurement starts a page of its own, its provenance line heading each of its pages.
            var paged = measurement.Select((line, index) => index == 0 && runs.Lines.Count > 0
                ? new SectionLine(line.Tag, line.Text, line.Tone, line.Detail, line.Rows, line.Source, startsPage: true, repeats: true)
                : line);
            return new SectionPresentation(SectionKind.Checked, runs.Provenance, runs.ProvenanceTone, runs.Lines.Concat(paged).ToList(),
                runs.Simulated || simulated);
        }

        /// <summary>
        /// The checks the understanding source saw run, the earliest first, each with the change it
        /// ran after, as many as fit and the rest counted; whether a check ran after every change;
        /// then, where there is room, what needs a look and what the agent said about its checks.
        /// </summary>
        private static SectionPresentation Runs(Understanding understanding, IntelligenceRead<UnderstandingResponse> read, string status, DateTimeOffset now,
            TimeZoneInfo zone, AnswerRoom room)
        {
            var source = understanding.Source;
            var page = room.Page();
            var runs = UnderstandingPresenter.Runs(understanding);
            if (runs.Count == 0)
            {
                page.Add(new SectionLine("", IntelligenceText.Plain(understanding.Verification.Summary), SectionTone.Secondary));
            }
            var coverage = UnderstandingPresenter.Coverage(understanding);
            page.AddCounted(runs.Select(run => (IReadOnlyList<SectionLine>)UnderstandingPresenter.Run(run, understanding.Changes.Files, zone).ToList()).ToList(),
                more => new SectionLine("", "And " + IntelligenceText.Plural(more, "more check"), SectionTone.Secondary), reserve: coverage);
            if (coverage != null) page.Add(coverage);
            var review = understanding.Review;
            if (review.Open > 0 && review.Groups.Count > 0)
            {
                var labels = string.Join("; ", review.Groups.Take(2).Select(group => IntelligenceText.Plain(group.Label)));
                var first = review.Groups[0].Items.Count > 0 ? UnderstandingPresenter.Word(review.Groups[0].Items[0].Epistemic) : "";
                page.AddIfRoom(new SectionLine(first, IntelligenceText.Plain(review.Summary) + ": " + labels, SectionTone.Attention, rows: 2));
            }
            var statement = understanding.Verification.Statements.LastOrDefault();
            if (statement != null)
            {
                page.AddIfRoom(new SectionLine(UnderstandingPresenter.Word(statement.Epistemic), UnderstandingPresenter.Quote(statement), SectionTone.Claim, rows: 2));
            }
            return new SectionPresentation(SectionKind.Checked, IntelligenceText.UnderstandingProvenance(read, source, now, zone, checks: true) + status,
                source.Synthetic ? SectionTone.Attention : SectionTone.Secondary, page.Lines, source.Synthetic);
        }
    }

    /// <summary>
    /// An answer split into the pages a section shows one at a time: as many rows as fit, never a
    /// line apart from the detail under it, never a provenance line at a page's foot, and a new page
    /// at every step of a flow, a step that takes more than one showing its heading again on the
    /// next. Every page keeps the answer's provenance, so where it comes from is always in view.
    /// </summary>
    public static class AnswerPages
    {
        public static IReadOnlyList<SectionPresentation> Split(SectionPresentation section, AnswerRoom room)
        {
            var rows = Math.Max(1, room.Rows);
            // A line with the details under it, and a provenance line with the group it heads.
            var groups = new List<List<SectionLine>>();
            foreach (var line in section.Lines)
            {
                var last = groups.Count == 0 ? null : groups[groups.Count - 1];
                var joins = last != null && !line.StartsPage && (line.Detail || last[last.Count - 1].Source);
                if (joins) last!.Add(line);
                else groups.Add(new List<SectionLine> { line });
            }
            var pages = new List<List<SectionLine>>();
            var current = new List<SectionLine>();
            var heads = new List<SectionLine>();
            var used = 0;
            foreach (var group in groups)
            {
                if (group[0].StartsPage) heads.Clear();
                var size = group.Sum(room.RowsOf);
                if (current.Count > 0 && (group[0].StartsPage || used + size > rows))
                {
                    pages.Add(current);
                    // A step that goes on shows its heading again; a new step brings its own.
                    current = group[0].StartsPage ? new List<SectionLine>() : new List<SectionLine>(heads);
                    used = current.Sum(room.RowsOf);
                }
                current.AddRange(group);
                used += size;
                heads.AddRange(group.Where(line => line.Repeats));
            }
            if (current.Count > 0 || pages.Count == 0) pages.Add(current);
            Balance(pages, rows, room);
            return pages
                .Select(lines => new SectionPresentation(section.Kind, section.Provenance, section.ProvenanceTone, lines, section.Simulated, section.Steps))
                .ToList();
        }

        /// <summary>
        /// A step that goes on to a page with one line of its own under its heading takes the line
        /// before it over too, where that leaves the page before more than one and both fit, so no
        /// page stands nearly empty.
        /// </summary>
        private static void Balance(List<List<SectionLine>> pages, int rows, AnswerRoom room)
        {
            for (var index = 1; index < pages.Count; index++)
            {
                var page = pages[index];
                var before = pages[index - 1];
                // A page that goes on with a step starts with the step's own heading, shown again.
                if (page.Count == 0 || before.Count == 0 || !ReferenceEquals(page[0], before[0])) continue;
                var heads = page.TakeWhile(line => line.Repeats).Count();
                var movable = before.Count - before.TakeWhile(line => line.Repeats).Count();
                if (page.Count - heads != 1 || movable < 2) continue;
                // The last line of the page before, with the line it details where it is a detail.
                var start = before.Count - 1;
                while (start > 0 && before[start].Detail) start--;
                if (before.Count - start >= movable) continue;
                var moving = before.GetRange(start, before.Count - start);
                if (page.Sum(room.RowsOf) + moving.Sum(room.RowsOf) > rows) continue;
                before.RemoveRange(start, moving.Count);
                page.InsertRange(heads, moving);
            }
        }

        /// <summary>What a page's provenance line starts with: "Step 2 of 6" through a flow, else "Part 1 of 3", as the request's parts.</summary>
        public static string Caption(SectionPresentation section, int page, int pages) =>
            section.Steps ? "Step " + (page + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture)
            : EntryText.Part(page, pages);
    }

    /// <summary>
    /// The rows a page of an answer holds under its provenance, and how many rows a line takes there:
    /// as many as its words wrap to at the page's width, never more than it may take. The headset
    /// measures both on its labels; without a measure, every line takes all the rows it may.
    /// </summary>
    public sealed class AnswerRoom
    {
        private readonly Func<SectionLine, int>? measure;

        public AnswerRoom(int rows, Func<SectionLine, int>? measure = null)
        {
            Rows = rows;
            this.measure = measure;
        }

        /// <summary>Room without end: every answer whole, on one page but for a flow's steps.</summary>
        public static AnswerRoom Unlimited { get; } = new AnswerRoom(int.MaxValue / 4);

        public int Rows { get; }

        public int RowsOf(SectionLine line) => Math.Max(1, Math.Min(line.Rows, measure?.Invoke(line) ?? line.Rows));

        /// <summary>A page to fill, the most telling lines first.</summary>
        internal AnswerPage Page() => new AnswerPage(this);
    }

    /// <summary>
    /// One page of a list being filled: lines it must show, lines it shows where there is room, and
    /// a list of which it shows as many as fit and counts the rest, as the Doing log gives way
    /// rather than page.
    /// </summary>
    internal sealed class AnswerPage
    {
        private readonly AnswerRoom room;
        private readonly List<SectionLine> lines = new List<SectionLine>();
        private int used;

        public AnswerPage(AnswerRoom room) => this.room = room;

        public IReadOnlyList<SectionLine> Lines => lines;

        private int Left => room.Rows - used;

        private int RowsOf(IEnumerable<SectionLine> group) => group.Sum(room.RowsOf);

        /// <summary>A line the page always shows.</summary>
        public void Add(SectionLine line)
        {
            lines.Add(line);
            used += room.RowsOf(line);
        }

        /// <summary>A line the page shows only where it has room for it.</summary>
        public void AddIfRoom(SectionLine line)
        {
            if (room.RowsOf(line) <= Left) Add(line);
        }

        public void AddCounted(IReadOnlyList<SectionLine> items, Func<int, SectionLine> more, SectionLine? reserve = null) =>
            AddCounted(items.Select(item => (IReadOnlyList<SectionLine>)new[] { item }).ToList(), more, reserve);

        /// <summary>
        /// Shows <paramref name="items"/>, each a line and the details under it, as many as fit with
        /// room kept for <paramref name="reserve"/>, and where some don't fit, a line counting them in
        /// place of the last that would.
        /// </summary>
        public void AddCounted(IReadOnlyList<IReadOnlyList<SectionLine>> items, Func<int, SectionLine> more, SectionLine? reserve = null)
        {
            var kept = reserve == null ? 0 : room.RowsOf(reserve);
            var all = items.Sum(RowsOf);
            if (all <= Left - kept)
            {
                foreach (var item in items) foreach (var line in item) Add(line);
                return;
            }
            var counted = room.RowsOf(more(items.Count));
            var shown = 0;
            foreach (var item in items)
            {
                if (RowsOf(item) > Left - kept - counted) break;
                foreach (var line in item) Add(line);
                shown++;
            }
            Add(more(items.Count - shown));
        }
    }
}
