using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ConnectionTextTests
{
    [Test]
    public void EveryPhaseReadsInPlainWordsNeverItsName()
    {
        foreach (ConnectionPhase phase in Enum.GetValues(typeof(ConnectionPhase)))
        {
            var words = ConnectionText.Phase(new ConnectionStatus(phase, phase == ConnectionPhase.Refused ? ConnectionText.OtherVersion : null));
            Assert.That(words, Is.Not.Empty);
            if (phase != ConnectionPhase.Live) Assert.That(words, Is.Not.EqualTo(phase.ToString()), phase + " shows as its enum name");
            Assert.That(words, Does.Not.Contain("WaitingToRetry").And.Not.Contain("Synchronizing"), "no identifier reads as words");
        }
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Synchronizing)), Is.EqualTo("Catching up with your computer…"));
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Connecting)), Is.EqualTo("Connecting to your computer…"));
    }

    /// <summary>
    /// Why your computer turned the connection away is said by its code, never the control plane's
    /// message; and nothing a person reads names the control plane, a protocol or a client.
    /// </summary>
    [TestCase("unsupported_protocol", "Your computer runs another version of this app. Install the same version on both.")]
    [TestCase("device_revoked", ConnectionText.PairingRefused)]
    [TestCase("too_many_connections", "This headset already has too many connections open to your computer. Close the app, then open it again.")]
    [TestCase("invalid_message", "Your computer couldn't read what this app sent. Install the same version on both.")]
    [TestCase("hello_required", "Your computer couldn't read what this app sent. Install the same version on both.")]
    [TestCase("a_code_from_later", "Your computer ended the connection. The headset tries again by itself.")]
    public void WhyTheConnectionEndedIsSaidByItsCode(string code, string words)
    {
        Assert.That(ConnectionText.Ended(code), Is.EqualTo(words));
        foreach (var line in new[] { ConnectionText.Ended(code), ConnectionText.Closed, ConnectionText.Unreadable, ConnectionText.FellBehind,
            ConnectionText.NoAnswer(TimeSpan.FromSeconds(10)), ConnectionText.Silent(TimeSpan.FromSeconds(1)) })
        {
            Assert.That(line, Does.Not.Contain("control plane").And.Not.Contain("protocol").And.Not.Contain("client"));
        }
        Assert.That(ConnectionText.Silent(TimeSpan.FromSeconds(1)), Is.EqualTo("Your computer sent nothing for 1 second. The headset tries again by itself."));
        Assert.That(ConnectionText.ClosedWith("1008 device revoked"), Is.EqualTo(ConnectionText.PairingRefused));
        Assert.That(ConnectionText.ClosedWith("1001 going away"), Is.EqualTo(ConnectionText.Closed));
        Assert.That(ConnectionText.ClosedWith(null), Is.EqualTo(ConnectionText.Closed));
        // Shown alone, as on the demonstration's banner, each says what happens next (the review, 2026-10-07).
        Assert.That(ConnectionText.Closed, Is.EqualTo("Your computer closed the connection. The headset tries again by itself."));
    }
}
