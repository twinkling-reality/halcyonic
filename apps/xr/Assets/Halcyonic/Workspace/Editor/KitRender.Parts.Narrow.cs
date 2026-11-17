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
        private static readonly string[] NarrowStates = { "56-narrow-colour", "56-narrow-shelf", "56-narrow-colour-wide", "57-destructive-confirm", "58-row-label", "58-narrow-status", "58-answer-rows" };

        private static MenuFrame NarrowFrame(string state)
        {
            if (state == "57-destructive-confirm")
            {
                // A Yes that forgets something, its model declaring it destructive (AUDIT's MUST, 2026-11-13T1754Z): drawn in the
                // destructive kind's red with its cross, behind Confirm's lock, never as going on.
                var before = new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close), farRight: new Prompt("narrow-forget", "Forget Computer", GlazeIcon.Next));
                return new MenuFrame("Paired Computer", Footer.Confirm(before, PromptSlot.FarRight,
                        new Prompt("narrow-forget-yes", "Forget Computer", GlazeIcon.Next, PromptKind.Yes, destructive: true),
                        new Prompt("narrow-forget-cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel)),
                    lines: new[] { new PageLine("This headset stops working with this computer until you pair it again.", rows: 2) });
            }
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
            if (state == "58-row-label") return RowLabelShot(shot, root, eyes, name);
            if (state == "58-narrow-status") return NarrowStatusShot(shot, root, eyes, name);
            if (state == "58-answer-rows") return AnswerRowsShot(shot, root, eyes, name);
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
            var narrow = state != "56-narrow-colour-wide" && state != "57-destructive-confirm";
            bar.Narrow = narrow;

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            var unit = RoomType.Units(1f);
            var column = RoomType.Units(RoomGeometry.ColumnDp);
            string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            // Every page the reader made, as the plane turns it (its column holds four actions a page, the spec's cap): each page
            // measured on its own, and the frame's lines and actions met across them. The render keeps the first page.
            var drawn = new List<string>();
            var expected = frame.Lines.Select(line => line.Words).ToList();
            var pages = bar.GeneralPart.Pages;
            var degrees = Vector2.zero;
            for (var page = pages - 1; page >= 0; page--)
            {
                if (pages > 1) bar.Turn();
                var at = bar.GeneralPart.Page;
                view.Show(bar);
                view.SettleForRender();
                foreach (var button in view.Column.Where(button => button.gameObject.activeSelf))
                {
                    button.Press.SettleForRender();
                    button.Settle();
                }
                if (at == 0) Place(plane, eyes, 0f, shot.Elevation);
                var where = pages > 1 ? " on page " + (at + 1) + " of " + pages : "";

                var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
                var widest = buttons.Count == 0 ? 0f : buttons.Max(button => button.Size.x);
                if (narrow)
                {
                    // As wide as the column, or its widest action where that needs more; the panel standing over the column, never beside.
                    Require(view.Size.x <= Mathf.Max(column, widest) + unit * 0.5f, "the narrow bar stands " + (view.Size.x / unit).ToString("0.0") + " dp wide" + where + ", wider than the column's "
                        + (column / unit).ToString("0") + " dp and its widest action's " + (widest / unit).ToString("0.0"));
                    Require(Mathf.Abs(view.PanelRight - (-view.Size.x / 2f + column)) < unit * 0.5f, "the narrow panel is not the column's width" + where);
                    var columnTop = buttons.Count == 0 ? view.Foot : buttons.Max(button => button.transform.localPosition.y + button.Size.y / 2f);
                    Require(buttons.All(button => button.transform.localPosition.x - button.Size.x / 2f >= -view.Size.x / 2f - unit * 0.5f), "an action stands left of the narrow bar" + where);
                    Require(view.Size.y > columnTop - view.Foot + unit, "the panel does not stand over the column" + where);
                }
                else Require(view.Size.x > column + RoomType.Units(RoomGeometry.PanelDp), "the ordinary bar is not the pair's width" + where);

                Require(!view.PassedLargest, "the bar laid past its largest footprint" + where + ": " + (view.Size / unit).ToString("0") + " dp against "
                    + (RoomBarView.LargestAt(view.LaidPanelDp) / unit).ToString("0"));
                if (state == "57-destructive-confirm" && buttons.FirstOrDefault(button => button.Words.text.Contains("Forget Computer")) is RoomButton yes)
                {
                    Require(yes.RimPainted && yes.RimNow == Halcyonic.XR.UI.RoomSurface.Hex(RoomButton.DestructiveRimColour),
                        "the destructive Yes does not stand in the destructive red's rim");
                }
                var here = new List<string>();
                foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    label.ForceMeshUpdate(true);
                    var said = label.GetParsedText();
                    if (said.Trim().Length == 0) continue;
                    here.Add(said);
                    Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut" + where);
                    Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot" + where);
                }
                // What the reader gave on this page: the actions it stands (a confirmation's one way out is Cancel, never Close too).
                foreach (var words in bar.Column.Select(action => action.Words))
                    Require(here.Any(said => Words(said).Contains(Words(words))), "\"" + words + "\" is not drawn whole" + where + ": " + string.Join(" | ", here.Select(Words)));
                drawn.AddRange(here);
                if (at == 0) degrees = view.Size / unit / RoomGeometry.DpPerDegree;
            }
            Require(state != "57-destructive-confirm" || drawn.Any(said => said.Contains("Forget Computer")), "the destructive Yes is not drawn");
            foreach (var words in expected)
                Require(drawn.Any(said => Words(said).Contains(Words(words))), "\"" + words + "\" is not drawn whole on any page: " + string.Join(" | ", drawn.Select(Words)));
            Debug.Log("Halcyonic: parts: " + name + ": " + (narrow ? "narrow" : "ordinary") + " bar "
                + degrees.x.ToString("0.0", CultureInfo.InvariantCulture) + " by " + degrees.y.ToString("0.0", CultureInfo.InvariantCulture)
                + " degrees at text scale " + GlazeText.Scale.ToString("0.00", CultureInfo.InvariantCulture) + " in " + pages + (pages == 1 ? " page" : " pages")
                + "; drawn: " + string.Join(" | ", drawn.Select(Words)) + ".");
            return shot;
        }

        /// <summary>
        /// A card's state laid on the narrow form's panel, as wide as the column (JUDGE's live walk at Larger, 2026-11-13T2059Z:
        /// "Turn Finished" cut beside its mark): a state alone, a state under a name, and a long state, each whole at both sizes,
        /// none on a dot. Synthetic words only.
        /// </summary>
        private static PartShot NarrowStatusShot(PartShot shot, Transform root, Vector3 eyes, string name)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var cards = new[]
            {
                new RoomCard(status: new RoomCardStatus("Turn Finished", "The agent finished its turn.", RoomTone.Good)),
                new RoomCard(LineWords.Theirs("Kite Run"), status: new RoomCardStatus("Turn Finished", "The agent finished its turn.", RoomTone.Good)),
                new RoomCard(status: new RoomCardStatus("Waiting on the Computer", "It has not answered yet.", RoomTone.Attention)),
            };
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var width = RoomType.Units(RoomGeometry.ColumnDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp);
            var drawn = new List<string>();
            for (var at = 0; at < cards.Length; at++)
            {
                var view = RoomCardView.Create(plane);
                view.Show(cards[at], width);
                view.transform.localPosition = new Vector3((at - 1) * (width + RoomType.Units(40f)), 0f, 0f);
                Require(view.LaidWhole, "card " + (at + 1) + " does not stand whole at the column's width");
                foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    label.ForceMeshUpdate(true);
                    var said = label.GetParsedText();
                    if (said.Trim().Length == 0) continue;
                    drawn.Add(string.Join(" ", said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
                    Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut at the column's width");
                    Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot");
                }
                foreach (var part in new[] { cards[at].Title?.Text, cards[at].Status?.Title, cards[at].Status?.Line }.OfType<string>())
                    Require(drawn.Contains(part), "\"" + part + "\" is not drawn whole: " + string.Join(" | ", drawn));
            }
            Place(plane, eyes, 0f, shot.Elevation);
            Debug.Log("Halcyonic: parts: " + name + ": " + (width / RoomType.Units(1f)).ToString("0", CultureInfo.InvariantCulture) + " dp cards drawn: " + string.Join(" | ", drawn) + ".");
            return shot;
        }

        /// <summary>
        /// A frame's choices as a card's pressed rows (the lead's 2026-11-13T21:11Z, pickers in the panel): three colours, one
        /// chosen, each with its disc; two answers of several, one chosen, each with its squircle. Measured at the floor's width
        /// and the full panel's: every name whole, one mark a row in the tile's place, the chosen ones filled with their check,
        /// no chevron on an answer, nothing on a dot. Synthetic words only.
        /// </summary>
        private static PartShot AnswerRowsShot(PartShot shot, Transform root, Vector3 eyes, string name)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            KitControl Choose(string key) => new KitControl(KitPlace.Step, new MenuFrame("Character Colour", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close))),
                "answer-rows-choose", key, key, null, true, null, false);
            var swatches = new[] { 0x8A93A6, 0x5FB3A5, 0xE0703A };
            var colours = new[] { "Original Colour", "Sea Glass", "Ember" }
                .Select((each, at) => new RoomListRow(RoomKind.None, LineWords.Ours(each), press: Choose(each), chosen: at == 1, mark: AnswerMark.Single, swatch: swatches[at])).ToList();
            var several = new[] { "Run Tests", "Check Types" }
                .Select((each, at) => new RoomListRow(RoomKind.Task, LineWords.Ours(each), press: Choose(each), chosen: at == 0, mark: AnswerMark.Multiple)).ToList();
            var cards = new[] { new RoomCard(LineWords.Ours("Character Colour"), list: colours), new RoomCard(LineWords.Ours("Before Finishing"), list: several) };
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var widths = new[] { PlaneWidth.FloorDp(GlazeText.Scale), RoomGeometry.PanelDp };
            var drawn = new List<string>();
            var placed = 0;
            foreach (var panelDp in widths)
                foreach (var card in cards)
                {
                    var width = RoomType.Units(panelDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp);
                    var view = RoomCardView.Create(plane);
                    view.Show(card, width);
                    view.transform.localPosition = new Vector3((placed - 1.5f) * (width + RoomType.Units(40f)), 0f, 0f);
                    placed++;
                    var where = " (" + panelDp.ToString("0", CultureInfo.InvariantCulture) + " dp panel)";
                    Require(view.LaidWhole, "the card " + card.Title!.Text + " does not stand whole" + where);
                    var rows = card.List!;
                    Require(view.Plates.Count(each => each.What.StartsWith("answer mark", StringComparison.Ordinal)) == rows.Count,
                        "the card " + card.Title!.Text + " does not draw one answer mark a row" + where);
                    Require(view.Plates.Count(each => each.What == "answer mark chosen") == rows.Count(row => row.Chosen)
                        && view.Glyphs.Count(each => each.What == "answer chosen") == rows.Count(row => row.Chosen),
                        "the chosen answers are not filled with their check" + where);
                    Require(!view.Glyphs.Any(each => each.What == "row chevron"), "an answer carries a chevron" + where);
                    Require(view.Plates.Count(each => each.What == "row swatch") == rows.Count(row => row.Swatch != null), "a colour row does not show its colour" + where);
                    // One measure for the file and the reader (AUDIT's 22:06Z): each row measured alone on a view kept for measuring
                    // counts the lines the card laid it in.
                    var measurer = RoomCardView.Create(plane);
                    var measured = rows.Select(row => measurer.LinesOfListRow(row, width)).ToList();
                    measurer.gameObject.SetActive(false);
                    Require(view.ListLines.Count == rows.Count && measured.SequenceEqual(view.ListLines) && measured.All(lines => lines >= 1),
                        "a row measured alone is not the lines the card laid it in: " + string.Join(",", measured) + " against " + string.Join(",", view.ListLines) + where);
                    Require(!view.Plates.Any(each => each.What.StartsWith("row tile", StringComparison.Ordinal)), "an answer draws its kind's tile under its mark" + where);
                    foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    {
                        label.ForceMeshUpdate(true);
                        var said = label.GetParsedText();
                        if (said.Trim().Length == 0) continue;
                        drawn.Add(string.Join(" ", said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
                        Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut" + where);
                        Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot" + where);
                    }
                    foreach (var row in rows)
                        Require(drawn.Contains(row.Name.Text), "\"" + row.Name.Text + "\" is not drawn whole" + where);
                }
            Place(plane, eyes, 0f, shot.Elevation);
            Debug.Log("Halcyonic: parts: " + name + ": drawn: " + string.Join(" | ", drawn.Distinct()) + ".");
            return shot;
        }

        /// <summary>
        /// A card's pressed rows each holding four parts apart (SCREENS's 2026-11-13T1757Z ask, AUDIT's 17:56Z MUST): what the
        /// name is above it, the name, its value as the note, a quiet line under it. Every part drawn whole at both sizes, none on
        /// a dot. Synthetic names only.
        /// </summary>
        private static PartShot RowLabelShot(PartShot shot, Transform root, Vector3 eyes, string name)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var rows = new[] { ("5-Hour Window", "Ember App", "Up to 60% Left", "Seen 2 minutes ago. Resets at 18:40."),
                    ("Weekly Window", "Ember App", "Up to 85% Left", "Seen 2 minutes ago. Resets on Monday."),
                    ("Daily Window", "Lantern App", "Up to 12% Left", "Seen 1 hour ago. Resets not known.") }
                .Select(each => new RoomListRow(RoomKind.None, LineWords.Theirs(each.Item2), each.Item3, under: LineWords.Ours(each.Item4), label: each.Item1,
                    press: new KitControl(KitPlace.Route, new MenuFrame("Usage", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close))), "row-label-open", each.Item1, each.Item2, null, true, null, false)))
                .ToList();
            var card = new RoomCard(LineWords.Ours("Usage"), list: rows);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var width = RoomType.Units(RoomGeometry.PanelDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp);
            var view = RoomCardView.Create(plane);
            // The card laid from its top left, centred on the eyes' axis, so no word is read at a slant (F112: its far words read 84 percent).
            var tall = view.Show(card, width);
            view.transform.localPosition = new Vector3(-width / 2f, tall / 2f, 0f);
            Place(plane, eyes, 0f, shot.Elevation);
            Require(view.LaidWhole, "the card does not stand whole");
            var drawn = new List<string>();
            var capitals = new List<string>();
            foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                label.ForceMeshUpdate(true);
                var said = label.GetParsedText();
                if (said.Trim().Length == 0) continue;
                var line = string.Join(" ", said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                drawn.Add(line);
                if ((label.fontStyle & TMPro.FontStyles.UpperCase) != 0) capitals.Add(line);
                Require(!label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut");
                Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot");
            }
            foreach (var row in rows)
            {
                foreach (var part in new[] { row.Label!, row.Name.Text, row.Under!.Text })
                    Require(drawn.Contains(part), "\"" + part + "\" is not drawn as a line of its own: " + string.Join(" | ", drawn));
                // The note is our state or value word, drawn in the label role (the one language): exactly its words in capitals, by the
                // face's capitals style; a note that names a file or a folder keeps its own case (RoomListRow.NoteInLabelRole).
                var note = RoomListRow.NoteInLabelRole(row.Note) ? row.Note!.ToUpperInvariant() : row.Note!;
                Require(drawn.Contains(note), "\"" + note + "\" is not drawn as a line of its own: " + string.Join(" | ", drawn));
                Require(RoomListRow.NoteInLabelRole(row.Note) == capitals.Contains(note), "\"" + row.Note + "\" is drawn in the wrong case's style");
                // Never a name in capitals: a pooled label once in the label role gives its capitals up (AUDIT2's 03:40Z pooled case).
                // A capitals line may be drawn uppercased, so a part matches it in any case; a part already all capitals cannot tell.
                foreach (var part in new[] { row.Label!, row.Name.Text, row.Under!.Text })
                    if (part.ToUpperInvariant() != part)
                        Require(!capitals.Exists(line => string.Equals(line, part, StringComparison.OrdinalIgnoreCase)),
                            "\"" + part + "\", a row's name, label or quieter line, is drawn in the label role's capitals");
            }
            Debug.Log("Halcyonic: parts: " + name + ": drawn: " + string.Join(" | ", drawn) + ".");
            return shot;
        }
    }
}
