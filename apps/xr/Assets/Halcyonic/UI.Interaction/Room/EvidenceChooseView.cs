#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Choose for the evidence view's strip (<see cref="EvidenceChoice"/>): a panel on the kit's neutral glass, its title in the label
    /// role, and one row a stat kind in the strip's order, each the panel's text column wide, after mirrors-edge-catalyst-main-menu.jpg's
    /// rows, where the white bar marks only the row you are on: at rest a chosen stat's words white with the kit's check at its right,
    /// an open one's words quiet, neither on a plate; the row pointed at, chosen or not, the full-width white bar with dark words, a
    /// chosen row's check drawn dark on it; a kind the read lacks its words quiet and "Not Measured" at its right in the label role,
    /// taking no press. Done stands at the foot. A press of a row toggles it (<see cref="EvidenceChoice.Toggle"/>), draws the new choice and raises
    /// <see cref="Chosen"/>; keeping the choice is the owner's. No hue: white, greys and the glass only. Its origin is the panel's
    /// middle. Presentation only.
    /// </summary>
    public sealed class EvidenceChooseView : MonoBehaviour
    {
        /// <summary>The panel's least width, the kit's panel; it grows where a row's words need more.</summary>
        public const float PanelDp = RoomGeometry.PanelDp;

        /// <summary>A row's bar: the kit's action core tall, its corner small as Catalyst's, its words this far in from its ends.</summary>
        public const float RowDp = RoomButton.CoreDp;
        public const float RowCornerDp = 6f;
        public const float RowPadDp = 16f;

        /// <summary>The least room between a row's head and what stands at its right, its check or its kind.</summary>
        public const float KindGapDp = 16f;

        /// <summary>A chosen row's check: the kit's own glyph at an action chip's size.</summary>
        public const float CheckDp = RoomButton.GlyphDp;

        /// <summary>The room under the title before the first row.</summary>
        public const float TitleGapDp = 12f;

        /// <summary>The pointed row's words and check on its white bar: a true grey near black, no hue (Catalyst's selected row puts its own colour there).</summary>
        public const int InkColour = 0x1A1A1A;

        private RoomSurface panel = null!;
        private TextMeshPro title = null!;
        private RoomButton done = null!;
        private readonly List<RowParts> rows = new List<RowParts>();
        private int used;
        private EvidenceStrip? strip;
        private IReadOnlyList<string>? chosen;
        private string? pointedForRender;

        /// <summary>Raised with the whole new choice after a row's press.</summary>
        public event Action<IReadOnlyList<string>>? Chosen;

        /// <summary>Raised by Done's press.</summary>
        public event Action? Done;

        /// <summary>The panel's size as last shown, in its parent's units.</summary>
        public Vector2 Size { get; private set; }

        public RoomSurface Panel => panel;

        public TextMeshPro Title => title;

        public RoomButton DonePress => done;

        /// <summary>The choice as last shown; null where none was made.</summary>
        public IReadOnlyList<string>? Choice => chosen;

        /// <summary>
        /// The rows as drawn, for checks: each its row, its bar (laid at the row's place and size, shown only while the row is pointed
        /// at), its head, its kind at the right (null where none), its check (null where none) and its press (null where it takes none).
        /// </summary>
        public IReadOnlyList<(EvidenceChoiceRow Row, RoomShape Plate, TMP_Text Words, TMP_Text? Kind, RoomGlyphView? Check, KitPress? Press)> Drawn =>
            rows.Take(used).Select(parts => (parts.Row!, parts.Plate, (TMP_Text)parts.Head, parts.Kind.gameObject.activeSelf ? (TMP_Text?)parts.Kind : null,
                parts.Check.gameObject.activeSelf ? parts.Check : null, parts.Target.gameObject.activeSelf ? parts.Press : null)).ToList();

        /// <summary>For renders, which have no hands: draws the row of <paramref name="key"/> as pointed at, as a hover draws it; null for none.</summary>
        public void PointForRender(string? key)
        {
            pointedForRender = key;
            for (var index = 0; index < used; index++) rows[index].Point();
        }

        public static EvidenceChooseView Create(Transform parent)
        {
            var go = new GameObject("Evidence choose");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<EvidenceChooseView>();
            view.panel = RoomSurface.Create(go.transform, "Choose glass", RoomSurfaceKind.Information, KitType.Order.Plate);
            view.title = RoomType.Create(go.transform, "Choose title", Color.white, KitType.Order.Words);
            view.title.textWrappingMode = TextWrappingModes.NoWrap;
            view.title.overflowMode = TextOverflowModes.Overflow;
            view.done = RoomButton.Create(go.transform, "Choose done");
            view.done.Pressed += () => view.Done?.Invoke();
            return view;
        }

        /// <summary>Draws the rows of <paramref name="strip"/> under <paramref name="chosen"/> (null: no choice made, the strip's default).</summary>
        public void Show(EvidenceStrip? strip, IReadOnlyList<string>? chosen)
        {
            this.strip = strip;
            this.chosen = chosen;
            gameObject.SetActive(true);
            var shown = EvidenceChoice.Rows(strip, chosen);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var rowPad = RoomType.Scaled(RowPadDp);
            var kindGap = RoomType.Scaled(KindGapDp);
            var check = RoomType.Scaled(CheckDp);
            var target = RoomType.Units(RoomButton.HeightDp);
            var plateTall = Mathf.Min(RoomType.Scaled(RowDp), target);
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);

            RoomType.Label(title, EvidenceChoice.Words.Title);
            var column = Mathf.Max(RoomType.Scaled(PanelDp) - 2f * pad, title.GetPreferredValues(title.text).x);
            while (rows.Count < shown.Count) rows.Add(RowParts.Create(this, rows.Count));
            used = shown.Count;
            for (var index = 0; index < used; index++)
            {
                var widths = rows[index].Lay(shown[index]);
                // A row that takes a press keeps its check's room chosen or not, so a press never moves its words.
                var right = Mathf.Max(widths.Kind, shown[index].State == EvidenceChoiceState.NotMeasured ? 0f : check);
                column = Mathf.Max(column, 2f * rowPad + widths.Head + (right > 0f ? kindGap + right : 0f));
            }
            var doneSize = done.Measure(EvidenceChoice.Words.Done, 0f);
            column = Mathf.Max(column, doneSize.x);

            // Laid top down from the panel's top: the title, the rows a target apart, Done a target's gap under the last row.
            var doneTarget = Mathf.Max(target, doneSize.y);
            var tall = 2f * pad + line + RoomType.Scaled(TitleGapDp) + used * target + Mathf.Max(0, used - 1) * gap + gap + doneTarget;
            Size = new Vector2(column + 2f * pad, tall);
            panel.Draw(Size);
            var top = Size.y / 2f - pad;
            var left = -column / 2f;
            title.rectTransform.sizeDelta = new Vector2(column, line);
            title.transform.localPosition = new Vector3(left, top, -RoomType.Units(0.5f));
            var y = top - line - RoomType.Scaled(TitleGapDp);
            for (var index = 0; index < rows.Count; index++)
            {
                var parts = rows[index];
                if (index >= used)
                {
                    parts.Root.gameObject.SetActive(false);
                    continue;
                }
                parts.Root.gameObject.SetActive(true);
                parts.Root.localPosition = new Vector3(0f, y - target / 2f, 0f);
                parts.Draw(column, plateTall, target, rowPad);
                y -= target + gap;
            }
            if (used == 0) y -= gap;
            // Done on the rows' right edge, as every panel's foot press stands (the kit pass; visionos-window-example.png).
            done.Show(EvidenceChoice.Words.Done, null, isMain: false, available: true, new Vector2(left + column - doneSize.x / 2f, y - doneTarget / 2f), doneSize.x);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Pressed(RowParts parts)
        {
            if (strip == null || parts.Row == null || parts.Row.State == EvidenceChoiceState.NotMeasured) return;
            var next = EvidenceChoice.Toggle(chosen, parts.Row.Key, strip);
            Show(strip, next);
            Chosen?.Invoke(next);
        }

        private void Update()
        {
            for (var index = 0; index < used; index++) rows[index].Point();
        }

        private static Color Ink => new Color(((InkColour >> 16) & 0xFF) / 255f, ((InkColour >> 8) & 0xFF) / 255f, (InkColour & 0xFF) / 255f, 1f);

        /// <summary>Quiet white: the label role's, the quietest the kit holds at 4.5 to 1 where the bright room shows through the glass.</summary>
        private static Color Quiet => new Color(1f, 1f, 1f, RoomType.LabelAlpha);

        /// <summary>One row's parts, kept and drawn again as the choice changes.</summary>
        private sealed class RowParts
        {
            public Transform Root = null!;
            public RoomShape Plate = null!;
            public TextMeshPro Head = null!;
            public TextMeshPro Kind = null!;
            public RoomGlyphView Check = null!;
            public Transform Target = null!;
            public KitPress Press = null!;
            public EvidenceChoiceRow? Row;
            private EvidenceChooseView view = null!;
            private bool pointed;

            public static RowParts Create(EvidenceChooseView view, int index)
            {
                var parts = new RowParts { view = view };
                var root = new GameObject("Choose row " + index).transform;
                root.SetParent(view.transform, false);
                parts.Root = root;
                parts.Plate = RoomShape.Create(root, "Row bar", KitType.Order.Chip);
                parts.Plate.gameObject.SetActive(false);
                parts.Head = RoomType.Create(root, "Row head", Color.white, KitType.Order.Words, TextAlignmentOptions.Left);
                parts.Kind = RoomType.Create(root, "Row kind", Color.white, KitType.Order.Words, TextAlignmentOptions.Right);
                foreach (var label in new[] { parts.Head, parts.Kind })
                {
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode = TextOverflowModes.Overflow;
                }
                parts.Head.rectTransform.pivot = new Vector2(0f, 0.5f);
                parts.Kind.rectTransform.pivot = new Vector2(1f, 0.5f);
                parts.Check = RoomGlyphView.Create(root, "Row check", KitType.Order.Words + 1);
                var targetGo = new GameObject("Row target");
                targetGo.transform.SetParent(root, false);
                parts.Target = targetGo.transform;
                parts.Press = KitPress.Create(targetGo, Vector2.one * RoomType.Units(RoomButton.HeightDp));
                parts.Press.Pressed += () => view.Pressed(parts);
                return parts;
            }

            /// <summary>Sets the row's words and returns their widths, the kind's 0 where it has none.</summary>
            public (float Head, float Kind) Lay(EvidenceChoiceRow row)
            {
                var another = Row == null || Row.Key != row.Key;
                Row = row;
                RoomType.Size(Head);
                GlazeText.SetLiteral(Head, row.Head);
                var kindWidth = 0f;
                Kind.gameObject.SetActive(row.Kind != null);
                if (row.Kind != null)
                {
                    RoomType.Label(Kind, row.Kind);
                    kindWidth = Kind.GetPreferredValues(Kind.text).x;
                }
                // Another stat in this place is another control: a press already under way is not one of it.
                if (another) Press.Unsettle();
                return (Head.GetPreferredValues(Head.text).x, kindWidth);
            }

            public void Draw(float column, float plateTall, float target, float rowPad)
            {
                var row = Row!;
                var line = RoomType.Scaled(RoomGeometry.LineDp);
                // The bar is laid at rest too, hidden, so it stands ready, full width, for the pointer.
                Plate.Draw(new Vector2(column, plateTall), RoomType.Scaled(RowCornerDp), Color.white);
                Plate.transform.localPosition = new Vector3(0f, 0f, -RoomType.Units(0.3f));
                Head.rectTransform.sizeDelta = new Vector2(Mathf.Max(Head.GetPreferredValues(Head.text).x, 1e-4f), line);
                Head.transform.localPosition = new Vector3(-column / 2f + rowPad, 0f, -RoomType.Units(0.5f));
                if (Kind.gameObject.activeSelf)
                {
                    Kind.rectTransform.sizeDelta = new Vector2(Mathf.Max(Kind.GetPreferredValues(Kind.text).x, 1e-4f), line);
                    Kind.transform.localPosition = new Vector3(column / 2f - rowPad, 0f, -RoomType.Units(0.5f));
                }
                Check.transform.localPosition = new Vector3(column / 2f - rowPad - RoomType.Scaled(CheckDp) / 2f, 0f, -RoomType.Units(0.6f));
                var pressable = row.State != EvidenceChoiceState.NotMeasured;
                Target.gameObject.SetActive(pressable);
                if (pressable) Press.Resize(new Vector2(column, target));
                pointed = Pointed();
                Paint();
            }

            /// <summary>Repaints the row when the pointer comes onto it or leaves.</summary>
            public void Point()
            {
                var now = Pointed();
                if (now == pointed) return;
                pointed = now;
                Paint();
            }

            private bool Pointed() =>
                Row != null && Row.State != EvidenceChoiceState.NotMeasured && (view.pointedForRender == Row.Key || (Target.gameObject.activeInHierarchy && Press.Hovered));

            private void Paint()
            {
                var row = Row!;
                Plate.gameObject.SetActive(pointed);
                var words = pointed ? Ink : row.State == EvidenceChoiceState.Chosen ? Color.white : Quiet;
                Head.color = words;
                Kind.color = pointed ? Ink : Quiet;
                if (row.State == EvidenceChoiceState.Chosen) Check.Show(RoomGlyph.Check, CheckDp * GlazeText.Scale, pointed ? Ink : Color.white);
                else Check.Hide();
            }
        }
    }
}
