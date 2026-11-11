#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Where History stands (FIELD's A14, in A17's card language).</summary>
    public enum HistoryStage
    {
        /// <summary>The versions are being read.</summary>
        Reading,
        /// <summary>The versions, newest first, a page at a time.</summary>
        Versions,
        /// <summary>The computer keeps no history of this project (undo is off there).</summary>
        NotSetUp,
        /// <summary>The versions or a review could not be read.</summary>
        Unreadable,
        /// <summary>What restoring a version would do is being read.</summary>
        Previewing,
        /// <summary>What restoring a version would do, its files a page at a time, Restore behind its lock.</summary>
        Review,
        /// <summary>The restore was sent and the computer has not said it is done.</summary>
        Sending,
        /// <summary>The computer restored the version; Undo Restore is the main action.</summary>
        Done,
        /// <summary>The computer could not put every file back.</summary>
        Incomplete,
        /// <summary>The folder changed since the review, so nothing changed: review again.</summary>
        Stale,
        /// <summary>The computer refused the restore for another reason.</summary>
        Refused,
    }

    /// <summary>What a press in History does.</summary>
    public enum HistoryActionKind
    {
        /// <summary>Review restoring one version (a row of the list).</summary>
        Version,
        /// <summary>The next page of older versions.</summary>
        Older,
        /// <summary>Restore the version reviewed (Undo Add where it is the version before an add): behind its lock, as an add is.</summary>
        Restore,
        Cancel,
        /// <summary>Review restoring the version kept just before the restore: going back is itself undone in one step.</summary>
        UndoRestore,
        ReviewAgain,
        TryAgain,
        /// <summary>Open the project's Changes (XR's page).</summary>
        Changes,
        /// <summary>Leave History; on a later page of the versions, the newer page (<see cref="HistoryColumn.Back"/>).</summary>
        Back,
        /// <summary>Leave History from an outcome.</summary>
        Close,
    }

    /// <summary>One action History offers now, with its words, whether it is the main one, and whether it waits behind its lock.</summary>
    public readonly struct HistoryAction
    {
        public HistoryAction(HistoryActionKind kind, string words, bool main = false, bool locked = false, string? checkpointId = null)
        {
            Kind = kind;
            Words = words;
            Main = main;
            Locked = locked;
            CheckpointId = checkpointId;
        }

        public HistoryActionKind Kind { get; }

        public string Words { get; }

        public bool Main { get; }

        public bool Locked { get; }

        /// <summary>The version a <see cref="HistoryActionKind.Version"/> or <see cref="HistoryActionKind.UndoRestore"/> reviews.</summary>
        public string? CheckpointId { get; }
    }

    /// <summary>One version as History shows it: its name, when, and its kind's glyph.</summary>
    public sealed class HistoryVersion
    {
        public HistoryVersion(CheckpointView view, LineWords name, string when, RoomKind kind)
        {
            View = view;
            Name = name;
            When = when;
            Kind = kind;
        }

        public CheckpointView View { get; }

        public LineWords Name { get; }

        public string When { get; }

        public RoomKind Kind { get; }
    }

    /// <summary>
    /// History and Restore (gap 2 of the parity map; FIELD's A14 states in A17's card language): the versions the
    /// computer kept of a project, newest first; a version's review of what restoring it would do, its files a page at
    /// a time, Restore behind its lock on the first part; then sent, done with Undo Restore, incomplete, out of date
    /// and not set up. Each state is a <see cref="RoomCard"/> (<see cref="Card"/>) with its actions
    /// (<see cref="Actions"/>). A restore carries the digest of the review the person read (ADR 0051), so a folder that
    /// changed since is never restored: the computer says the review is out of date. Undo Restore opens the review of
    /// the version kept just before the restore, so even the undo is read before it acts. Nothing here reads the
    /// network or sends: the host reads through <see cref="ProjectHistoryReader"/> and sends <see cref="Restore"/>'s
    /// command.
    /// </summary>
    public sealed class HistoryColumn
    {
        /// <summary>The rows a page shows: the card's own.</summary>
        public const int APage = RoomCard.ListAPage;

        private IReadOnlyList<HistoryVersion> versions = Array.Empty<HistoryVersion>();
        private RestorePreview? preview;
        private string? review;
        private HistoryVersion? reviewed;
        private ProjectRestoredPayload? restored;
        private ProjectRestoreIncompletePayload? incomplete;
        private string? sentCommandId;
        private int versionPage;
        private bool armed;

        public HistoryColumn(string projectId, string projectName)
        {
            ProjectId = string.IsNullOrEmpty(projectId) ? throw new ArgumentException("A project is required.", nameof(projectId)) : projectId;
            ProjectName = projectName ?? "";
        }

        public string ProjectId { get; }

        public string ProjectName { get; }

        public HistoryStage Stage { get; private set; } = HistoryStage.Reading;

        /// <summary>The versions, newest first.</summary>
        public IReadOnlyList<HistoryVersion> Versions => versions;

        /// <summary>The version whose restore is being read, reviewed, sent or done; null at the list.</summary>
        public HistoryVersion? Reviewed => reviewed;

        /// <summary>The digest of the review shown, which a restore carries back; null where none is shown.</summary>
        public string? ReviewDigest => Stage == HistoryStage.Review ? review : null;

        /// <summary>The page of the versions shown, from 0 (the review is one part, its rows past five counted in its foot).</summary>
        public int Page => versionPage;

        /// <summary>How many pages the versions have.</summary>
        public int PageCount => Pages(versions.Count);

        /// <summary>
        /// What History says in the column's sentence where it shows no card (reading, or sent), else null: the sweep's words.
        /// </summary>
        public string? Sentence => Stage switch
        {
            HistoryStage.Reading => HistoryText.Reading,
            HistoryStage.Previewing => HistoryText.Previewing,
            HistoryStage.Sending => HistoryText.Sending(Files()),
            _ => null,
        };

        /// <summary>Restore was pressed once and waits for its second press (its lock).</summary>
        public bool Armed => Stage == HistoryStage.Review && armed;

        /// <summary>The id of the restore command sent, so the host can match its answer; null where none is out.</summary>
        public string? SentCommandId => Stage == HistoryStage.Sending ? sentCommandId : null;

        /// <summary>
        /// The versions as the computer answered, named from the person's own tasks in <paramref name="state"/>, with the
        /// time of each in <paramref name="zone"/> against <paramref name="now"/>.
        /// </summary>
        public void Listed(CheckpointsResponse response, ClientProjection? state, DateTimeOffset now, TimeZoneInfo zone)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.ProjectId != ProjectId) return;
            switch (response.Result)
            {
                case AvailableCheckpoints available:
                    versions = available.Checkpoints
                        .Where(each => each.ProjectId == ProjectId)
                        .OrderByDescending(each => each.RecordedAt, StringComparer.Ordinal)
                        .ThenByDescending(each => each.CheckpointId, StringComparer.Ordinal)
                        .Select(each => Version(each, state, now, zone))
                        .ToList();
                    versionPage = 0;
                    Stage = HistoryStage.Versions;
                    break;
                case UnavailableCheckpoints:
                    versions = Array.Empty<HistoryVersion>();
                    Stage = HistoryStage.NotSetUp;
                    break;
            }
            Forget();
        }

        /// <summary>The versions or a review could not be read (the computer unreachable, or its answer unreadable).</summary>
        public void ReadFailed()
        {
            Forget();
            Stage = HistoryStage.Unreadable;
        }

        /// <summary>Reads again from the start (Try Again): the host then reads the versions.</summary>
        public void Reread()
        {
            Forget();
            versions = Array.Empty<HistoryVersion>();
            Stage = HistoryStage.Reading;
        }

        /// <summary>
        /// Opens the review of <paramref name="checkpointId"/>, one of the versions listed: the host then reads its
        /// preview. False where it is not one of them, or where a review cannot open now.
        /// </summary>
        public bool Choose(string checkpointId)
        {
            if (Stage != HistoryStage.Versions && Stage != HistoryStage.Done && Stage != HistoryStage.Stale && Stage != HistoryStage.Incomplete
                && Stage != HistoryStage.Refused && Stage != HistoryStage.Review)
                return false;
            var version = versions.FirstOrDefault(each => each.View.CheckpointId == checkpointId);
            if (version == null && restored?.BeforeRestoreCheckpointId == checkpointId)
                version = BeforeRestore(checkpointId);
            if (version == null) return false;
            Forget();
            reviewed = version;
            Stage = HistoryStage.Previewing;
            return true;
        }

        /// <summary>What restoring the version chosen would do now, and the digest of that review.</summary>
        public void Previewed(RestorePreviewResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (Stage != HistoryStage.Previewing || reviewed == null || response.ProjectId != ProjectId) return;
            switch (response.Result)
            {
                case AvailableRestorePreview available when available.Preview.CheckpointId == reviewed.View.CheckpointId:
                    preview = available.Preview;
                    review = available.Review;
                    armed = false;
                    Stage = HistoryStage.Review;
                    break;
                case UnavailableRestorePreview:
                    Stage = HistoryStage.NotSetUp;
                    break;
            }
        }

        /// <summary>The next page of older versions (Older); false where none follows.</summary>
        public bool Older()
        {
            if (Stage != HistoryStage.Versions || versionPage + 1 >= PageCount) return false;
            versionPage++;
            return true;
        }

        /// <summary>Back on the versions: the newer page where a later page shows (true), else leave History (false).</summary>
        public bool Back()
        {
            if (Stage != HistoryStage.Versions || versionPage == 0) return false;
            versionPage--;
            return true;
        }

        /// <summary>The first press of Restore: it stands behind its lock until pressed again.</summary>
        public bool Arm()
        {
            if (Stage != HistoryStage.Review || review == null) return false;
            armed = true;
            return true;
        }

        /// <summary>
        /// The restore to send once Restore was pressed through its lock: the version reviewed, with the digest of the
        /// review shown. Null where it was not armed. The column then waits for the computer's answer.
        /// </summary>
        public ProjectRestoreCommand? Restore(ClientInfo client, DateTimeOffset at)
        {
            if (!Armed || reviewed == null || review == null) return null;
            var command = new ProjectRestoreCommand
            {
                CommandId = Guid.NewGuid().ToString("D"),
                IssuedAt = HalcyonicJson.FormatTimestamp(at),
                Client = client,
                Payload = new ProjectRestorePayload { ProjectId = ProjectId, CheckpointId = reviewed.View.CheckpointId, Review = review },
            };
            sentCommandId = command.CommandId;
            armed = false;
            Stage = HistoryStage.Sending;
            return command;
        }

        /// <summary>Back from the review to the list, or from an outcome to the list as it was.</summary>
        public void Cancel()
        {
            Forget();
            Stage = HistoryStage.Versions;
        }

        /// <summary>
        /// The computer's answer to the restore sent: refused as out of date (<c>review_stale</c>, nothing changed), or
        /// refused otherwise. An accepted command waits for the restore's own event.
        /// </summary>
        public void Answered(CommandView command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (Stage != HistoryStage.Sending || command.CommandId != sentCommandId) return;
            if (command.Status != CommandStatus.Rejected) return;
            Stage = command.Rejection?.Code == RejectionCode.ReviewStale ? HistoryStage.Stale : HistoryStage.Refused;
        }

        /// <summary>The computer restored a version of this project (<c>project.restored</c>).</summary>
        public void Restored(ProjectRestoredEvent restoredEvent)
        {
            if (restoredEvent == null) throw new ArgumentNullException(nameof(restoredEvent));
            if (restoredEvent.ProjectId != ProjectId) return;
            var payload = restoredEvent.Payload;
            if (Stage != HistoryStage.Sending || reviewed?.View.CheckpointId != payload.CheckpointId) return;
            restored = payload;
            Stage = HistoryStage.Done;
        }

        /// <summary>The computer could not put every file of the version back (<c>project.restore_incomplete</c>).</summary>
        public void RestoreIncomplete(ProjectRestoreIncompleteEvent incompleteEvent)
        {
            if (incompleteEvent == null) throw new ArgumentNullException(nameof(incompleteEvent));
            if (incompleteEvent.ProjectId != ProjectId) return;
            var payload = incompleteEvent.Payload;
            if (Stage != HistoryStage.Sending || reviewed?.View.CheckpointId != payload.CheckpointId) return;
            incomplete = payload;
            Stage = HistoryStage.Incomplete;
        }

        /// <summary>What History shows now, as a card, never a fact joined to another by a dot; null where it shows only its <see cref="Sentence"/>.</summary>
        public RoomCard? Card() => Stage switch
        {
            HistoryStage.Reading or HistoryStage.Previewing or HistoryStage.Sending => null,
            HistoryStage.Versions => VersionsCard(),
            HistoryStage.NotSetUp => Status(HistoryText.NoHistory, HistoryText.NoneKeptLine, RoomTone.Quiet),
            HistoryStage.Unreadable => Status(HistoryText.NotRead, HistoryText.NotReadLine, RoomTone.Bad),
            HistoryStage.Review => ReviewCard(),
            HistoryStage.Done => Status(HistoryText.Restored, HistoryText.AsTheyWere(RestoredFiles()), RoomTone.Good),
            HistoryStage.Stale => Status(HistoryText.NotRestored, HistoryText.ChangedWhileLooking, RoomTone.Bad),
            HistoryStage.Incomplete => Status(HistoryText.NotRestored, incomplete?.Written == true ? HistoryText.SomeChanged : HistoryText.NothingChanged, RoomTone.Bad),
            _ => Status(HistoryText.NotRestored, HistoryText.NothingChanged, RoomTone.Bad),
        };

        /// <summary>The actions History offers now, in the column's order: the main one first, the ways out last.</summary>
        public IReadOnlyList<HistoryAction> Actions()
        {
            var actions = new List<HistoryAction>();
            switch (Stage)
            {
                case HistoryStage.Versions:
                    foreach (var version in VersionsOnPage())
                        actions.Add(new HistoryAction(HistoryActionKind.Version, version.Name.Text, checkpointId: version.View.CheckpointId));
                    if (versionPage + 1 < PageCount) actions.Add(new HistoryAction(HistoryActionKind.Older, HistoryText.Older));
                    actions.Add(new HistoryAction(HistoryActionKind.Back, HistoryText.Back));
                    break;
                case HistoryStage.Review:
                    var words = reviewed!.View.Reason == CheckpointReason.BeforeAdd ? HistoryText.UndoAdd : HistoryText.Restore;
                    actions.Add(new HistoryAction(HistoryActionKind.Restore, words, main: true, locked: !armed, checkpointId: reviewed.View.CheckpointId));
                    actions.Add(new HistoryAction(HistoryActionKind.Cancel, HistoryText.Cancel));
                    break;
                case HistoryStage.Done:
                    actions.Add(new HistoryAction(HistoryActionKind.UndoRestore, HistoryText.UndoRestore, main: true, checkpointId: restored!.BeforeRestoreCheckpointId));
                    actions.Add(new HistoryAction(HistoryActionKind.Changes, HistoryText.Changes));
                    actions.Add(new HistoryAction(HistoryActionKind.Close, HistoryText.Close));
                    break;
                case HistoryStage.Stale:
                    actions.Add(new HistoryAction(HistoryActionKind.ReviewAgain, HistoryText.ReviewAgain, main: true, checkpointId: reviewed!.View.CheckpointId));
                    actions.Add(new HistoryAction(HistoryActionKind.Close, HistoryText.Close));
                    break;
                case HistoryStage.Incomplete:
                    actions.Add(new HistoryAction(HistoryActionKind.Changes, HistoryText.Changes));
                    actions.Add(new HistoryAction(HistoryActionKind.Close, HistoryText.Close));
                    break;
                case HistoryStage.Refused:
                    actions.Add(new HistoryAction(HistoryActionKind.Close, HistoryText.Close));
                    break;
                case HistoryStage.Unreadable:
                    actions.Add(new HistoryAction(HistoryActionKind.TryAgain, HistoryText.TryAgain, main: true));
                    actions.Add(new HistoryAction(HistoryActionKind.Back, HistoryText.Back));
                    break;
                case HistoryStage.NotSetUp:
                    actions.Add(new HistoryAction(HistoryActionKind.Back, HistoryText.Back));
                    break;
            }
            return actions;
        }

        private static RoomCard Status(string title, string line, RoomTone tone) => new RoomCard(status: new RoomCardStatus(title, line, tone));

        private RoomCard VersionsCard()
        {
            if (versions.Count == 0) return Status(HistoryText.NoHistory, HistoryText.NoneYetLine, RoomTone.Quiet);
            var rows = VersionsOnPage().Select(version => new RoomListRow(version.Kind, version.Name, version.When)).ToList();
            return new RoomCard(title: LineWords.Ours(HistoryText.Title), description: LineWords.Ours(HistoryText.Description), list: rows);
        }

        private RoomCard ReviewCard()
        {
            var all = ReviewRows();
            var rows = all.Take(APage).ToList();
            var more = preview!.ProjectHeadMoved == true ? HistoryText.ChangedSince
                : Files() > rows.Count ? HistoryText.AndMore(Files() - rows.Count)
                : null;
            return new RoomCard(title: reviewed!.Name, description: LineWords.Ours(HistoryText.CanUndo), list: rows, more: more);
        }

        private List<RoomListRow> ReviewRows()
        {
            var rows = new List<RoomListRow>();
            if (preview == null) return rows;
            rows.AddRange(preview.ChangedBack.Paths.Select(path => new RoomListRow(RoomKind.Reverts, LineWords.Theirs(path), HistoryText.Reverts, mono: true)));
            rows.AddRange(preview.Removed.Paths.Select(path => new RoomListRow(RoomKind.Removed, LineWords.Theirs(path), HistoryText.Goes, RoomTone.Bad, mono: true)));
            rows.AddRange(preview.Returned.Paths.Select(path => new RoomListRow(RoomKind.Added, LineWords.Theirs(path), HistoryText.ComesBack, RoomTone.Good, mono: true)));
            return rows;
        }

        private long RestoredFiles() => restored == null ? 0 : restored.ChangedBack.Total + restored.Removed.Total + restored.Returned.Total;

        private long Files() => preview == null ? 0 : preview.ChangedBack.Total + preview.Removed.Total + preview.Returned.Total;

        private IEnumerable<HistoryVersion> VersionsOnPage() => versions.Skip(versionPage * APage).Take(APage);

        private static int Pages(int rows) => Math.Max(1, (rows + APage - 1) / APage);

        private void Forget()
        {
            preview = null;
            review = null;
            reviewed = null;
            restored = null;
            incomplete = null;
            sentCommandId = null;
            armed = false;
        }

        private HistoryVersion BeforeRestore(string checkpointId)
        {
            var view = new CheckpointView
            {
                CheckpointId = checkpointId,
                ProjectId = ProjectId,
                Reason = CheckpointReason.BeforeRestore,
                // Kept by the restore just now; the list holds it once read again.
                RecordedAt = "",
            };
            return new HistoryVersion(view, LineWords.Ours(HistoryText.BeforeARestore), "", RoomKind.Reverts);
        }

        private static HistoryVersion Version(CheckpointView view, ClientProjection? state, DateTimeOffset now, TimeZoneInfo zone)
        {
            var task = TaskOf(view.ExecutionId, state);
            var (name, kind) = view.Reason switch
            {
                CheckpointReason.BeforeTask => (Named("Before ", task, " Started", HistoryText.BeforeATask), RoomKind.Started),
                CheckpointReason.AfterTurn => (Named("After ", task, " Worked", HistoryText.AfterATask), RoomKind.Worked),
                CheckpointReason.BeforeAdd => (LineWords.Ours(HistoryText.BeforeTheAdd), RoomKind.ToAdd),
                _ => (LineWords.Ours(HistoryText.BeforeARestore), RoomKind.Reverts),
            };
            return new HistoryVersion(view, name, HistoryText.When(view.RecordedAt, now, zone), kind);
        }

        private static LineWords Named(string before, string? task, string after, string without) =>
            task == null ? LineWords.Ours(without) : LineWords.Ours(before) + LineWords.Theirs(task) + LineWords.Ours(after);

        private static string? TaskOf(string? executionId, ClientProjection? state)
        {
            if (executionId == null || state == null) return null;
            if (!state.Executions.TryGetValue(executionId, out var execution)) return null;
            if (!state.Workstreams.TryGetValue(execution.WorkstreamId, out var work)) return null;
            return string.IsNullOrWhiteSpace(work.Title) ? null : work.Title;
        }
    }

    /// <summary>History's words, from FIELD's parts note of 2026-11-11 (A14 as cards, in A17's terms; Go Back is Restore).</summary>
    public static class HistoryText
    {
        public const string Title = "History";
        public const string Description = "Pick a moment to restore.";
        public const string CanUndo = "You can undo this.";
        public const string Reading = "Reading History";
        public const string Previewing = "Reading Changes";
        public const string NoHistory = "No History Yet";
        public const string NoneKeptLine = "The computer keeps none for this project";
        public const string NoneYetLine = "Nothing is kept until a task starts";
        public const string NotRead = "Not Read";
        public const string NotReadLine = "The computer did not answer";
        public const string Restored = "Restored";
        public const string NotRestored = "Not Restored";
        public const string ChangedWhileLooking = "The project changed while you looked";
        public const string SomeChanged = "Some files changed: check Changes";
        public const string NothingChanged = "Nothing changed";
        public const string ChangedSince = "The project changed since then too.";
        public const string Reverts = "Reverts";
        public const string Goes = "Goes";
        public const string ComesBack = "Comes Back";
        public const string BeforeATask = "Before a Task Started";
        public const string AfterATask = "After a Task Worked";
        public const string BeforeARestore = "Before a Restore";
        public const string BeforeTheAdd = "Before the Add";
        public const string Restore = "Restore";
        public const string UndoAdd = "Undo Add";
        public const string Cancel = "Cancel";
        public const string UndoRestore = "Undo Restore";
        public const string ReviewAgain = "Review Again";
        public const string TryAgain = "Try Again";
        public const string Changes = "Changes";
        public const string Older = "Older";
        public const string Back = "Back";
        public const string Close = "Close";

        public static string Sending(long files) => "Restoring " + (files == 1 ? "1 File" : files.ToString(CultureInfo.InvariantCulture) + " Files");

        public static string AsTheyWere(long files) =>
            files == 1 ? "1 file is as it was" : files.ToString(CultureInfo.InvariantCulture) + " files are as they were";

        public static string AndMore(long files) => "And " + files.ToString(CultureInfo.InvariantCulture) + " More";

        /// <summary>When a version was kept, as a row's note: "18:40" today, "Yesterday 09:12", else "12 Nov 09:00".</summary>
        public static string When(string recordedAt, DateTimeOffset now, TimeZoneInfo zone)
        {
            if (!DateTimeOffset.TryParse(recordedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return "";
            var local = TimeZoneInfo.ConvertTime(at, zone);
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (local.Date == today) return time;
            if (local.Date == today.AddDays(-1)) return "Yesterday " + time;
            return local.ToString("d MMM", CultureInfo.InvariantCulture) + " " + time;
        }
    }
}
