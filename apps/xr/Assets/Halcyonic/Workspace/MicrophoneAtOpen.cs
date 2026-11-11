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

        /// <summary>How often a question asked looks for the system's answer, in seconds.</summary>
        public const float AnswerSeconds = 0.25f;

        private volatile int answer;
        private MenuDirector? director;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Read once here, never per frame: a headset never asked and not driven by settings. In the Editor access
            // reads allowed, so nothing installs there, nor in any render check.
            if (!MicrophoneAsk.WaitsAtOpen(Access(), DrivenLaunch())) return;
            var host = new GameObject(nameof(MicrophoneAtOpen)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<MicrophoneAtOpen>();
        }

        // No Update: every wait is one scheduled callback, so no frame does any work for this.
        private void Start() => InvokeRepeating(nameof(Look), LookSeconds, LookSeconds);

        /// <summary>
        /// Every few seconds until voice is offered: never with no computer connected (a judge's headset with none), nor
        /// over any demonstration, recorded or for a computer that cannot be reached. Then the line, and the question
        /// once it can be read.
        /// </summary>
        private void Look()
        {
            if (!VoiceOffered()) return;
            CancelInvoke(nameof(Look));
            Explaining?.Invoke(MicrophoneAsk.Why);
            Invoke(nameof(AskWhenFocused), ExplainSeconds);
        }

        private void AskWhenFocused()
        {
            if (!VoiceOffered())
            {
                // Voice stopped being offered while the line stood (a demonstration began): take the line back and wait.
                Explaining?.Invoke(null);
                InvokeRepeating(nameof(Look), LookSeconds, LookSeconds);
                return;
            }
            // Another system dialog (the room's permission, for one) holds focus: wait for it to close.
            if (!Application.isFocused)
            {
                Invoke(nameof(AskWhenFocused), LookSeconds);
                return;
            }
            if (!MicrophoneAsk.WaitsAtOpen(Access(), drivenLaunch: false))
            {
                // Allowed or answered meanwhile (at a hold, for one): nothing to ask.
                Explaining?.Invoke(null);
                Destroy(gameObject);
                return;
            }
            Ask();
            InvokeRepeating(nameof(TakeAnswer), AnswerSeconds, AnswerSeconds);
        }

        private void TakeAnswer()
        {
            var given = answer;
            if (given == 0) return;
            CancelInvoke(nameof(TakeAnswer));
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
        /// Whether the menu's director offers voice now (<see cref="MenuDirector.VoiceOffered"/>, the rule
        /// <see cref="MicrophoneAsk.AskAtOpen"/> takes); with no director found, voice counts as not offered. Nothing of
        /// the system is read here.
        /// </summary>
        private bool VoiceOffered()
        {
            if (director == null) director = FindAnyObjectByType<MenuDirector>();
            return MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false, director != null && director.VoiceOffered);
        }

        private void Ask()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new PermissionCallbacks();
            // Answered on Android's thread; read in TakeAnswer.
            callbacks.PermissionGranted += _ => answer = 1 + (int)MicrophoneAnswer.Granted;
            // Unity 6000.3 marks PermissionDeniedAndDontAskAgain unreliable: a denial is told apart in TakeAnswer, on the
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
