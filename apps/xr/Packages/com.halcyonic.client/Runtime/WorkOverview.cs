#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One project as the rail and Connect projects show it: its name and what its work is doing.</summary>
    public sealed class ProjectSummary
    {
        public ProjectSummary(string projectId, string name, bool shown, int work, int active, int needsYou, int notice, int offStage, int notStarted)
        {
            NotStarted = notStarted;
            ProjectId = projectId;
            Name = name;
            Shown = shown;
            Work = work;
            Active = active;
            NeedsYou = needsYou;
            Notice = notice;
            OffStage = offStage;
        }

        public string ProjectId { get; }

        /// <summary>The project's name, as <see cref="LabelText.Plain"/> shows it.</summary>
        public string Name { get; }

        /// <summary>Its work stands on the stage (<see cref="StageVisibility"/>).</summary>
        public bool Shown { get; }

        /// <summary>How many workstreams it has.</summary>
        public int Work { get; }

        /// <summary>Starting, working or running tests.</summary>
        public int Active { get; }

        /// <summary>Waiting for the person: an approval is pending.</summary>
        public int NeedsYou { get; }

        /// <summary>Failed, unknown, or finished with failing tests.</summary>
        public int Notice { get; }

        /// <summary>At rest and never started: created, with nothing to look at. The rest at rest ran and are paused.</summary>
        public int NotStarted { get; }

        /// <summary>Its workstreams without a character on the stage, hidden or beyond its slots.</summary>
        public int OffStage { get; }
    }

    /// <summary>Why a workstream has no character on the stage.</summary>
    public enum OffStageReason
    {
        /// <summary>The person chose not to show its project.</summary>
        ProjectHidden,

        /// <summary>Its project shows, but the stage's slots hold work that ranks higher.</summary>
        StageFull,
    }

    /// <summary>A workstream without a character, for More work.</summary>
    public sealed class OffStageWork
    {
        public OffStageWork(WorkstreamView workstream, string projectName, OffStageReason reason)
        {
            Workstream = workstream;
            ProjectName = projectName;
            Reason = reason;
        }

        public WorkstreamView Workstream { get; }

        /// <summary>Its project's name, as <see cref="LabelText.Plain"/> shows it.</summary>
        public string ProjectName { get; }

        public OffStageReason Reason { get; }
    }

    /// <summary>
    /// Every project and every workstream the state holds, counted for the project rail, Connect
    /// projects and More work, so no work disappears because its project is hidden or the stage is
    /// full: whatever has no character is listed as off the stage, what needs the person first, and
    /// its attention is counted with its project whether or not the project shows. It derives
    /// nothing from the work itself; the counts are the lineup's tiers of the projected status and
    /// attention (<see cref="CharacterLineup.TierOf"/>).
    /// </summary>
    public sealed class WorkOverview
    {
        private WorkOverview(IReadOnlyList<ProjectSummary> projects, IReadOnlyList<OffStageWork> offStage)
        {
            Projects = projects;
            OffStage = offStage;
        }

        /// <summary>Every project, by name.</summary>
        public IReadOnlyList<ProjectSummary> Projects { get; }

        /// <summary>Every workstream without a character, as the lineup ranks them: what needs the person first.</summary>
        public IReadOnlyList<OffStageWork> OffStage { get; }

        public int Work => Projects.Sum(project => project.Work) + Unfiled;

        public int NeedsYou => Projects.Sum(project => project.NeedsYou) + UnfiledNeedsYou;

        /// <summary>What needs the person and has no character: only the rail and More work show it.</summary>
        public int NeedsYouOffStage => OffStage.Count(work => CharacterLineup.TierOf(work.Workstream) == LineupTier.NeedsYou);

        public int ShownProjects => Projects.Count(project => project.Shown);

        /// <summary>Workstreams whose project the state does not hold, which a consistent journal never has.</summary>
        private int Unfiled { get; set; }

        private int UnfiledNeedsYou { get; set; }

        /// <summary>
        /// Counts the state. <paramref name="onStage"/> says whether a workstream has a character now,
        /// as the stage's lineup decided.
        /// </summary>
        public static WorkOverview Of(ClientProjection state, StageVisibility visibility, Func<string, bool> onStage)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (visibility == null) throw new ArgumentNullException(nameof(visibility));
            if (onStage == null) throw new ArgumentNullException(nameof(onStage));
            var byProject = state.Workstreams.Values.ToLookup(workstream => workstream.ProjectId, StringComparer.Ordinal);
            var projects = state.Projects.Values
                .Select(project =>
                {
                    var work = byProject[project.ProjectId].ToList();
                    return new ProjectSummary(
                        project.ProjectId,
                        LabelText.Name(project.Name),
                        visibility.Shows(project.ProjectId),
                        work.Count,
                        work.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.Active),
                        work.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou),
                        work.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.Notice),
                        work.Count(workstream => !onStage(workstream.WorkstreamId)),
                        work.Count(workstream => workstream.Status == WorkstreamStatus.Created && CharacterLineup.TierOf(workstream) == LineupTier.AtRest));
                })
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.ProjectId, StringComparer.Ordinal)
                .ToList();
            var offStage = state.Workstreams.Values
                .Where(workstream => !onStage(workstream.WorkstreamId))
                .OrderBy(workstream => workstream, Comparer<WorkstreamView>.Create(CharacterLineup.Compare))
                .Select(workstream =>
                {
                    var known = state.Projects.TryGetValue(workstream.ProjectId, out var project);
                    return new OffStageWork(
                        workstream,
                        known ? LabelText.Name(project!.Name) : "",
                        visibility.Shows(workstream.ProjectId) ? OffStageReason.StageFull : OffStageReason.ProjectHidden);
                })
                .ToList();
            var unfiled = state.Workstreams.Values.Where(workstream => !state.Projects.ContainsKey(workstream.ProjectId)).ToList();
            return new WorkOverview(projects, offStage)
            {
                Unfiled = unfiled.Count,
                UnfiledNeedsYou = unfiled.Count(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou),
            };
        }

        /// <summary>
        /// The projects a rail with room for <paramref name="count"/> shows: those with work that needs
        /// the person first, then shown ones, then by name. What does not fit is counted by
        /// Connect projects, and its attention by More work.
        /// </summary>
        public IReadOnlyList<ProjectSummary> ForRail(int count) => Projects
            .Select((project, index) => (project, index))
            .OrderBy(entry => entry.project.NeedsYou > 0 ? 0 : 1)
            .ThenBy(entry => entry.project.Shown ? 0 : 1)
            .ThenBy(entry => entry.index)
            .Take(Math.Max(0, count))
            .Select(entry => entry.project)
            .ToList();
    }
}
