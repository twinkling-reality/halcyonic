#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Three actual production routes in the candidate spatial composition, at both reading sizes.</summary>
        public static void SpatialWorkspaceCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpatialWorkspaceRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = new List<string>();
                try
                {
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Running(), false));
                    var multiple = Path.Combine(folder, "multiple-projects"); Directory.CreateDirectory(multiple);
                    found.AddRange(RenderSpatialWorkspace(multiple, Work.Running(), false, multipleProjects: true));
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Approval("Run make migrate"), true));
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: spatial composition: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: spatial composition: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Existing full Results consumer flow under the candidate layout, including exact paging and return.</summary>
        public static void SpatialWorkspaceInteractionCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpatialInteractionRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = new List<string>();
                try
                {
                    var pending = Work.Asking(new QuestionView {
                        QuestionId = "render-spatial-long-question", Answerable = true, AskedAt = Time,
                        Prompts = new List<QuestionPrompt> { new QuestionPrompt {
                            Key = "environment", Header = "Release target", Multiple = false, FreeText = false,
                            Text = string.Join("\n", Enumerable.Repeat("Which environment should receive this change after the current tests finish?", 40)),
                            Options = new List<QuestionOption> {
                                new() { Label = "Staging", Description = string.Join("\n", Enumerable.Repeat("Preserve the current staging data.", 28)) },
                                new() { Label = "Production", Description = "Use the production environment." } } } } });
                    found.AddRange(RenderSpatialWorkspace(folder, pending, waiting: true, question: true));
                    found.AddRange(RenderTaskResults("spatial", folder, withShelf: false, desk: false, spatialWorkspace: true));
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: spatial interaction: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: spatial interaction: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderSpatialWorkspace(string folder, Work work, bool waiting, bool question = false, bool multipleProjects = false)
        {
            var failures = new List<string>();
            var root = new GameObject("Actual spatial workspace composition witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                // Explicit synthetic transport; production Pump establishes the real current-watermark guard.
                var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution },
                    Runtimes = work.State.Runtimes.ToList() };
                if (multipleProjects)
                    snapshot.Projects.AddRange(new[] {
                        new ProjectView { ProjectId = "0192a7a0-0000-7000-8000-0000000000b2", Name = "Search service", CreatedAt = Time, UpdatedAt = Time },
                        new ProjectView { ProjectId = "0192a7a0-0000-7000-8000-0000000000b3", Name = "Mobile client", CreatedAt = Time, UpdatedAt = Time }
                    });
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time,
                    CommandPolicies = new List<CommandPolicy> {
                        new() { CommandType = CommandType.ExecutionRespondToApproval, Policy = PolicyCategory.ReviewRequired },
                        new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.LowConsequence },
                        new() { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.LowConsequence } } });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) failures.Add("spatial fixture did not establish current session authority");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var commands = new CommandFactory(client);
                Action<string>? done = null;
                var sends = 0;
                var connected = true;
                FileColumn? selectedFile = null;
                var presentation = new WorkspacePresentationState();
                var targets = new List<CharacterTarget>();
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, Commands = commands, Presentation = presentation,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    State = () => session.State, Session = () => session, Connected = () => connected,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => done = accept,
                    MakeNewProject = host => new NewProjectFlow(host, commands, new KeptInMemory()),
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => selectedFile = new FileColumn(host, () => work.Present(live: connected), commands, () => null, _ => null, () => ""),
                    CharacterOf = id => targets.FirstOrDefault(target => target.View.WorkstreamId == id),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, targets, null, false),
                });
                // Production host pagination must count the actual title face, not the previous
                // title role; a font whose width changes the wrap must not silently overfill a page.
                var titleWitnesses = Enumerable.Range(1, 12).Select(count => string.Join(" ", Enumerable.Repeat("Build rate limiting", count))).ToList();
                foreach (var title in titleWitnesses)
                {
                    var actualRows = MenuFrameView.SpatialTitleRows(title, Glaze.Menu.FileColumnDegrees);
                    if (director.TitleRows(title, Glaze.Menu.FileColumnDegrees) != actualRows)
                        failures.Add("spatial: host title row budget differs from the actual reading title");
                }
                void Draw()
                { for (var turn = 0; turn < 4; turn++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Press(string action, string? key = null)
                {
                    Draw();
                    var visible = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame &&
                        (frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available) ||
                         frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)));
                    if (visible.View == null || !director.PressForRender(visible.Kind, action, key, visible.View.Frame, null))
                        failures.Add("spatial: actual visible route refused " + action);
                }
                void Shot(string name)
                {
                    Draw();
                    failures.AddRange(SpatialState(name, folder, root, camera, texture, director, characters, eyes));
                }
                if (!waiting)
                {
                    Shot("spatial-projects");
                    var typicalProject = PageLine.KnownProject("Typical project", "1 task running", "measured-only");
                    var projectBudget = director.PageHeight(director.TitleRows("Projects", Glaze.Menu.FileColumnDegrees), besideMenu: true);
                    if (projectBudget < 2f * MenuFrameView.ProjectListLineHeight(typicalProject, Glaze.Menu.FileColumnDegrees))
                        failures.Add("spatial: Projects must fit at least two typical project rows at the current reading size");
                    foreach (var row in director.Plane.Shown.SelectMany(shown => shown.View.ShownLines).Where(row => row.Model.ProjectIdentity))
                    {
                        if (row.Fact == null || row.Icon == null || row.Words.fontSize <= row.Fact.fontSize
                            || row.Fact.transform.position.y >= row.Words.transform.position.y)
                            failures.Add("spatial: known project identity must lead its own supporting summary line");
                        if (GlazeRuns.Of(row.Words).Theirs.Count == 0 || row.Words.richText)
                            failures.Add("spatial: known project identity lost literal outside-text provenance");
                        if (row.Words.rectTransform.pivot != new Vector2(0f, 1f)
                            || !Mathf.Approximately(row.Words.fontSize, GlazeTokens.FontSize(GlazeTokens.Units(Glaze.TitleDegrees)))
                            || row.Fact != null && !Mathf.Approximately(row.Fact.fontSize, GlazeTokens.FontSize(GlazeTokens.Units(Glaze.BodyDegrees))))
                            failures.Add("spatial: project row text must keep its left origin and unscaled metric before composition zoom");
                        row.Words.ForceMeshUpdate(); row.Fact?.ForceMeshUpdate();
                        if (row.Words.textBounds.min.x < -0.001f)
                            failures.Add("spatial: project name mesh extends left of its measured content origin");
                        if (row.Fact != null)
                        {
                            var actual = Mathf.Max(GlazeButton.HeightOf(true), GlazeText.LineHeight(row.Words)
                                + Mathf.Max(1, row.Fact.textInfo.lineCount) * GlazeText.LineHeight(row.Fact)
                                + 3f * GlazeTokens.Units(Glaze.Menu.GridDegrees)) + Glaze.TargetGapMeters / Glaze.Menu.PlaneMeters;
                            if (Mathf.Abs(MenuFrameView.ProjectListLineHeight(row.Model, Glaze.Menu.FileColumnDegrees) - actual) > 0.001f)
                                failures.Add("spatial: project pagination metric disagrees with actual title and summary mesh line heights");
                        }
                    }
                }
                if (multipleProjects)
                {
                    var simultaneous = director.Plane.Shown.SelectMany(shown => shown.View.ShownLines)
                        .Where(row => row.Model.ProjectIdentity).ToList();
                    var keys = simultaneous.Select(row => row.Model.Key).ToList();
                    if (keys.Count < 2 || keys.Distinct().Count() != keys.Count
                        || simultaneous.Any(row => !row.Model.Available || !row.Model.Pressable || !row.Words.gameObject.activeInHierarchy))
                        failures.Add("spatial: multiple-project snapshot did not render at least two distinct current selectable project identities together");
                    foreach (var key in keys)
                    {
                        Press(ProjectsScreens.ChooseProject, key);
                        // Dispatch dirties the navigator; inspect only its newly drawn Tasks frame.
                        Shot("spatial-selected-project-" + key);
                        var tasks = director.Plane.Shown.Select(shown => shown.View.Frame).Where(frame => frame != null).ToList();
                        if (director.Navigator.Place != MenuPlace.Tasks
                            || !tasks.Any(frame => frame!.Lines.Any(line => line.Action == TasksColumn.NewTask && line.Key == key && line.Available))
                            || tasks.SelectMany(frame => frame!.Lines).Any(line => line.Action == TasksColumn.OpenTask
                                && session.State.Workstreams.TryGetValue(line.Key!, out var shownTask) && shownTask.ProjectId != key))
                            failures.Add("spatial: visible project selection opened task controls belonging to a different project");
                        director.Open(MenuPlace.Projects); Draw();
                    }
                    Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                    Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                    Shot("spatial-multiple-projects-selected-task");
                    if (selectedFile?.Now?.Character.WorkstreamId != work.Workstream.WorkstreamId
                        || director.Navigator.BesideTask != work.Workstream.WorkstreamId || sends != 0)
                        failures.Add("spatial: project selection failed to open its exact existing task without sending work");
                    return failures;
                }
                Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                if (!waiting)
                {
                    Press(TasksColumn.ProjectDetails);
                    Shot("spatial-project-details");
                    var details = director.Plane.Shown.Single();
                    if (!director.PressForRender(details.Kind, SidePanel.Close, null, details.View.InPlaceOf, details.View.Side))
                        failures.Add("spatial: actual project details Close did not return navigation");
                    Draw(); Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                }
                Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                if (!waiting) { Press(FileScreens.TellIt); done?.Invoke("Add a regression test for the rate limit."); }
                Shot(question ? "spatial-question-first-part" : waiting ? "spatial-waiting-approval" : "spatial-working-direction");
                if (question && selectedFile != null)
                {
                    void SettledPress(string action, string? key = null)
                    {
                        Draw(); System.Threading.Thread.Sleep(450); Press(action, key); Draw();
                    }
                    var firstFrame = director.Plane.Showing(MenuColumn.File);
                    if (selectedFile.Screen.Question.QuestionPart == null) failures.Add("spatial: long question did not exercise full-question paging");
                    if (firstFrame != null) director.PressForRender(MenuColumn.File, FileScreens.SendAnswer, null, firstFrame, null);
                    if (sends != 0 || selectedFile.Steering.Armed != null || FileScreens.WhySendWaits(selectedFile.Screen) == null)
                        failures.Add("spatial: unread question sent or armed an answer, or lost its reading gate");
                    var questionParts = 0;
                    while (selectedFile.Screen.Question.QuestionPart != null && questionParts++ < 64)
                    {
                        Shot("spatial-question-part-" + questionParts);
                        SettledPress(FileScreens.NextPart, FileScreens.QuestionKey);
                    }
                    if (questionParts < 2 || questionParts >= 64 || selectedFile.Screen.Question.QuestionPart != null)
                        failures.Add("spatial: whole question pager did not finish within its measured bound");
                    Draw();
                    var option = director.Plane.Showing(MenuColumn.File)?.Lines.FirstOrDefault(line => line.Action == FileScreens.Choose && line.Available);
                    if (option?.Key == null) failures.Add("spatial: fully read question has no actual answer choice");
                    else SettledPress(FileScreens.Choose, option.Key);
                    var sideParts = 0;
                    while (selectedFile.Screen.Question.SideOption != null && sideParts++ < 64)
                    {
                        Shot("spatial-question-answer-detail-" + sideParts);
                        if (director.Plane.Composition?.Columns.Count != 1) failures.Add("spatial: task answer details restored a competing chooser rail");
                        var sideView = director.Plane.Shown.Single().View;
                        Draw(); System.Threading.Thread.Sleep(450);
                        var next = sideView.Footer.Showing?.All.FirstOrDefault(slot => slot.Prompt.Id == Footer.NextPage).Prompt;
                        var action = next != null && selectedFile.Screen.Question.SidePart + 1 < selectedFile.Screen.Question.SideParts ? Footer.NextPage : SidePanel.Close;
                        if (!director.PressForRender(MenuColumn.Side, action, null, sideView.InPlaceOf, sideView.Side))
                            failures.Add("spatial: actual full-answer detail refused its current paging/return action");
                        Draw();
                    }
                    if (sideParts < 2 || sideParts >= 64 || selectedFile.Screen.Question.SideOption != null)
                        failures.Add("spatial: full answer detail did not page and return within its measured bound");
                    Shot("spatial-question-returned-from-detail");
                    var beforeReview = director.Plane.Showing(MenuColumn.File);
                    if (beforeReview != null) director.PressForRender(MenuColumn.File, FileScreens.SendAnswer, null, beforeReview, null);
                    if (sends != 0 || selectedFile.Steering.Armed != null || selectedFile.Screen.Question.Reviewing || FileScreens.WhySendWaits(selectedFile.Screen) == null)
                        failures.Add("spatial: selected answer bypassed the mandatory review");
                    SettledPress(FileScreens.NextQuestion);
                    if (!selectedFile.Screen.Question.Reviewing) failures.Add("spatial: Review answers did not open the actual review");
                    var reviewParts = 0;
                    while (selectedFile.Screen.Question.Page + 1 < selectedFile.Screen.Question.Pages && reviewParts++ < 64)
                    {
                        Shot("spatial-question-review-part-" + reviewParts);
                        SettledPress(FileScreens.MoreAnswers);
                    }
                    Shot("spatial-question-reviewed-answer");
                    if (FileScreens.WhySendWaits(selectedFile.Screen) != null) failures.Add("spatial: completed actual review still blocks the answer");
                    SettledPress(FileScreens.SendAnswer);
                    if (sends != 1) failures.Add("spatial: explicit answer Send did not submit exactly once");
                }
                if (!director.Plane.SelectedMarks.Any(mark => mark.Word == StateLanguage.Practice)) failures.Add("spatial: synthetic work lost Practice provenance");
                if (!waiting)
                {
                    // Explicit synthetic snapshot seam: exercise the actual bound project's header,
                    // not a hand-painted breadcrumb, then return to the original fixture snapshot.
                    var originalName = session.State.Projects[work.Workstream.ProjectId].Name;
                    session.State.Projects[work.Workstream.ProjectId].Name = string.Join(" ", Enumerable.Repeat("Long project identity", 8));
                    director.Navigator.Renew(); Shot("spatial-long-project-header");
                    connected = false; Shot("spatial-long-project-disconnected-header");
                    connected = true; session.State.Projects[work.Workstream.ProjectId].Name = originalName;
                    director.Navigator.Renew(); Draw();
                    var shown = ConfirmedDirectorAgent(root.transform, director, eyes, work.Present().Character, failures, "spatial character context");
                    targets.Add(shown.Target); characters.Add(shown); presentation.SetAgentsEnabled(true);
                    director.StageRefreshedForRender();
                    Shot("spatial-working-direction-with-character");
                    if (!shown.View.Label.IdentityInWorkspace || shown.View.Label.Title.gameObject.activeSelf || shown.View.Label.Plate.gameObject.activeSelf
                        || !shown.View.Label.Badge.gameObject.activeInHierarchy || !shown.View.Label.Tags.All(tag => tag.gameObject.activeInHierarchy))
                        failures.Add("spatial: expanded exact task must share its title while retaining character state/provenance");
                    if (director.Plane.LightLine == null) failures.Add("spatial: selected real character has no actual work-file connection");
                    if (director.Plane.LightLine is (Vector3 fromCharacter, Vector3 _) && Vector3.Distance(fromCharacter,
                        shown.Target.BodyPosition + Vector3.up * (CharacterView.BodyExtent * shown.Target.Scale)) > 0.0001f)
                        failures.Add("spatial: shared task identity connector did not start at the actual character body");
                    var retainedFile = selectedFile;
                    var staleFrame = director.Plane.Showing(MenuColumn.File);
                    void Click(GlazeButton button, int pointer)
                    {
                        button.SettleForRender();
                        button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, button.transform.position);
                        button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, button.transform.position);
                        Draw();
                    }
                    Click(director.Plane.Chrome.Targets.Single(button => button.Label.text == "Projects"), 951);
                    if (shown.View.Label.IdentityInWorkspace || !shown.View.Label.Title.gameObject.activeSelf)
                        failures.Add("spatial: character title did not return when Projects replaced its task");
                    if (director.Plane.Composition?.Columns.Count != 2 || new[] { "Projects", "Tasks", "Usage", "Settings" }.Any(words =>
                        !director.Plane.Chrome.Targets.Single(button => button.Label.text == words).gameObject.activeInHierarchy))
                        failures.Add("spatial: actual Projects target did not return the complete chooser navigation");
                    if (director.Navigator.Place != MenuPlace.Projects || retainedFile?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                        failures.Add("spatial: peripheral Projects navigation discarded the local draft");
                    if (staleFrame != null && director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, staleFrame, null))
                        failures.Add("spatial: hidden task retained input authority after place navigation");
                    Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                    Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                    if (!ReferenceEquals(retainedFile, selectedFile) || selectedFile?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                        failures.Add("spatial: reopening the actual task replaced its retained draft");
                    Draw(); // The newly reopened task must actually stand before Move can accept a press.
                    var move = director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.MoveWorkspace);
                    var point = move.transform.position;
                    director.HoldSubjectForRender(point);
                    if (!director.Dragging) failures.Add("spatial: named Move did not begin its real hand drag");
                    director.DragSubjectForRender(eyes + Quaternion.Euler(0f, 4f, 0f) * (point - eyes));
                    director.LetGoForRender(); Draw();
                    var moved = director.Plane.Moved;
                    Click(director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), 952);
                    if (shown.View.Label.IdentityInWorkspace || !shown.View.Label.Title.gameObject.activeSelf)
                        failures.Add("spatial: minimizing retained a hidden character title");
                    if (!presentation.Minimized || director.Plane.Shown.Count != 0 || director.Plane.Chrome.Visible)
                        failures.Add("spatial: Minimize left the reading object or peripheral controls active");
                    if (director.Plane.Bar is MenuBarView dock) Click(dock.Target, 953);
                    else failures.Add("spatial: minimized workspace has no actual restore target");
                    if (presentation.Minimized || director.Plane.Moved != moved || !ReferenceEquals(retainedFile, selectedFile)
                        || selectedFile?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                        failures.Add("spatial: restore lost hand placement, selected task or draft");
                }
                if (sends != (question ? 1 : 0)) failures.Add("spatial: navigation, review or drafting submitted an unexpected command");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static IEnumerable<string> SpatialState(string name, string folder, GameObject root, Camera camera,
            RenderTexture texture, MenuDirector director, List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
        {
            var failures = new List<string>();
            var plane = director.Plane; ForceMeshes(root);
            if (plane.Composition == null || plane.Shown.Count != 1) { failures.Add(name + ": expected one focused reading surface"); return failures; }
            var view = plane.Shown[0].View;
            if (plane.Shown[0].Kind == MenuColumn.File || view.InPlaceOf?.IdentityKind == MenuIdentityKind.Task)
            {
                if (plane.Composition.Columns.Count != 1) failures.Add(name + ": focused work retained the chooser rail column");
                var header = WorkspaceChromeView.SpatialHeader(true);
                if (!Mathf.Approximately(plane.Composition.Header, header) || !Mathf.Approximately(header, GlazeButton.HeightOf(false))
                    || header >= WorkspaceChromeView.SpatialTop)
                    failures.Add(name + ": focused context must reserve exactly its full Projects target, without chooser padding");
                var projects = plane.Chrome.Targets.Single(button => button.Label.text == "Projects");
                var projectCentre = plane.Chrome.transform.InverseTransformPoint(projects.transform.position);
                var bandTop = plane.Chrome.Size.y / 2f; var bandBottom = bandTop - header;
                bool ContainsTarget(float bandHeight) => projects.Size.y >= GlazeButton.HeightOf(false) - 1e-6f
                    && projectCentre.y + projects.Size.y / 2f <= bandTop + 1e-6f
                    && projectCentre.y - projects.Size.y / 2f >= bandTop - bandHeight - 1e-6f;
                if (!ContainsTarget(header))
                    failures.Add(name + ": compact context cropped or reduced its actual Projects target; targetHeight=" + projects.Size.y.ToString("R")
                        + ", header=" + header.ToString("R") + ", centreY=" + projectCentre.y.ToString("R")
                        + ", bandTop=" + bandTop.ToString("R") + ", bandBottom=" + bandBottom.ToString("R") + ", zoom=" + plane.Composition.Zoom.ToString("R"));
                // A real negative against the same actual target: the prior 48dp row cannot
                // contain this ordinary 60dp prompt. Do not weaken the geometry to accept it.
                if (ContainsTarget(MenuPage.Target()))
                    failures.Add(name + ": compact-context negative accepted the former undersized minimum-row band");
                var firstBody = plane.Composition.Parts.First(part => part.Column == 0 && part.Index == 0);
                // PlaneComposition's stack uses tan(full angle), unlike Units' two half angles.
                var partGap = Glaze.MetersAt(PlaneComposition.PartGapDegrees, 1f) * plane.Composition.Zoom;
                var expectedTop = plane.Composition.Height / 2f - header * plane.Composition.Zoom - partGap;
                if (Mathf.Abs(firstBody.Top - expectedTop) > 1e-6f)
                    failures.Add(name + ": focused reading body retained an extra unallocated context gap; actualTop=" + firstBody.Top.ToString("R")
                        + ", expectedTop=" + expectedTop.ToString("R") + ", header=" + header.ToString("R")
                        + ", partGap=" + partGap.ToString("R") + ", zoom=" + plane.Composition.Zoom.ToString("R"));
                foreach (var words in new[] { "Projects", "Tasks", "Usage", "Settings" })
                {
                    var target = plane.Chrome.Targets.Single(button => button.Label.text == words);
                    if (target.gameObject.activeInHierarchy != (words == "Projects"))
                        failures.Add(name + ": focused work has the wrong actual place target visibility: " + words);
                }
            }
            var bodyColumn = plane.Composition.Columns.Count - 1;
            var body = plane.Composition.Parts.First(part => part.Column == bodyColumn && part.Index == 0);
            var shape = new GlazeChecks.PlaneShape("work reading surface", view.Parts[0], new Vector2(body.Width, body.Height) * PlaneComposition.Distance);
            var chrome = new GlazeChecks.PlaneShape("peripheral controls", plane.Chrome.transform, plane.Chrome.Size * plane.Chrome.transform.localScale.x);
            // The chrome bounds deliberately enclose the work, so the old separated-rectangle
            // gap invariant is inapplicable. Text, targets, field and reading orientation retain their thresholds.
            if (Quaternion.Angle(shape.Root.rotation, chrome.Root.rotation) > 0.05f || Mathf.Abs(Vector3.Dot(shape.Root.position - chrome.Root.position, chrome.Root.forward)) > 0.0005f)
                failures.Add(name + ": peripheral controls and reading face do not share their physical plane");
            // A fixed kind caption above the actual name is an intentional header hierarchy.
            // Retain the existing descending type check inside the actual reading content.
            var contentShape = new GlazeChecks.PlaneShape("reading content", view.Content.transform.parent, shape.Size);
            var projectGroups = view.ShownLines.Where(row => row.Model.ProjectIdentity && row.Fact != null).ToList();
            var grouped = new HashSet<TMPro.TMP_Text>();
            foreach (var row in projectGroups)
            {
                var labels = new TMPro.TMP_Text[] { row.Words, row.Fact! };
                foreach (var label in labels) grouped.Add(label);
                failures.AddRange(GlazeChecks.TypeStepsDownLabels(labels, contentShape.Root, eyes, name + " project " + row.Model.Key));
            }
            var ordinary = contentShape.Root.GetComponentsInChildren<TMPro.TMP_Text>(false)
                .Where(label => !grouped.Contains(label) && label.GetComponentInParent<StateBadgeView>() == null).ToList();
            failures.AddRange(GlazeChecks.TypeStepsDownLabels(ordinary, contentShape.Root, eyes, name));
            if (projectGroups.Count > 0)
            {
                // A real-label negative witness: reversing one row's hierarchy must still fail.
                var row = projectGroups[0]; var before = row.Words.fontSize;
                try
                {
                    row.Words.fontSize = row.Fact!.fontSize * 0.8f; row.Words.ForceMeshUpdate();
                    if (!GlazeChecks.TypeStepsDownLabels(new TMPro.TMP_Text[] { row.Words, row.Fact }, contentShape.Root, eyes, "reversed project row").Any())
                        failures.Add(name + ": project-row hierarchy checker accepted a title smaller than its own summary");
                }
                finally { row.Words.fontSize = before; row.Words.ForceMeshUpdate(); }
            }
            failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
            failures.AddRange(GlazeChecks.TargetsLargeEnough(view.Targets.Concat(plane.Chrome.Targets), eyes, name));
            failures.AddRange(GlazeChecks.OneSelectionTreatment(plane.GetComponentsInChildren<Surface>(false), eyes, name));
            var headerTexts = plane.Chrome.GetComponentsInChildren<TMPro.TMP_Text>(false);
            var contextText = headerTexts.SingleOrDefault(label => label.name == "Project and task");
            var connectionText = headerTexts.SingleOrDefault(label => label.name == "Connection state");
            if (contextText != null && connectionText != null && !string.IsNullOrEmpty(contextText.text))
            {
                var contextSeen = GlazeChecks.Of("project context", eyes, contextText.gameObject);
                var connectionSeen = GlazeChecks.Of("connection", eyes, connectionText.gameObject);
                if (contextSeen.Right > connectionSeen.Left - 0.01f) failures.Add(name + ": actual project and connection header text overlap");
            }
            var named = view.Side?.IdentityKind ?? view.Frame?.IdentityKind;
            if (named != null)
            {
                var caption = view.GetComponentsInChildren<TMPro.TMP_Text>(false).SingleOrDefault(label => label.name == "Identity kind");
                if (caption == null || caption.text != (named == MenuIdentityKind.Task ? "Task" : "Project"))
                    failures.Add(name + ": named work identity lacks its fixed host caption");
                var title = view.SubjectShown.Title;
                if (caption != null)
                {
                    var titleSeen = GlazeChecks.Of("named title", eyes, title.gameObject);
                    var captionSeen = GlazeChecks.Of("identity caption", eyes, caption.gameObject);
                    if (captionSeen.Bottom < titleSeen.Top - 0.01f)
                    {
                        var captionBounds = caption.textBounds; var titleBounds = title.textBounds;
                        failures.Add(name + ": identity caption overlaps its name; projected mesh caption bottom=" + captionSeen.Bottom.ToString("0.000")
                            + ", title top=" + titleSeen.Top.ToString("0.000") + "; local TMP caption bounds=" + captionBounds
                            + ", title bounds=" + titleBounds + "; subject positions caption=" + caption.transform.localPosition + ", title=" + title.transform.localPosition);
                    }
                    var titleSize = title.fontSize * title.transform.lossyScale.y / Vector3.Distance(eyes, title.transform.position);
                    var captionSize = caption.fontSize * caption.transform.lossyScale.y / Vector3.Distance(eyes, caption.transform.position);
                    if (titleSize <= captionSize) failures.Add(name + ": named work title does not lead its subordinate caption");
                    foreach (var label in new TMPro.TMP_Text[] { caption, title })
                    {
                        var bounds = label.textBounds;
                        foreach (var x in new[] { bounds.min.x, bounds.max.x })
                        foreach (var y in new[] { bounds.min.y, bounds.max.y })
                        {
                            var point = view.Subject.InverseTransformPoint(label.transform.TransformPoint(new Vector3(x, y, 0f)));
                            if (Mathf.Abs(point.x) > view.SubjectSize.x / 2f + 0.0001f || Mathf.Abs(point.y) > view.SubjectSize.y / 2f + 0.0001f)
                                failures.Add(name + ": named header text exceeds its allocated subject bounds");
                        }
                    }
                }
                if (GlazeRuns.Of(view.SubjectShown.Title).Theirs.Count == 0)
                    failures.Add(name + ": named identity lost outside-text provenance");
            }
            foreach (var character in characters.Where(character => character.View.gameObject.activeInHierarchy))
            {
                var label = GlazeChecks.Of("character label", eyes, character.View.Label.gameObject);
                var bodyExtent = BodyExtent(character.View, eyes);
                foreach (var obstacle in new[] { label, bodyExtent })
                    if (GlazeChecks.OutlineApart(chrome, eyes, obstacle) < GlazeChecks.GapDegrees - 0.01f)
                        failures.Add(name + ": work object or controls overlap the actual character");
            }
            if (plane.LightLine is (Vector3 from, Vector3 to))
            {
                foreach (var character in characters.Where(character => character.View.gameObject.activeInHierarchy))
                {
                    var label = MenuPlane.LabelOutline(character.View);
                    var bodyExtent = BodyExtent(character.View, eyes);
                    for (var step = 1; step < 40; step++)
                    {
                        var at = Vector3.Lerp(from, to, step / 40f);
                        var point = at - eyes;
                        var across = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
                        var up = Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg;
                        var onBody = across >= bodyExtent.Left && across <= bodyExtent.Right && up >= bodyExtent.Bottom && up <= bodyExtent.Top;
                        if (!onBody && !MenuPlane.OnLabel(label, eyes, at)) continue;
                        failures.Add(name + ": task connector crosses a character or its label");
                        break;
                    }
                }
            }
            if (!view.Footer.Fits) failures.Add(name + ": current frame actions do not fit");
            if (ViewField.Current is ViewField field) {
                var corners = new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }.Select(y => chrome.Root.position + chrome.Root.right * x * chrome.Size.x + chrome.Root.up * y * chrome.Size.y));
                failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, chrome.Root.position, WorkspacePlacement.ReadingPitch(plane.Composition.Size, plane.Direction.Elevation, field), field));
            }
            var drawnActions = (view.Footer.Showing?.All.Select(slot => slot.Prompt.Id) ?? Enumerable.Empty<string>()).ToHashSet();
            var carried = view.Side == null ? null : plane.Front!.Footer.InPlace(view.Side);
            foreach (var (_, prompt) in plane.Front!.Footer.All)
            {
                if (prompt.Kind == PromptKind.Close || drawnActions.Contains(prompt.Id)) continue;
                // Match production Footer.InPlace: only actions whose complete subject is
                // visible are carried. Close details must return the original page for the rest.
                if (carried == null || carried.All.Any(slot => slot.Prompt.Id == prompt.Id) || !drawnActions.Contains(SidePanel.Close))
                    failures.Add(name + ": offered action has neither its target nor Close details to return it: " + prompt.Id);
            }
            camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
            var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            return failures;
        }
    }
}
