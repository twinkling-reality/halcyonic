using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Share a Link and Share on This Wi-Fi (ADR 0054): offered only on the host's own conditions and only the ways the
/// computer says are ready, started through its lock and stopped without one, a link's address never as text, the
/// Wi-Fi address as text, the code drawn only from well-formed cells, and every state a card with no dot.
/// </summary>
public class ShareColumnTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string CheckId = "01a19000-0000-7000-8000-0000000000c1";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 30, 0, TimeSpan.Zero);

    private static ProjectView View(bool passed = true, bool confined = true, string files = "current", bool preview = true, JObject? share = null)
    {
        var json = new JObject
        {
            ["project_id"] = ProjectId, ["name"] = "Kite Run", ["location"] = null, ["starter"] = "iwsdk-vr",
            ["created_at"] = "2026-11-11T05:00:00.000Z", ["updated_at"] = "2026-11-11T05:00:00.000Z",
            ["check"] = new JObject
            {
                ["check_id"] = CheckId, ["recorded_at"] = "2026-11-11T05:00:00.000Z", ["position"] = 4, ["files"] = files,
                ["findings"] = new JObject
                {
                    ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 }, ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
                    ["page"] = null, ["unknown"] = null, ["passed"] = passed, ["reads_confined"] = confined,
                },
            },
        };
        if (preview) json["preview"] = new JObject { ["check_id"] = CheckId, ["port"] = 47931, ["files"] = "current" };
        if (share != null) json["share"] = share;
        return HalcyonicJson.Deserialize<ProjectView>(json.ToString(), strict: true);
    }

    private static JObject Share(string way, string url, string files = "current") =>
        new() { ["check_id"] = CheckId, ["url"] = url, ["way"] = way, ["files"] = files };

    private static SharingWaysResponse Ways(ShareWayState link = ShareWayState.Ready, ShareWayState wifi = ShareWayState.Ready) => new() { Link = link, Wifi = wifi };

    private static void NoDots(ShareColumn column)
    {
        var card = column.Card();
        if (card == null) Assert.That(column.Sentence, Is.Not.Null.And.Not.Contain("·"));
        else Assert.That(card.AnyDot, Is.False, string.Join(" | ", card.Words()));
    }

    private static ShareCode Code(int size, Func<int, int, bool>? dark = null) => new()
    {
        Size = size,
        Rows = Enumerable.Range(0, size).Select(row => new string(Enumerable.Range(0, size).Select(column => (dark ?? ((r, c) => (r + c) % 2 == 0))(row, column) ? '1' : '0').ToArray())).ToList(),
    };

    [Test]
    public void AShareIsOfferedOnlyOnTheHostsOwnConditions()
    {
        foreach (var (view, reason) in new[]
        {
            (View(passed: false), ShareText.CheckFirst),
            (View(files: "changed"), ShareText.CheckAgain),
            (View(files: "unknown"), ShareText.CheckAgain),
            (View(confined: false), ShareText.NotConfined),
            (View(preview: false), ShareText.PreviewFirst),
        })
        {
            var column = new ShareColumn(view);
            Assert.That(column.Stage, Is.EqualTo(ShareStage.NotShareable), reason);
            Assert.That(column.Card()!.Status!.Value.Title, Is.EqualTo("Not Shared Yet"));
            Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(reason));
            Assert.That(column.Card()!.Words().Any(words => words.Contains("confined", StringComparison.OrdinalIgnoreCase)), Is.False, "never on screen");
            Assert.That(column.Actions().Last().Kind, Is.EqualTo(ShareActionKind.Back));
            NoDots(column);
        }
        Assert.That(new ShareColumn(View(passed: false)).Actions().First().Kind, Is.EqualTo(ShareActionKind.CheckGame));
        Assert.That(new ShareColumn(View(preview: false)).Actions().First().Kind, Is.EqualTo(ShareActionKind.StartPreview));
        Assert.That(new ShareColumn(View(confined: false)).Actions().Single().Kind, Is.EqualTo(ShareActionKind.Back), "no fix exists there");
        Assert.That(new ShareColumn(View()).Stage, Is.EqualTo(ShareStage.Reading), "admitted, the ways not read yet (AUDIT MUST 4)");
    }

    [Test]
    public void OnlyAWayTheComputerSaysIsReadyCanBeChosen()
    {
        var column = new ShareColumn(View());
        Assert.That(column.Choose(ShareWay.Link), Is.False, "nothing is offered before the ways are read");
        column.WaysRead(Ways());
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(card.Description!.Text, Is.EqualTo("Pick who can open it."));
        Assert.That(card.List.Select(row => (row.Name.Text, row.Note)), Is.EqualTo(new[] { ("Anyone With the Link", "Plays in VR"), ("Anyone on This Wi-Fi", "In a Browser") }));
        NoDots(column);

        // A way this computer cannot use is not a row, and with one way left the choice is skipped to its confirmation.
        var one = new ShareColumn(View());
        one.WaysRead(Ways(link: ShareWayState.NotSetUp));
        Assert.That(one.Stage, Is.EqualTo(ShareStage.Confirm));
        Assert.That(one.Way, Is.EqualTo(ShareWay.Wifi));
        Assert.That(one.Choose(ShareWay.Link), Is.False);
        Assert.That(one.Card()!.Description!.Text, Is.EqualTo(ShareText.ConfirmWifi));
        // TODO(FIELD's next kit commit): the rows' marks become RoomKind.Link and RoomKind.Wifi.
    }

    [Test]
    public void StartingAsksThroughItsLockAndStoppingAsksNothing()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways());
        column.Choose(ShareWay.Link);
        Assert.That(column.Card()!.Description!.Text, Is.EqualTo(ShareText.ConfirmLink));
        var share = column.Actions().First();
        Assert.That((share.Kind, share.Main, share.Locked), Is.EqualTo((ShareActionKind.Share, true, true)));
        Assert.That(column.Start(new CommandFactory(Samples.Client)), Is.Null, "never sent before its lock");
        column.Arm();
        var command = column.Start(new CommandFactory(Samples.Client))!;
        Assert.That((command.Payload.ProjectId, command.Payload.Way), Is.EqualTo((ProjectId, ShareWay.Link)));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Starting));
        Assert.That(column.Card()!.Status!.Value.Title, Is.EqualTo("Sharing"));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo("Getting the link ready"));
        Assert.That(column.Actions().Single().Kind, Is.EqualTo(ShareActionKind.Cancel));

        column.Viewed(View(share: Share("link", "https://example-name.invalid/")));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Shared));
        var card = column.Card()!;
        Assert.That((card.Status!.Value.Title, card.Status.Value.Tone), Is.EqualTo(("Shared", RoomTone.Working)));
        Assert.That(card.Status.Value.Line, Is.EqualTo(ShareText.SharedLink));
        Assert.That(card.Words().Any(words => words.Contains("example-name") || words.Contains("https")), Is.False, "a link's address is never text");
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { ShareActionKind.Stop, ShareActionKind.Close }));
        var stop = column.Stop(new CommandFactory(Samples.Client));
        Assert.That(stop!.Payload.ProjectId, Is.EqualTo(ProjectId));
        NoDots(column);
    }

    [Test]
    public void TheWifiAddressShowsAsTextAndTheCodeDrawsOnlyForTheShareStanding()
    {
        var column = new ShareColumn(View(share: Share("wifi", "http://192.168.1.20:47970/", files: "changed")));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Shared));
        var card = column.Card()!;
        Assert.That(card.Status!.Value.Line, Is.EqualTo(ShareText.SharedWifi));
        Assert.That(card.More!.Text, Is.EqualTo(ShareText.ChangedSince + " 192.168.1.20:47970"), "the change since, then the address to type");
        NoDots(column);

        Assert.That(column.Cells, Is.Null);
        column.CodeRead(new ShareCodeResponse { CheckId = "another", Way = ShareWay.Wifi, Code = Code(21) }, column.ShareGeneration);
        Assert.That(column.Cells, Is.Null, "a code of another check is not this share's");
        column.CodeRead(new ShareCodeResponse { CheckId = CheckId, Way = ShareWay.Wifi, Code = Code(25, (r, c) => r == 0 && c == 3) }, column.ShareGeneration);
        Assert.That(column.Cells!.Size, Is.EqualTo(25));
        Assert.That(column.Cells.Drawn, Is.EqualTo(33));
        Assert.That((column.Cells.Dark(0, 3), column.Cells.Dark(0, 2)), Is.EqualTo((true, false)));

        Assert.That(ShareColumn.WifiAddress("https://192.168.1.20:47970/"), Is.Null);
        Assert.That(ShareColumn.WifiAddress("http://name.local:47970/"), Is.Null);
    }

    [Test]
    public void AMalformedCodeIsNeverDrawn()
    {
        Assert.That(ShareCells.Of(null), Is.Null);
        Assert.That(ShareCells.Of(Code(20)), Is.Null);
        Assert.That(ShareCells.Of(Code(70)), Is.Null);
        var ragged = Code(21);
        ragged.Rows[3] = ragged.Rows[3].Substring(1);
        Assert.That(ShareCells.Of(ragged), Is.Null);
        var other = Code(21);
        other.Rows[0] = "2" + other.Rows[0].Substring(1);
        Assert.That(ShareCells.Of(other), Is.Null);
        var short1 = Code(21);
        short1.Rows.RemoveAt(0);
        Assert.That(ShareCells.Of(short1), Is.Null);
        Assert.That(ShareCells.Of(Code(21)), Is.Not.Null);
    }

    [Test]
    public void AFailedStartAndAShareThatEndedSaySoAndOfferAgain()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways());
        column.Choose(ShareWay.Wifi);
        column.Arm();
        var command = column.Start(new CommandFactory(Samples.Client))!;
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo("Opening it on the Wi-Fi"));
        column.Answered(new CommandView { CommandId = "another", Status = CommandStatus.Failed, Failure = new CommandFailure { Code = "no_network", Message = "x" } });
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Starting), "another command's answer changes nothing");
        column.Answered(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Failed, Failure = new CommandFailure { Code = "no_network", Message = "x" } });
        Assert.That(column.Stage, Is.EqualTo(ShareStage.NotShared));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.NoNetwork));
        Assert.That(column.Card()!.Words(), Does.Not.Contain("x"), "the computer's message is not shown as Halcyonic's words");
        Assert.That(column.Actions().First().Kind, Is.EqualTo(ShareActionKind.TryAgain));
        NoDots(column);
        column.Again();
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Choose));

        var shared = new ShareColumn(View(share: Share("link", "https://example-name.invalid/")));
        shared.Viewed(View());
        shared.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Ended } });
        Assert.That(shared.Stage, Is.EqualTo(ShareStage.Stopped));
        Assert.That(shared.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.LinkEnded));
        Assert.That(shared.Actions().Select(action => action.Kind), Is.EqualTo(new[] { ShareActionKind.ShareAgain, ShareActionKind.Close }));
        NoDots(shared);

        var asked = new ShareColumn(View(share: Share("link", "https://example-name.invalid/")));
        asked.Viewed(View());
        asked.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Asked } });
        Assert.That(asked.Stage, Is.EqualTo(ShareStage.Stopped));
        Assert.That(asked.Card()!.Status!.Value.Line, Is.EqualTo("You stopped it."));
    }

    [Test]
    public async Task TheReaderReadsTheWaysAndTheCode()
    {
        var asked = new List<string>();
        var reader = new ShareReader((path, _) =>
        {
            asked.Add(path);
            return Task.FromResult(path == ShareReader.WaysPath
                ? HalcyonicJson.Serialize(Ways(wifi: ShareWayState.NoNetwork))
                : HalcyonicJson.Serialize(new ShareCodeResponse { CheckId = CheckId, Way = ShareWay.Link, Code = Code(21) }));
        });
        Assert.That((await reader.WaysAsync()).Wifi, Is.EqualTo(ShareWayState.NoNetwork));
        Assert.That((await reader.CodeAsync(ProjectId)).Code.Size, Is.EqualTo(21));
        Assert.That(asked, Is.EqualTo(new[] { "api/sharing", $"api/projects/{ProjectId}/share/code" }));
    }

    // AUDIT's probes (AUDIT/evidence/DEVICE-share/ShareAuditProbeTests.cs), each now a check of its fix.

    [Test]
    public void AuditMust4NoChoiceOfNothing()
    {
        var unread = new ShareColumn(View());
        Assert.That(unread.Stage, Is.EqualTo(ShareStage.Reading), "unknown never reads as a choice");
        Assert.That(unread.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.ReadingWays));
        Assert.That(unread.Actions().Single().Kind, Is.EqualTo(ShareActionKind.Back));
        var none = new ShareColumn(View());
        none.WaysRead(Ways(link: ShareWayState.NotSetUp, wifi: ShareWayState.NoNetwork));
        Assert.That(none.Stage, Is.EqualTo(ShareStage.NoWay));
        Assert.That(none.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.NoLinkProgram + " " + ShareText.NoNetwork), "the computer's own reasons");
        var unknown = new ShareColumn(View());
        unknown.WaysRead(Ways(link: ShareWayState.Unknown, wifi: ShareWayState.Unknown));
        Assert.That(unknown.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.CannotTell));
        NoDots(none);
    }

    [Test]
    public void AuditMust1CancelWhileStartingStopsTheShareTheMomentItStands()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways(wifi: ShareWayState.NotSetUp));
        column.Arm();
        Assert.That(column.Start(new CommandFactory(Samples.Client)), Is.Not.Null);
        column.Cancel();
        Assert.That(column.StopWhenItStands, Is.True);
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.StoppingWhenReady), "the card says so");
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { ShareActionKind.Close }), "only a way off the page while it stops");
        Assert.That(column.StopNow, Is.False, "nothing stands yet");
        column.Viewed(View(share: Share("link", "https://example-name.invalid/")));
        Assert.That(column.StopNow, Is.True, "the host stops it now");
        Assert.That(column.Stop(new CommandFactory(Samples.Client)), Is.Not.Null);
        column.StopSent();
        Assert.That(column.StopNow, Is.False, "the stop is sent once");
        column.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Asked } });
        Assert.That(column.StopWhenItStands, Is.False);
    }

    [Test]
    public void AuditMust2ARefusalTakesTheLocksFirstPressBack()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways(wifi: ShareWayState.NotSetUp));
        column.Arm();
        Assert.That(column.Armed, Is.True);
        column.Viewed(View(passed: false));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.NotShareable));
        column.Viewed(View());
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Confirm));
        Assert.That(column.Armed, Is.False, "Share is locked again");
        Assert.That(column.Actions().First().Locked, Is.True);
        Assert.That(column.Start(new CommandFactory(Samples.Client)), Is.Null, "one press shares nothing");
    }

    [Test]
    public void AuditMust3AShareThatEndedBeforeAnyViewIsNotStillStarting()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways(wifi: ShareWayState.NotSetUp));
        column.Arm();
        var start = column.Start(new CommandFactory(Samples.Client))!;
        column.Answered(new CommandView { CommandId = start.CommandId, Status = CommandStatus.Completed });
        column.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Ended } });
        column.Viewed(View());
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Stopped));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.LinkEnded));

        // With no stop event at all: a completed start and a view with no share is over, not still getting ready.
        var silent = new ShareColumn(View());
        silent.WaysRead(Ways(wifi: ShareWayState.NotSetUp));
        silent.Arm();
        var quiet = silent.Start(new CommandFactory(Samples.Client))!;
        silent.Answered(new CommandView { CommandId = quiet.CommandId, Status = CommandStatus.Completed });
        silent.Viewed(View());
        Assert.That(silent.Stage, Is.EqualTo(ShareStage.Stopped));
    }

    [Test]
    public void AuditShould1ACodeReadForAnEarlierShareIsNeverDrawnForTheNewOne()
    {
        var column = new ShareColumn(View(share: Share("link", "https://old-link.example.invalid/")));
        var generation = column.ShareGeneration;
        column.Viewed(View());
        column.Viewed(View(share: Share("link", "https://new-link.example.invalid/")));
        column.CodeRead(new ShareCodeResponse { CheckId = CheckId, Way = ShareWay.Link, Code = Code(21) }, generation);
        Assert.That(column.Cells, Is.Null, "a read begun for the old link");
        column.CodeRead(new ShareCodeResponse { CheckId = CheckId, Way = ShareWay.Link, Code = Code(21) }, column.ShareGeneration);
        Assert.That(column.Cells, Is.Not.Null, "a read begun for the share standing now");
    }

    // AUDIT's reread (AUDIT/DEVICE-share-reread-VERDICT.md), the column's part of each fix.

    [Test]
    public void AuditRereadAColumnWithAStartOrAStopToSendIsKeptAndCancelledHasAWayOff()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways(wifi: ShareWayState.NotSetUp));
        Assert.That(column.KeepAfterLeaving, Is.False, "nothing under way");
        column.Arm();
        column.Start(new CommandFactory(Samples.Client));
        Assert.That(column.KeepAfterLeaving, Is.True, "a start under way is kept when the page is left");
        column.Cancel();
        Assert.That(column.KeepAfterLeaving, Is.True, "and a stop still to send");
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { ShareActionKind.Close }), "a way off the page after Cancelled");
        column.Viewed(View(share: Share("link", "https://example-name.invalid/")));
        Assert.That(column.StopNow, Is.True);
        Assert.That(column.Stop(new CommandFactory(Samples.Client)), Is.Not.Null);
        Assert.That(column.StopNow, Is.True, "the wish stays until the host says the stop went (SHOULD 1)");
        column.StopSent();
        Assert.That(column.StopNow, Is.False);
    }

    [Test]
    public void AuditRereadAFailedWaysReadSaysTheComputerCannotTellWithTryAgain()
    {
        var column = new ShareColumn(View());
        column.WaysFailed();
        Assert.That(column.Stage, Is.EqualTo(ShareStage.NoWay));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(ShareText.CannotTell), "never still asking");
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { ShareActionKind.TryAgain, ShareActionKind.Back }));
        column.Again();
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Reading), "Try Again reads again");
    }
}
