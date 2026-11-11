#nullable enable
using System.Threading.Tasks;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// Plays a person's world as it is heard (<see cref="WorldScore"/>): the dusk and night beds looping together,
    /// crossfaded as the look's night comes, and the chime when a project's place lights. The sounds are rendered
    /// once, off the main thread, when the world first shows; until then it is silent. The beds are heard all
    /// round, not from a point, and fade in and out with the world.
    /// </summary>
    public sealed class WorldSoundPlayer : MonoBehaviour
    {
        /// <summary>How long the beds take to fade in when the world shows, or out when it goes, in seconds.</summary>
        public const float FadeSeconds = 3f;

        private Task<(float[] Dusk, float[] Night, float[] Chime, int Rate)>? rendering;
        private AudioSource? dusk;
        private AudioSource? night;
        private AudioSource? chime;
        private float shown;
        private float lastPlace;

        /// <summary>Whether the world shows now: the beds fade toward it.</summary>
        public bool Showing { get; set; }

        /// <summary>How far night has come, 0 to 1, as the look says.</summary>
        public float Night { get; set; }

        /// <summary>How far the project's place is lit, 0 to 1: rising past a half plays the chime once.</summary>
        public float Place { get; set; }

        private void Update()
        {
            if (Showing && rendering == null && dusk == null)
            {
                var rate = AudioSettings.outputSampleRate;
                rendering = Task.Run(() => (WorldScore.Render(WorldSound.Dusk, rate), WorldScore.Render(WorldSound.Night, rate), WorldScore.Render(WorldSound.PlaceLit, rate), rate));
            }
            if (rendering != null && rendering.IsCompleted)
            {
                if (rendering.Status == TaskStatus.RanToCompletion) Make(rendering.Result);
                else Debug.LogWarning("Halcyonic: the world's sound could not be made, so the world is silent.");
                rendering = null;
            }
            if (dusk == null || night == null) return;
            shown = Mathf.MoveTowards(shown, Showing ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            var level = shown * shown * (3f - 2f * shown);
            // Equal-power crossfade, so the bed does not dip between dusk and night.
            var n = Mathf.Clamp01(Night);
            dusk.volume = level * Mathf.Cos(n * Mathf.PI * 0.5f);
            night.volume = level * Mathf.Sin(n * Mathf.PI * 0.5f);
            if (Showing && Place >= 0.5f && lastPlace < 0.5f && chime != null) chime.PlayOneShot(chime.clip, level);
            lastPlace = Place;
        }

        private void Make((float[] Dusk, float[] Night, float[] Chime, int Rate) sounds)
        {
            dusk = Source("Dusk", sounds.Dusk, sounds.Rate, true);
            night = Source("Night", sounds.Night, sounds.Rate, true);
            chime = Source("Chime", sounds.Chime, sounds.Rate, false);
            dusk.Play();
            night.Play();
        }

        private AudioSource Source(string name, float[] stereo, int rate, bool loop)
        {
            var clip = AudioClip.Create("Halcyonic world " + name, stereo.Length / 2, 2, rate, false);
            clip.SetData(stereo, 0);
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }
    }
}
