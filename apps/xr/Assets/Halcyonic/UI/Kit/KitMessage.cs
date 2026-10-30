#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The agent's own words as the slate reads them: its source chip, Agent says, and when it said it, then every
    /// word it said, wrapped at the reading size and pitch. Its words stay outside text: literal, rich text off,
    /// their ranges kept, and upright only through the explicit attributed-prose treatment
    /// (<see cref="GlazeRuns.SetProseMode"/>) that the chip beside them justifies; upright never means observed.
    /// What does not fit the room given is not cut: the message says how many of its lines show, so its reader
    /// can page it. Laid from its top left on its transform.
    /// </summary>
    public sealed class KitMessage : MonoBehaviour
    {
        /// <summary>The chip's words: Halcyonic's own name for an agent's claim (WORDS.md).</summary>
        public const string Source = SectionLine.AgentChip;

        private KitChip chip = null!;
        private TextMeshPro when = null!;
        private TextMeshPro prose = null!;

        public TextMeshPro Prose => prose;

        public KitChip Chip => chip;

        /// <summary>How many lines of the message there are at the width last laid, and how many show.</summary>
        public int Lines { get; private set; }

        public int ShownLines { get; private set; }

        /// <summary>Every line of the message shows.</summary>
        public bool Complete => ShownLines >= Lines && Lines > 0;

        public float Height { get; private set; }

        public IReadOnlyList<(int Start, int End)> Theirs { get; private set; } = new (int, int)[0];

        public static KitMessage Create(Transform parent, string name, int order = KitType.Order.Words)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var message = go.AddComponent<KitMessage>();
            message.chip = KitChip.Create(go.transform, "Source chip", order - 4);
            message.when = KitType.Create(go.transform, "When", KitRole.Body, KitType.Secondary, order, TextAlignmentOptions.Left);
            message.when.rectTransform.pivot = new Vector2(0f, 0.5f);
            message.when.textWrappingMode = TextWrappingModes.NoWrap;
            message.prose = KitType.Create(go.transform, "Reported words", KitRole.Body, KitType.Text, order, leading: SpatialKit.ReadingLeading);
            return message;
        }

        /// <summary>
        /// Lays out <paramref name="entry"/>, an agent message from the activity (<see cref="ActivityEntry.Reported"/>),
        /// <paramref name="width"/> wide, in at most <paramref name="room"/> of height, and returns the height it takes.
        /// </summary>
        public float Show(ActivityEntry entry, float width, float room)
        {
            var chipSize = chip.Show(LineWords.Ours(Source), GlazeIcon.Quote, KitType.Secondary, width);
            chip.transform.localPosition = new Vector3(chipSize.x / 2f, -chipSize.y / 2f, 0f);
            KitType.Size(when, KitRole.Body);
            GlazeText.SetLiteral(when, ClockOf(entry.OccurredAt));
            KitType.Lay(when, KitType.Units(12f), 1);
            when.transform.localPosition = new Vector3(chipSize.x + KitType.Scaled(0.6f), -chipSize.y / 2f, -0.0005f);

            KitType.Size(prose, KitRole.Body, SpatialKit.ReadingLeading);
            var runs = GlazeRuns.Of(prose);
            Theirs = runs.Set(entry.Words);
            runs.SetProseMode(true);
            var top = chipSize.y + KitType.Scaled(0.6f);
            var pitch = KitType.LinePitch(prose);
            var (all, _, _) = KitType.Lay(prose, width, 400);
            Lines = all;
            var fits = Mathf.Max(1, Mathf.FloorToInt((room - top + 1e-5f) / pitch));
            ShownLines = Mathf.Min(all, fits);
            // The lines that fit, the last cut inside their words with an ellipsis where more remain, for the
            // reader's pager to go on from: the room is the slate's, the words stay theirs.
            KitType.Lay(prose, width, ShownLines);
            prose.transform.localPosition = new Vector3(0f, -top, -0.0005f);
            Height = top + ShownLines * pitch;
            return Height;
        }

        /// <summary>A journal time as the person reads it: the hour and minute it was recorded.</summary>
        public static string ClockOf(string occurredAt)
        {
            var at = occurredAt.IndexOf('T');
            return at >= 0 && occurredAt.Length >= at + 6 ? occurredAt.Substring(at + 1, 5) : occurredAt;
        }
    }
}
