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
    /// One file's glass panel in Watch It Work (FIELD's SHAPE of 2026-11-16, merged with XR's and CORE's; AUDIT's three MUSTs):
    /// the redesign's information glass, the panel's width, and on it the file as the Changes reading draws a file (FIELD's
    /// card: the file's own name in the mono face, its folder quiet under it, its lines in the reading's dark well with their
    /// indent), the rows a later read changed lit with the one accent (<see cref="RoomCard.ReadingLit"/>). It arrives only with
    /// its first read in hand (<see cref="WatchBuild"/> stands it then), rising from 0.92 of its size over the standard curve's
    /// 600 ms, its glass and its words together; each read's new rows then pour in top down at 18 a second, or all within a
    /// second, whichever is quicker, each fading in over 120 ms: one short pass, never typing speed (AUDIT2's condition), and
    /// rows already shown stay shown. Kept still (<see cref="GlazeMotion.Still"/>) it cross-fades in over 200 ms where it stands
    /// and each read stands whole at once. A file whose text is
    /// not shown says why in one line, its name standing. It takes no press. Its origin is its middle.
    /// </summary>
    public sealed class RoomWritingPanel : MonoBehaviour
    {
        /// <summary>How long the panel takes to arrive, and the size it rises from.</summary>
        public const float ArriveSeconds = 0.6f;
        public const float ArriveFrom = 0.92f;

        /// <summary>How far back its glass and words start before they come to rest, and how long Still's cross-fade takes in its place.</summary>
        public const float ArriveBackDp = 2f;
        public const float StillFadeSeconds = 0.2f;

        /// <summary>The reveal's pace: rows a second, the most seconds a read's reveal takes, and each row's fade.</summary>
        public const float RowsASecond = 18f;
        public const float RevealMostSeconds = 1f;
        public const float RowFadeSeconds = 0.12f;

        private RoomSurface glass = null!;
        private RoomCardView card = null!;
        private string? path;
        private float arrivedAt;
        private IReadOnlyList<string> shownRows = Array.Empty<string>();
        private int revealFrom;
        private float revealAt;
        private float revealRate = RowsASecond;
        private readonly List<(TMP_Text Label, float Alpha, int Row)> words = new List<(TMP_Text, float, int)>();
        private readonly List<(RoomShape Shape, Color Fill, Color Edge)> plates = new List<(RoomShape, Color, Color)>();
        private float platesAt = -1f;
        private float folded = 1f;

        /// <summary>How small a panel folds to as it reaches its row.</summary>
        public const float FoldTo = 0.3f;

        /// <summary>The clock the panel moves by; renders fix it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>The card the panel draws its file on, for the checks.</summary>
        public RoomCardView Card => card;

        public RoomSurface Glass => glass;

        /// <summary>How wide and tall the panel stands as last laid, glass included.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The file shown, by its path; null before any.</summary>
        public string? Path => path;

        public static RoomWritingPanel Create(Transform parent, string name = "Writing panel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomWritingPanel>();
            view.glass = RoomSurface.Create(go.transform, "Glass", RoomSurfaceKind.Information, KitType.Order.Plate);
            view.card = RoomCardView.Create(view.glass.transform);
            go.SetActive(false);
            return view;
        }

        /// <summary>How many columns a continued row hangs in past its line's own indent.</summary>
        public const int HangColumns = 2;

        /// <summary>
        /// One line of a file as the rows it lays at the reading measure and the person's text size: WatchBuild's wrap. Each row
        /// keeps its line's indent as leading spaces, a continued row <see cref="HangColumns"/> more, so <see cref="Show"/> lays
        /// each with its indent again.
        /// </summary>
        public static IReadOnlyList<string> Wrap(string line) =>
            FileChangeColumn.Wrap(RoomLineKind.Same, null, line, GlazeText.Scale)
                .Select(row => new string(' ', row.Indent + (row.Continued ? HangColumns : 0)) + (row.Text?.Text ?? "")).ToList();

        /// <summary>A row as <see cref="Wrap"/> gave it, laid: its leading spaces as its indent, its words as the file has them.</summary>
        public static RoomReadingRow RowOf(string row)
        {
            var words = row.TrimStart(' ');
            return new RoomReadingRow(RoomLineKind.Same, null, words.Length == 0 ? null : LineWords.Theirs(words), indent: row.Length - words.Length);
        }

        /// <summary>The words that stand in place of a file's text it does not show, from the product's words; null where it shows its text.</summary>
        public static Func<WritingPanel, string?> NotShownWords { get; set; } = _ => null;

        /// <summary>Lays <paramref name="panel"/>; a new file arrives, a newer read of the same file pours in its new rows. Returns the panel's size.</summary>
        public Vector2 Show(WritingPanel panel)
        {
            gameObject.SetActive(true);
            var now = Clock();
            if (path != panel.Path)
            {
                path = panel.Path;
                folded = 1f;
                arrivedAt = now;
                shownRows = Array.Empty<string>();
            }
            // The rows that stand as before stay; the reveal runs from the first that differs.
            var same = 0;
            while (same < shownRows.Count && same < panel.Lines.Count && shownRows[same] == panel.Lines[same]) same++;
            if (same != panel.Lines.Count || shownRows.Count != panel.Lines.Count)
            {
                revealFrom = same;
                revealAt = now;
                revealRate = Mathf.Max(RowsASecond, (panel.Lines.Count - same) / RevealMostSeconds);
            }
            shownRows = panel.Lines;
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var reading = panel.Lines.Select(RowOf).ToList();
            var notShown = NotShownWords(panel);
            var laid = new RoomCard(panel.Name, panel.Folder, status: notShown == null ? null : new RoomCardStatus(notShown, null, RoomTone.Quiet),
                reading: notShown == null ? reading : null, titleMono: true, readingLit: notShown == null ? panel.Lit : null);
            var tall = card.Show(laid, width - 2f * pad);
            Size = new Vector2(width, tall + 2f * pad);
            glass.InnerBar = null;
            glass.Draw(Size);
            card.transform.localPosition = new Vector3(-width / 2f + pad, Size.y / 2f - pad, -RoomType.Units(0.5f));
            // Each word and plate as the card laid it, so the arrival scales their own alphas and never overwrites a quiet one.
            var rowOf = new Dictionary<TMP_Text, int>();
            for (int row = 0, label = 0; row < reading.Count && notShown == null; row++)
                if (reading[row].Text is LineWords text && !text.IsEmpty && label < card.ReadingShown.Count) rowOf[card.ReadingShown[label++]] = row;
            words.Clear();
            foreach (var label in GetComponentsInChildren<TMP_Text>(false))
                words.Add((label, label.alpha, rowOf.TryGetValue(label, out var row) ? row : -1));
            plates.Clear();
            foreach (var shape in card.GetComponentsInChildren<RoomShape>(false)) plates.Add((shape, shape.Fill, shape.Edge));
            platesAt = -1f;
            Follow();
            return Size;
        }

        private void Update() => Follow();

        /// <summary>The panel as its arrival and its reveal stand now: its scale, its glass and words faded in, each new row by its turn.</summary>
        private void Follow()
        {
            if (path == null) return;
            // Kept still, the panel only cross-fades in where it stands; else it rises from 0.92 and 2 dp back.
            var still = GlazeMotion.Still;
            var since = Clock() - arrivedAt;
            var arrived = still ? Mathf.Clamp01(since / StillFadeSeconds) : RoomMotion.Standard(since / ArriveSeconds);
            // Folding into its row, its code goes first (the fold's first third), then its glass shrinks and fades.
            var code = Mathf.Clamp01((folded - 2f / 3f) * 3f);
            transform.localScale = Vector3.one * (still ? 1f : Mathf.Lerp(ArriveFrom, 1f, arrived) * Mathf.Lerp(FoldTo, 1f, folded));
            arrived *= folded;
            glass.transform.localPosition = new Vector3(0f, 0f, still ? 0f : RoomType.Units(ArriveBackDp) * (1f - arrived));
            glass.Shown = arrived;
            foreach (var (label, own, row) in words)
            {
                if (label == null) continue;
                var alpha = own * arrived * (row >= 0 ? RowShown(row) * code : 1f);
                if (!Mathf.Approximately(label.alpha, alpha)) label.alpha = alpha;
            }
            if (Mathf.Approximately(platesAt, arrived)) return;
            platesAt = arrived;
            foreach (var (shape, fill, edge) in plates)
            {
                if (shape == null) continue;
                shape.Draw(shape.Size, shape.CornerRadius, Faded(fill, arrived), Faded(edge, arrived), shape.EdgeWidth);
            }
        }

        private static Color Faded(Color colour, float shown) => new Color(colour.r, colour.g, colour.b, colour.a * shown);

        /// <summary>How far row <paramref name="row"/> of the reading stands in now: shown before this read, or by its turn in the pour.</summary>
        public float RowShown(int row)
        {
            if (GlazeMotion.Still || row < revealFrom) return 1f;
            var turn = (row - revealFrom) / revealRate;
            return Mathf.Clamp01((Clock() - revealAt - turn) / RowFadeSeconds);
        }

        /// <summary>For renders: the panel arrived and every row in.</summary>
        public void Settle()
        {
            arrivedAt = Clock() - ArriveSeconds;
            revealAt = Clock() - (RevealMostSeconds + RowFadeSeconds + 1f);
            Follow();
        }

        /// <summary>
        /// Folds the panel toward its row: <paramref name="left"/> runs from 1, whole, to 0, gone into the row. Kept still
        /// it only fades where it stands.
        /// </summary>
        public void Fold(float left, bool still)
        {
            folded = Mathf.Clamp01(left);
            Follow();
        }

        public void Hide()
        {
            folded = 1f;
            path = null;
            gameObject.SetActive(false);
        }
    }
}
