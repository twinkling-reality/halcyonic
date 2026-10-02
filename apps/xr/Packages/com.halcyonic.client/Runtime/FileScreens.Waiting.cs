#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// A file's Waiting page (ADR 0026), from the work's own state alone, never an answer still being
    /// read: the request an approval answers, with Approve as the main action and Deny beside it; the
    /// agent's question with its answers as rows and Send answer; or that nothing waits. Approving or
    /// denying shows the whole request again in parts, each ending in a row to the next, Cancel in
    /// the place of the press, and Yes only once the last part has shown (<see cref="WorkspaceSteering"/>).
    /// </summary>
    public static partial class FileScreens
    {
        /// <summary>What a part's last row raises, with <see cref="RequestKey"/> or <see cref="QuestionKey"/> as its key.</summary>
        public const string NextPart = "next-part";
        public const string RequestKey = "request";
        public const string QuestionKey = "question";

        /// <summary>The words on a part's last row: the next part, or from the last back to the first, as a list's pager goes.</summary>
        public static string NextPartWords(int part, int parts) => part + 1 < parts
            ? "Next part, " + (part + 2).ToString(CultureInfo.InvariantCulture) + " of " + parts.ToString(CultureInfo.InvariantCulture)
            : "First part, 1 of " + parts.ToString(CultureInfo.InvariantCulture);

        /// <summary>Said, with the page's source line, while nothing waits for the person.</summary>
        public const string NothingWaits = "Nothing is waiting for you.";

        private static Page Waiting(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, AnswerRoom room)
        {
            var source = RuntimeSource(workspace);
            if (steering.Request(workspace) is string request && steering.Armed is WorkspaceAction answer)
            {
                var before = ApprovalFooter(workspace);
                var pressed = answer == WorkspaceAction.Approve ? PromptSlot.FarRight : PromptSlot.Secondary;
                if (before[pressed] != null) return Request(workspace, steering, screen, request, answer, before, pressed, source);
            }
            if (steering.Armed == WorkspaceAction.Answer && Asked(workspace, screen) is QuestionDraft armed)
            {
                var before = QuestionFooter(workspace, screen);
                if (before[PromptSlot.FarRight]?.Kind == PromptKind.Action) return Answers(armed, steering, workspace, before, source);
            }
            if (WorkspaceText.NeedFromYou(workspace) is NeedAnswer need) return Approval(workspace, need, room, source);
            if (Asked(workspace, screen) is QuestionDraft draft) return Question(workspace, screen, draft, source);
            return new Page(new[] { new PageLine(NothingWaits) }, source, new Footer(CloseFile));
        }

        /// <summary>An approval's footer: Close, Stop beside it, Deny beside Approve, and Approve as the main action.</summary>
        private static Footer ApprovalFooter(WorkspacePresentation workspace)
        {
            var actions = workspace.Actions;
            return new Footer(
                CloseFile,
                rare: actions.Contains(WorkspaceAction.Interrupt) ? Action(WorkspaceAction.Interrupt, Stop) : null,
                secondary: actions.Contains(WorkspaceAction.Deny) ? Action(WorkspaceAction.Deny, Deny) : null,
                farRight: actions.Contains(WorkspaceAction.Approve) ? Action(WorkspaceAction.Approve, Approve, main: true) : null);
        }

        /// <summary>
        /// What the approval asks: what it wants, in the waiting colour; the request as the runtime
        /// reported it; and what each answer does, as many notes as fit. Nothing is sent from here: Approve
        /// and Deny each show the whole request first.
        /// </summary>
        private static Page Approval(WorkspacePresentation workspace, NeedAnswer need, AnswerRoom room, string source)
        {
            var lines = new List<PageLine> { new PageLine(need.Asks, wordsAreData: true, tone: LineTone.Waiting) };
            if (need.Request != null) lines.Add(new PageLine(need.Request, wordsAreData: true, rows: RequestRows));
            var footer = ApprovalFooter(workspace);
            if (footer[PromptSlot.FarRight] == null && footer[PromptSlot.Secondary] == null && WorkspaceText.WhyNoActions(workspace) is string why)
            {
                lines.Add(new PageLine(why, tone: LineTone.Secondary, rows: 2));
            }
            var used = lines.Sum(line => line.Rows);
            foreach (var note in need.Notes)
            {
                if (used + 1 > room.Rows) break;
                lines.Add(new PageLine(note, tone: LineTone.Secondary));
                used++;
            }
            return new Page(lines, source, footer);
        }

        /// <summary>
        /// The whole request an armed approval or denial answers, a part at a time and never cut, each
        /// part ending in a row to the next; then the question the confirmation asks. Cancel takes the
        /// place of the press, and Yes stands in the free middle only once the last part has shown.
        /// </summary>
        private static Page Request(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, string request, WorkspaceAction armed,
            Footer before, PromptSlot pressed, string source)
        {
            var measured = screen.RequestRows > 0;
            var perPart = measured ? screen.RequestPartRows : RequestRows;
            var parts = measured ? screen.RequestParts : 1;
            var part = measured ? screen.RequestPart : 0;
            var lines = new List<PageLine> { new PageLine(request, wordsAreData: true, rows: perPart, fromRow: part * perPart) };
            if (parts > 1) lines.Add(new PageLine(NextPartWords(part, parts), action: NextPart, key: RequestKey));
            lines.Add(new PageLine(steering.Prompt(workspace)!, tone: LineTone.Secondary, rows: 2));
            // Yes only once the whole request has shown: until the layout measured it and the person
            // reached its last part, there is none to press.
            var yes = measured && steering.CanConfirm ? YesFor(armed) : null;
            return new Page(lines, source, Footer.Confirm(before, pressed, yes, CancelConfirm));
        }

        /// <summary>The agent's question the person reads here, while it is the one the work shows.</summary>
        private static QuestionDraft? Asked(WorkspacePresentation workspace, FileScreen screen)
        {
            var draft = screen.Place.Draft;
            var execution = workspace.Execution?.ExecutionId;
            return draft != null && execution != null && draft.Answers(execution, workspace.QuestionToAnswer) ? draft : null;
        }

        /// <summary>
        /// A question's footer: Close, Stop beside it, Hold to talk beside Send answer, and Send answer as
        /// the main action; while an answer sent may still take effect, Sent… in its place, taking no press.
        /// </summary>
        private static Footer QuestionFooter(WorkspacePresentation workspace, FileScreen screen)
        {
            var actions = workspace.Actions;
            var secret = workspace.QuestionToAnswer?.Prompts.Any(prompt => prompt.Secret) == true;
            var send = actions.Contains(WorkspaceAction.Answer)
                ? Action(WorkspaceAction.Answer, SendAnswer, main: true)
                : workspace.AnswerInFlight
                    ? new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: SentWaiting)
                    : null;
            return new Footer(
                CloseFile,
                rare: actions.Contains(WorkspaceAction.Interrupt) ? Action(WorkspaceAction.Interrupt, Stop) : null,
                secondary: screen.Speak && !secret && actions.Contains(WorkspaceAction.Answer) ? Talk : null,
                farRight: send);
        }

        /// <summary>Why Send answer takes no press while an answer sent may still take effect.</summary>
        public const string SentWaiting = "Sent. Waiting for the agent…";

        /// <summary>
        /// The agent's question (ADR 0022), a step at a time: a part of the prompt's text, then a page of
        /// its answers as rows to choose, then a row to the next part, then how it is answered. Choosing
        /// only drafts the answer; Send answer sends it. A question Halcyonic can't answer says why.
        /// </summary>
        private static Page Question(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var place = screen.Place;
            var prompt = place.Prompt;
            var asked = draft.Prompts[prompt];
            var lines = new List<PageLine>
            {
                new PageLine(WorkspaceText.PromptHeading(draft.Question, prompt), wordsAreData: true, tone: LineTone.Waiting),
                new PageLine(WorkspaceText.OneLine(asked.Text), wordsAreData: true, rows: QuestionRows, fromRow: place.TextPart * QuestionRows),
            };
            if (!draft.Question.Answerable)
            {
                lines.Add(new PageLine(WorkspaceText.CannotAnswer(draft.Question), rows: 2, tone: LineTone.Secondary));
                lines.Add(new PageLine(WorkspaceText.AgentWaits, tone: LineTone.Secondary));
            }
            else
            {
                foreach (var index in place.Answers) lines.Add(Answer(draft, prompt, index));
            }
            if (place.Steps > 1) lines.Add(new PageLine(NextPartWords(place.Step, place.Steps), action: NextPart, key: QuestionKey));
            if (draft.Question.Answerable)
            {
                var lead = WorkspaceText.QuestionLead(workspace);
                lines.Add(new PageLine(WorkspaceText.PromptHow(asked) + (lead.Length == 0 ? "" : " " + lead), tone: LineTone.Secondary, rows: 2));
            }
            return new Page(lines, source, QuestionFooter(workspace, screen));
        }

        /// <summary>One answer offered, as a row to choose, its description after its label; or Type an answer.</summary>
        private static PageLine Answer(QuestionDraft draft, int prompt, int index)
        {
            var asked = draft.Prompts[prompt];
            var key = index.ToString(CultureInfo.InvariantCulture);
            if (index < asked.Options.Count)
            {
                var option = asked.Options[index];
                var description = string.IsNullOrWhiteSpace(option.Description) ? "" : " · " + WorkspaceText.OneLine(option.Description!);
                return new PageLine(WorkspaceText.OneLine(option.Label) + description, wordsAreData: true, action: Choose, key: key, choice: true,
                    chosen: draft.IsChosen(prompt, option.Label), rows: 2);
            }
            var typed = draft.Typed(prompt);
            return new PageLine(WorkspaceText.TypedLabel(typed), wordsAreData: typed != null, action: TypeAnswer, key: key, choice: true,
                chosen: typed != null, rows: 2);
        }

        /// <summary>The answers about to be sent, a line for each prompt, and the question the confirmation asks.</summary>
        private static Page Answers(QuestionDraft draft, WorkspaceSteering steering, WorkspacePresentation workspace, Footer before, string source)
        {
            var lines = new List<PageLine>();
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                var asked = draft.Prompts[prompt];
                var given = asked.Options.Where(option => draft.IsChosen(prompt, option.Label)).Select(option => WorkspaceText.OneLine(option.Label)).ToList();
                if (draft.Typed(prompt) is string typed) given.Add("“" + WorkspaceText.OneLine(typed) + "”");
                lines.Add(new PageLine(WorkspaceText.PromptHeading(draft.Question, prompt) + ": " + string.Join(", ", given), wordsAreData: true, rows: 2));
            }
            lines.Add(new PageLine(steering.Prompt(workspace)!, tone: LineTone.Secondary));
            return new Page(lines, source, Footer.Confirm(before, PromptSlot.FarRight, YesFor(WorkspaceAction.Answer), CancelConfirm));
        }
    }
}
