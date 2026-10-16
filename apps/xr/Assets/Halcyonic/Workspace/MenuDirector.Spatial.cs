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
        private void DropSpatialComposer(FileColumn file) => composerLayouts.Remove(file);
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
            file.ConfigureActivityComposer(() => new HeightBudget(IndependentPageHeight(2, true, false, file)),
                () => new HeightBudget(IndependentPageHeight(2, true, true, file)));
        }

        private void PrepareSpatialContext()
        {
            plane.ActivityFrame = SeparateWorkspace && !navigator.IsCompact && navigator.Beside is FileColumn file ? file.ActivityContextFrame : null;
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
            OpenProjectTasks(projectId);
        }

        private void OnContextPlace(MenuPlace place, object owner)
        {
            if (!ContextStanding(owner)) return;
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
            bool Fits(float total)
            {
                var heights = split
                    ? new[] { header + MenuPage.Content(total * 0.55f), MenuFrameView.IndependentSubjectHeightOfRows(1, false) + MenuPage.Content(total * 0.45f) }
                    : new[] { header + section + MenuPage.Content(total) };
                var stacked = heights.Sum() + (heights.Length - 1) * PlaneComposition.Units(PlaneComposition.PartGapDegrees);
                var envelope = MenuPlane.IndependentEnvelope(UnityEngine.Mathf.Max(leftHeight, stacked));
                var composition = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(WorkspaceContextView.WidthDegrees) + MenuPlane.PortraitClearance, envelope), new PlaneColumn(width, heights) }, zoom);
                return MenuPage.Inside(composition, direction, ViewField.Current ?? MenuPage.Quest3S)
                    && MenuPage.Readable(composition) && WorkspacePlacement.Clears(direction, occupied, composition.Size);
            }
            float low = 0f, high = 2f;
            if (!Fits(0f)) return 0f;
            for (var step = 0; step < 40; step++) { var middle = (low + high) / 2f; if (Fits(middle)) low = middle; else high = middle; }
            return split ? low * (composer ? 0.45f : 0.55f) : low;
        }
    }
}
