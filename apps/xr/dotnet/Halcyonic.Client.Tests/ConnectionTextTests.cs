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
            var words = ConnectionText.Phase(new ConnectionStatus(phase, phase == ConnectionPhase.Refused ? "The control plane speaks realtime protocol 2." : null));
            Assert.That(words, Is.Not.Empty);
            if (phase != ConnectionPhase.Live) Assert.That(words, Is.Not.EqualTo(phase.ToString()), phase + " shows as its enum name");
            Assert.That(words, Does.Not.Contain("WaitingToRetry").And.Not.Contain("Synchronizing"), "no identifier reads as words");
        }
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Synchronizing)), Is.EqualTo("Catching up with your computer…"));
        Assert.That(ConnectionText.Phase(new ConnectionStatus(ConnectionPhase.Connecting)), Is.EqualTo("Connecting to your computer…"));
    }
}
