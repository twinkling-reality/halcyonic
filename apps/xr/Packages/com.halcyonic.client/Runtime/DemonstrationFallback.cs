#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Why the recorded demonstration is shown instead of a control plane.</summary>
    public enum DemonstrationReason
    {
        /// <summary>No control plane is configured on this device, for example no access token.</summary>
        NotConfigured,

        /// <summary>A control plane is configured, but it has not been live since the fallback started.</summary>
        Unreachable,
    }

    /// <summary>
    /// Chooses the session to show. With no control plane configured, it is the recorded
    /// demonstration. With one configured, it is that control plane, except while the control plane
    /// has never been live since <see cref="Start"/> and its connection has failed: then the
    /// demonstration plays, and the control plane keeps being tried behind it. Once the control plane
    /// is live the demonstration stops for good, so a control plane that drops later shows its last
    /// known state as usual. A switch reaches consumers as a resynchronization, like a journal change.
    /// <para>
    /// With a recorded run as well (ADR 0012, JUDGE's D2), a judge meets the recorded run first: once
    /// its end has held, the demonstration follows, and once the demonstration's end has held, the
    /// recorded run again, each time a new player from its beginning. Both must be made not to loop
    /// (<see cref="DemonstrationOptions.Loop"/>), or a switch never comes.
    /// </para>
    /// </summary>
    public sealed class DemonstrationFallback : IDisposable
    {
        private static readonly PresetInstruction[] NoInstructions = new PresetInstruction[0];
        private readonly RealtimeSession? controlPlane;
        private readonly Func<DemonstrationPlayer?> createDemonstration;
        private readonly Func<DemonstrationPlayer?>? createRecordedRun;
        private DemonstrationPlayer? player;
        private bool showingRecordedRun;
        private bool recordedRunUnreadable;
        private Task retired = Task.CompletedTask;
        private bool demonstrationCreated;
        private bool controlPlaneWasLive;

        /// <param name="controlPlane">The configured control plane, not started; null when none is configured.</param>
        /// <param name="createDemonstration">
        /// Creates the demonstration's player, whose session is not started, the first time it is
        /// needed; returns null when there is no demonstration to play.
        /// </param>
        public DemonstrationFallback(RealtimeSession? controlPlane, Func<DemonstrationPlayer?> createDemonstration)
            : this(controlPlane, createDemonstration, null)
        {
        }

        /// <param name="controlPlane">The configured control plane, not started; null when none is configured.</param>
        /// <param name="createDemonstration">Creates the demonstration's player, whose session is not started.</param>
        /// <param name="createRecordedRun">
        /// Creates the recorded run's player, whose session is not started, each time it is shown; null,
        /// or returning null, when there is none, and the demonstration plays alone.
        /// </param>
        public DemonstrationFallback(RealtimeSession? controlPlane, Func<DemonstrationPlayer?> createDemonstration, Func<DemonstrationPlayer?>? createRecordedRun)
        {
            this.controlPlane = controlPlane;
            this.createDemonstration = createDemonstration;
            this.createRecordedRun = createRecordedRun;
        }

        /// <summary>The session to show, or null when there is neither a control plane nor a demonstration.</summary>
        public RealtimeSession? Current => Demonstration ?? controlPlane;

        /// <summary>The configured control plane, whether it is shown or not.</summary>
        public RealtimeSession? ControlPlane => controlPlane;

        /// <summary>The demonstration's session, while it is shown.</summary>
        public RealtimeSession? Demonstration => player?.Session;

        /// <summary>The demonstration's player, while it is shown.</summary>
        public DemonstrationPlayer? Player => player;

        /// <summary>Why the demonstration is shown, or null while it is not.</summary>
        public DemonstrationReason? Reason =>
            player == null ? (DemonstrationReason?)null
            : controlPlane == null ? DemonstrationReason.NotConfigured
            : DemonstrationReason.Unreachable;

        /// <summary>
        /// What the line above the stage says while the demonstration is shown; null while it is not.
        /// While a recording is still being read, or once it has failed to be read, it names that recording.
        /// </summary>
        public string? Line => Reason is DemonstrationReason reason
            ? Describe(reason, controlPlane?.Status, (player?.Ended ?? false) || (player?.Finished ?? false),
                player?.Recording?.IsRecordedRun ?? showingRecordedRun, next: RecordedRunFollows)
            : null;

        /// <summary>
        /// The instructions the demonstration offers for this execution where it stands, to show instead
        /// of a keyboard; empty while it offers none, or while it is not shown.
        /// </summary>
        public IReadOnlyList<PresetInstruction> InstructionsFor(string executionId) =>
            player?.InstructionsFor(executionId) ?? NoInstructions;

        /// <summary>
        /// The demonstration's usage limits as if read at <paramref name="now"/> (<see cref="DemonstrationRecording.UsageLimitsAt"/>);
        /// null while it is not shown, still loading, or holds none. Reading them sends nothing.
        /// </summary>
        public AvailableUsageLimits? UsageLimitsAt(DateTimeOffset now) => player?.Recording?.UsageLimitsAt(now);

        /// <summary>
        /// The demonstration's sample catalog as if read at <paramref name="now"/> (<see cref="DemonstrationRecording.CatalogAt"/>);
        /// null while nothing is played, while a recording is read or failed, and while a recorded run plays, which holds none.
        /// </summary>
        public AvailableCatalog? CatalogAt(DateTimeOffset now) => player?.Recording?.CatalogAt(now);

        public void Start()
        {
            if (controlPlane == null) ShowDemonstration();
            else controlPlane.Start();
        }

        /// <summary>
        /// Applies what arrived to both sessions, switches if needed, and returns what changed in the
        /// session shown, on the calling thread.
        /// </summary>
        public StateChanges Pump()
        {
            var demonstration = Demonstration;
            if (controlPlane == null) return Done() ? Switched(ShowNext()) : demonstration?.Pump() ?? new StateChanges();
            var fromControlPlane = controlPlane.Pump();
            if (controlPlane.Status.IsLive || fromControlPlane.Resynchronized) controlPlaneWasLive = true;
            if (demonstration != null)
            {
                if (controlPlaneWasLive)
                {
                    retired = demonstration.StopAsync();
                    player = null;
                    return Switched(fromControlPlane);
                }
                if (Done()) return Switched(ShowNext());
                var shown = demonstration.Pump();
                // The line says why the control plane is not shown, so it changes with its status.
                if (fromControlPlane.ConnectionChanged) shown.ConnectionChanged = true;
                return shown;
            }
            var failed = controlPlane.Status.Phase == ConnectionPhase.WaitingToRetry
                || controlPlane.Status.Phase == ConnectionPhase.Refused;
            if (!controlPlaneWasLive && failed && ShowDemonstration()) return Switched(new StateChanges());
            return fromControlPlane;
        }

        /// <summary>Follows the application's pause state in both sessions.</summary>
        public Task SetPausedAsync(bool paused) =>
            Task.WhenAll(
                controlPlane?.SetPausedAsync(paused) ?? Task.CompletedTask,
                Demonstration?.SetPausedAsync(paused) ?? Task.CompletedTask);

        public Task StopAsync() =>
            Task.WhenAll(
                controlPlane?.StopAsync() ?? Task.CompletedTask,
                Demonstration?.StopAsync() ?? Task.CompletedTask,
                retired);

        public void Dispose()
        {
            controlPlane?.Dispose();
            Demonstration?.Dispose();
        }

        /// <summary>
        /// The line above the stage while the demonstration is shown. Its first sentence names what plays
        /// and says nothing is live; its second, that the demonstration follows the person's answers, or
        /// that a recorded run waits only where the agent waited, and that nothing reaches an agent. Once a
        /// recording has ended, an end line takes the second sentence's place, saying what comes next and
        /// still that nothing reaches an agent, so the banner keeps to its rows with a task waiting.
        /// </summary>
        public static string Describe(DemonstrationReason reason, ConnectionStatus? controlPlane, bool ended = false, bool recordedRun = false, bool next = false)
        {
            var first = recordedRun
                ? "Recorded: real agent work, played back here. Nothing here is live."
                : "Demo: recorded work played on this headset. Nothing here is live.";
            var second = !ended
                ? recordedRun ? "It waits only where the agent waited." : "It follows your answers."
                : !next ? (recordedRun ? "Run ended." : "Demo ended.") + " It starts again soon."
                : recordedRun ? "Run ended. Next, a demo you can answer." : "Demo ended. Next, the recorded run.";
            var line = first + "\n" + second + " Nothing reaches an agent.";
            if (reason == DemonstrationReason.NotConfigured) return line;
            return line + "\n" + ConnectionText.WhyNotLive(controlPlane);
        }

        /// <summary>
        /// Only the demonstration's first start: once shown and stopped, it is never shown again. The
        /// recorded run comes first when there is one.
        /// </summary>
        private bool ShowDemonstration()
        {
            if (demonstrationCreated) return false;
            demonstrationCreated = true;
            player = createRecordedRun?.Invoke();
            showingRecordedRun = player != null;
            player ??= createDemonstration();
            player?.Session.Start();
            return player != null;
        }

        /// <summary>
        /// The recording shown has finished, or is a recorded run that can't be read while the
        /// demonstration can still be shown in its place.
        /// </summary>
        private bool Done() =>
            player != null && (player.Finished || (showingRecordedRun && player.Loading.IsFaulted));

        /// <summary>
        /// Whether a recorded run takes turns with the demonstration: there is one, and it has not failed
        /// to be read. One that could not be read is not read again while the app runs.
        /// </summary>
        private bool RecordedRunFollows => createRecordedRun != null && !recordedRunUnreadable;

        /// <summary>
        /// The recording shown has finished: the other one follows from its beginning, or, when it can't
        /// be made, the same one again. Returns what the stopped one's last pump reported.
        /// </summary>
        private StateChanges ShowNext()
        {
            var finished = player!;
            var changes = finished.Session.Pump();
            retired = Task.WhenAll(retired, finished.Session.StopAsync());
            if (showingRecordedRun && finished.Loading.IsFaulted) recordedRunUnreadable = true;
            var run = RecordedRunFollows ? createRecordedRun : null;
            var order = showingRecordedRun
                ? new[] { (false, createDemonstration), (true, run) }
                : new[] { (true, run), (false, createDemonstration) };
            player = null;
            foreach (var (recordedRun, create) in order)
            {
                if (create == null || !(create() is DemonstrationPlayer next)) continue;
                player = next;
                showingRecordedRun = recordedRun;
                break;
            }
            player?.Session.Start();
            return changes;
        }

        /// <summary>Another session is shown now, so consumers redraw everything, as after a journal change.</summary>
        private static StateChanges Switched(StateChanges changes)
        {
            changes.Resynchronized = true;
            changes.ConnectionChanged = true;
            return changes;
        }
    }
}
