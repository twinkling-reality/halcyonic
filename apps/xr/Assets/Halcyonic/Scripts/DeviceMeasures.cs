#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.XR;

namespace Halcyonic.XR
{
    /// <summary>
    /// Logs what a device session needs to know about the headset itself, in numbers only: when the
    /// first frame came after the app started, the field of view of each eye once the headset
    /// renders in stereo, and every minute the frames drawn, the slowest and how many fell below 60 a
    /// second (<see cref="FrameTally"/>), and pauses and resumes with how long the app was away. The
    /// field measured becomes <see cref="ViewField.Current"/>, which the layout keeps inside.
    /// <c>tooling/quest/session.ts</c> reads these lines; see XR_DEVELOPMENT.md, "Device measures on a
    /// Quest". Each frame allocates nothing.
    /// </summary>
    public sealed class DeviceMeasures : MonoBehaviour
    {
        private readonly FrameTally tally = new FrameTally();
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private bool firstFrame = true;
        private int fieldTries;
        private float fieldAfter;
        private double pausedAt = -1;

        private void Update()
        {
            if (firstFrame)
            {
                firstFrame = false;
                Log("first frame " + Milliseconds(Time.realtimeSinceStartupAsDouble) + " ms after start");
                fieldAfter = Time.unscaledTime + 3f;
                return;
            }
            if (fieldTries < 10 && Time.unscaledTime >= fieldAfter) LogField();
            if (tally.Add(Time.unscaledDeltaTime))
            {
                Log(tally.Line());
                tally.Reset();
                ReadRefresh();
            }
        }

        private void LogField()
        {
            // Until the headset renders in stereo, which may take a few seconds, try again.
            fieldTries++;
            fieldAfter = Time.unscaledTime + 3f;
            ReadRefresh();
            var eyes = Camera.main;
            if (eyes == null || !eyes.stereoEnabled) return;
            fieldTries = int.MaxValue;
            var left = Field(eyes.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left));
            var right = Field(eyes.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right));
            var both = ViewField.Both(left, right);
            Log("view field left eye " + left.Line() + ", right eye " + right.Line()
                + ", both " + both.Across.ToString("0.0", CultureInfo.InvariantCulture)
                + " across " + both.Tall.ToString("0.0", CultureInfo.InvariantCulture) + " tall");
            // The panels keep inside it from now on (WorkspacePlacement.Lowest).
            ViewField.Current = both;
        }

        private static ViewField Field(Matrix4x4 projection) =>
            ViewField.FromProjection(projection.m00, projection.m02, projection.m11, projection.m12);

        private void ReadRefresh()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (display.running && display.TryGetDisplayRefreshRate(out var hertz) && hertz > 0)
                {
                    tally.RefreshHertz = hertz;
                    return;
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (paused)
            {
                pausedAt = now;
                Log("paused");
                return;
            }
            if (pausedAt < 0) return;
            Log("resumed after " + Milliseconds(now - pausedAt) + " ms");
            pausedAt = -1;
            tally.Reset();
        }

        private static string Milliseconds(double seconds) =>
            (seconds * 1000).ToString("0", CultureInfo.InvariantCulture);

        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: device {0}", message);
    }
}
