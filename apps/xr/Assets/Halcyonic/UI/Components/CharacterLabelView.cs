#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A character's label (ADR 0023), its three parts apart: the state badge on the title plate's
    /// top edge, the task's title on the plate in at most two lines, and its mark, Practice, Demo or
    /// Recorded, on the plate's bottom edge. The reason it needs the person is not here; the peek
    /// shows it (<see cref="PeekCardView"/>). Built in units of the distance from the eyes, under the
    /// character's own body; it reads seen along its parent's forward axis.
    /// </summary>
    /// <remarks>
    /// The mark sits under the title rather than beside the badge: six characters stand 12 degrees
    /// apart, and a badge with a mark beside it would reach a neighbour's. For the same reason the
    /// badge shows its icon only while it stays as narrow as the widest plate, 10.5 degrees; a longer
    /// word, such as Finished this round, shows alone. Beside a window the label shows its badge,
    /// under it the title in one short line on a plate of its own, cut to the widest plate's 10.5
    /// degrees, so tasks can be told apart, and under that its mark (<see cref="BadgeOnly"/>): work
    /// that is not real always says so, and the whole title waits for the peek.
    /// </remarks>
    public sealed class CharacterLabelView : MonoBehaviour
    {
        /// <summary>The badge's top, below the body's centre: clear of the body as it settles or slumps.</summary>
        public const float TopDegrees = 5f;

        /// <summary>The widest a plate gets: 1.5 degrees less than the 12 between neighbours.</summary>
        public const float MaxWidthDegrees = 10.5f;

        public const int TitleLines = 2;

        private const float SideDegrees = 0.7f;
        private const float TitleGapDegrees = 0f;
        private const float BottomDegrees = 0.5f;
        /// <summary>Between the title's last line and the mark on the plate's edge: its descenders' room.</summary>
        private const float MarkRoomDegrees = 0.15f;
        private const float MarkEdgeDegrees = 0.1f;

        /// <summary>Beside a window: between the badge and the short title's plate, and inside that plate above and below its line.</summary>
        private const float ShortTitleGapDegrees = 0.25f;

        private const float ShortTitlePaddingDegrees = 0.15f;
        private const float MarkDashDegrees = 0.45f;

        private readonly List<MarkTag> tags = new List<MarkTag>();
        private Surface plate = null!;
        private TextMeshPro title = null!;
        private TextMeshPro shortTitle = null!;
        private StateBadgeView badge = null!;
        private string shownTitle = "";
        private string shownMark = "";
        private float shownBadgeWidth = -1f;
        private float topDegrees = TopDegrees;
        private CharacterLabel? shown;
        private float plateWidth;
        private float bottom;
        private bool badgeOnly;

        /// <summary>How far below its origin the label reaches, its mark included: a negative height, in its parent's units.</summary>
        public float Bottom => bottom;

        /// <summary>Half the width of the label's widest part, in its parent's units.</summary>
        public float HalfWidth { get; private set; }

        public StateBadgeView Badge => badge;

        public TextMeshPro Title => title;

        /// <summary>The title in one short line, beside a window.</summary>
        public TextMeshPro ShortTitle => shortTitle;

        public IReadOnlyList<MarkTag> Tags => tags;

        public Surface Plate => plate;

        /// <summary>Shows only the badge and the marks under it, no plate or title, as the characters beside a window do.</summary>
        public bool BadgeOnly
        {
            get => badgeOnly;
            set
            {
                if (badgeOnly == value) return;
                badgeOnly = value;
                if (shown != null) Layout(shown);
            }
        }

        /// <summary>
        /// The lowest any label reaches below its origin, with two lines of title and a mark: where
        /// what stands under the stage starts.
        /// </summary>
        public static float DeepestBottom
        {
            get
            {
                var face = TMP_Settings.defaultFontAsset.faceInfo;
                var line = GlazeTokens.Units(Glaze.TitleDegrees) * face.lineHeight / face.pointSize;
                return -(GlazeTokens.Units(TopDegrees) + StateBadgeView.Height + GlazeTokens.Units(TitleGapDegrees) + TitleLines * line
                    + GlazeTokens.Units(MarkRoomDegrees) + MarkTag.Height);
            }
        }

        public static CharacterLabelView Create(Transform parent)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CharacterLabelView>();
            view.plate = Surface.Create(go.transform, "Plate", 0);
            view.title = GlazeText.Create(go.transform, "Title", GlazeType.Title, GlazeTokens.Text, TextAlignmentOptions.Top, 2);
            view.shortTitle = GlazeText.Create(go.transform, "Short title", GlazeType.Caption, GlazeTokens.Text, TextAlignmentOptions.Top, 2);
            view.shortTitle.gameObject.SetActive(false);
            view.badge = StateBadgeView.Create(go.transform, "Badge", 1);
            // No wider with its icon than the widest plate, so a badge never reaches a neighbour's; a
            // longer one shows its word alone.
            view.badge.MaxWidth = GlazeTokens.Units(MaxWidthDegrees);
            return view;
        }

        /// <summary>
        /// Lowers the label as much as being seen from <paramref name="elevationDegrees"/> above or
        /// below eye level shortens the drop from the body to it, so its body never covers its badge,
        /// as on a desk seen from above.
        /// </summary>
        public void ViewFrom(float elevationDegrees)
        {
            var top = TopDegrees / Mathf.Max(Mathf.Cos(elevationDegrees * Mathf.Deg2Rad), 0.5f);
            if (Mathf.Abs(top - topDegrees) < 0.01f) return;
            topDegrees = top;
            if (shown != null) Layout(shown);
        }

        public void Show(CharacterLabel label)
        {
            shown = label;
            badge.Show(label.Badge);
            var mark = label.Marks.Count > 0 ? label.Marks[0].Word : "";
            // The plate is as wide as the badge on its edge, so a new state can widen it.
            if (label.Title == shownTitle && mark == shownMark && Mathf.Approximately(badge.Width, shownBadgeWidth)) return;
            shownTitle = label.Title;
            shownMark = mark;
            shownBadgeWidth = badge.Width;
            Layout(label);
        }

        private void Layout(CharacterLabel label)
        {
            var top = -GlazeTokens.Units(topDegrees);
            var badgeHeight = StateBadgeView.Height;
            badge.transform.localPosition = new Vector3(0f, top - badgeHeight / 2f, -0.001f);

            var side = GlazeTokens.Units(SideDegrees);
            var maxWidth = GlazeTokens.Units(MaxWidthDegrees);
            GlazeText.SetLiteral(title, label.Title);
            var titleTop = top - badgeHeight - GlazeTokens.Units(TitleGapDegrees);
            var (lines, titleWidth) = GlazeText.Lay(title, maxWidth - 2f * side, TitleLines);
            title.transform.localPosition = new Vector3(0f, titleTop, -0.001f);

            while (tags.Count < label.Marks.Count) tags.Add(MarkTag.Create(transform, "Mark " + tags.Count, 1));
            var widest = badge.Width;
            for (var index = 0; index < tags.Count; index++)
            {
                var shows = index < label.Marks.Count;
                tags[index].gameObject.SetActive(shows);
                if (!shows) continue;
                tags[index].Show(label.Marks[index]);
                widest = Mathf.Max(widest, tags[index].Width);
            }

            title.gameObject.SetActive(!badgeOnly);
            shortTitle.gameObject.SetActive(badgeOnly);
            if (badgeOnly)
            {
                // The title in one line on a plate of its own, as narrow as the widest plate, then
                // the marks in a row under it, a little apart.
                GlazeText.SetLiteral(shortTitle, label.Title);
                var (_, shortWidth) = GlazeText.Lay(shortTitle, maxWidth - 2f * side, 1);
                var line = GlazeText.LineHeight(shortTitle);
                var pad = GlazeTokens.Units(ShortTitlePaddingDegrees);
                var shortTop = top - badgeHeight - GlazeTokens.Units(ShortTitleGapDegrees);
                shortTitle.transform.localPosition = new Vector3(0f, shortTop - pad, -0.001f);
                var shortPlate = new Vector2(Mathf.Min(maxWidth, shortWidth + 2f * side), line + 2f * pad);
                plate.transform.localPosition = new Vector3(0f, shortTop - shortPlate.y / 2f, 0f);
                plate.Draw(shortPlate, GlazeTokens.Units(Glaze.PlateRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity));
                plate.gameObject.SetActive(true);
                var under = shortTop - shortPlate.y;
                var markRow = 0f;
                for (var index = 0; index < label.Marks.Count; index++) markRow += tags[index].Width + (index > 0 ? GlazeTokens.Units(0.35f) : 0f);
                var markMiddle = under - GlazeTokens.Units(MarkRoomDegrees) - MarkTag.Height / 2f;
                var left = -markRow / 2f;
                for (var index = 0; index < label.Marks.Count; index++)
                {
                    tags[index].transform.localPosition = new Vector3(left + tags[index].Width / 2f, markMiddle, -0.001f);
                    left += tags[index].Width + GlazeTokens.Units(0.35f);
                }
                bottom = label.Marks.Count > 0 ? markMiddle - MarkTag.Height / 2f : under;
                HalfWidth = Mathf.Max(Mathf.Max(widest, markRow), shortPlate.x) / 2f;
                return;
            }
            plate.gameObject.SetActive(true);
            var plateTop = top - badgeHeight / 2f;
            var marked = label.Marks.Count > 0;
            var plateBottom = titleTop - lines * GlazeText.LineHeight(title)
                - (marked ? GlazeTokens.Units(MarkRoomDegrees) + MarkTag.Height / 2f : GlazeTokens.Units(BottomDegrees));
            plateWidth = Mathf.Min(maxWidth, Mathf.Max(titleWidth + 2f * side, widest + side));
            plate.transform.localPosition = new Vector3(0f, (plateTop + plateBottom) / 2f, 0f);
            var edge = marked ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Simulated).Strong) : Color.clear;
            plate.Draw(new Vector2(plateWidth, plateTop - plateBottom), GlazeTokens.Units(Glaze.PlateRadiusDegrees),
                GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), edge, marked ? GlazeTokens.Units(MarkEdgeDegrees) : 0f,
                marked ? GlazeTokens.Units(MarkDashDegrees) : 0f);

            // The marks on the plate's bottom edge, side by side.
            var row = 0f;
            for (var index = 0; index < label.Marks.Count; index++) row += tags[index].Width + (index > 0 ? GlazeTokens.Units(0.35f) : 0f);
            var x = -row / 2f;
            for (var index = 0; index < label.Marks.Count; index++)
            {
                tags[index].transform.localPosition = new Vector3(x + tags[index].Width / 2f, plateBottom, -0.001f);
                x += tags[index].Width + GlazeTokens.Units(0.35f);
            }
            bottom = marked ? plateBottom - MarkTag.Height / 2f : plateBottom;
            HalfWidth = Mathf.Max(plateWidth, Mathf.Max(widest, row)) / 2f;
        }
    }
}
