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
        private readonly Dictionary<FileColumn, (float ContextHeight, bool Portrait, int TitleRows, int ProvenanceRows)> composerLayouts
            = new Dictionary<FileColumn, (float, bool, int, int)>();
        private readonly Dictionary<FileColumn, bool> composerFocus = new Dictionary<FileColumn, bool>();
        private void DropSpatialComposer(FileColumn file) { composerLayouts.Remove(file); composerFocus.Remove(file); }
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
            var geometry = (contextHeight, HasTaskPortrait, titleRows, provenanceRows);
            if (composerLayouts.TryGetValue(file, out var previous))
            {
                if (previous != geometry) { composerLayouts[file] = geometry; file.Reflow(); }
                return;
            }
            composerLayouts[file] = geometry;
            // Each retained file keeps its own callbacks and geometry lifetime. Background rebuilds
            // must never borrow another selected task's provenance or portrait allocation.
            file.ConfigureActivityComposer(() => new HeightBudget(IndependentPageHeight(OwnerTitleRows(file), true, false, file)),
                () => new HeightBudget(IndependentPageHeight(OwnerTitleRows(file), true, true, file)));
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
        }

        private bool ContextStanding(object owner)
        {
            return !Away && !dirty && drag == null && !plane.Repositioning && !plane.Sliding
                && plane.Context.gameObject.activeInHierarchy && plane.Context.Accepting()
                && ReferenceEquals(owner, contextOwner) && ReferenceEquals(owner, plane.Context.Binding)
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
            var section = GlazeButton.HeightOf(true);
            (bool Inside, bool Readable, bool Clear, float Width, float Height) lastFit = default;
            bool Fits(float activity, float instruction, bool focused = false)
            {
                var heights = split && !focused
                    ? new[] { header + MenuPage.Content(activity) - FooterView.Height - MenuPage.TargetGap,
                        FooterView.Height + MenuPage.TargetGap + MenuPage.Content(instruction) }
                    : new[] { header + (focused ? MenuPage.Words(1) : section) + FooterView.Height + MenuPage.TargetGap + MenuPage.Content(instruction) };
                var stacked = heights.Sum() + (heights.Length - 1) * PlaneComposition.Units(PlaneComposition.PartGapDegrees);
                var envelope = MenuPlane.IndependentEnvelope(UnityEngine.Mathf.Max(leftHeight, stacked), header, portrait);
                var composition = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(WorkspaceContextView.WidthDegrees) + MenuPlane.PortraitClearance, envelope), new PlaneColumn(width, heights) }, zoom);
                var inside = MenuPage.Inside(composition, direction, ViewField.Current ?? MenuPage.Quest3S);
                var readable = MenuPage.Readable(composition);
                var clear = WorkspacePlacement.Clears(direction, occupied, composition.Size);
                lastFit = (inside, readable, clear, composition.Width, composition.Height);
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
#if UNITY_EDITOR
            UnityEngine.Debug.Log($"Halcyonic: independent measured allocation minActivity={minimumActivity:R}; minComposer={minimumComposer:R}; titleHeader={header:R}; pairFits={together}; inside={lastFit.Inside}; readable={lastFit.Readable}; clear={lastFit.Clear}; width={lastFit.Width:R}; height={lastFit.Height:R}; pairedPager={paired}; textScale={zoom:R}.");
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
