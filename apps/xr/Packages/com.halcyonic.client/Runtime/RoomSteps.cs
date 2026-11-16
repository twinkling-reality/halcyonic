#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The task's steps list for the redesign's bar (SCREENS's TaskSteps, placed by XR: the 02:0xZ split, SCREENS's 08:52Z yes), read
    /// from the sources the result reads (RoomResult.Of): the project's way of checking and what its check last said, whether a Check
    /// Project the person sent is under way (an accepted project.check command for the project), and what the computer saw of the
    /// folder over the last turn. Nothing without a run or a project.
    /// </summary>
    public static class RoomSteps
    {
        public static IReadOnlyList<TaskStep> Of(ExecutionView? execution, ProjectView? project, ClientProjection? state, ObservedTurn? lastTurn)
        {
            if (execution == null || project == null) return new TaskStep[0];
            // No starter the headset reads declares that it has no build: the way is the project's own (TaskSteps.WayOf).
            var way = TaskSteps.WayOf(project, starterHasNoBuild: false);
            var checkRunning = state != null && state.Commands.Values.Any(command => command.CommandType == CommandType.ProjectCheck
                && command.ProjectId == project.ProjectId && command.Status == CommandStatus.Accepted);
            return TaskSteps.Of(execution, way, TaskSteps.SeenOf(project, way), checkRunning, lastTurn);
        }
    }
}
