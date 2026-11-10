#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel's rows drawn (<see cref="RoomRows"/>): each row's words in the panel's reading face, the next
    /// row under a 1 dp hairline with 9 dp above it and below, and a check or a cross before the words of a row that
    /// carries one, in the result's own colours. Its origin is the top left of the first row; it grows down, and says how
    /// tall it stands so its panel can size itself. It draws nothing of the panel and takes no press.
    /// </summary>
    public sealed class RoomRowsView : MonoBehaviour
    {
        /// <summary>The lines one row's words may take: a longer fact is the screen's to part.</summary>
        public const int RowLines = 6;

        private readonly List<TextMeshPro> labels = new List<TextMeshPro>();
        private readonly List<Surface> hairlines = new List<Surface>();
        private readonly List<RoomGlyphView> marks = new List<RoomGlyphView>();
        private int order;
        private int shown;

        /// <summary>How wide and how tall the rows stand as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The rows it shows now.</summary>
        public int Count => gameObject.activeSelf ? shown : 0;

        /// <summary>Row <paramref name="index"/>'s words, its mark (hidden where it has none) and the hairline under it (hidden under the last).</summary>
        public TextMeshPro Label(int index) => labels[index];

        public RoomGlyphView Mark(int index) => marks[index];

        public Surface Hairline(int index) => hairlines[index];

        /// <summary>The hairline's colour: white at the page's 12 percent, as the alpha a blend in linear light needs over the panel to read as the page's.</summary>
        public static Color HairlineColour => new Color(1f, 1f, 1f, RoomRows.LightAlpha(RoomRows.HairlineAlpha, RoomRows.PanelGround));

        public static Color PassedColour => Rgb(RoomRows.PassedColour);

        public static Color FailedColour => Rgb(RoomRows.FailedColour);

        private static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>Rows under <paramref name="parent"/>, hidden, drawn at <paramref name="order"/> (the panel's words).</summary>
        public static RoomRowsView Create(Transform parent, int order = KitType.Order.Words)
        {
            var go = new GameObject("Rows");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomRowsView>();
            view.order = order;
            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// Shows <paramref name="rows"/> <paramref name="width"/> wide, at the person's text size, and returns how tall
        /// they stand. More than a page's rows are the caller's to turn (<see cref="RoomRows.Page"/>): every row given is drawn.
        /// </summary>
        public float Show(IReadOnlyList<RoomRow> rows, float width)
        {
            gameObject.SetActive(true);
            shown = rows.Count;
            var unit = RoomType.Units(1f);
            var gap = RoomType.Scaled(RoomRows.GapDp);
            var hair = unit * RoomRows.HairlineDp;
            var indent = RoomType.Scaled(RoomRows.IndentDp(rows));
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var down = 0f;
            for (var index = 0; index < rows.Count; index++)
            {
                while (labels.Count <= index)
                {
                    labels.Add(RoomType.Create(transform, "Row", Color.white, order));
                    hairlines.Add(Surface.Create(transform, "Hairline", order));
                    marks.Add(RoomGlyphView.Create(transform, "Mark", order));
                }
                var label = labels[index];
                label.gameObject.SetActive(true);
                RoomType.Size(label);
                var runs = GlazeRuns.Of(label);
                runs.Set(rows[index].Words);
                runs.SetProseMode(true);
                var laid = KitType.Lay(label, width - indent, RowLines);
                var tall = Mathf.Max(line, laid.Height);
                label.transform.localPosition = new Vector3(indent, -down, 0f);
                var mark = marks[index];
                if (rows[index].Mark == RoomRowMark.None) mark.Hide();
                else
                {
                    var passed = rows[index].Mark == RoomRowMark.Check;
                    mark.Show(passed ? RoomGlyph.Check : RoomGlyph.Cross, RoomRows.MarkDp * GlazeText.Scale, passed ? PassedColour : FailedColour);
                    mark.transform.localPosition = new Vector3(RoomType.Scaled(RoomRows.MarkDp) / 2f, -down - tall / 2f, 0f);
                }
                down += tall;
                var under = hairlines[index];
                if (index == rows.Count - 1) under.gameObject.SetActive(false);
                else
                {
                    under.gameObject.SetActive(true);
                    under.Draw(new Vector2(width, hair), 0f, HairlineColour);
                    under.transform.localPosition = new Vector3(width / 2f, -down - gap - hair / 2f, 0f);
                    down += gap + hair + gap;
                }
            }
            for (var index = rows.Count; index < labels.Count; index++)
            {
                labels[index].gameObject.SetActive(false);
                hairlines[index].gameObject.SetActive(false);
                marks[index].Hide();
            }
            Size = new Vector2(width, down);
            return down;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
