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

        /// <summary>
        /// The menu's Compact (the addendum's A15), offered under the redesign alone, never the main action: the director
        /// takes it and clears the room to the characters, the way back being a character picked.
        /// </summary>
        public const string Compact = "menu-compact";

        /// <summary>The Assistant row's press, the host's entry Use Model (<see cref="AssistantPlace.UseModelCommand"/>): opens the host's list.</summary>
        public const string UseModel = "usage-use-model";

        /// <summary>A row of the host's list pressed, its index the key: sends Use Model for it, and its review follows.</summary>
        public const string ChooseModel = "usage-choose-model";

        /// <summary>Back from the host's list to the usage card.</summary>
        public const string Back = "usage-back";

        /// <summary>The list's words while the computer is read, and while a press waits for the computer's review or its result.</summary>
        public const string ReadingModels = "Reading Models", Asking = "Asking the Computer", Choosing = "Choosing Model";

        /// <summary>Said where the computer did not take the press and gave no words of its own (unknown stays unknown).</summary>
        public const string NotTaken = "Not Taken";

        /// <summary>How long the column waits between reads of a press's command and request, in seconds.</summary>
        private const double ReadEvery = 0.25;

        /// <summary>The most usage windows the redesign's card holds (FIELD's two wells); more, and the older view draws the page.</summary>
        private const int CardWindows = 2;

        private readonly IMenuHost host;
        private readonly Func<DateTimeOffset, AvailableUsageLimits?> recorded;
        private UsageLeftPresentation? shown;
        private Task<UsageLimitsResponse>? reading;

        /// <summary>The assistant's status as the computer last said it (GET /api/workspace/agent), read beside the limits; null until read.</summary>
        private WorkspaceAgentStatus? agent;
        private Task<WorkspaceAgentStatus>? agentReading;
        private CancellationTokenSource? cancel;
        private int? chosen;
        private int page;
        private int detailPart;
        private int detailParts;
        private int minute = -1;

        /// <summary>Use Model, where the person stands in it: none (the usage card), the host's list, the press sent, its review, its confirm sent.</summary>
        private enum Stage { None, List, Sent, Review, Confirmed }

        private readonly CommandFactory? commands;
        private Stage stage;
        private Task<ChoicesResponse>? modelsReading;
        private ChoicesResponse? models;
        private bool modelsFailed;
        private AssistantOption? pressed;
        private Task<CommandAckMessage>? sending;
        private string? sentId;
        private Task<WorkspaceCommandResponse>? commandReading;
        private string? requestId;
        private Task<WorkspaceRequestResponse>? requestReading;
        private WorkspaceRequestStep? review;
        private int reviewPart;
        private double readAt;

        /// <summary>The host's own words where it did not take the press, or <see cref="NotTaken"/>; shown on the list until another press.</summary>
        private string? refused;

        /// <param name="recorded">The demonstration's limits as if read at a time (<see cref="DemonstrationRecording.UsageLimitsAt"/>), or null where it holds none.</param>
        /// <param name="commands">Makes Use Model's press and its confirm; null where the host has none, and the row then presses nothing.</param>
        public UsageColumn(IMenuHost host, Func<DateTimeOffset, AvailableUsageLimits?> recorded, CommandFactory? commands = null)
        {
            this.host = host;
            this.recorded = recorded;
            this.commands = commands;
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
                var compact = RoomDesign.Enabled ? new Prompt(Compact, WorkspaceText.Compact, GlazeIcon.Next) : null;
                if (RoomDesign.Enabled && stage != Stage.None) return ChoiceFrame();
                var presentation = shown;
                if (presentation == null || presentation.Rows.Count == 0)
                {
                    var words = presentation?.Note ?? UsageLeftPresenter.Reading;
                    var tone = presentation?.Failed == true ? LineTone.Problem : LineTone.Secondary;
                    var empty = new MenuFrame(UsageText.Subject, new Footer(close, rare: refresh, secondary: compact), lines: new[] { new PageLine(words, tone: tone, rows: 2) });
                    // The redesign's menu (A4): the panel's one line, the product's own, on the usage card's title; with the assistant's
                    // row known, the card stands for that row (FIELD's Usage with no windows).
                    if (RoomDesign.Enabled && AssistantRow() is RoomListRow alone && RoomPanelCards.Usage(Array.Empty<UsageLeftRow>(), alone) is RoomCard rowCard)
                        return empty.WithCard(rowCard);
                    return RoomDesign.Enabled ? empty.WithCard(new RoomCard(LineWords.Ours(RoomPanelCards.UsageTitle), LineWords.Ours(words))) : empty;
                }

                // Named organization controls narrow the heading; count its actual rows before packing.
                // The source remains on every page and takes its own measured space.
                var subjectRows = host.TitleRows(UsageText.Subject, Glaze.Menu.MenuColumnDegrees);
                var room = new HeightBudget(host.PageHeight(subjectRows, besideMenu: true)).Room;
                var most = host.PageRows(sourceLine: true);
                var rows = Math.Max(1, Enumerable.Range(1, most).LastOrDefault(count => MenuPage.Rows(count) <= room + 1e-6f));
                var pages = Math.Max(1, (presentation.Rows.Count + rows - 1) / rows);
                if (chosen is int index && index < presentation.Rows.Count) page = index / rows;
                else chosen = null;
                if (page >= pages) page = 0;
                var lines = presentation.Rows.Select((row, at) => (row, at)).Skip(page * rows).Take(rows)
                    .Select(each => new PageLine(each.row.TitleRuns, fact: LineWords.Ours(UsageText.Left(each.row.Left)), action: OpenLimit,
                        key: each.at.ToString(CultureInfo.InvariantCulture), opens: true, chosen: each.at == chosen))
                    .ToList();
                var footer = new Footer(close, rare: refresh, secondary: compact);
                // Paging waits while a row is chosen.
                if (pages > 1 && chosen == null)
                {
                    footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage));
                }
                var side = chosen is int open ? Side(presentation, presentation.Rows[open]) : null;
                if (side?.Parts is (int part, int parts) && parts > 1)
                    footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(part, parts), GlazeIcon.Next, PromptKind.NextPage));
                var frame = new MenuFrame(UsageText.Subject, footer, lines: lines, source: presentation.Source, side: side);
                // The redesign's menu (A4): FIELD's usage card, its windows as wells, where it holds every window read; the
                // row's side panel is left out there, as the addendum says.
                return RoomDesign.Enabled && presentation.Rows.Count <= CardWindows ? frame.WithCard(RoomPanelCards.Usage(presentation.Rows, AssistantRow())) : frame;
            }
        }

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case UseModel when Offered && stage == Stage.None:
                    OpenList();
                    return;
                case ChooseModel when stage == Stage.List && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var row)
                    && AssistantPlace.Options(models).FirstOrDefault(option => option.Index == row) is AssistantOption option && option.Choosable:
                    Choose(option);
                    return;
                case ReviewCard.Confirm when stage == Stage.Review:
                    Confirm();
                    return;
                case ReviewCard.NextPart when stage == Stage.Review:
                    reviewPart++;
                    Changed?.Invoke();
                    return;
                case ReviewCard.PreviousPart when stage == Stage.Review && reviewPart > 0:
                    reviewPart--;
                    Changed?.Invoke();
                    return;
                case Back when stage == Stage.List || stage == Stage.Sent:
                case ReviewCard.Cancel when stage == Stage.Review:
                    // Nothing was confirmed: the request waits unconfirmed, and nothing changes where the assistant runs.
                    Done();
                    return;
                case OpenLimit when int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && shown != null && index < shown.Rows.Count:
                    chosen = chosen == index ? (int?)null : index;
                    detailPart = 0;
                    Changed?.Invoke();
                    break;
                case SidePanel.Close when chosen != null:
                    chosen = null;
                    detailPart = 0;
                    Changed?.Invoke();
                    break;
                case UsageLeftScreens.Refresh when !host.Demonstration && reading == null:
                    Read();
                    break;
                case Footer.NextPage when chosen != null && detailParts > 1:
                    detailPart = (detailPart + 1) % detailParts;
                    Changed?.Invoke();
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
            if (stage != Stage.None || modelsReading != null) TickChoice();
            if (agentReading is Task<WorkspaceAgentStatus> asked && asked.IsCompleted)
            {
                agentReading = null;
                // A read that failed says nothing of the assistant: no row is guessed (unknown stays unknown).
                agent = asked.Status == TaskStatus.RanToCompletion ? asked.Result : null;
                Changed?.Invoke();
            }
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
            // The assistant's model and where it runs, for the card's Assistant row (the lead's 16:46Z, FIELD's RoomPanelCards.Assistant).
            agentReading = api.GetWorkspaceAgentAsync(cancel.Token);
            Changed?.Invoke();
        }

        /// <summary>
        /// The assistant's row as the computer said it: its model and where it runs (the provider's own name where hosted), or the host's own
        /// words why it cannot run; none until read, or where the read failed. Use Model, its press, comes with the host's action (not yet).
        /// </summary>
        private RoomListRow? AssistantRow()
        {
            var row = AssistantPlace.Row(agent);
            if (row.Model == null && row.Reason == null) return null;
            // The press is the host's Use Model, offered only where the computer can be asked; the bar binds it to the frame drawn.
            var press = Offered ? new KitControl(KitPlace.Choice, Placeholder, UseModel, null, AssistantPlace.UseModelTitle, GlazeIcon.Next, true, null, false) : null;
            return RoomPanelCards.Assistant(row.Model, row.Provider, row.Reason, press);
        }

        /// <summary>Use Model is offered: the redesign, the computer's own (never the recording), and a way to send it.</summary>
        private bool Offered => RoomDesign.Enabled && !host.Demonstration && host.Api != null && commands != null;

        /// <summary>The frame a card's press is made with before the bar binds it to the frame drawn (<see cref="RoomBar"/>'s Menu).</summary>
        private static readonly MenuFrame Placeholder = new MenuFrame(UsageText.Subject, new Footer());

        /// <summary>
        /// Use Model's own frame under the redesign: the host's list as rows, each its place and a press where the host offers
        /// one (a row it does not shows its reason and presses nothing), the place it runs now marked; while the press waits, the
        /// list's card says so; then the ONE review card (<see cref="ReviewCard"/>) with its Confirm. Back, or Cancel, returns
        /// to the usage card.
        /// </summary>
        private MenuFrame ChoiceFrame()
        {
            var title = LineWords.Written(LabelText.Plain(AssistantPlace.UseModelTitle));
            if (stage == Stage.Review && review?.Proposal is WorkspaceRequestStepProposal proposal && proposal.Review != null)
            {
                var shown = ReviewOf(proposal);
                var parts = ReviewCard.Parts(shown);
                reviewPart = Math.Max(0, Math.Min(reviewPart, parts - 1));
                Prompt? confirm = null, cancel = null, next = null, previous = null;
                foreach (var action in ReviewCard.Actions(shown, reviewPart))
                {
                    // Under the redesign the bar reads these (RoomBar.Menu): Confirm the main action at the far right, Cancel the way out.
                    if (action.Id == ReviewCard.Confirm) confirm = new Prompt(action.Id, action.Words, GlazeIcon.Next, main: true);
                    else if (action.Id == ReviewCard.Cancel) cancel = new Prompt(action.Id, action.Words, GlazeIcon.Close, PromptKind.Close);
                    else if (action.Id == ReviewCard.NextPart) next = new Prompt(action.Id, action.Words, GlazeIcon.Next);
                    else previous = new Prompt(action.Id, action.Words, GlazeIcon.Back);
                }
                return new MenuFrame(UsageText.Subject, new Footer(cancel, rare: previous, secondary: next, farRight: confirm))
                    .WithCard(ReviewCard.Card(shown, AssistantPlace.UseModelTitle, reviewPart));
            }
            var back = new Prompt(Back, EntryText.Back, GlazeIcon.Back, PromptKind.Close);
            var footer = new Footer(back);
            var options = AssistantPlace.Options(models);
            if (stage != Stage.List || options.Count == 0)
            {
                var words = stage == Stage.Sent ? Asking : stage == Stage.Confirmed ? Choosing : modelsFailed ? UsageLeftPresenter.Unreachable().Note : ReadingModels;
                return new MenuFrame(UsageText.Subject, footer).WithCard(new RoomCard(title, LineWords.Ours(words)));
            }
            var here = AssistantPlace.Row(agent);
            var rows = options.Select(option => new RoomListRow(
                option.Theirs ? RoomKind.Link : RoomKind.Computer,
                option.Theirs ? LineWords.TheirName(option.Label) : LineWords.Written(LabelText.Plain(option.Label)),
                press: option.Choosable
                    ? new KitControl(KitPlace.Choice, Placeholder, ChooseModel, option.Index.ToString(CultureInfo.InvariantCulture), option.Label, GlazeIcon.Next, true, null, false, Runs(option, here))
                    : null,
                chosen: Runs(option, here),
                under: option.Detail == null ? null : option.Theirs || option.Choosable ? DetailOf(option) : LineWords.Written(LabelText.Plain(option.Detail)))).ToList();
            return new MenuFrame(UsageText.Subject, footer)
                .WithCard(new RoomCard(title, refused == null ? null : LineWords.Written(LabelText.Plain(refused)), list: rows.Take(RoomCard.ListAPage).ToList()));
        }

        /// <summary>The row the assistant runs on now: this computer's where it runs here, else the hosted model of that name.</summary>
        private static bool Runs(AssistantOption option, AssistantRow here) =>
            here.Model != null && (option.Theirs ? here.Provider != null && option.Label == here.Model : here.OnThisComputer);

        /// <summary>A row's second line: this computer's model's name (theirs), or the host's line naming where it runs.</summary>
        private static LineWords DetailOf(AssistantOption option) =>
            !option.Theirs ? LineWords.TheirName(option.Detail) : LineWords.Written(LabelText.Plain(option.Detail!));

        /// <summary>
        /// The review the step waits on, as the one review card reads it: the host's line of what happens and the words that go
        /// out, its digest carried back by Confirm. The step does not carry the host's own confirm words, facts or warnings
        /// (LOOP, asked 2026-11-13): Confirm says the entry's title.
        /// </summary>
        private static ActionReview ReviewOf(WorkspaceRequestStepProposal proposal) => new ActionReview
        {
            CommandType = proposal.CommandType,
            Digest = proposal.Review!,
            ConfirmWords = AssistantPlace.UseModelTitle,
            WhatHappens = proposal.WhatHappens,
            Said = proposal.Said,
        };

        /// <summary>Opens the host's list, read afresh each time it opens (never cached: the host decides what may be chosen).</summary>
        private void OpenList()
        {
            if (host.Api is not ControlPlaneApi api) return;
            stage = Stage.List;
            models = null;
            modelsFailed = false;
            refused = null;
            modelsReading = api.GetAssistantModelsAsync(cancel?.Token ?? default);
            Changed?.Invoke();
        }

        /// <summary>A row of the list pressed: Use Model sent for it, as any command, its review read from the request it makes.</summary>
        private void Choose(AssistantOption option)
        {
            if (commands == null || models == null) return;
            var command = commands.UseModel(option.Index, models.Digest);
            if (host.Submit(command) is not Task<CommandAckMessage> ack)
            {
                refused = NotTaken;
                Changed?.Invoke();
                return;
            }
            pressed = option;
            sentId = command.CommandId;
            sending = ack;
            requestId = null;
            review = null;
            reviewPart = 0;
            refused = null;
            stage = Stage.Sent;
            Changed?.Invoke();
        }

        /// <summary>The review's one press: its step confirmed with the review's digest; the row reads the computer again once it is done.</summary>
        private void Confirm()
        {
            if (commands == null || requestId == null || review?.Proposal?.Review is not string digest) return;
            var command = commands.ConfirmRequest(requestId, review.StepId, digest);
            if (host.Submit(command) is not Task<CommandAckMessage> ack)
            {
                Refused(NotTaken);
                return;
            }
            sentId = command.CommandId;
            sending = ack;
            stage = Stage.Confirmed;
            readAt = host.Now + ReadEvery;
            Changed?.Invoke();
        }

        /// <summary>Back to the list with the host's words, or <see cref="NotTaken"/>, and the list read again.</summary>
        private void Refused(string? words)
        {
            var said = string.IsNullOrWhiteSpace(words) ? NotTaken : words!.Trim();
            OpenList();
            refused = said;
            Changed?.Invoke();
        }

        /// <summary>Leaves Use Model for the usage card, and reads where the assistant runs again.</summary>
        private void Done()
        {
            stage = Stage.None;
            sending = null;
            commandReading = null;
            requestReading = null;
            review = null;
            requestId = null;
            pressed = null;
            if (host.Api is ControlPlaneApi api) agentReading = api.GetWorkspaceAgentAsync(cancel?.Token ?? default);
            Changed?.Invoke();
        }

        /// <summary>Use Model's reads, once a frame: the list, the press's acknowledgement, its command, then its request.</summary>
        private void TickChoice()
        {
            if (modelsReading is Task<ChoicesResponse> list && list.IsCompleted)
            {
                modelsReading = null;
                models = list.Status == TaskStatus.RanToCompletion ? list.Result : null;
                modelsFailed = models == null;
                Changed?.Invoke();
            }
            if (sending is Task<CommandAckMessage> ack && ack.IsCompleted)
            {
                sending = null;
                var taken = ack.Status == TaskStatus.RanToCompletion ? ack.Result : null;
                if (taken == null || taken.CommandId != sentId
                    || taken.Disposition != CommandAckDisposition.Accepted && taken.Disposition != CommandAckDisposition.Duplicate)
                {
                    Refused(taken?.Command?.Rejection?.Message);
                    return;
                }
                if (stage == Stage.Sent && taken.Command is CommandView view) Took(view);
                readAt = host.Now + ReadEvery;
            }
            if (host.Api is not ControlPlaneApi api || sending != null) return;
            if (commandReading is Task<WorkspaceCommandResponse> found && found.IsCompleted)
            {
                commandReading = null;
                if (found.Status == TaskStatus.RanToCompletion) Took(found.Result.Command);
                readAt = host.Now + ReadEvery;
            }
            if (requestReading is Task<WorkspaceRequestResponse> read && read.IsCompleted)
            {
                requestReading = null;
                if (read.Status == TaskStatus.RanToCompletion) Read(read.Result.Request);
                readAt = host.Now + ReadEvery;
            }
            if (host.Now < readAt || commandReading != null || requestReading != null) return;
            if (stage == Stage.Sent && requestId == null && sentId != null) commandReading = api.ReadWorkspaceCommandAsync(sentId, cancel?.Token ?? default);
            else if ((stage == Stage.Sent || stage == Stage.Confirmed) && requestId != null) requestReading = api.ReadWorkspaceRequestAsync(requestId, cancel?.Token ?? default);
        }

        /// <summary>The press's command as the computer says it: done, its request read next; refused or failed, the host's words.</summary>
        private void Took(CommandView command)
        {
            if (command.CommandId != sentId || stage != Stage.Sent) return;
            if (command.Status == CommandStatus.Completed && command.Result is WorkspaceRequestCreatedResult created) requestId = created.RequestId;
            else if (command.Status == CommandStatus.Rejected) Refused(command.Rejection?.Message);
            else if (command.Status == CommandStatus.Failed) Refused(command.Failure?.Message);
        }

        /// <summary>
        /// The request a press made: its review where a step waits on the person, and after the confirm, the request ended
        /// (the usage card again, its row read afresh) or failed (the host's words).
        /// </summary>
        private void Read(WorkspaceRequestView request)
        {
            if (request.RequestId != requestId) return;
            var status = request.Status.Type == Newtonsoft.Json.Linq.JTokenType.String ? request.Status.ToString() : null;
            var waiting = AssistantPlace.WaitingReview(request);
            if (stage == Stage.Sent && waiting != null)
            {
                review = waiting;
                reviewPart = 0;
                stage = Stage.Review;
                Changed?.Invoke();
            }
            else if (status == "failed" || status == "stopped" || status == "interrupted") Refused(request.Reply?.Text);
            else if (stage == Stage.Confirmed && waiting == null && status != "running" && status != "waiting_for_runtime") Done();
            else if (stage == Stage.Sent && status == "responded") Refused(request.Reply?.Text);
        }

        /// <summary>What a chosen limit's side panel says: when it was seen, when it resets, and whose account it is.</summary>
        private SidePanel Side(UsageLeftPresentation presentation, UsageLeftRow row)
        {
            var facts = new List<SideFact>();
            // Which agent the reading is for, as Seorak names it, leaning (settled 2026-10-08).
            if (LineWords.Theirs(row.Agent) is { IsEmpty: false } agent) facts.Add(new SideFact(UsageText.AgentApp, agent));
            facts.Add(new SideFact(UsageText.Seen, UsageText.Sentence(row.Seen)));
            facts.Add(new SideFact(UsageText.Resets, UsageText.Sentence(row.Resets)));
            facts.Add(new SideFact(UsageText.Account, host.Demonstration ? UsageText.PartOfTheRecording : UsageText.NotIdentified));
            // Use the existing side-panel parts and carried footer, preserving each fact and its runs.
            var subjectRows = host.TitleRows(row.Title, Glaze.Menu.SideColumnDegrees);
            var budget = new HeightBudget(host.PageHeight(subjectRows, besideMenu: true));
            var parts = new List<IReadOnlyList<SideFact>>();
            var part = new List<SideFact>();
            var used = 0f;
            foreach (var fact in facts)
            {
                // MenuFrameView.LayFact uses one label row, its label/value gap, and up to three value rows.
                var valueRows = Math.Min(3, Math.Max(1, host.RowsOf(new PageLine(fact.ValueRuns), Glaze.Menu.SideColumnDegrees)));
                var height = budget.Words(1) + PlaneComposition.Units(Glaze.Menu.LabelToValueDegrees) + budget.Words(valueRows);
                var gap = part.Count == 0 ? 0f : budget.GroupGap;
                if (part.Count > 0 && used + gap + height > budget.Room + 1e-6f)
                {
                    parts.Add(part);
                    part = new List<SideFact>();
                    used = 0f;
                    gap = 0f;
                }
                part.Add(fact);
                used += gap + height;
            }
            if (part.Count > 0) parts.Add(part);
            detailParts = parts.Count;
            detailPart = Math.Min(detailPart, detailParts - 1);
            return new SidePanel(row.TitleRuns, facts: parts[detailPart], source: presentation.Source == null ? null : LineWords.Written(presentation.Source),
                parts: detailParts > 1 ? (detailPart, detailParts) : ((int, int)?)null);
        }
    }

    /// <summary>Usage's own words (WORDS.md, ADR 0026).</summary>
    public static class UsageText
    {
        public const string Close = "Close";

        public const string Subject = "Usage";

        public const string Seen = "Seen";

        public const string Resets = "Resets";

        public const string Account = "Account";

        /// <summary>A limit's fact naming the agent app the reading is for (settled 2026-10-08).</summary>
        public const string AgentApp = "Agent App";

        /// <summary>A limit's Account fact in the demonstration, in place of who it belongs to.</summary>
        public const string PartOfTheRecording = "Part of Recording";

        /// <summary>A limit's Account fact: the source cannot tell accounts apart.</summary>
        public const string NotIdentified = "Account Unknown · Any Account on Computer";

        /// <summary>A limit's share left as its small fact, rounded up so "at most" stays true.</summary>
        public static string Left(int left) => "Up to " + left.ToString(CultureInfo.InvariantCulture) + "% Left";

        /// <summary>A time as a fact's value, starting as a sentence does: "Today at 15:18".</summary>
        public static string Sentence(string when) => when.Length == 0 ? when : char.ToUpperInvariant(when[0]) + when.Substring(1);
    }
}
