#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Create a project, and Add a task to a known one. The person types an idea in their own words or
    /// answers a few fixed questions (<see cref="ProjectIdea"/>); both lead to the same editable recap:
    /// the project's name, its first task, where its files live, and how it runs. Where its files
    /// live is chosen from the folders the Mac lists (<c>GET /api/locations</c>, read when the person
    /// opens the choice), asked only when the agent app works in a project folder, and an existing
    /// project keeps its own unless the person moves it, which the review shows as now and from now
    /// on. More options chooses the agent app and, from its own list, the model, with where each model
    /// runs; nothing but a model on the Mac is chosen for the person. Start building shows the whole
    /// request in parts (<see cref="NewWorkReview"/>), and only its last part unlocks Yes, start
    /// building, which stands left of Change, never where Start building stood. Then
    /// <see cref="BuildSequence"/> sends the ordinary commands one at a time, and every step says how
    /// it went; the character that appears reads Starting until the runtime confirms.
    /// </summary>
    /// <remarks>
    /// The draft lives in memory while the app runs, one for a new project and one for each project
    /// a task is added to: closing the panel, opening a character, Open now, or starting work
    /// elsewhere keeps it exactly, with how far its start got, and the rail offers Continue creating.
    /// An app restart loses it. A request whose outcome is unknown keeps its command id on the device
    /// and blocks another start, even after a restart, until the person checks the work and clears it
    /// with two separate presses in two places. A refusal about a folder offers its next action from
    /// the refusal's code: Use that folder after <c>location_exists</c>, Choose a folder after the
    /// others; a project already made is then moved to the new folder before its work is started again.
    /// </remarks>
    public sealed partial class EntryPanel
    {
        private const string UnresolvedCommandPreference = "halcyonic.new-work.unresolved-command-id";

        /// <summary>At most one item a line, so a page never needs more labels than it has lines.</summary>
        private const int ReviewLabels = 14;

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
        private bool confirmingStartOver;
        private bool showModels;
        /// <summary>A line for one screen, such as why a folder name was refused: shown only there, dropped once another screen shows.</summary>
        private (Screen On, string Text)? notice;
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
        private Vector2 paginatedFor;
        private List<int> reviewItemLines = new List<int>();

        /// <summary>Each item as laid out at the panel's width: its characters and where each of its lines starts.</summary>
        private List<(string Characters, int[] LineStarts)> reviewItemLayout = new List<(string, int[])>();
        private float reviewLine;
        private HoldToTalk voice = null!;

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
                var label = RequestLabel("Request item " + index);
                label.overflowMode = TextOverflowModes.Overflow;
                label.gameObject.SetActive(false);
                reviewLabels.Add(label);
            }
            // Never shown: it lays each item out at the panel's width to count its lines.
            reviewMeasure = RequestLabel("Request measure");
            reviewMeasure.overflowMode = TextOverflowModes.Overflow;
            reviewMeasure.gameObject.SetActive(false);
            // Hold to talk, in development builds: the idea or task spoken, heard on the Mac as a draft.
            voice = gameObject.AddComponent<HoldToTalk>();
            frame.HoldStarted += id =>
            {
                if (id == EntryScreens.HoldToTalk) voice.Begin();
            };
            frame.HoldEnded += (id, released) =>
            {
                if (id == EntryScreens.HoldToTalk) voice.End(released);
            };
            voice.Said += OnVoiceSaid;
            voice.Heard += OnHeard;
        }

        private TextMeshPro RequestLabel(string name)
        {
            var label = GlazeText.Create(frame.Content, name, GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 12);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            return label;
        }

        private void DestroyCreate()
        {
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            locationsCancellation?.Cancel();
            locationsCancellation?.Dispose();
        }

        /// <summary>
        /// Opens Create a project, or Add a task to <paramref name="projectId"/>: the draft for that place
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
            confirmingStartOver = false;
            var now = state();
            if (now != null) watch.Begin(now);
            Open(idea!.HasRecap ? Screen.Recap : idea.Guided ? Screen.Guide : Screen.CreateStart);
        }

        /// <summary>The notice for <paramref name="shown"/>, or null when there is none for it.</summary>
        private string? NoticeOn(Screen shown) => notice is { } line && line.On == shown ? line.Text : null;

        /// <summary>Drops a notice meant for another screen, so a line never outlives the screen it was for.</summary>
        private void DropStaleNotice()
        {
            if (notice is { } line && line.On != screen) notice = null;
        }

        /// <summary>The current Create screen as a model, with work that came to wait for the person in its banner.</summary>
        private PanelModel CreateModel()
        {
            if (idea == null && screen != Screen.Previous)
            {
                screen = Screen.CreateStart;
                idea = new ProjectIdea();
            }
            if (screen == Screen.Guide && idea!.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
            if (screen == Screen.Sending && sequence == null) screen = Screen.Recap;
            if (screen == Screen.Review && review == null) screen = Screen.Recap;
            if (screen == Screen.Previous && (unresolved ?? sequence?.Unresolved) == null)
            {
                screen = Screen.CreateStart;
                idea ??= new ProjectIdea();
            }
            var current = idea!;
            var live = demonstration() == null;
            var banner = screen == Screen.Previous ? null : Banner();
            PanelModel model;
            switch (screen)
            {
                case Screen.CreateStart:
                    model = EntryScreens.CreateStart(current, HoldToTalk.Offered && Live, NoticeOn(Screen.CreateStart));
                    break;
                case Screen.Guide:
                    model = EntryScreens.Guide(current);
                    break;
                case Screen.Options:
                    model = EntryScreens.Options(draft, state()?.Runtimes ?? (IEnumerable<RuntimeDescriptor>)Array.Empty<RuntimeDescriptor>(), showModels, live);
                    break;
                case Screen.Folder:
                    model = EntryScreens.Folder(current, locations, locationsProblem, NoticeOn(Screen.Folder));
                    break;
                case Screen.Review:
                    model = EntryScreens.Review(review!, StartProblem());
                    break;
                case Screen.Sending:
                    model = EntryScreens.Sending(sequence!, current.Folder);
                    break;
                case Screen.Previous:
                    var id = (unresolved ?? sequence?.Unresolved)!;
                    var record = state()?.Commands.TryGetValue(id, out var found) == true ? found : null;
                    return EntryScreens.Previous(id, record, recoveryArmed, Live);
                default:
                    screen = Screen.Recap;
                    model = EntryScreens.Recap(current, draft, CurrentFolder(), live, NoticeOn(Screen.Recap), StartProblem(), confirmingStartOver,
                        compact: banner != null);
                    break;
            }
            model.Banner = banner;
            return model;
        }

        /// <summary>What a press on a Create screen does, by the action's id.</summary>
        private void ActCreate(string id, string? key)
        {
            var current = idea;
            switch (id)
            {
                case EntryScreens.TypeIdea:
                    TypeIdea();
                    return;
                case EntryScreens.HoldToTalk:
                    // A press let go before its hold started.
                    OnVoiceSaid(VoiceText.TooShort);
                    return;
                case EntryScreens.HelpMe when current != null:
                    current.BeginGuide();
                    Open(Screen.Guide);
                    return;
                case EntryScreens.Answer when current != null && key != null:
                    current.Answer(key);
                    if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                    Layout();
                    return;
                case EntryScreens.TypeAnswer when current != null && current.Question < ProjectIdea.Questions.Count:
                    var at = current.Question;
                    OpenKeyboard(current.AnswerTo(at) ?? "", ProjectIdea.Questions[at].Prompt, text =>
                    {
                        if (current.Question == at) current.Answer(text);
                        if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                    });
                    return;
                case EntryScreens.Skip when current != null:
                    current.Skip();
                    if (current.Question >= ProjectIdea.Questions.Count) screen = Screen.Recap;
                    Layout();
                    return;
                case EntryScreens.Back when screen == Screen.Guide && current != null:
                    if (!current.Back()) screen = Screen.CreateStart;
                    Layout();
                    return;
                case EntryScreens.Back:
                    Open(Screen.Recap);
                    return;
                case EntryScreens.Rename when current != null:
                    OpenKeyboard(current.Name, EntryText.NameTheProject, text => current.Rename(text));
                    return;
                case EntryScreens.Rewrite when current != null:
                    OpenKeyboard(current.FirstTask, EntryText.WhatFirstTask, text => current.Rewrite(text));
                    return;
                case EntryScreens.ChooseWhere:
                case EntryScreens.ChooseAnotherFolder:
                    Open(Screen.Folder);
                    ReadFolders();
                    return;
                case EntryScreens.MoreOptions:
                    showModels = draft.Runtime?.ModelChoice == ModelChoice.Listed;
                    Open(Screen.Options);
                    return;
                case EntryScreens.StartOver:
                    confirmingStartOver = true;
                    Layout();
                    return;
                case EntryScreens.ConfirmStartOver when current != null:
                    idea = new ProjectIdea(current.ExistingProjectId, current.ExistingProjectId == null ? null : current.Name);
                    review = null;
                    sequence = null;
                    sentFolder = null;
                    notice = null;
                    confirmingStartOver = false;
                    Open(Screen.CreateStart);
                    return;
                case EntryScreens.Cancel:
                    confirmingStartOver = false;
                    recoveryArmed = false;
                    Layout();
                    return;
                case EntryScreens.StartBuilding:
                    StartBuilding();
                    return;
                case EntryScreens.ChooseRuntime when key != null:
                    var runtime = state()?.Runtimes.FirstOrDefault(each => each.RuntimeId == key);
                    if (runtime != null) ChooseRuntime(runtime);
                    return;
                case EntryScreens.ChooseModel when key != null:
                    var model = draft.Models.FirstOrDefault(each => each.ModelRef == key);
                    if (model != null) draft.ChooseModel(model);
                    Layout();
                    return;
                case EntryScreens.ChangeRuntime:
                    showModels = false;
                    frame.Page = 0;
                    Layout();
                    return;
                case EntryScreens.Done:
                    Open(Screen.Recap);
                    return;
                case EntryScreens.ChooseFolder when key != null && locations != null:
                    var options = ProjectFolder.Options(locations);
                    var index = int.Parse(key, CultureInfo.InvariantCulture);
                    if (index >= 0 && index < options.Count && options[index].Choosable) ChooseOption(options[index]);
                    return;
                case EntryScreens.ReadFolders:
                    ReadFolders();
                    Layout();
                    return;
                case PanelModel.PreviousPart when review != null:
                    review.Previous();
                    Layout();
                    return;
                case PanelModel.NextPart when review != null:
                    review.Next();
                    Layout();
                    return;
                case EntryScreens.ConfirmStart:
                    ConfirmReviewed();
                    return;
                case EntryScreens.Change:
                    review = null;
                    Open(Screen.Recap);
                    return;
                case EntryScreens.Close:
                    drafts.Remove(draftKey);
                    idea = null;
                    sequence = null;
                    sentFolder = null;
                    shownProject = null;
                    Hide();
                    return;
                case EntryScreens.TryAgain:
                    if (StartProblem() == null && sequence?.CanRetry == true) Send(sequence.Retry(idea?.ExistingProjectId == null ? idea?.Name : null));
                    Layout();
                    return;
                case EntryScreens.UseThatFolder when current?.Folder is ProjectFolder taken && taken.IsNew:
                    current.ChooseFolder(ProjectFolder.Existing(taken));
                    StartBuilding();
                    return;
                case EntryScreens.CheckFirst:
                    recoveryArmed = false;
                    Open(Screen.Previous);
                    return;
                case EntryScreens.Clear:
                    recoveryArmed = true;
                    Layout();
                    return;
                case EntryScreens.ConfirmClear when recoveryArmed:
                    recoveryArmed = false;
                    pendingAck = null;
                    sequence = null;
                    Remember(null);
                    idea = new ProjectIdea();
                    notice = (Screen.CreateStart, EntryText.Cleared);
                    Open(Screen.CreateStart);
                    return;
            }
        }

        /// <summary>Shows hold to talk's words as if it had said them, for the editor's renders.</summary>
        public void SayForRender(string words) => OnVoiceSaid(words);

        /// <summary>Hold to talk's words, under the field on the start screen.</summary>
        private void OnVoiceSaid(string words)
        {
            if (!visible || screen != Screen.CreateStart) return;
            notice = (Screen.CreateStart, words);
            Layout();
        }

        /// <summary>
        /// The idea or task the Mac heard: a draft taken as typed text is, onto the recap, which says
        /// it was heard so the person checks it; nothing is sent until they start building.
        /// </summary>
        private void OnHeard(string text)
        {
            var current = idea;
            if (!visible || screen != Screen.CreateStart || current == null) return;
            current.UseIdea(text);
            notice = (Screen.Recap, VoiceText.HeardNote);
            screen = Screen.Recap;
            Layout();
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

        /// <summary>Why Start building cannot go ahead now, or null.</summary>
        private string? StartProblem() => EntryScreens.StartProblem(demonstration() != null, state(), connected(), idea, draft, CurrentFolder());

        /// <summary>An existing project's folder as the host bound it, or null for a new project or one without.</summary>
        private ProjectLocation? CurrentFolder()
        {
            var projectId = idea?.ExistingProjectId;
            return projectId != null && state()?.Projects.TryGetValue(projectId, out var project) == true ? project!.Location : null;
        }

        private void StartBuilding()
        {
            if (StartProblem() != null || idea == null) return;
            review = EntryScreens.ReviewOf(idea, draft, CurrentFolder(), demonstration() == null);
            Open(Screen.Review);
        }

        /// <summary>
        /// Focus went to another window. A confirmation half done is confirmed afresh once back: a
        /// first press on a model elsewhere lapses, a recovery clear disarms, and a review whose final
        /// press waits goes back to the recap, so Start building shows the whole request again.
        /// Nothing is sent, and the draft is kept.
        /// </summary>
        private void OnFocusLeft()
        {
            draft.FocusLeft();
            recoveryArmed = false;
            confirmingStartOver = false;
            if (visible && screen == Screen.Review && review != null)
            {
                // Open waits for input, which is suspended now; the screen changes while the panel is shown.
                review = null;
                screen = Screen.Recap;
                notice = (Screen.Recap, EntryText.ReviewAfresh);
                Layout();
                return;
            }
            if (visible) Layout();
        }

        private void ChooseRuntime(RuntimeDescriptor runtime)
        {
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            modelCancellation = null;
            modelRead = null;
            draft.ChooseRuntime(runtime);
            review = null;
            frame.Page = 0;
            if (runtime.ModelChoice == ModelChoice.Listed)
            {
                // Listing may start the runtime, so it is read when the person chooses it, never on a timer.
                var api = ControlPlaneSettings.Api();
                if (api == null) draft.ModelReadFailed("No " + HostText.Noun + " is connected.");
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
            if (read.IsFaulted) draft.ModelReadFailed(read.Exception?.GetBaseException().Message ?? "No reason given.");
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
                locationsProblem = "No " + HostText.Noun + " is connected.";
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
            if (read.IsFaulted) locationsProblem = read.Exception?.GetBaseException().Message ?? "No reason given.";
            else locations = read.Result;
            if (visible && screen == Screen.Folder) Layout();
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
                            notice = (Screen.Folder, EntryText.NewFolderRule);
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
        /// The whole request, a part at a time, as it will be sent: each value under its label, wrapped
        /// at the body's width at word boundaries, whole on one part unless it alone is taller than a
        /// part, when it fills parts of its own. Laid out in the space the frame leaves above its pager.
        /// </summary>
        private void LayRequest(PanelModel model)
        {
            var current = review!;
            var area = frame.CustomBody;
            if (Paginate(current, area.size))
            {
                // The parts are known now: the pager and the final press say so.
                var counted = EntryScreens.Review(current, StartProblem());
                counted.Banner = model.Banner;
                frame.Show(counted);
                area = frame.CustomBody;
            }
            var y = area.yMax;
            var parts = current.Parts;
            for (var index = 0; index < reviewLabels.Count; index++)
            {
                var label = reviewLabels[index];
                if (index >= parts.Count)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }
                var part = parts[index];
                // An item taller than a part shows the lines this part holds, cut where its measured lines start.
                var (characters, starts) = reviewItemLayout[part.Item];
                var from = starts[part.FirstLine];
                var to = part.FirstLine + part.Lines < starts.Length ? starts[part.FirstLine + part.Lines] : characters.Length;
                // Each value is already spelled in ASCII by NewWorkReview; TextMeshPro only needs its backslashes doubled.
                label.text = characters.Substring(from, to - from).Replace("\\", "\\\\");
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                label.rectTransform.sizeDelta = new Vector2(area.width, (part.Lines + 0.3f) * reviewLine);
                label.rectTransform.localPosition = new Vector3(area.xMin, y, -0.0005f);
                label.gameObject.SetActive(true);
                y -= part.Lines * reviewLine;
            }
        }

        private void HideRequest()
        {
            foreach (var label in reviewLabels)
            {
                if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// The review's parts, laid out once for each review and space from what each item takes at the
        /// body's width. Returns whether it laid them out now.
        /// </summary>
        private bool Paginate(NewWorkReview current, Vector2 space)
        {
            if (paginated == current && current.Paginated && paginatedFor == space) return false;
            // One line's height: the distance between the baselines of two lines of this label.
            reviewMeasure.gameObject.SetActive(true);
            reviewMeasure.rectTransform.sizeDelta = new Vector2(space.x, 1f);
            reviewMeasure.text = "A\nA";
            reviewMeasure.ForceMeshUpdate(true);
            reviewLine = reviewMeasure.textInfo.lineInfo[0].baseline - reviewMeasure.textInfo.lineInfo[1].baseline;
            reviewMeasure.gameObject.SetActive(false);
            reviewItemLayout = current.Items.Select(item => WrapAtWidth(item, space.x)).ToList();
            reviewItemLines = reviewItemLayout.Select(layout => layout.LineStarts.Length).ToList();
            var page = current.Paginated && paginated == current ? current.Page : 0;
            current.Paginate(reviewItemLines, Mathf.Max(1, Mathf.FloorToInt(space.y / Mathf.Max(reviewLine, 0.0001f) + 0.01f)));
            while (current.Page < Mathf.Min(page, current.PageCount - 1)) current.Next();
            paginated = current;
            paginatedFor = space;
            return true;
        }

        /// <summary>An item wrapped at the body's width: the characters it shows and where each line starts.</summary>
        private (string Characters, int[] LineStarts) WrapAtWidth(ReviewItem item, float width)
        {
            reviewMeasure.gameObject.SetActive(true);
            reviewMeasure.rectTransform.sizeDelta = new Vector2(width, 1f);
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
    }
}
