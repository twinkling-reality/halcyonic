#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Pairing beside the menu (ADR 0017, amendment of 2026-11-11): the column's frames, its card under the redesign, and
/// what a press does.
/// </summary>
public sealed class PairingColumnTests
{
    private static readonly PairingChoice Studio = new("Studio Mac", "studio-mac.local:47801");
    private static readonly PairingChoice Attic = new("Attic", "attic.local:47801");

    private static (PairingColumn Column, List<string> Heard) Column()
    {
        var heard = new List<string>();
        var column = new PairingColumn(choice => heard.Add(choice?.Address ?? "type"), () => heard.Add("again"));
        column.Closed += () => heard.Add("closed");
        return (column, heard);
    }

    private static RoomBar Bar(PairingColumn column) => RoomBar.OfFrame(column.Frame!, VoiceStage.Idle)!;

    [Test]
    public void EveryStepIsTheRedesignsAsItsCardWithNothingOnADot()
    {
        var (column, _) = Column();
        var steps = new System.Action[]
        {
            column.Looking, () => column.Found(new[] { Studio, Attic }), () => column.Found(new PairingChoice[0]),
            () => column.Code("Studio Mac", Studio.Address), column.Paired, () => column.Refused("wrong_code", 2),
        };
        foreach (var step in steps)
        {
            step();
            var bar = Bar(column);
            Assert.That(bar, Is.Not.Null, column.Step.ToString());
            Assert.That(bar.Card, Is.Not.Null, column.Step.ToString());
            Assert.That(bar.Card!.AnyDot, Is.False, column.Step.ToString());
            Assert.That(bar.Column.Last().Place, Is.EqualTo(KitPlace.Close), "Close is last: " + column.Step);
        }
    }

    [Test]
    public void EachFoundComputerIsARowPressedAsItsLine()
    {
        var (column, heard) = Column();
        column.Found(new[] { Studio, Attic });
        var frame = column.Frame!;
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        Assert.That(bar.Card!.List.Select(row => row.Name.Text), Is.EqualTo(new[] { "Studio Mac", "Attic" }));
        Assert.That(bar.Card.List.All(row => row.Press != null && ReferenceEquals(row.Press.Frame, frame)), Is.True,
            "a row's press is the drawn frame's own, so the press reaches this column");
        var press = bar.Card.List[1].Press!;
        column.Act(press.Action, press.Key);
        Assert.That(heard, Is.EqualTo(new[] { "attic.local:47801" }));
        Assert.That(bar.Column.Select(action => action.Words), Is.EqualTo(new[] { PairingColumn.TypeAddressWords, "Close" }));
    }

    [Test]
    public void NoneFoundLeadsWithTypingTheAddress()
    {
        var (column, heard) = Column();
        column.Found(new PairingChoice[0]);
        var bar = Bar(column);
        Assert.That(bar.Card!.Status?.Title, Is.EqualTo(PairingCards.NoneFoundTitle));
        Assert.That(bar.Column.First().Main, Is.True);
        Assert.That(bar.Column.First().Words, Is.EqualTo(PairingColumn.TypeAddressWords));
        column.Act(PairingColumn.TypeAddress, null);
        Assert.That(heard, Is.EqualTo(new[] { "type" }));
    }

    [Test]
    public void APressNamesOnlyAnAddressTheSearchOffered()
    {
        var (column, heard) = Column();
        column.Found(new[] { Studio });
        column.Act(PairingColumn.Choose, "elsewhere.local:47801");
        column.Act(PairingColumn.Choose, null);
        column.Act(PairingColumn.TryAgain, null);
        Assert.That(heard, Is.Empty, "nothing the search did not offer, and no Try Again before a refusal");
        column.Code("Studio Mac", Studio.Address);
        column.Act(PairingColumn.Choose, Studio.Address);
        column.Act(PairingColumn.TypeAddress, null);
        Assert.That(heard, Is.Empty, "while the code is asked for, a row or Type Address does nothing");
    }

    [Test]
    public void ARefusalOffersTryAgainFirstThenTypingTheAddress()
    {
        var (column, heard) = Column();
        column.Refused("wrong_code", 1);
        var bar = Bar(column);
        Assert.That(bar.Card!.Status?.Title, Is.EqualTo("Code Refused"));
        Assert.That(bar.Card.TrackOf, Is.EqualTo(3));
        Assert.That(bar.Column.Select(action => (action.Words, action.Main)),
            Is.EqualTo(new[] { (PairingColumn.TryAgainWords, true), (PairingColumn.TypeAddressWords, false), ("Close", false) }));
        column.Act(PairingColumn.TryAgain, null);
        column.Act(Footer.Close, null);
        Assert.That(heard, Is.EqualTo(new[] { "again", "closed" }));
    }

    [Test]
    public void TheCodeAndPairedFramesShowTheNameNeverTheHost()
    {
        var (column, _) = Column();
        column.Code("Studio Mac", Studio.Address);
        Assert.That(Bar(column).Card!.Title?.Text, Is.EqualTo("Studio Mac"));
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Has.None.Contains("studio-mac.local"));
        column.Paired();
        Assert.That(Bar(column).Card!.Status?.Title, Is.EqualTo(PairingCards.PairedTitle));
        Assert.That(Bar(column).Card!.Words(), Has.None.Contains("studio-mac.local"));
    }

    [Test]
    public void AViewWithoutTheRedesignReadsTheSameStepFromTheLines()
    {
        var (column, _) = Column();
        column.Looking();
        Assert.That(column.Frame!.Lines.Single().Words, Is.EqualTo(PairingCards.LookingLine));
        column.Found(new[] { Studio });
        Assert.That(column.Frame!.Lines.Single().Words, Is.EqualTo("Studio Mac"));
        Assert.That(column.Frame!.Lines.Single().Action, Is.EqualTo(PairingColumn.Choose));
    }
}
