#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

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
            var source = AgentSource;
            if (WorkspaceText.NeedFromYou(workspace) is NeedAnswer need) return Approval(workspace, need, room, source);
            if (Asked(workspace, screen) is QuestionDraft draft) return Question(workspace, screen, draft, source);
            return new Page(new[] { new PageLine(NothingWaits) }, source, new Footer(CloseFile));
        }

        /// <summary>
        /// The confirmation of whatever <paramref name="steering"/> has armed, on any section: the whole
        /// request an approval or denial answers, the answers about to be sent, or what stopping or the
        /// instruction would do; Close, Cancel in the place of the press, and Yes in the free middle.
        /// </summary>
        private static Page Confirmation(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, WorkspaceAction armed)
        {
            var source = AgentSource;
            var (pressed, before) = Pressed(armed);
            if ((armed == WorkspaceAction.Approve || armed == WorkspaceAction.Deny) && steering.Request(workspace) is string request)
            {
                return Request(workspace, steering, screen, request, armed, before, pressed, source);
            }
            if (armed == WorkspaceAction.Answer && Asked(workspace, screen) is QuestionDraft draft) return Answers(draft, steering, workspace, before, source);
            return new Page(new[] { new PageLine(steering.Prompt(workspace)!, wordsAreData: armed == WorkspaceAction.Instruct, rows: 3) }, source,
                Footer.Confirm(before, pressed, YesFor(armed), CancelConfirm));
        }

        /// <summary>Where an armed action's press stood, and a footer holding it there, which its confirmation replaces.</summary>
        private static (PromptSlot Slot, Footer Before) Pressed(WorkspaceAction armed)
        {
            var (slot, id) = armed switch
            {
                WorkspaceAction.Approve => (PromptSlot.FarRight, Approve),
                WorkspaceAction.Deny => (PromptSlot.Secondary, Deny),
                WorkspaceAction.Interrupt => (PromptSlot.Rare, Stop),
                WorkspaceAction.Instruct => (PromptSlot.FarRight, TellIt),
                _ => (PromptSlot.FarRight, SendAnswer),
            };
            return (slot, new Footer(CloseFile).With(slot, Action(armed, id, main: slot == PromptSlot.FarRight)));
        }

        /// <summary>
        /// An approval's footer: Close, Stop beside it, Deny beside Approve, and Approve as the main
        /// action; while a decision sent on it may still take effect, Sent… in Approve's place, taking no
        /// press, and no Deny, so a second decision never races the first.
        /// </summary>
        private static Footer ApprovalFooter(WorkspacePresentation workspace)
        {
            var actions = workspace.Actions;
            var approve = actions.Contains(WorkspaceAction.Approve)
                ? Action(WorkspaceAction.Approve, Approve, main: true)
                : workspace.ApprovalInFlight
                    ? new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.Approve, main: true, available: false, reason: SentWaiting)
                    : null;
            return new Footer(
                CloseFile,
                rare: actions.Contains(WorkspaceAction.Interrupt) ? Action(WorkspaceAction.Interrupt, Stop) : null,
                secondary: actions.Contains(WorkspaceAction.Deny) ? Action(WorkspaceAction.Deny, Deny) : null,
                farRight: approve);
        }

        /// <summary>
        /// What the approval asks: what it wants, in the waiting colour; the request as the runtime
        /// reported it; and what each answer does, as many notes as fit. Nothing is sent from here: Approve
        /// and Deny each show the whole request first.
        /// </summary>
        private static Page Approval(WorkspacePresentation workspace, NeedAnswer need, AnswerRoom room, string source)
        {
            var rows = WithSource(room).Rows;
            var lines = new List<PageLine> { new PageLine(need.Asks, wordsAreData: true, tone: LineTone.Waiting) };
            // The request as much as fits under what it wants; Approve and Deny show it whole.
            if (need.Request != null) lines.Add(new PageLine(need.Request, wordsAreData: true, rows: Math.Max(1, Math.Min(RequestRows, rows - 1))));
            var footer = ApprovalFooter(workspace);
            if (footer[PromptSlot.FarRight] == null && footer[PromptSlot.Secondary] == null && WorkspaceText.WhyNoActions(workspace) is string why)
            {
                lines.Add(new PageLine(why, tone: LineTone.Secondary, rows: 2));
            }
            var used = lines.Sum(line => line.Rows);
            foreach (var note in need.Notes)
            {
                if (used + 1 > rows) break;
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
            // A measurement counts only for this arming and this very text; one left from another
            // confirmation, or of a request that has since read differently, is none.
            var measured = screen.Measured(steering, request);
            var perPart = measured ? screen.RequestPartRows : RequestRows;
            var parts = measured ? screen.RequestParts : 1;
            var part = measured ? screen.RequestPart : 0;
            var lines = new List<PageLine> { new PageLine(request, wordsAreData: true, rows: perPart, fromRow: part * perPart) };
            if (parts > 1) lines.Add(new PageLine(NextPartWords(part, parts), action: NextPart, key: RequestKey));
            lines.Add(new PageLine(steering.Prompt(workspace)!, tone: LineTone.Secondary, rows: 2));
            // Approve's Yes only once the whole request has shown: until the layout measured it and
            // the person reached its last part, there is none to press. Deny's shows at once, as
            // denying runs nothing and a person who sees part 1 of something dangerous must be able to
            // refuse it then.
            var yes = measured && steering.CanConfirm ? YesFor(armed) : null;
            return new Page(lines, source, Footer.Confirm(before, pressed, yes, CancelConfirm));
        }

        /// <summary>The agent's question the person reads here, while it is the one the work shows.</summary>
        private static QuestionDraft? Asked(WorkspacePresentation workspace, FileScreen screen)
        {
            var draft = screen.Question.Draft;
            var execution = workspace.Execution?.ExecutionId;
            return draft != null && execution != null && draft.Answers(execution, workspace.QuestionToAnswer) ? draft : null;
        }

        // A question's words, as the coordinator settled them on 2026-10-02.
        public const string OpenToRead = "Open the question to read the rest.";
        public const string SendFromYourAnswers = "Answer each question, then send from Your answers.";
        public const string TypeMyAnswer = "Type my answer";
        public const string YourAnswers = "Your answers";

        /// <summary>What a row on the question's page raises: more answers, the next question, or a prompt from the person's answers, by its index.</summary>
        public const string MoreAnswers = "more-answers";
        public const string NextQuestion = "next-question";
        public const string GoToQuestion = "go-to-question";

        /// <summary>The words on the row for more answers: the next page, or from the last back to the first.</summary>
        public static string MoreAnswersWords(int page, int pages) => page + 1 < pages
            ? "More answers, " + (page + 2).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture)
            : "First answers, 1 of " + pages.ToString(CultureInfo.InvariantCulture);

        /// <summary>The words on the row after a prompt: the next question, or after the last, the person's answers.</summary>
        public static string NextQuestionWords(int prompt, int prompts) => prompt + 1 < prompts
            ? "Next question, " + (prompt + 2).ToString(CultureInfo.InvariantCulture) + " of " + prompts.ToString(CultureInfo.InvariantCulture)
            : YourAnswers;

        /// <summary>The row for the person's own answer: Type my answer, or once typed, their words as written, chosen.</summary>
        public static string TypedWords(string? typed) => typed == null ? TypeMyAnswer : "Your answer: “" + WorkspaceText.OneLine(typed) + "”";

        /// <summary>
        /// Why Send answer can't send yet, or null when it can: a question of several prompts sends only
        /// from the person's answers; every prompt answered; and every question read whole, a cut one
        /// only once its side panel has shown.
        /// </summary>
        private static string? SendProblem(QuestionDraft draft, FileQuestion question)
        {
            if (draft.Prompts.Count > 1 && !question.Reviewing) return SendFromYourAnswers;
            if (!(draft.Problem is string problem)) return null;
            var answered = Enumerable.Range(0, draft.Prompts.Count).All(draft.IsAnswered);
            return answered && WorkspaceText.Answerable(draft.Question) ? OpenToRead : problem;
        }

        /// <summary>
        /// A question's footer: Close, Stop beside it, Hold to talk beside Send answer, and Send answer as
        /// the main action, waiting in its place with its reason until it can send; while an answer sent
        /// may still take effect, Sent… in its place, taking no press.
        /// </summary>
        private static Footer QuestionFooter(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft)
        {
            var actions = workspace.Actions;
            var secret = workspace.QuestionToAnswer?.Prompts.Any(prompt => prompt.Secret) == true;
            Prompt? send = null;
            if (actions.Contains(WorkspaceAction.Answer))
            {
                var problem = SendProblem(draft, screen.Question);
                send = new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), WorkspaceText.IconOf(WorkspaceAction.Answer), main: true,
                    available: problem == null, reason: problem);
            }
            else if (workspace.AnswerInFlight)
            {
                send = new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: SentWaiting);
            }
            return new Footer(
                CloseFile,
                rare: actions.Contains(WorkspaceAction.Interrupt) ? Action(WorkspaceAction.Interrupt, Stop) : null,
                secondary: screen.Speak && !secret && actions.Contains(WorkspaceAction.Answer) ? Talk : null,
                farRight: send);
        }

        /// <summary>Why Send answer takes no press while an answer sent may still take effect.</summary>
        public const string SentWaiting = "Sent. Waiting for the agent…";

        /// <summary>
        /// The agent's question (ADR 0022, ADR 0026), one prompt at a time: its question, quoted, heading
        /// every page in at most two rows, a longer one cut and opening its side panel; its answers in
        /// the agent's order as rows to choose, a longer one cut and showing all its words beside the
        /// page once chosen; the row for the person's own answer last; then a row for more answers and a
        /// row on to the next question. Choosing only drafts the answer; Send answer sends it. A question
        /// Halcyonic can't answer says why. After the last of several prompts, the person's answers.
        /// </summary>
        private static Page Question(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var question = screen.Question;
            if (question.Reviewing) return YourAnswersPage(workspace, screen, draft, source);
            var prompt = question.Prompt;
            var asked = draft.Prompts[prompt];
            var cut = question.QuestionCut(prompt);
            var opened = cut && screen.Chosen == QuestionKey;
            var lines = new List<PageLine>
            {
                new PageLine("“" + WorkspaceText.OneLine(asked.Text) + "”", wordsAreData: true, rows: question.QuestionShows(prompt),
                    action: cut ? Open : null, key: cut ? QuestionKey : null, opens: cut, chosen: opened),
            };
            SidePanel? side = null;
            if (opened)
            {
                side = new SidePanel(WorkspaceText.PromptHeading(draft.Question, prompt), subjectIsData: true,
                    lines: new[] { new PageLine("“" + WorkspaceText.OneLine(asked.Text) + "”", wordsAreData: true, rows: question.QuestionMeasured(prompt)) },
                    source: source);
            }
            question.Shown(prompt, sidePanel: opened);
            if (!WorkspaceText.Answerable(draft.Question))
            {
                lines.Add(new PageLine(WorkspaceText.CannotAnswer(draft.Question), rows: 2, tone: LineTone.Secondary));
                lines.Add(new PageLine(WorkspaceText.AgentWaits, tone: LineTone.Secondary));
            }
            else
            {
                foreach (var index in question.Answers) lines.Add(Answer(draft, question, prompt, index));
                if (asked.FreeText)
                {
                    var typed = draft.Typed(prompt);
                    lines.Add(new PageLine(TypedWords(typed), wordsAreData: typed != null, action: TypeAnswer,
                        key: asked.Options.Count.ToString(CultureInfo.InvariantCulture), choice: true, chosen: typed != null, rows: typed == null ? 1 : 2));
                }
                if (question.Pages > 1) lines.Add(new PageLine(MoreAnswersWords(question.Page, question.Pages), action: MoreAnswers));
            }
            if (draft.Prompts.Count > 1) lines.Add(new PageLine(NextQuestionWords(prompt, draft.Prompts.Count), action: NextQuestion));
            return new Page(lines, source, QuestionFooter(workspace, screen, draft), side);
        }

        /// <summary>One answer offered, as a row to choose, its description after its label, in at most two rows.</summary>
        private static PageLine Answer(QuestionDraft draft, FileQuestion question, int prompt, int index)
        {
            var option = draft.Prompts[prompt].Options[index];
            var description = string.IsNullOrWhiteSpace(option.Description) ? "" : " · " + WorkspaceText.OneLine(option.Description!);
            return new PageLine(WorkspaceText.OneLine(option.Label) + description, wordsAreData: true, action: Choose,
                key: index.ToString(CultureInfo.InvariantCulture), choice: true, chosen: draft.IsChosen(prompt, option.Label),
                rows: question.AnswerShows(prompt, index));
        }

        /// <summary>A prompt's name on the person's answers: its header as the agent wrote it, else which question it is.</summary>
        private static string Name(QuestionView question, int prompt) => string.IsNullOrWhiteSpace(question.Prompts[prompt].Header)
            ? "Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture)
            : WorkspaceText.OneLine(question.Prompts[prompt].Header!);

        /// <summary>
        /// The person's answers to a question of several prompts, the only page it sends from: each
        /// prompt's name, its answer as the row's small fact, and choosing a row goes back to that prompt.
        /// </summary>
        private static Page YourAnswersPage(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var lines = new List<PageLine>();
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                var asked = draft.Prompts[prompt];
                var given = asked.Options.Where(option => draft.IsChosen(prompt, option.Label)).Select(option => WorkspaceText.OneLine(option.Label)).ToList();
                if (draft.Typed(prompt) is string typed) given.Add("“" + WorkspaceText.OneLine(typed) + "”");
                lines.Add(new PageLine(Name(draft.Question, prompt), wordsAreData: true, fact: given.Count == 0 ? "Not answered" : string.Join(", ", given),
                    action: GoToQuestion, key: prompt.ToString(CultureInfo.InvariantCulture)));
            }
            return new Page(lines, source, QuestionFooter(workspace, screen, draft));
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
