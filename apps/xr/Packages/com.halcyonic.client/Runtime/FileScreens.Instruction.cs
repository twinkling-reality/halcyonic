#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public static partial class FileScreens
    {
        public const string EditInstruction = "instruction-edit";
        public const string CancelInstruction = "instruction-cancel";
        public const string SendInstruction = "instruction-send";
        public const string NextInstructionPart = "instruction-next-part";
        public const string PreviousInstructionPart = "instruction-previous-part";
        public const string InstructionNotRead = "Read the whole instruction first.";
        public const string InstructionEmpty = "Write an instruction first.";
        public const string InstructionChanged = "Nothing was sent: the instruction's task changed.";

        public static PageLine InstructionLine(FileInstruction instruction, bool keyboard, int rows = 1, int? fromRow = null, bool available = true) =>
            new PageLine(instruction.Words.Length == 0 ? LineWords.Ours(keyboard ? "Type your instruction" : "Hold to talk below") : LineWords.Theirs(instruction.Words),
                icon: keyboard ? GlazeIcon.Type : GlazeIcon.Change, action: EditInstruction,
                control: LineControl.Input, inputFocused: instruction.InputFocused, rows: rows, fromRow: fromRow, available: available);

        public static PageLine InstructionPager(bool previous = false, bool besideNext = false) =>
            new PageLine(previous ? "Previous part" : "Next part", icon: previous ? GlazeIcon.Back : GlazeIcon.Next,
                action: previous ? PreviousInstructionPart : NextInstructionPart, control: LineControl.Action, besideNext: besideNext);

        private static Page InstructionPage(WorkspacePresentation workspace, FileScreen screen)
        {
            var instruction = screen.Instruction;
            var lines = new List<PageLine>
            {
                new PageLine("Your instruction", tone: LineTone.Secondary),
                InstructionLine(instruction, screen.KeyboardOffered, instruction.PartRows, instruction.Part * instruction.PartRows,
                    workspace.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(workspace) == null),
            };
            if (instruction.Part > 0)
                lines.Add(InstructionPager(previous: true, besideNext: instruction.PairedPager && instruction.Part + 1 < instruction.Parts));
            if (instruction.Part + 1 < instruction.Parts) lines.Add(InstructionPager());
            var reason = InstructionSendReason(workspace, screen);
            return new Page(lines, null, new Footer(new Prompt(CancelInstruction, "Cancel", GlazeIcon.Close, PromptKind.Close),
                secondary: screen.Speak ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true,
                    available: workspace.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(workspace) == null,
                    reason: workspace.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(workspace) == null ? null : WorkspaceText.NeedFromYou(workspace) != null ? "The agent needs your approval first." : WorkspaceText.WhyNoActions(workspace) ?? "It no longer takes instructions.") : null,
                farRight: new Prompt(SendInstruction, "Send instruction", GlazeIcon.TellIt, main: true, available: reason == null, reason: reason)));
        }

        public static string? InstructionSendReason(WorkspacePresentation workspace, FileScreen screen)
        {
            var instruction = screen.Instruction;
            if (!instruction.Open || instruction.ExecutionId != workspace.Execution?.ExecutionId) return InstructionChanged;
            if (WorkspaceText.NeedFromYou(workspace) != null) return "The agent needs your approval first.";
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct)) return WorkspaceText.WhyNoActions(workspace) ?? "It no longer takes instructions.";
            if (instruction.Words.Length == 0) return InstructionEmpty;
            return !instruction.Read ? InstructionNotRead : null;
        }
    }
}
