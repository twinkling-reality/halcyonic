#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What a photo or video given to a task is.</summary>
    public enum TaskMediaKind
    {
        Photo,
        Video,
    }

    /// <summary>
    /// A photo or video the person gave a task (ADR 0066, protocol 3), as the task's card shows it: its file's own name
    /// (theirs), what it is, and the still the host made of it to show, where it made one. Nothing is fetched here: the
    /// Unity layer reads the still's path through the host's project reads.
    /// </summary>
    public sealed class TaskMediaItem
    {
        public TaskMediaItem(string path, TaskMediaKind kind, string? still)
        {
            Path = path;
            Kind = kind;
            Still = still;
        }

        /// <summary>The file's path in the project's folder, as the host placed it.</summary>
        public string Path { get; }

        /// <summary>The file's own name, the path's last part, as an outside name (LabelText.Name).</summary>
        public LineWords Name => LineWords.TheirName(Path.Substring(Path.LastIndexOf('/') + 1));

        public TaskMediaKind Kind { get; }

        /// <summary>The still to show for it, or null where the host made none (a video too long, ffmpeg missing).</summary>
        public string? Still { get; }
    }

    /// <summary>The photos and videos a task was given, from its execution's view, in the order the journal holds them.</summary>
    public static class TaskMedia
    {
        public static IReadOnlyList<TaskMediaItem> Of(ExecutionView? execution)
        {
            var items = new List<TaskMediaItem>();
            if (execution?.Media is not { } media) return items;
            foreach (var given in media)
            {
                if (given == null || string.IsNullOrEmpty(given.Path)) continue;
                items.Add(new TaskMediaItem(given.Path, KindOf(given.MediaType), string.IsNullOrEmpty(given.Still) ? null : given.Still));
            }
            return items;
        }

        public static TaskMediaKind KindOf(MediaType type) => type switch
        {
            MediaType.ImagePng or MediaType.ImageJpeg or MediaType.ImageWebp or MediaType.ImageHeic => TaskMediaKind.Photo,
            MediaType.VideoMp4 or MediaType.VideoQuicktime => TaskMediaKind.Video,
            _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, "Unhandled media type."),
        };
    }
}
