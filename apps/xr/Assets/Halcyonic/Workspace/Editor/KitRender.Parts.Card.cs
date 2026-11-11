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
    /// The information panel as a card (<see cref="RoomCardView"/>, the addendum's A17) on the panel itself: New Project's
    /// recap as a head and slots; a result's slots; its changes as a list; its checks after the tries ran out, a step's
    /// pips spent; the check being fixed, a status with its pips; usage as two gauges; the agent's page. Each is measured
    /// (every block on the card's own left and right edges, no label cut, no word joined on a dot, a list's tile clear of
    /// its name and its note at the right edge) and read in the pixels (the title's words, each mark in its colour).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] CardStates = { "47-card-recap", "47-card-result", "47-card-changes", "47-card-checks", "47-card-fixing", "47-card-usage", "47-card-agent" };

        private static RoomCard CardOf(string state)
        {
            LineWords Theirs(string words) => LineWords.TheirName(words);
            LineWords Ours(string words) => LineWords.Ours(words);
            var tail = new[]
            {
                new RoomListRow(RoomKind.Changed, Theirs("kite.js"), "Edited", mono: true), new RoomListRow(RoomKind.Added, Theirs("tail.js"), "New", mono: true),
                new RoomListRow(RoomKind.Changed, Theirs("score.js"), "Edited", mono: true), new RoomListRow(RoomKind.Removed, Theirs("old-plan.md"), "Removed", mono: true),
            };
            return state switch
            {
                "47-card-recap" => new RoomCard(Theirs("Kite Run"), Theirs("Make a kite that follows my hand, and climbs when I raise it. In a new folder, kite-run."), RoomCardHead.Project,
                    slots: new[] { new RoomSlot(RoomKind.Starter, Ours("VR Game")), new RoomSlot(RoomKind.Agent, Theirs("Glimmer 30B")), new RoomSlot(RoomKind.Level, Ours("Never Ask")) }),
                "47-card-result" => new RoomCard(Theirs("Tail"), Theirs("I added a tail that streams behind the kite."), RoomCardHead.Picture,
                    slots: new[] { new RoomSlot(RoomKind.Passed, Ours("Passed")), new RoomSlot(RoomKind.ToAdd, Ours("4 Files")), new RoomSlot(RoomKind.Agent, Theirs("Glimmer 30B")) }),
                "47-card-changes" => new RoomCard(Ours("Changes"), Ours("What Tail did to the project"), list: tail),
                "47-card-checks" => new RoomCard(status: new RoomCardStatus("Build Failed", "Both tries ran out", RoomTone.Bad), steps: new[]
                {
                    new RoomCardStep("Types", RoomTone.Good), new RoomCardStep("Build", RoomTone.Bad, 2, 2), new RoomCardStep("Page", RoomTone.Skipped), new RoomCardStep("VR", RoomTone.Skipped),
                }),
                "47-card-fixing" => RoomBar.FixingCard(new Halcyonic.Contracts.AutoFixView
                    { State = Halcyonic.Contracts.AutoFixViewState.Fixing, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = 1, Cap = 2 })!,
                "47-card-usage" => new RoomCard(Ours("Usage"), gauges: new[] { new RoomGauge(Ours("Session"), 62, "Resets 18:40", RoomKind.Session), new RoomGauge(Ours("Week"), 38, "Resets Thu", RoomKind.Week) }),
                _ => new RoomCard(Theirs("Glimmer 30B"), Ours(AgentColumn.ModelWords), RoomCardHead.Agent),
            };
        }

        private static PartShot CardShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var card = CardOf(state);
            Require(!card.AnyDot, "the card joins facts on a dot: " + string.Join(" / ", card.Words()));
            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var inner = width - 2f * pad;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomCardView.Create(panel.transform);
            var tall = view.Show(card, inner);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Draw(size);
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);

            // The measures. Every label whole, inside the card's column; every tile and mark inside it; a block that spans
            // the card meets both its edges.
            var slack = unit * 0.05f;
            foreach (var label in view.Shown)
            {
                label.ForceMeshUpdate(true);
                var words = label.GetParsedText();
                Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !words.Contains("…"), "\"" + words + "\" is cut");
                Require(!words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
                var bounds = label.textBounds;
                var left = label.transform.localPosition.x + bounds.min.x;
                var right = label.transform.localPosition.x + bounds.max.x;
                Require(left >= -slack && right <= inner + slack, "\"" + words + "\" runs past the card's column: " + (left / unit).ToString("0.0") + " to " + (right / unit).ToString("0.0") + " dp");
            }
            foreach (var (plate, what) in view.Plates)
            {
                var x = plate.transform.localPosition.x;
                Require(x - plate.Size.x / 2f >= -slack && x + plate.Size.x / 2f <= inner + slack, what + " runs past the card's column");
            }
            float Left(string what) => view.Plates.Where(each => each.What.StartsWith(what, StringComparison.Ordinal)).Min(each => each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f);
            float Right(string what) => view.Plates.Where(each => each.What.StartsWith(what, StringComparison.Ordinal)).Max(each => each.Plate.transform.localPosition.x + each.Plate.Size.x / 2f);
            if (card.Slots.Count > 0)
                Require(Mathf.Abs(Left("slot")) < slack && Mathf.Abs(Right("slot") - inner) < slack, "the slots do not meet the card's two edges");
            if (card.Gauges.Count > 0)
            {
                Require(Mathf.Abs(Left("gauge tile")) < slack && Mathf.Abs(Right("gauge tile") - inner) < slack, "the gauges do not meet the card's two edges");
                var ends = view.Plates.Where(each => each.What == "gauge track").Select(each => each.Plate.transform.localPosition.x + each.Plate.Size.x / 2f).ToList();
                Require(ends.Count == card.Gauges.Count && ends.Max() - ends.Min() < slack, "the gauges' bars do not end at one point");
            }
            if (card.Steps.Count > 0)
            {
                Require(Left("step") > -slack && Right("step") < inner + slack, "the steps run past the card");
                Require(view.Plates.Count(each => each.What == "join") == card.Steps.Count - 1, "the steps are not joined one to the next");
                var used = card.Steps.Sum(step => step.TriesUsed);
                var pips = card.Steps.Sum(step => step.TriesOf);
                Require(view.Plates.Count(each => each.What == "pip used") == used && view.Plates.Count(each => each.What.StartsWith("pip", StringComparison.Ordinal) && each.What != "pip ring") == pips,
                    "the steps' pips are not their tries");
            }
            if (card.Status != null && card.TrackOf is int of)
                Require(view.Plates.Count(each => each.What.StartsWith("pip", StringComparison.Ordinal) && each.What != "pip ring") == of, "the status's pips are not its tries");
            if (card.List.Count > 0)
            {
                // Each row's tile at the card's left, its name 14 dp after the tile, its note ending at the card's right.
                var tiles = view.Plates.Where(each => each.What.StartsWith("row tile", StringComparison.Ordinal)).ToList();
                Require(tiles.Count == card.List.Count, "the list draws " + tiles.Count + " tiles for " + card.List.Count + " rows");
                Require(tiles.All(each => Mathf.Abs(each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f) < slack), "a row's tile does not stand at the card's left");
                foreach (var row in card.List)
                {
                    var nameLabel = view.Shown.FirstOrDefault(label => label.GetParsedText() == row.Name.Text);
                    Require(nameLabel != null, row.Name.Text + " is not shown");
                    if (nameLabel != null)
                        Require(nameLabel.transform.localPosition.x + nameLabel.textBounds.min.x >= RoomType.Scaled(RoomCardView.RowTileDp + RoomCardView.RowGapDp) - unit * 1.5f,
                            row.Name.Text + " touches its tile");
                    if (row.Note == null) continue;
                    var noteLabel = view.Shown.FirstOrDefault(label => label.GetParsedText() == row.Note);
                    if (noteLabel != null)
                        Require(Mathf.Abs(noteLabel.transform.localPosition.x + RoomCursor.WordsWidth(noteLabel, 0f) - inner) < unit * 0.6f, row.Note + " does not end at the card's right");
                }
            }

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                // Every label's words are in the pixels.
                foreach (var label in view.Shown)
                {
                    var bounds = label.textBounds;
                    var most = 0;
                    // Along the middle of its first line, where its letters stand.
                    for (var step = 0; step <= 60; step++)
                        most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 60f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                    if (most < 110) failures.Add("\"" + label.GetParsedText() + "\" is not in the pixels, the lightest reads " + most + " of 255");
                }
                // Every mark's ink on the middle of its shapes, apart from the ground at its box's corner (lighter on a dark well,
                // darker on a light disc): the farthest a middle's colour stands from that ground.
                foreach (var (glyph, what) in view.Glyphs)
                {
                    if (glyph.Glyph is not RoomGlyph shown) continue;
                    var ground = At(dark, glyph.transform.TransformPoint(new Vector3(0.48f, 0.48f, 0f)));
                    var apart = 0;
                    foreach (var shape in RoomGlyphs.Of(shown))
                    foreach (var middle in shape.Middles())
                    {
                        var pixel = At(dark, glyph.transform.TransformPoint(new Vector3(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid, 0f)));
                        apart = Mathf.Max(apart, Mathf.Max(Mathf.Abs(pixel.r - ground.r), Mathf.Max(Mathf.Abs(pixel.g - ground.g), Mathf.Abs(pixel.b - ground.b))));
                    }
                    if (apart < 40) failures.Add(what + "'s mark is not in the pixels: its ink stands " + apart + " of 255 from the ground beside it");
                }
                Debug.Log("Halcyonic: parts: " + name + ": a card " + (tall / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp tall, " + view.Shown.Count + " labels, "
                    + view.Plates.Count + " tiles and marks, " + view.Glyphs.Count + " glyphs.");
            };
            return shot;
        }
    }
}
