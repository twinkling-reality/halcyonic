#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>New project's steps, left to right as its row of shapes shows them (ADR 0026).</summary>
    public enum NewProjectStep
    {
        YourIdea,
        Questions,
        Recap,
        StartBuilding,
    }

    /// <summary>
    /// New project as frames of the menu (ADR 0026), opened by New project, the main prompt of
    /// Projects, and by Add a task for a project that exists: its steps as a row of shapes, the chosen
    /// step's page, and a footer of prompts. Choosing an earlier step is the way back. The companion's
    /// turn is the Questions page (ADR 0025): its words quoted as its own, the note that it is an AI as
    /// the page's source line, and its suggestions as answers that choosing only lights, so nothing
    /// reaches it but Send answer. What to show, never where.
    /// </summary>
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

        /// <summary>
        /// The characters a row of the content holds, about: ADR 0026's subject line holds 36 at 24 dp
        /// in the menu's 32 degree column, so 48 at 18 dp.
        /// </summary>
        public const int RowCharacters = 48;

        /// <summary>
        /// The companion's quote takes 2 rows (lane V, by render: with its view's line and a third row,
        /// the page passes a Quest 3S's field). Its line shares the quote with its question while both
        /// fit; else the question shows alone, never cut, taking a third row only if it alone needs one.
        /// </summary>
        public const int QuoteRows = 2;

        /// <summary>Every step, left to right.</summary>
        public static IReadOnlyList<NewProjectStep> Steps { get; } =
            new[] { NewProjectStep.YourIdea, NewProjectStep.Questions, NewProjectStep.Recap, NewProjectStep.StartBuilding };

        /// <summary>A step's words in the row of shapes.</summary>
        public static string Words(NewProjectStep step) => step switch
        {
            NewProjectStep.YourIdea => "Your idea",
            NewProjectStep.Questions => "Questions",
            NewProjectStep.Recap => "Recap",
            _ => EntryText.StartBuilding,
        };

        /// <summary>
        /// The row of steps with <paramref name="chosen"/> lit. Your idea is always reached; Questions
        /// once the companion or the fixed questions have begun; Recap once there is a first task; and
        /// Start building once nothing stops it (<paramref name="startReached"/>). A step not reached
        /// stays quiet and takes no press; the one shown is always reached.
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
            _ => "start-building",
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
        /// their own words, or the project's name once there is one; else Create a project.
        /// </summary>
        public static (string Subject, bool IsData) Subject(ProjectIdea idea)
        {
            if (idea.ExistingProjectId != null) return (EntryText.CreateTitle(LabelText.Plain(idea.Name)), true);
            if (!string.IsNullOrWhiteSpace(idea.OwnWords)) return (LabelText.Plain(idea.OwnWords), true);
            if (idea.Name.Length > 0) return (LabelText.Plain(idea.Name), true);
            return (EntryText.CreateProject, false);
        }

        /// <summary>
        /// Your idea: what to make, or for a project that exists what the task should do; Type my idea,
        /// which shows the idea once typed or heard; and a way to figure it out, the companion where it
        /// can run for a new project, else the fixed questions, with why the companion can't when it
        /// can't. Choosing a row lights it and sets the main action: Make the recap from the idea, Talk
        /// it through, or Answer the questions. Hold to talk says the idea; Close keeps the draft.
        /// </summary>
        /// <param name="chosen">The row the person chose; the typed idea is chosen while nothing else is.</param>
        /// <param name="voice">Hold to talk is offered: development builds, connected, never in the demonstration.</param>
        /// <param name="said">A line for this page only, such as hold to talk's words, or that the idea is what the computer heard.</param>
        /// <param name="companion">
        /// Whether the companion can be asked, as the computer said when New project opened (ADR 0025);
        /// null when not read, as in the demonstration.
        /// </param>
        public static MenuFrame YourIdea(ProjectIdea idea, IdeaRow chosen, bool startReached, bool voice, string? said, CompanionStatus? companion = null)
        {
            var existing = idea.ExistingProjectId != null;
            var typed = idea.OwnWords;
            if (chosen == IdeaRow.None && typed != null) chosen = IdeaRow.Typed;
            var talk = !existing && companion is AvailableCompanion;
            var lines = new List<PageLine>
            {
                new PageLine(existing ? EntryText.WorkPrompt : EntryText.IdeaPrompt),
                new PageLine(typed == null ? EntryText.TypeIdea : LabelText.Plain(typed), wordsAreData: typed != null, icon: GlazeIcon.Type,
                    action: TypeIdea, choice: true, chosen: chosen == IdeaRow.Typed && typed != null, rows: typed == null ? 1 : 2),
                talk
                    ? new PageLine(CompanionText.TalkItThrough, action: ChooseCompanion, choice: true, chosen: chosen == IdeaRow.Companion)
                    : new PageLine(EntryText.AnswerQuestions, action: ChooseQuestions, choice: true, chosen: chosen == IdeaRow.FixedQuestions),
            };
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            else if (!existing && companion is UnavailableCompanion unavailable)
            {
                lines.Add(new PageLine(CompanionText.Unavailable(unavailable.Reason?.Code), tone: LineTone.Secondary, rows: 2));
            }
            lines.Add(new PageLine(EntryText.NothingStartsYet, tone: LineTone.Secondary));
            var main = chosen switch
            {
                IdeaRow.Companion when talk => new Prompt(BeginCompanion, CompanionText.TalkItThroughShort, GlazeIcon.Next, main: true),
                IdeaRow.FixedQuestions when !talk => new Prompt(BeginQuestions, EntryText.StartQuestions, GlazeIcon.Next, main: true),
                _ => new Prompt(UseIdea, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true, available: typed != null),
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
        /// Hold to talk is the secondary prompt wherever the person may speak; Close keeps the draft.
        /// </summary>
        /// <param name="startReached">Start building's step can be chosen: nothing stops it.</param>
        /// <param name="voice">Hold to talk is offered: development builds, connected, never in the demonstration.</param>
        /// <param name="said">A line for this page only, such as hold to talk's words or why an answer was refused.</param>
        /// <param name="waitedSeconds">How long the reply has been on its way.</param>
        /// <param name="recording">
        /// The demonstration plays this recorded exchange: said so in the source line, only the
        /// recorded answer can be chosen, and Make the recap only where the recording asked for it.
        /// </param>
        public static MenuFrame Questions(ProjectIdea idea, bool startReached, bool voice, string? said, double waitedSeconds,
            CompanionRecording? recording = null)
        {
            var exchange = idea.Companion ?? throw new System.ArgumentException("The idea has no exchange with the companion.", nameof(idea));
            var recorded = recording != null;
            var lines = new List<PageLine>();
            var withoutIt = exchange.Chosen == CompanionAnswerRow.WithoutIt;
            Prompt main;
            if (exchange.Waiting)
            {
                // Make the recap's reason, the page's last line, says it is waiting, and once the wait is long, why it may be.
                var waiting = waitedSeconds >= CompanionText.WaitingLongSeconds ? CompanionText.Waiting + " " + CompanionText.WaitingLong : CompanionText.Waiting;
                main = new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true, available: false, reason: waiting);
            }
            else if (exchange.Failure != null)
            {
                lines.Add(new PageLine(CompanionText.Failure(exchange.Failure) ?? CompanionText.CouldNotAsk, tone: LineTone.Problem, rows: 3));
                main = new Prompt(TryAgain, EntryText.TryAgain, GlazeIcon.Refresh, main: true);
            }
            else if (exchange.Latest is AskReply ask)
            {
                // Its view only when it thinks the idea can't be built; asking already says it found it unclear.
                var notBuildable = ask.View == CompanionView.NotBuildable;
                if (notBuildable) lines.Add(new PageLine(CompanionText.ThinksNotBuildable, tone: LineTone.Secondary, rows: 2));
                var quote = Asked(ask, withLine: !notBuildable);
                lines.Add(new PageLine(quote, wordsAreData: true, claim: true, rows: System.Math.Max(QuoteRows, Rows(quote))));
                var choices = ask.Question?.Choices ?? new List<string>();
                var recordedAnswer = recording?.RecordedAnswer(exchange);
                for (var index = 0; index < choices.Count; index++)
                {
                    lines.Add(new PageLine(LabelText.Plain(choices[index]), wordsAreData: true, action: ChooseSuggestion,
                        key: index.ToString(CultureInfo.InvariantCulture), choice: true,
                        chosen: exchange.Chosen == CompanionAnswerRow.Suggestion && exchange.ChosenSuggestion == index,
                        available: recorded ? choices[index] == recordedAnswer : exchange.CanSay, rows: 2));
                }
                if (!recorded)
                {
                    var written = exchange.Written;
                    lines.Add(new PageLine(written == null ? CompanionText.TypeAnswer : LabelText.Plain(written), wordsAreData: written != null,
                        icon: GlazeIcon.Type, action: TypeAnswer, choice: true, chosen: exchange.Chosen == CompanionAnswerRow.Written,
                        available: exchange.CanSay, rows: written == null ? 1 : 2));
                }
                main = exchange.Answer != null
                    ? new Prompt(SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), WorkspaceText.IconOf(WorkspaceAction.Answer), main: true)
                    : new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true,
                        available: recorded ? recording!.RecapHere(exchange) : exchange.CanAskForRecap,
                        reason: recorded || exchange.PersonSpoke ? null : CompanionText.AnswerFirst);
            }
            else
            {
                // The companion has proposed: its recap is made, and Make the recap only opens it.
                if (exchange.Latest is CompanionReply reply) lines.Add(new PageLine(CompanionText.Says(reply.Line), wordsAreData: true, claim: true, rows: 3));
                main = new Prompt(MakeRecap, CompanionText.MakeTheRecap, GlazeIcon.Next, main: true);
            }
            // Go on without it, the last answer, until the companion has proposed; the recording plays to its proposal.
            if (!recorded && exchange.Proposal == null)
            {
                lines.Add(new PageLine(CompanionText.GoOnWithout, icon: GlazeIcon.Next, action: GoOnWithout, choice: true, chosen: withoutIt));
            }
            if (withoutIt && exchange.Proposal == null) main = new Prompt(MakeRecapFromMyWords, CompanionText.MakeTheRecapFromMyWords, GlazeIcon.Next, main: true);
            if (exchange.WrittenHeard && exchange.Chosen == CompanionAnswerRow.Written) lines.Add(new PageLine(VoiceText.HeardAnswer, tone: LineTone.Secondary, rows: 2));
            if (said != null) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            if (!recorded && !exchange.Waiting && exchange.Failure == null && exchange.Latest is AskReply && !exchange.CanSay)
            {
                lines.Add(new PageLine(CompanionText.Full, tone: LineTone.Secondary, rows: 2));
            }
            var talk = voice && !recorded
                ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, available: exchange.CanSay, holds: true)
                : null;
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

        private static Prompt Close() => new Prompt(Footer.Close, EntryText.Close, GlazeIcon.Close, PromptKind.Close);
    }
}
