#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Plays the room's sounds (<see cref="KitSounds"/>, the art direction's ten cues in one key) from where their elements
    /// stand: clips made once from the client core's numbers, no audio assets, each cue on a fully spatial source that never
    /// loops, heard only while the app has the person's attention. Sources are set as the stage's own are (no Doppler, full
    /// level within 2.5 m and quietest from 25 m, Meta XR Audio's head-related spatializer where it is loaded). Every cue
    /// has its visual twin.
    /// </summary>
    /// <remarks>
    /// The stage's sound (StageSound) sounds the stage's events. Where the room shows a card, its cues play at the card and
    /// the stage stays silent for those same events: that switch belongs to the unit that wires the room into MenuPlane.
    /// Until then the room sounds only its answers' presses, which the stage does not hear.
    /// </remarks>
    public sealed class KitSoundPlayer : MonoBehaviour
    {
        private const string Spatializer = "Meta XR Audio";
        private const int Sources = 6;
        private const int Variants = 3;

        private static KitSoundPlayer? shared;
        private readonly Dictionary<(KitSound Sound, int Variant), AudioClip> clips = new Dictionary<(KitSound, int), AudioClip>();
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private int next;
        private int pressed;

        /// <summary>The one player, made when first needed while the app runs; none in the editor's batch renders.</summary>
        public static KitSoundPlayer? Shared
        {
            get
            {
                if (shared != null || !Application.isPlaying) return shared;
                var go = new GameObject("Room sounds");
                DontDestroyOnLoad(go);
                shared = go.AddComponent<KitSoundPlayer>();
                return shared;
            }
        }

        /// <summary>The cues play only while the app has the person's attention, as the stage's do.</summary>
        public static bool Audible => Application.isFocused && !FocusGuard.InputSuspended;

        /// <summary>The rate clips are made at: the device's output rate, or 48 kHz where it reports none.</summary>
        public static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

        /// <summary>
        /// The clip for <paramref name="sound"/>: the core's samples made once; a cue that varies (a press, a tick) has three
        /// variants, so presses differ a little.
        /// </summary>
        public AudioClip ClipOf(KitSound sound, int variant = 0) => Clip(sound, variant, Rate);

        /// <summary>A clip made from the core's numbers at <paramref name="rate"/> Hz, for the engine check and for playing.</summary>
        public static AudioClip MakeClip(KitSound sound, int variant, int rate)
        {
            var share = Variants > 1 ? 2.0 * variant / (Variants - 1) - 1 : 0;
            var samples = KitSounds.Render(sound, rate, share);
            var clip = AudioClip.Create("Room " + sound + (variant > 0 ? " " + variant : ""), samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip Clip(KitSound sound, int variant, int rate)
        {
            if (!clips.TryGetValue((sound, variant), out var clip)) clips[(sound, variant)] = clip = MakeClip(sound, variant, rate);
            return clip;
        }

        /// <summary>Plays <paramref name="sound"/> from <paramref name="at"/>, in the world; false where it stays silent.</summary>
        public bool Play(KitSound sound, Vector3 at)
        {
            if (!Audible) return false;
            var variant = System.Linq.Enumerable.Any(KitSounds.PartsOf(sound), part => part.Vary > 0) ? pressed++ % Variants : 0;
            var source = Source();
            source.transform.position = at;
            source.clip = Clip(sound, variant, Rate);
            source.Play();
            return true;
        }

        /// <summary>Plays <paramref name="sound"/> at <paramref name="at"/> through the shared player, where one runs.</summary>
        public static bool TryPlay(KitSound sound, Vector3 at) => Shared is KitSoundPlayer player && player.Play(sound, at);

        private AudioSource Source()
        {
            if (sources.Count < Sources)
            {
                var go = new GameObject("Room sound " + sources.Count);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                Configure(source);
                sources.Add(source);
                return source;
            }
            var reused = sources[next];
            next = (next + 1) % Sources;
            return reused;
        }

        /// <summary>A fully spatial source that never loops, as the stage's sources are set.</summary>
        public static void Configure(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 2.5f;
            source.maxDistance = 25f;
            source.spatialize = AudioSettings.GetSpatializerPluginName() == Spatializer;
            source.volume = 1f;
        }

        private void OnDestroy()
        {
            foreach (var clip in clips.Values) Destroy(clip);
            if (shared == this) shared = null;
        }
    }
}
