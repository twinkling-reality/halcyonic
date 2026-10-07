#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The first visit (ADR 0026): once the session is live, with nothing open beside the menu, the menu
    /// opens by itself on Projects, once on this device for the recorded demonstration and once for the
    /// person's computer, so someone who saw the demonstration first still finds Projects open on their
    /// first visit to their own work. The headset keeps the computer's under the key the retired entry
    /// panel's welcome kept, so no one welcomed before is welcomed again.
    /// </summary>
    public sealed class FirstVisit
    {
        /// <param name="visited">The device has seen its first visit to the person's computer already.</param>
        /// <param name="demonstrationVisited">The device has seen the demonstration's first visit already.</param>
        public FirstVisit(bool visited, bool demonstrationVisited = false)
        {
            Visited = visited;
            DemonstrationVisited = demonstrationVisited;
        }

        public bool Visited { get; private set; }

        public bool DemonstrationVisited { get; private set; }

        /// <summary>Whether the menu opens on Projects now; once true, never again for the same kind of session.</summary>
        public bool Due(bool live, bool demonstration, bool somethingOpen)
        {
            if (!live || somethingOpen) return false;
            if (demonstration)
            {
                if (DemonstrationVisited) return false;
                DemonstrationVisited = true;
                return true;
            }
            if (Visited) return false;
            Visited = true;
            return true;
        }
    }

    /// <summary>What showing or hiding a project's work on the stage asks of the stage.</summary>
    public readonly struct ShowingChange
    {
        public ShowingChange(bool changed, bool withdrawRequest)
        {
            Changed = changed;
            WithdrawRequest = withdrawRequest;
        }

        /// <summary>Which projects show changed, so the stage refreshes and the device keeps it.</summary>
        public bool Changed { get; }

        /// <summary>The work the person brought forward belongs to the project just hidden, so it leaves with it.</summary>
        public bool WithdrawRequest { get; }
    }

    /// <summary>Projects' Show on stage and Hide from stage (ADR 0026), as the stage's choice of projects takes them.</summary>
    public static class ProjectShowing
    {
        /// <summary>
        /// Shows or hides <paramref name="projectId"/>'s work in <paramref name="visibility"/>. Hiding
        /// withdraws <paramref name="requested"/>, the work brought forward from Tasks, when it is that
        /// project's. Nothing changes when the project already stands as asked.
        /// </summary>
        public static ShowingChange Apply(StageVisibility visibility, string projectId, bool shown, ClientProjection state, string? requested)
        {
            if (visibility.Shows(projectId) == shown) return new ShowingChange(false, false);
            if (shown)
            {
                visibility.Show(projectId);
                return new ShowingChange(true, false);
            }
            visibility.Hide(projectId, state.Projects.Keys);
            var withdraw = requested != null && state.Workstreams.TryGetValue(requested, out var work) && work.ProjectId == projectId;
            return new ShowingChange(true, withdraw);
        }
    }
}
