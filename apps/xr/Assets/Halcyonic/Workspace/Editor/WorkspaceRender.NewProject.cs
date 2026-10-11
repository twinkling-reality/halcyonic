#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
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
        private const string RenderIdea = "A page of race times for my running club";

        /// <summary>
        /// Every New project footer (ADR 0026: never four prompts), laid in the file's column at this
        /// pass's text size: each step's screens in every state that changes its footer, and each again
        /// with the footer's Next page, "Next page" and "First page", wherever the flow pages there
        /// (<see cref="NewProjectFlow.PagesInFooter"/>): not a question's answers, which turn by More
        /// answers rows, nor the review or the unknown start, which read in parts. A footer of four
        /// prompts fails, and so does one whose words don't fit its column; so do any of Halcyonic's own
        /// words cut short on a page or its side panel, as Start over's, which must say all it clears.
        /// A side panel stands as the plane stands it (<c>MenuPlane</c>): beside its page in a side
        /// panel's column, and with text a step larger in its page's place, as wide, carrying what
        /// <see cref="Footer.InPlace"/> carries, whose footer must fit too.
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
                        var shown = frame.With(footer, frame.Lines, frame.Side);
                        var view = MenuFrameView.Create(root.transform, what);
                        view.Show(shown, Glaze.Menu.FileColumnDegrees, MenuFrameView.SubjectHeight(shown.Subject, Glaze.Menu.FileColumnDegrees, false), pillRoom: false);
                        var (needed, room) = view.Footer.Measure;
                        laid++;
                        if (needed / room > tightest.Needed / tightest.Room) tightest = (what + " (" + prompts + ")", needed, room);
                        if (!view.Footer.Fits)
                        {
                            failures.Add("New project's " + what + ": its footer (" + prompts + ") needs " + Measured(needed) + " of " + Measured(room) + "; it does not fit.");
                        }
                        failures.AddRange(GlazeChecks.NothingCut(view.GetComponentsInChildren<TMP_Text>(false).Except(view.MayCut), "New project's " + what));
                    }
                    if (frame.Side is SidePanel side)
                    {
                        var inPlace = GlazeText.Scale > 1f;
                        var degrees = inPlace ? Glaze.Menu.FileColumnDegrees : Glaze.Menu.SideColumnDegrees;
                        var what = "New project's " + name + ", its side panel" + (inPlace ? " in the page's place" : "");
                        var panel = MenuFrameView.Create(root.transform, what);
                        panel.Show(side, degrees, MenuFrameView.SubjectHeight(side.Subject, degrees, false), pillRoom: false, inPlaceOf: inPlace ? frame : null);
                        failures.AddRange(GlazeChecks.NothingCut(panel.GetComponentsInChildren<TMP_Text>(false).Except(panel.MayCut), what));
                        if (inPlace)
                        {
                            var carried = frame.Footer.InPlace(side);
                            var prompts = string.Join(", ", carried.All.Select(each => each.Prompt.Words));
                            var (needed, room) = panel.Footer.Measure;
                            laid++;
                            if (needed / room > tightest.Needed / tightest.Room) tightest = (what + " (" + prompts + ")", needed, room);
                            if (!panel.Footer.Fits) failures.Add(what + ": the footer it carries (" + prompts + ") needs " + Measured(needed) + " of " + Measured(room) + "; it does not fit.");
                        }
                    }
                }
                Debug.Log("Halcyonic: workspace render: " + laid + " New project footers at " + pass + " text; the tightest, " + tightest.What + ", needs "
                    + Measured(tightest.Needed) + " of " + Measured(tightest.Room) + ".");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            return failures;
        }

        /// <summary>A focused batch entry point for the review's real-font regression, including mutation checks.</summary>
        public static void CheckReviewRows()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorkspaceRenders", variant));
                Directory.CreateDirectory(folder);
                return RenderNewProjectReview(folder).ToList();
            });
            foreach (var failure in failures) Debug.LogError("Halcyonic: review boundary: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: review boundary: every check passed.");
            KeepFontAssetsAsCommitted();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>A real-font row boundary, then every review part drawn before Yes, with its trailing word intact.</summary>
        private static IEnumerable<string> RenderNewProjectReview(string folder)
        {
            var failures = new List<string>();
            foreach (var spaced in new[] { false, true })
            {
                string? objective = null;
                var candidates = Enumerable.Range(1, 300).SelectMany(count => new[] { 'i', 'n', 'W' }.Select(letter =>
                    spaced ? "i" + new string(' ', count) + new string(letter, 10) + " TAIL" : new string(letter, count) + " TAIL"));
                foreach (var candidate in candidates)
                {
                    var item = new NewWorkReview("Project", "Task", "Agent", "Model", "on this Mac", "model", candidate).Items
                        .Single(read => read.Label == NewWorkReview.FirstTaskLabel);
                    var exact = MenuFrameView.RowsOf(new PageLine(item.Runs), Glaze.Menu.FileColumnDegrees);
                    if (exact > MenuFrameView.RowsOf(item.Text, Glaze.Menu.FileColumnDegrees))
                    {
                        objective = candidate;
                        break;
                    }
                }
                if (objective == null)
                {
                    failures.Add("review boundary: found no " + (spaced ? "exact-space" : "padding") + " row boundary with the committed font.");
                    continue;
                }
                var host = new ReviewRenderHost();
                var commands = new CommandFactory(new ClientInfo { Name = "render", Version = "0", DeviceLabel = "editor" });
                var flow = new NewProjectFlow(host, commands, new KeptInMemory());
                flow.Open(null, null);
                void Press(string action, string? key = null)
                {
                    for (var turn = 0; turn < 12 && key != null && flow.Frame!.Lines.All(line => line.Action != action || line.Key != key); turn++)
                    {
                        var frame = flow.Frame!;
                        var next = frame.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Kind == PromptKind.NextPage);
                        if (next == null) break;
                        flow.Drawn(frame, null);
                        host.Now += 1;
                        flow.Act(next.Id, null);
                    }
                    flow.Drawn(flow.Frame!, null);
                    host.Now += 1;
                    flow.Act(action, key);
                }
                Press(NewProjectScreens.TypeIdea);
                Press(NewProjectScreens.UseIdea);
                Press(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
                Press(NewProjectScreens.MoreOptions);
                Press(NewProjectScreens.ChooseRuntime, "review-render");
                Press(NewProjectScreens.Done);
                if (flow.Idea?.Rewrite(objective) != true) throw new InvalidOperationException("The review's boundary task was refused.");
                Press(NewProjectScreens.StartBuilding);
                _ = flow.Frame; // Building the frame measures the review, as the director does before drawing it.
                var review = flow.Review ?? throw new InvalidOperationException("The boundary review did not open: " + flow.Step + "; " + string.Join(", ", flow.Frame!.Footer.All.Select(each => each.Prompt.Words + ": " + each.Prompt.Reason)));
                for (var item = 0; item < review.Items.Count; item++)
                {
                    var expected = MenuFrameView.RowsOf(new PageLine(review.Items[item].Runs), Glaze.Menu.FileColumnDegrees);
                    var counted = review.Pages.SelectMany(page => page).Where(part => part.Item == item).Sum(part => part.Lines);
                    if (counted != expected) failures.Add("review boundary: item " + item + " requires " + expected + " rendered rows but the review counts " + counted + " before Yes.");
                }
                var root = new GameObject("Review boundary " + (spaced ? "spaces" : "padding"));
                var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                try
                {
                    var eyes = new Vector3(0f, EyeHeight, 0f);
                    var camera = MakeCamera(root.transform, eyes, texture);
                    var file = MenuFrameView.Create(root.transform, "File column");
                    var side = MenuFrameView.Create(root.transform, "Side panel");
                    var tailShown = false;
                    for (var page = 0; page < review.PageCount; page++)
                    {
                        var frame = flow.Frame!;
                        FileRender.Lay(frame, file, side, eyes);
                        failures.AddRange(GlazeChecks.NothingCut(file.ShownLines.Select(line => (TMP_Text)line.Words), "review boundary page " + page));
                        tailShown |= file.ShownLines.Any(line => line.Model.Runs.Text == review.Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel).Runs.Text
                            && line.Words.text.EndsWith("TAIL", StringComparison.Ordinal));
                        var image = Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, "review-boundary-" + (spaced ? "spaces" : "padding") + "-" + page + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        flow.Drawn(frame, null);
                        if (page < review.PageCount - 1)
                        {
                            if (review.CanConfirm) failures.Add("review boundary: Yes appears before the final part.");
                            host.Now += 1;
                            flow.Act(NewProjectScreens.NextPart, null);
                        }
                    }
                    if (!tailShown || !review.CanConfirm) failures.Add("review boundary: the trailing word must show before Yes is offered.");
                    Debug.Log("Halcyonic: workspace render: review boundary " + (spaced ? "spaces" : "padding") + ", " + review.PageCount + " parts, trailing word shown=" + tailShown + ", Yes=" + review.CanConfirm + ".");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            return failures;
        }

        /// <summary>The flow's host with the production font's measures and a deterministic clock; submits nothing.</summary>
        private sealed class ReviewRenderHost : IMenuHost
        {
            public ReviewRenderHost()
            {
                var runtime = RenderRuntime("review-render", ModelChoice.None);
                runtime.UsesProjectLocation = false;
                State.ApplySnapshot(new Snapshot
                {
                    Journal = new JournalInfo { JournalId = "render", Origin = JournalOrigin.Live },
                    Position = 1, Runtimes = new List<RuntimeDescriptor> { runtime },
                }, new StateChanges());
            }

            public ClientProjection State { get; } = new();
            public bool Connected => true;
            public bool Demonstration => false;
            public double Now { get; set; }
            public DateTimeOffset Clock => DateTimeOffset.Parse(Time, CultureInfo.InvariantCulture).AddSeconds(Now);
            public TimeZoneInfo Zone => TimeZoneInfo.Utc;
            public TextSize TextSize => GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;
            public bool VoiceOffered => false;
            public ControlPlaneApi? Api => null;
            public Task<CommandAckMessage>? Submit(CommandEnvelope command) => throw new InvalidOperationException("A render never submits work.");
            public bool KeyboardOffered => true;
            public void OpenKeyboard(string text, string prompt, Action<string> done) => done(RenderIdea);
            public int RowsOf(string words, float degrees) => MenuFrameView.RowsOf(words, degrees);
            public int RowsOf(PageLine line, float degrees) => MenuFrameView.RowsOf(line, degrees);
            public bool FitsHalf(PageLine line, float degrees) => MenuFrameView.FitsHalf(line, degrees);
            public int TitleRows(string subject, float degrees) => MenuFrameView.TitleRows(subject, degrees);
            public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);
            public float PageHeight(int rows, bool besideMenu) => MenuPage.Height(TextSize, rows, besideMenu: besideMenu);
            public void OpenFile(string id) { }
            public void OpenNewProject(string? id, string? name) { }
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
            // Adding a task to a project, whose Start over keeps the project.
            var forTask = new ProjectIdea("proj_render", "Race Times");
            forTask.UseIdea("Add a page of results for each race");
            yield return ("recap for a task, Start over chosen", NewProjectScreens.Recap(forTask, draft, null, true, null, null, RecapFact.StartOver));
            yield return ("recap for a task, Start over confirming", NewProjectScreens.Recap(forTask, draft, null, true, null, null, RecapFact.StartOver,
                confirmingStartOver: true));

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
            // The longest whys, within the three rows the page gives them (the review of batch 3).
            yield return ("folder, unread by another computer", NewProjectScreens.RecapFolder(proposed, false, null, ConnectionText.NotThePairedComputer, null));
            yield return ("folder, unread unproved", NewProjectScreens.RecapFolder(proposed, false, null, EntryText.FoldersUnproved, null));
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
            yield return ("the unknown start, changed before Yes", NewProjectScreens.Unresolved(typed, "c-1",
                new CommandView { CommandId = "c-1", Status = CommandStatus.Accepted, IssuedAt = Time, UpdatedAt = Time }, armed: false, live: true,
                said: EntryText.ChangedBeforeClear));

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
