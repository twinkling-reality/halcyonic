#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public static partial class FileScreens
    {
        public const string BackInstructionNotice = "instruction-notice-back";
        public const string NextInstructionNotice = "instruction-notice-next";
        public const string EditInstruction = "instruction-edit";
        public const string HideInstruction = "instruction-hide";
        public const string CancelInstruction = "instruction-cancel";
        public const string SendInstruction = "instruction-send";
        public const string NextInstructionPart = "instruction-next-part";
        public const string PreviousInstructionPart = "instruction-previous-part";
        public const string InstructionNotRead = "Read Full Instruction";
        public const string InstructionEmpty = "Write Instruction First";
        public const string InstructionChanged = "Not Sent · Task Changed";

        /// <summary>The composer's caption over the person's own words.</summary>
        public const string YourInstruction = "Your Instruction";

        public static PageLine InstructionLine(FileInstruction instruction, bool keyboard, int rows = 1, int? fromRow = null, bool available = true) =>
            new PageLine(instruction.Words.Length == 0 ? LineWords.Ours(keyboard ? "Instruction" : "Hold to Talk") : LineWords.Theirs(instruction.Words),
                icon: keyboard ? GlazeIcon.Type : GlazeIcon.Change, action: EditInstruction,
                control: LineControl.Input, inputFocused: instruction.InputFocused, rows: rows, fromRow: fromRow, available: available);

        public static PageLine InstructionPager(bool previous = false, bool besideNext = false) =>
            new PageLine(previous ? "Previous Part" : "Next Part", icon: previous ? GlazeIcon.Back : GlazeIcon.Next,
                action: previous ? PreviousInstructionPart : NextInstructionPart, control: LineControl.Action, besideNext: besideNext);

        public static PageLine ReadResultsLine(int rows = 1) => new PageLine("Read Results", icon: GlazeIcon.Next, action: OpenResults, opens: true, rows: rows);

        private static Page InstructionPage(WorkspacePresentation workspace, FileScreen screen)
        {
            var instruction = screen.Instruction;
            if (instruction.ExplainingNotice && instruction.Notice != null)
                return new Page(new[]
                {
                    new PageLine("What Happened", tone: LineTone.Secondary),
                    new PageLine(instruction.Notice, rows: Math.Min(instruction.NoticePartRows, instruction.NoticeRows - instruction.NoticePart * instruction.NoticePartRows),
                        fromRow: instruction.NoticePart * instruction.NoticePartRows),
                }, null, new Footer(new Prompt(screen.InstructionHideOffered ? HideInstruction : CancelInstruction, screen.InstructionHideOffered ? "Hide Composer" : "Cancel", GlazeIcon.Close, PromptKind.Close),
                    rare: screen.InstructionHideOffered ? new Prompt(CancelInstruction, "Discard Draft", GlazeIcon.Close) : null,
                    secondary: new Prompt(BackInstructionNotice, "Back to Instruction", GlazeIcon.Back),
                    farRight: instruction.NoticeParts > 1 ? new Prompt(NextInstructionNotice, Footer.NextPageWords(instruction.NoticePart, instruction.NoticeParts), GlazeIcon.Next, PromptKind.NextPage) : null));
            var lines = new List<PageLine>
            {
                new PageLine(YourInstruction, tone: LineTone.Secondary),
                InstructionLine(instruction, screen.KeyboardOffered, instruction.PartRows, instruction.Part * instruction.PartRows,
                    WhyNoInstruction(workspace) == null),
            };
            if (instruction.Part > 0)
                lines.Add(InstructionPager(previous: true, besideNext: instruction.PairedPager && instruction.Part + 1 < instruction.Parts));
            if (instruction.Part + 1 < instruction.Parts) lines.Add(InstructionPager());
            if (screen.InstructionResultsOffered) lines.Add(ReadResultsLine(screen.ResultsRows));
            var reason = InstructionSendReason(workspace, screen);
            return new Page(lines, null, new Footer(new Prompt(screen.InstructionHideOffered ? HideInstruction : CancelInstruction, screen.InstructionHideOffered ? "Hide Composer" : "Cancel", GlazeIcon.Close, PromptKind.Close),
                rare: screen.InstructionHideOffered ? new Prompt(CancelInstruction, "Discard Draft", GlazeIcon.Close) : null,
                secondary: screen.Speak ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true,
                    available: WhyNoInstruction(workspace) == null, reason: WhyNoInstruction(workspace)) : null,
                farRight: new Prompt(SendInstruction, "Send Instruction", GlazeIcon.TellIt, main: true, available: reason == null, reason: reason)));
        }

        /// <summary>
        /// Why no instruction can be sent to the work now, whatever is written: it waits on an approval first, or it
        /// takes no instructions, in production's words; null while it takes one.
        /// </summary>
        public static string? WhyNoInstruction(WorkspacePresentation workspace)
        {
            if (WorkspaceText.NeedFromYou(workspace) != null) return "Answer Approval First";
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct)) return WorkspaceText.WhyNoActions(workspace) ?? "Not Taking Instructions";
            return null;
        }

        public static string? InstructionSendReason(WorkspacePresentation workspace, FileScreen screen)
        {
            var instruction = screen.Instruction;
            if (!instruction.Open || instruction.ExecutionId != workspace.Execution?.ExecutionId) return InstructionChanged;
            if (WhyNoInstruction(workspace) is string why) return why;
            if (instruction.Words.Length == 0) return InstructionEmpty;
            return !instruction.Read ? InstructionNotRead : null;
        }
    }
}
