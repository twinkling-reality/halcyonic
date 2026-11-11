#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The two reads History needs from the computer (gap 2; CORE's note of 2026-11-11): a project's versions
    /// (<c>GET /api/projects/:id/checkpoints</c>, read as the relative path "api/projects/..." the control plane's own reads use) and what restoring one would do now, with the digest of that review
    /// (<c>GET /api/projects/:id/checkpoints/:checkpoint_id/restore-preview</c>, ADR 0051). It reads through
    /// <paramref name="get"/>, the control plane's own authorized GET of a path to its JSON body, so it holds no
    /// credential of its own. Both answers parse strictly, as every contract does.
    /// </summary>
    public sealed class ProjectHistoryReader
    {
        private readonly Func<string, CancellationToken, Task<string>> get;

        public ProjectHistoryReader(Func<string, CancellationToken, Task<string>> get)
        {
            this.get = get ?? throw new ArgumentNullException(nameof(get));
        }

        /// <summary>The path of a project's versions.</summary>
        public static string VersionsPath(string projectId) =>
            "api/projects/" + Uri.EscapeDataString(Required(projectId, nameof(projectId))) + "/checkpoints";

        /// <summary>The path of what restoring <paramref name="checkpointId"/> would do now.</summary>
        public static string PreviewPath(string projectId, string checkpointId) =>
            VersionsPath(projectId) + "/" + Uri.EscapeDataString(Required(checkpointId, nameof(checkpointId))) + "/restore-preview";

        /// <summary>Every version the computer kept of the project, oldest first as it answers, or why there are none.</summary>
        public async Task<CheckpointsResponse> VersionsAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var json = await get(VersionsPath(projectId), cancellationToken).ConfigureAwait(false);
            return Matching(HalcyonicJson.Deserialize<CheckpointsResponse>(json, strict: true), projectId, response => response.ProjectId);
        }

        /// <summary>What restoring the version would do now and the digest of that review, or why it cannot be read.</summary>
        public async Task<RestorePreviewResponse> PreviewAsync(string projectId, string checkpointId, CancellationToken cancellationToken = default)
        {
            var json = await get(PreviewPath(projectId, checkpointId), cancellationToken).ConfigureAwait(false);
            var response = Matching(HalcyonicJson.Deserialize<RestorePreviewResponse>(json, strict: true), projectId, each => each.ProjectId);
            if (response.Result is AvailableRestorePreview available && available.Preview.CheckpointId != checkpointId)
                throw new FormatException("The computer answered a review of another version.");
            return response;
        }

        private static T Matching<T>(T response, string projectId, Func<T, string> of)
        {
            if (of(response) != projectId) throw new FormatException("The computer answered for another project.");
            return response;
        }

        private static string Required(string value, string name) =>
            string.IsNullOrEmpty(value) ? throw new ArgumentException("An id is required.", name) : value;
    }
}
