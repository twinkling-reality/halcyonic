#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        public static void ActivityComposerCheck()
        {
            var previous = ViewField.Current;
            var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                    ViewField.Current = FieldChecks.Quest3S;
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ActivityComposerRenders", variant)); Directory.CreateDirectory(folder);
                    var result = RenderActivityComposer(folder);
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                    var voiceFolder = Path.Combine(folder, "voice-offered"); Directory.CreateDirectory(voiceFolder);
                    result.AddRange(RenderActivityComposer(voiceFolder, offerVoice: true));
                    return result;
                }));
            }
            catch (Exception error) { failures.Add("Activity/composer fixture threw " + error); }
            finally { ViewField.Current = previous; KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: Activity/composer: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: Activity/composer: every check passed.");
            UnityEditor.EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderActivityComposer(string folder, bool offerVoice = false)
        {
            var failures = new List<string>(); var root = new GameObject("Actual Activity and optional ordinary composer");
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var work = Work.Running();
                var otherWork = Work.Running();
                otherWork.Workstream.WorkstreamId = "second-synthetic-task"; otherWork.Workstream.ProjectId = "second-synthetic-project";
                otherWork.Workstream.Title = "Check search indexing"; otherWork.Workstream.CurrentExecutionId = "second-synthetic-execution";
                otherWork.Workstream.ExecutionIds = new List<string> { "second-synthetic-execution" };
                otherWork.Execution.ExecutionId = "second-synthetic-execution"; otherWork.Execution.WorkstreamId = otherWork.Workstream.WorkstreamId;
                otherWork.Execution.ProjectId = otherWork.Workstream.ProjectId;
                var snapshot = new Snapshot { Journal = new JournalInfo { JournalId = "activity-composer-synthetic", Origin = JournalOrigin.Live }, Position = 1,
                    Projects = work.State.Projects.Values.Concat(new[] { new ProjectView { ProjectId = "second-synthetic-project", Name = "Search service", CreatedAt = Time, UpdatedAt = Time } }).ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream, otherWork.Workstream }, Executions = new List<ExecutionView> { work.Execution, otherWork.Execution }, Runtimes = work.State.Runtimes.ToList() };
                work.State.ApplySnapshot(snapshot, new StateChanges()); otherWork.State.ApplySnapshot(snapshot, new StateChanges());
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = snapshot.Journal, Head = 1, ServerTime = Time }); inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot });
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, target); camera.backgroundColor = Color.clear;
                FileColumn? file = null; Action<string>? typed = null; var sends = 0; var commands = new CommandFactory(client); var presentation = new WorkspacePresentationState();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, InitialPlacementReady = () => true, Presentation = presentation, Commands = commands,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    State = () => session.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => typed = accept,
                    // Offer the real voice control without ever beginning microphone capture.
                    Voice = offerVoice ? root.AddComponent<HoldToTalk>() : null,
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, id) =>
                    {
                        var chosen = id == work.Workstream.WorkstreamId ? work : otherWork;
                        var column = new FileColumn(host, () => With(chosen.Present(live: true), activity: new[] {
                            new ActivityEntry(1, "2026-10-01T09:00:01.000Z", ActivityKind.Turn, "Round started", false),
                            new ActivityEntry(2, "2026-10-01T09:00:02.000Z", ActivityKind.Tool, id == work.Workstream.WorkstreamId ? "read: src/auth/sign-in.ts" : "read: src/search/index.ts", false),
                            new ActivityEntry(3, "2026-10-01T09:00:03.000Z", ActivityKind.Message,
                                id == work.Workstream.WorkstreamId ? "I found that sign-in attempts share one address counter. I am separating the account limit and checking the regression." : "I am checking the search index boundary.", true),
                            new ActivityEntry(4, "2026-10-01T09:00:04.000Z", ActivityKind.Tool, id == work.Workstream.WorkstreamId ? "Command failed: node --test test/sign-in.test.ts (exit 1)" : "Command failed: node --test test/search.test.ts (exit 1)", false),
                        }), commands, () => null, _ => null, () => "");
                        if (id == work.Workstream.WorkstreamId) file = column;
                        return column;
                    },
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                void Require(bool value, string message) { if (!value) failures.Add(message); }
                void Projects(IEnumerable<ProjectView> projects)
                {
                    snapshot.Projects = projects.ToList(); snapshot.Position++;
                    inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); session.Pump();
                }
                void Draw() { for (var step = 0; step < 4; step++) { director.UpdateForRender(); director.DrawNow(); } ForceMeshes(root); }
                bool Press(string action, string? key = null)
                {
                    Draw(); var frame = director.Plane.Showing(MenuColumn.File);
                    var accepted = director.PressForRender(MenuColumn.File, action, key, frame, null);
                    Require(accepted, "actual current action refused: " + action); Draw(); return accepted;
                }
                void Pointer(GlazeButton button, int id)
                {
                    button.SettleForRender(); button.Target.PointerForRender(id, Oculus.Interaction.PointerEventType.Select, button.transform.position);
                    button.Target.PointerForRender(id, Oculus.Interaction.PointerEventType.Unselect, button.transform.position);
                }
                void Shot(string name)
                {
                    Draw();
                    var views = director.Plane.Shown.Select(shown => shown.View).Concat(director.Plane.ActivityContext == null ? Array.Empty<MenuFrameView>() : new[] { director.Plane.ActivityContext! }).ToList();
                    Require(director.Plane.Context.ProjectChoices.Count == 2, name + ": actual multiple project choices absent");
                    Require(!director.Plane.Chrome.Visible, name + ": legacy full-workspace face remained visible");
                    foreach (var mark in director.Plane.SelectedMarks)
                        Require(views.Count(view => view.ProvenanceLabel.gameObject.activeInHierarchy && view.ProvenanceLabel.text.Contains(mark.Word)) == 1,
                            name + ": actual selected provenance is missing or duplicated: " + mark.Word);
                    foreach (var view in views.Where(view => !view.PassiveActivity))
                    {
                        Require(view.Footer.Fits, name + ": complete composer/reader actions do not fit their actual row");
                        foreach (var (_, button) in view.Footer.Shown)
                        foreach (var x in new[] { -.5f, .5f }) foreach (var y in new[] { -.5f, .5f })
                        {
                            var world = button.transform.TransformPoint(new Vector3(button.Size.x * x, button.Size.y * y, 0f));
                            var within = view.ReadingSurface.transform.InverseTransformPoint(world);
                            Require(Mathf.Abs(within.x) <= .5001f && Mathf.Abs(within.y) <= .5001f, name + ": actual action escaped its allocated opaque reading object");
                        }
                    }
                    failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                    failures.AddRange(GlazeChecks.TargetsLargeEnough(views.SelectMany(view => view.Targets).Concat(director.Plane.Context.Targets), eyes, name));
                    var image = Render(camera, target);
                    try
                    {
                        failures.AddRange(TransparentMetrics(name + " context", camera, director.Plane.Context.ReadingSurface, image));
                        foreach (var view in views) failures.AddRange(TransparentMetrics(name + " " + view.name, camera, view.ReadingSurface, image));
                        File.WriteAllBytes(Path.Combine(folder, name + "-rgba.png"), image.EncodeToPNG());
                        foreach (var room in new[] { ("bright", Color.white), ("dark", new Color(.02f, .03f, .04f, 1f)) })
                        {
                            var composite = CompositeActivityRoom(image, room.Item2); File.WriteAllBytes(Path.Combine(folder, name + "-" + room.Item1 + ".png"), composite.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(composite);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                var authoritativeDraws = 0;
                director.Plane.Drawn += (_, view) =>
                {
                    authoritativeDraws++;
                    Require(!director.Plane.Sliding && !view.Opening && !view.Closing && view.gameObject.activeInHierarchy, "unsettled or hidden view reported an authoritative draw");
                    Require(!ReferenceEquals(view, director.Plane.ActivityContext), "passive Activity reached the authoritative draw channel");
                };
                Draw(); director.OpenFile(work.Workstream.WorkstreamId); Draw(); Shot("01-activity-only");
                Require(director.Plane.ActivityContext == null, "composer context appeared without invoking direction");
                var beforeOpening = authoritativeDraws;
                Require(director.PressForRender(MenuColumn.File, FileScreens.TellIt, null, director.Plane.Showing(MenuColumn.File), null), "actual current Tell it refused");
                director.UpdateForRender(); director.Plane.Advance(.1f);
                Require(authoritativeDraws == beforeOpening, "opening composer acknowledged its frame early");
                director.Plane.Advance(MenuPlane.OpeningSeconds);
                Require(authoritativeDraws > beforeOpening, "settled composer never acknowledged its current frame");
                Require(typed != null, "actual ordinary instruction keyboard did not open");
                const string draft = "Keep the account and address limits separate. Add a regression test for two accounts sharing one address. Report the observed test result and explain any remaining uncertainty before changing unrelated behavior.";
                typed?.Invoke(draft); typed = null; Draw(); Shot("02-activity-and-composer");
                if (offerVoice)
                {
                    var footer = director.Plane.Shown.Single(shown => !shown.View.PassiveActivity).View.Footer;
                    Require(footer.Shown.Count() == 4, "voice-offered ordinary composer did not expose its actual four actions");
                    Require(footer.Showing?[PromptSlot.Secondary]?.Id == FileScreens.HoldToTalk, "voice-offered composer omitted the actual Hold to talk action");
                    Debug.Log($"Halcyonic: voice-offered composer footer measured {footer.Measure.Needed:R} needed / {footer.Measure.Room:R} available; fits={footer.Fits}.");
                }
                var context = director.Plane.ActivityContext;
                Require(context?.Frame != null && file?.ActivityContextFrame == context.Frame, "actual Activity was not rendered beside its own composer");
                Require(context == null || !context.Targets.Any(), "passive Activity acquired action targets");
                var forwardedPassive = 0;
                director.Plane.Drawn += (_, view) => { if (ReferenceEquals(view, context)) forwardedPassive++; };
                var passiveDraws = 0;
                if (context != null) context.Drawn += _ => passiveDraws++;
                Draw();
                Require(passiveDraws > 0 && authoritativeDraws > 0, "actual independent draw callbacks were not exercised");
                Require(file != null && file.Screen.Instruction.Parts > 1, "long draft did not exercise actual whole-text paging");
                if (context?.Frame != null && file != null)
                {
                    var wasRead = file.Screen.Instruction.Read;
                    file.Drawn(context.Frame, null);
                    Require(file.Screen.Instruction.Read == wasRead, "passive Activity draw granted composer reading credit");
                    Require(context.Frame.Lines.All(line => line.Action == null), "passive Activity carries an actionable line");
                }
                var opacityFace = context?.ReadingSurface;
                if (opacityFace != null)
                {
                    opacityFace.Fade(.15f);
                    try
                    {
                        var negative = Render(camera, target);
                        try { Require(TransparentMetrics("Activity opacity negative", camera, opacityFace, negative).Any(failure => failure.Contains("interior alpha")), "translucent Activity negative was accepted"); }
                        finally { UnityEngine.Object.DestroyImmediate(negative); }
                    }
                    finally { opacityFace.Fade(1f); }
                }
                Require(forwardedPassive == 0, "passive Activity draw was forwarded as authoritative File draw");
                if (director.Plane.ReaderAllocation is PlanePart reader && director.Plane.ComposerAllocation is PlanePart composer)
                    Require(reader.Bottom > composer.Top, "reader and composer allocations overlap");
                else failures.Add("independent reader/composer allocations missing");
                var stale = director.Plane.Showing(MenuColumn.File);
                director.MinimizeWorkspace(); Draw();
                Require(director.Plane.ActivityContext == null && !director.Plane.Context.gameObject.activeSelf, "Minimize retained hidden spatial parts");
                Require(!director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, stale, null) && sends == 0, "hidden composer accepted stale Send");
                director.Expand(); Draw(); Require(file?.Screen.Instruction.Words == draft, "compact restore lost exact ordinary draft");
                Press(FileScreens.HideInstruction); Shot("03-composer-hidden");
                Require(file?.Screen.Instruction.Words == draft && file.Screen.Instruction.Open == false && director.Plane.ActivityContext == null, "Hide composer lost draft or left a second passive view");
                Press(FileScreens.TellIt); Require(typed == null, "reopening retained composer unexpectedly opened keyboard");
                Draw();
                var loops = 0;
                while (file != null && file.Screen.Instruction.Part + 1 < file.Screen.Instruction.Parts && loops++ < 64)
                { Thread.Sleep(450); Press(FileScreens.NextInstructionPart); }
                Require(loops < 64 && file?.Screen.Instruction.Read == true, "actual bounded draft review never completed");
                Shot("04-draft-reviewed"); Require(sends == 0, "reading/reopening sent work without an explicit Send");
                var pickerPart = file!.Screen.Instruction.Part;
                Pointer(director.Plane.Context.TasksTarget, 219); Draw();
                Require(director.Navigator.Place == MenuPlace.Tasks && director.Navigator.BesideTask == null && file.Screen.Instruction.Words == draft,
                    "actual Tasks context control failed to promote the picker or discarded its retained draft");
                director.OpenFile(work.Workstream.WorkstreamId); Draw();
                Require(file.Screen.Instruction.Part == pickerPart, "same-project picker return unnecessarily lost the retained draft part");
                // Returning to the same retained file must not lose its part merely because B
                // was selected, but a real allocation change while B is active must invalidate A.
                var retainedPart = file!.Screen.Instruction.Part;
                director.OpenFile(otherWork.Workstream.WorkstreamId); Draw();
                var hiddenEpoch = file.Screen.Instruction.Epoch;
                director.OpenFile(work.Workstream.WorkstreamId); Draw();
                Require(file.Screen.Instruction.Part == retainedPart && file.Screen.Instruction.Epoch == hiddenEpoch,
                    "same-geometry A/B/A unnecessarily repacked the retained ordinary draft");
                director.OpenFile(otherWork.Workstream.WorkstreamId); Draw();
                hiddenEpoch = file.Screen.Instruction.Epoch;
                var extraProjects = new[] {
                    new ProjectView { ProjectId = "layout-project-three", Name = "A bounded third project", CreatedAt = Time, UpdatedAt = Time },
                    new ProjectView { ProjectId = "layout-project-four", Name = "A deliberately long fourth project name that wraps", CreatedAt = Time, UpdatedAt = Time },
                };
                Projects(session.State.Projects.Values.Concat(extraProjects));
                Draw(); director.OpenFile(work.Workstream.WorkstreamId); Draw();
                Require(file.Screen.Instruction.Epoch > hiddenEpoch && file.Screen.Instruction.Words == draft,
                    "A returned with a stale allocation after context geometry changed while B was active");
                Require(director.Plane.Context.MoreProjectsTarget.gameObject.activeInHierarchy
                    && director.Plane.Context.ProjectChoices.Any(choice => choice.ProjectId == work.Workstream.ProjectId && choice.Button.On),
                    "bounded project choices lost the actual selected project when more projects arrived");
                Projects(session.State.Projects.Values.Where(project => !extraProjects.Any(extra => extra.ProjectId == project.ProjectId)));
                Draw();
                director.FocusLeftForRender(); Draw();
                Require(file?.Steering.Armed == null && file?.Screen.Instruction.Words == draft, "focus loss lost draft or retained confirmation");
                // A pointer denied under old placement cannot fire a newly bound project on release.
                var held = director.Plane.Context.ProjectChoices.Single(choice => choice.ProjectId == "second-synthetic-project").Button;
                director.Plane.Repositioning = true;
                held.SettleForRender(); held.Target.PointerForRender(220, Oculus.Interaction.PointerEventType.Select, held.transform.position);
                var third = new ProjectView { ProjectId = "third-synthetic-project", Name = "A new project", CreatedAt = Time, UpdatedAt = Time };
                Projects(session.State.Projects.Values.Concat(new[] { third })); Draw(); director.Plane.Repositioning = false; Draw();
                held.Target.PointerForRender(220, Oculus.Interaction.PointerEventType.Unselect, held.transform.position);
                Require(director.Navigator.BesideTask == work.Workstream.WorkstreamId && sends == 0, "old denied pointer fired a replacement context binding");
                Projects(session.State.Projects.Values.Where(project => project.ProjectId != third.ProjectId)); Draw();
                // A deleted project is not selectable even before a redraw replaces its old button.
                var second = director.Plane.Context.ProjectChoices.Single(choice => choice.ProjectId == "second-synthetic-project");
                var removedProject = session.State.Projects[second.ProjectId];
                Projects(session.State.Projects.Values.Where(project => project.ProjectId != second.ProjectId)); Pointer(second.Button, 221);
                Require(director.Navigator.BesideTask == work.Workstream.WorkstreamId && sends == 0, "deleted project control changed selected work or sent a command");
                Projects(session.State.Projects.Values.Concat(new[] { removedProject })); Draw();
                second = director.Plane.Context.ProjectChoices.Single(choice => choice.ProjectId == "second-synthetic-project");
                Pointer(second.Button, 222); Draw();
                Require(director.Navigator.Place == MenuPlace.Tasks && director.Navigator.BesideTask == null
                    && file?.Screen.Instruction.Words == draft && sends == 0, "actual other-project choice retargeted/lost the retained draft instead of opening its task picker");
                director.OpenFile(work.Workstream.WorkstreamId); Draw();
                if (file?.Screen.Instruction.Open != true) Press(FileScreens.TellIt);
                loops = 0;
                while (file != null && file.Screen.Instruction.Part + 1 < file.Screen.Instruction.Parts && loops++ < 64)
                { Thread.Sleep(450); Press(FileScreens.NextInstructionPart); }
                Require(file?.Screen.Instruction.Read == true, "returned ordinary draft never regained current reading proof");
                Press(FileScreens.SendInstruction);
                Require(sends == 1, "explicit reviewed ordinary Send did not submit exactly once");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            return failures;
        }

        private static Texture2D CompositeActivityRoom(Texture2D eye, Color room)
        {
            var result = new Texture2D(eye.width, eye.height, TextureFormat.RGBA32, false);
            var pixels = eye.GetPixels(); room = room.linear;
            for (var index = 0; index < pixels.Length; index++)
            {
                var pixel = pixels[index];
                pixels[index] = new Color(pixel.r + room.r * (1f - pixel.a), pixel.g + room.g * (1f - pixel.a), pixel.b + room.b * (1f - pixel.a), 1f).gamma;
            }
            result.SetPixels(pixels); result.Apply(); return result;
        }
    }
}
