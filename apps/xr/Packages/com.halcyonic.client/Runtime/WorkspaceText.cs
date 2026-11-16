#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>An instruction offered as a button, for where typing is not possible.</summary>
    public sealed class PresetInstruction
    {
        public PresetInstruction(string label, string text)
        {
            Label = label;
            Text = text;
        }

        /// <summary>What the button says.</summary>
        public string Label { get; }

        /// <summary>Exactly what is sent.</summary>
        public string Text { get; }
    }

    /// <summary>The person's questions an open workspace answers, one under each tab.</summary>
    public enum WorkspaceQuestion
    {
        /// <summary>How its requests are going and its recent activity.</summary>
        Doing,

        /// <summary>What the understanding source concluded about the execution.</summary>
        Understand,

        /// <summary>What the evaluation source measured about it: checks, outcome and estimated cost.</summary>
        Checked,

        /// <summary>The request it waits on; asked only while one is pending.</summary>
        NeedFromYou,
    }

    /// <summary>What the work needs from the person, as What do you need from me? answers it.</summary>
    public sealed class NeedAnswer
    {
        public NeedAnswer(LineWords asks, string? request, IReadOnlyList<string> notes)
        {
            AsksRuns = asks;
            Asks = asks.Text;
            Request = request;
            Notes = notes;
        }

        /// <summary>What it asks for, for example "It asks for approval to use bash:".</summary>
        public string Asks { get; }

        /// <summary>What it asks for as runs: a tool's name leans on Halcyonic's ground inside Halcyonic's sentence.</summary>
        public LineWords AsksRuns { get; }

        /// <summary>The request as the runtime reported it, one line by the one rule, or null when it named no tool.</summary>
        public string? Request { get; }

        /// <summary>How many requests wait, and what each answer does.</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// The words of the peek and of the expanded workspace, so the XR layer only lays them out. Agent
    /// text is always marked as a claim, every state is written out, never left to color, and text
    /// Halcyonic did not write shows by <see cref="LabelText"/>'s rule.
    /// </summary>
    public static class WorkspaceText
    {
        public const string Product = "Halcyonic";
        public const string WhatHappened = "What Happened";
        public const string OtherApps = "Other Apps";
        public const string Minimize = "Minimize";
        public const string OpenWorkspace = "Open Workspace";

        /// <summary>The menu's way to clear the room to the characters alone (WORDS.md, Workspace organization; the addendum's A15).</summary>
        public const string Compact = "Compact";
        public const string Connected = "Connected";
        public const string Disconnected = "Disconnected";
        public const string Connecting = "Connecting";
        public const string Demo = "Demo";
        public const string Recorded = "Recorded";

        /// <summary>
        /// What the workspace says of the session shown: Recorded while a recorded run plays (ADR 0035: never Practice or
        /// Demo), Demo while the demonstration does, and otherwise the control plane's state.
        /// </summary>
        public static string ConnectionWords(bool demonstration, bool recordedRun, bool connected, bool connecting) =>
            demonstration ? recordedRun ? Recorded : Demo
            : connected ? Connected : connecting ? Connecting : Disconnected;
        public const string MoveWorkspace = "Move";
        public const string WhatIsItDoing = "Status";

        /// <summary>What is it doing?, turned to the run's details in place of its log.</summary>
        public const string HowIsItRunning = "Run Details";

        public const string ShowDetails = "Show Details";

        public const string ShowLog = "Show Log";
        public const string HelpMeUnderstand = "Explain";
        public const string WhatWasChecked = "Checked";
        public const string WhatDoYouNeed = "Needs You";

        /// <summary>The whole question a tab asks.</summary>
        public static string Question(WorkspaceQuestion question) => question switch
        {
            WorkspaceQuestion.Doing => WhatIsItDoing,
            WorkspaceQuestion.Understand => HelpMeUnderstand,
            WorkspaceQuestion.Checked => WhatWasChecked,
            WorkspaceQuestion.NeedFromYou => WhatDoYouNeed,
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, "Unhandled question."),
        };

        /// <summary>
        /// A question's tab, in a short name (ADR 0023): what waits for the person under the state's own
        /// word, and the others in one word each. The whole question heads the tab's answer.
        /// </summary>
        public static string TabLabel(WorkspaceQuestion question) => question switch
        {
            WorkspaceQuestion.Doing => "Doing",
            WorkspaceQuestion.Understand => "Understand",
            WorkspaceQuestion.Checked => "Checked",
            WorkspaceQuestion.NeedFromYou => StateLanguage.WordOf(WorkState.WaitingForYou),
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, "Unhandled question."),
        };

        /// <summary>Something waits for the person: an approval or an agent's question.</summary>
        public static bool SomethingWaits(WorkspacePresentation workspace) =>
            workspace.ApprovalToAnswer != null || workspace.QuestionToAnswer != null;

        /// <summary>
        /// The questions to offer now, in the tabs' order: What do you need from me? first, and only
        /// while a real request or question waits.
        /// </summary>
        public static IReadOnlyList<WorkspaceQuestion> Questions(WorkspacePresentation workspace) => !SomethingWaits(workspace)
            ? new[] { WorkspaceQuestion.Doing, WorkspaceQuestion.Understand, WorkspaceQuestion.Checked }
            : new[] { WorkspaceQuestion.NeedFromYou, WorkspaceQuestion.Doing, WorkspaceQuestion.Understand, WorkspaceQuestion.Checked };

        /// <summary>The question a workspace opens on: what waits for the person, else what it is doing.</summary>
        public static WorkspaceQuestion FirstQuestion(WorkspacePresentation workspace) =>
            SomethingWaits(workspace) ? WorkspaceQuestion.NeedFromYou : WorkspaceQuestion.Doing;

        /// <summary>
        /// Said with an agent's question when more wait after it: how many, and, when the most the
        /// control plane shows at once (three) are shown, that there may be more, since how many more is
        /// not known. Empty for one question.
        /// </summary>
        public static string QuestionLead(WorkspacePresentation workspace)
        {
            var shown = workspace.Execution?.PendingQuestions.Count ?? 0;
            if (shown <= 1) return "";
            return shown >= 3 ? "At Least 2 More Questions" : "1 More Question";
        }

        /// <summary>Which prompt of the question shows, with its header as the agent wrote it.</summary>
        public static string PromptHeading(QuestionView question, int prompt) => PromptHeadingRuns(question, prompt).Text;

        /// <summary>
        /// Which prompt of the question shows, as runs: Halcyonic's count upright, the header as the agent
        /// wrote it leaning, so a header such as "Your answers" never reads as Halcyonic's.
        /// </summary>
        public static LineWords PromptHeadingRuns(QuestionView question, int prompt)
        {
            var heading = LineWords.Theirs(question.Prompts[prompt].Header);
            if (question.Prompts.Count <= 1) return heading.IsEmpty ? LineWords.Ours(CharacterPresenter.AsksYouWords) : heading;
            var count = LineWords.Ours("Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture) + " of " + question.Prompts.Count.ToString(CultureInfo.InvariantCulture));
            return heading.IsEmpty ? count : count + LineWords.Ours(", ") + heading;
        }

        /// <summary>How a prompt is answered, in a few words.</summary>
        public static string PromptHow(QuestionPrompt prompt)
        {
            var offers = prompt.Options.Count > 0;
            if (!offers) return prompt.FreeText ? "Write Answer" : "No Answers Offered";
            // One phrase, never two facts on a dot (AUDIT2's 2026-11-14T05:36Z words).
            if (prompt.Multiple) return prompt.FreeText ? "Choose Any or Write" : "Choose Any";
            return prompt.FreeText ? "Choose One or Write" : "Choose One";
        }

        /// <summary>The typed answer's button: what it says before and after typing.</summary>
        public static string TypedLabel(string? typed) => typed == null ? "Write Answer" : "Typed: " + OneLine(typed);

        /// <summary>
        /// Why Halcyonic cannot send an answer to a question it shows, with the way on: stopping the
        /// turn, which withdraws a question on every runtime (ADR 0022).
        /// </summary>
        public static string CannotAnswer(QuestionView question)
        {
            // One sentence each, never two facts on a dot; "it" is the question, and stopping is named as the deed that withdraws it,
            // never as a button (AUDIT2's 2026-11-14T05:52Z words).
            if (question.Prompts.Any(prompt => prompt.Secret)) return "It asks for a secret, which can't be sent from here; stopping the task withdraws it";
            if (!question.Prompts.All(HasAWayToAnswer)) return SameAnswersTwice;
            if (question.Prompts.Any(prompt => IsCut(prompt.Header) || IsCut(prompt.Text) || prompt.Options.Any(option => IsCut(option.Label) || IsCut(option.Description))))
            {
                return "It is too long to show whole, so it can't be answered here; stopping the task withdraws it";
            }
            return "It can't be answered here; stopping the task withdraws it";
        }

        /// <summary>
        /// Whether a question can be answered from the headset: the runtime says it can, no prompt asks for
        /// something secret, whatever an adapter says, since what is typed or said here is journaled, and every
        /// prompt still has a way to answer (<see cref="HasAWayToAnswer"/>). Every place that offers or sends an
        /// answer asks this, never the flag alone.
        /// </summary>
        public static bool Answerable(QuestionView question) =>
            question.Answerable && !question.Prompts.Any(prompt => prompt.Secret) && question.Prompts.All(HasAWayToAnswer);

        /// <summary>
        /// A prompt still has a way to answer here, its answers that read alike aside (<see cref="ReadsAlike"/>):
        /// it offers none that do, or it takes typed words, or it offers an answer that reads like no other.
        /// </summary>
        public static bool HasAWayToAnswer(QuestionPrompt prompt) =>
            !OffersALabelTwice(prompt) || prompt.FreeText || Enumerable.Range(0, prompt.Options.Count).Any(option => !ReadsAlike(prompt, option));

        /// <summary>
        /// The answer reads the same as another of the prompt's (<see cref="AsShown"/>): its row shows, and
        /// can't be chosen, since nobody could tell which of them they chose, nor the agent which was meant.
        /// </summary>
        public static bool ReadsAlike(QuestionPrompt prompt, int option)
        {
            var shown = AsShown(prompt.Options[option].Label);
            return prompt.Options.Where((_, other) => other != option).Any(other => string.Equals(AsShown(other.Label), shown, StringComparison.Ordinal));
        }

        /// <summary>
        /// Words typed or heard for the prompt that an agent could take for one of its answers that can't be
        /// chosen: compared more loosely than the answers are with each other (<see cref="Loosely"/>), since
        /// speech writes "Yes." for "Yes" and the agent reads free text as it likes.
        /// </summary>
        public static bool ReadsLikeOneThatCantBeChosen(QuestionPrompt prompt, string words)
        {
            var heard = Loosely(words);
            return Enumerable.Range(0, prompt.Options.Count)
                .Any(option => ReadsAlike(prompt, option) && string.Equals(Loosely(prompt.Options[option].Label), heard, StringComparison.Ordinal));
        }

        /// <summary>Text as shown (<see cref="AsShown"/>), case folded, with any . ! ? or , at its end left off.</summary>
        private static string Loosely(string text) => AsShown(text).ToLowerInvariant().TrimEnd('.', '!', '?', ',', ' ');

        /// <summary>
        /// Said under a prompt's answers where some read the same, with Stop where the page offers it, as stopping
        /// is the only way to the one meant (settled by the coordinator, 2026-10-07).
        /// </summary>
        /// <remarks>One sentence for one fact, never two on a dot, Stop named as a deed (AUDIT2's 2026-11-14T05:49Z words).</remarks>
        public static string AlikeCantBeChosen(bool stop) =>
            FileScreens.AlikeLine + (stop ? "; stopping the task is the only way past them" : "");

        /// <summary>Why words typed or heard weren't taken, as a notice (settled by the coordinator, 2026-10-07).</summary>
        /// <remarks>One sentence, never two facts on a dot (AUDIT2's 2026-11-14T05:49Z words).</remarks>
        public static string TypedReadsAlike(bool stop) =>
            "That matches answers that share a name: answer differently" + (stop ? ", or stop the task" : "");

        /// <summary>
        /// The prompt offers two answers whose labels read the same (<see cref="AsShown"/>). An answer names
        /// what it chooses by its label alone, so choosing one of two labels written alike sends that label
        /// twice, which the control plane refuses ("An answer chooses the same option twice"), and between
        /// two that only show alike a person can't tell which they chose: a "Yes" that keeps the data from a
        /// "Yes " that deletes it.
        /// </summary>
        public static bool OffersALabelTwice(QuestionPrompt prompt) =>
            prompt.Options.Select(option => AsShown(option.Label)).Distinct(StringComparer.Ordinal).Count() != prompt.Options.Count;

        /// <summary>
        /// A label as its row shows it, for telling answers apart: <see cref="LabelText.Plain"/>, then in
        /// Unicode's composed form (NFC), compared character for character. White space at the ends, as a tab
        /// or as a run of spaces, and one letter written composed or decomposed, read the same; case, width, ligatures and
        /// letters of other scripts that only look alike do not (OPEN_QUESTIONS.md).
        /// </summary>
        public static string AsShown(string? label)
        {
            // A run of spaces reads as one: "Delete  all" and "Delete all" can't be told apart on a row.
            var plain = System.Text.RegularExpressions.Regex.Replace(LabelText.Plain(label), " {2,}", " ");
            try
            {
                return plain.Normalize(NormalizationForm.FormC);
            }
            catch (Exception)
            {
                // Whatever a runtime's normalizer refuses is compared as shown, so drawing a question never fails:
                // on .NET that is an ArgumentException, and on the headset a broken normalizer (Mono's checks
                // nothing) fails as anything, a type initializer or an unsupported internal call.
                return plain;
            }
        }

        /// <summary>Why a question with a prompt whose answers read alike and no other way to answer can't be answered here (settled by the coordinator, 2026-10-04).</summary>
        public const string SameAnswersTwice = "Its answers share a name, so it can't be answered here; stopping the task withdraws it";

        /// <summary>The agent waits while nobody can answer here: said under a question Halcyonic cannot answer.</summary>
        public const string AgentWaits = "Awaiting Answer";

        /// <summary>
        /// A command whose effect can't be ruled out, by what it asked, in WORDS.md's unknown-effect pattern:
        /// what may or may not have happened, then what to check first. Never "Couldn't", which says nothing
        /// happened (proposed by lane W, 2026-10-04).
        /// </summary>
        public static string NotSure(CommandType type) => type switch
        {
            CommandType.ExecutionSendInstruction => "Sent, not confirmed yet. Check Activity.",
            CommandType.ExecutionInterrupt => "The stop isn't confirmed. Check Activity.",
            CommandType.ExecutionRespondToApproval => "The decision isn't confirmed. Check Activity.",
            CommandType.ExecutionAnswerQuestion => AnswerNotConfirmed,
            CommandType.ExecutionStart => "The start isn't confirmed. Check Activity.",
            CommandType.WorkspaceRequestSubmit or CommandType.WorkspaceRequestResume or CommandType.WorkspaceRequestCancel
                => "The request isn't confirmed. Refresh it.",
            _ => "Its effect isn't confirmed. Check Activity.",
        };

        private const string CheckFirst = "Check Activity";

        /// <summary>An answer sent whose effect the runtime never confirmed (Codex's question_unconfirmed, answer_ambiguous).</summary>
        public const string AnswerNotConfirmed = "The answer isn't confirmed. Check Activity.";

        private static bool IsCut(string? text) => text != null && text.Contains("[truncated]");

        /// <summary>
        /// Why an approval's request can only be denied, or null when it can be approved: the request
        /// was not complete, as its agent app reported it or once the control plane cut it to fit
        /// (`Approvable`), so the person could never read all of what it would run. The words name the
        /// cut when the summary ends with its mark.
        /// </summary>
        public static string? OnlyDeny(ApprovalView approval) => approval.Approvable
            ? null
            : approval.Subject is ToolUseSubject tool && tool.Summary.EndsWith("[truncated]", StringComparison.Ordinal)
                ? TooLongToShow
                : approval.Subject is ToolUseSubject command && RunsCommand(command.ToolName)
                    ? CommandNotKnown
                    : RequestNotKnown;

        public const string TooLongToShow = "The command is too long to show, so you can only deny it.";
        public const string CommandNotKnown = "The command isn't complete, so you can only deny it.";
        public const string RequestNotKnown = "The request isn't complete, so you can only deny it.";

        /// <summary>The goal line under the status, one line where the panel gives the context one: "Goal: " and the workstream's objective.</summary>
        public static string Goal(WorkspacePresentation workspace) => GoalLabel + ": " + Objective(workspace);

        public const string GoalLabel = "Goal";
        public const string LatestLabel = "Latest";
        public const string LastKnownLabel = "Last Known";

        /// <summary>
        /// The one plain answer under the goal: what waits for the person, pointing to where it shows
        /// whole, or what went wrong, one line per reason; else that nothing waits, and what it did last,
        /// its label ("Latest", or "Last Known" where the state is stale) handed apart from it so the panel
        /// lays the label small over the line (AUDIT2's rule A, 2026-11-16). Every other line has no label.
        /// </summary>
        public static IReadOnlyList<(string? Label, string Line)> Answer(WorkspacePresentation workspace)
        {
            // The request or question shows whole under Waiting for you, so the answer names it once.
            if (workspace.ApprovalToAnswer != null) return new (string?, string)[] { (null, "An approval is waiting. Open " + TabLabel(WorkspaceQuestion.NeedFromYou) + ".") };
            if (workspace.QuestionToAnswer != null) return new (string?, string)[] { (null, "A question is waiting. Open " + TabLabel(WorkspaceQuestion.NeedFromYou) + ".") };
            var attention = Attention(workspace);
            if (attention.Count > 0) return attention.Select(line => ((string?)null, line)).ToList();
            var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
            var line = latest == null ? StateLanguage.LineWordOf(workspace.Character) : Describe(latest);
            return new (string?, string)[] { (null, "Nothing Waiting"), (workspace.Character.Stale ? LastKnownLabel : LatestLabel, line) };
        }

        /// <summary>The tools whose approvals run a command, as the agent apps name them: the request reads as a command.</summary>
        private static readonly string[] CommandTools = { "shell", "bash", "commandExecution" };

        /// <summary>Whether an approval's tool runs a command, by the name its agent app gave it; an unknown name is not one.</summary>
        public static bool RunsCommand(string toolName) => CommandTools.Any(tool => string.Equals(tool, toolName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Where a command runs once approved, from what its agent app declares (ADR 0033): it may leave
        /// the sandbox, or it stays inside one. Null when the agent app declares nothing, so nothing is said.
        /// </summary>
        public static string? ApprovedCommandNote(RuntimeDescriptor? runtime) => runtime?.ApprovalMayLeaveSandbox switch
        {
            true => "Runs on " + KitMomentWords.ThisMac,
            false => "Runs in " + KitMomentWords.InSandbox,
            _ => null,
        };

        /// <summary>
        /// The answer to What do you need from me?: the request the work waits on, as the runtime
        /// reported it, with how many wait, why only Deny is offered, or where an approved command runs; null
        /// while no request is pending. No note says what Approve or Deny does: the buttons beside it say so
        /// (WORDS rule 2). The whole request, never shortened, shows again when Approve or Deny is chosen.
        /// </summary>
        public static NeedAnswer? NeedFromYou(WorkspacePresentation workspace)
        {
            var approval = workspace.ApprovalToAnswer;
            if (approval == null) return null;
            var tool = approval.Subject as ToolUseSubject;
            var notes = new List<string>();
            var waiting = workspace.Execution?.PendingApprovals.Count ?? 0;
            if (waiting > 1) notes.Add(waiting.ToString(CultureInfo.InvariantCulture) + " requests waiting, oldest first");
            if (OnlyDeny(approval) is string onlyDeny) notes.Add(onlyDeny);
            // Where an approved command runs, as its agent app declares it, never read from the request (ADR 0033).
            else if (tool != null && RunsCommand(tool.ToolName) && ApprovedCommandNote(workspace.Runtime) is string where) notes.Add(where);
            return new NeedAnswer(
                tool == null ? LineWords.Ours("Approval Requested") : RunsCommand(tool.ToolName) ? LineWords.Ours("Run Command")
                    : LineWords.Ours("Use ") + LineWords.TheirName(tool.ToolName),
                tool == null ? null : LabelText.Exact(CaptureNames.Request(tool.Summary)),
                notes);
        }

        /// <summary>
        /// Offered instead of typing only where the system keyboard is not supported, such as the
        /// editor. Deliberately generic: they ask the runtime to go on or to report, nothing new.
        /// </summary>
        public static IReadOnlyList<PresetInstruction> PresetInstructions { get; } = new[]
        {
            new PresetInstruction("Continue", "Continue."),
            new PresetInstruction("Summarize", "Summarize what you changed and why."),
            new PresetInstruction("Run Checks Again", "Run the tests again and report the results."),
        };

        /// <summary>The system keyboard's prompt for an instruction: Halcyonic's words alone, since the keyboard draws it with no lean (settled 2026-10-08).</summary>
        public const string TellItPrompt = "Instruction";

        /// <summary>What the workspace says while the system keyboard is open.</summary>
        public const string TypingPrompt = "Type Instruction";

        /// <summary>The caption over the workspace's log of what the work did.</summary>
        public const string RecentActivity = "Recent Activity";

        /// <summary>Reads a section's source again: Understand's or Checked's.</summary>
        public const string Refresh = "Refresh";

        /// <summary>Help me understand's questions, each a pill under its heading.</summary>
        public static string PromptLabel(UnderstandPrompt prompt) => prompt switch
        {
            UnderstandPrompt.WhatChanged => "What changed?",
            UnderstandPrompt.WhyChanged => "Why?",
            UnderstandPrompt.HowBuilt => "How was it built?",
            _ => throw new ArgumentOutOfRangeException(nameof(prompt), prompt, "Unhandled prompt."),
        };

        /// <summary>Stands, taking no press, where Send answer was while the answer sent may still take effect.</summary>
        public const string Sent = "Sending";

        /// <summary>
        /// The first-time hint beside a pinch cue, three words that teach look and pinch: rest the gaze
        /// on a character until its peek shows, then pinch with either hand, wherever it is.
        /// </summary>
        public const string OpenHint = "Open";

        /// <summary>What the confirmation asks while part of the request it answers has not been shown yet.</summary>
        public const string ReadRequestFirst = "Read Full Request";

        /// <summary>An answer armed to send, cancelled as words heard or typed changed it (settled by the coordinator, 2026-10-04).</summary>
        public const string AnswerChanged = "Nothing was sent: your answer changed. Read it again.";

        /// <summary>An answer armed to send, cancelled as the text size changed (settled by the coordinator, 2026-10-04).</summary>
        public const string TextSizeChanged = "Nothing was sent: the text size changed. Read it again.";

        /// <summary>A confirmation dropped because focus went to another window.</summary>
        public const string ConfirmAfresh = "Nothing was sent: you moved to another panel. Press again.";

        /// <summary>
        /// Why your computer refused a command, from its code alone, with the way on; null where the code
        /// says nothing more a person can act on. Never the refusal's message: the control plane writes it
        /// for developers, in its own terms ("An answer chooses the same option twice"). A refusal over a
        /// folder is said as <see cref="EntryText.FolderProblem"/> says it; the demonstration's refusal,
        /// in Halcyonic's own words, is shown as written by whoever shows it.
        /// </summary>
        /// <summary>A photo or video the computer no longer keeps (the host's own sentence).</summary>
        public const string MediaNotKept = "That photo or video is no longer kept on this computer. Add it again.";

        /// <summary>A project at its most photos and videos (50, or 240 MB), AUDIT2's words: no remove exists, so the way on is another project.</summary>
        public const string MediaFull = "This project already holds as many photos and videos as it can keep. Add it to another project.";

        /// <summary>The same photo or video already in the project (the host's own first sentence).</summary>
        public const string MediaPresent = "That photo or video is already in this project. Nothing was added.";

        public static string? WhyRefused(RejectionCode? code) => code switch
        {
            RejectionCode.RequestNotFound => "That request is gone. Refresh the task.",
            RejectionCode.ProjectNotFound => "That project is gone. Choose another.",
            RejectionCode.WorkstreamNotFound => "That task is gone. Choose another.",
            RejectionCode.ExecutionNotFound => "That work is gone. Add the task again in Projects.",
            RejectionCode.RuntimeNotFound => "The agent app isn't available. Set it up and try again.",
            RejectionCode.ApprovalNotFound => "That decision has closed. See Activity.",
            RejectionCode.ApprovalNotWhole => "It was never shown whole. Deny it or stop the task.",
            RejectionCode.QuestionNotFound => "That question has closed. See Activity.",
            RejectionCode.InvalidAnswer => "The answer was refused. Read the question again.",
            RejectionCode.ModelRequired => "A model is needed. Choose one.",
            RejectionCode.CapabilityUnsupported => "That isn't supported. See Activity.",
            RejectionCode.InvalidState => "That can't be done now. See Activity.",
            RejectionCode.InvalidRuntimeOptions => "Those settings were refused. Choose again.",
            RejectionCode.LocationRequired or RejectionCode.LocationMissing or RejectionCode.LocationNotAllowed or RejectionCode.LocationExists
                => EntryText.FolderProblem(code, null),
            RejectionCode.CheckpointNotFound => "That version isn't kept. Choose another.",
            // One sentence for a revoked pairing, wherever it is said.
            RejectionCode.DeviceRevoked => ConnectionText.PairingRefused,
            // A move or a restore from this headset without the review it read, or one that changed since (ADR 0051).
            RejectionCode.ReviewRequired or RejectionCode.ReviewStale => "Nothing was sent: the project changed. Check it and try again.",
            // A photo or video the Mac's page adds (ADR 0066, protocol 3): in sentences, never the dot cadence; the first
            // and the last are the host's own (AUDIT2 passed, command-service.ts MEDIA_REFUSED).
            RejectionCode.MediaNotKept => MediaNotKept,
            RejectionCode.MediaFull => MediaFull,
            RejectionCode.MediaPresent => MediaPresent,
            _ => null,
        };

        /// <summary>
        /// Why a command failed with no effect, from a code every agent app's adapter and the control plane
        /// share, in a sentence already said elsewhere, with the way on; a folder's problem as
        /// <see cref="EntryText.FolderProblem"/> says it; null for any other code, an adapter's own, which
        /// says nothing a person can act on. Never the failure's message. <paramref name="running"/> says
        /// there is work to look at: where there may be none yet, as Start building's and Connect's steps or a
        /// start that never ran, a lost agent app points to the computer instead (settled by the coordinator,
        /// 2026-10-04). <paramref name="step"/> says it is Start building's or Connect's own step, whose ways
        /// on are what that step offers, never adding the task again from inside New project (2026-10-07).
        /// </summary>
        public static string? WhyFailed(string? code, bool running = true, bool step = false) => code switch
        {
            "approval_not_pending" => WhyRefused(RejectionCode.ApprovalNotFound),
            "question_not_pending" => WhyRefused(RejectionCode.QuestionNotFound),
            "no_running_turn" => WhyRefused(RejectionCode.InvalidState),
            // One code covers a blip and an agent app's ended process or lost thread, where trying again can't succeed.
            "runtime_unreachable" or "runtime_closed" => running ? "The agent app can't be reached. See Activity and add the task again if it's stuck."
                : "The agent app can't be reached. Check it and try again.",
            "execution_unknown_to_runtime" => WhyRefused(RejectionCode.ExecutionNotFound),
            "model_unavailable" => WhyRefused(RejectionCode.ModelRequired),
            "media_not_kept" => WhyRefused(RejectionCode.MediaNotKept),
            "media_full" => WhyRefused(RejectionCode.MediaFull),
            "media_present" => WhyRefused(RejectionCode.MediaPresent),
            "capability_unimplemented" => running ? WhyRefused(RejectionCode.CapabilityUnsupported)
                : step ? "That isn't supported. Choose another agent app." : "That isn't supported. Add the task again with another app.",
            // A start that would fail the same way again (the review's L5, settled by the coordinator, 2026-10-07).
            // A step's lines are New project's, within its 110 characters (settled by the coordinator, 2026-10-07).
            "runtime_unavailable" when !running => step
                ? "The agent app isn't available. Check its setup and try again."
                : "The agent app isn't available. Check its setup, then add the task again.",
            "runtime_version_unsupported" when !running => step
                ? "This version of the agent app isn't supported. Choose another agent app."
                : "This version of the agent app isn't supported. Add the task again with another app.",
            // Something other than Halcyonic changed what the task may do, so the agent app's adapter stopped it; a
            // turn's failure, never a step's, which no start command fails with (settled by the coordinator, 2026-10-08).
            "runtime_tampered" when !step => Tampered,
            _ => EntryText.FolderProblem(null, code),
        };

        /// <summary>A task stopped because something on the computer changed what it was allowed to do (runtime_tampered).</summary>
        public const string Tampered = "The task stopped because its permissions changed. See Activity.";

        private const string WithAnotherAgentApp = "Re-Add Task with Another App";
        private const string InHowItRuns = "Change Agent App";

        /// <summary>Said of a step of Start building or Connect that failed with no effect and no cause a person can act on.</summary>
        public const string NothingChanged = "Nothing changed. Try again.";

        /// <summary>
        /// Said of a command on a task that failed with no effect and no cause a person can act on. Trying
        /// again at once can fail the same way, as when the agent app isn't answering, so the way on looks
        /// at the task first (the coordinator, 2026-10-04).
        /// </summary>
        public const string NothingChangedSeeIt = "Nothing changed. Check Activity.";

        /// <summary>
        /// "{lead}: {why}", or <paramref name="lead"/> alone, ended, where there is no why: a refusal or a
        /// failure as a person reads it, never its message.
        /// </summary>
        public static string Couldnt(string lead, string? why) => why == null ? lead : StateLanguage.Lead(lead, why);

        /// <summary>What a press on an approval's confirmation says before the whole request has been shown.</summary>
        public const string RequestNotRead = "Nothing was sent: read the whole request first.";

        /// <summary>What a press on an instruction's confirmation says before all its words have been shown (settled by the coordinator, 2026-10-04).</summary>
        public const string InstructionNotRead = "Nothing was sent: read to the last part first.";

        /// <summary>
        /// What an instruction's confirmation asks once all its words, shown above in parts where they are
        /// long, have been read: as heard on the computer, or as typed (settled by the coordinator, 2026-10-04).
        /// </summary>
        public static string SendWordsAbove(bool heard) => heard ? "Send This?" : "Send Instruction?";

        /// <summary>What Watch It Work's count says while the turn is still at work and has changed nothing (AUDIT2 03:06Z).</summary>
        public const string NoFilesChangedYet = "No Files Changed Yet";

        /// <summary>
        /// Watch It Work's count of the files the turn changed, one source for the view and its renders (AUDIT2 03:06Z and 03:07Z):
        /// "Changed 3 Files So Far" while at work, "Changed 3 Files" once the turn has ended (TaskSteps' own words), "No Files Changed
        /// Yet" while at work with none. An ended turn that changed nothing has no count to say: null, never a "Yet" that is false.
        /// </summary>
        public static string? WatchCount(long files, bool atWork)
        {
            if (files <= 0) return atWork ? NoFilesChangedYet : null;
            return atWork ? TaskSteps.Changed(files) + " So Far" : TaskSteps.Changed(files);
        }

        /// <summary>What the Doing tab says before anything was sent to the work from here.</summary>
        public const string NothingSentYet = "Nothing Sent";

        public static string Objective(WorkspacePresentation workspace) =>
            string.IsNullOrWhiteSpace(workspace.Objective) ? "No Goal" : OneLine(workspace.Objective!);

        /// <summary>
        /// The run's details, a line each: the agent app and whether it is on the Mac now, the model it
        /// was given, the folder it works in, and when it started with the round it is in. Only what
        /// the workspace already knows; where a model runs it does not, and says so. Each line says
        /// whether it holds text from outside.
        /// </summary>
        public static IReadOnlyList<(string? Label, string Line, bool IsData)> RunDetails(WorkspacePresentation workspace, TimeZoneInfo zone)
        {
            var execution = workspace.Execution;
            if (execution == null) return new (string?, string, bool)[] { (null, "Not Run Yet", false) };
            var lines = new List<(string?, string, bool)>();
            if (execution.Runtime.Synthetic) lines.Add(("Agent App", EntryText.PracticeRun + (workspace.Runtime == null ? ", unavailable" : ""), false));
            else
            {
                var name = CaptureNames.AgentName(OneLine(workspace.Runtime?.DisplayName ?? execution.Runtime.DisplayName));
                lines.Add(("Agent App", name + (workspace.Runtime == null ? ", unavailable" : ", on your computer"), true));
                // Where a model runs the workspace is never told, so it never says "on your computer" of a model.
                lines.Add(execution.ModelRef == null
                    ? ("Model", "The app's choice, " + ModelPlaceUnknown, false)
                    : ("Model", CaptureNames.ModelName(OneLine(execution.ModelRef)) + ", " + ModelPlaceUnknown, true));
            }
            lines.Add(FolderOf(execution.Directory) is string folder ? ("Folder", folder, true) : ("Folder", "None given", false));
            var started = StartedAt(execution.StartedAt, zone);
            if (started != null) lines.Add(("Started", started, false));
            var round = execution.TurnCount > 0 ? "Round " + execution.TurnCount.ToString(CultureInfo.InvariantCulture) : null;
            lines.Add((null, round ?? (started != null ? "No Round Yet" : "Not Started"), false));
            return lines;
        }

        /// <summary>Said under the model: the workspace is not told where it runs.</summary>
        public const string ModelPlaceUnknown = "location unknown";

        /// <summary>
        /// A folder by its name and the folder it is in, "shop, in Projects"; null for none. The path is
        /// the Mac's, so only a slash divides it: a backslash may be part of a folder's name.
        /// </summary>
        private static string? FolderOf(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            var parts = directory!.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return OneLine(directory);
            // Under the capture extra the parent folder, which can be the home folder, is left out.
            return parts.Length == 1 || CaptureNames.Generic ? OneLine(parts[parts.Length - 1]) : OneLine(parts[parts.Length - 1]) + " in " + OneLine(parts[parts.Length - 2]);
        }

        /// <summary>When it started, in the local time and day: "09:00 on 2 Oct"; null when it hasn't or the time can't be read.</summary>
        private static string? StartedAt(string? startedAt, TimeZoneInfo zone)
        {
            if (startedAt == null || !DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return null;
            var local = TimeZoneInfo.ConvertTime(at, zone);
            return local.ToString("HH:mm", CultureInfo.InvariantCulture) + ", " + local.Day.ToString(CultureInfo.InvariantCulture) + " "
                + local.ToString("MMM", CultureInfo.InvariantCulture);
        }

        /// <summary>What needs the person or went wrong, one line per reason; empty when nothing does.</summary>
        public static IReadOnlyList<string> Attention(WorkspacePresentation workspace) =>
            workspace.Character.AttentionNotes.Select(OneLine).ToList();

        /// <summary>One line of activity with its local time; agent text is quoted and attributed.</summary>
        public static string Activity(ActivityEntry entry, TimeZoneInfo zone)
        {
            var text = Describe(entry);
            return DateTimeOffset.TryParse(entry.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text
                : text;
        }

        /// <summary>The icon beside an action's words, and beside its confirmation's: never hold to talk's.</summary>
        public static GlazeIcon IconOf(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => GlazeIcon.Approve,
            WorkspaceAction.Deny => GlazeIcon.Deny,
            WorkspaceAction.Interrupt => GlazeIcon.Stop,
            WorkspaceAction.Instruct => GlazeIcon.TellIt,
            WorkspaceAction.Answer => GlazeIcon.SendAnswer,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        public static string Label(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Approve",
            WorkspaceAction.Deny => "Deny",
            WorkspaceAction.Interrupt => "Stop",
            WorkspaceAction.Instruct => "Write Instruction",
            WorkspaceAction.Answer => "Send Answer",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>
        /// The question a deliberate confirmation asks, naming exactly what would be sent. Approving
        /// and denying ask about the request shown whole above the question (<see cref="Request"/>).
        /// </summary>
        /// <summary>Stop's confirmation, its question, handed apart from what stopping does where the redesign lays them (AUDIT's 2026-11-14T05:37Z).</summary>
        public const string StopQuestion = "Stop Task?";

        /// <summary>What stopping does, handed apart from <see cref="StopQuestion"/>: the task stops only once its agent app confirms it.</summary>
        public const string StopEffect = "Stopped Once Agent Confirms";

        public static string ConfirmationPrompt(WorkspaceAction action, string? instruction) => action switch
        {
            WorkspaceAction.Approve => "Approve Request?",
            WorkspaceAction.Deny => "Deny Request?",
            WorkspaceAction.Interrupt => StopQuestion + KitMomentWords.Dot + StopEffect,
            WorkspaceAction.Instruct => "Send Instruction? " + OneLine(instruction ?? ""),
            WorkspaceAction.Answer => "Send Answers?",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>
        /// The whole of what an approval asks for, as the runtime reported it: the tool and what it
        /// would do, for example "bash: Run the migration". Never shortened; the workspace shows it in
        /// parts when it does not fit.
        /// </summary>
        public static string Request(ApprovalView? approval) => approval?.Subject switch
        {
            ToolUseSubject tool => OneLine(tool.ToolName) + ": " + LabelText.Exact(CaptureNames.Request(tool.Summary)),
            _ => ApprovalWithoutTool,
        };

        /// <summary>
        /// The request read before Yes where an approval names no tool: Halcyonic's own sentence. A tool's request
        /// always holds its name and ": ", so it never reads as this one.
        /// </summary>
        public const string ApprovalWithoutTool = "Approval Requested";

        /// <summary>The caption over a request shown whole, with which part shows when it takes more than one.</summary>
        public static string RequestCaption(int part, int parts) => parts <= 1
            ? "Full Request"
            : "Full Request, Part " + part.ToString(CultureInfo.InvariantCulture) + " of " + parts.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The confirmation's Yes: Confirm, one word, under the question that names the action ("Approve Request?"), so it
        /// stays short enough for the kit's decision bar at the larger text size.
        /// </summary>
        public static string ConfirmLabel(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Confirm",
            WorkspaceAction.Deny => "Confirm",
            WorkspaceAction.Interrupt => "Confirm",
            WorkspaceAction.Instruct => "Confirm",
            WorkspaceAction.Answer => "Confirm",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>Why nothing can be sent while a recorded run plays. Each reason here is one plain sentence, never parts on a dot (AUDIT2 2026-11-16 09:28Z).</summary>
        public const string NotSendableRecorded = "This is a recording, so nothing can be sent.";

        /// <summary>Why nothing can be done right now, or null when an action is offered.</summary>
        public static string? WhyNoActions(WorkspacePresentation workspace)
        {
            if (workspace.Actions.Count > 0) return null;
            if (workspace.Character.Stale) return "Nothing can be sent until the computer reconnects.";
            // A recorded run is played as it happened: nothing reaches an agent, whatever its agent app's state then.
            if (workspace.Character.Recorded && !workspace.Character.Synthetic) return NotSendableRecorded;
            if (workspace.Execution == null) return "Nothing can be sent until the task starts.";
            if (workspace.Runtime == null) return "Nothing can be sent: the agent app isn't available on your computer.";
            return workspace.Character.Activity switch
            {
                CharacterActivity.Starting => "Nothing can be sent while the agent starts.",
                CharacterActivity.Unknown => "Nothing can be sent until your computer says what the agent is doing.",
                _ => "Nothing can be sent right now.",
            };
        }

        /// <summary>
        /// Shortens text to at most <paramref name="maxLength"/> characters, ending in an ellipsis when
        /// cut (<see cref="Prefix"/>): the one cut for text from outside, made plain first.
        /// </summary>
        public static string Truncate(string text, int maxLength)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Must be at least 1.");
            if (text.Length <= maxLength) return text;
            return Prefix(text, maxLength - 1).TrimEnd() + "…";
        }

        /// <summary>
        /// At most <paramref name="maxLength"/> characters from the start of text, never between the two halves of a
        /// character outside the Basic Multilingual Plane and never inside a code point <see cref="LabelText.Plain"/>
        /// shows, as ‹U+202E›, which would leave half of it reading as something else.
        /// </summary>
        public static string Prefix(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text;
            var kept = Math.Max(0, maxLength);
            if (kept > 0 && char.IsHighSurrogate(text[kept - 1])) kept--;
            var open = kept == 0 ? -1 : text.LastIndexOf('\u2039', kept - 1);
            var close = open < 0 ? -1 : text.IndexOf('\u203A', open);
            // Cut before the code point; one it starts with stays whole rather than leave nothing.
            if (open >= 0 && close >= kept) kept = open > 0 ? open : close + 1;
            return text.Substring(0, kept);
        }

        /// <summary>
        /// Text as one line of exactly what it says, by the one rule for text Halcyonic did not write
        /// (<see cref="LabelText.Plain"/>): line breaks and tabs as spaces, and what would not show as
        /// itself shown as its code point.
        /// </summary>
        public static string OneLine(string text) => LabelText.Plain(text);

        /// <summary>
        /// The latest of the work as a file's Doing says it while nothing waits, as runs: "Latest: " and
        /// the log's last line other than a round's, a name or the agent's message in it leaning, or the
        /// task's state; "Last known. " first where the state is stale. Reported where it is the agent's
        /// message, which the line marks with "Agent says" (settled 2026-10-08).
        /// </summary>
        public static (LineWords Words, bool Reported) Latest(WorkspacePresentation workspace)
        {
            var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
            var label = workspace.Character.Stale ? LastKnownLabel : LatestLabel;
            var line = LineWords.Written(label + ": ") + (latest == null ? LineWords.Written(StateLanguage.LineWordOf(workspace.Character)) : latest.Words);
            return (line, latest?.Reported == true);
        }

        /// <summary>An activity entry in one line, agent text quoted as the agent's.</summary>
        public static string Describe(ActivityEntry entry) =>
            entry.Reported ? "Reported: " + OneLine(entry.Text) : OneLine(entry.Text);
    }
}
