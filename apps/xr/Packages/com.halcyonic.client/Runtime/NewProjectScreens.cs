#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>New project's steps, left to right as its row of shapes shows them (ADR 0026).</summary>
    public enum NewProjectStep
    {
        YourIdea,
        Questions,
        Recap,
        Build,
    }

    /// <summary>
    /// New project as frames of the menu (ADR 0026), opened by New project, the main prompt of
    /// Projects, and by Add a task for a project that exists: its steps as a row of shapes, the chosen
    /// step's page, and a footer of prompts. Choosing an earlier step is the way back. The companion's
    /// turn is the Questions page (ADR 0025): its words quoted as its own, the note that it is an AI as
    /// the page's source line, and its suggestions as answers that choosing only lights, so nothing
    /// reaches it but Send answer. What to show, never where.
    /// </summary>
    /// <summary>The recap's facts, top to bottom.</summary>
    public enum RecapFact
    {
        Name,
        FirstTask,
        Folder,
        HowItRuns,
    }

    /// <summary>What the words page is for.</summary>
    public enum WordsFor
    {
        Name,
        FirstTask,
        FolderName,
    }

    /// <summary>What Change does for a first task (<see cref="NewProjectScreens.ChangeFor"/>).</summary>
    public enum TaskChange
    {
        /// <summary>The page of its answers: the companion's suggestion, the person's own words, Type my own.</summary>
        Answers,

        /// <summary>The fixed questions again, each answer chosen.</summary>
        Questions,

        /// <summary>The words page.</summary>
        Words,
    }

    /// <summary>The rows of Your idea the person can choose.</summary>
    public enum IdeaRow
    {
        None,

        /// <summary>The idea typed or heard.</summary>
        Typed,

        /// <summary>Talk it through with the companion.</summary>
        Companion,

        /// <summary>Answer the fixed questions.</summary>
        FixedQuestions,
    }

    public static class NewProjectScreens
    {
        /// <summary>Type my idea: the keyboard, the idea then kept and chosen.</summary>
        public const string TypeIdea = "type-idea";

        public const string ChooseCompanion = "choose-companion";
        public const string ChooseQuestions = "choose-questions";

        /// <summary>Make the recap from the typed idea, which is already its first task: opens the Recap.</summary>
        public const string UseIdea = "use-idea";

        public const string BeginCompanion = "talk-it-through";
        public const string BeginQuestions = "answer-questions";

        /// <summary>What choosing one of the companion's suggestions raises, with its place in the question's choices as the key.</summary>
        public const string ChooseSuggestion = "companion-suggestion";

        /// <summary>Type my answer: the keyboard, its words then written and chosen.</summary>
        public const string TypeAnswer = "companion-type";

        public const string GoOnWithout = "go-on-without";
        public const string SendAnswer = "send-answer";

        /// <summary>Asks the companion for its recap now; with a recap already proposed, only opens the Recap.</summary>
        public const string MakeRecap = "make-recap";

        /// <summary>Goes on without the companion: the recap from the person's own words.</summary>
        public const string MakeRecapFromMyWords = "make-recap-from-my-words";

        public const string TryAgain = "companion-retry";
        public const string HoldToTalk = "hold-to-talk";

        /// <summary>What choosing one of the recap's facts raises, with <see cref="FactKey"/> as the key.</summary>
        public const string ChooseFact = "recap-fact";

        public const string StartOver = "start-over";
        public const string ConfirmStartOver = "confirm-start-over";
        public const string Cancel = "cancel";
        public const string Rename = "rename";

        /// <summary>Change for a first task: the keyboard, or for a suggested one the page of its answers.</summary>
        public const string ChangeTask = "change-task";

        public const string ChooseWhere = "choose-where";
        public const string MoreOptions = "more-options";
        public const string StartBuilding = "start-building";

        /// <summary>Done: back to the recap's facts from a page that changes one.</summary>
        public const string Done = "done";

        public const string UseSuggestedTask = "use-suggested-task";
        public const string UseMyWords = "use-my-words";
        public const string TypeTask = "type-task";

        /// <summary>What choosing a folder raises, with its place in <see cref="ProjectFolder.Options"/> as the key.</summary>
        public const string ChooseFolder = "choose-folder";

        public const string ReadFolders = "read-folders";
        public const string ChooseRuntime = "choose-runtime";
        public const string ChooseModel = "choose-model";
        public const string ChangeRuntime = "change-runtime";

        /// <summary>What choosing an offered answer to a fixed question raises, with its place among the answers as the key.</summary>
        public const string ChooseFixedAnswer = "fixed-answer";

        public const string TypeFixedAnswer = "fixed-type";
        public const string SkipFixedQuestion = "fixed-skip";

        /// <summary>Next question, or on the last question Make the recap: gives the chosen answer.</summary>
        public const string NextQuestion = "next-question";

        /// <summary>The words page's row: the keyboard, with the words as they stand.</summary>
        public const string TypeWords = "type-words";

        /// <summary>The review's row to the next part; only once the last part has shown is Yes, start building offered.</summary>
        public const string NextPart = "next-part";

        public const string ConfirmStart = "confirm-start";

        // Start building, once Yes was pressed, and a start whose outcome is unknown.
        public const string TryAgainStart = "try-again";
        public const string ChangeRequest = "change";
        public const string ChooseAnotherFolder = "choose-another-folder";
        public const string UseThatFolder = "use-that-folder";
        public const string CheckFirst = "check-first";
        public const string Clear = "clear";
        public const string ConfirmClear = "confirm-clear";

        /// <summary>
        /// The characters a row of New project's content holds, about: lane V's MeasureQuote found the
        /// quote's 2 rows hold 127 characters of ordinary English, its own words included, at 18 dp in
        /// a file's 36 degrees (66 of a question in the widest letters). The view measures what it draws.
        /// </summary>
        public const int RowCharacters = 63;

        /// <summary>
        /// The companion's quote takes 2 rows (lane V, by render: with its view's line and a third row,
        /// the page passes a Quest 3S's field). Its line shares the quote with its question while both
        /// fit by this estimate; else the question shows alone, never cut: a question of the widest
        /// letters can need a third row, which the view, measuring the real words, grows to.
        /// </summary>
        public const int QuoteRows = 2;

        /// <summary>Every step, left to right.</summary>
        public static IReadOnlyList<NewProjectStep> Steps { get; } =
            new[] { NewProjectStep.YourIdea, NewProjectStep.Questions, NewProjectStep.Recap, NewProjectStep.Build };

        /// <summary>A step's words in the row of shapes.</summary>
        public static string Words(NewProjectStep step) => step switch
        {
            NewProjectStep.YourIdea => "Your idea",
            NewProjectStep.Questions => "Questions",
            NewProjectStep.Recap => "Recap",
            _ => "Build",
        };

        /// <summary>
        /// The row of steps with <paramref name="chosen"/> lit. Your idea is always reached; Questions
        /// once the companion or the fixed questions have begun; Recap once there is a first task; and
        /// Build once nothing stops it (<paramref name="startReached"/>). A step not reached
        /// stays quiet and takes no press; the one shown is always reached. Choosing a step throws away
        /// a review in progress: whatever changes on the way back is read afresh before it is sent.
        /// </summary>
        public static IReadOnlyList<FrameSection> Sections(NewProjectStep chosen, ProjectIdea idea, bool startReached)
        {
            var sections = new List<FrameSection>();
            foreach (var step in Steps)
            {
                var reached = step switch
                {
                    NewProjectStep.YourIdea => true,
                    NewProjectStep.Questions => idea.Companion != null || idea.Guided,
                    NewProjectStep.Recap => idea.HasRecap,
                    _ => startReached && idea.HasRecap,
                };
                sections.Add(new FrameSection(Key(step), Words(step), chosen: step == chosen, reached: reached || step == chosen));
            }
            return sections;
        }

        /// <summary>The key a step's shape raises with <see cref="MenuFrame.ChooseSection"/>.</summary>
        public static string Key(NewProjectStep step) => step switch
        {
            NewProjectStep.YourIdea => "your-idea",
            NewProjectStep.Questions => "questions",
            NewProjectStep.Recap => "recap",
            _ => "build",
        };

        /// <summary>The step a shape's key names, or null for a key that names none.</summary>
        public static NewProjectStep? StepOf(string? key)
        {
            foreach (var step in Steps)
            {
                if (Key(step) == key) return step;
            }
            return null;
        }

        /// <summary>
        /// The frame's subject: a project's name when adding a task to it; else the person's idea in
        /// their own words, or the project's name once there is one that is not only the companion's
        /// suggestion, which shows as the recap's, marked Suggested, until the person takes it; else
        /// Create a project.
        /// </summary>
        public static (string Subject, bool IsData) Subject(ProjectIdea idea)
        {
            if (idea.ExistingProjectId != null) return (EntryText.CreateTitle(LabelText.Plain(idea.Name)), true);
            if (!string.IsNullOrWhiteSpace(idea.OwnWords)) return (LabelText.Plain(idea.OwnWords), true);
            if (idea.Name.Length > 0 && !idea.NameSuggested) return (LabelText.Plain(idea.Name), true);
            return (EntryText.CreateProject, false);
        }

        /// <summary>
        /// Your idea: what to make, or for a project that exists what the task should do; Type my idea,
        /// which shows the idea once typed or heard; and a way to figure it out, the companion where it
        /// can run for a new project, else the fixed questions, with why the companion can't when it
        /// can't. Choosing a row lights it and sets the main action: Make the recap from the idea, Talk
        /// it through, or Answer the questions. Hold to talk says the idea; Close keeps the draft.
        /// </summary>
        /// <param name="chosen">
        /// The row the person chose. While nothing is, the typed idea is chosen, or before there is one
        /// the way to figure it out, so the main action can always be taken.
        /// </param>
        /// <param name="voice">Hold to talk is offered: development builds, connected, never in the demonstration.</param>
        /// <param name="said">A line for this page only, such as hold to talk's words, or that the idea is what the computer heard.</param>
        /// <param name="companion">
        /// Whether the companion can be asked, as the computer said when New project opened (ADR 0025);
        /// null when not read, as in the demonstration.
        /// </param>
        /// <param name="keyboard">
        /// The system keyboard can open here. Where it can't, as in the editor, a row that only types is
        /// left off, since it would do nothing; one already holding words, heard or kept, stays as a choice.
        /// </param>
        public static MenuFrame YourIdea(ProjectIdea idea, IdeaRow chosen, bool startReached, bool voice, string? said, CompanionStatus? companion = null,
            bool keyboard = true)
        {
            var existing = idea.ExistingProjectId != null;
            var typed = idea.OwnWords;
            var talk = !existing && companion is AvailableCompanion;
            // A row not on the page, or the typed idea before there is one, is never what the main action acts on.
            if ((chosen == IdeaRow.Typed && typed == null) || (chosen == IdeaRow.Companion && !talk) || (chosen == IdeaRow.FixedQuestions && talk))
            {
                chosen = IdeaRow.None;
            }
            if (chosen == IdeaRow.None) chosen = typed != null ? IdeaRow.Typed : talk ? IdeaRow.Companion : IdeaRow.FixedQuestions;
            var lines = new List<PageLine> { new PageLine(existing ? EntryText.WorkPrompt : EntryText.IdeaPrompt) };
            if (keyboard || typed != null)
            {
                lines.Add(new PageLine(typed == null ? EntryText.TypeIdea : LabelText.Plain(typed), wordsAreData: typed != null, icon: GlazeIcon.Type,
                    action: TypeIdea, choice: true, chosen: chosen == IdeaRow.Typed, rows: typed == null ? 1 : 2));
            }
            lines.Add(talk
                ? new PageLine(CompanionText.TalkItThrough, action: ChooseCompanion, choice: true, chosen: chosen == IdeaRow.Companion)
                : new PageLine(EntryText.AnswerQuestions, action: ChooseQuestions, choice: true, chosen: chosen == IdeaRow.FixedQuestions));
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            else if (!existing && companion is UnavailableCompanion unavailable)
            {
                lines.Add(new PageLine(CompanionText.Unavailable(unavailable.Reason?.Code), tone: LineTone.Secondary, rows: 2));
            }
            lines.Add(new PageLine(EntryText.NothingStartsYet, tone: LineTone.Secondary));
            var main = chosen switch
            {
                IdeaRow.Companion => new Prompt(BeginCompanion, CompanionText.TalkItThroughShort, GlazeIcon.Next, main: true),
                IdeaRow.FixedQuestions => new Prompt(BeginQuestions, EntryText.StartQuestions, GlazeIcon.Next, main: true),
                _ => new Prompt(UseIdea, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true),
            };
            var hold = voice ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), secondary: hold, farRight: main), subjectIsData: isData,
                sections: Sections(NewProjectStep.YourIdea, idea, startReached), lines: lines);
        }

        /// <summary>
        /// The companion's turn (ADR 0025). Its question: its line and question quoted as its own, its
        /// suggestions as answers, the person's own answer typed or heard, and Go on without it, the
        /// last answer. Choosing an answer only lights it and sets the main action: Send answer for a
        /// suggestion or the person's words, Make the recap from my words for Go on without it, and Make
        /// the recap with nothing chosen. While a reply is on its way, only that it is waiting, and, once
        /// the wait is long, that the computer's model may be busy; after a failure, why, and Try again.
        /// Hold to talk is the secondary prompt wherever the person may answer, quiet with its reason
        /// while a reply is on its way or the exchange is full; Close keeps the draft.
        /// </summary>
        /// <param name="startReached">Start building's step can be chosen: nothing stops it.</param>
        /// <param name="voice">Hold to talk is offered: development builds, connected, never in the demonstration.</param>
        /// <param name="said">A line for this page only, such as hold to talk's words or why an answer was refused.</param>
        /// <param name="waitedSeconds">How long the reply has been on its way.</param>
        /// <param name="recording">
        /// The demonstration plays this recorded exchange: said so in the source line, only the
        /// recorded answer can be chosen, and Make the recap only where the recording asked for it.
        /// </param>
        /// <param name="keyboard">The system keyboard can open here; where it can't, Type my answer shows only once it holds words, as a choice.</param>
        public static MenuFrame Questions(ProjectIdea idea, bool startReached, bool voice, string? said, double waitedSeconds,
            CompanionRecording? recording = null, bool keyboard = true)
        {
            var exchange = idea.Companion ?? throw new System.ArgumentException("The idea has no exchange with the companion.", nameof(idea));
            var recorded = recording != null;
            var lines = new List<PageLine>();
            var withoutIt = exchange.Chosen == CompanionAnswerRow.WithoutIt;
            Prompt main;
            // Hold to talk stands where the person answers the companion: quiet, saying why, while they can't now.
            var answering = true;
            string? quiet = null;
            if (exchange.Waiting)
            {
                // Make the recap's reason, the page's last line, says it is waiting, and once the wait is long, why it may be.
                var waiting = waitedSeconds >= CompanionText.WaitingLongSeconds ? CompanionText.Waiting + " " + CompanionText.WaitingLong : CompanionText.Waiting;
                main = new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true, available: false, reason: waiting);
                quiet = waiting;
            }
            else if (exchange.Failure != null)
            {
                lines.Add(new PageLine(CompanionText.Failure(exchange.Failure) ?? CompanionText.CouldNotAsk, tone: LineTone.Problem, rows: 3));
                main = new Prompt(TryAgain, EntryText.TryAgain, GlazeIcon.Refresh, main: true);
                answering = false;
            }
            else if (exchange.Latest is AskReply ask)
            {
                // Its view only when it thinks the idea can't be built; asking already says it found it unclear.
                var notBuildable = ask.View == CompanionView.NotBuildable;
                if (notBuildable) lines.Add(new PageLine(CompanionText.ThinksNotBuildable, tone: LineTone.Secondary, rows: 2));
                var quote = Asked(ask, withLine: !notBuildable);
                // At most a third row: the view measures the real words and grows to it rather than cut the question.
                lines.Add(new PageLine(quote, wordsAreData: true, claim: true, rows: QuoteRows + 1));
                var choices = ask.Question?.Choices ?? new List<string>();
                var recordedAnswer = recording?.RecordedAnswer(exchange);
                for (var index = 0; index < choices.Count; index++)
                {
                    lines.Add(new PageLine(LabelText.Plain(choices[index]), wordsAreData: true, action: ChooseSuggestion,
                        key: AnswerKey(exchange.Generation, choices[index]), choice: true,
                        chosen: exchange.Chosen == CompanionAnswerRow.Suggestion && exchange.ChosenSuggestion == index,
                        available: recorded ? choices[index] == recordedAnswer : exchange.CanSay, rows: 2));
                }
                if (!recorded && (keyboard || exchange.Written != null))
                {
                    var written = exchange.Written;
                    lines.Add(new PageLine(written == null ? CompanionText.TypeAnswer : LabelText.Plain(written), wordsAreData: written != null,
                        icon: GlazeIcon.Type, action: TypeAnswer, choice: true, chosen: exchange.Chosen == CompanionAnswerRow.Written,
                        available: exchange.CanSay, rows: written == null ? 1 : 2));
                }
                var send = new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), WorkspaceText.IconOf(WorkspaceAction.Answer), main: true);
                if (exchange.Answer != null) main = send;
                else if (recorded && !recording!.RecapHere(exchange))
                {
                    // The recording answers this question before its recap: its answer is the one to choose.
                    main = new Prompt(SendAnswer, send.Words, send.Icon, main: true, available: false, reason: CompanionText.ChooseOne, pageExplains: true);
                }
                else
                {
                    main = new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true, available: recorded || exchange.CanAskForRecap,
                        reason: exchange.PersonSpoke ? CompanionText.Full : CompanionText.AnswerFirst);
                }
                if (!exchange.CanSay) quiet = CompanionText.Full;
            }
            else
            {
                // The companion has proposed: its recap is made, and Make the recap only opens it.
                if (exchange.Latest is CompanionReply reply) lines.Add(new PageLine(CompanionText.Says(reply.Line), wordsAreData: true, claim: true, rows: 3));
                main = new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true);
                answering = false;
            }
            // Go on without it, the last answer, until the companion has proposed; the recording plays to its proposal.
            if (!recorded && exchange.Proposal == null)
            {
                lines.Add(new PageLine(CompanionText.GoOnWithout, icon: GlazeIcon.Next, action: GoOnWithout, choice: true, chosen: withoutIt));
            }
            if (withoutIt && exchange.Proposal == null) main = new Prompt(MakeRecapFromMyWords, CompanionText.MakeTheRecapFromMyWords, GlazeIcon.Next, main: true);
            if (exchange.WrittenHeard && exchange.Chosen == CompanionAnswerRow.Written) lines.Add(new PageLine(VoiceText.HeardAnswer, tone: LineTone.Secondary, rows: 2));
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            var talk = voice && !recorded && answering
                ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, available: quiet == null, reason: quiet, holds: true)
                : null;
            // A full exchange says so: as Hold to talk's reason where it stands, else on a line of its own.
            if (talk == null && !recorded && quiet == CompanionText.Full) lines.Add(new PageLine(CompanionText.Full, tone: LineTone.Secondary, rows: 2));
            var footer = new Footer(Close(), secondary: talk, farRight: main);
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, footer, subjectIsData: isData, sections: Sections(NewProjectStep.Questions, idea, startReached), lines: lines,
                source: recorded ? CompanionText.Recorded : CompanionText.Note);
        }

        /// <summary>
        /// The companion's question quoted as its own words, after its line while both fit in
        /// <see cref="QuoteRows"/> rows and <paramref name="withLine"/>; else the question alone, so the
        /// line drops first and the question is never cut.
        /// </summary>
        public static string Asked(AskReply ask, bool withLine = true)
        {
            var question = CompanionText.Says(ask.Question?.Text ?? "");
            if (!withLine) return question;
            var both = CompanionText.Says(LabelText.Plain(ask.Line) + " " + LabelText.Plain(ask.Question?.Text));
            return Rows(both) <= QuoteRows ? both : question;
        }

        /// <summary>The rows <paramref name="text"/> takes at the content's size, about.</summary>
        private static int Rows(string text) => (text.Length + RowCharacters - 1) / RowCharacters;

        /// <summary>
        /// An answer row's key: the question it answers, by the companion's request that brought it or
        /// the fixed question's place, and its words, so a press on a frame drawn before the next
        /// question never lights the same words there.
        /// </summary>
        public static string AnswerKey(int question, string words) => question.ToString(CultureInfo.InvariantCulture) + "|" + words;

        /// <summary>The words an answer row's key gives, when it answers <paramref name="question"/>; null for another question's.</summary>
        public static string? AnswerWords(string? key, int question)
        {
            var asked = question.ToString(CultureInfo.InvariantCulture) + "|";
            return key != null && key.StartsWith(asked, System.StringComparison.Ordinal) ? key.Substring(asked.Length) : null;
        }

        /// <summary>
        /// A folder row's key: what it chooses, by its place's path and the folder's own name, so a press
        /// on a list drawn before the folders were read again never chooses another.
        /// </summary>
        public static string FolderKey(FolderOption option) =>
            option.Kind + "|" + option.Root.Path + "|" + (option.Folder?.Name ?? "");

        /// <summary>The key a recap fact's row raises with <see cref="ChooseFact"/>.</summary>
        public static string FactKey(RecapFact fact) => fact switch
        {
            RecapFact.Name => "name",
            RecapFact.FirstTask => "first-task",
            RecapFact.Folder => "folder",
            _ => "how-it-runs",
        };

        /// <summary>The fact a row's key names, or null for a key that names none.</summary>
        public static RecapFact? FactOf(string? key)
        {
            foreach (RecapFact fact in System.Enum.GetValues(typeof(RecapFact)))
            {
                if (FactKey(fact) == key) return fact;
            }
            return null;
        }

        /// <summary>
        /// The recap: the companion's proposal, quoted as its own, while what it suggested stands, else
        /// what this page is for; then each fact as a row, its value with Suggested beside it while the
        /// companion's suggestion stands. Choosing a fact lights it, slides out its side panel with the
        /// whole of it, and puts its change beside Close: Change, Choose a folder or More options; with
        /// no fact chosen, Start over stands there, confirmed in place. Start building is the main
        /// action, and while something is missing it stays, unavailable, saying what.
        /// </summary>
        /// <param name="currentFolder">An existing project's folder as its computer bound it, or null.</param>
        /// <param name="notice">A line for the recap only, such as what the computer heard, shown first.</param>
        /// <param name="problem">Why Start building can't go ahead now (<see cref="EntryScreens.StartProblem"/>), or null.</param>
        /// <param name="chosen">The fact chosen, whose side panel shows; null for none.</param>
        /// <param name="confirmingStartOver">Start over was pressed once: Cancel stands in its place and Yes, start over in the middle.</param>
        /// <param name="buildReached">Whether Build's step can be chosen, where it differs from nothing stopping a start: a start whose outcome is unknown shows there.</param>
        /// <param name="startOverProblem">Why Start over can't be taken now, as while a build is on its way or its outcome unknown; null when it can.</param>
        /// <param name="words">
        /// Words can be given here, by the keyboard or Hold to talk. Where neither is offered, a change
        /// that only leads to a page of words (renaming, a first task in the person's own words) is left
        /// off, since that page could only show them.
        /// </param>
        public static MenuFrame Recap(ProjectIdea idea, NewWorkDraft draft, ProjectLocation? currentFolder, bool live, string? notice, string? problem,
            RecapFact? chosen = null, bool confirmingStartOver = false, bool? buildReached = null, string? startOverProblem = null, bool words = true)
        {
            var existing = idea.ExistingProjectId != null;
            var suggested = idea.NameSuggested || idea.TaskSuggested;
            var proposal = suggested ? idea.Companion?.Proposal : null;
            // An existing project's name is not changed here, so its row takes no press.
            if (existing && chosen == RecapFact.Name) chosen = null;
            var lines = new List<PageLine>();
            if (notice != null) lines.Add(new PageLine(notice, tone: LineTone.Secondary, rows: 2));
            else if (proposal != null) lines.Add(new PageLine(EntryScreens.Proposed(proposal), wordsAreData: true, claim: true, rows: 2));
            else lines.Add(new PageLine(EntryScreens.Moves(idea, currentFolder) ? EntryText.RebindWarning : EntryText.RecapLine, tone: LineTone.Secondary, rows: 2));
            var named = idea.Name.Length > 0;
            lines.Add(existing
                ? new PageLine(LabelText.Plain(idea.Name), wordsAreData: true)
                : new PageLine(named ? LabelText.Plain(idea.Name) : EntryText.NotNamedYet, wordsAreData: named,
                    fact: idea.NameSuggested ? CompanionText.SuggestedShort : null,
                    action: ChooseFact, key: FactKey(RecapFact.Name), opens: true, chosen: chosen == RecapFact.Name));
            lines.Add(new PageLine(LabelText.Plain(idea.FirstTask), wordsAreData: true, fact: idea.TaskSuggested ? CompanionText.SuggestedShort : null,
                action: ChooseFact, key: FactKey(RecapFact.FirstTask), opens: true, chosen: chosen == RecapFact.FirstTask));
            var nothingChosen = idea.Folder == null && currentFolder == null;
            lines.Add(new PageLine(EntryText.FolderFact(currentFolder, idea.Folder, draft.Runtime?.UsesProjectLocation == true), wordsAreData: !nothingChosen,
                action: ChooseFact, key: FactKey(RecapFact.Folder), opens: true, chosen: chosen == RecapFact.Folder));
            lines.Add(new PageLine(EntryText.RunsWith(draft, live), wordsAreData: RuntimeIsData(draft, live),
                action: ChooseFact, key: FactKey(RecapFact.HowItRuns), opens: true, chosen: chosen == RecapFact.HowItRuns));
            var change = chosen switch
            {
                RecapFact.Name => words ? new Prompt(Rename, EntryText.Change, GlazeIcon.Change) : null,
                RecapFact.FirstTask => words || ChangeFor(idea) != TaskChange.Words ? new Prompt(ChangeTask, EntryText.Change, GlazeIcon.Change) : null,
                RecapFact.Folder => new Prompt(ChooseWhere, EntryText.ChooseAnotherFolder, GlazeIcon.Change),
                RecapFact.HowItRuns => new Prompt(MoreOptions, EntryText.MoreOptions, GlazeIcon.Change),
                _ => new Prompt(StartOver, EntryText.StartOver, GlazeIcon.StartOver, available: startOverProblem == null, reason: startOverProblem),
            };
            var footer = new Footer(Close(), rare: change,
                farRight: new Prompt(StartBuilding, EntryText.StartBuilding, GlazeIcon.StartBuilding, main: true, available: problem == null, reason: problem));
            if (confirmingStartOver && chosen == null)
            {
                lines.Add(new PageLine(EntryText.StartOverQuestion, rows: 2));
                footer = Footer.Confirm(footer, PromptSlot.Rare,
                    new Prompt(ConfirmStartOver, EntryText.ConfirmStartOver, GlazeIcon.StartOver, PromptKind.Yes),
                    new Prompt(Cancel, EntryText.Cancel, GlazeIcon.Close, PromptKind.Cancel));
            }
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, footer, subjectIsData: isData, sections: Sections(NewProjectStep.Recap, idea, buildReached ?? problem == null), lines: lines,
                source: suggested ? CompanionText.Note : null, side: chosen is RecapFact fact ? Side(fact, idea, draft, currentFolder, live) : null);
        }

        /// <summary>
        /// Changing a first task the companion suggested: its suggestion and the person's own words,
        /// each as it was written, and Type my own. Choosing one makes it the first task, here on the
        /// headset; Done goes back to the facts. Hold to talk gives the person's own, which lands in Type
        /// my own, chosen, for them to check.
        /// </summary>
        /// <param name="said">A line for this page only, such as hold to talk's words or that the task is what the computer heard.</param>
        /// <param name="keyboard">The system keyboard can open here; where it can't, Type my own shows only once it holds the task, as a choice.</param>
        public static MenuFrame RecapTask(ProjectIdea idea, bool startReached, bool voice = false, string? said = null, bool keyboard = true)
        {
            var proposal = idea.Companion?.Proposal?.Proposal;
            var lines = new List<PageLine> { new PageLine(EntryText.FirstTask, tone: LineTone.Secondary) };
            if (proposal != null)
            {
                lines.Add(new PageLine(LabelText.Plain(proposal.FirstTask), wordsAreData: true, claim: true, fact: CompanionText.SuggestedShort,
                    action: UseSuggestedTask, choice: true, chosen: idea.TaskSuggested, rows: 3));
            }
            if (idea.OwnWords != null)
            {
                lines.Add(new PageLine(LabelText.Plain(idea.OwnWords), wordsAreData: true, action: UseMyWords, choice: true,
                    chosen: !idea.TaskSuggested && idea.FirstTask == idea.OwnWords, rows: 3));
            }
            var typed = !idea.TaskSuggested && idea.FirstTask != idea.OwnWords;
            if (keyboard || typed)
            {
                lines.Add(new PageLine(typed ? LabelText.Plain(idea.FirstTask) : EntryText.TypeMyOwn, wordsAreData: typed, icon: GlazeIcon.Type,
                    action: TypeTask, choice: true, chosen: typed, rows: typed ? 3 : 1));
            }
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            var hold = voice ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), secondary: hold, farRight: new Prompt(Done, EntryText.Done, GlazeIcon.Next, main: true)),
                subjectIsData: isData,
                sections: Sections(NewProjectStep.Recap, idea, startReached), lines: lines, source: proposal != null ? CompanionText.Note : null);
        }

        /// <summary>
        /// Where its files live: what the computer lists, place by place, a new folder, the place itself
        /// and each folder in it, each an answer; a place not on the computer now shows and takes no
        /// press. Choosing one makes it the folder, here on the headset, and Done goes back to the facts;
        /// nothing is sent. While the folders can't be read, why, and Try again.
        /// </summary>
        /// <param name="problem">Why the folders couldn't be read, as it arrived, or null while reading.</param>
        /// <param name="notice">A line for this page only, such as why a new folder's name was refused.</param>
        /// <param name="words">Words can be given here, by the keyboard or Hold to talk; where neither is offered, no new folder is offered, since it could not be named.</param>
        public static MenuFrame RecapFolder(ProjectIdea idea, bool startReached, LocationsResponse? locations, string? problem, string? notice,
            bool words = true)
        {
            var lines = new List<PageLine>();
            var main = new Prompt(Done, EntryText.Done, GlazeIcon.Next, main: true);
            var tryAgain = new Prompt(ReadFolders, EntryText.TryAgain, GlazeIcon.Refresh, main: true);
            if (notice != null) lines.Add(new PageLine(notice, tone: LineTone.Problem, rows: 2));
            if (locations == null)
            {
                lines.Add(new PageLine(problem == null ? EntryText.ReadingFolders : EntryText.FoldersUnread(problem), rows: 3));
                if (problem != null) main = tryAgain;
            }
            else if (locations.Roots.Count == 0)
            {
                lines.Add(new PageLine(EntryText.NoFolders, rows: 3));
                main = tryAgain;
            }
            else
            {
                if (notice == null)
                {
                    var cut = locations.Roots.Any(root => root.FoldersTruncated);
                    lines.Add(new PageLine(cut ? EntryText.FolderLine + " " + EntryText.FoldersCut : EntryText.FolderLine, tone: LineTone.Secondary, rows: 2));
                }
                var options = ProjectFolder.Options(locations);
                var current = idea.Folder;
                for (var index = 0; index < options.Count; index++)
                {
                    var option = options[index];
                    if (!words && option.Kind == FolderOptionKind.NewFolder) continue;
                    var chosen = current != null && current.RootPath == option.Root.Path
                        && (option.Kind == FolderOptionKind.NewFolder ? current.IsNew
                            : !current.IsNew && (option.Kind == FolderOptionKind.Root ? current.FolderName == null
                                : option.Kind == FolderOptionKind.Folder && current.FolderName == option.Folder!.Name));
                    lines.Add(new PageLine(option.Label, wordsAreData: true, fact: option.Detail, action: ChooseFolder,
                        key: FolderKey(option), choice: true, chosen: chosen, available: option.Choosable));
                }
            }
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), farRight: main), subjectIsData: isData,
                sections: Sections(NewProjectStep.Recap, idea, startReached), lines: lines);
        }

        /// <summary>
        /// How it runs: the agent apps that can start work, each an answer, or the chosen one's own
        /// models with where each runs, Change agent app beside Close. Choosing is all this does; nothing
        /// but a model on the computer is chosen for the person, which says so, and a model that runs
        /// elsewhere is chosen only by a second press, after its row says where it runs.
        /// </summary>
        public static MenuFrame RecapOptions(ProjectIdea idea, bool startReached, NewWorkDraft draft, IEnumerable<RuntimeDescriptor> runtimes,
            bool showModels, bool live)
        {
            var lines = new List<PageLine> { new PageLine(EntryText.OptionsLine, tone: LineTone.Secondary, rows: 2) };
            Prompt? rare = null;
            if (showModels && draft.Runtime?.ModelChoice == ModelChoice.Listed)
            {
                if (draft.Models.Count == 0) lines.Add(new PageLine(draft.ModelProblem ?? EntryText.NoModels, rows: 2));
                var elsewhere = draft.Elsewhere;
                for (var index = 0; index < draft.Models.Count; index++)
                {
                    if (index == elsewhere) lines.Add(new PageLine(EntryText.ElsewhereDivider(draft.Models.Skip(elsewhere)), tone: LineTone.Secondary, rows: 2));
                    var each = draft.Models[index];
                    var chosen = draft.Model?.ModelRef == each.ModelRef;
                    var pending = draft.PendingModel == each;
                    lines.Add(new PageLine(LabelText.Plain(each.DisplayName), wordsAreData: true,
                        fact: pending ? EntryText.ConfirmElsewhere(each)
                            : (chosen && draft.ModelPreselected ? EntryText.ChosenForYou + " · " : "") + EntryText.ServedShort(each.Served),
                        action: ChooseModel, key: each.ModelRef, choice: true, chosen: chosen));
                }
                rare = new Prompt(ChangeRuntime, EntryText.ChangeAgentApp, GlazeIcon.Change);
            }
            else
            {
                var choices = EntryText.RuntimeChoices(runtimes);
                if (choices.Count == 0) lines.Add(new PageLine(EntryText.NoRuntimes, rows: 3));
                foreach (var runtime in choices)
                {
                    var fact = runtime.Synthetic ? (live ? EntryText.PracticeDetail : EntryText.Practice)
                        : runtime.ModelChoice == ModelChoice.Listed ? EntryText.ListsModels : EntryText.ChoosesModel;
                    lines.Add(new PageLine(EntryText.RuntimeName(runtime, live), wordsAreData: !(runtime.Synthetic && live), fact: fact,
                        action: ChooseRuntime, key: runtime.RuntimeId, choice: true, chosen: draft.Runtime?.RuntimeId == runtime.RuntimeId));
                }
            }
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), rare: rare, farRight: new Prompt(Done, EntryText.Done, GlazeIcon.Next, main: true)),
                subjectIsData: isData, sections: Sections(NewProjectStep.Recap, idea, startReached), lines: lines);
        }

        /// <summary>A chosen fact's side panel: its name, then the whole of it; a suggested first task with the person's own words below.</summary>
        private static SidePanel Side(RecapFact fact, ProjectIdea idea, NewWorkDraft draft, ProjectLocation? currentFolder, bool live)
        {
            switch (fact)
            {
                case RecapFact.Name:
                    var named = idea.Name.Length > 0;
                    return new SidePanel(EntryText.ProjectName,
                        lines: new[] { new PageLine(named ? LabelText.Plain(idea.Name) : EntryText.NotNamedYet, wordsAreData: named, rows: 2) },
                        source: idea.NameSuggested ? CompanionText.Note : null);
                case RecapFact.FirstTask when idea.TaskSuggested && idea.OwnWords != null:
                    return new SidePanel(EntryText.FirstTask, facts: new[]
                    {
                        new SideFact(CompanionText.Suggested, LabelText.Plain(idea.FirstTask), valueIsData: true),
                        new SideFact(CompanionText.YourOwnWords, LabelText.Plain(idea.OwnWords), valueIsData: true),
                    }, source: CompanionText.Note);
                case RecapFact.FirstTask:
                    return new SidePanel(EntryText.FirstTask, lines: new[] { new PageLine(LabelText.Plain(idea.FirstTask), wordsAreData: true, rows: 8) },
                        source: idea.TaskSuggested ? CompanionText.Note : null);
                case RecapFact.Folder:
                    var folderLines = new List<PageLine>
                    {
                        new PageLine(EntryText.FolderFact(currentFolder, idea.Folder, draft.Runtime?.UsesProjectLocation == true),
                            wordsAreData: idea.Folder != null || currentFolder != null, rows: 3),
                    };
                    if (EntryScreens.Moves(idea, currentFolder)) folderLines.Add(new PageLine(EntryText.RebindWarning, tone: LineTone.Secondary, rows: 3));
                    return new SidePanel(EntryText.FolderTitle, lines: folderLines);
                default:
                    var runLines = new List<PageLine> { new PageLine(EntryText.RunsWith(draft, live), wordsAreData: RuntimeIsData(draft, live), rows: 2) };
                    if (EntryText.ModelLine(draft) is string model && model.Length > 0) runLines.Add(new PageLine(model, tone: LineTone.Secondary, rows: 3));
                    return new SidePanel(EntryText.HowItRuns, lines: runLines);
            }
        }

        /// <summary>
        /// One fixed question, forward only: the question with which of how many it is, its answers,
        /// the person's own answer typed or heard, and its skip where it can be skipped. Choosing only
        /// lights an answer; Next question gives it, and on the last question Make the recap does,
        /// composing the first task. Coming back to a question finds its answer chosen. The note that
        /// these are fixed questions, not an AI, is the source line.
        /// </summary>
        /// <param name="keyboard">The system keyboard can open here; where it can't, the typed answer's row shows only once it holds words, as a choice.</param>
        public static MenuFrame FixedQuestion(ProjectIdea idea, bool startReached, bool voice, string? said, bool keyboard = true)
        {
            if (idea.Question >= ProjectIdea.Questions.Count) throw new System.ArgumentException("Every fixed question is answered.", nameof(idea));
            var question = ProjectIdea.Questions[idea.Question];
            var asked = idea.ExistingProjectId != null ? ProjectIdea.Questions.Count - 1 : ProjectIdea.Questions.Count;
            var stands = idea.GuideAnswer;
            var lines = new List<PageLine> { new PageLine(question.Prompt, fact: EntryText.Question(idea.Question, asked), rows: 2) };
            var choices = idea.Choices;
            for (var index = 0; index < choices.Count; index++)
            {
                lines.Add(new PageLine(choices[index], action: ChooseFixedAnswer, key: AnswerKey(idea.Question, choices[index]), choice: true,
                    chosen: stands == choices[index]));
            }
            var written = idea.GuideWritten ?? (stands != null && !choices.Contains(stands) ? stands : null);
            if (keyboard || written != null)
            {
                lines.Add(new PageLine(written == null ? question.TypeLabel : LabelText.Plain(written), wordsAreData: written != null, icon: GlazeIcon.Type,
                    action: TypeFixedAnswer, choice: true, chosen: written != null && stands == written, rows: written == null ? 1 : 2));
            }
            if (question.SkipLabel != null) lines.Add(new PageLine(question.SkipLabel, action: SkipFixedQuestion, choice: true, chosen: idea.GuideSkipChosen));
            if (idea.GuideWrittenHeard && written != null && stands == written) lines.Add(new PageLine(VoiceText.HeardNote, tone: LineTone.Secondary, rows: 2));
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            var next = new Prompt(NextQuestion, idea.LastQuestion ? CompanionText.MakeTheRecap : EntryText.NextQuestion, GlazeIcon.Next, main: true,
                available: idea.CanGoOn, reason: EntryText.ChooseOrTypeFirst, pageExplains: true);
            var hold = voice ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), secondary: hold, farRight: next), subjectIsData: isData,
                sections: Sections(NewProjectStep.Questions, idea, startReached), lines: lines, source: EntryText.GuideNote);
        }

        /// <summary>
        /// What Change does for the first task: the page of its answers once the companion proposed one,
        /// the fixed questions again for a task composed from their answers and unchanged since, and
        /// otherwise the words page, so the person's own words are never composed over.
        /// </summary>
        public static TaskChange ChangeFor(ProjectIdea idea) =>
            idea.Companion?.Proposal != null ? TaskChange.Answers : idea.TaskFromAnswers ? TaskChange.Questions : TaskChange.Words;

        /// <summary>
        /// Giving words where there are no answers to choose: the project's name, a first task in the
        /// person's own words, or a new folder's name. A line names what is written; the words, as they
        /// stand, are a row that opens the keyboard; Hold to talk says them instead. Done keeps them, and
        /// while they break a rule it stays, unavailable, saying the rule. The steps are the way back
        /// without keeping them.
        /// </summary>
        /// <param name="written">The words given on this page, typed or heard, not yet kept; null for none yet.</param>
        /// <param name="heard"><paramref name="written"/> is what the computer heard, for the person to check.</param>
        /// <param name="root">For a new folder, the place it goes in.</param>
        /// <param name="keyboard">The system keyboard can open here; where it can't, the words show without a row to type them, and none shows before there are any.</param>
        public static MenuFrame Words(ProjectIdea idea, bool startReached, WordsFor what, string? written, bool heard, bool voice, string? said,
            LocationRoot? root = null, bool keyboard = true)
        {
            if (what == WordsFor.FolderName && root == null) throw new System.ArgumentException("A new folder goes in a place.", nameof(root));
            var current = what switch
            {
                WordsFor.Name => idea.Name.Length > 0 ? idea.Name : null,
                WordsFor.FirstTask => idea.FirstTask.Length > 0 ? idea.FirstTask : null,
                _ => idea.Folder is ProjectFolder folder && folder.IsNew && folder.RootPath == root!.Path ? folder.FolderName : null,
            };
            var words = written ?? current;
            var problem = what switch
            {
                WordsFor.Name => string.IsNullOrWhiteSpace(words) || words!.Trim().Length > ProjectIdea.NameLimit ? EntryText.NameRule : null,
                WordsFor.FirstTask => string.IsNullOrWhiteSpace(words) ? EntryText.DescribeTask : words!.Trim().Length > ProjectIdea.TaskLimit ? EntryText.ShortenTask : null,
                _ => words == null || ProjectFolder.New(root!, words) == null ? EntryText.NewFolderRule : null,
            };
            // Blank words show as the row's invitation; Done says what is missing.
            var shown = string.IsNullOrWhiteSpace(words) ? null : LabelText.Plain(words);
            var lines = new List<PageLine>
            {
                new PageLine(what switch
                {
                    WordsFor.Name => EntryText.ProjectName,
                    WordsFor.FirstTask => EntryText.FirstTask,
                    _ => EntryText.NewFolderIn(root!),
                }, wordsAreData: what == WordsFor.FolderName),
            };
            if (keyboard)
            {
                lines.Add(new PageLine(shown ?? (what == WordsFor.FirstTask ? EntryText.TypeMyOwn : EntryText.TypeName), wordsAreData: shown != null,
                    icon: GlazeIcon.Type, action: TypeWords, choice: true, chosen: shown != null && written != null, rows: what == WordsFor.FirstTask ? 4 : 1));
            }
            else if (shown != null) lines.Add(new PageLine(shown, wordsAreData: true, rows: what == WordsFor.FirstTask ? 4 : 1));
            if (heard && written != null) lines.Add(new PageLine(VoiceText.HeardNote, tone: LineTone.Secondary, rows: 2));
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            var hold = voice ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
            var done = new Prompt(Done, EntryText.Done, GlazeIcon.Next, main: true, available: problem == null, reason: problem);
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), secondary: hold, farRight: done), subjectIsData: isData,
                sections: Sections(NewProjectStep.Recap, idea, startReached), lines: lines);
        }

        /// <summary>
        /// Start building's confirmation: the whole request as it will be sent, a part at a time as the
        /// Unity layer measured it, never cut, with Next part as the last row of every part but the last:
        /// the view measures a part in the room left above that row, so no line of a part hides under it.
        /// Cancel stands where Start building was pressed; Yes, start building appears in the middle,
        /// where nothing stood, only once the last part has shown, and stays there unavailable, saying
        /// why, while something else stops the start.
        /// </summary>
        /// <param name="problem">Why it can't start now even on the last part, such as the computer gone, or null.</param>
        public static MenuFrame Review(ProjectIdea idea, NewWorkReview review, string? problem)
        {
            var lines = new List<PageLine> { new PageLine(EntryText.ReviewLine, tone: LineTone.Secondary) };
            foreach (var part in review.Parts)
            {
                lines.Add(new PageLine(review.Items[part.Item].Text, wordsAreData: true, rows: part.Lines, fromRow: part.FirstLine));
            }
            // Only while a later part is there to turn to: never a "Next part" past the last.
            if (review.Paginated && review.Page < review.PageCount - 1)
            {
                lines.Add(new PageLine(EntryText.NextPart(review.Page + 2, review.PageCount), icon: GlazeIcon.Next, action: NextPart));
            }
            var before = new Footer(Close(), farRight: new Prompt(StartBuilding, EntryText.StartBuilding, GlazeIcon.StartBuilding, main: true));
            var yes = review.CanConfirm
                ? new Prompt(ConfirmStart, EntryText.ConfirmStart, GlazeIcon.StartBuilding, PromptKind.Yes, available: problem == null, reason: problem)
                : null;
            var footer = Footer.Confirm(before, PromptSlot.FarRight, yes, new Prompt(Cancel, EntryText.Cancel, GlazeIcon.Close, PromptKind.Cancel));
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, footer, subjectIsData: isData, sections: Sections(NewProjectStep.Build, idea, startReached: true),
                lines: lines);
        }

        /// <summary>
        /// Start building, once Yes was pressed: each step of the build and how it went, in words; sent
        /// is not done, and a step is confirmed only by its completed record. Its footer comes from the
        /// outcome: Close alone while it runs and once it started; Try again, with Change, after a
        /// refusal, which opens the review afresh, since a send needs a review read to its end and
        /// confirmed once (<see cref="BuildSequence.Retry"/>); Use that folder or Try again, with Choose
        /// a folder, for one about a folder; and only
        /// checking the work first when the outcome is unknown.
        /// </summary>
        /// <param name="chosenFolder">Where the person chose the files live, for Use that folder after the computer says it exists.</param>
        public static MenuFrame Starting(ProjectIdea idea, BuildSequence sequence, ProjectFolder? chosenFolder)
        {
            var lines = new List<PageLine> { new PageLine(EntryText.SendingLine, tone: LineTone.Secondary) };
            var newProject = sequence.Steps[0].Kind == BuildStepKind.CreateProject;
            foreach (var step in sequence.Steps)
            {
                lines.Add(new PageLine(EntryText.StepName(step.Kind, newProject)));
                lines.Add(new PageLine(EntryText.StepStatus(step), tone: ToneOf(step), rows: 3));
            }
            Prompt? beside = null;
            Prompt? main = null;
            if (sequence.Started)
            {
                lines.Add(new PageLine(EntryText.Started, tone: LineTone.Good, rows: 2));
            }
            else if (sequence.CanRetry && sequence.StoppedAt is BuildStep stopped && EntryText.AboutFolder(stopped))
            {
                // The next action comes from the refusal's code. Either way it opens the review: nothing is sent again unread.
                var taken = stopped.Refusal == RejectionCode.LocationExists ? chosenFolder : null;
                beside = new Prompt(ChooseAnotherFolder, EntryText.ChooseAnotherFolder, GlazeIcon.Change);
                main = taken != null && taken.IsNew
                    ? new Prompt(UseThatFolder, EntryText.UseThatFolder, GlazeIcon.Next, main: true)
                    : new Prompt(TryAgainStart, EntryText.TryAgain, GlazeIcon.Refresh, main: true);
            }
            else if (sequence.CanRetry)
            {
                beside = new Prompt(ChangeRequest, EntryText.Change, GlazeIcon.Change);
                main = new Prompt(TryAgainStart, EntryText.TryAgain, GlazeIcon.Refresh, main: true);
            }
            else if (sequence.Stopped || sequence.Steps.Any(step => step.Status == BuildStepStatus.Unknown))
            {
                main = new Prompt(CheckFirst, EntryText.CheckFirst, GlazeIcon.Next, main: true);
            }
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, new Footer(Close(), secondary: beside, farRight: main), subjectIsData: isData,
                sections: Sections(NewProjectStep.Build, idea, startReached: true), lines: lines);
        }

        /// <summary>
        /// A start that may have run: its reference, the computer's record of it when one arrives, and
        /// how to clear it, by two separate presses: Clear, then Yes, clear, which stands in the middle
        /// where nothing stood. Clearing starts a blank draft, never a retry. Nothing is offered unless
        /// connected.
        /// </summary>
        public static MenuFrame Unresolved(ProjectIdea idea, string commandId, CommandView? record, bool armed, bool live)
        {
            var lines = new List<PageLine>
            {
                new PageLine(EntryText.PreviousRequestTitle),
                new PageLine(EntryText.PreviousRequestLine, rows: 3),
                new PageLine(EntryText.Recorded(record?.Status), rows: 2),
                new PageLine(armed ? EntryText.ClearOnlyAfterChecking : EntryText.ClearOnceChecked, rows: 2),
                new PageLine(EntryText.Reference(commandId), tone: LineTone.Secondary),
            };
            var footer = new Footer(Close(), farRight: live ? new Prompt(Clear, EntryText.Clear, GlazeIcon.Next, main: true) : null);
            if (live && armed)
            {
                footer = Footer.Confirm(footer, PromptSlot.FarRight,
                    new Prompt(ConfirmClear, EntryText.ConfirmClear, GlazeIcon.Next, PromptKind.Yes),
                    new Prompt(Cancel, EntryText.Cancel, GlazeIcon.Close, PromptKind.Cancel));
            }
            var (subject, isData) = Subject(idea);
            return new MenuFrame(subject, footer, subjectIsData: isData, sections: Sections(NewProjectStep.Build, idea, startReached: true),
                lines: lines);
        }

        /// <summary>A build step's colour: confirmed is good, refused or failed a problem, and waiting or unknown secondary, its words saying which.</summary>
        private static LineTone ToneOf(BuildStep step) => step.Status switch
        {
            BuildStepStatus.Confirmed => LineTone.Good,
            BuildStepStatus.Refused or BuildStepStatus.NotSent => LineTone.Problem,
            BuildStepStatus.Failed => step.EffectUnknown ? LineTone.Secondary : LineTone.Problem,
            _ => LineTone.Secondary,
        };

        /// <summary>A runtime's own name, where it is shown, comes from outside; the practice agent's words in a live session are ours.</summary>
        private static bool RuntimeIsData(NewWorkDraft draft, bool live) =>
            draft.Runtime is RuntimeDescriptor runtime && (!runtime.Synthetic || !live) && (runtime.Synthetic || runtime.ModelChoice != ModelChoice.Listed);

        private static Prompt Close() => new Prompt(Footer.Close, EntryText.Close, GlazeIcon.Close, PromptKind.Close);
    }
}
