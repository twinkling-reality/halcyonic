using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class HostTextTests
{
    [Test]
    public void TheHostHasOneWordInEveryForm()
    {
        Assert.That(HostText.Your, Is.EqualTo("your " + HostText.Noun));
        Assert.That(HostText.YourStart, Is.EqualTo("Your " + HostText.Noun));
        Assert.That(HostText.Noun, Is.EqualTo("computer"), "no product name: the competition rules forbid brand names in what judges see");
    }

    [Test]
    public void TheConnectionWordsNameTheHostOnlyThroughHostText()
    {
        var words = new[]
        {
            ConnectionText.AccessRefused,
            ConnectionText.AccessTokenRefused,
            ConnectionText.PairingRefused,
            ConnectionText.Unreachable,
            ConnectionText.WhyNotLive(new ConnectionStatus(ConnectionPhase.Refused)),
            ConnectionText.WhyNotLive(null),
            SettingsText.YourMac,
            UsageLeftPresenter.NotSetUp,
            VoiceText.Unreachable,
            EntryText.NoFolders,
            EntryText.DemoCannotStart,
        }.Concat(System.Enum.GetValues<ConnectionPhase>().Select(phase => ConnectionText.Phase(new ConnectionStatus(phase))));
        foreach (var word in words)
        {
            Assert.That(Regex.IsMatch(word, @"\bMacs?\b"), Is.False, word);
            Assert.That(word, Does.Not.Contain("control plane").IgnoreCase, word);
        }
        Assert.That(ConnectionText.Unreachable, Is.EqualTo("Can't reach your computer; trying again. Check that Halcyonic is running there and this headset can reach it."));
        Assert.That(EntryText.NoFolders, Is.EqualTo("Your computer doesn't allow any folder yet. Allow one on your computer, then press Try again."));
        Assert.That(EntryText.DemoCannotStart, Is.EqualTo("The demo can't start new work. Real work runs on your computer."));
        Assert.That(SettingsText.YourMac, Is.EqualTo("Your computer"));
    }
}
