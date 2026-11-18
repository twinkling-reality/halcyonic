#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The moment that comes out of a task's character (ADR 0040, rule 4), drawn to the art direction's anatomy (handoff
    /// 2026-10-30; R16, R17 one light primary; R14, R15 metadata as one quiet line; R5, R7 a short title, the object shown whole
    /// before the act): one panel of quiet glass, its eyebrow (a mark, a ring for the runtime's report and a disc for what the
    /// computer saw, then who says it), a short Title, a metadata line, a command in a well or their own prose, a risk line,
    /// and its answers: the one primary as a capsule of light at full width, every other as its word. The moments behind it
    /// stand as one edge of the same glass above it, counted at the eyebrow's end. Colour only for a question, a failure or a pass, on the
    /// mark and the edge and said in its words. Centred on its transform; where it stands is the placement's.
    /// </summary>
    public sealed class KitMomentCard : MonoBehaviour
    {
        /// <summary>The Title takes at most two lines: a few words, never a sentence (the handoff's 1 to 3).</summary>
        public const int TitleLines = 2;

        private const int ProseLines = 6;
        private const int RiskLines = 3;

        private KitCarrier carrier = null!;
        private KitCarrier earlier = null!;
        private Surface mark = null!;
        private TextMeshPro tone = null!;
        private TextMeshPro symbol = null!;
        private TextMeshPro eyebrow = null!;
        private TextMeshPro count = null!;
        private TextMeshPro title = null!;
        private TextMeshPro prose = null!;
        private TextMeshPro fact = null!;
        private KitWell well = null!;
        private readonly List<TextMeshPro> meta = new List<TextMeshPro>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<TextMeshPro> riskSegments = new List<TextMeshPro>();
        private readonly List<TextMeshPro> riskDots = new List<TextMeshPro>();
        private readonly Dictionary<KitMomentAction, KitButton> buttons = new Dictionary<KitMomentAction, KitButton>();
        private readonly List<(KitMomentAction Action, KitButton Button)> shown = new List<(KitMomentAction, KitButton)>();
        private readonly List<string> cut = new List<string>();
        private Transform body = null!;
        private float popAt = 1f;
        private TextMeshPro receiptLead = null!;
        private TextMeshPro receiptThen = null!;
        private float eyebrowFoot;
        private float radius;
        private GlazeColor? lightTone;

        /// <summary>The moment's tone (<see cref="ToneOf"/>), which its arrival sounds; never drawn on the chrome, which takes no hue.</summary>
        public GlazeColor? MomentTone { get; private set; }

        public Vector2 Size { get; private set; }

        /// <summary>The glass as drawn now: <see cref="Size"/> at rest, shorter while the card shrinks to its receipt.</summary>
        public Vector2 Drawn { get; private set; }

        /// <summary>The card's height once shrunk to its receipt: its eyebrow row and the receipt line, padded.</summary>
        public float ReceiptHeight { get; private set; }

        /// <summary>The receipt: "Sent" in the primary text, then the rest secondary (the handoff's Receipt).</summary>
        public TextMeshPro ReceiptLead => receiptLead;

        public TextMeshPro ReceiptThen => receiptThen;

        /// <summary>The parts of the receipt form, in reading order: the eyebrow row, then the receipt line.</summary>
        public IReadOnlyList<IReadOnlyList<Transform>> ReceiptParts => new IReadOnlyList<Transform>[]
        {
            new[] { mark.transform, tone.transform, symbol.transform, eyebrow.transform, count.transform }.Where(part => part.gameObject.activeSelf).ToList(),
            new[] { receiptLead.transform, receiptThen.transform },
        };

        public KitCarrier Carrier => carrier;

        /// <summary>The earlier moments' edge, one edge of the same glass above the card; hidden with none.</summary>
        public KitCarrier Earlier => earlier;

        /// <summary>The mark: a ring for the runtime's report, a disc for what the computer saw, in the tone where one stands.</summary>
        public Surface Mark => mark;

        /// <summary>A pass's check inside the mark; hidden for every other moment.</summary>
        public TextMeshPro Tone => tone;

        /// <summary>A question's "?" or a failure's "!" inside the mark; hidden for every other moment.</summary>
        public TextMeshPro Symbol => symbol;

        /// <summary>Who says it, Halcyonic's words: "Codex says", "Seen on your computer".</summary>
        public TextMeshPro Eyebrow => eyebrow;

        /// <summary>"Earlier: n more", then "n newer" where a waiting question stays shown, at the eyebrow's end.</summary>
        public TextMeshPro Count => count;

        public TextMeshPro Title => title;

        /// <summary>The metadata line's labels: theirs in their monospace, ours in Label, the separators between.</summary>
        public IReadOnlyList<TextMeshPro> Meta => meta.Where(label => label.gameObject.activeSelf).ToList();

        public TextMeshPro Prose => prose;

        public TextMeshPro Fact => fact;

        public KitWell Well => well;

        public IReadOnlyList<(KitMomentAction Action, KitButton Button)> Shown => shown;

        /// <summary>The actions the card offers: the slot's, except that a question shown cut offers Read it in place of Yes.</summary>
        public IReadOnlyList<KitMomentAction> Actions { get; private set; } = new KitMomentAction[0];

        public IEnumerable<KitPress> Targets => shown.Select(each => each.Button.Press);

        public IEnumerable<KitCarrier> Carriers => earlier.gameObject.activeSelf ? new[] { carrier, earlier } : new[] { carrier };

        /// <summary>Every label the card shows, for the checks that read its words.</summary>
        public IEnumerable<TextMeshPro> Words => GetComponentsInChildren<TextMeshPro>(false).Where(label => !GlazeIcons.IsIcon(label));

        /// <summary>The parts whose words did not fit, by name: none on a card drawn whole. A command cut in its well is not one.</summary>
        public IReadOnlyList<string> CutParts => cut;

        /// <summary>The command did not fit its well and shows cut, with what is left counted.</summary>
        public bool WellCut => well.gameObject.activeSelf && well.Cut;

        /// <summary>Every word laid whole and the command, if any, whole in its well.</summary>
        public bool ReadsWhole => cut.Count == 0 && !WellCut;

        /// <summary>The card's inner body, which its arrival scales about its growing corner (<see cref="KitMomentChoreography"/>).</summary>
        public Transform Body => body;

        /// <summary>
        /// The parts shown, in reading order, each the objects that rise and fade in together as the card arrives: the eyebrow
        /// row, the Title, the metadata, their prose, the fact, the well, the risk lines, the answers.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Transform>> Parts
        {
            get
            {
                var parts = new List<IReadOnlyList<Transform>>
                {
                    new[] { mark.transform, tone.transform, symbol.transform, eyebrow.transform, count.transform },
                    new[] { title.transform },
                    meta.Select(label => label.transform).ToArray(),
                    new[] { prose.transform },
                    new[] { fact.transform },
                    new[] { well.transform },
                    lines.Select(line => line.transform).Concat(riskSegments.Select(segment => segment.transform)).Concat(riskDots.Select(dot => dot.transform)).ToArray(),
                    shown.Select(each => each.Button.transform).ToArray(),
                };
                return parts.Select(group => (IReadOnlyList<Transform>)group.Where(part => part.gameObject.activeSelf).ToList()).Where(group => group.Count > 0).ToList();
            }
        }

        /// <summary>How far the card has come out of the character, 0 to 1; 1 at rest.</summary>
        public float Popped => popAt;

        /// <summary>The card grows from its lower-left corner, for a character standing to its left; from its lower-right otherwise.</summary>
        public bool GrowsFromLeft { get; set; }

        /// <summary>
        /// A wash colour on the card's glass: none, the kit's own dark glass, since chrome takes no hue (the lead's 2026-11-17T19:15Z; AUDIT's
        /// eye on 26-receipt, 2026-11-18: the avatar's blue wash was a hue).
        /// </summary>
        public GlazeColor? Tint { get; set; }

        public static KitMomentCard Create(Transform parent)
        {
            var go = new GameObject("Moment");
            go.transform.SetParent(parent, false);
            var card = go.AddComponent<KitMomentCard>();
            card.body = new GameObject("Card").transform;
            card.body.SetParent(go.transform, false);
            card.earlier = KitCarrier.Create(card.body, "Earlier moments", KitType.Order.Carrier - 2);
            card.carrier = KitCarrier.Create(card.body, "Carrier");
            card.mark = Surface.Create(card.body, "Mark", KitType.Order.Plate);
            card.tone = GlazeIcons.Create(card.body, "Tone", SpatialKit.IconDegrees, KitType.Text, KitType.Order.Words);
            card.symbol = KitType.Create(card.body, "Mark symbol", KitRole.Strong, KitType.Text, KitType.Order.Words, TextAlignmentOptions.Center, room: true);
            card.symbol.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            card.symbol.textWrappingMode = TextWrappingModes.NoWrap;
            // Its line is taller than the 20 dp mark: let it stand over its box rather than be cut to an ellipsis.
            card.symbol.overflowMode = TextOverflowModes.Overflow;
            card.eyebrow = KitType.Create(card.body, "Eyebrow", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            card.eyebrow.textWrappingMode = TextWrappingModes.NoWrap;
            card.count = KitType.Create(card.body, "Earlier and newer", KitRole.Label, KitType.Colour(SpatialKit.TextTertiary), KitType.Order.Words, room: true);
            card.count.textWrappingMode = TextWrappingModes.NoWrap;
            card.title = KitType.Create(card.body, "Title", KitRole.Title, KitType.Text, KitType.Order.Words, room: true);
            card.prose = KitType.Create(card.body, "Their words", KitRole.Body, KitType.Text, KitType.Order.Words, room: true);
            card.fact = KitType.Create(card.body, "Fact", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            card.well = KitWell.Create(card.body, "Command");
            card.receiptLead = KitType.Create(card.body, "Receipt", KitRole.Body, KitType.Text, KitType.Order.Words, room: true);
            card.receiptThen = KitType.Create(card.body, "Receipt state", KitRole.Body, KitType.Secondary, KitType.Order.Words, room: true);
            foreach (var label in new[] { card.receiptLead, card.receiptThen })
            {
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.gameObject.SetActive(false);
            }
            return card;
        }

        private KitButton ButtonFor(KitMomentAction action)
        {
            if (!buttons.TryGetValue(action, out var button))
                buttons[action] = button = KitButton.Create(body, KitMomentWords.Action(action), KitButtonRole.Word, room: true);
            return button;
        }

        private TextMeshPro LineAt(int index)
        {
            while (lines.Count <= index)
                lines.Add(KitType.Create(body, "Risk " + lines.Count, KitRole.Body, KitType.Secondary, KitType.Order.Words, room: true));
            return lines[index];
        }

        /// <summary>
        /// A risk line's segment or the dot between two, from <paramref name="pool"/>: one line in Body, secondary; made hidden,
        /// so one the flow does not show never stands empty on the card.
        /// </summary>
        private TextMeshPro RiskAt(List<TextMeshPro> pool, int index, string name)
        {
            while (pool.Count <= index)
            {
                var made = KitType.Create(body, name + " " + pool.Count, KitRole.Body, KitType.Secondary, KitType.Order.Words, room: true);
                made.gameObject.SetActive(false);
                pool.Add(made);
            }
            var label = pool[index];
            label.textWrappingMode = TextWrappingModes.NoWrap;
            KitType.Size(label, KitRole.Body);
            return label;
        }

        private TextMeshPro MetaAt(int index, KitRole role)
        {
            while (meta.Count <= index)
            {
                var made = KitType.Create(body, "Meta " + meta.Count, KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
                made.gameObject.SetActive(false);
                meta.Add(made);
            }
            var label = meta[index];
            label.textWrappingMode = TextWrappingModes.NoWrap;
            KitType.Size(label, role);
            if (KitType.Face(role) is TMP_FontAsset face && label.font != face)
            {
                label.font = face;
                label.fontSharedMaterial = face.material;
                KitType.Size(label, role);
            }
            label.color = role == KitRole.Mono ? KitType.Text : KitType.Secondary;
            return label;
        }

        /// <summary>A moment's tone as the colour that means it, or none.</summary>
        public static GlazeColor? ToneOf(KitMomentTone tone) => tone switch
        {
            KitMomentTone.Attention => SpatialKit.Attention,
            KitMomentTone.Failure => SpatialKit.Failure,
            KitMomentTone.Passed => SpatialKit.Passed,
            _ => (GlazeColor?)null,
        };

        /// <summary>A pass's mark: a check, drawn as an icon (it draws itself as the card settles).</summary>
        private static GlazeIcon? ToneGlyph(KitMomentTone tone) => tone == KitMomentTone.Passed ? GlazeIcon.Approve : (GlazeIcon?)null;

        /// <summary>A question's and a failure's mark: the bare symbol in the face's own type, never an icon that reads as another act.</summary>
        internal static string? ToneSymbol(KitMomentTone tone) => tone switch
        {
            KitMomentTone.Attention => "?",
            KitMomentTone.Failure => "!",
            _ => null,
        };

        /// <summary>A sign's colour inside its ring: its tone lifted in its own hue, so it holds 4.5:1 on the face's lit top.</summary>
        internal static GlazeColor? SignOf(KitMomentTone tone) => tone switch
        {
            KitMomentTone.Attention => SpatialKit.AttentionSign,
            KitMomentTone.Failure => SpatialKit.FailureSign,
            _ => (GlazeColor?)null,
        };

        /// <summary>
        /// Whether <paramref name="action"/> is the one primary on a moment of <paramref name="tone"/>: the next sensible step
        /// (the handoff's answers): Run, Read Command or Try Build; Read Output only after a failure (ART's A3b review), so a
        /// passed check offers it as its word.
        /// </summary>
        public static bool IsPrimary(KitMomentAction action, KitMomentTone tone) =>
            action is KitMomentAction.YesRunIt or KitMomentAction.TryIt or KitMomentAction.ReadIt
            || (action == KitMomentAction.SeeTheOutput && tone == KitMomentTone.Failure);

        /// <summary>
        /// The glossy capsule hue of <paramref name="action"/> on a moment of <paramref name="tone"/>: sapphire blue for the one
        /// primary (<see cref="IsPrimary"/>), and cool dark-slate neutral glass for every secondary capsule.
        /// </summary>
        public static KitPillMeaning MeaningOf(KitMomentAction action, KitMomentTone tone) =>
            IsPrimary(action, tone) ? KitPillMeaning.Blue : KitPillMeaning.Neutral;

        /// <summary>Shows <paramref name="slot"/>'s shown moment, coming out of the character where it is new.</summary>
        public void Show(KitMomentSlot slot)
        {
            foreach (var button in buttons.Values) button.Hide();
            foreach (var line in lines) line.gameObject.SetActive(false);
            foreach (var segment in riskSegments) segment.gameObject.SetActive(false);
            foreach (var dot in riskDots) dot.gameObject.SetActive(false);
            foreach (var label in meta) label.gameObject.SetActive(false);
            receiptLead.gameObject.SetActive(false);
            receiptThen.gameObject.SetActive(false);
            shown.Clear();
            cut.Clear();
            if (slot.Shown is not int index)
            {
                gameObject.SetActive(false);
                return;
            }
            var moment = slot.Moments[index];
            float Dp(float dp) => KitType.Scaled(SpatialKit.Degrees(dp));
            var width = 2f * KitType.Units(SpatialKit.MomentDegrees / 2f);
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var inner = width - 2f * pad;
            var left = -width / 2f + pad;
            var y = -pad;
            // No hue in the chrome (the lead's 19:15Z; AUDIT's 26-receipt eye, 2026-11-18): the glass is the kit's dark glass, never lit in the
            // moment's tone; the tone is said by its mark's sign and its words.
            lightTone = null;
            MomentTone = ToneOf(moment.Tone);

            // The eyebrow: the mark, then who says it. A ring for the runtime's report, a disc for what the computer saw.
            var markSize = Dp(SpatialKit.MarkDp);
            // The mark is white whatever the tone (the lead's 2026-11-18 MUST): a white ring, or a white disc with its sign in the well's dark.
            var markColour = KitType.Colour(SpatialKit.Text);
            var disc = moment.Provenance == KitProvenance.Observed;
            mark.Draw(new Vector2(markSize, markSize), markSize / 2f, disc ? markColour : Color.clear, disc ? Color.clear : markColour, disc ? 0f : Dp(2f));
            mark.transform.localPosition = new Vector3(left + markSize / 2f, y - markSize / 2f, -0.0004f);
            var glyph = ToneGlyph(moment.Tone);
            tone.gameObject.SetActive(glyph.HasValue);
            if (glyph is GlazeIcon shownGlyph)
            {
                var iconEm = markSize * 0.7f;
                tone.fontSize = GlazeTokens.FontSize(iconEm);
                tone.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(tone, shownGlyph);
                tone.color = disc ? KitType.Colour(SpatialKit.Well) : markColour;
                tone.transform.localPosition = new Vector3(left + markSize / 2f, y - markSize / 2f, -0.0006f);
            }
            var shownSymbol = ToneSymbol(moment.Tone);
            symbol.gameObject.SetActive(shownSymbol != null);
            if (shownSymbol != null)
            {
                KitType.Size(symbol, KitRole.Strong);
                GlazeText.SetLiteral(symbol, shownSymbol);
                symbol.rectTransform.sizeDelta = new Vector2(markSize, markSize);
                symbol.ForceMeshUpdate(true);
                if (symbol.isTextTruncated) cut.Add("mark sign");
                symbol.color = disc ? KitType.Colour(SpatialKit.Well) : markColour;
                symbol.transform.localPosition = new Vector3(left + markSize / 2f, y - markSize / 2f, -0.0006f);
            }
            var eyebrowLeft = left + markSize + Dp(SpatialKit.MarkGapDp);
            KitType.Size(eyebrow, KitRole.Label);
            GlazeText.SetLiteral(eyebrow, moment.Source);
            // The count of the moments behind and after it stands at the eyebrow's end, in the cadence; the stacked edge carries no words.
            // Each count a line of its own, never joined on a dot (the owner's ban; AUDIT's MUST, 09:34Z): both stand under the eyebrow.
            var countParts = new[] { slot.Earlier, slot.Newer }.Where(words => words != null).ToList();
            var counted = string.Join("\n", countParts);
            var eyebrowRoom = width / 2f - pad - eyebrowLeft;
            var countBelow = false;
            if (counted.Length > 0)
            {
                KitType.Size(count, KitRole.Label);
                // Each part made literal on its own and joined after: made literal together, the line break folds into a space.
                GlazeText.SetLiteral(count, countParts[0]!);
                for (var part = 1; part < countParts.Count; part++) count.text += "\n" + LabelText.ForTextMeshPro(countParts[part]!);
                var beside = count.GetPreferredValues(count.text).x + Dp(SpatialKit.MarkGapDp);
                // Beside the eyebrow where both fit; on its own line under it where the source is long.
                countBelow = countParts.Count > 1 || eyebrow.GetPreferredValues(eyebrow.text).x > eyebrowRoom - beside;
                if (!countBelow) eyebrowRoom -= beside;
            }
            var (_, _, eyebrowHeight) = KitType.Lay(eyebrow, eyebrowRoom, 1);
            if (eyebrow.isTextTruncated) cut.Add("eyebrow");
            eyebrow.transform.localPosition = new Vector3(eyebrowLeft, y - (markSize - eyebrowHeight) / 2f, -0.0005f);
            y -= Mathf.Max(markSize, eyebrowHeight);
            var countTop = eyebrow.transform.localPosition.y;
            if (countBelow)
            {
                countTop = y - Dp(SpatialKit.MarkGapDp) / 2f;
                y = countTop - KitType.LinePitch(count) * countParts.Count;
            }
            eyebrowFoot = y;

            // The Title: a few words of ours, a name of theirs in its run where the words carry one.
            title.gameObject.SetActive(moment.Title != null);
            if (moment.Title != null)
            {
                y -= Dp(SpatialKit.TitleGapDp);
                KitType.Size(title, KitRole.Title);
                GlazeRuns.Of(title).Set(moment.Title);
                var (_, _, titleHeight) = KitType.Lay(title, inner, TitleLines);
                if (title.isTextTruncated) cut.Add("title");
                title.transform.localPosition = new Vector3(left, y, -0.0005f);
                y -= titleHeight;
            }

            // The metadata: each segment on a line of its own, theirs in their monospace, never joined on a dot (the owner's ban,
            // 2026-11-11; AUDIT's MUST, 09:34Z). Segment i is meta[2i]; the label once kept for the dot before it, meta[2i-1], stays hidden.
            if (moment.Meta.Count > 0)
            {
                y -= Dp(SpatialKit.MarkGapDp);
                for (var i = 0; i < moment.Meta.Count; i++)
                {
                    if (i > 0) MetaAt(2 * i - 1, KitRole.Label).gameObject.SetActive(false);
                    var segment = MetaAt(2 * i, moment.Meta[i].AllTheirs ? KitRole.Mono : KitRole.Label);
                    segment.gameObject.SetActive(true);
                    GlazeRuns.Of(segment).Set(moment.Meta[i]);
                    var (_, _, segmentHeight) = KitType.Lay(segment, width - 2f * pad, 2);
                    if (segment.isTextTruncated) cut.Add("meta " + i);
                    segment.transform.localPosition = new Vector3(left, y, -0.0005f);
                    y -= segmentHeight;
                }
            }

            // Their own words, as written: a runtime's prose.
            prose.gameObject.SetActive(moment.Prose != null);
            if (moment.Prose != null)
            {
                y -= Dp(SpatialKit.TitleGapDp);
                KitType.Size(prose, KitRole.Body);
                GlazeRuns.Of(prose).Set(moment.Prose);
                var (_, _, proseHeight) = KitType.Lay(prose, inner, ProseLines);
                if (prose.isTextTruncated) cut.Add("prose");
                prose.transform.localPosition = new Vector3(left, y, -0.0005f);
                y -= proseHeight;
            }

            fact.gameObject.SetActive(moment.Fact != null);
            if (moment.Fact != null)
            {
                y -= Dp(SpatialKit.MarkGapDp);
                KitType.Size(fact, KitRole.Label);
                GlazeText.SetLiteral(fact, moment.Fact);
                var (_, _, factHeight) = KitType.Lay(fact, inner, 2);
                if (fact.isTextTruncated) cut.Add("fact");
                fact.transform.localPosition = new Vector3(left, y, -0.0005f);
                y -= factHeight;
            }

            // Their command in its well: whole, or cut with what is left counted.
            if (moment.Well != null)
            {
                y -= Dp(SpatialKit.WellGapDp);
                var wellSize = well.Show(moment.Well, inner);
                if (!well.RestWhole) cut.Add("well's cut line");
                well.transform.localPosition = new Vector3(left, y, 0f);
                y -= wellSize.y;
            }
            else well.Hide();

            // The risk line: what answering may do, Halcyonic's words. A line given as " · " segments stands as its segments, each on a
            // line of its own, never joined on the dot (the owner's ban, 2026-11-11; AUDIT's MUST, 09:34Z), so a phrase never splits.
            var segmentsUsed = 0;
            var dotsUsed = 0;
            for (var i = 0; i < moment.Lines.Count; i++)
            {
                if (moment.Lines[i].Contains(KitFlow.Separator))
                {
                    y -= Dp(i == 0 ? SpatialKit.RiskGapDp : SpatialKit.MarkGapDp);
                    var first = segmentsUsed;
                    var firstDot = dotsUsed;
                    var segments = KitFlow.Segments(LineWords.Ours(moment.Lines[i]));
                    for (var p = 0; p < segments.Count; p++)
                    {
                        if (p > 0) RiskAt(riskDots, firstDot + p - 1, "Risk dot").gameObject.SetActive(false);
                        var segment = RiskAt(riskSegments, first + p, "Risk segment");
                        segment.gameObject.SetActive(true);
                        GlazeRuns.Of(segment).Set(segments[p]);
                        var (_, _, segmentHeight) = KitType.Lay(segment, inner, RiskLines);
                        if (segment.isTextTruncated) cut.Add("risk " + i + " segment " + p);
                        segment.transform.localPosition = new Vector3(left, y, -0.0005f);
                        y -= segmentHeight;
                    }
                    segmentsUsed += segments.Count;
                    dotsUsed += Mathf.Max(0, segments.Count - 1);
                    continue;
                }
                var line = LineAt(i);
                line.gameObject.SetActive(true);
                KitType.Size(line, KitRole.Body);
                GlazeText.SetLiteral(line, moment.Lines[i]);
                var (_, _, lineHeight) = KitType.Lay(line, inner, RiskLines);
                if (line.isTextTruncated) cut.Add("risk " + i);
                y -= Dp(i == 0 ? SpatialKit.RiskGapDp : SpatialKit.MarkGapDp);
                line.transform.localPosition = new Vector3(left, y, -0.0005f);
                y -= lineHeight;
            }

            // The answers: the one primary at full width, then the secondary capsules sharing the row's full inner width, 12 mm apart.
            // A command shown cut can only be refused (SECURITY.md): Run leaves, and Read it opens the command whole first.
            var actions = slot.Actions;
            if (moment.Kind == KitMomentKind.Question && !ReadsWhole)
                actions = new[] { KitMomentAction.ReadIt }.Concat(actions.Where(action => action != KitMomentAction.YesRunIt)).ToList();
            Actions = actions;
            if (actions.Count > 0)
            {
                y -= Dp(SpatialKit.AnswersGapDp);
                var target = KitButton.Height;
                var between = KitType.Units(SpatialKit.TargetGapDegrees);
                bool Leads(KitMomentAction action) => IsPrimary(action, moment.Tone);
                var primary = actions.FirstOrDefault(Leads);
                var rowTop = y;
                if (actions.Any(Leads))
                {
                    var button = ButtonFor(primary);
                    button.Role = KitButtonRole.Primary;
                    button.Meaning = MeaningOf(primary, moment.Tone);
                    button.Show(KitMomentWords.Action(primary, moment.Kind), null, new Vector2(0f, rowTop - target / 2f), inner);
                    shown.Add((primary, button));
                    rowTop -= target + between;
                }
                var rest = actions.Where(action => !actions.Any(Leads) || !action.Equals(primary)).ToList();
                var restRows = new List<List<(KitMomentAction Action, KitButton Button, float Measured)>>();
                foreach (var action in rest)
                {
                    var button = ButtonFor(action);
                    button.Role = KitButtonRole.Word;
                    button.Meaning = MeaningOf(action, moment.Tone);
                    var w = button.Measure(KitMomentWords.Action(action, moment.Kind), null);
                    if (restRows.Count == 0 || restRows[^1].Sum(b => b.Measured) + restRows[^1].Count * between + w > inner + 1e-5f)
                        restRows.Add(new List<(KitMomentAction, KitButton, float)>());
                    restRows[^1].Add((action, button, w));
                }
                foreach (var rowButtons in restRows)
                {
                    var n = rowButtons.Count;
                    var available = inner - (n - 1) * between;
                    var cellWidth = available / n;
                    var allFitEqual = rowButtons.All(b => b.Measured <= cellWidth + 1e-5f);
                    var extraEach = (available - rowButtons.Sum(b => b.Measured)) / n;
                    var rowX = left;
                    foreach (var (action, button, measured) in rowButtons)
                    {
                        var w = allFitEqual ? cellWidth : measured + Mathf.Max(0f, extraEach);
                        button.Show(KitMomentWords.Action(action, moment.Kind), null, new Vector2(rowX + w / 2f, rowTop - target / 2f), w);
                        shown.Add((action, button));
                        rowX += w + between;
                    }
                    rowTop -= target + between;
                }
                y = rowTop + between;
            }
            y -= pad;

            var height = -y;
            Size = new Vector2(width, height);
            Drawn = Size;
            radius = KitType.Units(SpatialKit.Degrees(SpatialKit.PanelRadiusDp));
            carrier.DrawGlass(Size, radius, KitGlassMaterial.RimDp, null, tint: Tint);
            carrier.transform.localPosition = new Vector3(0f, -height / 2f, 0f);

            // The moments behind it: one edge of the same glass, narrower, rising above the card.
            earlier.gameObject.SetActive(slot.Earlier != null);
            if (slot.Earlier != null)
            {
                var rise = Dp(SpatialKit.EarlierRiseDp);
                var edgeSize = new Vector2(width - KitType.Units(SpatialKit.Degrees(SpatialKit.EarlierNarrowerDp)), rise + 2f * radius);
                earlier.DrawGlass(edgeSize, radius, KitGlassMaterial.PlateRimDp, tint: Tint);
                earlier.transform.localPosition = new Vector3(0f, rise - edgeSize.y / 2f, 0.0004f);
            }
            count.gameObject.SetActive(counted.Length > 0);
            if (counted.Length > 0)
            {
                var (_, countWidth, _) = KitType.Lay(count, width, countParts.Count);
                if (count.isTextTruncated) cut.Add("count");
                count.alignment = TextAlignmentOptions.TopLeft;
                count.transform.localPosition = new Vector3(countBelow ? eyebrowLeft : width / 2f - pad - countWidth, countTop, -0.0005f);
            }
            // Everything above was laid from the card's top; centre it on the transform.
            body.localPosition = new Vector3(0f, height / 2f, 0f);
            gameObject.SetActive(true);
            Pop(GlazeMotion.Still ? 1f : 0f);
        }

        /// <summary>
        /// Lays the receipt an answer leaves (the handoff's Receipt) under the eyebrow, in Body: "Sent" in the primary text and under it,
        /// on its own line, "Waiting for the agent to start." or "Declined", secondary, never after a joining dot; never "Started" (T1).
        /// Its opacity is the choreography's.
        /// </summary>
        public void ShowReceipt(bool ran)
        {
            float Dp(float dp) => KitType.Scaled(SpatialKit.Degrees(dp));
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var left = -Size.x / 2f + pad;
            var (lead, then) = KitMomentWords.Receipt(ran);
            var top = eyebrowFoot - Dp(SpatialKit.TitleGapDp);
            KitType.Size(receiptLead, KitRole.Body);
            GlazeText.SetLiteral(receiptLead, lead);
            var (_, _, tall) = KitType.Lay(receiptLead, Size.x - 2f * pad, 1);
            receiptLead.transform.localPosition = new Vector3(left, top, -0.0005f);
            // The rest on its own line under "Sent", never after a joining dot (the owner, 2026-11-11; AUDIT's MUST, 08:53Z).
            KitType.Size(receiptThen, KitRole.Body);
            GlazeRuns.Of(receiptThen).Set(LineWords.Ours(then));
            var (_, _, thenTall) = KitType.Lay(receiptThen, Size.x - 2f * pad, 2);
            receiptThen.transform.localPosition = new Vector3(left, top - tall, -0.0005f);
            receiptLead.gameObject.SetActive(true);
            receiptThen.gameObject.SetActive(true);
            ReceiptHeight = -(top - tall - thenTall - pad);
        }

        /// <summary>Draws the glass <paramref name="height"/> tall from the card's top, lit in the moment's tone or not.</summary>
        public void DrawHeight(float height, bool lit)
        {
            Drawn = new Vector2(Size.x, height);
            carrier.DrawGlass(Drawn, radius, KitGlassMaterial.RimDp, lit ? lightTone : null, tint: Tint);
            carrier.transform.localPosition = new Vector3(0f, -height / 2f, 0f);
        }

        /// <summary>
        /// Sets how far the card has come out of the character, from 0 to 1, with the spring's small overshoot
        /// (<see cref="SpatialKit.PopBounce"/>); it grows from its lower corner on the character's side
        /// (<see cref="GrowsFromLeft"/>). Only its inner body scales: a scale the placement sets on the card stays.
        /// </summary>
        public void Pop(float progress)
        {
            popAt = Mathf.Clamp01(progress);
            var c1 = 1.70158f * SpatialKit.PopBounce / 0.15f;
            var c3 = c1 + 1f;
            var t = popAt - 1f;
            var eased = popAt >= 1f ? 1f : 1f + c3 * t * t * t + c1 * t * t;
            var s = Mathf.LerpUnclamped(0.2f, 1f, eased);
            // The body hangs from its top centre; keep the growing corner where it stands at rest.
            var side = GrowsFromLeft ? -1f : 1f;
            body.localScale = new Vector3(s, s, 1f);
            body.localPosition = new Vector3(side * Size.x / 2f * (1f - s), -Size.y / 2f + s * Size.y, 0f);
        }

        /// <summary>Advances the pop by <paramref name="seconds"/>; at once while things are kept still.</summary>
        public void Advance(float seconds) => Pop(GlazeMotion.Still ? 1f : popAt + seconds / SpatialKit.PopSeconds);

        private void Update()
        {
            if (popAt < 1f) Advance(Time.deltaTime);
        }
    }
}
