#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A row of the spatial kit: its icon in the icon column in its state's tone, its words (wrapping to their lines),
    /// a second line under them, and at its right end the words saying where it goes or a chevron. The whole row is
    /// one hand target at least a target tall. Chosen, it stands lit on a raised fill with a crisp white frame;
    /// pointed at, the frame alone; no bar and no underline marks anything. Laid from its top left.
    /// </summary>
    public sealed class KitRow : MonoBehaviour
    {
        private const float PadDegrees = 0.55f;
        private const float EndGapDegrees = 0.6f;

        private Surface plate = null!;
        private TextMeshPro icon = null!;
        private TextMeshPro words = null!;
        private TextMeshPro detail = null!;
        private TextMeshPro end = null!;
        private TextMeshPro chevron = null!;
        private KitPress press = null!;
        private KitRowModel? model;
        private Vector2 size;
        private int painted = -1;

        public KitPress Press => press;

        public KitRowModel? Model => model;

        public Vector2 Size => size;

        public TextMeshPro Words => words;

        /// <summary>The row was pressed: its owner goes where its key says.</summary>
        public event Action<string>? Pressed;

        public static KitRow Create(Transform parent, string name, int order = KitType.Order.Control)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var row = go.AddComponent<KitRow>();
            row.plate = Surface.Create(go.transform, "Row", order);
            row.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Secondary, order + 2);
            row.words = KitType.Create(go.transform, "Words", KitRole.Body, KitType.Text, order + 2);
            row.detail = KitType.Create(go.transform, "Detail", KitRole.Body, KitType.Secondary, order + 2);
            row.end = KitType.Create(go.transform, "End", KitRole.Strong, KitType.ToneText(GlazeTone.Accent), order + 2, TextAlignmentOptions.TopRight);
            row.end.rectTransform.pivot = new Vector2(1f, 1f);
            row.end.textWrappingMode = TextWrappingModes.NoWrap;
            row.chevron = GlazeIcons.Create(go.transform, "Opens more", SpatialKit.IconDegrees, KitType.Secondary, order + 2);
            row.press = KitPress.Create(go, Vector2.one * KitType.Units(SpatialKit.TargetDegrees));
            row.press.Pressed += () => { if (row.model != null) row.Pressed?.Invoke(row.model.Key); };
            go.SetActive(false);
            return row;
        }

        /// <summary>Shows <paramref name="row"/> from <paramref name="topLeft"/>, <paramref name="width"/> wide, and returns its height.</summary>
        public float Show(KitRowModel row, Vector2 topLeft, float width)
        {
            var changed = model == null || model.Key != row.Key || model.Words.Text != row.Words.Text || model.End != row.End;
            model = row;
            var pad = KitType.Scaled(PadDegrees);
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            var column = KitType.Scaled(SpatialKit.IconDegrees + 0.35f);
            var left = pad + (row.Icon.HasValue ? column : 0f);
            // The right end: its words, or a chevron, or nothing.
            var endRoom = 0f;
            end.gameObject.SetActive(row.End != null);
            chevron.gameObject.SetActive(row.End == null && row.OpensMore);
            if (row.End != null)
            {
                KitType.Size(end, KitRole.Strong);
                end.color = KitType.ToneText(row.EndTone ?? GlazeTone.Accent);
                GlazeText.SetLiteral(end, row.End);
                var (_, endWidth, _) = KitType.Lay(end, width * 0.45f, 1);
                endRoom = endWidth + KitType.Scaled(EndGapDegrees);
            }
            else if (row.OpensMore)
            {
                chevron.fontSize = GlazeTokens.FontSize(iconEm);
                chevron.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(chevron, GlazeIcon.OpensMore);
                endRoom = iconEm + KitType.Scaled(EndGapDegrees) * 0.5f;
            }
            var textWidth = Mathf.Max(KitType.Units(3f), width - left - pad - endRoom);
            KitType.Size(words, row.Selected ? KitRole.Strong : KitRole.Body);
            GlazeText.SetStrong(words, row.Selected);
            if (row.IsName) GlazeRuns.Of(words).SetIdentity(row.Words.Text);
            else GlazeRuns.Of(words).Set(row.Words);
            var (_, _, wordsHeight) = KitType.Lay(words, textWidth, row.WordLines);
            var detailHeight = 0f;
            detail.gameObject.SetActive(row.Detail != null);
            if (row.Detail != null)
            {
                KitType.Size(detail, KitRole.Body);
                detail.color = row.DetailTone is GlazeTone tone ? KitType.ToneText(tone) : KitType.Secondary;
                GlazeText.SetLiteral(detail, row.Detail);
                // Halcyonic's own words are never cut: a long state or fact wraps instead.
                (_, _, detailHeight) = KitType.Lay(detail, textWidth, 3);
            }
            var content = wordsHeight + detailHeight;
            var height = Mathf.Max(KitType.Units(SpatialKit.TargetDegrees), content + 2f * KitType.Scaled(0.55f));
            size = new Vector2(width, height);
            var top = (height - content) / 2f;
            words.transform.localPosition = new Vector3(-width / 2f + left, height / 2f - top, -0.0005f);
            detail.transform.localPosition = new Vector3(-width / 2f + left, height / 2f - top - wordsHeight, -0.0005f);
            var firstLine = height / 2f - top - KitType.LinePitch(words) / 2f;
            icon.gameObject.SetActive(row.Icon.HasValue);
            if (row.Icon is GlazeIcon glyph)
            {
                icon.fontSize = GlazeTokens.FontSize(iconEm);
                icon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                icon.color = row.IconTone == GlazeTone.Neutral ? KitType.Secondary : KitType.ToneText(row.IconTone);
                GlazeIcons.Show(icon, glyph);
                icon.transform.localPosition = new Vector3(-width / 2f + pad + iconEm / 2f, firstLine, -0.0005f);
            }
            end.transform.localPosition = new Vector3(width / 2f - pad, height / 2f - top, -0.0005f);
            chevron.transform.localPosition = new Vector3(width / 2f - pad - iconEm / 2f, 0f, -0.0005f);
            press.Resize(size);
            transform.localPosition = new Vector3(topLeft.x + width / 2f, topLeft.y - height / 2f, -KitType.Units(0.05f));
            if (changed) press.Unsettle();
            painted = -1;
            gameObject.SetActive(true);
            Paint(press.Hovered);
            return height;
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update() => Paint(press.Hovered);

        public void PaintForRender(bool pointed)
        {
            painted = -1;
            Paint(pointed);
        }

        private void Paint(bool pointed)
        {
            if (model == null) return;
            var hovered = pointed && press.Available;
            var state = (hovered ? 1 : 0) | (model.Selected ? 2 : 0) | (FocusGuard.InputSuspended ? 4 : 0);
            if (state == painted) return;
            painted = state;
            var radius = KitType.Units(SpatialKit.ControlRadiusDegrees);
            var frame = KitType.Units(0.08f);
            if (model.Selected)
            {
                // Chosen: raised toward the person on its own fill, its words heavier; framed only while pointed at.
                plate.Draw(size, radius, KitType.Colour(SpatialKit.Raised), hovered ? Color.white : Color.clear, hovered ? frame : 0f);
                plate.Selection = SurfaceSelection.Lit;
            }
            else if (hovered)
            {
                plate.Draw(size, radius, Color.clear, KitType.Mix(SpatialKit.Side, GlazeColor.Hex(0xFFFFFF), 0.5f), frame);
                plate.Selection = SurfaceSelection.Pointed;
            }
            else
            {
                plate.Draw(size, radius, Color.clear);
                plate.Selection = SurfaceSelection.None;
            }
            plate.Fade(FocusGuard.InputSuspended ? 0.45f : 1f);
        }
    }
}
