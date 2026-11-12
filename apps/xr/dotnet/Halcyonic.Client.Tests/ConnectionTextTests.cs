using System.Linq;
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
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Synchronizing)), Is.EqualTo("Catching Up"));
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Connecting)), Is.EqualTo("Connecting to Computer"));
    }

    /// <summary>
    /// Why your computer turned the connection away is said by its code, never the control plane's
    /// message; and nothing a person reads names the control plane, a protocol or a client.
    /// </summary>
    [TestCase("unsupported_protocol", "Different Versions, install the same version on the headset and the computer")]
    [TestCase("device_revoked", ConnectionText.PairingRefused)]
    [TestCase("too_many_connections", "Too Many Connections, restart the app to connect again")]
    [TestCase("invalid_message", "Message Unreadable, the headset and the computer may run different versions")]
    [TestCase("hello_required", "Message Unreadable, the headset and the computer may run different versions")]
    [TestCase("a_code_from_later", "Connection Ended, trying again")]
    public void WhyTheConnectionEndedIsSaidByItsCode(string code, string words)
    {
        Assert.That(ConnectionText.Ended(code), Is.EqualTo(words));
        foreach (var line in new[] { ConnectionText.Ended(code), ConnectionText.Closed, ConnectionText.Unreadable, ConnectionText.FellBehind,
            ConnectionText.NoAnswer(TimeSpan.FromSeconds(10)), ConnectionText.Silent(TimeSpan.FromSeconds(1)) })
        {
            Assert.That(line, Does.Not.Contain("control plane").And.Not.Contain("protocol").And.Not.Contain("client"));
        }
        Assert.That(ConnectionText.Silent(TimeSpan.FromSeconds(1)), Is.EqualTo("Computer Silent, nothing for 1 s, so trying again"));
        Assert.That(ConnectionText.ClosedWith("1008 device revoked"), Is.EqualTo(ConnectionText.PairingRefused));
        Assert.That(ConnectionText.ClosedWith("1001 going away"), Is.EqualTo(ConnectionText.Closed));
        Assert.That(ConnectionText.ClosedWith(null), Is.EqualTo(ConnectionText.Closed));
        // Shown alone, as on the demonstration's banner, each says what happens next (the review, 2026-10-07).
        Assert.That(ConnectionText.Closed, Is.EqualTo("Connection Closed, trying again"));
    }

    /// <summary>Every connection notice is a title and its line as one phrase (FIELD, 2026-11-12), never facts joined on a dot.</summary>
    [Test]
    public void NoConnectionWordsAreJoinedOnADot()
    {
        var words = typeof(ConnectionText).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!)
            .Concat(new[] { "unsupported_protocol", "device_revoked", "too_many_connections", "invalid_message", "a_code_from_later" }.Select(ConnectionText.Ended))
            .Concat(new[] { ConnectionText.NoAnswer(TimeSpan.FromSeconds(10)), ConnectionText.Silent(TimeSpan.FromSeconds(3)),
                ConnectionText.UpgradeRefused("host_not_allowed", "x"), ConnectionText.UpgradeRefused("too_many_requests", "x"),
                ConnectionText.Retrying(new ConnectionStatus(ConnectionPhase.WaitingToRetry, ConnectionText.NoAnswer(TimeSpan.FromSeconds(1)))) })
            .ToList();
        Assert.That(words, Has.Count.GreaterThan(15));
        Assert.That(words.Where(each => each.Contains('\u00b7')), Is.Empty);
    }
}
