#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The context spine at the instrument's left: where the person is. The connected switch always stands at its top,
    /// in one fixed place in every state; under it the places and the selected project's tasks as rows, the chosen
    /// one lit; and one quiet row at its foot. Its rows only take the person somewhere (<see cref="RowPressed"/>).
    /// Laid from its top left.
    /// </summary>
    public sealed class KitSpine : MonoBehaviour
    {
        private KitModeSwitch modeSwitch = null!;
        private readonly List<KitRow> rows = new List<KitRow>();
        private readonly List<TextMeshPro> placeHeadings = new List<TextMeshPro>();
        private readonly List<TextMeshPro> nameHeadings = new List<TextMeshPro>();
        private readonly List<TextMeshPro> notes = new List<TextMeshPro>();
        private readonly List<Surface> dividers = new List<Surface>();
        private KitRow footer = null!;

        /// <summary>A row was pressed: the owner goes where its key says.</summary>
        public event Action<string>? RowPressed;

        public KitModeSwitch Switch => modeSwitch;

        /// <summary>Every row shown, the foot's included.</summary>
        public IEnumerable<KitRow> Rows
        {
            get
            {
                foreach (var row in rows) if (row.gameObject.activeSelf) yield return row;
                if (footer.gameObject.activeSelf) yield return footer;
            }
        }

        public static KitSpine Create(Transform parent, string name = "Context spine")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var spine = go.AddComponent<KitSpine>();
            spine.modeSwitch = KitModeSwitch.Create(go.transform);
            spine.footer = KitRow.Create(go.transform, "Foot row");
            spine.footer.Pressed += key => spine.RowPressed?.Invoke(key);
            return spine;
        }

        /// <summary>
        /// Lays the spine from <paramref name="topLeft"/>, <paramref name="width"/> wide, its foot row on the last line of
        /// <paramref name="height"/>, and returns the height its content needs, so its instrument can be at least that tall.
        /// </summary>
        public float Show(IReadOnlyList<KitSpineSection> sections, KitRowModel? foot, Vector2 topLeft, float width, float height)
        {
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            var switchSize = modeSwitch.Layout(width);
            modeSwitch.transform.localPosition = new Vector3(width / 2f, -switchSize.y / 2f, 0f);
            // Targets stand 12 mm apart everywhere: the switch's lower position and the first row included.
            var targetGap = KitType.Units(SpatialKit.TargetGapDegrees);
            var y = switchSize.y + Mathf.Max(KitType.Scaled(SpatialKit.GroupGapDegrees), targetGap);
            var rowIndex = 0;
            var placeIndex = 0;
            var nameIndex = 0;
            var dividerIndex = 0;
            var noteIndex = 0;
            foreach (var section in sections)
            {
                // Sections are divided by space and their headings, not by rules.
                y += KitType.Scaled(SpatialKit.GridDegrees);
                if (section.Heading != null)
                {
                    // A place's heading in the instrument face; a project's own name in the reading face, upright as a named identity.
                    var label = section.HeadingIsName
                        ? Take(nameHeadings, nameIndex++, () => KitType.Create(transform, "Name heading " + nameHeadings.Count, KitRole.Strong, KitType.Text, KitType.Order.Words))
                        : Take(placeHeadings, placeIndex++, () => KitType.Create(transform, "Place heading " + placeHeadings.Count, KitRole.Heading, KitType.Secondary, KitType.Order.Words));
                    KitType.Size(label, section.HeadingIsName ? KitRole.Strong : KitRole.Heading);
                    label.color = section.HeadingIsName ? KitType.Text : KitType.Secondary;
                    if (section.HeadingIsName) GlazeRuns.Of(label).SetIdentity(section.Heading);
                    else GlazeRuns.Of(label).Set(LineWords.Ours(section.Heading));
                    var (_, _, headingHeight) = KitType.Lay(label, width - KitType.Scaled(0.7f), 2);
                    label.transform.localPosition = new Vector3(KitType.Scaled(0.7f), -y, -0.0005f);
                    y += headingHeight + KitType.Scaled(0.25f);
                }
                if (section.Note != null)
                {
                    // A quiet line under the heading, never a target: the connection's state, as the context column says it.
                    var note = Take(notes, noteIndex++, () => KitType.Create(transform, "Note " + notes.Count, KitRole.Body, KitType.Secondary, KitType.Order.Words));
                    KitType.Size(note, KitRole.Body);
                    GlazeText.SetLiteral(note, section.Note);
                    var (_, _, noteHeight) = KitType.Lay(note, width - KitType.Scaled(0.7f), 1);
                    note.transform.localPosition = new Vector3(KitType.Scaled(0.7f), -y, -0.0005f);
                    y += noteHeight + KitType.Scaled(0.35f);
                }
                foreach (var model in section.Rows)
                {
                    var row = Take(rows, rowIndex++, () =>
                    {
                        var made = KitRow.Create(transform, "Row " + rows.Count);
                        made.Pressed += key => RowPressed?.Invoke(key);
                        return made;
                    });
                    y += row.Show(model, new Vector2(0f, -y), width) + targetGap;
                }
                y += KitType.Scaled(0.5f);
            }
            for (var index = rowIndex; index < rows.Count; index++) rows[index].Hide();
            for (var index = placeIndex; index < placeHeadings.Count; index++) placeHeadings[index].gameObject.SetActive(false);
            for (var index = nameIndex; index < nameHeadings.Count; index++) nameHeadings[index].gameObject.SetActive(false);
            for (var index = dividerIndex; index < dividers.Count; index++) dividers[index].gameObject.SetActive(false);
            for (var index = noteIndex; index < notes.Count; index++) notes[index].gameObject.SetActive(false);
            if (foot != null)
            {
                // At the foot, measured first so it stands on the spine's last line.
                var footHeight = footer.Show(foot, Vector2.zero, width);
                footer.Show(foot, new Vector2(0f, -Mathf.Max(y, height - footHeight)), width);
                return y + footHeight;
            }
            footer.Hide();
            return y - targetGap;
        }

        private static T Take<T>(List<T> pool, int index, Func<T> make) where T : Component
        {
            while (pool.Count <= index) pool.Add(make());
            var item = pool[index];
            item.gameObject.SetActive(true);
            return item;
        }
    }
}
