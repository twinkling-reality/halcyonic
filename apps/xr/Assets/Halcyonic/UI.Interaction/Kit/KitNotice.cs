#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A notice of the spatial kit: one band on a raised fill, holding what is true about what happened that the
    /// work's state alone cannot say, such as that a command's outcome can't be told; it stands over the activity it
    /// points to, at the evidence rail's top. Its icon takes the state's tone; its words are Halcyonic's, upright, laid
    /// as their " · " segments (<see cref="KitFlow"/>): a line breaks between two segments, never beside a dot, and a
    /// segment wider than the band wraps inside itself, to five lines, never cut. It takes no press: where a way on exists,
    /// the instrument's own controls offer it. Laid from its top left.
    /// </summary>
    public sealed class KitNotice : MonoBehaviour
    {
        private const float PadDegrees = 0.75f;
        private const float GapDegrees = 0.5f;

        /// <summary>The most lines one segment of the words wraps to.</summary>
        private const int SegmentLines = 5;

        private Surface plate = null!;
        private TextMeshPro icon = null!;
        private readonly List<TextMeshPro> segments = new List<TextMeshPro>();
        private readonly List<TextMeshPro> dots = new List<TextMeshPro>();
        private Vector2 size;

        public Vector2 Size => size;

        public static KitNotice Create(Transform parent, string name = "Notice")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var notice = go.AddComponent<KitNotice>();
            notice.plate = Surface.Create(go.transform, "Band", KitType.Order.Plate);
            notice.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Text, KitType.Order.Words);
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
            foreach (var label in segments.Concat(dots)) label.gameObject.SetActive(false);
            var (wordsHeight, _) = KitFlow.Lay(KitFlow.Segments(model.Words), index => Words(segments, index, "Words"), index => Words(dots, index, "Dot"),
                pad + iconRoom, -pad, width - pad, -0.0005f, SegmentLines);
            size = new Vector2(width, wordsHeight + 2f * pad);
            plate.Draw(size, KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Raised));
            plate.transform.localPosition = new Vector3(width / 2f, -size.y / 2f, 0f);
            var pitch = segments.Count > 0 ? KitType.LinePitch(segments[0]) : iconEm;
            icon.transform.localPosition = new Vector3(pad + iconEm / 2f, -pad - pitch / 2f, -0.0005f);
            return size.y;
        }

        /// <summary>
        /// A label of the notice's words, a segment's or a dot's, from <paramref name="pool"/>, in Body at the reading pitch; made
        /// hidden, so one the flow does not show never stands empty on the band.
        /// </summary>
        private TextMeshPro Words(List<TextMeshPro> pool, int index, string name)
        {
            while (pool.Count <= index)
            {
                var made = KitType.Create(transform, name + " " + pool.Count, KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
                made.gameObject.SetActive(false);
                pool.Add(made);
            }
            var label = pool[index];
            KitType.Size(label, KitRole.Body, SpatialKit.ReadingLeading);
            return label;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
