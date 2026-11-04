#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// The capture presets (<see cref="CapturePresets"/>): extras that are not ids and fixed words ask for nothing; each
        /// preset applied to a real director waits until the snapshot holds its id, opens what it names, and sends nothing,
        /// the approval armed even under a low-consequence approval policy; and the presets are compiled into a
        /// development build's scripts and out of a release build's, read from each compile's assemblies. Batch mode with
        /// graphics not needed: -executeMethod Halcyonic.XR.Workspace.Editor.WorkspaceRender.CapturePresetsCheck; exits 1
        /// when a check fails.
        /// </summary>
        public static void CapturePresetsCheck()
        {
            var failures = new List<string>();
            try
            {
                // The compiles first: a player compile saves what is dirty, and drawing a director dirties the font assets.
                var release = PresetsCompiled(ScriptCompilationOptions.None, "release", failures);
                var development = PresetsCompiled(ScriptCompilationOptions.DevelopmentBuild, "development", failures);
                if (development == false) failures.Add("the development build's scripts do not carry " + nameof(CapturePresets));
                if (release == true) failures.Add("the release build's scripts carry " + nameof(CapturePresets));
                Debug.Log("Halcyonic: capture presets: a development build carries them: " + development + "; a release build carries them: " + release + ".");
                failures.AddRange(PresetsParse());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(PresetsApply(lowConsequenceApproval: false));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(PresetsApply(lowConsequenceApproval: true));
            }
            catch (Exception error) { failures.Add("the check threw " + error); }
            finally { KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: capture presets: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: capture presets: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> PresetsParse()
        {
            var failures = new List<string>();
            void Expect(bool held, string what) { if (!held) failures.Add("parse: " + what); }
            var task = CapturePresets.Parse("task:0192f1b0-0000-7000-8000-000000000103:waiting", true, "render-sandboxed", true, false);
            Expect(task?.Execution == "0192f1b0-0000-7000-8000-000000000103" && task.Section == FileSection.Waiting && task.Compose && task.Arm
                && task.Runtime == "render-sandboxed", "a task preset reads its execution, section, agent and switches");
            foreach (var section in new[] { "activity", "changes", "checks" })
                Expect(CapturePresets.Parse("task:abc:" + section, false, null, false, false)?.Section != null, "a task preset reads " + section);
            Expect(CapturePresets.Parse("task:abc:Waiting", false, null, false, false) == null, "a section is one of four fixed words, as written");
            Expect(CapturePresets.Parse("task:abc def:waiting", false, null, false, false) == null, "an execution id with a space is no id");
            Expect(CapturePresets.Parse("task:abc", false, null, false, false) == null, "a task preset names its section");
            Expect(CapturePresets.Parse("screen:0192a7a0-0000-7000-8000-0000000000b2", false, null, false, true) is { Project: "0192a7a0-0000-7000-8000-0000000000b2", Bar: true },
                "a screen preset reads its project and bar");
            Expect(CapturePresets.Parse("screen:Storefront API", false, null, false, false) == null, "a project's name is no id");
            Expect(CapturePresets.Parse("gallery", false, null, false, false)?.Gallery == true, "the gallery preset");
            Expect(CapturePresets.Parse("approve", false, null, true, false) == null && CapturePresets.Parse(null, true, null, true, true) == null,
                "anything else asks for nothing");
            Expect(CapturePresets.Parse("gallery", false, "Practice agent", false, false)?.Runtime == null, "an agent's name is no id, and is ignored");
            return failures;
        }

        /// <summary>Each preset applied to a director as the headset builds it, the approval's command policy as given.</summary>
        private static List<string> PresetsApply(bool lowConsequenceApproval)
        {
            var failures = new List<string>();
            var label = lowConsequenceApproval ? "apply, low-consequence approvals: " : "apply: ";
            var root = new GameObject("Capture presets witness");
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var waiting = Work.Approval("Run make migrate");
                var running = Work.Running();
                const string runningProject = "0192f1b0-0000-7000-8000-000000000101";
                running.Workstream.ProjectId = runningProject;
                running.Workstream.WorkstreamId = "0192f1b0-0000-7000-8000-000000000102";
                running.Execution.ExecutionId = "0192f1b0-0000-7000-8000-000000000103";
                running.Execution.WorkstreamId = running.Workstream.WorkstreamId;
                running.Execution.ProjectId = runningProject;
                running.Workstream.CurrentExecutionId = running.Execution.ExecutionId;
                running.Workstream.ExecutionIds = new List<string> { running.Execution.ExecutionId };
                var policies = new List<CommandPolicy>
                {
                    new() { CommandType = CommandType.ExecutionRespondToApproval, Policy = lowConsequenceApproval ? PolicyCategory.LowConsequence : PolicyCategory.ReviewRequired },
                    new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.LowConsequence },
                };
                var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
                var welcome = new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time, CommandPolicies = policies };
                foreach (var work in new[] { waiting, running }) work.State.ApplyWelcome(welcome);
                var projects = waiting.State.Projects.Values.Concat(new[] { new ProjectView { ProjectId = runningProject, Name = "Search service", CreatedAt = Time, UpdatedAt = Time } }).ToList();
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = projects,
                    Workstreams = new List<WorkstreamView> { waiting.Workstream, running.Workstream },
                    Executions = new List<ExecutionView> { waiting.Execution, running.Execution },
                    Runtimes = waiting.State.Runtimes.ToList() };
                // The running task's file presents from its own state, which must hold the ids it was given above.
                running.State.ApplySnapshot(snapshot, new StateChanges());
                running.State.ApplyWelcome(welcome);
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(welcome);
                var commands = new CommandFactory(client);
                var sends = 0;
                var keyboards = 0;
                var presentation = new WorkspacePresentationState();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, Commands = commands, Presentation = presentation,
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    State = () => session.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true, Keyboard = (_, _, _, _) => keyboards++,
                    MakeNewProject = host => new NewProjectFlow(host, commands, new KeptInMemory()),
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, id) => new FileColumn(host, () => (id == running.Workstream.WorkstreamId ? running : waiting).Present(live: true), commands, () => null, _ => null, () => ""),
                    CharacterOf = _ => null,
                    StageNow = () => new MenuDirector.Stage(new Vector3(0f, EyeHeight, 0f), Vector3.forward, new List<CharacterTarget>(), null, false),
                });
                string? Apply(CapturePresets.Preset? preset) => preset == null ? "unparsed" : CapturePresets.Apply(director, presentation, session.State, preset);
                FileColumn? Opened() => director.Navigator.Beside as FileColumn;

                // Before the snapshot holds the id, a preset waits and changes nothing.
                var early = CapturePresets.Parse("task:" + waiting.Execution.ExecutionId + ":waiting", false, null, true, false);
                if (Apply(early) != null || Opened() != null) failures.Add(label + "a preset opened something before the snapshot held its id");
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) failures.Add(label + "the witness's session never held a current snapshot");
                if (Apply(CapturePresets.Parse("task:0192f1b0-0000-7000-8000-0000000009ff:activity", false, null, false, false)) != null)
                    failures.Add(label + "a preset for an execution the snapshot does not hold did not wait");

                // The waiting approval, armed, whatever the approval policy: the confirming bar, nothing sent.
                var armed = Apply(early);
                var file = Opened();
                if (armed != "task at waiting, approval armed" || file?.Steering.Armed != WorkspaceAction.Approve
                    || file.Screen.Section != FileSection.Waiting || director.Navigator.BesideTask != waiting.Workstream.WorkstreamId)
                    failures.Add(label + "the waiting approval did not open armed at Waiting (" + armed + ")");
                if (sends != 0) failures.Add(label + "arming the approval sent " + sends);

                // A launch lays the file out again in its first seconds, which drops the arm (DEVICE, 2026-11-04): the
                // preset presses Approve again while nothing is armed, and never once it is armed, as that would confirm.
                file?.Reflow();
                if (file?.Steering.Armed != null) failures.Add(label + "a layout did not drop the arm, so the hold went unchecked");
                if (!CapturePresets.Rearm(director, waiting.Execution.ExecutionId) || file?.Steering.Armed != WorkspaceAction.Approve)
                    failures.Add(label + "the preset did not arm the approval again after a layout dropped it");
                if (!CapturePresets.Rearm(director, waiting.Execution.ExecutionId) || file?.Steering.Armed != WorkspaceAction.Approve)
                    failures.Add(label + "the arm did not hold");
                if (sends != 0) failures.Add(label + "holding the arm sent " + sends);

                // The running task composing, with the agent chosen by its id: no keyboard, nothing sent.
                var runtimeId = snapshot.Runtimes.First().RuntimeId;
                var composing = Apply(CapturePresets.Parse("task:" + running.Execution.ExecutionId + ":activity", true, runtimeId, false, false));
                if (composing != "task at activity, agent chosen, composing" || Opened()?.Screen.Instruction.Open != true || keyboards != 0
                    || presentation.SelectedRuntimeId != runtimeId || director.Navigator.BesideTask != running.Workstream.WorkstreamId)
                    failures.Add(label + "the composing preset did not open the instruction without the keyboard for the chosen agent (" + composing + ")");
                if (Apply(CapturePresets.Parse("task:" + running.Execution.ExecutionId + ":changes", false, "render-unregistered", false, false)) != "task at changes"
                    || presentation.SelectedRuntimeId != runtimeId)
                    failures.Add(label + "a runtime no one registered was chosen");

                // The project's screen with its bar; the gallery waits, as no headset view draws it yet.
                var screen = Apply(CapturePresets.Parse("screen:" + runningProject, false, null, false, true));
                if (screen != "screen with its bar" || presentation.ScreenProjectId != runningProject || !presentation.ScreenBarOpen)
                    failures.Add(label + "the screen preset did not show the project's screen with its bar (" + screen + ")");
                if (Apply(CapturePresets.Parse("gallery", false, null, false, false)) != null) failures.Add(label + "the gallery preset opened something");
                if (sends != 0) failures.Add(label + "the presets sent " + sends + " commands");
                Debug.Log("Halcyonic: capture presets: " + label + "armed " + armed + "; composing " + composing + "; screen " + screen + "; sent " + sends + ".");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        /// <summary>Whether the player's scripts, compiled for Android with <paramref name="options"/>, carry the presets; null where the compile failed.</summary>
        private static bool? PresetsCompiled(ScriptCompilationOptions options, string label, List<string> failures)
        {
            var folder = Path.Combine(Path.GetTempPath(), "halcyonic-presets-" + label + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var settings = new ScriptCompilationSettings { target = BuildTarget.Android, group = BuildTargetGroup.Android, options = options };
                var result = PlayerBuildInterface.CompilePlayerScripts(settings, folder);
                if (result.assemblies == null || !result.assemblies.Contains("Halcyonic.XR.Workspace.dll"))
                {
                    failures.Add("the " + label + " compile made no workspace assembly");
                    return null;
                }
                // The type's name, as the assembly's metadata holds it: present only where the class was compiled.
                var name = Encoding.UTF8.GetBytes(nameof(CapturePresets));
                var bytes = File.ReadAllBytes(Path.Combine(folder, "Halcyonic.XR.Workspace.dll"));
                return Enumerable.Range(0, bytes.Length - name.Length + 1).Any(at => bytes[at] == name[0] && name.Select((b, i) => bytes[at + i] == b).All(same => same));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
