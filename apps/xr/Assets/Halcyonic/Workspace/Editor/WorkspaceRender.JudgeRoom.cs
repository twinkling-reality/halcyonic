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
        /// What a judge sees in the redesign (<see cref="RoomDesign.Enabled"/> on), as the release player runs it with no
        /// computer connected: the recorded run moment by moment (launch, at work, the result, Changes, Checks, its end), then
        /// the labelled demonstration (the question, it asks and Confirm, at work, Build Failed, the written instruction, the
        /// correction, Build Passed, the restart). At each moment the redesign must draw it and nothing of the older views may
        /// show; what it draws is held to the frame checks (every word whole, inside a Quest 3S field, contrast at least 4.5:1,
        /// no brand names) at both reading sizes. A press goes through a drawn control where the redesign draws one; where none
        /// is drawn the walk says so and moves the recording on itself, so every later moment is still seen. A task's pages
        /// are reached the same way, through a drawn row or section control, and the moment list's "reached by" says how
        /// each was, or that the walk opened it. A result is shot once the task's file has read it. Renders and the moment
        /// list (moments.tsv) land in Builds/JudgeRoomRenders.
        /// </summary>
        public static void JudgeRoomWalkCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            List<string> failures;
            try
            {
                failures = GlazeChecks.AtEachTextSize(variant =>
                {
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "JudgeRoomRenders", variant));
                    Directory.CreateDirectory(folder);
                    foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var found = new List<string>();
                    var moments = new List<string> { "moment\tdrawn by\tdots | older cut | overlaps\tcoverage\tredesign drew\tolder parts drew\tactions drawn\treached by\tfailures" };
                    ViewField.Current = FieldChecks.Quest3S;
                    var context = SynchronizationContext.Current;
                    // As the judge kit walk: the session's acknowledgements continue on the thread pool while the walk holds the Editor's thread.
                    SynchronizationContext.SetSynchronizationContext(null);
                    try { found.AddRange(RenderJudgeRoomWalk(folder, moments)); }
                    catch (Exception error) { found.Add("judge room: " + error); }
                    finally { SynchronizationContext.SetSynchronizationContext(context); ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                    File.WriteAllText(Path.Combine(folder, "moments.tsv"), string.Join("\n", moments) + "\n");
                    foreach (var failure in found) Debug.LogError("Halcyonic: judge room: " + failure);
                    if (found.Count == 0) Debug.Log("Halcyonic: judge room: every check passed; " + (moments.Count - 1) + " moments; " + folder);
                    return found;
                });
            }
            finally { RoomDesign.Enabled = was; }
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderJudgeRoomWalk(string folder, List<string> moments)
        {
            var failures = new List<string>();
            var root = new GameObject("Judge room walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            DemonstrationFallback? fallback = null;
            try
            {
                // As ControlPlaneConnection loads them with no control plane configured: the recorded run first where the build
                // bundles one, then the demonstration. The recorded run at ten times its pace, the demonstration fast; the holds
                // that wait for an answer still wait.
                DemonstrationPlayer? Load(string resource, bool recordedRun, bool loop)
                {
                    var asset = Resources.Load<TextAsset>(resource);
                    if (asset == null) return null;
                    var recording = DemonstrationRecording.Parse(asset.text);
                    Resources.UnloadAsset(asset);
                    if (recording.IsRecordedRun != recordedRun) failures.Add("judge room: " + resource + " holds the wrong kind of recording");
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
                    RecordedRun = () => fallback.ShowingRecordedRun,
                    KeyboardOffered = () => false,
                    // Hold to talk as the release player carries it (ADR 0021, amended): with no computer it must never be offered.
                    Voice = root.AddComponent<HoldToTalk>(),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                    Bar = place => TasksColumn.Bar(place, State()),
                    SomethingWaits = () => State()?.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou) == true,
                    Comfort = comfort,
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

                void Pump()
                {
                    if (fallback.Player is DemonstrationPlayer player) player.Reading = !navigator.IsCompact && navigator.Beside != null;
                    var changes = fallback.Pump();
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
                bool Until(Func<bool> done, string what, double seconds = 30)
                {
                    var until = DateTime.UtcNow.AddSeconds(seconds);
                    while (DateTime.UtcNow < until)
                    {
                        Pump();
                        if (done()) { Draw(); return true; }
                        Thread.Sleep(20);
                    }
                    failures.Add("judge room: waited " + seconds + " s and " + what + " never came");
                    return false;
                }
                WorkstreamView? Directed() => State()?.Workstreams.Values.FirstOrDefault(task => task.Title == JudgeDirectedTitle);
                ExecutionView? Run(WorkstreamView? task) => task != null && State() is ClientProjection state ? state.CurrentExecution(task) : null;
                ExecutionView? DirectedRun() => Run(Directed());
                FileColumn? Opened() => navigator.Beside as FileColumn;

                // A press: through the redesign's drawn control where one is bound to the action (not built yet for any judge
                // moment but the instruction's own row), otherwise through the file's frame of record, which the redesign may
                // not draw. A press that only the frame of record took is a moment the redesign does not draw a control for.
                var momentFailures = new List<string>();
                bool Press(string action, string? key = null)
                {
                    Draw();
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    Draw();
                    if (Drawn(action, key) is RoomButton button)
                    {
                        button.Press.SettleForRender();
                        button.Press.PressForRender(990);
                        Draw();
                        return true;
                    }
                    if (Opened()?.Frame is MenuFrame record && director.PressForRender(MenuColumn.File, action, key, record, null))
                    {
                        momentFailures.Add("no drawn control for " + action + (key == null ? "" : " " + key) + ": the frame of record took it");
                        Draw();
                        return true;
                    }
                    return false;
                }
                // The redesign's own drawn button that raises the file's action, where the redesign draws the file.
                RoomButton? Drawn(string action, string? key) => plane.KitShows ? plane.Kit.Bar.Button(action, key) : null;
                // Closing the task's file: through the frame of record's Close, else the navigator, said either way.
                void CloseFile()
                {
                    if (!Press(Footer.Close))
                    {
                        momentFailures.Add("nothing drawn closes the task's file: the walk closed it");
                        navigator.CloseBeside();
                    }
                    Draw();
                }
                // Confirm behind its lock: drawn locked and taking no press, then, its lock passed, one press. True when pressed.
                bool ConfirmBehindLock(string what)
                {
                    Draw();
                    if (!plane.KitShows || plane.Kit.Bar.Locking is not RoomButton locked || plane.Kit.Bar.ControlOf(locked) is not KitControl control) return false;
                    var before = sent.Count;
                    Moment(what + " confirm locked");
                    locked.Press.SettleForRender();
                    locked.Press.PressForRender(993);
                    Draw();
                    if (sent.Count != before) failures.Add("judge room: the " + what + "'s Confirm took a press behind its lock");
                    plane.Kit.Bar.AgeLockForRender(RoomBar.ConfirmLockSeconds);
                    Draw();
                    Moment(what + " confirm");
                    return Press(control.Action, control.Key);
                }
                // Where nothing took a press, the walk sends what a judge's press would have, so the recording moves on.
                void SendInstead(string what, CommandEnvelope command)
                {
                    momentFailures.Add("nothing drawn took " + what + ": the walk sent it");
                    var session = Session()!;
                    sent.Add(command);
                    submissions.Submit(each => session.SubmitAsync(each), command, ExecutionOf(command));
                }

                // How the walk reached the next moment's page, for the census: a drawn control it pressed, or the walk itself
                // where the redesign drew none (a judge would have no way there but the shelf, which this walk does not draw).
                var reachedBy = "";
                // A row of the redesign's card (a list row with its own control), pressed as a person pinches it: what it pressed,
                // or null where no row matched.
                string? PressRow(Func<KitControl, bool> which)
                {
                    Draw();
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    Draw();
                    if (!plane.KitShows || plane.Kit.Bar.Card is not RoomCardView card) return null;
                    foreach (var (press, control) in card.RowTargets)
                    {
                        if (!which(control)) continue;
                        press.SettleForRender();
                        press.PressForRender(989);
                        Draw();
                        return "pressed row " + control.Action + (control.Key == null ? "" : "/" + control.Key);
                    }
                    return null;
                }
                // A task's file opened on a section the way a person opens it: a card row naming the task or its project, then the
                // redesign's drawn section control (the result card's Checks for Checks) or row; each press given the file's
                // refresh to land. Where none reaches it the walk opens it, and the census says so either way.
                void OpenSection(string workstreamId, FileSection? section)
                {
                    bool Open() => Opened()?.Now?.Character.WorkstreamId == workstreamId;
                    bool There() => Open() && (section == null || Opened()!.Screen.Section == section);
                    var project = State()?.Workstreams.TryGetValue(workstreamId, out var work) == true ? work.ProjectId : null;
                    var ways = new List<string>();
                    bool PressedButton(string action, string? key)
                    {
                        if (Drawn(action, key) is not RoomButton button) return false;
                        button.Press.SettleForRender();
                        button.Press.PressForRender(988);
                        Fresh();
                        ways.Add("pressed " + action + (key == null ? "" : "/" + key));
                        return true;
                    }
                    // Projects' row opens the project's tasks, whose row opens the task: as many rows as it takes, three at most.
                    for (var step = 0; step < 3 && !Open(); step++)
                    {
                        if (PressRow(control => control.Key != null && (control.Key.Contains(workstreamId) || project != null && control.Key.Contains(project))) is not string row) break;
                        Fresh();
                        ways.Add(row);
                    }
                    var key = section?.ToString().ToLowerInvariant();
                    if (Open() && !There() && key != null && !PressedButton("section", key) && !(section == FileSection.Checks && PressedButton("room-checks", null))
                        && PressRow(control => control.Key == key || control.Key == KitFileBinding.SectionRowKey + key) is string sectionRow)
                    {
                        Fresh();
                        ways.Add(sectionRow);
                    }
                    if (!There())
                    {
                        director.OpenFile(workstreamId, section);
                        ways.Add("no drawn control reached it: the walk opened it");
                    }
                    reachedBy = ways.Count == 0 ? "already open" : string.Join(", then ", ways);
                    Draw();
                }
                // A result moment waits for the task's file to read the new state (it rebuilds every FileColumn.RefreshSeconds),
                // so the shot shows what a person meets, not the state of a moment before (XR, 2026-11-11T19:14Z).
                void Fresh()
                {
                    Thread.Sleep(TimeSpan.FromSeconds(FileColumn.RefreshSeconds + 0.15));
                    Draw();
                }

                // Which parts drew: the redesign's own (its types are Room*), the characters (kept as built), and anything else,
                // the older views and kit, which no judge moment may show in the redesign.
                (List<string> Redesign, List<string> Older) Parts()
                {
                    var redesign = new SortedSet<string>();
                    var older = new SortedSet<string>();
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!renderer.enabled) continue;
                        var owner = OwnerOf(renderer.transform);
                        if (owner == null) continue;
                        if (owner.StartsWith("Room", StringComparison.Ordinal)) redesign.Add(owner);
                        else if (!owner.StartsWith("Character", StringComparison.Ordinal)) older.Add(owner);
                    }
                    return (redesign.ToList(), older.ToList());
                }

                var shots = 0;
                void Moment(string moment, bool expectDrawn = true)
                {
                    Draw();
                    shots++;
                    var name = "room-" + shots.ToString("00") + "-" + moment.Replace(' ', '-');
                    // The Editor runs no Update, so each part's arrival and easing stand where they began: taken to their end, as
                    // a person sees them once they have arrived.
                    foreach (var button in root.GetComponentsInChildren<RoomButton>(false)) button.Settle();
                    foreach (var band in root.GetComponentsInChildren<RoomBand>(false)) band.Settle();
                    foreach (var cursor in root.GetComponentsInChildren<RoomCursor>(false)) cursor.Settle();
                    foreach (var field in root.GetComponentsInChildren<RoomField>(false)) field.Settle();
                    ForceMeshes(root);
                    camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                    var image = Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    var (redesign, older) = Parts();
                    // What the redesign does not draw yet is listed; on a moment it covers it fails. What it does draw is held
                    // to the frame checks on every moment.
                    var covered = Covered(moment);
                    var gaps = new List<string>(momentFailures);
                    momentFailures.Clear();
                    if (expectDrawn && redesign.Count == 0) gaps.Add("the redesign draws nothing");
                    if (older.Count > 0) gaps.Add("older parts draw: " + string.Join(", ", older));
                    var found = new List<string>(RoomFrameChecks(root, camera, image, eyes, plane));
                    // No computer: a recording plays, so voice is never offered and Hold to Talk never drawn (ADR 0021, amended).
                    if (director.VoiceOffered) found.Add("voice is offered with no computer");
                    if (plane.KitShows && plane.Kit.Bar.Talk.gameObject.activeInHierarchy) found.Add("Hold to Talk is drawn with no computer");
                    found.AddRange(RoomBrands(root));
                    if (covered) found.AddRange(gaps);
                    UnityEngine.Object.DestroyImmediate(image);
                    // The redesign's actions drawn now, as action/key, so a moment's way on can be read off the list.
                    var actions = plane.KitShows
                        ? string.Join(", ", plane.Kit.Bar.Column.Where(each => each.gameObject.activeInHierarchy)
                            .Select(each => plane.Kit.Bar.ControlOf(each) is KitControl control ? control.Action + (control.Key == null ? "" : "/" + control.Key) : "?")
                            .Concat(plane.Kit.Bar.Card is RoomCardView card
                                ? card.RowTargets.Select(row => "row " + row.Control.Action + (row.Control.Key == null ? "" : "/" + row.Control.Key))
                                : Enumerable.Empty<string>()))
                        : "";
                    var census = Census(root, camera);
                    var drawnBy = "place " + navigator.Place + (navigator.Beside is IMenuColumn beside ? ", beside " + beside.GetType().Name : "")
                        + (Opened() is FileColumn open ? ", section " + open.Screen.Section : "") + (plane.KitShows ? ", room host" : "");
                    moments.Add(name + "\t" + drawnBy + "\t" + census + "\t" + (covered ? "covered" : "not yet") + "\t" + string.Join(", ", redesign) + "\t" + string.Join(", ", older) + "\t" + actions
                        + "\t" + reachedBy + "\t" + string.Join("; ", found) + (covered || gaps.Count == 0 ? "" : "\t(not yet: " + string.Join("; ", gaps) + ")"));
                    failures.AddRange(found.Select(each => name + ": " + each));
                    reachedBy = "";
                    Debug.Log("Halcyonic: judge room: " + name + ": redesign [" + string.Join(", ", redesign) + "], older [" + string.Join(", ", older) + "]");
                }

                Until(() => Session()?.Status.IsLive == true && State()?.Workstreams.Count > 0, "the recording's welcome");
                if (hasRecordedRun)
                {
                    // The recorded run: launch, its task picked, at work, the result, Changes, Checks and its end.
                    Until(() => fallback.Player?.Recording?.IsRecordedRun == true, "the recorded run");
                    Moment("recorded launch");
                    var task = State()!.Workstreams.Values.First();
                    // Picking its character on the shelf opens its file; the shelf is not part of this walk yet.
                    OpenSection(task.WorkstreamId, null);
                    if (Until(() => Run(task)?.ActiveTools.Count > 0, "the recorded run at work", 60)) Moment("recorded at work");
                    if (Until(() => Run(task)?.Status == ExecutionStatus.Completed, "the recorded run's result", 120)) { Fresh(); Moment("recorded result"); }
                    OpenSection(task.WorkstreamId, FileSection.Changes);
                    Moment("recorded changes");
                    OpenSection(task.WorkstreamId, FileSection.Checks);
                    Moment("recorded checks");
                    Until(() => fallback.Player?.Finished == true || fallback.Player?.Ended == true, "the recorded run's end", 120);
                    Moment("recorded end");
                    // The demonstration follows once the end has held with no file open.
                    CloseFile();
                    Until(() => fallback.Player?.Recording?.IsRecordedRun == false && State()?.Workstreams.Count > 0, "the demonstration after the recorded run", 120);
                }
                Moment("demonstration launch");

                // The question: read, an answer chosen, sent.
                if (Until(() => DirectedRun()?.PendingQuestions.Count > 0, "the directed task's question"))
                {
                    var directed = Directed()!;
                    OpenSection(directed.WorkstreamId, FileSection.Waiting);
                    Moment("question");
                    // The prompt on the band, read to its last part where it pages.
                    for (var part = 2; part < 8 && plane.KitShows && plane.Kit.Bar.BandNextButton is RoomButton turning; part++)
                    {
                        turning.Press.SettleForRender();
                        turning.Press.PressForRender(992);
                        Draw();
                        Moment("question part " + part);
                    }
                    var answered = Press(FileScreens.Choose, "0");
                    if (answered) Moment("answer chosen");
                    if (sent.Count != 0) failures.Add("judge room: choosing an answer sent something");
                    answered = answered && Press(FileScreens.NextQuestion);
                    if (answered) Moment("answers reviewed");
                    answered = answered && Press(FileScreens.SendAnswer);
                    // Send Answer asks for its confirmation: Confirm behind its lock first, then its press sends the one answer.
                    if (answered && !sent.OfType<ExecutionAnswerQuestionCommand>().Any()) answered = ConfirmBehindLock("answer");
                    answered = answered && sent.OfType<ExecutionAnswerQuestionCommand>().Count() == 1;
                    if (!answered && DirectedRun()?.PendingQuestions.FirstOrDefault() is QuestionView question)
                        SendInstead("the answer", commands.AnswerQuestion(DirectedRun()!.ExecutionId, question.QuestionId,
                            question.Prompts.Select(prompt => new QuestionAnswer { Key = prompt.Key, Selected = prompt.Options.Take(1).Select(option => option.Label).ToList() })));
                    Moment("answer sent");
                }

                // It asks, and Confirm: the request read to its last part, then Approve and its confirmation.
                if (Until(() => DirectedRun()?.PendingApprovals.Count > 0, "the directed task's approval"))
                {
                    OpenSection(Directed()!.WorkstreamId, FileSection.Waiting);
                    Moment("it asks");
                    if (Drawn(FileScreens.Deny, null) == null && Drawn(FileScreens.Approve, null) != null) momentFailures.Add("Approve stands without Deny");
                    // Approve, then Cancel: the request asks again and nothing was sent.
                    if (Press(FileScreens.Approve) && Drawn(FileScreens.Cancel, null) != null)
                    {
                        Moment("confirm cancel offered");
                        Press(FileScreens.Cancel);
                        if (Drawn(FileScreens.Approve, null) == null) momentFailures.Add("Cancel did not bring the request back to Approve and Deny");
                        if (sent.OfType<ExecutionRespondToApprovalCommand>().Any()) momentFailures.Add("Cancel sent an answer");
                        Moment("cancelled asks again");
                    }
                    var approved = Press(FileScreens.Approve);
                    for (var part = 0; approved && part < 20 && Opened()?.Steering.CanConfirm != true; part++)
                    {
                        if (!Press(FileScreens.NextPart, FileScreens.RequestKey)) break;
                        Moment("request part " + (part + 2));
                    }
                    if (approved && Drawn(FileScreens.Yes, null) is RoomButton confirm)
                    {
                        // Confirm arrives behind its lock: drawn, ring part filled, a press taken by nothing.
                        Draw();
                        if (plane.Kit.Bar.Locking != confirm) momentFailures.Add("Confirm arrived without its lock");
                        Moment("confirm locked");
                        confirm.Press.SettleForRender();
                        confirm.Press.PressForRender(991);
                        Draw();
                        if (sent.OfType<ExecutionRespondToApprovalCommand>().Any()) failures.Add("judge room: Confirm took a press behind its lock");
                        plane.Kit.Bar.AgeLockForRender(RoomBar.ConfirmLockSeconds);
                        Draw();
                        Moment("confirm");
                    }
                    else if (approved) Moment("confirm");
                    approved = approved && Press(FileScreens.Yes) && sent.OfType<ExecutionRespondToApprovalCommand>().Count() == 1;
                    if (approved && sent.OfType<ExecutionRespondToApprovalCommand>().Single().Payload.Decision != ApprovalDecision.Approve)
                        failures.Add("judge room: Confirm sent a denial");
                    if (!approved && DirectedRun()?.PendingApprovals.FirstOrDefault() is ApprovalView approval)
                        SendInstead("Approve and Confirm", commands.RespondToApproval(DirectedRun()!.ExecutionId, approval.ApprovalId, ApprovalDecision.Approve));
                }

                // At work, then Build Failed.
                if (Until(() => DirectedRun()?.ActiveTools.Count > 0 || DirectedRun()?.Status == ExecutionStatus.Completed, "the approved turn at work")
                    && DirectedRun()?.ActiveTools.Count > 0) Moment("at work");
                if (Until(() => DirectedRun()?.Status == ExecutionStatus.Completed, "the approved turn's end"))
                {
                    Fresh();
                    Moment("result build failed");
                    OpenSection(Directed()!.WorkstreamId, FileSection.Checks);
                    Moment("checks build failed");
                }

                // Write Instruction: an offered instruction chosen, read and sent; the correction at work; Build Passed.
                if (Directed() is not WorkstreamView directedTask) { failures.Add("judge room: the demonstration's directed task never came"); return failures; }
                OpenSection(directedTask.WorkstreamId, FileSection.Activity);
                var told = Press(FileScreens.TellIt);
                if (told) Moment("write instruction");
                told = told && Press(FileScreens.Preset, "0");
                if (told) Moment("instruction chosen");
                // The redesign's way on from a chosen instruction is its main action (tell-it, Review Instruction), then Confirm.
                if (told && Drawn(FileScreens.TellIt, null) != null && Press(FileScreens.TellIt)) Moment("instruction review");
                for (var part = 0; told && part < 8 && Opened()?.Screen.Instruction.Read == false; part++) Press(FileScreens.NextInstructionPart);
                told = told && Press(FileScreens.SendInstruction);
                if (told && !sent.OfType<ExecutionSendInstructionCommand>().Any()) ConfirmBehindLock("instruction");
                told = told && sent.OfType<ExecutionSendInstructionCommand>().Any();
                if (!told && DirectedRun() is ExecutionView run && fallback.InstructionsFor(run.ExecutionId).FirstOrDefault() is PresetInstruction preset)
                    SendInstead("the offered instruction", commands.SendInstruction(run.ExecutionId, preset.Text));
                if (Until(() => DirectedRun()?.ActiveTools.Count > 0 || DirectedRun()?.LastTestRun?.Outcome == TestOutcome.Passed, "the correction at work", 60)
                    && DirectedRun()?.ActiveTools.Count > 0) Moment("correction at work");
                if (Until(() => DirectedRun()?.LastTestRun?.Outcome == TestOutcome.Passed, "the passing checks", 60))
                {
                    Fresh();
                    Moment("result build passed");
                    OpenSection(Directed()!.WorkstreamId, FileSection.Checks);
                    Moment("checks build passed");
                }

                // The end: with no file open the end hold runs and the recording starts again.
                var ending = fallback.Player;
                var plays = ending?.Plays ?? 0;
                CloseFile();
                if (Until(() => !ReferenceEquals(fallback.Player, ending) || (fallback.Player?.Plays ?? 0) > plays, "the restart after the end", 120)) Moment("restarted");
                if (sent.Count != 3) failures.Add("judge room: " + sent.Count + " commands were sent, not the answer, the approval and the instruction");
            }
            catch (Exception error) { failures.Add("judge room: the walk stopped: " + error); }
            finally
            {
                fallback?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// The judge moments the redesign draws today, each failing the walk where it is not drawn by the redesign alone or a
        /// press found no drawn control: at work, the question and its answer, the request with Approve, Cancel and Confirm,
        /// and the offered instructions. The rest (the recorded run's launch, result, Changes, Checks and end, the
        /// demonstration's launch, the results and Checks, the correction's send and the restart) are listed until their
        /// units land, then added here. Every moment's drawn redesign is held to the frame checks either way.
        /// </summary>
        private static readonly string[] CoveredMoments =
        {
            "recorded at work", "at work", "correction at work", "question", "question part", "answer chosen", "answers reviewed",
            "answer confirm locked", "answer confirm", "answer sent", "it asks", "confirm cancel offered", "cancelled asks again",
            "request part", "confirm locked", "confirm", "write instruction", "instruction chosen",
        };

        private static bool Covered(string moment) =>
            CoveredMoments.Any(each => moment == each || moment.StartsWith(each + " ", StringComparison.Ordinal) && char.IsDigit(moment[each.Length + 1]));

        /// <summary>The name of the nearest Halcyonic component that owns <paramref name="part"/>, or null for the walk's own camera.</summary>
        private static string? OwnerOf(Transform part)
        {
            for (var at = part; at != null; at = at.parent)
            {
                if (at.GetComponent<Camera>() != null) return null;
                foreach (var component in at.GetComponents<MonoBehaviour>())
                {
                    var type = component.GetType();
                    // A label's own helpers (its runs and its shimmer) belong to whatever part owns the label.
                    if (component is GlazeRuns || component is GlazeShimmer) continue;
                    if (type.Namespace?.StartsWith("Halcyonic", StringComparison.Ordinal) == true && type.Name != nameof(MenuDirector)) return type.Name;
                }
            }
            return null;
        }

        /// <summary>
        /// The frame checks on what the redesign draws: every word whole; every vertex of its parts inside the field less its
        /// margin, the head turned to the workspace and level (the design's angles are from the eyes, level); and each word's
        /// strokes at least 4.5:1 against what they stand on, as the render drew them.
        /// </summary>
        private static List<string> RoomFrameChecks(GameObject root, Camera camera, Texture2D image, Vector3 eyes, MenuPlane plane)
        {
            var failures = new List<string>();
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.GetComponent<Renderer>() is not Renderer renderer || !renderer.enabled || filter.sharedMesh == null) continue;
                if (OwnerOf(filter.transform)?.StartsWith("Room", StringComparison.Ordinal) != true) continue;
                points.AddRange(filter.sharedMesh.vertices.Select(vertex => filter.transform.TransformPoint(vertex)));
            }
            if (points.Count > 0 && ViewField.Current is ViewField field)
                failures.AddRange(GlazeChecks.InsideField("redesign", points, eyes, plane.WorkspaceCentre, 0f, field));
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrWhiteSpace(label.text) || label.GetComponent<Renderer>() is Renderer shown && !shown.enabled) continue;
                if (OwnerOf(label.transform)?.StartsWith("Room", StringComparison.Ordinal) != true) continue;
                label.ForceMeshUpdate(true);
                if (label.isTextTruncated) failures.Add(PathOf(label.transform) + " \"" + Codes(label.text) + "\" is cut");
                // The label's rectangle on the render: its strokes against the most common colour under them.
                var bounds = label.textBounds;
                var corners = new[] { new Vector3(bounds.min.x, bounds.min.y), new Vector3(bounds.max.x, bounds.max.y), new Vector3(bounds.min.x, bounds.max.y), new Vector3(bounds.max.x, bounds.min.y) }
                    .Select(corner => camera.WorldToScreenPoint(label.transform.TransformPoint(corner))).ToList();
                if (corners.Any(corner => corner.z <= 0f)) continue;
                var rect = new RectInt(Mathf.FloorToInt(corners.Min(c => c.x)), Mathf.FloorToInt(corners.Min(c => c.y)),
                    Mathf.CeilToInt(corners.Max(c => c.x) - corners.Min(c => c.x)) + 1, Mathf.CeilToInt(corners.Max(c => c.y) - corners.Min(c => c.y)) + 1);
                if (rect.width < 2 || rect.height < 2) continue;
                var ground = MostCommon(image, rect);
                var contrast = GlazeChecks.Contrast(image, rect, ground);
                if (contrast < 4.5f) failures.Add(PathOf(label.transform) + " \"" + Codes(label.text) + "\" reaches " + contrast.ToString("0.0") + ":1, under 4.5:1");
            }
            return failures;
        }

        /// <summary>
        /// For the census, never a failure: every drawn label holding facts joined by a dot (the form the owner rejected),
        /// every older label cut, and every pair of the redesign's labels whose rectangles on the render overlap.
        /// </summary>
        private static string Census(GameObject root, Camera camera)
        {
            var dots = new List<string>();
            var cut = new List<string>();
            var rects = new List<(string Text, Rect Rect, Transform Owner)>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrWhiteSpace(label.text) || label.GetComponent<Renderer>() is Renderer shown && !shown.enabled) continue;
                var owner = OwnerOf(label.transform) ?? "?";
                var text = label.text.Length > 60 ? label.text.Substring(0, 60) : label.text;
                if (label.text.Contains("\u00b7")) dots.Add(owner + " \"" + text.Replace('\n', ' ') + "\"");
                if (!owner.StartsWith("Room", StringComparison.Ordinal))
                {
                    label.ForceMeshUpdate(true);
                    if (label.isTextTruncated) cut.Add(owner + " \"" + text.Replace('\n', ' ') + "\"");
                    continue;
                }
                var bounds = label.textBounds;
                var corners = new[] { new Vector3(bounds.min.x, bounds.min.y), new Vector3(bounds.max.x, bounds.max.y) }
                    .Select(corner => camera.WorldToScreenPoint(label.transform.TransformPoint(corner))).ToList();
                if (corners.Any(corner => corner.z <= 0f)) continue;
                var rect = Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[1].x), Mathf.Min(corners[0].y, corners[1].y),
                    Mathf.Max(corners[0].x, corners[1].x), Mathf.Max(corners[0].y, corners[1].y));
                rects.Add((text.Replace('\n', ' '), rect, label.transform.parent));
            }
            var overlaps = new List<string>();
            for (var a = 0; a < rects.Count; a++)
            {
                for (var b = a + 1; b < rects.Count; b++)
                {
                    if (rects[a].Owner == rects[b].Owner) continue;
                    var x = Mathf.Min(rects[a].Rect.xMax, rects[b].Rect.xMax) - Mathf.Max(rects[a].Rect.xMin, rects[b].Rect.xMin);
                    var y = Mathf.Min(rects[a].Rect.yMax, rects[b].Rect.yMax) - Mathf.Max(rects[a].Rect.yMin, rects[b].Rect.yMin);
                    var smaller = Mathf.Min(rects[a].Rect.width * rects[a].Rect.height, rects[b].Rect.width * rects[b].Rect.height);
                    if (x > 0f && y > 0f && smaller > 0f && x * y > 0.2f * smaller) overlaps.Add("\"" + rects[a].Text + "\" over \"" + rects[b].Text + "\"");
                }
            }
            return string.Join("; ", dots) + " | " + string.Join("; ", cut) + " | " + string.Join("; ", overlaps);
        }

        /// <summary>The most common colour inside <paramref name="rect"/>, quantized, as the ground words stand on.</summary>
        private static Color MostCommon(Texture2D image, RectInt rect)
        {
            var counts = new Dictionary<int, (int Count, Color Colour)>();
            for (var y = Mathf.Max(0, rect.yMin); y < Mathf.Min(image.height, rect.yMax); y++)
            {
                for (var x = Mathf.Max(0, rect.xMin); x < Mathf.Min(image.width, rect.xMax); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    var key = ((int)(pixel.r * 31f) << 10) | ((int)(pixel.g * 31f) << 5) | (int)(pixel.b * 31f);
                    counts[key] = counts.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Colour) : (1, pixel);
                }
            }
            return counts.Count == 0 ? Color.black : counts.Values.OrderByDescending(each => each.Count).First().Colour;
        }

        /// <summary>Every label drawn names no brand, as JudgeWordsTests lists them.</summary>
        private static List<string> RoomBrands(GameObject root)
        {
            var found = new List<string>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || label.GetComponent<Renderer>() is Renderer shown && !shown.enabled) continue;
                foreach (var brand in JudgeBrands)
                    if (Regex.IsMatch(label.text, @"\b" + Regex.Escape(brand) + @"\b"))
                        found.Add(PathOf(label.transform) + " names " + brand);
            }
            return found;
        }
    }
}
