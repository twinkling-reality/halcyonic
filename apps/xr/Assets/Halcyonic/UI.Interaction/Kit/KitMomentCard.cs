#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The moment that comes out of a task's character (ADR 0040, rule 4; R08 Cluely's stable overlay, R06 Destiny's Ghost,
    /// R13 a bounded "Earlier: n more"): one opaque card in one place, its source said first (the runtime's report carries the
    /// quote mark, what the computer saw says so in words), its subject, its facts, its actions beneath, and the moments behind
    /// it as one edge and a count. Colour only for a question, a failure or a pass, always with its glyph and its words. It
    /// comes out of the character's side with a small overshoot; while things are kept still it lands at once. Centred on
    /// its transform; where it stands is the placement's, never the card's.
    /// </summary>
    public sealed class KitMomentCard : MonoBehaviour
    {
        private const float GapDegrees = 0.6f;
        // A stopgap (the lead, 2026-10-30): the subject in Title 28 at the larger size can need a fourth line until A3 rebuilds the
        // card to the handoff (a short Title, counts and time in the Label line) and returns this to the handoff's count.
        private const int HeadlineLines = 4;

        private KitCarrier carrier = null!;
        private Surface stack = null!;
        private KitChip chip = null!;
        private TextMeshPro tone = null!;
        private TextMeshPro count = null!;
        private TextMeshPro headline = null!;
        private TextMeshPro fact = null!;
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly Dictionary<KitMomentAction, KitButton> buttons = new Dictionary<KitMomentAction, KitButton>();
        private readonly List<(KitMomentAction Action, KitButton Button)> shown = new List<(KitMomentAction, KitButton)>();
        private Transform body = null!;
        private float popAt = 1f;

        public Vector2 Size { get; private set; }

        public KitCarrier Carrier => carrier;

        public KitChip Source => chip;

        /// <summary>The glyph of a question, a failure or a pass, in its colour; hidden for every other moment.</summary>
        public TextMeshPro Tone => tone;

        /// <summary>"Earlier: n more", then "n newer" where a waiting question stays shown.</summary>
        public TextMeshPro Count => count;

        public TextMeshPro Headline => headline;

        public TextMeshPro Fact => fact;

        public Surface Stack => stack;

        public IReadOnlyList<(KitMomentAction Action, KitButton Button)> Shown => shown;

        /// <summary>The actions the card offers: the slot's, except that a question shown cut offers Read it in place of Yes.</summary>
        public IReadOnlyList<KitMomentAction> Actions { get; private set; } = new KitMomentAction[0];

        public IEnumerable<KitPress> Targets => shown.Select(each => each.Button.Press);

        public IEnumerable<KitCarrier> Carriers => new[] { carrier };

        /// <summary>Every word laid whole: the subject in its lines, the facts and lines uncut.</summary>
        public bool ReadsWhole { get; private set; }

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
            card.stack = Surface.Create(card.body, "Earlier moments", KitType.Order.Carrier);
            card.carrier = KitCarrier.Create(card.body, "Carrier");
            card.chip = KitChip.Create(card.body, "Source chip", room: true);
            card.tone = GlazeIcons.Create(card.body, "Tone", SpatialKit.IconDegrees, KitType.Text, KitType.Order.Words);
            card.count = KitType.Create(card.body, "Earlier and newer", KitRole.Label, KitType.Colour(SpatialKit.TextTertiary), KitType.Order.Words, TextAlignmentOptions.Right, room: true);
            card.count.rectTransform.pivot = new Vector2(1f, 0.5f);
            card.count.textWrappingMode = TextWrappingModes.NoWrap;
            card.headline = KitType.Create(card.body, "Subject", KitRole.Title, KitType.Text, KitType.Order.Words, room: true);
            card.fact = KitType.Create(card.body, "Fact", KitRole.Label, KitType.Secondary, KitType.Order.Words, room: true);
            return card;
        }

        private KitButton ButtonFor(KitMomentAction action)
        {
            if (!buttons.TryGetValue(action, out var button))
                buttons[action] = button = KitButton.Create(body, KitMomentWords.Action(action), KitButtonRole.Secondary, room: true);
            return button;
        }

        private TextMeshPro LineAt(int index)
        {
            while (lines.Count <= index)
                lines.Add(KitType.Create(body, "Line " + lines.Count, KitRole.Body, KitType.Secondary, KitType.Order.Words, room: true));
            return lines[index];
        }

        private static Color ToneColour(KitMomentTone tone) => KitType.Colour(tone switch
        {
            KitMomentTone.Attention => SpatialKit.Attention,
            KitMomentTone.Failure => SpatialKit.Failure,
            KitMomentTone.Passed => SpatialKit.Passed,
            _ => SpatialKit.TextSecondary,
        });

        private static GlazeIcon? ToneGlyph(KitMomentTone tone) => tone switch
        {
            KitMomentTone.Attention => GlazeIcon.WaitingForYou,
            KitMomentTone.Failure => GlazeIcon.ChecksFailed,
            KitMomentTone.Passed => GlazeIcon.FinishedThisRound,
            _ => null,
        };

        private static KitButtonRole RoleOf(KitMomentAction action) => action switch
        {
            KitMomentAction.YesRunIt or KitMomentAction.TryIt => KitButtonRole.Primary,
            KitMomentAction.ReadIt => KitButtonRole.Primary,
            KitMomentAction.Stop or KitMomentAction.SaySomethingElse => KitButtonRole.Quiet,
            _ => KitButtonRole.Secondary,
        };

        /// <summary>Shows <paramref name="slot"/>'s shown moment, coming out of the character where it is new.</summary>
        public void Show(KitMomentSlot slot)
        {
            foreach (var button in buttons.Values) button.Hide();
            foreach (var line in lines) line.gameObject.SetActive(false);
            shown.Clear();
            if (slot.Shown is not int index)
            {
                gameObject.SetActive(false);
                return;
            }
            var moment = slot.Moments[index];
            var width = 2f * KitType.Units(SpatialKit.MomentDegrees / 2f);
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var gap = KitType.Scaled(GapDegrees);
            var inner = width - 2f * pad;
            var y = -pad;
            var whole = true;

            // The head: the tone's glyph where it means something, the source, and the count of what stands behind.
            var glyph = ToneGlyph(moment.Tone);
            tone.gameObject.SetActive(glyph.HasValue);
            var x = -width / 2f + pad;
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            if (glyph is GlazeIcon shownGlyph)
            {
                tone.fontSize = GlazeTokens.FontSize(iconEm);
                tone.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(tone, shownGlyph);
                tone.color = ToneColour(moment.Tone);
                x += iconEm + gap;
            }
            var chipSize = chip.Show(LineWords.Ours(moment.Source), moment.Provenance == KitProvenance.Reported ? GlazeIcon.Quote : (GlazeIcon?)null,
                KitType.Secondary, inner * 0.62f, 1);
            chip.transform.localPosition = new Vector3(x + chipSize.x / 2f, y - chipSize.y / 2f, -0.0005f);
            if (glyph.HasValue) tone.transform.localPosition = new Vector3(-width / 2f + pad + iconEm / 2f, y - chipSize.y / 2f, -0.0006f);
            var counted = string.Join(" · ", new[] { slot.Earlier, slot.Newer }.Where(words => words != null));
            count.gameObject.SetActive(counted.Length > 0);
            var countBelow = false;
            if (counted.Length > 0)
            {
                KitType.Size(count, KitRole.Label);
                GlazeText.SetLiteral(count, counted);
                // Beside the source where it fits; on its own line under it where the source is long.
                var room = (width / 2f - pad) - (x + chipSize.x + gap);
                countBelow = count.GetPreferredValues(count.text).x > room;
                var (countLines, _, _) = KitType.Lay(count, countBelow ? inner : room, 1);
                whole &= countLines == 1 && !count.isTextTruncated;
                count.transform.localPosition = new Vector3(width / 2f - pad, y - chipSize.y / 2f - (countBelow ? chipSize.y : 0f), -0.0005f);
            }
            y -= chipSize.y * (countBelow ? 2f : 1f) + gap;

            // The subject, outside text in its run where it is theirs.
            KitType.Size(headline, KitRole.Title);
            GlazeRuns.Of(headline).Set(moment.Headline);
            var (_, _, headHeight) = KitType.Lay(headline, inner, HeadlineLines);
            whole &= !headline.isTextTruncated;
            headline.transform.localPosition = new Vector3(-width / 2f + pad, y, -0.0005f);
            y -= headHeight;

            // Its fact and lines, Halcyonic's words.
            fact.gameObject.SetActive(moment.Fact != null);
            if (moment.Fact != null)
            {
                KitType.Size(fact, KitRole.Label);
                GlazeText.SetLiteral(fact, moment.Fact);
                var (_, _, factHeight) = KitType.Lay(fact, inner, 2);
                whole &= !fact.isTextTruncated;
                y -= gap * 0.5f;
                fact.transform.localPosition = new Vector3(-width / 2f + pad, y, -0.0005f);
                y -= factHeight;
            }
            for (var i = 0; i < moment.Lines.Count; i++)
            {
                var line = LineAt(i);
                line.gameObject.SetActive(true);
                KitType.Size(line, KitRole.Body);
                GlazeText.SetLiteral(line, moment.Lines[i]);
                var (_, _, lineHeight) = KitType.Lay(line, inner, 3);
                whole &= !line.isTextTruncated;
                y -= gap * 0.5f;
                line.transform.localPosition = new Vector3(-width / 2f + pad, y, -0.0005f);
                y -= lineHeight;
            }

            // The actions beneath, in their order, wrapping to another row where the card is too narrow. A command shown cut can only
            // be refused (SECURITY.md): Yes leaves, and Read it opens the command whole first.
            var actions = slot.Actions;
            if (moment.Kind == KitMomentKind.Question && !whole)
                actions = new[] { KitMomentAction.ReadIt }.Concat(actions.Where(action => action != KitMomentAction.YesRunIt)).ToList();
            Actions = actions;
            if (actions.Count > 0)
            {
                y -= gap;
                var target = KitButton.Height;
                var between = KitType.Units(SpatialKit.TargetGapDegrees);
                var rowX = -width / 2f + pad;
                var rowTop = y;
                foreach (var action in actions)
                {
                    var button = ButtonFor(action);
                    button.Role = RoleOf(action);
                    var w = button.Measure(KitMomentWords.Action(action), null);
                    if (rowX + w > width / 2f - pad + 1e-5f && rowX > -width / 2f + pad)
                    {
                        rowX = -width / 2f + pad;
                        rowTop -= target + between;
                    }
                    button.Show(KitMomentWords.Action(action), null, new Vector2(rowX + w / 2f, rowTop - target / 2f), w);
                    shown.Add((action, button));
                    rowX += w + between;
                }
                y = rowTop - target;
            }
            y -= pad;

            var height = -y;
            Size = new Vector2(width, height);
            carrier.Draw(Size, KitType.Units(SpatialKit.ControlRadiusDegrees), SpatialKit.Carrier);
            carrier.transform.localPosition = new Vector3(0f, -height / 2f, 0f);
            var edge = KitType.Units(SpatialKit.StackEdgeDegrees);
            stack.gameObject.SetActive(slot.Earlier != null);
            if (slot.Earlier != null)
            {
                var inset = KitType.Units(1f);
                stack.Draw(new Vector2(width - 2f * inset, 2f * edge), KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Raised));
                stack.transform.localPosition = new Vector3(0f, edge / 2f, 0.0004f);
            }
            // Everything above was laid from the card's top; centre it on the transform.
            body.localPosition = new Vector3(0f, height / 2f, 0f);
            ReadsWhole = whole;
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
