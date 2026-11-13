#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// A panel row's label and note given apart from its words (FIELD's b5a36c9b9, AUDIT's 2026-11-13T1228Z): "First Task" over a
    /// person's idea of two lines, "Create First Task" beside "Not Sent", "Create Project" beside "Confirmed", at their longest
    /// likely words. Measured: the label, the words and the note each whole, none over another, all inside the row; and the
    /// measure fails on a row crowded on purpose, so it is seen to bite. Synthetic words only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] RowsApartStates = { "45-rows-apart" };

        private static PartShot RowsApartShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            var rows = new List<RoomRow>
            {
                new RoomRow(LineWords.TheirName("Make a kite that follows my hand, climbs when I raise it, and dips when I lower it"), label: "First Task"),
                new RoomRow(LineWords.Ours("Create First Task"), note: "Not Sent"),
                new RoomRow(LineWords.Ours("Create Project"), note: "Confirmed"),
            };
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomRowsView.Create(panel.transform);
            var tall = view.Show(rows, width - 2f * pad);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Draw(size);
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);
            shot.Failures.AddRange(RowsApartMeasure(view, rows, width - 2f * pad));

            // The measure bites: a note too long for its row, on purpose, must be named.
            var probe = RoomRowsView.Create(panel.transform);
            var crowded = new List<RoomRow> { new RoomRow(LineWords.Ours("Create First Task"), note: string.Join(" ", Enumerable.Repeat("Waiting on the computer", 6))) };
            probe.Show(crowded, width - 2f * pad);
            if (RowsApartMeasure(probe, crowded, width - 2f * pad).Count == 0) shot.Failures.Add("a row crowded on purpose passes the label and note measure, so it checks nothing");
            Object.DestroyImmediate(probe.gameObject);
            Debug.Log("Halcyonic: parts: " + name + ": " + rows.Count + " rows, their labels and notes apart, " + (tall / RoomType.Units(1f)).ToString("0.0") + " dp tall.");
            return shot;
        }

        /// <summary>Each row's label, words and note whole, inside the row's width, and none of them over another; the failures, by row.</summary>
        private static List<string> RowsApartMeasure(RoomRowsView view, IReadOnlyList<RoomRow> rows, float width)
        {
            var failures = new List<string>();
            var slack = RoomType.Units(0.05f);
            Rect? Box(TMP_Text? label)
            {
                if (label == null) return null;
                label.ForceMeshUpdate(true);
                var bounds = label.textBounds;
                var at = label.transform.localPosition;
                return Rect.MinMaxRect(at.x + bounds.min.x, at.y + bounds.min.y, at.x + bounds.max.x, at.y + bounds.max.y);
            }
            var ends = new List<float>();
            bool Crosses(Rect a, Rect b) => a.xMin < b.xMax - slack && a.xMax > b.xMin + slack && a.yMin < b.yMax - slack && a.yMax > b.yMin + slack;
            for (var index = 0; index < rows.Count; index++)
            {
                var what = "row " + index + " (\"" + rows[index].Words.Text + "\")";
                var parts = new List<(string Name, TMP_Text? Label)> { ("words", view.Label(index)), ("label", view.Tag(index)), ("note", view.Note(index)) };
                if (rows[index].Label != null && view.Tag(index) == null) failures.Add(what + " shows no label");
                if (rows[index].Note != null && view.Note(index) == null) failures.Add(what + " shows no note");
                var boxes = new List<(string Name, Rect Box)>();
                foreach (var (part, label) in parts)
                {
                    if (label == null) continue;
                    if (label.isTextOverflowing || label.GetParsedText().Contains("\u2026")) failures.Add(what + "'s " + part + " is cut");
                    if (Box(label) is Rect box)
                    {
                        if (box.xMin < -slack || box.xMax > width + slack) failures.Add(what + "'s " + part + " runs past the row");
                        boxes.Add((part, box));
                    }
                }
                var noted = boxes.Where(each => each.Name == "note").Select(each => each.Box).ToList();
                if (noted.Count > 0) ends.Add(noted[0].xMax);
                for (var a = 0; a < boxes.Count; a++)
                    for (var b = a + 1; b < boxes.Count; b++)
                        if (Crosses(boxes[a].Box, boxes[b].Box)) failures.Add(what + "'s " + boxes[a].Name + " and " + boxes[b].Name + " stand over each other");
            }
            // Every note ends on one right edge: the states stand in a column.
            if (ends.Count > 1 && ends.Max() - ends.Min() > RoomType.Units(1f)) failures.Add("the rows' notes end " + ((ends.Max() - ends.Min()) / RoomType.Units(1f)).ToString("0.0") + " dp apart, not on one edge");
            return failures;
        }
    }
}
