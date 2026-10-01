#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What the stage's banner says about the whole stage, which sets its edge.</summary>
    public enum BannerKind
    {
        /// <summary>Connected and current: quiet text, no edge.</summary>
        Live,

        /// <summary>Not live: the characters show their last known state, or none. A dashed fog edge, as Can't tell yet has.</summary>
        NotLive,

        /// <summary>A demonstration or recording plays: a dashed lilac edge, as practice work's plates have.</summary>
        Practice,
    }

    /// <summary>
    /// The stage's banner (ADR 0023): one plate in the ambient strip under the characters, saying
    /// whether what they show is live, and, while another window keeps focus, how many need the
    /// person, in the attention colour. Built in units of the distance from the eyes, its top edge's
    /// middle on its origin.
    /// </summary>
    public sealed class StageBanner : MonoBehaviour
    {
        public const float MaxWidthDegrees = 24f;
        public const int MaxLines = 4;
        private const float SideDegrees = 1f;
        private const float EndDegrees = 0.55f;
        private const float RadiusDegrees = 0.9f;
        private const float EdgeDegrees = 0.1f;
        private const float DashDegrees = 0.5f;

        private Surface plate = null!;
        private TextMeshPro line = null!;
        private TextMeshPro waiting = null!;

        public float Height { get; private set; }

        public float Width { get; private set; }

        public TextMeshPro Line => line;

        public Surface Plate => plate;

        public TextMeshPro Waiting => waiting;

        public static StageBanner Create(Transform parent)
        {
            var go = new GameObject("Banner");
            go.transform.SetParent(parent, false);
            var banner = go.AddComponent<StageBanner>();
            banner.plate = Surface.Create(go.transform, "Plate", 0);
            banner.line = GlazeText.Create(go.transform, "Line", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Top, 2);
            banner.waiting = GlazeText.Create(go.transform, "Waiting", GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground),
                TextAlignmentOptions.Top, 2, strong: true);
            return banner;
        }

        /// <summary>Shows <paramref name="text"/> as written, and under it <paramref name="needsYou"/> when something waits for the person.</summary>
        public void Show(string text, BannerKind kind, string? needsYou)
        {
            var side = GlazeTokens.Units(SideDegrees);
            var end = GlazeTokens.Units(EndDegrees);
            var room = GlazeTokens.Units(MaxWidthDegrees) - 2f * side;
            line.color = kind == BannerKind.Live ? GlazeTokens.TextSecondary : GlazeTokens.Text;
            GlazeText.SetLiteral(line, text);
            var (lines, width) = GlazeText.Lay(line, room, needsYou == null ? MaxLines : MaxLines - 1);
            line.transform.localPosition = new Vector3(0f, -end, -0.001f);
            var bottom = -end - lines * GlazeText.LineHeight(line);
            waiting.gameObject.SetActive(needsYou != null);
            if (needsYou != null)
            {
                GlazeText.SetLiteral(waiting, needsYou);
                var (_, waitingWidth) = GlazeText.Lay(waiting, room, 1);
                waiting.transform.localPosition = new Vector3(0f, bottom, -0.001f);
                bottom -= GlazeText.LineHeight(waiting);
                width = Mathf.Max(width, waitingWidth);
            }
            Width = width + 2f * side;
            Height = -bottom + end;
            var edge = kind switch
            {
                BannerKind.NotLive => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Unknown).Strong),
                BannerKind.Practice => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Simulated).Strong),
                _ => Color.clear,
            };
            plate.transform.localPosition = new Vector3(0f, -Height / 2f, 0f);
            plate.Draw(new Vector2(Width, Height), GlazeTokens.Units(RadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), edge,
                kind == BannerKind.Live ? 0f : GlazeTokens.Units(EdgeDegrees), kind == BannerKind.Live ? 0f : GlazeTokens.Units(DashDegrees));
        }
    }
}
