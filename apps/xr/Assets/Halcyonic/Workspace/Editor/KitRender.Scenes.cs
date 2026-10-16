#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The kit fixture's synthetic work. Every project, task, message, event and draft here is invented for the
    /// renders and says so on every image; nothing comes from a repository, a runtime or a journal on this computer.
    /// The activity is made the way real activity is: synthetic journal events, recorded through the production
    /// <see cref="ActivityLog"/>, so every line's words are the production derivation's own.
    /// </summary>
    public static partial class KitRender
    {
        private const string SelectedProject = "Storefront API";
        private const string SelectedTask = "Add rate limiting to the sign-in endpoint";
        private const string WaitingTask = "Update the payment webhook retries";
        private const string FinishedTask = "Paginate the order history endpoint";

        /// <summary>The agent's report: long on purpose, about a hundred words, a claim the evidence beside it does not settle.</summary>
        private const string Report = "I found the cause. Sign-in attempts from one network address share a single counter, so two people "
            + "behind the same office router lock each other out after five tries. I separated the account limit from the "
            + "address limit and kept the address limit at twenty attempts per minute. The new regression test signs in two "
            + "accounts from one address; it still fails because the address window resets one second late. I have not "
            + "changed the session store. I think the remaining failure is a timing boundary in the test clock, not in the "
            + "limiter, but I have not confirmed that.";

        private const string Draft = "Keep the address limit at twenty a minute. Fix the window boundary, then run the sign-in "
            + "tests again and tell me the exact result.";

        private const string Day = "2026-10-16T";

        private static CharacterPresentation Presentation(string id, string title, CharacterActivity activity, AttentionLevel attention = AttentionLevel.None, int approvals = 0) =>
            new CharacterPresentation(id, title, activity, StateLanguage.WordOf(StateLanguage.StateOf(activity, attention)), attention,
                approvals > 0 ? new[] { "It wants to run a command." } : new string[0], approvals, false, false, false);

        private static StateBadge Badge(CharacterActivity activity, AttentionLevel attention = AttentionLevel.None, int approvals = 0) =>
            StateLanguage.BadgeOf(Presentation("synthetic", "synthetic", activity, attention, approvals));

        /// <summary>The selected task's synthetic journal, recorded through the production activity log, with each entry's kind icon and recorded outcome.</summary>
        private static (IReadOnlyList<KitTrailEntry> Trail, ActivityEntry Report) SelectedActivity()
        {
            var log = new ActivityLog();
            var stored = new List<StoredEvent>();
            var outcomes = new Dictionary<long, (GlazeIcon Icon, GlazeTone Tone)>();
            long position = 0;
            void Add(EventEnvelope envelope, string at, GlazeIcon icon, GlazeTone tone = GlazeTone.Neutral)
            {
                position++;
                envelope.EventId = "synthetic-event-" + position;
                envelope.OccurredAt = Day + at + ".000Z";
                envelope.IngestedAt = Day + at + ".100Z";
                envelope.ProjectId = "synthetic-project-storefront";
                envelope.WorkstreamId = "synthetic-task-rate-limit";
                envelope.ExecutionId = "synthetic-execution-rate-limit";
                envelope.Source = new RuntimeSource { RuntimeId = "synthetic-runtime" };
                stored.Add(new StoredEvent { Position = position, Event = envelope });
                outcomes[position] = (icon, tone);
            }
            Add(new RuntimeTurnStartedEvent { Provenance = new ObservedProvenance(), Payload = new RuntimeTurnStartedPayload { TurnId = "synthetic-turn" } }, "09:00:01", GlazeIcon.StartBuilding);
            Add(Tool("read-1", "read", "src/auth/sign-in.ts"), "09:00:04", GlazeIcon.TextFile);
            Add(Tool("read-2", "read", "src/auth/rate-limit.ts"), "09:00:06", GlazeIcon.TextFile);
            Add(Tool("edit-1", "edit", "src/auth/rate-limit.ts"), "09:01:12", GlazeIcon.Change);
            Add(Tool("edit-2", "edit", "test/sign-in.test.ts"), "09:01:40", GlazeIcon.Change);
            Add(new RuntimeTestRunStartedEvent { Provenance = new ObservedProvenance(), Payload = new RuntimeTestRunStartedPayload { TestRunId = "synthetic-tests", Label = "sign-in rate limit" } }, "09:02:05", GlazeIcon.CheckingItsWork);
            Add(new RuntimeTestRunCompletedEvent { Provenance = new ObservedProvenance(), Payload = new RuntimeTestRunCompletedPayload { TestRunId = "synthetic-tests", Outcome = TestOutcome.Failed, Summary = "1 failed, 23 passed" } }, "09:02:31", GlazeIcon.ChecksFailed, GlazeTone.Failure);
            Add(new RuntimeAgentMessageEvent { Provenance = new ReportedProvenance(), Payload = new RuntimeAgentMessagePayload { Text = Report } }, "09:02:48", GlazeIcon.Quote);
            log.Record(stored);
            var entries = log.For("synthetic-execution-rate-limit");
            var report = entries.Last(entry => entry.Kind == ActivityKind.Message && entry.Reported);
            // Agent messages are read in the slate; the rail holds what was recorded.
            var trail = entries.Where(entry => entry.Kind != ActivityKind.Message)
                .Select(entry => new KitTrailEntry(entry, outcomes[entry.Position].Icon, outcomes[entry.Position].Tone)).ToList();
            return (trail, report);
        }

        private static RuntimeToolStartedEvent Tool(string id, string name, string title) => new RuntimeToolStartedEvent
        {
            Provenance = new ObservedProvenance(),
            Payload = new RuntimeToolStartedPayload { ToolCallId = "synthetic-" + id, ToolName = name, Title = title },
        };

        private static KitRowModel TaskRow(string key, string title, CharacterActivity activity, AttentionLevel attention = AttentionLevel.None,
            int approvals = 0, bool selected = false, string? next = null, bool opensMore = true, int lines = 2)
        {
            var badge = Badge(activity, attention, approvals);
            return new KitRowModel(key, LineWords.TheirName(title), badge.Icon, badge.Tone, badge.Text, badge.Tone,
                end: next, endTone: badge.State == WorkState.WaitingForYou ? GlazeTone.Attention : GlazeTone.Accent,
                opensMore: opensMore, selected: selected, wordLines: lines, isName: true);
        }

        /// <summary>The focused work: the selected task, its report, its recorded events, the person's draft.</summary>
        private static KitWorkModel WorkModel()
        {
            var (trail, report) = SelectedActivity();
            var spine = new[]
            {
                new KitSpineSection(new[]
                {
                    // Where space is fixed, the short count that keeps its noun (WORDS.md).
                    new KitRowModel("place:projects", LineWords.Ours("Projects"), GlazeIcon.Folder, detail: "1 task waiting", detailTone: GlazeTone.Attention),
                }),
                new KitSpineSection(new[]
                {
                    // The spine's rows are a list of where to go: the chosen one lit, no chevrons, a name in at most two
                    // lines, cut inside itself beyond that; the chosen task's whole name heads the slate.
                    TaskRow("task:rate-limit", SelectedTask, CharacterActivity.Working, selected: true, opensMore: false),
                    TaskRow("task:webhook", WaitingTask, CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1, opensMore: false),
                    TaskRow("task:pagination", FinishedTask, CharacterActivity.TurnFinished, opensMore: false),
                }, SelectedProject, headingIsName: true),
            };
            return new KitWorkModel(SelectedProject, SelectedTask, Badge(CharacterActivity.Working), new WorkMark[0], report, trail,
                Draft, voiceOffered: true, sendable: true, whyNotSend: null, spine, spineFoot: null);
        }

        /// <summary>Three projects with running, waiting and failed work, each failed or waiting task with its next action.</summary>
        private static KitOverviewModel OverviewModel()
        {
            var projects = new[]
            {
                new KitProjectModel("project:storefront", SelectedProject, "1 task waiting for you, 1 to look at, 1 running", new[]
                {
                    TaskRow("task:webhook", WaitingTask, CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1, next: "Review request"),
                    TaskRow("task:rate-limit", SelectedTask, CharacterActivity.Working),
                    TaskRow("task:pagination", FinishedTask, CharacterActivity.TurnFinished, next: "Read results"),
                }),
                new KitProjectModel("project:search", "Search service", "2 tasks to look at", new[]
                {
                    TaskRow("task:stale-index", "Fix the stale index after deletes", CharacterActivity.Failed, next: "Read what happened"),
                    TaskRow("task:indexing", "Check search indexing", CharacterActivity.TurnFinished, next: "Read results"),
                }),
                new KitProjectModel("project:field-notes", "Field notes app", "1 task running", new[]
                {
                    TaskRow("task:offline", "Add offline sync for drafts", CharacterActivity.Working),
                }),
            };
            var spine = new[]
            {
                new KitSpineSection(new[]
                {
                    new KitRowModel("place:projects", LineWords.Ours("Projects"), GlazeIcon.Folder, selected: true, opensMore: false),
                    new KitRowModel("action:new-project", LineWords.Ours("New project"), GlazeIcon.CreateProject),
                }),
            };
            return new KitOverviewModel("Projects", "1 task is waiting for you, 1 couldn't finish this round, and 2 are running.", projects, spine,
                new KitRowModel("place:settings", LineWords.Ours("Settings"), GlazeIcon.Settings));
        }

        private static KitPresenceModel PresenceModel() => new KitPresenceModel(SelectedTask, Badge(CharacterActivity.Working), waiting: 1);

        /// <summary>
        /// The control kit's specimen on one opaque sheet, for what the three states do not show: the connected switch
        /// in each of its states (at rest on either position, the other position pointed at, and mid-move as one
        /// stretched shape), the decision bar before and after its request has been read, and every state's signal.
        /// Rows, chips, buttons and the composer are reviewed in the states themselves.
        /// </summary>
        private static Shot ControlKit(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "Control kit specimen, synthetic data" };
            var sheet = new GameObject("Control kit").transform;
            sheet.SetParent(root, false);
            var carrier = KitCarrier.Create(sheet, "Sheet", KitType.Order.Carrier + 3);
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var width = PlaneComposition.Units(60f);
            var inner = width - 2f * pad;
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var targets = new List<KitPress>();
            var x0 = -width / 2f + pad;
            var y = -pad;
            void Heading(string words)
            {
                var label = KitType.Create(carrier.transform, words, KitRole.Heading, KitType.Secondary, KitType.Order.Words);
                GlazeText.SetLiteral(label, words);
                var (_, _, h) = KitType.Lay(label, inner, 1);
                label.transform.localPosition = new Vector3(x0, y, -0.0005f);
                y -= h + KitType.Scaled(0.5f);
            }

            // The connected switch, in each of its states.
            Heading("Connected switch");
            var captions = new[] { "On Workspace", "Compact pointed at", "Moving to Compact", "On Compact" };
            var probe = KitModeSwitch.Create(carrier.transform, "Switch 0");
            var switchWidth = probe.NarrowestWidth();
            var stride = (inner - switchWidth) / 3f;
            var switches = new[] { probe, KitModeSwitch.Create(carrier.transform, "Switch 1"), KitModeSwitch.Create(carrier.transform, "Switch 2"), KitModeSwitch.Create(carrier.transform, "Switch 3") };
            for (var index = 0; index < switches.Length; index++)
            {
                var size = switches[index].Layout(switchWidth);
                switches[index].transform.localPosition = new Vector3(x0 + index * stride + size.x / 2f, y - size.y / 2f, 0f);
                targets.Add(switches[index].WorkspaceTarget);
                targets.Add(switches[index].CompactTarget);
                var label = KitType.Create(carrier.transform, captions[index], KitRole.Body, KitType.Secondary, KitType.Order.Words);
                GlazeText.SetLiteral(label, captions[index]);
                KitType.Lay(label, stride, 1);
                label.transform.localPosition = new Vector3(x0 + index * stride, y - size.y - KitType.Scaled(0.35f), -0.0005f);
            }
            switches[1].PointForRender(workspacePosition: false);
            GlazeMotion.Still = false;
            switches[2].ShowConfirmed(true);
            switches[2].Advance(SpatialKit.SwitchSeconds * 0.5f);
            GlazeMotion.Still = true;
            switches[3].ShowConfirmed(true, immediately: true);
            y -= switches[0].Size.y + KitType.Scaled(0.35f) + KitType.LinePitch(probe.WorkspaceLabel) + KitType.Scaled(1.2f);

            // The decision bar, before and after its request has been read: Yes stands only after.
            Heading("Decision bar");
            var unread = KitDecisionBar.Create(carrier.transform, "Decision bar, unread");
            y -= unread.Show(new Vector2(x0, y), inner, "Allow the command above?", "Yes, allow", read: false, "Read the whole request first.") + gap;
            var read = KitDecisionBar.Create(carrier.transform, "Decision bar, read");
            y -= read.Show(new Vector2(x0, y), inner, "Allow the command above?", "Yes, allow", read: true, "Read the whole request first.") + KitType.Scaled(1.2f);
            foreach (var bar in new[] { unread, read })
                foreach (var each in new[] { bar.Close, bar.Cancel, bar.Yes }) if (each.gameObject.activeSelf) targets.Add(each.Press);

            // Every state's signal on its pill, a last-known one at the end.
            Heading("Every state");
            var states = new (CharacterActivity Activity, AttentionLevel Attention, int Approvals, bool Stale)[]
            {
                (CharacterActivity.Idle, AttentionLevel.None, 0, false), (CharacterActivity.Starting, AttentionLevel.None, 0, false),
                (CharacterActivity.Working, AttentionLevel.None, 0, false), (CharacterActivity.Verifying, AttentionLevel.None, 0, false),
                (CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 2, false), (CharacterActivity.TurnFinished, AttentionLevel.None, 0, false),
                (CharacterActivity.TurnFinished, AttentionLevel.Notice, 0, false), (CharacterActivity.Failed, AttentionLevel.None, 0, false),
                (CharacterActivity.Interrupted, AttentionLevel.None, 0, false), (CharacterActivity.Unknown, AttentionLevel.None, 0, false),
                (CharacterActivity.Working, AttentionLevel.None, 0, true),
            };
            var sx = x0;
            var lineHeight = 0f;
            foreach (var (activity, attention, approvals, stale) in states)
            {
                var signal = KitStateSignal.Create(carrier.transform, "State " + activity);
                var badge = StateLanguage.BadgeOf(new CharacterPresentation("specimen", "specimen", activity,
                    StateLanguage.WordOf(StateLanguage.StateOf(activity, attention)), attention,
                    approvals > 0 ? Enumerable.Repeat("It wants to run a command.", approvals).ToArray() : new string[0], approvals, false, false, stale));
                var size = signal.Show(badge, onPill: true);
                if (sx + size.x > x0 + inner)
                {
                    sx = x0;
                    y -= lineHeight + KitType.Scaled(0.6f);
                    lineHeight = 0f;
                }
                signal.transform.localPosition = new Vector3(sx + size.x / 2f, y - size.y / 2f, -0.0003f);
                sx += size.x + KitType.Scaled(0.8f);
                lineHeight = Mathf.Max(lineHeight, size.y);
            }
            var height = -(y - lineHeight) + pad;
            carrier.Draw(new Vector2(width, height), KitType.Units(SpatialKit.InstrumentRadiusDegrees), SpatialKit.Slate, lit: true);
            // Everything above was laid from the sheet's top; the sheet is centred on its own transform.
            foreach (Transform child in carrier.transform)
                if (child.name != "Opaque face" && child.name != "Lit top edge") child.localPosition += new Vector3(0f, height / 2f, 0f);
            shot.Elevation = CentreFor(height);
            shot.Centre = Place(sheet, eyes, 0f, shot.Elevation);
            shot.PitchDown = PitchFor(new Vector2(width, height), shot.Elevation);
            shot.Targets.Add(("specimen", sheet, targets));
            shot.Faces.Add(carrier.Face);
            shot.Corners.AddRange(carrier.Corners());
            return shot;
        }
    }
}
