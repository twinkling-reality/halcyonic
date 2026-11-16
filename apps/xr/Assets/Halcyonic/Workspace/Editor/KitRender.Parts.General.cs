#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The general reader (<see cref="RoomBar.OfFrame"/>'s last, the lead's 2026-11-13T16:40Z ruling) on the screens that fell to
    /// the older view: Settings' own frames (the groups with their values, a group's settings, a setting opened in its side
    /// panel), a side panel of facts over a page, a confirmation, more rows than a page (its first page and its second) and more
    /// actions than the column. Each frame is the column's own, drawn by the room's view; measured (the
    /// general reader took it, it holds whole, nothing cut, no word joined on a dot, the side panel titled with its way out
    /// last) and logged for a fresh reader. Synthetic data only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] GeneralBaseStates = { "52-general-settings", "52-general-comfort", "52-general-setting", "52-general-side", "52-general-confirm", "52-general-rows", "52-general-rows-2", "52-general-column", "52-general-usage", "52-general-caps", "52-general-colour", "52-general-shelf", "52-general-recap-words" };

        /// <summary>
        /// Every state twice: as the full bar, and in the kit's narrow form (AUDIT's 17:15Z condition 2, the lead's 17:16Z condition 1),
        /// read with the rows a page the plane would measure (fewer until the laid bar keeps inside its largest narrow footprint),
        /// and the pages it then takes logged as the worst case in parts.
        /// </summary>
        /// <remarks>And a third time at the plane's floor (the lead's 18:07Z: the twins render at the floor; XR's width rule, PlaneWidth): the
        /// narrowest width the plane lays a main screen at for the text size, its pages logged as the worst case in parts.</remarks>
        private static readonly string[] GeneralStates = GeneralBaseStates.Concat(GeneralBaseStates.Select(each => each + "-narrow"))
            .Concat(GeneralBaseStates.Select(each => each + "-floor")).ToArray();

        private static readonly (string, string)[] GeneralShelfPairs = { ("Left", "Right"), ("Closer", "Farther"), ("Up", "Down"), ("Turn Left", "Turn Right") };

        private static PartShot GeneralShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var narrow = state.EndsWith("-narrow", StringComparison.Ordinal);
            if (narrow) state = state.Substring(0, state.Length - "-narrow".Length);
            var floor = state.EndsWith("-floor", StringComparison.Ordinal);
            if (floor) state = state.Substring(0, state.Length - "-floor".Length);
            // The panel's width: the column's in the narrow form, the floor of this text size, or the full panel.
            var width = narrow ? RoomGeometry.ColumnDp : floor ? PlaneWidth.FloorDp(GlazeText.Scale) : RoomGeometry.PanelDp;
            // Narrower than the full panel: the plane's measure loop runs and its pages may pass what the full panel holds on one.
            var measured = narrow || floor;
            var was = RoomDesign.Enabled;
            MenuFrame? frame;
            RoomBar? bar;
            try
            {
                RoomDesign.Enabled = true;
                var comfort = new Comfort();
                var hub = new SettingsHubColumn(new GeneralRenderHost(), ComfortSettings.Of(comfort, () => { }), new AppearanceSettings(), () => { });
                if (state != "52-general-settings" && state != "52-general-side") hub.Act(SettingsHubColumn.OpenCategory, Comfort.Heading);
                if (state == "52-general-setting" && hub.Frame?.Lines.FirstOrDefault(line => line.Action == SettingsColumn.OpenSetting) is PageLine first)
                    hub.Act(SettingsColumn.OpenSetting, first.Key);
                frame = hub.Frame;
                // A side panel of facts over a page whose main action it does not act on: the panel's own Close is its one way out.
                if (state == "52-general-side")
                    frame = new MenuFrame(LineWords.TheirName("Kite Run"),
                        new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close), farRight: new Prompt("run-checks", "Run Checks", GlazeIcon.Next, main: true)),
                        lines: new[] { new PageLine("Tests", action: "open-tests", opens: true, chosen: true), new PageLine("Folder", action: "open-folder", opens: true) },
                        side: new SidePanel("Tests", facts: new[]
                        {
                            new SideFact("Result", "12 passed, none failed"), new SideFact("Checked", "After the agent's last turn"), new SideFact("Folder", LineWords.TheirName("kite-run")),
                        }));
                var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
                // A confirmation no specific reader takes: its words whole, its Yes the main action behind the lock, then Cancel.
                if (state == "52-general-confirm")
                    // As the plane's own (SpaceSettings' Paired setting, the critic's 22:01Z): the setting's subject, its Yes declared destructive.
                    frame = new MenuFrame("Paired Computer", Footer.Confirm(new Footer(close, farRight: new Prompt("forget", "Forget Computer", GlazeIcon.Close)), PromptSlot.FarRight,
                            new Prompt("forget-yes", "Forget Computer", GlazeIcon.Close, PromptKind.Yes, destructive: true), new Prompt("forget-cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel)),
                        lines: new[] { new PageLine("This headset stops reaching the computer until you pair it again. The computer keeps every project and task.") });
                // More rows than a page: the general reader's own pages, its pager between the actions and Close; -2 is the second page.
                if (state is "52-general-rows" or "52-general-rows-2")
                    frame = new MenuFrame("Settings", new Footer(close),
                        lines: Enumerable.Range(1, 11).Select(n => new PageLine("Setting " + n, fact: n % 2 == 0 ? "On" : "Off")).ToList());
                // More actions than the column holds: the general reader pages them, Close on every page.
                if (state == "52-general-column")
                    frame = new MenuFrame("Settings", new Footer(close),
                        lines: Enumerable.Range(1, 8).Select(n => new PageLine("Choice " + n, action: "choose", key: n.ToString(CultureInfo.InvariantCulture), opens: true)).ToList());
                // Usage with more windows than its card holds (two apps, two windows each): the general reader draws them. Synthetic names.
                // At the design's caps (AUDIT's 20:1xZ, for FIELD's measured envelope): eight rows a page, each a label over a value that
                // wraps (a real sentence, two to three lines), and four actions in all, three of them answers with their descriptions.
                if (state == "52-general-caps")
                {
                    const string value = "The agent keeps the kite's tail short and steady while it climbs, then lets it ripple when the wind turns.";
                    var lines = Enumerable.Range(1, 7).Select(n => new PageLine("Setting Number " + n, fact: value)).ToList();
                    lines.AddRange(Enumerable.Range(1, 3).Select(n => new PageLine("Answer " + n, action: "answer", key: n.ToString(CultureInfo.InvariantCulture), choice: true,
                        answerMark: AnswerMark.Single).WithUnder(LineWords.Ours("Keeps what the agent made and starts the next step from it."))));
                    frame = new MenuFrame("Settings", new Footer(close), lines: lines);
                }
                // A picker's choices and a shelf's moves in the panel (the lead's 21:11Z): the plane's own Character Colour, and the shelf's
                // nine moves handed in pairs, never split across a page.
                if (state == "52-general-colour")
                    frame = new CharacterColourColumn(new CharacterColourSettings(), new CharacterColourScope("render", "journal", "task"), () => true, _ => true).Frame;
                if (state == "52-general-shelf")
                {
                    // The shelf's shape (XR's a46dc4651): its two instructions each a line of its own, then its moves in pairs.
                    var moves = new List<PageLine> { new PageLine("Hold Move Shelf to Drag", tone: LineTone.Secondary), new PageLine("Apply for This Session", tone: LineTone.Secondary) };
                    foreach (var (a, b) in GeneralShelfPairs)
                    {
                        moves.Add(new PageLine(a, action: "move", key: a, control: LineControl.Action).WithGroup(a));
                        moves.Add(new PageLine(b, action: "move", key: b, control: LineControl.Action).WithGroup(a));
                    }
                    moves.Add(new PageLine("Find Room Surface", action: "read-room", control: LineControl.Action));
                    frame = new MenuFrame("Shelf", new Footer(new Prompt(Footer.Close, "Cancel", GlazeIcon.Close, PromptKind.Close),
                        farRight: new Prompt("apply", "Apply", GlazeIcon.Change, main: true)), lines: moves);
                }
                // A person's own long words read whole (the lead's 2026-11-14T01:19Z next unit): the recap card's press opens XR's own
                // reading page (NewProjectScreens.RecapWords), the name and the first task as labelled facts under the recap's own labels,
                // each a row, a long one in parts with its label kept, Back to the recap on every page. Synthetic words: the recap card's
                // own project (48-recap-built) with its first task lengthened, so the two renders read as one press.
                if (state == "52-general-recap-words")
                {
                    var idea = new ProjectIdea();
                    idea.UseIdea("Make a kite that follows my hand, and climbs when I raise it. "
                        + string.Join(" ", Enumerable.Repeat("Make the kite climb faster when the person leans forward, and let the tail ripple.", 4)));
                    idea.Rename("Kite Run");
                    frame = NewProjectScreens.RecapWords(idea);
                }
                if (state == "52-general-usage")
                {
                    var limits = JsonConvert.DeserializeObject<UsageLimitsResponse>(GeneralUsageLimits, HalcyonicJson.Strict) as AvailableUsageLimits;
                    var usage = new UsageColumn(new GeneralRenderHost(demonstration: true), _ => limits);
                    var sections = new MenuBar(MenuPlace.Usage, "Nothing Waiting").Sections();
                    frame = usage.Frame?.WithSections(sections, sections.Count);
                }
                bar = frame == null ? null : RoomBar.OfFrame(frame, VoiceStage.Idle);
            }
            finally { RoomDesign.Enabled = was; }
            Require(frame != null && bar != null, "the general reader gives the redesign no bar");
            if (frame == null || bar == null) return shot;
            MenuFrame given = frame;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            // The kit's own line measure at the width the bar is laid (the lead's 20:15Z: eight lines means eight lines), as the plane
            // hands it; narrow: the plane's measure loop too, one row and one line fewer a page while the laid bar passes its footprint.
            var form = new RoomForm(narrow, RoomRows.APage, panelDp: floor ? width : RoomGeometry.PanelDp).Measured((row, marked) => view.RowLines(row, marked, width));
            RoomBar Read()
            {
                RoomDesign.Enabled = true;
                try
                {
                    var read = RoomBar.OfFrame(given, VoiceStage.Idle, form)!;
                    read.Narrow = narrow;
                    // As RoomBar.Of lays a plain frame: the panel at the form's width where it is under the full one.
                    if (floor) read.PanelWidthDp = width;
                    return read;
                }
                finally { RoomDesign.Enabled = was; }
            }
            int PagesOf(RoomBar each) => Math.Max(1, each.GeneralPart.Pages);
            // Fewer lines only mend a bar too TALL, judged on its tallest page (FIELD's 20:53Z: page 1 alone hid the rest); a bar too wide
            // (its widest action past the column) is the kit's finding, kept.
            bool TooTall(RoomBar each)
            {
                var largest = RoomBarView.LargestAt(width);
                var tall = false;
                for (var page = 0; page < PagesOf(each); page++)
                {
                    view.Show(each);
                    view.SettleForRender();
                    tall |= view.PassedLargest && view.Size.y > largest.y;
                    if (PagesOf(each) > 1) each.Turn();
                }
                return tall;
            }
            while (true)
            {
                bar = Read();
                if (!measured || form.LinesAPage == 1 || !TooTall(bar)) break;
                form = form.Fewer();
            }
            bar = Read();
            // The last page of the long list, every earlier one shown on the way (eight lines a page may make more than two).
            if (state == "52-general-rows-2") while (bar.GeneralPart.Page < bar.GeneralPart.Pages - 1) bar.Turn();
            Debug.Log("Halcyonic: parts: " + name + ": " + (narrow ? "narrow" : floor ? "floor " + width.ToString("0", CultureInfo.InvariantCulture) + " dp" : "full") + " at " + form.LinesAPage + " lines a page, " + bar.GeneralPart.Pages + " page"
                + (bar.GeneralPart.Pages == 1 ? "" : "s") + (bar.Card != null ? " (" + bar.Card.List.Count + " pressed rows on the first)" : "") + ".");
            var confirm = state == "52-general-confirm";
            var paged = state is "52-general-rows" or "52-general-rows-2" or "52-general-column" or "52-general-shelf" or "52-general-recap-words" || (state is "52-general-usage" or "52-general-caps" || measured || confirm) && bar.GeneralPart.Pages > 1;
            // The general reader's card holds pressed rows, never the usage card's gauges (which would mean a specific reader took it).
            if (state == "52-general-usage") Require(bar.Card == null || bar.Card.Gauges.Count == 0, "Usage's four windows were taken by a specific reader, not the general one");
            Require(bar.FromMoment && (paged ? bar.GeneralPart.Pages > 1 : bar.Whole), "the frame is not held whole, or not paged, by the general reader");
            // The way out: a side panel's own Close, a confirmation's Cancel, else the frame's Close.
            var wayOut = frame.Side != null ? SidePanel.Close : confirm ? "forget-cancel" : state == "52-general-recap-words" ? NewProjectScreens.BackToRecap : Footer.Close;
            if (paged)
            {
                Require(bar.Column.Last().Control.Action == wayOut && bar.Column.Any(action => action.Control.Action == RoomBar.TurnPage), "a paged frame lacks its pager or its way out");
                // Read once every page with something to read was shown: the second page of rows, and at once where only bare actions page.
                // Usage's card pages by the plane's line measure, so which later page holds words to read depends on the size (gen-n11,
                // FIELD's F112): its first-page Whole is not fixed here; the sweep below still requires it whole after every page, and the
                // client tests pin the rule itself.
                if (!measured && !confirm && state != "52-general-usage")
                    Require(bar.Whole == (state is "52-general-rows-2" or "52-general-column" or "52-general-shelf"), "Whole before every page to read was shown, or not after");
            }
            Require(!bar.LostPart, "the general reader lost a part of the frame (not on screen)");
            if (frame.Side is SidePanel panel)
            {
                Require(bar.Words.Text == panel.Subject, "the side panel's title is not the bar's words");
                Require(bar.Column.Last().Control.Action == SidePanel.Close, "the side panel's own Close is not the last way out");
                Require(bar.Column.All(action => action.Control.Action != "run-checks" && action.Control.Action != Footer.Close), "the hidden page's actions stand beside its side panel");
            }

            // EVERY page laid and read (FIELD's 20:53Z): a fresh read turned through all its pages, each measured, every word of the
            // frame drawn whole on one page or across a long row's parts, each page's own actions drawn, and back at the first page.
            string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var drawn = new List<string>();
            var sweep = Read();
            var sweepPages = PagesOf(sweep);
            var pageOf = new Dictionary<string, int>();
            if (state is "52-general-colour" or "52-general-shelf")
                Require(sweep.Card != null && sweep.Card.List.Count > 0 && sweep.Column.All(action => action.Control.Place != KitPlace.Choice && action.Control.Action != "move"),
                    "the picker's lines do not stand in the panel as the card's pressed rows");
            for (var page = 0; page < sweepPages; page++)
            {
                var at = "page " + (page + 1) + " of " + sweepPages + ": ";
                view.Show(sweep);
                view.SettleForRender();
                Require(!view.PassedLargest, at + "the bar laid past its largest footprint");
                Require(sweep.Column.Count is > 0 and <= RoomBar.ColumnMost, at + "the column holds " + sweep.Column.Count + " actions");
                Require(sweep.Column.Last().Control.Action == wayOut, at + "the way out is not last");
                var shownButtons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
                Require(shownButtons.Count == sweep.Column.Count, at + "the column draws " + shownButtons.Count + " of " + sweep.Column.Count + " actions");
                // A confirmation's Yes stands first behind its lock only on the page that ends what it does.
                if (confirm)
                    Require(page < sweepPages - 1
                            ? sweep.Column.All(action => action.Control.Action != "forget-yes")
                            : sweep.Column[0].Control.Action == "forget-yes" && sweep.Column[0].Locks,
                        at + "the confirmation's Yes stands before what it does was shown, or not first behind its lock once it was");
                var onPage = new List<string>();
                foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    // Parsed afresh: a reused button's label measured with other words keeps them in TMP's buffer until it is parsed again, so
                    // its parsed text could echo them ("Next Pagee" after "First Page", b208's parts log) while its mesh draws the words.
                    label.ForceMeshUpdate(true, true);
                    var said = label.GetParsedText();
                    if (said.Trim().Length == 0) continue;
                    onPage.Add(said);
                    Require(!label.isTextOverflowing && !said.Contains("…"), at + "\"" + said + "\" is cut");
                    Require(!said.Contains(KitMomentWords.Dot.Trim()), at + "\"" + said + "\" joins facts on a dot");
                }
                foreach (var action in sweep.Column)
                    // Exactly the action's words, or its words over its detail, on one drawn label: never words that merely contain them.
                    Require(action.Detail != null && onPage.Any(said => Words(said) == Words(action.Words + " " + action.Detail))
                        || onPage.Any(said => Words(said) == Words(action.Words)) && (action.Detail == null || onPage.Any(said => Words(said) == Words(action.Detail))),
                        at + "the action \"" + action.Words + "\" is not drawn as its words exactly, with its detail");
                drawn.AddRange(onPage);
                foreach (var row in sweep.Card?.List ?? (IReadOnlyList<RoomListRow>)Array.Empty<RoomListRow>()) pageOf[row.Name.Text] = page;
                Debug.Log("Halcyonic: parts: " + name + ": " + at + "drew " + string.Join(" | ", onPage.Select(Words)) + ".");
                if (sweepPages > 1) sweep.Turn();
            }
            Require(sweep.Whole, "every page was shown and the frame is still not read");
            if (state == "52-general-shelf")
                foreach (var (a, b) in GeneralShelfPairs)
                    Require(pageOf.TryGetValue(a, out var at) && pageOf.TryGetValue(b, out var beside) && at == beside, "\"" + a + "\" and \"" + b + "\" stand on two pages");
            Require(sweep.GeneralPart.Page == 0, "the pager does not come back to the first page");
            var expected = new List<string>();
            if (frame.Side is SidePanel side)
            {
                expected.Add(side.Subject);
                expected.AddRange(side.Facts.Select(fact => fact.Name));
                expected.AddRange(side.Facts.Select(fact => fact.ValueRuns.Text));
            }
            else
            {
                expected.Add(frame.Subject);
                expected.AddRange(frame.Lines.Where(line => line.Action == null).Select(line => line.Words));
                expected.AddRange(frame.Lines.Where(line => line.Action != null && line.Available).Select(line => line.Words));
                expected.AddRange(frame.Lines.Where(line => line.Action != null && line.Fact != null).Select(line => line.Fact!));
                expected.AddRange(frame.Lines.Where(line => line.Action != null && line.Under != null).Select(line => line.Under!.Text));
            }
            // Whole on one page, or a long row's parts in order across pages (each part is a run of the row's own lines).
            bool DrawnWhole(string words)
            {
                var whole = Words(words);
                if (drawn.Any(said => Words(said).Contains(whole))) return true;
                var cursor = 0;
                foreach (var piece in drawn.Select(Words).Where(piece => piece.Length > 0))
                {
                    if (cursor + piece.Length > whole.Length || string.CompareOrdinal(whole, cursor, piece, 0, piece.Length) != 0) continue;
                    cursor += piece.Length;
                    while (cursor < whole.Length && whole[cursor] == ' ') cursor++;
                }
                return cursor >= whole.Length;
            }
            foreach (var words in expected.Where(words => words.Trim().Length > 0))
                Require(DrawnWhole(words), "\"" + words + "\" is not drawn whole on any page: " + string.Join(" | ", drawn.Select(Words)));

            // The image: the bar as the state names it (the first page, or the long list's last), laid and placed.
            view.Show(bar);
            view.SettleForRender();
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            foreach (var button in buttons)
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);
            Debug.Log("Halcyonic: parts: " + name + ": the general reader drew " + string.Join(" | ", view.GetComponentsInChildren<TMPro.TMP_Text>(false)
                    .Select(label => Words(label.GetParsedText())).Where(said => said.Length > 0)) + "; actions: "
                + string.Join(", ", buttons.Select(button => button.Words.text)) + ".");
            return shot;
        }

        /// <summary>Four windows of two agent apps (synthetic names), more than the usage card holds.</summary>
        private const string GeneralUsageLimits = @"{
  ""availability"": ""available"",
  ""source"": { ""system"": ""seorak"", ""synthetic"": true, ""api_version"": ""v1"" },
  ""complete"": true,
  ""readings"": [
    { ""agent"": ""ember"", ""label"": ""Ember App"", ""window"": ""rolling-5h"", ""used_percent"": 40.2, ""resets_at"": ""2026-11-11T11:05:00.000Z"", ""observed_at"": ""2026-11-11T08:58:00.000Z"", ""freshness"": ""fresh"", ""account"": { ""state"": ""unidentified"" } },
    { ""agent"": ""ember"", ""label"": ""Ember App"", ""window"": ""weekly"", ""used_percent"": 15.1, ""resets_at"": ""2026-11-16T09:00:00.000Z"", ""observed_at"": ""2026-11-11T08:58:00.000Z"", ""freshness"": ""fresh"", ""account"": { ""state"": ""unidentified"" } },
    { ""agent"": ""tide"", ""label"": ""Tide App"", ""window"": ""rolling-5h"", ""used_percent"": 97.2, ""resets_at"": ""2026-11-11T11:05:00.000Z"", ""observed_at"": ""2026-11-11T08:58:00.000Z"", ""freshness"": ""fresh"", ""account"": { ""state"": ""unidentified"" } },
    { ""agent"": ""tide"", ""label"": ""Tide App"", ""window"": ""weekly"", ""used_percent"": 60.4, ""resets_at"": ""2026-11-16T09:00:00.000Z"", ""observed_at"": ""2026-11-11T08:58:00.000Z"", ""freshness"": ""fresh"", ""account"": { ""state"": ""unidentified"" } }
  ]
}";

        /// <summary>A menu host for the general reader's renders: the production font's measures, nothing sent; a recording for Usage.</summary>
        private sealed class GeneralRenderHost : IMenuHost
        {
            public GeneralRenderHost(bool demonstration = false) => Demonstration = demonstration;
            public ClientProjection State { get; } = new();
            public bool Connected => !Demonstration;
            public bool Demonstration { get; }
            public double Now => 0;
            public DateTimeOffset Clock => DateTimeOffset.Parse(AskColumnTime, CultureInfo.InvariantCulture);
            public TimeZoneInfo Zone => TimeZoneInfo.Utc;
            public TextSize TextSize => GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;
            public bool VoiceOffered => true;
            public ControlPlaneApi? Api => null;
            public Task<CommandAckMessage>? Submit(CommandEnvelope command) => null;
            public bool KeyboardOffered => true;
            public void OpenKeyboard(string text, string prompt, Action<string> done, Action? dismissed = null) { }
            public int RowsOf(string words, float degrees) => MenuFrameView.RowsOf(words, degrees);
            public int RowsOf(PageLine line, float degrees) => MenuFrameView.RowsOf(line, degrees);
            public bool FitsHalf(PageLine line, float degrees) => MenuFrameView.FitsHalf(line, degrees);
            public int TitleRows(string subject, float degrees) => MenuFrameView.TitleRows(subject, degrees);
            public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);
            public float PageHeight(int rows, bool besideMenu, bool focused = false) => MenuPage.Height(TextSize, rows, besideMenu: besideMenu, focused: focused);
            public void OpenFile(string id) { }
            public void OpenNewProject(string? id, string? name) { }
        }
    }
}
