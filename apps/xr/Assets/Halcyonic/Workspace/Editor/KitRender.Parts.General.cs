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
        private static readonly string[] GeneralBaseStates = { "52-general-settings", "52-general-comfort", "52-general-setting", "52-general-side", "52-general-confirm", "52-general-rows", "52-general-rows-2", "52-general-column", "52-general-usage", "52-general-caps" };

        /// <summary>
        /// Every state twice: as the full bar, and in the kit's narrow form (AUDIT's 17:15Z condition 2, the lead's 17:16Z condition 1),
        /// read with the rows a page the plane would measure (fewer until the laid bar keeps inside its largest narrow footprint),
        /// and the pages it then takes logged as the worst case in parts.
        /// </summary>
        private static readonly string[] GeneralStates = GeneralBaseStates.Concat(GeneralBaseStates.Select(each => each + "-narrow")).ToArray();

        private static PartShot GeneralShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var narrow = state.EndsWith("-narrow", StringComparison.Ordinal);
            if (narrow) state = state.Substring(0, state.Length - "-narrow".Length);
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
                    frame = new MenuFrame("Settings", Footer.Confirm(new Footer(close, farRight: new Prompt("forget", "Forget Computer", GlazeIcon.Close)), PromptSlot.FarRight,
                            new Prompt("forget-yes", "Forget Computer", GlazeIcon.Close, PromptKind.Yes), new Prompt("forget-cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel)),
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
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            // The kit's own line measure at the width the bar is laid (the lead's 20:15Z: eight lines means eight lines), as the plane
            // hands it; narrow: the plane's measure loop too, one row and one line fewer a page while the laid bar passes its footprint.
            var form = new RoomForm(narrow, RoomRows.APage).Measured((row, indented) => (row.Label == null ? 0 : view.Lines(LineWords.Ours(row.Label), false, narrow)) + view.Lines(row.Words, false, narrow));
            while (true)
            {
                RoomDesign.Enabled = true;
                try
                {
                    bar = RoomBar.OfFrame(frame, VoiceStage.Idle, form)!;
                    // The last page of the long list, every earlier one shown on the way (eight lines a page may make more than two).
                    if (state == "52-general-rows-2") while (bar.GeneralPart.Page < bar.GeneralPart.Pages - 1) bar.Turn();
                }
                finally { RoomDesign.Enabled = was; }
                bar.Narrow = narrow;
                view.Show(bar);
                view.SettleForRender();
                if (!narrow || !view.PassedLargest || form.LinesAPage == 1) break;
                form = form.Fewer();
            }
            Debug.Log("Halcyonic: parts: " + name + ": " + (narrow ? "narrow" : "full") + " at " + form.LinesAPage + " lines a page, " + bar.GeneralPart.Pages + " page"
                + (bar.GeneralPart.Pages == 1 ? "" : "s") + (bar.Card != null ? " (" + bar.Card.List.Count + " pressed rows on the first)" : "") + ".");
            var paged = state is "52-general-rows" or "52-general-rows-2" or "52-general-column" || (state is "52-general-usage" or "52-general-caps" || narrow) && bar.GeneralPart.Pages > 1;
            // The general reader's card holds pressed rows, never the usage card's gauges (which would mean a specific reader took it).
            if (state == "52-general-usage") Require(bar.Card == null || bar.Card.Gauges.Count == 0, "Usage's four windows were taken by a specific reader, not the general one");
            Require(bar.FromMoment && (paged ? bar.GeneralPart.Pages > 1 : bar.Whole), "the frame is not held whole, or not paged, by the general reader");
            if (paged)
            {
                Require(bar.Column.Last().Control.Action == Footer.Close && bar.Column.Any(action => action.Control.Action == RoomBar.TurnPage), "a paged frame lacks its pager or its Close");
                // Read once every page with something to read was shown: the second page of rows, and at once where only bare actions page.
                if (!narrow) Require(bar.Whole == (state is "52-general-rows-2" or "52-general-column"), "Whole before every page to read was shown, or not after");
            }
            if (state == "52-general-confirm")
                Require(bar.Column[0].Control.Action == "forget-yes" && bar.Column[0].Locks && bar.Column.Last().Control.Action == "forget-cancel", "the confirmation's Yes is not first behind its lock, or Cancel not last");
            Require(bar.Column.Count is > 0 and <= RoomBar.ColumnMost, "the column holds " + bar.Column.Count + " actions");
            Require(!bar.LostPart, "the general reader lost a part of the frame (not on screen)");
            var expected = new List<string>();
            if (frame.Side is SidePanel side)
            {
                Require(bar.Words.Text == side.Subject, "the side panel's title is not the bar's words");
                Require(bar.Column.Last().Control.Action == SidePanel.Close, "the side panel's own Close is not the last way out");
                Require(bar.Column.All(action => action.Control.Action != "run-checks" && action.Control.Action != Footer.Close), "the hidden page's actions stand beside its side panel");
                expected.Add(side.Subject);
                expected.AddRange(side.Facts.Select(fact => fact.Name));
                expected.AddRange(side.Facts.Select(fact => fact.ValueRuns.Text));
            }
            else if (paged)
            {
                expected.AddRange((bar.Facts ?? new List<RoomRow>()).Select(row => row.Words.Text));
                expected.AddRange(bar.Card?.List.Select(row => row.Name.Text) ?? Enumerable.Empty<string>());
                expected.AddRange(bar.Column.Select(action => action.Words));
            }
            else
            {
                expected.AddRange(frame.Lines.Where(line => line.Action == null).Select(line => line.Words));
                expected.AddRange(frame.Lines.Where(line => line.Action != null && line.Available).Select(line => line.Words));
                expected.AddRange(frame.Lines.Where(line => line.Action != null && line.Fact != null).Select(line => line.Fact!));
            }

            Require(!view.PassedLargest, "the bar laid past its largest footprint");
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            foreach (var button in buttons)
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);
            Require(buttons.Count == bar.Column.Count, "the column draws " + buttons.Count + " of " + bar.Column.Count + " actions");
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
            foreach (var words in expected.Where(words => words.Trim().Length > 0))
                Require(drawn.Any(said => Words(said).Contains(Words(words))), "\"" + words + "\" is not drawn whole: " + string.Join(" | ", drawn.Select(Words)));
            Debug.Log("Halcyonic: parts: " + name + ": the general reader drew " + string.Join(" | ", drawn.Select(Words)) + "; actions: "
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
