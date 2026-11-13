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
        /// the labelled demonstration by its own decision points (each question, approval and failed check it offers
        /// instructions for, each turn at work, its result and Checks, the restart), naming no task, title or count of one
        /// story. At each moment the redesign must draw it and nothing of the older views may
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
                    try { found.AddRange(RenderRoomWalk(folder, moments, null)); }
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

        /// <summary>
        /// The walk's harness, one for both walks: the director set up as the release WorkspaceDirector sets it up, drawn at each
        /// moment and held to the frame checks, every press through what is drawn. With <paramref name="live"/> null it plays the
        /// bundled recordings as a judge's headset does; with it, a session to that control plane, as a headset with a token.
        /// </summary>
        private static IEnumerable<string> RenderRoomWalk(string folder, List<string> moments, LiveWalk? live)
        {
            var failures = new List<string>();
            var root = new GameObject("Judge room walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            DemonstrationFallback? fallback = null;
            ControlPlaneApi? api = null;
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
                var hasRecordedRun = live == null && Resources.Load<TextAsset>("HalcyonicRecordedRun") != null;
                var target = live == null ? null : ControlPlaneTarget.Local(live.Endpoint, live.Token);
                api = target?.CreateApi();
                fallback = live != null ? new DemonstrationFallback(target!.CreateSession(client), () => null) : new DemonstrationFallback(null,
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
                // Each press a person makes, through a drawn control or the frame of record; each sentence typed; the keyboard
                // the director opened and has not been answered.
                var presses = 0;
                var typed = 0;
                (string Prompt, Action<string> Done)? keyboard = null;
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
                    Api = () => api,
                    Demonstration = () => fallback.Line != null,
                    RecordedRun = () => fallback.ShowingRecordedRun,
                    KeyboardOffered = () => live != null,
                    // The system keyboard: the walk answers it with the journey's words, as the person's typing would.
                    Keyboard = (text, prompt, done, dismissed) => keyboard = (prompt, done),
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
                    MakeNewProject = host => live != null
                        ? new NewProjectFlow(host, commands, new KeptUnknownStart(new WalkPreferences(), () => host.State))
                        : NewProjectColumn.Create(host, commands, () => new MemoryCreationDraftStore()),
                    File = (host, task) => Present(task) == null ? null : new FileColumn(host, () => Present(task), commands,
                        // A live walk reads a file's sections (its Changes, its checks) from its own plane, as the headset reads them
                        // from the one it is on; the recorded walk reads the recording's (the lead's first journey, 2026-11-12).
                        () => (IIntelligenceReader?)api ?? fallback.Player?.Reads, execution => fallback.InstructionsFor(execution), () => ""),
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
                ExecutionView? Run(WorkstreamView? task) => task != null && State() is ClientProjection state ? state.CurrentExecution(task) : null;
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
                        presses++;
                        Draw();
                        return true;
                    }
                    if (Opened()?.Frame is MenuFrame record && director.PressForRender(MenuColumn.File, action, key, record, null))
                    {
                        momentFailures.Add("no drawn control for " + action + (key == null ? "" : " " + key) + ": the frame of record took it");
                        presses++;
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
                        presses++;
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
                        presses++;
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
                    // A result at rest offers its own Changes and Checks, which turn the result's page: a press of one reaches it.
                    var resultPage = section == FileSection.Changes ? RoomResult.ShowChanges : section == FileSection.Checks ? RoomResult.ShowChecks : null;
                    // Where the result offers only its other page (Changes, then Checks from it), that page first, as a person goes.
                    var otherPage = resultPage == RoomResult.ShowChecks ? RoomResult.ShowChanges : resultPage == RoomResult.ShowChanges ? RoomResult.ShowChecks : null;
                    if (Open() && !There() && resultPage != null && Drawn("section", key) == null && Drawn(resultPage, null) == null && otherPage != null
                        && PressedButton(otherPage, null) && plane.KitShows)
                        ways.Add("its page drew " + string.Join(" ", plane.Kit.Bar.Column.Where(each => each.gameObject.activeInHierarchy)
                            .Select(each => plane.Kit.Bar.ControlOf(each) is KitControl control ? control.Action : "?")));
                    if (Open() && !There() && resultPage != null && Drawn("section", key) == null && PressedButton(resultPage, null))
                    {
                        reachedBy = string.Join(", then ", ways);
                        Draw();
                        return;
                    }
                    if (Open() && !There() && key != null && !PressedButton("section", key)
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
                    // A recording says what it is wherever it plays (lead, 14:33Z): its own line, in its own words, drawn whole.
                    // The notice is the recording's own line whole, both sentences, word for word (lead, 17:45Z): the product's
                    // DemonstrationFallback.Notice for the kind of work shown, recorded or demonstration.
                    if (live == null && fallback.Line != null && DemonstrationFallback.Notice(fallback.ShowingRecordedRun) is string line
                        && !root.GetComponentsInChildren<TMP_Text>(false).Any(text => text.enabled && text.GetParsedText().Replace("\n", " ").Contains(line)))
                        found.Add("the recording's own line is not drawn whole: \"" + line + "\"");
                    // A line that is drawn and cannot be read is not said (lead, 00:47Z): nothing drawn nearer the eye crosses it.
                    if (live == null && fallback.Line != null && plane.KitShows && plane.Kit.Bar.NoticeWords is TMP_Text noticeWords
                        && plane.Kit.Bar.NoticePlate is Halcyonic.XR.UI.RoomSurface noticePlate)
                        found.AddRange(NoticeCovered(root, camera, noticeWords, noticePlate.transform).Select(part => "the recording's line is covered by " + part));
                    // No text joined on a dot anywhere a person reads (the owner, 2026-11-11; lead, 04:22Z): each one named.
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (!text.enabled || text.GetComponent<Renderer>() is Renderer hidden && !hidden.enabled) continue;
                        var read = text.GetParsedText().Replace("\n", " ");
                        if (read.Contains("\u00b7")) found.Add("a text joined on a dot: " + (OwnerOf(text.transform) ?? "?") + "/" + text.name + " \"" + read + "\"");
                    }
                    if (live == null && director.VoiceOffered) found.Add("voice is offered with no computer");
                    if (live == null && plane.KitShows && plane.Kit.Bar.Talk.gameObject.activeInHierarchy) found.Add("Hold to Talk is drawn with no computer");
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

                var handled = new HashSet<string>();
                // The tasks the walk has answered, approved or instructed: their results are what a person meets.
                var engaged = new HashSet<string>();
                var decisions = 0;
                var results = 0;
                // The recorded walk tries Cancel once on a confirmation; the live walk counts only a person's own presses, so it does not.
                var cancelTried = live != null;
                List<(WorkstreamView Task, ExecutionView Run)> Runs()
                {
                    var runs = new List<(WorkstreamView Task, ExecutionView Run)>();
                    if (State() is not ClientProjection now) return runs;
                    foreach (var each in now.Workstreams.Values)
                        if (now.CurrentExecution(each) is ExecutionView current) runs.Add((each, current));
                    return runs;
                }
                string ResultKey(ExecutionView run) => "r:" + run.ExecutionId + ":" + (run.LastTestRun?.TestRunId ?? "");
                string Outcome(ExecutionView run) => run.LastTestRun?.Outcome.ToString().ToLowerInvariant() ?? "unchecked";
                bool SentSince<T>(int before) => sent.Skip(before).OfType<T>().Any();
                ExecutionView? Current(string workstreamId) => Runs().FirstOrDefault(pair => pair.Task.WorkstreamId == workstreamId).Run;
                void Answer(WorkstreamView task, ExecutionView run, QuestionView question)
                {
                    OpenSection(task.WorkstreamId, FileSection.Waiting);
                    // The file reads the waiting decision on its next refresh (the critic of run 19: room-12 showed the answer still sending).
                    Fresh();
                    Moment("question");
                    // The prompt on the band, read to its last part where it pages.
                    for (var part = 2; part < 8 && plane.KitShows && plane.Kit.Bar.BandNextButton is RoomButton turning; part++)
                    {
                        turning.Press.SettleForRender();
                        turning.Press.PressForRender(992);
                        presses++;
                        Draw();
                        Moment("question part " + part);
                    }
                    var before = sent.Count;
                    var answered = Press(FileScreens.Choose, "0");
                    if (answered) Moment("answer chosen");
                    if (sent.Count != before) failures.Add("judge room: choosing an answer sent something");
                    // The way on is whatever the product draws: Review Answers where it stands, Send Answer, and a Confirm behind
                    // its lock only where the command's own policy asks one.
                    if (answered && !SentSince<ExecutionAnswerQuestionCommand>(before) && Press(FileScreens.NextQuestion)) Moment("answers reviewed");
                    if (answered && !SentSince<ExecutionAnswerQuestionCommand>(before)) answered = Press(FileScreens.SendAnswer);
                    if (answered && !SentSince<ExecutionAnswerQuestionCommand>(before)) ConfirmBehindLock("answer");
                    if (!SentSince<ExecutionAnswerQuestionCommand>(before) && Current(task.WorkstreamId)?.PendingQuestions.Any(each => each.QuestionId == question.QuestionId) == true)
                        SendInstead("the answer", commands.AnswerQuestion(run.ExecutionId, question.QuestionId,
                            question.Prompts.Select(prompt => new QuestionAnswer { Key = prompt.Key, Selected = prompt.Options.Take(1).Select(option => option.Label).ToList() })));
                    Moment("answer sent");
                }

                void Approve(WorkstreamView task, ExecutionView run, ApprovalView approval)
                {
                    OpenSection(task.WorkstreamId, FileSection.Waiting);
                    // The file reads the waiting decision on its next refresh (the critic of run 19: room-12 showed the answer still sending).
                    Fresh();
                    Moment("it asks");
                    if (Drawn(FileScreens.Deny, null) == null && Drawn(FileScreens.Approve, null) != null) momentFailures.Add("Approve stands without Deny");
                    var before = sent.Count;
                    var approved = Press(FileScreens.Approve);
                    // Where Approve asks a confirmation, Cancel once: the request asks again and nothing was sent.
                    if (approved && !cancelTried && !SentSince<ExecutionRespondToApprovalCommand>(before) && Drawn(FileScreens.Cancel, null) != null)
                    {
                        cancelTried = true;
                        Moment("confirm cancel offered");
                        Press(FileScreens.Cancel);
                        if (Drawn(FileScreens.Approve, null) == null) momentFailures.Add("Cancel did not bring the request back to Approve and Deny");
                        if (SentSince<ExecutionRespondToApprovalCommand>(before)) momentFailures.Add("Cancel sent an answer");
                        Moment("cancelled asks again");
                        approved = Press(FileScreens.Approve);
                    }
                    for (var part = 0; approved && part < 20 && !SentSince<ExecutionRespondToApprovalCommand>(before) && Opened()?.Steering.CanConfirm != true; part++)
                    {
                        if (!Press(FileScreens.NextPart, FileScreens.RequestKey)) break;
                        Moment("request part " + (part + 2));
                    }
                    if (approved && !SentSince<ExecutionRespondToApprovalCommand>(before) && Drawn(FileScreens.Yes, null) is RoomButton confirm)
                    {
                        // Confirm arrives behind its lock: drawn, ring part filled, a press taken by nothing.
                        Draw();
                        if (plane.Kit.Bar.Locking != confirm) momentFailures.Add("Confirm arrived without its lock");
                        Moment("confirm locked");
                        confirm.Press.SettleForRender();
                        confirm.Press.PressForRender(991);
                        Draw();
                        if (SentSince<ExecutionRespondToApprovalCommand>(before)) failures.Add("judge room: Confirm took a press behind its lock");
                        plane.Kit.Bar.AgeLockForRender(RoomBar.ConfirmLockSeconds);
                        Draw();
                        Moment("confirm");
                        approved = Press(FileScreens.Yes);
                    }
                    var answers = sent.Skip(before).OfType<ExecutionRespondToApprovalCommand>().ToList();
                    if (answers.Count > 1) failures.Add("judge room: one approval sent " + answers.Count + " answers");
                    if (answers.Any(each => each.Payload.Decision != ApprovalDecision.Approve)) failures.Add("judge room: Approve sent a denial");
                    if (answers.Count == 0)
                        SendInstead("Approve", commands.RespondToApproval(run.ExecutionId, approval.ApprovalId, ApprovalDecision.Approve));
                }

                void Instruct(WorkstreamView task, ExecutionView run)
                {
                    // Write Instruction: an offered instruction chosen, read and sent.
                    OpenSection(task.WorkstreamId, FileSection.Activity);
                    var before = sent.Count;
                    var told = Press(FileScreens.TellIt);
                    if (told) Moment("write instruction");
                    told = told && Press(FileScreens.Preset, "0");
                    if (told) Moment("instruction chosen");
                    // The redesign's way on from a chosen instruction is its main action (tell-it, Review Instruction), then Send.
                    if (told && !SentSince<ExecutionSendInstructionCommand>(before) && Drawn(FileScreens.TellIt, null) != null && Press(FileScreens.TellIt)) Moment("instruction review");
                    for (var part = 0; told && part < 8 && Opened()?.Screen.Instruction.Read == false; part++) Press(FileScreens.NextInstructionPart);
                    if (told && !SentSince<ExecutionSendInstructionCommand>(before)) told = Press(FileScreens.SendInstruction);
                    if (told && !SentSince<ExecutionSendInstructionCommand>(before)) ConfirmBehindLock("instruction");
                    if (!SentSince<ExecutionSendInstructionCommand>(before) && fallback.InstructionsFor(run.ExecutionId).FirstOrDefault() is PresetInstruction preset)
                        SendInstead("the offered instruction", commands.SendInstruction(run.ExecutionId, preset.Text));
                }


                // The live walk (LiveWalkCheck): a person's own journeys against a live control plane, by drawn controls only, each
                // journey's presses counted and written beside what the same takes in a terminal agent session at a desk.
                string? journey = null;
                var journeyDesk = "";
                var journeyFrom = 0;
                var journeyTyped = 0;
                var journeyWays = new List<string>();
                void Journey(string name, string desk)
                {
                    EndJourney();
                    (journey, journeyDesk, journeyFrom, journeyTyped) = (name, desk, presses, typed);
                    journeyWays.Clear();
                }
                void EndJourney()
                {
                    if (journey == null || live == null) return;
                    var count = presses - journeyFrom;
                    var words = typed - journeyTyped;
                    // A journey nothing drawn finished is not a count of zero: it is said as not reached.
                    var reached = !journeyWays.Any(way => way.Contains("the walk opened it") || way.StartsWith("nothing drawn", StringComparison.Ordinal));
                    var notRun = journeyWays.Any(way => way.StartsWith("not run", StringComparison.Ordinal));
                    var headset = notRun ? "not run" : reached ? count + (words == 0 ? "" : " presses and " + words + " typed") : "not reached by what is drawn (" + count + " presses before it)";
                    live.Presses.Add(journey + "\t" + headset + "\t" + journeyDesk + "\t" + string.Join("; ", journeyWays));
                    journey = null;
                }
                // The redesign's main action drawn now, or a control that opens the keyboard where the person is asked for words.
                RoomButton? MainDrawn() => plane.KitShows ? plane.Kit.Bar.Column.FirstOrDefault(each => each.gameObject.activeInHierarchy && each.Main) : null;
                RoomButton? TypeDrawn() => plane.KitShows
                    ? plane.Kit.Bar.Column.FirstOrDefault(each => each.gameObject.activeInHierarchy && plane.Kit.Bar.ControlOf(each) is KitControl control
                        && (control.Action.StartsWith("type", StringComparison.Ordinal) || control.Action.Contains("-type")))
                        ?? (plane.Kit.Bar.Type.gameObject.activeInHierarchy ? plane.Kit.Bar.Type : null)
                    : null;
                string ActionOf(RoomButton button) => plane.Kit.Bar.ControlOf(button) is KitControl control ? control.Action + (control.Key == null ? "" : "/" + control.Key) : "?";
                void PressDrawn(RoomButton button, string why)
                {
                    // A Confirm behind its lock takes its press once the ring has filled, as a person's hold fills it: one press.
                    if (plane.KitShows && plane.Kit.Bar.Locking == button)
                    {
                        plane.Kit.Bar.AgeLockForRender(RoomBar.ConfirmLockSeconds);
                        Draw();
                    }
                    button.Press.SettleForRender();
                    button.Press.PressForRender(987);
                    presses++;
                    journeyWays.Add(why + " " + ActionOf(button));
                    Draw();
                }
                // Where the redesign draws nothing to press, the frame of record's main prompt, said in the census as such.
                bool PressFrameMain()
                {
                    foreach (var (kind, view) in plane.Shown.Reverse())
                    {
                        var prompt = view.Footer.Showing?.All.Select(slot => slot.Prompt).FirstOrDefault(each => each.Main && each.Available);
                        if (prompt == null || !director.PressForRender(kind, prompt.Id, null, view.Frame ?? view.InPlaceOf, view.Side)) continue;
                        presses++;
                        journeyWays.Add("no drawn control: the frame of record's " + prompt.Id + " took it");
                        Draw();
                        return true;
                    }
                    // The column beside, as New Project under the redesign, which no plane frame stands for.
                    if (navigator.Beside?.Frame is MenuFrame frame
                        && frame.Footer.All.Select(slot => slot.Prompt).FirstOrDefault(each => each.Main && each.Available) is Prompt main
                        && director.PressForRender(MenuColumn.File, main.Id, null, frame, null))
                    {
                        presses++;
                        journeyWays.Add("no drawn control: the frame of record's " + main.Id + " took it");
                        Draw();
                        return true;
                    }
                    return false;
                }
                // Steps the person takes until the goal holds: words where a keyboard is open, a control that asks for words
                // where words are what is missing, else the screen's main action. Where none is drawn the journey stops there.
                bool DriveUntil(Func<bool> goal, string label, string words, int steps = 12)
                {
                    var wrote = false;
                    var tried = new HashSet<string>();
                    for (var step = 1; step <= steps; step++)
                    {
                        Fresh();
                        if (goal()) return true;
                        Moment(label + " " + step);
                        if (keyboard is { } open)
                        {
                            keyboard = null;
                            typed++;
                            wrote = true;
                            journeyWays.Add("typed into \"" + open.Prompt + "\"");
                            open.Done(words);
                            Draw();
                            continue;
                        }
                        if (!wrote && TypeDrawn() is RoomButton type) { PressDrawn(type, "pressed"); continue; }
                        if (MainDrawn() is RoomButton main) { PressDrawn(main, "pressed main"); continue; }
                        if (PressFrameMain()) continue;
                        // No main action to take: the next drawn control this journey has not pressed, as a person tries what
                        // is there (never Close, Hold to Talk or a control that asks for words already given).
                        if (plane.KitShows && plane.Kit.Bar.Column.FirstOrDefault(each => each.gameObject.activeInHierarchy && !tried.Contains(ActionOf(each))
                            && ActionOf(each) != Footer.Close && !ActionOf(each).Contains("talk") && !(wrote && TypeDrawn() == each)) is RoomButton other)
                        {
                            tried.Add(ActionOf(other));
                            PressDrawn(other, "tried");
                            continue;
                        }
                        if (PressRow(control => !tried.Contains("row " + control.Action + (control.Key == null ? "" : "/" + control.Key))) is string row)
                        {
                            tried.Add(row.Replace("pressed row ", "row "));
                            journeyWays.Add("tried " + row.Replace("pressed ", ""));
                            continue;
                        }
                        if (goal()) return true;
                        journeyWays.Add("nothing drawn to press at step " + step + (navigator.Beside?.Frame is MenuFrame stuck
                            ? " (its frame of record offers: " + string.Join(", ", stuck.Footer.All.Select(slot => slot.Prompt.Id + (slot.Prompt.Main ? " main" : "") + (slot.Prompt.Available ? "" : " unavailable: " + slot.Prompt.Reason))
                                .Concat(stuck.Lines.Where(line => line.Action != null).Select(line => "line " + line.Action + (line.Available ? "" : " unavailable")))) + ")"
                            : ""));
                        return goal();
                    }
                    return goal();
                }
                void WalkLive(LiveWalk on)
                {
                    if (!Until(() => Session()?.Status.IsLive == true && State() != null, "the live control plane", 60)) return;
                    Moment("live launch");
                    var projectsBefore = State()!.Projects.Count;
                    var tasksBefore = State()!.Workstreams.Count;

                    Journey("start a project", "2: make a folder, start the agent in it");
                    if (Press(ProjectsScreens.NewProject)) journeyWays.Add("pressed " + ProjectsScreens.NewProject);
                    else journeyWays.Add("no drawn New Project");
                    var started = DriveUntil(() => State()!.Projects.Count > projectsBefore && State()!.Workstreams.Count > tasksBefore
                        && State()!.Workstreams.Values.Any(task => State()!.CurrentExecution(task) != null), "new project", on.Idea);
                    if (!started) failures.Add("live walk: no project and task started through drawn controls (" + string.Join("; ", journeyWays) + ")");
                    var task = State()!.Workstreams.Values.FirstOrDefault(each => State()!.CurrentExecution(each) != null);
                    EndJourney();
                    if (task == null) return;

                    Journey("answer an approval", "1: one key");
                    if (Until(() => Current(task.WorkstreamId)?.PendingApprovals.Count > 0, "the live task's approval", 60)
                        && Current(task.WorkstreamId) is ExecutionView asking)
                    {
                        // The way to the waiting request is what the screen draws (its main action, a row naming the task): the
                        // walk opens the request itself only where nothing drawn reaches it, and says so.
                        DriveUntil(() => Drawn(FileScreens.Approve, null) != null, "to the request", "", 4);
                        Approve(task, asking, asking.PendingApprovals[0]);
                        decisions++;
                    }
                    EndJourney();

                    Journey("read the result", "0: it prints");
                    if (Until(() => Current(task.WorkstreamId)?.Status == ExecutionStatus.Completed, "the live task's result", 90))
                    {
                        Fresh();
                        Moment("live result");
                        OpenSection(task.WorkstreamId, FileSection.Activity);
                        journeyWays.Add(reachedBy);
                        Moment("live result read");
                    }
                    EndJourney();

                    Journey("see what changed", "1: git diff");
                    OpenSection(task.WorkstreamId, FileSection.Changes);
                    journeyWays.Add(reachedBy);
                    // The page is read from the plane, as the app's Update reads it: the shot waits for its answer, 15 s at most.
                    // Changes is the result's page of changed files where it stands (one Changes, lead 11:15Z), else the file's section.
                    bool Answered() => presentation.ResultPageFor(task.WorkstreamId, Current(task.WorkstreamId)?.ExecutionId).Page == RoomResultPage.Changes
                        || Opened()?.Screen is FileScreen read && (read.WhatChanged ?? read.WhyChanged ?? read.HowBuilt) != null;
                    var asked = DateTime.UtcNow;
                    while (!Answered() && DateTime.UtcNow - asked < TimeSpan.FromSeconds(15)) { Draw(); Thread.Sleep(250); }
                    journeyWays.Add(Answered() ? "its answer came in " + (int)(DateTime.UtcNow - asked).TotalSeconds + " s" : "no answer in 15 s");
                    Moment("live changes");
                    EndJourney();

                    Journey("keep it", "0 to 1: the work is in the folder; a commit is 1");
                    if (Drawn(RoomResult.ShowAdd, null) != null && Press(RoomResult.ShowAdd))
                    {
                        journeyWays.Add("pressed " + RoomResult.ShowAdd);
                        Moment("live keep review");
                        if (Press(RoomResult.AddConfirm)) journeyWays.Add("pressed " + RoomResult.AddConfirm);
                        Fresh();
                        Moment("live kept");
                    }
                    else journeyWays.Add("nothing drawn to keep it (Add to Project not offered)");
                    EndJourney();
                    // Give the next instruction at rest, typed and picked from the ready-made ones (lead, 12:04Z); said needs a
                    // microphone, which a batch Editor has none of, so it is listed by what is drawn and not run.
                    int Instructions() => sent.OfType<ExecutionSendInstructionCommand>().Count();
                    Fresh();
                    Journey("give the next instruction, typed", "1 typed + 1: type it, Enter");
                    var told = Instructions();
                    if (!DriveUntil(() => Instructions() > told, "instruct typed", on.NextInstruction, 6)) journeyWays.Add("nothing drawn sent it");
                    EndJourney();
                    Until(() => Current(task.WorkstreamId)?.Status == ExecutionStatus.Completed, "the instructed turn's end", 90);
                    Fresh();
                    Journey("give the next instruction, picked", "1 typed + 1: a desk has no ready-made ones");
                    told = Instructions();
                    if (Press(FileScreens.TellIt)) { journeyWays.Add("pressed " + FileScreens.TellIt); Moment("instruct picked 1"); }
                    if (Press(FileScreens.Preset, "0")) { journeyWays.Add("pressed " + FileScreens.Preset + "/0"); Moment("instruct picked 2"); }
                    if (!DriveUntil(() => Instructions() > told, "instruct picked", "", 4)) journeyWays.Add("nothing drawn sent it");
                    EndJourney();
                    Journey("give the next instruction, said", "1 typed + 1: type it, Enter");
                    journeyWays.Add("not run: a batch Editor has no microphone; Hold to Talk "
                        + (plane.KitShows && plane.Kit.Bar.Talk.gameObject.activeInHierarchy ? "is drawn" : "is not drawn"));
                    EndJourney();
                    if (sent.Count == 0) failures.Add("live walk: nothing was sent");
                }
                if (live != null)
                {
                    WalkLive(live);
                    EndJourney();
                    return failures;
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

                // The demonstration by its own decision points, as the lead's decision of 2026-11-12 asks: whatever waits on the
                // person (a question, an approval, a failed check its recording offers instructions for) is reached the way a
                // person reaches it and answered through the drawn controls, as many presses as the product draws and no more
                // expected; each turn's end is shot as its result and its Checks. No task, title or count of one story is named,
                // so another recording walks the same way.
                var demonstration = fallback.Player;
                var plays = demonstration?.Plays ?? 0;
                bool Restarted() => !ReferenceEquals(fallback.Player, demonstration) || (fallback.Player?.Plays ?? 0) > plays;
                // A turn already at rest when the demonstration begins is part of its story, not a result the walk saw arrive.
                foreach (var (_, settled) in Runs()) if (settled.Status == ExecutionStatus.Completed) handled.Add(ResultKey(settled));

                // The walk: one thing at a time, in the order a person meets them, until the recording starts again.
                var walkUntil = DateTime.UtcNow.AddSeconds(240);
                var atWork = false;
                var idleSince = DateTime.UtcNow;
                var closed = false;
                while (DateTime.UtcNow < walkUntil && !Restarted())
                {
                    Pump();
                    var runsNow = Runs();
                    if (!atWork && runsNow.FirstOrDefault(pair => pair.Run.ActiveTools.Count > 0).Run != null)
                    {
                        Draw();
                        Moment("at work");
                        atWork = true;
                        idleSince = DateTime.UtcNow;
                        continue;
                    }
                    // A result a person meets: of a task the walk answered or has open; another task finishing out of view is
                    // marked seen and not opened, as a person would not open it.
                    foreach (var (_, aside) in runsNow.Where(pair => pair.Run.Status == ExecutionStatus.Completed && !handled.Contains(ResultKey(pair.Run))
                        && !engaged.Contains(pair.Task.WorkstreamId) && Opened()?.Now?.Character.WorkstreamId != pair.Task.WorkstreamId).ToList())
                        handled.Add(ResultKey(aside));
                    var ended = runsNow.FirstOrDefault(pair => pair.Run.Status == ExecutionStatus.Completed && !handled.Contains(ResultKey(pair.Run)));
                    if (ended.Run != null)
                    {
                        handled.Add(ResultKey(ended.Run));
                        results++;
                        atWork = false;
                        Fresh();
                        var outcome = Outcome(ended.Run);
                        Moment("result " + outcome);
                        OpenSection(ended.Task.WorkstreamId, FileSection.Checks);
                        Moment("checks " + outcome);
                        idleSince = DateTime.UtcNow;
                        closed = false;
                        continue;
                    }
                    var acted = false;
                    foreach (var (task, run) in runsNow)
                    {
                        if (run.PendingQuestions.FirstOrDefault(each => !handled.Contains("q:" + each.QuestionId)) is QuestionView question)
                        {
                            handled.Add("q:" + question.QuestionId);
                            Answer(task, run, question);
                        }
                        else if (run.PendingApprovals.FirstOrDefault(each => !handled.Contains("a:" + each.ApprovalId)) is ApprovalView approval)
                        {
                            handled.Add("a:" + approval.ApprovalId);
                            Approve(task, run, approval);
                        }
                        else if (run.Status == ExecutionStatus.Completed && run.LastTestRun is TestRunResultView test && test.Outcome != TestOutcome.Passed
                            && !handled.Contains("i:" + test.TestRunId) && fallback.InstructionsFor(run.ExecutionId).Count > 0)
                        {
                            handled.Add("i:" + test.TestRunId);
                            Instruct(task, run);
                        }
                        else continue;
                        decisions++;
                        engaged.Add(task.WorkstreamId);
                        acted = true;
                        atWork = false;
                        break;
                    }
                    if (acted) { idleSince = DateTime.UtcNow; closed = false; continue; }
                    // Nothing waits and nothing new arrived: close the file, as a person who has seen the result does, so the
                    // recording's end hold runs and it starts again.
                    if (!closed && DateTime.UtcNow - idleSince > TimeSpan.FromSeconds(2) && Opened() != null)
                    {
                        CloseFile();
                        closed = true;
                    }
                    Thread.Sleep(20);
                }
                if (Restarted()) { Draw(); Moment("restarted"); }
                else failures.Add("judge room: waited 240 s and the restart after the end never came");
                if (decisions == 0) failures.Add("judge room: the demonstration offered no decision");
                if (results == 0) failures.Add("judge room: the demonstration showed no result");
                if (sent.Count != decisions) failures.Add("judge room: " + sent.Count + " commands were sent for " + decisions + " decisions");
            }
            catch (Exception error) { failures.Add("judge room: the walk stopped: " + error); }
            finally
            {
                fallback?.Dispose();
                api?.Dispose();
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
        /// <summary>
        /// The live walk: the judges' walk's harness against a live control plane, named by HALCYONIC_LIVE_ENDPOINT and
        /// HALCYONIC_LIVE_TOKEN_FILE, as a headset with a token reaches one. A person's journeys (start a project with a sentence,
        /// answer the approval, read the result, see what changed, keep it), every press through what is drawn and every
        /// sentence through the keyboard's own hook; renders and moments.tsv as the judges' walk writes them, and presses.tsv:
        /// each journey's presses beside what the same takes in a terminal agent session at a desk. It refuses any plane but a
        /// scratch one on loopback (never 47800) and any token in the owner's data folder. Renders land in Builds/LiveWalkRenders.
        /// </summary>
        public static void LiveWalkCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = new List<string>();
            var endpoint = Environment.GetEnvironmentVariable("HALCYONIC_LIVE_ENDPOINT");
            var tokenFile = Environment.GetEnvironmentVariable("HALCYONIC_LIVE_TOKEN_FILE");
            var owners = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".halcyonic"));
            Uri? uri = null;
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(tokenFile) || !Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
                failures.Add("live walk: HALCYONIC_LIVE_ENDPOINT and HALCYONIC_LIVE_TOKEN_FILE must name a scratch control plane");
            else if (!uri.IsLoopback || uri.Port == 47800)
                failures.Add("live walk: refused " + uri.Host + ":" + uri.Port + ": only a scratch control plane on loopback, never 47800");
            else if (Path.GetFullPath(tokenFile) is var full && (full == owners || full.StartsWith(owners + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                failures.Add("live walk: refused a token in the owner's data folder");
            else
            {
                var was = RoomDesign.Enabled;
                RoomDesign.Enabled = true;
                // One size a run, since a live plane's state goes on from one run to the next: HALCYONIC_LIVE_SIZE=Larger
                // walks at the larger reading size, on a fresh plane of its own.
                var larger = Environment.GetEnvironmentVariable("HALCYONIC_LIVE_SIZE") == "Larger";
                if (larger) GlazeText.SetScale(Comfort.LargerTextScale);
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "LiveWalkRenders", larger ? "Larger" : ""));
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                var moments = new List<string> { "moment\tdrawn by\tdots | older cut | overlaps\tcoverage\tredesign drew\tolder parts drew\tactions drawn\treached by\tfailures" };
                var live = new LiveWalk(uri!, File.ReadAllText(Path.GetFullPath(tokenFile)).Trim(),
                    Environment.GetEnvironmentVariable("HALCYONIC_LIVE_IDEA") ?? "A page that says hello and counts my visits");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var context = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);
                try { failures.AddRange(RenderRoomWalk(folder, moments, live)); }
                catch (Exception error) { failures.Add("live walk: " + error); }
                finally { SynchronizationContext.SetSynchronizationContext(context); ViewField.Current = null; KeepFontAssetsAsCommitted(); RoomDesign.Enabled = was; if (larger) GlazeText.SetScale(1f); }
                File.WriteAllText(Path.Combine(folder, "moments.tsv"), string.Join("\n", moments) + "\n");
                File.WriteAllText(Path.Combine(folder, "presses.tsv"), "journey\theadset\tdesk\tthe way\n" + string.Join("\n", live.Presses) + "\n");
                foreach (var row in live.Presses) Debug.Log("Halcyonic: live walk: presses: " + row);
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: live walk: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: live walk: every check passed");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>A live control plane for the walk, its token, the idea a journey types, and each journey's count as it ends.</summary>
        private sealed class LiveWalk
        {
            public LiveWalk(Uri endpoint, string token, string idea) => (Endpoint, Token, Idea) = (endpoint, token, idea);

            public Uri Endpoint { get; }

            public string Token { get; }

            public string Idea { get; }

            /// <summary>The words the walk types as the next instruction at rest.</summary>
            public string NextInstruction => "Add a button that resets the count";

            public List<string> Presses { get; } = new List<string>();
        }

        /// <summary>A device's preferences held in memory for one walk, so a walk keeps nothing in the Editor's own.</summary>
        private sealed class WalkPreferences : IDevicePreferences
        {
            private readonly Dictionary<string, string> kept = new Dictionary<string, string>();

            public string? Get(string key) => kept.TryGetValue(key, out var value) ? value : null;

            public void Set(string key, string? value)
            {
                if (value == null) kept.Remove(key);
                else kept[key] = value;
            }
        }

        private static readonly string[] CoveredMoments =
        {
            "recorded at work", "at work", "question", "question part", "answer chosen", "answers reviewed",
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
        /// <summary>
        /// Every drawn part not clearly behind the notice's words (its nearest corner 5 mm or more behind them) whose screen box
        /// crosses theirs by more than half a dp, named by its owner and part; empty where nothing does. The notice's own plate
        /// and words are left out.
        /// </summary>
        private static List<string> NoticeCovered(GameObject root, Camera camera, TMP_Text words, Transform plate)
        {
            var covered = new SortedSet<string>();
            words.ForceMeshUpdate(true);
            var bounds = words.textBounds;
            var ends = new[] { new Vector3(bounds.min.x, bounds.min.y), new Vector3(bounds.max.x, bounds.max.y) }
                .Select(corner => camera.WorldToScreenPoint(words.transform.TransformPoint(corner))).ToList();
            if (ends.Any(corner => corner.z <= 0f)) return covered.ToList();
            var notice = Rect.MinMaxRect(Mathf.Min(ends[0].x, ends[1].x), Mathf.Min(ends[0].y, ends[1].y),
                Mathf.Max(ends[0].x, ends[1].x), Mathf.Max(ends[0].y, ends[1].y));
            var depth = Mathf.Min(ends[0].z, ends[1].z);
            if (notice.width <= 0f || notice.height <= 0f) return covered.ToList();
            // The kit's small touch, half a dp, in screen terms at the words' own distance (AUDIT, 01:52Z).
            var middle = words.transform.TransformPoint(bounds.center);
            var touch = Vector3.Distance(camera.WorldToScreenPoint(middle), camera.WorldToScreenPoint(middle + words.transform.right * RoomType.Units(0.5f)));
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || renderer.transform == plate || renderer.transform.IsChildOf(plate)) continue;
                var box = renderer.bounds;
                var points = new List<Vector3>();
                for (var i = 0; i < 8; i++)
                    points.Add(camera.WorldToScreenPoint(new Vector3(i % 2 == 0 ? box.min.x : box.max.x, i / 2 % 2 == 0 ? box.min.y : box.max.y, i / 4 == 0 ? box.min.z : box.max.z)));
                if (points.Any(point => point.z <= 0f)) continue;
                // The notice keeps a band no part is laid over (lead, 00:47Z): a part is behind only when its NEAREST corner is
                // 5 mm or more behind the words; any other part that crosses them stands over it (AUDIT, 01:52Z).
                if (points.Min(point => point.z) >= depth + 0.005f) continue;
                var rect = Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
                var x = Mathf.Min(rect.xMax, notice.xMax) - Mathf.Max(rect.xMin, notice.xMin);
                var y = Mathf.Min(rect.yMax, notice.yMax) - Mathf.Max(rect.yMin, notice.yMin);
                if (x > touch && y > touch) covered.Add((OwnerOf(renderer.transform) ?? "?") + "/" + renderer.name);
            }
            return covered.ToList();
        }

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
