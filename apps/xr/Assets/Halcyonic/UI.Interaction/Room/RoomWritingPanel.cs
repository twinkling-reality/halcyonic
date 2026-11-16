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
        private readonly List<(TMP_Text Label, Color Own, int Row)> words = new List<(TMP_Text, Color, int)>();
        private readonly List<(RoomShape Shape, Color Fill, Color Edge, bool Lit)> plates = new List<(RoomShape, Color, Color, bool)>();
        private readonly List<(int Row, Vector2 Left, float Width)> lightRows = new List<(int, Vector2, float)>();
        private readonly List<RoomShape[]> lights = new List<RoomShape[]>();
        private readonly List<(RoomShape Plate, Vector2 Size, Vector3 At, int First, int Count)> litRuns = new List<(RoomShape, Vector2, Vector3, int, int)>();
        private float platesAt = -1f;

        /// <summary>How far toward the accent a row's words arrive: a light tint that settles into their own, always 4.5:1 over the lit tint (AUDIT, 07:54Z).</summary>
        public const float ArriveTint = 0.35f;

        /// <summary>
        /// The light a read lands with (AUDIT's raised bar, the lead's 04:19Z): as each new row's turn comes, a thin bright
        /// line of the one accent runs under its words, left to right in 180 ms, with a faint glow rising from it in two wider
        /// layers, so it reads as light and never as a selection (the reader of 07:54Z), all fading over the 400 ms after; its
        /// words arrive in the accent and settle into their own colour over the same 400 ms. Nothing shows before a row's
        /// turn. Kept still, there is none of it.
        /// </summary>
        public const float LightAcrossSeconds = 0.18f;
        public const float LightFadeSeconds = 0.3f;

        /// <summary>The light's line: this tall, this strong; and its glow's two layers, each a share of the row's pitch and its strength.</summary>
        public const float LightLineDp = 3f;
        public const float LightPeak = 1f;
        public static readonly (float Tall, float Alpha)[] LightGlow = { (0.7f, 0.08f), (0.38f, 0.14f) };

        /// <summary>The light's head, the bright front it runs with: this long, this tall, white-hot accent while it runs, gone once across.</summary>
        public const float LightHeadDp = 28f;
        public const float LightHeadTallDp = 5f;
        private float folded = 1f;

        /// <summary>How small a panel folds to as it reaches its row.</summary>
        public const float FoldTo = 0.3f;

        /// <summary>How much of the fold is left when the words are gone: the glass then 0.93 of its size, where the code still stands at the kit's floor.</summary>
        public const float WordsGoneAt = 0.9f;

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

        /// <summary>
        /// The words that stand in place of a file's text it does not show, its title and its quieter line: the Changes
        /// reading's own (<see cref="FileChangeText"/>), so a file reads the same while it is written and in the result;
        /// null where it shows its text.
        /// </summary>
        public static Func<WritingPanel, (string Title, string? Line)?> NotShownWords { get; set; } = NotShownOf;

        /// <summary>A file the computer could not read: its title, Title Case beside the reading's own (AUDIT2's 08:16Z), no line under it.</summary>
        public const string UnreadableWords = "Couldn't Be Read";

        public static (string Title, string? Line)? NotShownOf(WritingPanel panel) => panel.State switch
        {
            WritingState.Changing or WritingState.Settled => panel.Lines.Count == 0 ? (FileChangeText.Empty, null) : null,
            WritingState.KeptPrivate => (FileChangeText.KeptPrivate, FileChangeText.KeptPrivateLine),
            WritingState.NotText => (FileChangeText.NotText, null),
            WritingState.TooLarge => (FileChangeText.TooLarge, FileChangeText.TooLargeLine),
            WritingState.Unreadable => (UnreadableWords, null),
            _ => (UnreadableWords, null),
        };

        /// <summary>Lays <paramref name="panel"/>; a new file arrives, a newer read of the same file pours in its new rows. Returns the panel's size.</summary>
        public Vector2 Show(WritingPanel panel)
        {
            gameObject.SetActive(true);
            card.gameObject.SetActive(true);
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
            var laid = new RoomCard(panel.Name, panel.Folder, status: notShown is var (said, line) ? new RoomCardStatus(said, line, RoomTone.Quiet) : null,
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
                words.Add((label, label.color, rowOf.TryGetValue(label, out var row) ? row : -1));
            plates.Clear();
            var litBands = new HashSet<RoomShape>(card.Plates.Where(each => each.What == "reading lit band" || each.What == "reading lit rule").Select(each => each.Plate));
            var ours = new HashSet<RoomShape>(lights.SelectMany(layers => layers));
            foreach (var shape in card.GetComponentsInChildren<RoomShape>(false))
                if (!ours.Contains(shape)) plates.Add((shape, shape.Fill, shape.Edge, litBands.Contains(shape)));
            platesAt = -1f;
            // The changed rows' tint and rule, each run of them in the file's order, so each grows down with its rows' words.
            litRuns.Clear();
            var runs = new List<(int First, int Count)>();
            foreach (var row in (panel.Lit ?? Array.Empty<int>()).Where(row => row >= 0 && row < reading.Count).Distinct().OrderBy(row => row))
            {
                if (runs.Count > 0 && runs[runs.Count - 1].First + runs[runs.Count - 1].Count == row) runs[runs.Count - 1] = (runs[runs.Count - 1].First, runs[runs.Count - 1].Count + 1);
                else runs.Add((row, 1));
            }
            foreach (var what in new[] { "reading lit band", "reading lit rule" })
            {
                var drawnRuns = card.Plates.Where(each => each.What == what).Select(each => each.Plate).ToList();
                for (var index = 0; index < drawnRuns.Count && index < runs.Count && notShown == null; index++)
                    litRuns.Add((drawnRuns[index], drawnRuns[index].Size, drawnRuns[index].transform.localPosition, runs[index].First, runs[index].Count));
            }
            // Where each row this read pours stands, in the card's own space: the light runs under its words.
            lightRows.Clear();
            foreach (var (label, row) in rowOf)
            {
                if (row < revealFrom) continue;
                label.ForceMeshUpdate(true);
                var bounds = label.textBounds;
                if (bounds.size.x <= 0f) continue;
                // The line under the words' baseline, the glow rising from it.
                var left = card.transform.InverseTransformPoint(label.transform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, 0f)));
                var right = card.transform.InverseTransformPoint(label.transform.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, 0f)));
                lightRows.Add((row, new Vector2(left.x, left.y), right.x - left.x));
            }
            while (lights.Count < lightRows.Count)
                lights.Add(new[] { RoomShape.Create(card.transform, "Read light", KitType.Order.Words - 1) }
                    .Concat(LightGlow.Select(_ => RoomShape.Create(card.transform, "Read light glow", KitType.Order.Words - 3)))
                    .Concat(new[] { RoomShape.Create(card.transform, "Read light head", KitType.Order.Words - 1) }).ToArray());
            for (var index = lightRows.Count; index < lights.Count; index++)
                foreach (var layer in lights[index]) layer.gameObject.SetActive(false);
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
            // Folding into its row, its words go first, gone before the glass shrinks a word under the kit's floor (the fold's
            // first tenth), then the glass alone shrinks and fades into the row.
            var kept = folded >= 1f ? 1f : Mathf.Clamp01((folded - WordsGoneAt) / (1f - WordsGoneAt));
            transform.localScale = Vector3.one * (still ? 1f : Mathf.Lerp(ArriveFrom, 1f, arrived) * Mathf.Lerp(FoldTo, 1f, folded));
            var glassShown = arrived * folded;
            arrived *= kept;
            glass.transform.localPosition = new Vector3(0f, 0f, still ? 0f : RoomType.Units(ArriveBackDp) * (1f - arrived));
            glass.Shown = glassShown;
            var accent = RoomSurface.Hex(RoomCardView.Accent);
            foreach (var (label, own, row) in words)
            {
                if (label == null) continue;
                var alpha = own.a * arrived * (row >= 0 ? RowShown(row) : 1f);
                // A row this read pours arrives in the accent and settles into its own colour.
                var tint = row >= 0 ? ArriveTint * LightAt(row).Strength : 0f;
                var colour = new Color(Mathf.Lerp(own.r, accent.r, tint), Mathf.Lerp(own.g, accent.g, tint), Mathf.Lerp(own.b, accent.b, tint), alpha);
                if (label.color != colour) label.color = colour;
            }
            for (var index = 0; index < lightRows.Count; index++)
            {
                var (row, left, width) = lightRows[index];
                var (across, strength) = LightAt(row);
                var layers = lights[index];
                var on = strength > 0.001f && across > 0.001f && kept > 0f;
                foreach (var layer in layers)
                    if (layer.gameObject.activeSelf != on) layer.gameObject.SetActive(on);
                if (!on) continue;
                var pitch = RoomType.Scaled(RoomReading.LineDp);
                var line = RoomType.Scaled(LightLineDp);
                var long_ = Mathf.Max(line, width * across);
                // Its strength grows with its run, so a light just begun is faint, never a mark standing where words will be.
                var grown = Mathf.Sqrt(across);
                layers[0].Draw(new Vector2(long_, line), line / 2f, new Color(accent.r, accent.g, accent.b, LightPeak * strength * grown * arrived));
                layers[0].transform.localPosition = new Vector3(left.x + long_ / 2f, left.y - line, -RoomType.Units(0.6f));
                // The glow grows with the run, so the light's start is never a block standing where words will be (no caret).
                for (var glow = 0; glow < LightGlow.Length; glow++)
                {
                    var tall = pitch * LightGlow[glow].Tall;
                    layers[glow + 1].Draw(new Vector2(long_, tall), Mathf.Min(tall, long_) / 2f, new Color(accent.r, accent.g, accent.b, LightGlow[glow].Alpha * strength * across * arrived));
                    layers[glow + 1].transform.localPosition = new Vector3(left.x + long_ / 2f, left.y - line + tall / 2f, -RoomType.Units(0.4f));
                }
                // The head: a brighter, whiter front on the line while it runs, gone as it reaches the row's end.
                var head = layers[layers.Length - 1];
                var heat = across < 1f ? grown * (1f - across) * 2f : 0f;
                var headOn = heat > 0.01f;
                if (head.gameObject.activeSelf != headOn) head.gameObject.SetActive(headOn);
                if (headOn)
                {
                    var headLong = Mathf.Min(long_, RoomType.Scaled(LightHeadDp));
                    var headTall = RoomType.Scaled(LightHeadTallDp);
                    var hot = Color.Lerp(accent, Color.white, 0.55f);
                    head.Draw(new Vector2(headLong, headTall), headTall / 2f, new Color(hot.r, hot.g, hot.b, Mathf.Clamp01(heat) * strength * arrived));
                    head.transform.localPosition = new Vector3(left.x + long_ - headLong / 2f, left.y - line, -RoomType.Units(0.65f));
                }
            }
            // The card, its words and its well, stands only while it shows: gone once the fold has taken its words.
            var cardShown = kept > 0f;
            if (card.gameObject.activeSelf != cardShown) card.gameObject.SetActive(cardShown);
            // A changed row's tint comes with its words, never before them: each run grows down as its rows pour in.
            var litOf = new Dictionary<RoomShape, (Color Fill, Color Edge)>();
            foreach (var (shape, fill, edge, lit) in plates) if (lit && shape != null) litOf[shape] = (fill, edge);
            var poured = 0f;
            foreach (var (plate, size, at, first, count) in litRuns)
            {
                if (plate == null || !litOf.TryGetValue(plate, out var look)) continue;
                var rowsIn = 0f;
                for (var row = first; row < first + count; row++) rowsIn += RowShown(row);
                poured += rowsIn;
                var pitch = size.y / count;
                var tall = Mathf.Max(0f, pitch * rowsIn);
                var on = tall > 0.0001f;
                if (plate.gameObject.activeSelf != on) plate.gameObject.SetActive(on);
                if (!on) continue;
                var top = at.y + size.y / 2f;
                plate.Draw(new Vector2(size.x, tall), Mathf.Min(plate.CornerRadius, tall / 2f), Faded(look.Fill, arrived), Faded(look.Edge, arrived), plate.EdgeWidth);
                plate.transform.localPosition = new Vector3(at.x, top - tall / 2f, at.z);
            }
            var drawnAt = arrived + poured * 7f;
            if (Mathf.Approximately(platesAt, drawnAt)) return;
            platesAt = drawnAt;
            foreach (var (shape, fill, edge, lit) in plates)
            {
                if (shape == null || lit) continue;
                shape.Draw(shape.Size, shape.CornerRadius, Faded(fill, arrived), Faded(edge, arrived), shape.EdgeWidth);
            }
        }

        /// <summary>
        /// The light under row <paramref name="row"/> now: how far across its words it has run (0 to 1) and how strong it
        /// stands (1 while it runs, fading to 0 over <see cref="LightFadeSeconds"/>); none for a row shown before this read.
        /// </summary>
        public (float Across, float Strength) LightAt(int row)
        {
            if (GlazeMotion.Still || row < revealFrom) return (1f, 0f);
            var t = Clock() - revealAt - (row - revealFrom) / revealRate;
            if (t < 0f) return (0f, 0f);
            var across = RoomMotion.Standard(Mathf.Clamp01(t / LightAcrossSeconds));
            var strength = t <= LightAcrossSeconds ? 1f : 1f - RoomMotion.Standard(Mathf.Clamp01((t - LightAcrossSeconds) / LightFadeSeconds));
            return (across, strength);
        }

        /// <summary>The light's line under row <paramref name="row"/> as drawn now, and how wide its row's words stand; null where none runs.</summary>
        public (RoomShape Light, float Width)? LightOf(int row)
        {
            for (var index = 0; index < lightRows.Count; index++)
                if (lightRows[index].Row == row && lights[index][0].gameObject.activeSelf) return (lights[index][0], lightRows[index].Width);
            return null;
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
            revealAt = Clock() - (RevealMostSeconds + LightAcrossSeconds + LightFadeSeconds + 1f);
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
