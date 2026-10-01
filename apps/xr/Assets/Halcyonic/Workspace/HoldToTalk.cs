#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR && DEVELOPMENT_BUILD
using UnityEngine.Android;
#endif

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Hold to talk (ADR 0021): while a hold button is held, the microphone records; let go, and the
    /// clip goes to the Mac, which answers with a draft or says it heard nothing. Only one clip at a
    /// time, at most 30 seconds. The microphone permission is asked on the first hold, never at
    /// launch. Capture stops and the clip is discarded, never sent, when the hold is dropped or the
    /// app loses input focus (the rule agreed with the focus work). Compiled into development builds
    /// and the editor only, so a release player never uses the microphone and carries no
    /// RECORD_AUDIO; there <see cref="Offered"/> is false and nothing here runs.
    /// </summary>
    public sealed class HoldToTalk : MonoBehaviour
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>The words to show while talking, from <see cref="VoiceText"/>: listening, hearing, or why nothing came of it.</summary>
        public event Action<string>? Said;

        /// <summary>What the Mac heard: untrusted text, a draft to show through <see cref="LabelText"/> and confirm.</summary>
        public event Action<string>? Heard;

        private const int RequestedRate = SpeechClip.SampleRate;

        /// <summary>Recording, so a second hold anywhere waits for this one.</summary>
        private static HoldToTalk? capturing;

        private AudioClip? clip;
        private float startedAt;
        private ControlPlaneApi? api;
        private Task<TranscriptionResponse>? pending;
        private CancellationTokenSource? cancellation;
        private volatile int permissionAnswer;

        /// <summary>Voice is in this build: development builds and the editor.</summary>
        public static bool Offered => true;

        /// <summary>Recording, or waiting for the Mac's answer.</summary>
        public bool Busy => capturing == this || pending != null;

        /// <summary>The hold started: record, once the microphone may be used.</summary>
        public void Begin()
        {
            if (Busy || capturing != null || FocusGuard.InputSuspended) return;
            api = ControlPlaneSettings.Api();
            if (api == null)
            {
                Said?.Invoke(VoiceText.Unreachable);
                return;
            }
            if (!MicrophoneAllowed()) return;
            if (Microphone.devices.Length == 0)
            {
                Said?.Invoke(VoiceText.NoMicrophone);
                return;
            }
            Microphone.GetDeviceCaps(null, out var min, out var max);
            var rate = min == 0 && max == 0 ? RequestedRate : Mathf.Clamp(RequestedRate, min, max);
            // Not looping, a second longer than a clip may be, so the end is never overwritten.
            clip = Microphone.Start(null, false, (int)SpeechClip.MaxSeconds + 1, rate);
            if (clip == null)
            {
                Said?.Invoke(VoiceText.NoMicrophone);
                return;
            }
            capturing = this;
            startedAt = Time.unscaledTime;
            Said?.Invoke(VoiceText.Listening);
        }

        /// <summary>The hold ended: let go sends what was recorded; dropped discards it.</summary>
        public void End(bool send)
        {
            if (capturing != this || clip == null) return;
            var position = Microphone.GetPosition(null);
            Microphone.End(null);
            capturing = null;
            var recorded = clip;
            clip = null;
            if (!send)
            {
                Destroy(recorded);
                Said?.Invoke(VoiceText.Stopped);
                return;
            }
            var samples = new float[Math.Max(0, position) * recorded.channels];
            if (samples.Length > 0) recorded.GetData(samples, 0);
            var wav = SpeechClip.Encode(samples, samples.Length, recorded.channels, recorded.frequency);
            Destroy(recorded);
            if (wav == null)
            {
                Said?.Invoke(VoiceText.TooShort);
                return;
            }
            Said?.Invoke(VoiceText.Hearing);
            cancellation = new CancellationTokenSource();
            pending = api!.TranscribeAsync(wav, cancellation.Token);
        }

        /// <summary>Drops a recording or an answer still to come; nothing is shown for it.</summary>
        public void Drop()
        {
            if (capturing == this)
            {
                Microphone.End(null);
                capturing = null;
                if (clip != null) Destroy(clip);
                clip = null;
            }
            cancellation?.Cancel();
            cancellation = null;
            pending = null;
        }

        private void Update()
        {
            if (permissionAnswer != 0)
            {
                Said?.Invoke(permissionAnswer > 0 ? VoiceText.MicrophoneAllowed : VoiceText.NoMicrophone);
                permissionAnswer = 0;
            }
            if (capturing == this)
            {
                if (FocusGuard.InputSuspended) End(false);
                else if (Time.unscaledTime - startedAt >= SpeechClip.MaxSeconds) End(true);
            }
            if (pending == null || !pending.IsCompleted) return;
            var answered = pending;
            pending = null;
            cancellation?.Dispose();
            cancellation = null;
            if (answered.IsCanceled) return;
            if (answered.IsFaulted)
            {
                var refused = answered.Exception?.InnerException as ControlPlaneRequestException;
                Said?.Invoke(refused == null ? VoiceText.Unreachable : VoiceText.Refusal(refused.Code));
                return;
            }
            switch (answered.Result)
            {
                case HeardTranscription heard:
                    Heard?.Invoke(heard.Text);
                    break;
                default:
                    Said?.Invoke(VoiceText.NothingHeard);
                    break;
            }
        }

        private void OnDisable() => Drop();

        /// <summary>
        /// Whether the microphone may be used now. On the headset, the first hold asks for it and
        /// records nothing; the person holds again once they have allowed it.
        /// </summary>
        private bool MicrophoneAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone)) return true;
            var callbacks = new PermissionCallbacks();
            // Answered on Android's thread; shown from Update.
            callbacks.PermissionGranted += _ => permissionAnswer = 1;
            callbacks.PermissionDenied += _ => permissionAnswer = -1;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            Said?.Invoke(VoiceText.AllowMicrophone);
            return false;
#else
            return true;
#endif
        }
#else
        public event Action<string>? Said { add { } remove { } }

        public event Action<string>? Heard { add { } remove { } }

        /// <summary>Voice is in this build: development builds and the editor.</summary>
        public static bool Offered => false;

        public bool Busy => false;

        public void Begin()
        {
        }

        public void End(bool send)
        {
        }

        public void Drop()
        {
        }
#endif
    }
}
