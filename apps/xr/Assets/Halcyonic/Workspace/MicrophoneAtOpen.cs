#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The microphone's question at the first open (ADR 0021, amended 2026-11-11; the rules in <see cref="MicrophoneAsk"/>):
    /// only where voice is offered (<see cref="MenuDirector.VoiceOffered"/>: connected, no demonstration of any kind),
    /// one line that says why
    /// (<see cref="Explaining"/>, for the host to show), then, once the app has focus, the system's question. The answer
    /// is kept on the headset, so no later open asks again. Hold to talk reads <see cref="Access"/> before it records.
    /// Every build, as hold to talk is since the owner's decision; a headset with no computer, as a judge's, is never
    /// asked. A launch with the setting <c>halcyonic.noask=true</c> skips the open's question (nothing may press a
    /// system dialog in a driven run).
    /// </summary>
    public sealed class MicrophoneAtOpen : MonoBehaviour
    {
        /// <summary>Where the answer is kept on the headset.</summary>
        public const string AnsweredKey = "halcyonic.microphone.answered";

        /// <summary>The development launch setting that skips the open's question.</summary>
        public const string NoAskExtra = "halcyonic.noask";

        /// <summary>How long the line stands before the system's question, so it can be read.</summary>
        public const float ExplainSeconds = 3f;

        /// <summary>How often the open looks again whether voice is offered, in seconds.</summary>
        public const float LookSeconds = 2f;

        /// <summary>The line to show before the question, or null when it should go.</summary>
        public static event Action<string?>? Explaining;

        /// <summary>The system answered the open's question.</summary>
        public static event Action<MicrophoneAnswer>? Answered;

        private float explainedAt = -1f;
        private float lookedAt = -LookSeconds;
        private volatile int answer;
        private bool asked;
        private MenuDirector? director;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!MicrophoneAsk.WaitsAtOpen(Access(), DrivenLaunch())) return;
            var host = new GameObject(nameof(MicrophoneAtOpen)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<MicrophoneAtOpen>();
        }

        private void Update()
        {
            if (explainedAt < 0f)
            {
                // Asked only where voice is offered: never with no computer connected (a judge's headset with none), nor over
                // any demonstration, recorded or for a computer that cannot be reached.
                if (Time.unscaledTime - lookedAt < LookSeconds) return;
                lookedAt = Time.unscaledTime;
                if (!MayAskNow()) return;
                explainedAt = Time.unscaledTime;
                Explaining?.Invoke(MicrophoneAsk.Why);
                return;
            }
            if (!asked)
            {
                // Another system dialog (the room's permission, for one) holds focus: wait for it to close.
                if (Time.unscaledTime - explainedAt < ExplainSeconds || !Application.isFocused) return;
                if (!MayAskNow())
                {
                    // Voice stopped being offered while the line stood (a demonstration began): take the line back and wait.
                    explainedAt = -1f;
                    Explaining?.Invoke(null);
                    return;
                }
                asked = true;
                Ask();
                return;
            }
            var given = answer;
            if (given == 0) return;
            var result = (MicrophoneAnswer)(given - 1);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (result == MicrophoneAnswer.Denied && !Permission.ShouldShowRequestPermissionRationale(Permission.Microphone))
                result = MicrophoneAnswer.DeniedForGood;
#endif
            if (MicrophoneAsk.CountsAsAnswer(result))
            {
                PlayerPrefs.SetInt(AnsweredKey, 1);
                PlayerPrefs.Save();
            }
            Explaining?.Invoke(null);
            Answered?.Invoke(result);
            Destroy(gameObject);
        }

        /// <summary>
        /// Whether the open asks now (<see cref="MicrophoneAsk.AskAtOpen"/>): only where the menu's director offers voice
        /// (<see cref="MenuDirector.VoiceOffered"/>); with no director found, voice counts as not offered.
        /// </summary>
        private bool MayAskNow()
        {
            if (director == null) director = FindAnyObjectByType<MenuDirector>();
            var offered = director != null && director.VoiceOffered;
            return MicrophoneAsk.AskAtOpen(Access(), DrivenLaunch(), offered);
        }

        private void Ask()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new PermissionCallbacks();
            // Answered on Android's thread; read in Update.
            callbacks.PermissionGranted += _ => answer = 1 + (int)MicrophoneAnswer.Granted;
            // Unity 6000.3 marks PermissionDeniedAndDontAskAgain unreliable: a denial is told apart in Update, on the
            // main thread, by the rationale.
            callbacks.PermissionDenied += _ => answer = 1 + (int)MicrophoneAnswer.Denied;
            callbacks.PermissionRequestDismissed += _ => answer = 1 + (int)MicrophoneAnswer.Dismissed;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#else
            answer = 1 + (int)MicrophoneAnswer.Granted;
#endif
        }

        /// <summary>The microphone's access now, from the system and the kept answer.</summary>
        public static MicrophoneAccess Access()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return MicrophoneAsk.Read(
                Permission.HasUserAuthorizedPermission(Permission.Microphone),
                PlayerPrefs.GetInt(AnsweredKey, 0) == 1,
                Permission.ShouldShowRequestPermissionRationale(Permission.Microphone));
#else
            return MicrophoneAccess.Allowed;
#endif
        }

        /// <summary>Records an answer given at a hold, so no open asks after it.</summary>
        public static void Remember(MicrophoneAnswer given)
        {
            if (!MicrophoneAsk.CountsAsAnswer(given)) return;
            PlayerPrefs.SetInt(AnsweredKey, 1);
            PlayerPrefs.Save();
        }

        private static bool DrivenLaunch()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>("getBooleanExtra", NoAskExtra, false);
            }
            catch (Exception)
            {
                return false;
            }
#else
            return true;
#endif
        }
    }
}
