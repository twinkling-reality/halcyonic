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
        Back,
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

        public ShareColumn(ProjectView project)
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string ProjectId => project.ProjectId;

        /// <summary>The way chosen or shared; null at the choice.</summary>
        public ShareWay? Way => project.Share?.Way ?? chosen;

        /// <summary>The live share's code, once read for the share standing now; null until then.</summary>
        public ShareCells? Cells => project.Share != null && cellsCheckId == project.Share.CheckId ? cells : null;

        /// <summary>Share was pressed once and waits for its second press (its lock).</summary>
        public bool Armed => Stage == ShareStage.Confirm && armed;

        public ShareStage Stage
        {
            get
            {
                if (project.Share != null) return ShareStage.Shared;
                if (sentCommandId != null) return ShareStage.Starting;
                if (failure != null) return ShareStage.NotShared;
                if (stopped != null) return ShareStage.Stopped;
                if (Reason(project) != null) return ShareStage.NotShareable;
                return chosen == null ? ShareStage.Choose : ShareStage.Confirm;
            }
        }

        /// <summary>What Share says in the column's sentence where it shows no card (starting), else null.</summary>
        public string? Sentence => Stage == ShareStage.Starting ? (chosen == ShareWay.Wifi ? ShareText.StartingWifi : ShareText.StartingLink) : null;

        /// <summary>The project's view as the host sent it now: a live share appears, or goes, from here.</summary>
        public void Viewed(ProjectView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.ProjectId != ProjectId) return;
            var had = project.Share;
            project = view;
            if (view.Share != null)
            {
                sentCommandId = null;
                failure = null;
                stopped = null;
            }
            if (had != null && view.Share == null && stopped == null) chosen = null;
        }

        /// <summary>Which ways this computer can share now, read when the column opens.</summary>
        public void WaysRead(SharingWaysResponse response) => ways = response ?? throw new ArgumentNullException(nameof(response));

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
            return true;
        }

        /// <summary>The start to send once Share was pressed through its lock; null where it was not armed or is no longer admitted.</summary>
        public ProjectShareStartCommand? Start(ClientInfo client, DateTimeOffset at)
        {
            if (!Armed || chosen is not ShareWay way || Reason(project) != null) return null;
            var command = Stamp(new ProjectShareStartCommand { Payload = new ProjectShareStartPayload { ProjectId = ProjectId, Way = way } }, client, at);
            sentCommandId = command.CommandId;
            armed = false;
            return command;
        }

        /// <summary>The stop of the live share: no confirmation. Null where nothing is shared.</summary>
        public ProjectShareStopCommand? Stop(ClientInfo client, DateTimeOffset at) =>
            project.Share == null ? null : Stamp(new ProjectShareStopCommand { Payload = new ProjectShareStopPayload { ProjectId = ProjectId } }, client, at);

        /// <summary>Back from the confirmation to the choice.</summary>
        public void Cancel()
        {
            chosen = null;
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
        }

        /// <summary>The share ended (<c>project.share.stopped</c>): the person's own Stop goes back to the choice, any other reason says so.</summary>
        public void ShareStopped(ProjectShareStoppedEvent stoppedEvent)
        {
            if (stoppedEvent == null) throw new ArgumentNullException(nameof(stoppedEvent));
            if (stoppedEvent.ProjectId != ProjectId) return;
            cells = null;
            cellsCheckId = null;
            if (stoppedEvent.Payload.Reason == ShareStopReason.Asked)
            {
                stopped = null;
                chosen = null;
            }
            else stopped = stoppedEvent.Payload.Reason;
        }

        /// <summary>The live share's code, read once it stands; a code for another check or way than the share's is dropped.</summary>
        public void CodeRead(ShareCodeResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            var share = project.Share;
            if (share == null || response.CheckId != share.CheckId || response.Way != share.Way) return;
            cells = ShareCells.Of(response.Code);
            cellsCheckId = cells == null ? null : share.CheckId;
        }

        /// <summary>What Share shows now, as a card, never facts joined on a dot; null where it shows only its <see cref="Sentence"/>.</summary>
        public RoomCard? Card()
        {
            switch (Stage)
            {
                case ShareStage.NotShareable:
                    return Status(ShareText.NotShareable, Reason(project)!, RoomTone.Quiet);
                case ShareStage.Choose:
                    return new RoomCard(title: LineWords.TheirName(project.Name), description: LineWords.Ours(ShareText.ChooseLine), list: new[]
                    {
                        new RoomListRow(RoomKind.App, LineWords.Ours(ShareText.Link), Offers(ShareWay.Link) ? ShareText.LinkNote : ShareText.LinkNotReady,
                            Offers(ShareWay.Link) ? RoomTone.Quiet : RoomTone.Skipped),
                        new RoomListRow(RoomKind.Computer, LineWords.Ours(ShareText.Wifi), Offers(ShareWay.Wifi) ? ShareText.WifiNote : ShareText.WifiNotReady,
                            Offers(ShareWay.Wifi) ? RoomTone.Quiet : RoomTone.Skipped),
                    });
                case ShareStage.Confirm:
                    return new RoomCard(title: LineWords.TheirName(project.Name),
                        description: LineWords.Ours(chosen == ShareWay.Wifi ? ShareText.ConfirmWifi : ShareText.ConfirmLink));
                case ShareStage.Starting:
                    return null;
                case ShareStage.Shared:
                    var share = project.Share!;
                    var slots = new List<RoomSlot> { new RoomSlot(share.Way == ShareWay.Wifi ? RoomKind.Computer : RoomKind.App, LineWords.Ours(share.Way == ShareWay.Wifi ? ShareText.Wifi : ShareText.Link)) };
                    // The Wi-Fi address carries no brand and may show as text; a link's never does.
                    if (share.Way == ShareWay.Wifi && WifiAddress(share.Url) is string address) slots.Add(new RoomSlot(RoomKind.Computer, LineWords.Ours(address)));
                    return new RoomCard(status: new RoomCardStatus(ShareText.Shared, ShareText.SharedLine, RoomTone.Good), slots: slots,
                        more: share.Files == ProjectShareViewFiles.Current ? null : LineWords.Ours(ShareText.LastChecked));
                case ShareStage.NotShared:
                    return Status(ShareText.NotShared, FailureLine(failure!), RoomTone.Bad);
                default:
                    return Status(ShareText.Stopped, StopLine(stopped!.Value), RoomTone.Quiet);
            }
        }

        /// <summary>The actions Share offers now, in the column's order: the main one first, the ways out last.</summary>
        public IReadOnlyList<ShareAction> Actions()
        {
            var actions = new List<ShareAction>();
            switch (Stage)
            {
                case ShareStage.Choose:
                    if (Offers(ShareWay.Link)) actions.Add(new ShareAction(ShareActionKind.Way, ShareText.Link, way: ShareWay.Link));
                    if (Offers(ShareWay.Wifi)) actions.Add(new ShareAction(ShareActionKind.Way, ShareText.Wifi, way: ShareWay.Wifi));
                    break;
                case ShareStage.Confirm:
                    actions.Add(new ShareAction(ShareActionKind.Share, ShareText.Share, main: true, locked: !armed, way: chosen));
                    actions.Add(new ShareAction(ShareActionKind.Cancel, ShareText.Cancel));
                    return actions;
                case ShareStage.Starting:
                    return actions;
                case ShareStage.Shared:
                    actions.Add(new ShareAction(ShareActionKind.Stop, ShareText.Stop, main: true));
                    break;
                case ShareStage.NotShared:
                    actions.Add(new ShareAction(ShareActionKind.TryAgain, ShareText.TryAgain, main: true));
                    break;
                case ShareStage.Stopped:
                    actions.Add(new ShareAction(ShareActionKind.ShareAgain, ShareText.ShareAgain, main: true));
                    break;
            }
            actions.Add(new ShareAction(ShareActionKind.Back, ShareText.Back));
            return actions;
        }

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

        private static string FailureLine(CommandFailure failure) => failure.Code switch
        {
            "unavailable" => ShareText.NotSetUp,
            "no_network" => ShareText.NoNetwork,
            "rejected" => ShareText.Refused,
            _ => ShareText.NoAnswer,
        };

        private static string StopLine(ShareStopReason reason) => reason switch
        {
            ShareStopReason.PreviewStopped => ShareText.StoppedByCheck,
            ShareStopReason.Ended => ShareText.StoppedWorking,
            _ => ShareText.StoppedByComputer,
        };

        private static RoomCard Status(string title, string line, RoomTone tone) => new RoomCard(status: new RoomCardStatus(title, line, tone));

        private static T Stamp<T>(T command, ClientInfo client, DateTimeOffset at) where T : CommandEnvelope
        {
            command.CommandId = Guid.NewGuid().ToString("D");
            command.IssuedAt = HalcyonicJson.FormatTimestamp(at);
            command.Client = client;
            return command;
        }
    }

    /// <summary>Share's words: placeholders until FIELD's parts note (FIELD/FROM-DEVICE-share-states-2026-11-11T0830Z.md).</summary>
    public static class ShareText
    {
        public const string Link = "Share a Link";
        public const string Wifi = "Share on This Wi-Fi";
        public const string ChooseLine = "Who should be able to open it?";
        public const string LinkNote = "Anyone With the Code";
        public const string WifiNote = "This Wi-Fi Only";
        public const string LinkNotReady = "Set Up on Computer";
        public const string WifiNotReady = "No Wi-Fi";
        public const string ConfirmLink = "Anyone with the code can play it until you stop.";
        public const string ConfirmWifi = "Anyone on this Wi-Fi can play it until you stop.";
        public const string StartingLink = "Getting the Link Ready";
        public const string StartingWifi = "Sharing on This Wi-Fi";
        public const string Shared = "Shared";
        public const string SharedLine = "Scan the code to play";
        public const string LastChecked = "This is the last checked build.";
        public const string NotShareable = "Not Shareable";
        public const string CheckFirst = "Check the game first";
        public const string CheckAgain = "Check it again first";
        public const string NotConfined = "Not every agent run was confined";
        public const string PreviewFirst = "Start its preview first";
        public const string NotShared = "Not Shared";
        public const string NotSetUp = "Set up a link on your computer";
        public const string NoNetwork = "Your computer is not on a Wi-Fi network";
        public const string NoAnswer = "No link answered";
        public const string Refused = "Your computer refused it";
        public const string Stopped = "Sharing Stopped";
        public const string StoppedByCheck = "A new check stopped it";
        public const string StoppedWorking = "The link stopped working";
        public const string StoppedByComputer = "Your computer stopped it";
        public const string Share = "Share";
        public const string Cancel = "Cancel";
        public const string Stop = "Stop";
        public const string TryAgain = "Try Again";
        public const string ShareAgain = "Share Again";
        public const string Back = "Back";
    }
}
