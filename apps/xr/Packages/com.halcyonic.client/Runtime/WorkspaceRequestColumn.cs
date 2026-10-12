#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>An open-ended request for one immutable task/run. Local words never submit themselves.</summary>
    public sealed class WorkspaceRequestColumn : IMenuColumn, ICompactableMenuColumn
    {
        public const string Edit = "workspace-request-edit", Talk = "workspace-request-talk", Start = "workspace-request-start",
            Yes = "workspace-request-yes", CancelReview = "workspace-request-cancel-review", Resume = "workspace-request-resume",
            Stop = "workspace-request-stop", Refresh = "workspace-request-refresh", Next = "workspace-request-next", Previous = "workspace-request-previous";
        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private readonly WorkspaceContinuation context;
        private readonly Func<bool> current;
        private readonly Func<CancellationToken, Task<WorkspaceAgentStatus>> offer;
        private readonly Func<string, CancellationToken, Task<WorkspaceRequestResponse>> read;
        private readonly FileInstruction editor = new FileInstruction();
        private CancellationTokenSource reads = new CancellationTokenSource();
        private Task<WorkspaceAgentStatus>? offerPending;
        private Task<WorkspaceRequestResponse>? readPending;
        private Task<CommandAckMessage>? acknowledgement;
        private AvailableWorkspaceAgent? agent;
        private WorkspaceRequestView? request;
        private string? requestId, commandId, notice;
        private MenuFrame? given, drawn;
        private bool active = true, armed, talking, currentBefore, offerRead, readAttempted;
        private int keyboardGeneration, voiceEpoch, reportPart;
        private string section = "draft";
        private long readPosition = -1;
        private TextSize measuredSize;
        public WorkspaceRequestColumn(IMenuHost host, CommandFactory commands, WorkspaceContinuation context, Func<bool> current,
            Func<CancellationToken, Task<WorkspaceAgentStatus>>? offer = null,
            Func<string, CancellationToken, Task<WorkspaceRequestResponse>>? read = null)
        {
            if (context.JournalId == null || context.ProjectId == null || context.WorkstreamId == null || context.ExecutionId == null)
                throw new ArgumentException("A request needs a bound task and execution.", nameof(context));
            this.host = host; this.commands = commands; this.current = current;
            this.context = HalcyonicJson.Deserialize<WorkspaceContinuation>(HalcyonicJson.Serialize(context));
            this.offer = offer ?? (token => host.Api?.GetWorkspaceAgentAsync(token) ?? Task.FromException<WorkspaceAgentStatus>(new InvalidOperationException()));
            this.read = read ?? ((id, token) => host.Api?.ReadWorkspaceRequestAsync(id, token) ?? Task.FromException<WorkspaceRequestResponse>(new InvalidOperationException()));
            editor.Begin(context.ExecutionId);
            currentBefore = Current;
            if (context.Request != null) Restore(context.Request);
        }
        public event Action? Changed;
        public event Action? Closed;
        public Action? BackToTask { get; set; }
        public string ExecutionId => context.ExecutionId!;
        public string? RequestId => requestId;
        public WorkspaceRequestContinuation Draft => new WorkspaceRequestContinuation
        { RequestId = requestId, Text = editor.Words, Spoken = editor.Spoken, Part = editor.Part };
        private bool Current => !host.Demonstration && host.Connected && current()
            && (host.State == null || host.State.Executions.TryGetValue(context.ExecutionId!, out var execution)
                && !execution.PendingQuestions.Any(question => question.Prompts.Any(prompt => prompt.Secret)));
        private bool Verified => request != null && Matches(request);
        private static readonly string[] RequestStatuses = { "running", "waiting_for_human", "waiting_for_runtime", "responded", "stopped", "interrupted", "failed" };
        private bool Matches(WorkspaceRequestView value) => value.RequestId == requestId && value.JournalId == context.JournalId
            && value.ProjectId == context.ProjectId && value.WorkstreamId == context.WorkstreamId && value.ExecutionId == context.ExecutionId
            && value.Status.Type == Newtonsoft.Json.Linq.JTokenType.String && RequestStatuses.Contains(value.Status.ToString());
        private string Status => Verified ? request!.Status.ToString() : "unknown";
        private bool CanResume => Verified && new[] { "responded", "interrupted", "failed", "waiting_for_human", "waiting_for_runtime" }.Contains(Status);
        private bool CanStop => Verified && new[] { "running", "waiting_for_human", "waiting_for_runtime", "interrupted", "failed" }.Contains(Status);
        public void Restore(WorkspaceRequestContinuation saved)
        {
            FocusLeft();
            requestId = saved.RequestId;
            request = null; readAttempted = false;
            editor.Restore(context.ExecutionId!, saved.Text, saved.Spoken, (int)saved.Part);
            section = requestId == null ? "draft" : "response";
            reportPart = 0;
            Invalidate();
        }
        public MenuFrame? Frame
        {
            get
            {
                active = true;
                BeginReads();
                if (given != null && measuredSize == host.TextSize) return given;
                if (measuredSize != host.TextSize) SuspendDraft();
                measuredSize = host.TextSize;
                return given = Build();
            }
        }
        private void BeginReads()
        {
            if (!active || !Current) return;
            if (reads.IsCancellationRequested) { reads.Dispose(); reads = new CancellationTokenSource(); }
            if (!offerRead && offerPending == null) { offerRead = true; offerPending = offer(reads.Token); }
            if (requestId != null && readPending == null && (!readAttempted || readPosition != host.State?.Position))
            { readAttempted = true; readPosition = host.State?.Position ?? -1; readPending = read(requestId, reads.Token); }
        }
        private MenuFrame Build()
        {
            var close = new Prompt(Footer.Close, "Back to task", GlazeIcon.Back, PromptKind.Close);
            var sections = new[] { new FrameSection("draft", "Request", section == "draft"),
                new FrameSection("response", "Reply", section == "response"), new FrameSection("receipts", "Actions", section == "receipts") };
            var source = agent == null ? LineWords.Ours("Workspace assistant") : LineWords.Ours("On this computer · ").Then(LineWords.TheirName(agent.Model));
            var room = new HeightBudget(host.PageHeight(host.TitleRows("Workspace request", Glaze.Menu.FileColumnDegrees), false, focused: true));
            if (section != "draft")
            {
                var runs = Report();
                var rows = Math.Max(1, host.RowsOf(new PageLine(runs, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
                var partRows = room.WordsIn(room.Room);
                var parts = Math.Max(1, (rows + partRows - 1) / partRows);
                reportPart %= parts;
                return new MenuFrame(LineWords.Ours("Workspace request"), new Footer(close,
                    rare: CanStop && Current ? new Prompt(Stop, "Cancel request", GlazeIcon.Close) : null,
                    secondary: new Prompt(Refresh, "Refresh", GlazeIcon.Refresh),
                    farRight: parts > 1 ? new Prompt(Next, Footer.NextPageWords(reportPart, parts), GlazeIcon.Next, PromptKind.NextPage)
                        : CanResume && Current ? new Prompt(Resume, "Continue request", GlazeIcon.Next) : null), sections: sections,
                    lines: new[] { new PageLine(runs, rows: Math.Min(partRows, rows - reportPart * partRows), fromRow: reportPart * partRows) }, sourceRuns: source).Focus();
            }
            var grant = "Read this task and send instructions to this run";
            var grantRows = Math.Max(1, host.RowsOf(grant, Glaze.Menu.FileColumnDegrees));
            // FileInstruction reserves one heading row. Reserve any additional scope rows as measured words.
            var draftRoom = new HeightBudget(room.Room + MenuPage.SourceLine - Math.Max(0, grantRows - 1) * MenuPage.Words(1));
            var allRows = Math.Max(1, host.RowsOf(EditorLine(int.MaxValue), Glaze.Menu.FileColumnDegrees));
            editor.Measure(allRows, draftRoom, host.TextSize, pairedPager: true);
            var reason = !Current ? "Reconnect to the current task and run." : agent == null ? "Workspace assistant is unavailable. Refresh to try again."
                : commandId != null ? "Waiting for the recorded result." : editor.Words.Length == 0 ? "Write or say a request first."
                : !WorkspaceContinuity.IsDraftTextWithinLimit(editor.Words) ? "Shorten this request before sending." : !editor.Read ? "Read every part of this request first." : notice;
            var lines = new List<PageLine> { new PageLine(grant, rows: grantRows), EditorLine(Math.Min(editor.PartRows, allRows - editor.Part * editor.PartRows)) };
            if (editor.Parts > 1)
            {
                lines.Add(new PageLine("Previous part", icon: GlazeIcon.Back, action: Previous, available: editor.Part > 0, besideNext: true));
                lines.Add(new PageLine("Next part", icon: GlazeIcon.Next, action: Next, available: editor.Part + 1 < editor.Parts));
            }
            var action = requestId == null ? Start : Resume;
            var maySend = requestId == null || CanResume;
            var send = new Prompt(action, requestId == null ? "Start request" : "Continue request", GlazeIcon.Next,
                main: true, available: reason == null && maySend,
                reason: reason ?? (!maySend ? "Refresh the current request before continuing." : null));
            var before = new Footer(close, rare: new Prompt(Refresh, "Refresh", GlazeIcon.Refresh),
                secondary: host.VoiceOffered ? new Prompt(Talk, "Hold to talk", GlazeIcon.HoldToTalk, holds: true) : null, farRight: send);
            var footer = armed ? Footer.Confirm(before, PromptSlot.FarRight,
                reason == null && maySend ? new Prompt(Yes, "Yes, send request", GlazeIcon.Next, PromptKind.Yes) : null,
                new Prompt(CancelReview, "Cancel", GlazeIcon.Close, PromptKind.Cancel)) : before;
            return new MenuFrame(LineWords.Ours("Workspace request"), footer, sections: sections, lines: lines, sourceRuns: source).Focus();
        }
        private PageLine EditorLine(int rows) => new PageLine(editor.Words.Length == 0 ? LineWords.Ours("Write a request") : LineWords.Theirs(editor.Words),
            action: host.KeyboardOffered ? Edit : null, rows: rows, fromRow: editor.Words.Length == 0 ? null : editor.Part * editor.PartRows,
            control: LineControl.Input, inputFocused: editor.InputFocused);
        private LineWords Report()
        {
            if (!Current) return LineWords.Ours("This request belongs to an earlier or disconnected task and run. Reconnect before continuing.");
            if (notice != null) return LineWords.Ours(notice);
            if (!Verified) return LineWords.Ours(requestId == null ? "No request has been submitted." : "Reading the recorded request.");
            if (section == "receipts")
            {
                var words = LineWords.Written("Observed action receipts\n");
                if (request!.Steps.Count == 0) return words.Then(LineWords.Ours("No actions have been recorded."));
                if (request.EarlierSteps > 0) words = words.Then(LineWords.Written(request.EarlierSteps + " earlier actions are retained on the computer.\n"));
                foreach (var step in request.Steps)
                    words = words.Then(LineWords.Written("\n" + ReceiptWords(step.Status.ToString()) + " · recorded position " + step.ObservationPosition + "\n"))
                        .Then(LineWords.Theirs(step.Summary)).Then(LineWords.Written("\n"));
                return words;
            }
            var result = LineWords.Ours("Request: ").Then(LineWords.Theirs(request!.OriginalText)).Then(LineWords.Written("\nStatus: " + StatusWords(Status) + "\n"));
            if (Status == "stopped") result = result.Then(LineWords.Written("Planning stopped. Task execution may continue.\n"));
            if (request.Reply != null) result = result.Then(LineWords.Written("\nAssistant reply · reported\n")).Then(LineWords.Theirs(request.Reply.Text));
            else result = result.Then(LineWords.Ours("No reply has been recorded yet."));
            return result;
        }
        private static string ReceiptWords(string status) => status switch
        {
            "prepared" => "Prepared", "running" => "In progress", "waiting" => "Waiting", "confirmed" => "Confirmed",
            "rejected" => "Refused", "failed" => "Failed", _ => "Outcome unknown",
        };
        private static string StatusWords(string status) => status switch
        {
            "running" => "Working", "waiting_for_human" => "Needs your reply", "waiting_for_runtime" => "Waiting for the task",
            "responded" => "Reply ready", "stopped" => "Planning stopped", "interrupted" => "Interrupted", "failed" => "Failed", _ => "Unknown",
        };
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { FocusLeft(); if (BackToTask != null) BackToTask(); else Closed?.Invoke(); return; }
            if (id == MenuFrame.ChooseSection && new[] { "draft", "response", "receipts" }.Contains(key))
            { section = key!; reportPart = 0; armed = false; Invalidate(); return; }
            if (id == Refresh) { notice = null; agent = null; request = null; offerRead = false; readAttempted = false; readPosition = -1; Invalidate(); return; }
            if (id == Next && section != "draft") { reportPart++; Invalidate(); return; }
            if (id == Next) { editor.Next(host.Clock); Invalidate(); return; }
            if (id == Previous) { editor.Previous(host.Clock); Invalidate(); return; }
            if (id == CancelReview) { armed = false; Invalidate(); return; }
            if (!Current || commandId != null) return;
            if (id == Edit && !armed && host.KeyboardOffered && drawn.Lines.Any(line => line.Action == Edit && line.Available))
            {
                editor.Edit(); var epoch = ++keyboardGeneration;
                MenuKeyboard.Open(host, editor.Words, "Workspace request", text =>
                {
                    if (epoch != keyboardGeneration || !Current || !editor.InputFocused) return;
                    editor.Changed(text, false); notice = null; armed = false; Invalidate();
                }, () => { if (epoch == keyboardGeneration) { SuspendDraft(); Invalidate(); } });
                Invalidate(); return;
            }
            if (id == Stop && CanStop && drawn.Footer.All.Any(each => each.Prompt.Id == Stop && each.Prompt.Available)) { Submit(commands.CancelRequest(requestId!)); return; }
            if (id == Resume && section != "draft" && CanResume && drawn.Footer.All.Any(each => each.Prompt.Id == Resume && each.Prompt.Available)) { section = "draft"; SuspendDraft(); Invalidate(); return; }
            if (!drawn.Footer.All.Any(each => each.Prompt.Id == id && each.Prompt.Available) || agent == null || !editor.Read || !WorkspaceContinuity.IsDraftTextWithinLimit(editor.Words)) return;
            if ((id == Start || id == Resume) && editor.Spoken) { armed = true; Invalidate(); return; }
            if (id != Start && id != Resume && !(id == Yes && armed)) return;
            if (requestId != null && !CanResume) return;
            Submit(requestId == null ? commands.Request(context.ProjectId!, context.WorkstreamId!, context.ExecutionId!, editor.Words)
                : commands.ResumeRequest(requestId, editor.Words));
        }
        private void Submit(CommandEnvelope command)
        {
            var sent = host.Submit(command);
            if (sent == null) { notice = "The request was not sent. Reconnect and try again."; Invalidate(); return; }
            commandId = command.CommandId; acknowledgement = sent; armed = false; notice = null; Invalidate();
        }
        public void Drawn(MenuFrame frame, Footer? sidePanel)
        {
            if (frame != given || sidePanel != null) return;
            drawn = frame;
            if (section == "draft") { var wasRead = editor.Read; editor.Drawn(host.Clock); if (wasRead != editor.Read) Invalidate(); }
        }
        public void Tick()
        {
            var live = Current;
            if (live != currentBefore) { currentBefore = live; agent = null; offerRead = false; readAttempted = false; SuspendDraft(); Invalidate(); }
            if (!live || !active) return;
            if (offerPending?.IsCompleted == true)
            {
                try { agent = offerPending.GetAwaiter().GetResult() as AvailableWorkspaceAgent; if (agent?.Served != "this_mac") agent = null; }
                catch (Exception) { agent = null; }
                offerPending = null; if (agent == null) notice = "Workspace assistant is unavailable on this computer. Refresh to try again."; Invalidate();
            }
            if (readPending?.IsCompleted == true)
            {
                try { var found = readPending.GetAwaiter().GetResult().Request;
                    if (Matches(found)) { request = found; notice = null; }
                    else { request = null; notice = "This request belongs to another task or run. Its saved draft remains available for recovery."; } }
                catch (Exception) { request = null; notice = "The recorded request could not be read. Refresh to try again."; }
                readPending = null; Invalidate();
            }
            if (acknowledgement?.IsCompleted == true)
            {
                try { if (acknowledgement.GetAwaiter().GetResult().Disposition != CommandAckDisposition.Accepted) { commandId = null; notice = "The request was refused. Refresh before trying again."; } }
                catch (Exception) { notice = "Delivery is unknown. Reconnect to check the recorded result."; }
                acknowledgement = null; Invalidate();
            }
            if (commandId != null && host.State?.Commands.TryGetValue(commandId, out var command) == true)
            {
                if (command.Result is WorkspaceRequestCreatedResult created) { requestId = created.RequestId; commandId = null; request = null; section = "response"; readPosition = -1; }
                else if (command.Status == CommandStatus.Failed || command.Status == CommandStatus.Rejected) { commandId = null; notice = "The request was not completed. Refresh before trying again."; }
                else if (command.Status == CommandStatus.Completed) { commandId = null; request = null; readPosition = -1; }
                if (commandId == null) Invalidate();
            }
            BeginReads();
        }
        private void SuspendDraft() { keyboardGeneration++; armed = false; talking = false; editor.Restore(context.ExecutionId!, editor.Words, editor.Spoken, editor.Part); }
        private void Invalidate() { given = null; drawn = null; Changed?.Invoke(); }
        public void FocusLeft()
        { active = false; reads.Cancel(); offerPending = null; readPending = null; offerRead = false; readAttempted = false; agent = null; SuspendDraft(); Invalidate(); }
        public void Compact() => FocusLeft();
        public void HoldStarted(string id) { if (id != Talk || !Current || !host.VoiceOffered || armed || drawn?.Footer.All.Any(each => each.Prompt.Id == Talk && each.Prompt.Available) != true) return; talking = true; voiceEpoch = editor.Epoch; }
        public void HoldEnded(string id, bool letGo) { if (id == Talk && !letGo) talking = false; }
        public void Heard(string words)
        { if (!talking || !Current || voiceEpoch != editor.Epoch) return; talking = false; editor.Changed(words, true); notice = null; Invalidate(); }
        public void Said(string words) { notice = words; Invalidate(); }
    }
}
