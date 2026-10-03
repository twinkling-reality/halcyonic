#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Every screen of the entry panel as a <see cref="PanelModel"/> (ADR 0023): what each shows and
    /// which actions it offers, from the draft, the overview and the Mac's state, so the XR layer only
    /// draws it and acts on what is pressed. The flows stay where they are; this decides what is shown.
    /// Nothing here sends anything: Start building leads to the whole request, and only its last part
    /// unlocks Yes, start building, which stands where Start building never did.
    /// </summary>
    public static class EntryScreens
    {
        // What each action raises, for the entry panel to act on.
        public const string Connect = "connect";
        public const string Create = "create";
        public const string ToggleProject = "toggle-project";
        public const string AddTask = "add-task";
        public const string ShowAll = "show-all";
        public const string Done = "done";
        public const string OpenWork = "open-work";
        public const string TypeIdea = "type-idea";
        public const string HoldToTalk = "hold-to-talk";
        public const string HelpMe = "help-me";
        public const string Answer = "answer";
        public const string TypeAnswer = "type-answer";
        public const string Skip = "skip";
        public const string Back = "back";
        public const string Rename = "rename";
        public const string Rewrite = "rewrite";
        public const string ChooseWhere = "choose-where";
        public const string MoreOptions = "more-options";
        public const string StartOver = "start-over";
        public const string ConfirmStartOver = "confirm-start-over";
        public const string StartBuilding = "start-building";
        public const string ChooseRuntime = "choose-runtime";
        public const string ChooseModel = "choose-model";
        public const string ChangeRuntime = "change-runtime";
        public const string ChooseFolder = "choose-folder";
        public const string ReadFolders = "read-folders";
        public const string ConfirmStart = "confirm-start";
        public const string Change = "change";
        public const string Close = "close-done";
        public const string TryAgain = "try-again";
        public const string UseThatFolder = "use-that-folder";
        public const string ChooseAnotherFolder = "choose-another-folder";
        public const string CheckFirst = "check-first";
        public const string Clear = "clear";
        public const string ConfirmClear = "confirm-clear";
        public const string Cancel = "cancel";
        public const string OpenNow = "open-now";
        public const string KeepCreating = "keep-creating";

        // Create's companion (ADR 0025).
        public const string CompanionChoice = "companion-choice";
        public const string CompanionType = "companion-type";
        public const string MakeRecap = "make-recap";
        public const string GoOnWithout = "go-on-without";
        public const string CompanionRetry = "companion-retry";
        public const string UseMyWords = "use-my-words";

        /// <summary>The first live visit: two choices, and Not now in Close's place.</summary>
        public static PanelModel Welcome()
        {
            var model = new PanelModel(EntryText.WelcomeTitle) { CloseLabel = EntryText.NotNow, CloseIcon = GlazeIcon.NotNow, Lead = EntryText.WelcomeLine, Columns = 2 };
            model.Rows.Add(new PanelRow { Card = true, Title = EntryText.ConnectProjects, Detail = EntryText.ConnectInvite, Action = Connect });
            model.Rows.Add(new PanelRow { Card = true, Title = EntryText.CreateProject, Detail = EntryText.CreateInvite, Action = Create });
            return model;
        }

        /// <summary>
        /// Every project the Mac's journal knows: pressing one shows or hides its work on the stage, Add
        /// a task starts new work in it while connected. Its counts include hidden work, so what needs
        /// the person is never hidden with its project.
        /// </summary>
        public static PanelModel ConnectProjects(WorkOverview? overview, bool connected, bool demonstration)
        {
            var live = connected && !demonstration;
            var model = new PanelModel(EntryText.ConnectProjects)
            {
                Lead = demonstration ? EntryText.ExampleProjects : connected ? EntryText.ConnectLine : EntryText.ConnectLine + " " + EntryText.LastKnownProjects,
                Columns = 2,
            };
            var done = new PanelAction(Done, EntryText.Done, PanelActionRole.Primary);
            if (overview == null || overview.Projects.Count == 0)
            {
                model.Rows.Add(Line(overview == null ? EntryText.WaitingForMac : EntryText.NoProjects));
                if (live && overview != null) model.Rows.Add(new PanelRow { Card = true, Title = EntryText.CreateProject, Detail = EntryText.CreateInvite, Action = Create });
                model.Actions = new ActionSet(done);
                return model;
            }
            foreach (var project in overview.Projects)
            {
                model.Rows.Add(new PanelRow
                {
                    Filter = true,
                    Chosen = project.Shown,
                    Title = project.Name,
                    TitleIsData = true,
                    Detail = EntryText.ProjectDetail(project),
                    ShortDetail = EntryText.ChipDetail(project),
                    DetailTone = project.NeedsYou > 0 ? GlazeTone.Attention : (GlazeTone?)null,
                    Action = ToggleProject,
                    Key = project.ProjectId,
                    Side = live ? new PanelAction(AddTask, EntryText.AddTask, PanelActionRole.Secondary, icon: GlazeIcon.AddTask) : null,
                });
            }
            model.Actions = new ActionSet(
                overview.Projects.All(project => project.Shown) ? null : new PanelAction(ShowAll, EntryText.ShowAll, PanelActionRole.Secondary, icon: GlazeIcon.ShowAll),
                done);
            return model;
        }

        /// <summary>Every task without a character, what needs the person first; pressing one brings it to the stage and opens it.</summary>
        public static PanelModel MoreTasks(WorkOverview? overview, bool connected)
        {
            var model = new PanelModel(EntryText.MoreTasks) { Lead = EntryText.MoreTasksLine, Columns = 2 };
            if (overview == null || overview.OffStage.Count == 0)
            {
                model.Rows.Add(Line(overview == null ? EntryText.WaitingForMac : EntryText.AllOnStage));
                return model;
            }
            foreach (var item in overview.OffStage)
            {
                model.Rows.Add(new PanelRow
                {
                    Title = LabelText.Plain(item.Workstream.Title),
                    TitleIsData = true,
                    Detail = EntryText.OffStageDetail(item, connected),
                    DetailLines = 2,
                    DetailTone = CharacterLineup.TierOf(item.Workstream) == LineupTier.NeedsYou ? GlazeTone.Attention : (GlazeTone?)null,
                    Action = OpenWork,
                    Key = item.Workstream.WorkstreamId,
                });
            }
            return model;
        }

        /// <summary>
        /// Create a project, or a task in a known one: type the idea, or hold to talk beside it, or the
        /// fixed questions. <paramref name="said"/> is a line for this screen only, such as hold to
        /// talk's words, shown under the field.
        /// </summary>
        /// <param name="voice">Hold to talk is offered: in development builds, while connected, never in the demonstration.</param>
        /// <param name="companion">
        /// Whether the companion can be asked, as the Mac said when Create opened (ADR 0025); null when
        /// not read, as in the demonstration. Help me figure it out says which helper it opens, and when
        /// the companion can't run, one line says why.
        /// </param>
        public static PanelModel CreateStart(ProjectIdea idea, bool voice, string? said, CompanionStatus? companion = null)
        {
            var existing = idea.ExistingProjectId != null;
            var model = new PanelModel(EntryText.CreateTitle(existing ? idea.Name : null))
            {
                TitleIsData = existing,
                Context = existing ? EntryText.WorkPrompt : EntryText.IdeaPrompt,
                Lead = EntryText.NothingStartsYet,
            };
            model.Rows.Add(new PanelRow
            {
                Title = EntryText.TypeIdea,
                Detail = EntryText.TypeIdeaInvite,
                Action = TypeIdea,
                Side = voice ? new PanelAction(HoldToTalk, VoiceText.HoldToTalk, PanelActionRole.Secondary, holds: true, icon: GlazeIcon.HoldToTalk) : null,
            });
            if (said != null) model.Rows.Add(Line(said, PanelTextSize.Caption, lines: 2));
            var talk = !existing && companion is AvailableCompanion;
            model.Rows.Add(new PanelRow { Title = EntryText.HelpMe, Detail = talk ? CompanionText.TalkItThrough : EntryText.HelpMeInvite, Action = HelpMe });
            if (!existing && said == null && companion is UnavailableCompanion unavailable)
            {
                model.Rows.Add(Line(CompanionText.Unavailable(unavailable.Reason?.Code), PanelTextSize.Caption, lines: 2));
            }
            return model;
        }

        /// <summary>
        /// The exchange with the companion (ADR 0025): its latest line, quoted and tagged as its own, its
        /// view of the idea as its opinion under the title, then its question with up to four choices,
        /// and Type my answer with hold to talk beside it. While a reply is on its way, only that it is
        /// waiting, and, once the wait is long, that the computer's model may be busy with a task; after
        /// a failure, why and Try again. Back, Go on without it and Make the recap keep their places on
        /// every one of these screens. Nothing here is sent anywhere but to the companion.
        /// </summary>
        /// <param name="voice">Hold to talk is offered: development builds, connected, never in the demonstration.</param>
        /// <param name="said">A line for this screen only, such as hold to talk's words or why an answer was refused.</param>
        /// <param name="waitedSeconds">How long the reply has been on its way.</param>
        /// <param name="recording">
        /// The demonstration plays this recorded exchange: said so, only the recorded answer can be
        /// pressed, and Make the recap only where the recording asked for it.
        /// </param>
        public static PanelModel Companion(ProjectIdea idea, bool voice, string? said, double waitedSeconds, CompanionRecording? recording = null)
        {
            var recorded = recording != null;
            var exchange = idea.Companion ?? throw new System.ArgumentException("The idea has no exchange with the companion.", nameof(idea));
            var model = new PanelModel(EntryText.HelpMe)
            {
                Lead = recorded ? CompanionText.Recorded : CompanionText.Note,
                LeadTone = recorded ? GlazeTone.Simulated : (GlazeTone?)null,
                Columns = 2,
            };
            var goOn = new PanelAction(GoOnWithout, CompanionText.GoOnWithout, PanelActionRole.Secondary);
            var back = new PanelAction(Back, EntryText.Back, PanelActionRole.Back, icon: GlazeIcon.Back);
            if (exchange.Waiting)
            {
                model.Rows.Add(Line(CompanionText.Waiting, PanelTextSize.Title, lines: 1));
                if (waitedSeconds >= CompanionText.WaitingLongSeconds) model.Rows.Add(Line(CompanionText.WaitingLong, PanelTextSize.Body, lines: 2));
                model.Actions = new ActionSet(back, goOn,
                    new PanelAction(MakeRecap, CompanionText.MakeTheRecap, PanelActionRole.Primary, available: false, reason: CompanionText.Waiting));
                return model;
            }
            if (exchange.Failure != null)
            {
                model.Rows.Add(Line(CompanionText.Failure(exchange.Failure) ?? CompanionText.CouldNotAsk, PanelTextSize.Body, lines: 3, tone: GlazeTone.Failure));
                model.Actions = new ActionSet(back, goOn,
                    new PanelAction(CompanionRetry, EntryText.TryAgain, PanelActionRole.Primary, icon: GlazeIcon.Refresh));
                return model;
            }
            if (exchange.Latest is AskReply ask)
            {
                model.Context = CompanionText.View(ask.View);
                model.Rows.Add(new PanelRow { Line = true, Title = CompanionText.Says(ask.Line), TitleIsData = true, TitleLines = 3, Claim = true });
                model.Rows.Add(new PanelRow
                {
                    Line = true, Title = LabelText.Plain(ask.Question.Text), TitleIsData = true, TitleLines = 2, Size = PanelTextSize.Title, Claim = true,
                });
                var choices = ask.Question.Choices ?? new List<string>();
                var recordedAnswer = recording?.RecordedAnswer(exchange);
                for (var index = 0; index < choices.Count; index++)
                {
                    var pressable = recorded ? choices[index] == recordedAnswer : exchange.CanSay;
                    model.Rows.Add(new PanelRow
                    {
                        Title = LabelText.Plain(choices[index]),
                        TitleIsData = true,
                        TitleLines = 2,
                        Action = pressable ? CompanionChoice : null,
                        Available = pressable,
                        Key = index.ToString(CultureInfo.InvariantCulture),
                    });
                }
                if (!recorded)
                {
                    model.Rows.Add(new PanelRow
                    {
                        Title = CompanionText.TypeAnswer,
                        Action = exchange.CanSay ? CompanionType : null,
                        Available = exchange.CanSay,
                        Side = voice && exchange.CanSay
                            ? new PanelAction(HoldToTalk, VoiceText.HoldToTalk, PanelActionRole.Secondary, holds: true, icon: GlazeIcon.HoldToTalk)
                            : null,
                    });
                }
                if (said != null) model.Rows.Add(Line(said, PanelTextSize.Caption, lines: 2));
                if (!exchange.CanSay && !recorded) model.Rows.Add(Line(CompanionText.Full, PanelTextSize.Caption, lines: 2));
            }
            var canRecap = recording != null ? recording.RecapHere(exchange) : exchange.CanAskForRecap;
            model.Actions = new ActionSet(back, recorded ? null : goOn,
                new PanelAction(MakeRecap, CompanionText.MakeTheRecap, PanelActionRole.Primary, available: canRecap,
                    reason: canRecap ? null : recorded ? null : CompanionText.Waiting));
            return model;
        }

        /// <summary>
        /// What the recap says first when the companion has just proposed it: its view, when it found
        /// the idea unclear or not buildable, then its line, quoted and tagged as its own.
        /// </summary>
        public static string Proposed(ProposeReply reply)
        {
            var view = CompanionText.View(reply.View);
            return (view == null ? "" : view + " ") + CompanionText.Says(reply.Line);
        }

        /// <summary>
        /// One fixed question: the answers offered, the one given before marked Chosen, typing one's own,
        /// skipping where it can be skipped, and Back. Said to be fixed questions, not an AI.
        /// </summary>
        public static PanelModel Guide(ProjectIdea idea)
        {
            var question = ProjectIdea.Questions[idea.Question];
            var model = new PanelModel(EntryText.HelpMe)
            {
                Context = EntryText.Question(idea.Question, ProjectIdea.Questions.Count),
                Lead = EntryText.GuideNote,
                Columns = 2,
            };
            model.Rows.Add(Line(question.Prompt, PanelTextSize.Title, lines: 1));
            var answered = idea.AnswerTo(idea.Question);
            foreach (var choice in idea.Choices)
            {
                var chosen = answered == choice;
                model.Rows.Add(new PanelRow { Title = choice, Detail = chosen ? EntryText.Chosen : null, Chosen = chosen, Action = Answer, Key = choice });
            }
            model.Actions = new ActionSet(
                new PanelAction(Back, EntryText.Back, PanelActionRole.Back, icon: GlazeIcon.Back),
                question.SkipLabel == null ? null : new PanelAction(Skip, question.SkipLabel, PanelActionRole.Secondary),
                new PanelAction(TypeAnswer, question.TypeLabel, PanelActionRole.Secondary, icon: GlazeIcon.Type));
            return model;
        }

        /// <summary>
        /// The editable recap, one fact a cell, each changed by pressing it: the project's name, its
        /// first task, where its files live and how it runs. Start building stands at the bar's right
        /// end, and while something is missing it stays there, unavailable, saying what. Start over,
        /// at the left, is confirmed in place.
        /// </summary>
        /// <param name="currentFolder">An existing project's folder as its Mac bound it, or null.</param>
        /// <param name="notice">A line for the recap only, such as what the Mac heard, shown before anything else.</param>
        /// <param name="problem">Why Start building can't go ahead now (<see cref="StartProblem"/>), or null.</param>
        /// <param name="confirmingStartOver">Start over was pressed once: the bar asks to confirm it.</param>
        /// <param name="compact">
        /// A banner takes room at the top: each fact in one line and how it runs without its note, so
        /// every fact still shows at once. The whole request shows each whole before anything is sent.
        /// </param>
        public static PanelModel Recap(ProjectIdea idea, NewWorkDraft draft, ProjectLocation? currentFolder, bool live, string? notice, string? problem,
            bool confirmingStartOver = false, bool compact = false)
        {
            var lines = compact ? 1 : 2;
            var existing = idea.ExistingProjectId != null;
            var model = new PanelModel(existing ? EntryText.WorkRecapTitle : EntryText.RecapTitle)
            {
                // Moving a project changes where all its later work runs, so the recap says so before the review.
                Lead = notice ?? (idea.Folder != null && existing ? EntryText.RebindWarning : EntryText.RecapLine),
                Columns = 2,
            };
            var named = idea.Name.Length > 0;
            model.Rows.Add(new PanelRow
            {
                Overline = EntryText.ProjectName,
                Title = named ? LabelText.Plain(idea.Name) : EntryText.NotNamedYet,
                TitleIsData = named,
                TitleLines = lines,
                Detail = idea.NameSuggested && !compact ? CompanionText.Suggested : null,
                Action = existing ? null : Rename,
                End = existing ? null : EntryText.Change,
            });
            model.Rows.Add(new PanelRow
            {
                Overline = EntryText.FirstTask,
                Title = LabelText.Plain(idea.FirstTask),
                TitleIsData = true,
                TitleLines = lines,
                Detail = idea.TaskSuggested && !compact ? CompanionText.Suggested : null,
                // The person's own words, one press away, beside the suggestion they replace; the bar keeps its places.
                Side = idea.TaskSuggested && idea.OwnWords != null ? new PanelAction(UseMyWords, CompanionText.UseMyWords, PanelActionRole.Secondary) : null,
                Action = Rewrite,
                End = EntryText.Change,
            });
            var nothingChosen = idea.Folder == null && currentFolder == null;
            model.Rows.Add(new PanelRow
            {
                Overline = EntryText.FolderTitle,
                Title = EntryText.FolderFact(currentFolder, idea.Folder, draft.Runtime?.UsesProjectLocation == true),
                TitleIsData = !nothingChosen,
                TitleLines = lines,
                Action = ChooseWhere,
                End = nothingChosen ? EntryText.ChooseFolder : EntryText.Change,
            });
            var runtime = draft.Runtime;
            model.Rows.Add(new PanelRow
            {
                Overline = EntryText.HowItRuns,
                Title = EntryText.RunsWith(draft, live),
                // A runtime's own name, where it is shown, comes from outside.
                TitleIsData = runtime != null && (!runtime.Synthetic || !live) && (runtime.Synthetic || runtime.ModelChoice != ModelChoice.Listed),
                Detail = compact ? null : EntryText.ModelLine(draft),
                DetailLines = 2,
                Action = MoreOptions,
                End = EntryText.MoreOptions,
            });
            if (confirmingStartOver)
            {
                model.Confirm = new ConfirmStep(EntryText.StartOverQuestion,
                    new PanelAction(ConfirmStartOver, EntryText.ConfirmStartOver, PanelActionRole.Destructive, icon: GlazeIcon.StartOver),
                    new PanelAction(Cancel, EntryText.Cancel, PanelActionRole.Secondary, icon: GlazeIcon.Close));
                return model;
            }
            model.Actions = new ActionSet(
                new PanelAction(StartOver, EntryText.StartOver, PanelActionRole.Destructive, icon: GlazeIcon.StartOver),
                new PanelAction(StartBuilding, EntryText.StartBuilding, PanelActionRole.Primary, available: problem == null, reason: problem,
                    icon: GlazeIcon.StartBuilding));
            return model;
        }

        /// <summary>
        /// Why Start building can't go ahead now, or null: the demonstration, no Mac, the idea itself,
        /// nothing chosen to run it, a choice no longer there, no model, the project gone, no folder,
        /// checked in that order.
        /// </summary>
        public static string? StartProblem(bool demonstration, ClientProjection? state, bool connected, ProjectIdea? idea, NewWorkDraft draft,
            ProjectLocation? currentFolder)
        {
            if (demonstration) return EntryText.DemoCannotStart;
            if (state == null || !connected) return EntryText.WaitingForMac;
            if (idea?.Problem is string ideaProblem) return ideaProblem;
            var runtime = draft.Runtime;
            if (runtime == null) return EntryText.ChooseHowItRuns;
            if (!state.Runtimes.Any(each => each.RuntimeId == runtime.RuntimeId)) return EntryText.ChooseAgain;
            if (runtime.ModelChoice == ModelChoice.Listed && draft.Model == null) return EntryText.FinishChoosing;
            if (idea?.ExistingProjectId != null && !state.Projects.ContainsKey(idea.ExistingProjectId)) return EntryText.ProjectGone;
            if (runtime.UsesProjectLocation && idea?.Folder == null && currentFolder == null) return EntryText.ChooseWhereFilesLive;
            return null;
        }

        /// <summary>
        /// The agent apps that can start work, or the chosen one's own models with where each runs.
        /// Choosing is all this does; nothing but a model on the Mac is chosen for the person, and that
        /// says so. Models that run elsewhere follow a line saying what that means, and take a second press.
        /// </summary>
        public static PanelModel Options(NewWorkDraft draft, IEnumerable<RuntimeDescriptor> runtimes, bool showModels, bool live)
        {
            var model = new PanelModel(EntryText.OptionsTitle) { Lead = EntryText.OptionsLine, Columns = 2 };
            var done = new PanelAction(Done, EntryText.Done, PanelActionRole.Primary);
            if (showModels && draft.Runtime?.ModelChoice == ModelChoice.Listed)
            {
                if (draft.Models.Count == 0) model.Rows.Add(Line(draft.ModelProblem ?? EntryText.NoModels));
                var elsewhere = draft.Elsewhere;
                for (var index = 0; index < draft.Models.Count; index++)
                {
                    if (index == elsewhere) model.Rows.Add(Line(EntryText.ElsewhereDivider(draft.Models.Skip(elsewhere)), PanelTextSize.Caption, lines: 1));
                    var each = draft.Models[index];
                    var chosen = draft.Model?.ModelRef == each.ModelRef;
                    var pending = draft.PendingModel == each;
                    model.Rows.Add(new PanelRow
                    {
                        Title = LabelText.Plain(each.DisplayName),
                        TitleIsData = true,
                        // A model that runs elsewhere is chosen only by a second press, after it says where it runs.
                        Detail = pending ? EntryText.ConfirmElsewhere(each)
                            : (chosen ? (draft.ModelPreselected ? EntryText.ChosenForYou : EntryText.Chosen) + " · " : "")
                                + EntryText.ServedShort(each.Served) + " · " + EntryText.Tools(each.ToolCalling),
                        DetailLines = pending ? 3 : 2,
                        Chosen = chosen,
                        Action = ChooseModel,
                        Key = each.ModelRef,
                    });
                }
                model.Actions = new ActionSet(new PanelAction(ChangeRuntime, EntryText.ChangeAgentApp, PanelActionRole.Secondary, icon: GlazeIcon.Change), done);
                return model;
            }
            // Simulated runtimes after the real ones, named for what they do in a live session.
            var choices = EntryText.RuntimeChoices(runtimes);
            if (choices.Count == 0) model.Rows.Add(Line(EntryText.NoRuntimes));
            foreach (var runtime in choices)
            {
                var chosen = draft.Runtime?.RuntimeId == runtime.RuntimeId;
                var detail = runtime.Synthetic ? (live ? EntryText.PracticeDetail : EntryText.Practice)
                    : runtime.ModelChoice == ModelChoice.Listed ? EntryText.ListsModels : EntryText.ChoosesModel;
                model.Rows.Add(new PanelRow
                {
                    Title = EntryText.RuntimeName(runtime, live),
                    TitleIsData = !(runtime.Synthetic && live),
                    Detail = (chosen ? EntryText.Chosen + " · " : "") + detail,
                    Chosen = chosen,
                    Action = ChooseRuntime,
                    Key = runtime.RuntimeId,
                });
            }
            model.Actions = new ActionSet(done);
            return model;
        }

        /// <summary>
        /// Where its files live: what the Mac lists, place by place, a new folder, the place itself and
        /// each folder in it; a place not on the Mac now shows and takes no press. Choosing returns to
        /// the recap; nothing is sent. <paramref name="notice"/> is a line for this screen only, such as
        /// why a new folder's name was refused.
        /// </summary>
        /// <param name="problem">Why the folders couldn't be read, as it arrived, or null while reading.</param>
        public static PanelModel Folder(ProjectIdea idea, LocationsResponse? locations, string? problem, string? notice)
        {
            var model = new PanelModel(EntryText.FolderTitle) { Columns = 2 };
            model.Lead = notice ?? (locations?.Roots.Any(root => root.FoldersTruncated) == true ? EntryText.FolderLine + " " + EntryText.FoldersCut : EntryText.FolderLine);
            if (notice != null) model.LeadTone = GlazeTone.Failure;
            var back = new PanelAction(Back, EntryText.Back, PanelActionRole.Back, icon: GlazeIcon.Back);
            var tryAgain = new PanelAction(ReadFolders, EntryText.TryAgain, PanelActionRole.Primary, icon: GlazeIcon.Refresh);
            if (locations == null)
            {
                model.Rows.Add(Line(problem == null ? EntryText.ReadingFolders : EntryText.FoldersUnread(problem)));
                model.Actions = new ActionSet(back, problem == null ? null : tryAgain);
                return model;
            }
            if (locations.Roots.Count == 0)
            {
                model.Rows.Add(Line(EntryText.NoFolders));
                model.Actions = new ActionSet(back, tryAgain);
                return model;
            }
            var options = ProjectFolder.Options(locations);
            var current = idea.Folder;
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                var chosen = current != null && !current.IsNew && current.RootPath == option.Root.Path
                    && (option.Kind == FolderOptionKind.Root ? current.FolderName == null
                        : option.Kind == FolderOptionKind.Folder && current.FolderName == option.Folder!.Name);
                model.Rows.Add(new PanelRow
                {
                    Title = option.Label,
                    TitleIsData = true,
                    Detail = (chosen ? EntryText.Chosen + " · " : "") + option.Detail,
                    Chosen = chosen,
                    Available = option.Choosable,
                    Action = ChooseFolder,
                    Key = index.ToString(CultureInfo.InvariantCulture),
                });
            }
            model.Actions = new ActionSet(back);
            return model;
        }

        /// <summary>
        /// The whole request as it will be sent, from the draft: names and the person's words reach the
        /// review as they are, so it spells each once; a project that exists and moves shows its folder
        /// now and from now on.
        /// </summary>
        public static NewWorkReview ReviewOf(ProjectIdea idea, NewWorkDraft draft, ProjectLocation? currentFolder, bool live)
        {
            // Reviewing changes nothing: the draft takes the first task only when its Yes is pressed.
            var model = draft.Model;
            // The review spells what Halcyonic did not write by its code points, so it is given those names as they are.
            var folder = idea.Folder?.Describe(name => name) ?? currentFolder?.Name ?? "none";
            var before = idea.Folder != null && idea.ExistingProjectId != null ? currentFolder?.Name ?? "none" : null;
            var (title, titleCut) = NewWorkDraft.TitleSourceOf(idea.FirstTask);
            return new NewWorkReview(
                idea.Name,
                title,
                EntryText.RuntimeName(draft.Runtime!, live, plain: false),
                model?.DisplayName ?? "chosen by the agent app",
                model == null ? "the agent app chooses" : EntryText.ServedInSentence(model.Served) + "; " + EntryText.Tools(model.ToolCalling),
                model?.ModelRef ?? "none",
                idea.FirstTask,
                folder,
                before,
                titleCut,
                idea.Folder?.ToContract());
        }

        /// <summary>
        /// The whole request, a part at a time, drawn by the panel in the space the frame leaves. Yes,
        /// start building stands left of Change, never where Start building stood, and until the last
        /// part it stays in its place, locked, saying what is left to read.
        /// </summary>
        /// <param name="problem">Why it can't start now even on the last part, such as the Mac gone, or null.</param>
        public static PanelModel Review(NewWorkReview review, string? problem)
        {
            var model = new PanelModel(EntryText.ReviewTitle)
            {
                Context = EntryText.ReviewLine,
                CustomBody = true,
                Parts = (review.Page, review.PageCount),
            };
            var yes = review.CanConfirm
                ? new PanelAction(ConfirmStart, EntryText.ConfirmStart, PanelActionRole.Primary, available: problem == null, reason: problem,
                    icon: GlazeIcon.StartBuilding)
                : new PanelAction(ConfirmStart, EntryText.ReadToPart(review.PageCount), PanelActionRole.Primary, available: false, icon: GlazeIcon.Locked);
            model.Confirm = new ConfirmStep(null, yes, new PanelAction(Change, EntryText.Change, PanelActionRole.Secondary, icon: GlazeIcon.Change));
            return model;
        }

        /// <summary>
        /// Each step of Start building and how it went, in words: sent is not done, and a step is
        /// confirmed only by its completed record. A refusal offers Try again or Change, one about a
        /// folder the action its code names; an unknown outcome offers only checking the work first.
        /// </summary>
        /// <param name="chosenFolder">Where the person chose the files live, for Use that folder after the Mac says it exists.</param>
        public static PanelModel Sending(BuildSequence sequence, ProjectFolder? chosenFolder)
        {
            var model = new PanelModel(EntryText.SendingTitle) { Lead = EntryText.SendingLine };
            var newProject = sequence.Steps[0].Kind == BuildStepKind.CreateProject;
            foreach (var step in sequence.Steps)
            {
                model.Rows.Add(new PanelRow
                {
                    Title = EntryText.StepName(step.Kind, newProject),
                    Detail = EntryText.StepStatus(step),
                    DetailLines = 3,
                    DetailTone = ToneOf(step),
                });
            }
            if (sequence.Started)
            {
                model.Rows.Add(Line(EntryText.Started, tone: GlazeTone.Success));
                model.Actions = new ActionSet(new PanelAction(Close, EntryText.Close, PanelActionRole.Primary, icon: GlazeIcon.Close));
            }
            else if (sequence.CanRetry && sequence.StoppedAt is BuildStep stopped && EntryText.AboutFolder(stopped))
            {
                // The next action comes from the refusal's code. Either way the request is reviewed again before it is sent.
                var taken = stopped.Refusal == RejectionCode.LocationExists ? chosenFolder : null;
                model.Actions = new ActionSet(
                    new PanelAction(ChooseAnotherFolder, EntryText.ChooseAnotherFolder, PanelActionRole.Secondary),
                    taken != null && taken.IsNew
                        ? new PanelAction(UseThatFolder, EntryText.UseThatFolder, PanelActionRole.Primary)
                        : new PanelAction(TryAgain, EntryText.TryAgain, PanelActionRole.Primary, icon: GlazeIcon.Refresh));
            }
            else if (sequence.CanRetry)
            {
                model.Actions = new ActionSet(
                    new PanelAction(Change, EntryText.Change, PanelActionRole.Back, icon: GlazeIcon.Change),
                    new PanelAction(TryAgain, EntryText.TryAgain, PanelActionRole.Primary, icon: GlazeIcon.Refresh));
            }
            else if (sequence.Stopped || sequence.Steps.Any(step => step.Status == BuildStepStatus.Unknown))
            {
                model.Actions = new ActionSet(new PanelAction(CheckFirst, EntryText.CheckFirst, PanelActionRole.Primary, icon: GlazeIcon.Next));
            }
            return model;
        }

        /// <summary>
        /// A start that may have run: its reference, the Mac's record of it when one arrives, and how to
        /// clear it, by two separate presses: Clear, then Yes, clear, which stands where Clear did not.
        /// Clearing starts a blank draft, never a retry. Nothing is offered unless connected.
        /// </summary>
        public static PanelModel Previous(string commandId, CommandView? record, bool armed, bool live)
        {
            var model = new PanelModel(EntryText.PreviousRequestTitle) { Lead = EntryText.PreviousRequestLine };
            model.Rows.Add(Line(EntryText.Recorded(record?.Status)));
            model.Rows.Add(Line(armed ? EntryText.ClearOnlyAfterChecking : EntryText.ClearOnceChecked));
            model.Rows.Add(Line(EntryText.Reference(commandId), PanelTextSize.Caption, lines: 1));
            if (!live) return model;
            if (armed)
            {
                model.Confirm = new ConfirmStep(null,
                    new PanelAction(ConfirmClear, EntryText.ConfirmClear, PanelActionRole.Primary),
                    new PanelAction(Cancel, EntryText.Cancel, PanelActionRole.Secondary, icon: GlazeIcon.Close));
            }
            else model.Actions = new ActionSet(new PanelAction(Clear, EntryText.Clear, PanelActionRole.Primary));
            return model;
        }

        /// <summary>Work that came to wait for the person while they create: Open now, or Keep creating. It never switches by itself.</summary>
        public static PanelBanner WaitingBanner(WorkstreamView work) =>
            new PanelBanner(EntryText.WaitingNow(LabelText.Plain(work.Title)), GlazeTone.Attention,
                new PanelAction(OpenNow, EntryText.OpenNow, PanelActionRole.Attention, icon: GlazeIcon.OpenNow),
                new PanelAction(KeepCreating, EntryText.KeepCreating, PanelActionRole.Secondary, icon: GlazeIcon.KeepCreating));

        private static GlazeTone? ToneOf(BuildStep step) => step.Status switch
        {
            BuildStepStatus.Waiting => GlazeTone.Active,
            BuildStepStatus.Confirmed => GlazeTone.Success,
            BuildStepStatus.Refused or BuildStepStatus.NotSent => GlazeTone.Failure,
            BuildStepStatus.Failed => step.EffectUnknown ? GlazeTone.Unknown : GlazeTone.Failure,
            BuildStepStatus.Unknown or BuildStepStatus.Unexpected => GlazeTone.Unknown,
            _ => null,
        };

        private static PanelRow Line(string text, PanelTextSize size = PanelTextSize.Body, int lines = 3, GlazeTone? tone = null) =>
            new PanelRow { Line = true, Title = text, Size = size, TitleLines = lines, Tone = tone };
    }
}
