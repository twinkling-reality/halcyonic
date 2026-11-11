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
            (View(passed: false), "Check the game first"),
            (View(files: "changed"), "Check it again first"),
            (View(files: "unknown"), "Check it again first"),
            (View(confined: false), "Not every agent run was confined"),
            (View(preview: false), "Start its preview first"),
        })
        {
            var column = new ShareColumn(view);
            Assert.That(column.Stage, Is.EqualTo(ShareStage.NotShareable), reason);
            Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo(reason));
            Assert.That(column.Actions().Single().Kind, Is.EqualTo(ShareActionKind.Back));
            NoDots(column);
        }
        Assert.That(new ShareColumn(View()).Stage, Is.EqualTo(ShareStage.Choose));
    }

    [Test]
    public void OnlyAWayTheComputerSaysIsReadyCanBeChosen()
    {
        var column = new ShareColumn(View());
        Assert.That(column.Choose(ShareWay.Link), Is.False, "nothing is offered before the ways are read");
        column.WaysRead(Ways(link: ShareWayState.NotSetUp));
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(card.List.Select(row => (row.Name.Text, row.Note)), Is.EqualTo(new[] { ("Share a Link", "Set Up on Computer"), ("Share on This Wi-Fi", "This Wi-Fi Only") }));
        Assert.That(column.Actions().Where(action => action.Kind == ShareActionKind.Way).Select(action => action.Way), Is.EqualTo(new ShareWay?[] { ShareWay.Wifi }));
        Assert.That(column.Choose(ShareWay.Link), Is.False);
        Assert.That(column.Choose(ShareWay.Wifi), Is.True);
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Confirm));
        NoDots(column);
    }

    [Test]
    public void StartingAsksThroughItsLockAndStoppingAsksNothing()
    {
        var column = new ShareColumn(View());
        column.WaysRead(Ways());
        column.Choose(ShareWay.Link);
        Assert.That(column.Card()!.Description!.Text, Is.EqualTo("Anyone with the code can play it until you stop."));
        var share = column.Actions().First();
        Assert.That((share.Kind, share.Main, share.Locked), Is.EqualTo((ShareActionKind.Share, true, true)));
        Assert.That(column.Start(Samples.Client, Now), Is.Null, "never sent before its lock");
        column.Arm();
        var command = column.Start(Samples.Client, Now)!;
        Assert.That((command.Payload.ProjectId, command.Payload.Way), Is.EqualTo((ProjectId, ShareWay.Link)));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Starting));
        Assert.That(column.Card(), Is.Null);
        Assert.That(column.Sentence, Is.EqualTo("Getting the Link Ready"));
        Assert.That(column.Actions(), Is.Empty);

        column.Viewed(View(share: Share("link", "https://example-name.invalid/")));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Shared));
        var card = column.Card()!;
        Assert.That(card.Status!.Value.Title, Is.EqualTo("Shared"));
        Assert.That(card.Words().Any(words => words.Contains("example-name") || words.Contains("https")), Is.False, "a link's address is never text");
        Assert.That(column.Actions().First().Kind, Is.EqualTo(ShareActionKind.Stop));
        var stop = column.Stop(Samples.Client, Now);
        Assert.That(stop!.Payload.ProjectId, Is.EqualTo(ProjectId));
        NoDots(column);
    }

    [Test]
    public void TheWifiAddressShowsAsTextAndTheCodeDrawsOnlyForTheShareStanding()
    {
        var column = new ShareColumn(View(share: Share("wifi", "http://192.168.1.20:47970/", files: "changed")));
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Shared));
        var card = column.Card()!;
        Assert.That(card.Slots.Select(slot => slot.Value.Text), Is.EqualTo(new[] { "Share on This Wi-Fi", "192.168.1.20:47970" }));
        Assert.That(card.More!.Text, Is.EqualTo("This is the last checked build."));
        NoDots(column);

        Assert.That(column.Cells, Is.Null);
        column.CodeRead(new ShareCodeResponse { CheckId = "another", Way = ShareWay.Wifi, Code = Code(21) });
        Assert.That(column.Cells, Is.Null, "a code of another check is not this share's");
        column.CodeRead(new ShareCodeResponse { CheckId = CheckId, Way = ShareWay.Wifi, Code = Code(25, (r, c) => r == 0 && c == 3) });
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
        var command = column.Start(Samples.Client, Now)!;
        Assert.That(column.Sentence, Is.EqualTo("Sharing on This Wi-Fi"));
        column.Answered(new CommandView { CommandId = "another", Status = CommandStatus.Failed, Failure = new CommandFailure { Code = "no_network", Message = "x" } });
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Starting), "another command's answer changes nothing");
        column.Answered(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Failed, Failure = new CommandFailure { Code = "no_network", Message = "x" } });
        Assert.That(column.Stage, Is.EqualTo(ShareStage.NotShared));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo("Your computer is not on a Wi-Fi network"));
        Assert.That(column.Card()!.Words(), Does.Not.Contain("x"), "the computer's message is not shown as Halcyonic's words");
        Assert.That(column.Actions().First().Kind, Is.EqualTo(ShareActionKind.TryAgain));
        NoDots(column);
        column.Again();
        Assert.That(column.Stage, Is.EqualTo(ShareStage.Choose));

        var shared = new ShareColumn(View(share: Share("link", "https://example-name.invalid/")));
        shared.Viewed(View());
        shared.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Ended } });
        Assert.That(shared.Stage, Is.EqualTo(ShareStage.Stopped));
        Assert.That(shared.Card()!.Status!.Value.Line, Is.EqualTo("The link stopped working"));
        Assert.That(shared.Actions().First().Kind, Is.EqualTo(ShareActionKind.ShareAgain));
        NoDots(shared);

        var asked = new ShareColumn(View(share: Share("link", "https://example-name.invalid/")));
        asked.Viewed(View());
        asked.ShareStopped(new ProjectShareStoppedEvent { ProjectId = ProjectId, Payload = new ProjectShareStoppedPayload { CheckId = CheckId, Reason = ShareStopReason.Asked } });
        Assert.That(asked.Stage, Is.EqualTo(ShareStage.Choose), "the person's own Stop goes back to the choice");
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
}
