#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The Projects panel in the one language (FIELD's sheet, tile f1): the renders' synthetic portfolio's Projects frame through
    /// <see cref="RoomBar.OfFrame"/>, drawn by the room's own view. Measured (one glass panel and nothing beside it; HALORA in the
    /// label role over the title; the panel's Menu at its top right, clear of the title, the bar's own press; the heads PROJECT
    /// and STATE over the rows, the state's at the right; each row's state word in the label role; no rule or chevron in the
    /// table; every action in one row at the panel's foot, inside its glass, in the column's order, none lit) and read in the
    /// pixels (the label, the heads, the title, the Menu and every action). Synthetic names only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ProjectsStates = { "46-projects" };

        private static PartShot ProjectsShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            // The renders' synthetic portfolio, one project waiting on the person, as the room lists it live.
            var portfolio = WorkspaceRender.Portfolio(hostile: false, needsYouNow: true);
            var frame = ProjectsScreens.Projects(new ProjectsScreens.State
            {
                Overview = WorkOverview.Of(portfolio, new StageVisibility(), _ => true), Live = true, Now = DateTimeOffset.UnixEpoch, Zone = TimeZoneInfo.Utc,
                LineHeight = _ => 1d, AvailableHeight = 100d, TextSize = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard,
            }).WithSections(new MenuBar(MenuPlace.Projects, "Projects").Sections());
            var bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
            Require(bar?.Card != null, "the Projects frame gives the redesign no card");
            if (bar?.Card == null) return shot;
            Require(bar.ActionsAtFoot && bar.PanelMenu?.Control.Action == RoomBar.OpenMenu, "the Projects bar asks for no foot row or no Menu of its own");

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            view.Show(bar);
            view.SettleForRender();
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            var menu = view.PanelMenu;
            foreach (var button in menu == null ? buttons : buttons.Append(menu))
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);

            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var panelSize = view.Panel.Size;
            var panelCentre = (Vector2)view.Panel.transform.localPosition;
            var panelLeft = panelCentre.x - panelSize.x / 2f;
            var panelRight = panelCentre.x + panelSize.x / 2f;
            var panelTop = panelCentre.y + panelSize.y / 2f;
            var panelFoot = panelCentre.y - panelSize.y / 2f;
            Vector2 At(Transform part) => (Vector2)plane.InverseTransformPoint(part.position) - (Vector2)plane.InverseTransformPoint(view.transform.position);
            (float Left, float Right, float Top, float Bottom) Bounds(TMP_Text label)
            {
                label.ForceMeshUpdate(true);
                var box = label.textBounds;
                var origin = At(label.transform);
                return (origin.x + box.min.x, origin.x + box.max.x, origin.y + box.max.y, origin.y + box.min.y);
            }

            // One glass panel and nothing beside it: the actions are inside it.
            Require(view.Panel.gameObject.activeSelf && view.ActionsAtFoot, "the actions do not stand in the panel's foot row");
            Require(Mathf.Abs(view.Size.x - panelSize.x) < unit * 0.5f, "something stands beside the panel: the bar is " + (view.Size.x / unit).ToString("0") + " dp wide, its panel " + (panelSize.x / unit).ToString("0"));

            var card = view.Card;
            Require(card != null, "the panel holds no card");
            if (card == null) return shot;
            // HALORA over the title, in the label role.
            var label = card.LabelShown;
            Require(label != null && label.gameObject.activeSelf, "no label over the title");
            var title = card.Shown.FirstOrDefault(each => each != label && each.gameObject.activeSelf && string.Equals(each.GetParsedText().Trim(), RoomBar.ProjectsTitle, StringComparison.Ordinal));
            Require(title != null, "the title \"" + RoomBar.ProjectsTitle + "\" is not shown");
            if (label != null)
            {
                label.ForceMeshUpdate(true);
                Require(string.Equals(label.GetParsedText().Trim(), WorkspaceText.Product, StringComparison.OrdinalIgnoreCase) && (label.fontStyle & FontStyles.UpperCase) != 0
                    && label.characterSpacing > 0f && !label.isTextOverflowing, "the label is not \"" + WorkspaceText.Product + "\" in spaced capitals: \"" + label.GetParsedText() + "\"");
                if (title != null) Require(Bounds(label).Bottom > Bounds(title).Top - unit * 0.5f, "the label does not stand over the title");
            }
            // The Menu at the top right, inside the panel, clear of the title, the bar's own press and never lit.
            Require(menu != null && view.ControlOf(menu)?.Action == RoomBar.OpenMenu && !menu.Main, "the panel's Menu is not drawn as the bar's own quiet press");
            if (menu != null)
            {
                var words = Bounds(menu.Words);
                Require(words.Right <= panelRight - pad + unit && words.Top <= panelTop - pad + unit && words.Left > panelCentre.x,
                    "the Menu does not stand at the panel's top right, inside its pad");
                if (title != null) Require(words.Left > Bounds(title).Right + unit * 8f, "the Menu crowds the title");
                Require(string.Equals(menu.Words.GetParsedText().Trim(), RoomBar.MenuWords, StringComparison.Ordinal), "the Menu reads \"" + menu.Words.GetParsedText() + "\"");
            }
            // The heads over the rows, in the label role, the state's at the panel's words' right edge.
            Require(card.HeadsShown != null, "the list has no heads");
            if (card.HeadsShown is { } heads)
            {
                var (nameHead, noteHead) = heads;
                foreach (var head in new[] { nameHead, noteHead })
                {
                    head.ForceMeshUpdate(true);
                    Require((head.fontStyle & FontStyles.UpperCase) != 0 && head.characterSpacing > 0f && !head.isTextOverflowing, "the head \"" + head.GetParsedText() + "\" is not in spaced capitals");
                }
                Require(string.Equals(nameHead.GetParsedText().Trim(), RoomBar.ProjectHead, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(noteHead.GetParsedText().Trim(), RoomBar.StateHead, StringComparison.OrdinalIgnoreCase), "the heads are not PROJECT and STATE");
                Require(Mathf.Abs(Bounds(noteHead).Right - (panelRight - pad)) < unit * 2f, "the STATE head does not end at the panel's words' right edge");
                if (title != null) Require(Bounds(nameHead).Top < Bounds(title).Bottom, "the heads do not stand under the title");
            }
            // Each row's state word in the label role, at the right; no rule, no chevron, no mark before a name.
            foreach (var row in bar.Card.List)
            {
                var note = card.Shown.FirstOrDefault(each => each.gameObject.activeSelf && (each.fontStyle & FontStyles.UpperCase) != 0
                    && string.Equals(each.GetParsedText().Trim(), row.Note, StringComparison.OrdinalIgnoreCase) && each != card.HeadsShown?.Note);
                Require(note != null && note.characterSpacing > 0f, "the row \"" + row.Name.Text + "\" does not say its state \"" + row.Note + "\" in the label role");
                if (note != null) Require(Mathf.Abs(Bounds(note).Right - (panelRight - pad)) < unit * 2f, "\"" + row.Note + "\" does not stand at the right under STATE");
            }
            Require(card.RowTargets.Count() == bar.Card.List.Count, "the card lets " + card.RowTargets.Count() + " of " + bar.Card.List.Count + " projects be pressed");
            Require(!card.Plates.Any(each => each.What == "hairline") && !card.Glyphs.Any(each => each.What == "row chevron" || each.What.StartsWith("row ", StringComparison.Ordinal)),
                "the table draws a rule, a chevron or a mark: " + string.Join(", ", card.Plates.Select(each => each.What).Concat(card.Glyphs.Select(each => each.What))));
            // Every action in one row at the panel's foot, inside its glass, in the column's order, none lit at rest.
            Require(buttons.Count == bar.Column.Count, "the foot row draws " + buttons.Count + " of " + bar.Column.Count + " actions");
            var lowest = card.Shown.Where(each => each.gameObject.activeSelf).Select(each => Bounds(each).Bottom).DefaultIfEmpty(panelTop).Min();
            var lastRight = float.NegativeInfinity;
            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                var centre = At(button.transform);
                var words = Bounds(button.Words);
                Require(view.ControlOf(button) == bar.Column[index].Control, "the foot row's action " + index + " is not the column's " + index);
                Require(!button.Main, "\"" + button.Words.text + "\" is lit at rest");
                Require(Mathf.Abs(centre.y - At(buttons[0].transform).y) < unit * 0.5f, "\"" + button.Words.text + "\" leaves the foot row's line");
                Require(words.Left >= panelLeft + pad - unit && words.Right <= panelRight - pad + unit && words.Bottom >= panelFoot && words.Top < lowest,
                    "\"" + button.Words.text + "\" is not inside the panel's foot, under its words");
                Require(words.Left > lastRight, "\"" + button.Words.text + "\" is out of the column's order or overlaps the one before");
                lastRight = words.Right;
            }

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 Pixel(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                // Our quiet words (the label and the heads, white at 0.52) read about 190 in the pixels; the title and the actions are white.
                var quiet = new TMP_Text?[] { label, card.HeadsShown?.Name, card.HeadsShown?.Note };
                foreach (var each in quiet)
                    if (each != null && InkFault(each, world => Pixel(dark, world), 150) is string fault) failures.Add("\"" + each.GetParsedText() + "\" " + fault);
                foreach (var each in buttons.Select(button => button.Words).Append(title).Append(menu?.Words))
                    if (each != null && InkFault(each, world => Pixel(dark, world), 180) is string fault) failures.Add("\"" + each.GetParsedText() + "\" " + fault);
                Debug.Log("Halcyonic: parts: " + name + ": the Projects panel " + (panelSize.x / unit).ToString("0", CultureInfo.InvariantCulture) + " by "
                    + (panelSize.y / unit).ToString("0", CultureInfo.InvariantCulture) + " dp, " + bar.Card.List.Count + " rows ("
                    + string.Join(", ", bar.Card.List.Select(row => row.Name.Text + " " + row.Note)) + "), foot row: " + string.Join(", ", buttons.Select(button => button.Words.text)) + ".");
            };
            return shot;
        }
    }
}
