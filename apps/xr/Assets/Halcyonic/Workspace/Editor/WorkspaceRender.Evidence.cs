#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        /// <summary>The demonstration's run whose reads the evidence view is shot from (LINK, 2026-11-18): its eight understanding and evaluation answers all fall in the first node.</summary>
        private const string EvidenceRun = "01a0ec64-6fac-77a9-827e-bdcf356af403";

        /// <summary>The early state, the run's fourth answer, before its check run has arrived; and the full state, its eighth.</summary>
        private const int EvidenceEarlyEvents = 20, EvidenceFullEvents = 30;

        /// <summary>
        /// What the headset draws when a person opens a project screen's Reasoning door and Outcome door (LINK's evidence story:
        /// plain questions and answers on glass along a thread of light, Seorak's strip over them), on the shipped path at both reading sizes. The director is set up as the release
        /// WorkspaceDirector sets it up, the room redesign on, playing the bundled demonstration as a headset with no computer does,
        /// slowly enough to stop at a state: the doors are pressed through <see cref="MenuDirector.PressScreenDoor"/>, the bar's own
        /// path, and the view reads the run live through the task's own file, as the demonstration answers those reads where it
        /// stands. Shots: the reveal part-way just after the Reasoning door opens, the early state and the full state from that
        /// door, then the full state from the Outcome door, from the person's eyes looking ahead, left, right and up to the strip.
        /// The frames are for AUDIT's scores, not a check of their own; the journey fails only where a door opens nothing or opens
        /// it for another task, a state never arrives, or the story stands without its places (all six at the full state, Was It
        /// Checked? among them) or its strip. Renders
        /// and the shot list (shots.tsv) land in Builds/EvidenceRenders/Standard and Builds/EvidenceRenders/Larger.
        /// </summary>
        public static void EvidenceFlowCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            List<string> failures;
            try
            {
                failures = GlazeChecks.AtEachTextSize(variant =>
                {
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "EvidenceRenders", variant == "" ? "Standard" : variant));
                    Directory.CreateDirectory(folder);
                    foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var found = new List<string>();
                    var shots = new List<string> { "shot\tdoor\tstate\tplayed\ttask\trun\tstory places\tstrip\tlines" };
                    var asyncShadersWere = ShaderUtil.allowAsyncCompilation;
                    ShaderUtil.allowAsyncCompilation = false;
                    ViewField.Current = FieldChecks.Quest3S;
                    var context = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(null);
                    JoinedFacts.Listen();
                    try { found.AddRange(RenderEvidenceWalk(folder, shots)); }
                    catch (Exception error) { found.Add("evidence: " + error); }
                    finally { ShaderUtil.allowAsyncCompilation = asyncShadersWere; SynchronizationContext.SetSynchronizationContext(context); ViewField.Current = null; KeepFontAssetsAsCommitted(); JoinedFacts.Stop(); }
                    File.WriteAllText(Path.Combine(folder, "shots.tsv"), string.Join("\n", shots) + "\n");
                    foreach (var failure in found) Debug.LogError("Halcyonic: evidence: " + failure);
                    if (found.Count == 0) Debug.Log("Halcyonic: evidence: every shot taken; " + (shots.Count - 1) + " shots; " + folder);
                    return found;
                });
            }
            finally { RoomDesign.Enabled = was; }
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderEvidenceWalk(string folder, List<string> shots)
        {
            var failures = new List<string>();
            var root = new GameObject("Evidence walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            DemonstrationFallback? fallback = null;
            try
            {
                // The demonstration as ControlPlaneConnection loads it with no control plane configured, at a tenth of its recorded
                // pace so the walk can stand at each state: the run's answers come seconds apart, and the file reads every half second.
                DemonstrationPlayer? Load()
                {
                    var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                    if (asset == null) return null;
                    var recording = DemonstrationRecording.Parse(asset.text);
                    Resources.UnloadAsset(asset);
                    return new DemonstrationPlayer(recording, client, new DemonstrationOptions { Speed = 0.1, Loop = false });
                }
                fallback = new DemonstrationFallback(null, Load);
                fallback.Start();
                // Where the playback stands (the node, and how many of its events have played), read as the demonstration's own reads
                // read it; the player keeps it to the client assembly, so the walk reads it as a probe.
                var whereProperty = typeof(DemonstrationPlayer).GetProperty("Where", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("DemonstrationPlayer.Where is gone; the walk cannot tell where the playback stands");
                (int Node, int Played) Where() => fallback.Player is DemonstrationPlayer player ? ((int, int))whereProperty.GetValue(player)! : (-1, 0);

                var activity = new ActivityLog();
                var submissions = new CommandSubmissions { Demonstration = () => fallback.Line != null };
                var commands = new CommandFactory(client);
                var presentation = new WorkspacePresentationState();
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                string? journal = null;
                RealtimeSession? Session() => fallback.Current;
                ClientProjection? State() => fallback.Current?.State;
                WorkspacePresentation? Present(string task)
                {
                    var session = Session();
                    return session != null && session.State.Workstreams.TryGetValue(task, out var work)
                        ? WorkspacePresenter.Present(work, session.State, activity, session.Status.IsLive, submissions) : null;
                }
                // The person stands at the eyes and looks ahead, with no character on the stage: the evidence view opens around them.
                var stageMade = typeof(MenuDirector.Stage).GetConstructors().OrderByDescending(each => each.GetParameters().Length).First();
                MenuDirector.Stage StageNow()
                {
                    var given = new object?[] { eyes, Vector3.forward, new List<CharacterTarget>(), null, false, null, (Action<float>)(_ => { }) };
                    return (MenuDirector.Stage)stageMade.Invoke(stageMade.GetParameters()
                        .Select((parameter, index) => index < given.Length ? given[index] : parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray());
                }
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    // WorkspaceDirector.Start's setup, as a release build gives it with no computer connected.
                    SpatialWorkspace = true,
                    Observed = run => activity.ObservedFor(run),
                    ResponsiveWorkspace = true,
                    Presentation = presentation,
                    InitialPlacementReady = () => true,
                    State = State,
                    Connected = () => Session()?.Status.IsLive == true,
                    Session = Session,
                    // The walk sends nothing: it only opens doors.
                    Submit = (_, _) => null,
                    Api = () => null,
                    ConnectionTarget = () => null,
                    CharacterSourceConnectionKey = () => null,
                    SaveCharacterColours = _ => true,
                    Demonstration = () => fallback.Line != null,
                    RecordedRun = () => fallback.ShowingRecordedRun,
                    KeyboardOffered = () => false,
                    Keyboard = (_, _, _, _) => { },
                    Voice = root.AddComponent<HoldToTalk>(),
                    StageNow = StageNow,
                    CharacterOf = _ => null,
                    Bar = place => TasksColumn.Bar(place, State()),
                    SomethingWaits = () => State()?.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou) == true,
                    Comfort = comfort,
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null), _ => { }, _ => { }),
                    Commands = commands,
                    Overview = () => State() is ClientProjection state ? WorkOverview.Of(state, new StageVisibility(), _ => true) : null,
                    RecordedUsage = now => fallback.Line != null ? fallback.UsageLimitsAt(now) : null,
                    MakeNewProject = host => NewProjectColumn.Create(host, commands, () => new MemoryCreationDraftStore()),
                    // The file reads its run's sections, and the evidence view's understanding and evaluation, from the recording where
                    // the playback stands, as the headset's demonstration does.
                    File = (host, task) => Present(task) == null ? null : new FileColumn(host, () => Present(task), commands,
                        () => fallback.Player?.Reads, execution => fallback.InstructionsFor(execution), () => ""),
                });
                director.Open();
                var navigator = director.Navigator;
                var plane = director.Plane;

                // Each change of the screen shown, logged with the playback's own changes, so a screen that goes is seen going.
                string? screenWas = null;
                void Pump()
                {
                    if (fallback.Player is DemonstrationPlayer player) player.Reading = navigator.Reading;
                    var changes = fallback.Pump();
                    if (presentation.ScreenProjectId != screenWas)
                    {
                        Debug.Log("Halcyonic: evidence: the screen shown went from " + (screenWas ?? "nothing") + " to " + (presentation.ScreenProjectId ?? "nothing")
                            + " (resynchronized " + changes.Resynchronized + ", rewound " + changes.Rewound + ", at " + Where() + ")");
                        screenWas = presentation.ScreenProjectId;
                    }
                    if (changes.Resynchronized && (State()?.Journal?.JournalId != journal || changes.Rewound))
                    {
                        activity.Clear();
                        submissions.Clear();
                        journal = State()?.Journal?.JournalId;
                    }
                    activity.Record(changes.Events);
                }
                void Draw()
                {
                    Pump();
                    for (var turn = 0; turn < 4; turn++)
                    {
                        director.UpdateForRender(); director.DrawNow(); plane.Advance(MenuPlane.OpeningSeconds);
                        if (plane.KitShows) { plane.Kit.SettleForRender(); plane.Kit.DrawnForRender(); }
                    }
                }
                bool Until(Func<bool> done, string what, double seconds)
                {
                    var until = DateTime.UtcNow.AddSeconds(seconds);
                    while (DateTime.UtcNow < until)
                    {
                        Pump();
                        if (done()) { Draw(); return true; }
                        Thread.Sleep(20);
                    }
                    failures.Add("waited " + seconds + " s and " + what + " never came");
                    return false;
                }
                // The file reads again every RefreshSeconds while the view stands: wait out one read and draw what it read.
                void Fresh()
                {
                    var until = DateTime.UtcNow.AddSeconds(FileColumn.RefreshSeconds * 2 + 0.2);
                    while (DateTime.UtcNow < until) { Draw(); Thread.Sleep(50); }
                }

                // The run's task, once the playback has made it and its project.
                WorkstreamView? RunTask() => State()?.Workstreams.Values.FirstOrDefault(each => each.CurrentExecutionId == EvidenceRun || each.ExecutionIds.Contains(EvidenceRun));
                if (!Until(() => RunTask() != null, "the demonstration's run " + EvidenceRun, 120)) return failures;
                var runTask = RunTask()!;
                var projectId = runTask.ProjectId;
                if (!director.ShowProjectScreen(projectId, barOpen: true)) failures.Add("the project screen for " + projectId + " did not open");
                Draw();

                // The story reveals its places one by one from its first telling, by the view's clock, which does not move in a batch
                // render (LINK): the walk holds the clock where it stood at the press and moves it on itself, then advances the story.
                var opened = UnityEngine.Time.time;
                void At(float seconds)
                {
                    if (director.EvidenceFlow is not EvidenceFlowView view) return;
                    view.Clock = () => opened + seconds;
                    view.AdvanceStory();
                }
                void Shots(string door, string state, float seconds)
                {
                    At(seconds);
                    var view = director.EvidenceFlow;
                    if (director.EvidenceWorkstream != runTask.WorkstreamId || view == null || !view.gameObject.activeInHierarchy)
                    {
                        failures.Add("the " + door + " door at " + state + ": the evidence view does not stand for the run's task (stands for " + (director.EvidenceWorkstream ?? "nothing") + ")");
                        return;
                    }
                    var places = view.StoryPlaces;
                    // Just after the press the run may not have been read yet: the reveal shot is of the view arriving, not a check of it.
                    if (places.Count == 0 && state != "reveal") failures.Add("the " + door + " door at " + state + ": the story stands with no places");
                    if (state == "full")
                    {
                        if (places.Count != 6) failures.Add("the " + door + " door at full: the story stands with " + places.Count + " of its six places");
                        if (!places.ContainsKey(StoryPlaceKind.Worked)) failures.Add("the " + door + " door at full: Was It Checked? does not stand");
                        if (view.Strip == null) failures.Add("the " + door + " door at full: the strip does not stand");
                    }
                    // The story's two columns (LINK, after the owner's reference): ahead to the clear middle, left 32 degrees to the ask, its
                    // stack and the overview, right 32 degrees to the checks' answer and What's Left, and up 21 degrees to the strip.
                    var looks = state == "reveal" || door == "outcome"
                        ? new[] { ("ahead", 0f, 0f) }
                        : new[] { ("ahead", 0f, 0f), ("left", 0f, -26f), ("right", 0f, 26f), ("down", 28f, 0f) };
                    foreach (var (look, pitch, yaw) in looks)
                    {
                        Draw();
                        At(seconds);
                        ForceMeshes(root);
                        camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                        var image = Render(camera, texture);
                        var name = door + "-" + state + "-" + look;
                        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        var (node, played) = Where();
                        shots.Add(string.Join("\t", name, door, state, node + ":" + played, runTask.WorkstreamId, EvidenceRun,
                            string.Join(",", places.Keys), view.Strip == null ? "none" : "stands", view.Lines.Count.ToString()));
                    }
                }

                // A door press is taken only once the plane has settled (MenuDirector.OnScreenActed ignores one while it slides or is
                // moved, or while the headset is away), as a person's finger reaches the bar only once it stands still.
                bool Settled() => !plane.Sliding && !plane.Repositioning && !FocusGuard.InputSuspended && !FocusGuard.Folded;
                void Press(KitScreenAction action)
                {
                    // The screen stands open at the press, as a person's does when they reach its bar.
                    if (presentation.ScreenProjectId != projectId) director.ShowProjectScreen(projectId, barOpen: true);
                    Draw();
                    Until(Settled, "the plane to settle before the " + action + " door", 20);
                    Debug.Log("Halcyonic: evidence: before " + action + ", the screen shows " + (presentation.ScreenProjectId ?? "nothing"));
                    director.PressScreenDoor(action, projectId);
                    Draw();
                    Debug.Log("Halcyonic: evidence: pressed " + action + " on " + projectId + ": the view stands for " + (director.EvidenceWorkstream ?? "nothing")
                        + "; the screen shows " + (presentation.ScreenProjectId ?? "nothing") + "; the project's primary task is "
                        + (WorkspacePresentationState.PrimaryWorkstreamOf(projectId, State())?.WorkstreamId ?? "none")
                        + "; sliding " + plane.Sliding + ", repositioning " + plane.Repositioning + ", away " + (FocusGuard.InputSuspended || FocusGuard.Folded));
                }
                // The Reasoning door, pressed as the bar presses it, while the run's task is the project's newest. The task it opened the
                // view for is logged; a door bound to another task fails its shots (LINK: PrimaryWorkstreamOf picks by priority).
                opened = UnityEngine.Time.time;
                Press(KitScreenAction.Why);
                Debug.Log("Halcyonic: evidence: the Reasoning door opened the view for " + (director.EvidenceWorkstream ?? "nothing") + "; the run's task is " + runTask.WorkstreamId);
                if (director.EvidenceWorkstream != runTask.WorkstreamId)
                    failures.Add("the Reasoning door opened the evidence view for " + (director.EvidenceWorkstream ?? "nothing") + ", not the run's task " + runTask.WorkstreamId);
                // Part-way through the reveal, as a person sees the story arrive (LINK: 0.8 s after the first telling).
                Shots("reasoning", "reveal", 0.8f);
                // The early state: the run's fourth answer is in force, its check run not yet arrived.
                if (Until(() => Where() is var (node, played) && node == 0 && played >= EvidenceEarlyEvents, "the run's early state", 120))
                {
                    Fresh();
                    Shots("reasoning", "early", 10f);
                }
                // The full state: the run's last answer is in force for good.
                if (Until(() => Where() is var (node, played) && (node > 0 || played >= EvidenceFullEvents), "the run's full state", 180))
                {
                    Fresh();
                    Shots("reasoning", "full", 10f);
                }
                // The same door again puts the view away; the Outcome door opens the same view.
                Press(KitScreenAction.Why);
                if (director.EvidenceWorkstream != null) failures.Add("pressing the Reasoning door again left the evidence view standing");
                Press(KitScreenAction.HowItWent);
                Fresh();
                Shots("outcome", "full", 10f);
            }
            finally
            {
                fallback?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}
