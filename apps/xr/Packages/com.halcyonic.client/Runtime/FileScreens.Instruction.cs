#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public static partial class FileScreens
    {
        /// <summary>The instruction's send, one name for the action (AUDIT2, 2026-11-16 03:23Z W2); "Send This?" asks over heard words.</summary>
        public const string SendWords = "Send";

        public const string BackInstructionNotice = "instruction-notice-back";
        public const string NextInstructionNotice = "instruction-notice-next";
        public const string EditInstruction = "instruction-edit";
        public const string HideInstruction = "instruction-hide";
        public const string CancelInstruction = "instruction-cancel";
        public const string SendInstruction = "instruction-send";

        /// <summary>Send's words where it starts the task's next run on the person's choice (SWITCH); the Agent chip names whose.</summary>
        public const string StartNewRun = "Start New Run";
        public const string NextInstructionPart = "instruction-next-part";
        public const string PreviousInstructionPart = "instruction-previous-part";
        public const string InstructionNotRead = "Read Full Instruction";
        public const string InstructionEmpty = "Write Instruction First";
        public const string InstructionChanged = "Nothing was sent: the task changed.";
        /// <summary>Why Send waits while a request of this client's to the task is under way: nothing more goes until it is taken or refused.</summary>
        public const string RequestUnderWay = "Request Under Way";

        /// <summary>The composer's caption over the person's own words.</summary>
        public const string YourInstruction = "Your Instruction";

        public static PageLine InstructionLine(FileInstruction instruction, bool keyboard, int rows = 1, int? fromRow = null, bool available = true) =>
            new PageLine(instruction.Words.Length == 0 ? LineWords.Ours(keyboard ? "Instruction" : "Hold to Talk") : LineWords.Theirs(instruction.Words),
                icon: keyboard ? GlazeIcon.Type : GlazeIcon.Change, action: EditInstruction,
                control: LineControl.Input, inputFocused: instruction.InputFocused, rows: rows, fromRow: fromRow, available: available);

        public static PageLine InstructionPager(bool previous = false, bool besideNext = false) =>
            new PageLine(previous ? "Previous Part" : "Next Part", icon: previous ? GlazeIcon.Back : GlazeIcon.Next,
                action: previous ? PreviousInstructionPart : NextInstructionPart, control: LineControl.Action, besideNext: besideNext);

        /// <summary>The press on an amount in the composer of a task at its spending limit; its key is <see cref="SpendingLimit.Key"/>.</summary>
        public const string ChooseLimit = "choose-limit";

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
            // A task at its limit, or with its spend not known, carries on only under a new limit the person presses here, with these
            // words: the amounts, none chosen until pressed, each new money from now (SCREENS's spending-limit unit; AUDIT3's S1 to S4).
            if (!screen.StartsNewRun && SpendingLimit.NeedsNewLimit(workspace.Execution))
            {
                // "Counts from now" stands under the first amount, so it travels with the presses wherever they are drawn, the kit's
                // composer included, which draws a choice's quieter line and not a heading (AUDIT3's S4).
                lines.Add(new PageLine(SpendingLimit.SpendUpTo, tone: LineTone.Secondary));
                foreach (var amount in SpendingLimit.Amounts)
                    lines.Add(new PageLine(SpendingLimit.More(amount), action: ChooseLimit, key: SpendingLimit.Key(amount), choice: true,
                        chosen: instruction.NewLimit == amount, answerMark: AnswerMark.Single)
                        .WithUnder(amount == SpendingLimit.Amounts[0] ? LineWords.Ours(SpendingLimit.CountsFromNow) : null));
            }
            var reason = InstructionSendReason(workspace, screen);
            return new Page(lines, null, new Footer(new Prompt(screen.InstructionHideOffered ? HideInstruction : CancelInstruction, screen.InstructionHideOffered ? "Hide Composer" : "Cancel", GlazeIcon.Close, PromptKind.Close),
                rare: screen.InstructionHideOffered ? new Prompt(CancelInstruction, "Discard Draft", GlazeIcon.Close) : null,
                secondary: screen.Speak ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true,
                    available: WhyNoInstruction(workspace) == null, reason: WhyNoInstruction(workspace)) : null,
                farRight: new Prompt(SendInstruction, screen.StartsNewRun ? StartNewRun : SendWords, GlazeIcon.TellIt, main: true,
                    available: reason == null, reason: reason)));
        }

        /// <summary>
        /// Why no instruction can be sent to the work now, whatever is written: it waits on an approval first, a request of
        /// this client's to it is still under way, or it takes no instructions, in production's words; null while it takes one.
        /// </summary>
        public static string? WhyNoInstruction(WorkspacePresentation workspace)
        {
            if (WorkspaceText.NeedFromYou(workspace) != null) return "Answer Approval First";
            if (RequestPending(workspace)) return RequestUnderWay;
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct)) return WorkspaceText.WhyNoActions(workspace) ?? "Not Taking Instructions";
            return null;
        }

        /// <summary>
        /// Whether a request of this client's to the task is still under way: being sent, or accepted and not yet confirmed.
        /// A start pending leaves the old execution at rest, so an instruction now would set it going beside the new run.
        /// </summary>
        public static bool RequestPending(WorkspacePresentation workspace) =>
            workspace.Commands.Any(command => command.Waits || command.Status == CommandStatus.Accepted);

        public static string? InstructionSendReason(WorkspacePresentation workspace, FileScreen screen)
        {
            var instruction = screen.Instruction;
            if (!instruction.Open || instruction.ExecutionId != workspace.Execution?.ExecutionId) return InstructionChanged;
            if (WhyNoInstruction(workspace) is string why) return why;
            if (instruction.Words.Length == 0) return InstructionEmpty;
            if (!instruction.Read) return InstructionNotRead;
            // A task at its limit, or with its spend not known, takes no instruction without a new limit (commands.ts, spending_cap_usd).
            return SpendingLimit.NeedsNewLimit(workspace.Execution) && instruction.NewLimit == null ? SpendingLimit.ChooseAmountFirst : null;
        }
    }
}
