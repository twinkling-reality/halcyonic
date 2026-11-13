#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// A start of New Project stopped on a step, as the redesign draws it (AUDIT's 2026-11-13T1327Z, SCREENS's sentences): the
    /// computer refused the run settings; the agent app stopped answering (the longest of the step sentences); and a step that
    /// may have happened. Built by the flow's own sequence (BuildSequence on a draft, the computer's answers as CommandViews),
    /// framed by NewProjectScreens.Starting and drawn by the room's bar. Measured: every word whole, no word given or drawn on a
    /// dot, and the step's own sentence among what is drawn. Synthetic names only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] StartStoppedStates = { "49-start-refused", "49-start-failed", "49-start-unsure" };

        private static PartShot StartStoppedShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            RoomBar? bar;
            BuildStep? stopped;
            try
            {
                RoomDesign.Enabled = true;
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var idea = new ProjectIdea();
                idea.UseIdea("Make a kite that follows my hand, and climbs when I raise it");
                idea.Rename("Kite Run");
                var draft = new NewWorkDraft(commands) { ProjectId = "kite-run", Objective = "Make a kite that follows my hand, and climbs when I raise it" };
                draft.ChooseRuntime(new RuntimeDescriptor
                {
                    RuntimeId = "local", DisplayName = "Ember App", Kind = "render", Synthetic = false, ModelChoice = ModelChoice.None, UsesProjectLocation = false,
                    Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, AnswerQuestion = true, Interrupt = true },
                });
                var sequence = new BuildSequence(draft, commands, null);
                var review = new NewWorkReview("Kite Run", "Title", "Ember App", "Model", "on your computer", "none", draft.Objective);
                review.Paginate(review.Items.Select(_ => 1).ToList(), review.Items.Count);
                var now = 0.0;
                review.Drawn(now);
                while (review.Next(now += 1)) review.Drawn(now);
                var task = sequence.Begin(review);
                var time = DateTimeOffset.Parse("2026-11-13T12:00:00Z").ToString("o");
                ClientProjection With(CommandView command)
                {
                    var projection = new ClientProjection();
                    projection.ApplySnapshot(new Snapshot
                    {
                        Journal = new JournalInfo { JournalId = "render", Origin = JournalOrigin.Live }, Position = 1,
                        Projects = new List<ProjectView>(), Workstreams = new List<WorkstreamView>(), Executions = new List<ExecutionView>(),
                        Commands = new List<CommandView> { command }, Runtimes = new List<RuntimeDescriptor>(),
                    }, new StateChanges());
                    return projection;
                }
                if (state == "49-start-unsure")
                {
                    // The task's own step may have happened: its outcome is not known.
                    sequence.Advance(With(new CommandView
                    {
                        CommandId = task.CommandId, Status = CommandStatus.Failed, IssuedAt = time, UpdatedAt = time,
                        Failure = new CommandFailure { Code = "timeout", Message = "No answer.", Effect = FailureEffect.Unknown },
                    }));
                }
                else
                {
                    var start = sequence.Advance(With(new CommandView
                    {
                        CommandId = task.CommandId, Status = CommandStatus.Completed, IssuedAt = time, UpdatedAt = time,
                        Result = new WorkstreamCreatedResult { WorkstreamId = "w1" },
                    }))!;
                    sequence.Advance(With(state == "49-start-refused"
                        ? new CommandView
                        {
                            CommandId = start.CommandId, Status = CommandStatus.Rejected, IssuedAt = time, UpdatedAt = time,
                            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "The runtime needs a working directory." },
                        }
                        : new CommandView
                        {
                            CommandId = start.CommandId, Status = CommandStatus.Failed, IssuedAt = time, UpdatedAt = time,
                            Failure = new CommandFailure { Code = "runtime_unreachable", Message = "The agent app did not answer.", Effect = FailureEffect.None },
                        }));
                }
                stopped = sequence.StoppedAt;
                bar = RoomBar.NewProject(NewProjectScreens.Starting(idea, sequence, null), VoiceStage.Idle);
            }
            finally { RoomDesign.Enabled = was; }
            Require(stopped != null, "the start did not stop on a step");
            Require(bar != null, "New Project's stopped start gives the redesign no bar");
            if (bar == null || stopped == null) return shot;
            var sentence = EntryText.StepStatus(stopped);
            Require(!sentence.Contains(" · ") && sentence.TrimEnd().EndsWith(".", StringComparison.Ordinal), "the stopped step's state is not a sentence: \"" + sentence + "\"");

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            view.Show(bar);
            view.SettleForRender();
            foreach (var button in view.Column.Where(button => button.gameObject.activeSelf))
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);
            var drawn = new List<string>();
            foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                label.ForceMeshUpdate(true);
                var said = label.GetParsedText();
                if (said.Trim().Length == 0) continue;
                drawn.Add(said);
                Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut");
                Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot");
            }
            string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Require(drawn.Any(said => Words(said).Contains(Words(sentence))), "the step's sentence \"" + sentence + "\" is not drawn whole: " + string.Join(" | ", drawn.Select(Words)));
            Debug.Log("Halcyonic: parts: " + name + ": stopped on \"" + sentence + "\"; drawn: " + string.Join(" | ", drawn.Select(Words)) + ".");
            return shot;
        }
    }
}
