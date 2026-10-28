#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// D2a's states of the focused work on the curve, each on synthetic work and in production's own words: an
    /// approval asked and then confirming, checks that failed, the connection to the person's computer lost with every
    /// state last known, a command whose outcome can't be told, and a question that waits for the person's answer.
    /// The approval's words come from a projection the production presenter reads, as the director's would, with an
    /// agent app that declares its approved commands may leave their sandbox (ADR 0033).
    /// </summary>
    public static partial class KitRender
    {
        private const string ApprovalCommand = "npm test -- --grep \"sign-in rate limit\" --reporter spec";

        private const string AskedQuestion = "Should the address limit reset at the start of each minute, or count the last sixty seconds "
            + "from each attempt? The tests assume the second.";

        private static Shot StateShot(string state, Transform root, Vector3 eyes)
        {
            var shot = StateOf(state, root, eyes);
            // The base state already shows the rail at a glance; these states change the slate.
            shot.Glance = null;
            return shot;
        }

        private static Shot StateOf(string state, Transform root, Vector3 eyes) => state switch
        {
            "5-approval" => FocusedWork(root, eyes, true, Approval(confirming: false), "An approval asked, synthetic data", RequireAsked),
            "5-approval-confirming" => FocusedWork(root, eyes, true, Approval(confirming: true), "An approval confirming, its request read, synthetic data", RequireConfirming),
            "6-checks-failed" => FocusedWork(root, eyes, true, ChecksFailed(), "Checks failed, synthetic data", RequireFailed),
            "7-disconnected" => FocusedWork(root, eyes, true, Disconnected(), "The connection lost, everything last known, synthetic data", RequireDisconnected),
            "8-cant-tell" => FocusedWork(root, eyes, true, CantTell(), "An outcome that can't be told, synthetic data", RequireCantTell),
            "9-question" => FocusedWork(root, eyes, true, Question(), "A question waits for an answer, synthetic data", RequireQuestion),
            "10-avatar-working" => FocusedWork(root, eyes, true, WithCharacter(CharacterActivity.Working), "The task's character at its work, synthetic data", RequireFacing(KitAvatarZone.AwayDegrees)),
            "10-avatar-turning" => FocusedWork(root, eyes, true, WithCharacter(CharacterActivity.Working), "An approval arrives: the character turns to you, halfway, synthetic data",
                RequireTurning, placed: (view, _) => TurnHalfway(view)),
            "10-avatar-waiting" => FocusedWork(root, eyes, true, WithCharacter(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired), "The character faces you while the task waits, synthetic data", RequireFacing(0f)),
            "11-avatar-agents-off" => FocusedWork(root, eyes, true, Focused(Badge(CharacterActivity.Working), SelectedActivity().Report,
                avatar: KitAvatar.Absent(AgentsOff, ShowAgents, null)), "No character: agents are off, synthetic data", RequireAbsent(AgentsOff, ShowAgents)),
            "11-avatar-no-room" => FocusedWork(root, eyes, true, Focused(Badge(CharacterActivity.Working), SelectedActivity().Report,
                avatar: KitAvatar.Absent(NoRoom, WorkspaceMoveWords, GlazeIcon.Move)), "No character: no room above the workspace, synthetic data", RequireAbsent(NoRoom, WorkspaceMoveWords)),
            "12-to-compact-mid" => Moving(FocusedWork(root, eyes, true, WorkModel(), "Workspace to Compact, halfway, synthetic data", placed: (view, _) => TowardCompact(view, eyes))),
            "13-bound-running" => Bound(root, eyes, Running(), new FileScreen { Section = FileSection.Activity, Speak = true }, "Bound to the file: running, Stop beside Write instruction, synthetic data",
                (view, shot) => RequireBound(view, shot, KitPlace.Stop, KitPlace.Talk, KitPlace.Field)),
            "13-bound-composing" => Bound(root, eyes, Running(ShortReport()), Composing(), "Bound to the file: the instruction, Cancel, Hold to talk and Send, a short report, synthetic data",
                (view, shot) => RequireBound(view, shot, KitPlace.Close, KitPlace.Talk, KitPlace.Send)),
            "13-bound-nothing-yet" => Bound(root, eyes, Running(new ActivityEntry[0]), new FileScreen { Section = FileSection.Activity, Speak = true },
                "Bound to the file: nothing recorded yet, synthetic data", RequireNothingYet),
            "13-bound-approval-from-activity" => Bound(root, eyes, WithActivity(ApprovalWorkspace(), SelectedEntries()), new FileScreen { Section = FileSection.Activity, Speak = true },
                "Bound to the file: an approval waits on Waiting, seen from Activity, synthetic data", RequireSaid),
            _ => Moving(FocusedWork(root, eyes, true, WorkModel(), "Project to task, halfway: the task grows from its row, synthetic data", placed: (view, _) => FromRow(view, eyes))),
        };

        /// <summary>
        /// The kit bound to the frame a task's file draws for <paramref name="screen"/> (<see cref="KitFileBinding"/>), shown
        /// as the workspace's host shows it: the file's sections as tabs on the slate, no switch until its route exists. Fails where the frame has no binding: the kit would not stand in for the file there.
        /// </summary>
        private static Shot Bound(Transform root, Vector3 eyes, WorkspacePresentation workspace, FileScreen screen, string caption, System.Action<KitWorkView, Shot> require)
        {
            // The file's sections stand as its tabs on the slate, in its words, the one showing raised.
            void Sections(KitWorkView view, Shot shot, KitFileBinding bound)
            {
                var shown = view.Tabs.Select(tab => (tab.Words.text, tab.Role == KitButtonRole.Secondary)).ToList();
                var drawn = bound.Sections.Select(section => (LabelText.ForTextMeshPro(section.Words), section.Chosen)).ToList();
                if (!shown.SequenceEqual(drawn)) shot.Failures.Add("the tabs are not the file's sections: " + string.Join(", ", shown) + " against " + string.Join(", ", drawn));
            }
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }));
            if (screen.Instruction.Open) screen.Instruction.Restore(workspace.Execution!.ExecutionId, Draft, false, 0);
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
            var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live),
                SelectedProject, ProductionSpine(workspace), new KitRowModel("workspace", LineWords.Ours("Workspace"), GlazeIcon.Move));
            if (binding == null)
            {
                var failed = new Shot { Caption = caption };
                failed.Failures.Add("the kit has no binding to this frame");
                return failed;
            }
            var model = binding.Model;
            return FocusedWork(root, eyes, true, model, caption, (view, shot) =>
            {
                foreach (var control in binding.Controls.Where(control => control.Place != KitPlace.Section))
                    if (!Shows(view, control)) shot.Failures.Add("the frame's " + control.Words + " has no control showing");
                Sections(view, shot, binding);
                require(view, shot);
            }, placed: (view, _) =>
            {
                view.Show(model, binding);
                view.Spine.Switch.gameObject.SetActive(false);
            });
        }

        /// <summary>Whether the bound control stands in the view, in the frame's own words.</summary>
        private static bool Shows(KitWorkView view, KitControl control)
        {
            var composer = view.Composer;
            KitButton? button = control.Place switch
            {
                KitPlace.Close => composer.Close,
                KitPlace.Stop or KitPlace.Discard => composer.Rare,
                KitPlace.Talk => composer.Talk,
                KitPlace.Send => composer.Send,
                KitPlace.Deny => view.Decision.Deny,
                KitPlace.Approve => view.Decision.Approve,
                KitPlace.Cancel => view.Decision.Cancel,
                KitPlace.Yes => view.Decision.Yes,
                _ => null,
            };
            if (control.Place == KitPlace.Field) return composer.gameObject.activeInHierarchy && composer.Field.gameObject.activeInHierarchy;
            if (control.Place == KitPlace.Pager) return view.Earlier.gameObject.activeInHierarchy;
            return button != null && button.gameObject.activeInHierarchy && button.Words.text == LabelText.ForTextMeshPro(control.Words);
        }

        private static void RequireBound(KitWorkView view, Shot shot, params KitPlace[] places)
        {
            var shown = new[] { view.Composer.Close, view.Composer.Rare, view.Composer.Talk, view.Composer.Send }.Count(button => button.gameObject.activeInHierarchy);
            Debug.Log("Halcyonic: spatial kit: bound composer shows " + shown + " controls for " + string.Join(", ", places));
            if (view.Spine.Switch.gameObject.activeSelf) shot.Failures.Add("the switch stands before its route exists");
            if (view.Spine.Rows.Any(row => row.gameObject.activeSelf && row.Model?.Key.StartsWith(KitPresenter.TaskRowKey, System.StringComparison.Ordinal) == true))
                shot.Failures.Add("the spine lists tasks, which the context column does not");
            if (!view.Spine.GetComponentsInChildren<TMPro.TextMeshPro>(false).Any(label => label.text == LabelText.ForTextMeshPro(WorkspaceText.Connected)))
                shot.Failures.Add("the connection's state is not under Projects");
        }

        private static void RequireNothingYet(KitWorkView view, Shot shot)
        {
            RequireBound(view, shot);
            if (!view.Trail.GetComponentsInChildren<TMPro.TextMeshPro>(false).Any(label => label.text == LabelText.ForTextMeshPro(FileScreens.NoActivityYet)))
                shot.Failures.Add("the rail does not say " + FileScreens.NoActivityYet);
        }

        private static void RequireSaid(KitWorkView view, Shot shot)
        {
            RequireBound(view, shot);
            if (!view.Said.Any(line => line.text.Contains("Open Waiting"))) shot.Failures.Add("the file's line that the approval waits on Waiting is not shown");
            if (view.Decision.gameObject.activeSelf) shot.Failures.Add("an approval is answered on Waiting, not from Activity");
        }

        /// <summary>The instruction composer open on the person's draft.</summary>
        private static FileScreen Composing()
        {
            var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
            screen.Instruction.Begin("synthetic-execution-rate-limit");
            return screen;
        }

        /// <summary>The selected task running, its every action offered, with <paramref name="activity"/> recorded (the synthetic journal where none is given).</summary>
        private static WorkspacePresentation Running(IReadOnlyList<ActivityEntry>? activity = null) => WithActivity(SelectedWorkspace(waiting: false), activity ?? SelectedEntries());

        /// <summary>
        /// The synthetic journal with the agent's report cut to its first two sentences: beside the whole composer at the
        /// larger size the long report does not read whole, and there the host leaves the frame to the file (Holds); the
        /// long report's whole reading is held by the running and approval states at both sizes.
        /// </summary>
        private static IReadOnlyList<ActivityEntry> ShortReport()
        {
            var (trail, report) = SelectedActivity();
            var shortened = new ActivityEntry(report.Position, report.OccurredAt, report.Kind, LineWords.TheirName(string.Join(" ", Report.Split(new[] { ". " }, System.StringSplitOptions.None).Take(2)) + "."), report.Reported);
            return trail.Select(entry => entry.Entry).Append(shortened).OrderBy(entry => entry.Position).ToList();
        }

        private static IReadOnlyList<ActivityEntry> SelectedEntries()
        {
            var (trail, report) = SelectedActivity();
            return trail.Select(entry => entry.Entry).Append(report).OrderBy(entry => entry.Position).ToList();
        }

        private static WorkspacePresentation WithActivity(WorkspacePresentation workspace, IReadOnlyList<ActivityEntry> activity) =>
            new WorkspacePresentation(workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, workspace.Actions,
                workspace.Actions.Where(workspace.RequiresConfirmation).ToList(), workspace.Commands, activity);

        /// <summary>The spine as the director builds it: today's context column (DESIGN to XR, 2026-10-28), Projects with the connection's state, then Tasks.</summary>
        private static IReadOnlyList<KitSpineSection> ProductionSpine(WorkspacePresentation workspace) => new[]
        {
            new KitSpineSection(new[]
            {
                new KitRowModel("project:synthetic-project-storefront", LineWords.TheirName(SelectedProject), opensMore: false, selected: true, isName: true),
                new KitRowModel("project:synthetic-project-search", LineWords.TheirName("Search service"), opensMore: false, isName: true),
                new KitRowModel("project:synthetic-project-field-notes", LineWords.TheirName("Field notes app"), opensMore: false, isName: true),
            }, "Projects", note: WorkspaceText.Connected),
            new KitSpineSection(new[] { new KitRowModel("place:Tasks", LineWords.Ours("Tasks")) }),
        };

        private static readonly string[] States = { "5-approval", "5-approval-confirming", "6-checks-failed", "7-disconnected", "8-cant-tell", "9-question",
            "10-avatar-working", "10-avatar-turning", "10-avatar-waiting", "11-avatar-agents-off", "11-avatar-no-room", "12-to-compact-mid", "12-to-task-mid",
            "13-bound-running", "13-bound-composing", "13-bound-nothing-yet", "13-bound-approval-from-activity" };

        // The ways back are the ones that exist: Settings' agents switch (SpaceSettings) and the placement column's move.
        private const string AgentsOff = "The workspace works without characters";
        private const string ShowAgents = "Show agents";
        private const string NoRoom = "No room above the workspace for its character";
        private const string WorkspaceMoveWords = "Move workspace";

        private static KitWorkModel WithCharacter(CharacterActivity activity, AttentionLevel attention = AttentionLevel.None)
        {
            var (_, report) = SelectedActivity();
            var presentation = Presentation("synthetic-task-rate-limit", SelectedTask, activity, attention, attention == AttentionLevel.ActionRequired ? 1 : 0);
            return Focused(StateLanguage.BadgeOf(presentation), report, avatar: KitAvatar.Standing(presentation));
        }

        /// <summary>A real change of state, an approval arriving, turns the character; the frame is half a slide in.</summary>
        private static void TurnHalfway(KitWorkView view)
        {
            GlazeMotion.Still = false;
            view.Show(WithCharacter(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired));
            view.Avatar.Advance(Glaze.SlideSeconds / 2f);
            GlazeMotion.Still = true;
        }

        private static System.Action<KitWorkView, Shot> RequireFacing(float turn) => (view, shot) =>
        {
            if (view.Avatar.Character == null || !view.Avatar.Character.gameObject.activeSelf) shot.Failures.Add("the task's character does not stand");
            else if (Mathf.Abs(view.Avatar.Turn - turn) > 0.01f) shot.Failures.Add("the character stands turned " + Degrees(view.Avatar.Turn) + ", not " + Degrees(turn));
        };

        private static void RequireTurning(KitWorkView view, Shot shot)
        {
            var turn = view.Avatar.Turn;
            if (turn <= 0.5f || turn >= KitAvatarZone.AwayDegrees - 0.5f) shot.Failures.Add("the character is not between its work and the person mid-turn (" + Degrees(turn) + ")");
            // Kept still, the same change ends at once.
            GlazeMotion.Still = true;
            view.Show(WithCharacter(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired));
            view.Show(WithCharacter(CharacterActivity.Working));
            if (Mathf.Abs(view.Avatar.Turn - KitAvatarZone.AwayDegrees) > 0.01f) shot.Failures.Add("kept still, the turn did not end at once");
            GlazeMotion.Still = false;
            view.Show(WithCharacter(CharacterActivity.Working));
            view.Show(WithCharacter(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired));
            view.Avatar.Advance(Glaze.SlideSeconds / 2f);
            GlazeMotion.Still = true;
        }

        private static System.Action<KitWorkView, Shot> RequireAbsent(string why, string wayBack) => (view, shot) =>
        {
            if (view.Avatar.Character != null && view.Avatar.Character.gameObject.activeSelf) shot.Failures.Add("a character stands where none should");
            if (view.Avatar.Words.text != LabelText.ForTextMeshPro(why) || view.Avatar.Cut) shot.Failures.Add("the absence does not say why, whole");
            if (!view.Avatar.Action.gameObject.activeSelf || view.Avatar.Action.Words.text != LabelText.ForTextMeshPro(wayBack)) shot.Failures.Add("the absence does not offer " + wayBack);
        };

        private static Shot Moving(Shot shot)
        {
            shot.Moving = true;
            shot.Glance = null;
            // Kept still, a move is over before it starts.
            GlazeMotion.Still = true;
            if (KitTransition.Progress(0f) != 1f) shot.Failures.Add("kept still, a move does not end at once");
            return shot;
        }

        /// <summary>Halfway, eased, from the workspace's place toward the compact presence's, taking its size.</summary>
        private static void TowardCompact(KitWorkView view, Vector3 eyes)
        {
            var from = new Pose(view.transform.position, view.transform.rotation);
            var scale = view.transform.localScale.x;
            var target = new GameObject("Compact's place").transform;
            Place(target, eyes, SpatialKit.PresenceYawDegrees, SpatialKit.PresenceElevationDegrees);
            var share = PlaneComposition.Units(SpatialKit.PresenceDegrees) / view.Size.x;
            GlazeMotion.Still = false;
            KitTransition.Pose(view.transform, from, scale, new Pose(target.position, target.rotation), scale * share, KitTransition.Progress(Glaze.SlideSeconds / 2f));
            GlazeMotion.Still = true;
            Object.DestroyImmediate(target.gameObject);
        }

        /// <summary>Halfway, eased, from the task's row on the board, at the row's size, to the focused work's place.</summary>
        private static void FromRow(KitWorkView view, Vector3 eyes)
        {
            var to = new Pose(view.transform.position, view.transform.rotation);
            var scale = view.transform.localScale.x;
            var row = new GameObject("The row's place").transform;
            Place(row, eyes, 12f, CentreFor(view.Size.y) + 4f);
            var share = PlaneComposition.Units(SpatialKit.BoardDegrees * 0.8f) / view.Size.x;
            GlazeMotion.Still = false;
            KitTransition.Pose(view.transform, new Pose(row.position, row.rotation), scale * share, to, scale, KitTransition.Progress(Glaze.SlideSeconds / 2f));
            GlazeMotion.Still = true;
            Object.DestroyImmediate(row.gameObject);
        }

        /// <summary>The focused work as <see cref="WorkModel"/> shows it, with its state, report, need, question and notice as given.</summary>
        private static KitWorkModel Focused(StateBadge badge, ActivityEntry? report, KitNeed? need = null, KitAsk? ask = null, KitNoticeModel? notice = null,
            bool sendable = true, string? whyNot = null, bool stale = false, string draft = Draft, KitAvatar? avatar = null)
        {
            var (trail, _) = SelectedActivity();
            var spine = new[]
            {
                new KitSpineSection(new[] { new KitRowModel("place:Projects", LineWords.Ours("Projects"), GlazeIcon.Folder, detail: "1 task waiting", detailTone: GlazeTone.Attention) }),
                new KitSpineSection(new[]
                {
                    TaskRow("task:rate-limit", SelectedTask, CharacterActivity.Working, selected: true, opensMore: false, stale: stale),
                    TaskRow("task:webhook", WaitingTask, CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1, opensMore: false, stale: stale),
                    TaskRow("task:pagination", FinishedTask, CharacterActivity.TurnFinished, opensMore: false, stale: stale),
                }, SelectedProject, headingIsName: true),
            };
            return new KitWorkModel(SelectedProject, SelectedTask, badge, new WorkMark[0], report, trail, draft, voiceOffered: true,
                sendable, whyNot, spine, spineFoot: null, need, ask, notice, avatar);
        }

        /// <summary>
        /// The selected task waiting for approval of a shell command, as the control plane would report it, presented by
        /// the production presenter and worded by <see cref="WorkspaceText.NeedFromYou"/>; confirming, as after Approve.
        /// </summary>
        private static KitWorkModel Approval(bool confirming)
        {
            var workspace = ApprovalWorkspace();
            var confirmation = confirming ? KitPresenter.ConfirmationOf(WorkspaceAction.Approve, read: true, parts: 2) : null;
            var kitNeed = KitPresenter.NeedOf(workspace, confirmation)!;
            return Focused(Badge(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1), null, need: kitNeed);
        }

        private static KitWorkModel ChecksFailed()
        {
            var (_, report) = SelectedActivity();
            return Focused(Badge(CharacterActivity.TurnFinished, AttentionLevel.Notice), report);
        }

        /// <summary>
        /// The connection lost while it retries: every state last known, and Send unavailable with ConnectionText's
        /// own line as its reason, "Last known: can't reach your computer. Trying again…", where the person meets it.
        /// </summary>
        private static KitWorkModel Disconnected()
        {
            var (_, report) = SelectedActivity();
            var status = new ConnectionStatus(ConnectionPhase.WaitingToRetry);
            return Focused(Badge(CharacterActivity.Working, stale: true), report, sendable: false, whyNot: ConnectionText.Retrying(status), stale: true);
        }

        /// <summary>An answer sent whose effect the agent app never confirmed: WORDS.md's unknown-effect line, the state can't be told.</summary>
        private static KitWorkModel CantTell()
        {
            var (_, report) = SelectedActivity();
            var notice = new KitNoticeModel(GlazeIcon.CantTellYet, GlazeTone.Unknown, LineWords.Ours(WorkspaceText.NotSure(CommandType.ExecutionRespondToApproval)));
            return Focused(Badge(CharacterActivity.Unknown), report, notice: notice);
        }

        /// <summary>The agent's question, under production's heading for a single prompt with no header of its own.</summary>
        private static KitWorkModel Question()
        {
            var question = new QuestionView
            {
                QuestionId = "synthetic-question",
                Answerable = true,
                AskedAt = Day + "09:03:10.000Z",
                Prompts = new List<QuestionPrompt> { new QuestionPrompt { Key = "reset", Text = AskedQuestion, FreeText = true } },
            };
            var ask = new KitAsk(WorkspaceText.PromptHeadingRuns(question, 0), question.Prompts[0].Text);
            return Focused(Badge(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired), null, ask: ask, draft: "");
        }

        /// <summary>
        /// A projection holding the selected task waiting for approval of <see cref="ApprovalCommand"/> by a synthetic
        /// agent app that declares an approved command may leave its sandbox, presented as the director presents work.
        /// </summary>
        private static WorkspacePresentation ApprovalWorkspace() => SelectedWorkspace(waiting: true);

        /// <summary>
        /// The selected task as the production presenter presents it from a projection: waiting for approval of
        /// <see cref="ApprovalCommand"/>, or running with nothing waiting, the agent app's every action offered.
        /// </summary>
        private static WorkspacePresentation SelectedWorkspace(bool waiting)
        {
            const string at = Day + "09:03:00.000Z";
            var runtime = new RuntimeDescriptor
            {
                RuntimeId = "synthetic-runtime",
                Kind = "synthetic",
                DisplayName = "Synthetic agent app (render)",
                Synthetic = true,
                ApprovalMayLeaveSandbox = true,
                Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, InstructWhileRunning = true, RespondToApproval = true, Interrupt = true, AnswerQuestion = true },
            };
            var workstream = new WorkstreamView
            {
                WorkstreamId = "synthetic-task-rate-limit",
                ProjectId = "synthetic-project-storefront",
                Title = SelectedTask,
                Objective = "Limit sign-in attempts per address and per account.",
                Status = waiting ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running,
                Attention = waiting
                    ? new Attention
                    {
                        Level = AttentionLevel.ActionRequired,
                        Reasons = new List<AttentionReason> { new ApprovalPendingReason { ExecutionId = "synthetic-execution-rate-limit", ApprovalId = "synthetic-approval" } },
                    }
                    : new Attention { Level = AttentionLevel.None, Reasons = new List<AttentionReason>() },
                CurrentExecutionId = "synthetic-execution-rate-limit",
                ExecutionIds = new List<string> { "synthetic-execution-rate-limit" },
                CreatedAt = at,
                UpdatedAt = at,
            };
            var execution = new ExecutionView
            {
                ExecutionId = "synthetic-execution-rate-limit",
                WorkstreamId = workstream.WorkstreamId,
                ProjectId = workstream.ProjectId,
                Runtime = new RuntimeRef { RuntimeId = runtime.RuntimeId, Kind = runtime.Kind, DisplayName = runtime.DisplayName, Synthetic = true },
                Instruction = "Do the work.",
                Status = waiting ? ExecutionStatus.WaitingForHuman : ExecutionStatus.Running,
                TurnCount = 1,
                CreatedAt = at,
                StartedAt = at,
                UpdatedAt = at,
            };
            if (waiting) execution.PendingApprovals.Add(new ApprovalView
            {
                ApprovalId = "synthetic-approval",
                Subject = new ToolUseSubject { ToolName = "shell", Summary = ApprovalCommand },
                Approvable = true,
                RequestedAt = at,
            });
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = "synthetic-journal", Origin = JournalOrigin.Live },
                Position = 1,
                Projects = new List<ProjectView> { new() { ProjectId = workstream.ProjectId, Name = SelectedProject, CreatedAt = at, UpdatedAt = at } },
                Workstreams = new List<WorkstreamView> { workstream },
                Executions = new List<ExecutionView> { execution },
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor> { runtime },
            }, new StateChanges());
            state.ApplyWelcome(new WelcomeMessage
            {
                Journal = new JournalInfo { JournalId = "synthetic-journal", Origin = JournalOrigin.Live },
                Head = 1,
                Resumed = false,
                ServerTime = at,
                CommandPolicies = new List<CommandPolicy>
                {
                    new() { CommandType = CommandType.ExecutionRespondToApproval, Policy = PolicyCategory.ReviewRequired },
                    new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.LowConsequence },
                },
            });
            return WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true);
        }

        private static void RequireAsked(KitWorkView view, Shot shot)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            Require(view.Decision.gameObject.activeSelf && !view.Composer.gameObject.activeSelf, "the decision bar does not stand in the composer's place");
            Require(view.Decision.Deny.gameObject.activeSelf && view.Decision.Approve.gameObject.activeSelf, "the approval does not offer Deny and Approve");
            Require(!view.Decision.Yes.gameObject.activeSelf && !view.Decision.Cancel.gameObject.activeSelf, "the approval offers a confirmation before an answer was pressed");
            RequireRequest(view, Require);
        }

        private static void RequireConfirming(KitWorkView view, Shot shot)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var bar = view.Decision;
            Require(!bar.Deny.gameObject.activeSelf && !bar.Approve.gameObject.activeSelf, "the answers still stand while confirming");
            Require(bar.Cancel.gameObject.activeSelf && bar.Yes.gameObject.activeSelf, "the confirmation lacks Cancel or Yes");
            Require(bar.Yes.Words.text == WorkspaceText.ConfirmLabel(WorkspaceAction.Approve), "Yes does not say " + WorkspaceText.ConfirmLabel(WorkspaceAction.Approve));
            // Yes stands where no control stood a moment before: left of both answers' places, Cancel in Approve's.
            var approveWidth = bar.Approve.Measure(WorkspaceText.Label(WorkspaceAction.Approve), GlazeIcon.Approve);
            var denyWidth = bar.Deny.Measure(WorkspaceText.Label(WorkspaceAction.Deny), GlazeIcon.Deny);
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var denyLeft = bar.Size.x - approveWidth - gap - denyWidth;
            Require(Mathf.Abs(bar.Cancel.transform.localPosition.x - (bar.Size.x - approveWidth / 2f)) < 1e-4f, "Cancel does not stand where Approve was pressed");
            Require(bar.Yes.transform.localPosition.x + bar.Yes.Size.x / 2f <= denyLeft - gap + 1e-4f, "Yes stands where a control stood a moment before");
            RequireRequest(view, Require);
        }

        /// <summary>The request reads whole and literal, leaning as outside text; where it runs, Halcyonic's words, upright and first.</summary>
        private static void RequireRequest(KitWorkView view, System.Action<bool, string> require)
        {
            require(view.Request.gameObject.activeSelf && view.Request.Theirs.Count > 0 && view.Request.Words.text == LabelText.ForTextMeshPro(ApprovalCommand),
                "the request is not shown whole and literal as outside text");
            var scope = view.Notes.FirstOrDefault(note => note.gameObject.activeSelf);
            require(scope != null && scope.text == WorkspaceText.ApprovedCommandNote(new RuntimeDescriptor { ApprovalMayLeaveSandbox = true }),
                "where an approved command runs is not said first, in ADR 0033's words");
            require(scope != null && GlazeRuns.Of(scope).Theirs.Count == 0, "where an approved command runs leans as if outside words");
        }

        private static void RequireFailed(KitWorkView view, Shot shot)
        {
            if (view.State.Shown?.State != WorkState.ChecksFailed) shot.Failures.Add("the failed work does not say Checks failed");
            if (!view.Trail.Shown.Any(entry => entry.Tone == GlazeTone.Failure)) shot.Failures.Add("the failure's evidence is not on the rail");
        }

        private static void RequireDisconnected(KitWorkView view, Shot shot)
        {
            var status = new ConnectionStatus(ConnectionPhase.WaitingToRetry);
            if (!view.Composer.Reason.gameObject.activeSelf || view.Composer.Reason.text != LabelText.ForTextMeshPro(ConnectionText.Retrying(status)))
                shot.Failures.Add("the lost connection is not said in ConnectionText's words where Send waits");
            if (view.State.Shown?.LastKnown != true) shot.Failures.Add("the task's state is not labelled last known");
            if (view.Composer.Send.Available) shot.Failures.Add("Send stays available with no connection");
        }

        private static void RequireCantTell(KitWorkView view, Shot shot)
        {
            if (!view.Notice.gameObject.activeSelf || view.Notice.Words.text != LabelText.ForTextMeshPro(WorkspaceText.NotSure(CommandType.ExecutionRespondToApproval)))
                shot.Failures.Add("the unknown outcome is not said in WORDS.md's unknown-effect line");
            if (view.State.Shown?.State != WorkState.CantTellYet) shot.Failures.Add("the state does not say Can't tell yet");
        }

        private static void RequireQuestion(KitWorkView view, Shot shot)
        {
            if (!view.Composer.Answering || view.Composer.Send.Words.text != WorkspaceText.Label(WorkspaceAction.Answer))
                shot.Failures.Add("the composer does not ask for the answer");
            if (!view.AskedQuestion.gameObject.activeSelf || GlazeRuns.Of(view.AskedQuestion).Theirs.Count == 0)
                shot.Failures.Add("the agent's question does not lean as its own words");
        }
    }
}
