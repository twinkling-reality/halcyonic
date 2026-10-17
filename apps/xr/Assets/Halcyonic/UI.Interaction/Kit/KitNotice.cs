#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a notice says: Halcyonic's words, the icon of the state they are about, and that state's tone.</summary>
    public sealed class KitNoticeModel
    {
        public KitNoticeModel(GlazeIcon icon, GlazeTone tone, LineWords words)
        {
            Icon = icon;
            Tone = tone;
            Words = words;
        }

        public GlazeIcon Icon { get; }

        public GlazeTone Tone { get; }

        /// <summary>Halcyonic's own sentence, as ConnectionText or WorkspaceText words it; an agent's words never stand here.</summary>
        public LineWords Words { get; }
    }

    /// <summary>
    /// A notice of the spatial kit: one band on a raised fill, holding what is true about what happened that the
    /// work's state alone cannot say, such as that a command's outcome can't be told; it stands over the activity it
    /// points to, at the evidence rail's top. Its icon takes the state's tone; its words are Halcyonic's, upright,
    /// wrapped to five lines and never cut. It takes no press: where a way on exists, the instrument's own controls
    /// offer it. Laid from its top left.
    /// </summary>
    public sealed class KitNotice : MonoBehaviour
    {
        private const float PadDegrees = 0.75f;
        private const float GapDegrees = 0.5f;

        private Surface plate = null!;
        private TextMeshPro icon = null!;
        private TextMeshPro words = null!;
        private Vector2 size;

        public TextMeshPro Words => words;

        public Vector2 Size => size;

        public static KitNotice Create(Transform parent, string name = "Notice")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var notice = go.AddComponent<KitNotice>();
            notice.plate = Surface.Create(go.transform, "Band", KitType.Order.Plate);
            notice.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Text, KitType.Order.Words);
            notice.words = KitType.Create(go.transform, "Words", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            go.SetActive(false);
            return notice;
        }

        /// <summary>Shows <paramref name="model"/> <paramref name="width"/> wide with its top left at <paramref name="topLeft"/>; returns its height.</summary>
        public float Show(KitNoticeModel model, Vector2 topLeft, float width)
        {
            gameObject.SetActive(true);
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            var pad = KitType.Scaled(PadDegrees);
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            var iconRoom = iconEm + KitType.Scaled(GapDegrees);
            icon.fontSize = GlazeTokens.FontSize(iconEm);
            icon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
            icon.color = KitType.ToneText(model.Tone);
            GlazeIcons.Show(icon, model.Icon);
            KitType.Size(words, KitRole.Body, SpatialKit.ReadingLeading);
            GlazeRuns.Of(words).Set(model.Words);
            var (_, _, wordsHeight) = KitType.Lay(words, width - 2f * pad - iconRoom, 5);
            size = new Vector2(width, wordsHeight + 2f * pad);
            plate.Draw(size, KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Raised));
            plate.transform.localPosition = new Vector3(width / 2f, -size.y / 2f, 0f);
            icon.transform.localPosition = new Vector3(pad + iconEm / 2f, -pad - KitType.LinePitch(words) / 2f, -0.0005f);
            words.transform.localPosition = new Vector3(pad + iconRoom, -pad, -0.0005f);
            return size.y;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
