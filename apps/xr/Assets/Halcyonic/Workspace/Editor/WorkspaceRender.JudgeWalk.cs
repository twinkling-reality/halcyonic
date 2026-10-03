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
                ? Task.FromException<IntelligenceRead<T>>(new ControlPlaneRequestException(DemonstrationReads.NothingRecorded))
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
                void Shot(string step) => failures.AddRange(PlaneState(name + " judge " + step, folder, camera, texture, director.Plane, characters, eyes, null));
                // A part's or a page's row turns nothing until what it shows has stood a moment.
                void Settle() => Thread.Sleep(TimeSpan.FromSeconds(0.45));

                // The closed bar: one line saying what waits.
                director.DrawNow();
                if (director.Plane.Bar == null) failures.Add(name + ": the judge walk starts with no closed bar.");
                if (TasksColumn.Bar(MenuPlace.Tasks, state).ClosedLine != "1 task is waiting for you") failures.Add(name + ": the closed bar does not say a task waits.");
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
                Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                Press(MenuColumn.Menu, SettingsColumn.OpenSetting, "text-size");
                Shot("11 settings");

                // Closed again: the bar alone.
                director.CloseMenu();
                director.DrawNow();
                Shot("12 closed");
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
