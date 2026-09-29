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

    /// <summary>
    /// The words of the peek and of the expanded workspace, so the XR layer only lays them out. Agent
    /// text is always marked as a claim, and every state is written out, never left to color.
    /// </summary>
    public static class WorkspaceText
    {
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

        /// <summary>What the workspace says when it offers presets instead of the keyboard.</summary>
        public const string PresetPrompt = "No keyboard here. Send one of these instead:";

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
            var line = "On " + (workspace.Runtime?.DisplayName ?? execution.Runtime.DisplayName);
            if (execution.Runtime.Synthetic) line += ", simulated work";
            line += execution.TurnCount == 1 ? " · 1 turn" : " · " + execution.TurnCount.ToString(CultureInfo.InvariantCulture) + " turns";
            if (workspace.Runtime == null) line += " · the control plane no longer offers this runtime";
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
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>The question a deliberate confirmation asks, naming exactly what would be sent.</summary>
        public static string ConfirmationPrompt(WorkspaceAction action, ApprovalView? approval, string? instruction) => action switch
        {
            WorkspaceAction.Approve => "Approve this request? " + Subject(approval),
            WorkspaceAction.Deny => "Deny this request? " + Subject(approval),
            WorkspaceAction.Interrupt => "Stop the current turn? The runtime confirms when it has stopped.",
            WorkspaceAction.Instruct => "Send this instruction? “" + OneLine(instruction ?? "") + "”",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        public static string ConfirmLabel(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => "Yes, approve",
            WorkspaceAction.Deny => "Yes, deny",
            WorkspaceAction.Interrupt => "Yes, stop it",
            WorkspaceAction.Instruct => "Yes, send",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        /// <summary>Why nothing can be done right now, or null when an action is offered.</summary>
        public static string? WhyNoActions(WorkspacePresentation workspace)
        {
            if (workspace.Actions.Count > 0) return null;
            if (workspace.Character.Stale) return "Nothing can be sent until the connection is live again.";
            if (workspace.Execution == null) return "Nothing to steer until work starts.";
            if (workspace.Runtime == null) return "Nothing can be sent: the control plane no longer offers its runtime.";
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

        /// <summary>Shortens text to at most <paramref name="maxLength"/> characters, ending in an ellipsis when cut.</summary>
        public static string Truncate(string text, int maxLength)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Must be at least 1.");
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 1).TrimEnd() + "…";
        }

        /// <summary>Collapses line breaks and runs of whitespace, so text fits a single line.</summary>
        public static string OneLine(string text)
        {
            var result = new StringBuilder(text.Length);
            var space = false;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    space = result.Length > 0;
                    continue;
                }
                if (space) result.Append(' ');
                space = false;
                result.Append(character);
            }
            return result.ToString();
        }

        private static string Describe(ActivityEntry entry) =>
            entry.Reported ? "Agent says: “" + OneLine(entry.Text) + "”" : OneLine(entry.Text);

        private static string Subject(ApprovalView? approval) => approval?.Subject switch
        {
            ToolUseSubject tool => tool.ToolName + ": " + OneLine(tool.Summary),
            _ => "The runtime asked for approval.",
        };
    }
}
