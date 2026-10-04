#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A task's file as one column of the menu's plane (ADR 0026, <see cref="IMenuColumn"/>): its
    /// <see cref="FileScreen"/> and <see cref="WorkspaceSteering"/>, its frame built by
    /// <see cref="FileScreens"/>, and what Changes and Checks read. Every press is judged by the
    /// steering's rules and the page's own, and only they send, through <see cref="IMenuHost.Submit"/>.
    /// What counts as read counts only as the director reports drawing the very frame this column built
    /// (<see cref="Drawn"/>). The page packs by height against what a page holds on its stage, read once
    /// when the file opens, the lower of alone and beside the menu, so it never packs again while it shows.
    /// </summary>
    public sealed class FileColumn : IMenuColumn
    {
        /// <summary>How often the file reads its work again by itself, in seconds of the host's clock.</summary>
        public const double RefreshSeconds = 0.5;

        /// <summary>How long a notice stays on the page, in seconds of the host's clock.</summary>
        public const double NoticeSeconds = 8;

        private readonly IMenuHost host;
        private readonly Func<WorkspacePresentation?> present;
        private readonly Func<string, IReadOnlyList<PresetInstruction>?> recorded;
        private readonly Func<string> historyNote;
        private readonly Action<WorkspaceAct>? acted;
        private readonly IntelligenceFeed<UnderstandingResponse> understanding;
        private readonly IntelligenceFeed<EvaluationResponse> evaluation;
        private readonly Func<IIntelligenceReader?> reader;
        private PageBudget? budget;
        private QuestionDraft? draft;
        private QuestionDraft? measuredFor;
        private TextSize measuredAt;
        private IReadOnlyList<PromptMeasure>? measures;
        private bool presets;
        private IReadOnlyList<PresetInstruction>? recordedNow;
        private string? notice;
        private double noticeUntil;
        private double nextRefresh;
        private bool answering;
        private int answerPrompt;

        /// <summary>The question Hold to talk was held for, which what is heard may type into only while it still shows.</summary>
        private QuestionDraft? answerDraft;

        /// <param name="present">The work as the director presents it now, its own commands in flight included.</param>
        /// <param name="commands">Makes the commands the steering sends.</param>
        /// <param name="reader">Where Changes and Checks read: the control plane, or the demonstration while it plays.</param>
        /// <param name="recorded">The instructions the demonstration recorded for an execution, offered in place of the keyboard.</param>
        /// <param name="historyNote">How reading the work's earlier activity goes, said after its activity.</param>
        /// <param name="acted">What the person did, for the stage's sound.</param>
        public FileColumn(IMenuHost host, Func<WorkspacePresentation?> present, CommandFactory commands, Func<IIntelligenceReader?> reader,
            Func<string, IReadOnlyList<PresetInstruction>?> recorded, Func<string> historyNote, Action<WorkspaceAct>? acted = null)
        {
            this.host = host;
            this.present = present;
            this.reader = reader;
            this.recorded = recorded;
            this.historyNote = historyNote;
            this.acted = acted;
            Steering = new WorkspaceSteering(commands, () => host.Clock);
            understanding = new IntelligenceFeed<UnderstandingResponse>((id, cancel) => Reader().ReadUnderstandingAsync(id, cancel));
            evaluation = new IntelligenceFeed<EvaluationResponse>((id, cancel) => Reader().ReadEvaluationAsync(id, cancel));
            Screen.Zone = host.Zone;
            Screen.Speak = host.VoiceOffered;
            if (present() is WorkspacePresentation opening) Screen.Section = FileScreens.Opening(opening);
            Rebuild();
        }

        /// <summary>The section showing and where the person is in it.</summary>
        public FileScreen Screen { get; } = new FileScreen();

        public WorkspaceSteering Steering { get; }

        /// <summary>The work as last presented; presses are judged against it.</summary>
        public WorkspacePresentation? Now { get; private set; }

        public MenuFrame? Frame { get; private set; }

        public event Action? Changed;

        public event Action? Closed;

        private IIntelligenceReader Reader() => reader() ?? throw new ControlPlaneRequestException("There is no control plane or demonstration to ask.");

        public void Act(string id, string? key)
        {
            if (Now == null) return;
            // A press is judged against the work as it is now, not as the last rebuild read it up to
            // RefreshSeconds ago, so the steering's checks see a request or question that changed since.
            if (present() is WorkspacePresentation fresh) Now = fresh;
            else
            {
                Closed?.Invoke();
                return;
            }
            var question = Screen.Question;
            var clock = host.Clock;
            switch (id)
            {
                case Footer.Close:
                    acted?.Invoke(WorkspaceAct.Collapse);
                    Closed?.Invoke();
                    return;
                case MenuFrame.ChooseSection when FileScreens.SectionOf(key) is FileSection section:
                    Screen.Section = section;
                    break;
                case FileScreens.Open:
                    Screen.Chosen = Screen.Chosen == key ? null : key;
                    break;
                case SidePanel.Close:
                    // Closes whichever side panel shows, a chosen answer's too, so the page comes back.
                    Screen.Chosen = null;
                    question.CloseSide();
                    break;
                case FileScreens.Refresh:
                    var execution = Now.Execution;
                    understanding.Refresh(execution?.UpdatedAt, clock);
                    evaluation.Refresh(execution?.UpdatedAt, clock);
                    break;
                case Footer.NextPage:
                    if (Screen.Section == FileSection.Waiting && question.SideOption != null) question.NextSidePart(clock);
                    else Screen.NextPage();
                    break;
                case FileScreens.NextPart when key == FileScreens.RequestKey:
                    Screen.NextRequestPart(Steering, clock);
                    break;
                case FileScreens.NextPart when key == FileScreens.QuestionKey:
                    question.NextPart(clock);
                    break;
                case FileScreens.MoreAnswers:
                    question.MoreAnswers(clock);
                    break;
                case FileScreens.NextQuestion:
                    question.NextQuestion(clock);
                    break;
                case FileScreens.GoToQuestion when Index(key) is int prompt:
                    question.GoTo(prompt);
                    break;
                case FileScreens.Choose when Index(key) is int option:
                    // Only an answer on the page in view is taken; nothing is sent.
                    question.Choose(option);
                    break;
                case FileScreens.TypeAnswer when draft != null && question.ReopenTyped():
                    // Words cut and not yet read to their end: their side panel again, at the part to read next.
                    break;
                case FileScreens.TypeAnswer when draft != null && !question.Reviewing && !host.KeyboardOffered:
                    // A row holding words heard stays their choice; no keyboard is asked to open.
                    break;
                case FileScreens.TypeAnswer when draft != null && !question.Reviewing:
                    var typing = question.Prompt;
                    var typingFor = draft;
                    host.OpenKeyboard(draft.Typed(typing) ?? "", "Your answer", text =>
                    {
                        if (typingFor != draft) return;
                        if (typingFor.Type(typing, text) is string problem) Notify(problem);
                        Rebuild();
                    });
                    return;
                case FileScreens.HoldToTalk:
                case FileScreens.SpeakAnswer:
                    // Pressed and let go before its hold started.
                    Notify(VoiceText.TooShort);
                    break;
                case FileScreens.Preset when Index(key) is int chosen:
                    // A row only chooses; Tell it sends the chosen words as shown.
                    Screen.ChoosePreset(chosen);
                    break;
                case FileScreens.TellIt when Screen.Presets != null:
                    if (Screen.PresetToSend is string words)
                    {
                        presets = false;
                        Steer(Steering.Typed(words, Now));
                        return;
                    }
                    Notify(FileScreens.ChooseAnInstruction);
                    break;
                case FileScreens.Yes:
                    Steer(Steering.Confirm(Now));
                    return;
                case FileScreens.Cancel:
                    Steering.StopTyping();
                    Steering.Cancel();
                    presets = false;
                    Screen.ForgetRequest();
                    break;
                case FileScreens.SendAnswer:
                    Steer(Steering.SendAnswer(Screen, Now));
                    return;
                default:
                    if (WorkspaceScreens.ActionOf(id) is WorkspaceAction action && action != WorkspaceAction.Answer)
                    {
                        Steer(Steering.Press(action, Now));
                        return;
                    }
                    return;
            }
            Rebuild();
        }

        private static int? Index(string? key) => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : (int?)null;

        /// <summary>
        /// Carries out what the steering decided: sends through the host, says why nothing was sent, or
        /// asks for the words of an instruction. Nothing else sends.
        /// </summary>
        private void Steer(SteeringOutcome outcome)
        {
            switch (outcome.Step)
            {
                case SteeringStep.Send:
                    Submit(outcome.Command!);
                    break;
                case SteeringStep.Explain:
                    Notify(outcome.Message!);
                    break;
                case SteeringStep.Type:
                    AskForWords();
                    break;
            }
            Rebuild();
        }

        private void Submit(CommandEnvelope command)
        {
            if (host.Submit(command) is null)
            {
                Notify("Couldn't send: " + HostText.Your + " isn't connected. Try again when it is.");
                return;
            }
            notice = null;
            // An answer sent shows how it goes with the activity: sent, then taken, refused or not confirmed.
            if (command is ExecutionAnswerQuestionCommand) Screen.Section = FileSection.Activity;
            if (WorkspaceActs.Of(command) is WorkspaceAct act) acted?.Invoke(act);
        }

        /// <summary>
        /// Tell it: the demonstration's recorded instructions where it plays, as rows to choose; else the
        /// keyboard, whose words the steering takes. Closed without words, the page's Cancel stops it.
        /// </summary>
        private void AskForWords()
        {
            var execution = Now?.Execution?.ExecutionId;
            recordedNow = execution == null ? null : recorded(execution);
            // Where no keyboard opens, the instructions offered stand in for it: the demonstration's own,
            // else the generic ones; the host invents none.
            if (recordedNow?.Count > 0 || !host.KeyboardOffered)
            {
                Steering.StopTyping();
                presets = true;
                return;
            }
            host.OpenKeyboard("", "What to tell it: " + (Now?.Character.Title ?? ""), text =>
            {
                if (Now != null) Steer(Steering.Typed(text, Now));
            });
        }

        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
            // Only the frame this column stands by counts: what it shows is this state's.
            if (drawn != Frame || Now == null) return;
            var clock = host.Clock;
            if (sidePanel)
            {
                if (Steering.Armed == null && Screen.Section == FileSection.Waiting && Screen.Question.SideOption != null) Screen.Question.SideDrawn(clock);
                return;
            }
            if (Steering.Request(Now) is string request)
            {
                var from = Screen.RequestPart * Screen.RequestPartRows;
                if (Screen.Measured(Steering, request) && drawn.Lines.Any(line => line.Words == request && line.FromRow == from))
                {
                    var could = Steering.CanConfirm;
                    Screen.RequestDrawn(Screen.RequestPart, Steering, clock);
                    // Built again only when Yes may show now. Building on every draw would hand the
                    // director a new frame each time, so no press would ever stand on the one drawn.
                    if (Steering.CanConfirm != could) Rebuild();
                }
                return;
            }
            // Only a page of the question counts toward it, never another Waiting shows meanwhile.
            if (Steering.Armed == null && Screen.Section == FileSection.Waiting && Screen.Question.Draft is QuestionDraft asked
                && FileScreens.ShowsQuestion(drawn, asked, Screen.Question.Prompt))
            {
                var whole = Screen.Question.Draft.Prompts.Select((_, prompt) => Screen.Question.Draft.WasShownWhole(prompt)).ToList();
                Screen.Question.Drawn(clock);
                if (!whole.SequenceEqual(Screen.Question.Draft.Prompts.Select((_, prompt) => Screen.Question.Draft.WasShownWhole(prompt)))) Rebuild();
            }
        }

        public void HoldStarted(string id)
        {
            // An answer held for, kept with the question and prompt showing; on the page of the person's
            // answers there is no one question to answer, so what is heard there types into none.
            answering = id == FileScreens.SpeakAnswer;
            if (!answering) return;
            answerPrompt = Screen.Question.Prompt;
            answerDraft = draft;
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
            if (answering)
            {
                // What was heard becomes the typed answer, sent only by Send answer, and only for the very
                // question and prompt it was spoken for, still showing; else the words are dropped, as the keyboard's are.
                if (draft == null || draft != answerDraft || Screen.Question.Reviewing || Screen.Question.Prompt != answerPrompt)
                {
                    Notify(VoiceText.QuestionChangedWhileSpeaking);
                }
                else Notify(draft.Type(answerPrompt, text) ?? VoiceText.HeardAnswer);
                Rebuild();
                return;
            }
            if (Now != null) Steer(Steering.Spoken(text, Now));
        }

        public void Said(string words)
        {
            Notify(words);
            Rebuild();
        }

        public void Tick()
        {
            var execution = Now?.Execution;
            var clock = host.Clock;
            if (Screen.Section == FileSection.Changes || Screen.Section == FileSection.Checks)
            {
                understanding.Show(execution?.ExecutionId, execution?.UpdatedAt, clock, TimeSpan.FromSeconds(2));
            }
            if (Screen.Section == FileSection.Checks) evaluation.Show(execution?.ExecutionId, execution?.UpdatedAt, clock);
            var read = understanding.Poll() | evaluation.Poll();
            if (read || host.Now >= nextRefresh) Rebuild();
        }

        public void FocusLeft()
        {
            var outcome = Steering.FocusLeft();
            if (outcome.Step == SteeringStep.Explain) Notify(outcome.Message!);
            Rebuild();
        }

        private void Notify(string words)
        {
            notice = words;
            noticeUntil = host.Now + NoticeSeconds;
        }

        /// <summary>
        /// Builds the frame from the work as it stands: the question measured as the view wraps it, the
        /// request's parts, the answers read, then the page; the director draws it once told.
        /// </summary>
        private void Rebuild()
        {
            nextRefresh = host.Now + RefreshSeconds;
            var presentation = present();
            if (presentation == null)
            {
                // The work is gone from the state.
                Closed?.Invoke();
                return;
            }
            Now = presentation;
            var asked = presentation.QuestionToAnswer;
            var execution = presentation.Execution?.ExecutionId;
            if (asked == null || execution == null) draft = null;
            else if (draft == null || !draft.Answers(execution, asked)) draft = new QuestionDraft(execution, asked);
            if (Steering.Refresh(presentation) is string lapse) Notify(lapse);
            if (notice != null && host.Now > noticeUntil) notice = null;
            if (Screen.Section == FileSection.Waiting && !WorkspaceText.SomethingWaits(presentation) && Steering.Armed == null) Screen.Section = FileSection.Activity;
            Screen.Notice = notice;
            Screen.KeyboardOffered = host.KeyboardOffered;
            Screen.Presets = presets ? (recordedNow?.Count > 0 ? recordedNow : WorkspaceText.PresetInstructions) : null;
            Screen.ActivityNote = historyNote();

            budget ??= Budget(presentation);
            if (draft != null) ReadQuestion(draft, budget);
            if (Steering.Request(presentation) is string request)
            {
                Screen.ReadRequest(request, host.RowsOf(new PageLine(request, wordsAreData: true), Glaze.Menu.FileColumnDegrees),
                    RequestPartRows(Steering.Prompts(), budget), Steering);
            }

            var room = new AnswerRoom(host.PageRows(sourceLine: false), line =>
                host.RowsOf(new PageLine(line.Words, wordsAreData: true, chip: line.Chip), Glaze.Menu.FileColumnDegrees));
            var clock = host.Clock;
            if (Screen.Section == FileSection.Changes)
            {
                Screen.WhatChanged = Understand(UnderstandPrompt.WhatChanged, room, clock);
                Screen.WhyChanged = Understand(UnderstandPrompt.WhyChanged, room, clock);
                Screen.HowBuilt = Understand(UnderstandPrompt.HowBuilt, room, clock);
            }
            if (Screen.Section == FileSection.Checks)
            {
                Screen.Checked = understanding.Last == null && understanding.Error == null ? null : new FileAnswer(
                    CheckedPresenter.Present(understanding, evaluation, clock, host.Zone, room, AnswerDepth.Brief),
                    CheckedPresenter.Present(understanding, evaluation, clock, host.Zone, AnswerRoom.Unlimited, AnswerDepth.Full));
            }
            Frame = FileScreens.Screen(presentation, Steering, Screen, room);
            Changed?.Invoke();
        }

        /// <summary>
        /// The page's room on this stage, read once when the file opens, so it packs once: the file's
        /// alone (ADR 0026), the menu stepping aside where the two together would not fit.
        /// </summary>
        private PageBudget Budget(WorkspacePresentation presentation)
        {
            var rows = host.TitleRows(presentation.Character.Title, Glaze.Menu.FileColumnDegrees);
            return new HeightBudget(host.PageHeight(rows, besideMenu: false));
        }

        private FileAnswer? Understand(UnderstandPrompt prompt, AnswerRoom room, DateTimeOffset clock) =>
            understanding.Last == null && understanding.Error == null ? null : new FileAnswer(
                UnderstandingPresenter.Present(prompt, understanding, clock, host.Zone, room, AnswerDepth.Brief),
                UnderstandingPresenter.Present(prompt, understanding, clock, host.Zone, AnswerRoom.Unlimited, AnswerDepth.Full));

        /// <summary>
        /// The agent's question measured as the view wraps it at the file's and the side panel's widths:
        /// every prompt and every answer, and the typed answers and the person's answers as they read now.
        /// </summary>
        private void ReadQuestion(QuestionDraft asked, PageBudget page)
        {
            const float File = Glaze.Menu.FileColumnDegrees;
            const float Side = Glaze.Menu.SideColumnDegrees;
            if (measuredFor != asked || measuredAt != host.TextSize || measures == null)
            {
                measures = asked.Prompts.Select(prompt =>
                {
                    var answers = prompt.Options.Select(FileScreens.AnswerWords).ToList();
                    return new PromptMeasure(
                        host.RowsOf(new PageLine("“" + WorkspaceText.OneLine(prompt.Text) + "”", wordsAreData: true), File),
                        answers.Select(words => host.RowsOf(new PageLine(words, wordsAreData: true, action: FileScreens.Choose, key: "0", choice: true), File)).ToList(),
                        answers.Select(words => host.RowsOf(new PageLine(words, wordsAreData: true), Side)).ToList());
                }).ToList();
                measuredFor = asked;
                measuredAt = host.TextSize;
            }
            Screen.ReadQuestion(asked, measures, page, page);
            for (var prompt = 0; prompt < asked.Prompts.Count; prompt++)
            {
                if (!(asked.Typed(prompt) is string typed)) continue;
                Screen.Question.MeasureTyped(prompt,
                    host.RowsOf(new PageLine(FileScreens.TypedWords(typed), wordsAreData: true, action: FileScreens.TypeAnswer, key: "0", choice: true, chosen: true), File),
                    host.RowsOf(new PageLine("“" + WorkspaceText.OneLine(typed) + "”", wordsAreData: true), Side));
            }
            if (asked.Prompts.Count > 1)
            {
                Screen.Question.MeasureReview(Enumerable.Range(0, asked.Prompts.Count)
                    .Select(prompt => host.RowsOf(new PageLine(FileScreens.ReviewWords(asked, prompt), wordsAreData: true, action: FileScreens.GoToQuestion, key: "0"), File))
                    .ToList());
            }
        }

        /// <summary>
        /// The rows of a request a part shows: what is left of the page beside the part's row and the
        /// tallest question the confirmation may ask, so its parts stay put when that question changes.
        /// </summary>
        public int RequestPartRows(IEnumerable<string> asking, PageBudget page) =>
            RequestPartRows(asking.Select(words => host.RowsOf(words, Glaze.Menu.FileColumnDegrees)).DefaultIfEmpty(1).Max(), page);

        /// <summary>The rows of a request a part shows, beside the part's row and a question of <paramref name="askingRows"/> rows, at most two.</summary>
        public static int RequestPartRows(int askingRows, PageBudget page)
        {
            var asked = Math.Min(2, Math.Max(1, askingRows));
            return page.WordsIn(page.Room - page.LineGap - page.Target() - page.LineGap - page.Words(asked));
        }
    }
}
