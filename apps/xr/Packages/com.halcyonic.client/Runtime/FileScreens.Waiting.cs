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
            if (workspace.QuestionToAnswer is QuestionView pending && pending.Prompts.Count > 0) return NotLaidOut(workspace, pending, source);
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
            // The request an approval answers, and an instruction's words, show whole, in parts where they are long.
            if (steering.ToRead(workspace) is string request) return Request(workspace, steering, screen, request, armed, before, pressed, source);
            if (armed == WorkspaceAction.Answer && Asked(workspace, screen) is QuestionDraft draft) return Answers(draft, steering, workspace, before, source);
            return new Page(new[] { new PageLine(steering.Prompt(workspace)!, rows: 3) }, source, Footer.Confirm(before, pressed, YesFor(armed), CancelConfirm));
        }

        /// <summary>
        /// The question a confirmation asks below what it shows whole: an approval's or a denial's own, or
        /// for an instruction, whose words show above it rather than inside it, to read to the last of its
        /// <paramref name="parts"/> first, as a locked final press says, then whether to send them.
        /// </summary>
        public static string Asks(WorkspaceSteering steering, WorkspacePresentation workspace, int parts)
        {
            if (steering.Armed != WorkspaceAction.Instruct) return steering.Prompt(workspace) ?? "";
            return steering.CanConfirm ? WorkspaceText.SendWordsAbove(steering.Heard) : EntryText.ReadToPart(parts);
        }

        /// <summary>
        /// Every question <see cref="Asks"/> may ask before the confirmation is answered, so a part's rows stay
        /// put as it changes; an instruction's at its widest, a part numbered in two digits.
        /// </summary>
        public static IReadOnlyList<string> AllAsks(WorkspaceSteering steering) => steering.Armed == WorkspaceAction.Instruct
            ? new[] { EntryText.ReadToPart(99), WorkspaceText.SendWordsAbove(steering.Heard) }
            : steering.Prompts();

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

        /// <summary>Said while a question waits but isn't laid out on this page yet.</summary>
        public const string QuestionNotReady = "Getting the question ready.";

        /// <summary>
        /// A question that waits but isn't laid out here yet: what it asks, cut, and Send answer waiting in
        /// its place with why, never that nothing waits.
        /// </summary>
        private static Page NotLaidOut(WorkspacePresentation workspace, QuestionView pending, string source)
        {
            var lines = new[] { new PageLine(QuestionWords(pending.Prompts[0]), rows: FileQuestion.QuestionRows) };
            var actions = workspace.Actions;
            var send = actions.Contains(WorkspaceAction.Answer)
                ? new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), WorkspaceText.IconOf(WorkspaceAction.Answer), main: true, available: false,
                    reason: QuestionNotReady)
                : null;
            return new Page(lines, source, new Footer(CloseFile, rare: StopWhereUnanswerable(workspace, pending), farRight: send));
        }

        /// <summary>
        /// Stop, on Waiting only where Halcyonic can't answer the question, as one asking for a secret or
        /// marked unanswerable, or where a prompt offers answers that read the same, which can't be chosen:
        /// stopping is then the way on, or the only way to the one meant (ADR 0022; settled by the
        /// coordinator, 2026-10-07). Elsewhere Stop stands on Activity (ADR 0026).
        /// </summary>
        private static Prompt? StopWhereUnanswerable(WorkspacePresentation workspace, QuestionView question) =>
            StopsHere(workspace, question) ? Action(WorkspaceAction.Interrupt, Stop) : null;

        /// <summary>Whether Waiting offers Stop under this question (<see cref="StopWhereUnanswerable"/>).</summary>
        public static bool StopsHere(WorkspacePresentation workspace, QuestionView question) =>
            workspace.Actions.Contains(WorkspaceAction.Interrupt)
            && (!WorkspaceText.Answerable(question) || question.Prompts.Any(WorkspaceText.OffersALabelTwice));

        /// <summary>
        /// An approval's footer: Close, Deny beside Approve, and Approve as the main action, Stop standing
        /// on Activity; while a decision sent on it may still take effect, Sent… in Approve's place,
        /// taking no press, and no Deny, so a second decision never races the first.
        /// </summary>
        private static Footer ApprovalFooter(WorkspacePresentation workspace)
        {
            var actions = workspace.Actions;
            var approve = actions.Contains(WorkspaceAction.Approve)
                ? Action(WorkspaceAction.Approve, Approve, main: true)
                : workspace.ApprovalInFlight
                    ? new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.Approve, main: true, available: false, reason: SentWaiting, waits: true)
                    : null;
            return new Footer(
                CloseFile,
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
            var lines = new List<PageLine> { new PageLine(need.AsksRuns, tone: LineTone.Waiting) };
            // The request as much as fits under what it wants; Approve and Deny show it whole.
            if (need.Request != null) lines.Add(new PageLine(LineWords.TheirName(need.Request), rows: Math.Max(1, Math.Min(RequestRows, rows - 1))));
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
        /// The whole request an armed approval or denial answers, or the armed instruction's words, a part
        /// at a time and never cut, each part ending in a row to the next; then the question the
        /// confirmation asks. Cancel takes the
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
            var lines = new List<PageLine> { RequestLine(steering, request, perPart, part * perPart) };
            if (parts > 1) lines.Add(new PageLine(NextPartWords(part, parts), action: NextPart, key: RequestKey));
            lines.Add(new PageLine(Asks(steering, workspace, parts), tone: LineTone.Secondary, rows: 2));
            // Approve's Yes, and an instruction's, only once all of it has shown: until the layout
            // measured it and the person reached its last part, there is none to press. Deny's shows at
            // once, as denying runs nothing and a person who sees part 1 of something dangerous must be
            // able to refuse it then.
            var yes = measured && steering.CanConfirm ? YesFor(armed) : null;
            return new Page(lines, source, Footer.Confirm(before, pressed, yes, CancelConfirm));
        }

        /// <summary>
        /// The request a confirmation reads whole, as its parts show it: an approval's request, the agent
        /// app's, or an instruction's words, the person's, a block leaning on Halcyonic's ground, as
        /// everything Halcyonic didn't write is (lane V's ruling, 2026-10-08).
        /// </summary>
        /// <remarks>
        /// Where an approval names no tool, what is read is Halcyonic's own sentence, upright (the independent review,
        /// 2026-10-08). An approval's request is <see cref="LabelText.Exact"/>'s, which <see cref="LabelText.Name"/>
        /// leaves as it is.
        /// </remarks>
        public static PageLine RequestLine(WorkspaceSteering steering, string request, int rows = 1, int? fromRow = null) =>
            steering.Armed != WorkspaceAction.Instruct && request == WorkspaceText.ApprovalWithoutTool
                ? new PageLine(request, rows: rows, fromRow: fromRow)
                : new PageLine(LineWords.TheirName(request), rows: rows, fromRow: fromRow);

        /// <summary>
        /// The agent's question as its lines show it: its words alone, leaning beside Halcyonic's quote mark, never
        /// quoted; words that show as nothing, as a lone U+0085, spelled by code point, so its page is still drawn.
        /// </summary>
        public static LineWords QuestionWords(QuestionPrompt asked) => LineWords.TheirName(asked.Text);

        /// <summary>The agent's question the person reads here, while it is the one the work shows.</summary>
        private static QuestionDraft? Asked(WorkspacePresentation workspace, FileScreen screen)
        {
            var draft = screen.Question.Draft;
            var execution = workspace.Execution?.ExecutionId;
            return draft != null && execution != null && draft.Answers(execution, workspace.QuestionToAnswer) ? draft : null;
        }

        // A question's words, as the coordinator settled them on 2026-10-02.
        public const string SendFromYourAnswers = "Answer each question, then send from Your answers.";
        public const string TypeMyAnswer = "Type my answer";
        public const string YourAnswers = "Your answers";
        public const string OnToTheAnswers = "On to the answers";
        public const string ReadTheQuestion = "Read the whole question first.";
        public const string ReadTheAnswer = "Read the whole answer you chose first, or choose another.";
        public const string ReadYourAnswers = "Read all your answers first.";

        /// <summary>What a row on the question's page raises: more answers, the next question, or a prompt from the person's answers, by its index.</summary>
        public const string MoreAnswers = "more-answers";
        public const string NextQuestion = "next-question";
        public const string GoToQuestion = "go-to-question";

        /// <summary>The words on the row for more answers: the next page, or from the last back to the first.</summary>
        public static string MoreAnswersWords(int page, int pages) => page + 1 < pages
            ? "More answers, " + (page + 2).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture)
            : "First answers, 1 of " + pages.ToString(CultureInfo.InvariantCulture);

        /// <summary>The words on the row turning the person's answers, where they take more than one page.</summary>
        public static string YourAnswersWords(int page, int pages) =>
            YourAnswers + ", " + (page + 1 < pages ? page + 2 : 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture);

        /// <summary>The words on the row after a prompt: the next question, or after the last, the person's answers.</summary>
        public static string NextQuestionWords(int prompt, int prompts) => prompt + 1 < prompts
            ? "Next question, " + (prompt + 2).ToString(CultureInfo.InvariantCulture) + " of " + prompts.ToString(CultureInfo.InvariantCulture)
            : YourAnswers;

        /// <summary>The words on the row ending a long question's part: the next part, or after the last, on to its answers.</summary>
        public static string QuestionPartWords(int part, int parts, int answerPages) => part + 1 < parts
            ? NextPartWords(part, parts)
            : answerPages > 1 ? OnToTheAnswers + ", 1 of " + answerPages.ToString(CultureInfo.InvariantCulture) : OnToTheAnswers;

        /// <summary>The row for the person's own answer: Type my answer, or once typed, their words as written, chosen.</summary>
        public static string TypedWords(string? typed) => TypedRuns(typed).Text;

        /// <summary>
        /// The row for the person's own answer as runs: Type my answer, or once typed or heard, "Your
        /// answer: " and their words, leaning as everything Halcyonic didn't write does, never quoted
        /// (lane V's ruling, 2026-10-08).
        /// </summary>
        public static LineWords TypedRuns(string? typed) =>
            typed == null || LineWords.Theirs(typed).IsEmpty ? LineWords.Ours(TypeMyAnswer) : LineWords.Ours("Your answer: ") + LineWords.Theirs(typed);

        /// <summary>The row for the person's own answer, as the page draws it and the layout measures it.</summary>
        public static PageLine TypedRow(string? typed, string key, bool besideNext = false, int rows = 1) => typed == null
            ? new PageLine(TypeMyAnswer, action: TypeAnswer, key: key, choice: true, besideNext: besideNext)
            : new PageLine(TypedRuns(typed), action: TypeAnswer, key: key, choice: true, chosen: true, rows: rows, besideNext: besideNext);

        /// <summary>
        /// A prompt's answer as the page of the person's answers shows it, whole: its name, then each label
        /// chosen and the words typed. The layout measures exactly these words (<see cref="FileQuestion.MeasureReview"/>).
        /// </summary>
        public static string ReviewWords(QuestionDraft draft, int prompt) => ReviewRuns(draft, prompt).Text;

        /// <summary>
        /// A prompt's answer on the page of the person's answers, as runs: its name, the agent's header
        /// leaning, then each label chosen, the agent's, leaning, and the words typed, the person's,
        /// leaning as the person's own words.
        /// </summary>
        public static LineWords ReviewRuns(QuestionDraft draft, int prompt) =>
            NameRuns(draft.Question, prompt) + LineWords.Ours(": ") + Given(draft, prompt, LineWords.Ours("Not answered"));

        /// <summary>The answers given to a prompt, the agent's labels and the person's words leaning, or <paramref name="none"/>.</summary>
        private static LineWords Given(QuestionDraft draft, int prompt, LineWords none)
        {
            var asked = draft.Prompts[prompt];
            var given = asked.Options.Where(option => draft.IsChosen(prompt, option.Label)).Select(option => LineWords.TheirName(option.Label)).ToList();
            if (draft.Typed(prompt) is string typed && LineWords.Theirs(typed) is { IsEmpty: false } words) given.Add(words);
            if (given.Count == 0) return none;
            var joined = given[0];
            for (var index = 1; index < given.Count; index++) joined = joined + LineWords.Ours(", ") + given[index];
            return joined;
        }

        /// <summary>
        /// Why Send answer can't send yet, or null when it can, so nothing goes that was not read whole and
        /// in view: a question of several prompts sends only from the person's answers, every page of them
        /// drawn; every prompt answered and its question read to the end; every answer chosen on the page
        /// in view; and a cut answer or a long typed one read whole beside the page. The director asks this
        /// before Send answer reaches the steering.
        /// </summary>
        public static string? WhySendWaits(FileScreen screen)
        {
            var question = screen.Question;
            if (!(question.Draft is QuestionDraft draft)) return QuestionNotReady;
            if (!question.Reviewing || question.Writing) return "Review your answers first.";
            if (draft.Problem is string problem) return problem;
            if (Enumerable.Range(0, draft.Prompts.Count).Any(prompt => !question.AnswersRead(prompt))) return ReadTheAnswer;
            if (!question.ReviewRead) return ReadYourAnswers;
            return null;
        }

        public static string? WhyNextWaits(FileQuestion question)
        {
            if (!(question.Draft is QuestionDraft draft) || question.Reviewing) return QuestionNotReady;
            if (!draft.WasShownWhole(question.Prompt)) return ReadTheQuestion;
            if (!draft.IsAnswered(question.Prompt)) return draft.Prompts[question.Prompt].Options.Count == 0 ? "Write an answer first."
                : draft.Prompts[question.Prompt].FreeText ? "Choose or write an answer first." : "Choose an answer first.";
            return question.AnswersRead(question.Prompt) ? null : ReadTheAnswer;
        }

        public static string? WhyForwardWaits(FileScreen screen, bool stopOnPage = false)
        {
            var question = screen.Question;
            if (question.Writing) return question.InputReading && question.InputPart < question.InputParts - 1
                ? question.InputPageRead ? null : "Read this part first." : question.WhyUseWaits(stopOnPage);
            if (question.Reviewing) return question.Page < question.Pages - 1 ? question.ReviewPageRead ? null : "Read this part first." : WhySendWaits(screen);
            return question.QuestionPart != null ? question.QuestionPartRead ? null : "Read this part first." : WhyNextWaits(question);
        }

        public const string BackQuestion = "back-question";
        public const string CancelInput = "cancel-answer-input";
        public const string UseInput = "use-answer-input";
        public const string EditInput = "edit-answer-input";
        public const string NextInputPart = "next-answer-input-part";
        public const string PreviousInputPart = "previous-answer-input-part";
        public const string PreviousReviewPart = "previous-answer-review-part";
        public const string ReviewAnswers = "Review answers";
        public const string WriteAnswer = "Write an answer";
        public const string EditAnswer = "Edit answer";

        /// <summary>
        /// A question's footer: Close, Stop beside it, Hold to talk beside Send answer, and Send answer as
        /// the main action, waiting in its place with its reason until it can send; while an answer sent
        /// may still take effect, Sent… in its place, taking no press.
        /// </summary>
        private static Footer QuestionFooter(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft)
        {
            var question = screen.Question;
            if (workspace.AnswerInFlight) return new Footer(CloseFile,
                farRight: new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: SentWaiting, waits: true));
            if (!WorkspaceText.Answerable(draft.Question)) return new Footer(CloseFile, rare: StopWhereUnanswerable(workspace, draft.Question));
            if (question.Writing)
            {
                var reason = question.WhyUseWaits(StopsHere(workspace, draft.Question));
                var reading = question.InputReading;
                var next = reading && question.InputPart < question.InputParts - 1
                    ? new Prompt(NextInputPart, NextPartWords(question.InputPart, question.InputParts), GlazeIcon.Next, main: true, available: question.InputPageRead, reason: question.InputPageRead ? null : "Read this part first.")
                    : new Prompt(UseInput, "Use this answer", GlazeIcon.Approve, main: true, available: reason == null, reason: reason);
                return new Footer(new Prompt(CancelInput, "Cancel", GlazeIcon.Close, PromptKind.Close),
                    rare: reading && question.InputPart > 0 ? new Prompt(PreviousInputPart, "Previous part", GlazeIcon.Back) : null,
                    secondary: !reading && screen.Speak && draft.Prompts[question.Prompt].FreeText && workspace.Actions.Contains(WorkspaceAction.Answer)
                        ? new Prompt(SpeakAnswer, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null,
                    farRight: next);
            }
            var back = question.Reviewing && question.Page > 0 ? new Prompt(PreviousReviewPart, "Previous part", GlazeIcon.Back)
                : question.Reviewing || question.Prompt > 0 ? new Prompt(BackQuestion, "Back", GlazeIcon.Back) : null;
            Prompt forward;
            if (question.Reviewing)
            {
                if (question.Page < question.Pages - 1)
                    forward = new Prompt(MoreAnswers, NextPartWords(question.Page, question.Pages), GlazeIcon.Next, main: true,
                        available: question.ReviewPageRead, reason: question.ReviewPageRead ? null : "Read this part first.");
                else
                {
                    var reason = WhySendWaits(screen);
                    forward = new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), GlazeIcon.SendAnswer, main: true,
                        available: reason == null && workspace.Actions.Contains(WorkspaceAction.Answer), reason: reason ?? (!workspace.Actions.Contains(WorkspaceAction.Answer) ? QuestionNotReady : null));
                }
            }
            else if (question.QuestionPart is int part)
                forward = new Prompt(NextPart, part + 1 < question.QuestionParts(question.Prompt) ? NextPartWords(part, question.QuestionParts(question.Prompt)) : "See answers",
                    GlazeIcon.Next, main: true, available: question.QuestionPartRead, reason: question.QuestionPartRead ? null : "Read this part first.");
            else
            {
                var reason = WhyNextWaits(question);
                var words = question.ReturningToReview || question.Prompt == draft.Prompts.Count - 1 ? ReviewAnswers : "Next question";
                forward = new Prompt(NextQuestion, words, GlazeIcon.Next, main: true, available: reason == null, reason: reason);
            }
            return new Footer(CloseFile, rare: back, farRight: forward);
        }

        /// <summary>
        /// The page itself says why Send answer waits, so its reason isn't drawn (lane V, 2026-10-02): a
        /// question of one prompt waiting only for a choice, as its answers above say what to do; a short
        /// question before its first drawing; or a chosen answer read whole beside the page, whose side
        /// panel shows what to read. A question of several prompts always says to send from Your answers.
        /// </summary>
        private static bool PageExplains(QuestionDraft draft, FileQuestion question, string problem)
        {
            if (draft.Prompts.Count > 1 || question.Reviewing) return false;
            if (!draft.IsAnswered(0)) return true;
            if (problem == ReadTheQuestion) return !question.QuestionCut(0);
            return problem == ReadTheAnswer && question.SideOption != null;
        }

        /// <summary>Why Send answer takes no press while an answer sent may still take effect.</summary>
        public const string SentWaiting = "Sent. Waiting for the agent…";

        /// <summary>
        /// The agent's question (ADR 0022, ADR 0026), one prompt at a time. A long question shows first on
        /// pages of its own, a part at a time, each ending in a row to the next and the last in a row on to
        /// its answers. Then its answers in the agent's order as rows to choose, in at most two rows each,
        /// headed by the question, whole where it is short and by its first row where it is long; the row
        /// for the person's own answer last; then a row for more answers and a row on to the next
        /// question. Choosing only drafts the answer; Send answer sends it. A question Halcyonic can't
        /// answer says why. After the last of several prompts, the person's answers.
        /// </summary>
        private static Page Question(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var question = screen.Question;
            if (question.Reviewing) return YourAnswersPage(workspace, screen, draft, source);
            if (question.Writing) return InputPage(workspace, screen, draft, source);
            var prompt = question.Prompt;
            var asked = draft.Prompts[prompt];
            // The agent's words, leaning beside Halcyonic's quote mark, as its messages are.
            var text = QuestionWords(asked);
            if (!WorkspaceText.Answerable(draft.Question))
            {
                // Nothing can be answered here, so nothing is read through to: what it asks, why it
                // can't be answered, and Stop, the way on.
                return new Page(new[]
                {
                    new PageLine(text, rows: FileQuestion.QuestionRows),
                    new PageLine(WorkspaceText.CannotAnswer(draft.Question), rows: 2, tone: LineTone.Secondary),
                    new PageLine(WorkspaceText.AgentWaits, tone: LineTone.Secondary),
                }, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
            }
            if (question.QuestionPart is int part)
            {
                var perPart = question.QuestionPartRows;
                var parts = question.QuestionParts(prompt);
                var partLines = new List<PageLine>
                {
                    new PageLine(QuestionNumber(prompt, draft.Prompts.Count), tone: LineTone.Secondary),
                    new PageLine(text, rows: perPart, fromRow: part * perPart),
                };
                return new Page(partLines, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
            }
            var lines = new List<PageLine> { new PageLine(QuestionNumber(prompt, draft.Prompts.Count), tone: LineTone.Secondary) };
            // The question heads its answers where it leaves room for one; a long one by its first row.
            if (question.HeadRows(prompt) > 0) lines.Add(new PageLine(text, rows: question.HeadRows(prompt)));
            if (!WorkspaceText.Answerable(draft.Question))
            {
                lines.Add(new PageLine(WorkspaceText.CannotAnswer(draft.Question), rows: 2, tone: LineTone.Secondary));
                lines.Add(new PageLine(WorkspaceText.AgentWaits, tone: LineTone.Secondary));
            }
            else
            {
                lines.Add(new PageLine(asked.Options.Count == 0 ? WriteAnswer : asked.Multiple ? "Choose one or more" : "Choose one", tone: LineTone.Secondary));
                foreach (var index in question.Answers) lines.Add(Answer(draft, question, prompt, index));
                // Under its answers, where some read the same and can't be chosen, why, and the way to the one meant.
                if (WorkspaceText.OffersALabelTwice(asked))
                {
                    lines.Add(new PageLine(WorkspaceText.AlikeCantBeChosen(StopsHere(workspace, draft.Question)), tone: LineTone.Secondary, rows: FileQuestion.AlikeRows));
                }
                if (asked.FreeText && (screen.KeyboardOffered || screen.Speak || draft.Typed(prompt) != null))
                {
                    var typed = draft.Typed(prompt);
                    if (typed != null) lines.Add(SelectedTypedLine(typed, Math.Max(1, Math.Min(FileQuestion.AnswerRows, question.TypedMeasured(prompt)))));
                    lines.Add(new PageLine(typed == null ? WriteAnswer : EditAnswer, icon: screen.KeyboardOffered ? GlazeIcon.Type : GlazeIcon.Change, action: TypeAnswer,
                        control: LineControl.Action));
                }
                if (question.Pages > 1) lines.Add(new PageLine(MoreAnswersWords(question.Page, question.Pages), icon: GlazeIcon.Next, action: MoreAnswers,
                    control: LineControl.Action));
            }
            var footer = QuestionFooter(workspace, screen, draft);
            SidePanel? side = null;
            if (question.SideOption is int open)
            {
                // All the words of the chosen cut answer, or of the typed one, beside the page, in parts
                // where they don't fit; the footer's Next page turns them, in Hold to talk's place.
                var perPart = question.SidePartRows;
                var parts = question.SideParts;
                side = new SidePanel(NameRuns(draft.Question, prompt),
                    lines: new[] { SideAnswerLine(open < asked.Options.Count ? asked.Options[open] : null, draft.Typed(prompt), perPart, question.SidePart * perPart) },
                    source: LineWords.Written(source), parts: parts > 1 ? (question.SidePart, parts) : ((int, int)?)null);
                if (parts > 1 && footer[PromptSlot.FarRight] is Prompt main && main.Main)
                {
                    footer = new Footer(footer[PromptSlot.Close], footer[PromptSlot.Rare], farRight: main)
                        .WithNext(Next(question.SidePart, parts));
                }
            }
            return new Page(lines, source, footer, side, ofQuestion: true);
        }

        /// <summary>
        /// A chosen answer's words beside the page, all of them: the agent's answer, leaning beside
        /// Halcyonic's quote mark, or the person's typed words, leaning as the person's own words.
        /// </summary>
        public static PageLine SideAnswerLine(QuestionOption? option, string? typed, int rows = 1, int? fromRow = null) => option != null
            ? new PageLine(AnswerRuns(option), rows: rows, fromRow: fromRow)
            : new PageLine(LineWords.TheirName(typed), rows: rows, fromRow: fromRow);

        /// <summary>An answer as its row shows it: the agent's label and description, all outside text, leaning beside Halcyonic's quote mark.</summary>
        public static LineWords AnswerRuns(QuestionOption option) => LineWords.TheirName(AnswerWords(option));

        /// <summary>An answer's row to choose, as the page draws it and the layout measures it.</summary>
        public static PageLine AnswerRow(QuestionOption option, string key, bool chosen = false, bool available = true, int rows = 1, bool multiple = false) =>
            new PageLine(AnswerRuns(option), action: Choose, key: key, choice: true, chosen: chosen, available: available, rows: rows, answerMark: multiple ? AnswerMark.Multiple : AnswerMark.Single);

        /// <summary>An answer's words as its row and its side panel show them: its label, then its description.</summary>
        public static string AnswerWords(QuestionOption option) =>
            LabelText.Name(option.Label) + (string.IsNullOrWhiteSpace(option.Description) ? "" : " · " + WorkspaceText.OneLine(option.Description!));

        /// <summary>One answer offered, as a row to choose, its description after its label, in at most two rows.</summary>
        private static PageLine Answer(QuestionDraft draft, FileQuestion question, int prompt, int index)
        {
            var option = draft.Prompts[prompt].Options[index];
            // One that reads the same as another shows, and can't be chosen.
            return AnswerRow(option, index.ToString(CultureInfo.InvariantCulture), draft.IsChosen(prompt, option.Label),
                !WorkspaceText.ReadsAlike(draft.Prompts[prompt], index), question.AnswerShows(prompt, index), draft.Prompts[prompt].Multiple);
        }

        /// <summary>A prompt's name on the person's answers: its header as the agent wrote it, else which question it is.</summary>
        private static LineWords NameRuns(QuestionView question, int prompt) => LineWords.Theirs(question.Prompts[prompt].Header) is { IsEmpty: false } header
            ? header
            : LineWords.Ours("Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// The person's answers to a question of several prompts, the only page it sends from: each
        /// prompt's answer whole, by its name, choosing one going back to that prompt; paged by a row
        /// where they don't fit, and sent only once every page has been drawn.
        /// </summary>
        private static Page YourAnswersPage(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var question = screen.Question;
            var lines = new List<PageLine> { new PageLine(YourAnswers, tone: LineTone.Secondary) };
            lines.AddRange(question.Reviewed.Select(prompt => ReviewRow(draft, prompt, question.ReviewShows(prompt), question.ReviewFirst(prompt))));
            return new Page(lines, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
        }

        public static string QuestionNumber(int prompt, int prompts) => "Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture) + " of " + prompts.ToString(CultureInfo.InvariantCulture);

        public static PageLine ReviewRow(QuestionDraft draft, int prompt, int rows = 1, int fromRow = 0) =>
            new PageLine(ReviewRuns(draft, prompt), fact: LineWords.Ours("Edit"), action: GoToQuestion, key: prompt.ToString(CultureInfo.InvariantCulture),
                opens: true, control: LineControl.Action, rows: rows, fromRow: fromRow);

        public static PageLine SelectedTypedLine(string typed, int rows = 1) =>
            new PageLine(TypedRuns(typed), fact: LineWords.Ours("Selected"), rows: rows);

        public static PageLine InputLine(FileQuestion question, bool keyboard = true, int rows = 1, int? fromRow = null) =>
            new PageLine(question.Input.Length == 0 ? LineWords.Ours(keyboard ? "Type your answer" : "Hold to talk below") : LineWords.TheirName(question.Input), icon: keyboard ? GlazeIcon.Type : GlazeIcon.Change,
                action: EditInput, control: LineControl.Input, inputFocused: question.InputFocused, rows: rows, fromRow: fromRow);

        private static Page InputPage(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var question = screen.Question;
            var lines = new List<PageLine>
            {
                new PageLine("Your answer", tone: LineTone.Secondary),
                InputLine(question, screen.KeyboardOffered, question.InputPartRows, question.InputPart * question.InputPartRows),
            };
            if (question.InputReading) lines.Add(new PageLine(EditAnswer, icon: screen.KeyboardOffered ? GlazeIcon.Type : GlazeIcon.Change, action: EditInput, control: LineControl.Action));
            return new Page(lines, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
        }

        /// <summary>The answers about to be sent, a line for each prompt, and the question the confirmation asks.</summary>
        private static Page Answers(QuestionDraft draft, WorkspaceSteering steering, WorkspacePresentation workspace, Footer before, string source)
        {
            var lines = new List<PageLine>();
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                lines.Add(new PageLine(WorkspaceText.PromptHeadingRuns(draft.Question, prompt) + LineWords.Ours(": ") + Given(draft, prompt, LineWords.None), rows: 2));
            }
            lines.Add(new PageLine(steering.Prompt(workspace)!, tone: LineTone.Secondary));
            return new Page(lines, source, Footer.Confirm(before, PromptSlot.FarRight, YesFor(WorkspaceAction.Answer), CancelConfirm));
        }
    }
}
