#nullable enable
using System;
using System.Threading.Tasks;

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
    /// </summary>
    public sealed class DemonstrationFallback : IDisposable
    {
        private readonly RealtimeSession? controlPlane;
        private readonly Func<RealtimeSession?> createDemonstration;
        private RealtimeSession? demonstration;
        private Task retired = Task.CompletedTask;
        private bool demonstrationCreated;
        private bool controlPlaneWasLive;

        /// <param name="controlPlane">The configured control plane, not started; null when none is configured.</param>
        /// <param name="createDemonstration">
        /// Creates the demonstration session, not started, the first time it is needed; returns null
        /// when there is no demonstration to play.
        /// </param>
        public DemonstrationFallback(RealtimeSession? controlPlane, Func<RealtimeSession?> createDemonstration)
        {
            this.controlPlane = controlPlane;
            this.createDemonstration = createDemonstration;
        }

        /// <summary>The session to show, or null when there is neither a control plane nor a demonstration.</summary>
        public RealtimeSession? Current => demonstration ?? controlPlane;

        /// <summary>The configured control plane, whether it is shown or not.</summary>
        public RealtimeSession? ControlPlane => controlPlane;

        /// <summary>The demonstration, while it is shown.</summary>
        public RealtimeSession? Demonstration => demonstration;

        /// <summary>Why the demonstration is shown, or null while it is not.</summary>
        public DemonstrationReason? Reason =>
            demonstration == null ? (DemonstrationReason?)null
            : controlPlane == null ? DemonstrationReason.NotConfigured
            : DemonstrationReason.Unreachable;

        /// <summary>What the line above the stage says while the demonstration is shown; null while it is not.</summary>
        public string? Line => Reason is DemonstrationReason reason ? Describe(reason, controlPlane?.Status) : null;

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
            if (controlPlane == null) return demonstration?.Pump() ?? new StateChanges();
            var fromControlPlane = controlPlane.Pump();
            if (controlPlane.Status.IsLive || fromControlPlane.Resynchronized) controlPlaneWasLive = true;
            if (demonstration != null)
            {
                if (controlPlaneWasLive)
                {
                    retired = demonstration.StopAsync();
                    demonstration = null;
                    return Switched(fromControlPlane);
                }
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
                demonstration?.SetPausedAsync(paused) ?? Task.CompletedTask);

        public Task StopAsync() =>
            Task.WhenAll(
                controlPlane?.StopAsync() ?? Task.CompletedTask,
                demonstration?.StopAsync() ?? Task.CompletedTask,
                retired);

        public void Dispose()
        {
            controlPlane?.Dispose();
            demonstration?.Dispose();
        }

        /// <summary>
        /// The line above the stage while the demonstration is shown. It says, in words, that nothing
        /// is live and that nothing done reaches an agent.
        /// </summary>
        public static string Describe(DemonstrationReason reason, ConnectionStatus? controlPlane)
        {
            const string demonstration = "Demonstration: recorded work played on this device, not live.\n"
                + "Nothing done here reaches an agent.";
            if (reason == DemonstrationReason.NotConfigured) return demonstration;
            var detail = controlPlane?.Detail;
            var refused = controlPlane != null && controlPlane.Phase == ConnectionPhase.Refused;
            return demonstration
                + (refused ? "\nThe control plane refused this client." : "\nThe control plane is not reachable; trying again.")
                + (detail == null ? "" : " " + detail);
        }

        /// <summary>Only the demonstration's first start: once shown and stopped, it is never shown again.</summary>
        private bool ShowDemonstration()
        {
            if (demonstrationCreated) return false;
            demonstrationCreated = true;
            demonstration = createDemonstration();
            demonstration?.Start();
            return demonstration != null;
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
