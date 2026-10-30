#nullable enable
using System;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The spatial kit's composer, the slate's permanent foot, as a conversation's composer is: Your instruction, and
    /// whether the draft has gone, over the person's own words in a well sunk into the slate, then its footer, Hold to
    /// talk where the voice is offered and Send instruction, the region's one main action, at the right. It has no Hide: an empty composer is one line, so it never crowds the reading, and a draft stays where
    /// it was written. The person's words stay outside text, literal, upright only through the explicit prose
    /// treatment the input label justifies. Its field and buttons only ask their owner: typing and sending keep their
    /// existing rules, and Send waits, quiet, with the reason given above the footer, until the owner says the whole
    /// draft has been read. Laid from its top left.
    /// </summary>
    public sealed class KitComposer : MonoBehaviour
    {
        public const string Label = FileScreens.YourInstruction;
        public const string Empty = "Instruction";
        public const string NotSent = "Not Sent";
        public const string SendInstruction = "Send Instruction";

        /// <summary>The input's label while the work waits for the person's answer, as the file column labels it.</summary>
        public const string AnswerLabel = "Your Answer";

        private TextMeshPro label = null!;
        private Surface well = null!;
        private TextMeshPro draft = null!;
        private TextMeshPro reason = null!;
        private KitPress field = null!;
        private TextMeshPro status = null!;
        private KitButton talk = null!;
        private KitButton send = null!;
        private KitButton close = null!;
        private KitButton rare = null!;
        private Vector2 size;

        public event Action? FieldPressed;
        public event Action? SendPressed;

        /// <summary>The bound Close, Cancel or Hide composer, at the row's left.</summary>
        public event Action? ClosePressed;

        /// <summary>The bound Stop or Discard draft, beside it.</summary>
        public event Action? RarePressed;

        public TextMeshPro Draft => draft;
        public KitPress Field => field;
        public TextMeshPro Status => status;

        /// <summary>Why Send waits, shown above the footer while it does.</summary>
        public TextMeshPro Reason => reason;
        public KitButton Talk => talk;
        public KitButton Send => send;
        public KitButton Close => close;
        public KitButton Rare => rare;
        public Surface Well => well;
        public Vector2 Size => size;

        /// <summary>
        /// Whether the work waits for the person's answer to its question: the composer then asks for Your answer and
        /// sends it with Send answer, production's words for that action, read at the next <see cref="Show"/>.
        /// </summary>
        public bool Answering { get; set; }

        /// <summary>How many of the draft's lines there are at the width last laid, and how many show in the well.</summary>
        public int DraftLines { get; private set; }

        public int ShownDraftLines { get; private set; }

        public static KitComposer Create(Transform parent, string name = "Composer")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var composer = go.AddComponent<KitComposer>();
            composer.label = KitType.Create(go.transform, "Label", KitRole.Body, KitType.Secondary, KitType.Order.Words);
            composer.well = Surface.Create(go.transform, "Well", KitType.Order.Plate);
            composer.draft = KitType.Create(go.transform, "Your words", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            composer.reason = KitType.Create(go.transform, "Reason", KitRole.Body, KitType.Secondary, KitType.Order.Words);
            var fieldHost = new GameObject("Field target");
            fieldHost.transform.SetParent(go.transform, false);
            composer.field = KitPress.Create(fieldHost, Vector2.one * KitType.Units(SpatialKit.TargetDegrees));
            composer.field.Pressed += () => composer.FieldPressed?.Invoke();
            composer.status = KitType.Create(go.transform, "Draft state", KitRole.Body, KitType.Secondary, KitType.Order.Words, TextAlignmentOptions.Right);
            composer.status.rectTransform.pivot = new Vector2(1f, 0.5f);
            composer.status.textWrappingMode = TextWrappingModes.NoWrap;
            composer.talk = KitButton.Create(go.transform, "Hold to talk", KitButtonRole.Secondary);
            composer.send = KitButton.Create(go.transform, "Send instruction", KitButtonRole.Primary);
            composer.send.Pressed += () => composer.SendPressed?.Invoke();
            composer.close = KitButton.Create(go.transform, "Close", KitButtonRole.Secondary);
            composer.close.Pressed += () => composer.ClosePressed?.Invoke();
            composer.close.Hide();
            composer.rare = KitButton.Create(go.transform, "Rare action", KitButtonRole.Secondary);
            composer.rare.Pressed += () => composer.RarePressed?.Invoke();
            composer.rare.Hide();
            return composer;
        }

        /// <summary>
        /// Shows <paramref name="words"/>, the person's unsent draft, <paramref name="width"/> wide with up to
        /// <paramref name="rows"/> lines in the well; Hold to talk where <paramref name="voiceOffered"/>; Send available
        /// only when <paramref name="sendable"/>, else quiet with <paramref name="whyNot"/>. Returns its height.
        /// </summary>
        public float Show(Vector2 topLeft, float width, string words, int rows, bool voiceOffered, bool sendable, string? whyNot) =>
            Lay(topLeft, width, words, rows, sendable, whyNot, false, voiceOffered, true, null, null, null, null);

        /// <summary>
        /// Shows the composer bound to a task's file (<see cref="KitFileBinding"/>): each control only where the file's
        /// frame offers it, in its words and availability, in the footer's order along one row, <paramref name="close"/>
        /// (Close, Cancel or Hide composer) at the left, <paramref name="rare"/> (Stop or Discard draft) beside it, then
        /// <paramref name="talk"/> and <paramref name="send"/> at the right, Send rightmost; its caption only where the
        /// frame shows one (<paramref name="caption"/>), as the composer's own page does; no well where the frame offers no
        /// words to write (<paramref name="well"/>), as a question's page. Returns its height.
        /// </summary>
        public float Show(Vector2 topLeft, float width, string words, int rows, bool sendable, string? whyNot,
            KitControl? close, KitControl? rare, KitControl? talk, KitControl? send, bool caption, bool well = true) =>
            Lay(topLeft, width, words, rows, sendable, whyNot, true, talk != null, caption, close, rare, talk, send, well);

        /// <summary>Unbound, as the fixture shows it, Hold to talk and Send stand in the kit's own words; bound, each only where its control is.</summary>
        private float Lay(Vector2 topLeft, float width, string words, int rows, bool sendable, string? whyNot, bool bound, bool voiceOffered, bool caption,
            KitControl? closeControl, KitControl? rareControl, KitControl? talkControl, KitControl? sendControl, bool withWell = true)
        {
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            label.gameObject.SetActive(caption);
            KitType.Size(label, KitRole.Body);
            GlazeText.SetLiteral(label, Answering ? AnswerLabel : Label);
            var (_, labelWidth, labelHeight) = KitType.Lay(label, width, 1);
            label.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            // Whether the draft has gone, on the label's line at its right: said whole, or not at all.
            KitType.Size(status, KitRole.Body);
            GlazeText.SetLiteral(status, NotSent);
            var statusRoom = width - labelWidth - KitType.Scaled(1f);
            var statusFits = caption && !string.IsNullOrEmpty(words) && status.GetPreferredValues(status.text).x <= statusRoom;
            status.gameObject.SetActive(statusFits);
            if (statusFits)
            {
                KitType.Lay(status, statusRoom, 1);
                status.transform.localPosition = new Vector3(width, -labelHeight / 2f, -0.0005f);
            }
            var y = caption ? labelHeight + KitType.Scaled(0.35f) : 0f;

            well.gameObject.SetActive(withWell);
            draft.gameObject.SetActive(withWell);
            field.gameObject.SetActive(withWell);
            if (!withWell)
            {
                DraftLines = 0;
                ShownDraftLines = 0;
            }
            else
            {
                // The well: the person's words, upright and literal, or the empty field's own prompt.
                var pad = KitType.Scaled(0.75f);
                KitType.Size(draft, KitRole.Body, SpatialKit.ReadingLeading);
                var runs = GlazeRuns.Of(draft);
                if (string.IsNullOrEmpty(words))
                {
                    runs.Set(LineWords.Ours(Answering ? FileScreens.TypeMyAnswer : Empty));
                    draft.color = KitType.Secondary;
                }
                else
                {
                    runs.Set(LineWords.Theirs(words));
                    runs.SetProseMode(true);
                    draft.color = KitType.Text;
                }
                var inner = width - 2f * pad;
                var (all, _, _) = KitType.Lay(draft, inner, 200);
                DraftLines = all;
                ShownDraftLines = Mathf.Min(all, Mathf.Max(1, rows));
                KitType.Lay(draft, inner, ShownDraftLines);
                var pitch = KitType.LinePitch(draft);
                var wellHeight = 2f * pad + Mathf.Max(ShownDraftLines, Mathf.Min(rows, 2)) * pitch;
                // Sunk into the slate by its darker tone alone.
                well.Draw(new Vector2(width, wellHeight), KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Well));
                well.transform.localPosition = new Vector3(width / 2f, -y - wellHeight / 2f, 0f);
                draft.transform.localPosition = new Vector3(pad, -y - pad, -0.0005f);
                field.Resize(new Vector2(width, Mathf.Max(wellHeight, KitType.Units(SpatialKit.TargetDegrees))));
                field.transform.localPosition = new Vector3(width / 2f, -y - wellHeight / 2f, -KitType.Units(0.04f));
                // The field and the footer's buttons are targets too: 12 mm apart.
                y += wellHeight + Mathf.Max(KitType.Scaled(0.6f), KitType.Units(SpatialKit.TargetGapDegrees));
            }

            reason.gameObject.SetActive(!sendable && whyNot != null);
            if (!sendable && whyNot != null)
            {
                KitType.Size(reason, KitRole.Body);
                GlazeText.SetLiteral(reason, whyNot);
                var (_, _, reasonHeight) = KitType.Lay(reason, width, 2);
                reason.transform.localPosition = new Vector3(0f, -y, -0.0005f);
                y += reasonHeight + KitType.Scaled(0.35f);
            }

            // The footer: Hold to talk and Send instruction at the right, Send rightmost; bound, Close and the rare action
            // at the left, in the order the file's footer stands them.
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var row = -y - KitButton.Height / 2f;
            // Close and the rare action stand at the row's left; where they would not keep a target's gap from Hold to talk
            // and Send, they take a row of their own above them, the same order read row by row.
            var leftControls = new[] { (close, closeControl), (rare, rareControl) }.Where(each => each.Item2 != null).ToList();
            var leftWidth = leftControls.Sum(each => each.Item1.Measure(each.Item2!.Words, each.Item2.Icon ?? GlazeIcon.Close) + gap);
            var rightWidth = (sendControl != null || !bound ? send.Measure(sendControl?.Words ?? SendInstruction, GlazeIcon.SendAnswer) + gap : 0f)
                + (voiceOffered ? talk.Measure(talkControl?.Words ?? VoiceText.HoldToTalk, GlazeIcon.HoldToTalk) + gap : 0f);
            var leftRow = row;
            if (leftControls.Count > 0 && leftWidth + rightWidth - gap > width + 1e-5f)
            {
                row -= KitButton.Height + gap;
                y += KitButton.Height + gap;
            }
            var right = width;
            send.gameObject.SetActive(sendControl != null || !bound);
            if (send.gameObject.activeSelf)
            {
                var sendWords = sendControl != null ? sendControl.Words
                    : Answering ? WorkspaceText.Label(WorkspaceAction.Answer) : SendInstruction;
                var sendWidth = send.Measure(sendWords, GlazeIcon.SendAnswer);
                send.Available = sendable;
                send.Show(sendWords, GlazeIcon.SendAnswer, new Vector2(right - sendWidth / 2f, row), sendWidth);
                right -= sendWidth + gap;
            }
            talk.gameObject.SetActive(voiceOffered);
            if (voiceOffered)
            {
                var talkWords = talkControl?.Words ?? VoiceText.HoldToTalk;
                var talkWidth = talk.Measure(talkWords, GlazeIcon.HoldToTalk);
                talk.Available = talkControl == null || talkControl.Available;
                talk.Show(talkWords, GlazeIcon.HoldToTalk, new Vector2(right - talkWidth / 2f, row), talkWidth);
                right -= talkWidth + gap;
            }
            var left = 0f;
            foreach (var (button, control) in new[] { (close, closeControl), (rare, rareControl) })
            {
                if (control == null) { button.Hide(); continue; }
                var glyph = control.Icon ?? GlazeIcon.Close;
                var buttonWidth = button.Measure(control.Words, glyph);
                button.Available = control.Available;
                button.Show(control.Words, glyph, new Vector2(left + buttonWidth / 2f, leftRow), buttonWidth);
                left += buttonWidth + gap;
            }
            y += KitButton.Height;
            size = new Vector2(width, y);
            return y;
        }
    }
}
