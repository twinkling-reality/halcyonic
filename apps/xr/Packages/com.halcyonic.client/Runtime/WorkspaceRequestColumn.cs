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
        private readonly Func<string, CancellationToken, Task<WorkspaceCommandResponse>> readCommand;
        private readonly FileInstruction editor = new FileInstruction();
        private CancellationTokenSource reads = new CancellationTokenSource();
        private Task<WorkspaceAgentStatus>? offerPending;
        private Task<WorkspaceRequestResponse>? readPending;
        private Task<WorkspaceCommandResponse>? commandReadPending;
        private bool commandReadAttempted;
        private long commandReadPosition = -1;
        private CommandType? submittedType;
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
            Func<string, CancellationToken, Task<WorkspaceRequestResponse>>? read = null,
            Func<string, CancellationToken, Task<WorkspaceCommandResponse>>? readCommand = null)
        {
            if (context.JournalId == null || context.ProjectId == null || context.WorkstreamId == null || context.ExecutionId == null)
                throw new ArgumentException("A request needs a bound task and execution.", nameof(context));
            this.host = host; this.commands = commands; this.current = current;
            this.context = HalcyonicJson.Deserialize<WorkspaceContinuation>(HalcyonicJson.Serialize(context));
            this.offer = offer ?? (token => host.Api?.GetWorkspaceAgentAsync(token) ?? Task.FromException<WorkspaceAgentStatus>(new InvalidOperationException()));
            this.read = read ?? ((id, token) => host.Api?.ReadWorkspaceRequestAsync(id, token) ?? Task.FromException<WorkspaceRequestResponse>(new InvalidOperationException()));
            this.readCommand = readCommand ?? ((id, token) => host.Api?.ReadWorkspaceCommandAsync(id, token) ?? Task.FromException<WorkspaceCommandResponse>(new InvalidOperationException()));
            editor.Begin(context.ExecutionId);
            currentBefore = Current;
            if (context.Request != null) Restore(context.Request);
        }
        public event Action? Changed;
        public event Action? Closed;
        public Action? BackToTask { get; set; }
        public Func<WorkspaceRequestContinuation, bool>? BeforeSubmit { get; set; }
        public Action? InputContextLeft { get; set; }
        public string ExecutionId => context.ExecutionId!;
        public string? RequestId => requestId;
        public WorkspaceRequestContinuation Draft => new WorkspaceRequestContinuation
        { RequestId = requestId, PendingCommandId = commandId, Text = editor.Words, Spoken = editor.Spoken, Part = editor.Part };
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
            commandId = saved.PendingCommandId; submittedType = null; acknowledgement = null; commandReadAttempted = false; commandReadPosition = -1;
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
            if (commandId != null && commandReadPending == null && (!commandReadAttempted || commandReadPosition != (host.State?.Position ?? -1)))
            { commandReadAttempted = true; commandReadPosition = host.State?.Position ?? -1; commandReadPending = readCommand(commandId, reads.Token); }
            if (requestId != null && readPending == null && (!readAttempted || readPosition != (host.State?.Position ?? -1)))
            { readAttempted = true; readPosition = host.State?.Position ?? -1; readPending = read(requestId, reads.Token); }
        }
        private LineWords TaskSubject
        {
            get
            {
                var state = host.State;
                return state != null && state.Journal?.JournalId == context.JournalId && state.Workstreams.TryGetValue(context.WorkstreamId!, out var task)
                    && task.ProjectId == context.ProjectId ? LineWords.TheirName(task.Title) : LineWords.Ours("Workspace Request");
            }
        }
        private MenuFrame Build()
        {
            var subject = TaskSubject;
            var close = new Prompt(Footer.Close, "Back to Task", GlazeIcon.Back, PromptKind.Close);
            var sections = new[] { new FrameSection("draft", "Request", section == "draft"),
                new FrameSection("response", "Reply", section == "response"), new FrameSection("receipts", "Actions", section == "receipts") };
            var source = agent == null ? LineWords.Ours("Workspace Assistant") : LineWords.Ours("On This Computer · ").Then(LineWords.TheirName(agent.Model));
            var room = new HeightBudget(host.PageHeight(host.TitleRows(subject.Text, Glaze.Menu.FileColumnDegrees), false, focused: true));
            if (section != "draft")
            {
                var runs = Report();
                var rows = Math.Max(1, host.RowsOf(new PageLine(runs, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
                var partRows = room.WordsIn(room.Room);
                var parts = Math.Max(1, (rows + partRows - 1) / partRows);
                reportPart %= parts;
                return new MenuFrame(subject, new Footer(close,
                    rare: CanStop && Current ? new Prompt(Stop, "Cancel Request", GlazeIcon.Close) : null,
                    secondary: new Prompt(Refresh, "Refresh", GlazeIcon.Refresh),
                    farRight: parts > 1 ? new Prompt(Next, Footer.NextPageWords(reportPart, parts), GlazeIcon.Next, PromptKind.NextPage)
                        : CanResume && Current ? new Prompt(Resume, "Continue Request", GlazeIcon.Next) : null), sections: sections,
                    lines: new[] { new PageLine(runs, rows: Math.Min(partRows, rows - reportPart * partRows), fromRow: reportPart * partRows) }, sourceRuns: source).Focus();
            }
            var grant = "Read Task · Instruct This Run";
            var grantRows = Math.Max(1, host.RowsOf(grant, Glaze.Menu.FileColumnDegrees));
            // FileInstruction reserves one heading row. Reserve any additional scope rows as measured words.
            var draftRoom = new HeightBudget(room.Room + MenuPage.SourceLine - Math.Max(0, grantRows - 1) * MenuPage.Words(1));
            var allRows = Math.Max(1, host.RowsOf(EditorLine(int.MaxValue), Glaze.Menu.FileColumnDegrees));
            editor.Measure(allRows, draftRoom, host.TextSize, pairedPager: true);
            var reason = !Current ? "Reconnect First" : agent == null ? "Assistant Unavailable · Refresh"
                : commandId != null ? "Awaiting Recorded Result" : editor.Words.Length == 0 ? "Write or Say First"
                : !WorkspaceContinuity.IsDraftTextWithinLimit(editor.Words) ? "Shorten Request" : !editor.Read ? "Read Full Request" : notice;
            var lines = new List<PageLine> { new PageLine(grant, rows: grantRows), EditorLine(Math.Min(editor.PartRows, allRows - editor.Part * editor.PartRows)) };
            if (editor.Parts > 1)
            {
                lines.Add(new PageLine("Previous Part", icon: GlazeIcon.Back, action: Previous, available: editor.Part > 0, besideNext: true));
                lines.Add(new PageLine("Next Part", icon: GlazeIcon.Next, action: Next, available: editor.Part + 1 < editor.Parts));
            }
            var action = requestId == null ? Start : Resume;
            var maySend = requestId == null || CanResume;
            var send = new Prompt(action, requestId == null ? "Start Request" : "Continue Request", GlazeIcon.Next,
                main: true, available: reason == null && maySend,
                reason: reason ?? (!maySend ? "Refresh First" : null));
            var before = new Footer(close, rare: new Prompt(Refresh, "Refresh", GlazeIcon.Refresh),
                secondary: host.VoiceOffered ? new Prompt(Talk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true,
                    available: Current && commandId == null && !editor.InputFocused,
                    reason: Current && commandId == null && !editor.InputFocused ? null : reason ?? "Finish Typing First", pageExplains: true) : null, farRight: send);
            var footer = armed ? Footer.Confirm(before, PromptSlot.FarRight,
                reason == null && maySend ? new Prompt(Yes, "Confirm", GlazeIcon.Next, PromptKind.Yes) : null,
                new Prompt(CancelReview, "Cancel", GlazeIcon.Close, PromptKind.Cancel)) : before;
            return new MenuFrame(subject, footer, sections: sections, lines: lines, sourceRuns: source).Focus();
        }
        private PageLine EditorLine(int rows) => new PageLine(editor.Words.Length == 0 ? LineWords.Ours("Write Request") : LineWords.Theirs(editor.Words),
            action: host.KeyboardOffered ? Edit : null, rows: rows, fromRow: editor.Words.Length == 0 ? null : editor.Part * editor.PartRows,
            control: LineControl.Input, inputFocused: editor.InputFocused, available: Current && commandId == null && !armed);
        private LineWords Report()
        {
            if (!Current) return LineWords.Ours("Earlier or Disconnected Run · Reconnect First");
            if (notice != null) return LineWords.Ours(notice);
            if (commandId != null) return LineWords.Ours("Awaiting Recorded Result · Refresh to Check");
            if (!Verified) return LineWords.Ours(requestId == null ? "Not Submitted" : "Reading Request");
            if (section == "receipts")
            {
                var words = LineWords.None;
                if (request!.Steps.Count == 0) return words.Then(LineWords.Ours("No Actions Recorded"));
                if (request.EarlierSteps > 0) words = words.Then(LineWords.Written(request.EarlierSteps + " earlier actions are retained on the computer.\n"));
                foreach (var step in request.Steps)
                    words = words.Then(LineWords.Written("\n" + ReceiptWords(step) + "\n"))
                        .Then(step.CommandId != null && step.RuntimeStatus != null && step.Status.ToString() == "confirmed"
                            ? LineWords.Written("Changes Unverified\n") : LineWords.None)
                        .Then(LineWords.Theirs(step.Summary)).Then(LineWords.Written("\n"));
                return words;
            }
            var result = LineWords.Ours("Request · ").Then(LineWords.Theirs(request!.OriginalText)).Then(LineWords.Written("\nStatus · " + StatusWords(Status) + "\n"));
            if (Status == "stopped") result = result.Then(LineWords.Written("Planning Stopped · Task May Continue\n"));
            if (request.Reply != null) result = result.Then(LineWords.Written("\nAssistant Reply · Reported\n")).Then(LineWords.Theirs(request.Reply.Text));
            else result = result.Then(LineWords.Ours("No Reply Yet"));
            return result;
        }
        private static string ReceiptWords(WorkspaceRequestStep step) => step.Status.ToString() switch
        {
            "prepared" => "Not Sent", "running" => step.CommandId != null ? "Sent" : "Reading", "waiting" => "Waiting", "confirmed" => step.ToolName == "person_response" ? "Direction Recorded"
                : step.CommandId != null && step.RuntimeStatus != null ? "Round Ended" : "Read Confirmed",
            "rejected" => "Not Sent", "failed" => "Failed", _ => "Unknown",
        };
        private static string StatusWords(string status) => status switch
        {
            "running" => "Running", "waiting_for_human" => "Needs Your Reply", "waiting_for_runtime" => "Awaiting Task",
            "responded" => "Reply Ready", "stopped" => "Planning Stopped", "interrupted" => "Interrupted", "failed" => "Failed", _ => "Unknown",
        };
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { FocusLeft(); if (BackToTask != null) BackToTask(); else Closed?.Invoke(); return; }
            if (id == MenuFrame.ChooseSection && new[] { "draft", "response", "receipts" }.Contains(key))
            { SuspendDraft(); section = key!; reportPart = 0; Invalidate(); return; }
            if (id == Refresh) { notice = null; agent = null; request = null; offerRead = false; readAttempted = false; commandReadAttempted = false; readPosition = -1; Invalidate(); return; }
            if (id == Next && section != "draft") { reportPart++; Invalidate(); return; }
            if (id == Next) { editor.Next(host.Clock); Invalidate(); return; }
            if (id == Previous) { editor.Previous(host.Clock); Invalidate(); return; }
            if (id == CancelReview) { armed = false; Invalidate(); return; }
            if (!Current || commandId != null) return;
            if (id == Edit && !armed && host.KeyboardOffered && drawn.Lines.Any(line => line.Action == Edit && line.Available))
            {
                editor.Edit(); var epoch = ++keyboardGeneration;
                MenuKeyboard.Open(host, editor.Words, "Workspace Request", text =>
                {
                    if (epoch != keyboardGeneration || !Current || section != "draft" || !editor.InputFocused) return;
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
            commandId = command.CommandId;
            submittedType = command is WorkspaceRequestSubmitCommand ? CommandType.WorkspaceRequestSubmit
                : command is WorkspaceRequestResumeCommand ? CommandType.WorkspaceRequestResume : CommandType.WorkspaceRequestCancel;
            // Persist this exact ordinary draft and UUID before any call can reach the host.
            if (!PersistPending())
            {
                commandId = null; submittedType = null; notice = "Draft Not Saved · Refresh"; Invalidate(); return;
            }
            if (!Current) { ClearKnownUnsent(); Invalidate(); return; }
            try
            {
                acknowledgement = host.Submit(command);
                if (acknowledgement == null) ClearKnownUnsent();
                else { notice = null; commandReadAttempted = false; commandReadPosition = -1; }
            }
            catch (Exception) { notice = "Delivery Unconfirmed · Refresh"; }
            armed = false; Invalidate();
        }
        private bool PersistPending()
        {
            try { return BeforeSubmit?.Invoke(Draft) == true; }
            catch (Exception) { return false; }
        }
        private void ClearKnownUnsent()
        {
            var pending = commandId;
            commandId = null;
            if (!PersistPending()) commandId = pending;
            else submittedType = null;
            notice = "Not Sent · Refresh";
        }
        private bool MatchesCommand(CommandView command) => command.CommandId == commandId && command.ProjectId == context.ProjectId
            && command.WorkstreamId == context.WorkstreamId && command.ExecutionId == context.ExecutionId
            && (command.CommandType == CommandType.WorkspaceRequestSubmit || command.CommandType == CommandType.WorkspaceRequestResume || command.CommandType == CommandType.WorkspaceRequestCancel)
            && (submittedType == null || submittedType == command.CommandType)
            && (requestId != null || command.CommandType == CommandType.WorkspaceRequestSubmit);
        private void ApplyCommand(WorkspaceCommandResponse found)
        {
            if (found.JournalId != context.JournalId || !MatchesCommand(found.Command))
            { notice = "Result from Another Request · Draft Kept"; return; }
            var command = found.Command;
            if (command.Status == CommandStatus.Completed)
            {
                if (command.CommandType == CommandType.WorkspaceRequestSubmit)
                {
                    if (command.Result is not WorkspaceRequestCreatedResult created || requestId != null && requestId != created.RequestId)
                    { notice = "Request Reference Unconfirmed · Refresh"; return; }
                    var candidate = HalcyonicJson.Deserialize<WorkspaceContinuation>(HalcyonicJson.Serialize(context));
                    candidate.Request = Draft; candidate.Request.RequestId = created.RequestId; candidate.Request.PendingCommandId = null;
                    if (!WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(candidate), out _))
                    { notice = "Request Reference Unverified · Refresh"; return; }
                    requestId = created.RequestId;
                }
                else if (requestId == null) { notice = "Request Reference Unavailable · Refresh"; return; }
                commandId = null; submittedType = null; request = null; readAttempted = false; readPosition = -1; notice = null; section = "response";
            }
            else if (command.Status == CommandStatus.Rejected || command.Status == CommandStatus.Failed && command.Failure?.Effect == FailureEffect.None)
            { commandId = null; submittedType = null; notice = "Not Sent · Refresh"; }
            else if (command.Status == CommandStatus.Failed) notice = "Delivery Unconfirmed · Refresh";
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
                offerPending = null; if (agent == null) notice = "Assistant Unavailable · Refresh"; Invalidate();
            }
            if (readPending?.IsCompleted == true)
            {
                try { var found = readPending.GetAwaiter().GetResult().Request;
                    if (Matches(found)) { request = found; notice = null; }
                    else { request = null; notice = "Another Task or Run · Draft Kept"; } }
                catch (Exception) { request = null; notice = "Request Unreadable · Refresh"; }
                readPending = null; Invalidate();
            }
            if (acknowledgement?.IsCompleted == true)
            {
                try
                {
                    var ack = acknowledgement.GetAwaiter().GetResult();
                    // Even Duplicate is only an acknowledgement. Resolve its exact original-owner
                    // command through GET; a foreign/partial ack cannot discard a pending identity.
                    if (ack.CommandId != commandId || ack.Command != null && !MatchesCommand(ack.Command)
                        || ack.Disposition != CommandAckDisposition.Accepted && ack.Disposition != CommandAckDisposition.Duplicate)
                        notice = "Delivery Unconfirmed · Refresh";
                }
                catch (Exception) { notice = "Delivery Unconfirmed · Refresh"; }
                acknowledgement = null; Invalidate();
            }
            if (commandReadPending?.IsCompleted == true)
            {
                try { ApplyCommand(commandReadPending.GetAwaiter().GetResult()); }
                catch (Exception) { notice = "Pending Result Unreadable · Refresh"; }
                commandReadPending = null; Invalidate();
            }
            BeginReads();
        }
        private void SuspendDraft() { InputContextLeft?.Invoke(); keyboardGeneration++; armed = false; talking = false; editor.Restore(context.ExecutionId!, editor.Words, editor.Spoken, editor.Part); }
        private void Invalidate() { given = null; drawn = null; Changed?.Invoke(); }
        public void FocusLeft()
        { active = false; reads.Cancel(); offerPending = null; readPending = null; commandReadPending = null; offerRead = false; readAttempted = false; commandReadAttempted = false; agent = null; SuspendDraft(); Invalidate(); }
        public void Compact() => FocusLeft();
        public void HoldStarted(string id) { if (id != Talk || !Current || !host.VoiceOffered || armed || commandId != null || drawn?.Footer.All.Any(each => each.Prompt.Id == Talk && each.Prompt.Available) != true) return; talking = true; voiceEpoch = editor.Epoch; }
        public void HoldEnded(string id, bool letGo) { if (id == Talk && !letGo) talking = false; }
        public void Heard(string words)
        { if (!talking || !Current || section != "draft" || voiceEpoch != editor.Epoch) return; talking = false; editor.Changed(words, true); notice = null; Invalidate(); }
        public void Said(string words)
        {
            if (!active || words == VoiceText.Listening || words == VoiceText.Hearing) return;
            notice = words; section = "response"; reportPart = 0; Invalidate();
        }
    }
}
