#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What a character is doing. The XR layer chooses the pose, motion and sound for each.</summary>
    public enum CharacterActivity
    {
        /// <summary>The workstream has no execution yet.</summary>
        Idle,
        Starting,
        Working,
        /// <summary>Working, with a test run in progress.</summary>
        Verifying,
        WaitingForHuman,
        /// <summary>The last turn ended. This says nothing about whether the work is correct.</summary>
        TurnFinished,
        Failed,
        Interrupted,
        /// <summary>Halcyonic cannot currently observe the work; shown instead of a guess.</summary>
        Unknown,
    }

    /// <summary>
    /// What a workstream's character conveys. The control plane decides status and attention; this
    /// maps them to cues the XR layer renders. Every cue has a text form, so no state is conveyed by
    /// color alone (docs/internal/product/PRODUCT.md).
    /// </summary>
    public sealed class CharacterPresentation
    {
        public CharacterPresentation(
            string workstreamId,
            string title,
            CharacterActivity activity,
            string statusLabel,
            AttentionLevel attention,
            IReadOnlyList<string> attentionNotes,
            int pendingApprovals,
            bool synthetic,
            bool recorded,
            bool stale,
            IReadOnlyList<string>? attentionDetails = null,
            IReadOnlyList<LineWords>? attentionNoteRuns = null,
            IReadOnlyList<LineWords>? attentionDetailRuns = null)
        {
            WorkstreamId = workstreamId;
            Title = title;
            Activity = activity;
            StatusLabel = statusLabel;
            Attention = attention;
            AttentionNotes = attentionNotes;
            PendingApprovals = pendingApprovals;
            Synthetic = synthetic;
            Recorded = recorded;
            Stale = stale;
            AttentionDetails = attentionDetails ?? attentionNotes;
            AttentionNoteRuns = attentionNoteRuns ?? attentionNotes.Select(LineWords.Written).ToList();
            AttentionDetailRuns = attentionDetailRuns ?? AttentionDetails.Select(LineWords.Written).ToList();
        }

        public string WorkstreamId { get; }

        /// <summary>The workstream's title, as <see cref="LabelText.Plain"/> shows it.</summary>
        public string Title { get; }

        public CharacterActivity Activity { get; }

        public string StatusLabel { get; }

        public AttentionLevel Attention { get; }

        /// <summary>Why the character needs attention, one line per reason, as <see cref="LabelText.Plain"/> shows it.</summary>
        public IReadOnlyList<string> AttentionNotes { get; }

        /// <summary>
        /// The same reasons without the words the state already says, for the peek beside a state
        /// badge: "Tell it to try again, or what to do instead." under Couldn't finish, never "Couldn't finish this round. ...".
        /// Empty where a reason has nothing more to say than its state. Shown as <see cref="LabelText.Plain"/> shows it.
        /// </summary>
        public IReadOnlyList<string> AttentionDetails { get; }

        /// <summary>
        /// <see cref="AttentionNotes"/> as runs (lane V's treatment): Halcyonic's words upright, a tool's
        /// name, a command, a question or a check's summary from outside leaning, last.
        /// </summary>
        public IReadOnlyList<LineWords> AttentionNoteRuns { get; }

        /// <summary><see cref="AttentionDetails"/> as runs: a failed check's summary alone is all outside text, on a ground after the badge.</summary>
        public IReadOnlyList<LineWords> AttentionDetailRuns { get; }

        public int PendingApprovals { get; }

        /// <summary>
        /// The work is simulated (for example the mock runtime) and must be labeled as such: its current
        /// execution's runtime is, or, before it has any execution, every runtime that could run it is.
        /// </summary>
        public bool Synthetic { get; }

        /// <summary>The state comes from a recorded fixture journal and must be labeled as development data.</summary>
        public bool Recorded { get; }

        /// <summary>The session is not live, so this is the last known state.</summary>
        public bool Stale { get; }
    }

    public static class CharacterPresenter
    {
        public static CharacterPresentation Present(WorkstreamView workstream, ClientProjection state, bool live)
        {
            var execution = state.CurrentExecution(workstream);
            var activity = ActivityOf(workstream.Status);
            var notes = new List<LineWords>();
            var details = new List<LineWords>();
            foreach (var reason in workstream.Attention.Reasons)
            {
                var (note, detail) = Explain(reason, state);
                notes.Add(note);
                details.Add(detail);
            }
            return new CharacterPresentation(
                workstream.WorkstreamId,
                LabelText.Plain(workstream.Title),
                activity,
                StateLanguage.WordOf(StateLanguage.StateOf(activity, workstream.Attention.Level)),
                workstream.Attention.Level,
                notes.Select(note => note.Text).ToList(),
                execution?.PendingApprovals.Count ?? 0,
                execution?.Runtime.Synthetic ?? (workstream.ExecutionIds.Count == 0 && OnlyPractice(state)),
                state.Journal?.Origin == JournalOrigin.Fixture,
                !live,
                details.Select(detail => detail.Text).ToList(),
                notes,
                details);
        }

        /// <summary>
        /// Whether only simulated work can run here: the state registers at least one runtime and every one
        /// is synthetic, so a task not yet started can only ever run on a practice agent. One real runtime,
        /// or none, and nothing is claimed.
        /// </summary>
        public static bool OnlyPractice(ClientProjection state)
        {
            var runtimes = state.Runtimes;
            if (runtimes.Count == 0) return false;
            for (var index = 0; index < runtimes.Count; index++)
            {
                if (!runtimes[index].Synthetic) return false;
            }
            return true;
        }

        public static CharacterActivity ActivityOf(WorkstreamStatus status) => status switch
        {
            WorkstreamStatus.Created => CharacterActivity.Idle,
            WorkstreamStatus.Starting => CharacterActivity.Starting,
            WorkstreamStatus.Running => CharacterActivity.Working,
            WorkstreamStatus.Verifying => CharacterActivity.Verifying,
            WorkstreamStatus.WaitingForHuman => CharacterActivity.WaitingForHuman,
            WorkstreamStatus.Completed => CharacterActivity.TurnFinished,
            WorkstreamStatus.Failed => CharacterActivity.Failed,
            WorkstreamStatus.Interrupted => CharacterActivity.Interrupted,
            WorkstreamStatus.Unknown => CharacterActivity.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled workstream status."),
        };

        /// <summary>
        /// The state word for an activity alone, where its attention is not known: the state language's
        /// word (<see cref="StateLanguage"/>), so every surface says the same.
        /// </summary>
        public static string LabelOf(CharacterActivity activity) => StateLanguage.WordOf(StateLanguage.StateOf(activity, AttentionLevel.None));

        /// <summary>
        /// A question in one line: its one prompt whole, or, for several, how many and their headers,
        /// so it names the question as a whole and never the one prompt the workspace may not show.
        /// </summary>
        public static string AsksYou(QuestionView question) => AsksYouRuns(question).Text;

        /// <summary>A question in one line as runs: Halcyonic's "Agent · Question" upright, the agent's header and words leaning.</summary>
        public static LineWords AsksYouRuns(QuestionView question)
        {
            if (question.Prompts.Count == 1)
            {
                var only = question.Prompts[0];
                var header = LineWords.Theirs(only.Header);
                return LineWords.Ours(AsksYouWords + " · ") + (header.IsEmpty ? LineWords.Theirs(only.Text) : header + LineWords.Ours(" · ") + LineWords.Theirs(only.Text));
            }
            var count = LineWords.Ours(AsksYouWords + " · " + question.Prompts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Questions");
            var headers = question.Prompts.Select(prompt => LineWords.Theirs(prompt.Header)).Where(header => !header.IsEmpty).ToList();
            return headers.Count == question.Prompts.Count
                ? count + LineWords.Ours(" · ") + headers.Aggregate((said, next) => said + LineWords.Ours("; ") + next)
                : count;
        }

        /// <summary>Who asks, before the question's words: the agent, as ART's chip names it (WORDS.md).</summary>
        public const string AsksYouWords = "Agent · Question";

        /// <summary>
        /// A reason in words, and the same without what the state already says: under Couldn't finish
        /// the peek gives only why. A reason that has nothing more to say than its state has no detail.
        /// </summary>
        private static (LineWords Note, LineWords Detail) Explain(AttentionReason reason, ClientProjection state)
        {
            LineWords Ours(string words) => LineWords.Written(words);
            (LineWords, LineWords) Both(LineWords words) => (words, words);
            state.Executions.TryGetValue(reason.ExecutionId, out var execution);
            switch (reason)
            {
                case ApprovalPendingReason approval:
                    foreach (var pending in execution?.PendingApprovals ?? new List<ApprovalView>())
                    {
                        if (pending.ApprovalId == approval.ApprovalId && pending.Subject is ToolUseSubject tool)
                        {
                            // A command reads as one; any other tool, or one this app doesn't know, by its own name.
                            return Both(WorkspaceText.RunsCommand(tool.ToolName)
                                ? Ours("Run Command · ") + LineWords.Theirs(tool.Summary)
                                : Ours("Use ") + LineWords.TheirName(tool.ToolName) + Ours(" · ") + LineWords.Theirs(tool.Summary));
                        }
                    }
                    return Both(Ours("Approval Requested"));
                case QuestionPendingReason question:
                    foreach (var pending in execution?.PendingQuestions ?? new List<QuestionView>())
                    {
                        if (pending.QuestionId != question.QuestionId || pending.Prompts.Count == 0) continue;
                        return Both(AsksYouRuns(pending));
                    }
                    return Both(Ours("Question Asked"));
                case ExecutionFailedReason _:
                    // Why, from what Halcyonic itself knows, and the way on: the reason's message is an
                    // agent app's own error, never a person's words.
                    var (failed, how) = StateLanguage.CouldNotFinish(execution, execution == null ? null : state.RuntimeOf(execution));
                    return (Ours(failed), Ours(how));
                case ExecutionStateUnknownReason _:
                    // Why, by its code alone: the reason's message is a diagnostic, never a person's words.
                    var why = StateLanguage.CantTellWhy(execution?.StatusReason?.Code);
                    return (Ours(StateLanguage.CantTell(why)), Ours(why ?? ""));
                case VerificationFailedReason tests:
                    var run = execution?.LastTestRun;
                    // The runtime's summary leans on a ground after the badge, never as Halcyonic's reason.
                    return run != null && run.TestRunId == tests.TestRunId && LineWords.Theirs(run.Summary) is { IsEmpty: false } summary
                        ? (Ours("Checks · ") + summary, summary)
                        : (Ours("Checks Not Passed"), LineWords.None);
                default:
                    return (Ours("Needs a Look"), LineWords.None);
            }
        }
    }
}
