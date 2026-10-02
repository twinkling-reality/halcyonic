#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Renders the project rail and every screen of the entry panel over the stage, with the
    /// characters 2.4 m away and on a desk half a meter away, and checks what the headset showed wrong
    /// with the New work panel: that the panel covers no character, lets nothing behind it show
    /// through, stays in the comfortable band and clear of the rail; that the rail keeps its right end
    /// free and its buttons apart; that none of Halcyonic's own words is cut short; that the whole
    /// request fits its pages; and that names and titles from outside show by the one rule. It saves
    /// each render in apps/xr/Builds/EntryRenders, which git ignores, with a close-up at a Quest 3's
    /// 25 pixels per degree. In the editor: Halcyonic > Render the Entry Panel Over the Stage. In batch
    /// mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a check fails.
    /// </summary>
    public static class EntryRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const string Journal = "01a0dcf1-5a80-7000-8000-000000000001";
        private const string Time = "2026-09-30T09:00:00.000Z";

        /// <summary>
        /// Where the room and pairing controls' buttons begin, to either side: 26 degrees less half of
        /// the widest resting button, the pairing panel's 0.24 of the design width at its scale, 0.4 m out.
        /// </summary>

        [MenuItem("Halcyonic/Render the Entry Panel Over the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Entry render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "EntryRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(RenderStage("far", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, hostile: false));
                failures.AddRange(RenderStage("desk", folder, radius: 0.55f, surfaceDrop: 0.46f, hostile: false));
                failures.AddRange(RenderStage("far-untrusted", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, hostile: true));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: entry render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: entry render: every check passed; the renders are in " + folder);
            return failures;
        }

        private static IEnumerable<string> RenderStage(string name, string folder, float radius, float? surfaceDrop, bool hostile)
        {
            var failures = new List<string>();
            var root = new GameObject("Entry render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var characters = WorkspaceRender.Lineup(root.transform, eyes, radius, surfaceDrop, WorkspaceRender.Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;

                var before = Portfolio(hostile, needsYouNow: false);
                var state = Portfolio(hostile, needsYouNow: true);
                var visibility = new StageVisibility();
                visibility.UseJournal(Journal);
                visibility.Hide("p-recipes", state.Projects.Keys);
                var lineup = new CharacterLineup(6);
                lineup.Update(state.Workstreams.Values.Where(work => visibility.Shows(work.ProjectId)));
                var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);

                var rail = ProjectRail.ForRender(root.transform, overview, surface);
                rail.ResetPosition();
                failures.AddRange(RailFits(name, rail, camera, hostile));
                WorkspaceRender.ForceMeshes(root);
                var railRender = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + "-rail.png"), railRender.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(railRender);
                var railCloseUp = WorkspaceRender.CloseUp(camera, texture, rail.Root);
                File.WriteAllBytes(Path.Combine(folder, name + "-rail-closeup.png"), railCloseUp.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(railCloseUp);
                // Every rail button a degree or more from every label and body, as the eyes see them.
                foreach (var button in rail.Shown)
                {
                    var near = new List<GlazeChecks.Extent> { GlazeChecks.Of("the rail's " + button.name, eyes, button.gameObject) };
                    foreach (var (view, _) in characters)
                    {
                        near.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                        near.Add(WorkspaceRender.BodyExtent(view, eyes));
                    }
                    failures.AddRange(GlazeChecks.Apart(near).Where(failure => failure.Contains("the rail's")).Select(failure => name + ": " + failure));
                }
                failures.AddRange(SettingsFits(name, folder, root, rail, camera, texture, characters, surface, hostile));
                // The rail steps out of the way while the entry panel is open, as on the headset.
                rail.Root.gameObject.SetActive(false);

                var panel = EntryPanel.ForRender(root.transform, state, overview, targets, surface);
                var places = new List<(string Screen, Places Places)>();
                foreach (var (suffix, show) in Screens(state, before, hostile))
                {
                    show(panel);
                    WorkspaceRender.ForceMeshes(root);
                    var what = name + " " + suffix;
                    var withStage = WorkspaceRender.Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(false);
                    var alone = WorkspaceRender.Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(true);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), withStage.EncodeToPNG());
                    var closeUp = WorkspaceRender.CloseUp(camera, texture, panel.Root);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);
                    var whole = Whole(camera, texture, panel.Root);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-panel.png"), whole.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(whole);

                    var frame = panel.Frame;
                    var rect = WorkspaceRender.ScreenRect(camera, frame.transform, frame.Size);
                    var (changed, compared) = ChangedInPanel(withStage, alone, camera, frame);
                    UnityEngine.Object.DestroyImmediate(withStage);
                    UnityEngine.Object.DestroyImmediate(alone);
                    if (compared == 0) failures.Add(what + ": the panel is not in the render, so the render checks nothing.");
                    if (changed > 0) failures.Add(what + ": " + changed + " pixels of the panel change when the stage behind it is drawn.");
                    foreach (var (view, target) in characters)
                    {
                        if (WorkspaceRender.Covered(camera, target, rect)) failures.Add(what + ": " + view.WorkstreamId + "'s body is behind the panel.");
                    }
                    failures.AddRange(PanelFits(what, frame, characters, eyes));
                    if (!hostile) failures.AddRange(NothingOfOursCut(panel.ShownParts, what, frame));
                    failures.AddRange(NoticesStayOnTheirScreen(panel.ShownParts, suffix, what));
                    if (suffix == "options-models-pages") failures.AddRange(PagesAndDone(frame, what));
                    places.Add((suffix, Places.Of(frame)));
                    // Every confirmation, reached by showing its screen and then pressing: Yes clear of every control shown before it and since.
                    if (frame.Shown?.Confirm != null) failures.AddRange(WorkspaceRender.YesClear(frame, what));
                    if (frame.Shown?.Confirm != null && frame.Shown.Parts is (_, var parts) && parts > 1) failures.AddRange(PagerOnTop(frame, what));
                    if (hostile && !suffix.StartsWith("review", StringComparison.Ordinal))
                    {
                        failures.AddRange(WorkspaceRender.AllShowLiterally(panel.Root.gameObject, "entry render " + what, eyes));
                    }
                    else if (hostile)
                    {
                        // The whole request spells what the headset font may lack as ASCII code points instead
                        // (NewWorkReview); the only other character is Halcyonic's own ellipsis on a cut title.
                        foreach (var request in panel.RequestLabels)
                        {
                            if (request.text.Any(character => (character < ' ' || character > '~') && character != '…'))
                            {
                                failures.Add(what + ": the whole request shows a character beyond ASCII.");
                            }
                        }
                        failures.AddRange(WorkspaceRender.AllShowLiterally(frame.Title.gameObject, "entry render " + what, eyes));
                    }
                }
                failures.AddRange(PlacesHold(name, places));
                var size = new PanelSize(PanelFrame.Distance, panel.Frame.Size.x / 2f * PanelFrame.Distance, panel.Frame.Size.y / 2f * PanelFrame.Distance);
                var (_, direction) = WorkspaceLayout.PlaceForeground(targets, eyes, camera.transform.forward, surface, new List<BodyInView>(), size);
                Debug.Log("Halcyonic: entry render " + name + ": the panel's center is " + WorkspaceRender.Degrees(direction.Elevation)
                    + " degrees from eye level, " + (direction.Above ? "above" : "below") + " the characters it passes, "
                    + (direction.Clear ? "clear of every body." : "over a body."));
                if (!direction.Clear) failures.Add(name + ": the panel covers a character's body.");
                if (direction.Elevation < WorkspacePlacement.LowestDegrees - 0.01f || direction.Elevation > WorkspacePlacement.HighestDegrees + 0.01f)
                {
                    failures.Add(name + ": the panel's center is outside the comfortable band.");
                }
                if (!hostile) failures.AddRange(ReviewShowsEverything(name, folder, camera, texture, panel, state));
                if (hostile)
                {
                    // The rail as it shows, which stepped aside for the panel: its icons are measured as drawn.
                    rail.Root.gameObject.SetActive(true);
                    failures.AddRange(WorkspaceRender.AllShowLiterally(rail.Root.gameObject, "entry render " + name + " rail", eyes));
                    rail.Root.gameObject.SetActive(false);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>Each screen, as the person reaches it, with what it needs in place.</summary>
        private static IEnumerable<(string Suffix, Action<EntryPanel> Show)> Screens(ClientProjection state, ClientProjection before, bool hostile)
        {
            var listing = Listing(hostile);
            yield return ("welcome", panel => panel.ShowForRender(EntryPanel.Screen.Welcome));
            yield return ("connect", panel => panel.ShowForRender(EntryPanel.Screen.Connect));
            yield return ("more-work", panel => panel.ShowForRender(EntryPanel.Screen.MoreWork));
            yield return ("create", panel => panel.ShowForRender(EntryPanel.Screen.CreateStart, new ProjectIdea(), Draft(state, listed: false)));
            // Hold to talk's longest words, beside it.
            yield return ("create-voice", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.CreateStart, new ProjectIdea(), Draft(state, listed: false));
                panel.SayForRender(VoiceText.Shown.OrderByDescending(words => words.Length).First());
            });
            yield return ("guide", panel =>
            {
                var idea = new ProjectIdea();
                idea.BeginGuide();
                idea.Answer("A website");
                idea.Answer("My team");
                panel.ShowForRender(EntryPanel.Screen.Guide, idea, Draft(state, listed: false));
            });
            yield return ("recap", panel => panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true)));
            yield return ("recap-needs-you", panel => panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true), before: before));
            // Start over asks to be confirmed in place, Cancel where Start building stood.
            yield return ("recap-start-over", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true));
                panel.PressForRender(EntryScreens.StartOver);
            });
            yield return ("recap-long", panel =>
            {
                var idea = new ProjectIdea();
                idea.UseIdea(string.Join(" ", Enumerable.Repeat("Track recipes, plan the week's dinners and write the shopping list.", 40)));
                panel.ShowForRender(EntryPanel.Screen.Recap, idea, Draft(state, listed: false));
            });
            yield return ("options-runtimes", panel => panel.ShowForRender(EntryPanel.Screen.Options, Idea(), Draft(state, listed: false)));
            yield return ("options-models", panel => panel.ShowForRender(EntryPanel.Screen.Options, Idea(), Draft(state, listed: true)));
            // More models than a page holds: Next page and Done each keep a place.
            yield return ("options-models-pages", panel => panel.ShowForRender(EntryPanel.Screen.Options, Idea(), Draft(state, listed: true, extra: 7)));
            // A model on the Mac chosen for the person, and the first press on one that runs elsewhere, which chooses nothing yet.
            yield return ("recap-chosen-for-you", panel => panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true, chosen: false)));
            yield return ("options-models-elsewhere", panel =>
            {
                var draft = Draft(state, listed: true, chosen: false);
                draft.ChooseModel(draft.Models[1]);
                panel.ShowForRender(EntryPanel.Screen.Options, Idea(), draft);
            });
            // The review as the person reaches it: the recap, then Start building; then every part with Next.
            yield return ("review", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true));
                panel.PressForRender(EntryScreens.StartBuilding);
            });
            yield return ("review-last", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.Recap, Idea(), Draft(state, listed: true));
                panel.PressForRender(EntryScreens.StartBuilding);
                while (panel.Review != null && !panel.Review.CanConfirm) panel.PressForRender(PanelModel.NextPart);
            });
            yield return ("sending-refused", panel => panel.ShowForRender(EntryPanel.Screen.Sending, Idea(), Draft(state, listed: true), Refused(state)));
            yield return ("folder", panel => panel.ShowForRender(EntryPanel.Screen.Folder, Idea(), Draft(state, listed: true), listing: listing));
            yield return ("folder-none", panel => panel.ShowForRender(EntryPanel.Screen.Folder, Idea(), Draft(state, listed: true), listing: new LocationsResponse()));
            yield return ("recap-move", panel => panel.ShowForRender(EntryPanel.Screen.Recap, Moving(), Draft(state, listed: true)));
            yield return ("review-move", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.Recap, Moving(), Draft(state, listed: true));
                panel.PressForRender(EntryScreens.StartBuilding);
            });
            yield return ("sending-folder-exists", panel =>
            {
                var idea = Idea();
                panel.ShowForRender(EntryPanel.Screen.Sending, idea, Draft(state, listed: true), FolderTaken(state, idea));
            });
            yield return ("previous", panel => panel.ShowForRender(EntryPanel.Screen.Previous, unresolvedCommand: "0192f3c1-7e2a-7b3c-8d4e-5f6a7b8c9d0e"));
            // The second of the two presses, which stands where the first did not.
            yield return ("previous-armed", panel =>
            {
                panel.ShowForRender(EntryPanel.Screen.Previous, unresolvedCommand: "0192f3c1-7e2a-7b3c-8d4e-5f6a7b8c9d0e");
                panel.PressForRender(EntryScreens.Clear);
            });
        }

        private static ProjectIdea Idea()
        {
            var idea = new ProjectIdea();
            idea.UseIdea("A recipe tracker that suggests dinners from what is in my fridge.");
            idea.ChooseFolder(ProjectFolder.New(Listing(false).Roots[0], ProjectFolder.SuggestName("Recipe tracker")));
            return idea;
        }

        /// <summary>New work in the storefront project, which moves it to a new folder.</summary>
        private static ProjectIdea Moving()
        {
            var idea = new ProjectIdea("p-store", "Storefront API");
            idea.UseIdea("Add a search page to the storefront.");
            idea.ChooseFolder(ProjectFolder.New(Listing(false).Roots[0], "storefront-v2"));
            return idea;
        }

        /// <summary>
        /// What the Mac lists: a place with folders, more of them than it lists, and a place no longer
        /// on the Mac; with <paramref name="hostile"/>, folder and place names at their worst.
        /// </summary>
        private static LocationsResponse Listing(bool hostile)
        {
            string Named(string plain, string field) => hostile ? WorkspaceRender.Hostile(field) : plain;
            return new LocationsResponse
            {
                Roots = new List<LocationRoot>
                {
                    new()
                    {
                        Path = "/Users/person/Projects", Name = Named("Projects", "root"), Status = LocationRootStatus.Available, FoldersTruncated = true,
                        Folders = new List<LocationFolder>
                        {
                            new() { Name = Named("recipe-tracker", "folder"), Path = "/Users/person/Projects/recipe-tracker" },
                            new() { Name = Named("storefront-api", "folder"), Path = "/Users/person/Projects/storefront-api" },
                        },
                    },
                    new() { Path = "/Volumes/Old/Code", Name = Named("Code", "root"), Status = LocationRootStatus.Missing, Folders = new List<LocationFolder>(), FoldersTruncated = false },
                },
            };
        }

        /// <summary>A new project whose new folder's name the Mac already has: refused, offering to use that folder.</summary>
        private static BuildSequence FolderTaken(ClientProjection shown, ProjectIdea idea)
        {
            var draft = Draft(shown, listed: true);
            draft.Objective = idea.FirstTask;
            var sequence = new BuildSequence(draft, Commands(), idea.Name, idea.Folder!.ToContract());
            var project = sequence.Begin();
            sequence.Advance(With(new CommandView
            {
                CommandId = project.CommandId, Status = CommandStatus.Rejected, IssuedAt = Time, UpdatedAt = Time,
                Rejection = new CommandRejection { Code = RejectionCode.LocationExists, Message = "A folder with that name already exists." },
            }));
            return sequence;
        }

        /// <param name="chosen">Whether the person chose the model on the Mac themselves; otherwise it stands as chosen for them.</param>
        /// <param name="extra">More local models than one page holds, to page through.</param>
        private static NewWorkDraft Draft(ClientProjection state, bool listed, bool chosen = true, int extra = 0)
        {
            var draft = new NewWorkDraft(Commands());
            var runtime = state.Runtimes.First(each => (each.ModelChoice == ModelChoice.Listed) == listed);
            draft.ChooseRuntime(runtime);
            if (!listed) return draft;
            draft.SetModels(new RuntimeModelsResponse
            {
                RuntimeId = runtime.RuntimeId,
                Result = new AvailableModels
                {
                    Models = new List<RuntimeModel>
                    {
                        new() { ModelRef = "ollama/render-local:latest", DisplayName = "Local model (render)", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared },
                        new() { ModelRef = "hosted/render-remote", DisplayName = "Hosted model (render)", Served = ModelServed.Remote, ToolCalling = ModelToolCalling.Unknown },
                    }.Concat(Enumerable.Range(1, extra).Select(index => new RuntimeModel
                    {
                        ModelRef = "ollama/render-" + index.ToString(CultureInfo.InvariantCulture),
                        DisplayName = "Local model " + index.ToString(CultureInfo.InvariantCulture) + " (render)",
                        Served = ModelServed.ThisMac,
                        ToolCalling = ModelToolCalling.Declared,
                    })).ToList(),
                },
            });
            if (chosen) draft.ChooseModel(draft.Models[0]);
            return draft;
        }

        /// <summary>A list of more than a page shows both Next and Done, 12 mm apart or more, so every page and the way back can be reached.</summary>
        private static IEnumerable<string> PagesAndDone(PanelFrame frame, string what)
        {
            var next = frame.NextPage;
            var done = frame.ButtonFor(EntryScreens.Done);
            if (next == null) yield return what + ": a list longer than a page offers no Next.";
            if (done == null) yield return what + ": Done does not show beside a list longer than a page.";
            if (next != null && done != null && !Apart(RectOf(next), RectOf(done), TargetGap(frame)))
            {
                yield return what + ": Next and Done are closer than 12 mm.";
            }
        }

        /// <summary>A start the runtime refused after the project and its work were made, as a runtime without a folder refuses.</summary>
        private static BuildSequence Refused(ClientProjection shown)
        {
            var draft = Draft(shown, listed: true);
            draft.Objective = Idea().FirstTask;
            var sequence = new BuildSequence(draft, Commands(), "Recipe tracker");
            var project = sequence.Begin();
            var workstream = sequence.Advance(With(Done(project, new ProjectCreatedResult { ProjectId = "p-new" })))!;
            var start = sequence.Advance(With(Done(workstream, new WorkstreamCreatedResult { WorkstreamId = "w-new" })))!;
            sequence.Advance(With(new CommandView
            {
                CommandId = start.CommandId, Status = CommandStatus.Rejected, IssuedAt = Time, UpdatedAt = Time,
                Rejection = new CommandRejection { Code = RejectionCode.LocationRequired, Message = "The project has no folder to work in." },
            }));
            return sequence;
        }

        private static CommandView Done(CommandEnvelope command, CommandResult result) =>
            new() { CommandId = command.CommandId, Status = CommandStatus.Completed, IssuedAt = Time, UpdatedAt = Time, Result = result };

        private static ClientProjection With(CommandView command)
        {
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = Journal, Origin = JournalOrigin.Live },
                Position = 1,
                Projects = new List<ProjectView>(),
                Workstreams = new List<WorkstreamView>(),
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView> { command },
                Runtimes = new List<RuntimeDescriptor>(),
            }, new StateChanges());
            return state;
        }

        private static CommandFactory Commands() => new(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });

        /// <summary>
        /// Three projects with the demonstration's kind of work: ten workstreams, of which six stand on
        /// the stage; one project hidden, with work that needs the person. With <paramref name="needsYouNow"/>
        /// another comes to need the person, for the banner; <paramref name="hostile"/> names every
        /// project and titles every workstream with text from outside at its worst.
        /// </summary>
        internal static ClientProjection Portfolio(bool hostile, bool needsYouNow)
        {
            string Named(string plain, string field) => hostile ? WorkspaceRender.Hostile(field) : plain;
            var projects = new List<ProjectView>
            {
                new()
                {
                    ProjectId = "p-store", Name = Named("Storefront API", "project"), CreatedAt = Time, UpdatedAt = Time,
                    Location = new ProjectLocation { Path = "/Users/person/Projects/storefront-api", Name = Named("storefront-api", "folder"), Created = false },
                },
                new() { ProjectId = "p-docs", Name = Named("Docs site", "project"), CreatedAt = Time, UpdatedAt = Time },
                new() { ProjectId = "p-recipes", Name = Named("Recipe tracker", "project"), CreatedAt = Time, UpdatedAt = Time },
            };
            var work = new List<WorkstreamView>
            {
                Work("w-a", "p-store", Named("Add rate limiting to sign-in", "title"), needsYouNow ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running, 1),
                Work("w-b", "p-store", Named("Paginate the order history", "title"), WorkstreamStatus.Running, 2),
                Work("w-c", "p-store", Named("Send order confirmations", "title"), WorkstreamStatus.Verifying, 3),
                Work("w-d", "p-store", Named("Refresh the checkout copy", "title"), WorkstreamStatus.Completed, 4),
                Work("w-e", "p-store", Named("Upgrade the image pipeline", "title"), WorkstreamStatus.Failed, 5),
                Work("w-f", "p-docs", Named("Rewrite the quick start", "title"), WorkstreamStatus.Running, 6),
                Work("w-g", "p-docs", Named("Fix broken links", "title"), WorkstreamStatus.Completed, 7),
                Work("w-h", "p-docs", Named("Add a search page", "title"), WorkstreamStatus.Created, 8),
                Work("w-i", "p-recipes", Named("Import recipes from a file", "title"), WorkstreamStatus.WaitingForHuman, 9),
                Work("w-j", "p-recipes", Named("Plan the week's dinners", "title"), WorkstreamStatus.Completed, 10),
            };
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = Journal, Origin = JournalOrigin.Live },
                Position = 1,
                Projects = projects,
                Workstreams = work,
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor>
                {
                    Runtime("mock", "Simulated agent (render)", synthetic: true, ModelChoice.None),
                    Runtime("local", "Local agent (render)", synthetic: false, ModelChoice.Listed),
                },
            }, new StateChanges());
            return state;
        }

        private static WorkstreamView Work(string id, string project, string title, WorkstreamStatus status, int minute) => new()
        {
            WorkstreamId = id,
            ProjectId = project,
            Title = title,
            Objective = null,
            Status = status,
            Attention = new Attention
            {
                Level = status == WorkstreamStatus.WaitingForHuman ? AttentionLevel.ActionRequired
                    : status == WorkstreamStatus.Failed ? AttentionLevel.Notice : AttentionLevel.None,
                Reasons = new List<AttentionReason>(),
            },
            CurrentExecutionId = null,
            ExecutionIds = new List<string>(),
            CreatedAt = Time,
            UpdatedAt = "2026-09-30T09:" + minute.ToString("00", CultureInfo.InvariantCulture) + ":00.000Z",
        };

        private static RuntimeDescriptor Runtime(string id, string name, bool synthetic, ModelChoice choice) => new()
        {
            RuntimeId = id,
            Kind = id,
            DisplayName = name,
            Synthetic = synthetic,
            ModelChoice = choice,
            // A real runtime works in the project's folder; the simulated one needs none.
            UsesProjectLocation = !synthetic,
            Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, Interrupt = true },
        };

        /// <summary>
        /// The rail keeps its buttons inside its width, 24 degrees to either side, 12 mm apart in each
        /// row, each at least 60 dp tall (48 compact), and its own words whole (ADR 0023). Nothing rests
        /// beside it any more: the room and pairing controls are in Settings.
        /// </summary>
        private static IEnumerable<string> RailFits(string name, ProjectRail rail, Camera camera, bool hostile)
        {
            var failures = new List<string>();
            WorkspaceRender.ForceMeshes(rail.gameObject);
            var buttons = rail.Shown.ToList();
            var gap = Glaze.TargetGapMeters / rail.Root.lossyScale.x;
            foreach (var row in buttons.GroupBy(button => Mathf.Round(button.transform.localPosition.y * 1000f)))
            {
                var ordered = row.OrderBy(button => button.transform.localPosition.x).ToList();
                for (var index = 0; index < ordered.Count; index++)
                {
                    var button = ordered[index];
                    var left = button.transform.localPosition.x - button.Width / 2f;
                    var right = button.transform.localPosition.x + button.Width / 2f;
                    if (right > ProjectRail.Width / 2f + 1e-4f) failures.Add(name + ": the rail's " + button.name + " runs past its right end.");
                    if (left < -ProjectRail.Width / 2f - 1e-4f) failures.Add(name + ": the rail's " + button.name + " runs past its left end.");
                    if (index > 0 && ordered[index - 1].transform.localPosition.x + ordered[index - 1].Width / 2f > left - gap + 1e-4f)
                    {
                        failures.Add(name + ": the rail's " + ordered[index - 1].name + " and " + button.name + " are closer than 12 mm.");
                    }
                }
            }
            if (!hostile) failures.AddRange(NothingOfOursCut(rail.Shown.Cast<Component>(), name + " rail"));
            var eyes = camera.transform.position;
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, eyes, name + " rail"));
            failures.AddRange(GlazeChecks.TextLargeEnough(rail.Root.gameObject, eyes, name + " rail"));
            // How far to the side, as the angle from the rail's middle as seen from the eyes, whatever its height.
            var widest = 0f;
            var center = rail.Root.position - eyes;
            foreach (var button in buttons)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var edge = button.transform.TransformPoint(new Vector3(side * button.Width / 2f, 0f, 0f)) - eyes;
                    widest = Mathf.Max(widest, Vector3.Angle(center, edge));
                }
            }
            Debug.Log("Halcyonic: entry render " + name + ": the rail shows " + buttons.Count + " buttons, reaches " + WorkspaceRender.Degrees(widest)
                + " degrees to the side and stands " + WorkspaceRender.Degrees(Mathf.Atan2(-center.y, new Vector2(center.x, center.z).magnitude) * Mathf.Rad2Deg)
                + " degrees below eye level.");
            if (widest > ProjectRail.HalfWidthDegrees + 0.5f) failures.Add(name + ": the rail reaches " + WorkspaceRender.Degrees(widest) + " degrees to the side.");
            return failures;
        }

        /// <summary>
        /// The Settings sheet, opened as the rail's Settings opens it, with the sections the room, where
        /// the characters stand under it, and pairing fill: it opens clear of every character and label, a degree or more, in the
        /// comfortable band, its targets 60 dp, its words whole and large enough, and a refusal from
        /// whatever answered at the typed address shows as written.
        /// </summary>
        private static IEnumerable<string> SettingsFits(string name, string folder, GameObject root, ProjectRail rail, Camera camera, RenderTexture texture,
            List<(CharacterView View, CharacterTarget Target)> characters, float? surface, bool hostile)
        {
            var failures = new List<string>();
            var sheet = SettingsSheet.On(rail.gameObject);
            var room = sheet.Section(SettingsText.YourRoom, 0);
            room.Say(surface.HasValue ? "Your agents are on your desk." : "No free desk or table in reach, so your agents stand in front of you.");
            room.Offer(room.Button("Space switch", ButtonRole.Secondary), "Show a virtual space");
            // Where the characters stand, under the room's buttons, with its longest line: beside a window, the other two offered.
            var window = sheet.Continuation(room);
            window.Say(surface.HasValue ? SettingsText.ArrangedByRoom : SettingsText.Arrangement(StageArrangement.BesideAWindow));
            if (!surface.HasValue)
            {
                window.Offer(window.Button("Arrangement 0", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.InFront));
                window.Offer(window.Button("Arrangement 1", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.TurnedAside));
            }
            var mac = sheet.Section(SettingsText.YourMac, 1);
            mac.Say(hostile ? "Pairing failed: " + WorkspaceRender.Hostile("refusal") : "Paired with the Mac at 192.168.1.23:47801. Connecting over Wi-Fi.");
            mac.Offer(mac.Button("Pairing", ButtonRole.Destructive), "Forget this Mac");
            sheet.OpenForRender(characters.ConvertAll(character => character.Target), surface);
            rail.Root.gameObject.SetActive(false);
            WorkspaceRender.ForceMeshes(root);
            var image = WorkspaceRender.Render(camera, texture);
            File.WriteAllBytes(Path.Combine(folder, name + "-settings.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            var closeUp = WorkspaceRender.CloseUp(camera, texture, sheet.Root);
            File.WriteAllBytes(Path.Combine(folder, name + "-settings-closeup.png"), closeUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(closeUp);

            var eyes = camera.transform.position;
            var others = new List<GlazeChecks.Extent> { GlazeChecks.Of("the Settings sheet", eyes, sheet.Root.gameObject) };
            foreach (var (view, _) in characters)
            {
                others.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                others.Add(WorkspaceRender.BodyExtent(view, eyes));
            }
            failures.AddRange(GlazeChecks.Apart(others).Where(failure => failure.Contains("the Settings sheet")).Select(failure => name + ": " + failure));
            failures.AddRange(GlazeChecks.TargetsLargeEnough(sheet.Root.GetComponentsInChildren<GlazeButton>(false), eyes, name + " settings"));
            failures.AddRange(GlazeChecks.TextLargeEnough(sheet.Root.gameObject, eyes, name + " settings"));
            if (hostile) failures.AddRange(WorkspaceRender.AllShowLiterally(sheet.Root.gameObject, name + " settings", eyes));
            else failures.AddRange(NothingOfOursCut(sheet.Root.GetComponentsInChildren<TMP_Text>(false).Cast<Component>()
                .Concat(sheet.Root.GetComponentsInChildren<GlazeButton>(false)), name + " settings"));
            var toward = sheet.Root.position - eyes;
            var elevation = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
            Debug.Log("Halcyonic: entry render " + name + ": Settings opens " + WorkspaceRender.Degrees(-elevation) + " degrees below eye level, "
                + WorkspaceRender.Degrees(GlazeTokens.DegreesOf(sheet.Size.x)) + " by " + WorkspaceRender.Degrees(GlazeTokens.DegreesOf(sheet.Size.y)) + " degrees.");
            if (elevation < WorkspacePlacement.LowestDegrees - 0.01f || elevation > WorkspacePlacement.HighestDegrees + 0.01f)
            {
                failures.Add(name + ": Settings opens outside the comfortable band.");
            }
            sheet.Close();
            rail.Root.gameObject.SetActive(true);
            return failures;
        }

        /// <summary>
        /// A line said on one screen never shows on another: hold to talk's words only on the Create
        /// start screen that said them. The screens render one after another on the same panel, so a
        /// line left over from an earlier screen shows here.
        /// </summary>
        private static IEnumerable<string> NoticesStayOnTheirScreen(IEnumerable<Component> parts, string suffix, string what)
        {
            if (suffix == "create-voice") yield break;
            var voice = new HashSet<string>(VoiceText.Shown.Select(LabelText.ForTextMeshPro));
            foreach (var part in parts)
            {
                if (part is TMP_Text label && voice.Contains(label.text)) yield return what + ": " + label.name + " still shows hold to talk's words: " + label.text;
            }
        }

        /// <summary>
        /// Nothing showing is cut short, with the render's short names and titles: only text from
        /// outside that a row or a title says is data may end in an ellipsis, as a long first task does.
        /// </summary>
        internal static IEnumerable<string> NothingOfOursCut(IEnumerable<Component> parts, string what, PanelFrame? frame = null)
        {
            var failures = new List<string>();
            foreach (var part in parts)
            {
                var labels = part is GlazeButton button ? new TMP_Text?[] { button.Label, button.Detail, button.Overline, button.End } : new[] { part as TMP_Text };
                foreach (var label in labels)
                {
                    if (label == null || !label.gameObject.activeInHierarchy) continue;
                    if (frame != null && frame.HoldsData(label)) continue;
                    label.ForceMeshUpdate();
                    if (!label.isTextTruncated || label.name == "First task") continue;
                    failures.Add(what + ": " + part.name + " cuts its words short: " + label.text);
                }
            }
            return failures;
        }

        /// <summary>
        /// The panel as the eyes see it: a degree or more from every character's label and body, its
        /// targets 60 dp (48 for the window controls and the pager) and 12 mm apart, and every word at
        /// least the caption's size at its own distance.
        /// </summary>
        private static IEnumerable<string> PanelFits(string what, PanelFrame frame, List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
        {
            var failures = new List<string>();
            var near = new List<GlazeChecks.Extent> { GlazeChecks.Of("the entry panel", eyes, frame.gameObject) };
            foreach (var (view, _) in characters)
            {
                near.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                near.Add(WorkspaceRender.BodyExtent(view, eyes));
            }
            failures.AddRange(GlazeChecks.Apart(near).Where(failure => failure.Contains("the entry panel")).Select(failure => what + ": " + failure));
            // A row that only says something takes no press, so it is no target.
            var buttons = frame.Buttons.Where(button => !button.Static).ToList();
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, eyes, what));
            failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(buttons, what));
            failures.AddRange(GlazeChecks.TextLargeEnough(frame.gameObject, eyes, what));
            if (!frame.BarIcons) Debug.Log("Halcyonic: entry render " + what + ": the bar has no room for its icons, so its words stand alone.");
            var gap = TargetGap(frame);
            for (var a = 0; a < buttons.Count; a++)
            {
                for (var b = a + 1; b < buttons.Count; b++)
                {
                    if (!Apart(RectOf(buttons[a]), RectOf(buttons[b]), gap)) failures.Add(what + ": " + buttons[a].name + " and " + buttons[b].name + " are closer than 12 mm.");
                }
            }
            var size = frame.Size;
            foreach (var button in buttons)
            {
                var rect = RectOf(button);
                if (rect.xMin < -size.x / 2f || rect.xMax > size.x / 2f || rect.yMin < -size.y / 2f || rect.yMax > size.y / 2f)
                {
                    failures.Add(what + ": " + button.name + " runs past the panel's edge.");
                }
            }
            return failures;
        }

        /// <summary>Where the parts that never move stand on one screen: Close, the bar's right end, Back, and the pager.</summary>
        private readonly struct Places
        {
            private Places(Vector2? close, Vector2? rightEnd, Vector2? back, Vector2? pager)
            {
                Close = close;
                RightEnd = rightEnd;
                Back = back;
                Pager = pager;
            }

            /// <summary>Close's right edge and its middle's height: Not now, in its place on the welcome, is wider.</summary>
            public Vector2? Close { get; }

            /// <summary>The bar's right end: its right edge and its middle's height.</summary>
            public Vector2? RightEnd { get; }

            /// <summary>Back's left edge and its middle's height.</summary>
            public Vector2? Back { get; }

            /// <summary>Next's right edge and its middle's height.</summary>
            public Vector2? Pager { get; }

            public static Places Of(PanelFrame frame)
            {
                var close = frame.ButtonFor(PanelModel.Close);
                var back = frame.Back;
                var end = frame.RightEnd;
                var rightEnd = end != null ? new Vector2(RectOf(end).xMax, RectOf(end).center.y) : (Vector2?)null;
                var backRect = back != null ? RectOf(back) : (Rect?)null;
                // A confirmation's pager stands at the top, away from Yes (PagerOnTop); every other screen's at the body's bottom right.
                var next = frame.Shown?.Confirm == null ? frame.NextPage : null;
                return new Places(close != null ? new Vector2(RectOf(close).xMax, RectOf(close).center.y) : (Vector2?)null, rightEnd,
                    backRect.HasValue ? new Vector2(backRect.Value.xMin, backRect.Value.center.y) : (Vector2?)null,
                    next != null ? new Vector2(RectOf(next).xMax, RectOf(next).center.y) : (Vector2?)null);
            }
        }

        /// <summary>Navigation and the primary never move: Close, the bar's right end, Back and the pager stand in the same place on every screen.</summary>
        private static IEnumerable<string> PlacesHold(string name, List<(string Screen, Places Places)> screens)
        {
            const float tolerance = 1e-4f;
            IEnumerable<string> Same(string part, Func<Places, Vector2?> of)
            {
                var first = screens.FirstOrDefault(screen => of(screen.Places).HasValue);
                if (first.Screen == null) yield break;
                var at = of(first.Places)!.Value;
                foreach (var (screen, places) in screens)
                {
                    var here = of(places);
                    if (here.HasValue && (Mathf.Abs(here.Value.x - at.x) > tolerance || Mathf.Abs(here.Value.y - at.y) > tolerance))
                    {
                        yield return name + " " + screen + ": " + part + " stands somewhere else than on " + first.Screen + ".";
                    }
                }
            }
            return Same("Close", places => places.Close)
                .Concat(Same("the bar's right end", places => places.RightEnd))
                .Concat(Same("Back", places => places.Back))
                .Concat(Same("the pager", places => places.Pager));
        }

        /// <summary>While a confirmation pages through what it confirms, its pager stands under the header, above the body, away from Yes.</summary>
        private static IEnumerable<string> PagerOnTop(PanelFrame frame, string what)
        {
            var next = frame.NextPage;
            if (next == null) yield return what + ": the confirmation pages but shows no pager.";
            else if (RectOf(next).yMin < frame.CustomBody.yMax - 1e-4f) yield return what + ": the confirmation's pager does not stand above the body.";
        }

        /// <summary>The whole panel, the eyes turned to its middle, a little wider than the panel: to see every edge, not to judge size.</summary>
        private static Texture2D Whole(Camera camera, RenderTexture texture, Transform panel)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(panel.position - camera.transform.position, Vector3.up);
            camera.fieldOfView = PanelFrame.WidthDegrees + 6f;
            var image = WorkspaceRender.Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>A button's outline in the frame's units.</summary>
        private static Rect RectOf(GlazeButton button)
        {
            var place = button.transform.localPosition;
            return new Rect(place.x - button.Size.x / 2f, place.y - button.Size.y / 2f, button.Size.x, button.Size.y);
        }

        /// <summary>12 mm at the panel's distance, in its units.</summary>
        private static float TargetGap(PanelFrame frame) => Glaze.TargetGapMeters / PanelFrame.Distance;

        /// <summary>Two outlines at least <paramref name="gap"/> apart one way or the other.</summary>
        private static bool Apart(Rect a, Rect b, float gap)
        {
            var dx = Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax);
            var dy = Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax);
            return dx >= gap * 0.99f || dy >= gap * 0.99f;
        }

        /// <summary>
        /// The whole request, page by page, for ideas at their hardest: the longest name and task in
        /// one unbroken word, a task made only of characters shown as code points, a task with no place
        /// to break, and a long task in ordinary words. Every character of every item shows exactly
        /// once across the pages, inside the space above the page buttons; an item that fits a page is
        /// never split; a line breaks inside a word only where the word is longer than a line; and Yes,
        /// start building shows on the last page only.
        /// </summary>
        private static IEnumerable<string> ReviewShowsEverything(string name, string folder, Camera camera, RenderTexture texture, EntryPanel panel, ClientProjection state)
        {
            var failures = new List<string>();
            var cases = new (string Suffix, string Name, string Task)[]
            {
                ("longest", new string('P', ProjectIdea.NameLimit), new string('W', ProjectIdea.TaskLimit)),
                ("code-points", "Recipe tracker", string.Concat(Enumerable.Repeat("\u202E\u200B\u0003\u2066", 250))),
                ("unbroken", "Recipe tracker", new string('x', ProjectIdea.TaskLimit)),
                ("words", "Recipe tracker", string.Join(" ", Enumerable.Repeat("Track recipes, plan the week's dinners and write the shopping list.", 55))),
            };
            foreach (var (suffix, projectName, task) in cases)
            {
                var what = name + " review " + suffix;
                var idea = new ProjectIdea();
                idea.UseIdea(task);
                idea.Rename(projectName);
                idea.ChooseFolder(ProjectFolder.New(Listing(false).Roots[0], "recipe-tracker"));
                // As the person reaches it: the recap, then Start building.
                panel.ShowForRender(EntryPanel.Screen.Recap, idea, Draft(state, listed: true));
                panel.PressForRender(EntryScreens.StartBuilding);
                var review = panel.Review;
                if (review == null || !review.Paginated)
                {
                    failures.Add(what + ": the review shows no request.");
                    continue;
                }
                var shown = review.Items.Select(_ => new StringBuilder()).ToList();
                for (var page = 0; page < review.PageCount; page++)
                {
                    WorkspaceRender.ForceMeshes(panel.gameObject);
                    var labels = panel.RequestLabels;
                    var parts = review.Parts;
                    if (labels.Count != parts.Count) failures.Add(what + ": page " + (page + 1) + " shows " + labels.Count + " labels for " + parts.Count + " items.");
                    for (var index = 0; index < parts.Count && index < labels.Count; index++)
                    {
                        var part = parts[index];
                        var label = labels[index];
                        label.ForceMeshUpdate(true);
                        var info = label.textInfo;
                        var lines = info.lineCount;
                        var bottom = float.MaxValue;
                        for (var character = 0; character < info.characterCount; character++)
                        {
                            var each = info.characterInfo[character];
                            shown[part.Item].Append(each.character);
                            if (each.isVisible) bottom = Mathf.Min(bottom, label.transform.parent.InverseTransformPoint(label.transform.TransformPoint(each.bottomLeft)).y);
                        }
                        if (index > 0 && label.rectTransform.localPosition.y > labels[index - 1].rectTransform.localPosition.y - (parts[index - 1].Lines - 0.01f) * LinePitch(labels[index - 1]))
                        {
                            failures.Add(what + ": item " + part.Item + " overlaps the item above it on page " + (page + 1) + ".");
                        }
                        if (lines != part.Lines) failures.Add(what + ": item " + part.Item + " takes " + lines + " lines on page " + (page + 1) + " where " + part.Lines + " were measured.");
                        if (bottom < panel.Frame.CustomBody.yMin - 1e-4f) failures.Add(what + ": item " + part.Item + " runs below the page on page " + (page + 1) + ".");
                        failures.AddRange(BreaksBetweenWords(label, what + " item " + part.Item));
                    }
                    var confirming = panel.Frame.ButtonFor(EntryScreens.ConfirmStart) is GlazeButton yes && yes.Available
                        && yes.Label.text == LabelText.ForTextMeshPro(EntryText.ConfirmStart);
                    if (confirming != review.CanConfirm) failures.Add(what + ": Yes, start building " + (confirming ? "shows before" : "does not show on") + " the last page.");
                    if (review.PageCount > 1) failures.AddRange(PagerOnTop(panel.Frame, what + " page " + (page + 1)));
                    // Every part stepped through with Next first, so Yes keeps clear of the pager on every part too.
                    if (review.CanConfirm) failures.AddRange(WorkspaceRender.YesClear(panel.Frame, what));
                    if (page == 0 && suffix == "words" && name == "far")
                    {
                        var closeUp = WorkspaceRender.CloseUp(camera, texture, panel.Root);
                        File.WriteAllBytes(Path.Combine(folder, name + "-review-words-closeup.png"), closeUp.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(closeUp);
                    }
                    if (page == 0 || page == review.PageCount - 1)
                    {
                        // The first and last part of each hard case, the whole panel, to see the pager and Yes.
                        var whole = Whole(camera, texture, panel.Root);
                        File.WriteAllBytes(Path.Combine(folder, name + "-review-" + suffix + "-" + (page + 1).ToString(CultureInfo.InvariantCulture) + "-panel.png"), whole.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(whole);
                    }
                    if (page < review.PageCount - 1) panel.PressForRender(PanelModel.NextPart);
                }
                for (var item = 0; item < review.Items.Count; item++)
                {
                    if (shown[item].ToString() != review.Items[item].Text)
                    {
                        failures.Add(what + ": item " + item + " shows " + shown[item].Length + " of its " + review.Items[item].Text.Length + " characters across the pages, or not in order.");
                    }
                }
                Debug.Log("Halcyonic: entry render " + what + ": " + review.Items.Sum(item => item.Text.Length) + " characters in " + review.PageCount + " pages, each shown once.");
            }
            return failures;
        }

        /// <summary>The distance between the baselines of a label's first two lines, or one line's height when it has one.</summary>
        private static float LinePitch(TMP_Text label)
        {
            var info = label.textInfo;
            return info.lineCount > 1 ? info.lineInfo[0].baseline - info.lineInfo[1].baseline : info.lineInfo[0].lineHeight;
        }

        /// <summary>
        /// Where a label wraps, it wraps between words, unless the word it breaks is wider than a whole
        /// line and so has to be broken somewhere.
        /// </summary>
        private static IEnumerable<string> BreaksBetweenWords(TMP_Text label, string what)
        {
            var info = label.textInfo;
            var width = label.rectTransform.sizeDelta.x;
            for (var line = 0; line + 1 < info.lineCount; line++)
            {
                var last = info.lineInfo[line].lastCharacterIndex;
                var next = info.lineInfo[line + 1].firstCharacterIndex;
                if (info.characterInfo[last].character == ' ' || info.characterInfo[next].character == ' ') continue;
                // The word broken here, whole.
                var text = new StringBuilder();
                var from = last;
                while (from > 0 && info.characterInfo[from - 1].character != ' ') from--;
                for (var character = from; character < info.characterCount && info.characterInfo[character].character != ' '; character++) text.Append(info.characterInfo[character].character);
                if (label.GetPreferredValues(text.ToString().Replace("\\", "\\\\")).x > width) continue;
                yield return what + ": a line breaks inside the word " + text + ", which fits a line.";
                yield break;
            }
        }

        internal static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;

        /// <summary>World bounds on the render, as a rectangle.</summary>
        internal static Rect ScreenBounds(Camera camera, Bounds bounds)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                var screen = camera.WorldToScreenPoint(point);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// How many pixels differ between two renders inside the panel's own outline on the render, a
        /// little in from its rounded edge, and how many were compared. A panel turned toward the eyes
        /// projects as a quadrilateral, whose bounding box would also hold pixels beside it.
        /// </summary>
        private static (int Changed, int Compared) ChangedInPanel(Texture2D a, Texture2D b, Camera camera, PanelFrame frame)
        {
            const float inset = 0.03f;
            var quad = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) }
                .Select(corner => (Vector2)camera.WorldToScreenPoint(frame.transform.TransformPoint(new Vector3(
                    corner.x * (frame.Size.x / 2f - inset), corner.y * (frame.Size.y / 2f - inset), 0f))))
                .ToArray();
            var minX = Mathf.Clamp(Mathf.FloorToInt(quad.Min(point => point.x)), 0, Size);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(quad.Max(point => point.x)), 0, Size);
            var minY = Mathf.Clamp(Mathf.FloorToInt(quad.Min(point => point.y)), 0, Size);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(quad.Max(point => point.y)), 0, Size);
            var changed = 0;
            var compared = 0;
            for (var y = minY; y < maxY; y++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    if (!Inside(quad, new Vector2(x + 0.5f, y + 0.5f))) continue;
                    compared++;
                    var p = a.GetPixel(x, y);
                    var q = b.GetPixel(x, y);
                    if (Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b))) > 2f / 255f) changed++;
                }
            }
            return (changed, compared);
        }

        /// <summary>Whether a point lies inside a convex quadrilateral, whichever way round its corners go.</summary>
        private static bool Inside(Vector2[] quad, Vector2 point)
        {
            var sign = 0f;
            for (var index = 0; index < quad.Length; index++)
            {
                var from = quad[index];
                var to = quad[(index + 1) % quad.Length];
                var cross = (to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x);
                if (Mathf.Abs(cross) < 1e-6f) continue;
                if (sign == 0f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return false;
            }
            return true;
        }
    }
}
