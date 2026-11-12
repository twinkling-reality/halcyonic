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
    /// <c>gallery</c>; <c>halcyonic.compose</c> opens the task's instruction without the keyboard, empty, and
    /// <c>halcyonic.draft</c> or <c>halcyonic.heard</c> open it holding one fixed synthetic draft (<see cref="DraftWords"/>),
    /// typed or as if heard, so a draft's screen can be captured with no keyboard and no voice; <c>halcyonic.choose</c>
    /// opens the instructions offered to choose from where Tell it offers them; <c>halcyonic.agent</c>
    /// chooses a registered runtime by its id; <c>halcyonic.arm</c> arms the waiting approval so its confirming bar shows;
    /// <c>halcyonic.bar</c> opens the screen's bar; <c>halcyonic.generic</c>, on its own or with any of them, draws every agent
    /// app, model, host and sibling source by a generic name (<see cref="CaptureNames"/>), for footage free of product
    /// names. Presentation only: it never sends a command, never confirms, and
    /// never approves, denies, answers, starts or previews anything; it arms only where a confirm step stands before the
    /// send, as an approval's always does. Ids and fixed words only: extras that are neither are ignored.
    /// It waits for the live snapshot to hold the id; one fixed log line then says what it applied, or that it found
    /// nothing, and the app is as a person would have left it. An armed approval is said only once it has held for
    /// <see cref="HoldSeconds"/>, pressed again while the launch's layouts drop it.
    /// </summary>
    public sealed class CapturePresets : MonoBehaviour
    {
        /// <summary>How long it waits for the live snapshot to hold the id before it says it found nothing.</summary>
        private const float WaitSeconds = 60f;

        /// <summary>
        /// How long an armed approval must stand unbroken before the preset says so. A launch lays the file out again in
        /// its first seconds (the room, the stage, the composer's geometry), and each layout drops an armed confirmation
        /// ("Not Sent · Window Changed"), so the preset presses Approve again until the arm holds (DEVICE, 2026-11-04).
        /// </summary>
        public const float HoldSeconds = 3f;

        /// <summary>How long it keeps arming the approval again before it says the approval is not armed.</summary>
        private const float ArmSeconds = 30f;

        /// <summary>The words that end what <see cref="Apply"/> returns when it armed the approval.</summary>
        public const string ArmedWords = ", approval armed";

        /// <summary>The words that end what <see cref="Apply"/> returns when it could not arm the approval.</summary>
        public const string NotArmedWords = ", approval not armed";

        /// <summary>The one draft a preset writes, synthetic and fixed: no extra carries words. Nothing sends it; a person's Send would.</summary>
        public const string DraftWords = "Add a golden balloon worth five points, and remove the old backup file.";

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

            /// <summary>The instruction opens holding <see cref="DraftWords"/> as typed (the redesign's typed draft).</summary>
            public bool Draft;

            /// <summary>The instruction opens holding <see cref="DraftWords"/> as heard (the redesign's heard draft).</summary>
            public bool Heard;

            /// <summary>Tell it pressed where it offers instructions to choose from (no keyboard, or a recorded run's own): their page opens.</summary>
            public bool Choose;
            public string? Runtime;
            public bool Arm;
            public bool Bar;
        }

        private Preset preset = null!;
        private float waited;
        private string? applied;
        private float arming;
        private float held;

        /// <summary>An id as the device tool can pass it: letters, digits and <c>._:/-</c> only, never a name.</summary>
        public static bool IsId(string? text) => !string.IsNullOrEmpty(text) && text!.Length <= 128
            && text.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or ':' or '/' or '-');

        /// <summary>The preset the extras ask for; null where <paramref name="open"/> names none, or names one that is not ids and fixed words.</summary>
        public static Preset? Parse(string? open, bool compose, string? runtime, bool arm, bool bar, bool draft = false, bool heard = false, bool choose = false)
        {
            var preset = new Preset { Compose = compose, Arm = arm, Bar = bar, Runtime = IsId(runtime) ? runtime : null, Draft = draft, Heard = heard, Choose = choose };
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
            if (!Debug.isDebugBuild || Application.isEditor) return;
            if (ReadExtra("halcyonic.redesign"))
            {
                RoomDesign.Enabled = true;
                Debug.Log("Halcyonic: the redesign draws where it is built.");
            }
            if (ReadExtra("halcyonic.generic"))
            {
                CaptureNames.UseGeneric(true);
                Debug.Log("Halcyonic: capture names generic.");
            }
            if (ReadIntent() is not Preset preset) return;
            new GameObject("Halcyonic capture preset").AddComponent<CapturePresets>().preset = preset;
        }

        /// <summary>Whether the launch intent carries the boolean extra <paramref name="name"/>: generic names (<c>halcyonic.generic</c>), the redesign (<c>halcyonic.redesign</c>).</summary>
        private static bool ReadExtra(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent != null && intent.Call<bool>("getBooleanExtra", name, false);
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
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
                    intent.Call<bool>("getBooleanExtra", "halcyonic.bar", false), intent.Call<bool>("getBooleanExtra", "halcyonic.draft", false),
                    intent.Call<bool>("getBooleanExtra", "halcyonic.heard", false), intent.Call<bool>("getBooleanExtra", "halcyonic.choose", false));
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
            if (applied != null)
            {
                // The approval armed: it holds once it has stood unbroken, pressed again while a layout drops it.
                arming += Time.unscaledDeltaTime;
                var holds = director != null && director.Menu != null && Rearm(director.Menu, preset.Execution);
                held = holds ? held + Time.unscaledDeltaTime : 0f;
                if (held < HoldSeconds && arming < ArmSeconds) return;
                if (held < HoldSeconds) applied = applied.Substring(0, applied.Length - ArmedWords.Length) + NotArmedWords;
                Say(applied);
                return;
            }
            var session = director != null ? director.GetComponent<ControlPlaneConnection>().Session : null;
            var now = director != null && director.Menu != null && session != null && session.SnapshotCurrent
                ? Apply(director.Menu, director.Presentation, session.State, preset) : null;
            if (now == null && waited < WaitSeconds) return;
            if (preset.Arm && now != null && now.EndsWith(ArmedWords, StringComparison.Ordinal))
            {
                applied = now;
                return;
            }
            Say(now);
        }

        private void Say(string? applied)
        {
            Debug.Log(string.IsNullOrEmpty(applied)
                ? "Halcyonic: capture preset found nothing to open; staying on Projects."
                : "Halcyonic: capture preset applied: " + applied + ".");
            Destroy(gameObject);
        }

        /// <summary>
        /// Whether the task's file stands at its approval, armed, pressing Approve again first where the arm was dropped.
        /// It presses only while nothing is armed, as a second press on an armed approval would confirm it.
        /// </summary>
        public static bool Rearm(MenuDirector menu, string? execution)
        {
            if (execution == null || menu.Navigator.Beside is not FileColumn file || file.Now is not WorkspacePresentation now
                || now.Execution?.ExecutionId != execution) return false;
            return Arm(file, now);
        }

        /// <summary>
        /// The person's Approve press, which arms: an approval is confirmed whatever the policy
        /// (WorkspacePresenter.AlwaysConfirmed), so the confirming bar shows and nothing is sent until its Yes, which
        /// no preset presses. Checked all the same, so that a press which would send is never made.
        /// </summary>
        private static bool Arm(FileColumn file, WorkspacePresentation now)
        {
            // A preset's press is never the decision (R1's one press is a person's): it arms and shows the confirmation only.
            file.DecideOnPress = false;
            if (now.Actions.Contains(WorkspaceAction.Approve) && now.RequiresConfirmation(WorkspaceAction.Approve) && file.Steering.Armed == null)
                file.Act(FileScreens.Approve, null);
            return file.Steering.Armed == WorkspaceAction.Approve;
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
            if (preset.Compose || preset.Draft || preset.Heard)
            {
                if (now.Actions.Contains(WorkspaceAction.Instruct))
                {
                    // The instruction opened as Tell it opens it, without the system keyboard: an empty draft, or the one fixed
                    // synthetic draft, typed or as if heard. Only a person's Send, and then its confirmation, would send it.
                    var worded = preset.Draft || preset.Heard;
                    file.Screen.Instruction.Restore(execution.ExecutionId, worded ? DraftWords : "", preset.Heard, 0);
                    file.Reflow();
                    words += preset.Heard ? ", heard draft" : preset.Draft ? ", typed draft" : ", composing";
                }
                else words += ", not composing";
            }
            if (preset.Choose)
            {
                // Tell it's own press, only where it offers instructions to choose from: their page opens and nothing is sent
                // (a choice sends only from its draft, by the person's Send). Where it would open the keyboard, it is not pressed.
                if (now.Actions.Contains(WorkspaceAction.Instruct) && file.ChoosesInstruction && !file.Screen.Instruction.Open)
                {
                    file.Act(FileScreens.TellIt, null);
                    words += file.Screen.Presets != null ? ", choosing" : ", not choosing";
                }
                else words += ", not choosing";
            }
            if (preset.Arm) words += Arm(file, now) ? ArmedWords : NotArmedWords;
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
