#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The reading surface (the art direction's handoff; R7: the thing acted on shown whole before the act): a command of theirs
    /// read on a panel of the room's glass before it can run. Its head says who asks and what (the card's mark, eyebrow and
    /// Title), with Fold at its top right; its well shows the command in their monospace by rows, each line wrapped at its
    /// spaces (<see cref="KitReading"/>) and never cut, a wrapped row hanging two characters in, comments quieter; at an edge
    /// where rows are hidden, the next row stands half-faded under a fade; at the end, "End of Command" and what answering may do. Its foot stands fixed from the first
    /// frame: Run's slot is the locked "Read to End", filling as far as the command has been read, and becomes Run only once the
    /// last row of the last line has been shown (<see cref="KitReading"/>), so a line longer than the well is read to its end
    /// first; then Decline and Reply. Centred on its transform.
    /// </summary>
    public sealed class KitReader : MonoBehaviour
    {
        private const int MostLines = 64;
        private static readonly float[] FadeSteps = { 0.75f, 0.5f, 0.3f, 0.12f };

        private KitCarrier carrier = null!;
        private Surface mark = null!;
        private TextMeshPro symbol = null!;
        private TextMeshPro eyebrow = null!;
        private TextMeshPro peekAbove = null!;
        private TextMeshPro peekBelow = null!;
        private TextMeshPro title = null!;
        private TextMeshPro end = null!;
        private Surface well = null!;
        private KitButton fold = null!;
        private KitButton primary = null!;
        private KitButton decline = null!;
        private KitButton reply = null!;
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<Surface> fadeTop = new List<Surface>();
        private readonly List<Surface> fadeBottom = new List<Surface>();
        private KitReading reading = null!;
        private string? risk;
        private bool unlocked;

        public Vector2 Size { get; private set; }

        public KitCarrier Carrier => carrier;

        public KitReading Reading => reading;

        /// <summary>Run's slot: the locked Read to End until the command has been read to its end, then Run.</summary>
        public KitButton Primary => primary;

        public KitButton Decline => decline;

        public KitButton Reply => reply;

        public KitButton Fold => fold;

        public TextMeshPro EndLine => end;

        /// <summary>The card's mark: a ring for the runtime's report, a disc for what the computer saw, with the question's "?".</summary>
        public Surface Mark => mark;

        public TextMeshPro MarkSymbol => symbol;

        public TextMeshPro Eyebrow => eyebrow;

        /// <summary>The row before the first shown, half-faded under the top fade; shown only where rows are hidden above.</summary>
        public TextMeshPro PeekAbove => peekAbove;

        /// <summary>The row after the last shown, half-faded under the bottom fade; shown only where rows are hidden below.</summary>
        public TextMeshPro PeekBelow => peekBelow;

        /// <summary>Where a row's words start in the well, and how far a wrapped row hangs in from there: two characters.</summary>
        public float RowLeft { get; private set; }

        public float Indent { get; private set; }

        /// <summary>The command's rows shown now, top to bottom, one label each.</summary>
        public IReadOnlyList<TextMeshPro> Shown => lines.Where(line => line.gameObject.activeSelf).ToList();

        /// <summary>How many rows the well holds at the person's text size.</summary>
        public int Fits { get; private set; }

        public bool FadedAbove => fadeTop.Count > 0 && fadeTop[0].gameObject.activeSelf;

        public bool FadedBelow => fadeBottom.Count > 0 && fadeBottom[0].gameObject.activeSelf;

        public IEnumerable<KitPress> Targets => new[] { fold, primary, decline, reply }.Where(button => button.gameObject.activeSelf).Select(button => button.Press);

        /// <summary>The player the unlock's sound plays through, where the owner gives one; silent without (A3c's rule).</summary>
        public KitSoundPlayer? Sound { get; set; }

        public static KitReader Create(Transform parent)
        {
            var go = new GameObject("Reader");
            go.transform.SetParent(parent, false);
            var reader = go.AddComponent<KitReader>();
            reader.carrier = KitCarrier.Create(go.transform, "Carrier");
            reader.mark = Surface.Create(go.transform, "Mark", KitType.Order.Plate);
            reader.symbol = KitType.Create(go.transform, "Mark symbol", KitRole.Strong, KitType.Text, KitType.Order.Words, TextAlignmentOptions.Center, room: true);
            reader.symbol.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            reader.symbol.textWrappingMode = TextWrappingModes.NoWrap;
            // Its line is taller than the 20 dp mark, as on the card: it stands over its box rather than be cut.
            reader.symbol.overflowMode = TextOverflowModes.Overflow;
            // Body, not Label: at the reader's far corner, 22 degrees out, Label reads under the 14 dp floor (0.82 degrees).
            reader.eyebrow = KitType.Create(go.transform, "Eyebrow", KitRole.Body, KitType.Secondary, KitType.Order.Words, room: true);
            reader.eyebrow.textWrappingMode = TextWrappingModes.NoWrap;
            reader.title = KitType.Create(go.transform, "Title", KitRole.Title, KitType.Text, KitType.Order.Words, room: true);
            reader.well = Surface.Create(go.transform, "Well", KitType.Order.Plate);
            reader.end = KitType.Create(go.transform, "End of Command", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            reader.fold = KitButton.Create(go.transform, KitReaderWords.Fold, KitButtonRole.Word, room: true);
            reader.primary = KitButton.Create(go.transform, KitMomentWords.Action(KitMomentAction.YesRunIt), KitButtonRole.Primary, room: true);
            reader.decline = KitButton.Create(go.transform, KitMomentWords.Action(KitMomentAction.No), KitButtonRole.Word, room: true);
            reader.reply = KitButton.Create(go.transform, KitMomentWords.Action(KitMomentAction.SaySomethingElse), KitButtonRole.Word, room: true);
            reader.peekAbove = reader.MonoLine("Command row above");
            reader.peekBelow = reader.MonoLine("Command row below");
            go.SetActive(false);
            return reader;
        }

        private static float Dp(float dp) => KitType.Units(SpatialKit.Degrees(dp));

        /// <summary>A panel's whole width or height as a length at the plane: the angle it subtends, half on each side (as the card's).</summary>
        private static float Across(float dp) => 2f * KitType.Units(SpatialKit.Degrees(dp) / 2f);

        private static float Scaled(float dp) => KitType.Scaled(SpatialKit.Degrees(dp));

        /// <summary>
        /// Shows <paramref name="moment"/>'s <paramref name="command"/> for reading, from its first line: the command as the runtime
        /// asked it, with its line breaks (the card's well shows it on one flow); each line is cleaned as theirs where it is drawn.
        /// </summary>
        public void Show(KitMoment moment, string command)
        {
            Size = new Vector2(Across(SpatialKit.ReaderWidthDp), Across(SpatialKit.ReaderHeightDp));
            while (lines.Count < MostLines) lines.Add(MonoLine("Command row " + lines.Count));
            var wellInner = WellInner();
            KitType.Size(lines[0], KitRole.Mono);
            Indent = lines[0].GetPreferredValues("MM").x;
            reading = new KitReading(command, (words, continued) => Within(words, continued ? wellInner - Indent : wellInner));
            risk = moment.Lines.FirstOrDefault();
            unlocked = false;
            var (w, h) = (Size.x, Size.y);
            carrier.DrawGlass(Size, Dp(SpatialKit.PanelRadiusDp), KitGlassMaterial.RimDp, KitMomentCard.ToneOf(moment.Tone));
            carrier.transform.localPosition = Vector3.zero;
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var left = -w / 2f + pad;

            // The head: who asks, and what, as on the card; Fold at the top right.
            var foldWidth = fold.Measure(KitReaderWords.Fold, null);
            fold.Show(KitReaderWords.Fold, null, new Vector2(w / 2f - pad - foldWidth / 2f, h / 2f - pad - KitButton.Height / 2f), foldWidth);
            // The card's mark: a ring for the runtime's report, a disc for what the computer saw, in the tone, with its sign.
            var top = h / 2f - pad;
            var markSize = Scaled(SpatialKit.MarkDp);
            var toneColour = KitMomentCard.ToneOf(moment.Tone);
            var markColour = KitType.Colour(toneColour ?? SpatialKit.TextSecondary);
            var disc = moment.Provenance == KitProvenance.Observed;
            mark.Draw(new Vector2(markSize, markSize), markSize / 2f, disc ? markColour : Color.clear, disc ? Color.clear : markColour, disc ? 0f : Scaled(2f));
            mark.transform.localPosition = new Vector3(left + markSize / 2f, top - markSize / 2f, -0.0004f);
            var sign = KitMomentCard.ToneSymbol(moment.Tone);
            symbol.gameObject.SetActive(sign != null);
            if (sign != null)
            {
                KitType.Size(symbol, KitRole.Strong);
                GlazeText.SetLiteral(symbol, sign);
                symbol.rectTransform.sizeDelta = new Vector2(markSize, markSize);
                symbol.color = disc ? KitType.Colour(SpatialKit.OnFill) : markColour;
                symbol.transform.localPosition = new Vector3(left + markSize / 2f, top - markSize / 2f, -0.0006f);
            }
            var eyebrowLeft = left + markSize + Scaled(SpatialKit.MarkGapDp);
            KitType.Size(eyebrow, KitRole.Body);
            GlazeText.SetLiteral(eyebrow, moment.Source);
            var (_, _, eyebrowHeight) = KitType.Lay(eyebrow, w / 2f - pad - foldWidth - eyebrowLeft, 1);
            eyebrow.transform.localPosition = new Vector3(eyebrowLeft, top - (markSize - eyebrowHeight) / 2f, -0.0005f);
            KitType.Size(title, KitRole.Title);
            if (moment.Title != null) GlazeRuns.Of(title).Set(moment.Title);
            KitType.Lay(title, w - 2f * pad - foldWidth, 1);
            title.transform.localPosition = new Vector3(left, top - Mathf.Max(markSize, eyebrowHeight) - Scaled(SpatialKit.TitleGapDp), -0.0005f);

            // The well, from 96 dp below the top to 84 above the bottom, inset 14.
            var wellTop = h / 2f - Dp(SpatialKit.ReaderWellTopDp);
            var wellBottom = -h / 2f + Dp(SpatialKit.ReaderWellBottomDp);
            var wellWidth = w - 2f * Dp(SpatialKit.ReaderWellInsetDp);
            well.Draw(new Vector2(wellWidth, wellTop - wellBottom), Dp(SpatialKit.InsetRadiusDp), KitType.Colour(SpatialKit.Well));
            well.transform.localPosition = new Vector3(0f, (wellTop + wellBottom) / 2f, 0.0001f);

            // The foot, fixed from the first frame: Run's slot, then Decline and Reply, 12 apart, 12 dp above the bottom.
            var footY = -h / 2f + Dp(SpatialKit.ReaderFootDp) + KitButton.Height / 2f;
            var between = KitType.Units(SpatialKit.TargetGapDegrees);
            var slot = Dp(SpatialKit.ReaderPrimaryDp);
            var x = -w / 2f + pad;
            primary.Role = KitButtonRole.Primary;
            primary.Show(KitReaderWords.ReadToEnd, null, new Vector2(x + slot / 2f, footY), slot);
            x += slot + between;
            var declineWords = KitMomentWords.Action(KitMomentAction.No);
            var declineWidth = decline.Measure(declineWords, null);
            decline.Show(declineWords, null, new Vector2(x + declineWidth / 2f, footY), declineWidth);
            x += declineWidth + between;
            var replyWords = KitMomentWords.Action(KitMomentAction.SaySomethingElse);
            var replyWidth = reply.Measure(replyWords, null);
            reply.Show(replyWords, null, new Vector2(x + replyWidth / 2f, footY), replyWidth);
            gameObject.SetActive(true);
            ScrollTo(0);
        }

        /// <summary>The well's width for the command's words, inside its padding.</summary>
        private float WellInner() => Size.x - 2f * Dp(SpatialKit.ReaderWellInsetDp) - 2f * Scaled(SpatialKit.ReaderWellPadXDp);

        /// <summary>Whether words of the command, as shown, stand within <paramref name="room"/> on one row, in the monospace.</summary>
        private bool Within(LineWords words, float room)
        {
            var label = lines[0];
            KitType.Size(label, KitRole.Mono);
            GlazeRuns.Of(label).Set(words);
            return label.GetPreferredValues(label.text).x <= room + 1e-6f;
        }

        /// <summary>Shows the command from row <paramref name="first"/>, as many rows as the well holds; the input drives this.</summary>
        public void ScrollTo(int first)
        {
            var (w, h) = (Size.x, Size.y);
            var wellTop = h / 2f - Dp(SpatialKit.ReaderWellTopDp) - Scaled(SpatialKit.ReaderWellPadYDp);
            var wellBottom = -h / 2f + Dp(SpatialKit.ReaderWellBottomDp) + Scaled(SpatialKit.ReaderWellPadYDp);
            var inner = WellInner();
            var lineLeft = -w / 2f + Dp(SpatialKit.ReaderWellInsetDp) + Scaled(SpatialKit.ReaderWellPadXDp);
            // Room kept at the well's foot for the end line, so it never stands over the command's last row.
            KitType.Size(end, KitRole.Label);
            var endLineAt = wellBottom + 2f * KitType.LinePitch(end);
            wellBottom = endLineAt + Scaled(SpatialKit.MarkGapDp);
            foreach (var line in lines) line.gameObject.SetActive(false);

            // Rows from the first asked, as many as the well holds; the last page is full (KitReading clamps the start). Where
            // rows are hidden above, the first slot holds the row before, half-faded, and is not counted as read.
            KitType.Size(lines[0], KitRole.Mono);
            var pitch = KitType.LinePitch(lines[0]);
            var whole = Mathf.Clamp(Mathf.FloorToInt((wellTop - wellBottom + 1e-5f) / pitch), 2, lines.Count);
            Fits = first > 0 ? whole - 1 : whole;
            var start = reading.Show(first, Fits);
            if (start == 0 && Fits < whole) start = reading.Show(0, Fits = whole);
            var shown = Mathf.Min(Fits, reading.Rows.Count - start);
            var rowsTop = reading.HiddenAbove ? wellTop - pitch : wellTop;
            RowLeft = lineLeft;
            for (var i = 0; i < shown; i++) LayRow(lines[i], start + i, rowsTop - i * pitch, inner);
            var y = rowsTop - shown * pitch;
            peekAbove.gameObject.SetActive(reading.HiddenAbove);
            if (reading.HiddenAbove) LayRow(peekAbove, start - 1, wellTop, inner);
            peekBelow.gameObject.SetActive(reading.HiddenBelow(shown));
            if (reading.HiddenBelow(shown)) LayRow(peekBelow, start + shown, y, inner);
            // The fades lie over those half-shown rows, deepest at the well's edge.
            Fade(fadeTop, reading.HiddenAbove, wellTop, -1f);
            Fade(fadeBottom, reading.HiddenBelow(shown), y - pitch, 1f);

            // The end, and Run in Read to End's place once the last row has been shown; Read to End fills as far as read.
            end.gameObject.SetActive(reading.ReadWhole && !reading.HiddenBelow(shown));
            if (end.gameObject.activeSelf)
            {
                KitType.Size(end, KitRole.Label);
                GlazeText.SetLiteral(end, KitReaderWords.EndOfCommand + (risk != null ? " · " + risk : ""));
                KitType.Lay(end, inner, 2);
                end.transform.localPosition = new Vector3(lineLeft, y - Scaled(SpatialKit.MarkGapDp), -0.0005f);
            }
            var slot = Dp(SpatialKit.ReaderPrimaryDp);
            var centre = new Vector2(primary.transform.localPosition.x, primary.transform.localPosition.y);
            if (reading.ReadWhole)
            {
                primary.Available = true;
                primary.Progress = 0f;
                primary.Show(KitMomentWords.Action(KitMomentAction.YesRunIt), null, centre, slot);
                if (!unlocked) Sound?.Play(KitSound.Moment, primary.transform.position);
                unlocked = true;
            }
            else
            {
                primary.Available = false;
                primary.Progress = (float)reading.Progress;
                primary.Show(KitReaderWords.ReadToEnd, null, centre, slot);
            }
        }

        /// <summary>Lays row <paramref name="row"/> of the command with its top at <paramref name="top"/>: whole on one row, hanging where it is wrapped.</summary>
        private void LayRow(TextMeshPro label, int row, float top, float inner)
        {
            var part = reading.Rows[row];
            label.gameObject.SetActive(true);
            KitType.Size(label, KitRole.Mono);
            label.color = reading.IsComment(part.Line) ? KitType.Colour(SpatialKit.TextTertiary) : KitType.Text;
            GlazeRuns.Of(label).Set(reading.RowWords(row));
            var hang = part.Continued ? Indent : 0f;
            KitType.Lay(label, inner - hang, 1);
            label.transform.localPosition = new Vector3(RowLeft + hang, top, -0.0005f);
        }

        private TextMeshPro MonoLine(string name)
        {
            var label = KitType.Create(transform, name, KitRole.Mono, KitType.Text, KitType.Order.Words, room: true);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.gameObject.SetActive(false);
            return label;
        }

        private void Fade(List<Surface> bands, bool on, float edge, float inward)
        {
            var width = Size.x - 2f * Dp(SpatialKit.ReaderWellInsetDp);
            var band = Scaled(SpatialKit.ReaderFadeDp) / FadeSteps.Length;
            while (bands.Count < FadeSteps.Length) bands.Add(Surface.Create(transform, "Fade " + bands.Count, KitType.Order.Words + 1));
            for (var step = 0; step < bands.Count; step++)
            {
                bands[step].gameObject.SetActive(on);
                if (!on) continue;
                bands[step].Draw(new Vector2(width, band), 0f, KitType.Colour(SpatialKit.Well, FadeSteps[step]));
                bands[step].transform.localPosition = new Vector3(0f, edge + inward * band * (step + 0.5f), -0.0007f);
            }
        }
    }
}
