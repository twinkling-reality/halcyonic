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
        public NeedAnswer(string asks, string? request, IReadOnlyList<string> notes)
        {
            Asks = asks;
            Request = request;
            Notes = notes;
        }

        /// <summary>What it asks for, for example "It asks for approval to use bash:".</summary>
        public string Asks { get; }

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
        public const string WhatIsItDoing = "What is it doing?";

        /// <summary>What is it doing?, turned to the run's details in place of its log.</summary>
        public const string HowIsItRunning = "How is it running?";

        public const string ShowDetails = "Show details";

        public const string ShowLog = "Show the log";
        public const string HelpMeUnderstand = "Help me understand";
        public const string WhatWasChecked = "What was checked?";
        public const string WhatDoYouNeed = "What do you need from me?";

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
            return shown >= 3 ? "At least 2 more questions wait after this one." : "1 more question waits after this one.";
        }

        /// <summary>Which prompt of the question shows, with its header as the agent wrote it.</summary>
        public static string PromptHeading(QuestionView question, int prompt)
        {
            var header = question.Prompts[prompt].Header;
            var heading = string.IsNullOrWhiteSpace(header) ? "" : OneLine(header!);
            if (question.Prompts.Count <= 1) return heading.Length == 0 ? "It asks you" : heading;
            var count = "Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture) + " of " + question.Prompts.Count.ToString(CultureInfo.InvariantCulture);
            return heading.Length == 0 ? count : count + " · " + heading;
        }

        /// <summary>How a prompt is answered, in a few words.</summary>
        public static string PromptHow(QuestionPrompt prompt)
        {
            var offers = prompt.Options.Count > 0;
            if (!offers) return prompt.FreeText ? "Type your answer." : "It offers no answers to choose.";
            if (prompt.Multiple) return prompt.FreeText ? "Choose any that apply, and type more if you like." : "Choose any that apply.";
            return prompt.FreeText ? "Choose one, or type your own." : "Choose one.";
        }

        /// <summary>The typed answer's button: what it says before and after typing.</summary>
        public static string TypedLabel(string? typed) => typed == null ? "Type an answer" : "Typed: " + OneLine(typed);

        /// <summary>
        /// Why Halcyonic cannot send an answer to a question it shows, with the way on: stopping the
        /// turn, which withdraws a question on every runtime (ADR 0022).
        /// </summary>
        public static string CannotAnswer(QuestionView question)
        {
            if (question.Prompts.Any(prompt => prompt.Secret)) return "It asks for something secret, which can't be sent from here. Press Stop to go on.";
            if (!question.Prompts.All(HasAWayToAnswer)) return SameAnswersTwice;
            if (question.Prompts.Any(prompt => IsCut(prompt.Header) || IsCut(prompt.Text) || prompt.Options.Any(option => IsCut(option.Label) || IsCut(option.Description))))
            {
                return "This question is too long to show in full, so it can't be answered here. Press Stop to go on.";
            }
            return "This question can't be answered from here. Press Stop to go on.";
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
        public static string AlikeCantBeChosen(bool stop) =>
            "Answers that read the same can't be chosen here." + (stop ? " Press Stop if you meant one of them." : "");

        /// <summary>Why words typed or heard weren't taken, as a notice (settled by the coordinator, 2026-10-07).</summary>
        public static string TypedReadsAlike(bool stop) =>
            "Your answer reads the same as answers that can't be chosen here. " + (stop ? "Answer another way, or press Stop if you meant one of them." : "Answer another way.");

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
        public const string SameAnswersTwice = "Two of its answers read the same, so your choice can't be sent from here. Press Stop to go on.";

        /// <summary>The agent waits while nobody can answer here: said under a question Halcyonic cannot answer.</summary>
        public const string AgentWaits = "It's waiting for an answer.";

        /// <summary>
        /// A command whose effect can't be ruled out, by what it asked, in WORDS.md's unknown-effect pattern:
        /// what may or may not have happened, then what to check first. Never "Couldn't", which says nothing
        /// happened (proposed by lane W, 2026-10-04).
        /// </summary>
        public static string NotSure(CommandType type) => type switch
        {
            CommandType.ExecutionSendInstruction => "Not sure the instruction reached it. " + CheckFirst,
            CommandType.ExecutionInterrupt => "Not sure it stopped. " + CheckFirst,
            CommandType.ExecutionRespondToApproval => "Not sure it has your decision. " + CheckFirst,
            CommandType.ExecutionAnswerQuestion => AnswerNotConfirmed,
            CommandType.ExecutionStart => "Not sure it started. " + CheckFirst,
            _ => "Not sure it happened. " + CheckFirst,
        };

        private const string CheckFirst = "Check its activity before you try again.";

        /// <summary>An answer sent whose effect the runtime never confirmed (Codex's question_unconfirmed, answer_ambiguous).</summary>
        public const string AnswerNotConfirmed = "Not sure it has your answer. Check its activity before you answer again.";

        private static bool IsCut(string? text) => text != null && text.Contains("[truncated]");

        /// <summary>The goal line under the status: the workstream's objective.</summary>
        public static string Goal(WorkspacePresentation workspace) => "Goal: " + Objective(workspace);

        /// <summary>
        /// The one plain answer under the goal: what waits for the person, pointing to where it shows
        /// whole, or what went wrong, one line per reason; else that nothing waits, and what it did last.
        /// </summary>
        public static IReadOnlyList<string> Answer(WorkspacePresentation workspace)
        {
            // The request or question shows whole under Waiting for you, so the answer names it once.
            if (workspace.ApprovalToAnswer != null) return new[] { "It wants your approval. See it under " + TabLabel(WorkspaceQuestion.NeedFromYou) + "." };
            if (workspace.QuestionToAnswer != null) return new[] { "It asks you a question. See it under " + TabLabel(WorkspaceQuestion.NeedFromYou) + "." };
            var attention = Attention(workspace);
            if (attention.Count > 0) return attention;
            var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
            var line = "Latest: " + (latest == null ? workspace.Character.StatusLabel : Describe(latest));
            if (workspace.Character.Stale) line = "Last known. " + line;
            return new[] { "Nothing is waiting for you.", line };
        }

        /// <summary>The tools whose approvals run a command, as the agent apps name them: the request reads as a command.</summary>
        private static readonly string[] CommandTools = { "shell", "bash", "commandExecution" };

        /// <summary>Whether an approval's tool runs a command, by the name its agent app gave it; an unknown name is not one.</summary>
        public static bool RunsCommand(string toolName) => CommandTools.Any(tool => string.Equals(tool, toolName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The answer to What do you need from me?: the request the work waits on, as the runtime
        /// reported it, and what each answer does; null while no request is pending. The whole
        /// request, never shortened, shows again when Approve or Deny is chosen, before either is sent.
        /// </summary>
        public static NeedAnswer? NeedFromYou(WorkspacePresentation workspace)
        {
            var approval = workspace.ApprovalToAnswer;
            if (approval == null) return null;
            var tool = approval.Subject as ToolUseSubject;
            var notes = new List<string>();
            var waiting = workspace.Execution?.PendingApprovals.Count ?? 0;
            if (waiting > 1) notes.Add(waiting.ToString(CultureInfo.InvariantCulture) + " requests are waiting. This is the oldest.");
            notes.Add("Approve lets it go ahead. Deny refuses; it may try another way.");
            notes.Add("Your answer counts once the agent confirms it.");
            notes.Add("Approve and Deny both show the whole request before you confirm.");
            return new NeedAnswer(
                tool == null ? "It wants your approval." : RunsCommand(tool.ToolName) ? "It wants to run a command:" : "It wants to use " + OneLine(tool.ToolName) + ":",
                tool == null ? null : OneLine(tool.Summary),
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
            new PresetInstruction("Run checks again", "Run the tests again and report the results."),
        };

        /// <summary>What the workspace says while the system keyboard is open.</summary>
        public const string TypingPrompt = "Type what to tell it, then press Enter.";

        /// <summary>The caption over the workspace's log of what the work did.</summary>
        public const string RecentActivity = "Recent activity";

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
        public const string Sent = "Sent…";

        /// <summary>
        /// The first-time hint beside a pinch cue, three words that teach look and pinch: rest the gaze
        /// on a character until its peek shows, then pinch with either hand, wherever it is.
        /// </summary>
        public const string OpenHint = "Look, then pinch";

        /// <summary>What the confirmation asks while part of the request it answers has not been shown yet.</summary>
        public const string ReadRequestFirst = "Read the whole request above before approving it.";

        /// <summary>An answer armed to send, cancelled as words heard or typed changed it (settled by the coordinator, 2026-10-04).</summary>
        public const string AnswerChanged = "Nothing was sent: your answer changed. Read it again.";

        /// <summary>An answer armed to send, cancelled as the text size changed (settled by the coordinator, 2026-10-04).</summary>
        public const string TextSizeChanged = "Nothing was sent: the text size changed. Read it again.";

        /// <summary>A confirmation dropped because focus went to another window.</summary>
        public const string ConfirmAfresh = "You went to another window, so nothing was sent. Press it again to confirm.";

        /// <summary>
        /// Why your computer refused a command, from its code alone, with the way on; null where the code
        /// says nothing more a person can act on. Never the refusal's message: the control plane writes it
        /// for developers, in its own terms ("An answer chooses the same option twice"). A refusal over a
        /// folder is said as <see cref="EntryText.FolderProblem"/> says it; the demonstration's refusal,
        /// in Halcyonic's own words, is shown as written by whoever shows it.
        /// </summary>
        public static string? WhyRefused(RejectionCode? code) => code switch
        {
            RejectionCode.ProjectNotFound => "This project isn't on " + HostText.Your + " any more. Choose another in Projects.",
            RejectionCode.WorkstreamNotFound => "This task isn't on " + HostText.Your + " any more. Choose another in Tasks.",
            RejectionCode.ExecutionNotFound => "This work isn't on " + HostText.Your + " any more. " + StateLanguage.AddItAgain,
            RejectionCode.RuntimeNotFound => "Its agent app isn't on " + HostText.Your + " now. Set it up there, then try again.",
            RejectionCode.ApprovalNotFound => "It no longer waits for that decision. See what it's doing now.",
            RejectionCode.QuestionNotFound => "It's no longer waiting for this answer. See what it's doing now.",
            RejectionCode.InvalidAnswer => "It couldn't take that answer. Read the question again, then answer it.",
            RejectionCode.ModelRequired => "Its agent app needs a model. Choose one, then try again.",
            RejectionCode.CapabilityUnsupported => "Its agent app can't do that. See what it's doing, then try something it offers.",
            RejectionCode.InvalidState => "It can't take that right now. See what it's doing, then try again.",
            RejectionCode.InvalidRuntimeOptions => "Its agent app didn't accept those settings. Choose them again.",
            RejectionCode.LocationRequired or RejectionCode.LocationMissing or RejectionCode.LocationNotAllowed or RejectionCode.LocationExists
                => EntryText.FolderProblem(code, null),
            // One sentence for a revoked pairing, wherever it is said.
            RejectionCode.DeviceRevoked => ConnectionText.PairingRefused,
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
            "runtime_unreachable" or "runtime_closed" => StateLanguage.LostTouch
                + (running ? " See what it's doing; if it can't go on, add the task again in Projects."
                    : step ? " Check it's running there, then try again." : " Check that the agent app is running on " + HostText.Your + ", then try again."),
            "execution_unknown_to_runtime" => WhyRefused(RejectionCode.ExecutionNotFound),
            "model_unavailable" => WhyRefused(RejectionCode.ModelRequired),
            "capability_unimplemented" => running ? WhyRefused(RejectionCode.CapabilityUnsupported)
                : step ? "Its agent app doesn't support it. " + InHowItRuns : "Its agent app can't do that. " + WithAnotherAgentApp,
            // A start that would fail the same way again (the review's L5, settled by the coordinator, 2026-10-07).
            // A step's lines are New project's, within its 110 characters (settled by the coordinator, 2026-10-07).
            "runtime_unavailable" when !running => step
                ? "The agent app isn't available on " + HostText.Your + ". Check its setup, then try again."
                : "The agent app isn't available on " + HostText.Your + ". Check its setup there, then add the task again in Projects.",
            "runtime_version_unsupported" when !running => step
                ? "This agent app's version isn't supported yet. Choose another in How it runs."
                : "This agent app's version on " + HostText.Your + " isn't supported yet. " + WithAnotherAgentApp,
            _ => EntryText.FolderProblem(null, code),
        };

        private const string WithAnotherAgentApp = "Add the task again in Projects with another agent app.";
        private const string InHowItRuns = "Choose another agent app in How it runs, then try again.";

        /// <summary>Said of a step of Start building or Connect that failed with no effect and no cause a person can act on.</summary>
        public const string NothingChanged = "Nothing changed. Try again.";

        /// <summary>
        /// Said of a command on a task that failed with no effect and no cause a person can act on. Trying
        /// again at once can fail the same way, as when the agent app isn't answering, so the way on looks
        /// at the task first (the coordinator, 2026-10-04).
        /// </summary>
        public const string NothingChangedSeeIt = "Nothing changed. See what it's doing, then try again.";

        /// <summary>
        /// "{lead}: {why}", or <paramref name="lead"/> alone, ended, where there is no why: a refusal or a
        /// failure as a person reads it, never its message.
        /// </summary>
        public static string Couldnt(string lead, string? why) => why == null ? lead + "." : StateLanguage.Lead(lead, why);

        /// <summary>What a press on an approval's confirmation says before the whole request has been shown.</summary>
        public const string RequestNotRead = "Nothing was sent: read the whole request before approving it.";

        /// <summary>What a press on an instruction's confirmation says before all its words have been shown (settled by the coordinator, 2026-10-04).</summary>
        public const string InstructionNotRead = "Nothing was sent: read to the last part first.";

        /// <summary>
        /// What an instruction's confirmation asks once all its words, shown above in parts where they are
        /// long, have been read: as heard on the computer, or as typed (settled by the coordinator, 2026-10-04).
        /// </summary>
        public static string SendWordsAbove(bool heard) => heard ? HostText.YourStart + " heard the words above. Send them?" : "Tell it the words above?";

        /// <summary>What the Doing tab says before anything was sent to the work from here.</summary>
        public const string NothingSentYet = "Nothing sent from here yet.";

        public static string Objective(WorkspacePresentation workspace) =>
            string.IsNullOrWhiteSpace(workspace.Objective) ? "No goal was given." : OneLine(workspace.Objective!);

        /// <summary>
        /// The run's details, a line each: the agent app and whether it is on the Mac now, the model it
        /// was given, the folder it works in, and when it started with the round it is in. Only what
        /// the workspace already knows; where a model runs it does not, and says so. Each line says
        /// whether it holds text from outside.
        /// </summary>
        public static IReadOnlyList<(string Line, bool IsData)> RunDetails(WorkspacePresentation workspace, TimeZoneInfo zone)
        {
            var execution = workspace.Execution;
            if (execution == null) return new[] { ("Nothing has run yet.", false) };
            var lines = new List<(string, bool)>();
            var gone = workspace.Runtime == null ? ", not available on " + HostText.Your + " now." : ".";
            if (execution.Runtime.Synthetic) lines.Add((EntryText.PracticeRun + gone, false));
            else
            {
                var name = OneLine(workspace.Runtime?.DisplayName ?? execution.Runtime.DisplayName);
                lines.Add(("Agent app: " + name + (workspace.Runtime == null ? gone : ", on " + HostText.Your + "."), true));
                lines.Add(execution.ModelRef == null
                    ? ("Model: chosen by the agent app. " + ModelPlaceUnknown + ".", false)
                    : ("Model: " + OneLine(execution.ModelRef) + ". " + ModelPlaceUnknown + ".", true));
            }
            lines.Add(FolderOf(execution.Directory) is string folder ? ("Folder: " + folder + ".", true) : ("Folder: none given to it.", false));
            var started = StartedAt(execution.StartedAt, zone);
            var round = execution.TurnCount > 0 ? "Round " + execution.TurnCount.ToString(CultureInfo.InvariantCulture) : null;
            lines.Add((started != null ? "Started at " + started + " · " + (round ?? "No round yet") + "." : round != null ? round + "." : "Not started yet.", false));
            return lines;
        }

        /// <summary>Said under the model: the workspace is not told where it runs.</summary>
        public const string ModelPlaceUnknown = "Where it runs isn't known here";

        /// <summary>
        /// A folder by its name and the folder it is in, "shop, in Projects"; null for none. The path is
        /// the Mac's, so only a slash divides it: a backslash may be part of a folder's name.
        /// </summary>
        private static string? FolderOf(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            var parts = directory!.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return OneLine(directory);
            return parts.Length == 1 ? OneLine(parts[0]) : OneLine(parts[parts.Length - 1]) + ", in " + OneLine(parts[parts.Length - 2]);
        }

        /// <summary>When it started, in the local time and day: "09:00 on 2 Oct"; null when it hasn't or the time can't be read.</summary>
        private static string? StartedAt(string? startedAt, TimeZoneInfo zone)
        {
            if (startedAt == null || !DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return null;
            var local = TimeZoneInfo.ConvertTime(at, zone);
            return local.ToString("HH:mm", CultureInfo.InvariantCulture) + " on " + local.Day.ToString(CultureInfo.InvariantCulture) + " "
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
            WorkspaceAction.Instruct => "Tell it",
            WorkspaceAction.Answer => "Send answer",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>
        /// The question a deliberate confirmation asks, naming exactly what would be sent. Approving
        /// and denying ask about the request shown whole above the question (<see cref="Request"/>).
        /// </summary>
        public static string ConfirmationPrompt(WorkspaceAction action, string? instruction) => action switch
        {
            WorkspaceAction.Approve => "Approve the request above?",
            WorkspaceAction.Deny => "Deny the request above?",
            WorkspaceAction.Interrupt => "Stop what it's doing now? It counts as stopped once the agent confirms.",
            WorkspaceAction.Instruct => "Tell it this? “" + OneLine(instruction ?? "") + "”",
            WorkspaceAction.Answer => "Send these answers?",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>
        /// The whole of what an approval asks for, as the runtime reported it: the tool and what it
        /// would do, for example "bash: Run the migration". Never shortened; the workspace shows it in
        /// parts when it does not fit.
        /// </summary>
        public static string Request(ApprovalView? approval) => approval?.Subject switch
        {
            ToolUseSubject tool => OneLine(tool.ToolName) + ": " + OneLine(tool.Summary),
            _ => "It wants your approval.",
        };

        /// <summary>The caption over a request shown whole, with which part shows when it takes more than one.</summary>
        public static string RequestCaption(int part, int parts) => parts <= 1
            ? "The whole request"
            : "The whole request, part " + part.ToString(CultureInfo.InvariantCulture) + " of " + parts.ToString(CultureInfo.InvariantCulture);

        public static string ConfirmLabel(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Yes, approve",
            WorkspaceAction.Deny => "Yes, deny",
            WorkspaceAction.Interrupt => "Yes, stop",
            WorkspaceAction.Instruct => "Yes, tell it",
            WorkspaceAction.Answer => "Yes, send answer",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>Why nothing can be done right now, or null when an action is offered.</summary>
        public static string? WhyNoActions(WorkspacePresentation workspace)
        {
            if (workspace.Actions.Count > 0) return null;
            if (workspace.Character.Stale) return "Nothing can be sent until " + HostText.Your + " reconnects.";
            if (workspace.Execution == null) return "Nothing to send until it starts.";
            if (workspace.Runtime == null) return "Nothing can be sent: it isn't available on " + HostText.Your + " now.";
            return workspace.Character.Activity switch
            {
                CharacterActivity.Starting => "Nothing to send while it starts.",
                CharacterActivity.Unknown => "Can't tell yet what it's doing, so nothing can be sent.",
                _ => "Nothing you can send right now.",
            };
        }

        /// <summary>
        /// Shortens text to at most <paramref name="maxLength"/> characters, ending in an ellipsis when
        /// cut, and never between the two halves of a character outside the Basic Multilingual Plane.
        /// </summary>
        public static string Truncate(string text, int maxLength)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Must be at least 1.");
            if (text.Length <= maxLength) return text;
            var kept = maxLength - 1;
            if (kept > 0 && char.IsHighSurrogate(text[kept - 1])) kept--;
            return text.Substring(0, kept).TrimEnd() + "…";
        }

        /// <summary>
        /// Text as one line of exactly what it says, by the one rule for text Halcyonic did not write
        /// (<see cref="LabelText.Plain"/>): line breaks and tabs as spaces, and what would not show as
        /// itself shown as its code point.
        /// </summary>
        public static string OneLine(string text) => LabelText.Plain(text);

        /// <summary>An activity entry in one line, agent text quoted as the agent's.</summary>
        public static string Describe(ActivityEntry entry) =>
            entry.Reported ? "It says: “" + OneLine(entry.Text) + "”" : OneLine(entry.Text);
    }
}
