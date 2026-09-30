#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.XR.Workspace;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Halcyonic.XR.Sound
{
    /// <summary>
    /// The stage's sound, in the Glaze direction the owner chose: the characters sound like small
    /// glazed ceramic bells, and are silent while work goes well
    /// (docs/internal/architecture/XR_CLIENT.md, "Sound"). The client core decides everything:
    /// <see cref="GlazeSynthesizer"/> renders the cues and <see cref="SoundCueSelector"/> chooses
    /// them from what changed; this component only renders the clips at startup, gives each character
    /// a voice at its body, and plays what the selector chooses, where and when it says.
    /// </summary>
    /// <remarks>
    /// Every clip is rendered once, on a worker thread, and made an <see cref="AudioClip"/> on the main
    /// thread a few per frame; nothing is synthesized while sound plays. A character's cues come from
    /// its body, the person's own actions from in front of them where the workspace opens, and the
    /// dropped connection's one cue from the middle of the stage, spread across it. Unity's built-in
    /// panning places them; nothing else is needed. No cue starts while the app lacks focus, as while
    /// the system menu or a window such as Virtual Display's has it, and cues scheduled but not yet
    /// started are cancelled when focus goes; the person's act that arrives just before focus
    /// returns, as the system keyboard's result does, waits for it. The recorded demonstration
    /// sounds as live work does: the same data, the same flow. Once the clips are made nothing runs
    /// per frame: cues are scheduled on the audio clock as the state changes.
    /// </remarks>
    [RequireComponent(typeof(ControlPlaneConnection), typeof(CharacterStage))]
    public sealed class StageSound : MonoBehaviour
    {
        /// <summary>Between choosing a cue and its earliest start, so the audio thread has it in time.</summary>
        private const double Lead = 0.05;

        /// <summary>The person's own actions sound this far in front of them, where the workspace opens.</summary>
        private const float WorkspaceReach = 0.6f;

        /// <summary>
        /// Within this distance a cue plays at its own level. The stage stands within it both on a desk,
        /// about 0.55 m away, and in the virtual space, 2.4 m away, so where it stands is heard in the
        /// direction of each cue, not in its loudness, as the characters keep their apparent size.
        /// </summary>
        private const float FullLevelWithin = 2.5f;

        /// <summary>Beyond this distance a cue gets no quieter, a tenth of its level.</summary>
        private const float QuietestFrom = 25f;

        /// <summary>The dropped connection's cue is spread across the arc's 60 degrees.</summary>
        private const float StageSpread = 60f;

        private const int ClipsPerFrame = 4;

        [Tooltip("The level of every cue, from 0 to 1. The soundbook played them at half, its starting volume; the headset's own volume applies on top.")]
        [SerializeField] private float volume = 0.5f;

        private readonly SoundCueSelector selector = new SoundCueSelector();
        private readonly Dictionary<string, Voice> voices = new Dictionary<string, Voice>();
        private readonly List<string> silent = new List<string>();
        private readonly AudioClip?[][] clips = new AudioClip?[GlazeSynthesizer.Cues.Count][];
        private ControlPlaneConnection connection = null!;
        private CharacterStage stage = null!;
        private WorkspaceDirector? director;
        private Func<string, int> slotOf = null!;
        private Voice workspace = null!;
        private Voice whole = null!;
        private bool ready;

        /// <summary>When the last cue was scheduled to start, on the audio clock.</summary>
        private double lastStart = double.NegativeInfinity;

        /// <summary>No cue starts while the app lacks focus or its input is suspended.</summary>
        private bool Audible => ready && Application.isFocused && !FocusGuard.InputSuspended;

        /// <summary>The selector's clock: real time, which moves on while the audio output is idle.</summary>
        private static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            stage = GetComponent<CharacterStage>();
            director = GetComponent<WorkspaceDirector>();
            slotOf = stage.SlotOf;
            for (var cue = 0; cue < clips.Length; cue++) clips[cue] = new AudioClip?[GlazeSynthesizer.Bots];
            workspace = CreateVoice(new GameObject("Workspace sound"), 0f);
            whole = CreateVoice(new GameObject("Stage sound"), StageSpread);
            workspace.Host.SetParent(transform, false);
            whole.Host.SetParent(transform, false);
        }

        private void OnEnable()
        {
            connection.Changed += OnChanged;
            stage.CharacterCreated += OnCharacterCreated;
            if (director != null) director.Acted += OnActed;
            // What the stage shows already is what later changes are compared with; the characters it
            // made before this component existed get their voices now.
            var session = connection.Session;
            if (session == null) return;
            selector.Reset(session.State, session.Status, slotOf);
            foreach (var workstreamId in session.State.Workstreams.Keys)
            {
                if (stage.TryGetCharacter(workstreamId, out var view)) OnCharacterCreated(workstreamId, view);
            }
        }

        private void OnDisable()
        {
            connection.Changed -= OnChanged;
            stage.CharacterCreated -= OnCharacterCreated;
            if (director != null) director.Acted -= OnActed;
            CancelPending();
        }

        private void Start()
        {
            if (director == null) Log("sound for the person's actions off, because the stage has no workspace");
            StartCoroutine(Load());
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                CancelPending();
                return;
            }
            // An act that came as focus was returning, as the system keyboard's result does, sounds now.
            var cue = selector.HeardAgain(Now);
            if (cue != null) Play(cue);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) CancelPending();
        }

        /// <summary>
        /// Renders every clip on a worker thread and makes them audio clips here, on the main thread,
        /// a few per frame. Until they are ready nothing sounds, and nothing chosen meanwhile plays late.
        /// </summary>
        private IEnumerator Load()
        {
            var rate = AudioSettings.outputSampleRate;
            var rendered = new ConcurrentQueue<GlazeClip>();
            var renderMilliseconds = 0L;
            var since = Stopwatch.StartNew();
            var rendering = Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                foreach (var clip in GlazeSynthesizer.RenderAll(rate)) rendered.Enqueue(clip);
                renderMilliseconds = clock.ElapsedMilliseconds;
            });
            long samples = 0;
            var count = 0;
            while (!rendering.IsCompleted || !rendered.IsEmpty)
            {
                for (var made = 0; made < ClipsPerFrame && rendered.TryDequeue(out var clip); made++)
                {
                    var audio = AudioClip.Create("Glaze " + clip.Cue + " " + clip.Bot, clip.Samples.Length, 1, rate, false);
                    audio.SetData(clip.Samples, 0);
                    clips[(int)clip.Cue][clip.Bot] = audio;
                    samples += clip.Samples.Length;
                    count++;
                }
                yield return null;
            }
            if (rendering.IsFaulted)
            {
                Debug.LogError("Halcyonic: sound off, because its cues could not be rendered. " + rendering.Exception?.GetBaseException().Message);
                yield break;
            }
            ready = true;
            Log($"sound ready: {count} clips rendered at {rate} Hz in {renderMilliseconds} ms on a worker thread, "
                + $"{samples * sizeof(float) / 1048576.0:F1} MiB of samples, ready {since.ElapsedMilliseconds} ms after it began");
        }

        private void OnChanged(StateChanges changes)
        {
            var session = connection.Session;
            if (session == null) return;
            ForgetSilentVoices();
            foreach (var cue in selector.Observe(changes, session.State, session.Status, slotOf, Now, Audible))
            {
                Play(cue);
            }
        }

        private void OnActed(string workstreamId, WorkspaceAct act)
        {
            // Without focus the act waits for it, briefly (SoundCueSelector.HeardAgain).
            var cue = selector.Act(act, workstreamId, Now, Audible);
            if (cue != null) Play(cue);
        }

        /// <summary>A character's voice: two sources at its body, which moves as the character does.</summary>
        private void OnCharacterCreated(string workstreamId, CharacterView view)
        {
            if (voices.TryGetValue(workstreamId, out var voice) && voice.Alive && voice.Host == view.Body) return;
            voices[workstreamId] = CreateVoice(view.Body.gameObject, 0f);
        }

        private void Play(CueOnset cue)
        {
            var clip = clips[(int)cue.Cue][Math.Max(0, cue.Bot)];
            if (clip == null) return;
            Voice? voice;
            switch (cue.Place)
            {
                case CuePlace.Workspace:
                    voice = workspace;
                    voice.Host.position = InFrontOfThePerson(WorkspaceReach);
                    break;
                case CuePlace.Stage:
                    voice = whole;
                    voice.Host.position = MiddleOfTheStage();
                    break;
                default:
                    voice = cue.WorkstreamId != null && voices.TryGetValue(cue.WorkstreamId, out var own) && own.Alive ? own : null;
                    break;
            }
            if (voice == null) return;
            // The selector keeps time on the real-time clock, since the audio clock may stand still
            // while the output is suspended; on the audio clock onsets keep their gap too.
            var start = Math.Max(AudioSettings.dspTime + Math.Max(0, cue.At - Now) + Lead, lastStart + SoundCueSelector.MinimumGap);
            lastStart = start;
            voice.Play(clip, start);
            Log("sound " + cue.Cue + " from " + (cue.Place == CuePlace.Character ? "its character" : cue.Place == CuePlace.Workspace ? "the workspace" : "the whole stage")
                + (cue.Bot >= 0 ? ", note " + cue.Bot : ""));
        }

        private Vector3 InFrontOfThePerson(float distance)
        {
            var camera = Camera.main;
            if (camera == null) return transform.position;
            var head = camera.transform;
            return head.position + head.forward * distance;
        }

        /// <summary>The middle of the characters on the stage, or in front of the person without any.</summary>
        private Vector3 MiddleOfTheStage()
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var voice in voices.Values)
            {
                if (!voice.Alive) continue;
                sum += voice.Host.position;
                count++;
            }
            return count > 0 ? sum / count : InFrontOfThePerson(1.5f);
        }

        private void CancelPending()
        {
            var now = AudioSettings.dspTime;
            if (workspace != null) workspace.CancelPending(now);
            if (whole != null) whole.CancelPending(now);
            foreach (var voice in voices.Values) voice.CancelPending(now);
        }

        /// <summary>Voices whose characters left the stage went with them.</summary>
        private void ForgetSilentVoices()
        {
            silent.Clear();
            foreach (var pair in voices)
            {
                if (!pair.Value.Alive) silent.Add(pair.Key);
            }
            foreach (var workstreamId in silent) voices.Remove(workstreamId);
        }

        private Voice CreateVoice(GameObject host, float spread) => new Voice(host.transform, Source(host, spread), Source(host, spread));

        /// <summary>A fully spatial source with no Doppler, level within <see cref="FullLevelWithin"/>.</summary>
        private AudioSource Source(GameObject host, float spread)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = FullLevelWithin;
            source.maxDistance = QuietestFrom;
            source.spread = spread;
            source.volume = Mathf.Clamp01(volume);
            return source;
        }

        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", message);

        /// <summary>Two sources at one place, so a cue can start while the one before it still rings.</summary>
        private sealed class Voice
        {
            private readonly AudioSource[] sources;
            private readonly double[] starts = new double[2];
            private readonly double[] ends = new double[2];

            public Voice(Transform host, AudioSource first, AudioSource second)
            {
                Host = host;
                sources = new[] { first, second };
            }

            public Transform Host { get; }

            /// <summary>False once its character left the stage, which destroys its sources with it.</summary>
            public bool Alive => sources[0] != null;

            /// <summary>Schedules a clip on the audio clock, on the source that falls silent first.</summary>
            public void Play(AudioClip clip, double at)
            {
                var i = ends[0] <= ends[1] ? 0 : 1;
                sources[i].clip = clip;
                sources[i].PlayScheduled(at);
                starts[i] = at;
                ends[i] = at + clip.length;
            }

            /// <summary>Cancels the cues that have not started yet; those playing finish.</summary>
            public void CancelPending(double now)
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    if (starts[i] <= now || sources[i] == null) continue;
                    sources[i].Stop();
                    starts[i] = now;
                    ends[i] = now;
                }
            }
        }
    }
}
