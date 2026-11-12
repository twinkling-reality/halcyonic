#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuDirector
    {
        private readonly Dictionary<FileColumn, (bool Separate, float ContextHeight, bool Portrait, int TitleRows, int ProvenanceRows, bool Kit)> composerLayouts
            = new Dictionary<FileColumn, (bool, float, bool, int, int, bool)>();
        private readonly Dictionary<FileColumn, bool> composerFocus = new Dictionary<FileColumn, bool>();
        private bool lastSeparateWorkspace;
        private void DropSpatialComposer(FileColumn file) { composerLayouts.Remove(file); composerFocus.Remove(file); }

        /// <summary>The last measured Activity and composer pair decision, for the renders: field, readable text bands, clearance, and the bands' least em in degrees.</summary>
        public (bool Inside, bool Readable, bool Clear, float LeastEm)? PairDecisionForRender { get; private set; }
        private object? contextOwner;
        private RealtimeSession? contextSession;
        private ClientProjection? contextState;
        private string? contextJournal, contextConnection, contextTask, contextProject;
        private (string Id, string Name)[] contextChoices = Array.Empty<(string, string)>();
        // At Larger, the two-column workspace leaves no shelf candidate inside the Quest 3S field; the chooser column steps
        // aside when a character stands on the stage or a task file offers shelf placement or character colour (LARGER-SHELF call (a)).
        private bool ShelfYieldsChooserAtLarger => TextSize == TextSize.Larger
            && (setup.StageNow().Characters.Any(character => character != null)
                || navigator.Beside is FileColumn file && (file.HasShowTaskHere || !setup.Presentation.AgentsVisible && CurrentColourSource(out _)));
        // Under the redesign New Project's steps and the agent's page stand in the workspace too, where FIELD's readers draw
        // them (RoomBar.Frames); otherwise the older single column drew them and the readers never ran (ON-1 census).
        private bool SeparateWorkspace => setup.SpatialWorkspace && placement == null && !ShelfYieldsChooserAtLarger
            && (navigator.Beside is FileColumn || navigator.Beside is TaskResultsColumn || navigator.Beside is WorkspaceRequestColumn
                || RoomDesign.Enabled && (navigator.Beside is NewProjectFlow || navigator.Beside is AgentColumn)
                || navigator.Beside == null && (navigator.Place == MenuPlace.Projects || navigator.Place == MenuPlace.Tasks
                    // The menu (A4) under the redesign, where its reader draws it (2026-11-12 census); a page it has no reader for
                    // still falls back to the older view in the workspace, as Projects' does.
                    || RoomDesign.Enabled && navigator.Place == MenuPlace.Usage));

        private void ConfigureSpatialComposer()
        {
            var separate = SeparateWorkspace;
            plane.SeparateWorkspace = separate;
            if (separate != lastSeparateWorkspace)
            {
                lastSeparateWorkspace = separate;
                if (navigator.Shown is ProjectsColumn projects) projects.Reflow();
            }
            if (!setup.SpatialWorkspace || navigator.Beside is not FileColumn file) return;
            var contextHeight = plane.Context.HeightFor(State?.Projects.Count ?? 0);
            var titleRows = navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true
                ? MenuFrameView.IndependentTitleRows(work.Title, Glaze.Menu.FileColumnDegrees) : 2;
            var provenanceRows = MenuFrameView.IndependentProvenanceRows(CurrentSpatialMarks(), Glaze.Menu.FileColumnDegrees);
            // The kit lays the file out by its own rows while it draws it (D3.4): Activity's page the rail's rows, the
            // composer's the rows its well shows, always the pair; the file's views by their measured heights otherwise.
            var geometry = (separate, contextHeight, HasTaskPortrait, titleRows, provenanceRows, KitDraws(file));
            if (composerLayouts.TryGetValue(file, out var previous))
            {
                if (previous != geometry) { composerLayouts[file] = geometry; file.Reflow(); }
                return;
            }
            composerLayouts[file] = geometry;
            // While the redesign draws this file, a part of what a confirmation reads is eight of the panel's own lines.
            file.RequestPartRowsOf = () => RoomDesign.Enabled && KitDraws(file) ? RoomBar.RequestLines : (int?)null;
            // And a question is laid for it: every word whole, four choices a page.
            file.QuestionBudgetOf = () => RoomDesign.Enabled && KitDraws(file) ? new ChoiceBudget(RoomBar.ChoicesAPage) : null;
            // Each retained file keeps its own callbacks and geometry lifetime. Background rebuilds
            // must never borrow another selected task's provenance or portrait allocation.
            file.ConfigureActivityComposer(
                () => KitDraws(file) ? new RowBudget(plane.Kit.RailRows) : SeparateWorkspace ? new HeightBudget(IndependentPageHeight(OwnerTitleRows(file), true, false, file)) : null,
                () =>
                {
                    if (!KitDraws(file))
                        return new HeightBudget(SeparateWorkspace
                            ? IndependentPageHeight(OwnerTitleRows(file), true, true, file)
                            : PageHeight(TitleRows(file.Now?.Character.Title ?? "", Glaze.Menu.FileColumnDegrees), besideMenu: false, focused: true));
                    composerFocus[file] = false;
                    // The redesign's panel holds a draft of eight of its lines whole; a longer one stands in parts the column turns.
                    return KitFileBinding.ComposerBudget(RoomDesign.Enabled ? RoomBar.RequestLines : plane.Kit.ComposerRows);
                });
        }

        /// <summary>Whether the kit draws <paramref name="file"/> now, as it last drew it in the plane's place.</summary>
        /// <summary>
        /// Under the redesign a task's file stands on a section the redesign draws (the census, JUDGE 2026-11-11), before the
        /// draw asks for its frames, so the first frame drawn is already the right one:
        /// - Checks, for a task at rest with a check of the computer's own: the file stands at its Activity and the result's
        ///   page turns to Checks (the Checks card), whatever turned it to Checks.
        /// - Waiting with nothing waiting and nothing armed (an answered request still showing): the file stands at its Activity.
        /// </summary>
        private void SectionsForRedesign()
        {
            if (!RoomDesign.Enabled || !SeparateWorkspace || navigator.IsCompact || navigator.Beside is not FileColumn file
                || file.Now is not WorkspacePresentation now) return;
            var section = file.Screen.Section;
            var checkedByComputer = false;
            if (section == FileSection.Checks && KitPresenter.AtRest(now))
            {
                var projectId = State?.Workstreams.TryGetValue(now.Character.WorkstreamId, out var work) == true ? work!.ProjectId : null;
                checkedByComputer = ResultOf(now, projectId)?.Steps.Count > 0;
            }
            if (KitPresenter.RedesignSection(section, now, file.Steering.Armed != null, checkedByComputer) is not FileSection turned) return;
            file.ShowSection(turned);
            if (section == FileSection.Checks)
                setup.Presentation.ShowResultPage(now.Character.WorkstreamId, now.Execution?.ExecutionId, RoomResultPage.Checks);
        }

        private bool KitDraws(FileColumn file) => plane.KitShows && ReferenceEquals(navigator.Beside, file);

        /// <summary>
        /// The kit bound to <paramref name="file"/>'s frame (D3.4), its spine exactly the context column's offers in its words
        /// (WorkspaceContextView): the connection state under Projects, the same up to three projects, More projects past
        /// three, Tasks, and Workspace at its foot. Null where the kit is off or can't draw the frame.
        /// </summary>
        /// <summary>
        /// For the redesign: the frame of the column beside the menu, where the task's binding has none for it (a page of
        /// the file with no place of its own, or another column's frame, as New Project's steps) and a reader of the
        /// redesign takes it (<see cref="RoomBar.OfFrame"/>). Null otherwise, and the older view draws the frame.
        /// </summary>
        private KitFileBinding? PlainBindingOf(MenuFrame? menuFrame = null)
        {
            var room = KitWorkspaceHost.Enabled && RoomDesign.Enabled && SeparateWorkspace && !navigator.IsCompact;
            // New Project's review is cut by the redesign's own lines where the redesign draws it, eight a part, so a part
            // shows exactly its lines; for the older page it is cut as ever (FIELD's flow, NewProjectFlow.RoomLines).
            if (navigator.Beside is NewProjectFlow flow) flow.RoomLines = room ? (roomLines ??= words => plane.Kit.Bar.Lines(words, false)) : null;
            // With nothing beside the menu (the Projects place, the launch screen), the menu's own frame is the one a reader takes.
            var frame = navigator.Beside != null ? navigator.Beside.Frame : menuFrame;
            if (!room || frame == null) return null;
            return RoomBar.OfFrame(frame, VoiceStage.Idle) != null ? KitFileBinding.PlainOf(frame) : null;
        }

        /// <summary>
        /// The agent's page (FIELD's <see cref="AgentColumn"/>) opened from the task's file: the agent and model the next
        /// start would use, the level the project keeps, and every agent and model to choose from. A pick is kept for the
        /// project and the page closes back to the file. An instruction armed meanwhile lapses where the choice changed
        /// (<see cref="WorkspaceSteering.ChoiceNow"/>), so the level shown before a start is the level sent.
        /// </summary>
        private bool OpenAgentPage(MenuFrame? frame)
        {
            if (navigator.Taking(MenuColumn.File, AgentColumn.Open, frame, null) is not FileColumn file || file.Now is not WorkspacePresentation now
                || State?.Workstreams.TryGetValue(now.Character.WorkstreamId, out var work) != true || plane.KitBinding is not { Plain: false } binding) return false;
            var projectId = work!.ProjectId;
            var task = now.Character.WorkstreamId;
            AgentOffer? Offer()
            {
                var current = CurrentKitRuntime(file.Now);
                var execution = file.Now?.Execution;
                var modelRef = setup.Presentation.SelectedModelRef
                    ?? (execution != null && execution.Runtime.RuntimeId == current?.RuntimeId ? execution.ModelRef : null);
                return AgentOffer.Of(binding.Model.Agent, State?.Runtimes, setup.Presentation.ModelsForRuntime, current?.RuntimeId, modelRef,
                    setup.Presentation.NeverAskFor(projectId));
            }
            var page = new AgentColumn(Offer, neverAsk => setup.Presentation.ChooseNeverAsk(projectId, neverAsk), key =>
            {
                var (runtimeId, modelRef) = AgentOffer.Picked(key);
                setup.Presentation.SelectRuntime(runtimeId, modelRef);
            });
            page.Closed += () => { if (ReferenceEquals(navigator.Beside, page)) { navigator.ShowBeside(file, task); ViewsChanged(); } };
            navigator.ShowBeside(page, task);
            ViewsChanged();
            return true;
        }

        private Func<LineWords, int>? roomLines;

        private KitFileBinding? KitBindingOf(FileColumn? file, IReadOnlyList<ProjectView> choices, string? selected)
        {
            if (!KitWorkspaceHost.Enabled || file?.Frame is not MenuFrame frame || file.Now is not WorkspacePresentation now) return null;
            var projects = choices.Take(3).Select(project => new KitRowModel("project:" + project.ProjectId, LineWords.TheirName(project.Name),
                opensMore: false, selected: project.ProjectId == selected, isName: true)).ToList();
            if (choices.Count > 3) projects.Add(new KitRowModel("place:" + MenuPlace.Projects, LineWords.Ours("More projects")));
            var spine = new[]
            {
                new KitSpineSection(projects, "Projects", note: plane.ConnectionState),
                new KitSpineSection(new[] { new KitRowModel("place:" + MenuPlace.Tasks, LineWords.Ours("Tasks")) }),
            };
            var name = selected != null && State?.Projects.TryGetValue(selected, out var project) == true ? project.Name : "";
            // While the run is active, Send instructs this execution, so the chips are its runtime's, locked (ADR 0033; AUDIT,
            // 2026-11-03). At rest, Send starts the next run on the person's choice, so the chips are that choice (SWITCH).
            var chosen = setup.Presentation.SelectedRuntimeId is string chosenId ? State?.Runtimes.FirstOrDefault(item => item.RuntimeId == chosenId) : null;
            var selectors = KitPresenter.ChipsOf(now, setup.Presentation.ExecutionModelDisplay(now.Runtime, now.Execution), chosen,
                chosen != null ? setup.Presentation.SelectedModelDisplay(chosen, now.Execution) : null, State?.Runtimes,
                neverAskChosen: setup.Presentation.NeverAskFor(selected));
            return KitFileBinding.Of(frame, file.Steering.Armed, now, file.Screen.Instruction.Open ? file.Screen.Instruction.Words : "",
                setup.Session()?.Status ?? ConnectionStatus.Stopped, name, spine, new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move),
                selectors: selectors, roomDesign: RoomDesign.Enabled, draftSpoken: file.Screen.Instruction.Open && file.Screen.Instruction.Spoken,
                requestRows: file.Steering.ToRead(now) is string read && file.Screen.Measured(file.Steering, read) ? file.Screen.RequestRows : (int?)null,
                chooses: file.ChoosesInstruction,
                asking: file.Screen.Question.Draft != null && !file.Screen.Question.Reviewing ? file.Screen.Question.Prompt : (int?)null,
                writing: file.Screen.Question.Writing ? file.Screen.Question.Input : null, result: ResultOf(now, selected));
        }

        /// <summary>
        /// For the redesign: what the computer knows of the task at rest (<see cref="RoomResult"/>), from its project's last
        /// check and the last turn of its run the computer observed, at the page the person opened; null with no project.
        /// </summary>
        private RoomResult? ResultOf(WorkspacePresentation now, string? projectId)
        {
            if (!RoomDesign.Enabled || projectId == null || State?.Projects.TryGetValue(projectId, out var project) != true) return null;
            var run = now.Execution?.ExecutionId;
            var (page, part) = setup.Presentation.ResultPageFor(now.Character.WorkstreamId, run);
            var task = State!.Workstreams.TryGetValue(now.Character.WorkstreamId, out var work) ? work : null;
            var target = ControlPlaneSettings.Target();
            return RoomResult.Of(project, State, run != null ? setup.Observed(run).LastOrDefault() : null, page, part, (target, Demonstration), task,
                page == RoomResultPage.History ? HistoryFor(projectId) : null, page == RoomResultPage.Share ? ShareFor(projectId) : null,
                canSend: CanSendShare, relays: HeadsetRelay.Serves(target), relayBuilt: HeadsetRelay.Built);
        }

        /// <summary>
        /// A press on one of the result's own actions (<see cref="RoomResult.Actions"/>), taken as any press of the file's is:
        /// only from the file's frame as drawn and still standing. Changes, Checks, Next Page and Back turn the page shown;
        /// Make VR Version and Try Build go the way the project's screen sends them (<see cref="OnScreenActed"/>).
        /// </summary>
        private bool OnResultActed(string action, string? key, MenuFrame? frame)
        {
            if (navigator.Taking(MenuColumn.File, action, frame, null) is not FileColumn file || file.Now is not WorkspacePresentation now) return false;
            var task = now.Character.WorkstreamId;
            var run = now.Execution?.ExecutionId;
            if (State?.Workstreams.TryGetValue(task, out var work) != true) return false;
            var (page, part) = setup.Presentation.ResultPageFor(task, run);
            switch (action)
            {
                case RoomResult.ShowChanges: setup.Presentation.ShowResultPage(task, run, RoomResultPage.Changes); break;
                case RoomResult.ShowChecks: setup.Presentation.ShowResultPage(task, run, RoomResultPage.Checks); break;
                case RoomResult.NextPage: setup.Presentation.ShowResultPage(task, run, page, part + 1); break;
                case RoomResult.Back: setup.Presentation.ShowResultPage(task, run, RoomResultPage.Result); break;
                case RoomResult.MakeVrVersion: OnScreenActed(KitScreenAction.MakeAVrVersion, work!.ProjectId); break;
                case RoomResult.TryBuild: OnScreenActed(KitScreenAction.TryIt, work!.ProjectId); break;
                case RoomResult.PlayInHeadset: PlayInHeadset(work!.ProjectId); break;
                case RoomResult.ShowAdd: setup.Presentation.ShowResultPage(task, run, RoomResultPage.Add); break;
                case RoomResult.ShowConflict: setup.Presentation.ShowResultPage(task, run, RoomResultPage.Conflict); break;
                // A13: the add or the fit, sent only while the copy still stands as the page showed it; the page returns to the
                // result, which says what the host recorded once it does (ADR 0045: the host refuses a copy changed since).
                // The add sends the digest of the review the person was shown, the confirm's own key; a copy read again since is
                // refused by the host (review_stale, changed_since_review) and the page shows it again.
                case RoomResult.AddConfirm when key != null && RoomAdd.Of(work!.Copy, working: false).State == RoomAddState.Ready:
                    if (!SendCommand(setup.Commands.AddToProject(task, key))) return false;
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.Result);
                    break;
                case RoomResult.Fit when RoomAdd.Of(work!.Copy, working: false).State == RoomAddState.Conflict:
                    if (!SendCommand(setup.Commands.FitToProject(task))) return false;
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.Result);
                    break;
                // DEVICE's History (gap 2): the versions read on open, a review read on each pick, Restore sent only through
                // its lock and with the digest of the review shown; Changes and Close leave it.
                case RoomResult.ShowHistory:
                    OpenHistory(work!.ProjectId);
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.History);
                    break;
                case RoomResult.HistoryVersion or RoomResult.HistoryUndo or RoomResult.HistoryAgain
                    when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn picking && key != null && picking.Choose(key):
                    ReadPreview(picking);
                    break;
                case RoomResult.HistoryOlder when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn older:
                    if (!older.Older()) return false;
                    break;
                case RoomResult.HistoryRestore when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn restoring:
                    if (!restoring.Armed)
                    {
                        if (!restoring.Arm()) return false;
                        break;
                    }
                    if (!Connected || Demonstration || setup.Session() is not RealtimeSession session) return false;
                    if (setup.Commands is not CommandFactory commands || restoring.Restore(commands) is not ProjectRestoreCommand restore) return false;
                    setup.Presentation.RecordCommandDispatched(restore);
                    _ = setup.Submit(session, restore);
                    break;
                case RoomResult.HistoryCancel when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn cancelling:
                    cancelling.Cancel();
                    break;
                case RoomResult.HistoryTryAgain when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn again:
                    again.Reread();
                    ReadVersions(again);
                    break;
                case RoomResult.HistoryBack when page == RoomResultPage.History && HistoryFor(work!.ProjectId) is HistoryColumn back:
                    if (!back.Back()) CloseHistory(work!.ProjectId, task, run, RoomResultPage.Changes);
                    break;
                case RoomResult.HistoryChanges when page == RoomResultPage.History:
                    CloseHistory(work!.ProjectId, task, run, RoomResultPage.Changes);
                    break;
                case RoomResult.HistoryClose when page == RoomResultPage.History:
                    CloseHistory(work!.ProjectId, task, run, RoomResultPage.Result);
                    break;
                // DEVICE's Share (ADR 0054): the ways read on open; Share sent only through its lock and only where the host
                // would admit it; Stop sent at once; the code read once the share stands; the fixes go the project's way.
                case RoomResult.ShowShare:
                    if (!OpenShare(work!.ProjectId)) return false;
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.Share);
                    break;
                case RoomResult.ShareWayAction when page == RoomResultPage.Share && ShareFor(work!.ProjectId) is ShareColumn choosing
                    && Enum.TryParse<ShareWay>(key, out var way) && choosing.Choose(way):
                    break;
                case RoomResult.ShareStart when page == RoomResultPage.Share && ShareFor(work!.ProjectId) is ShareColumn starting:
                    if (!starting.Armed)
                    {
                        if (!starting.Arm()) return false;
                        break;
                    }
                    // What SendShare checks is checked first, as Restore does: nothing is built that cannot be sent (AUDIT reread MUST 1).
                    if (!CanSendShare || setup.Commands is not CommandFactory startCommands || starting.Start(startCommands) is not ProjectShareStartCommand start) return false;
                    if (!SendShare(start)) return false;
                    break;
                case RoomResult.ShareStop when page == RoomResultPage.Share && ShareFor(work!.ProjectId) is ShareColumn stopping:
                    if (!CanSendShare || setup.Commands is not CommandFactory stopCommands || stopping.Stop(stopCommands) is not ProjectShareStopCommand stop) return false;
                    if (!SendShare(stop)) return false;
                    stopping.StopSent();
                    break;
                case RoomResult.ShareCancel when page == RoomResultPage.Share && ShareFor(work!.ProjectId) is ShareColumn cancelling:
                    cancelling.Cancel();
                    break;
                case RoomResult.ShareAgain when page == RoomResultPage.Share && ShareFor(work!.ProjectId) is ShareColumn again:
                    again.Again();
                    ReadWays(again);
                    break;
                case RoomResult.ShareCheckGame when page == RoomResultPage.Share:
                    // The project's own way to a check, as Try Build's screen sends it; Share reads the new facts from the view.
                    if (setup.Commands is not CommandFactory checkCommands || !SendShare(checkCommands.CheckProject(work!.ProjectId))) return false;
                    break;
                case RoomResult.ShareStartPreview when page == RoomResultPage.Share:
                    if (setup.Commands is not CommandFactory previewCommands || !SendShare(previewCommands.StartPreview(work!.ProjectId))) return false;
                    break;
                case RoomResult.ShareBack or RoomResult.ShareClose when page == RoomResultPage.Share:
                    // A column with a start under way or a stop to send is kept, so nothing is dropped (AUDIT reread MUST 2).
                    if (ShareFor(work!.ProjectId) is not { KeepAfterLeaving: true }) shares.Remove(work!.ProjectId);
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.Result);
                    break;
                default: return false;
            }
            dirty = true;
            return true;
        }

        private RuntimeDescriptor? CurrentKitRuntime(WorkspacePresentation? now = null)
        {
            if (setup.Presentation.SelectedRuntimeId is string selectedId
                && State?.Runtimes.FirstOrDefault(item => item.RuntimeId == selectedId) is RuntimeDescriptor chosen)
            {
                return chosen;
            }
            return (now ?? (navigator.Beside as FileColumn)?.Now)?.Runtime;
        }

        private static int OwnerTitleRows(FileColumn owner) => owner.Now?.Character is CharacterPresentation character
            ? MenuFrameView.IndependentTitleRows(character.Title, Glaze.Menu.FileColumnDegrees) : 2;

        private void PrepareSpatialContext(MenuFrame? menuFrame = null)
        {
            var activeFile = SeparateWorkspace && !navigator.IsCompact ? navigator.Beside as FileColumn : null;
            plane.ComposerFocused = activeFile?.ActivityContextFrame != null && composerFocus.TryGetValue(activeFile, out var focused) && focused;
            plane.ActivityFrame = plane.ComposerFocused ? null : activeFile?.ActivityContextFrame;
            var choices = State?.Projects.Values.OrderBy(project => project.Name, StringComparer.Ordinal).ThenBy(project => project.ProjectId, StringComparer.Ordinal).ToList()
                ?? new List<ProjectView>();
            // The selected project remains among the bounded choices even when it sorts after them.
            var selected = memory.SelectedProjectId;
            if (navigator.BesideTask is string selectedTask && State?.Workstreams.TryGetValue(selectedTask, out var selectedWork) == true)
                selected = selectedWork.ProjectId;
            // The project's kept agent app and model become the choice for its tasks' next starts (kept across restarts).
            setup.Presentation.UseProject(selected);
            if (selected != null && choices.FindIndex(project => project.ProjectId == selected) >= 3)
            {
                var current = choices.Single(project => project.ProjectId == selected); choices.Remove(current); choices.Insert(0, current);
            }
            var identities = choices.Select(project => (project.ProjectId, project.Name)).ToArray();
            var connection = TryKey(out var offeredConnection) ? offeredConnection : null;
            if (contextOwner == null || !ReferenceEquals(contextSession, setup.Session()) || !ReferenceEquals(contextState, State)
                || contextJournal != State?.Journal?.JournalId || contextConnection != connection || contextTask != navigator.BesideTask
                || contextProject != selected || !contextChoices.SequenceEqual(identities))
            {
                contextOwner = new object(); contextSession = setup.Session(); contextState = State;
                contextJournal = State?.Journal?.JournalId; contextConnection = connection; contextTask = navigator.BesideTask;
                contextProject = selected; contextChoices = identities;
            }
            plane.ContextProjects = choices; plane.ContextSelectedProject = selected; plane.ContextBinding = contextOwner;
            plane.KitBinding = KitBindingOf(activeFile, choices, selected) ?? PlainBindingOf(menuFrame);
        }

        private bool ContextStanding(object owner)
        {
            // The context stands where it is drawn: the kit's spine while the kit draws the file, the context column otherwise.
            var standing = plane.KitShows
                ? plane.Kit.Settled && plane.Kit.Accepting() && ReferenceEquals(owner, plane.KitOwner)
                : plane.Context.gameObject.activeInHierarchy && plane.Context.Accepting() && ReferenceEquals(owner, plane.Context.Binding);
            return !Away && !dirty && drag == null && !plane.Repositioning && !plane.Sliding && standing
                && ReferenceEquals(owner, contextOwner)
                && ReferenceEquals(contextSession, setup.Session()) && ReferenceEquals(contextState, State)
                && contextJournal == State?.Journal?.JournalId && contextTask == navigator.BesideTask
                && contextConnection == (TryKey(out var connection) ? connection : null);
        }

        private void OnContextProject(string projectId, object owner)
        {
            if (!ContextStanding(owner) || !contextChoices.Take(3).Any(choice => choice.Id == projectId)
                || State?.Projects.ContainsKey(projectId) != true) return;
            // The deliberate project choice promotes its picker, even for the current project.
            // CloseBeside suspends the retained file; it does not drop it from MenuMemory.
            navigator.CloseBeside();
            OpenProjectTasks(projectId);
        }

        private void OnContextPlace(MenuPlace place, object owner)
        {
            if (!ContextStanding(owner)) return;
            if (place == MenuPlace.Tasks) navigator.CloseBeside();
            if (place == MenuPlace.Tasks && contextProject is string project) OpenProjectTasks(project);
            else if (place == MenuPlace.Tasks) OpenAllTasks();
            else if (place == MenuPlace.Projects) { navigator.CloseBeside(); Open(MenuPlace.Projects); }
        }

        private void OnContextWorkspace(object owner)
        {
            if (ContextStanding(owner)) OpenPlacement();
        }

        /// <summary>A row of the kit's spine, by its key, to the context column's own handlers, each behind <see cref="ContextStanding"/>.</summary>
        private void OnKitContext(string key, object owner)
        {
            const string Project = "project:";
            if (key.StartsWith(Project, StringComparison.Ordinal)) OnContextProject(key.Substring(Project.Length), owner);
            else if (key == "place:" + MenuPlace.Projects) OnContextPlace(MenuPlace.Projects, owner);
            else if (key == "place:" + MenuPlace.Tasks) OnContextPlace(MenuPlace.Tasks, owner);
            else if (key == "workspace") OnContextWorkspace(owner);
        }

        private bool KitStanding() => !Away && !dirty && drag == null && !plane.Repositioning && !plane.Sliding
            && plane.KitShows && plane.Kit.Settled && plane.Kit.Accepting();

        /// <summary>Host pressed on the kit's composer: it changes what runs only where the binding says a press can; a locked chip is a fact.</summary>
        private void OnKitEnvironmentPressed()
        {
            if (!KitStanding() || plane.KitBinding is not { Plain: false, Model: { EnvironmentChangeable: true } }) return;
            var current = CurrentKitRuntime();
            setup.Presentation.CycleEnvironment(State?.Runtimes, current?.RuntimeId);
            (navigator.Beside as FileColumn)?.Reflow();
        }

        private void OnKitProjectPressed()
        {
            if (!KitStanding()) return;
            navigator.CloseBeside();
            Open(MenuPlace.Projects);
        }

        private void OnKitAgentPressed()
        {
            if (!KitStanding() || plane.KitBinding is not { Plain: false, Model: { AgentChangeable: true } } || State?.Runtimes is not IReadOnlyList<RuntimeDescriptor> runtimes) return;
            var current = CurrentKitRuntime();
            var execution = (navigator.Beside as FileColumn)?.Now?.Execution;
            var currentModelRef = setup.Presentation.SelectedModelRef
                ?? (execution != null && execution.Runtime.RuntimeId == current?.RuntimeId ? execution.ModelRef : null);
            var cycled = setup.Presentation.CycleAgent(runtimes, current?.RuntimeId, currentModelRef);
            (navigator.Beside as FileColumn)?.Reflow();
            if (cycled is var (runtime, _)
                && runtime.ModelChoice == ModelChoice.Listed
                && setup.Presentation.ModelsForRuntime(runtime.RuntimeId).Count == 0
                && pendingRuntimeModelsRead == null
                && Connected && !Demonstration
                && setup.Api?.Invoke() is ControlPlaneApi api)
            {
                pendingRuntimeModelsRead = api.GetRuntimeModelsAsync(runtime.RuntimeId);
            }
        }

        /// <summary>The kit's compact presence stands in the closed bar's place, settled, so its Workspace is the bar's Open (OrganizationStanding).</summary>
        private bool KitCompactStands() => plane.KitCompactShows && plane.Kit.Settled;

        /// <summary>The presentation the existing routes confirmed, to both of the kit's switches: their thumbs move only on it.</summary>
        private void KitPresentationConfirmed() => plane.Kit.ShowConfirmed(setup.Presentation.Minimized);

        private IReadOnlyList<WorkMark> CurrentSpatialMarks() => navigator.BesideTask is string task
            && State?.Workstreams.TryGetValue(task, out var work) == true
            ? StateLanguage.MarksOf(CharacterPresenter.Present(work, State, Connected && !Demonstration)) : Array.Empty<WorkMark>();

        /// <summary>Measured union of the actual left navigation and central reading objects.</summary>
        private float IndependentPageHeight(int subjectRows, bool split, bool composer, FileColumn? owner = null)
        {
            var stage = setup.StageNow(); var at = anchor?.Eyes ?? stage.Eyes; var looking = anchor?.Looking ?? stage.Looking;
            var occupied = stage.Characters.Where(character => character != null).Select(character => WorkspaceLayout.InView(character, at)).ToList();
            var direction = new PanelDirection(UnityEngine.Mathf.Atan2(looking.x, looking.z) * UnityEngine.Mathf.Rad2Deg + moved.Yaw, moved.Elevation, true, false);
            var zoom = TextSize == TextSize.Larger ? Comfort.LargerTextScale : 1f;
            var width = PlaneComposition.Units(Glaze.Menu.FileColumnDegrees);
            var marks = owner == null ? CurrentSpatialMarks()
                : owner.Now?.Character is CharacterPresentation character ? StateLanguage.MarksOf(character) : Array.Empty<WorkMark>();
            var portrait = owner == null ? HasTaskPortrait : showTaskPortraits && owner.Now?.Character != null;
            var header = MenuFrameView.IndependentSubjectHeightOfRows(subjectRows, true, portrait,
                MenuFrameView.IndependentProvenanceRows(marks, Glaze.Menu.FileColumnDegrees));
            var leftHeight = plane.Context.HeightFor(State?.Projects.Count ?? 0);
            // The context column as WorkspaceContextView lays it, so the bands stand where it draws.
            var context = new UnityEngine.Vector2(GlazeTokens.Units(WorkspaceContextView.WidthDegrees), leftHeight);
            var section = GlazeButton.HeightOf(true);
            (bool Inside, bool Readable, bool Clear, float Width, float Height, float LeastEm) lastFit = default;
            bool Fits(float activity, float instruction, bool focused = false)
            {
                var pair = split && !focused;
                var heights = pair
                    ? new[] { header + MenuPage.Content(activity) - FooterView.Height - MenuPage.TargetGap,
                        FooterView.Height + MenuPage.TargetGap + MenuPage.Content(instruction) }
                    : new[] { header + (focused ? MenuPage.Words(1) : section) + FooterView.Height + MenuPage.TargetGap + MenuPage.Content(instruction) };
                var stacked = heights.Sum() + (heights.Length - 1) * PlaneComposition.Units(PlaneComposition.PartGapDegrees);
                var envelope = MenuPlane.IndependentEnvelope(UnityEngine.Mathf.Max(leftHeight, stacked), header, portrait);
                var composition = new PlaneComposition(new[] { new PlaneColumn(context.x + MenuPlane.PortraitClearance, envelope), new PlaneColumn(width, heights) }, zoom);
                var inside = MenuPage.Inside(composition, direction, ViewField.Current ?? MenuPage.Quest3S);
                // Text is measured where it can stand, at the parts' true offsets; the empty envelope
                // corners stay in the field and clearance checks but hold no body text.
                var parts = MenuPlane.IndependentParts(composition, context, heights);
                var bands = MenuPlane.IndependentTextBands(parts.Context, pair
                    ? new[] { (parts.Reading[0], false), (parts.Reading[1], true) }
                    : new[] { (parts.Reading[0], true) }, zoom);
                var readable = MenuPage.Readable(bands, zoom);
                var clear = WorkspacePlacement.Clears(direction, occupied, composition.Size);
                lastFit = (inside, readable, clear, composition.Width, composition.Height, MenuPage.LeastEm(bands, zoom));
                return inside && readable && clear;
            }
            if (!split)
            {
                float low = 0f, high = 2f;
                if (!Fits(0f, 0f)) return 0f;
                for (var step = 0; step < 40; step++) { var middle = (low + high) / 2f; if (Fits(0f, middle)) low = middle; else high = middle; }
                return low;
            }
            // FileInstruction.Measure removes these exact host lines/reason/pager terms before
            // allocating its input target. Reserve three rows up front, independent of typed length.
            var terms = new HeightBudget(0f);
            var paired = FitsHalf(FileScreens.InstructionPager(previous: true), Glaze.Menu.FileColumnDegrees)
                && FitsHalf(FileScreens.InstructionPager(), Glaze.Menu.FileColumnDegrees);
            var pager = paired ? terms.Target() : terms.Targets(1, 1);
            var minimumComposer = MenuPage.SourceLine + terms.Words(1) + terms.LineGap + terms.Reason
                + terms.Target(3) + pager + terms.TargetGap;
            var minimumActivity = MenuPage.SourceLine + terms.Words(2) + terms.LineGap;
            var together = Fits(minimumActivity, minimumComposer);
            PairDecisionForRender = (lastFit.Inside, lastFit.Readable, lastFit.Clear, lastFit.LeastEm);
#if UNITY_EDITOR
            UnityEngine.Debug.Log($"Halcyonic: independent measured allocation minActivity={minimumActivity:R}; minComposer={minimumComposer:R}; titleHeader={header:R}; pairFits={together}; inside={lastFit.Inside}; readable={lastFit.Readable}; leastEm={lastFit.LeastEm:R}; clear={lastFit.Clear}; width={lastFit.Width:R}; height={lastFit.Height:R}; pairedPager={paired}; textScale={zoom:R}.");
#endif
            if (owner != null) composerFocus[owner] = !together;
            if (!together)
            {
                // Same authoritative file/draft, explicitly labelled Editing instruction. Hide
                // composer is its return to Activity; passive context grants no draw credit.
                float low = 0f, high = 2f;
                if (!Fits(0f, 0f, focused: true)) return 0f;
                for (var step = 0; step < 40; step++) { var middle = (low + high) / 2f; if (Fits(0f, middle, focused: true)) low = middle; else high = middle; }
                return composer ? low : 0f;
            }
            var activityLow = minimumActivity; var activityHigh = 2f;
            for (var step = 0; step < 40; step++) { var middle = (activityLow + activityHigh) / 2f; if (Fits(middle, minimumComposer)) activityLow = middle; else activityHigh = middle; }
            return composer ? minimumComposer : activityLow;
        }

        public bool InspectMoment(string workstreamId) => !Away && setup.Presentation.InspectMoment(workstreamId);

        public bool CloseMoment() => !Away && setup.Presentation.CloseMoment(State);

        public bool ShowProjectScreen(string projectId, bool barOpen = false) =>
            !Away && State?.Projects.ContainsKey(projectId) == true && setup.Presentation.ShowProjectScreen(projectId, barOpen);

        public bool SetProjectScreenBarOpen(bool open) => !Away && setup.Presentation.SetScreenBarOpen(open);

        public bool CloseProjectScreen() => !Away && setup.Presentation.CloseProjectScreen();

        private System.Threading.Tasks.Task<CommandAckMessage>? pendingVrConversionAck;
        private System.Threading.Tasks.Task<RuntimeModelsResponse>? pendingRuntimeModelsRead;

        private void ResetSpatialFlows()
        {
            pendingVrConversionAck = null;
            pendingRuntimeModelsRead = null;
            setup.Presentation.ResetPendingCommands();
        }

        private void AdvanceSpatialFlows()
        {
            if (pendingRuntimeModelsRead is System.Threading.Tasks.Task<RuntimeModelsResponse> modelsTask && modelsTask.IsCompleted)
            {
                pendingRuntimeModelsRead = null;
                if (modelsTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    setup.Presentation.SetRuntimeModels(modelsTask.Result);
                    dirty = true;
                }
            }

            if (setup.Presentation.PendingVrConversionSequence is BuildSequence activeSequence
                && pendingVrConversionAck is System.Threading.Tasks.Task<CommandAckMessage> ack
                && ack.IsCompleted)
            {
                pendingVrConversionAck = null;
                if (ack.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) activeSequence.Acknowledged(ack.Result);
                else activeSequence.AcknowledgementLost(ack.Exception?.GetBaseException() ?? new InvalidOperationException("The acknowledgement was lost."));
            }

            if (!Connected || Demonstration) return;

            var nextCommand = setup.Presentation.AdvancePendingCommands(State, setup.Commands, out var convertedProjectId);
            if (convertedProjectId != null)
            {
                memory.SelectProject(convertedProjectId);
                dirty = true;
            }
            if (nextCommand != null && setup.Session() is RealtimeSession session)
            {
                var submitted = setup.Submit(session, nextCommand);
                if (setup.Presentation.PendingVrConversionSequence is BuildSequence conversion)
                {
                    if (submitted == null) conversion.AcknowledgementLost(new SessionUnavailableException("Not connected."));
                    else pendingVrConversionAck = submitted;
                }
                dirty = true;
            }
            else if (nextCommand != null && setup.Presentation.PendingVrConversionSequence is BuildSequence disconnectedConversion)
            {
                disconnectedConversion.AcknowledgementLost(new SessionUnavailableException("Not connected."));
            }
            else if (convertedProjectId != null
                && nextCommand == null
                && State?.Projects.TryGetValue(convertedProjectId, out var newVrProject) == true)
            {
                OpenNewProject(newVrProject.ProjectId, newVrProject.Name);
            }
        }

        private void OnScreenBarToggled()
        {
            if (Away || drag != null || plane.Repositioning || plane.Sliding) return;
            SetProjectScreenBarOpen(true);
        }

        /// <summary>DEVICE's Share, one per project while its page is open (<see cref="ShareColumn"/>).</summary>
        private readonly Dictionary<string, ShareColumn> shares = new Dictionary<string, ShareColumn>();

        private ShareColumn? ShareFor(string projectId) => shares.TryGetValue(projectId, out var share) ? share : null;

        private bool OpenShare(string projectId)
        {
            if (State?.Projects.TryGetValue(projectId, out var project) != true) return false;
            // The project's column where one stands (a start under way, a stop to send), else a new one (AUDIT reread MUST 2).
            if (ShareFor(projectId) is ShareColumn kept)
            {
                kept.Viewed(project!);
                return true;
            }
            var share = new ShareColumn(project!);
            shares[projectId] = share;
            ReadWays(share);
            if (project!.Share != null) ReadCode(share);
            return true;
        }

        /// <summary>Whether a Share command can be sent now: the computer connected, no recording plays, a session to send through.</summary>
        private bool CanSendShare => Connected && !Demonstration && setup.Session() is RealtimeSession;

        /// <summary>Sends a Share command where the computer is connected and no recording plays; false where it cannot.</summary>
        private bool SendShare(CommandEnvelope command)
        {
            if (!Connected || Demonstration || setup.Session() is not RealtimeSession session) return false;
            setup.Presentation.RecordCommandDispatched(command);
            _ = setup.Submit(session, command);
            return true;
        }

        /// <summary>Reads which ways this computer can share now; none offered where it cannot be asked or did not answer.</summary>
        private async void ReadWays(ShareColumn share)
        {
            if (Api is not ControlPlaneApi api)
            {
                share.WaysFailed();
                dirty = true;
                return;
            }
            try { share.WaysRead(await new ShareReader(api.GetDocumentAsync).WaysAsync()); }
            // The computer could not tell: said so, with Try Again (AUDIT reread SHOULD 2).
            catch (Exception) { share.WaysFailed(); }
            dirty = true;
        }

        /// <summary>Reads the live share's code to scan; the card waits without it where it cannot be read.</summary>
        private async void ReadCode(ShareColumn share)
        {
            if (Api is not ControlPlaneApi api) return;
            // The read is drawn only for the share standing when it began (AUDIT SHOULD 1).
            var generation = share.ShareGeneration;
            try { share.CodeRead(await new ShareReader(api.GetDocumentAsync).CodeAsync(share.ProjectId), generation); }
            catch (Exception) { /* The code stays unread; the share still stands and Stop still works. */ }
            dirty = true;
        }

        /// <summary>What the computer said since the last change for every Share open: the project's view, the start's answer, a share's end.</summary>
        private void ShareHeard(StateChanges changes)
        {
            if (shares.Count == 0) return;
            foreach (var share in shares.Values.ToList())
            {
                if (State?.Projects.TryGetValue(share.ProjectId, out var view) == true)
                {
                    var had = share.Cells != null;
                    share.Viewed(view!);
                    if (view!.Share != null && !had) ReadCode(share);
                }
                if (share.SentCommandId is string sent && State?.Commands.TryGetValue(sent, out var answer) == true) share.Answered(answer!);
                // Cancel was pressed while it started: stop it the moment it stands (AUDIT MUST 1).
                if (share.StopNow && CanSendShare && setup.Commands is CommandFactory stopCommands && share.Stop(stopCommands) is ProjectShareStopCommand stopNow
                    && SendShare(stopNow))
                    share.StopSent();
                dirty = true;
            }
            foreach (var stored in changes.Events)
                if (stored.Event is ProjectShareStoppedEvent stopped && stopped.ProjectId is string ended && ShareFor(ended) is ShareColumn share)
                {
                    share.ShareStopped(stopped);
                    dirty = true;
                }
        }

        /// <summary>DEVICE's History, one per project while its page is open (<see cref="HistoryColumn"/>).</summary>
        private readonly Dictionary<string, HistoryColumn> histories = new Dictionary<string, HistoryColumn>();

        private HistoryColumn? HistoryFor(string projectId) => histories.TryGetValue(projectId, out var history) ? history : null;

        private void OpenHistory(string projectId)
        {
            var name = State?.Projects.TryGetValue(projectId, out var project) == true ? project!.Name : "";
            var history = new HistoryColumn(projectId, name);
            histories[projectId] = history;
            ReadVersions(history);
        }

        private void CloseHistory(string projectId, string task, string? run, RoomResultPage to)
        {
            histories.Remove(projectId);
            setup.Presentation.ShowResultPage(task, run, to);
        }

        /// <summary>Reads the project's versions into <paramref name="history"/>; unreadable where the computer cannot be asked or did not answer.</summary>
        private async void ReadVersions(HistoryColumn history)
        {
            if (Api is not ControlPlaneApi api)
            {
                history.ReadFailed();
                dirty = true;
                return;
            }
            try { history.Listed(await new ProjectHistoryReader(api.GetDocumentAsync).VersionsAsync(history.ProjectId), State, Clock, Zone); }
            catch (Exception) { history.ReadFailed(); }
            dirty = true;
        }

        /// <summary>Reads what restoring the version picked would do now, with the digest of that review, into <paramref name="history"/>.</summary>
        private async void ReadPreview(HistoryColumn history)
        {
            if (Api is not ControlPlaneApi api || history.Reviewed is not HistoryVersion version)
            {
                history.ReadFailed();
                dirty = true;
                return;
            }
            try { history.Previewed(await new ProjectHistoryReader(api.GetDocumentAsync).PreviewAsync(history.ProjectId, version.View.CheckpointId)); }
            catch (Exception) { history.ReadFailed(); }
            dirty = true;
        }

        /// <summary>
        /// What the computer said since the last change (WorkspaceDirector): a restore done or incomplete, and the answer
        /// to a restore sent, for every History open.
        /// </summary>
        public void HistoryHeard(StateChanges changes)
        {
            // Share hears through the same hook (one line in WorkspaceDirector for both).
            ShareHeard(changes);
            if (histories.Count == 0) return;
            foreach (var stored in changes.Events)
            {
                switch (stored.Event)
                {
                    case ProjectRestoredEvent restored when restored.ProjectId is string done && HistoryFor(done) is HistoryColumn history:
                        history.Restored(restored);
                        dirty = true;
                        break;
                    case ProjectRestoreIncompleteEvent incomplete when incomplete.ProjectId is string partial && HistoryFor(partial) is HistoryColumn history:
                        history.RestoreIncomplete(incomplete);
                        dirty = true;
                        break;
                }
            }
            foreach (var history in histories.Values)
                if (history.SentCommandId is string sent && State?.Commands.TryGetValue(sent, out var answer) == true)
                {
                    history.Answered(answer!);
                    dirty = true;
                }
        }

        /// <summary>
        /// DEVICE's Play in Headset pressed on the result: where no preview of the latest check runs, its start is sent and the
        /// press waits for it (<see cref="HeadsetPlayWait"/>); where one runs, Quest Browser opens its loopback address. Nothing
        /// where Play in Headset does not stand (the refusal's words are not pressable). On a paired computer the app's own
        /// relay serves the preview on the headset's loopback, started as the address opens (<see cref="HeadsetRelay"/>).
        /// </summary>
        private void PlayInHeadset(string projectId)
        {
            if (Demonstration || State?.Projects.TryGetValue(projectId, out var project) != true) return;
            var target = ControlPlaneSettings.Target();
            var relays = HeadsetRelay.Serves(target);
            if (!HeadsetPlay.Stands(HeadsetPlay.For(project!, target, Demonstration, relays: relays))) return;
            if (HeadsetPlay.CommandFor(project!, target, Demonstration, setup.Commands, relays: relays) is CommandEnvelope command)
            {
                if (!Connected || Demonstration || setup.Session() is not RealtimeSession session) return;
                setup.Presentation.RecordCommandDispatched(command);
                _ = setup.Submit(session, command);
                headsetPlay.Sent(command);
                dirty = true;
                return;
            }
            var port = target?.Pairing != null ? HeadsetRelay.PortFor(target, projectId) : null;
            HeadsetBrowser.Open(HeadsetPlay.AddressOf(project!, target, Demonstration, port));
        }

        /// <summary>Sends <paramref name="command"/> where the computer is connected and no recording plays; false where it cannot.</summary>
        private bool SendCommand(CommandEnvelope command)
        {
            if (!Connected || Demonstration || setup.Session() is not RealtimeSession session) return false;
            setup.Presentation.RecordCommandDispatched(command);
            _ = setup.Submit(session, command);
            return true;
        }

        /// <summary>A Play in Headset press waiting on the preview it started; it opens once, when the preview runs.</summary>
        private readonly HeadsetPlayWait headsetPlay = new HeadsetPlayWait();

        /// <summary>Each frame: where a press waits on a preview, the address opens once the project's view shows it running.</summary>
        private void AdvanceHeadsetPlay()
        {
            if (headsetPlay.ProjectId == null) return;
            // A demonstration showing meanwhile opens nothing.
            if (Demonstration)
            {
                headsetPlay.Clear();
                return;
            }
            // HeadsetBrowser opens only an address HeadsetPlay.IsOpenable admits; null opens nothing.
            var target = ControlPlaneSettings.Target();
            HeadsetBrowser.Open(headsetPlay.Advance(State, target, Demonstration,
                relay: HeadsetRelay.Serves(target) ? project => HeadsetRelay.PortFor(target, project) : null));
        }

        private void OnScreenActed(KitScreenAction action, string? projectId)
        {
            if (Away || drag != null || plane.Repositioning || plane.Sliding) return;
            if (action == KitScreenAction.Close)
            {
                CloseProjectScreen();
                return;
            }
            var activeProjectId = projectId ?? setup.Presentation.ScreenProjectId;
            if (activeProjectId == null || State?.Projects.TryGetValue(activeProjectId, out var project) != true) return;
            var primaryWork = WorkspacePresentationState.PrimaryWorkstreamOf(project.ProjectId, State);
            if (WorkspacePresentationState.SectionForScreenAction(action) is FileSection section)
            {
                if (primaryWork != null) OpenFile(primaryWork.WorkstreamId, section);
                else OpenNewProject(project.ProjectId, project.Name);
                return;
            }
            if (action == KitScreenAction.ChangeIt)
            {
                if (primaryWork != null) OpenFile(primaryWork.WorkstreamId, FileSection.Activity);
                else OpenNewProject(project.ProjectId, project.Name);
                return;
            }
            if (WorkspacePresentationState.CommandForScreenAction(project, action, setup.Commands) is CommandEnvelope command)
            {
                if (Connected && !Demonstration && setup.Session() is RealtimeSession session)
                {
                    setup.Presentation.RecordCommandDispatched(command);
                    _ = setup.Submit(session, command);
                    dirty = true;
                }
                return;
            }
            if (action == KitScreenAction.MakeAVrVersion)
            {
                OpenNewProject(project.ProjectId, project.Name);
            }
        }

        private void OnMomentActed(KitMomentAction action, string? workstreamId)
        {
            if (Away || drag != null || plane.Repositioning || plane.Sliding) return;
            var activeWorkstreamId = workstreamId ?? plane.MomentWorkstreamId;
            if (activeWorkstreamId == null || State?.Workstreams.TryGetValue(activeWorkstreamId, out var workstream) != true) return;
            if (action == KitMomentAction.TryIt)
            {
                if (State.Projects.TryGetValue(workstream.ProjectId, out var project)
                    && WorkspacePresentationState.CommandForScreenAction(project, KitScreenAction.TryIt, setup.Commands) is CommandEnvelope command
                    && Connected && !Demonstration
                    && setup.Session() is RealtimeSession session)
                {
                    setup.Presentation.RecordCommandDispatched(command);
                    _ = setup.Submit(session, command);
                    dirty = true;
                    return;
                }
                OpenFile(workstream.WorkstreamId, FileSection.Checks);
                return;
            }
            if (WorkspacePresentationState.SectionForMomentAction(action) is FileSection section)
            {
                OpenFile(workstream.WorkstreamId, section);
            }
        }

        private void UpdateProgressiveSpatial(UnityEngine.Vector3 eyes, UnityEngine.Vector3 looking)
        {
            AdvanceSpatialFlows();
            if (setup.SpatialWorkspace && !Away && setup.Presentation.ActiveScreen(State, Connected || Demonstration) is KitScreenModel screenModel)
            {
                // It stands where the console stands, on the plane's own direction: the look's bearing and its elevation held
                // within the room's pitch, plus the person's move. Level at eye height, it sat low under a raised look (D-XR-8).
                var direction = plane.Direction;
                plane.ShowScreen(screenModel, eyes, direction.Yaw, direction.Elevation, setup.Presentation.ScreenProjectId);
            }
            else if (plane.RoomScreen != null)
            {
                plane.HideScreen();
            }

            var allowMoments = setup.SpatialWorkspace
                && !Away
                && navigator.BesideTask == null
                && (setup.Presentation.InspectedWorkstreamId != null
                    || (setup.Presentation.ContextualMomentsEnabled
                        && (setup.Presentation.Minimized || setup.Presentation.ScreenProjectId != null)));
            if (allowMoments
                && setup.Presentation.ActiveMoment(State, task => setup.CharacterOf(task) != null) is var (workstreamId, slot)
                && setup.CharacterOf(workstreamId) is CharacterTarget momentTarget)
            {
                var body = WorkspaceLayout.InView(momentTarget, eyes);
                var lookBearing = UnityEngine.Mathf.Atan2(looking.x, looking.z) * UnityEngine.Mathf.Rad2Deg;
                plane.ShowMoment(slot, eyes, lookBearing, momentTarget.RestPosition, body.HalfWidth, workstreamId);
                plane.Moment?.Pop(1f);
            }
            else if (plane.Moment != null)
            {
                plane.HideMoment();
            }
        }
    }
}
