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

        // The plane's own steps (PlaneWidth, XR's rule), never a copy, so the table and the plane cannot drift apart.
        private static float[] TableWidthsDp => PlaneWidth.Steps;

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
            var broken = false;
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
                        // A review is read in parts of eight of the panel's own lines, as the flow cuts it (NewProjectFlow.Measure, SCREENS's
                        // b4c61286d): each item's value measured at this width, its label apart over it one head line, paginated by the review itself.
                        var itemLines = review.Items.Select(item => System.Math.Max(1, view.LinesAt(item.RowRuns, false, width))).ToList();
                        review.Paginate(itemLines, RoomBar.RequestLines, review.Items.Select(item => item.RowLabel == null ? 0 : 1).ToList());
                        parts = review.PageCount;
                        whole = parts > 0;
                    }
                    else
                    {
                        while (true)
                        {
                            RoomBar? bar;
                            RoomDesign.Enabled = true;
                            // Laid as the plane lays a width (PlaneWidthRule): the full form at that panel width, never the narrow one.
                            try { bar = RoomBar.OfFrame(frame, VoiceStage.Idle, new RoomForm(false, rows, panelDp: width)); }
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
                // The floor is the narrowest width with every wider one fitting too: a width that fits under one that does not is no floor.
                if (allFit && !broken) floor = width;
                if (!allFit) broken = true;
                Debug.Log("Halcyonic: width table: " + size + " " + width.ToString("0", CultureInfo.InvariantCulture) + " dp (" + (width / RoomGeometry.DpPerDegree).ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees): " + string.Join("; ", cells) + (allFit ? "; ALL FIT" : "") + ".");
            }
            Debug.Log("Halcyonic: width table: " + size + " floor " + (floor is float f ? f.ToString("0", CultureInfo.InvariantCulture) + " dp (" + (f / RoomGeometry.DpPerDegree).ToString("0.0", CultureInfo.InvariantCulture) + " degrees)" : "NONE") + ".");
            Require(floor != null, "no width lays every screen whole in three parts or fewer");
            // The plane lays nothing under its floor (PlaneWidth.FloorDp): no screen may need MORE than it, or a person meets a screen cut at
            // the floor. The floor is held EQUAL to the measure by the caps' state, which adds the second limit, every word at 14 dp as the
            // eyes see it (the lead's 2026-11-14T02:54Z (A)); this state hands it the first.
            measuredLayFloor[GlazeText.Scale] = floor;
            var planeFloor = PlaneWidth.FloorDp(GlazeText.Scale);
            Require(floor is float measuredFloor && measuredFloor <= planeFloor + 0.5f, "the measured " + size + " floor, "
                + (floor is float floorSaid ? floorSaid.ToString("0", CultureInfo.InvariantCulture) + " dp" : "none") + ", is above the plane's " + planeFloor.ToString("0", CultureInfo.InvariantCulture) + " dp (PlaneWidth): a screen is cut at the floor");
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
        /// <summary>The width table's floor at each text size this run (every main screen whole), for the caps' state to hold the floor by both measures.</summary>
        private static readonly Dictionary<float, float?> measuredLayFloor = new Dictionary<float, float?>();

        private static PartShot CapsShot(PartShot shot, Transform root, Vector3 eyes, string name)
        {
            var readsAt = new Dictionary<float, bool>();
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
            // The shelf's placing frame too (XR, 2026-11-13T2027Z: it laid wider than the record while placing): the frame the plane shows
            // while a shelf is placed, at every width, so the record holds what placing lays.
            // The shelf's placing page in the shape the plane hands it (XR's a46dc4651 and 56e6726e2: two instruction lines, the moves in
            // pairs, Find Room Surface alone; SCREENS's 52-general-shelf), not the older narrow copy: the plane at rest while placing.
            var placing = new List<PageLine> { new PageLine("Hold Move Shelf to Drag", tone: LineTone.Secondary), new PageLine("Apply for This Session", tone: LineTone.Secondary) };
            foreach (var (pairLeft, pairRight) in GeneralShelfPairs)
            {
                placing.Add(new PageLine(pairLeft, action: "move", key: pairLeft, control: LineControl.Action).WithGroup(pairLeft));
                placing.Add(new PageLine(pairRight, action: "move", key: pairRight, control: LineControl.Action).WithGroup(pairLeft));
            }
            placing.Add(new PageLine("Find Room Surface", action: "read-room", control: LineControl.Action));
            var shelf = new MenuFrame("Shelf", new Footer(new Prompt(Footer.Close, "Cancel", GlazeIcon.Close, PromptKind.Close),
                farRight: new Prompt("apply", "Apply", GlazeIcon.Approve, main: true)), lines: placing);
            foreach (var (capsFrame, width) in new[] { frame, recap, shelf }.SelectMany(each => TableWidthsDp.Select(width => (each, width))))
            {
                RoomBar? bar;
                RoomDesign.Enabled = true;
                try { bar = RoomBar.OfFrame(capsFrame, VoiceStage.Idle, new RoomForm(false, RoomRows.APage, panelDp: width)); }
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
                // The shelf's placing page at the floor's width, where the plane lays it while a shelf is placed: the plane at rest (XR's 01:15Z).
                if (ReferenceEquals(capsFrame, shelf) && Mathf.Abs(width - PlaneWidth.FloorDp(GlazeText.Scale)) < 0.5f) restingViews.Add(view);
                // A width under the floor the plane never lays (PlaneWidth.FloorDp): recorded for the envelope, never judged as seen.
                if (width < PlaneWidth.FloorDp(GlazeText.Scale) - 0.5f) recordOnly.Add(plane.gameObject);
                Debug.Log("Halcyonic: parts: " + name + ": caps of " + capsFrame.Subject + " at " + width.ToString("0", CultureInfo.InvariantCulture) + " dp: " + (view.Size / RoomType.Units(1f)).ToString("0") + " dp laid.");
                // The as-seen floor beside the width table's (the lead's 02:54Z (A)): the smallest word of the caps at every width, under the
                // floor too, so the floor lowered to the measure is the narrowest width where every screen lays whole AND reads at 14 dp.
                var seenCaps = Halcyonic.XR.UI.Editor.GlazeChecks.TextSeen(plane.gameObject, eyes);
                readsAt[width] = (!readsAt.TryGetValue(width, out var readSoFar) || readSoFar) && (seenCaps.Count == 0 || seenCaps[0].Degrees >= Glaze.MinimumTextDegrees - 0.0005f);
                if (seenCaps.Count > 0)
                    Debug.Log("Halcyonic: parts: " + name + ": caps of " + capsFrame.Subject + " at " + width.ToString("0", CultureInfo.InvariantCulture) + " dp: the smallest word as the eyes see it "
                        + seenCaps[0].Degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees (" + (seenCaps[0].Degrees >= Glaze.MinimumTextDegrees - 0.0005f ? "at or over" : "UNDER") + " the 14 dp floor).");
            }
            // The floor by both of its measures, held EQUAL to the plane's (the lead's 2026-11-14T02:54Z (A)): the narrowest width at and above
            // which every caps word reads at 14 dp as the eyes see it, and the width table's (every main screen whole), whichever is wider.
            float? seenFloor = null;
            foreach (var width in TableWidthsDp.OrderByDescending(each => each))
            {
                if (!readsAt.TryGetValue(width, out var reads) || !reads) break;
                seenFloor = width;
            }
            Debug.Log("Halcyonic: parts: " + name + ": every caps word reads at 14 dp as the eyes see it down to " + (seenFloor is float seenAt ? seenAt.ToString("0", CultureInfo.InvariantCulture) + " dp" : "NO width") + ".");
            Require(seenFloor != null, "at no width does every caps word read at 14 dp as the eyes see it");
            // A whole run measures the width table first, or the equality would pass unread (AUDIT's 05:41Z MUST); only a run narrowed to
            // other states may skip it, and says so.
            var layMeasured = measuredLayFloor.TryGetValue(GlazeText.Scale, out var layFloor) && layFloor != null;
            if (!layMeasured)
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HALCYONIC_KIT_ONLY")))
                    Require(false, "the width table's floor was not measured before the caps, so the floor by both measures was not held equal to the plane's");
                else Debug.Log("Halcyonic: parts: " + name + ": a narrowed run without the width table: the floor by both measures is not held equal here.");
            }
            if (layMeasured && layFloor is float lay && seenFloor is float seen)
            {
                var byMeasure = Mathf.Max(lay, seen);
                var plane = PlaneWidth.FloorDp(GlazeText.Scale);
                Debug.Log("Halcyonic: parts: " + name + ": the floor by measure " + byMeasure.ToString("0", CultureInfo.InvariantCulture) + " dp (every screen whole down to "
                    + lay.ToString("0", CultureInfo.InvariantCulture) + ", every word at 14 dp down to " + seen.ToString("0", CultureInfo.InvariantCulture) + "); the plane's " + plane.ToString("0", CultureInfo.InvariantCulture) + " dp.");
                Require(Mathf.Abs(byMeasure - plane) < 0.5f, "the floor by measure, " + byMeasure.ToString("0", CultureInfo.InvariantCulture) + " dp, is not the plane's "
                    + plane.ToString("0", CultureInfo.InvariantCulture) + " dp (PlaneWidth)");
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
