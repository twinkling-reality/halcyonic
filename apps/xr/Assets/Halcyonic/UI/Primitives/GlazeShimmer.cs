#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>The headset's one reduced-motion switch (ADR 0027): Settings' "Keep badges still" (<see cref="Comfort.Still"/>).</summary>
    public static class GlazeMotion
    {
        /// <summary>Every loop stands still: no breath, no turning, no pulse and no shimmer, each state shown by colour, icon and words alone.</summary>
        public static bool Still { get; set; }
    }

    /// <summary>
    /// A wait's shimmer on a label's words (ADR 0027): a soft band of brightness sweeps across them every
    /// <see cref="Glaze.ShimmerSeconds"/>, lifting each letter toward white and never moving it. It
    /// recolours the vertices TextMeshPro already built, so it adds no mesh and no draw call, and
    /// allocates nothing a frame. Off while the label does not wait, and under <see cref="GlazeMotion.Still"/>,
    /// the words in their own colours.
    /// </summary>
    public sealed class GlazeShimmer : MonoBehaviour
    {
        private TMP_Text label = null!;
        private bool lifted;

        /// <summary>The label's words say what is under way, so they shimmer.</summary>
        public bool Waits { get; set; }

        /// <summary>The words show lifted by the shimmer now, not in their own colours.</summary>
        public bool Lifted => lifted;

        /// <summary>The shimmer for <paramref name="label"/>, made once beside it.</summary>
        public static GlazeShimmer On(TMP_Text label)
        {
            var shimmer = label.gameObject.AddComponent<GlazeShimmer>();
            shimmer.label = label;
            return shimmer;
        }

        private void LateUpdate() => Draw(Time.unscaledTime);

        /// <summary>
        /// The words as they show at <paramref name="now"/>, in seconds: swept by the band where the label
        /// waits and motion may play, else in their own colours. Called every frame; the renders call it
        /// at the times they draw.
        /// </summary>
        public void Draw(float now)
        {
            if (!Waits || GlazeMotion.Still || !label.gameObject.activeInHierarchy)
            {
                if (lifted) Recolour(-1f);
                return;
            }
            Recolour(now % Glaze.ShimmerSeconds / Glaze.ShimmerSeconds);
        }

        /// <summary>
        /// Every visible letter in its own colour lifted by the band at <paramref name="phase"/> of its
        /// sweep, which runs from before the words' start to past their end; a negative phase puts every
        /// letter back in its own colour.
        /// </summary>
        private void Recolour(float phase)
        {
            var info = label.textInfo;
            if (info == null || info.characterCount == 0)
            {
                lifted = false;
                return;
            }
            var from = float.MaxValue;
            var to = float.MinValue;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                from = Mathf.Min(from, character.bottomLeft.x);
                to = Mathf.Max(to, character.topRight.x);
            }
            var span = to - from;
            if (span <= 0f) return;
            var band = Glaze.ShimmerWidth;
            var middle = -band + phase * (1f + 2f * band);
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var own = character.color;
                var colour = own;
                if (phase >= 0f)
                {
                    var at = ((character.bottomLeft.x + character.topRight.x) / 2f - from) / span;
                    var off = Mathf.Abs(at - middle) / band;
                    if (off < 1f)
                    {
                        var lift = Glaze.ShimmerDepth * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * off));
                        colour = new Color32((byte)(own.r + (255 - own.r) * lift), (byte)(own.g + (255 - own.g) * lift), (byte)(own.b + (255 - own.b) * lift), own.a);
                    }
                }
                var colours = info.meshInfo[character.materialReferenceIndex].colors32;
                for (var corner = 0; corner < 4; corner++) colours[character.vertexIndex + corner] = colour;
            }
            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            lifted = phase >= 0f;
        }
    }
}
