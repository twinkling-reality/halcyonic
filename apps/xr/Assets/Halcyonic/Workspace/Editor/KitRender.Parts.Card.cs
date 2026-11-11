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
        private static readonly string[] CardStates = { "47-card-recap", "47-card-result", "47-card-changes", "47-card-checks", "47-card-fixing", "47-card-usage", "47-card-agent", "47-card-history", "47-card-needs-you", "47-card-share", "47-card-reading" };

        /// <summary>A row of History picked from, as DEVICE's moment hands it: its own control on a page of its own.</summary>
        private static KitControl CardRowControl(string key)
        {
            var frame = new MenuFrame("History", new Footer(new Prompt(Footer.Close, "Back", GlazeIcon.Back, PromptKind.Close)));
            return new KitControl(KitPlace.Choice, frame, "history-pick", key, key, null, available: true, reason: null, holds: false);
        }

        /// <summary>
        /// A code 29 cells a side shaped as a real one: three finder squares, the timing rows, and a fixed scatter. Only a
        /// picture for the check, which reads every cell back from the pixels; the kit never makes a code.
        /// </summary>
        internal static RoomCode CardCode()
        {
            const int side = 29;
            var dark = new bool[side * side];
            void Finder(int left, int top)
            {
                for (var row = 0; row < 7; row++)
                for (var column = 0; column < 7; column++)
                {
                    var ring = Math.Max(Math.Abs(row - 3), Math.Abs(column - 3));
                    dark[(top + row) * side + left + column] = ring != 2;
                }
            }
            for (var row = 0; row < side; row++)
            for (var column = 0; column < side; column++)
            {
                var hash = unchecked((uint)(row * 73856093) ^ (uint)(column * 19349663) ^ 0x9E3779B9u);
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;
                dark[row * side + column] = row == 6 || column == 6 ? (row + column) % 2 == 0 : (hash & 1u) == 1u;
            }
            Finder(0, 0);
            Finder(side - 7, 0);
            Finder(0, side - 7);
            // The light ring round each finder square.
            for (var index = 0; index < 8; index++)
            {
                dark[7 * side + index] = dark[index * side + 7] = false;
                dark[7 * side + side - 1 - index] = dark[index * side + side - 8] = false;
                dark[(side - 8) * side + index] = dark[(side - 1 - index) * side + 7] = false;
            }
            return new RoomCode(side, dark);
        }

        /// <summary>
        /// A change to a file as SCREENS's reader pages it: lines kept, one removed and the one that took its place, a line as
        /// long as a row holds at this text size wrapped onto a continued row, a gap, and an indent kept as a count.
        /// </summary>
        private static RoomReadingRow[] CardReading()
        {
            LineWords Theirs(string words) => LineWords.TheirName(words);
            var columns = RoomReading.Columns(GlazeText.Scale);
            const string longLine = "return points.map(point => point.lerp(kite.at, wind.pull)).filter(Boolean).slice(0, 40);";
            var first = longLine.Substring(0, columns - 4);
            var rest = longLine.Substring(columns - 4);
            var hung = rest.Substring(0, Math.Min(rest.Length, RoomReading.Columns(GlazeText.Scale, continued: true) - 4));
            return new[]
            {
                new RoomReadingRow(RoomLineKind.Same, 10, Theirs("export function tail(kite, wind) {")),
                new RoomReadingRow(RoomLineKind.Same, 11, Theirs("const points = [];"), indent: 2),
                new RoomReadingRow(RoomLineKind.Removed, 12, Theirs("points.push(kite.at);"), indent: 2),
                new RoomReadingRow(RoomLineKind.Added, 12, Theirs("points.push(kite.at, wind.at);"), indent: 2),
                new RoomReadingRow(RoomLineKind.Added, 13, Theirs("// streams behind the kite"), indent: 2),
                new RoomReadingRow(RoomLineKind.Added, 14, Theirs(first), indent: 4),
                new RoomReadingRow(RoomLineKind.Added, null, Theirs(hung), continued: true, indent: 4),
                RoomReadingRow.GapRow,
                new RoomReadingRow(RoomLineKind.Same, 38, Theirs("return points;"), indent: 2),
                new RoomReadingRow(RoomLineKind.Same, 39, Theirs("}")),
                new RoomReadingRow(RoomLineKind.Same, 40, null),
                new RoomReadingRow(RoomLineKind.Same, 41, Theirs("export default tail;")),
            };
        }

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
                "47-card-needs-you" => new RoomCard(Theirs("Tail"), Theirs("Should the tail stream left or right?"),
                    status: new RoomCardStatus("Needs You", "It asks you something below", RoomTone.Attention)),
                "47-card-share" => new RoomCard(status: new RoomCardStatus("Shared", "Scan the code to play", RoomTone.Good),
                    slots: new[] { new RoomSlot(RoomKind.Wifi, Ours("192.168.1.20:47970")) },
                    more: Ours("The game has changed since. They still get the last checked build."), code: CardCode()),
                "47-card-reading" => new RoomCard(Theirs("tail.js"), slots: new[] { new RoomSlot(RoomKind.Changed, Ours("Edited")), new RoomSlot(RoomKind.Added, Ours("4 Lines")) },
                    reading: CardReading(), more: Ours("Page 1 of 2")),
                "47-card-history" => new RoomCard(Ours("History"), Ours("Pick a moment to restore."), list: new[]
                {
                    new RoomListRow(RoomKind.Worked, Ours("After ") + Theirs("Tail") + Ours(" Worked"), "18:40", press: CardRowControl("c1")),
                    new RoomListRow(RoomKind.Started, Ours("Before ") + Theirs("Tail") + Ours(" Started"), "18:12", press: CardRowControl("c2")),
                    new RoomListRow(RoomKind.Worked, Ours("After ") + Theirs("Score") + Ours(" Worked"), "17:55", press: CardRowControl("c3")),
                }),
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
                // A file's own line may hold any character: only the card's own words are read for the separator.
                Require(view.ReadingShown.Contains(label) || !words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
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
            if (card.Code is RoomCode code)
            {
                // The code: its well square in the card's middle, the cells inside the kit's margin, the same share for every cell.
                var codeView = view.CodeShown;
                var well = view.Plates.Where(each => each.What == "code well").Select(each => each.Plate).ToList();
                Require(codeView != null && codeView.Code == code, "the card shows no code");
                Require(well.Count == 1, "the code has " + well.Count + " wells");
                if (codeView != null && well.Count == 1)
                {
                    var ground = well[0];
                    Require(Mathf.Abs(ground.Size.x - ground.Size.y) < slack && Mathf.Abs(ground.transform.localPosition.x - inner / 2f) < slack, "the code's well is not square in the card's middle");
                    var cell = ground.Size.x / (code.Side + 2 * RoomCode.MarginCells);
                    Require(Mathf.Abs(codeView.Side - cell * code.Side) < slack * 0.1f, "the code's cells do not stand inside a margin of " + RoomCode.MarginCells + " cells");
                    Require(ground.CornerRadius <= cell * RoomCode.MarginCells, "the well's corners cut into its margin");
                    Debug.Log("Halcyonic: parts: " + name + ": the code " + code.Side + " cells a side, its well " + (ground.Size.x / unit).ToString("0", CultureInfo.InvariantCulture)
                        + " dp across, a cell " + (GlazeTokens.DegreesOf(cell)).ToString("0.000", CultureInfo.InvariantCulture) + " degrees at a metre.");
                }
            }
            if (card.Reading.Count > 0)
            {
                // Each line's words at the card's text column plus their indent, a continued row hung in; one band per changed
                // row, one mark per changed line (not its continued rows), one hairline per gap; the longest row whole.
                var advance = RoomType.Scaled(RoomReading.TypeDp) * RoomReading.AdvanceEm;
                var texts = card.Reading.Where(row => row.Text != null && !row.Text.IsEmpty).ToList();
                Require(view.ReadingShown.Count == texts.Count, "the page draws " + view.ReadingShown.Count + " lines for " + texts.Count);
                for (var index = 0; index < Math.Min(texts.Count, view.ReadingShown.Count); index++)
                {
                    var row = texts[index];
                    var want = RoomType.Scaled(RoomReading.TextLeftDp(1f, row.Continued)) + row.Indent * advance;
                    Require(Mathf.Abs(view.ReadingShown[index].transform.localPosition.x - want) < slack, row.Text!.Text + " does not stand at its indent");
                }
                Require(view.Plates.Count(each => each.What == "reading band") == card.Reading.Count(row => row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed), "a changed row has no band");
                Require(view.Glyphs.Count(each => each.What == "reading mark") == card.Reading.Count(row => (row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed) && !row.Continued), "a changed line has no mark");
                Require(view.Plates.Count(each => each.What == "reading gap") == card.Reading.Count(row => row.Kind == RoomLineKind.Gap), "a gap has no hairline");
                Require(card.Reading.Count <= RoomReading.RowsAPage, "the page holds more rows than a page");
            }
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
            if (card.List.Any(row => row.Press != null))
            {
                // Each picked-from row: its chevron at the card's right, and a press target as wide as the card, its own control.
                var targets = view.RowTargets.ToList();
                Require(targets.Count == card.List.Count(row => row.Press != null), "the list draws " + targets.Count + " press targets for its picked-from rows");
                Require(targets.Select(each => each.Control.Key).SequenceEqual(card.List.Where(row => row.Press != null).Select(row => row.Press!.Key)), "a row's target raises another row's control");
                Require(view.Glyphs.Count(each => each.What == "row chevron") == targets.Count, "a picked-from row carries no chevron");
                Require(targets.All(each => each.Press.Size.x >= inner - slack), "a row's target is narrower than the card");
            }
            if (card.List.Count > 0)
            {
                // Each row's tile at the card's left, its name 14 dp after the tile, its note ending at the card's right.
                var tiles = view.Plates.Where(each => each.What.StartsWith("row tile", StringComparison.Ordinal)).ToList();
                Require(tiles.Count == card.List.Count, "the list draws " + tiles.Count + " tiles for " + card.List.Count + " rows");
                Require(tiles.All(each => Mathf.Abs(each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f) < slack), "a row's tile does not stand at the card's left");
                foreach (var row in card.List)
                {
                    // Leaning outside words stand a hair apart from ours (GlazeRuns), so a name is matched by its words, not its spaces.
                    string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    var nameLabel = view.Shown.FirstOrDefault(label => Words(label.GetParsedText()) == Words(row.Name.Text));
                    Require(nameLabel != null, row.Name.Text + " is not shown");
                    if (nameLabel != null)
                        Require(nameLabel.transform.localPosition.x + nameLabel.textBounds.min.x >= RoomType.Scaled(RoomCardView.RowTileDp + RoomCardView.RowGapDp) - unit * 1.5f,
                            row.Name.Text + " touches its tile");
                    if (row.Note == null) continue;
                    var noteLabel = view.Shown.FirstOrDefault(label => label.GetParsedText() == row.Note);
                    if (noteLabel != null)
                    {
                        var right = inner - (row.Press != null ? RoomType.Scaled(RoomCardView.ChevronDp + RoomCardView.ChevronGapDp) : 0f);
                        Require(Mathf.Abs(noteLabel.transform.localPosition.x + RoomCursor.WordsWidth(noteLabel, 0f) - right) < unit * 0.6f, row.Note + " does not end at the card's right, or before its chevron");
                    }
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
                // Every cell of the code, read back at its middle over the bright room and the dark: dark where the host's cell
                // is, light elsewhere, the margin light all round. Proves the cells stand row by row from the top left.
                if (view.CodeShown is RoomCodeView shownCode && shownCode.Code is RoomCode cells)
                {
                    var wrong = 0;
                    var first = "";
                    var lightestDark = 0;
                    var darkestLight = 255;
                    var lit = new Color32[][] { dark, bright };
                    for (var row = -2; row < cells.Side + 2; row++)
                    for (var column = -2; column < cells.Side + 2; column++)
                    {
                        var inside = row >= 0 && column >= 0 && row < cells.Side && column < cells.Side;
                        var wantDark = inside && cells.IsDark(column, row);
                        var at = shownCode.transform.TransformPoint(new Vector3((column + 0.5f) / cells.Side - 0.5f, 0.5f - (row + 0.5f) / cells.Side, 0f));
                        foreach (var pixels in lit)
                        {
                            var light = Light(At(pixels, at));
                            if (wantDark) lightestDark = Mathf.Max(lightestDark, light);
                            else darkestLight = Mathf.Min(darkestLight, light);
                            if (wantDark ? light > 80 : light < 190)
                            {
                                if (wrong == 0) first = "cell " + column + " across, " + row + " down reads " + light + " of 255, wanted " + (wantDark ? "dark" : "light");
                                wrong++;
                            }
                        }
                    }
                    if (wrong > 0) failures.Add("the code's cells do not read back from the pixels: " + wrong + " wrong, the first " + first);
                    Debug.Log("Halcyonic: parts: " + name + ": the code's darkest light cell reads " + darkestLight + ", its lightest dark cell " + lightestDark + " of 255.");
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
