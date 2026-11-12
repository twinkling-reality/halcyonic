#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Pairing as cards in the card language (the addendum's A17; ADR 0017, amendment of 2026-11-11): looking, one found,
/// several found, none found, the code, paired, refused.
/// </summary>
public sealed class PairingCardsTests
{
    private static readonly PairingChoice Studio = new("Studio Mac", "studio-mac.local:47801");

    private static IEnumerable<RoomCard> EveryState()
    {
        yield return PairingCards.Looking();
        yield return PairingCards.Found(new[] { Studio });
        yield return PairingCards.Found(new[] { Studio, new PairingChoice("Attic", "attic.local:47801") });
        yield return PairingCards.Found(new[] { Studio, new PairingChoice("Studio Mac", "pretender.local:47801", true) });
        yield return PairingCards.Found(new PairingChoice[0]);
        yield return PairingCards.Code("Studio Mac", Studio.Address);
        yield return PairingCards.Code(null, "192.168.1.20:47801");
        yield return PairingCards.Paired("Studio Mac", Studio.Address);
        foreach (var code in Codes) yield return PairingCards.Refused(code, 1);
    }

    private static readonly string?[] Codes =
    {
        "unreachable", "invalid_code", "refused", "pairing_closed", "wrong_code", "too_many_requests", "busy", "timeout", "connection_lost",
        "protocol_error", "not_secure", "server_not_proven",
        "something_new", null,
    };

    [Test]
    public void NoStateJoinsTwoFactsOnADot()
    {
        foreach (var card in EveryState())
            Assert.That(card.AnyDot, Is.False, string.Join(" | ", card.Words()));
    }

    [Test]
    public void EveryTitleIsOneToThreeWordsInTitleCase()
    {
        var titles = EveryState().Select(card => card.Status?.Title).Concat(new[]
        {
            PairingCards.OneFoundTitle, PairingCards.SeveralFoundTitle, PairingCards.SameNameNote,
        }).Where(title => title != null).Cast<string>();
        foreach (var title in titles)
        {
            var words = title.Split(' ');
            Assert.That(words.Length, Is.InRange(1, 3), title);
            Assert.That(words.All(word => char.IsUpper(word[0])), Is.True, title);
        }
    }

    [Test]
    public void LookingIsAStateUnderWay()
    {
        var card = PairingCards.Looking();
        Assert.That(card.Status?.Title, Is.EqualTo("Looking"));
        Assert.That(card.Status?.Tone, Is.EqualTo(RoomTone.Working));
        Assert.That(card.List, Is.Empty);
    }

    [Test]
    public void OneFoundComputerIsANeutralRowWithTheComputersMarkAndItsName()
    {
        var card = PairingCards.Found(new[] { Studio });
        Assert.That(card.Title?.Text, Is.EqualTo("Your Computer"));
        var row = card.List.Single();
        Assert.That(row.Kind, Is.EqualTo(RoomKind.Computer));
        Assert.That(row.Name.Text, Is.EqualTo("Studio Mac"));
        Assert.That(row.Name.Runs.All(run => run.Theirs), Is.True, "the name came from the network: it leans");
        Assert.That(row.Note, Is.Null, "a found row says nothing more (FIELD, 2026-11-11)");
        Assert.That(row.Tone, Is.EqualTo(RoomTone.Quiet), "the one accent stays on the main action");
        Assert.That(card.Status, Is.Null);
    }

    [Test]
    public void SeveralFoundAreRowsInTheFindersOrderAPageAtMost()
    {
        var found = Enumerable.Range(1, 7).Select(n => new PairingChoice("Mac " + n, "mac-" + n + ".local:47801")).ToList();
        var card = PairingCards.Found(found);
        Assert.That(card.Title?.Text, Is.EqualTo("Pick Your Computer"));
        Assert.That(card.List.Select(row => row.Name.Text), Is.EqualTo(found.Take(RoomCard.ListAPage).Select(choice => choice.Name)));
        Assert.That(card.More, Is.Null);
    }

    [Test]
    public void ANameThatCameTwiceIsMarkedOnItsRowsAndSaidUnderTheList()
    {
        var card = PairingCards.Found(new[]
        {
            new PairingChoice("Studio Mac", "pretender.local:47801", true), new PairingChoice("Studio Mac", "studio-mac.local:47801", true),
        });
        Assert.That(card.List.Select(row => row.Note), Is.EqualTo(new[] { "Same Name Twice", "Same Name Twice" }));
        Assert.That(card.List.All(row => row.Tone == RoomTone.Bad), Is.True);
        Assert.That(card.More?.Text, Is.EqualTo(PairingCards.SameNameMore));
        Assert.That(card.More!.Runs.Any(run => run.Theirs), Is.False, "Halcyonic's own sentence stands upright");
    }

    [Test]
    public void NoneFoundSaysTheWayOnTypingTheAddress()
    {
        var card = PairingCards.Found(new PairingChoice[0]);
        Assert.That(card.Status?.Title, Is.EqualTo("None Found"));
        Assert.That(card.Status?.Line, Does.Contain("type its address"));
        Assert.That(card.Status?.Tone, Is.EqualTo(RoomTone.Quiet), "finding nothing is no failure");
    }

    [Test]
    public void TheCodeCardIsTitledByTheComputerAndShowsNoHostOrPort()
    {
        var found = PairingCards.Code("Studio Mac", Studio.Address);
        Assert.That(found.Title?.Text, Is.EqualTo("Studio Mac"));
        Assert.That(found.Title!.Runs.All(run => run.Theirs), Is.True);
        Assert.That(found.Description?.Text, Is.EqualTo(PairingCards.CodeLine));
        Assert.That(found.Slots, Is.Empty, "host and port stay off screen; the code's places are the field's");
        Assert.That(found.Words(), Has.None.Contains("studio-mac.local"));

        var typed = PairingCards.Code(null, "192.168.1.20:47801");
        Assert.That(typed.Title?.Text, Is.EqualTo("192.168.1.20:47801"), "typed, the address is all there is to name it by");
    }

    [Test]
    public void PairedIsAGoodStatusOverTheComputer()
    {
        var card = PairingCards.Paired("Studio Mac", Studio.Address);
        Assert.That(card.Status?.Title, Is.EqualTo("Paired"));
        Assert.That(card.Status?.Tone, Is.EqualTo(RoomTone.Good));
        Assert.That(card.Title?.Text, Is.EqualTo("Studio Mac"));
        Assert.That(card.Slots, Is.Empty);
        Assert.That(PairingCards.Paired(null, "192.168.1.20:47801").Title?.Text, Is.EqualTo("192.168.1.20:47801"));
    }

    [Test]
    public void EachRefusalHasItsOwnWordsAndAnUnknownOneSaysItIsUnknown()
    {
        var titles = Codes.Take(Codes.Length - 2).Select(code => PairingCards.Refused(code).Status!.Value.Title).ToList();
        Assert.That(titles.Distinct().Count(), Is.EqualTo(titles.Count), string.Join(", ", titles));
        // AUDIT's census (2026-11-12): a code with no words of its own never borrows another case's.
        foreach (var unknown in new[] { "something_new", "error", null })
        {
            Assert.That(PairingCards.Refused(unknown).Status?.Title, Is.EqualTo("Reason Unknown"), unknown ?? "no code");
            Assert.That(PairingCards.Refused(unknown).Status?.Line, Is.Null, unknown ?? "no code");
        }
        // Nothing was refused where nothing answered, or where the code was never sent.
        Assert.That(PairingCards.Refused("unreachable").Status?.Title, Is.EqualTo("Computer Not Found"));
        Assert.That(PairingCards.Refused("invalid_code").Status?.Title, Is.EqualTo("Code Incomplete"));
        var lines = Codes.Select(code => PairingCards.Refused(code).Status?.Line ?? "").Where(line => line.Length > 0).ToList();
        Assert.That(lines, Has.None.EndsWith("."), "a status line ends with no stop");
        Assert.That(lines, Has.None.Contains("·"), "nothing joined on a dot");
        Assert.That(PairingClient.WhyRefused("unreachable"), Is.EqualTo("Computer Not Found. Nothing answered at that address; check it, and that both are on one network."));
        Assert.That(Codes.All(code => PairingCards.Refused(code).Status?.Tone == RoomTone.Bad), Is.True);
    }

    [Test]
    public void AWrongCodeShowsTheTriesAsPipsTheUsedOnesFilled()
    {
        Assert.That((PairingCards.Refused("wrong_code", 2).TrackAt, PairingCards.Refused("wrong_code", 2).TrackOf), Is.EqualTo((1, 3)));
        Assert.That(PairingCards.Refused("wrong_code", 1).TrackAt, Is.EqualTo(2));
        Assert.That(PairingCards.Refused("wrong_code", 0).TrackAt, Is.EqualTo(3), "the last try spent: the window closed");
        Assert.That(PairingCards.Refused("wrong_code").TrackOf, Is.Null, "the computer said nothing of the tries left");
        Assert.That(PairingCards.Refused("wrong_code", 7).TrackOf, Is.Null, "more left than a window holds is not believed");
        Assert.That(PairingCards.Refused("busy", 2).TrackOf, Is.Null, "only a wrong code spends a try");
    }

    [Test]
    public void ARefusalIsSaidByItsCodeNeverByWhatAnswered()
    {
        var card = PairingCards.Refused("wrong_code", 2);
        Assert.That(card.Words(), Has.None.Contains("·"));
        Assert.That(PairingCards.Why("wrong_code"), Is.EqualTo(("Code Refused", "Check the code on your computer and type it again.")));
    }
}
