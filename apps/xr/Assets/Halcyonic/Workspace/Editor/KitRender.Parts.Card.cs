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
        private static readonly string[] CardStates = { "47-card-recap", "47-card-result", "47-card-changes", "47-card-checks", "47-card-fixing", "47-card-usage", "47-card-usage-here", "47-card-usage-hosted", "47-card-usage-unavailable", "47-card-usage-models", "47-card-usage-asking", "47-card-usage-review", "47-card-agent", "47-card-history", "47-card-needs-you", "47-card-share", "47-card-reading", "47-card-review-task", "47-card-review", "47-card-review-check", "47-card-recap-starter", "47-card-file-state", "47-card-slot-long", "47-card-tasks-all", "47-card-waiting" };

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
        /// long as a row holds at this text size wrapped onto a continued row, a gap, and an indent kept as a count. Every
        /// other line fits a row at the larger text size too (29 characters, its indent counted), as a screen wraps them.
        /// </summary>
        private static RoomReadingRow[] CardReading()
        {
            LineWords Theirs(string words) => LineWords.TheirName(words);
            const string longLine = "const trail = points.map(point => point.lerp(kite.at, wind.pull));";
            var rows = new List<RoomReadingRow>();
            // Every line wrapped as a screen wraps it, so none is cut at either text size.
            void Line(RoomLineKind kind, int number, string text, int indent)
            {
                var first = true;
                foreach (var part in CardWrap(text, indent))
                {
                    rows.Add(new RoomReadingRow(kind, first ? number : (int?)null, Theirs(part), continued: !first, indent: indent));
                    first = false;
                }
            }
            Line(RoomLineKind.Same, 10, "export function tail(k, w) {", 0);
            Line(RoomLineKind.Same, 11, "const points = [];", 2);
            Line(RoomLineKind.Removed, 12, "points.push(kite.at);", 2);
            Line(RoomLineKind.Added, 12, "points.push(kite.at, w.at);", 2);
            Line(RoomLineKind.Added, 13, "// streams behind the kite", 2);
            Line(RoomLineKind.Added, 14, longLine, 2);
            rows.Add(RoomReadingRow.Gap(23));
            Line(RoomLineKind.Same, 38, "return trail;", 2);
            Line(RoomLineKind.Same, 39, "}", 0);
            Line(RoomLineKind.Same, 40, "export default tail;", 0);
            return rows.Take(RoomReading.RowsAPage).ToArray();
        }

        /// <summary>
        /// A line wrapped as a screen wraps it (RoomReading's rule): to the columns a row holds at this text size, its indent
        /// counted, at a space or after a comma, else before a dot or a bracket, never inside an operator or a name; every
        /// part kept.
        /// </summary>
        private static List<string> CardWrap(string line, int indent)
        {
            var parts = new List<string>();
            var rest = line;
            var continued = false;
            while (rest.Length > 0)
            {
                var room = RoomReading.Columns(GlazeText.Scale, continued) - indent;
                if (rest.Length <= room) { parts.Add(rest); break; }
                // At a space or after a comma first; only where a row holds neither, before a dot or a bracket after a name.
                var cut = -1;
                for (var at = room; at > 0 && cut < 0; at--)
                    if (rest[at - 1] == ' ' || rest[at - 1] == ',') cut = at;
                for (var at = room; at > 0 && cut < 0; at--)
                    if ((rest[at] == '.' || rest[at] == '(') && char.IsLetterOrDigit(rest[at - 1])) cut = at;
                if (cut <= 0) cut = room;
                parts.Add(rest.Substring(0, cut).TrimEnd());
                rest = rest.Substring(cut).TrimStart();
                continued = true;
            }
            return parts;
        }

        /// <summary>A review before Start whose first task pages and names a web address, so its warning stands.</summary>
        private static NewWorkReview CardReview() => new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "Glimmer 30B", "On This Computer", "glimmer-30b-q4",
            string.Join(" ", Enumerable.Repeat("Make a kite that follows my hand, climbs when I raise it, and dips when I lower it.", 4))
            + " Use the wind from https://example.com/wind.json to pull it.", folder: "kite-run", neverAsk: false);

        /// <summary>Two windows read, as the usage card's gauges take them.</summary>
        private static IReadOnlyList<UsageLeftRow> CardUsage() => new[]
        {
            new UsageLeftRow(LineWords.Ours(UsageLeftPresenter.FiveHourWindow), "At most 62% left", 62, resets: "18:40", window: UsageLeftPresenter.FiveHourWindow),
            new UsageLeftRow(LineWords.Ours(UsageLeftPresenter.Weekly), "At most 38% left", 38, resets: "Thursday", window: UsageLeftPresenter.Weekly),
        };

        /// <summary>The host's list of where the assistant may run, as it gives it: this computer's model, a hosted one, one whose key was refused.</summary>
        private static IReadOnlyList<AssistantOption> CardModels() => new[]
        {
            new AssistantOption(0, "This Computer's Model", "glimmer-30b-q4", theirs: false, choosable: true),
            new AssistantOption(1, "muse-wonder-1.0", "On Tidewater, Costs Money", theirs: true, choosable: true),
            new AssistantOption(2, "orbit-large-2", "Fernhill refused its key", theirs: true, choosable: false),
        };

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
                "47-card-recap" => new RoomCard(Theirs("Kite Run"), Theirs("Make a kite that follows my hand, and climbs when I raise it."), RoomCardHead.Project,
                    slots: new[] { new RoomSlot(RoomKind.Starter, Ours("VR Game")), new RoomSlot(RoomKind.Agent, Theirs("Glimmer 30B")), new RoomSlot(RoomKind.Level, Ours("Never Ask")) }),
                "47-card-result" => new RoomCard(Theirs("Tail"), Theirs("I added a tail that streams behind the kite."), RoomCardHead.Picture,
                    slots: new[] { new RoomSlot(RoomKind.Passed, Ours("Passed")), new RoomSlot(RoomKind.ToAdd, Ours("4 Files")), new RoomSlot(RoomKind.Agent, Theirs("Glimmer 30B")) }),
                "47-card-changes" => new RoomCard(Ours("Changes"), Ours("What Tail did to the project"), list: tail),
                "47-card-checks" => new RoomCard(status: new RoomCardStatus("Build Failed", "Both tries ran out", RoomTone.Bad), steps: new[]
                {
                    new RoomCardStep("Types", RoomTone.Good), new RoomCardStep("Build", RoomTone.Bad, 2, 2), new RoomCardStep("Page", RoomTone.Skipped), new RoomCardStep("VR", RoomTone.Skipped),
                }),
                "47-card-fixing" => RoomBar.FixingCard(new Halcyonic.Contracts.AutoFixView
                    {
                        State = Halcyonic.Contracts.AutoFixViewState.Fixing, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = 1, Cap = 2,
                        FailedSteps = new List<Halcyonic.Contracts.AutoFixViewFailedStepsItem> { Halcyonic.Contracts.AutoFixViewFailedStepsItem.Build },
                    })!,
                "47-card-usage" => new RoomCard(Ours("Usage"), gauges: new[] { new RoomGauge(Ours("Session"), 62, "Resets 18:40", RoomKind.Session), new RoomGauge(Ours("Week"), 38, "Resets Thu", RoomKind.Week) }),
                // The assistant's row under the gauges: on this computer, on a provider's hosted model with a long id, and unable to run.
                "47-card-usage-here" => RoomPanelCards.Usage(CardUsage(), RoomPanelCards.Assistant("glimmer-30b-q4", null, null, CardRowControl("use-model")))!,
                "47-card-usage-hosted" => RoomPanelCards.Usage(CardUsage(), RoomPanelCards.Assistant("muse-wonder-1.0-preview", "Tidewater", null, CardRowControl("use-model")))!,
                "47-card-usage-unavailable" => RoomPanelCards.Usage(CardUsage(), RoomPanelCards.Assistant(null, null, "Tidewater refused its key. Give it a new key on this computer.", CardRowControl("use-model")))!,
                // Use Model, the row pressed: the host's list (this computer's model chosen, a hosted one, one whose key was refused),
                // the card while the computer is asked, and the one review a hosted pick waits on.
                "47-card-usage-models" => UsageColumn.ModelsCard(CardModels(), new AssistantRow("glimmer-30b-q4", null, null), null, option => CardRowControl("model-" + option.Index)),
                "47-card-usage-asking" => UsageColumn.AskingCard(UsageColumn.Asking, CardModels()[1]),
                "47-card-usage-review" => ReviewCard.Card(UsageColumn.ReviewOf(new Halcyonic.Contracts.WorkspaceRequestStepProposal
                    {
                        CommandType = Halcyonic.Contracts.CommandType.AssistantModelChoose,
                        WhatHappens = "From now on the assistant answers on this model, and Tidewater charges you money for each request.",
                        Review = "0193b3a0-review-digest",
                        // The host's own words on the step (LOOP's 8c6895ce7 and 89984589a), as the control plane gives them for a hosted pick.
                        ConfirmWords = "Use This Model",
                        Facts = new System.Collections.Generic.List<Halcyonic.Contracts.ActionReviewFact>
                        {
                            new Halcyonic.Contracts.ActionReviewFact { Kind = Halcyonic.Contracts.ReviewKind.Words, Value = "muse-wonder-1.0", Theirs = true },
                            new Halcyonic.Contracts.ActionReviewFact { Kind = Halcyonic.Contracts.ReviewKind.Words, Value = "Tidewater", Theirs = true },
                        },
                        Warnings = new System.Collections.Generic.List<Halcyonic.Contracts.ActionReviewWarning>
                        {
                            new Halcyonic.Contracts.ActionReviewWarning { Code = "costs_money", Line = "You pay for it on your own Tidewater account, through the access key from that account you added on this computer" },
                            new Halcyonic.Contracts.ActionReviewWarning { Code = "price_unknown", Line = "Halcyonic does not know the price, so it cannot say how much a request costs or show a total yet" },
                            new Halcyonic.Contracts.ActionReviewWarning { Code = "sent_to_provider", Line = "Your requests, and what the assistant reads from your projects to answer them, are sent to Tidewater" },
                            new Halcyonic.Contracts.ActionReviewWarning { Code = "free_here", Line = "This Computer's Model is free: nobody charges you for its requests" },
                        },
                    }, CardModels()[1]), AssistantPlace.UseModelTitle),
                "47-card-needs-you" => new RoomCard(Theirs("Tail"), Theirs("Should the tail stream left or right?"),
                    status: new RoomCardStatus("Needs You", "Waiting on your answer", RoomTone.Attention)),
                "47-card-share" => new RoomCard(Theirs("Kite Run"), status: new RoomCardStatus("Shared", "Scan the code to play", RoomTone.Good),
                    slots: new[] { new RoomSlot(RoomKind.Wifi, Ours("192.168.1.20:47970"), mono: true) },
                    more: Ours("Players get the last checked build"), code: CardCode()),
                // kite.js as the Changes list has it, edited: three lines added, one removed, each count by its own mark.
                "47-card-reading" => new RoomCard(Theirs("kite.js"), slots: new[]
                    {
                        new RoomSlot(RoomKind.Changed, Ours("Edited")), new RoomSlot(RoomKind.Added, Ours("3 Lines")), new RoomSlot(RoomKind.Removed, Ours("1 Line")),
                    },
                    reading: CardReading(), more: Ours("Page 1 of 2"), titleMono: true),
                // The review before Start, its first task too long to stand whole: the second of its pages, with a note to check.
                // The review before Start: the second page of its long first task, and its last part, what else is sent, with a note to check.
                "47-card-review-task" => RoomReview.Card(CardReview(), part: 1, taskPages: 2),
                "47-card-review" => RoomReview.Card(CardReview(), part: 2, taskPages: 2),
                "47-card-review-check" => RoomReview.Card(CardReview(), part: 3, taskPages: 2),
                // A new folder from a starter: four facts, two rows of two.
                "47-card-recap-starter" => new RoomCard(Theirs("Kite Run"), Theirs("Make a kite that follows my hand."), RoomCardHead.Project, slots: new[]
                {
                    new RoomSlot(RoomKind.Folder, Theirs("kite-run")), new RoomSlot(RoomKind.Starter, Ours("VR Game")),
                    new RoomSlot(RoomKind.Agent, Theirs("Glimmer 30B")), new RoomSlot(RoomKind.Level, Ours("Ask First")),
                }),
                // A value wider than the card, as a long web address to check: whole, wrapped in its own well, never cut.
                "47-card-slot-long" => new RoomCard(Theirs("Kite Run"), status: new RoomCardStatus(RoomReview.CheckTitle, RoomReview.CheckAddressLine, RoomTone.Attention, RoomKind.Failed),
                    slots: new[] { new RoomSlot(RoomKind.Link, Theirs("https://assets.example.com/weather/v2/regions/coastal/wind-speeds-hourly.json"), mono: true) }),
                // A request waiting on the person, where they stand: the card itself is the way in, in the moment's accent.
                "47-card-waiting" => new RoomCard(Theirs("Tail"), status: new RoomCardStatus("Needs You", "The agent asks to run a command", RoomTone.Attention, RoomKind.Waiting),
                    press: CardRowControl("open-request")),
                // All work's tasks: each row its project under its name, a long instruction of the person's own cut at a word.
                "47-card-tasks-all" => new RoomCard(Ours("Tasks"), list: new[]
                {
                    new RoomListRow(RoomKind.Task, Theirs("Tail"), "Needs You", RoomTone.Attention, press: CardRowControl("t1"), under: Theirs("Kite Run")),
                    new RoomListRow(RoomKind.Task, Theirs("Score"), "Working", RoomTone.Working, press: CardRowControl("t2"), under: Theirs("Kite Run")),
                    new RoomListRow(RoomKind.Waiting, Theirs("Make the kite's tail longer, and let it ripple in the wind as it climbs and dips with my hand."), "Next",
                        press: CardRowControl("t3"), under: Theirs("Storefront"), excerpt: true),
                }),
                // A file's state with no line: its own mark, its title centred on it.
                "47-card-file-state" => new RoomCard(Theirs("sky.png"), status: new RoomCardStatus("Not Text", null, RoomTone.Quiet), titleMono: true),
                "47-card-history" => new RoomCard(Ours("History"), Ours("Pick a moment to restore."), list: new[]
                {
                    new RoomListRow(RoomKind.Worked, Ours("After ") + Theirs("Tail") + Ours(" Worked"), "18:40", press: CardRowControl("c1")),
                    new RoomListRow(RoomKind.Started, Ours("Before ") + Theirs("Tail") + Ours(" Started"), "18:12", press: CardRowControl("c2")),
                    new RoomListRow(RoomKind.Worked, Ours("After ") + Theirs("Score") + Ours(" Worked"), "17:55", press: CardRowControl("c3")),
                }),
                _ => new RoomCard(Theirs("Glimmer 30B"), Ours(AgentColumn.ModelWords), RoomCardHead.Agent),
            };
        }

        private static PartShot CardShot(string state, Transform root, Vector3 eyes, string name) => CardShot(CardOf(state), root, eyes, name, state);

        /// <summary>A card given, measured and read as every card state is (SCREENS's file reading's states use it, with no kit state).</summary>
        private static PartShot CardShot(RoomCard card, Transform root, Vector3 eyes, string name, string state = "")
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            Require(!card.AnyDot, "the card joins facts on a dot: " + string.Join(" / ", card.Words()));
            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var inner = width - 2f * pad;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomCardView.Create(panel.transform);
            var reviewPages = 0;
            if (state.StartsWith("47-card-review", StringComparison.Ordinal))
            {
                // As XR's flow does: part 0 shown first measures the task's pages at this width and size; then the part is shown.
                view.Show(RoomReview.Card(CardReview(), part: 0, taskPages: 1), inner);
                reviewPages = view.DescriptionPages;
                card = RoomReview.Card(CardReview(), part: state == "47-card-review" ? reviewPages : state == "47-card-review-check" ? reviewPages + 1 : 1, taskPages: reviewPages);
            }
            var tall = view.Show(card, inner);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Ring = view.PanelRing;
            panel.Draw(size);
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);

            // The measures. Every label whole, inside the card's column; every tile and mark inside it; a block that spans
            // the card meets both its edges.
            var slack = unit * 0.05f;
            Require(view.LaidWhole, "the card does not say it stands whole, though it is shown");
            Require((card.Press != null) == (view.CardTarget != null), card.Press != null ? "a card that is the way in takes no press" : "a card that only says takes a press");
            if (card.DescriptionPage is int asked)
            {
                var paged = view.PagedDescription;
                Require(paged != null && view.DescriptionPages >= 2, "a long description makes " + view.DescriptionPages + " pages");
                if (paged != null)
                {
                    Require(paged.pageToDisplay == asked + 1, "the description shows page " + paged.pageToDisplay + ", not " + (asked + 1));
                    if (reviewPages > 0) Require(card.More?.Text == RoomReview.PartOf(asked, RoomReview.Parts(CardReview(), view.DescriptionPages)), "the foot line does not name this part");
                    var info = paged.textInfo.pageInfo[paged.pageToDisplay - 1];
                    Require(info.firstCharacterIndex <= 0 || char.IsWhiteSpace(paged.textInfo.characterInfo[info.firstCharacterIndex - 1].character), "a page starts inside a word");
                }
            }
            if (state == "47-card-recap")
            {
                // A card that cannot stand whole says so: a description far past its four lines is cut, and LaidWhole is false.
                var probe = RoomCardView.Create(panel.transform);
                probe.Show(new RoomCard(LineWords.TheirName("Kite Run"), LineWords.TheirName(string.Join(" ", Enumerable.Repeat("a kite that climbs when I raise my hand", 30)))), inner);
                Require(!probe.LaidWhole, "a card cut short still says it stands whole");
                probe.Hide();
                Require(!probe.LaidWhole, "a hidden card says it stands whole");
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
            foreach (var label in view.Shown)
            {
                label.ForceMeshUpdate(true);
                var words = label.GetParsedText();
                // A paged description runs on past its page by design (its page breaks between lines); every other label is whole.
                // An excerpt row's outside words show their lead cut at a word, the ellipsis theirs; nothing else is ever cut.
                var excerpt = view.Excerpts.Contains(label);
                Require(label.textInfo.characterCount > 0 && (label == view.PagedDescription || !label.isTextOverflowing) && (excerpt || !words.Contains("…")), "\"" + words + "\" is cut");
                if (excerpt) Require(words.EndsWith("…", StringComparison.Ordinal) && !words.EndsWith(" …", StringComparison.Ordinal), "\"" + words + "\" is an excerpt not ended at a word");
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
                    var shown = view.ReadingShown[index];
                    Require(Mathf.Abs(shown.transform.localPosition.x - want) < slack, row.Text!.Text + " does not stand at its indent");
                    Require(shown.transform.localPosition.x + RoomCursor.WordsWidth(shown, 0f) <= inner - RoomType.Scaled(RoomReading.RightPadDp) + slack, row.Text.Text + " runs into the room kept at the right");
                }
                // One band per run of rows of one change, so no seam parts them.
                var runs = card.Reading.Where((row, index) => (row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed) && (index == 0 || card.Reading[index - 1].Kind != row.Kind)).Count();
                Require(view.Plates.Count(each => each.What == "reading band") == runs, "the page draws " + view.Plates.Count(each => each.What == "reading band") + " bands for " + runs + " changed runs");
                Require(view.Glyphs.Count(each => each.What == "reading mark") == card.Reading.Count(row => (row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed) && !row.Continued), "a changed line has no mark");
                Require(view.Plates.Count(each => each.What == "reading gap") == card.Reading.Count(row => row.Kind == RoomLineKind.Gap), "a gap has no hairline");
                foreach (var gap in card.Reading.Where(row => row.Kind == RoomLineKind.Gap && row.Number is int))
                    Require(view.Shown.Any(label => label.GetParsedText() == RoomReading.Skipped(gap.Number!.Value)), "a gap does not say the lines it skips");
                Require(view.Plates.Where(each => each.What == "reading gap").All(each => Mathf.Abs(each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f) < slack && Mathf.Abs(each.Plate.Size.y - unit) < slack * 0.2f),
                    "a gap's hairline does not start at the bands' edge, one dp tall");
                Require(card.Reading.Count <= RoomReading.RowsAPage, "the page holds more rows than a page");
            }
            if (card.Slots.Count == 4)
            {
                // Two rows of two, each row meeting both edges, the second on the first's columns.
                var tiles = view.Plates.Where(each => each.What.StartsWith("slot ", StringComparison.Ordinal)).Select(each => each.Plate).ToList();
                Require(tiles.Count == 4, "a card of four slots draws " + tiles.Count);
                if (tiles.Count == 4)
                    Require(Mathf.Abs(tiles[0].transform.localPosition.x - tiles[2].transform.localPosition.x) < slack && Mathf.Abs(tiles[1].transform.localPosition.x - tiles[3].transform.localPosition.x) < slack
                        && tiles[0].transform.localPosition.y > tiles[2].transform.localPosition.y + slack, "four slots do not stand as two rows of two on one grid");
            }
            if (card.Status is RoomCardStatus byline && byline.Mark == RoomKind.Says)
            {
                // Whose words follow: a bare bubble beside the speaker's words, never a filled tile (SCREENS, 2026-11-13).
                Require(view.Glyphs.Any(each => each.What == "byline " + RoomKind.Says), "whose words draws no bubble");
                Require(view.Plates.All(each => each.What != "status mark"), "whose words stands in a filled tile, which reads as a button");
                Require(view.Shown.Any(label => label.GetParsedText() == byline.Title), byline.Title + " is not shown");
            }
            else if (card.Status is RoomCardStatus shownStatus && shownStatus.Line == null)
            {
                // A title alone stands centred on its mark.
                var mark = view.Plates.First(each => each.What == "status mark").Plate;
                var title = view.Shown.First(label => label.GetParsedText() == shownStatus.Title);
                title.ForceMeshUpdate(true);
                var middle = title.transform.localPosition.y + (title.textInfo.lineInfo[0].ascender + title.textInfo.lineInfo[title.textInfo.lineCount - 1].descender) / 2f;
                Require(Mathf.Abs(middle - mark.transform.localPosition.y) < unit * 3f, "a status's title alone stands " + ((middle - mark.transform.localPosition.y) / unit).ToString("0.0") + " dp off its mark's middle");
            }
            if (card.Slots.Count > 1)
                Require(Mathf.Abs(Left("slot")) < slack && Mathf.Abs(Right("slot") - inner) < slack, "the slots do not meet the card's two edges");
            // A lone slot stands at its own width at the left; one whose value is wider than the card wraps whole across it.
            if (card.Slots.Count == 1 && view.SlotsWrapped == 0)
                Require(Mathf.Abs(Left("slot")) < slack && Right("slot") < inner - slack, "a card's one slot does not stand at its own width at the left");
            if (card.Slots.Count == 1 && view.SlotsWrapped == 1)
                Require(Mathf.Abs(Left("slot")) < slack && Mathf.Abs(Right("slot") - inner) < slack, "a wrapped slot does not span the card");
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
                Require(tiles.Count == card.List.Count(row => row.Kind != RoomKind.None), "the list draws " + tiles.Count + " tiles for " + card.List.Count(row => row.Kind != RoomKind.None) + " marked rows");
                Require(tiles.All(each => Mathf.Abs(each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f) < slack), "a row's tile does not stand at the card's left");
                foreach (var row in card.List)
                {
                    // Leaning outside words stand a hair apart from ours (GlazeRuns), so a name is matched by its words, not its spaces.
                    string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    // An excerpt shows the lead of its words, cut at a word: matched by that lead.
                    var nameLabel = view.Shown.FirstOrDefault(label => Words(label.GetParsedText()) == Words(row.Name.Text)
                        || (row.Excerpt && view.Excerpts.Contains(label) && Words(row.Name.Text).StartsWith(Words(label.GetParsedText()).TrimEnd('…'), StringComparison.Ordinal)));
                    Require(nameLabel != null, row.Name.Text + " is not shown");
                    // Where any row has a mark every name stands after the tiles' room; where none has, at the card's left with the title's.
                    if (nameLabel != null && tiles.Count > 0)
                        Require(nameLabel.transform.localPosition.x + nameLabel.textBounds.min.x >= RoomType.Scaled(RoomCardView.RowTileDp + RoomCardView.RowGapDp) - unit * 1.5f,
                            row.Name.Text + " touches its tile");
                    if (nameLabel != null && tiles.Count == 0)
                        Require(Mathf.Abs(nameLabel.transform.localPosition.x + nameLabel.textBounds.min.x) < unit * 1.5f,
                            row.Name.Text + " stands in from the card's left with no mark beside it");
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
