#if DEVELOPMENT_BUILD || UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Development builds only, compiled out of every other build (<c>WorkspaceRender.CapturePresetsCheck</c> proves it):
    /// opens the installed app straight into a named view, so the device tool can capture a frame on the headset with
    /// nobody wearing it. It reads the launch intent's extras once, at start: <c>halcyonic.open</c> is
    /// <c>task:{execution id}:{section}</c> (activity, waiting, changes or checks), <c>screen:{project id}</c> or
    /// <c>gallery</c>; <c>halcyonic.compose</c> opens the task's instruction without the keyboard; <c>halcyonic.agent</c>
    /// chooses a registered runtime by its id; <c>halcyonic.arm</c> arms the waiting approval so its confirming bar shows;
    /// <c>halcyonic.bar</c> opens the screen's bar. Presentation only: it never sends a command, never confirms, and
    /// never approves, denies, answers, starts or previews anything; it arms only where a confirm step stands before the
    /// send, as an approval's always does. Ids and fixed words only: extras that are neither are ignored.
    /// It waits for the live snapshot to hold the id; one fixed log line then says what it applied, or that it found
    /// nothing, and the app is as a person would have left it.
    /// </summary>
    public sealed class CapturePresets : MonoBehaviour
    {
        /// <summary>How long it waits for the live snapshot to hold the id before it says it found nothing.</summary>
        private const float WaitSeconds = 60f;

        /// <summary>What <see cref="Apply"/> returns where the snapshot holds what the preset names but nothing opened.</summary>
        public const string Nothing = "";

        private static readonly Dictionary<string, FileSection> Sections = new Dictionary<string, FileSection>(StringComparer.Ordinal)
        {
            ["activity"] = FileSection.Activity,
            ["waiting"] = FileSection.Waiting,
            ["changes"] = FileSection.Changes,
            ["checks"] = FileSection.Checks,
        };

        /// <summary>What the extras ask for, read and checked.</summary>
        public sealed class Preset
        {
            public string? Execution;
            public FileSection? Section;
            public string? Project;
            public bool Gallery;
            public bool Compose;
            public string? Runtime;
            public bool Arm;
            public bool Bar;
        }

        private Preset preset = null!;
        private float waited;

        /// <summary>An id as the device tool can pass it: letters, digits and <c>._:/-</c> only, never a name.</summary>
        public static bool IsId(string? text) => !string.IsNullOrEmpty(text) && text!.Length <= 128
            && text.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or ':' or '/' or '-');

        /// <summary>The preset the extras ask for; null where <paramref name="open"/> names none, or names one that is not ids and fixed words.</summary>
        public static Preset? Parse(string? open, bool compose, string? runtime, bool arm, bool bar)
        {
            var preset = new Preset { Compose = compose, Arm = arm, Bar = bar, Runtime = IsId(runtime) ? runtime : null };
            if (open == "gallery") preset.Gallery = true;
            else if (open != null && open.StartsWith("screen:", StringComparison.Ordinal) && IsId(open.Substring(7))) preset.Project = open.Substring(7);
            else if (open != null && open.StartsWith("task:", StringComparison.Ordinal))
            {
                var parts = open.Substring(5).Split(':');
                if (parts.Length != 2 || !IsId(parts[0]) || !Sections.TryGetValue(parts[1], out var section)) return null;
                preset.Execution = parts[0];
                preset.Section = section;
            }
            else return null;
            return preset;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (!Debug.isDebugBuild || Application.isEditor || ReadIntent() is not Preset preset) return;
            new GameObject("Halcyonic capture preset").AddComponent<CapturePresets>().preset = preset;
        }

        private static Preset? ReadIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null) return null;
                return Parse(intent.Call<string>("getStringExtra", "halcyonic.open"), intent.Call<bool>("getBooleanExtra", "halcyonic.compose", false),
                    intent.Call<string>("getStringExtra", "halcyonic.agent"), intent.Call<bool>("getBooleanExtra", "halcyonic.arm", false),
                    intent.Call<bool>("getBooleanExtra", "halcyonic.bar", false));
            }
            catch (Exception) { return null; }
#else
            return null;
#endif
        }

        private void Update()
        {
            waited += Time.unscaledDeltaTime;
            var director = FindAnyObjectByType<WorkspaceDirector>();
            var session = director != null ? director.GetComponent<ControlPlaneConnection>().Session : null;
            var applied = director != null && director.Menu != null && session != null && session.SnapshotCurrent
                ? Apply(director.Menu, director.Presentation, session.State, preset) : null;
            if (applied == null && waited < WaitSeconds) return;
            Debug.Log(string.IsNullOrEmpty(applied)
                ? "Halcyonic: capture preset found nothing to open; staying on Projects."
                : "Halcyonic: capture preset applied: " + applied + ".");
            Destroy(gameObject);
        }

        /// <summary>
        /// Applies <paramref name="preset"/> once the snapshot holds what it names, all at once, and returns the fixed words
        /// of what it opened (<see cref="Nothing"/> where it opened nothing); null, changing nothing, while it waits.
        /// </summary>
        public static string? Apply(MenuDirector menu, WorkspacePresentationState presentation, ClientProjection state, Preset preset)
        {
            // No headset view draws the gallery yet (G3): the preset waits, then says it found nothing.
            if (preset.Gallery) return null;
            if (preset.Project != null)
            {
                if (!state.Projects.ContainsKey(preset.Project)) return null;
                var chosen = Choose(presentation, state, preset);
                return menu.ShowProjectScreen(preset.Project, preset.Bar) ? (preset.Bar ? "screen with its bar" : "screen") + chosen : Nothing;
            }
            if (preset.Execution == null || !state.Executions.TryGetValue(preset.Execution, out var execution)) return null;
            var agent = Choose(presentation, state, preset);
            menu.OpenFile(execution.WorkstreamId, preset.Section);
            if (menu.Navigator.Beside is not FileColumn file || file.Now is not WorkspacePresentation now
                || now.Execution?.ExecutionId != execution.ExecutionId) return Nothing;
            var words = "task at " + preset.Section!.Value.ToString().ToLowerInvariant() + agent;
            if (preset.Compose)
            {
                if (now.Actions.Contains(WorkspaceAction.Instruct))
                {
                    // The instruction opened as Tell it opens it, without the system keyboard: an empty draft.
                    file.Screen.Instruction.Restore(execution.ExecutionId, "", false, 0);
                    file.Reflow();
                    words += ", composing";
                }
                else words += ", not composing";
            }
            if (preset.Arm)
            {
                // The person's Approve press, which arms: an approval is confirmed whatever the policy
                // (WorkspacePresenter.AlwaysConfirmed), so the confirming bar shows and nothing is sent until its Yes, which
                // no preset presses. Checked all the same, so that a press which would send is never made.
                if (now.Actions.Contains(WorkspaceAction.Approve) && now.RequiresConfirmation(WorkspaceAction.Approve) && file.Steering.Armed == null)
                    file.Act(FileScreens.Approve, null);
                words += file.Steering.Armed == WorkspaceAction.Approve ? ", approval armed" : ", approval not armed";
            }
            return words;
        }

        /// <summary>Chooses the runtime the preset names where one is registered: the words to say so, or none.</summary>
        private static string Choose(WorkspacePresentationState presentation, ClientProjection state, Preset preset)
        {
            if (preset.Runtime == null || !state.Runtimes.Any(runtime => runtime.RuntimeId == preset.Runtime)) return "";
            presentation.SelectRuntime(preset.Runtime);
            return ", agent chosen";
        }
    }
}
#endif
