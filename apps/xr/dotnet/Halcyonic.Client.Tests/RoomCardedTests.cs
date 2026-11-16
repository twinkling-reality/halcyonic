#nullable enable
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A task's page that carries a card: the card stands in place of the page's lines, so nothing pages them.</summary>
public sealed class RoomCardedTests
{
    [Test]
    public void ACardedPageOffersNoNextPageForTheLinesTheCardReplaces()
    {
        var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
        var next = new Prompt(Footer.NextPage, Footer.NextPageWords(0, 2), GlazeIcon.Next, PromptKind.NextPage);
        var card = new RoomCard(status: new RoomCardStatus(CheckedPresenter.NoChecksRecorded, CheckedPresenter.NoChecksRecordedLine, RoomTone.Quiet));
        var frame = new MenuFrame("Kite Run", new Footer(close).WithNext(next), subjectIsData: true, identityKind: MenuIdentityKind.Task).WithCard(card);

        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;

        Assert.That(bar.Card, Is.SameAs(card));
        Assert.That(bar.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { Footer.Close }), "the card shows whole: only Close");
    }

    /// <summary>
    /// The demonstration's Checks page: its card stands in place of the lines, but the line that opens the whole answer beside the
    /// page stays one press away as Read All, the summary's own words left to the card (JUDGE's item 8).
    /// </summary>
    [Test]
    public void ACardedChecksPageStillOpensTheWholeAnswerBesideIt()
    {
        var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
        var card = new RoomCard(status: new RoomCardStatus("Checks Passed", "Simulated at 09:00", RoomTone.Good));
        var lines = new[]
        {
            new PageLine("Checks Passed", action: FileScreens.Open, key: FileScreens.ChecksKey, opens: true),
            new PageLine("Code Check passed."),
            new PageLine("Builds", action: FileScreens.Open, key: FileScreens.HowBuiltKey, opens: true),
        };
        var frame = new MenuFrame("Kite Run", new Footer(close), subjectIsData: true, lines: lines, identityKind: MenuIdentityKind.Task).WithCard(card);

        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;

        Assert.That(bar.Card, Is.SameAs(card), "the card still stands in the lines' place");
        Assert.That(bar.Column.Select(action => (action.Control.Action, action.Control.Key, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (FileScreens.Open, (string?)FileScreens.ChecksKey, FileScreens.ReadAllWords, false), (Footer.Close, (string?)null, "Close", false),
        }), "one Read All, the first answer's, then Close");
    }
}
