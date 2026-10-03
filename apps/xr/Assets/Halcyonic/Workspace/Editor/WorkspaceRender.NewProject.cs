#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        private const string RenderIdea = "A page of race times for my running club";

        /// <summary>
        /// Every New project footer (ADR 0026: never four prompts), laid in the file's column at this
        /// pass's text size: each step's screens in every state that changes its footer, and each again
        /// with the footer's Next page, "Next page" and "First page", wherever the flow pages there
        /// (<see cref="NewProjectFlow.PagesInFooter"/>): not a question's answers, which turn by More
        /// answers rows, nor the review or the unknown start, which read in parts. A footer of four
        /// prompts fails, and so does one whose words don't fit its column.
        /// </summary>
        private static IEnumerable<string> RenderNewProjectFooters(string pass)
        {
            var failures = new List<string>();
            var root = new GameObject("New project footers " + pass);
            try
            {
                var laid = 0;
                var tightest = (What: "", Needed: 0f, Room: 1f);
                foreach (var (name, frame) in NewProjectFrames())
                {
                    var footers = new List<(string What, Footer Footer)> { (name, frame.Footer) };
                    if (FooterPages(frame) && NewProjectFlow.PagesInFooter(frame.Footer))
                    {
                        foreach (var page in new[] { 0, 1 })
                        {
                            var words = Footer.NextPageWords(page, 2);
                            footers.Add((name + ", with " + words, frame.Footer.WithNext(new Prompt(Footer.NextPage, words, GlazeIcon.Next, PromptKind.NextPage))));
                        }
                    }
                    foreach (var (what, footer) in footers)
                    {
                        var prompts = string.Join(", ", footer.All.Select(each => each.Prompt.Words));
                        if (footer.All.Count() > 3)
                        {
                            failures.Add("New project's " + what + " has four prompts (" + prompts + "); never four.");
                            continue;
                        }
                        var shown = new MenuFrame(frame.Subject, footer, frame.SubjectIsData, frame.Pill, frame.Sections, frame.Lines, frame.Source, frame.Side,
                            frame.SourceIsData, frame.SubjectWaits);
                        var view = MenuFrameView.Create(root.transform, what);
                        view.Show(shown, Glaze.Menu.FileColumnDegrees, MenuFrameView.SubjectHeight(shown.Subject, Glaze.Menu.FileColumnDegrees, false), pillRoom: false);
                        var (needed, room) = view.Footer.Measure;
                        laid++;
                        if (needed / room > tightest.Needed / tightest.Room) tightest = (what + " (" + prompts + ")", needed, room);
                        if (!view.Footer.Fits)
                        {
                            failures.Add("New project's " + what + ": its footer (" + prompts + ") needs " + Measured(needed) + " of " + Measured(room) + "; it does not fit.");
                        }
                    }
                }
                Debug.Log("Halcyonic: workspace render: " + laid + " New project footers at " + pass + " text; the tightest, " + tightest.What + ", needs "
                    + Measured(tightest.Needed) + " of " + Measured(tightest.Room) + ".");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return failures;
        }

        private static string Measured(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

        /// <summary>
        /// Whether the flow may turn <paramref name="frame"/> by the footer's Next page: a question's page
        /// with answers on offer turns them by More answers rows, and the review and the unknown start
        /// read in parts by Next part rows.
        /// </summary>
        private static bool FooterPages(MenuFrame frame)
        {
            var step = frame.Sections.FirstOrDefault(section => section.Chosen)?.Key;
            if (step == NewProjectScreens.Key(NewProjectStep.Questions)) return !frame.Lines.Any(line => line.Choice);
            return !frame.Lines.Any(line => line.Words == EntryText.ReviewLine || line.Words == EntryText.PreviousRequestTitle);
        }

        /// <summary>New project's screens, built by the client core as the flow builds them, in each state that changes the footer.</summary>
        private static IEnumerable<(string Name, MenuFrame Frame)> NewProjectFrames()
        {
            var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
            var companion = new AvailableCompanion { Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4 };

            // Your idea, blank, then typed with each row chosen; and adding a task to a project.
            yield return ("Your idea", NewProjectScreens.YourIdea(new ProjectIdea(), IdeaRow.None, false, voice: true, said: null, companion: companion));
            var typed = new ProjectIdea();
            typed.UseIdea(RenderIdea);
            foreach (var row in new[] { IdeaRow.None, IdeaRow.Typed, IdeaRow.Companion, IdeaRow.FixedQuestions })
            {
                yield return ("Your idea, " + row + " chosen", NewProjectScreens.YourIdea(typed, row, false, voice: true, said: null, companion: companion));
            }
            yield return ("Your idea for a task", NewProjectScreens.YourIdea(new ProjectIdea("proj_render", "Race Times"), IdeaRow.Companion, false, voice: true,
                said: null, companion: companion));

            // Questions: waiting for the companion, its question with nothing, an answer and Go on without it chosen, and its suggestion.
            var asked = new ProjectIdea();
            asked.UseIdea(RenderIdea);
            var exchange = asked.BeginCompanion(CompanionStart.Idea);
            exchange.Ask(CompanionWant.Next);
            yield return ("Questions, waiting", Questions(asked));
            exchange.Replied(exchange.Generation, Reply(new AskReply
            {
                Line = "A tracker for race times fits a small web page.",
                View = CompanionView.Unclear,
                Question = new CompanionQuestion { Text = "Who enters the times?", Choices = new List<string> { "Each runner", "One organiser" } },
            }));
            yield return ("Questions", Questions(asked));
            exchange.Choose(0);
            yield return ("Questions, an answer chosen", Questions(asked));
            exchange.ChooseWithoutIt();
            yield return ("Questions, Go on without it chosen", Questions(asked));
            yield return ("Questions, Go on without it chosen, no Hold to talk", Questions(asked, voice: false));
            var proposed = Proposed();
            yield return ("Questions, its suggestion", Questions(proposed));

            // The fixed questions, each with nothing and with an answer chosen.
            var guided = new ProjectIdea();
            guided.UseIdea(RenderIdea);
            guided.BeginGuide();
            for (var question = 0; question < ProjectIdea.Questions.Count && guided.Question < ProjectIdea.Questions.Count; question++)
            {
                yield return ("fixed question " + (question + 1), NewProjectScreens.FixedQuestion(guided, false, voice: true, said: null));
                var asking = ProjectIdea.Questions[guided.Question];
                if (asking.Choices.Count > 0) guided.ChooseGuideAnswer(asking.Choices[0]);
                else if (asking.SkipLabel != null) guided.ChooseGuideSkip();
                else guided.WriteGuideAnswer("Add a page of race times.");
                yield return ("fixed question " + (question + 1) + ", answered", NewProjectScreens.FixedQuestion(guided, false, voice: true, said: null));
                guided.NextQuestion();
            }

            // The recap: no row chosen, each row chosen, Start over confirming and waiting.
            var draft = new NewWorkDraft(commands);
            var local = RenderRuntime("local", ModelChoice.Listed);
            draft.ChooseRuntime(local);
            draft.SetModels(new RuntimeModelsResponse
            {
                RuntimeId = "local",
                Result = new AvailableModels
                {
                    Models = new List<RuntimeModel>
                    {
                        new() { ModelRef = "ollama/render", DisplayName = "Local model (render)", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared },
                    },
                },
            });
            yield return ("recap", NewProjectScreens.Recap(proposed, draft, null, true, null, EntryText.ChooseWhereFilesLive));
            yield return ("recap, ready", NewProjectScreens.Recap(proposed, draft, null, true, null, null));
            foreach (var fact in new[] { RecapFact.Name, RecapFact.FirstTask, RecapFact.Folder, RecapFact.HowItRuns, RecapFact.StartOver })
            {
                yield return ("recap, " + fact + " chosen", NewProjectScreens.Recap(proposed, draft, null, true, null, EntryText.ChooseWhereFilesLive, fact));
            }
            yield return ("recap, Start over confirming", NewProjectScreens.Recap(proposed, draft, null, true, null, null, RecapFact.StartOver, confirmingStartOver: true));
            yield return ("recap, Start over waiting", NewProjectScreens.Recap(proposed, draft, null, true, null, EntryText.AlreadyStarting, RecapFact.StartOver,
                startOverProblem: EntryText.AlreadyStarting));

            // What a recap's row changes: the first task, the folder, how it runs, and words.
            yield return ("first task", NewProjectScreens.RecapTask(proposed, false, voice: true));
            var place = new LocationRoot
            {
                Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
                Folders = new List<LocationFolder>(),
            };
            var listing = new LocationsResponse { Roots = new List<LocationRoot> { place } };
            yield return ("folder", NewProjectScreens.RecapFolder(proposed, false, listing, null, null));
            yield return ("folder, unread", NewProjectScreens.RecapFolder(proposed, false, null, "timeout", null));
            var runtimes = new[] { local, RenderRuntime("other", ModelChoice.None) };
            yield return ("how it runs", NewProjectScreens.RecapOptions(proposed, false, draft, runtimes, showModels: false, live: true));
            yield return ("how it runs, its models", NewProjectScreens.RecapOptions(proposed, false, draft, runtimes, showModels: true, live: true));
            foreach (var what in new[] { WordsFor.Name, WordsFor.FirstTask, WordsFor.FolderName })
            {
                yield return ("words for " + what, NewProjectScreens.Words(proposed, false, what, written: null, heard: false, voice: true, said: null,
                    root: what == WordsFor.FolderName ? place : null));
            }

            // Start building's review, unread, read and its Yes waiting.
            var review = new NewWorkReview("Race Times", "Title", "Local agent", "Local model", "on your computer", "ollama/render", "Create one web page.");
            yield return ("review", NewProjectScreens.Review(proposed, review, null));
            Read(review);
            yield return ("review, read", NewProjectScreens.Review(proposed, review, null));
            yield return ("review, waiting", NewProjectScreens.Review(proposed, review, EntryText.WaitingForMac));

            // Starting: on its way, a folder already there, and an outcome not known; then the unknown start.
            var building = new NewWorkDraft(commands);
            building.ChooseRuntime(RenderRuntime("local", ModelChoice.None));
            building.Objective = "Add a page.";
            var folder = ProjectFolder.New(place, "recipes")!;
            var sequence = new BuildSequence(building, commands, "Recipes", folder.ToContract());
            var create = sequence.Begin(Reviewed(sequence));
            yield return ("starting", NewProjectScreens.Starting(typed, sequence, folder));
            sequence.Advance(WithCommand(new CommandView
            {
                CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Time, UpdatedAt = Time,
                Rejection = new CommandRejection { Code = RejectionCode.LocationExists, Message = "There is already a folder named recipes." },
            }));
            yield return ("starting, the folder there", NewProjectScreens.Starting(typed, sequence, folder));
            yield return ("starting, to try again", NewProjectScreens.Starting(typed, sequence, null));
            var unknown = new BuildSequence(building, commands, "Recipes");
            var made = unknown.Begin(Reviewed(unknown));
            unknown.AcknowledgementLost(new CommandOutcomeUnknownException(made.CommandId, "The socket closed."));
            unknown.Advance(new ClientProjection());
            yield return ("starting, not known", NewProjectScreens.Starting(typed, unknown, null));
            yield return ("the unknown start", NewProjectScreens.Unresolved(typed, "c-1", null, armed: false, live: true));
            yield return ("the unknown start, Clear armed", NewProjectScreens.Unresolved(typed, "c-1",
                new CommandView { CommandId = "c-1", Status = CommandStatus.Failed, IssuedAt = Time, UpdatedAt = Time }, armed: true, live: true));

            static MenuFrame Questions(ProjectIdea idea, bool voice = true) => NewProjectScreens.Questions(idea, false, voice: voice, said: null, waitedSeconds: 0);

            static CompanionReplyResponse Reply(CompanionReply reply) => new()
            {
                Reply = reply,
                Provenance = "reported",
                Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" },
            };

            ProjectIdea Proposed()
            {
                var idea = new ProjectIdea();
                idea.UseIdea(RenderIdea);
                var talk = idea.BeginCompanion(CompanionStart.Idea);
                talk.Ask(CompanionWant.Proposal);
                talk.Replied(talk.Generation, Reply(new ProposeReply
                {
                    Line = "That is clear enough to start.",
                    View = CompanionView.Clear,
                    Proposal = new CompanionProposal { ProjectName = "Race Times", FirstTask = "Create one web page where an organiser types a name and a time." },
                }));
                idea.UseProposal(talk.Proposal!.Proposal);
                return idea;
            }

            static void Read(NewWorkReview read)
            {
                read.Paginate(read.Items.Select(_ => 1).ToList(), read.Items.Count);
                var now = 0.0;
                read.Drawn(now);
                while (read.Next(now += 1)) read.Drawn(now);
            }

            static NewWorkReview Reviewed(BuildSequence sent)
            {
                var read = new NewWorkReview(sent.NewProjectName ?? "Project", "Title", "Agent", "Model", "on your computer", sent.Draft.Model?.ModelRef ?? "none",
                    sent.Draft.Objective, folderChoice: sent.Location);
                Read(read);
                return read;
            }
        }

        private static RuntimeDescriptor RenderRuntime(string id, ModelChoice models) => new()
        {
            RuntimeId = id,
            DisplayName = id == "local" ? "Local agent (render)" : "Another agent (render)",
            Kind = "render",
            Synthetic = false,
            ModelChoice = models,
            UsesProjectLocation = true,
            Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, AnswerQuestion = true, Interrupt = true },
        };

        /// <summary>A projection whose only record is <paramref name="command"/>, as the computer reports it.</summary>
        private static ClientProjection WithCommand(CommandView command)
        {
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = "render", Origin = JournalOrigin.Live },
                Position = 1,
                Projects = new List<ProjectView>(),
                Workstreams = new List<WorkstreamView>(),
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView> { command },
                Runtimes = new List<RuntimeDescriptor>(),
            }, new StateChanges());
            return state;
        }
    }
}
