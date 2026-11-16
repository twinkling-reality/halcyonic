#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The still of a photo or video the person gave a task, as the headset reads it from the computer (ADR 0066, protocol 3):
    /// <c>GET /api/projects/:id/picture?journal_id&amp;path</c>, the project's own authorized read. Only the picture's bytes come
    /// back, a PNG or a JPEG (what the headset decodes), at most <see cref="MostBytes"/>; anything else is refused before it is
    /// drawn. Where the file left the project the computer answers <see cref="Gone"/>; where it is no picture it reads,
    /// <see cref="NotFound"/>. Nothing here holds a credential: <see cref="ControlPlaneApi.GetPictureAsync"/> reads it.
    /// </summary>
    public static class TaskPicture
    {
        /// <summary>
        /// The still the task's card shows: the first photo or video given to <paramref name="execution"/>'s run for which the host
        /// made one (TaskMedia), in its project; null where none was given or none has a still.
        /// </summary>
        public static TaskStill? StillOf(ExecutionView? execution)
        {
            if (execution == null || string.IsNullOrEmpty(execution.ProjectId)) return null;
            foreach (var item in TaskMedia.Of(execution))
                if (item.Still != null) return new TaskStill(execution.ProjectId, item.Still, item.Kind);
            return null;
        }

        /// <summary>The most bytes a picture may hold: the computer's own PICTURE_BYTES (TASKS's 02:32Z route answer).</summary>
        public const int MostBytes = 5_242_880;

        /// <summary>The computer's code where the photo or video is no longer in the project.</summary>
        public const string Gone = "media_gone";

        /// <summary>The computer's code where the path is no picture it reads in the project.</summary>
        public const string NotFound = "picture_not_found";

        /// <summary>This headset's own code where the answer is no picture it draws: another type, no body, or too large.</summary>
        public const string NotDrawn = "picture_not_drawn";

        /// <summary>The read's relative path, every part escaped.</summary>
        public static string PathOf(string projectId, string journalId, string path)
        {
            if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("A project id is required.", nameof(projectId));
            if (string.IsNullOrEmpty(journalId)) throw new ArgumentException("A journal id is required.", nameof(journalId));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", nameof(path));
            return "api/projects/" + Uri.EscapeDataString(projectId) + "/picture?journal_id=" + Uri.EscapeDataString(journalId)
                + "&path=" + Uri.EscapeDataString(path);
        }

        /// <summary>Whether an answer of <paramref name="contentType"/> is a picture this headset decodes: PNG or JPEG.</summary>
        public static bool Drawn(string? contentType) =>
            contentType != null && (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) || contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A still to draw on a card: its project, its path in the project's folder, and what it is a still of.</summary>
    public sealed class TaskStill : IEquatable<TaskStill>
    {
        public TaskStill(string projectId, string path, TaskMediaKind kind)
        {
            ProjectId = projectId;
            Path = path;
            Kind = kind;
        }

        public string ProjectId { get; }

        public string Path { get; }

        public TaskMediaKind Kind { get; }

        public bool Equals(TaskStill? other) => other != null && other.ProjectId == ProjectId && other.Path == Path && other.Kind == Kind;

        public override bool Equals(object? other) => Equals(other as TaskStill);

        public override int GetHashCode() => (ProjectId, Path, Kind).GetHashCode();
    }
}
