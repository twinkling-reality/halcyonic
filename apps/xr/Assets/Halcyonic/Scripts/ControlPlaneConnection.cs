#nullable enable
using System;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Owns the realtime session for the scene. Networking runs in the background; this component
    /// applies what arrived once per frame on the main thread and announces what changed.
    /// </summary>
    public sealed class ControlPlaneConnection : MonoBehaviour
    {
        private RealtimeSession? session;

        /// <summary>Raised on the main thread after received messages were applied.</summary>
        public event Action<StateChanges>? Changed;

        public RealtimeSession? Session => session;

        /// <summary>Why no session could be started, for display.</summary>
        public string? SetupProblem { get; private set; }

        private void OnEnable()
        {
            var token = ControlPlaneSettings.ReadAccessToken();
            if (token == null)
            {
                SetupProblem = "No access token. Start the control plane with pnpm dev, then restart.";
                Debug.LogWarning("Halcyonic: " + SetupProblem + " Looked in: " + string.Join(", ", ControlPlaneSettings.TokenPaths()));
                return;
            }
            var client = new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            };
            session = new RealtimeSession(new RealtimeSessionOptions(ControlPlaneSettings.Endpoint, token, client));
            session.Start();
        }

        private void Update()
        {
            if (session == null) return;
            var changes = session.Pump();
            if (!changes.IsEmpty) Changed?.Invoke(changes);
        }

        private void OnApplicationPause(bool paused)
        {
            // A sleeping headset loses its sockets: the session stops, then resumes from the last
            // position. Unity also reports resumes without a pause, which the session ignores.
            if (session != null) Report(session.SetPausedAsync(paused));
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
            if (session == null) return;
            _ = session.StopAsync();
            session = null;
        }
    }
}
