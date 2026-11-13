#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The bar's narrow form while a placement overlay stands (<see cref="RoomBar.Narrow"/>, the lead's 2026-11-13T1711Z ruling):
    /// a character colour's choices and a shelf's placing, each given as the plane gives it (a frame read by the kit's own
    /// readers, then the bar made narrow) beside the same frame at its ordinary width. Measured: the panel as wide as the
    /// column and standing over it, the whole no wider than the column or its widest action, every word whole, nothing joined
    /// on a dot. The width and height are logged in degrees, the room a shelf has beside it. Synthetic words only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] NarrowStates = { "56-narrow-colour", "56-narrow-shelf", "56-narrow-colour-wide" };

        private static MenuFrame NarrowFrame(string state)
        {
            if (state == "56-narrow-shelf")
                return new MenuFrame("Shelf", new Footer(new Prompt(Footer.Close, "Cancel", GlazeIcon.Close, PromptKind.Close),
                        farRight: new Prompt("narrow-apply", "Apply", GlazeIcon.Change, main: true)),
                    lines: new[]
                    {
                        new PageLine("Hold Move Shelf to drag it, then Apply.", tone: LineTone.Secondary, rows: 2),
                        new PageLine("Left", action: "narrow-move", key: "left", control: LineControl.Action),
                        new PageLine("Right", action: "narrow-move", key: "right", control: LineControl.Action),
                        new PageLine("Find Room Surface", action: "narrow-room", control: LineControl.Action),
                    });
            return new MenuFrame("Character Colour", new Footer(new Prompt(Footer.Close, "Back to Task", GlazeIcon.Back, PromptKind.Close),
                    farRight: new Prompt("narrow-preview", "Preview Colour", GlazeIcon.Change, main: true)),
                lines: new[] { "Original Colour", "Sea Glass", "Ember" }
                    .Select((name, at) => new PageLine(name, action: "narrow-choose", key: name, choice: true, chosen: at == 1, answerMark: AnswerMark.Single)).ToArray());
        }

        private static PartShot NarrowShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            RoomBar? bar;
            var frame = NarrowFrame(state);
            try
            {
                RoomDesign.Enabled = true;
                bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
            }
            finally { RoomDesign.Enabled = was; }
            Require(bar != null, "no reader of the kit takes the overlay's frame");
            if (bar == null) return shot;
            var narrow = state != "56-narrow-colour-wide";
            bar.Narrow = narrow;

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            view.Show(bar);
            view.SettleForRender();
            foreach (var button in view.Column.Where(button => button.gameObject.activeSelf))
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);

            var unit = RoomType.Units(1f);
            var column = RoomType.Units(RoomGeometry.ColumnDp);
            var degrees = view.Size / unit / RoomGeometry.DpPerDegree;
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            var widest = buttons.Count == 0 ? 0f : buttons.Max(button => button.Size.x);
            if (narrow)
            {
                // As wide as the column, or its widest action where that needs more; the panel standing over the column, never beside.
                Require(view.Size.x <= Mathf.Max(column, widest) + unit * 0.5f, "the narrow bar stands " + (view.Size.x / unit).ToString("0.0") + " dp wide, wider than the column's "
                    + (column / unit).ToString("0") + " dp and its widest action's " + (widest / unit).ToString("0.0"));
                Require(Mathf.Abs(view.PanelRight - (-view.Size.x / 2f + column)) < unit * 0.5f, "the narrow panel is not the column's width");
                var columnTop = buttons.Count == 0 ? view.Foot : buttons.Max(button => button.transform.localPosition.y + button.Size.y / 2f);
                Require(buttons.All(button => button.transform.localPosition.x - button.Size.x / 2f >= -view.Size.x / 2f - unit * 0.5f), "an action stands left of the narrow bar");
                Require(view.Size.y > columnTop - view.Foot + unit, "the panel does not stand over the column");
            }
            else Require(view.Size.x > column + RoomType.Units(RoomGeometry.PanelDp), "the ordinary bar is not the pair's width");

            Require(!view.PassedLargest, "the bar laid past its largest footprint: " + (view.Size / unit).ToString("0") + " dp against "
                + ((narrow ? RoomBarView.LargestNarrow : RoomBarView.Largest) / unit).ToString("0"));
            var drawn = new List<string>();
            foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                label.ForceMeshUpdate(true);
                var said = label.GetParsedText();
                if (said.Trim().Length == 0) continue;
                drawn.Add(said);
                Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut");
                Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot");
            }
            string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var expected = frame.Lines.Select(line => line.Words).Concat(frame.Footer.All.Select(each => each.Prompt.Words)).ToList();
            foreach (var words in expected)
                Require(drawn.Any(said => Words(said).Contains(Words(words))), "\"" + words + "\" is not drawn whole: " + string.Join(" | ", drawn.Select(Words)));
            Debug.Log("Halcyonic: parts: " + name + ": " + (narrow ? "narrow" : "ordinary") + " bar "
                + degrees.x.ToString("0.0", CultureInfo.InvariantCulture) + " by " + degrees.y.ToString("0.0", CultureInfo.InvariantCulture)
                + " degrees at text scale " + GlazeText.Scale.ToString("0.00", CultureInfo.InvariantCulture) + "; drawn: " + string.Join(" | ", drawn.Select(Words)) + ".");
            return shot;
        }
    }
}
