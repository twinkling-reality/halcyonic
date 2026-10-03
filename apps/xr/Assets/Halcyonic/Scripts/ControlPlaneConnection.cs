#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Owns what the scene shows: the realtime session with the configured control plane, or the
    /// recorded demonstration while no control plane is configured or reachable
    /// (<see cref="DemonstrationFallback"/>). Networking runs in the background; this component
    /// applies what arrived once per frame on the main thread, announces what changed, and logs each
    /// change of what is shown and of each session's connection status.
    /// </summary>
    public sealed class ControlPlaneConnection : MonoBehaviour
    {
        /// <summary>The demonstration: a text asset under Resources, written by pnpm demonstration:record.</summary>
        private const string DemonstrationResource = "HalcyonicDemonstration";

        private DemonstrationFallback? sessions;
        private ConnectionStatus loggedControlPlane = ConnectionStatus.Stopped;
        private ConnectionStatus loggedDemonstration = ConnectionStatus.Stopped;
        private DemonstrationReason? loggedReason;
        private int loggedPlays;
        private bool loggedEnded;

        /// <summary>Raised on the main thread after received messages were applied, or another session is shown.</summary>
        public event Action<StateChanges>? Changed;

        /// <summary>The session shown: the control plane's, or the demonstration's.</summary>
        public RealtimeSession? Session => sessions?.Current;

        /// <summary>
        /// What the line above the stage says while the demonstration is shown: that it is recorded
        /// and simulated, not live, that it follows the person's answers, and that nothing reaches an
        /// agent. Null while the control plane is shown.
        /// </summary>
        public string? DemonstrationLine => sessions?.Line;

        /// <summary>The recorded demonstration's usage limits as if read now, while it is shown and holds them; null otherwise.</summary>
        public AvailableUsageLimits? DemonstrationUsageLimits(DateTimeOffset now) => DemonstrationLine != null ? sessions?.UsageLimitsAt(now) : null;

        /// <summary>Why no session could be started, for display.</summary>
        public string? SetupProblem { get; private set; }

        /// <summary>
        /// While the demonstration is shown, the instructions it recorded for this execution where it
        /// stands, to offer instead of a keyboard; empty otherwise. Typed text is answered with one of
        /// these, and says so.
        /// </summary>
        public IReadOnlyList<PresetInstruction> DemonstrationInstructions(string executionId) =>
            sessions?.InstructionsFor(executionId) ?? Array.Empty<PresetInstruction>();

        /// <summary>While the demonstration is shown, its recorded understanding and evaluation answers; null otherwise.</summary>
        public DemonstrationReads? DemonstrationReads => sessions?.Player?.Reads;

        private void OnEnable()
        {
            var client = new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            };
            RealtimeSession? controlPlane = null;
            var target = ControlPlaneSettings.Target();
            if (target == null)
            {
                Log("no pairing and no access token, so no control plane is configured. Looked in: " + string.Join(", ", ControlPlaneSettings.TokenPaths()));
            }
            else
            {
                if (target.Pairing != null) Log("connecting over the network to the control plane paired at " + target.Pairing.Address);
                controlPlane = target.CreateSession(client);
            }
            sessions = new DemonstrationFallback(controlPlane, () => LoadDemonstration(client));
            sessions.Start();
            if (sessions.Current == null)
            {
                SetupProblem = "No access token. Start the control plane with pnpm dev, then restart.";
                Debug.LogWarning("Halcyonic: " + SetupProblem);
            }
        }

        private void Update()
        {
            if (sessions == null) return;
            var changes = sessions.Pump();
            LogShown(sessions.Reason);
            LogStatus("connection", sessions.ControlPlane, ref loggedControlPlane);
            LogStatus("demonstration", sessions.Demonstration, ref loggedDemonstration);
            LogPlayback(sessions.Player);
            if (!changes.IsEmpty) Changed?.Invoke(changes);
        }

        /// <summary>
        /// Loads the text asset on the main thread, as Unity requires, and reads it on another thread,
        /// so the half megabyte of recording never holds up a frame; the demonstration's session opens
        /// once it has been read.
        /// </summary>
        private static DemonstrationPlayer? LoadDemonstration(ClientInfo client)
        {
            var loading = System.Diagnostics.Stopwatch.StartNew();
            var asset = Resources.Load<TextAsset>(DemonstrationResource);
            if (asset == null)
            {
                Debug.LogError("Halcyonic: the demonstration is missing from the build.");
                return null;
            }
            var text = asset.text;
            var loaded = loading.ElapsedMilliseconds;
            var reading = Task.Run(() =>
            {
                // How long a judge's cold start spends on the recording, in numbers only (pnpm quest:cold-start reads it).
                var parsing = System.Diagnostics.Stopwatch.StartNew();
                var recording = DemonstrationRecording.Parse(text);
                Debug.Log("Halcyonic: demonstration read " + text.Length / 1024 + " KiB, loaded in " + loaded + " ms on the main thread, parsed in "
                    + parsing.ElapsedMilliseconds + " ms on another");
                return recording;
            });
            _ = reading.ContinueWith(
                failed => Debug.LogError("Halcyonic: the demonstration cannot be played. " + failed.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
            return new DemonstrationPlayer(reading, client);
        }

        private void LogShown(DemonstrationReason? reason)
        {
            if (reason == loggedReason) return;
            loggedReason = reason;
            Log(reason switch
            {
                DemonstrationReason.NotConfigured => "showing the recorded demonstration, because no control plane is configured",
                DemonstrationReason.Unreachable => "showing the recorded demonstration, because the control plane has not been reachable",
                _ => "showing the control plane; the demonstration stopped",
            });
        }

        /// <summary>
        /// On a headset the log (logcat, tag Unity) is the main diagnostic. It gets the phase and its
        /// detail, as of the end of the frame, and nothing else: never the token, workstream titles,
        /// instructions or agent text.
        /// </summary>
        private void LogStatus(string session, RealtimeSession? shown, ref ConnectionStatus logged)
        {
            if (shown == null || ReferenceEquals(shown.Status, logged)) return;
            logged = shown.Status;
            Log(session + " " + (logged.Detail == null ? logged.Phase.ToString() : logged.Phase + ": " + logged.Detail));
        }

        /// <summary>Each start of the recording from its beginning, and each of its ends, with no content.</summary>
        private void LogPlayback(DemonstrationPlayer? player)
        {
            if (player == null) return;
            if (player.Plays != loggedPlays)
            {
                loggedPlays = player.Plays;
                Log("demonstration plays from its beginning (" + loggedPlays + ")");
            }
            if (player.Ended != loggedEnded)
            {
                loggedEnded = player.Ended;
                if (loggedEnded) Log("demonstration reached an end; it starts again after holding it");
            }
        }

        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", message);

        private void OnApplicationPause(bool paused)
        {
            // A sleeping headset loses its sockets: the session stops, then resumes from the last
            // position; the demonstration goes on where it stood. Unity also reports resumes
            // without a pause, which the session ignores.
            if (sessions != null) Report(sessions.SetPausedAsync(paused));
        }

        private static async void Report(Task transition)
        {
            try
            {
                await transition;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        private void OnDisable()
        {
            if (sessions == null) return;
            _ = sessions.StopAsync();
            sessions = null;
        }
    }
}
