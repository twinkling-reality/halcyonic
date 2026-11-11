#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The redesign's pages over a task at rest: its result, the files the computer saw change, and the steps of the computer's own check.</summary>
    public enum RoomResultPage
    {
        Result,
        Changes,
        Checks,
        /// <summary>What adding the task's work to its project brings: FIELD's review card, then Add to Project behind its lock (A13).</summary>
        Add,
        /// <summary>Where the project changed the same files: FIELD's card naming them, then Let Agent Fix (A13).</summary>
        Conflict,
        /// <summary>The project's history and its restore (DEVICE's <see cref="HistoryColumn"/>, gap 2), reached from Changes.</summary>
        History,
        /// <summary>Sharing the checked game (DEVICE's <see cref="ShareColumn"/>, ADR 0054), reached from the passed result at rest.</summary>
        Share,
        /// <summary>SCREENS's file of Changes read: its change over the turn, then the whole file (<see cref="FileChangeColumn"/>).</summary>
        File,
    }

    /// <summary>
    /// What the computer itself knows of a task at rest, for the redesign's result (DESIGN's BUILD-SPEC-v4 2.5 to 2.8):
    /// the files it saw change over the last turn it observed, and its own last check of the project, step by step.
    /// Nothing here is the agent's word: a count stands only where the computer read the folder, a check line only
    /// where the computer ran the check, and what it does not know is left out.
    /// </summary>
    public sealed class RoomResult
    {
        /// <summary>The redesign's own actions over the result: none is the file's frame's, so the director takes each itself.</summary>
        public const string MakeVrVersion = "room-make-vr-version";
        public const string TryBuild = "room-try-build";
        public const string ShowChanges = "room-changes";
        public const string ShowChecks = "room-checks";
        public const string NextPage = "room-next-page";
        public const string Back = "room-back";
        /// <summary>DEVICE's Play in Headset (<see cref="HeadsetPlay"/>): the project's checked preview opened in Quest Browser.</summary>
        public const string PlayInHeadset = "room-play-in-headset";
        /// <summary>A13: the review of what adding brings, its confirming action, the files both changed, and Let Agent Fix.</summary>
        public const string ShowAdd = "room-show-add";
        public const string AddConfirm = "room-add";
        public const string ShowConflict = "room-show-conflict";
        public const string Fit = "room-fit";
        /// <summary>DEVICE's History (<see cref="HistoryColumn"/>): open it, pick a version (its key the version's id), page, restore behind the lock, and the ways out.</summary>
        public const string ShowHistory = "room-history";
        public const string HistoryVersion = "room-history-version";
        public const string HistoryOlder = "room-history-older";
        public const string HistoryRestore = "room-history-restore";
        public const string HistoryCancel = "room-history-cancel";
        public const string HistoryUndo = "room-history-undo";
        public const string HistoryAgain = "room-history-again";
        public const string HistoryTryAgain = "room-history-try-again";
        public const string HistoryChanges = "room-history-changes";
        public const string HistoryBack = "room-history-back";
        public const string HistoryClose = "room-history-close";
        /// <summary>DEVICE's Share (<see cref="ShareColumn"/>): open it, pick a way (its key the way), share behind the lock, stop, and the ways out.</summary>
        public const string ShowShare = "room-share";
        public const string ShareWayAction = "room-share-way";
        public const string ShareStart = "room-share-start";
        public const string ShareCancel = "room-share-cancel";
        public const string ShareStop = "room-share-stop";
        public const string ShareAgain = "room-share-again";
        public const string ShareCheckGame = "room-share-check";
        public const string ShareStartPreview = "room-share-preview";
        public const string ShareBack = "room-share-back";
        public const string ShareClose = "room-share-close";
        /// <summary>SCREENS's file reading (<see cref="FileChangeColumn"/>): a row of Changes pressed (its key the path), its pages, Whole File, Try Again, Back.</summary>
        public const string ShowFile = "room-file";
        public const string FileNextPage = "room-file-next-page";
        public const string FilePreviousPage = "room-file-previous-page";
        public const string FileWhole = "room-file-whole";
        public const string FileTryAgain = "room-file-try-again";
        public const string FileBack = "room-file-back";

        public const string ChangesWords = "Changes";
        public const string ChecksWords = "Checks";

        /// <summary>The kind of an observed file in a row, short for the product's File Made, Change Observed and File Removed (BUILD-SPEC-v4 2.6).</summary>
        public const string Made = "Made";
        public const string Changed = "Changed";
        public const string Removed = "Removed";

        /// <summary>Every action id of the result's own.</summary>
        public static readonly IReadOnlyList<string> Actions = new[]
        {
            MakeVrVersion, TryBuild, ShowChanges, ShowChecks, NextPage, Back, PlayInHeadset, ShowAdd, AddConfirm, ShowConflict, Fit,
            ShowHistory, HistoryVersion, HistoryOlder, HistoryRestore, HistoryCancel, HistoryUndo, HistoryAgain, HistoryTryAgain, HistoryChanges, HistoryBack, HistoryClose,
            ShowShare, ShareWayAction, ShareStart, ShareCancel, ShareStop, ShareAgain, ShareCheckGame, ShareStartPreview, ShareBack, ShareClose,
        }.Concat(new[] { ShowFile, FileNextPage, FilePreviousPage, FileWhole, FileTryAgain, FileBack }).ToArray();

        /// <summary>SCREENS's file reading's own action ids.</summary>
        public static readonly IReadOnlyList<string> FileActions = new[] { ShowFile, FileNextPage, FilePreviousPage, FileWhole, FileTryAgain, FileBack };

        /// <summary>A result with nothing known of it: no check, no files, no actions, as after a restart before its history is read.</summary>
        public static RoomResult Nothing { get; } = new RoomResult(false, false, false, null, null, null);

        public RoomResult(bool isVr, bool canMakeVr, bool canTry, bool? passed, ObservedTurn? observed, IReadOnlyList<CheckStepOutcome>? steps,
            RoomResultPage page = RoomResultPage.Result, int part = 0, Halcyonic.Client.PlayInHeadset? play = null, RoomAdd? add = null, string? addReview = null,
            HistoryColumn? history = null, ShareColumn? share = null, bool canShare = false, IReadOnlyList<ProjectPlace>? places = null, FileChangeColumn? file = null,
            bool readsFiles = false)
        {
            ReadsFiles = readsFiles;
            File = page == RoomResultPage.File ? file : null;
            AddReview = addReview;
            Play = play;
            // The project's one list of places (LINK's ProjectPlaces, the lead's 11:37Z); where none is given, as in a fixed state, the
            // same rule from Play: Play in Headset where it stands.
            Places = places ?? (play is Halcyonic.Client.PlayInHeadset given && HeadsetPlay.Stands(given)
                ? new[] { new ProjectPlace(ProjectPlaceKind.PlayInHeadset, HeadsetPlay.ActionWords, "") } : Array.Empty<ProjectPlace>());
            Add = add;
            History = page == RoomResultPage.History ? history : null;
            Share = page == RoomResultPage.Share ? share : null;
            CanShare = canShare;
            IsVr = isVr;
            CanMakeVr = canMakeVr;
            CanTry = canTry;
            Passed = passed;
            Observed = observed;
            Steps = steps ?? Array.Empty<CheckStepOutcome>();
            Page = page;
            Part = Math.Max(0, Math.Min(part, Parts - 1));
        }

        /// <summary>
        /// Whether a row of Changes may be pressed to read its file: only where a computer can be asked, never in the
        /// demonstration, whose recording holds no file contents (AUDIT's F1, 2026-11-12: an action that cannot act is not drawn).
        /// </summary>
        public bool ReadsFiles { get; }

        /// <summary>Whether the project is made from a starter, a VR project already.</summary>
        public bool IsVr { get; }

        /// <summary>Whether Make VR Version can be asked now: not a VR project, and a place for its copy can be named.</summary>
        public bool CanMakeVr { get; }

        /// <summary>Whether Try Build may stand (<see cref="KitScreenModel.CanTry"/>): a passed check of the computer's own, nothing changed since, no turn running.</summary>
        public bool CanTry { get; }

        /// <summary>The digest of the copy's reading whose files the add's review shows (ADR 0051, amended); null where none.</summary>
        public string? AddReview { get; }

        /// <summary>The task's own copy against its project (ADR 0045, A13); null where the task carries none.</summary>
        public RoomAdd? Add { get; }

        /// <summary>Whether Play in Headset stands for the project, and if not why (<see cref="HeadsetPlay.For"/>); null where unknown.</summary>
        public Halcyonic.Client.PlayInHeadset? Play { get; }

        /// <summary>The places the project offers now (<see cref="ProjectPlaces.Of"/>): the result's Play is drawn from this list, never a Play of its own.</summary>
        public IReadOnlyList<ProjectPlace> Places { get; }

        /// <summary>The project's history where the History page shows (DEVICE's <see cref="HistoryColumn"/>); null on every other page.</summary>
        public HistoryColumn? History { get; }
        /// <summary>The file of Changes being read where the File page shows (SCREENS's <see cref="FileChangeColumn"/>); null on every other page.</summary>
        public FileChangeColumn? File { get; }

        /// <summary>The project's share where the Share page shows (DEVICE's <see cref="ShareColumn"/>); null on every other page.</summary>
        public ShareColumn? Share { get; }

        /// <summary>Whether Share stands on the result at rest: the host would admit it, or a share of the project stands.</summary>
        public bool CanShare { get; }

        /// <summary>The computer's own last check of the project: passed, failed, or null where it ran none or its outcome is unknown.</summary>
        public bool? Passed { get; }

        /// <summary>The last turn of the task's run the computer observed, or null where none arrived.</summary>
        public ObservedTurn? Observed { get; }

        /// <summary>The steps of the computer's own last check; none where it ran none.</summary>
        public IReadOnlyList<CheckStepOutcome> Steps { get; }

        public RoomResultPage Page { get; }

        /// <summary>Which page of the rows shows, from 0.</summary>
        public int Part { get; }

        /// <summary>How many files the computer saw made, changed and removed; null where it read no turn or could not read the folder.</summary>
        public long? FilesChanged => Observed is { Skipped: null } turn ? turn.CreatedTotal + turn.ChangedTotal + turn.DeletedTotal : (long?)null;

        /// <summary>"4 Files Changed", "1 File Changed"; null where the computer observed nothing.</summary>
        public string? CountLine => FilesChanged is long count ? count + (count == 1 ? " File Changed" : " Files Changed") : null;

        /// <summary>The check's line, the product's own: Build Passed or Build Failed; null where the computer's check gave no outcome.</summary>
        public string? CheckLine => Passed is bool passed ? KitMomentWords.CheckTitle(KitCheckScope.Builds, ended: true, passed) : null;

        /// <summary>Whether the computer has anything to say of the task at rest.</summary>
        public bool Any => Observed != null || Passed != null || Steps.Count > 0 || Add?.State is RoomAddState.Ready or RoomAddState.Conflict || History != null || Share != null;

        /// <summary>The page's rows: an observed file a row, or a step of the check a row; none on the result itself.</summary>
        public IReadOnlyList<RoomRow> Rows => Page switch
        {
            RoomResultPage.Changes => ChangeRows(),
            // A step the check never reached has no row: a failure shows which step failed, and nothing is said of the rest.
            RoomResultPage.Checks => Steps.Where(step => step.Result != CheckStepResult.NotRun).Select(step => new RoomRow(LineWords.Ours(StepWords(step.Step)),
                step.Result == CheckStepResult.Passed ? RoomRowMark.Check : step.Result == CheckStepResult.Failed ? RoomRowMark.Cross : RoomRowMark.None)).ToList(),
            _ => Array.Empty<RoomRow>(),
        };

        /// <summary>The rows the page shows now, eight at most (<see cref="RoomRows.APage"/>).</summary>
        public IReadOnlyList<RoomRow> Shown => RoomRows.Page(Rows, Part);

        /// <summary>How many pages the page shown turns through: Changes by the card's list, five a page (FIELD's A17); the rest by rows.</summary>
        public int Parts => Page == RoomResultPage.Changes ? ListPages(Changes.Count)
            : Page == RoomResultPage.Add ? ListPages(Add?.Paths.Count ?? 0)
            : RoomRows.Pages(Rows.Count);

        private static int ListPages(int count) => Math.Max(1, (count + RoomCard.ListAPage - 1) / RoomCard.ListAPage);

        /// <summary>
        /// Every file the computer saw change, its facts apart for a card's list (FIELD's A17): the kind, the path as the folder
        /// names it, and for a change its lines before and after. Empty where it observed no turn or could not read the
        /// folder (<see cref="NotObserved"/>).
        /// </summary>
        public IReadOnlyList<RoomChange> Changes => Observed is { Skipped: null } turn
            ? turn.Files.Select(file => new RoomChange(
                file.Change == ObservedChange.Created ? RoomChangeKind.Made : file.Change == ObservedChange.Deleted ? RoomChangeKind.Removed : RoomChangeKind.Changed,
                file.Path,
                file.Change == ObservedChange.Changed ? file.LinesBefore : null,
                file.Change == ObservedChange.Changed ? file.LinesAfter : null)).ToList()
            : (IReadOnlyList<RoomChange>)Array.Empty<RoomChange>();

        /// <summary>
        /// Whether the computer cannot say what changed: it observed no turn, or observed one but could not read the folder. Its honest
        /// line, never an empty list read as no changes, now that Changes is always offered at rest (the lead's one Changes, 11:15Z).
        /// </summary>
        public bool NotObserved => Observed is null or { Skipped: not null };

        /// <summary>
        /// Every step of the computer's last check, named and toned apart (A17), in the check's order, those it never reached
        /// included, so a card can hang its marks on the step that failed.
        /// </summary>
        public IReadOnlyList<RoomStep> StepFacts => Steps.Select(step => new RoomStep(step.Step, StepWords(step.Step),
            step.Result == CheckStepResult.Passed ? RoomStepTone.Passed : step.Result == CheckStepResult.Failed ? RoomStepTone.Failed : RoomStepTone.NotRun)).ToList();

        /// <summary>A file as the computer saw it: its kind, for a change its lines before and after, then its path as the folder names it.</summary>
        private IReadOnlyList<RoomRow> ChangeRows()
        {
            if (Observed == null) return Array.Empty<RoomRow>();
            // The computer could not say what changed: its honest line, never an empty list read as no changes.
            if (Observed.Skipped != null) return new[] { new RoomRow(LineWords.Ours(KitMomentWords.NotObservedWords)) };
            // The path is the folder's own name, outside text, so it leans and stands last: Halcyonic's words never follow a
            // leaning name across a separator (the cadence the product keeps for every outside name).
            return Observed.Files.Select(file =>
            {
                var kind = file.Change == ObservedChange.Created ? Made : file.Change == ObservedChange.Deleted ? Removed : Changed;
                var lead = kind + KitMomentWords.Dot;
                if (file.Change == ObservedChange.Changed && file.LinesBefore is long before && file.LinesAfter is long after)
                    lead += before + " to " + after + " Lines" + KitMomentWords.Dot;
                return new RoomRow(LineWords.Ours(lead) + LineWords.TheirName(file.Path));
            }).ToList();
        }

        /// <summary>A step of the computer's check, in DESIGN's words (BUILD-SPEC-v4 2.8).</summary>
        public static string StepWords(CheckStepKind step) => step switch
        {
            CheckStepKind.TypeCheck => "Type Check",
            CheckStepKind.Build => "Build",
            CheckStepKind.PageDrawn => "Page Drawn",
            CheckStepKind.VrOffered => "VR Offered",
            // A step with no words of its own is unknown, never another step's name (the lead's rule, 2026-11-12).
            _ => "Step Unknown",
        };

        /// <summary>
        /// The result of <paramref name="project"/>'s task, from the project's own screen
        /// (<see cref="WorkspacePresentationState.ScreenOf"/>: its last check, whether Try Build may stand) and
        /// <paramref name="observed"/>, the last turn of the task's run the computer observed; null with no project. Play in
        /// Headset stands on a paired computer where the app can start its preview relay (<paramref name="relays"/>); where
        /// the build carries none (<paramref name="relayBuilt"/> false, a release build), it says so.
        /// </summary>
        public static RoomResult? Of(ProjectView? project, ClientProjection? state, ObservedTurn? observed,
            RoomResultPage page = RoomResultPage.Result, int part = 0, (ControlPlaneTarget? Target, bool Demonstration)? way = null, WorkstreamView? task = null,
            HistoryColumn? history = null, ShareColumn? share = null, bool canSend = false, bool relays = false, bool relayBuilt = true,
            FileChangeColumn? file = null)
        {
            if (project == null || state == null) return null;
            var screen = WorkspacePresentationState.ScreenOf(project, state, barOpen: true);
            // The computer's check speaks only for the files it checked: once the folder changed after it, it says nothing of
            // them, so neither its line nor its steps stand (AUDIT2, 2026-11-11).
            var check = screen.LastCheck is { Kind: KitMomentKind.HostCheck } host && project.Check?.Files == ProjectViewCheckFiles.Current ? host : null;
            var canMakeVr = !screen.IsVr && ProjectPlayFacts.TryVrVersionTarget(project, out _, out _);
            return new RoomResult(screen.IsVr, canMakeVr, screen.CanTry, check?.Passed, observed, check?.Steps, page, part,
                // Play in Headset by how this headset reaches the computer and whether a demonstration shows (DEVICE's rule); unknown without.
                way is { } reach ? HeadsetPlay.For(project, reach.Target, reach.Demonstration, relays: relays, relayBuilt: relayBuilt) : (Halcyonic.Client.PlayInHeadset?)null, task != null ? RoomAdd.Of(task.Copy, working: false) : null, (task?.Copy as OwnCopy)?.ToAddReview,
                history, share,
                // DEVICE's Share stands only where this headset can send to the computer (never in the demonstration; AUDIT reread
                // MUST 1), and where the host would admit it (TASKS's conditions) or a share stands to stop.
                canShare: canSend && (way?.Demonstration != true) && (project.Share != null || ShareColumn.Reason(project) == null),
                places: way is { } at ? ProjectPlaces.Of(project, at.Target, at.Demonstration, relays: relays, relayBuilt: relayBuilt) : Array.Empty<ProjectPlace>(), file: file,
                readsFiles: way is { Target: not null, Demonstration: false });
        }
    }

    /// <summary>How the computer saw a file change.</summary>
    public enum RoomChangeKind
    {
        Made,
        Changed,
        Removed,
    }

    /// <summary>A file the computer saw change, its facts apart: kind, path (outside text) and lines before and after a change.</summary>
    public readonly struct RoomChange
    {
        public RoomChange(RoomChangeKind kind, string path, long? linesBefore, long? linesAfter)
        {
            Kind = kind;
            Path = path;
            LinesBefore = linesBefore;
            LinesAfter = linesAfter;
        }

        public RoomChangeKind Kind { get; }

        /// <summary>The kind in the product's words: Made, Changed, Removed.</summary>
        public string KindWords => Kind == RoomChangeKind.Made ? RoomResult.Made : Kind == RoomChangeKind.Removed ? RoomResult.Removed : RoomResult.Changed;

        /// <summary>The path as the folder names it: outside text, shown as their name.</summary>
        public string Path { get; }

        public long? LinesBefore { get; }

        public long? LinesAfter { get; }

        /// <summary>A change's note, "12 to 30 Lines", where both counts are known; null otherwise.</summary>
        public string? Note => LinesBefore is long before && LinesAfter is long after ? before + " to " + after + " Lines" : null;
    }

    /// <summary>How a step of the computer's check ended.</summary>
    public enum RoomStepTone
    {
        Passed,
        Failed,
        /// <summary>The check never reached it: nothing is said of it.</summary>
        NotRun,
    }

    /// <summary>A step of the computer's check, apart: which step, its name in the product's words, and how it ended.</summary>
    public readonly struct RoomStep
    {
        public RoomStep(CheckStepKind step, string name, RoomStepTone tone)
        {
            Step = step;
            Name = name;
            Tone = tone;
        }

        public CheckStepKind Step { get; }

        public string Name { get; }

        public RoomStepTone Tone { get; }
    }
}
