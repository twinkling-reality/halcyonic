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
            var lines = new[] { new PageLine("“" + WorkspaceText.OneLine(pending.Prompts[0].Text) + "”", wordsAreData: true, claim: true, rows: FileQuestion.QuestionRows) };
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
            var lines = new List<PageLine> { new PageLine(request, wordsAreData: true, rows: perPart, fromRow: part * perPart) };
            if (parts > 1) lines.Add(new PageLine(NextPartWords(part, parts), action: NextPart, key: RequestKey));
            lines.Add(new PageLine(Asks(steering, workspace, parts), tone: LineTone.Secondary, rows: 2));
            // Approve's Yes, and an instruction's, only once all of it has shown: until the layout
            // measured it and the person reached its last part, there is none to press. Deny's shows at
            // once, as denying runs nothing and a person who sees part 1 of something dangerous must be
            // able to refuse it then.
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
        public static string TypedWords(string? typed) => typed == null ? TypeMyAnswer : "Your answer: “" + WorkspaceText.OneLine(typed) + "”";

        /// <summary>
        /// A prompt's answer as the page of the person's answers shows it, whole: its name, then each label
        /// chosen and the words typed. The layout measures exactly these words (<see cref="FileQuestion.MeasureReview"/>).
        /// </summary>
        public static string ReviewWords(QuestionDraft draft, int prompt)
        {
            var asked = draft.Prompts[prompt];
            var given = asked.Options.Where(option => draft.IsChosen(prompt, option.Label)).Select(option => WorkspaceText.OneLine(option.Label)).ToList();
            if (draft.Typed(prompt) is string typed) given.Add("“" + WorkspaceText.OneLine(typed) + "”");
            return Name(draft.Question, prompt) + ": " + (given.Count == 0 ? "Not answered" : string.Join(", ", given));
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
            if (!(question.Draft is QuestionDraft draft)) return null;
            if (draft.Prompts.Count > 1 && !question.Reviewing) return SendFromYourAnswers;
            if (draft.Problem is string problem)
            {
                var answered = Enumerable.Range(0, draft.Prompts.Count).All(draft.IsAnswered);
                return answered && WorkspaceText.Answerable(draft.Question) ? ReadTheQuestion : problem;
            }
            if (!question.ChosenInView) return ReadTheAnswer;
            if (Enumerable.Range(0, draft.Prompts.Count).Any(prompt => !question.AnswersRead(prompt))) return ReadTheAnswer;
            if (question.Reviewing && !question.ReviewRead) return ReadYourAnswers;
            return null;
        }

        /// <summary>
        /// A question's footer: Close, Stop beside it, Hold to talk beside Send answer, and Send answer as
        /// the main action, waiting in its place with its reason until it can send; while an answer sent
        /// may still take effect, Sent… in its place, taking no press.
        /// </summary>
        private static Footer QuestionFooter(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft)
        {
            var actions = workspace.Actions;
            Prompt? send = null;
            if (actions.Contains(WorkspaceAction.Answer))
            {
                var problem = WhySendWaits(screen);
                send = new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), WorkspaceText.IconOf(WorkspaceAction.Answer), main: true,
                    available: problem == null, reason: problem, pageExplains: problem != null && PageExplains(draft, screen.Question, problem));
            }
            else if (workspace.AnswerInFlight)
            {
                send = new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: SentWaiting, waits: true);
            }
            return new Footer(
                CloseFile,
                rare: StopWhereUnanswerable(workspace, draft.Question),
                // On the page of the person's answers there is no one question to answer by voice, nor under
                // a question that can't be answered here, as one asking for a secret.
                secondary: screen.Speak && WorkspaceText.Answerable(draft.Question) && actions.Contains(WorkspaceAction.Answer) && !screen.Question.Reviewing
                    ? new Prompt(SpeakAnswer, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true)
                    : null,
                farRight: send);
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
            var prompt = question.Prompt;
            var asked = draft.Prompts[prompt];
            // The agent's words: quoted and leaning, as its messages are.
            var text = "“" + WorkspaceText.OneLine(asked.Text) + "”";
            if (!WorkspaceText.Answerable(draft.Question))
            {
                // Nothing can be answered here, so nothing is read through to: what it asks, why it
                // can't be answered, and Stop, the way on.
                return new Page(new[]
                {
                    new PageLine(text, wordsAreData: true, claim: true, rows: FileQuestion.QuestionRows),
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
                    new PageLine(text, wordsAreData: true, claim: true, rows: perPart, fromRow: part * perPart),
                    new PageLine(QuestionPartWords(part, parts, question.Pages), action: NextPart, key: QuestionKey),
                };
                return new Page(partLines, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
            }
            var lines = new List<PageLine>();
            // The question heads its answers where it leaves room for one; a long one by its first row.
            if (question.HeadRows(prompt) > 0) lines.Add(new PageLine(text, wordsAreData: true, claim: true, rows: question.HeadRows(prompt)));
            if (!WorkspaceText.Answerable(draft.Question))
            {
                lines.Add(new PageLine(WorkspaceText.CannotAnswer(draft.Question), rows: 2, tone: LineTone.Secondary));
                lines.Add(new PageLine(WorkspaceText.AgentWaits, tone: LineTone.Secondary));
            }
            else
            {
                foreach (var index in question.Answers) lines.Add(Answer(draft, question, prompt, index));
                // Under its answers, where some read the same and can't be chosen, why, and the way to the one meant.
                if (WorkspaceText.OffersALabelTwice(asked))
                {
                    lines.Add(new PageLine(WorkspaceText.AlikeCantBeChosen(StopsHere(workspace, draft.Question)), tone: LineTone.Secondary, rows: FileQuestion.AlikeRows));
                }
                var paging = question.Pages > 1 || draft.Prompts.Count > 1;
                // Where no keyboard opens, a row that would only open it is left off; one holding words,
                // as heard through Hold to talk, stays as their choice.
                if (asked.FreeText && (screen.KeyboardOffered || draft.Typed(prompt) != null))
                {
                    // Beside the paging row after it, one row for the two (lane V's rule 4).
                    var typed = draft.Typed(prompt);
                    lines.Add(new PageLine(TypedWords(typed), wordsAreData: typed != null, action: TypeAnswer,
                        key: asked.Options.Count.ToString(CultureInfo.InvariantCulture), choice: true, chosen: typed != null,
                        rows: typed == null ? 1 : Math.Max(1, Math.Min(FileQuestion.AnswerRows, question.TypedMeasured(prompt))), besideNext: paging));
                }
                if (question.Pages > 1) lines.Add(new PageLine(MoreAnswersWords(question.Page, question.Pages), action: MoreAnswers));
            }
            if (draft.Prompts.Count > 1) lines.Add(new PageLine(NextQuestionWords(prompt, draft.Prompts.Count), action: NextQuestion));
            var footer = QuestionFooter(workspace, screen, draft);
            SidePanel? side = null;
            if (question.SideOption is int open)
            {
                // All the words of the chosen cut answer, or of the typed one, beside the page, in parts
                // where they don't fit; the footer's Next page turns them, in Hold to talk's place.
                var words = open < asked.Options.Count ? AnswerWords(asked.Options[open]) : "“" + WorkspaceText.OneLine(draft.Typed(prompt) ?? "") + "”";
                var perPart = question.SidePartRows;
                var parts = question.SideParts;
                side = new SidePanel(Name(draft.Question, prompt), subjectIsData: true,
                    lines: new[] { new PageLine(words, wordsAreData: true, rows: perPart, fromRow: question.SidePart * perPart) },
                    source: source, parts: parts > 1 ? (question.SidePart, parts) : ((int, int)?)null);
                if (parts > 1 && footer[PromptSlot.FarRight] is Prompt main && main.Main)
                {
                    footer = new Footer(footer[PromptSlot.Close], footer[PromptSlot.Rare], farRight: main)
                        .WithNext(Next(question.SidePart, parts));
                }
            }
            return new Page(lines, source, footer, side, ofQuestion: true);
        }

        /// <summary>An answer's words as its row and its side panel show them: its label, then its description.</summary>
        public static string AnswerWords(QuestionOption option) =>
            WorkspaceText.OneLine(option.Label) + (string.IsNullOrWhiteSpace(option.Description) ? "" : " · " + WorkspaceText.OneLine(option.Description!));

        /// <summary>One answer offered, as a row to choose, its description after its label, in at most two rows.</summary>
        private static PageLine Answer(QuestionDraft draft, FileQuestion question, int prompt, int index)
        {
            var option = draft.Prompts[prompt].Options[index];
            // One that reads the same as another shows, and can't be chosen.
            return new PageLine(AnswerWords(option), wordsAreData: true, action: Choose,
                key: index.ToString(CultureInfo.InvariantCulture), choice: true, chosen: draft.IsChosen(prompt, option.Label),
                available: !WorkspaceText.ReadsAlike(draft.Prompts[prompt], index), rows: question.AnswerShows(prompt, index));
        }

        /// <summary>A prompt's name on the person's answers: its header as the agent wrote it, else which question it is.</summary>
        private static string Name(QuestionView question, int prompt) => string.IsNullOrWhiteSpace(question.Prompts[prompt].Header)
            ? "Question " + (prompt + 1).ToString(CultureInfo.InvariantCulture)
            : WorkspaceText.OneLine(question.Prompts[prompt].Header!);

        /// <summary>
        /// The person's answers to a question of several prompts, the only page it sends from: each
        /// prompt's answer whole, by its name, choosing one going back to that prompt; paged by a row
        /// where they don't fit, and sent only once every page has been drawn.
        /// </summary>
        private static Page YourAnswersPage(WorkspacePresentation workspace, FileScreen screen, QuestionDraft draft, string source)
        {
            var question = screen.Question;
            var lines = question.Reviewed.Select(prompt => new PageLine(ReviewWords(draft, prompt), wordsAreData: true,
                action: GoToQuestion, key: prompt.ToString(CultureInfo.InvariantCulture), rows: question.ReviewShows(prompt), fromRow: 0)).ToList();
            if (question.Pages > 1) lines.Add(new PageLine(YourAnswersWords(question.Page, question.Pages), action: MoreAnswers));
            return new Page(lines, source, QuestionFooter(workspace, screen, draft), ofQuestion: true);
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
