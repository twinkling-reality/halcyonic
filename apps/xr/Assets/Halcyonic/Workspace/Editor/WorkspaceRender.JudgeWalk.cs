#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>The recorded judge journey in the current independent console, with zero or one optional agent.</summary>
        public static void JudgeWalkCheck()
        {
            var failures = Halcyonic.XR.UI.Editor.GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Builds", "JudgeWalkRenders", variant));
                System.IO.Directory.CreateDirectory(folder);
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                var found = new List<string>();
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    foreach (var withShelf in new[] { false, true })
                    {
                        var name = withShelf ? "shelf-3s" : "workspace-3s";
                        try { found.AddRange(RenderJudgeWalk(name, folder, CharacterStage.DefaultDistance, null, withShelf: withShelf)); }
                        catch (Exception error) { found.Add(name + ": " + error); }
                    }
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: judge walk: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: judge walk: every check passed; " + folder);
                return found;
            });
            UnityEditor.EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>The recording's answers where its playback stands at the end of one node, as the demonstration's reads give them.</summary>
        private sealed class RecordedReads : IIntelligenceReader
        {
            private readonly DemonstrationRecording recording;
            private readonly int node;

            public RecordedReads(DemonstrationRecording recording, int node)
            {
                this.recording = recording;
                this.node = node;
            }

            public Task<IntelligenceRead<UnderstandingResponse>> ReadUnderstandingAsync(string executionId, CancellationToken cancellationToken) =>
                Read(recording.UnderstandingAt(executionId, node, recording.Nodes[node].Events.Count));

            public Task<IntelligenceRead<EvaluationResponse>> ReadEvaluationAsync(string executionId, CancellationToken cancellationToken) =>
                Read(recording.EvaluationAt(executionId, node, recording.Nodes[node].Events.Count));

            private static Task<IntelligenceRead<T>> Read<T>(RecordedAnswer<T>? answer) where T : class => answer == null
                ? Task.FromException<IntelligenceRead<T>>(new UnaskedReadException(DemonstrationReads.NothingRecorded))
                : Task.FromResult(new IntelligenceRead<T>(answer.Response, answer.ReadAt, recorded: true));
        }

        /// <summary>The session as the recording stands at the end of the last node of <paramref name="path"/>, and its activity.</summary>
        private static (ClientProjection State, ActivityLog Activity) RecordedAt(DemonstrationRecording recording, IEnumerable<int> path)
        {
            var state = new ClientProjection();
            var activity = new ActivityLog();
            state.ApplyWelcome(recording.Welcome);
            state.ApplySnapshot(recording.Snapshot.Snapshot, new StateChanges());
            foreach (var node in path)
            {
                foreach (var recorded in recording.Nodes[node].Events)
                {
                    var changes = new StateChanges();
                    state.ApplyEvent(recorded.Message, changes);
                    activity.Record(changes.Events);
                }
            }
            return (state, activity);
        }

        /// <summary>The node an answer of <paramref name="kind"/> leads to where <paramref name="node"/> holds at its end.</summary>
        private static int After(DemonstrationRecording recording, int node, DemonstrationAnswerKind kind, string? label = null) =>
            recording.Nodes[node].BranchesAfter(recording.Nodes[node].Events.Count)
                .First(branch => branch.Answer.Kind == kind && (label == null || branch.Answer.Label == label)).Node;

        /// <summary>
        /// A judge's walk through the menu (ADR 0026) on the recorded demonstration, from the eyes, drawn
        /// by the menu's own director: Projects at launch, its retained Minimize/Restore dock,
        /// Tasks with the waiting task first, its file at the
        /// agent's question, an answer chosen, the approval's request part by part and its Yes, Checks,
        /// Tell it's recorded instructions, Usage's recorded limits and Settings, then the dock again. The
        /// recording stands where each step is reached; each state is held to the plane's checks and saved.
        /// Nothing is sent: in a render the file's host has no session, so a press only arms or chooses.
        /// </summary>
        private static IEnumerable<string> RenderJudgeWalk(string name, string folder, float radius, float? surfaceDrop, bool usageOnly = false, bool withShelf = false)
        {
            var failures = new List<string>();
            var root = new GameObject("Judge walk render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                var recording = DemonstrationRecording.Parse(asset.text);
                Resources.UnloadAsset(asset);
                var asked = After(recording, 0, DemonstrationAnswerKind.Answer, "15 minutes");
                var approved = After(recording, asked, DemonstrationAnswerKind.Approve);
                var (state, activity) = RecordedAt(recording, new[] { 0 });
                var reads = new RecordedReads(recording, 0);
                var instructions = new Dictionary<string, IReadOnlyList<PresetInstruction>>();

                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                // Work remains in the recorded project; the console needs no character or stage slot.
                var tasks = state.Workstreams.Values.OrderBy(task => task.CreatedAt).ToList();
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var targets = new List<CharacterTarget>();
                var directed = tasks.Single(task => task.Title == "Add rate limiting to the sign-in endpoint").WorkstreamId;
                var presentation = new WorkspacePresentationState();
                Transform? shelf = null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                FileColumn? file = null;
                NewProjectFlow? flow = null;
                var companionAsset = Resources.Load<TextAsset>(CompanionRecording.ResourceName);
                var companion = CompanionRecording.Parse(companionAsset.text);
                Resources.UnloadAsset(companionAsset);
                WorkspacePresentation? Present(string task) =>
                    state.Workstreams.TryGetValue(task, out var workstream) ? WorkspacePresenter.Present(workstream, state, activity, true) : null;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    Commands = commands,
                    Overview = () => WorkOverview.Of(state, new StageVisibility(), _ => true),
                    Comfort = comfort,
                    RecordedUsage = at => recording.UsageLimitsAt(at),
                    File = (host, task) => file = new FileColumn(host, () => Present(task), commands, () => reads,
                        execution => instructions.TryGetValue(execution, out var offered) ? offered : null, () => ""),
                    // Lane C's flow, playing the companion's recording, keeping nothing on this machine.
                    MakeNewProject = host => flow = new NewProjectFlow(host, commands, new KeptInMemory(), null, companion),
                    // Your space as the release build gives it: no Your computer.
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null), _ => { }, _ => { }),
                    Demonstration = () => true,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward,
                        presentation.AgentsVisible ? targets : new List<CharacterTarget>(), null, false),
                    CharacterOf = task => presentation.AgentsVisible ? targets.FirstOrDefault(target => target.View.WorkstreamId == task) : null,
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => state.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou),
                });
                var navigator = director.Navigator;
                presentation.Changed += () => { if (shelf != null) shelf.gameObject.SetActive(presentation.AgentsVisible); };
                director.DrawNow();
                if (withShelf)
                {
                    var agent = ConfirmedPlaneAgent(root.transform, director.Plane, eyes,
                        CharacterPresenter.Present(state.Workstreams[directed], state, true), failures, name);
                    characters.Add(agent);
                    targets.Add(agent.Target);
                    shelf = agent.View.transform.parent;
                    presentation.SetAgentsEnabled(true);
                    director.DrawNow();
                }
                bool Press(MenuColumn from, string action, string? key = null)
                {
                    director.DrawNow();
                    var taken = navigator.Act(from, action, key, director.Plane.Showing(from), null);
                    director.DrawNow();
                    return taken;
                }
                MenuFrame? Shown(MenuColumn column) => director.Plane.Showing(column);
                // The characters show the work as the recording stands.
                void Stand()
                {
                    foreach (var (view, _) in characters) view.Show(CharacterPresenter.Present(state.Workstreams[view.WorkstreamId], state, true));
                }
                void Shot(string step, bool lightLine = true) =>
                    failures.AddRange(PlaneState(name + " judge " + step, folder, camera, texture, director.Plane,
                        characters.Where(each => each.View.gameObject.activeInHierarchy).ToList(), eyes, null, lightLine && presentation.AgentsVisible));
                // A side panel's Close details, through its own column: with text a step larger it stands in its frame's place.
                void CloseSide()
                {
                    var side = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View?.Side;
                    if (side == null) return;
                    navigator.Act(MenuColumn.Side, SidePanel.Close, null, null, side);
                    director.DrawNow();
                }
                // A part's or a page's row turns nothing until what it shows has stood a moment.
                void Settle() => Thread.Sleep(TimeSpan.FromSeconds(0.45));

                // Prove Settings' actual reachable change and restore before the file opens. A lone-file
                // approval budget can legitimately hide the menu, so no hidden Settings target is pressed.
                void ReachableTextSize()
                {
                    var pass = comfort.Text;
                    if (!Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings))
                        || !OpenSettingsCategoryForRender(director, Comfort.Heading)
                        || !Press(MenuColumn.Menu, SettingsColumn.OpenSetting, "text-size"))
                        failures.Add(name + ": the visible Settings and Comfort route offered no text size.");
                    for (var changeAt = 0; changeAt < 2; changeAt++)
                    {
                        director.DrawNow();
                        var change = navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Footer[PromptSlot.FarRight];
                        var side = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View?.Side;
                        var inPlace = Shown(MenuColumn.Menu) == null && side != null;
                        if (change == null || !(inPlace
                            ? navigator.Act(MenuColumn.Side, change.Id, null, null, side)
                            : navigator.Act(MenuColumn.Menu, change.Id, null, Shown(MenuColumn.Menu), null)))
                            failures.Add(name + ": the text size's visible " + (changeAt == 0 ? "change" : "restore") + " took no press.");
                        var expected = changeAt == 0 ? (pass == TextSize.Larger ? TextSize.Standard : TextSize.Larger) : pass;
                        if (comfort.Text != expected) failures.Add(name + ": the visible text-size action did not reach its expected size.");
                        GlazeText.SetScale(comfort.TextScale);
                        navigator.Tick();
                        director.DrawNow();
                        Shot("2 comfort " + (changeAt == 0 ? "changed" : "restored"));
                    }
                    if (comfort.Text != pass) failures.Add(name + ": the visible Comfort restore did not restore the pass's size.");
                    CloseSide();
                    // Settings retains its category across minimize and navigation. Return through
                    // the leaf's actual Back control so the later Your space route starts at the index.
                    if (!Press(MenuColumn.Menu, SettingsHubColumn.Back))
                        failures.Add(name + ": Comfort offered no visible Back to the Settings index.");
                    director.CloseMenu();
                    director.Open(MenuPlace.Tasks);
                    director.DrawNow();
                }

                // Defensive model reflow witness, not a reachable Settings press while the file hides the
                // menu. Tick the same retained FileColumn before any draw at either changed size; the
                // original read/Yes invalidation assertions remain independent of physical visibility.
                void ChangeTextSize(string step, Action atNewSize, Action backAtPass)
                {
                    var pass = GlazeText.Scale;
                    var retained = navigator.Beside;
                    try
                    {
                        Debug.Log("Halcyonic: judge render: direct Comfort-model transition checks retained request reflow before drawing.");
                        comfort.Text = pass > 1f ? TextSize.Standard : TextSize.Larger;
                        GlazeText.SetScale(comfort.TextScale);
                        navigator.Tick();
                        atNewSize();
                        director.DrawNow();
                        navigator.Tick();
                        director.DrawNow();
                        if (navigator.Place != MenuPlace.Tasks || Shown(MenuColumn.File) == null)
                        {
                            failures.Add(name + ": after the text size changed, the menu stands on " + navigator.Place + (Shown(MenuColumn.File) == null ? " with no file shown" : "") + ", not Tasks beside the file.");
                        }
                        Shot(step + " direct model text " + (comfort.Text == TextSize.Larger ? "larger" : "standard"));
                    }
                    finally
                    {
                        comfort.Text = pass > 1f ? TextSize.Larger : TextSize.Standard;
                        GlazeText.SetScale(pass);
                    }
                    navigator.Tick();
                    backAtPass();
                    // This checks reflow of the same active confirmation. Global minimize is a
                    // separate lifecycle boundary that correctly withdraws its armed approval.
                    if (!ReferenceEquals(navigator.Beside, retained) || file?.Steering.Armed != WorkspaceAction.Approve)
                        failures.Add(name + ": text-size reflow did not retain the same active approval review.");
                    director.DrawNow();
                }

                // Usage evidence may need parts once the in-place heading carries named controls.
                void ReadUsageParts(string step)
                {
                    director.DrawNow();
                    var first = navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Side;
                    if (first == null) { failures.Add(name + ": the chosen Usage limit offered no details."); return; }
                    var parts = first.Parts?.Parts ?? 1;
                    var facts = new List<SideFact>();
                    for (var part = 0; part < parts; part++)
                    {
                        var frame = navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu!;
                        var details = frame.Side!;
                        facts.AddRange(details.Facts);
                        if (details.Source != first.Source || details.Subject != first.Subject)
                            failures.Add(name + ": Usage detail paging changed the chosen limit or its source.");
                        if (parts > 1 && details.Parts != (part, parts))
                            failures.Add(name + ": Usage detail parts did not follow their actual offered order.");
                        var shot = step + (part == 0 ? "" : " part " + (part + 1));
                        Shot(shot);
                        if (usageOnly)
                        {
                            // A second capture of the unchanged scene must keep its stage, title and panels.
                            var original = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                            ImageConversion.LoadImage(original, System.IO.File.ReadAllBytes(System.IO.Path.Combine(folder, (name + " judge " + shot).Replace(' ', '-') + ".png")));
                            var rotation = camera.transform.rotation;
                            camera.transform.rotation = PlaneLayout.Facing(director.Plane.Direction);
                            // Prime stale near depth and disable the camera's incidental clear for this one witness.
                            // The capture helper must produce the complete scene from a reused target itself.
                            var flags = camera.clearFlags;
                            var active = RenderTexture.active;
                            RenderTexture.active = texture;
                            GL.Clear(true, true, camera.backgroundColor, 0f);
                            RenderTexture.active = active;
                            camera.clearFlags = CameraClearFlags.Nothing;
                            var repeated = Render(camera, texture);
                            camera.clearFlags = flags;
                            camera.transform.rotation = rotation;
                            var (changed, _) = Compare(original, repeated, new RectInt(0, 0, Size, Size));
                            if (changed > 0) failures.Add(name + ": repeating unchanged Usage part " + (part + 1) + " lost or changed " + changed + " pixels.");
                            UnityEngine.Object.DestroyImmediate(original);
                            UnityEngine.Object.DestroyImmediate(repeated);
                        }
                        if (parts <= 1) continue;
                        var side = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View?.Side;
                        var inPlace = Shown(MenuColumn.Menu) == null && side != null;
                        var footer = inPlace ? frame.Footer.InPlace(details) : frame.Footer;
                        if (footer[PromptSlot.FarRight]?.Id != Footer.NextPage || !(inPlace
                            ? navigator.Act(MenuColumn.Side, Footer.NextPage, null, null, side)
                            : navigator.Act(MenuColumn.Menu, Footer.NextPage, null, Shown(MenuColumn.Menu), null)))
                            failures.Add(name + ": Usage details refused their actually offered current pager.");
                        director.DrawNow();
                    }
                    if (facts.Select(fact => fact.Name).SequenceEqual(new[] { UsageText.AgentApp, UsageText.Seen, UsageText.Resets, UsageText.Account }) == false
                        || facts.LastOrDefault()?.Value != UsageText.PartOfTheRecording)
                        failures.Add(name + ": the recorded limit's complete evidence did not retain Agent app, Seen, Resets and Account provenance.");
                }
                if (usageOnly)
                {
                    director.Open(MenuPlace.Usage);
                    director.DrawNow();
                    if (!Press(MenuColumn.Menu, UsageColumn.OpenLimit, "0")) failures.Add(name + ": the visible Usage limit refused its press.");
                    ReadUsageParts("usage details");
                    return failures;
                }

                // The production workspace opens on Projects, independent of optional characters.
                director.DrawNow();
                if (navigator.Place != MenuPlace.Projects || navigator.IsCompact || !director.Plane.Chrome.Visible)
                    failures.Add(name + ": the judge walk did not launch into the complete Projects workspace.");
                if (TasksColumn.Bar(MenuPlace.Tasks, state).ClosedLine != TasksText.Waiting(1)) failures.Add(name + ": the workspace attention does not say a task waits.");
                Shot("1 projects", lightLine: false);
                MinimizeForDirectorRender(director, failures, name);
                if (characters.Any(each => each.View.gameObject.activeInHierarchy)) failures.Add(name + ": Minimize left an optional agent visible.");
                Shot("1 minimized", lightLine: false);
                RestoreDockForDirectorRender(director, 730, failures, name);
                if (navigator.Place != MenuPlace.Projects) failures.Add(name + ": Restore changed the judge's retained Projects selection.");
                if (withShelf && characters.Any(each => !each.View.gameObject.activeInHierarchy)) failures.Add(name + ": Restore did not restore the optional agent shelf.");
                Shot("1 restored projects", lightLine: false);

                // Opened while something waits: Tasks, the waiting task first.
                if (!Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Tasks)))
                    failures.Add(name + ": the visible Tasks section took no press from Projects.");
                director.DrawNow();
                if (navigator.Place != MenuPlace.Tasks) failures.Add(name + ": the menu opened on " + navigator.Place + " while a task waits.");
                if (Shown(MenuColumn.Menu)?.Lines.FirstOrDefault()?.Key != directed) failures.Add(name + ": the waiting task is not Tasks' first row.");
                Shot("2 tasks");
                ReachableTextSize();

                // Its row opens its file on Waiting, at the agent's question; an answer chosen.
                if (!Press(MenuColumn.Menu, TasksColumn.OpenTask, directed) || file == null) failures.Add(name + ": the waiting task's row opened no file.");
                Shot("3 question");
                // QuestionParts can shrink after the last part is drawn and its missing-read
                // reason gives way, while that actual footer still awaits See answers.
                var questionPageLimit = Math.Max(1, (int)Math.Ceiling(file!.Screen.Question.QuestionMeasured(0) / (double)file.Screen.Question.QuestionPartRows)) + 1;
                for (var turn = 0; turn < questionPageLimit && Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight]?.Id == FileScreens.NextPart; turn++)
                {
                    Settle();
                    if (!Press(MenuColumn.File, FileScreens.NextPart))
                    {
                        failures.Add(name + ": the visible question-part footer took no press.");
                        break;
                    }
                }
                if (file.Screen.Question.QuestionPart != null) failures.Add(name + ": the question-only page never reached its visible answers.");
                for (var turn = 0; turn < file.Screen.Question.Pages && Shown(MenuColumn.File)?.Lines.Any(line => line.Action == FileScreens.Choose && line.Key == "0") != true; turn++)
                {
                    Settle();
                    Press(MenuColumn.File, FileScreens.MoreAnswers);
                }
                if (!Press(MenuColumn.File, FileScreens.Choose, "0")) failures.Add(name + ": the first visible answer took no choice press.");
                // A cut offered choice is read on its actual detail surface, then closed. Reading
                // proof comes from the director's rendered-view callbacks, never a fabricated draw.
                for (var part = 0; part < file.Screen.Question.SideParts && file.Screen.Question.SideOption != null; part++)
                {
                    director.DrawNow();
                    var sideView = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View;
                    if (sideView?.Side == null) { failures.Add(name + ": the selected cut answer has no visible details."); break; }
                    Settle();
                    if (file.Screen.Question.SidePart + 1 >= file.Screen.Question.SideParts)
                    {
                        if (!navigator.Act(MenuColumn.Side, SidePanel.Close, null, director.Plane.Showing(MenuColumn.Side), sideView.Side)) failures.Add(name + ": read details took no Close press.");
                        director.DrawNow();
                        break;
                    }
                    var next = sideView.Footer.Showing.All.FirstOrDefault(each => each.Prompt.Id == Footer.NextPage).Prompt;
                    if (next == null || !navigator.Act(MenuColumn.Side, next.Id, null, director.Plane.Showing(MenuColumn.Side), sideView.Side))
                    { failures.Add(name + ": the visible detail pager took no press."); break; }
                    director.DrawNow();
                }
                var answerPage = Shown(MenuColumn.File);
                if (answerPage?.Footer[PromptSlot.FarRight]?.Id != FileScreens.NextQuestion || answerPage.Footer[PromptSlot.FarRight]?.Available != true)
                    failures.Add(name + ": the chosen and read answer offers no next review step.");
                if (answerPage?.Footer.All.Any(each => each.Prompt.Id == FileScreens.SendAnswer) == true) failures.Add(name + ": Send answer appears before mandatory review.");
                Shot("4 answer chosen");
                Settle();
                if (!Press(MenuColumn.File, FileScreens.NextQuestion) || !file.Screen.Question.Reviewing) failures.Add(name + ": the chosen answer opened no mandatory review.");
                for (var part = 0; part < file.Screen.Question.Pages && Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight]?.Id == FileScreens.MoreAnswers; part++)
                {
                    Settle();
                    if (!Press(MenuColumn.File, FileScreens.MoreAnswers)) { failures.Add(name + ": the visible review pager took no press."); break; }
                }
                director.DrawNow();
                if (Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight]?.Id != FileScreens.SendAnswer || Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight]?.Available != true)
                    failures.Add(name + ": the rendered whole review did not offer Send answer.");
                Shot("4 answers reviewed");

                // Where the recording stands after that answer: the approval, its request in parts, and Yes only after the last.
                (state, activity) = RecordedAt(recording, new[] { 0, asked });
                reads = new RecordedReads(recording, asked);
                Stand();
                Press(MenuColumn.File, Footer.Close);
                Press(MenuColumn.Menu, TasksColumn.OpenTask, directed);
                Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Waiting));
                Shot("5 approval");
                Press(MenuColumn.File, FileScreens.Approve);
                if (Shown(MenuColumn.File)?.Footer[PromptSlot.Free] != null && file!.Screen.RequestParts > 1) failures.Add(name + ": Yes shows on the request's first part.");
                Shot("6 request part 1");
                // Text a step larger, or back, with the request half read: it is read again from its first part, no Yes until it is.
                Settle();
                Press(MenuColumn.File, FileScreens.NextPart, FileScreens.RequestKey);
                ChangeTextSize("6", () =>
                {
                    if (file!.Screen.RequestPart != 0 || file.Steering.CanConfirm)
                    {
                        failures.Add(name + ": at the new text size, before anything was drawn at it, the request half read shows its part " + (file.Screen.RequestPart + 1)
                            + (file.Steering.CanConfirm ? " and Yes may be pressed" : "") + ", not its first part to read again.");
                    }
                }, () =>
                {
                    if (file!.Screen.RequestPart != 0 || file.Steering.CanConfirm) failures.Add(name + ": back at the walk's text size, the request is not read again from its first part.");
                });
                var approvalPartLimit = Math.Max(1, file!.Screen.RequestParts) + 1;
                for (var part = 0; part < approvalPartLimit && Shown(MenuColumn.File)?.Footer[PromptSlot.Free] == null; part++)
                {
                    Settle();
                    var beforePart = file.Screen.RequestPart;
                    if (!Press(MenuColumn.File, FileScreens.NextPart, FileScreens.RequestKey)) failures.Add(name + ": the request's next part took no press.");
                    if (file.Screen.RequestParts > 1 && file.Screen.RequestPart == beforePart && !file.Steering.CanConfirm)
                    { failures.Add(name + ": the rendered approval request refused to advance from its current read part."); break; }
                }
                if (Shown(MenuColumn.File)?.Footer[PromptSlot.Free]?.Words != "Yes, approve") failures.Add(name + ": the whole request read, there is no Yes, approve.");
                Shot("7 yes");

                // Where it stands once the approved turn ended: Checks, then Tell it with the recorded instructions.
                (state, activity) = RecordedAt(recording, new[] { 0, asked, approved });
                reads = new RecordedReads(recording, approved);
                Stand();
                var execution = state.CurrentExecution(state.Workstreams[directed])!.ExecutionId;
                instructions[execution] = recording.Nodes[approved].BranchesAfter(recording.Nodes[approved].Events.Count)
                    .Where(branch => branch.Answer.Kind == DemonstrationAnswerKind.Instruct)
                    .Select(branch => new PresetInstruction(branch.Answer.Label!, branch.Answer.Text!)).ToList();
                Press(MenuColumn.File, FileScreens.Cancel);
                Press(MenuColumn.File, Footer.Close);
                Press(MenuColumn.Menu, TasksColumn.OpenTask, directed);
                Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Checks));
                for (var tick = 0; tick < 20 && Shown(MenuColumn.File)?.Lines.FirstOrDefault()?.Words.StartsWith("Tests failed", StringComparison.Ordinal) != true; tick++)
                {
                    navigator.Tick();
                    director.DrawNow();
                }
                if (Shown(MenuColumn.File)?.Source?.StartsWith("Simulated checks", StringComparison.Ordinal) != true) failures.Add(name + ": Checks does not show the recording's simulated checks.");
                Shot("8 checks");
                Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity));
                Press(MenuColumn.File, FileScreens.TellIt);
                var rows = Shown(MenuColumn.File)?.Lines.Count(line => line.Action == FileScreens.Preset) ?? 0;
                if (rows != instructions[execution].Count) failures.Add(name + ": Tell it offers " + rows + " recorded instructions, not " + instructions[execution].Count + ".");
                Press(MenuColumn.File, FileScreens.Preset, "0");
                Shot("9 tell it");

                // Usage, a limit's side panel open; Settings, a setting chosen.
                Press(MenuColumn.File, Footer.Close);
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage));
                Press(MenuColumn.Menu, UsageColumn.OpenLimit, "0");
                // With text a step larger the side panel takes its frame's place; it is the menu's either way.
                ReadUsageParts("10 usage");
                CloseSide();
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                if (!OpenSettingsCategoryForRender(director, SettingsText.YourSpace))
                    failures.Add(name + ": Settings offered no visible Your space category.");
                Press(MenuColumn.Menu, SettingsColumn.OpenSetting, SpaceSettings.SwitchSpace);
                if (navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Lines.FirstOrDefault()?.Words != SettingsText.YourSpace) failures.Add(name + ": Settings does not open on Your space.");
                Shot("11 settings");
                CloseSide();

                // New project from Projects: the companion's recorded questions, leaning as its own, then its recap, which can't start here.
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Projects));
                Press(MenuColumn.Menu, ProjectsScreens.NewProject);
                Press(MenuColumn.File, NewProjectScreens.BeginCompanion);
                if (flow?.Step != NewProjectStep.Questions) failures.Add(name + ": Talk it through did not bring the companion's recorded questions.");
                else if (Shown(MenuColumn.File)?.Lines.Any(line => line.Runs.AllTheirs && line.Action == null) != true)
                {
                    failures.Add(name + ": the companion's words don't lean as its own.");
                }
                Shot("12 new project questions", lightLine: false);
                if (flow?.Idea?.Companion is CompanionExchange exchange && companion.RecordedAnswer(exchange) is string answer)
                {
                    // Where the page holds less than the question and its answers, they follow under it by More answers: turn to them.
                    var key = NewProjectScreens.AnswerKey(exchange.Generation, answer);
                    for (var turn = 0; turn < 4 && Shown(MenuColumn.File)?.Lines.Any(line => line.Action == NewProjectScreens.ChooseSuggestion && line.Key == key) != true; turn++)
                    {
                        var more = Shown(MenuColumn.File)?.Lines.FirstOrDefault(line => line.Action == NewProjectScreens.MoreAnswers);
                        if (more == null) break;
                        Press(MenuColumn.File, NewProjectScreens.MoreAnswers, more.Key);
                    }
                    Press(MenuColumn.File, NewProjectScreens.ChooseSuggestion, key);
                    Press(MenuColumn.File, NewProjectScreens.SendAnswer);
                }
                if (flow?.Step != NewProjectStep.Recap) failures.Add(name + ": the recorded answer did not bring the recap.");
                var start = Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight];
                if (start?.Id != NewProjectScreens.StartBuilding || start.Available) failures.Add(name + ": the demonstration's recap offers a start it can't make.");
                if (Shown(MenuColumn.File)?.Source != CompanionText.Note) failures.Add(name + ": the recap does not say the companion is an AI that can be wrong.");
                Shot("13 new project recap", lightLine: false);
                // Every page of the recap, each held to the plane's checks, as a judge turns them with the footer's Next page.
                for (var page = 2; page < 10; page++)
                {
                    var next = Shown(MenuColumn.File)?.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Kind == PromptKind.NextPage);
                    if (next == null || next.Words.StartsWith("First page", StringComparison.Ordinal)) break;
                    Press(MenuColumn.File, Footer.NextPage);
                    Shot("13 new project recap page " + page, lightLine: false);
                }
                Press(MenuColumn.File, Footer.Close);

                // The whole workspace minimizes to its single retained Restore dock.
                MinimizeForDirectorRender(director, failures, name);
                director.DrawNow();
                Shot("14 minimized", lightLine: false);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}
