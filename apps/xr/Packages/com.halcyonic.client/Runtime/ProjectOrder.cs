#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The order a person reads their projects in (the lead's limits order, 2026-11-17T19:17Z, #17): a project with a task that needs them first,
    /// then one with work under way, then the rest, each group by name. A project that needs the person is never hidden behind a short list's end.
    /// </summary>
    public static class ProjectOrder
    {
        /// <summary>How much a project's work asks of the person now, most first.</summary>
        public static int TierOf(string projectId, IEnumerable<WorkstreamView> work)
        {
            var tier = 2;
            foreach (var each in work)
            {
                if (each.ProjectId != projectId) continue;
                var lineup = CharacterLineup.TierOf(each);
                if (lineup == LineupTier.NeedsYou) return 0;
                if (lineup == LineupTier.Active) tier = 1;
            }
            return tier;
        }

        /// <summary><paramref name="projects"/> in the person's reading order, as <see cref="TierOf"/> ranks them, then by name and id.</summary>
        public static List<ProjectView> Of(IEnumerable<ProjectView> projects, IEnumerable<WorkstreamView> work)
        {
            var all = work as IReadOnlyCollection<WorkstreamView> ?? work.ToList();
            return projects.OrderBy(project => TierOf(project.ProjectId, all)).ThenBy(project => project.Name, StringComparer.Ordinal)
                .ThenBy(project => project.ProjectId, StringComparer.Ordinal).ToList();
        }
    }
}
