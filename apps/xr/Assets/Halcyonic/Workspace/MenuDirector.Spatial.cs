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
        private readonly Dictionary<FileColumn, (float ContextHeight, bool Portrait, int TitleRows, int ProvenanceRows, bool Kit)> composerLayouts
            = new Dictionary<FileColumn, (float, bool, int, int, bool)>();
        private readonly Dictionary<FileColumn, bool> composerFocus = new Dictionary<FileColumn, bool>();
        private void DropSpatialComposer(FileColumn file) { composerLayouts.Remove(file); composerFocus.Remove(file); }

        /// <summary>The last measured Activity and composer pair decision, for the renders: field, readable text bands, clearance, and the bands' least em in degrees.</summary>
        public (bool Inside, bool Readable, bool Clear, float LeastEm)? PairDecisionForRender { get; private set; }
        private object? contextOwner;
        private RealtimeSession? contextSession;
        private ClientProjection? contextState;
        private string? contextJournal, contextConnection, contextTask, contextProject;
        private (string Id, string Name)[] contextChoices = Array.Empty<(string, string)>();
        private bool SeparateWorkspace => setup.SpatialWorkspace && placement == null
            && (navigator.Beside is FileColumn || navigator.Beside is TaskResultsColumn || navigator.Beside is WorkspaceRequestColumn
                || navigator.Beside == null && (navigator.Place == MenuPlace.Projects || navigator.Place == MenuPlace.Tasks));

        private void ConfigureSpatialComposer()
        {
            plane.SeparateWorkspace = SeparateWorkspace;
            if (!setup.SpatialWorkspace || navigator.Beside is not FileColumn file) return;
            var contextHeight = plane.Context.HeightFor(State?.Projects.Count ?? 0);
            var titleRows = navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true
                ? MenuFrameView.IndependentTitleRows(work.Title, Glaze.Menu.FileColumnDegrees) : 2;
            var provenanceRows = MenuFrameView.IndependentProvenanceRows(CurrentSpatialMarks(), Glaze.Menu.FileColumnDegrees);
            // The kit lays the file out by its own rows while it draws it (D3.4): Activity's page the rail's rows, the
            // composer's the rows its well shows, always the pair; the file's views by their measured heights otherwise.
            var geometry = (contextHeight, HasTaskPortrait, titleRows, provenanceRows, KitDraws(file));
            if (composerLayouts.TryGetValue(file, out var previous))
            {
                if (previous != geometry) { composerLayouts[file] = geometry; file.Reflow(); }
                return;
            }
            composerLayouts[file] = geometry;
            // Each retained file keeps its own callbacks and geometry lifetime. Background rebuilds
            // must never borrow another selected task's provenance or portrait allocation.
            file.ConfigureActivityComposer(
                () => KitDraws(file) ? new RowBudget(plane.Kit.RailRows) : new HeightBudget(IndependentPageHeight(OwnerTitleRows(file), true, false, file)),
                () =>
                {
                    if (!KitDraws(file)) return new HeightBudget(IndependentPageHeight(OwnerTitleRows(file), true, true, file));
                    composerFocus[file] = false;
                    return new RowBudget(plane.Kit.ComposerRows);
                });
        }

        /// <summary>Whether the kit draws <paramref name="file"/> now, as it last drew it in the plane's place.</summary>
        private bool KitDraws(FileColumn file) => plane.KitShows && ReferenceEquals(navigator.Beside, file);

        /// <summary>
        /// The kit bound to <paramref name="file"/>'s frame (D3.4), its spine exactly the context column's offers in its words
        /// (WorkspaceContextView): the connection state under Projects, the same up to three projects, More projects past
        /// three, Tasks, and Workspace at its foot. Null where the kit is off or can't draw the frame.
        /// </summary>
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
            return KitFileBinding.Of(frame, file.Steering.Armed, now, file.Screen.Instruction.Open ? file.Screen.Instruction.Words : "",
                setup.Session()?.Status ?? ConnectionStatus.Stopped, name, spine, new KitRowModel("workspace", LineWords.Ours("Workspace"), GlazeIcon.Move));
        }

        private static int OwnerTitleRows(FileColumn owner) => owner.Now?.Character is CharacterPresentation character
            ? MenuFrameView.IndependentTitleRows(character.Title, Glaze.Menu.FileColumnDegrees) : 2;

        private void PrepareSpatialContext()
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
            plane.KitBinding = KitBindingOf(activeFile, choices, selected);
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
    }
}
