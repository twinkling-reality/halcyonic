#nullable enable
using System;
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
        public const string Label = "Your instruction";
        public const string Empty = "Type your instruction";
        public const string NotSent = "Not sent yet.";

        private TextMeshPro label = null!;
        private Surface well = null!;
        private TextMeshPro draft = null!;
        private TextMeshPro reason = null!;
        private KitPress field = null!;
        private TextMeshPro status = null!;
        private KitButton talk = null!;
        private KitButton send = null!;
        private Vector2 size;

        public event Action? FieldPressed;
        public event Action? SendPressed;

        public TextMeshPro Draft => draft;
        public KitPress Field => field;
        public TextMeshPro Status => status;
        public KitButton Talk => talk;
        public KitButton Send => send;
        public Surface Well => well;
        public Vector2 Size => size;

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
            return composer;
        }

        /// <summary>
        /// Shows <paramref name="words"/>, the person's unsent draft, <paramref name="width"/> wide with up to
        /// <paramref name="rows"/> lines in the well; Hold to talk where <paramref name="voiceOffered"/>; Send available
        /// only when <paramref name="sendable"/>, else quiet with <paramref name="whyNot"/>. Returns its height.
        /// </summary>
        public float Show(Vector2 topLeft, float width, string words, int rows, bool voiceOffered, bool sendable, string? whyNot)
        {
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            KitType.Size(label, KitRole.Body);
            GlazeText.SetLiteral(label, Label);
            var (_, labelWidth, labelHeight) = KitType.Lay(label, width, 1);
            label.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            // Whether the draft has gone, on the label's line at its right: said whole, or not at all.
            KitType.Size(status, KitRole.Body);
            GlazeText.SetLiteral(status, NotSent);
            var statusRoom = width - labelWidth - KitType.Scaled(1f);
            var statusFits = !string.IsNullOrEmpty(words) && status.GetPreferredValues(status.text).x <= statusRoom;
            status.gameObject.SetActive(statusFits);
            if (statusFits)
            {
                KitType.Lay(status, statusRoom, 1);
                status.transform.localPosition = new Vector3(width, -labelHeight / 2f, -0.0005f);
            }
            var y = labelHeight + KitType.Scaled(0.35f);

            // The well: the person's words, upright and literal, or the empty field's own prompt.
            var pad = KitType.Scaled(0.75f);
            KitType.Size(draft, KitRole.Body, SpatialKit.ReadingLeading);
            var runs = GlazeRuns.Of(draft);
            if (string.IsNullOrEmpty(words))
            {
                runs.Set(LineWords.Ours(Empty));
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
            well.Draw(new Vector2(width, wellHeight), KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Well),
                KitType.Colour(SpatialKit.Edge), KitType.Units(SpatialKit.HairlineDegrees));
            well.transform.localPosition = new Vector3(width / 2f, -y - wellHeight / 2f, 0f);
            draft.transform.localPosition = new Vector3(pad, -y - pad, -0.0005f);
            field.Resize(new Vector2(width, Mathf.Max(wellHeight, KitType.Units(SpatialKit.TargetDegrees))));
            field.transform.localPosition = new Vector3(width / 2f, -y - wellHeight / 2f, -KitType.Units(0.04f));
            // The field and the footer's buttons are targets too: 12 mm apart.
            y += wellHeight + Mathf.Max(KitType.Scaled(0.6f), KitType.Units(SpatialKit.TargetGapDegrees));

            reason.gameObject.SetActive(!sendable && whyNot != null);
            if (!sendable && whyNot != null)
            {
                KitType.Size(reason, KitRole.Body);
                GlazeText.SetLiteral(reason, whyNot);
                var (_, _, reasonHeight) = KitType.Lay(reason, width, 2);
                reason.transform.localPosition = new Vector3(0f, -y, -0.0005f);
                y += reasonHeight + KitType.Scaled(0.35f);
            }

            // The footer: Hold to talk and Send instruction at the right, Send rightmost.
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var row = -y - KitButton.Height / 2f;
            var sendWidth = send.Measure("Send instruction", GlazeIcon.SendAnswer);
            send.Available = sendable;
            send.Show("Send instruction", GlazeIcon.SendAnswer, new Vector2(width - sendWidth / 2f, row), sendWidth);
            var right = width - sendWidth - gap;
            talk.gameObject.SetActive(voiceOffered);
            if (voiceOffered)
            {
                var talkWidth = talk.Measure("Hold to talk", GlazeIcon.HoldToTalk);
                talk.Show("Hold to talk", GlazeIcon.HoldToTalk, new Vector2(right - talkWidth / 2f, row), talkWidth);
                right -= talkWidth + gap;
            }
            y += KitButton.Height;
            size = new Vector2(width, y);
            return y;
        }
    }
}
