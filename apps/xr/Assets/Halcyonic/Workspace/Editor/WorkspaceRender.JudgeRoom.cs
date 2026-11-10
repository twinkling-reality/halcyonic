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
        /// <summary>
        /// What a judge sees in the redesign (<see cref="RoomDesign.Enabled"/> on), as the release player runs it with no
        /// computer connected: the recorded run moment by moment (launch, at work, the result, Changes, Checks, its end), then
        /// the labelled demonstration (the question, it asks and Confirm, at work, Build Failed, the written instruction, the
        /// correction, Build Passed, the restart). At each moment the redesign must draw it and nothing of the older views may
        /// show; what it draws is held to the frame checks (every word whole, inside a Quest 3S field, contrast at least 4.5:1,
        /// no brand names) at both reading sizes. A press goes through a drawn control where the redesign draws one; where none
        /// is drawn the walk says so and moves the recording on itself, so every later moment is still seen. Renders and the
        /// moment list (moments.tsv) land in Builds/JudgeRoomRenders.
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
                    var moments = new List<string> { "moment\tredesign drew\tolder parts drew\tfailures" };
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
                    if (Opened()?.Frame is MenuFrame record && director.PressForRender(MenuColumn.File, action, key, record, null))
                    {
                        momentFailures.Add("no drawn control for " + action + (key == null ? "" : " " + key) + ": the frame of record took it");
                        Draw();
                        return true;
                    }
                    return false;
                }
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
                // Where nothing took a press, the walk sends what a judge's press would have, so the recording moves on.
                void SendInstead(string what, CommandEnvelope command)
                {
                    momentFailures.Add("nothing drawn took " + what + ": the walk sent it");
                    var session = Session()!;
                    sent.Add(command);
                    submissions.Submit(each => session.SubmitAsync(each), command, ExecutionOf(command));
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
                    ForceMeshes(root);
                    camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                    var image = Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    var (redesign, older) = Parts();
                    var found = new List<string>(momentFailures);
                    momentFailures.Clear();
                    if (expectDrawn && redesign.Count == 0) found.Add("the redesign draws nothing");
                    if (older.Count > 0) found.Add("older parts draw: " + string.Join(", ", older));
                    found.AddRange(RoomFrameChecks(root, camera, image, eyes, plane));
                    found.AddRange(RoomBrands(root));
                    UnityEngine.Object.DestroyImmediate(image);
                    moments.Add(name + "\t" + string.Join(", ", redesign) + "\t" + string.Join(", ", older) + "\t" + string.Join("; ", found));
                    failures.AddRange(found.Select(each => name + ": " + each));
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
                    director.OpenFile(task.WorkstreamId);
                    if (Until(() => Run(task)?.ActiveTools.Count > 0, "the recorded run at work", 60)) Moment("recorded at work");
                    if (Until(() => Run(task)?.Status == ExecutionStatus.Completed, "the recorded run's result", 120)) Moment("recorded result");
                    director.OpenFile(task.WorkstreamId, FileSection.Changes);
                    Moment("recorded changes");
                    director.OpenFile(task.WorkstreamId, FileSection.Checks);
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
                    director.OpenFile(directed.WorkstreamId, FileSection.Waiting);
                    Moment("question");
                    var answered = Press(FileScreens.Choose, "0") && Press(FileScreens.NextQuestion) && Press(FileScreens.SendAnswer)
                        && sent.OfType<ExecutionAnswerQuestionCommand>().Any();
                    if (!answered && DirectedRun()?.PendingQuestions.FirstOrDefault() is QuestionView question)
                        SendInstead("the answer", commands.AnswerQuestion(DirectedRun()!.ExecutionId, question.QuestionId,
                            question.Prompts.Select(prompt => new QuestionAnswer { Key = prompt.Key, Selected = prompt.Options.Take(1).Select(option => option.Label).ToList() })));
                    Moment("answer sent");
                }

                // It asks, and Confirm: the request read to its last part, then Approve and its confirmation.
                if (Until(() => DirectedRun()?.PendingApprovals.Count > 0, "the directed task's approval"))
                {
                    director.OpenFile(Directed()!.WorkstreamId, FileSection.Waiting);
                    Moment("it asks");
                    var approved = Press(FileScreens.Approve);
                    for (var part = 0; approved && part < 20 && Opened()?.Steering.CanConfirm != true; part++)
                        if (!Press(FileScreens.NextPart, FileScreens.RequestKey)) break;
                    if (approved) Moment("confirm");
                    approved = approved && Press(FileScreens.Yes) && sent.OfType<ExecutionRespondToApprovalCommand>().Any();
                    if (!approved && DirectedRun()?.PendingApprovals.FirstOrDefault() is ApprovalView approval)
                        SendInstead("Approve and Confirm", commands.RespondToApproval(DirectedRun()!.ExecutionId, approval.ApprovalId, ApprovalDecision.Approve));
                }

                // At work, then Build Failed.
                if (Until(() => DirectedRun()?.ActiveTools.Count > 0 || DirectedRun()?.Status == ExecutionStatus.Completed, "the approved turn at work")
                    && DirectedRun()?.ActiveTools.Count > 0) Moment("at work");
                if (Until(() => DirectedRun()?.Status == ExecutionStatus.Completed, "the approved turn's end"))
                {
                    Moment("result build failed");
                    director.OpenFile(Directed()!.WorkstreamId, FileSection.Checks);
                    Moment("checks build failed");
                }

                // Write Instruction: an offered instruction chosen, read and sent; the correction at work; Build Passed.
                if (Directed() is not WorkstreamView directedTask) { failures.Add("judge room: the demonstration's directed task never came"); return failures; }
                director.OpenFile(directedTask.WorkstreamId, FileSection.Activity);
                var told = Press(FileScreens.TellIt);
                if (told) Moment("write instruction");
                told = told && Press(FileScreens.Preset, "0");
                for (var part = 0; told && part < 8 && Opened()?.Screen.Instruction.Read == false; part++) Press(FileScreens.NextInstructionPart);
                told = told && Press(FileScreens.SendInstruction) && sent.OfType<ExecutionSendInstructionCommand>().Any();
                if (!told && DirectedRun() is ExecutionView run && fallback.InstructionsFor(run.ExecutionId).FirstOrDefault() is PresetInstruction preset)
                    SendInstead("the offered instruction", commands.SendInstruction(run.ExecutionId, preset.Text));
                if (Until(() => DirectedRun()?.ActiveTools.Count > 0 || DirectedRun()?.LastTestRun?.Outcome == TestOutcome.Passed, "the correction at work", 60)
                    && DirectedRun()?.ActiveTools.Count > 0) Moment("correction at work");
                if (Until(() => DirectedRun()?.LastTestRun?.Outcome == TestOutcome.Passed, "the passing checks", 60))
                {
                    Moment("result build passed");
                    director.OpenFile(Directed()!.WorkstreamId, FileSection.Checks);
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

        /// <summary>The name of the nearest Halcyonic component that owns <paramref name="part"/>, or null for the walk's own camera.</summary>
        private static string? OwnerOf(Transform part)
        {
            for (var at = part; at != null; at = at.parent)
            {
                if (at.GetComponent<Camera>() != null) return null;
                foreach (var component in at.GetComponents<MonoBehaviour>())
                {
                    var type = component.GetType();
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
                if (label.isTextTruncated) failures.Add("\"" + label.text + "\" is cut");
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
                if (contrast < 4.5f) failures.Add("\"" + label.text + "\" reaches " + contrast.ToString("0.0") + ":1, under 4.5:1");
            }
            return failures;
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
