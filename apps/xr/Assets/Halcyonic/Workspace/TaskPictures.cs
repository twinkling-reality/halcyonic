#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The stills of photos and videos given to tasks, read from the computer (ControlPlaneApi.GetPictureAsync) and decoded once
    /// for the cards that draw them (RoomCardView.Pictures). A still is read once while it is kept; a read the computer refuses
    /// (the file left the project, no picture it reads, one this headset does not draw) is never asked again for that still, and
    /// its card draws its own glyph; a read that could not reach the computer is asked again after <see cref="RetrySeconds"/>.
    /// At most <see cref="MostKept"/> are kept, the oldest let go first, and every one goes with the journal it was read in.
    /// </summary>
    public sealed class TaskPictures
    {
        public const int MostKept = 8;

        public const float RetrySeconds = 10f;

        private sealed class Kept
        {
            public Texture2D? Picture;
            public bool Asking;
            public bool Refused;
            public float AskAgainAt;
        }

        private readonly Func<ControlPlaneApi?> api;
        private readonly Func<string?> journal;
        private readonly Func<float> clock;
        private readonly Dictionary<TaskStill, Kept> kept = new Dictionary<TaskStill, Kept>();
        private readonly List<TaskStill> order = new List<TaskStill>();
        private string? keptFor;

        public TaskPictures(Func<ControlPlaneApi?> api, Func<string?> journal, Func<float>? clock = null)
        {
            this.api = api;
            this.journal = journal;
            this.clock = clock ?? (() => Time.unscaledTime);
        }

        /// <summary>A still read and decoded: its card is laid again to draw it.</summary>
        public event Action? Changed;

        /// <summary>The still's picture where it is read; else null, and its read is asked where none is under way or refused.</summary>
        public Texture2D? Get(TaskStill still)
        {
            var journalId = journal();
            if (journalId != keptFor) Clear(journalId);
            if (kept.TryGetValue(still, out var known))
            {
                if (known.Picture != null || known.Asking || known.Refused || clock() < known.AskAgainAt) return known.Picture;
            }
            else
            {
                known = new Kept();
                kept[still] = known;
                order.Add(still);
                while (order.Count > MostKept) Forget(order[0]);
            }
            if (api() is not ControlPlaneApi reader || journalId == null) return null;
            known.Asking = true;
            _ = Read(reader, journalId, still, known);
            return null;
        }

        private async Task Read(ControlPlaneApi reader, string journalId, TaskStill still, Kept known)
        {
            byte[]? bytes = null;
            try
            {
                bytes = await reader.GetPictureAsync(still.ProjectId, journalId, still.Path);
            }
            catch (ControlPlaneRequestException refused)
            {
                // The computer's answer is final for this still; not reaching it is not.
                if (refused.Code != null) known.Refused = true;
                else known.AskAgainAt = clock() + RetrySeconds;
            }
            catch (Exception)
            {
                known.AskAgainAt = clock() + RetrySeconds;
            }
            known.Asking = false;
            if (bytes == null || !ReferenceEquals(kept.TryGetValue(still, out var now) ? now : null, known)) return;
            var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Task still", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            if (!picture.LoadImage(bytes, markNonReadable: true))
            {
                Release(picture);
                known.Refused = true;
                Debug.LogWarning("Halcyonic: a task's still came back as no picture this headset decodes; its card draws its glyph.");
                return;
            }
            known.Picture = picture;
            Changed?.Invoke();
        }

        /// <summary>Every still let go, as the journal they were read in changes or the director leaves.</summary>
        public void Clear(string? journalId = null)
        {
            foreach (var still in order.ToArray()) Forget(still);
            keptFor = journalId;
        }

        private void Forget(TaskStill still)
        {
            if (kept.TryGetValue(still, out var known)) Release(known.Picture);
            kept.Remove(still);
            order.Remove(still);
        }

        private static void Release(Texture2D? picture)
        {
            if (picture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(picture);
            else UnityEngine.Object.DestroyImmediate(picture);
        }
    }
}
