#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The id of a command whose outcome is unknown, kept on the device so a restart still blocks a blind retry.</summary>
    public interface IKeptCommand
    {
        string? Id { get; set; }
    }

    /// <summary>
    /// New project as a column of the menu's plane (ADR 0026): the draft for each place, the step and
    /// page showing, what is chosen on it, the companion's exchange (ADR 0025), the review and the build.
    /// It gives the director the frame to show and takes what a press raised; nothing reaches the
    /// computer but a read, a question to the companion, and the commands a review read to its end
    /// confirmed, which only <see cref="BuildSequence"/> builds and sends. Drafts stay on the device
    /// across a restart for the computer they were made with (<see cref="CreationDrafts"/>).
    /// </summary>
    public sealed class NewProjectFlow : IMenuColumn
    {
        private enum RecapPage
        {
            Facts,
            Task,
            Folder,
            Options,
            Words,
        }

        private enum BuildPage
        {
            Review,
            Starting,
            Unresolved,
        }

        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private readonly ICreationDraftStore? draftStore;
        private readonly CompanionRecording? recording;
        private readonly IKeptCommand unknownOutcome;

        /// <summary>The drafts of the places not open now, each with how far its start got.</summary>
        private readonly Dictionary<string, (ProjectIdea Idea, BuildSequence? Sequence)> others =
            new Dictionary<string, (ProjectIdea, BuildSequence?)>();

        private readonly NewWorkDraft draft;
        private ProjectIdea? idea;
        private string place = "";
        private BuildSequence? sequence;
        private Task<CommandAckMessage>? pendingAck;

        private NewProjectStep step;
        private RecapPage recapPage;
        private BuildPage buildPage;
        private IdeaRow ideaRow;
        private RecapFact? fact;
        private WordsFor wordsFor;
        private LocationRoot? wordsRoot;
        private string? written;
        private bool writtenHeard;
        private bool confirmingStartOver;
        private bool recoveryArmed;
        private bool showModels;
        private (NewProjectStep Step, string Text)? said;

        private NewWorkReview? review;
        private int measuredPartRows;

        /// <summary>The frame given to the director, built once for each change, so a drawn report names the very frame it drew.</summary>
        private MenuFrame? shown;

        /// <summary>The frame the director last drew: what the person saw, and so the only frame a press is checked against.</summary>
        private MenuFrame? seen;

        /// <summary>The unknown-outcome id this flow kept for its own build, which only it may replace or clear.</summary>
        private string? ownId;

        /// <summary>What the frame was built from that changes without a press: the start's problem, the connection, a long wait, the journal and the text size.</summary>
        private string? shownFrom;

        private LocationsResponse? locations;
        private string? locationsProblem;
        private Task<LocationsResponse>? locationsRead;
        private CancellationTokenSource? locationsCancellation;

        private Task<RuntimeModelsResponse>? modelsRead;
        private string? modelsFor;
        private CancellationTokenSource? modelsCancellation;

        private CompanionStatus? companion;
        private Task<CompanionStatus>? companionRead;
        private Task<CompanionReplyResponse>? companionReply;
        private CompanionExchange? companionPending;
        private int companionAsked;
        private double companionSince;
        private CancellationTokenSource? companionCancellation;

        private CreationDrafts? keptDrafts;
        private string? keptJournal;
        private string? keptRuntime;
        private string? keptModel;

        /// <param name="draftStore">Where drafts are kept across a restart, in the app's private files; null keeps none, as in the renders.</param>
        /// <param name="recording">The demonstration's recorded exchange with the companion; null when live.</param>
        public NewProjectFlow(IMenuHost host, CommandFactory commands, IKeptCommand unknownOutcome, ICreationDraftStore? draftStore = null,
            CompanionRecording? recording = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.unknownOutcome = unknownOutcome ?? throw new ArgumentNullException(nameof(unknownOutcome));
            this.draftStore = draftStore;
            this.recording = recording;
            draft = new NewWorkDraft(commands);
        }

        public event Action? Changed;

        public event Action? Closed;

        /// <summary>New project is open on the plane.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>A draft waits somewhere, unstarted: Projects may offer to go on with it.</summary>
        public bool HasDraft => Pending(idea, sequence) || others.Values.Any(other => Pending(other.Idea, other.Sequence));

        /// <summary>The idea open now, for the director's and the renders' reading; null before New project first opens.</summary>
        public ProjectIdea? Idea => idea;

        /// <summary>The step showing.</summary>
        public NewProjectStep Step => step;

        /// <summary>The review showing, for the director's measuring and the renders; null when none is.</summary>
        public NewWorkReview? Review => step == NewProjectStep.Build && buildPage == BuildPage.Review ? review : null;

        /// <summary>The build sent, or resumed after a restart; null before one.</summary>
        public BuildSequence? Sequence => sequence;

        private static bool Pending(ProjectIdea? idea, BuildSequence? sequence) =>
            idea != null && (idea.HasRecap || idea.Guided || idea.Companion != null) && sequence?.Started != true;

        private bool Live => !host.Demonstration;

        private bool Voice => host.VoiceOffered && Live;

        private string? Said(NewProjectStep on) => said is { } line && line.Step == on ? line.Text : null;

        /// <summary>
        /// Opens New project, or Add a task to <paramref name="projectId"/>: the draft for that place where
        /// it stands, or a new one, on the step it reached. A start whose outcome is unknown comes first,
        /// and a build on its way shows how it goes.
        /// </summary>
        public void Open(string? projectId, string? projectName)
        {
            IsOpen = true;
            RestoreDrafts();
            if (UnknownId() != null && sequence == null)
            {
                Show(NewProjectStep.Build, BuildPage.Unresolved);
                return;
            }
            if (sequence != null && !sequence.Started && (sequence.Current != null || sequence.Unresolved != null))
            {
                Show(NewProjectStep.Build, BuildPage.Starting);
                return;
            }
            var key = projectId ?? "";
            if (idea == null || key != place)
            {
                // Each place keeps its own draft, with how far its start got, so nothing is made twice.
                if (idea != null) others[place] = (idea, sequence);
                place = key;
                if (others.TryGetValue(key, out var kept))
                {
                    others.Remove(key);
                    (idea, sequence) = kept;
                }
                else (idea, sequence) = (new ProjectIdea(projectId, projectName), null);
                review = null;
                said = null;
            }
            if (sequence?.Started == true)
            {
                idea = new ProjectIdea(idea!.ExistingProjectId, idea.ExistingProjectId == null ? null : idea.Name);
                sequence = null;
            }
            draft.ProjectId = idea!.ExistingProjectId;
            ChooseKeptRuntime();
            ReadCompanion();
            // A kept folder's place is read again before it shows, since labels change with the places.
            if (idea.Folder != null && locationsRead == null) ReadFolders();
            confirmingStartOver = false;
            fact = null;
            Show(idea.HasRecap ? NewProjectStep.Recap : idea.Companion != null || idea.Guided ? NewProjectStep.Questions : NewProjectStep.YourIdea);
            shown = null;
            seen = null;
        }

        /// <summary>Takes New project off the plane; its draft stays where it is.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            review = null;
            shown = null;
            seen = null;
            confirmingStartOver = false;
            recoveryArmed = false;
            KeepDrafts();
            Closed?.Invoke();
        }

        public MenuFrame? Frame => IsOpen ? shown ??= Build() : null;

        private MenuFrame Build()
        {
            var current = idea ??= new ProjectIdea();
            var problem = StartProblem();
            var reached = BuildReached(problem);
            switch (step)
            {
                case NewProjectStep.YourIdea:
                    return NewProjectScreens.YourIdea(current, ideaRow, reached, Voice, Said(step), CompanionShown());
                case NewProjectStep.Questions when current.Companion != null && !(current.Companion.Left && current.Guided):
                    var waited = current.Companion.Waiting ? host.Now - companionSince : 0;
                    return NewProjectScreens.Questions(current, reached, Voice, Said(step), waited, recording);
                case NewProjectStep.Questions when current.Guided && current.Question < ProjectIdea.Questions.Count:
                    return NewProjectScreens.FixedQuestion(current, reached, Voice, Said(step));
                case NewProjectStep.Questions:
                    step = current.HasRecap ? NewProjectStep.Recap : NewProjectStep.YourIdea;
                    return Build();
                case NewProjectStep.Recap when !current.HasRecap:
                    // Nothing to recap yet, as after starting over or clearing a start: back to the idea.
                    step = NewProjectStep.YourIdea;
                    return Build();
                case NewProjectStep.Recap:
                    return recapPage switch
                    {
                        RecapPage.Task => NewProjectScreens.RecapTask(current, reached, Voice, Said(step)),
                        RecapPage.Folder => NewProjectScreens.RecapFolder(current, reached, locations, locationsProblem, Said(step)),
                        RecapPage.Options => NewProjectScreens.RecapOptions(current, reached, draft, host.State?.Runtimes ?? new List<RuntimeDescriptor>(),
                            showModels, Live),
                        RecapPage.Words => NewProjectScreens.Words(current, reached, wordsFor, written, writtenHeard, Voice, Said(step), wordsRoot),
                        _ => NewProjectScreens.Recap(current, draft, CurrentFolder(), Live, Said(step), problem, fact, confirmingStartOver, reached,
                            Building() || OutcomeUnknown ? problem : null),
                    };
                default:
                    switch (buildPage)
                    {
                        case BuildPage.Unresolved when UnknownId() is string id:
                            var record = host.State?.Commands.TryGetValue(id, out var found) == true ? found : null;
                            return NewProjectScreens.Unresolved(current, id, record, recoveryArmed, Live && host.Connected);
                        case BuildPage.Starting when sequence != null:
                            return NewProjectScreens.Starting(current, sequence, current.Folder);
                        case BuildPage.Review when review != null:
                            Measure();
                            return NewProjectScreens.Review(current, review, problem);
                        default:
                            step = current.HasRecap ? NewProjectStep.Recap : NewProjectStep.YourIdea;
                            recapPage = RecapPage.Facts;
                            return Build();
                    }
            }
        }

        /// <summary>What Your idea offers to figure an idea out with: the companion as the computer said, or the recording in the demonstration.</summary>
        private CompanionStatus? CompanionShown()
        {
            if (recording != null && host.Demonstration)
            {
                return new AvailableCompanion { Companion = new CompanionModel { Name = CompanionRecording.RecordedModel, Served = "this_mac" }, MaxQuestions = 4 };
            }
            return Live ? companion : null;
        }

        /// <summary>
        /// Why Start building can't go ahead now, or null: first a build on its way, then a start whose
        /// outcome is unknown, kept on the device or this build's own, which must be checked before
        /// anything starts again, so nothing is made twice.
        /// </summary>
        private string? StartProblem() =>
            Building() ? EntryText.AlreadyStarting
            : OutcomeUnknown ? EntryText.PreviousRequestLine
            : EntryScreens.StartProblem(host.Demonstration, host.State, host.Connected, idea, draft, CurrentFolder(), sequence);

        /// <summary>A start may have run without this headset knowing: an id is kept, and no build is on its way to settle it.</summary>
        private bool OutcomeUnknown => !Building() && UnknownId() != null;

        /// <summary>Build's step can be chosen: nothing stops a start, or a start whose outcome is unknown waits there to be checked.</summary>
        private bool BuildReached(string? problem) => (problem == null && idea?.HasRecap == true) || OutcomeUnknown;

        /// <summary>An existing project's folder as the computer bound it, or null for a new project or one without.</summary>
        private ProjectLocation? CurrentFolder()
        {
            var projectId = idea?.ExistingProjectId;
            return projectId != null && host.State?.Projects.TryGetValue(projectId, out var project) == true ? project!.Location : null;
        }

        /// <summary>
        /// What a press raised. Only what the frame showing offers acts, and only while it can be taken:
        /// a prompt that is there and available, a line that takes a press, a step reached, so nothing a
        /// person can't see or press now ever runs.
        /// </summary>
        public void Act(string id, string? key)
        {
            if (!IsOpen || !(seen is MenuFrame showing) || !Offers(showing, id, key)) return;
            var current = idea ??= new ProjectIdea();
            var exchange = current.Companion;
            switch (id)
            {
                case Footer.Close:
                    Close();
                    return;
                case SidePanel.Close:
                    fact = null;
                    break;
                case MenuFrame.ChooseSection when NewProjectScreens.StepOf(key) is NewProjectStep chosen:
                    ChooseStep(chosen);
                    break;

                // Your idea.
                case NewProjectScreens.TypeIdea:
                    host.OpenKeyboard(current.OwnWords ?? "", current.ExistingProjectId == null ? EntryText.IdeaPrompt : EntryText.WorkPrompt, text =>
                    {
                        if (text.Trim().Length == 0) return;
                        current.UseIdea(text);
                        ideaRow = IdeaRow.Typed;
                        said = null;
                        Done();
                    });
                    return;
                case NewProjectScreens.ChooseCompanion:
                    ideaRow = IdeaRow.Companion;
                    break;
                case NewProjectScreens.ChooseQuestions:
                    ideaRow = IdeaRow.FixedQuestions;
                    break;
                case NewProjectScreens.UseIdea when current.HasRecap:
                    Show(NewProjectStep.Recap);
                    break;
                case NewProjectScreens.BeginCompanion:
                    BeginCompanion(current.OwnWords != null ? CompanionStart.Idea : CompanionStart.Help);
                    break;
                case NewProjectScreens.BeginQuestions:
                    current.BeginGuide();
                    Show(NewProjectStep.Questions);
                    break;
                case NewProjectScreens.HoldToTalk:
                    // Pressed and let go before its hold began.
                    said = (step, VoiceText.TooShort);
                    break;

                // The companion's question.
                case NewProjectScreens.ChooseSuggestion when exchange != null && key != null:
                    // By its words: a reply that came after the frame was drawn offers other suggestions, and none of them is taken for it.
                    var suggestion = (exchange.Latest as AskReply)?.Question?.Choices?.IndexOf(key) ?? -1;
                    if (suggestion >= 0 && (recording == null || key == recording.RecordedAnswer(exchange))) exchange.Choose(suggestion);
                    break;
                case NewProjectScreens.TypeAnswer when exchange != null && recording == null:
                    host.OpenKeyboard(exchange.Written ?? "", CompanionText.TypeAnswer, text =>
                    {
                        if (!exchange.Write(text))
                        {
                            said = (NewProjectStep.Questions, text.Trim().Length > CompanionExchange.PersonLimit
                                ? CompanionText.TooLong(CompanionExchange.PersonLimit) : CompanionText.Full);
                        }
                        else said = null;
                        Done();
                    });
                    return;
                case NewProjectScreens.GoOnWithout when exchange != null && recording == null:
                    exchange.ChooseWithoutIt();
                    break;
                case NewProjectScreens.SendAnswer when exchange != null:
                    SendAnswer(exchange);
                    break;
                case NewProjectScreens.MakeRecap when exchange != null:
                    MakeRecap(current, exchange);
                    break;
                case NewProjectScreens.MakeRecapFromMyWords when exchange != null:
                    GoOnWithout(current, exchange);
                    break;
                case NewProjectScreens.TryAgain when exchange != null:
                    Ask(exchange.Retry());
                    break;

                // The fixed questions.
                case NewProjectScreens.ChooseFixedAnswer when key != null:
                    current.ChooseGuideAnswer(key);
                    break;
                case NewProjectScreens.TypeFixedAnswer when current.Question < ProjectIdea.Questions.Count:
                    var asked = current.Question;
                    host.OpenKeyboard(current.GuideWritten ?? "", ProjectIdea.Questions[asked].Prompt, text =>
                    {
                        if (current.Question == asked) current.WriteGuideAnswer(text);
                        Done();
                    });
                    return;
                case NewProjectScreens.SkipFixedQuestion:
                    current.ChooseGuideSkip();
                    break;
                case NewProjectScreens.NextQuestion:
                    if (current.NextQuestion() && current.Question >= ProjectIdea.Questions.Count) Show(NewProjectStep.Recap);
                    break;

                // The recap and its pages.
                case NewProjectScreens.ChooseFact when NewProjectScreens.FactOf(key) is RecapFact chosenFact:
                    fact = chosenFact;
                    confirmingStartOver = false;
                    break;
                case NewProjectScreens.Rename when current.ExistingProjectId == null:
                    OpenWords(WordsFor.Name, null);
                    break;
                case NewProjectScreens.ChangeTask:
                    switch (NewProjectScreens.ChangeFor(current))
                    {
                        case TaskChange.Answers:
                            recapPage = RecapPage.Task;
                            break;
                        case TaskChange.Questions:
                            current.BeginGuide();
                            Show(NewProjectStep.Questions);
                            break;
                        default:
                            OpenWords(WordsFor.FirstTask, null);
                            break;
                    }
                    break;
                case NewProjectScreens.ChooseWhere:
                case NewProjectScreens.ChooseAnotherFolder:
                    Show(NewProjectStep.Recap, page: RecapPage.Folder);
                    ReadFolders();
                    break;
                case NewProjectScreens.MoreOptions:
                    showModels = draft.Runtime?.ModelChoice == ModelChoice.Listed;
                    // An agent app kept with the draft lists its models only now, on the person's press: listing may start it.
                    if (showModels && draft.Models.Count == 0 && modelsRead == null && draft.Runtime is RuntimeDescriptor kept) ReadModels(kept);
                    Show(NewProjectStep.Recap, page: RecapPage.Options);
                    break;
                case NewProjectScreens.StartOver when step == NewProjectStep.Recap && !Building() && !OutcomeUnknown:
                    confirmingStartOver = true;
                    break;
                case NewProjectScreens.ConfirmStartOver when confirmingStartOver && !Building() && !OutcomeUnknown:
                    idea = new ProjectIdea(current.ExistingProjectId, current.ExistingProjectId == null ? null : current.Name);
                    LeaveCompanion(exchange);
                    review = null;
                    sequence = null;
                    ideaRow = IdeaRow.None;
                    Show(NewProjectStep.YourIdea);
                    break;
                case NewProjectScreens.Cancel when step == NewProjectStep.Build && buildPage == BuildPage.Review:
                    review = null;
                    Show(NewProjectStep.Recap);
                    break;
                case NewProjectScreens.Cancel:
                    confirmingStartOver = false;
                    recoveryArmed = false;
                    break;
                case NewProjectScreens.StartBuilding:
                    StartBuilding();
                    break;
                case NewProjectScreens.UseSuggestedTask when current.Companion?.Proposal is ProposeReply proposed:
                    current.UseProposal(proposed.Proposal);
                    break;
                case NewProjectScreens.UseMyWords:
                    current.UseOwnWords();
                    break;
                case NewProjectScreens.TypeTask:
                    host.OpenKeyboard(current.FirstTask, EntryText.WhatFirstTask, text =>
                    {
                        current.Rewrite(text);
                        said = null;
                        Done();
                    });
                    return;
                case NewProjectScreens.ChooseFolder when key != null && locations != null:
                    ChooseOption(current, key);
                    break;
                case NewProjectScreens.ReadFolders:
                    ReadFolders();
                    break;
                case NewProjectScreens.ChooseRuntime when key != null:
                    if (host.State?.Runtimes.FirstOrDefault(each => each.RuntimeId == key) is RuntimeDescriptor runtime) ChooseRuntime(runtime);
                    break;
                case NewProjectScreens.ChooseModel when key != null:
                    if (draft.Models.FirstOrDefault(each => each.ModelRef == key) is RuntimeModel model) draft.ChooseModel(model);
                    break;
                case NewProjectScreens.ChangeRuntime:
                    showModels = false;
                    break;
                case NewProjectScreens.TypeWords:
                    host.OpenKeyboard(written ?? WordsNow(current) ?? "", WordsPrompt(), text =>
                    {
                        written = text;
                        writtenHeard = false;
                        Done();
                    });
                    return;
                case NewProjectScreens.Done:
                    KeepWords(current);
                    break;

                // Build.
                case NewProjectScreens.NextPart when review != null:
                    review.Next(host.Now);
                    break;
                case NewProjectScreens.ConfirmStart:
                    ConfirmReviewed(current);
                    break;
                case NewProjectScreens.TryAgainStart:
                    if (sequence?.CanRetry == true) StartBuilding();
                    break;
                case NewProjectScreens.ChangeRequest:
                    review = null;
                    Show(NewProjectStep.Recap);
                    break;
                case NewProjectScreens.UseThatFolder when current.Folder is ProjectFolder taken && taken.IsNew:
                    current.ChooseFolder(ProjectFolder.Existing(taken));
                    StartBuilding();
                    break;
                case NewProjectScreens.CheckFirst:
                    recoveryArmed = false;
                    Show(NewProjectStep.Build, BuildPage.Unresolved);
                    break;
                case NewProjectScreens.Clear when Live && host.Connected:
                    recoveryArmed = true;
                    break;
                case NewProjectScreens.ConfirmClear when recoveryArmed:
                    recoveryArmed = false;
                    pendingAck = null;
                    sequence = null;
                    // Cleared by the person's two presses: whichever start it was, kept here or by an earlier run.
                    unknownOutcome.Id = null;
                    ownId = null;
                    idea = new ProjectIdea();
                    said = (NewProjectStep.YourIdea, EntryText.Cleared);
                    Show(NewProjectStep.YourIdea);
                    break;
                default:
                    return;
            }
            Done();
        }

        /// <summary>The director drew <paramref name="drawn"/>: a part of the review counts as read only when it is the very frame this column gave and still stands by.</summary>
        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
            if (!IsOpen || !ReferenceEquals(drawn, shown)) return;
            seen = drawn;
            if (Review is not NewWorkReview reading || !reading.Paginated) return;
            if (reading.Drawn(host.Now)) Redraw();
        }

        /// <summary>The director runs the one voice for any held prompt and routes what it hears; New project needs nothing more.</summary>
        public void HoldStarted(string id)
        {
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        /// <summary>Hold to talk: what the computer heard, where the person gives words now; nothing is sent until they confirm it.</summary>
        public void Heard(string text)
        {
            if (!IsOpen || idea == null || text.Trim().Length == 0) return;
            var current = idea;
            switch (step)
            {
                case NewProjectStep.YourIdea:
                    current.UseIdea(text);
                    ideaRow = IdeaRow.Typed;
                    said = (step, VoiceText.HeardNote);
                    break;
                case NewProjectStep.Questions when current.Companion != null && recording == null && !current.Companion.Left:
                    if (!current.Companion.Write(text, heard: true)) said = (step, CompanionText.Full);
                    else said = null;
                    break;
                case NewProjectStep.Questions when current.Guided && current.Question < ProjectIdea.Questions.Count:
                    current.WriteGuideAnswer(text, heard: true);
                    said = null;
                    break;
                case NewProjectStep.Recap when recapPage == RecapPage.Task:
                    current.Rewrite(text);
                    said = (step, VoiceText.HeardNote);
                    break;
                case NewProjectStep.Recap when recapPage == RecapPage.Words:
                    written = text;
                    writtenHeard = true;
                    said = null;
                    break;
                default:
                    return;
            }
            Done();
        }

        public void Said(string words)
        {
            if (!IsOpen) return;
            said = (step, words);
            Redraw();
        }

        /// <summary>Another window took focus: armed confirmations lapse, and a review waiting for its Yes is read again from the recap.</summary>
        public void FocusLeft()
        {
            draft.FocusLeft();
            recoveryArmed = false;
            confirmingStartOver = false;
            if (IsOpen && step == NewProjectStep.Build && buildPage == BuildPage.Review && review != null)
            {
                review = null;
                Show(NewProjectStep.Recap);
                said = (NewProjectStep.Recap, EntryText.ReviewAfresh);
            }
            if (IsOpen) Redraw();
        }

        public void Tick()
        {
            RestoreDrafts();
            var changed = PollCompanion() | PollModels() | PollFolders() | AdvanceBuild();
            var from = BuiltFrom();
            if (from != shownFrom)
            {
                shownFrom = from;
                changed = true;
            }
            if (changed && IsOpen) Redraw();
        }

        /// <summary>What a frame shows that changes with no press of the person's.</summary>
        private string BuiltFrom()
        {
            var waitedLong = idea?.Companion?.Waiting == true && host.Now - companionSince >= CompanionText.WaitingLongSeconds;
            return string.Join("|", StartProblem() ?? "", host.Connected, waitedLong, host.State?.Position ?? -1, host.TextSize);
        }

        /// <summary>The frame changed: built again when asked, and the director told.</summary>
        private void Redraw()
        {
            shown = null;
            Changed?.Invoke();
        }

        /// <summary>Whether <paramref name="frame"/> offers what a press raised, as the person sees it now.</summary>
        private static bool Offers(MenuFrame frame, string id, string? key)
        {
            if (id == SidePanel.Close) return frame.Side != null;
            if (id == MenuFrame.ChooseSection) return frame.Sections.Any(section => section.Key == key && section.Reached);
            if (frame.Footer.All.Any(each => each.Prompt.Id == id && each.Prompt.Available)) return true;
            return frame.Lines.Any(line => line.Action == id && line.Key == key && line.Pressable);
        }

        /// <summary>The steps row: a step reached shows, a review in progress is thrown away, and armed confirmations lapse.</summary>
        private void ChooseStep(NewProjectStep chosen)
        {
            var current = idea!;
            var sections = NewProjectScreens.Sections(step, current, BuildReached(StartProblem()));
            if (!sections.Any(section => section.Key == NewProjectScreens.Key(chosen) && section.Reached)) return;
            review = null;
            confirmingStartOver = false;
            recoveryArmed = false;
            if (chosen == NewProjectStep.Build)
            {
                // A start whose outcome is unknown is what Build shows, until it is checked and cleared.
                if (OutcomeUnknown) Show(NewProjectStep.Build, BuildPage.Unresolved);
                else StartBuilding();
                return;
            }
            if (chosen == NewProjectStep.Questions && current.Companion == null && current.Guided && current.Question >= ProjectIdea.Questions.Count)
            {
                // Walked again from the first, each answer already chosen.
                current.BeginGuide();
            }
            Show(chosen);
        }

        private void Show(NewProjectStep shown, BuildPage page = BuildPage.Review) => Show(shown, RecapPage.Facts, page);

        private void Show(NewProjectStep shown, RecapPage page, BuildPage build = BuildPage.Review)
        {
            if (shown != step) said = null;
            step = shown;
            recapPage = page;
            buildPage = build;
            if (page != RecapPage.Facts) fact = null;
        }

        private void Done()
        {
            KeepDrafts();
            Redraw();
        }

        // ---------------------------------------------------------------------------------------------
        // The companion (ADR 0025).

        private void ReadCompanion()
        {
            if (!Live || companionRead != null || host.Api is not ControlPlaneApi api) return;
            companionRead = api.GetCompanionAsync();
        }

        private void BeginCompanion(CompanionStart start)
        {
            var current = idea!;
            if (recording != null && host.Demonstration)
            {
                recording.Begin(current);
                Show(NewProjectStep.Questions);
                return;
            }
            if (!(companion is AvailableCompanion available)) return;
            current.BeginCompanion(start, (int)available.MaxQuestions);
            Ask(current.Companion!.Ask(CompanionWant.Next));
            Show(NewProjectStep.Questions);
        }

        private void SendAnswer(CompanionExchange exchange)
        {
            if (recording != null)
            {
                if (exchange.Answer is string answer && answer == recording.RecordedAnswer(exchange)) recording.Press(exchange, answer);
                // The recording's reply to that answer may be its proposal.
                if (exchange.Proposal is ProposeReply proposed && idea != null)
                {
                    idea.UseProposal(proposed.Proposal);
                    Show(NewProjectStep.Recap);
                }
                return;
            }
            Ask(exchange.SendAnswer());
        }

        private void MakeRecap(ProjectIdea current, CompanionExchange exchange)
        {
            if (exchange.Proposal is ProposeReply)
            {
                Show(NewProjectStep.Recap);
                return;
            }
            if (recording != null)
            {
                if (recording.AskForRecap(exchange) && exchange.Proposal is ProposeReply recorded)
                {
                    current.UseProposal(recorded.Proposal);
                    Show(NewProjectStep.Recap);
                }
                return;
            }
            Ask(exchange.Ask(CompanionWant.Proposal));
        }

        /// <summary>Go on without it: a reply on its way is dropped, the exchange stays, and the recap from the person's words, or the fixed questions, follow.</summary>
        private void GoOnWithout(ProjectIdea current, CompanionExchange exchange)
        {
            LeaveCompanion(exchange);
            if (current.HasRecap)
            {
                Show(NewProjectStep.Recap);
                return;
            }
            current.BeginGuide();
            Show(NewProjectStep.Questions);
        }

        private void LeaveCompanion(CompanionExchange? exchange)
        {
            exchange?.Leave();
            companionCancellation?.Cancel();
            companionReply = null;
            companionPending = null;
        }

        /// <summary>Sends one request to the companion, or says why it could not go.</summary>
        private void Ask(CompanionRepliesRequest? request)
        {
            var exchange = idea?.Companion;
            if (request == null || exchange == null) return;
            companionAsked = exchange.Generation;
            companionPending = exchange;
            companionSince = host.Now;
            companionCancellation?.Cancel();
            companionCancellation?.Dispose();
            companionCancellation = null;
            if (!Live || host.Api is not ControlPlaneApi api)
            {
                exchange.Failed(companionAsked, null);
                return;
            }
            companionCancellation = new CancellationTokenSource();
            companionReply = api.AskCompanionAsync(request, companionCancellation.Token);
        }

        private bool PollCompanion()
        {
            var changed = false;
            if (companionRead is Task<CompanionStatus> read && read.IsCompleted)
            {
                companionRead = null;
                // A read that failed leaves the companion unoffered, and the fixed questions as before.
                companion = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
                changed = true;
            }
            if (!(companionReply is Task<CompanionReplyResponse> answer) || !answer.IsCompleted) return changed;
            companionReply = null;
            var exchange = companionPending;
            companionPending = null;
            if (exchange == null) return changed;
            // The draft the reply belongs to, open or kept aside while another is open.
            var owner = idea?.Companion == exchange ? idea : others.Values.Select(each => each.Idea).FirstOrDefault(each => each.Companion == exchange);
            if (answer.Status == TaskStatus.RanToCompletion)
            {
                if (!exchange.Replied(companionAsked, answer.Result)) return changed;
                if (exchange.Proposal is ProposeReply proposed && owner != null)
                {
                    owner.UseProposal(proposed.Proposal);
                    if (owner == idea && step == NewProjectStep.Questions) Show(NewProjectStep.Recap);
                }
            }
            else if (!exchange.Failed(companionAsked, CompanionExchange.CodeOf(answer.Exception?.GetBaseException()))) return changed;
            KeepDrafts();
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // The recap's pages: words, folders, agent apps and models.

        private void OpenWords(WordsFor what, LocationRoot? root)
        {
            wordsFor = what;
            wordsRoot = root;
            written = null;
            writtenHeard = false;
            Show(NewProjectStep.Recap, page: RecapPage.Words);
        }

        private string? WordsNow(ProjectIdea current) => wordsFor switch
        {
            WordsFor.Name => current.Name.Length > 0 ? current.Name : null,
            WordsFor.FirstTask => current.FirstTask.Length > 0 ? current.FirstTask : null,
            _ => current.Folder is ProjectFolder folder && folder.IsNew && folder.RootPath == wordsRoot?.Path ? folder.FolderName
                : ProjectFolder.SuggestName(current.Name),
        };

        private string WordsPrompt() => wordsFor switch
        {
            WordsFor.Name => EntryText.NameTheProject,
            WordsFor.FirstTask => EntryText.WhatFirstTask,
            _ => EntryText.NewFolderPrompt,
        };

        /// <summary>Done on a page: the words page keeps its words only within their rule, and every page goes back to the facts.</summary>
        private void KeepWords(ProjectIdea current)
        {
            if (step != NewProjectStep.Recap) return;
            if (recapPage == RecapPage.Words && written != null)
            {
                var kept = wordsFor switch
                {
                    WordsFor.Name => current.Rename(written),
                    WordsFor.FirstTask => current.Rewrite(written),
                    _ => wordsRoot != null && ProjectFolder.New(wordsRoot, written) is ProjectFolder made && Chose(current, made),
                };
                if (!kept) return;
            }
            written = null;
            writtenHeard = false;
            Show(NewProjectStep.Recap);
        }

        private static bool Chose(ProjectIdea current, ProjectFolder folder)
        {
            current.ChooseFolder(folder);
            return true;
        }

        /// <summary>The folder row pressed, by what it chooses: a list read again since offers none that isn't still there.</summary>
        private void ChooseOption(ProjectIdea current, string key)
        {
            var option = ProjectFolder.Options(locations!).FirstOrDefault(each => NewProjectScreens.FolderKey(each) == key);
            if (option == null || !option.Choosable) return;
            if (option.Kind == FolderOptionKind.NewFolder)
            {
                OpenWords(WordsFor.FolderName, option.Root);
                return;
            }
            current.ChooseFolder(ProjectFolder.Existing(option.Root, option.Folder));
        }

        /// <summary>Reads the folders the computer lists, when the person opens where its files live or asks again; never on a timer.</summary>
        private void ReadFolders()
        {
            locationsCancellation?.Cancel();
            locationsCancellation?.Dispose();
            locationsCancellation = null;
            locations = null;
            locationsProblem = null;
            if (host.Api is not ControlPlaneApi api || !Live)
            {
                locationsProblem = EntryText.WaitingForMac;
                return;
            }
            locationsCancellation = new CancellationTokenSource();
            locationsRead = api.GetLocationsAsync(locationsCancellation.Token);
        }

        private bool PollFolders()
        {
            if (!(locationsRead is Task<LocationsResponse> read) || !read.IsCompleted) return false;
            locationsRead = null;
            if (read.IsCanceled) return false;
            if (read.IsFaulted) locationsProblem = read.Exception?.GetBaseException().Message ?? "No reason given.";
            else
            {
                // Only a listing read marks a place gone, and every one reads the places again: one back is no longer gone.
                locations = read.Result;
                idea?.ReadPlaces(read.Result);
                foreach (var other in others.Values) other.Idea.ReadPlaces(read.Result);
            }
            return true;
        }

        private void ChooseRuntime(RuntimeDescriptor runtime)
        {
            modelsCancellation?.Cancel();
            modelsCancellation?.Dispose();
            modelsCancellation = null;
            modelsRead = null;
            draft.ChooseRuntime(runtime);
            review = null;
            if (runtime.ModelChoice != ModelChoice.Listed) return;
            ReadModels(runtime);
            showModels = true;
        }

        /// <summary>Reads an agent app's models: listing may start the runtime, so only on the person's press, never on a timer or on opening.</summary>
        private void ReadModels(RuntimeDescriptor runtime)
        {
            if (host.Api is ControlPlaneApi api && Live)
            {
                modelsFor = runtime.RuntimeId;
                modelsCancellation = new CancellationTokenSource();
                modelsRead = api.GetRuntimeModelsAsync(runtime.RuntimeId, modelsCancellation.Token);
            }
            else draft.ModelReadFailed(EntryText.WaitingForMac);
        }

        private bool PollModels()
        {
            if (!(modelsRead is Task<RuntimeModelsResponse> read) || !read.IsCompleted) return false;
            modelsRead = null;
            if (draft.Runtime?.RuntimeId != modelsFor || read.IsCanceled) return false;
            if (read.IsFaulted) draft.ModelReadFailed(read.Exception?.GetBaseException().Message ?? "No reason given.");
            else draft.SetModels(read.Result);
            ChooseKeptModel();
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Build: the review, the commands its Yes confirmed, and a start whose outcome is unknown.

        private bool Building() => sequence?.InFlight == true;

        private void StartBuilding()
        {
            var current = idea;
            if (current == null || StartProblem() != null || Building()) return;
            review = EntryScreens.ReviewOf(current, draft, CurrentFolder(), Live);
            measuredPartRows = 0;
            Show(NewProjectStep.Build, BuildPage.Review);
        }

        /// <summary>Lays the review out in parts at the room a part holds now, and again whenever that changes, as with the text size.</summary>
        private void Measure()
        {
            var reading = review;
            if (reading == null) return;
            var partRows = ReviewPartRows();
            if (reading.Paginated && partRows == measuredPartRows) return;
            measuredPartRows = partRows;
            reading.Paginate(reading.Items.Select(item => host.RowsOf(item.Text, Glaze.Menu.FileColumnDegrees)).ToList(), partRows);
        }

        /// <summary>
        /// The rows of the request a part of the review holds: the page's height beside Projects, as the
        /// stage gives it, less the line over the parts and the Next part row; every row is counted as a
        /// line of its own, with a grid step after it, so a part always fits however its items split.
        /// </summary>
        private int ReviewPartRows()
        {
            var subject = Math.Max(1, host.TitleRows(NewProjectScreens.Subject(idea!).Subject, Glaze.Menu.FileColumnDegrees));
            var room = host.PageHeight(subject, besideMenu: true) - MenuPage.Words(1) - MenuPage.Target() - 2f * MenuPage.GroupGap;
            return Math.Max(1, (int)Math.Floor(room / (MenuPage.Words(1) + MenuPage.Grid)));
        }

        /// <summary>
        /// Yes, start building: only on the review read to its end, and only for the request as it stands
        /// now, which a change made since shows afresh. BuildSequence takes the review's Yes, once, and
        /// builds every command from what it confirmed.
        /// </summary>
        private void ConfirmReviewed(ProjectIdea current)
        {
            if (review?.CanConfirm != true || StartProblem() != null || Building()) return;
            var asItStands = EntryScreens.ReviewOf(current, draft, CurrentFolder(), Live);
            if (!review.SameRequest(asItStands))
            {
                review = asItStands;
                measuredPartRows = 0;
                return;
            }
            var reviewed = review;
            review = null;
            var newProject = current.ExistingProjectId == null ? current.Name : null;
            var folder = EntryScreens.FolderSent(current, CurrentFolder())?.ToContract();
            // The draft takes the first task only at this press; the build keeps what it confirmed, whatever changes after.
            draft.Objective = current.FirstTask;
            if (sequence != null && sequence.CanRetry)
            {
                Send(sequence.Retry(reviewed, newProject, folder));
            }
            else if (sequence == null)
            {
                draft.ProjectId = current.ExistingProjectId;
                var begun = new BuildSequence(draft, commands, newProject, folder);
                // Kept only once it has begun, so a refusal to begin leaves no build that never sent.
                var first = begun.Begin(reviewed);
                sequence = begun;
                Send(first);
            }
            Show(NewProjectStep.Build, BuildPage.Starting);
        }

        private void Send(CommandEnvelope command)
        {
            var submitted = host.Submit(command);
            if (submitted == null)
            {
                sequence?.AcknowledgementLost(new SessionUnavailableException("Not connected."));
                return;
            }
            Remember(sequence?.Unresolved);
            pendingAck = submitted;
        }

        private bool AdvanceBuild()
        {
            var current = sequence;
            if (current == null) return false;
            if (pendingAck is Task<CommandAckMessage> ack && ack.IsCompleted)
            {
                pendingAck = null;
                if (ack.Status == TaskStatus.RanToCompletion) current.Acknowledged(ack.Result);
                else current.AcknowledgementLost(ack.Exception?.GetBaseException() ?? new InvalidOperationException("The acknowledgement was lost."));
            }
            var wasStarted = current.Started;
            var wasStopped = current.Stopped;
            var next = current.Advance(host.State);
            if (next != null) Send(next);
            Remember(current.Unresolved);
            // Once made, the project is the one the work goes to, under the name it was made with: never renamed here.
            if (current.ProjectId != null && current.NewProjectName != null && idea != null && idea.ExistingProjectId == null && sequence == current)
            {
                idea.ProjectMade(current.ProjectId, current.NewProjectName);
            }
            var changed = next != null || current.Started != wasStarted || current.Stopped != wasStopped;
            if (changed) KeepDrafts();
            return changed;
        }

        private string? UnknownId() => unknownOutcome.Id ?? sequence?.Unresolved;

        /// <summary>
        /// Keeps the command of this flow's own build whose outcome may be unknown on the device, so a
        /// restart still blocks a blind retry. An id kept for another start, by an earlier run, is never
        /// replaced or cleared here: only the person's Clear, then Yes, clear does that.
        /// </summary>
        private void Remember(string? commandId)
        {
            var kept = unknownOutcome.Id;
            if (kept == commandId || (kept != null && kept != ownId)) return;
            unknownOutcome.Id = commandId;
            ownId = commandId;
        }

        // ---------------------------------------------------------------------------------------------
        // Drafts kept across a restart (ADR 0025).

        /// <summary>
        /// Brings back the drafts kept for the computer whose journal is live, once: each where it was, and
        /// the agent app and model chosen again only if the computer still offers them; never while a
        /// start's outcome is unknown, which decides first.
        /// </summary>
        private void RestoreDrafts()
        {
            if (draftStore == null || !Live) return;
            var state = host.State;
            var journal = state?.Journal;
            if (state == null || journal == null || journal.Origin != JournalOrigin.Live) return;
            if (keptJournal != null)
            {
                if (keptJournal == journal.JournalId) return;
                // Another computer: its drafts are its own, and this one's stay kept for it.
                ForgetDrafts();
            }
            if (UnknownId() != null) return;
            keptJournal = journal.JournalId;
            IReadOnlyList<CreationDraft> kept;
            try
            {
                keptDrafts ??= new CreationDrafts(draftStore);
                kept = keptDrafts.For(journal.JournalId);
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                // Nothing comes back, and creating goes on; only keeping drafts across a restart failed.
                return;
            }
            if (kept.Count == 0) return;
            foreach (var each in kept.Reverse())
            {
                // Held to what the computer says now: a task that started, or may have, is not resumed.
                var restored = each.Resume(state);
                if (restored == null) continue;
                var at = restored.ProjectId ?? "";
                // What the person has in hand now wins over what was kept.
                if (at == place && idea != null && Pending(idea, sequence)) continue;
                var resumed = restored.WorkstreamToStart != null && restored.ProjectId != null
                    ? BuildSequence.Resume(draft, commands, restored.ProjectId, restored.WorkstreamToStart)
                    : null;
                if (at == place && idea != null) (idea, sequence) = (restored.Idea, resumed);
                else others[at] = (restored.Idea, resumed);
            }
            // The agent app and model come back when New project opens, never at launch: choosing one reads its models.
            keptRuntime = kept[0].RuntimeId;
            keptModel = kept[0].ModelRef;
        }

        /// <summary>The agent app kept with the draft, chosen again if the computer still offers it; its models are read only when the person opens How it runs.</summary>
        private void ChooseKeptRuntime()
        {
            var wanted = keptRuntime;
            keptRuntime = null;
            if (wanted == null || draft.Runtime != null) return;
            if (host.State?.Runtimes.FirstOrDefault(each => each.RuntimeId == wanted) is RuntimeDescriptor runtime) draft.ChooseRuntime(runtime);
        }

        /// <summary>A model kept with the draft, chosen again once the runtime lists it, and only if it runs on the computer.</summary>
        private void ChooseKeptModel()
        {
            var wanted = keptModel;
            if (wanted == null || draft.Models.Count == 0) return;
            keptModel = null;
            var model = draft.Models.FirstOrDefault(each => each.ModelRef == wanted);
            if (model != null && NewWorkDraft.RunsHere(model)) draft.ChooseModel(model);
        }

        private void ForgetDrafts()
        {
            KeepDrafts();
            others.Clear();
            idea = null;
            sequence = null;
            review = null;
            place = "";
            keptJournal = null;
            keptRuntime = null;
            keptModel = null;
        }

        /// <summary>Writes every draft of this computer to the device after a change; nothing is written when nothing changed, or in the demonstration.</summary>
        private void KeepDrafts()
        {
            if (keptJournal == null || keptDrafts == null || !Live) return;
            var now = DateTimeOffset.UtcNow;
            var current = new List<CreationDraft>();
            foreach (var pair in others)
            {
                if (CreationDraft.Of(keptJournal, pair.Key, pair.Value.Idea, pair.Value.Sequence, draft, now) is CreationDraft each) current.Add(each);
            }
            if (idea != null && CreationDraft.Of(keptJournal, place, idea, sequence, draft, now) is CreationDraft mine) current.Add(mine);
            try
            {
                keptDrafts.Keep(keptJournal, current);
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                // The draft stays in memory; only keeping it across a restart failed.
            }
        }
    }
}
