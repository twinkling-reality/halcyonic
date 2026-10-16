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
        public static void ActivityComposerCheck() => CheckActivityComposer(forceMinimumPair: false);

        /// <summary>Editor-only controlled experiment; never a production selection-policy proof.</summary>
        public static void ActivityComposerPairExperiment() => CheckActivityComposer(forceMinimumPair: true);

        private static void CheckActivityComposer(bool forceMinimumPair)
        {
            var previous = ViewField.Current;
            var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                    ViewField.Current = FieldChecks.Quest3S;
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", forceMinimumPair ? "ActivityComposerPairExperiment" : "ActivityComposerRenders", variant)); Directory.CreateDirectory(folder);
                    var result = RenderActivityComposer(folder, forceMinimumPair: forceMinimumPair);
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                    var voiceFolder = Path.Combine(folder, "voice-offered"); Directory.CreateDirectory(voiceFolder);
                    result.AddRange(RenderActivityComposer(voiceFolder, offerVoice: true, forceMinimumPair: forceMinimumPair));
                    if (!forceMinimumPair)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                        var narrowFolder = Path.Combine(folder, "insufficient-field"); Directory.CreateDirectory(narrowFolder);
                        result.AddRange(RenderActivityComposer(narrowFolder, field: InsufficientPairField, selectionOnly: true));
                    }
                    return result;
                }));
            }
            catch (Exception error) { failures.Add("Activity/composer fixture threw " + error); }
            finally { ViewField.Current = previous; KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: Activity/composer: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: Activity/composer: every check passed.");
            UnityEditor.EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>
        /// A field 25 degrees above and below: the minimum Activity and composer pair's envelope spans
        /// about 50 degrees, the focused editor's about 35, so only the pair is genuinely refused.
        /// </summary>
        private static readonly ViewField InsufficientPairField = new ViewField(48f, 48f, 25f, 25f);

        private static List<string> RenderActivityComposer(string folder, bool offerVoice = false, bool forceMinimumPair = false,
            ViewField? field = null, bool selectionOnly = false)
        {
            if (field is ViewField measured) ViewField.Current = measured;
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
                    if (views.Any(view => view.TaskIdentity.gameObject.activeInHierarchy))
                        Require(director.Plane.WorkspaceCorners.Count >= 12, name + ": recovery footprint omitted external portrait motion volume");
                    if (ViewField.Current is ViewField field && director.Plane.Composition != null)
                    {
                        var faces = views.Select(view => view.ReadingSurface).Concat(new[] { director.Plane.Context.ReadingSurface });
                        var corners = faces.SelectMany(face => new[] { -.5f, .5f }.SelectMany(x => new[] { -.5f, .5f }.Select(y =>
                            face.transform.TransformPoint(new Vector3(x, y, 0f))))).Concat(director.Plane.WorkspaceCorners);
                        failures.AddRange(GlazeChecks.InsideField(name + " actual full workspace union", corners, eyes, director.Plane.WorkspaceCentre,
                            WorkspacePlacement.ReadingPitch(director.Plane.Composition.Size, director.Plane.Direction.Elevation, field), field));
                    }
                    foreach (var view in views)
                    foreach (var rendered in view.ShownLines)
                    {
                        var prose = MenuFrameView.UsesUprightProse(view.IndependentSurface, rendered.Model);
                        var runs = GlazeRuns.Of(rendered.Words);
                        Require(runs.IsAttributedProse == prose, name + ": pooled line retained the wrong prose treatment");
                        if (!prose) continue;
                        Require(!runs.IsNamedIdentity && !rendered.Words.richText && rendered.Words.parseCtrlCharacters,
                            name + ": attributed prose changed identity or literal rendering policy");
                        Require(!rendered.Grounds.Any(), name + ": clearly attributed prose retained decorative grounds");
                        if (rendered.Model.Runs.AnyTheirs || rendered.Model.Claim)
                            Require(runs.Theirs.Count > 0 && runs.Theirs.All(range => range.Start >= 0 && range.End <= rendered.Words.text.Length),
                                name + ": upright prose lost bounded outside-text ranges");
                        if (rendered.Model.Chip != null)
                            Require(rendered.Chip != null && rendered.Chip.gameObject.activeInHierarchy,
                                name + ": upright agent prose lost its reported-source chip");
                    }
                    foreach (var mark in director.Plane.SelectedMarks)
                        Require(views.Count(view => view.ProvenanceLabel.gameObject.activeInHierarchy && view.ProvenanceLabel.text.Contains(mark.Word)) == 1,
                            name + ": actual selected provenance is missing or duplicated: " + mark.Word);
                    foreach (var view in views.Where(view => view.HeadingOverride == null && view.Frame?.IdentityKind != null))
                    {
                        var title = view.SubjectShown.Title;
                        Require(GlazeRuns.Of(title).IsNamedIdentity, name + ": named task title acquired outside-prose mesh shear");
                        Debug.Log($"Halcyonic: {name} actual identity={view.Frame!.IdentityKind}; font={title.font.name}; style={title.fontStyle}; named={GlazeRuns.Of(title).IsNamedIdentity}.");
                        if (view.IdentityContextLabel.gameObject.activeInHierarchy)
                        {
                            Require(view.IdentityContextLabel.text == view.IdentityContext,
                                name + ": editor mode label was merged into provenance text");
                            if (view.ProvenanceLabel.gameObject.activeInHierarchy)
                                Require(view.ProvenanceLabel.transform.localPosition.y + view.ProvenanceLabel.textBounds.min.y
                                    > view.IdentityContextLabel.transform.localPosition.y + view.IdentityContextLabel.textBounds.max.y,
                                    name + ": actual provenance and editor mode label overlap");
                        }
                        if (view.ProvenanceLabel.gameObject.activeInHierarchy)
                        {
                            // Both labels share the subject part; compare actual mesh envelopes in that
                            // plane rather than world-axis AABBs, which grow when the workspace turns.
                            var titleFoot = title.transform.localPosition.y + title.textBounds.min.y;
                            var marksTop = view.ProvenanceLabel.transform.localPosition.y + view.ProvenanceLabel.textBounds.max.y;
                            Require(titleFoot > marksTop, name + ": actual title and provenance overlap");
                        }
                    }
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
                    failures.AddRange(TextInBands(director.Plane, name, witness: true));
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
                // Same pooled label, same literal runs: only mesh shear changes, never trust.
                var proseProbe = GlazeText.Create(root.transform, "Synthetic prose reuse probe", GlazeType.Body,
                    GlazeTokens.Text, TMPro.TextAlignmentOptions.TopLeft, 25);
                var probeRuns = GlazeRuns.Of(proseProbe);
                var probeWords = LineWords.Theirs("<b>literal source</b> \\n text");
                var (expectedProbeText, expectedProbeRanges) = probeWords.ForTextMeshPro();
                probeRuns.Set(probeWords); probeRuns.SetProseMode(true);
                GlazeText.Lay(proseProbe, 2f, 2); proseProbe.ForceMeshUpdate();
                var uprightVertices = proseProbe.textInfo.meshInfo[0].vertices.ToArray();
                Require(proseProbe.text == expectedProbeText && probeRuns.Theirs.SequenceEqual(expectedProbeRanges),
                    "explicit prose treatment changed literal text or outside ranges");
                Require(probeRuns.IsAttributedProse && !probeRuns.IsNamedIdentity, "prose treatment misused named identity");
                probeRuns.Set(probeWords); proseProbe.ForceMeshUpdate();
                Require(!probeRuns.IsAttributedProse && !probeRuns.IsNamedIdentity,
                    "ordinary Set did not reset pooled prose treatment");
                Require(proseProbe.textInfo.meshInfo[0].vertices.Where((vertex, index) => index < uprightVertices.Length
                    && Vector3.Distance(vertex, uprightVertices[index]) > 0.000001f).Any(),
                    "pooled prose reset failed to restore actual legacy mesh shear");
                probeRuns.SetProseMode(true); probeRuns.SetIdentity("Synthetic identity");
                Require(!probeRuns.IsAttributedProse && probeRuns.IsNamedIdentity, "identity reuse retained prose treatment");
                Require(!MenuFrameView.UsesUprightProse(true, new PageLine("synthetic tool output", wordsAreData: true))
                    && !MenuFrameView.UsesUprightProse(true, new PageLine("synthetic decision", claim: true))
                    && !MenuFrameView.UsesUprightProse(false, new PageLine("synthetic report", chip: "Agent says")),
                    "prose eligibility leaked into data, decisions or legacy consumers");
                UnityEngine.Object.DestroyImmediate(proseProbe.gameObject);
                // A mixed line, Halcyonic's words leading into the agent's, keeps lean, ground and grounded
                // colour beside its chip on a real pooled label: the chip is never the only mark (ADR 0026).
                // The same report whole stays upright prose with its quote mark.
                var mixed = new PageLine(LineWords.Ours("Failing: ") + LineWords.Theirs("synthetic remaining item"), chip: "Agent says");
                var whole = new PageLine(LineWords.Theirs("synthetic whole report"), chip: "Agent says");
                Require(!MenuFrameView.UsesUprightProse(true, mixed) && MenuFrameView.UsesUprightProse(true, whole),
                    "upright prose eligibility no longer separates mixed lines from agent prose attributed whole");
                var attributionProbe = MenuFrameView.Create(root.transform, "Mixed attribution probe");
                try
                {
                    attributionProbe.SpatialWorkspace = true; attributionProbe.IndependentSurface = true; attributionProbe.PassiveActivity = true;
                    attributionProbe.Show(new MenuFrame(LineWords.Ours("Attribution probe"), new Footer(), lines: new[] { mixed, whole }),
                        Glaze.Menu.FileColumnDegrees, 0f, false);
                    bool Leans(TMPro.TMP_Text label)
                    {
                        label.ForceMeshUpdate();
                        var info = label.textInfo;
                        for (var index = 0; index < info.characterCount; index++)
                        {
                            var glyph = info.characterInfo[index];
                            if (!glyph.isVisible || !GlazeText.InRun(info, index, GlazeRuns.Of(label).Theirs)) continue;
                            var vertices = info.meshInfo[glyph.materialReferenceIndex].vertices;
                            if (vertices[glyph.vertexIndex + 1].x - vertices[glyph.vertexIndex].x > 1e-5f) return true;
                        }
                        return false;
                    }
                    foreach (var shown in attributionProbe.ShownLines)
                    {
                        var runs = GlazeRuns.Of(shown.Words);
                        if (ReferenceEquals(shown.Model, mixed))
                            Require(!runs.IsAttributedProse && runs.Theirs.Count > 0 && Leans(shown.Words) && runs.Grounded != null && shown.Grounds.Any(),
                                "a mixed Agent says line lost its lean, ground or grounded colour beside its chip");
                        else if (ReferenceEquals(shown.Model, whole))
                            Require(runs.IsAttributedProse && !Leans(shown.Words) && shown.Icon != null && shown.Icon.gameObject.activeInHierarchy,
                                "agent prose attributed whole lost its upright treatment or its quote mark");
                    }
                    Require(attributionProbe.ShownLines.Count() == 2, "attribution probe did not lay both lines");
                }
                finally { UnityEngine.Object.DestroyImmediate(attributionProbe.gameObject); }
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
                const string draft = "Keep the account and address limits separate. Add a regression test for two accounts sharing one address. Report the observed test result and explain any remaining uncertainty before changing unrelated behavior. "
                    + "Check the first request, the limit boundary, and the first accepted request after the window resets. Keep the account identity separate from the network address in every case. "
                    + "Include one account moving between addresses and two accounts using the same address. Explain the expected behavior of each case before changing the implementation. "
                    + "Compare the new tests with the existing sign-in tests and retain their assertions. Run the focused tests, record their actual exit outcome, and describe any test that could not run. "
                    + "Keep this change limited to the rate-limit boundary. If the evidence points to a separate session or storage problem, report that evidence and leave the unrelated behavior for a separate task.";
                typed?.Invoke(draft); typed = null; Draw();
                if (forceMinimumPair && file != null)
                {
                    // This isolated Editor experiment bypasses only the conservative selection
                    // heuristic. Actual production views, fonts, camera and every Shot gate remain.
                    var terms = new HeightBudget(0f);
                    var paired = director.FitsHalf(FileScreens.InstructionPager(previous: true), Glaze.Menu.FileColumnDegrees)
                        && director.FitsHalf(FileScreens.InstructionPager(), Glaze.Menu.FileColumnDegrees);
                    var minimumActivity = MenuPage.SourceLine + terms.Words(2) + terms.LineGap;
                    var minimumComposer = MenuPage.SourceLine + terms.Words(1) + terms.LineGap + terms.Reason
                        + terms.Target(3) + (paired ? terms.Target() : terms.Targets(1, 1)) + terms.TargetGap;
                    file.ConfigureActivityComposer(() => new HeightBudget(minimumActivity), () => new HeightBudget(minimumComposer));
                    var modes = (Dictionary<FileColumn, bool>)typeof(MenuDirector).GetField("composerFocus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(director)!;
                    modes[file] = false;
                    Draw();
                    Require(!director.Plane.ComposerFocused && director.Plane.ActivityContext != null, "controlled minimum-pair experiment failed to select its experimental pair");
                    Debug.Log($"Halcyonic: EXPERIMENT ONLY forced exact minimum pair activity={minimumActivity:R}; composer={minimumComposer:R}; actual measured gates unchanged; no production selection-policy claim.");
                }
                Shot("02-activity-and-composer");
                if (offerVoice)
                {
                    var footer = director.Plane.Shown.Single(shown => !shown.View.PassiveActivity).View.Footer;
                    Require(footer.Shown.Count() == 4, "voice-offered ordinary composer did not expose its actual four actions");
                    Require(footer.Showing?[PromptSlot.Secondary]?.Id == FileScreens.HoldToTalk, "voice-offered composer omitted the actual Hold to talk action");
                    Debug.Log($"Halcyonic: voice-offered composer footer measured {footer.Measure.Needed:R} needed / {footer.Measure.Room:R} available; fits={footer.Fits}.");
                }
                Debug.Log($"Halcyonic: actual composer partRows={file?.Screen.Instruction.PartRows}; parts={file?.Screen.Instruction.Parts}; allocated height={director.Plane.ComposerAllocation?.Height:R}; voice offered={offerVoice}; focused editor={director.Plane.ComposerFocused}.");
                Require(file != null && file.Screen.Instruction.PartRows >= 3, "actual composer allocates fewer than three readable draft rows; inspect measured field limits before changing the split");
                var context = director.Plane.ActivityContext;
                var focusedComposer = director.Plane.ComposerFocused;
                if (focusedComposer)
                {
                    Require(context == null && director.Plane.Shown.Single().View.IdentityContext == "Editing instruction",
                        "editor focus silently removed Activity without its contextual mode label");
                    Require(ReferenceEquals(director.Plane.Shown.Single().View.Frame, file?.Frame), "editor focus changed the authoritative file owner");
                }
                else Require(context?.Frame != null && file?.ActivityContextFrame == context.Frame, "actual Activity was not rendered beside its own composer");
                if (!forceMinimumPair)
                {
                    // The shown mode is the measured decision: the simultaneous pair exactly where its
                    // text bands read, it stands inside the field and it clears the characters.
                    var decision = director.PairDecisionForRender;
                    Require(decision != null, "the measured Activity and composer pair decision was never made");
                    if (decision is { } made)
                    {
                        Require((made.Inside && made.Readable && made.Clear) == !focusedComposer, "shown Activity/composer mode differs from the measured text-band decision");
                        Debug.Log($"Halcyonic: measured pair decision inside={made.Inside}; readable={made.Readable}; clear={made.Clear}; leastEm={made.LeastEm:R}; pair shown={!focusedComposer}; text scale={GlazeText.Scale:R}.");
                    }
                    if (selectionOnly)
                    {
                        Require(focusedComposer && decision is { } refused && !refused.Inside, "a field too short for the minimum pair did not fall back to the labelled focused editor");
                        Require(director.Plane.Showing(MenuColumn.File)?.Footer.All.Any(slot => slot.Prompt.Id == FileScreens.HideInstruction && slot.Prompt.Available) == true,
                            "the focused fallback offered no Hide composer return to Activity");
                        Require(file?.Screen.Instruction.Words == draft, "the focused fallback lost the exact ordinary draft");
                        return failures;
                    }
                }
                Require(context == null || !context.Targets.Any(), "passive Activity acquired action targets");
                Require(focusedComposer || context?.Frame != null && context.Frame.Lines.Count >= 2,
                    "reclaimed passive allocation did not make room for the actual recent report and observed failure");
                if (context?.Frame != null)
                {
                    Require(!context.Footer.gameObject.activeInHierarchy, "passive allocation still drew a footer");
                    Require(context.Frame.Lines.Any(line => line.Words.Contains("I found that sign-in attempts"))
                        && context.Frame.Lines.Any(line => line.Words.Contains("Command failed: node --test")),
                        "actual reclaimed Activity preview omitted either of its two latest synthetic journal entries");
                    Debug.Log($"Halcyonic: reclaimed passive Activity shows {context.Frame.Lines.Count} actual entries; allocated height={director.Plane.ReaderAllocation?.Height:R}; footer visible={context.Footer.gameObject.activeInHierarchy}.");
                }
                if (forceMinimumPair)
                {
                    Require(!focusedComposer, "controlled pair experiment silently fell back to focused mode");
                    return failures;
                }
                var forwardedPassive = 0;
                director.Plane.Drawn += (_, view) => { if (ReferenceEquals(view, context)) forwardedPassive++; };
                var passiveDraws = 0;
                if (context != null) context.Drawn += _ => passiveDraws++;
                Draw();
                Require((focusedComposer ? passiveDraws == 0 : passiveDraws > 0) && authoritativeDraws > 0, "actual independent draw callbacks were not exercised");
                Require(file != null && file.Screen.Instruction.Parts > 1, "long draft did not exercise actual whole-text paging");
                if (file?.ActivityContextFrame is MenuFrame passiveFrame)
                {
                    var wasRead = file.Screen.Instruction.Read;
                    file.Drawn(passiveFrame, null);
                    Require(file.Screen.Instruction.Read == wasRead, "passive Activity draw granted composer reading credit");
                    Require(passiveFrame.Lines.All(line => line.Action == null), "passive Activity carries an actionable line");
                }
                var opacityFace = context?.ReadingSurface ?? director.Plane.Shown.Single().View.ReadingSurface;
                if (opacityFace != null)
                {
                    opacityFace.Fade(.15f);
                    try
                    {
                        var negative = Render(camera, target);
                        try { Require(TransparentMetrics("Workspace opacity negative", camera, opacityFace, negative).Any(failure => failure.Contains("interior alpha")), "translucent Activity negative was accepted"); }
                        finally { UnityEngine.Object.DestroyImmediate(negative); }
                    }
                    finally { opacityFace.Fade(1f); }
                }
                Require(forwardedPassive == 0, "passive Activity draw was forwarded as authoritative File draw");
                if (focusedComposer) Require(director.Plane.ComposerAllocation != null, "focused editor lost its actual allocation");
                else if (director.Plane.ReaderAllocation is PlanePart reader && director.Plane.ComposerAllocation is PlanePart composer)
                    Require(reader.Bottom > composer.Top, "reader and composer allocations overlap");
                else failures.Add("independent reader/composer allocations missing");
                var stale = director.Plane.Showing(MenuColumn.File);
                director.MinimizeWorkspace(); Draw();
                Require(director.Plane.ActivityContext == null && !director.Plane.Context.gameObject.activeSelf, "Minimize retained hidden spatial parts");
                Require(!director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, stale, null) && sends == 0, "hidden composer accepted stale Send");
                director.Expand(); Draw(); Require(file?.Screen.Instruction.Words == draft, "compact restore lost exact ordinary draft");
                Press(FileScreens.HideInstruction); Shot("03-composer-hidden");
                Require(file?.Screen.Instruction.Words == draft && file.Screen.Instruction.Open == false && director.Plane.ActivityContext == null && !director.Plane.ComposerFocused, "Hide composer lost draft or left a second passive view");
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
