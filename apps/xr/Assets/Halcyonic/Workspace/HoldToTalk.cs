#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Hold to talk (ADR 0021): while a hold button is held, the microphone records; let go, and the
    /// clip goes to the Mac, which answers with a draft or says it heard nothing. Only one clip at a
    /// time, at most 30 seconds. The microphone permission is asked on the first hold, never at
    /// launch. Capture stops and the clip is discarded, never sent, when the hold is dropped or the
    /// app loses input focus (the rule agreed with the focus work). In every build since the owner's
    /// decision (ADR 0021, amended): a release player carries RECORD_AUDIO, and voice is offered only
    /// while a computer is connected and no recording plays (MenuDirector.VoiceOffered), so a judge's
    /// headset with no computer is never asked for the microphone.
    /// </summary>
    public sealed class HoldToTalk : MonoBehaviour
    {
        /// <summary>The words to show while talking, from <see cref="VoiceText"/>: listening, hearing, or why nothing came of it.</summary>
        public event Action<string>? Said;

        /// <summary>What the Mac heard: untrusted text, a draft to show through <see cref="LabelText"/> and confirm.</summary>
        public event Action<string>? Heard;

        /// <summary>Capture a recording's scoped delivery before asking for its transcription.</summary>
        public Func<Action<string>>? CaptureHeard { get; set; }

        private Action<string>? heardForPending;

        private const int RequestedRate = SpeechClip.SampleRate;

        /// <summary>Recording, so a second hold anywhere waits for this one.</summary>
        private static HoldToTalk? capturing;

        private AudioClip? clip;
        private float startedAt;
        private ControlPlaneApi? api;
        private Task<TranscriptionResponse>? pending;
        private CancellationTokenSource? cancellation;
        private volatile int permissionAnswer;

        // The real level while recording (the lead's truth rule): the last 50 ms of what was recorded, read every 50 ms.
        private readonly LevelHistory level = new LevelHistory();
        private float[] levelSamples = new float[0];
        private float levelAt;

        /// <summary>The microphone's real level while this headset records; null when nothing records.</summary>
        public static LevelHistory? CurrentLevel => capturing?.level;

        /// <summary>Voice is in every build.</summary>
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
            level.Clear();
            levelAt = startedAt;
            var window = Mathf.CeilToInt(clip.frequency * LevelHistory.StepSeconds) * clip.channels;
            if (levelSamples.Length != window) levelSamples = new float[window];
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
            heardForPending = CaptureHeard?.Invoke();
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
            heardForPending = null;
        }

        private void Update()
        {
            if (permissionAnswer != 0)
            {
                Said?.Invoke(permissionAnswer > 0 ? VoiceText.MicrophoneAllowed : VoiceText.MicrophoneRefused);
                permissionAnswer = 0;
            }
            if (capturing == this)
            {
                if (FocusGuard.InputSuspended) End(false);
                else if (Time.unscaledTime - startedAt >= SpeechClip.MaxSeconds) End(true);
                else ReadLevel();
            }
            if (pending == null || !pending.IsCompleted) return;
            var answered = pending;
            var deliver = heardForPending;
            heardForPending = null;
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
                    if (deliver != null) deliver(heard.Text);
                    else Heard?.Invoke(heard.Text);
                    break;
                default:
                    Said?.Invoke(VoiceText.NothingHeard);
                    break;
            }
        }

        private void OnDisable() => Drop();

        /// <summary>Every 50 ms while recording, the RMS of the last 50 ms the microphone wrote into the clip.</summary>
        private void ReadLevel()
        {
            if (clip == null || Time.unscaledTime - levelAt < LevelHistory.StepSeconds) return;
            levelAt = Time.unscaledTime;
            var frames = levelSamples.Length / Mathf.Max(1, clip.channels);
            var position = Microphone.GetPosition(null);
            if (position < frames) return;
            clip.GetData(levelSamples, position - frames);
            level.Add(levelSamples, levelSamples.Length);
        }

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
    }
}
