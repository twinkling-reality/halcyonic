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
}
