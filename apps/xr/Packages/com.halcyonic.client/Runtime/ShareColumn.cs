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
    /// <summary>Where Share stands (ADR 0054; TASKS's brief of 2026-11-11, in A17's card language).</summary>
    public enum ShareStage
    {
        /// <summary>Which ways this computer can share is being read: no choice is offered on what is not known (AUDIT MUST 4).</summary>
        Reading,
        /// <summary>The computer said neither way is ready: its reason, never an empty choice (AUDIT MUST 4).</summary>
        NoWay,
        /// <summary>The project's view does not admit a share: its check, files, confinement or preview stop it.</summary>
        NotShareable,
        /// <summary>The two ways, each offered where this computer can share that way now.</summary>
        Choose,
        /// <summary>The confirmation of one way: Share behind its lock (the command is review_required).</summary>
        Confirm,
        /// <summary>The start was sent and no share stands yet.</summary>
        Starting,
        /// <summary>The project's view holds a live share: the code to scan, and Stop.</summary>
        Shared,
        /// <summary>The start failed; nothing was shared.</summary>
        NotShared,
        /// <summary>A share ended for a reason other than the person's own Stop.</summary>
        Stopped,
    }

    /// <summary>What a press in Share does.</summary>
    public enum ShareActionKind
    {
        /// <summary>Choose a way (a row of the list).</summary>
        Way,
        /// <summary>Start the share chosen: behind its lock.</summary>
        Share,
        Cancel,
        /// <summary>Stop the live share: no confirmation (low consequence).</summary>
        Stop,
        TryAgain,
        ShareAgain,
        /// <summary>The way to fix what keeps the game from being shared: check it (again).</summary>
        CheckGame,
        /// <summary>The way to fix what keeps the game from being shared: start the preview of its latest check.</summary>
        StartPreview,
        Back,
        Close,
    }

    /// <summary>One action Share offers now, with its words, whether it is the main one, and whether it waits behind its lock.</summary>
    public readonly struct ShareAction
    {
        public ShareAction(ShareActionKind kind, string words, bool main = false, bool locked = false, ShareWay? way = null)
        {
            Kind = kind;
            Words = words;
            Main = main;
            Locked = locked;
            Way = way;
        }

        public ShareActionKind Kind { get; }

        public string Words { get; }

        public bool Main { get; }

        public bool Locked { get; }

        /// <summary>The way a <see cref="ShareActionKind.Way"/> chooses.</summary>
        public ShareWay? Way { get; }
    }

    /// <summary>
    /// A live share's code to scan, as cells (a QR code the computer encoded, ADR 0054): <see cref="Size"/> cells a side,
    /// dark where true, with the light margin of <see cref="Margin"/> cells the headset draws round it. Built only from
    /// a code whose every row is the size given and holds only 0 and 1, so a view never draws a malformed code.
    /// </summary>
    public sealed class ShareCells
    {
        /// <summary>The light cells drawn round the code on every side, as a reader needs.</summary>
        public const int Margin = 4;

        private readonly bool[,] dark;

        private ShareCells(int size, bool[,] dark)
        {
            Size = size;
            this.dark = dark;
        }

        /// <summary>Cells a side, without the margin.</summary>
        public int Size { get; }

        /// <summary>Cells a side, with the margin on both sides.</summary>
        public int Drawn => Size + 2 * Margin;

        /// <summary>Whether the cell at <paramref name="row"/>, <paramref name="column"/> (from the top left, without the margin) is dark.</summary>
        public bool Dark(int row, int column) => dark[row, column];

        /// <summary>The cells of <paramref name="code"/>, or null where it is not a square of 0 and 1 of the size it gives (21 to 69).</summary>
        public static ShareCells? Of(ShareCode? code)
        {
            if (code == null || code.Size < 21 || code.Size > 69 || code.Rows == null || code.Rows.Count != code.Size) return null;
            var size = (int)code.Size;
            var cells = new bool[size, size];
            for (var row = 0; row < size; row++)
            {
                var line = code.Rows[row];
                if (line == null || line.Length != size) return null;
                for (var column = 0; column < size; column++)
                {
                    var cell = line[column];
                    if (cell != '0' && cell != '1') return null;
                    cells[row, column] = cell == '1';
                }
            }
            return new ShareCells(size, cells);
        }
    }

    /// <summary>
    /// The two reads Share needs (ADR 0054): which ways work on this computer now (<c>api/sharing</c>) and the live
    /// share's code (<c>api/projects/:id/share/code</c>), through the control plane's own authorized GET of a relative
    /// path, parsed strictly.
    /// </summary>
    public sealed class ShareReader
    {
        private readonly Func<string, CancellationToken, Task<string>> get;

        public ShareReader(Func<string, CancellationToken, Task<string>> get)
        {
            this.get = get ?? throw new ArgumentNullException(nameof(get));
        }

        public const string WaysPath = "api/sharing";

        public static string CodePath(string projectId) =>
            "api/projects/" + Uri.EscapeDataString(string.IsNullOrEmpty(projectId) ? throw new ArgumentException("A project is required.", nameof(projectId)) : projectId) + "/share/code";

        public async Task<SharingWaysResponse> WaysAsync(CancellationToken cancellationToken = default) =>
            HalcyonicJson.Deserialize<SharingWaysResponse>(await get(WaysPath, cancellationToken).ConfigureAwait(false), strict: true);

        public async Task<ShareCodeResponse> CodeAsync(string projectId, CancellationToken cancellationToken = default) =>
            HalcyonicJson.Deserialize<ShareCodeResponse>(await get(CodePath(projectId), cancellationToken).ConfigureAwait(false), strict: true);
    }

    /// <summary>
    /// Share a Link and Share on This Wi-Fi (ADR 0054; TASKS's brief; placeholder words until FIELD's parts note): read
    /// from the project's view (its check, preview and live share), which ways this computer can share now, the start's
    /// outcome and the share's events, as <see cref="RoomCard"/> values with their actions. A share is offered only
    /// where the host would admit it: a current, passed, confined check with its preview running. Starting asks first
    /// (Share behind its lock); stopping asks nothing. A link's address is never text here: the person scans the code
    /// the computer encoded (<see cref="ShareCells"/>). The Wi-Fi address may show as text too. Nothing here reads the
    /// network or sends: the host reads through <see cref="ShareReader"/> and sends <see cref="Start"/> or
    /// <see cref="Stop"/>.
    /// </summary>
    public sealed class ShareColumn
    {
        private ProjectView project;
        private SharingWaysResponse? ways;
        private ShareWay? chosen;
        private bool armed;
        private string? sentCommandId;
        private CommandFailure? failure;
        private ShareStopReason? stopped;
        private ShareCells? cells;
        private string? cellsCheckId;
        private string? armedCheckId;
        private ShareWay? armedWay;
        private bool stopWhenItStands;
        private bool startCompleted;

        public ShareColumn(ProjectView project)
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string ProjectId => project.ProjectId;

        /// <summary>The way chosen or shared; null at the choice.</summary>
        public ShareWay? Way => project.Share?.Way ?? chosen ?? (Ways().Count == 1 ? Ways()[0] : (ShareWay?)null);

        /// <summary>The live share's code, once read for the share standing now; null until then.</summary>
        public ShareCells? Cells => project.Share != null && cellsCheckId == project.Share.CheckId ? cells : null;

        /// <summary>
        /// Which share stands, as this column has seen it: it changes each time a share appears or its address changes. A code
        /// read takes the value at its start and is drawn only for that same share (AUDIT SHOULD 1, until the contract carries
        /// the share's identity).
        /// </summary>
        public int ShareGeneration { get; private set; }

        /// <summary>
        /// Cancel was pressed while the share was starting: the share is stopped the moment it stands (AUDIT MUST 1). The host
        /// sends <see cref="Stop"/> once <see cref="StopNow"/> says so.
        /// </summary>
        public bool StopWhenItStands => stopWhenItStands;

        /// <summary>Whether the host must send <see cref="Stop"/> now: Cancel was pressed while starting, and the share stands.</summary>
        public bool StopNow => stopWhenItStands && project.Share != null;

        /// <summary>Share was pressed once and waits for its second press (its lock).</summary>
        public bool Armed => Stage == ShareStage.Confirm && armed && armedWay == Way && armedCheckId == project.Check?.CheckId;

        public ShareStage Stage
        {
            get
            {
                if (project.Share != null) return ShareStage.Shared;
                if (sentCommandId != null) return ShareStage.Starting;
                if (failure != null) return ShareStage.NotShared;
                if (stopped != null) return ShareStage.Stopped;
                if (Reason(project) != null) return ShareStage.NotShareable;
                if (ways == null) return ShareStage.Reading;
                if (Ways().Count == 0) return ShareStage.NoWay;
                return chosen == null && Ways().Count != 1 ? ShareStage.Choose : ShareStage.Confirm;
            }
        }

        /// <summary>What Share says in the column's sentence where it shows no card (starting), else null.</summary>
        public string? Sentence => null;

        /// <summary>The project's view as the host sent it now: a live share appears, or goes, from here.</summary>
        public void Viewed(ProjectView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.ProjectId != ProjectId) return;
            var had = project.Share;
            project = view;
            if (view.Share != null)
            {
                if (had == null || had.Url != view.Share.Url || had.CheckId != view.Share.CheckId) ShareGeneration++;
                sentCommandId = null;
                startCompleted = false;
                failure = null;
                stopped = null;
            }
            else if (startCompleted)
            {
                // The start completed and no share stands now: it stood and ended between two views. It is not still getting
                // ready (AUDIT MUST 3); the stop's own event, if it comes, gives the reason.
                sentCommandId = null;
                startCompleted = false;
                stopped ??= ShareStopReason.Ended;
            }
            // A refusal, or a new check, takes the lock's first press back (AUDIT MUST 2).
            if (Reason(view) != null) armed = false;
            if (had != null && view.Share == null && stopped == null) chosen = null;
        }

        /// <summary>Which ways this computer can share now, read when the column opens.</summary>
        public void WaysRead(SharingWaysResponse response)
        {
            ways = response ?? throw new ArgumentNullException(nameof(response));
            // A way that no longer stands takes the lock's first press back with it.
            if (Way is ShareWay way && !Offers(way)) armed = false;
        }

        /// <summary>The ways offered now, link first: where only one stands, the choice is skipped and its confirmation shows (FIELD).</summary>
        public IReadOnlyList<ShareWay> Ways() =>
            new[] { ShareWay.Link, ShareWay.Wifi }.Where(Offers).ToList();

        /// <summary>Whether <paramref name="way"/> is offered now: only where the computer said it is ready.</summary>
        public bool Offers(ShareWay way) =>
            ways != null && (way == ShareWay.Link ? ways.Link : ways.Wifi) == ShareWayState.Ready;

        /// <summary>Chooses a way at the choice: false where it is not offered.</summary>
        public bool Choose(ShareWay way)
        {
            if (Stage != ShareStage.Choose && Stage != ShareStage.NotShared && Stage != ShareStage.Stopped) return false;
            if (!Offers(way)) return false;
            failure = null;
            stopped = null;
            chosen = way;
            armed = false;
            return true;
        }

        /// <summary>The first press of Share: it stands behind its lock until pressed again.</summary>
        public bool Arm()
        {
            if (Stage != ShareStage.Confirm) return false;
            armed = true;
            armedWay = Way;
            armedCheckId = project.Check?.CheckId;
            return true;
        }

        /// <summary>The start to send once Share was pressed through its lock; null where it was not armed or is no longer admitted.</summary>
        public ProjectShareStartCommand? Start(CommandFactory commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (!Armed || Way is not ShareWay way || Reason(project) != null) return null;
            var command = commands.StartShare(ProjectId, way);
            sentCommandId = command.CommandId;
            chosen = way;
            armed = false;
            return command;
        }

        /// <summary>The stop of the live share: no confirmation. Null where nothing is shared.</summary>
        public ProjectShareStopCommand? Stop(CommandFactory commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (project.Share == null) return null;
            // Sent once: a stop asked for while it started is now on its way (AUDIT MUST 1).
            stopWhenItStands = false;
            return commands.StopShare(ProjectId);
        }

        /// <summary>The sent start's id, so the host can match its answer; null where none is out.</summary>
        public string? SentCommandId => sentCommandId;

        /// <summary>
        /// Back from the confirmation to the choice; while starting, the share is to be stopped the moment it stands (AUDIT
        /// MUST 1), and the card says so.
        /// </summary>
        public void Cancel()
        {
            if (Stage == ShareStage.Starting) stopWhenItStands = true;
            else chosen = null;
            armed = false;
        }

        /// <summary>Try Again or Share Again: back to the choice, the old outcome forgotten.</summary>
        public void Again()
        {
            failure = null;
            stopped = null;
            chosen = null;
            armed = false;
        }

        /// <summary>The computer's answer to the start sent: refused or failed, it shared nothing.</summary>
        public void Answered(CommandView command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.CommandId != sentCommandId) return;
            if (command.Status == CommandStatus.Failed)
            {
                failure = command.Failure ?? new CommandFailure { Code = "share_failed", Message = "" };
                sentCommandId = null;
            }
            else if (command.Status == CommandStatus.Rejected)
            {
                failure = new CommandFailure { Code = "rejected", Message = "" };
                sentCommandId = null;
            }
            else if (command.Status == CommandStatus.Completed) startCompleted = true;
            if (failure != null) stopWhenItStands = false;
        }

        /// <summary>The share ended (<c>project.share.stopped</c>): the person's own Stop goes back to the choice, any other reason says so.</summary>
        public void ShareStopped(ProjectShareStoppedEvent stoppedEvent)
        {
            if (stoppedEvent == null) throw new ArgumentNullException(nameof(stoppedEvent));
            if (stoppedEvent.ProjectId != ProjectId) return;
            cells = null;
            cellsCheckId = null;
            stopped = stoppedEvent.Payload.Reason;
            // The start is over once its share has ended (AUDIT MUST 3), and nothing is left to stop.
            sentCommandId = null;
            startCompleted = false;
            stopWhenItStands = false;
        }

        /// <summary>The live share's code, read once it stands; a code for another check or way than the share's is dropped.</summary>
        public void CodeRead(ShareCodeResponse response, int generation)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            var share = project.Share;
            // A read that began for an earlier share is never drawn for this one (AUDIT SHOULD 1).
            if (share == null || generation != ShareGeneration || response.CheckId != share.CheckId || response.Way != share.Way) return;
            cells = ShareCells.Of(response.Code);
            cellsCheckId = cells == null ? null : share.CheckId;
        }

        /// <summary>What Share shows now, as a card, never facts joined on a dot (FIELD's parts note of 2026-11-11).</summary>
        public RoomCard? Card()
        {
            var way = Way;
            switch (Stage)
            {
                case ShareStage.NotShareable:
                    return Status(ShareText.NotSharedYet, Reason(project)!, RoomTone.Quiet);
                case ShareStage.Choose:
                    // Only the ways this computer can use are rows; each row is picked from.
                    return new RoomCard(title: LineWords.TheirName(project.Name), description: LineWords.Ours(ShareText.ChooseLine),
                        list: Ways().Select(each => new RoomListRow(Kind(each), LineWords.Ours(each == ShareWay.Link ? ShareText.LinkRow : ShareText.WifiRow),
                            each == ShareWay.Link ? ShareText.LinkNote : ShareText.WifiNote)).ToList());
                case ShareStage.Confirm:
                    return new RoomCard(title: LineWords.TheirName(project.Name), description: LineWords.Ours(way == ShareWay.Wifi ? ShareText.ConfirmWifi : ShareText.ConfirmLink));
                case ShareStage.Reading:
                    return Status(ShareText.Sharing, ShareText.ReadingWays, RoomTone.Working);
                case ShareStage.NoWay:
                    return Status(ShareText.NotSharedYet, NoWayLine(ways!), RoomTone.Quiet);
                case ShareStage.Starting:
                    return Status(ShareText.Sharing, stopWhenItStands ? ShareText.StoppingWhenReady
                        : way == ShareWay.Wifi ? ShareText.StartingWifi : ShareText.StartingLink, RoomTone.Working);
                case ShareStage.Shared:
                    var share = project.Share!;
                    // The code is the card's centre (FIELD's RoomCard.Code, to come; the host draws it under the card till then).
                    // The Wi-Fi address carries no brand and stands on the foot line; a link's never does.
                    var feet = new List<string>();
                    if (share.Files != ProjectShareViewFiles.Current) feet.Add(ShareText.ChangedSince);
                    if (share.Way == ShareWay.Wifi && WifiAddress(share.Url) is string address) feet.Add(address);
                    return new RoomCard(status: new RoomCardStatus(ShareText.Shared, share.Way == ShareWay.Wifi ? ShareText.SharedWifi : ShareText.SharedLink, RoomTone.Working),
                        more: feet.Count == 0 ? null : LineWords.Ours(string.Join(" ", feet)));
                case ShareStage.NotShared:
                    return Status(ShareText.NotShared, FailureLine(failure!, way), RoomTone.Bad);
                default:
                    return Status(ShareText.Stopped, StopLine(stopped!.Value, way), RoomTone.Quiet);
            }
        }

        /// <summary>The actions Share offers now, in the column's order: the main one first, the ways out last.</summary>
        public IReadOnlyList<ShareAction> Actions()
        {
            var actions = new List<ShareAction>();
            switch (Stage)
            {
                case ShareStage.NotShareable:
                    if (Fix(project) is ShareActionKind fix) actions.Add(new ShareAction(fix, fix == ShareActionKind.CheckGame ? ShareText.CheckGame : ShareText.StartPreview, main: true));
                    actions.Add(new ShareAction(ShareActionKind.Back, ShareText.Back));
                    break;
                case ShareStage.Choose:
                    foreach (var each in Ways()) actions.Add(new ShareAction(ShareActionKind.Way, each == ShareWay.Link ? ShareText.LinkRow : ShareText.WifiRow, way: each));
                    actions.Add(new ShareAction(ShareActionKind.Back, ShareText.Back));
                    break;
                case ShareStage.Confirm:
                    actions.Add(new ShareAction(ShareActionKind.Share, ShareText.Share, main: true, locked: !armed, way: Way));
                    actions.Add(new ShareAction(ShareActionKind.Cancel, ShareText.Cancel));
                    break;
                case ShareStage.Starting:
                    // Cancel once: then the share is stopped the moment it stands, and nothing more is offered.
                    if (!stopWhenItStands) actions.Add(new ShareAction(ShareActionKind.Cancel, ShareText.Cancel));
                    break;
                case ShareStage.Reading:
                case ShareStage.NoWay:
                    actions.Add(new ShareAction(ShareActionKind.Back, ShareText.Back));
                    break;
                case ShareStage.Shared:
                    actions.Add(new ShareAction(ShareActionKind.Stop, ShareText.Stop, main: true));
                    actions.Add(new ShareAction(ShareActionKind.Close, ShareText.Close));
                    break;
                case ShareStage.NotShared:
                    actions.Add(new ShareAction(ShareActionKind.TryAgain, ShareText.TryAgain, main: true));
                    actions.Add(new ShareAction(ShareActionKind.Back, ShareText.Back));
                    break;
                case ShareStage.Stopped:
                    actions.Add(new ShareAction(ShareActionKind.ShareAgain, ShareText.ShareAgain, main: true));
                    actions.Add(new ShareAction(ShareActionKind.Close, ShareText.Close));
                    break;
            }
            return actions;
        }

        /// <summary>The way to fix what keeps the game from being shared, where one exists: check it (again), or start its preview.</summary>
        public static ShareActionKind? Fix(ProjectView view)
        {
            var reason = Reason(view);
            if (reason == ShareText.CheckFirst || reason == ShareText.CheckAgain) return ShareActionKind.CheckGame;
            if (reason == ShareText.PreviewFirst) return ShareActionKind.StartPreview;
            return null;
        }

        /// <summary>A way's mark. FIELD's RoomKind.Link and RoomKind.Wifi come in its next kit commit; until then the nearest marks (TODO).</summary>
        private static RoomKind Kind(ShareWay way) => way == ShareWay.Link ? RoomKind.App : RoomKind.Computer;

        /// <summary>
        /// Why the host would refuse to share <paramref name="view"/> now, in the product's words, or null where it would
        /// admit it: the host's own conditions, read from the view as Play on Laptop's are (TASKS's brief, section 2).
        /// </summary>
        public static string? Reason(ProjectView view)
        {
            var check = view.Check;
            if (view.Share != null) return null;
            if (check == null || !check.Findings.Passed) return ShareText.CheckFirst;
            if (check.Files != ProjectViewCheckFiles.Current) return ShareText.CheckAgain;
            if (!check.Findings.ReadsConfined) return ShareText.NotConfined;
            if (ProjectPlayFacts.PreviewOfLatestCheck(view) == null) return ShareText.PreviewFirst;
            return null;
        }

        /// <summary>The Wi-Fi address as text ("192.168.1.20:47970"), or null where it is not a plain http address with a port.</summary>
        public static string? WifiAddress(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.IsDefaultPort) return null;
            if (uri.HostNameType != UriHostNameType.IPv4) return null;
            return uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        }

        private static string FailureLine(CommandFailure failure, ShareWay? way) => failure.Code switch
        {
            "unavailable" => way == ShareWay.Link ? ShareText.NoLinkProgram : ShareText.CannotShare,
            "no_network" => ShareText.NoNetwork,
            "rejected" => ShareText.Refused,
            _ => way == ShareWay.Wifi ? ShareText.NoWifiAnswer : ShareText.NoLinkAnswer,
        };

        /// <summary>Why neither way stands, in the product's words, from what the computer said of each.</summary>
        private static string NoWayLine(SharingWaysResponse ways)
        {
            if (ways.Link == ShareWayState.Unknown && ways.Wifi == ShareWayState.Unknown) return ShareText.CannotTell;
            var lines = new List<string>();
            if (ways.Link == ShareWayState.NotSetUp) lines.Add(ShareText.NoLinkProgram);
            if (ways.Wifi == ShareWayState.NoNetwork) lines.Add(ShareText.NoNetwork);
            return lines.Count == 0 ? ShareText.CannotShare : string.Join(" ", lines);
        }

        private static string StopLine(ShareStopReason reason, ShareWay? way) => reason switch
        {
            ShareStopReason.Asked => ShareText.YouStopped,
            ShareStopReason.PreviewStopped => ShareText.StoppedByCheck,
            ShareStopReason.Ended => way == ShareWay.Wifi ? ShareText.WifiEnded : ShareText.LinkEnded,
            _ => ShareText.StoppedByComputer,
        };

        private static RoomCard Status(string title, string line, RoomTone tone) => new RoomCard(status: new RoomCardStatus(title, line, tone));

    }

    /// <summary>Share's words, FIELD's parts note of 2026-11-11 (FROM-FIELD-share-parts.md) and TASKS's host sentences.</summary>
    public static class ShareText
    {
        public const string ChooseLine = "Pick who can open it.";
        public const string LinkRow = "Anyone With the Link";
        public const string WifiRow = "Anyone on This Wi-Fi";
        public const string LinkNote = "Plays in VR";
        public const string WifiNote = "In a Browser";
        public const string ConfirmLink = "Anyone with the link can play the last checked build. Stop ends it at once.";
        public const string ConfirmWifi = "Anyone on this Wi-Fi can play the last checked build in a browser. Stop ends it at once.";
        public const string Sharing = "Sharing";
        public const string StartingLink = "Getting the link ready";
        public const string StartingWifi = "Opening it on the Wi-Fi";
        /// <summary>Placeholder words until FIELD's: Cancel pressed while starting (AUDIT MUST 1).</summary>
        public const string StoppingWhenReady = "Cancelled. It stops as soon as it is ready.";
        /// <summary>Placeholder words until FIELD's: the ways are being read (AUDIT MUST 4).</summary>
        public const string ReadingWays = "Asking your computer how it can share";
        /// <summary>Placeholder words until FIELD's: the computer cannot tell which way works (AUDIT MUST 4).</summary>
        public const string CannotTell = "Your computer cannot tell how it can share right now.";
        public const string Shared = "Shared";
        public const string SharedLink = "Anyone with the link can play the last checked build.";
        public const string SharedWifi = "Anyone on this Wi-Fi can play the last checked build in a browser.";
        public const string ChangedSince = "The game has changed since. They still get the last checked build.";
        public const string NotSharedYet = "Not Shared Yet";
        public const string CheckFirst = "Only a passed check can be shared. Check the game first.";
        public const string CheckAgain = "The game's files may have changed since its last check. Check the game again first.";
        /// <summary>TASKS's sentence without the word FIELD keeps off the screen; placeholder until FIELD words it.</summary>
        public const string NotConfined = "Not every agent run in this project stayed in its folder, so it is not shared.";
        public const string PreviewFirst = "Start the preview of the latest check first.";
        public const string NotShared = "Not Shared";
        public const string NoLinkProgram = "This computer has no program for a shared link.";
        public const string CannotShare = "This computer cannot share a game.";
        public const string NoNetwork = "This computer isn't on a Wi-Fi network.";
        public const string NoLinkAnswer = "The game could not be shared: no link answered. Try again.";
        public const string NoWifiAnswer = "The game could not be shared on the Wi-Fi: its address did not answer. Try again.";
        public const string Refused = "Your computer refused it.";
        public const string Stopped = "Stopped";
        public const string YouStopped = "You stopped it.";
        public const string StoppedByCheck = "A new check or the preview's end stopped it.";
        public const string LinkEnded = "The link stopped working. Share again for a new one.";
        public const string WifiEnded = "The Wi-Fi share ended.";
        public const string StoppedByComputer = "The computer restarted. Nothing stays shared.";
        public const string Share = "Share";
        public const string Cancel = "Cancel";
        public const string Stop = "Stop";
        public const string TryAgain = "Try Again";
        public const string ShareAgain = "Share Again";
        public const string CheckGame = "Check Game";
        public const string StartPreview = "Start Preview";
        public const string Back = "Back";
        public const string Close = "Close";
    }
}
