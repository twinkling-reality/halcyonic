#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where the gallery reads the person's catalog (ADR 0039): the control plane over REST
    /// (<see cref="ControlPlaneApi"/>), which reads it through from its source and never journals it.
    /// </summary>
    public interface ICatalogReader
    {
        Task<IntelligenceRead<CatalogResponse>> ReadCatalogAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// When the gallery asks for the catalog, on the main thread: when it opens holding no answer or one
    /// older than <see cref="Interval"/>, when the person refreshes, and again each interval while it stays
    /// open. The source syncs on its own schedule, so asking more often shows nothing newer. The last
    /// answer stays shown while a new one is read and after the gallery closes, and a failed read says why.
    /// </summary>
    public sealed class CatalogFeed
    {
        /// <summary>How long an answer is kept before an open gallery asks again.</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        private readonly Func<CancellationToken, Task<IntelligenceRead<CatalogResponse>>> read;
        private readonly Func<string> accessRefused;
        private Task<IntelligenceRead<CatalogResponse>>? pending;
        private CancellationTokenSource? cancel;

        /// <param name="accessRefused">How a refused credential is said for how this headset reaches the computer (<see cref="ControlPlaneApi.AccessRefused"/>).</param>
        public CatalogFeed(Func<CancellationToken, Task<IntelligenceRead<CatalogResponse>>> read, Func<string>? accessRefused = null)
        {
            this.read = read;
            this.accessRefused = accessRefused ?? (() => ConnectionText.AccessRefused);
        }

        public bool IsOpen { get; private set; }

        /// <summary>The latest answer, kept while a new one is read.</summary>
        public IntelligenceRead<CatalogResponse>? Last { get; private set; }

        public bool Loading => pending != null;

        /// <summary>Why the latest read failed, in Halcyonic's words after a colon (<see cref="IntelligenceText.WhyUnread"/>), or null.</summary>
        public string? Error { get; private set; }

        /// <summary>When it last asked, by the caller's clock.</summary>
        public DateTimeOffset? AskedAt { get; private set; }

        /// <summary>Changes whenever anything the gallery shows from it changes.</summary>
        public int Version { get; private set; }

        /// <summary>The gallery is shown.</summary>
        public void Open(DateTimeOffset now)
        {
            IsOpen = true;
            Tick(now);
        }

        /// <summary>Called as time passes while the gallery is shown: asks once the interval has passed.</summary>
        public void Tick(DateTimeOffset now)
        {
            if (!IsOpen || pending != null) return;
            if (AskedAt is DateTimeOffset asked && now - asked < Interval) return;
            Ask(now);
        }

        /// <summary>The person asked again.</summary>
        public void Refresh(DateTimeOffset now) => Ask(now);

        /// <summary>The gallery is hidden: nothing more is asked until it opens again.</summary>
        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>Applies a finished read, on the main thread. Returns whether anything shown changed.</summary>
        public bool Poll()
        {
            var task = pending;
            if (task == null || !task.IsCompleted) return false;
            pending = null;
            cancel?.Dispose();
            cancel = null;
            if (task.Status == TaskStatus.RanToCompletion)
            {
                Last = task.Result;
                Error = null;
            }
            else
            {
                Error = IntelligenceText.WhyUnread(task.IsCanceled ? new OperationCanceledException() : task.Exception?.GetBaseException(), accessRefused());
            }
            Version++;
            return true;
        }

        private void Ask(DateTimeOffset now)
        {
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = new CancellationTokenSource();
            AskedAt = now;
            try
            {
                pending = read(cancel.Token);
            }
            catch (Exception error)
            {
                pending = Task.FromException<IntelligenceRead<CatalogResponse>>(error);
            }
            Version++;
        }
    }
}
