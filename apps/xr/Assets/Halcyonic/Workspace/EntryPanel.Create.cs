#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Create a project, and Add work to a known one. The person types an idea in their own words or
    /// answers a few fixed questions (<see cref="ProjectIdea"/>); both lead to the same editable recap:
    /// the project's name, its first task, where its files live, and how it runs. Where its files
    /// live is chosen from the folders the Mac lists (<c>GET /api/locations</c>, read when the person
    /// opens the choice), asked only when the runtime works in a project folder, and an existing
    /// project keeps its own unless the person moves it, which the review shows as now and from now on. More options chooses
    /// the runtime and, from its own list, the model, with where each model runs; nothing is chosen for
    /// the person. Start building shows the whole request in pages (<see cref="NewWorkReview"/>), and
    /// only its last part offers Yes, start building, in a place where no button was. Then
    /// <see cref="BuildSequence"/> sends the ordinary commands one at a time, and every step says how
    /// it went; the character that appears reads Starting until the runtime confirms.
    /// </summary>
    /// <remarks>
    /// The draft lives in memory while the app runs, one for a new project and one for each project
    /// work is added to: closing the panel, opening a character, Open now, or starting work elsewhere
    /// keeps it exactly, with how far its start got, and the rail offers Continue creating. An app
    /// restart loses it. A request
    /// whose outcome is unknown keeps its command id on the device and blocks another start, even
    /// after a restart, until the person checks the work and clears it with two separate presses.
    /// A refusal about a folder offers its next action from the refusal's code: Use that folder after
    /// <c>location_exists</c>, Choose where its files live after the others; a project already made is
    /// then moved to the new folder before its work is started again.
    /// </remarks>
    public sealed partial class EntryPanel
    {
        private const string UnresolvedCommandPreference = "halcyonic.new-work.unresolved-command-id";
        private const float ReviewSize = WorkspaceVisuals.DetailSize;

        /// <summary>Where the whole request's pages end, above the row that turns them.</summary>
        private const float ReviewBottom = -0.15f;

        /// <summary>At most one item a line, so a page never needs more labels than it has lines.</summary>
        private const int ReviewLabels = 14;
        private const float ChangeWidth = 0.14f;

        private readonly List<TextMeshPro> reviewLabels = new List<TextMeshPro>();

        private readonly Dictionary<string, (ProjectIdea Idea, BuildSequence? Sequence, ProjectFolder? Sent)> drafts =
            new Dictionary<string, (ProjectIdea, BuildSequence?, ProjectFolder?)>();
        private string draftKey = "";
        private NewWorkDraft draft = null!;
        private ProjectIdea? idea;
        private NewWorkReview? review;
        private BuildSequence? sequence;
        private Task<CommandAckMessage>? pendingAck;
        private string? unresolved;
        private bool recoveryArmed;
        private bool showModels;
        private string? notice;
        private string? shownProject;
        private ProjectFolder? sentFolder;
        private bool rendering;
        private LocationsResponse? locations;
        private string? locationsProblem;
        private Task<LocationsResponse>? locationsRead;
        private CancellationTokenSource? locationsCancellation;
        private CancellationTokenSource? modelCancellation;
        private Task<RuntimeModelsResponse>? modelRead;
        private string? modelRuntimeId;
        private TextMeshPro reviewMeasure = null!;
        private NewWorkReview? paginated;
        private List<int> reviewItemLines = new List<int>();

        /// <summary>Each item as laid out at the panel's width: its characters and where each of its lines starts.</summary>
        private List<(string Characters, int[] LineStarts)> reviewItemLayout = new List<(string, int[])>();
        private float reviewLine;
        private TextMeshPro recapProject = null!;
        private TextMeshPro recapTask = null!;
        private TextMeshPro recapLocation = null!;
        private TextMeshPro recapRuns = null!;
        private TextMeshPro recapServed = null!;
        private Slot changeName = null!;
        private Slot changeTask = null!;
        private Slot moreOptions = null!;
        private Slot changeFolder = null!;

        /// <summary>A creation draft waits: the rail offers Continue creating.</summary>
        public bool HasDraft => (idea != null && (idea.HasRecap || idea.Guided) && sequence?.Started != true)
            || drafts.Values.Any(other => other.Idea != idea && (other.Idea.HasRecap || other.Idea.Guided) && other.Sequence?.Started != true);

        private void AwakeCreate()
        {
            draft = new NewWorkDraft(commands);
            unresolved = PlayerPrefs.GetString(UnresolvedCommandPreference, "");
            if (unresolved.Length == 0) unresolved = null;
            for (var index = 0; index < ReviewLabels; index++)
            {
                var label = Label("Request item " + index, ReviewSize, WorkspaceVisuals.TextColor, wrap: true);
                label.overflowMode = TextOverflowModes.Overflow;
                reviewLabels.Add(label);
            }
            // Never shown: it lays each item out at the panel's width to count its lines.
            reviewMeasure = WorkspaceVisuals.Text(root, "Request measure", ReviewSize, WorkspaceVisuals.TextColor,
                new Vector2(ContentWidth, 1f), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            reviewMeasure.overflowMode = TextOverflowModes.Overflow;
            reviewMeasure.gameObject.SetActive(false);
            recapProject = Label("Project", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, wrap: false);
            recapTask = Label("First task", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, wrap: true);
            recapLocation = Label("Where its files live", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, wrap: true);
            recapRuns = Label("Runs with", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, wrap: false);
            recapServed = Label("Model", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, wrap: true);
            changeName = MakeSlot("Change name", RowHeight * 0.8f, WorkspaceVisuals.DetailSize);
            changeTask = MakeSlot("Change task", RowHeight * 0.8f, WorkspaceVisuals.DetailSize);
            moreOptions = MakeSlot("More options", RowHeight * 0.8f, WorkspaceVisuals.DetailSize);
            changeFolder = MakeSlot("Change folder", RowHeight * 0.8f, WorkspaceVisuals.DetailSize);
        }

        private void DestroyCreate()
        {
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            locationsCancellation?.Cancel();
            locationsCancellation?.Dispose();
        }

        /// <summary>
        /// Opens Create a project, or Add work to <paramref name="projectId"/>: the draft for that place
        /// where it stands, or a new one; without a project, the draft last worked on. A request whose
        /// outcome is unknown comes first.
        /// </summary>
        public void ShowCreate(string? projectId, string? projectName)
        {
            if (unresolved != null && sequence == null)
            {
                Open(Screen.Previous);
                return;
            }
            if (sequence != null && !sequence.Started && (sequence.Current != null || sequence.Unresolved != null))
            {
                Open(Screen.Sending);
                return;
            }
            var key = projectId == null && idea != null ? draftKey : projectId ?? "";
            if (idea == null || key != draftKey)
            {
                // Each place keeps its own draft, with how far its start got, so nothing is made twice.
                if (idea != null) drafts[draftKey] = (idea, sequence, sentFolder);
                draftKey = key;
                (idea, sequence, sentFolder) = drafts.TryGetValue(key, out var kept) ? kept : (new ProjectIdea(projectId, projectName), null, null);
                review = null;
                notice = null;
            }
            if (sequence?.Started == true)
            {
                idea = new ProjectIdea(idea!.ExistingProjectId, idea.ExistingProjectId == null ? null : idea.Name);
                sequence = null;
            }
            draft.ProjectId = idea!.ExistingProjectId;
            var now = state();
            if (now != null) watch.Begin(now);
            Open(idea!.HasRecap ? Screen.Recap : idea.Guided ? Screen.Guide : Screen.CreateStart);
        }

        private string CreateTitle() => screen switch
        {
            Screen.CreateStart => EntryText.CreateTitle(idea?.ExistingProjectId == null ? null : idea.Name),
            Screen.Guide => EntryText.HelpMe,
            Screen.Recap => idea?.ExistingProjectId == null ? EntryText.RecapTitle : EntryText.WorkRecapTitle,
            Screen.Options => EntryText.OptionsTitle,
            Screen.Folder => EntryText.FolderTitle,
            Screen.Review => "Check before starting",
            Screen.Sending => EntryText.SendingTitle,
            _ => EntryText.PreviousRequestTitle,
        };

        private void LayoutCreate()
        {
            if (idea == null && screen != Screen.Previous)
            {
                screen = Screen.CreateStart;
                idea = new ProjectIdea();
            }
            var banner = screen != Screen.Previous && Banner();
            switch (screen)
            {
                case Screen.CreateStart:
                    if (!banner) SayLine(EntryText.NothingStartsYet);
                    LayoutStart();
                    break;
                case Screen.Guide:
                    if (!banner) SayLine(EntryText.GuideNote);
                    LayoutGuide();
                    break;
                case Screen.Recap:
                    if (!banner) SayLine(notice ?? "Change anything before you start building.");
                    LayoutRecap();
                    break;
                case Screen.Options:
                    if (!banner) SayLine(EntryText.OptionsLine);
                    LayoutOptions();
                    break;
                case Screen.Folder:
                    if (!banner) SayLine(locations?.Roots.Any(root => root.FoldersTruncated) == true ? EntryText.FolderLine + " " + EntryText.FoldersCut : EntryText.FolderLine);
                    LayoutFolder();
                    break;
                case Screen.Review:
                    LayoutReview(banner);
                    break;
                case Screen.Sending:
                    if (!banner) SayLine("Work already running keeps going.");
                    LayoutSending();
                    break;
                default:
                    LayoutPrevious();
                    break;
            }
        }

        private void LayoutStart()
        {
            var prompt = idea!.ExistingProjectId == null ? EntryText.IdeaPrompt : EntryText.WorkPrompt;
            Say(body, prompt, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.04f));
            var width = (ContentWidth - Gap) / 2f;
            Put(bigs[0], EntryText.TypeIdea, new Vector2(Left + width / 2f, -0.04f), width, TypeIdea, detail: EntryText.TypeIdeaInvite);
            Put(bigs[1], EntryText.HelpMe, new Vector2(Right - width / 2f, -0.04f), width, () =>
            {
                idea.BeginGuide();
                Open(Screen.Guide);
            }, detail: EntryText.HelpMeInvite);
            if (notice != null) Say(note, notice, new Vector2(Left, -0.16f), new Vector2(ContentWidth, 0.05f));
        }

        private void TypeIdea()
        {
            var current = idea!;
            OpenKeyboard(current.HasRecap ? current.FirstTask : "", current.ExistingProjectId == null ? EntryText.IdeaPrompt : EntryText.WorkPrompt, text =>
            {
                if (text.Trim().Length == 0) return;
                current.UseIdea(text);
                notice = null;
                screen = Screen.Recap;
            });
        }

        /// <summary>
        /// One fixed question at a time: its offered answers as rows, typing an answer of one's own,
        /// skipping where it can be skipped, and Back. Said to be fixed questions, not an AI.
        /// </summary>
        private void LayoutGuide()
        {
            var current = idea!;
            if (current.Question >= ProjectIdea.Questions.Count)
            {
                screen = Screen.Recap;
                LayoutRecap();
                return;
            }
            var question = ProjectIdea.Questions[current.Question];
            Say(pageCaption, EntryText.Question(current.Question, ProjectIdea.Questions.Count), new Vector2(Left, BodyTop), new Vector2(0.3f, 0.03f));
            pageCaption.alignment = TextAlignmentOptions.TopLeft;
            Say(body, question.Prompt, new Vector2(Left, BodyTop - 0.03f), new Vector2(ContentWidth, 0.04f));
            var choices = current.Choices;
            for (var index = 0; index < choices.Count && index < 3; index++)
            {
                var choice = choices[index];
                var chosen = current.AnswerTo(current.Question) == choice;
                Put(rows[index], choice, new Vector2(0f, BodyTop - 0.12f - index * RowPitch), ContentWidth, () =>
                {
                    current.Answer(choice);
                    if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                    Layout();
                }, detail: chosen ? "Your answer" : null);
            }
            PutRightAligned(bottomRight, question.TypeLabel, Right, BottomCenter, () =>
            {
                var at = current.Question;
                OpenKeyboard(current.AnswerTo(at) ?? "", question.Prompt, text =>
                {
                    if (current.Question == at) current.Answer(text);
                    if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                });
            });
            if (question.SkipLabel != null)
            {
                PutRightAligned(bottomMiddle, question.SkipLabel, Right - 0.25f, BottomCenter, () =>
                {
                    current.Skip();
                    if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                    Layout();
                });
            }
            Put(bottomLeft, EntryText.Back, new Vector2(Left + 0.07f, BottomCenter), 0.14f, () =>
            {
                if (!current.Back()) screen = Screen.CreateStart;
                Layout();
            });
        }

        /// <summary>
        /// The editable recap: the project's name and first task, each with Change; where its files
        /// live, with Choose or Change; and how it runs, with More options. Start building shows the
        /// whole request first, and is offered once nothing is missing.
        /// </summary>
        private void LayoutRecap()
        {
            var current = idea!;
            var textWidth = ContentWidth - ChangeWidth - Gap;
            var y = BodyTop;
            Say(recapProject, EntryText.ProjectLine(current.Name.Length == 0 ? "not named yet" : current.Name), new Vector2(Left, y), new Vector2(textWidth, 0.04f));
            if (current.ExistingProjectId == null)
            {
                Put(changeName, EntryText.Change, new Vector2(Right - ChangeWidth / 2f, y - 0.02f), ChangeWidth, () =>
                    OpenKeyboard(current.Name, "Name the project", text => current.Rename(text)));
            }
            y -= 0.055f;
            Say(recapTask, EntryText.TaskLine(current.FirstTask), new Vector2(Left, y), new Vector2(textWidth, 0.056f));
            Put(changeTask, EntryText.Change, new Vector2(Right - ChangeWidth / 2f, y - 0.028f), ChangeWidth, () =>
                OpenKeyboard(current.FirstTask, "What should the first task be?", text => current.Rewrite(text)));
            y -= 0.072f;
            var place = CurrentFolder();
            Say(recapLocation, EntryText.FolderRecap(place, current.Folder, draft.Runtime?.UsesProjectLocation == true), new Vector2(Left, y),
                new Vector2(textWidth, 0.05f));
            Put(changeFolder, current.Folder == null && place == null ? EntryText.ChooseFolder : EntryText.Change,
                new Vector2(Right - ChangeWidth / 2f, y - 0.022f), ChangeWidth, () =>
                {
                    page = 0;
                    Open(Screen.Folder);
                    ReadFolders();
                });
            y -= 0.068f;
            var optionsWidth = moreOptions.Button.Measure(EntryText.MoreOptions, 0.18f);
            Say(recapRuns, EntryText.RunsWith(draft), new Vector2(Left, y), new Vector2(ContentWidth - optionsWidth - Gap, 0.034f));
            Put(moreOptions, draft.Runtime == null ? EntryText.ChooseHowItRuns : EntryText.MoreOptions,
                new Vector2(Right - optionsWidth / 2f, y - 0.017f), optionsWidth, () =>
                {
                    page = 0;
                    showModels = draft.Runtime?.ModelChoice == ModelChoice.Listed;
                    Open(Screen.Options);
                });
            y -= 0.04f;
            Say(recapServed, EntryText.ModelLine(draft), new Vector2(Left, y), new Vector2(ContentWidth - optionsWidth - Gap, 0.05f));
            var problem = StartProblem();
            // Moving a project changes where all its later work runs, so the recap says so before the review.
            var warning = problem ?? (current.Folder != null && current.ExistingProjectId != null ? EntryText.RebindWarning : null);
            if (warning != null) Say(note, warning, new Vector2(Left, -0.155f), new Vector2(ContentWidth, 0.06f));
            Put(bottomLeft, "Start over", new Vector2(Left + 0.1f, BottomCenter), 0.2f, () =>
            {
                idea = new ProjectIdea(current.ExistingProjectId, current.ExistingProjectId == null ? null : current.Name);
                review = null;
                sequence = null;
                sentFolder = null;
                notice = null;
                Open(Screen.CreateStart);
            });
            if (problem == null) PutRightAligned(bottomRight, EntryText.StartBuilding, Right, BottomCenter, StartBuilding);
        }

        /// <summary>Why Start building cannot go ahead now, or null.</summary>
        private string? StartProblem()
        {
            if (demonstration() != null) return "The interactive example cannot start work. Connect to your Mac.";
            var now = state();
            if (now == null || !connected()) return "Waiting for your Mac.";
            if (idea?.Problem is string ideaProblem) return ideaProblem;
            if (draft.Runtime == null) return "Choose how it runs.";
            if (!now.Runtimes.Any(runtime => runtime.RuntimeId == draft.Runtime.RuntimeId)) return "That runtime is not available now. Choose another.";
            if (draft.Runtime.ModelChoice == ModelChoice.Listed && draft.Model == null) return draft.ModelProblem ?? "Choose a model.";
            if (idea?.ExistingProjectId != null && !now.Projects.ContainsKey(idea.ExistingProjectId)) return "That project is not known here any more.";
            if (draft.Runtime.UsesProjectLocation && idea?.Folder == null && CurrentFolder() == null) return "Choose where its files live.";
            return null;
        }

        /// <summary>An existing project's folder as the host bound it, or null for a new project or one without.</summary>
        private ProjectLocation? CurrentFolder()
        {
            var projectId = idea?.ExistingProjectId;
            return projectId != null && state()?.Projects.TryGetValue(projectId, out var project) == true ? project!.Location : null;
        }

        private void StartBuilding()
        {
            if (StartProblem() != null || idea == null) return;
            draft.Objective = idea.FirstTask;
            var model = draft.Model;
            var place = CurrentFolder();
            var folder = idea.Folder?.Describe() ?? (place != null ? LabelText.Plain(place.Name) : "none");
            // A project that exists and is to move shows where its work runs now and from now on.
            var before = idea.Folder != null && idea.ExistingProjectId != null ? (place != null ? LabelText.Plain(place.Name) : "none") : null;
            review = new NewWorkReview(
                idea.Name,
                draft.Title,
                EntryText.RuntimeName(draft.Runtime!),
                model?.DisplayName ?? "Chosen by the runtime",
                model == null ? "The runtime does not list models" : EntryText.ServedShort(model.Served) + ", " + EntryText.Tools(model.ToolCalling),
                model?.ModelRef ?? "No model selected",
                idea.FirstTask,
                folder,
                before);
            Open(Screen.Review);
        }

        /// <summary>
        /// The runtimes that can start work, or the chosen runtime's own models with where each runs.
        /// Choosing is all this does; nothing is chosen for the person.
        /// </summary>
        private void LayoutOptions()
        {
            var now = state();
            if (showModels && draft.Runtime?.ModelChoice == ModelChoice.Listed)
            {
                if (draft.Models.Count == 0)
                {
                    Say(body, draft.ModelProblem ?? "This runtime lists no models.", new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.1f));
                }
                var models = Paged(draft.Models);
                for (var index = 0; index < models.Count; index++)
                {
                    var model = models[index];
                    var chosen = draft.Model?.ModelRef == model.ModelRef;
                    Put(rows[index], LabelText.Plain(model.DisplayName), new Vector2(0f, BodyTop - RowHeight / 2f - index * RowPitch), ContentWidth, () =>
                    {
                        draft.ChooseModel(model);
                        Layout();
                    }, detail: (chosen ? "Chosen · " : "") + EntryText.ServedShort(model.Served) + " · " + EntryText.Tools(model.ToolCalling),
                        detailColor: model.Served == ModelServed.Remote ? WorkspaceVisuals.AttentionColor : (Color?)null);
                }
                Pager(draft.Models.Count);
                Put(bottomLeft, "Change runtime", new Vector2(Left + 0.12f, BottomCenter), 0.24f, () =>
                {
                    showModels = false;
                    page = 0;
                    Layout();
                });
            }
            else
            {
                var runtimes = now?.Runtimes.Where(runtime => runtime.Capabilities.StartExecution)
                    .OrderBy(runtime => runtime.DisplayName, StringComparer.Ordinal).ToList() ?? new List<RuntimeDescriptor>();
                if (runtimes.Count == 0) Say(body, EntryText.NoRuntimes, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.1f));
                var shown = Paged(runtimes);
                for (var index = 0; index < shown.Count; index++)
                {
                    var runtime = shown[index];
                    var chosen = draft.Runtime?.RuntimeId == runtime.RuntimeId;
                    var detail = runtime.Synthetic ? "simulated work" : runtime.ModelChoice == ModelChoice.Listed ? "lists its models" : "chooses its own model";
                    Put(rows[index], EntryText.RuntimeName(runtime), new Vector2(0f, BodyTop - RowHeight / 2f - index * RowPitch), ContentWidth,
                        () => ChooseRuntime(runtime), detail: (chosen ? "Chosen · " : "") + detail);
                }
                Pager(runtimes.Count);
            }
            PutRightAligned(bottomRight, EntryText.Done, Right, BottomCenter, () => Open(Screen.Recap));
        }

        private void ChooseRuntime(RuntimeDescriptor runtime)
        {
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            modelCancellation = null;
            modelRead = null;
            draft.ChooseRuntime(runtime);
            review = null;
            page = 0;
            if (runtime.ModelChoice == ModelChoice.Listed)
            {
                // Listing may start the runtime, so it is read when the person chooses it, never on a timer.
                var api = ControlPlaneSettings.Api();
                if (api == null) draft.ModelReadFailed("No control plane is configured.");
                else
                {
                    modelRuntimeId = runtime.RuntimeId;
                    modelCancellation = new CancellationTokenSource();
                    modelRead = api.GetRuntimeModelsAsync(runtime.RuntimeId, modelCancellation.Token);
                }
                showModels = true;
            }
            Layout();
        }

        private void PollModels()
        {
            var read = modelRead;
            if (read == null || !read.IsCompleted) return;
            modelRead = null;
            if (draft.Runtime?.RuntimeId != modelRuntimeId || read.IsCanceled) return;
            if (read.IsFaulted) draft.ModelReadFailed(read.Exception?.GetBaseException().Message ?? "The request failed.");
            else draft.SetModels(read.Result);
            if (visible) Layout();
        }

        /// <summary>
        /// Reads the folders the Mac lists, on demand: when the person opens Where its files live, or
        /// asks again. Never on a timer, and never in the editor's renders.
        /// </summary>
        private void ReadFolders()
        {
            if (rendering) return;
            locationsCancellation?.Cancel();
            locationsCancellation?.Dispose();
            locationsCancellation = null;
            locations = null;
            locationsProblem = null;
            var api = ControlPlaneSettings.Api();
            if (api == null)
            {
                locationsProblem = "no control plane is configured.";
                return;
            }
            locationsCancellation = new CancellationTokenSource();
            locationsRead = api.GetLocationsAsync(locationsCancellation.Token);
        }

        private void PollFolders()
        {
            var read = locationsRead;
            if (read == null || !read.IsCompleted) return;
            locationsRead = null;
            if (read.IsCanceled) return;
            if (read.IsFaulted) locationsProblem = read.Exception?.GetBaseException().Message ?? "the request failed.";
            else locations = read.Result;
            if (visible && screen == Screen.Folder) Layout();
        }

        /// <summary>
        /// Where its files live: the folders the Mac lists, a page at a time. For each place the Mac
        /// allows, a new folder, which the person names with the host's rule, the place itself, and
        /// each folder in it; a place not on the Mac now shows and offers nothing. No places at all
        /// says the Mac allows no folder yet. Choosing returns to the recap; nothing is sent.
        /// </summary>
        private void LayoutFolder()
        {
            var current = idea!;
            Put(bottomLeft, EntryText.Back, new Vector2(Left + 0.07f, BottomCenter), 0.14f, () => Open(Screen.Recap));
            if (locations == null)
            {
                Say(body, locationsProblem == null ? EntryText.ReadingFolders : "Could not read the folders: " + LabelText.Plain(locationsProblem),
                    new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.12f));
                if (locationsProblem != null) PutRightAligned(bottomRight, EntryText.TryAgain, Right, BottomCenter, () => { ReadFolders(); Layout(); });
                return;
            }
            if (locations.Roots.Count == 0)
            {
                Say(body, EntryText.NoFolders, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.12f));
                PutRightAligned(bottomRight, EntryText.TryAgain, Right, BottomCenter, () => { ReadFolders(); Layout(); });
                return;
            }
            var options = ProjectFolder.Options(locations);
            var shown = Paged(options);
            for (var index = 0; index < shown.Count; index++)
            {
                var option = shown[index];
                var chosen = current.Folder != null && !current.Folder.IsNew && current.Folder.RootPath == option.Root.Path
                    && (option.Kind == FolderOptionKind.Root ? current.Folder.FolderName == null
                        : option.Kind == FolderOptionKind.Folder && current.Folder.FolderName == option.Folder!.Name);
                Put(rows[index], option.Label, new Vector2(0f, BodyTop - RowHeight / 2f - index * RowPitch), ContentWidth, () => ChooseOption(option),
                    detail: (chosen ? "Chosen · " : "") + option.Detail,
                    detailColor: option.Choosable ? (Color?)null : WorkspaceVisuals.AttentionColor);
            }
            Pager(options.Count);
            if (notice != null && screen == Screen.Folder) Say(note, notice, new Vector2(Left, -0.17f), new Vector2(ContentWidth, 0.05f));
        }

        private void ChooseOption(FolderOption option)
        {
            var current = idea!;
            switch (option.Kind)
            {
                case FolderOptionKind.NewFolder:
                    var suggested = current.Folder != null && current.Folder.IsNew ? current.Folder.FolderName! : ProjectFolder.SuggestName(current.Name);
                    OpenKeyboard(suggested, EntryText.NewFolderPrompt, text =>
                    {
                        var made = ProjectFolder.New(option.Root, text);
                        if (made == null)
                        {
                            notice = EntryText.NewFolderRule;
                            return;
                        }
                        notice = null;
                        current.ChooseFolder(made);
                        screen = Screen.Recap;
                    });
                    return;
                case FolderOptionKind.Root:
                case FolderOptionKind.Folder:
                    notice = null;
                    current.ChooseFolder(ProjectFolder.Existing(option.Root, option.Folder));
                    Open(Screen.Recap);
                    return;
            }
        }

        /// <summary>
        /// The whole request, a page at a time, as it will be sent. Yes, start building shows on the
        /// last page only, in the bottom row's middle, where Start building never was.
        /// </summary>
        /// <summary>
        /// The whole request, a page at a time, as it will be sent: each value under its label, wrapped
        /// at the panel's width at word boundaries, whole on one page unless it alone is taller than a
        /// page, when it fills pages of its own. Yes, start building shows on the last page only.
        /// </summary>
        private void LayoutReview(bool banner)
        {
            var current = review!;
            Paginate(current);
            if (!banner) SayLine("Part " + (current.Page + 1) + " of " + current.PageCount + ". This is exactly what is sent.");
            var y = BodyTop;
            var parts = current.Parts;
            for (var index = 0; index < parts.Count && index < reviewLabels.Count; index++)
            {
                var part = parts[index];
                var label = reviewLabels[index];
                // An item taller than a page shows the lines this part holds, cut where its measured lines start.
                var (characters, starts) = reviewItemLayout[part.Item];
                var from = starts[part.FirstLine];
                var to = part.FirstLine + part.Lines < starts.Length ? starts[part.FirstLine + part.Lines] : characters.Length;
                // Each value is already spelled in ASCII by NewWorkReview; TextMeshPro only needs its backslashes doubled.
                label.text = characters.Substring(from, to - from).Replace("\\", "\\\\");
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                label.rectTransform.sizeDelta = new Vector2(ContentWidth, (part.Lines + 0.3f) * reviewLine);
                label.rectTransform.localPosition = new Vector3(Left, y, -0.001f);
                label.gameObject.SetActive(true);
                used.Add(label);
                y -= part.Lines * reviewLine;
            }
            if (current.Page > 0) Put(pagePrevious, WorkspaceText.PreviousPart, new Vector2(Left + 0.1f, -0.18f), 0.2f, () => { current.Previous(); Layout(); });
            if (!current.CanConfirm) Put(pageNext, WorkspaceText.NextPart, new Vector2(Right - 0.1f, -0.18f), 0.2f, () => { current.Next(); Layout(); });
            Put(bottomLeft, EntryText.Change, new Vector2(Left + 0.08f, BottomCenter), 0.16f, () =>
            {
                review = null;
                Open(Screen.Recap);
            });
            if (current.CanConfirm && StartProblem() == null)
            {
                // Clear of where Start building was, on the recap's right, so pressing twice never confirms.
                Put(bottomMiddle, EntryText.ConfirmStart, new Vector2(-0.02f, BottomCenter), 0.3f, ConfirmReviewed, confirm: true);
            }
        }

        /// <summary>The review's pages, laid out once for each review from what each item takes at the panel's width.</summary>
        private void Paginate(NewWorkReview current)
        {
            if (paginated == current && current.Paginated) return;
            // One line's height: the distance between the baselines of two lines of this label.
            reviewMeasure.gameObject.SetActive(true);
            reviewMeasure.text = "A\nA";
            reviewMeasure.ForceMeshUpdate(true);
            reviewLine = reviewMeasure.textInfo.lineInfo[0].baseline - reviewMeasure.textInfo.lineInfo[1].baseline;
            reviewMeasure.gameObject.SetActive(false);
            reviewItemLayout = current.Items.Select(WrapAtWidth).ToList();
            reviewItemLines = reviewItemLayout.Select(layout => layout.LineStarts.Length).ToList();
            current.Paginate(reviewItemLines, PageLines());
            paginated = current;
        }

        private int PageLines() => Mathf.Max(1, Mathf.FloorToInt((BodyTop - ReviewBottom) / Mathf.Max(reviewLine, 0.001f) + 0.01f));

        /// <summary>An item wrapped at the panel's width: the characters it shows and where each line starts among them.</summary>
        private (string Characters, int[] LineStarts) WrapAtWidth(ReviewItem item)
        {
            reviewMeasure.gameObject.SetActive(true);
            reviewMeasure.rectTransform.sizeDelta = new Vector2(ContentWidth, 1f);
            reviewMeasure.text = ForReview(item);
            reviewMeasure.ForceMeshUpdate(true);
            var info = reviewMeasure.textInfo;
            var characters = new System.Text.StringBuilder(info.characterCount);
            for (var index = 0; index < info.characterCount; index++) characters.Append(info.characterInfo[index].character);
            var starts = new int[Mathf.Max(1, info.lineCount)];
            for (var line = 1; line < info.lineCount; line++) starts[line] = info.lineInfo[line].firstCharacterIndex;
            reviewMeasure.gameObject.SetActive(false);
            return (characters.ToString(), starts);
        }

        private static string ForReview(ReviewItem item) => item.Text.Replace("\\", "\\\\");

        private void ConfirmReviewed()
        {
            if (review?.CanConfirm != true || StartProblem() != null || idea == null) return;
            review = null;
            var newProject = idea.ExistingProjectId == null ? idea.Name : null;
            var folder = idea.Folder?.ToContract();
            if (sequence != null && sequence.CanRetry)
            {
                // Sent again with the folder only when the person chose another since the last try.
                Send(sequence.Retry(newProject, idea.Folder != sentFolder ? folder : null));
            }
            else if (sequence == null)
            {
                draft.ProjectId = idea.ExistingProjectId;
                sequence = new BuildSequence(draft, commands, newProject, folder);
                Send(sequence.Begin());
            }
            sentFolder = idea.Folder;
            Open(Screen.Sending);
        }

        private void Send(CommandEnvelope command)
        {
            var session = connection != null ? connection.Session : null;
            if (session == null)
            {
                sequence?.AcknowledgementLost(new SessionUnavailableException("Not connected."));
                return;
            }
            Remember(sequence?.Unresolved);
            pendingAck = session.SubmitAsync(command);
        }

        /// <summary>Keeps the command whose outcome may be unknown on the device, so a restart still blocks a blind retry.</summary>
        private void Remember(string? commandId)
        {
            if (commandId == unresolved) return;
            unresolved = commandId;
            if (commandId == null) PlayerPrefs.DeleteKey(UnresolvedCommandPreference);
            else PlayerPrefs.SetString(UnresolvedCommandPreference, commandId);
            PlayerPrefs.Save();
        }

        private void UpdateCreate()
        {
            PollModels();
            PollFolders();
            var current = sequence;
            if (current == null) return;
            var ack = pendingAck;
            if (ack != null && ack.IsCompleted)
            {
                pendingAck = null;
                if (ack.IsFaulted || ack.IsCanceled) current.AcknowledgementLost(ack.Exception?.GetBaseException() ?? new InvalidOperationException("The acknowledgement was lost."));
                else current.Acknowledged(ack.Result);
            }
            var wasStarted = current.Started;
            var next = current.Advance(state());
            if (next != null) Send(next);
            Remember(current.Unresolved);
            // A project made here shows on the stage, whatever the person chose to show before.
            if (current.ProjectId != null && current.ProjectId != shownProject && idea?.ExistingProjectId == null)
            {
                shownProject = current.ProjectId;
                rail?.ShowProject(current.ProjectId);
            }
            if ((next != null || current.Started != wasStarted) && visible && screen == Screen.Sending) Layout();
        }

        /// <summary>
        /// Each step and how it went, in words: sent is not done, and a step is confirmed only by its
        /// completed record. A refusal says why and offers Try again or Change; an unknown outcome
        /// offers only checking the work first.
        /// </summary>
        private void LayoutSending()
        {
            var current = sequence;
            if (current == null)
            {
                screen = Screen.Recap;
                LayoutRecap();
                return;
            }
            var newProject = current.Steps[0].Kind == BuildStepKind.CreateProject;
            var lines = current.Steps.Select(step => EntryText.StepName(step.Kind, newProject) + ": " + EntryText.StepStatus(step)).ToList();
            if (current.Started) lines.Add(EntryText.Started);
            SayLines(body, lines, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.3f));
            body.fontSize = WorkspaceVisuals.DetailSize;
            if (current.Started)
            {
                PutRightAligned(bottomRight, EntryText.Done, Right, BottomCenter, () =>
                {
                    drafts.Remove(draftKey);
                    idea = null;
                    sequence = null;
                    sentFolder = null;
                    shownProject = null;
                    Hide();
                });
            }
            else if (current.CanRetry && current.StoppedAt is BuildStep stopped && EntryText.AboutFolder(stopped))
            {
                // The next action comes from the refusal's code. Either way the request is reviewed again before it is sent.
                var taken = stopped.Refusal == RejectionCode.LocationExists ? idea?.Folder : null;
                if (taken != null && taken.IsNew)
                {
                    PutRightAligned(bottomRight, EntryText.UseThatFolder, Right, BottomCenter, () =>
                    {
                        idea!.ChooseFolder(ProjectFolder.Existing(taken));
                        StartBuilding();
                    });
                }
                else
                {
                    PutRightAligned(bottomRight, EntryText.TryAgain, Right, BottomCenter, () =>
                    {
                        if (StartProblem() == null && sequence?.CanRetry == true) Send(sequence.Retry(idea?.ExistingProjectId == null ? idea?.Name : null));
                        Layout();
                    });
                }
                Put(bottomLeft, EntryText.ChooseAnotherFolder, new Vector2(Left + 0.17f, BottomCenter), 0.34f, () =>
                {
                    page = 0;
                    Open(Screen.Folder);
                    ReadFolders();
                });
            }
            else if (current.CanRetry)
            {
                PutRightAligned(bottomRight, EntryText.TryAgain, Right, BottomCenter, () =>
                {
                    if (StartProblem() == null && sequence?.CanRetry == true) Send(sequence.Retry(idea?.ExistingProjectId == null ? idea?.Name : null));
                    Layout();
                });
                Put(bottomLeft, EntryText.Change, new Vector2(Left + 0.08f, BottomCenter), 0.16f, () => Open(Screen.Recap));
            }
            else if (current.Stopped || current.Steps.Any(step => step.Status == BuildStepStatus.Unknown))
            {
                PutRightAligned(bottomRight, EntryText.ICheckedTheWork, Right, BottomCenter, () =>
                {
                    recoveryArmed = false;
                    Open(Screen.Previous);
                });
            }
        }

        /// <summary>
        /// A request that may have run: its command id, and the control plane's record of it when one
        /// arrives. Clearing it takes two separate presses, the second where the first was not, and
        /// asks the person to check the work shown first. Clearing starts a blank draft, never a retry.
        /// </summary>
        private void LayoutPrevious()
        {
            var id = unresolved ?? sequence?.Unresolved;
            if (id == null)
            {
                screen = Screen.CreateStart;
                LayoutCreate();
                return;
            }
            var record = state()?.Commands.TryGetValue(id, out var found) == true ? found : null;
            SayLine("A request may have run. Check the work shown before starting more; starting again could do it twice.");
            SayLines(body, new[]
            {
                "Command: " + id,
                record == null ? "No result has arrived yet." : "Recorded status: " + record.Status.ToString().ToLowerInvariant(),
                recoveryArmed ? "Clear it only after checking the work." : "Clear it once you have checked the work.",
            }, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.2f));
            body.fontSize = WorkspaceVisuals.DetailSize;
            if (!Live) return;
            if (!recoveryArmed)
            {
                PutRightAligned(bottomRight, EntryText.ICheckedTheWork, Right, BottomCenter, () =>
                {
                    recoveryArmed = true;
                    Layout();
                });
            }
            else
            {
                Put(bottomMiddle, EntryText.ClearAfterChecking, new Vector2(-0.02f, BottomCenter), 0.34f, () =>
                {
                    recoveryArmed = false;
                    pendingAck = null;
                    sequence = null;
                    Remember(null);
                    idea = new ProjectIdea();
                    notice = "Cleared after your check. Start again from your idea.";
                    Open(Screen.CreateStart);
                }, confirm: true);
            }
        }
    }
}
