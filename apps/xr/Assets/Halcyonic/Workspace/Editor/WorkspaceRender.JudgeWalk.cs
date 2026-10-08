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
        /// by the menu's own director: the closed bar, Tasks with the waiting task first, its file at the
        /// agent's question, an answer chosen, the approval's request part by part and its Yes, Checks,
        /// Tell it's recorded instructions, Usage's recorded limits and Settings, then the bar again. The
        /// recording stands where each step is reached; each state is held to the plane's checks and saved.
        /// Nothing is sent: in a render the file's host has no session, so a press only arms or chooses.
        /// </summary>
        private static IEnumerable<string> RenderJudgeWalk(string name, string folder, float radius, float? surfaceDrop)
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
                // The recording's tasks on the stage, in its order, as the stage places three.
                var tasks = state.Workstreams.Values.OrderBy(task => task.CreatedAt).ToList();
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var spread = CharacterStage.Spread(radius, surfaceDrop.HasValue ? -surfaceDrop.Value : CharacterStage.DefaultHeightFromEyes);
                var origin = eyes + Vector3.down * (surfaceDrop ?? 0f);
                var slots = new[] { -12f, 0f, 12f };
                for (var slot = 0; slot < tasks.Count && slot < slots.Length; slot++)
                {
                    var view = CharacterView.Create(root.transform, tasks[slot].WorkstreamId);
                    view.Show(CharacterPresenter.Present(tasks[slot], state, true));
                    var (height, scale) = CharacterStage.Stance(view, radius, surfaceDrop ?? 0f, surfaceDrop.HasValue ? (float?)null : CharacterStage.DefaultHeightFromEyes);
                    var level = Quaternion.Euler(0f, slots[slot] * spread, 0f) * Vector3.forward;
                    view.transform.SetPositionAndRotation(origin + level * radius + Vector3.up * height, Quaternion.LookRotation(-level, Vector3.up));
                    view.transform.localScale = Vector3.one * scale;
                    characters.Add((view, CharacterTarget.Attach(view, tasks[slot].WorkstreamId)));
                }
                var targets = characters.ConvertAll(character => character.Target);
                var directed = tasks.Single(task => task.Title == "Add rate limiting to the sign-in endpoint").WorkstreamId;
                var waiting = targets.Single(target => target.View.WorkstreamId == directed);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
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
                    StageNow = () => new MenuDirector.Stage(eyes, waiting.BodyPosition - eyes, targets, surface, false),
                    CharacterOf = task => targets.FirstOrDefault(target => target.View.WorkstreamId == task),
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => state.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou),
                });
                var navigator = director.Navigator;
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
                    failures.AddRange(PlaneState(name + " judge " + step, folder, camera, texture, director.Plane, characters, eyes, null, lightLine));
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

                // The text size changed in Settings with the file open, as the owner's comfort walk does, then back
                // on Tasks beside the file: the file's page and Tasks' rows are read again at the new size, so the
                // plane stays inside the field. The render keeps drawing at its own size, so it is put back after,
                // and the menu opened again, its places made afresh, as the walk goes on.
                // atNewSize and backAtPass judge the file once its frame's tick has laid it again, before anything is drawn at that size.
                void ChangeTextSize(string step, Action atNewSize, Action backAtPass)
                {
                    var pass = GlazeText.Scale;
                    try
                    {
                        Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                        Press(MenuColumn.Menu, SettingsColumn.OpenSetting, "text-size");
                        var change = navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Footer[PromptSlot.FarRight];
                        var inPlace = director.Plane.Shown.Count == 1 && director.Plane.Shown[0].Kind == MenuColumn.Side ? director.Plane.Shown[0].View.Side : null;
                        var changed = change != null && (inPlace != null
                            ? navigator.Act(MenuColumn.Side, change.Id, null, null, inPlace)
                            : navigator.Act(MenuColumn.Menu, change.Id, null, Shown(MenuColumn.Menu), null));
                        if (!changed) failures.Add(name + ": the text size's change took no press.");
                        // What the app's comfort controls do with the change: reading text at its new size; then a
                        // frame's tick, as the director's Update gives every column before it draws.
                        GlazeText.SetScale(comfort.TextScale);
                        navigator.Tick();
                        atNewSize();
                        director.DrawNow();
                        CloseSide();
                        Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Tasks));
                        navigator.Tick();
                        director.DrawNow();
                        if (navigator.Place != MenuPlace.Tasks || Shown(MenuColumn.File) == null)
                        {
                            failures.Add(name + ": after the text size changed, the menu stands on " + navigator.Place + (Shown(MenuColumn.File) == null ? " with no file shown" : "") + ", not Tasks beside the file.");
                        }
                        Shot(step + " text " + (comfort.Text == TextSize.Larger ? "larger" : "standard"));
                    }
                    finally
                    {
                        comfort.Text = pass > 1f ? TextSize.Larger : TextSize.Standard;
                        GlazeText.SetScale(pass);
                    }
                    navigator.Tick();
                    backAtPass();
                    director.CloseMenu();
                    director.Open(MenuPlace.Tasks);
                    director.DrawNow();
                }

                // The closed bar: one line saying what waits.
                director.DrawNow();
                if (director.Plane.Bar == null) failures.Add(name + ": the judge walk starts with no closed bar.");
                if (TasksColumn.Bar(MenuPlace.Tasks, state).ClosedLine != TasksText.Waiting(1)) failures.Add(name + ": the closed bar does not say a task waits.");
                Shot("1 bar");

                // Opened while something waits: Tasks, the waiting task first.
                director.Open();
                director.DrawNow();
                if (navigator.Place != MenuPlace.Tasks) failures.Add(name + ": the menu opened on " + navigator.Place + " while a task waits.");
                if (Shown(MenuColumn.Menu)?.Lines.FirstOrDefault()?.Key != directed) failures.Add(name + ": the waiting task is not Tasks' first row.");
                Shot("2 tasks");

                // Its row opens its file on Waiting, at the agent's question; an answer chosen.
                if (!Press(MenuColumn.Menu, TasksColumn.OpenTask, directed) || file == null) failures.Add(name + ": the waiting task's row opened no file.");
                Shot("3 question");
                for (var turn = 0; turn < 8 && Shown(MenuColumn.File)?.Lines.Any(line => line.Action == FileScreens.NextPart) == true; turn++)
                {
                    Settle();
                    Press(MenuColumn.File, FileScreens.NextPart, FileScreens.QuestionKey);
                }
                for (var turn = 0; turn < 4 && Shown(MenuColumn.File)?.Lines.Any(line => line.Action == FileScreens.Choose && line.Key == "0") != true; turn++)
                {
                    Settle();
                    Press(MenuColumn.File, FileScreens.MoreAnswers);
                }
                Press(MenuColumn.File, FileScreens.Choose, "0");
                if (Shown(MenuColumn.File)?.Footer[PromptSlot.FarRight]?.Available != true) failures.Add(name + ": with an answer chosen and read, Send answer takes no press.");
                Shot("4 answer chosen");

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
                for (var part = 1; part < 12 && Shown(MenuColumn.File)?.Footer[PromptSlot.Free] == null; part++)
                {
                    Settle();
                    if (!Press(MenuColumn.File, FileScreens.NextPart, FileScreens.RequestKey)) failures.Add(name + ": the request's next part took no press.");
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
                if (navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Side?.Facts.LastOrDefault()?.Value != "Part of the recording") failures.Add(name + ": a recorded limit's Account does not say it is part of the recording.");
                Shot("10 usage");
                CloseSide();
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                Press(MenuColumn.Menu, SettingsColumn.OpenSetting, SpaceSettings.SwitchSpace);
                if (navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu?.Lines.FirstOrDefault()?.Words != SettingsText.YourSpace) failures.Add(name + ": Settings does not open on Your space.");
                Shot("11 settings");
                CloseSide();

                // New project from Projects: the companion's recorded questions, quoted as its own, then its recap, which can't start here.
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Projects));
                Press(MenuColumn.Menu, ProjectsScreens.NewProject);
                Press(MenuColumn.File, NewProjectScreens.BeginCompanion);
                if (flow?.Step != NewProjectStep.Questions) failures.Add(name + ": Talk it through did not bring the companion's recorded questions.");
                else if (Shown(MenuColumn.File)?.Lines.Any(line => line.Claim && line.Words.StartsWith("The companion says: “", StringComparison.Ordinal)) != true)
                {
                    failures.Add(name + ": the companion's words are not quoted as its own.");
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

                // Closed again: the bar alone.
                director.CloseMenu();
                director.DrawNow();
                Shot("14 closed");
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
