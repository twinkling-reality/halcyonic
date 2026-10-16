#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>One line of the evidence rail: a journal-derived activity entry, the icon its event's kind shows and the tone its recorded outcome takes.</summary>
    public sealed class KitTrailEntry
    {
        public KitTrailEntry(ActivityEntry entry, GlazeIcon icon, GlazeTone tone = GlazeTone.Neutral)
        {
            Entry = entry;
            Icon = icon;
            Tone = tone;
        }

        public ActivityEntry Entry { get; }

        public GlazeIcon Icon { get; }

        /// <summary>The recorded outcome's tone on the entry's icon; its words stay in the text colour.</summary>
        public GlazeTone Tone { get; }
    }

    /// <summary>
    /// The evidence rail's trail: what the journal recorded, newest last, each entry a node on one fine line with its
    /// words, under the minute it was recorded in. Where an entry ends in text from outside, as a file's path, a command
    /// or a test run's summary, that text stands in a chip under Halcyonic's words, as written, so evidence reads apart
    /// from the words that introduce it; nothing is reworded. Entries are recorded events only: agent messages are
    /// read in the slate, and the trail draws no progress of its own. The newest entries that fit show; how many
    /// earlier ones do not is <see cref="Earlier"/>, for the reader to open the whole history. Laid from its top left.
    /// </summary>
    public sealed class KitTrail : MonoBehaviour
    {
        private const float NodeDegrees = 1.6f;
        private const float EntryGapDegrees = 0.7f;

        private readonly List<Row> rows = new List<Row>();
        private readonly List<TextMeshPro> minutes = new List<TextMeshPro>();
        private Surface line = null!;
        private int order;

        /// <summary>How many earlier entries did not fit.</summary>
        public int Earlier { get; private set; }

        public float Height { get; private set; }

        /// <summary>The entries shown, oldest first.</summary>
        public IReadOnlyList<KitTrailEntry> Shown { get; private set; } = new KitTrailEntry[0];

        /// <summary>Every chip shown, for checks.</summary>
        public IEnumerable<KitChip> Chips => rows.Where(row => row.Root.activeSelf && row.Chip.gameObject.activeSelf).Select(row => row.Chip);

        public static KitTrail Create(Transform parent, string name, int order = KitType.Order.Words)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var trail = go.AddComponent<KitTrail>();
            trail.order = order;
            trail.line = Surface.Create(go.transform, "Trail line", order - 6);
            return trail;
        }

        /// <summary>Shows the newest of <paramref name="entries"/> (oldest first) that fit <paramref name="room"/>, <paramref name="width"/> wide; returns the height used.</summary>
        public float Show(IReadOnlyList<KitTrailEntry> entries, float width, float room)
        {
            var node = KitType.Scaled(NodeDegrees);
            var gap = KitType.Scaled(EntryGapDegrees);
            var textLeft = node + KitType.Scaled(0.6f);
            var textWidth = Mathf.Max(KitType.Units(4f), width - textLeft);
            while (rows.Count < entries.Count) rows.Add(Row.Make(transform, rows.Count, order));
            var heights = new float[entries.Count];
            var clocks = new string[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                heights[index] = rows[index].Lay(entries[index], textWidth, node);
                clocks[index] = KitMessage.ClockOf(entries[index].Entry.OccurredAt);
            }
            var minuteHeight = MinuteHeight();
            // The newest entries that fit, counted back from the last, each new minute under its own time.
            float WindowHeight(int from)
            {
                var total = 0f;
                for (var index = from; index < entries.Count; index++)
                {
                    if (index == from || clocks[index] != clocks[index - 1]) total += minuteHeight;
                    total += heights[index] + (index + 1 < entries.Count ? gap : 0f);
                }
                return total;
            }
            var first = entries.Count;
            while (first > 0 && WindowHeight(first - 1) <= room + 1e-5f) first--;
            Earlier = first;
            Shown = entries.Skip(first).ToList();
            var y = 0f;
            var minuteIndex = 0;
            var firstNode = 0f;
            var lastNode = 0f;
            for (var index = 0; index < rows.Count; index++)
            {
                var visible = index >= first && index < entries.Count;
                rows[index].Root.SetActive(visible);
                if (!visible) continue;
                if (index == first || clocks[index] != clocks[index - 1])
                {
                    var minute = Minute(minuteIndex++);
                    GlazeText.SetLiteral(minute, clocks[index]);
                    KitType.Lay(minute, textWidth, 1);
                    minute.transform.localPosition = new Vector3(textLeft, -y, -0.0005f);
                    y += minuteHeight;
                }
                rows[index].Root.transform.localPosition = new Vector3(0f, -y, 0f);
                var centre = -y - Mathf.Min(KitType.LinePitch(rows[index].Words), node) / 2f;
                if (index == first) firstNode = centre;
                lastNode = centre;
                y += heights[index] + gap;
            }
            for (var index = minuteIndex; index < minutes.Count; index++) minutes[index].gameObject.SetActive(false);
            Height = Mathf.Max(0f, y - gap);
            // One fine line through every node, from the first node's centre to the last's.
            line.gameObject.SetActive(entries.Count - first > 1);
            if (entries.Count - first > 1)
            {
                var thickness = KitType.Units(0.08f);
                line.Draw(new Vector2(thickness, firstNode - lastNode), thickness / 2f, KitType.Colour(SpatialKit.Edge));
                line.transform.localPosition = new Vector3(node / 2f, (firstNode + lastNode) / 2f, -0.0001f);
            }
            return Height;
        }

        private float MinuteHeight()
        {
            var probe = Minute(0);
            KitType.Size(probe, KitRole.Body);
            return KitType.LinePitch(probe) + KitType.Scaled(0.15f);
        }

        /// <summary>A minute's label, made once and reused: the time its entries were recorded, in Halcyonic's words.</summary>
        private TextMeshPro Minute(int index)
        {
            while (minutes.Count <= index)
                minutes.Add(KitType.Create(transform, "Minute " + minutes.Count, KitRole.Body, KitType.Secondary, order));
            var minute = minutes[index];
            minute.gameObject.SetActive(true);
            KitType.Size(minute, KitRole.Body);
            return minute;
        }

        /// <summary>One entry's views, made once and reused.</summary>
        private sealed class Row
        {
            public GameObject Root = null!;
            public Surface Node = null!;
            public TextMeshPro Icon = null!;
            public TextMeshPro Words = null!;
            public TextMeshPro When = null!;
            public KitChip Chip = null!;

            public static Row Make(Transform parent, int index, int order)
            {
                var root = new GameObject("Entry " + index);
                root.transform.SetParent(parent, false);
                var row = new Row { Root = root };
                row.Node = Surface.Create(root.transform, "Node", order - 4);
                row.Icon = GlazeIcons.Create(root.transform, "Icon", SpatialKit.IconDegrees, KitType.Secondary, order);
                row.Words = KitType.Create(root.transform, "Words", KitRole.Body, KitType.Text, order);
                row.When = KitType.Create(root.transform, "When", KitRole.Body, KitType.Secondary, order, TextAlignmentOptions.TopRight);
                row.When.rectTransform.pivot = new Vector2(1f, 1f);
                row.When.textWrappingMode = TextWrappingModes.NoWrap;
                row.Chip = KitChip.Create(root.transform, "Evidence", order - 3);
                return row;
            }

            /// <summary>Lays the entry out from its row's top left and returns its height.</summary>
            public float Lay(KitTrailEntry item, float width, float node)
            {
                var (head, evidence) = Split(item.Entry.Words);
                When.gameObject.SetActive(false);
                var textLeft = node + KitType.Scaled(0.6f);
                KitType.Size(Words, KitRole.Body);
                GlazeRuns.Of(Words).Set(head);
                var (_, _, wordsHeight) = KitType.Lay(Words, width, 3);
                var pitch = KitType.LinePitch(Words);
                Words.transform.localPosition = new Vector3(textLeft, 0f, -0.0005f);
                var height = Mathf.Max(wordsHeight, node);
                Chip.gameObject.SetActive(evidence != null);
                if (evidence != null)
                {
                    var glyph = item.Tone == GlazeTone.Failure ? GlazeIcon.ChecksFailed : IconOf(evidence.Text);
                    var size = Chip.Show(evidence, glyph, item.Tone == GlazeTone.Failure ? KitType.ToneText(GlazeTone.Failure) : KitType.Secondary, width, 3);
                    var top = height + KitType.Scaled(0.3f);
                    Chip.transform.localPosition = new Vector3(textLeft + size.x / 2f, -top - size.y / 2f, 0f);
                    height = top + size.y;
                }
                // The node: a disc that cuts the line, the event's icon in its outcome's tone.
                var centre = -Mathf.Min(pitch, node) / 2f;
                Node.Draw(new Vector2(node, node), node / 2f, KitType.Colour(SpatialKit.Side), KitType.Colour(SpatialKit.Edge), KitType.Units(SpatialKit.HairlineDegrees));
                Node.transform.localPosition = new Vector3(node / 2f, centre, 0f);
                var iconEm = Mathf.Max(KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale * 0.85f), KitType.Units(1f));
                Icon.fontSize = GlazeTokens.FontSize(iconEm);
                Icon.rectTransform.sizeDelta = Vector2.one * iconEm;
                Icon.color = item.Tone == GlazeTone.Neutral ? KitType.Secondary : KitType.ToneText(item.Tone);
                GlazeIcons.Show(Icon, item.Icon);
                Icon.transform.localPosition = new Vector3(node / 2f, centre, -0.0005f);
                return height;
            }

            /// <summary>
            /// An entry ending in outside text, after Halcyonic's words, splits there: Halcyonic's words with any closing
            /// ": " dropped, and that text whole, for its chip. Where that text is a tool's name, then ": " and what the
            /// tool worked on, as ActivityLog joins them, the name stays with the words and the chip holds what it worked
            /// on: every word still shows, in order. Anything else stays one line.
            /// </summary>
            private static (LineWords Head, LineWords? Evidence) Split(LineWords words)
            {
                var runs = words.Runs;
                if (runs.Count < 2 || !runs[runs.Count - 1].Theirs || runs[runs.Count - 2].Theirs) return (words, null);
                var head = LineWords.None;
                for (var index = 0; index < runs.Count - 1; index++)
                {
                    var run = runs[index];
                    var text = index == runs.Count - 2 ? run.Text.TrimEnd(' ').TrimEnd(':').TrimEnd(' ') : run.Text;
                    if (text.Length == 0) continue;
                    head = head + (run.Theirs ? LineWords.Theirs(text) : LineWords.Ours(text));
                }
                if (head.IsEmpty) return (words, null);
                var theirs = runs[runs.Count - 1].Text;
                var colon = theirs.IndexOf(": ", System.StringComparison.Ordinal);
                if (runs[runs.Count - 2].Text.EndsWith(" ", System.StringComparison.Ordinal) && colon > 0 && colon + 2 < theirs.Length)
                    return (head + LineWords.Ours(" ") + LineWords.Theirs(theirs.Substring(0, colon)), LineWords.Theirs(theirs.Substring(colon + 2)));
                return (head, LineWords.Theirs(theirs));
            }

            /// <summary>The glyph a piece of evidence shows: its file's kind where its last word names one.</summary>
            private static GlazeIcon IconOf(string text) => FileKinds.Of(text) switch
            {
                FileKind.Code => GlazeIcon.CodeFile,
                FileKind.Database => GlazeIcon.DatabaseFile,
                FileKind.Data => GlazeIcon.DataFile,
                FileKind.Image => GlazeIcon.ImageFile,
                FileKind.Script => GlazeIcon.ScriptFile,
                FileKind.Package => GlazeIcon.PackageFile,
                FileKind.Folder => GlazeIcon.Folder,
                _ => GlazeIcon.TextFile,
            };
        }
    }
}
