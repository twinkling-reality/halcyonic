#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The plane's floor, by measure (the lead's 2026-11-13T1807Z ruling): New Project's recap, a review, a page of eleven
    /// rows and Usage's four limits, each read by the kit's own readers and laid at panel widths from DESIGN's 440 dp down to
    /// the column's 240, at the person's text size. For each width it logs whether every row lays whole within the bar's
    /// largest footprint, and the parts the screen takes: the general reader's pages at the rows a page that hold (as the
    /// plane measures them), a review's parts at eight of the panel's lines a part, the recap whole or not. The floor is the
    /// narrowest width where every screen lays whole in three parts or fewer. A measure, not a check: it fails only if no
    /// width lays the screens at all. Synthetic words only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] WidthStates = { "59-width-table", "60-envelope-caps" };

        private static readonly float[] TableWidthsDp = { 440f, 400f, 360f, 320f, 280f, 240f };

        private static PartShot WidthShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            if (state == "60-envelope-caps") return CapsShot(shot, root, eyes, name);
            var was = RoomDesign.Enabled;
            var screens = new List<(string Name, MenuFrame Frame)>();
            NewWorkReview review;
            ProjectIdea idea;
            try
            {
                RoomDesign.Enabled = true;
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                idea = new ProjectIdea();
                idea.UseIdea("Make a kite that follows my hand, and climbs when I raise it, with a tail that streams behind it and a score for how high it flies");
                idea.Rename("Kite Run");
                var place = new LocationRoot { Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false, Folders = new List<LocationFolder>() };
                idea.ChooseFolder(ProjectFolder.New(place, "kite-run")!);
                var draft = new NewWorkDraft(commands);
                draft.ChooseRuntime(new RuntimeDescriptor
                {
                    RuntimeId = "local", DisplayName = "Ember App", Kind = "render", Synthetic = false, ModelChoice = ModelChoice.Listed, UsesProjectLocation = true, OffersNeverAsk = true,
                    Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, AnswerQuestion = true, Interrupt = true },
                });
                draft.SetModels(new RuntimeModelsResponse
                {
                    RuntimeId = "local",
                    Result = new AvailableModels { Models = new List<RuntimeModel> { new() { ModelRef = "render/glimmer-30b", DisplayName = "Glimmer 30B", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared } } },
                });
                screens.Add(("recap", NewProjectScreens.Recap(idea, draft, null, true, null, null)));
                review = new NewWorkReview("Kite Run", "Title", "Ember App", "Glimmer 30B", "on your computer", "none", idea.FirstTask);
                screens.Add(("review", NewProjectScreens.Review(idea, review, null, room: true)));
                var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
                screens.Add(("rows", new MenuFrame("Settings", new Footer(close),
                    lines: Enumerable.Range(1, 11).Select(n => new PageLine("Setting " + n, fact: n % 2 == 0 ? "On" : "Off")).ToList())));
                screens.Add(("usage", new MenuFrame("Usage", new Footer(close),
                    lines: new[] { ("5-Hour Window", "Ember App", "Up to 60% Left"), ("Weekly Window", "Ember App", "Up to 85% Left"), ("Daily Window", "Lantern App", "Up to 12% Left"), ("Monthly Window", "Lantern App", "Up to 40% Left") }
                        .Select((each, at) => new PageLine(LineWords.Theirs(each.Item2), fact: LineWords.Ours(each.Item3), action: "width-usage", key: at.ToString(CultureInfo.InvariantCulture), control: LineControl.Action)
                            .WithRowLabel(each.Item1).WithUnder(LineWords.Ours("Seen 2 minutes ago. Resets at 18:40."))).ToList())));
            }
            finally { RoomDesign.Enabled = was; }

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            var floor = (float?)null;
            var size = GlazeText.Scale > 1.001f ? "Larger" : "Standard";
            foreach (var width in TableWidthsDp)
            {
                var allFit = true;
                var cells = new List<string>();
                foreach (var (screen, frame) in screens)
                {
                    var whole = false;
                    var parts = 0;
                    var rows = RoomRows.APage;
                    if (screen == "review")
                    {
                        // A review is read in parts of eight of the panel's own lines, as the flow cuts it: each item, its label over its
                        // value, measured at this width, then paginated by the review itself.
                        var itemLines = review.Items.Select(item => view.LinesAt(LineWords.Ours(item.Label), false, width) + view.LinesAt(item.ValueRuns, false, width)).ToList();
                        review.Paginate(itemLines, RoomBar.RequestLines);
                        parts = review.PageCount;
                        whole = parts > 0;
                    }
                    else
                    {
                        while (true)
                        {
                            RoomBar? bar;
                            RoomDesign.Enabled = true;
                            try { bar = RoomBar.OfFrame(frame, VoiceStage.Idle, new RoomForm(width < RoomGeometry.PanelDp, rows)); }
                            finally { RoomDesign.Enabled = was; }
                            if (bar == null) break;
                            bar.PanelWidthDp = width;
                            view.Show(bar);
                            view.SettleForRender();
                            whole = view.LaidWhole && !view.PassedLargest && !bar.LostPart;
                            parts = bar.GeneralPart.Pages;
                            if (whole || rows == 1) break;
                            rows--;
                        }
                    }
                    if (!whole || parts > 3) allFit = false;
                    cells.Add(screen + " " + (whole ? parts + (parts == 1 ? " part" : " parts") : "NOT WHOLE (" + (view.FirstCut ?? "largest") + ")")
                        + (screen is "rows" or "usage" ? " at " + rows + " rows a page" : ""));
                }
                if (allFit) floor = width;
                Debug.Log("Halcyonic: width table: " + size + " " + width.ToString("0", CultureInfo.InvariantCulture) + " dp (" + (width / RoomGeometry.DpPerDegree).ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees): " + string.Join("; ", cells) + (allFit ? "; ALL FIT" : "") + ".");
            }
            Debug.Log("Halcyonic: width table: " + size + " floor " + (floor is float f ? f.ToString("0", CultureInfo.InvariantCulture) + " dp (" + (f / RoomGeometry.DpPerDegree).ToString("0.0", CultureInfo.InvariantCulture) + " degrees)" : "NONE") + ".");
            Require(floor != null, "no width lays every screen whole in three parts or fewer");
            // The render shows the recap at DESIGN's width; every measured bar is laid again so the state's own checks read it.
            RoomBar? shown;
            RoomDesign.Enabled = true;
            try { shown = RoomBar.OfFrame(screens[0].Frame, VoiceStage.Idle); }
            finally { RoomDesign.Enabled = was; }
            if (shown != null) { view.Show(shown); view.SettleForRender(); }
            Place(plane, eyes, 0f, shot.Elevation);
            return shot;
        }

        /// <summary>
        /// A bar laid AT the design's caps (AUDIT, 2026-11-13T2010Z: the envelope measured at the caps, not at the fixtures), at every
        /// width the table names: eight rows of one line each, the spec's eight lines a panel (the tallest eight lines, their seven
        /// hairlines between them), and four actions, the spec's column, each with its quieter second line. Every bar is laid in
        /// turn on its own plane so the run's envelope records it at that width; then the record's coverage is checked: both
        /// sizes and every width, so no build carries an envelope the plane could not trust.
        /// </summary>
        private static PartShot CapsShot(PartShot shot, Transform root, Vector3 eyes, string name)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
            var frame = new MenuFrame("Settings", new Footer(close, rare: new Prompt("caps-b", "Run Checks Again", GlazeIcon.Refresh).WithUnder(LineWords.Ours("All four")),
                    secondary: new Prompt("caps-c", "Choose Folder", GlazeIcon.Change).WithUnder(LineWords.Ours("In Projects")),
                    farRight: new Prompt("caps-a", "Start Building", GlazeIcon.StartBuilding, main: true).WithUnder(LineWords.Ours("Ember App"))),
                lines: Enumerable.Range(1, 8).Select(n => new PageLine("Row " + n)).ToList());
            // A specific reader at its budgets: New Project's recap card, its title wrapping, its first task past the description's
            // four lines (so it pages), the companion's note at its two lines, its run settings as slots.
            MenuFrame recap;
            RoomDesign.Enabled = true;
            try
            {
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var idea = new ProjectIdea();
                idea.UseIdea(string.Join(" ", Enumerable.Repeat("Make a kite that follows my hand and climbs when I raise it, with a tail that streams behind it", 6)));
                idea.Rename("A Kite Run Over the Long Bright Beach at the End of a Summer Afternoon");
                var place = new LocationRoot { Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false, Folders = new List<LocationFolder>() };
                idea.ChooseFolder(ProjectFolder.New(place, "a-kite-run-over-the-long-bright-beach")!);
                var draft = new NewWorkDraft(commands);
                draft.ChooseRuntime(new RuntimeDescriptor
                {
                    RuntimeId = "local", DisplayName = "Ember App", Kind = "render", Synthetic = false, ModelChoice = ModelChoice.Listed, UsesProjectLocation = true, OffersNeverAsk = true,
                    Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, AnswerQuestion = true, Interrupt = true },
                });
                draft.SetModels(new RuntimeModelsResponse
                {
                    RuntimeId = "local",
                    Result = new AvailableModels { Models = new List<RuntimeModel> { new() { ModelRef = "render/glimmer-30b-long-context-instruct", DisplayName = "Glimmer 30B Long Context Instruct", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared } } },
                });
                recap = NewProjectScreens.Recap(idea, draft, null, true, "It may have made the project and its folder. Look for it in Projects. A new start could repeat it.", null);
            }
            finally { RoomDesign.Enabled = was; }
            foreach (var (capsFrame, width) in new[] { frame, recap }.SelectMany(each => TableWidthsDp.Select(width => (each, width))))
            {
                RoomBar? bar;
                RoomDesign.Enabled = true;
                try { bar = RoomBar.OfFrame(capsFrame, VoiceStage.Idle, new RoomForm(width < RoomGeometry.PanelDp, RoomRows.APage)); }
                finally { RoomDesign.Enabled = was; }
                Require(bar != null, "no reader takes the caps frame " + capsFrame.Subject);
                if (bar == null) continue;
                bar.PanelWidthDp = width;
                var plane = new GameObject("Caps " + capsFrame.Subject + " " + width.ToString("0", CultureInfo.InvariantCulture)).transform;
                plane.SetParent(root, false);
                var view = RoomBarView.Create(plane);
                view.Show(bar);
                view.SettleForRender();
                Place(plane, eyes, 0f, shot.Elevation);
                Debug.Log("Halcyonic: parts: " + name + ": caps of " + capsFrame.Subject + " at " + width.ToString("0", CultureInfo.InvariantCulture) + " dp: " + (view.Size / RoomType.Units(1f)).ToString("0") + " dp laid.");
            }
            // The shipped record covers both sizes and every width the plane can choose.
            foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
                foreach (var width in TableWidthsDp)
                    Require(KitEnvelope.Entries.Any(entry => Mathf.Abs(entry.scale - scale) < 0.01f && Mathf.Abs(entry.panelDp - width) < 0.5f),
                        "the kit's recorded envelope (Resources/" + KitEnvelope.ResourceName + ".json) has no entry at scale " + scale.ToString("0.00", CultureInfo.InvariantCulture)
                        + " for a panel " + width.ToString("0", CultureInfo.InvariantCulture) + " dp wide");
            return shot;
        }
    }
}
