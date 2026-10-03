using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class SpaceSettingsTests
{
    private static readonly RoomStatus Room = new(RoomSpace.Room, PassthroughState.Running, RoomScan.Read, StagePlacement.InFront, null);

    private static (IReadOnlyList<MenuSetting> Rows, List<string> Raised) Of(Func<SpaceNow> now)
    {
        var raised = new List<string>();
        return (SpaceSettings.Of(now, raised.Add), raised);
    }

    private static SettingNow Read(IReadOnlyList<MenuSetting> rows, string key) => rows.Single(row => row.Key == key).Read();

    [Test]
    public void YourSpaceHoldsTheRoomTheCharactersAndTheMenuAndYourComputerOnlyWherePairingIs()
    {
        var (rows, _) = Of(() => new SpaceNow(Room, RoomOffer.None, StageArrangement.InFront, null));
        Assert.That(rows.Select(row => (row.Group, row.Name)), Is.EqualTo(new[]
        {
            ("Your space", "Around you"), ("Your space", "Your room's layout"), ("Your space", "The characters"), ("Your space", "The menu"),
        }));
        var (developing, _) = Of(() => new SpaceNow(Room, RoomOffer.None, StageArrangement.InFront, new PairingNow(null, PairingStep.Idle, true)));
        Assert.That(developing.Last().Group, Is.EqualTo("Your computer"));
        Assert.That(developing.Last().Name, Is.EqualTo("Pairing"));
    }

    [Test]
    public void EachChangeOnlyRaisesItsOwnId()
    {
        var (rows, raised) = Of(() => new SpaceNow(Room, RoomOffer.SetUpRoom, StageArrangement.InFront, new PairingNow(null, PairingStep.Idle, true)));
        foreach (var row in rows) row.Change();
        Assert.That(raised, Is.EqualTo(new[] { SpaceSettings.SwitchSpace, SpaceSettings.TakeOffer, SpaceSettings.NextArrangement, SpaceSettings.ResetPosition, SpaceSettings.Pairing }));
    }

    [Test]
    public void AroundYouSwitchesBetweenTheRoomAndAVirtualSpaceAndSaysWhyNotWhereTheRoomCantShow()
    {
        var room = Room;
        var (rows, _) = Of(() => new SpaceNow(room, RoomOffer.None, StageArrangement.InFront, null));
        var now = Read(rows, SpaceSettings.SwitchSpace);
        Assert.That((now.Value, now.Now, now.Prompt, now.Reason), Is.EqualTo(("Your room", room.Line, "Show a virtual space", (string?)null)));
        room = new RoomStatus(RoomSpace.Virtual, PassthroughState.Running, RoomScan.NotRead, StagePlacement.InFront, null);
        Assert.That(Read(rows, SpaceSettings.SwitchSpace).Prompt, Is.EqualTo("Show my room"), "read afresh each time");
        room = new RoomStatus(RoomSpace.Room, PassthroughState.Unavailable, RoomScan.NotRead, StagePlacement.InFront, null);
        Assert.That(Read(rows, SpaceSettings.SwitchSpace).Reason, Is.EqualTo("This headset can't show your room now."));
    }

    [Test]
    public void TheLayoutRowOffersWhatTheRoomOffersAndOtherwiseSaysItNeedsNothing()
    {
        var offer = RoomOffer.AllowRoomAccess;
        var (rows, _) = Of(() => new SpaceNow(Room, offer, StageArrangement.InFront, null));
        Assert.That((Read(rows, SpaceSettings.TakeOffer).Prompt, Read(rows, SpaceSettings.TakeOffer).Reason), Is.EqualTo(("Allow room access", (string?)null)));
        offer = RoomOffer.SetUpRoom;
        Assert.That(Read(rows, SpaceSettings.TakeOffer).Prompt, Is.EqualTo("Set up this room"));
        offer = RoomOffer.None;
        Assert.That(Read(rows, SpaceSettings.TakeOffer).Reason, Is.EqualTo("Your room needs nothing set up now."));
    }

    [Test]
    public void TheCharactersStepThroughTheThreeArrangementsInTurnAndNotOnADesk()
    {
        StageArrangement? standing = StageArrangement.InFront;
        var (rows, _) = Of(() => new SpaceNow(Room, RoomOffer.None, standing, null));
        var seen = new List<(string, string)>();
        foreach (var arrangement in new[] { StageArrangement.InFront, StageArrangement.TurnedAside, StageArrangement.BesideAWindow })
        {
            standing = arrangement;
            var now = Read(rows, SpaceSettings.NextArrangement);
            seen.Add((now.Value, now.Prompt));
            Assert.That(now.Now, Is.EqualTo(SettingsText.Arrangement(arrangement)));
            Assert.That(now.Reason, Is.Null);
        }
        Assert.That(seen, Is.EqualTo(new[]
        {
            ("In front of you", "Make room for a window"), ("Room for a window", "Either side of a window"), ("Either side of a window", "Characters in front"),
        }));
        Assert.That(SpaceSettings.Next(StageArrangement.BesideAWindow), Is.EqualTo(StageArrangement.InFront));
        standing = null;
        Assert.That(Read(rows, SpaceSettings.NextArrangement).Reason, Is.EqualTo(SettingsText.ArrangedByRoom), "on a desk the room places them");
    }

    [Test]
    public void PairingOffersToPairThenToForgetAskingTwiceAndWaitsWhileUnderWay()
    {
        var pairing = new PairingNow(null, PairingStep.Idle, true);
        var (rows, _) = Of(() => new SpaceNow(Room, RoomOffer.None, StageArrangement.InFront, pairing));
        var now = Read(rows, SpaceSettings.Pairing);
        Assert.That((now.Value, now.Prompt, now.Reason), Is.EqualTo(("Not paired", "Pair with a computer", (string?)null)));

        pairing = new PairingNow(null, PairingStep.Idle, false);
        Assert.That(Read(rows, SpaceSettings.Pairing).Reason, Is.EqualTo("Pairing needs the headset's system keyboard; pair from the headset."));

        pairing = new PairingNow(null, PairingStep.Pairing, true);
        Assert.That((Read(rows, SpaceSettings.Pairing).Value, Read(rows, SpaceSettings.Pairing).Reason), Is.EqualTo(("Pairing…", (string?)"Pairing is under way.")));

        pairing = new PairingNow("192.168.1.23:47801", PairingStep.Idle, true);
        now = Read(rows, SpaceSettings.Pairing);
        Assert.That((now.Value, now.Now, now.Prompt, now.Reason), Is.EqualTo(("Paired", "Paired with your computer at 192.168.1.23:47801", "Forget this computer", (string?)null)));
        Assert.That((now.Confirm, now.ValueIsData), Is.EqualTo(("Yes, forget this computer", true)), "Settings asks first, and the address shows as data");

        pairing = new PairingNow("192.168.1.23:47801", PairingStep.Forgetting, true);
        Assert.That(Read(rows, SpaceSettings.Pairing).Reason, Is.EqualTo("Forgetting is under way."));
    }

    [Test]
    public void SettingsTakesNoChangeARowSaysCantBeMadeNow()
    {
        var host = new FakeMenuHost();
        var (rows, raised) = Of(() => new SpaceNow(Room, RoomOffer.None, null, null));
        var settings = new SettingsColumn(host, rows);
        settings.Act(SettingsColumn.OpenSetting, SpaceSettings.NextArrangement);
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.False);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(raised, Is.Empty);
        settings.Act(SettingsColumn.OpenSetting, SpaceSettings.ResetPosition);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(raised, Is.EqualTo(new[] { SpaceSettings.ResetPosition }));
    }

    [Test]
    public void ForgettingIsArmedBySettingsAndRunsOnlyOnItsYes()
    {
        var host = new FakeMenuHost();
        var (rows, raised) = Of(() => new SpaceNow(Room, RoomOffer.None, StageArrangement.InFront, new PairingNow("192.168.1.23:47801", PairingStep.Idle, true)));
        var settings = new SettingsColumn(host, rows);
        settings.Act(SettingsColumn.OpenSetting, SpaceSettings.Pairing);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(raised, Is.Empty, "the first press only asks");
        Assert.That(settings.Frame!.Footer.Confirming, Is.True);
        var yes = settings.Frame!.Footer[PromptSlot.Free]!;
        Assert.That(yes.Words, Is.EqualTo("Yes, forget this computer"));
        settings.Act(yes.Id, null);
        Assert.That(raised, Is.EqualTo(new[] { SpaceSettings.Pairing }));
    }

    [Test]
    public void EveryWordNamesTheComputerAndNoEmDashOrMac()
    {
        foreach (var pairing in new[]
        {
            new PairingNow(null, PairingStep.Idle, true), new PairingNow(null, PairingStep.Idle, false), new PairingNow(null, PairingStep.Typing, true),
            new PairingNow("host:1", PairingStep.Idle, true), new PairingNow("host:1", PairingStep.Idle, true), new PairingNow("host:1", PairingStep.Forgetting, true),
        })
        {
            foreach (var offer in new[] { RoomOffer.None, RoomOffer.AllowRoomAccess, RoomOffer.SetUpRoom })
            {
                foreach (var arrangement in new StageArrangement?[] { null, StageArrangement.InFront, StageArrangement.TurnedAside, StageArrangement.BesideAWindow })
                {
                    var (rows, _) = Of(() => new SpaceNow(Room, offer, arrangement, pairing));
                    foreach (var row in rows)
                    {
                        var now = row.Read();
                        foreach (var words in new[] { row.Group, row.Name, now.Value, now.Now, now.Next, now.Does, now.Prompt, now.Reason ?? "" })
                        {
                            Assert.That(words, Does.Not.Contain("—").And.Not.Contain("Mac"), row.Key);
                        }
                    }
                }
            }
        }
    }
}
