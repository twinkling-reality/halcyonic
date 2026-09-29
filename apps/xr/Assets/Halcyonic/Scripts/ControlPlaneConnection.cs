#nullable enable
using System;
using System.IO;
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

        /// <summary>Raised on the main thread after received messages were applied, or another session is shown.</summary>
        public event Action<StateChanges>? Changed;

        /// <summary>The session shown: the control plane's, or the demonstration's.</summary>
        public RealtimeSession? Session => sessions?.Current;

        /// <summary>
        /// What the line above the stage says while the demonstration is shown: that it is recorded,
        /// not live, and that nothing done reaches an agent. Null while the control plane is shown.
        /// </summary>
        public string? DemonstrationLine => sessions?.Line;

        /// <summary>Why no session could be started, for display.</summary>
        public string? SetupProblem { get; private set; }

        private void OnEnable()
        {
            var client = new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            };
            RealtimeSession? controlPlane = null;
            var token = ControlPlaneSettings.ReadAccessToken();
            if (token == null)
            {
                Log("no access token, so no control plane is configured. Looked in: " + string.Join(", ", ControlPlaneSettings.TokenPaths()));
            }
            else
            {
                controlPlane = new RealtimeSession(new RealtimeSessionOptions(ControlPlaneSettings.Endpoint, token, client));
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
            if (!changes.IsEmpty) Changed?.Invoke(changes);
        }

        private static RealtimeSession? LoadDemonstration(ClientInfo client)
        {
            var asset = Resources.Load<TextAsset>(DemonstrationResource);
            if (asset == null)
            {
                Debug.LogError("Halcyonic: the demonstration is missing from the build.");
                return null;
            }
            try
            {
                return DemonstrationTransport.CreateSession(DemonstrationRecording.Parse(asset.text), client);
            }
            catch (InvalidDataException error)
            {
                Debug.LogError("Halcyonic: the demonstration cannot be played. " + error.Message);
                return null;
            }
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

        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", message);

        private void OnApplicationPause(bool paused)
        {
            // A sleeping headset loses its sockets: the session stops, then resumes from the last
            // position. Unity also reports resumes without a pause, which the session ignores.
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
