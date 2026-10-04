#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Usage, a place of the menu (ADR 0026): each limit a row, whose and which window, its share left
    /// the small fact, "At most 60% left", in words and never a meter, and a chevron; chosen, its side
    /// panel says when it was seen, when it resets and whose account it is, with the source, and paging
    /// waits while it is chosen. Its page's source line counts as one of its rows. Read from the
    /// computer when it opens and when the person presses Refresh, never on a timer; in the recorded
    /// demonstration, the recording's limits, with no Refresh, and the Account fact says they are part
    /// of the recording. A share is the most that was left when seen, never what is left now.
    /// </summary>
    public sealed class UsageColumn : IMenuColumn
    {
        /// <summary>What pressing a limit's row raises, with its place on the list as the key.</summary>
        public const string OpenLimit = "usage-open-limit";

        private readonly IMenuHost host;
        private readonly Func<DateTimeOffset, AvailableUsageLimits?> recorded;
        private UsageLeftPresentation? shown;
        private Task<UsageLimitsResponse>? reading;
        private CancellationTokenSource? cancel;
        private int? chosen;
        private int page;
        private int minute = -1;

        /// <param name="recorded">The demonstration's limits as if read at a time (<see cref="DemonstrationRecording.UsageLimitsAt"/>), or null where it holds none.</param>
        public UsageColumn(IMenuHost host, Func<DateTimeOffset, AvailableUsageLimits?> recorded)
        {
            this.host = host;
            this.recorded = recorded;
            Read();
        }

        public event Action? Changed;

        public event Action? Closed;

        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, UsageText.Close, GlazeIcon.Close, PromptKind.Close);
                var refresh = host.Demonstration ? null
                    : new Prompt(UsageLeftScreens.Refresh, WorkspaceText.Refresh, GlazeIcon.Refresh, available: reading == null, reason: reading == null ? null : UsageLeftPresenter.Reading);
                var presentation = shown;
                if (presentation == null || presentation.Rows.Count == 0)
                {
                    var words = presentation?.Note ?? UsageLeftPresenter.Reading;
                    var tone = presentation?.Failed == true ? LineTone.Problem : LineTone.Secondary;
                    return new MenuFrame(UsageText.Subject, new Footer(close, rare: refresh), lines: new[] { new PageLine(words, tone: tone, rows: 2) });
                }

                var rows = host.PageRows(sourceLine: true);
                var pages = Math.Max(1, (presentation.Rows.Count + rows - 1) / rows);
                if (chosen is int index && index < presentation.Rows.Count) page = index / rows;
                else chosen = null;
                if (page >= pages) page = 0;
                var lines = presentation.Rows.Select((row, at) => (row, at)).Skip(page * rows).Take(rows)
                    .Select(each => new PageLine(each.row.Title, wordsAreData: true, fact: UsageText.Left(each.row.Left), action: OpenLimit,
                        key: each.at.ToString(CultureInfo.InvariantCulture), opens: true, chosen: each.at == chosen))
                    .ToList();
                var footer = new Footer(close, rare: refresh);
                // Paging waits while a row is chosen.
                if (pages > 1 && chosen == null)
                {
                    footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage));
                }
                var side = chosen is int open ? Side(presentation, presentation.Rows[open]) : null;
                return new MenuFrame(UsageText.Subject, footer, lines: lines, source: presentation.Source, side: side);
            }
        }

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case OpenLimit when int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && shown != null && index < shown.Rows.Count:
                    chosen = chosen == index ? (int?)null : index;
                    Changed?.Invoke();
                    break;
                case SidePanel.Close when chosen != null:
                    chosen = null;
                    Changed?.Invoke();
                    break;
                case UsageLeftScreens.Refresh when !host.Demonstration && reading == null:
                    Read();
                    break;
                case Footer.NextPage when chosen == null:
                    page++;
                    Changed?.Invoke();
                    break;
                case Footer.Close:
                    cancel?.Cancel();
                    Closed?.Invoke();
                    break;
            }
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
        }

        public void HoldStarted(string id)
        {
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
        }

        public void Said(string words)
        {
        }

        /// <summary>Takes a read that came back, and says the times again each minute, as "today at 15:18" stays true.</summary>
        public void Tick()
        {
            if (reading is Task<UsageLimitsResponse> pending && pending.IsCompleted)
            {
                reading = null;
                shown = pending.Status == TaskStatus.RanToCompletion
                    ? UsageLeftPresenter.Present(pending.Result, host.Clock, host.Zone)
                    : pending.IsCanceled ? shown : UsageLeftPresenter.Unreachable();
                minute = host.Clock.Minute;
                Changed?.Invoke();
                return;
            }
            if (host.Clock.Minute != minute && reading == null && !host.Demonstration && shown != null)
            {
                minute = host.Clock.Minute;
                Changed?.Invoke();
            }
        }

        public void FocusLeft()
        {
        }

        /// <summary>Reads the limits: the recording's in the demonstration, else the computer's.</summary>
        private void Read()
        {
            if (host.Demonstration)
            {
                var limits = recorded(host.Clock);
                shown = limits == null ? UsageLeftPresenter.Message(UsageLeftPresenter.NotInDemo) : UsageLeftPresenter.Present(limits, host.Clock, host.Zone, recorded: true);
                Changed?.Invoke();
                return;
            }
            if (host.Api is not ControlPlaneApi api)
            {
                shown = UsageLeftPresenter.Unreachable();
                Changed?.Invoke();
                return;
            }
            cancel?.Cancel();
            cancel = new CancellationTokenSource();
            reading = api.GetUsageLimitsAsync(cancel.Token);
            Changed?.Invoke();
        }

        /// <summary>What a chosen limit's side panel says: when it was seen, when it resets, and whose account it is.</summary>
        private SidePanel Side(UsageLeftPresentation presentation, UsageLeftRow row) => new SidePanel(row.Title, subjectIsData: true, facts: new[]
        {
            new SideFact(UsageText.Seen, UsageText.Sentence(row.Seen)),
            new SideFact(UsageText.Resets, UsageText.Sentence(row.Resets)),
            new SideFact(UsageText.Account, host.Demonstration ? UsageText.PartOfTheRecording : UsageText.NotIdentified),
        }, source: presentation.Source);
    }

    /// <summary>Usage's own words (WORDS.md, ADR 0026).</summary>
    public static class UsageText
    {
        public const string Close = "Close";

        public const string Subject = "How much is left before each limit?";

        public const string Seen = "Seen";

        public const string Resets = "Resets";

        public const string Account = "Account";

        /// <summary>A limit's Account fact in the demonstration, in place of who it belongs to.</summary>
        public const string PartOfTheRecording = "Part of the recording";

        /// <summary>A limit's Account fact: the source cannot tell accounts apart.</summary>
        public const string NotIdentified = "Not identified: it may be any account used on " + HostText.Your;

        /// <summary>A limit's share left as its small fact, rounded up so "at most" stays true.</summary>
        public static string Left(int left) => "At most " + left.ToString(CultureInfo.InvariantCulture) + "% left";

        /// <summary>A time as a fact's value, starting as a sentence does: "Today at 15:18".</summary>
        public static string Sentence(string when) => when.Length == 0 ? when : char.ToUpperInvariant(when[0]) + when.Substring(1);
    }
}
