#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Read-only recorded results for one original task/run. No editor or command consumer.</summary>
    public sealed class TaskResultsColumn : IMenuColumn, ICompactableMenuColumn
    {
        public const string Read = "task-result-read", ReadFile = "task-result-file", Refresh = "task-results-refresh", Next = "task-results-next", Previous = "task-results-previous";
        private readonly IMenuHost host;
        private readonly WorkspaceContinuation context;
        private readonly Func<bool> current;
        private readonly string runtimeId, sourceName;
        private readonly ControlPlaneApi? api;
        private readonly Func<CancellationToken, Task<ToolResultsResponse>> list;
        private readonly Func<string, CancellationToken, Task<ToolResultResponse>> read;
        private CancellationTokenSource? reads;
        private Task<ToolResultsResponse>? listPending;
        private Task<ToolResultResponse>? readPending;
        private ToolResultsResponse? recorded;
        private ToolResultSummary? selected;
        private ToolResultResponse? result;
        private MenuFrame? given, drawn;
        private TextSize measuredAt;
        private int page, listPage, fileListPage, pages = 1;
        private int? selectedFile;
        private string? selectedFileKey;
        private bool active, attempted;
        private string? notice;

        public TaskResultsColumn(IMenuHost host, WorkspaceContinuation context, string runtimeId, string sourceName, Func<bool> current,
            Func<CancellationToken, Task<ToolResultsResponse>>? list = null,
            Func<string, CancellationToken, Task<ToolResultResponse>>? read = null)
        {
            if (context.JournalId == null || context.ProjectId == null || context.WorkstreamId == null || context.ExecutionId == null)
                throw new ArgumentException("Results require one bound task and execution.", nameof(context));
            this.host = host; this.current = current; this.runtimeId = runtimeId; this.sourceName = sourceName;
            this.context = HalcyonicJson.Deserialize<WorkspaceContinuation>(HalcyonicJson.Serialize(context));
            api = host.Api;
            var query = new ToolResultsQuery { JournalId = this.context.JournalId!, ProjectId = this.context.ProjectId!, WorkstreamId = this.context.WorkstreamId! };
            this.list = list ?? (token => api?.GetToolResultsAsync(this.context.ExecutionId!, query, token)
                ?? Task.FromException<ToolResultsResponse>(new InvalidOperationException()));
            this.read = read ?? ((id, token) => api?.GetToolResultAsync(this.context.ExecutionId!, id, query, token)
                ?? Task.FromException<ToolResultResponse>(new InvalidOperationException()));
        }
        /// <summary>
        /// Whether an explicit reopen may retain this reader's local selection. This compares the
        /// original copied scope and API only; visibility, fresh reads and action authority still
        /// depend on Current and the actual focused draw.
        /// </summary>
        public bool CanReuse(WorkspaceContinuation candidate, string candidateRuntimeId, ControlPlaneApi? candidateApi) =>
            candidate != null && candidate.ConnectionKey == context.ConnectionKey
            && Scope(candidate.JournalId!, candidate.ProjectId!, candidate.WorkstreamId!, candidate.ExecutionId!)
            && candidateRuntimeId == runtimeId && ReferenceEquals(candidateApi, api) && ReferenceEquals(candidateApi, host.Api);

        public event Action? Changed;
        public event Action? Closed;
        public Action? BackToTask { get; set; }
        private bool Current => !host.Demonstration && host.Connected && current() && ReferenceEquals(api, host.Api)
            && (host.State == null || WorkspaceContinuity.MatchesCurrent(context, context.ConnectionKey, host.State));
        public MenuFrame? Frame
        {
            get
            {
                Synchronize();
                if (given != null && measuredAt == host.TextSize) return given;
                measuredAt = host.TextSize;
                return given = Build();
            }
        }
        private void Synchronize()
        {
            if (!Current) { if (active) FocusLeft(); return; }
            if (!active) { active = true; attempted = false; notice = null; given = null; drawn = null; }
            if (!attempted)
            {
                attempted = true;
                reads = new CancellationTokenSource();
                try { listPending = list(reads.Token); }
                catch (Exception) { notice = "Results Unreadable · Refresh"; }
                if (selected != null)
                {
                    try { readPending = read(selected.EventId, reads.Token); }
                    catch (Exception) { notice = "Result Unreadable · Back and Refresh"; }
                }
            }
        }
        private bool Scope(string journal, string project, string work, string execution) => journal == context.JournalId
            && project == context.ProjectId && work == context.WorkstreamId && execution == context.ExecutionId;
        private bool Summary(ToolResultSummary item) => item.RuntimeId == runtimeId && item.EventId != null && item.Position >= 0
            && item.Epistemic == "observed" && Enum.IsDefined(typeof(ToolResultCompletion), item.Completion)
            && Enum.IsDefined(typeof(ToolResultSummaryKind), item.Kind)
            && DateTimeOffset.TryParse(item.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
            && (item.Kind == ToolResultSummaryKind.Command ? item.FilesTotal == null && (item.ExitCode == null || item.ExitCode >= int.MinValue && item.ExitCode <= int.MaxValue)
                : item.ExitCode == null && item.FilesTotal >= 0);
        private static bool ValidBody(ToolResultBody body)
        {
            if (body == null || !Enum.IsDefined(typeof(ToolResultBodyAvailability), body.Availability)
                || !Enum.IsDefined(typeof(ToolResultBodyTruncation), body.Truncation)
                || (body.Availability == ToolResultBodyAvailability.Available) != (body.Text != null)
                || body.Complete && (body.Text == null || body.Truncation != ToolResultBodyTruncation.None)) return false;
            if (body.Text == null) return true;
            var count = 0;
            for (var index = 0; index < body.Text.Length; index++, count++)
                if (char.IsHighSurrogate(body.Text[index]) && index + 1 < body.Text.Length && char.IsLowSurrogate(body.Text[index + 1])) index++;
            return count <= 4096 && Encoding.UTF8.GetByteCount(body.Text) <= 16384;
        }
        private static bool Consistent(ToolResultResponse response)
        {
            var value = response.Result; var summary = response.Summary;
            if (value == null || value.Kind != Wire(summary.Kind) || value.Completion != summary.Completion
                || value.Redaction == null || value.Redaction.Policy != "known_held_values" || value.Redaction.Replacements < 0
                || Encoding.UTF8.GetByteCount(HalcyonicJson.Serialize(value)) > 65536) return false;
            if (value is CommandToolResult command)
                return summary.FilesTotal == null && command.ExitCode == summary.ExitCode && ValidBody(command.Output)
                    && summary.BodyAvailable == (command.Output.Availability == ToolResultBodyAvailability.Available)
                    && summary.RetainedComplete == command.Output.Complete;
            if (value is not FileChangesToolResult changes || changes.Files.Count > 32 || changes.FilesTotal < changes.Files.Count
                || changes.ListComplete && changes.FilesTotal != changes.Files.Count || summary.ExitCode != null || summary.FilesTotal != changes.FilesTotal) return false;
            foreach (var file in changes.Files)
            {
                if (file == null || !ValidBody(file.Patch) || !Enum.IsDefined(typeof(ToolResultFileKind), file.Kind)
                    || !Enum.IsDefined(typeof(ToolResultPathScope), file.PathScope)
                    || (file.PathScope == ToolResultPathScope.ProjectRelative) != (file.Path != null)
                    || file.MovePathScope == null && (file.MovePath != null || file.MovePathComplete != null)
                    || file.MovePathScope != null && (!Enum.IsDefined(typeof(ToolResultPathScope), file.MovePathScope.Value)
                        || (file.MovePathScope == ToolResultPathScope.ProjectRelative) != (file.MovePath != null) || file.MovePathComplete == null)
                    || (file.PathScope != ToolResultPathScope.ProjectRelative || file.MovePathScope != null && file.MovePathScope != ToolResultPathScope.ProjectRelative)
                        && file.Patch.Text != null) return false;
            }
            return summary.BodyAvailable == changes.Files.Any(file => file.Patch.Availability == ToolResultBodyAvailability.Available)
                && summary.RetainedComplete == (changes.ListComplete && changes.Files.All(file => file.Patch.Complete
                    && file.PathComplete && (file.MovePathScope == null || file.MovePathComplete == true)));
        }
        public void Tick()
        {
            Synchronize();
            if (!active || !Current || reads?.IsCancellationRequested != false) return;
            if (listPending?.IsCompleted == true)
            {
                try
                {
                    var found = listPending.GetAwaiter().GetResult();
                    if (!Current || !Scope(found.JournalId, found.ProjectId, found.WorkstreamId, found.ExecutionId)
                        || !Enum.IsDefined(typeof(ToolResultsAvailability), found.Availability)
                        || found.Total < 0 || found.Earlier < 0 || found.Total - found.Earlier != found.Results.Count
                        || found.Results.Count > 16 || found.Results.Any(item => !Summary(item))
                        || found.Results.Zip(found.Results.Skip(1), (left, right) => left.Position <= right.Position).Any(unordered => unordered)
                        || found.Results.Select(item => item.EventId).Distinct().Count() != found.Results.Count)
                        throw new InvalidOperationException();
                    recorded = HalcyonicJson.Deserialize<ToolResultsResponse>(HalcyonicJson.Serialize(found));
                    notice = null;
                }
                catch (Exception) { recorded = null; notice = "Results Unreadable · Refresh"; }
                listPending = null; Invalidate();
            }
            if (readPending?.IsCompleted == true)
            {
                try
                {
                    var found = readPending.GetAwaiter().GetResult();
                    if (!Current || selected == null || !Scope(found.JournalId, found.ProjectId, found.WorkstreamId, found.ExecutionId)
                        || !Summary(found.Summary) || HalcyonicJson.Serialize(found.Summary) != HalcyonicJson.Serialize(selected)
                        || !Consistent(found) || selectedFile != null && (found.Result is not FileChangesToolResult files
                            || selectedFile.Value >= files.Files.Count || FileKey(files.Files[selectedFile.Value]) != selectedFileKey)) throw new InvalidOperationException();
                    result = HalcyonicJson.Deserialize<ToolResultResponse>(HalcyonicJson.Serialize(found));
                    notice = null;
                }
                catch (Exception) { result = null; notice = "Result Unreadable · Back and Refresh"; }
                readPending = null; Invalidate();
            }
        }
        private LineWords Subject => Current && host.State?.Workstreams.TryGetValue(context.WorkstreamId!, out var task) == true
            && task.ProjectId == context.ProjectId ? LineWords.TheirName(task.Title) : LineWords.Ours("Recorded Results");
        private MenuFrame Build()
        {
            var subject = Subject;
            var budget = new HeightBudget(host.PageHeight(host.TitleRows(subject.Text, Glaze.Menu.FileColumnDegrees), false, focused: true));
            var lines = new List<PageLine>();
            pages = 1;
            if (!Current) lines.Add(new PageLine("Current Task Only · Return to It", rows: 2));
            else if (notice != null) lines.Add(new PageLine(notice, rows: 2));
            else if (selected != null)
            {
                if (result == null) lines.Add(new PageLine("Reading Result"));
                else if (result.Result is FileChangesToolResult changes && selectedFile == null)
                    lines.AddRange(FileChoices(changes, budget));
                else
                {
                    var words = Detail(result);
                    var rows = Math.Max(1, host.RowsOf(new PageLine(words, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
                    var perPage = budget.WordsIn(budget.Room);
                    pages = Math.Max(1, (rows + perPage - 1) / perPage); page = Math.Min(page, pages - 1);
                    lines.Add(new PageLine(words, rows: Math.Min(perPage, rows - page * perPage), fromRow: page * perPage));
                }
            }
            else if (recorded == null) lines.Add(new PageLine("Reading Results"));
            else if (recorded.Results.Count == 0)
            {
                lines.Add(new PageLine(Availability(recorded.Availability, empty: true), rows: 3));
                pages = 1; page = 0;
            }
            else
            {
                var intro = Availability(recorded.Availability, empty: false);
                if (recorded.Earlier > 0) intro += (intro.Length == 0 ? "" : " ") + recorded.Earlier.ToString(CultureInfo.InvariantCulture) + " Earlier Results Kept on Computer";
                PageLine? explanation = intro.Length == 0 ? null : new PageLine(intro, rows: Math.Max(1, host.RowsOf(intro, Glaze.Menu.FileColumnDegrees)));
                if (explanation != null) lines.Add(explanation);
                var room = budget.Room - (explanation == null ? 0 : budget.Words(explanation.Rows) + budget.LineGap);
                var groups = new List<List<PageLine>>(); var group = new List<PageLine>(); var used = 0f;
                foreach (var item in recorded.Results)
                {
                    var words = LineWords.Ours(Wire(item.Kind) == "command" ? "Command Output" : "File Changes");
                    words = words.Then(LineWords.Written(" · " + Completion(item.Completion) + "\n" + When(item.OccurredAt)));
                    words = words.Then(LineWords.Written(item.Kind == ToolResultSummaryKind.Command
                        ? item.ExitCode.HasValue ? " · Exit " + item.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : " · Exit Not Recorded"
                        : " · " + item.FilesTotal!.Value.ToString(CultureInfo.InvariantCulture) + " Files"));
                    if (!item.RetainedComplete) words = words.Then(LineWords.Written(" · Partly Retained"));
                    var line = new PageLine(words, icon: GlazeIcon.Next, action: Read, key: item.EventId, opens: true, rows: int.MaxValue);
                    var rows = Math.Max(1, host.RowsOf(line, Glaze.Menu.FileColumnDegrees));
                    var cost = budget.Target(rows) + (group.Count > 0 ? budget.TargetGap : 0);
                    if (group.Count > 0 && used + cost > room) { groups.Add(group); group = new List<PageLine>(); used = 0; cost = budget.Target(rows); }
                    group.Add(new PageLine(words, icon: GlazeIcon.Next, action: Read, key: item.EventId, opens: true, rows: rows)); used += cost;
                }
                if (group.Count > 0) groups.Add(group);
                pages = groups.Count; page = Math.Min(page, pages - 1); lines.AddRange(groups[page]);
            }
            var footer = new Footer(new Prompt(Footer.Close, selected == null ? "Back to Task" : selectedFile != null ? "Back to Files" : "Back to Results", GlazeIcon.Back, PromptKind.Close),
                rare: page > 0 && (selected == null ? recorded != null : result != null) ? new Prompt(Previous, "Previous Page", GlazeIcon.Back) : null,
                secondary: selected == null ? new Prompt(Refresh, "Refresh", GlazeIcon.Refresh, available: Current,
                    reason: Current ? null : "Current Task Only", pageExplains: true) : null,
                farRight: page + 1 < pages ? new Prompt(Next, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage) : null);
            return new MenuFrame(subject, footer, lines: lines, sourceRuns: LineWords.Ours("Recorded · ").Then(LineWords.TheirName(sourceName))).Focus();
        }
        private static string Wire(object value) => HalcyonicJson.Serialize(value).Trim('"');
        private static string FileKey(ToolResultFile file) => HalcyonicJson.Serialize(new
        { file.Kind, file.Path, file.PathScope, file.PathComplete, file.MovePath, file.MovePathScope, file.MovePathComplete });
        private IReadOnlyList<PageLine> FileChoices(FileChangesToolResult changes, PageBudget budget)
        {
            var intro = Completion(changes.Completion) + " · " + When(result!.Summary.OccurredAt) + "\n"
                + changes.Files.Count + " of " + changes.FilesTotal.ToString(CultureInfo.InvariantCulture) + " Files · Recorded Patch · Files May Have Changed Since";
            var introRows = Math.Max(1, host.RowsOf(intro, Glaze.Menu.FileColumnDegrees));
            var head = new PageLine(intro, rows: introRows);
            if (changes.Files.Count == 0) { page = 0; pages = 1; return new[] { head, new PageLine("No Patches Retained") }; }
            var room = budget.Room - budget.Words(introRows) - budget.LineGap;
            var groups = new List<List<PageLine>>(); var group = new List<PageLine>(); var used = 0f;
            for (var index = 0; index < changes.Files.Count; index++)
            {
                var file = changes.Files[index];
                var words = LineWords.Ours(file.Kind switch { ToolResultFileKind.Add => "Added · ", ToolResultFileKind.Delete => "Deleted · ", _ => "Changed · " })
                    .Then(file.Path != null ? LineWords.TheirName(file.Path) : LineWords.Ours(file.PathScope == ToolResultPathScope.OutsideProject ? "Outside Project" : "Path Unknown"));
                if (file.MovePathScope != null) words = words.Then(LineWords.Ours(" · Moved"));
                var candidate = new PageLine(words, icon: GlazeIcon.Next, action: ReadFile, key: index.ToString(CultureInfo.InvariantCulture), opens: true, rows: 2);
                var rows = Math.Min(2, Math.Max(1, host.RowsOf(candidate, Glaze.Menu.FileColumnDegrees)));
                var cost = budget.Target(rows) + (group.Count > 0 ? budget.TargetGap : 0);
                if (group.Count > 0 && used + cost > room) { groups.Add(group); group = new List<PageLine>(); used = 0; cost = budget.Target(rows); }
                group.Add(new PageLine(words, icon: GlazeIcon.Next, action: ReadFile, key: index.ToString(CultureInfo.InvariantCulture), opens: true, rows: rows)); used += cost;
            }
            if (group.Count > 0) groups.Add(group);
            pages = groups.Count; page = Math.Min(page, pages - 1);
            return new[] { head }.Concat(groups[page]).ToList();
        }
        private static string Availability(ToolResultsAvailability availability, bool empty) => availability switch
        {
            ToolResultsAvailability.Unsupported => empty ? "Results Not Recorded by Agent App" : "Saved Results · Agent App No Longer Records",
            ToolResultsAvailability.Unavailable => empty ? "No Saved Results · Agent App Unavailable" : "Saved Results · Agent App Unavailable",
            ToolResultsAvailability.Unknown => empty ? "No Saved Results · Recording Support Unknown" : "Saved Results · Recording Support Unknown",
            _ => empty ? "No Results Yet" : "",
        };
        private static string Completion(ToolResultCompletion completion) => completion switch
        { ToolResultCompletion.Completed => "Finished", ToolResultCompletion.Failed => "Failed", ToolResultCompletion.Declined => "Declined", ToolResultCompletion.Interrupted => "Interrupted", _ => "Outcome Unknown" };
        private string When(string at) => TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), host.Zone)
            .ToString("d MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        private LineWords Detail(ToolResultResponse response)
        {
            var value = response.Result;
            var words = LineWords.Written((value.Kind == "command" ? "Command Output" : "File Changes") + " · " + Completion(value.Completion) + "\n" + When(response.Summary.OccurredAt) + "\n");
            var availability = recorded?.Availability ?? ToolResultsAvailability.Unknown;
            if (availability != ToolResultsAvailability.Available)
                words = words.Then(LineWords.Written(Availability(availability, empty: false) + "\n"));
            if (!string.IsNullOrEmpty(response.Summary.Title)) words = words.Then(LineWords.Theirs(response.Summary.Title)).Then(LineWords.Written("\n"));
            if (value is CommandToolResult command)
            {
                words = words.Then(LineWords.Written(command.ExitCode.HasValue ? "Exit Code · " + command.ExitCode.Value + "\n" : "Exit Code Not Recorded\n"));
                words = words.Then(Body(command.Output, patch: false));
            }
            else if (value is FileChangesToolResult changes)
            {
                words = words.Then(LineWords.Written(changes.FilesTotal.ToString(CultureInfo.InvariantCulture) + " Files · Recorded Patch · Files May Have Changed Since\n"));
                if (!changes.ListComplete) words = words.Then(LineWords.Written("Some File Changes Not Retained\n"));
                if (changes.Files.Count == 0) words = words.Then(LineWords.Written("No Patches Retained\n"));
                foreach (var file in selectedFile == null ? Array.Empty<ToolResultFile>() : new[] { changes.Files[selectedFile.Value] })
                {
                    var scoped = Wire(file.PathScope) == "project_relative";
                    words = words.Then(LineWords.Written("\n" + (Wire(file.Kind) switch { "add" => "Added · ", "delete" => "Deleted · ", _ => "Changed · " })))
                        .Then(scoped && file.Path != null ? LineWords.TheirName(file.Path) : LineWords.Ours(file.PathScope == ToolResultPathScope.OutsideProject ? "Outside Project" : "Path Unknown"));
                    if (file.Path != null && !file.PathComplete) words = words.Then(LineWords.Written("\nPath Shortened"));
                    if (file.MovePathScope != null)
                    {
                        words = words.Then(LineWords.Written("\nMoved To · ")).Then(Wire(file.MovePathScope) == "project_relative" && file.MovePath != null
                            ? LineWords.TheirName(file.MovePath) : LineWords.Ours(file.MovePathScope == ToolResultPathScope.OutsideProject ? "Outside Project" : "Path Unknown"));
                        if (file.MovePath != null && file.MovePathComplete == false) words = words.Then(LineWords.Written("\nPath Shortened"));
                    }
                    words = words.Then(LineWords.Written("\n"));
                    words = words.Then(scoped && (file.MovePathScope == null || Wire(file.MovePathScope) == "project_relative")
                        ? Body(file.Patch, patch: true) : LineWords.Ours(file.PathScope == ToolResultPathScope.Unknown || file.MovePathScope == ToolResultPathScope.Unknown
                            ? "Patch Not Retained · Scope Unknown" : "Patch Not Retained · Outside Project"));
                }
            }
            if (value.Redaction.Replacements > 0) words = words.Then(LineWords.Written("\nPrivate Text Removed"));
            return words;
        }
        private static LineWords Body(ToolResultBody body, bool patch)
        {
            if (Wire(body.Availability) != "available" || body.Text == null)
                return LineWords.Ours((patch ? "Patch" : "Output") + (body.Truncation == ToolResultBodyTruncation.AggregateBound ? " Omitted · Size Limit" : body.Availability switch
                { ToolResultBodyAvailability.Absent => " Not Supplied", ToolResultBodyAvailability.SourceOverLimit => " Too Large to Retain",
                    ToolResultBodyAvailability.OmittedScope => " Not Retained · Project Scope", ToolResultBodyAvailability.Malformed => " Unreadable",
                    _ => " Not Retained" }));
            var words = body.Text.Length == 0 ? LineWords.Ours(patch ? "Empty Patch" : "Empty Output") : RecordedResultText.Body(body.Text);
            return body.Complete ? words : words.Then(LineWords.Written(patch ? "\nPatch Shortened" : "\nOutput Shortened"));
        }
        public void Drawn(MenuFrame frame, Footer? sidePanel)
        { if (Current && active && frame == given && sidePanel == null) drawn = frame; }
        public void Act(string id, string? key)
        {
            if (!Current || !active || given == null || given != drawn) return;
            if (id == Footer.Close)
            {
                if (selectedFile != null) { FocusLeft(); selectedFile = null; selectedFileKey = null; page = fileListPage; pages = 1; Invalidate(); }
                else if (selected != null) { FocusLeft(); selected = null; page = listPage; pages = 1; Invalidate(); }
                else { FocusLeft(); if (BackToTask != null) BackToTask(); else Closed?.Invoke(); }
                return;
            }
            if (id == Refresh) { FocusLeft(); Invalidate(); return; }
            if (id == Next && page + 1 < pages) { page++; Invalidate(); return; }
            if (id == Previous && page > 0 && given.Footer.All.Any(slot => slot.Prompt.Id == Previous)) { page--; Invalidate(); return; }
            if (id == ReadFile && selectedFile == null && result?.Result is FileChangesToolResult changes
                && given.Lines.Any(line => line.Action == ReadFile && line.Key == key && line.Available)
                && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < changes.Files.Count)
            { selectedFile = index; selectedFileKey = FileKey(changes.Files[index]); fileListPage = page; page = 0; Invalidate(); return; }
            if (id != Read || key == null || selected != null || recorded == null
                || !given.Lines.Any(line => line.Action == Read && line.Key == key && line.Available)) return;
            var item = recorded.Results.SingleOrDefault(summary => summary.EventId == key);
            if (item == null) return;
            selected = HalcyonicJson.Deserialize<ToolResultSummary>(HalcyonicJson.Serialize(item)); selectedFile = null; selectedFileKey = null; listPage = page; page = 0; pages = 1; notice = null;
            reads?.Dispose(); reads = new CancellationTokenSource();
            try { readPending = read(item.EventId, reads.Token); }
            catch (Exception) { notice = "Result Unreadable · Back and Refresh"; }
            Invalidate();
        }
        private void Invalidate() { given = null; drawn = null; Changed?.Invoke(); }
        public void FocusLeft()
        {
            active = false; reads?.Cancel(); reads?.Dispose(); reads = null; attempted = false; listPending = null; readPending = null;
            // Selection and page are navigation only. They grant no read authority: a visible return
            // rereads the exact immutable event, even if it has left the recent summary list.
            recorded = null; result = null; notice = null; pages = 1; Invalidate();
        }
        public void Compact() => FocusLeft();
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}
