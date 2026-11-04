#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>The demonstration's directed task, the one that asks, asks for approval, fails its checks and is corrected.</summary>
        private const string JudgeDirectedTitle = "Add rate limiting to the sign-in endpoint";

        /// <summary>Names a judge must not read on any frame, as JudgeWordsTests lists them.</summary>
        private static readonly string[] JudgeBrands =
        {
            "Claude", "Anthropic", "OpenAI", "Codex", "OpenCode", "GPT", "Gemini", "Llama", "Qwen", "Ollama", "Mistral",
            "Meta", "Quest", "Oculus", "Horizon", "Apple", "Mac", "macOS", "iPhone", "Android", "Google", "YouTube",
            "GitHub", "Microsoft", "Windows", "Unity", "Salidium", "Seorak", "Omnorium", "Stripe", "Postgres", "PostgreSQL",
            "Redis", "Docker", "AWS", "npm", "React",
        };

        /// <summary>
        /// The judge build's path as the release player runs it: no token and no control plane, so the demonstration
        /// fallback plays the bundled recording (and the recorded run first, when one is bundled); the workspace's director
        /// set up as the release WorkspaceDirector sets it up, spatial, with the kit on by default (KIT-ON). A judge's walk,
        /// seated and hands only: Projects at launch, the closed workspace saying a task waits, Tasks with the waiting task
        /// first, the question read and Send answer, the request read part by part with Yes only after its last part, the
        /// failed checks, Tell it and the correction round, the passing checks, the end and the restart, New project's
        /// companion, Usage and Settings. Each press goes through the kit's own controls where the kit draws the file, and
        /// through the visible frame otherwise; each shot is held to the kit's checks where the kit draws and to the
        /// spatial workspace's otherwise, inside a Quest 3S field at both reading sizes, and every label drawn is scanned
        /// for brand names. Renders land in Builds/JudgeKitRenders.
        /// </summary>
        public static void JudgeKitWalkCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "JudgeKitRenders", variant));
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder, "*.png")) File.Delete(old);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var found = new List<string>();
                if (!KitWorkspaceHost.Enabled) found.Add("judge kit: the kit is not on by default");
                kitShots = 0; kitContextWitnessed = false; kitCompactWitnessed = false; kitComposerWitness = false; kitFallbacks = 0;
                spatialProductGaps.Clear();
                ViewField.Current = FieldChecks.Quest3S;
                var shots = 0;
                // The player loop runs what awaited on the main thread between frames; this walk holds the Editor's main thread
                // throughout, so the session's acknowledgements continue on the thread pool instead, as in the client core's tests.
                var context = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);
                try { found.AddRange(RenderJudgeKitWalk(folder, out shots)); }
                catch (Exception error) { found.Add("judge kit: " + error); }
                finally { SynchronizationContext.SetSynchronizationContext(context); ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                if (kitShots == 0) found.Add("judge kit: the kit never drew the task's file");
                foreach (var failure in found) Debug.LogError("Halcyonic: judge kit: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: judge kit: every check passed; " + shots + " shots, " + kitShots + " drawn by the kit; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderJudgeKitWalk(string folder, out int shotCount)
        {
            var failures = new List<string>();
            var root = new GameObject("Judge kit walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            DemonstrationFallback? fallback = null;
            var shots = 0;
            try
            {
                // As ControlPlaneConnection loads it with no control plane configured: the recorded run first where the build
                // has one, the demonstration after it (looping only when it plays alone). Played fast: the holds that wait
                // for an answer still wait, and the timed ones still wait while a task's file is open (Reading).
                DemonstrationPlayer? Load(string resource, bool recordedRun, bool loop)
                {
                    var asset = Resources.Load<TextAsset>(resource);
                    if (asset == null) return null;
                    var recording = DemonstrationRecording.Parse(asset.text);
                    Resources.UnloadAsset(asset);
                    if (recording.IsRecordedRun != recordedRun) failures.Add("judge kit: " + resource + " holds the wrong kind of recording");
                    // The recorded run at ten times its pace (its 2 min 14 s in 13 s, its end hold 2 s), so the walk sees it play and
                    // give way; the demonstration fast, its holds for an answer still waiting.
                    return new DemonstrationPlayer(recording, client, new DemonstrationOptions { Speed = recordedRun ? 10 : 1000, Loop = loop });
                }
                var hasRecordedRun = Resources.Load<TextAsset>("HalcyonicRecordedRun") != null;
                fallback = new DemonstrationFallback(null,
                    () => Load("HalcyonicDemonstration", recordedRun: false, loop: !hasRecordedRun),
                    hasRecordedRun ? () => Load("HalcyonicRecordedRun", recordedRun: true, loop: false) : null);
                fallback.Start();

                var activity = new ActivityLog();
                var submissions = new CommandSubmissions { Demonstration = () => fallback.Line != null };
                var commands = new CommandFactory(client);
                var presentation = new WorkspacePresentationState();
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                string? journal = null;
                RealtimeSession? Session() => fallback.Current;
                ClientProjection? State() => fallback.Current?.State;
                WorkspacePresentation? Present(string task)
                {
                    var session = Session();
                    return session != null && session.State.Workstreams.TryGetValue(task, out var work)
                        ? WorkspacePresenter.Present(work, session.State, activity, session.Status.IsLive, submissions) : null;
                }
                static string ExecutionOf(CommandEnvelope command) => command switch
                {
                    ExecutionRespondToApprovalCommand approval => approval.Payload.ExecutionId,
                    ExecutionAnswerQuestionCommand answer => answer.Payload.ExecutionId,
                    ExecutionInterruptCommand interrupt => interrupt.Payload.ExecutionId,
                    ExecutionSendInstructionCommand instruction => instruction.Payload.ExecutionId,
                    _ => "",
                };
                var sent = new List<CommandEnvelope>();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    // WorkspaceDirector.Start's setup, as a release build gives it.
                    SpatialWorkspace = true,
                    ResponsiveWorkspace = true,
                    Presentation = presentation,
                    InitialPlacementReady = () => true,
                    ConnectionTarget = () => null,
                    State = State,
                    Connected = () => Session()?.Status.IsLive == true,
                    Session = Session,
                    Submit = (madeIn, command) =>
                    {
                        if (!ReferenceEquals(madeIn, Session()) || !madeIn.Status.IsLive) return null;
                        sent.Add(command);
                        return submissions.Submit(each => madeIn.SubmitAsync(each), command, ExecutionOf(command));
                    },
                    Api = () => null,
                    Demonstration = () => fallback.Line != null,
                    KeyboardOffered = () => false,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                    Bar = place => TasksColumn.Bar(place, State()),
                    SomethingWaits = () => State()?.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou) == true,
                    Comfort = comfort,
                    // Your space alone: the release build pairs nothing and shows no boundary.
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null), _ => { }, _ => { }),
                    Commands = commands,
                    Overview = () => State() is ClientProjection state ? WorkOverview.Of(state, new StageVisibility(), _ => true) : null,
                    RecordedUsage = now => fallback.Line != null ? fallback.UsageLimitsAt(now) : null,
                    MakeNewProject = host => NewProjectColumn.Create(host, commands),
                    File = (host, task) => Present(task) == null ? null : new FileColumn(host, () => Present(task), commands,
                        () => fallback.Player?.Reads, execution => fallback.InstructionsFor(execution), () => ""),
                });
                director.Open();
                var navigator = director.Navigator;
                var plane = director.Plane;

                // A frame of the game loop: the session pumped and its events kept, the timed holds told whether anything is open.
                void Pump()
                {
                    if (fallback.Player is DemonstrationPlayer player) player.Reading = !navigator.IsCompact && navigator.Beside != null; // As WorkspaceDirector.FollowBeside tells it.
                    var changes = fallback.Pump();
                    if (changes.Resynchronized && (State()?.Journal?.JournalId != journal || changes.Rewound))
                    {
                        activity.Clear();
                        submissions.Clear();
                        journal = State()?.Journal?.JournalId;
                    }
                    activity.Record(changes.Events);
                }
                void Draw() { Pump(); KitDraw(director); }
                bool Until(Func<bool> done, string what, double seconds = 30)
                {
                    var until = DateTime.UtcNow.AddSeconds(seconds);
                    while (DateTime.UtcNow < until)
                    {
                        Pump();
                        if (done()) { Draw(); return true; }
                        Thread.Sleep(20);
                    }
                    failures.Add("judge kit: waited " + seconds + " s and " + what + " never came");
                    return false;
                }
                WorkstreamView? Directed() => State()?.Workstreams.Values.FirstOrDefault(task => task.Title == JudgeDirectedTitle);
                ExecutionView? DirectedRun() => Directed() is WorkstreamView task ? State()!.CurrentExecution(task) : null;
                FileColumn? File() => navigator.Beside as FileColumn;
                MenuFrame? FileFrame() => plane.KitShows ? plane.KitBinding?.Frame : plane.Showing(MenuColumn.File);

                string Visible()
                {
                    var words = new List<string>();
                    if (plane.KitShows && plane.KitBinding is KitFileBinding binding)
                        words.Add("kit: " + string.Join(", ", binding.Controls.Select(control => control.Action + (control.Key == null ? "" : "/" + control.Key))));
                    foreach (var (kind, view) in plane.Shown)
                    {
                        if (view.Frame is MenuFrame frame)
                            words.Add(kind + ": lines " + string.Join(", ", frame.Lines.Where(line => line.Action != null).Select(line => line.Action + "/" + line.Key + (line.Available ? "" : "(off)")))
                                + "; footer " + string.Join(", ", frame.Footer.All.Select(slot => slot.Prompt.Id + (slot.Prompt.Available ? "" : "(off)")))
                                + "; sections " + string.Join(", ", frame.Sections.Select(section => section.Key)));
                        if (view.Side is SidePanel side) words.Add(kind + " side: " + side.Subject);
                    }
                    if (plane.Chrome.Visible) words.Add("chrome: " + string.Join(", ", plane.Chrome.Targets.Where(target => target.gameObject.activeInHierarchy).Select(target => target.Label.text)));
                    if (plane.Bar != null) words.Add("bar");
                    if (plane.KitCompactShows) words.Add("presence");
                    return string.Join(" | ", words);
                }

                // A press the way a hand makes it: on the kit's own control where the kit draws the file and binds the action,
                // otherwise on the frame drawn that offers it now, after it has stood long enough to take a press.
                bool Press(string action, string? key = null, bool required = true)
                {
                    Draw();
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    Draw();
                    // Confirm arrives locked (KitDecisionBar.ConfirmLockSeconds); a render has no clock, so it waits out the lock as a hand would.
                    if (plane.KitShows && action == FileScreens.Yes) plane.Kit.View.Decision.AgeForRender(KitDecisionBar.ConfirmLockSeconds);
                    if (PressOnKit(director, action, key)) { Draw(); return true; }
                    foreach (var (kind, view) in plane.Shown.Reverse())
                    {
                        if (view.Side is SidePanel side && (action == SidePanel.Close || action == Footer.NextPage || side.Lines.Any(line => line.Action == action && line.Available)))
                        {
                            if (director.PressForRender(MenuColumn.Side, action, key, view.InPlaceOf, side)) { Draw(); return true; }
                        }
                        if (view.Frame is not MenuFrame frame) continue;
                        // A row is pressed with its own key, which may be empty rather than none.
                        var row = frame.Lines.FirstOrDefault(line => line.Action == action && (key == null || line.Key == key) && line.Available);
                        var offered = row != null
                            || frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)
                            || action == MenuFrame.ChooseSection && frame.Sections.Any(section => section.Key == key);
                        if (offered && director.PressForRender(kind, action, row?.Key ?? key, frame, view.Side)) { Draw(); return true; }
                        if (offered)
                        {
                            // Refused while the plane moves or owes a redraw: what moved and what shows, then once more after a longer settle.
                            Debug.Log("Halcyonic: judge kit: " + action + " refused on a drawn frame; sliding " + plane.Sliding + ", repositioning " + plane.Repositioning
                                + ", kit " + plane.KitShows + ", composer focused " + plane.ComposerFocused);
                            MenuFrame? previous = null;
                            for (var settle = 0; settle < 8; settle++)
                            {
                                director.UpdateForRender(); director.DrawNow(); plane.Advance(1f);
                                var record = (navigator.Beside as FileColumn)?.Frame;
                                Debug.Log("Halcyonic: judge kit: settle " + settle + ": kit " + plane.KitShows + ", kit holds " + (plane.KitBinding is KitFileBinding held && plane.Kit.Holds(held))
                                    + ", instruction parts " + File()?.Screen.Instruction.Parts + " of " + File()?.Screen.Instruction.PartRows + " rows, part " + File()?.Screen.Instruction.Part
                                    + ", new frame " + !ReferenceEquals(record, previous));
                                previous = record;
                            }
                            Thread.Sleep(TimeSpan.FromSeconds(0.45));
                            var again = plane.Shown.FirstOrDefault(shown => shown.Kind == kind).View?.Frame;
                            Debug.Log("Halcyonic: judge kit: after settling, sliding " + plane.Sliding + ", repositioning " + plane.Repositioning + ", kit " + plane.KitShows
                                + ", same frame " + ReferenceEquals(again, frame));
                            if (again != null && director.PressForRender(kind, action, again.Lines.FirstOrDefault(line => line.Action == action)?.Key ?? key, again, view.Side)) { Draw(); return true; }
                        }
                    }
                    if (required)
                    {
                        var why = string.Join(", ", plane.Shown.Where(shown => shown.View.Frame != null).Select(shown => shown.Kind + " current "
                            + ReferenceEquals(shown.View.Frame, shown.Kind == MenuColumn.File ? (navigator.Beside as FileColumn)?.Frame : shown.View.Frame)
                            + " standing " + (navigator.Standing(shown.Kind, shown.View.Frame, null) != null)));
                        failures.Add("judge kit: nothing drawn took " + action + (key == null ? "" : " " + key) + "; drawn: " + Visible() + "; " + why
                            + (File() is FileColumn open ? "; instruction part " + open.Screen.Instruction.Part + " of " + open.Screen.Instruction.Parts + ", read " + open.Screen.Instruction.Read : ""));
                    }
                    return false;
                }

                // Every label drawn names no brand.
                List<string> Brands(string name)
                {
                    var found = new List<string>();
                    foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (string.IsNullOrEmpty(label.text) || label.GetComponent<MeshRenderer>() is MeshRenderer renderer && !renderer.enabled) continue;
                        foreach (var brand in JudgeBrands)
                            if (Regex.IsMatch(label.text, @"\b" + Regex.Escape(brand) + @"\b"))
                                found.Add(name + ": " + PathOf(label.transform) + " names " + brand + ": " + (label.text.Length > 80 ? label.text.Substring(0, 80) : label.text));
                    }
                    return found;
                }

                void Shot(string step)
                {
                    Draw();
                    shots++;
                    var name = "judge-" + shots.ToString("00") + "-" + step.Replace(' ', '-');
                    if (presentation.Minimized || navigator.IsCompact)
                    {
                        ForceMeshes(root);
                        if (plane.KitCompactShows)
                        {
                            failures.AddRange(KitTextInBands(name, plane));
                            failures.AddRange(KitTargets(name, plane));
                        }
                        camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                        var image = Render(camera, texture);
                        System.IO.File.WriteAllBytes(Path.Combine(folder, name + (plane.KitCompactShows ? "-presence" : "-bar") + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    else failures.AddRange(KitState(name, folder, root, camera, texture, director, characters, eyes));
                    failures.AddRange(Brands(name));
                    Debug.Log("Halcyonic: judge kit: " + name + ": " + (plane.KitShows ? "kit" : plane.KitCompactShows ? "presence" : "views") + "; " + Visible());
                }

                // 1. Launch: the recording welcomes the headset and the complete workspace opens on Projects.
                Until(() => Session()?.Status.IsLive == true && State()?.Workstreams.Count > 0, "the recording's welcome");
                if (fallback.Line == null) failures.Add("judge kit: no demonstration line while the recording plays");
                if (hasRecordedRun)
                {
                    // The recorded run plays first, labelled Recorded: its project, its task's file as it plays and at its end.
                    Until(() => fallback.Player?.Recording?.IsRecordedRun == true, "the recorded run");
                    Shot("recorded run projects");
                    if (plane.ConnectionState == WorkspaceText.Demo)
                        failures.Add("judge kit: the recorded run's workspace is labelled " + plane.ConnectionState + ", not Recorded (ADR 0035)");
                    var project = State()!.Projects.Values.First().ProjectId;
                    Press(ProjectsScreens.ChooseProject, project, required: false);
                    Shot("recorded run tasks");
                    var task = State()!.Workstreams.Values.FirstOrDefault()?.WorkstreamId;
                    if (task != null && Press(TasksColumn.OpenTask, task, required: false))
                    {
                        Shot("recorded run task");
                        foreach (var section in new[] { FileSection.Changes, FileSection.Checks })
                            if (Press(MenuFrame.ChooseSection, FileScreens.Key(section), required: false)) Shot("recorded run " + section.ToString().ToLowerInvariant());
                        Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity), required: false);
                    }
                    else failures.Add("judge kit: the recorded run's task did not open; drawn: " + Visible());
                    Until(() => fallback.Player?.Finished == true || fallback.Player?.Ended == true, "the recorded run's end", 120);
                    Shot("recorded run end");
                    // The labelled demonstration follows once the recorded run's end has held.
                    Press(Footer.Close, required: false);
                    Until(() => fallback.Player?.Recording?.IsRecordedRun == false && State()?.Workstreams.Count > 0, "the demonstration after the recorded run", 120);
                }
                Shot(hasRecordedRun ? "demonstration follows" : "projects at launch");
                if (!hasRecordedRun && navigator.Place != MenuPlace.Projects) failures.Add("judge kit: the workspace did not open on Projects");
                // While the demonstration waits for its question's answer, nothing moves under the workspace's own places.
                Until(() => DirectedRun()?.PendingQuestions.Count > 0, "the directed task's question");
                // 2. New project's companion: its recorded questions and a recap that can't start here.
                ThroughWorkspace(PlacementColumn.Navigate, nameof(MenuPlace.Projects));
                Press(ProjectsScreens.NewProject);
                Shot("new project");
                Press(NewProjectScreens.BeginCompanion);
                Shot("companion questions");
                if (navigator.Beside is NewProjectFlow flow && flow.Idea?.Companion is CompanionExchange exchange)
                {
                    var companionAsset = Resources.Load<TextAsset>(CompanionRecording.ResourceName);
                    var companion = CompanionRecording.Parse(companionAsset.text);
                    if (companion.RecordedAnswer(exchange) is string answer)
                    {
                        var key = NewProjectScreens.AnswerKey(exchange.Generation, answer);
                        for (var turn = 0; turn < 6 && plane.Showing(MenuColumn.File)?.Lines.Any(line => line.Action == NewProjectScreens.ChooseSuggestion && line.Key == key) != true; turn++)
                        {
                            var more = plane.Showing(MenuColumn.File)?.Lines.FirstOrDefault(line => line.Action == NewProjectScreens.MoreAnswers);
                            if (more == null) break;
                            Press(NewProjectScreens.MoreAnswers, more.Key);
                        }
                        Press(NewProjectScreens.ChooseSuggestion, key);
                        Press(NewProjectScreens.SendAnswer);
                    }
                    if (flow.Step != NewProjectStep.Recap) failures.Add("judge kit: the companion's recorded answer did not bring the recap");
                    Shot("companion recap");
                    for (var page = 2; page < 10; page++)
                    {
                        var next = plane.Showing(MenuColumn.File)?.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Kind == PromptKind.NextPage);
                        if (next == null || next.Words.StartsWith("First Page", StringComparison.Ordinal)) break;
                        Press(Footer.NextPage);
                        Shot("companion recap page " + page);
                    }
                    var start = plane.Showing(MenuColumn.File)?.Footer[PromptSlot.FarRight];
                    if (start?.Id != NewProjectScreens.StartBuilding || start.Available) failures.Add("judge kit: the demonstration's recap offers a start it can't make");
                }
                else failures.Add("judge kit: Talk it through did not bring the companion's recorded questions; drawn: " + Visible());
                Press(Footer.Close, required: false);

                // 3. Usage: the recorded limits, a limit's details.
                ThroughWorkspace(PlacementColumn.Navigate, nameof(MenuPlace.Usage));
                Shot("usage");
                Press(UsageColumn.OpenLimit, "0");
                Shot("usage details");
                for (var page = 2; page < 6 && Press(Footer.NextPage, required: false); page++) Shot("usage details part " + page);
                Press(SidePanel.Close, required: false);

                // 4. Settings: Your space, a setting chosen; Comfort's text size.
                ThroughWorkspace(PlacementColumn.Navigate, nameof(MenuPlace.Settings));
                Shot("settings");
                if (OpenSettingsCategoryForRender(director, SettingsText.YourSpace))
                {
                    Shot("your space");
                    Press(SettingsColumn.OpenSetting, SpaceSettings.SwitchSpace, required: false);
                    Shot("your space setting");
                    Press(SidePanel.Close, required: false);
                    Press(SettingsHubColumn.Back, required: false);
                }
                else failures.Add("judge kit: Settings offered no Your space; drawn: " + Visible());
                if (OpenSettingsCategoryForRender(director, Comfort.Heading))
                {
                    Shot("comfort");
                    Press(SettingsColumn.OpenSetting, "text-size", required: false);
                    Shot("text size");
                    Press(SidePanel.Close, required: false);
                }
                else failures.Add("judge kit: Settings offered no Comfort; drawn: " + Visible());


                // 5. The closed workspace: the directed task comes to wait, and the closed bar or presence says so.
                PressMinimize();
                Until(() => DirectedRun()?.PendingQuestions.Count > 0, "the directed task's question");
                Shot("closed a task waits");
                if (TasksColumn.Bar(MenuPlace.Tasks, State()).ClosedLine != TasksText.Waiting(1)) failures.Add("judge kit: the closed line does not say one task waits");
                PressRestore();
                Shot("restored");

                // 6. Tasks, the waiting task first; its file on Waiting at the question.
                if (!Press(MenuFrame.ChooseSection, nameof(MenuPlace.Tasks), required: false) && plane.Context.TasksTarget.gameObject.activeInHierarchy)
                {
                    var tasksTarget = plane.Context.TasksTarget;
                    tasksTarget.SettleForRender();
                    tasksTarget.Target.PointerForRender(976, Oculus.Interaction.PointerEventType.Select, tasksTarget.transform.position);
                    tasksTarget.Target.PointerForRender(976, Oculus.Interaction.PointerEventType.Unselect, tasksTarget.transform.position);
                    Draw();
                }
                if (navigator.Place != MenuPlace.Tasks) ThroughWorkspace(PlacementColumn.Navigate, nameof(MenuPlace.Tasks));
                if (navigator.Place != MenuPlace.Tasks) { director.Open(MenuPlace.Tasks); failures.Add("judge kit: no drawn control reached Tasks; drawn: " + Visible()); }
                Shot("tasks");
                var directed = Directed()!.WorkstreamId;
                if (plane.Showing(MenuColumn.Menu)?.Lines.FirstOrDefault(line => line.Action == TasksColumn.OpenTask)?.Key != directed)
                    failures.Add("judge kit: the waiting task is not Tasks' first row");
                Press(TasksColumn.OpenTask, directed);
                Shot("question");
                for (var turn = 0; turn < 12 && FileFrame()?.Footer[PromptSlot.FarRight]?.Id == FileScreens.NextPart; turn++)
                {
                    Press(FileScreens.NextPart, FileScreens.QuestionKey);
                    Shot("question part " + (turn + 2));
                }
                for (var turn = 0; turn < 8 && FileFrame()?.Lines.Any(line => line.Action == FileScreens.Choose && line.Key == "0") != true; turn++)
                    Press(FileScreens.MoreAnswers);
                Press(FileScreens.Choose, "0");
                // A cut answer opens its details beside: read and closed.
                for (var turn = 0; turn < 6 && plane.Shown.Any(shown => shown.Kind == MenuColumn.Side || shown.View.Side != null); turn++)
                {
                    Shot("answer details " + (turn + 1));
                    if (!Press(Footer.NextPage, required: false)) Press(SidePanel.Close);
                }
                Shot("answer chosen");
                if (sent.Count != 0) failures.Add("judge kit: choosing an answer sent something");
                Press(FileScreens.NextQuestion);
                for (var turn = 0; turn < 8 && FileFrame()?.Footer[PromptSlot.FarRight]?.Id == FileScreens.MoreAnswers; turn++) Press(FileScreens.MoreAnswers);
                Shot("answers reviewed");
                Press(FileScreens.SendAnswer);
                if (sent.OfType<ExecutionAnswerQuestionCommand>().Count() != 1) failures.Add("judge kit: Send answer did not send the one answer");
                else
                {
                    // The recording acknowledges the answer itself; the next decision waits for that.
                    var answerId = sent.OfType<ExecutionAnswerQuestionCommand>().Single().CommandId;
                    Debug.Log("Halcyonic: judge kit: main-thread context " + (SynchronizationContext.Current?.GetType().FullName ?? "none"));
                    Until(() => submissions.StateOf(answerId) != SubmissionState.Sending, "the answer's acknowledgement", 10);
                    Debug.Log("Halcyonic: judge kit: the answer stands " + submissions.StateOf(answerId));
                }

                // 7. The approval: the request read part by part, Yes only after its last part.
                Until(() => DirectedRun()?.PendingApprovals.Count > 0, "the directed task's approval");
                Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Waiting), required: false);
                Shot("approval");
                Press(FileScreens.Approve);
                var parts = 1;
                Shot("request part 1");
                while (parts < 20 && File()?.Steering.CanConfirm != true)
                {
                    if (File()!.Screen.RequestParts > 1 && File()!.Screen.RequestPart + 1 < File()!.Screen.RequestParts && FileFrame()?.Footer[PromptSlot.Free] != null)
                        failures.Add("judge kit: Yes shows before the request's last part");
                    if (!Press(FileScreens.NextPart, FileScreens.RequestKey)) break;
                    parts++;
                    Shot("request part " + parts);
                }
                if (File()?.Screen.RequestDrawnWhole != true) failures.Add("judge kit: the request was never drawn whole");
                Shot("yes");
                Press(FileScreens.Yes);
                if (sent.OfType<ExecutionRespondToApprovalCommand>().Count() != 1)
                    failures.Add("judge kit: Yes did not send the one approval; armed " + File()?.Steering.Armed + ", can confirm " + File()?.Steering.CanConfirm
                        + ", sent " + sent.Count + ", answer " + (sent.FirstOrDefault() is CommandEnvelope first ? submissions.StateOf(first.CommandId)?.ToString() : "none")
                        + "; drawn: " + Visible());

                // 8. The approved turn ends with failed checks.
                Until(() => DirectedRun()?.Status == ExecutionStatus.Completed, "the approved turn's end");
                Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Checks));
                Until(() => { Draw(); return FileFrame()?.Lines.FirstOrDefault()?.Words.StartsWith("Tests failed", StringComparison.Ordinal) == true; }, "the failed checks", 10);
                Shot("checks failed");

                // 9. Tell it: the recorded instructions, one chosen, reviewed and sent, the correction round.
                Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity));
                Shot("activity after checks");
                Press(FileScreens.TellIt);
                Shot("tell it");
                Press(FileScreens.Preset, "0");
                Shot("instruction chosen");
                Press(FileScreens.TellIt, required: false);
                for (var part = 0; part < 8 && File()?.Screen.Instruction.Read == false; part++) Press(FileScreens.NextInstructionPart);
                Shot("instruction reviewed");
                Press(FileScreens.SendInstruction);
                if (sent.OfType<ExecutionSendInstructionCommand>().Count() != 1) failures.Add("judge kit: Send did not send the one instruction");
                Shot("correction sent");

                // 10. The correction round runs and the checks pass.
                Until(() => DirectedRun()?.LastTestRun?.Outcome == TestOutcome.Passed, "the passing checks", 60);
                Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Checks), required: false);
                Shot("checks passed");
                Press(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity), required: false);
                Shot("the end");

                // 11. The end: with no task's file open the end hold runs, the workspace still open, and the recording starts
                // again (with a recorded run bundled, the recorded run plays once more).
                var ending = fallback.Player;
                var plays = ending?.Plays ?? 0;
                Press(Footer.Close, required: false);
                Until(() => !ReferenceEquals(fallback.Player, ending) || (fallback.Player?.Plays ?? 0) > plays, "the restart after the end", 120);
                if (navigator.IsCompact) failures.Add("judge kit: the workspace closed by itself at the end");
                Shot("restarted");

                // Nothing a judge pressed reached an agent: the recording answered each send itself.
                if (sent.Count != 3) failures.Add("judge kit: " + sent.Count + " commands were sent, not the answer, the approval and the instruction");

                // The Workspace row at the context column's foot, or the kit spine's, opens the Workspace menu: then one of its rows,
                // turning its pages to find it. Where a file stands beside, it is closed first, as a hand would.
                bool ThroughWorkspace(string action, string? key = null)
                {
                    Draw();
                    if (plane.KitShows && plane.Kit.View.Spine.Rows.FirstOrDefault(row => row.Model?.Key == "workspace") is KitRow foot)
                    {
                        foot.Press.SettleForRender();
                        foot.Press.PressForRender(974);
                    }
                    else if (plane.Context.gameObject.activeInHierarchy && plane.Context.WorkspaceTarget.gameObject.activeInHierarchy)
                    {
                        var workspace = plane.Context.WorkspaceTarget;
                        workspace.SettleForRender();
                        workspace.Target.PointerForRender(975, Oculus.Interaction.PointerEventType.Select, workspace.transform.position);
                        workspace.Target.PointerForRender(975, Oculus.Interaction.PointerEventType.Unselect, workspace.transform.position);
                    }
                    else if (plane.Chrome.Visible && plane.Chrome.Targets.FirstOrDefault(target => target.gameObject.activeInHierarchy && target.Label.text == "Workspace") is GlazeButton chrome)
                    {
                        chrome.SettleForRender();
                        chrome.Target.PointerForRender(977, Oculus.Interaction.PointerEventType.Select, chrome.transform.position);
                        chrome.Target.PointerForRender(977, Oculus.Interaction.PointerEventType.Unselect, chrome.transform.position);
                    }
                    else { failures.Add("judge kit: no drawn Workspace row; drawn: " + Visible()); return false; }
                    Draw();
                    for (var page = 0; page < 4; page++)
                    {
                        if (plane.Shown.Any(shown => shown.View.Frame?.Lines.Any(line => line.Action == action && (key == null || line.Key == key) && line.Available) == true))
                            return Press(action, key);
                        if (!Press(Footer.NextPage, required: false)) break;
                    }
                    failures.Add("judge kit: the Workspace menu offered no " + action + (key == null ? "" : " " + key) + "; drawn: " + Visible());
                    return false;
                }
                void PressMinimize()
                {
                    Draw();
                    if (plane.KitShows)
                    {
                        // The kit's own switch, from a task's file.
                        plane.Kit.View.Spine.Switch.CompactTarget.SettleForRender();
                        plane.Kit.View.Spine.Switch.CompactTarget.PressForRender(970);
                    }
                    else ThroughWorkspace(PlacementColumn.Minimize);
                    Draw();
                    if (!navigator.IsCompact) failures.Add("judge kit: Minimize did not close the workspace; drawn: " + Visible());
                }
                void PressRestore()
                {
                    Draw();
                    if (plane.KitCompactShows)
                    {
                        plane.Kit.Presence.Switch.WorkspaceTarget.SettleForRender();
                        plane.Kit.Presence.Switch.WorkspaceTarget.PressForRender(972);
                    }
                    else if (plane.Bar is MenuBarView dock && dock.Target.gameObject.activeInHierarchy)
                    {
                        dock.Shown = 1f;
                        dock.Target.SettleForRender();
                        dock.Target.Target.PointerForRender(973, Oculus.Interaction.PointerEventType.Select, dock.Target.transform.position);
                        dock.Target.Target.PointerForRender(973, Oculus.Interaction.PointerEventType.Unselect, dock.Target.transform.position);
                    }
                    else failures.Add("judge kit: nothing drawn restores the workspace; drawn: " + Visible());
                    Draw();
                    if (navigator.IsCompact) failures.Add("judge kit: Restore did not open the workspace; drawn: " + Visible());
                }
            }
            finally
            {
                fallback?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            shotCount = shots;
            return failures;
        }
    }
}
