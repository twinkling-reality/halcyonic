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
            ConnectionText.AccessTokenUnproved,
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
            Assert.That(word, Does.Not.Contain("access token").IgnoreCase, "a person reads access code: " + word);
        }
        foreach (var outcome in System.Enum.GetValues<LoopbackProofOutcome>())
        {
            var detail = new TokenNotSentException(outcome, new System.Uri("http://127.0.0.1:47800/")).Message;
            Assert.That(detail, Does.Not.Contain("access token").IgnoreCase, "shown after Can't reach your computer: " + detail);
        }
        Assert.That(ConnectionText.AccessTokenRefused, Is.EqualTo(
            "Access Code Refused · Mismatch · Copy Current Code · Restart App"));
        Assert.That(ConnectionText.AccessTokenUnproved, Is.EqualTo(
            "Code Withheld · Host Unverified · Copy Current Code · Check App · Restart App"));
        Assert.That(ConnectionText.Unreachable, Is.EqualTo("Computer Unreachable · Retrying · Check App and Network"));
        Assert.That(EntryText.NoFolders, Is.EqualTo("No Folders Allowed · Allow on Computer · Try Again"));
        Assert.That(EntryText.DemoCannotStart, Is.EqualTo("Demo · No New Work"));
        Assert.That(SettingsText.YourMac, Is.EqualTo("Your computer"));
        // Our own words never name the product (the review, 2026-10-07).
        var unproved = new TokenNotSentException(LoopbackProofOutcome.Unproved, new System.Uri("http://127.0.0.1:47800/")).Message;
        Assert.That(new[] { VoiceText.NoMicrophone, unproved }, Has.None.Contains("Halcyonic"));
        Assert.That(unproved, Does.EndWith("It may be another program listening while this app is stopped on your computer."));
    }
}
