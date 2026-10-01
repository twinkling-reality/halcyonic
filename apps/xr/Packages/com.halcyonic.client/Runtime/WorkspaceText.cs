#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        /// A question on its tab, in two lines: the four whole questions do not fit one row of tabs at
        /// a size a headset shows legibly, and none is shortened.
        /// </summary>
        public static IReadOnlyList<string> TabLines(WorkspaceQuestion question) => question switch
        {
            WorkspaceQuestion.Doing => new[] { "What is it", "doing?" },
            WorkspaceQuestion.Understand => new[] { "Help me", "understand" },
            WorkspaceQuestion.Checked => new[] { "What was", "checked?" },
            WorkspaceQuestion.NeedFromYou => new[] { "What do you", "need from me?" },
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, "Unhandled question."),
        };

        /// <summary>Something waits for the person: an approval or an agent's question.</summary>
        public static bool SomethingWaits(WorkspacePresentation workspace) =>
            workspace.ApprovalToAnswer != null || workspace.QuestionToAnswer != null;

        /// <summary>The questions to offer now: What do you need from me? only while a real request or question waits.</summary>
        public static IReadOnlyList<WorkspaceQuestion> Questions(WorkspacePresentation workspace) => !SomethingWaits(workspace)
            ? new[] { WorkspaceQuestion.Doing, WorkspaceQuestion.Understand, WorkspaceQuestion.Checked }
            : new[] { WorkspaceQuestion.Doing, WorkspaceQuestion.Understand, WorkspaceQuestion.Checked, WorkspaceQuestion.NeedFromYou };

        /// <summary>The question a workspace opens on: what waits for the person, else what it is doing.</summary>
        public static WorkspaceQuestion FirstQuestion(WorkspacePresentation workspace) =>
            SomethingWaits(workspace) ? WorkspaceQuestion.NeedFromYou : WorkspaceQuestion.Doing;

        /// <summary>
        /// The line over an agent's question: that it asks, and, when the most the control plane shows
        /// at once (three) are shown, that more may follow, since how many more is not known.
        /// </summary>
        public static string QuestionLead(WorkspacePresentation workspace)
        {
            var shown = workspace.Execution?.PendingQuestions.Count ?? 0;
            var lead = shown > 1 ? "It asks you " + shown.ToString(CultureInfo.InvariantCulture) + " things; this is the first." : "It asks you:";
            return shown >= 3 ? lead + " More may follow." : lead;
        }

        /// <summary>Which prompt of the question shows, with its header as the agent wrote it.</summary>
        public static string PromptHeading(QuestionView question, int prompt)
        {
            var header = question.Prompts[prompt].Header;
            var heading = string.IsNullOrWhiteSpace(header) ? "" : OneLine(header!);
            if (question.Prompts.Count <= 1) return heading.Length == 0 ? "The question" : heading;
            var count = "Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture) + " of " + question.Prompts.Count.ToString(CultureInfo.InvariantCulture);
            return heading.Length == 0 ? count : count + " · " + heading;
        }

        /// <summary>How a prompt is answered, in a few words.</summary>
        public static string PromptHow(QuestionPrompt prompt)
        {
            var offers = prompt.Options.Count > 0;
            if (!offers) return prompt.FreeText ? "Type your answer." : "It offers no answer.";
            if (prompt.Multiple) return prompt.FreeText ? "Choose any that apply, and type more if you like." : "Choose any that apply.";
            return prompt.FreeText ? "Choose one, or type your own." : "Choose one.";
        }

        /// <summary>Under an offered answer: chosen or not, and the agent's description of it.</summary>
        public static string? OptionDetail(QuestionOption option, bool chosen)
        {
            var description = string.IsNullOrWhiteSpace(option.Description) ? null : OneLine(option.Description!);
            if (!chosen) return description;
            return description == null ? "Chosen" : "Chosen · " + description;
        }

        /// <summary>The typed answer's button: what it says before and after typing.</summary>
        public static string TypedLabel(string? typed) => typed == null ? "Type an answer" : "Typed: " + OneLine(typed);

        /// <summary>
        /// Why Halcyonic cannot send an answer to a question it shows, with the way on: stopping the
        /// turn, which withdraws a question on every runtime (ADR 0022).
        /// </summary>
        public static string CannotAnswer(QuestionView question)
        {
            if (question.Prompts.Any(prompt => prompt.Secret)) return "The agent asks for something secret. Halcyonic can't send it; stop the turn to go on.";
            if (question.Prompts.Any(prompt => IsCut(prompt.Header) || IsCut(prompt.Text) || prompt.Options.Any(option => IsCut(option.Label) || IsCut(option.Description))))
            {
                return "This question was too long to show whole, so Halcyonic can't answer it. Stop the turn to go on.";
            }
            return "Halcyonic can't send an answer to this question. Stop the turn to go on.";
        }

        /// <summary>The agent waits while nobody can answer here: said under a question Halcyonic cannot answer.</summary>
        public const string AgentWaits = "The agent is waiting for an answer.";

        /// <summary>An answer sent whose effect the runtime never confirmed (Codex's question_unconfirmed, answer_ambiguous).</summary>
        public const string AnswerNotConfirmed = "Not confirmed: the agent may or may not have your answer. Check What is it doing?";

        private static bool IsCut(string? text) => text != null && text.Contains("[truncated]");

        /// <summary>The goal line under the status: the workstream's objective.</summary>
        public static string Goal(WorkspacePresentation workspace) => "Goal: " + Objective(workspace);

        /// <summary>
        /// The one plain answer under the goal: what needs the person or went wrong, one line per
        /// reason; else that nothing does, and what it did last.
        /// </summary>
        public static IReadOnlyList<string> Answer(WorkspacePresentation workspace)
        {
            var attention = Attention(workspace);
            if (attention.Count > 0) return attention;
            var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
            var line = "Latest: " + (latest == null ? workspace.Character.StatusLabel : Describe(latest));
            if (workspace.Character.Stale) line = "Last known. " + line;
            return new[] { "Nothing needs you now.", line };
        }

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
            if (waiting > 1) notes.Add(waiting.ToString(CultureInfo.InvariantCulture) + " requests wait; this is the oldest.");
            notes.Add("Approve lets it go ahead. Deny refuses; it may try another way.");
            notes.Add("Either answer counts once the runtime confirms it.");
            notes.Add("Approve or Deny shows the whole request before you confirm.");
            return new NeedAnswer(
                tool == null ? "It asks for approval." : "It asks for approval to use " + OneLine(tool.ToolName) + ":",
                tool == null ? null : OneLine(tool.Summary),
                notes);
        }

        /// <summary>The longest peek, in characters: one line at the characters' distance.</summary>
        public const int PeekLength = 72;

        /// <summary>
        /// Offered instead of typing only where the system keyboard is not supported, such as the
        /// editor. Deliberately generic: they ask the runtime to go on or to report, nothing new.
        /// </summary>
        public static IReadOnlyList<PresetInstruction> PresetInstructions { get; } = new[]
        {
            new PresetInstruction("Continue", "Continue."),
            new PresetInstruction("Summarize", "Summarize what you changed and why."),
            new PresetInstruction("Rerun tests", "Run the tests again and report the results."),
        };

        /// <summary>What the workspace says while the system keyboard is open.</summary>
        public const string TypingPrompt = "Type the instruction on the keyboard, then press Enter.";

        /// <summary>
        /// The first-time hint beside a pinch cue, three words that teach look and pinch: rest the gaze
        /// on a character until its peek shows, then pinch with either hand, wherever it is.
        /// </summary>
        public const string OpenHint = "Look, then pinch";

        /// <summary>What the workspace says when it offers presets instead of the keyboard.</summary>
        public const string PresetPrompt = "No keyboard here. Send one of these instead:";

        /// <summary>What the confirmation asks while part of the request it answers has not been shown yet.</summary>
        public const string ReadRequestFirst = "Read the whole request below before approving it.";

        /// <summary>A confirmation dropped because focus went to another window.</summary>
        public const string ConfirmAfresh = "You went to another window, so nothing was sent. Press it again to confirm.";

        /// <summary>What a press on an approval's confirmation says before the whole request has been shown.</summary>
        public const string RequestNotRead = "Nothing was sent: read the whole request before approving it.";

        /// <summary>The buttons that step through a request shown in parts.</summary>
        public const string PreviousPart = "Previous part";

        public const string NextPart = "Next part";

        /// <summary>The status label with its qualifiers, for example "Needs you · simulated · last known".</summary>
        public static string StatusLine(CharacterPresentation character)
        {
            var line = character.StatusLabel;
            if (character.PendingApprovals > 1) line += " (" + character.PendingApprovals + " approvals)";
            if (character.Synthetic) line += " · simulated";
            if (character.Recorded) line += " · recorded";
            if (character.Stale) line += " · last known";
            return line;
        }

        public static string Objective(WorkspacePresentation workspace) =>
            string.IsNullOrWhiteSpace(workspace.Objective) ? "No objective was given." : OneLine(workspace.Objective!);

        /// <summary>The execution and its runtime, for example "On Mock runtime, simulated work · 2 turns".</summary>
        public static string Execution(WorkspacePresentation workspace)
        {
            var execution = workspace.Execution;
            if (execution == null) return "No execution yet.";
            var line = "On " + OneLine(workspace.Runtime?.DisplayName ?? execution.Runtime.DisplayName);
            if (execution.Runtime.Synthetic) line += ", simulated work";
            line += execution.TurnCount == 1 ? " · 1 turn" : " · " + execution.TurnCount.ToString(CultureInfo.InvariantCulture) + " turns";
            // Gone from the control plane, or never there, as in a recording.
            if (workspace.Runtime == null) line += " · the runtime is not available here";
            return line;
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

        public static string Label(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Approve",
            WorkspaceAction.Deny => "Deny",
            WorkspaceAction.Interrupt => "Stop the turn",
            WorkspaceAction.Instruct => "Instruct",
            WorkspaceAction.Answer => "Send answer",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>
        /// The question a deliberate confirmation asks, naming exactly what would be sent. Approving
        /// and denying ask about the request shown whole below the question (<see cref="Request"/>).
        /// </summary>
        public static string ConfirmationPrompt(WorkspaceAction action, string? instruction) => action switch
        {
            WorkspaceAction.Approve => "Approve the request below?",
            WorkspaceAction.Deny => "Deny the request below?",
            WorkspaceAction.Interrupt => "Stop the current turn? The runtime confirms when it has stopped.",
            WorkspaceAction.Instruct => "Send this instruction? “" + OneLine(instruction ?? "") + "”",
            WorkspaceAction.Answer => "Send these answers to the agent?",
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
            _ => "The runtime asked for approval.",
        };

        /// <summary>The caption over a request shown whole, with which part shows when it takes more than one.</summary>
        public static string RequestCaption(int part, int parts) => parts <= 1
            ? "The whole request"
            : "The whole request, part " + part.ToString(CultureInfo.InvariantCulture) + " of " + parts.ToString(CultureInfo.InvariantCulture);

        public static string ConfirmLabel(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Yes, approve",
            WorkspaceAction.Deny => "Yes, deny",
            WorkspaceAction.Interrupt => "Yes, stop it",
            WorkspaceAction.Instruct => "Yes, send",
            WorkspaceAction.Answer => "Yes, send answer",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>Why nothing can be done right now, or null when an action is offered.</summary>
        public static string? WhyNoActions(WorkspacePresentation workspace)
        {
            if (workspace.Actions.Count > 0) return null;
            if (workspace.Character.Stale) return "Nothing can be sent until the connection is live again.";
            if (workspace.Execution == null) return "Nothing to steer until work starts.";
            if (workspace.Runtime == null) return "Nothing can be sent: the runtime is not available here.";
            return workspace.Character.Activity switch
            {
                CharacterActivity.Starting => "Nothing to steer while it starts.",
                CharacterActivity.Unknown => "Nothing can be sent while its state is unknown.",
                _ => "Its runtime offers no action in this state.",
            };
        }

        /// <summary>
        /// One line on what the work needs from the person, or else what it did last: attention first,
        /// then the latest activity that is not a turn boundary, then the status.
        /// </summary>
        public static string Peek(WorkspacePresentation workspace, int maxLength = PeekLength)
        {
            var character = workspace.Character;
            string line;
            if (character.AttentionNotes.Count > 0)
            {
                line = character.AttentionNotes[0];
                if (character.AttentionNotes.Count > 1) line += " (+" + (character.AttentionNotes.Count - 1) + " more)";
            }
            else
            {
                var latest = workspace.Activity.LastOrDefault(entry => entry.Kind != ActivityKind.Turn);
                line = latest == null ? character.StatusLabel : Describe(latest);
            }
            if (character.Stale) line = "Last known: " + line;
            return Truncate(OneLine(line), maxLength);
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

        private static string Describe(ActivityEntry entry) =>
            entry.Reported ? "Agent says: “" + OneLine(entry.Text) + "”" : OneLine(entry.Text);
    }
}
