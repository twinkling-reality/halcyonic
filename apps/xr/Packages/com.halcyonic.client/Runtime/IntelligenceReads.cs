#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// One answer about an execution's understanding or evaluation, as the control plane gave it: read
    /// from a control plane now, or played from the recorded demonstration.
    /// </summary>
    public sealed class IntelligenceRead<T> where T : class
    {
        public IntelligenceRead(T response, DateTimeOffset readAt, bool recorded)
        {
            Response = response;
            ReadAt = readAt;
            Recorded = recorded;
        }

        public T Response { get; }

        /// <summary>When the control plane answered: by this device's clock, or by the recording's for a recorded answer.</summary>
        public DateTimeOffset ReadAt { get; }

        /// <summary>Played from a recording rather than read from a control plane now.</summary>
        public bool Recorded { get; }
    }

    /// <summary>
    /// Where the workspace reads what the understanding and evaluation sources say about an execution:
    /// the control plane over REST (<see cref="ControlPlaneApi"/>), or the recorded demonstration
    /// (<see cref="DemonstrationReads"/>). Either answers with the control plane's own responses.
    /// </summary>
    public interface IIntelligenceReader
    {
        Task<IntelligenceRead<UnderstandingResponse>> ReadUnderstandingAsync(string executionId, CancellationToken cancellationToken);

        /// <summary>Each read spends three of Seorak's requests, so it is asked on demand, never on a timer.</summary>
        Task<IntelligenceRead<EvaluationResponse>> ReadEvaluationAsync(string executionId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// What one section of the workspace shows, and when it asks, on the main thread. It asks when the
    /// section is shown for an execution it holds no answer about, and when the person refreshes; given
    /// an interval to follow, it also asks again once the execution changed and the interval has
    /// passed since it last asked, which suits the understanding source. The evaluation source's reads
    /// are budgeted, so its section never follows: it asks when shown and refreshed, never on a timer.
    /// The last answer stays shown while a new one is read, and a read that fails says why.
    /// </summary>
    public sealed class IntelligenceFeed<T> where T : class
    {
        private readonly Func<string, CancellationToken, Task<IntelligenceRead<T>>> read;
        private Task<IntelligenceRead<T>>? pending;
        private CancellationTokenSource? cancel;
        private string? askedMark;

        public IntelligenceFeed(Func<string, CancellationToken, Task<IntelligenceRead<T>>> read)
        {
            this.read = read;
        }

        /// <summary>The execution the section is about, or null while the work has none.</summary>
        public string? ExecutionId { get; private set; }

        /// <summary>The latest answer about <see cref="ExecutionId"/>, kept while a new one is read.</summary>
        public IntelligenceRead<T>? Last { get; private set; }

        public bool Loading => pending != null;

        /// <summary>Why the latest read failed, or null.</summary>
        public string? Error { get; private set; }

        /// <summary>When it last asked, by the caller's clock.</summary>
        public DateTimeOffset? AskedAt { get; private set; }

        /// <summary>Changes whenever anything the section shows changes.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// The section is shown for this execution, which has changed as far as <paramref name="changeMark"/>
        /// says, for example its last update time. Asks unless it holds or awaits an answer about it; with
        /// <paramref name="follow"/>, also once the mark changed since it last asked and that long has passed.
        /// </summary>
        public void Show(string? executionId, string? changeMark, DateTimeOffset now, TimeSpan? follow = null)
        {
            if (executionId != ExecutionId)
            {
                Forget();
                ExecutionId = executionId;
                Version++;
            }
            if (ExecutionId == null || pending != null) return;
            if (Last == null && Error == null)
            {
                Ask(changeMark, now);
                return;
            }
            if (follow is TimeSpan interval && changeMark != askedMark && AskedAt is DateTimeOffset asked && now - asked >= interval)
            {
                Ask(changeMark, now);
            }
        }

        /// <summary>The person asked again.</summary>
        public void Refresh(string? changeMark, DateTimeOffset now)
        {
            if (ExecutionId != null) Ask(changeMark, now);
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
                // A read this feed abandoned is never polled, so a cancelled one timed out.
                Error = task.IsCanceled ? "The control plane did not answer in time." : task.Exception?.GetBaseException().Message ?? "No reason was given.";
            }
            Version++;
            return true;
        }

        /// <summary>Forgets the execution and everything read about it, for example when the workspace closes.</summary>
        public void Clear()
        {
            Forget();
            ExecutionId = null;
            Version++;
        }

        private void Ask(string? changeMark, DateTimeOffset now)
        {
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = new CancellationTokenSource();
            AskedAt = now;
            askedMark = changeMark;
            try
            {
                pending = read(ExecutionId!, cancel.Token);
            }
            catch (Exception error)
            {
                pending = Task.FromException<IntelligenceRead<T>>(error);
            }
            Version++;
        }

        private void Forget()
        {
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = null;
            pending = null;
            Last = null;
            Error = null;
            AskedAt = null;
            askedMark = null;
        }
    }

    /// <summary>
    /// Answers the workspace's reads of understanding and evaluation from the recorded demonstration:
    /// what the recording's control plane answered where the playback stands, a sibling of
    /// <see cref="DemonstrationTransport"/> for what a client reads over REST. Nothing leaves the
    /// device. Every answer is one a stand-in for a source gave and says so, and every read is marked
    /// recorded, with the recording's time.
    /// </summary>
    public sealed class DemonstrationReads : IIntelligenceReader
    {
        /// <summary>Why there is no answer where the playback stands.</summary>
        public const string NothingRecorded = "The demonstration recorded no answer about this work here.";

        private readonly DemonstrationPlayer player;

        internal DemonstrationReads(DemonstrationPlayer player)
        {
            this.player = player;
        }

        public Task<IntelligenceRead<UnderstandingResponse>> ReadUnderstandingAsync(string executionId, CancellationToken cancellationToken = default) =>
            Answer((recording, node, played) => recording.UnderstandingAt(executionId, node, played));

        public Task<IntelligenceRead<EvaluationResponse>> ReadEvaluationAsync(string executionId, CancellationToken cancellationToken = default) =>
            Answer((recording, node, played) => recording.EvaluationAt(executionId, node, played));

        private Task<IntelligenceRead<T>> Answer<T>(Func<DemonstrationRecording, int, int, RecordedAnswer<T>?> find) where T : class
        {
            var recording = player.Recording;
            if (recording == null)
            {
                return Task.FromException<IntelligenceRead<T>>(new ControlPlaneRequestException("The demonstration is still being read."));
            }
            var (node, played) = player.Where;
            var answer = find(recording, node, played);
            return answer == null
                ? Task.FromException<IntelligenceRead<T>>(new ControlPlaneRequestException(NothingRecorded))
                : Task.FromResult(new IntelligenceRead<T>(answer.Response, answer.ReadAt, recorded: true));
        }
    }
}
