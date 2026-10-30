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
        private TextMeshPro eyebrow = null!;
        private TextMeshPro count = null!;
        private TextMeshPro title = null!;
        private TextMeshPro prose = null!;
        private TextMeshPro fact = null!;
        private KitWell well = null!;
        private readonly List<TextMeshPro> meta = new List<TextMeshPro>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly Dictionary<KitMomentAction, KitButton> buttons = new Dictionary<KitMomentAction, KitButton>();
        private readonly List<(KitMomentAction Action, KitButton Button)> shown = new List<(KitMomentAction, KitButton)>();
        private readonly List<string> cut = new List<string>();
        private Transform body = null!;
        private float popAt = 1f;

        public Vector2 Size { get; private set; }

        public KitCarrier Carrier => carrier;

        /// <summary>The earlier moments' edge, one edge of the same glass above the card; hidden with none.</summary>
        public KitCarrier Earlier => earlier;

        /// <summary>The mark: a ring for the runtime's report, a disc for what the computer saw, in the tone where one stands.</summary>
        public Surface Mark => mark;

        /// <summary>The glyph of a question, a failure or a pass inside the mark, in its colour; hidden for every other moment.</summary>
        public TextMeshPro Tone => tone;

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

        /// <summary>How far the card has come out of the character, 0 to 1; 1 at rest.</summary>
        public float Popped => popAt;

        /// <summary>
        /// The card grows from its lower-left corner, for a character standing to its left; from its lower-right otherwise.
        /// The placement sets it; the card's words never mirror.
        /// </summary>
        public bool GrowsFromLeft { get; set; }

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
            card.eyebrow = KitType.Create(card.body, "Eyebrow", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            card.eyebrow.textWrappingMode = TextWrappingModes.NoWrap;
            card.count = KitType.Create(card.body, "Earlier and newer", KitRole.Label, KitType.Colour(SpatialKit.TextTertiary), KitType.Order.Words, room: true);
            card.count.textWrappingMode = TextWrappingModes.NoWrap;
            card.title = KitType.Create(card.body, "Title", KitRole.Title, KitType.Text, KitType.Order.Words, room: true);
            card.prose = KitType.Create(card.body, "Their words", KitRole.Body, KitType.Text, KitType.Order.Words, room: true);
            card.fact = KitType.Create(card.body, "Fact", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            card.well = KitWell.Create(card.body, "Command");
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

        private TextMeshPro MetaAt(int index, KitRole role)
        {
            while (meta.Count <= index) meta.Add(KitType.Create(body, "Meta " + meta.Count, KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true));
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

        private static GlazeIcon? ToneGlyph(KitMomentTone tone) => tone switch
        {
            KitMomentTone.Attention => GlazeIcon.WaitingForYou,
            KitMomentTone.Failure => GlazeIcon.ChecksFailed,
            KitMomentTone.Passed => GlazeIcon.FinishedThisRound,
            _ => null,
        };

        /// <summary>The one primary of a moment: the next sensible step (handoff, Answers); every other answer is its word.</summary>
        public static bool IsPrimary(KitMomentAction action) =>
            action is KitMomentAction.YesRunIt or KitMomentAction.TryIt or KitMomentAction.ReadIt;

        /// <summary>Shows <paramref name="slot"/>'s shown moment, coming out of the character where it is new.</summary>
        public void Show(KitMomentSlot slot)
        {
            foreach (var button in buttons.Values) button.Hide();
            foreach (var line in lines) line.gameObject.SetActive(false);
            foreach (var label in meta) label.gameObject.SetActive(false);
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
            var toneColour = ToneOf(moment.Tone);

            // The eyebrow: the mark, then who says it. A ring for the runtime's report, a disc for what the computer saw.
            var markSize = Dp(SpatialKit.MarkDp);
            var markColour = KitType.Colour(toneColour ?? SpatialKit.TextSecondary);
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
                tone.color = disc ? KitType.Colour(SpatialKit.OnFill) : markColour;
                tone.transform.localPosition = new Vector3(left + markSize / 2f, y - markSize / 2f, -0.0006f);
            }
            var eyebrowLeft = left + markSize + Dp(SpatialKit.MarkGapDp);
            KitType.Size(eyebrow, KitRole.Label);
            GlazeText.SetLiteral(eyebrow, moment.Source);
            // The count of the moments behind and after it stands at the eyebrow's end: on the earlier edge, 36 dp higher, Label
            // would read under the floor on a tall card (13.7 dp seen at the larger card's top), so the edge carries no words.
            var counted = string.Join(" · ", new[] { slot.Earlier, slot.Newer }.Where(words => words != null));
            var eyebrowRoom = width / 2f - pad - eyebrowLeft;
            var countBelow = false;
            if (counted.Length > 0)
            {
                KitType.Size(count, KitRole.Label);
                GlazeText.SetLiteral(count, counted);
                var beside = count.GetPreferredValues(count.text).x + Dp(SpatialKit.MarkGapDp);
                // Beside the eyebrow where both fit; on its own line under it where the source is long.
                countBelow = eyebrow.GetPreferredValues(eyebrow.text).x > eyebrowRoom - beside;
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
                y = countTop - KitType.LinePitch(count);
            }

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

            // The metadata line: segments joined by " · ", theirs in their monospace, flowing onto a second line where needed.
            if (moment.Meta.Count > 0)
            {
                y -= Dp(SpatialKit.MarkGapDp);
                var x = left;
                var lineTop = y;
                var lineHeight = 0f;
                var used = 0;
                for (var i = 0; i < moment.Meta.Count; i++)
                {
                    var segment = moment.Meta[i];
                    var parts = i == 0 ? new[] { (segment, KitRole.Label) } : new[] { (LineWords.Ours(" · "), KitRole.Label), (segment, KitRole.Label) };
                    foreach (var (words, asRole) in parts)
                    {
                        var role = words.AllTheirs ? KitRole.Mono : asRole;
                        var label = MetaAt(used++, role);
                        label.gameObject.SetActive(true);
                        GlazeRuns.Of(label).Set(words);
                        var wide = label.GetPreferredValues(label.text).x;
                        if (x + wide > width / 2f - pad + 1e-5f && x > left)
                        {
                            x = left;
                            lineTop -= lineHeight;
                            lineHeight = 0f;
                        }
                        var (laid, laidWidth, laidHeight) = KitType.Lay(label, width / 2f - pad - x, 1);
                        if (laid != 1 || label.isTextTruncated) cut.Add("meta " + i);
                        label.transform.localPosition = new Vector3(x, lineTop, -0.0005f);
                        x += laidWidth;
                        lineHeight = Mathf.Max(lineHeight, laidHeight);
                    }
                }
                y = lineTop - lineHeight;
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

            // The risk line: what answering may do, Halcyonic's words.
            for (var i = 0; i < moment.Lines.Count; i++)
            {
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

            // The answers: the one primary at full width, then the words in a row, 12 mm apart. A command shown cut can only be
            // refused (SECURITY.md): Run leaves, and Read it opens the command whole first.
            var actions = slot.Actions;
            if (moment.Kind == KitMomentKind.Question && !ReadsWhole)
                actions = new[] { KitMomentAction.ReadIt }.Concat(actions.Where(action => action != KitMomentAction.YesRunIt)).ToList();
            Actions = actions;
            if (actions.Count > 0)
            {
                y -= Dp(SpatialKit.AnswersGapDp);
                var target = KitButton.Height;
                var between = KitType.Units(SpatialKit.TargetGapDegrees);
                var primary = actions.FirstOrDefault(IsPrimary);
                var rowX = left;
                var rowTop = y;
                if (actions.Any(IsPrimary))
                {
                    var button = ButtonFor(primary);
                    button.Role = KitButtonRole.Primary;
                    button.Show(KitMomentWords.Action(primary), null, new Vector2(0f, rowTop - target / 2f), inner);
                    shown.Add((primary, button));
                    rowTop -= target + between;
                }
                var rest = actions.Where(action => !actions.Any(IsPrimary) || !action.Equals(primary)).ToList();
                foreach (var action in rest)
                {
                    var button = ButtonFor(action);
                    button.Role = KitButtonRole.Word;
                    var w = button.Measure(KitMomentWords.Action(action), null);
                    if (rowX + w > width / 2f - pad + 1e-5f && rowX > left)
                    {
                        rowX = left;
                        rowTop -= target + between;
                    }
                    button.Show(KitMomentWords.Action(action), null, new Vector2(rowX + w / 2f, rowTop - target / 2f), w);
                    shown.Add((action, button));
                    rowX += w + between;
                }
                y = rest.Count > 0 ? rowTop - target : rowTop + between;
            }
            y -= pad;

            var height = -y;
            Size = new Vector2(width, height);
            var radius = KitType.Units(SpatialKit.Degrees(SpatialKit.PanelRadiusDp));
            carrier.DrawGlass(Size, radius, KitGlassMaterial.RimDp, toneColour);
            carrier.transform.localPosition = new Vector3(0f, -height / 2f, 0f);

            // The moments behind it: one edge of the same glass, narrower, rising above the card.
            earlier.gameObject.SetActive(slot.Earlier != null);
            if (slot.Earlier != null)
            {
                var rise = Dp(SpatialKit.EarlierRiseDp);
                var edgeSize = new Vector2(width - KitType.Units(SpatialKit.Degrees(SpatialKit.EarlierNarrowerDp)), rise + 2f * radius);
                earlier.DrawGlass(edgeSize, radius, KitGlassMaterial.PlateRimDp);
                earlier.transform.localPosition = new Vector3(0f, rise - edgeSize.y / 2f, 0.0004f);
            }
            count.gameObject.SetActive(counted.Length > 0);
            if (counted.Length > 0)
            {
                var (_, countWidth, _) = KitType.Lay(count, width, 1);
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
