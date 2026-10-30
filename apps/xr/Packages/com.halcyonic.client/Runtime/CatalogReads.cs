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
    /// When the gallery asks for the catalog, on the main thread: each time the person opens their projects,
    /// and when they refresh, never on a timer (AUDIT's S2): each read can make the computer read folders
    /// and ask Phat Porcupine, and the source syncs on its own schedule, so asking more often shows nothing
    /// newer. The last answer stays shown while a new one is read and after the gallery closes, and a
    /// failed read says why.
    /// </summary>
    public sealed class CatalogFeed
    {
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

        /// <summary>
        /// The person opened their projects: asks once as the gallery goes from closed to open, unless a
        /// read is already under way. Saying it is open again while it is open asks nothing.
        /// </summary>
        public void Open(DateTimeOffset now)
        {
            if (IsOpen) return;
            IsOpen = true;
            if (pending == null) Ask(now);
        }

        /// <summary>The person asked again.</summary>
        public void Refresh(DateTimeOffset now) => Ask(now);

        /// <summary>The gallery is hidden: nothing is asked until it opens again.</summary>
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

    /// <summary>
    /// Answers the gallery's catalog reads from what is played (<see cref="DemonstrationFallback.CatalogAt"/>):
    /// the demonstration's sample catalog, recorded and marked simulated; while a recorded run plays, or
    /// before a recording is read, an answer that there is none, also recorded, so the gallery says the
    /// other projects aren't part of this recording and names no source. Nothing leaves the device.
    /// </summary>
    public sealed class DemonstrationCatalogReads : ICatalogReader
    {
        /// <summary>Why a recording has no catalog to give.</summary>
        public const string NotRecorded = "This recording holds no catalog.";

        private readonly Func<DateTimeOffset, AvailableCatalog?> catalogAt;
        private readonly Func<DateTimeOffset> clock;

        public DemonstrationCatalogReads(Func<DateTimeOffset, AvailableCatalog?> catalogAt, Func<DateTimeOffset>? clock = null)
        {
            this.catalogAt = catalogAt;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public Task<IntelligenceRead<CatalogResponse>> ReadCatalogAsync(CancellationToken cancellationToken = default)
        {
            var now = clock();
            CatalogResponse answer = catalogAt(now)
                ?? (CatalogResponse)new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_recorded", Message = NotRecorded } };
            return Task.FromResult(new IntelligenceRead<CatalogResponse>(answer, now, recorded: true));
        }
    }
}
