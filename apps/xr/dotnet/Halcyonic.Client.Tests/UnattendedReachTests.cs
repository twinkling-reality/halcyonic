#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// AUDIT3's alert (2026-11-12): a run with nobody at it reaches only a scratch plane named on purpose, and with nothing
/// named it refuses in plain words, so no Editor check can fall back to a developer's own computer.
/// </summary>
public sealed class UnattendedReachTests
{
    private string folder = null!;

    [SetUp]
    public void SetUp() => folder = Directory.CreateTempSubdirectory("halcyonic-reach-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(folder, recursive: true);

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("ws://127.0.0.1:47851/realtime", null)]
    [TestCase(null, "token")]
    [TestCase("  ", "token")]
    public void WithNothingNamedItRefusesInPlainWords(string? endpoint, string? tokenFile)
    {
        // A file named is a real one holding a token, so only the missing endpoint or file is refused.
        var file = string.IsNullOrEmpty(tokenFile) ? tokenFile : Path.Combine(folder, tokenFile);
        if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, "scratch-token");
        var refusal = Assert.Throws<UnattendedReachRefusedException>(() => UnattendedReach.Target(endpoint, file))!;
        Assert.That(refusal.Message, Is.EqualTo(UnattendedReach.Refused));
        Assert.That(refusal.Message, Does.Contain("may not reach a real control plane"));
    }

    [Test]
    public void TheScratchPlaneNamedOnPurposeIsTaken()
    {
        var file = Path.Combine(folder, "access-token");
        File.WriteAllText(file, "scratch-token\n");
        var target = UnattendedReach.Target("ws://127.0.0.1:47851/realtime", file);
        Assert.That(target.Endpoint, Is.EqualTo(new Uri("ws://127.0.0.1:47851/realtime")));
        Assert.That(target.Credential, Is.EqualTo("scratch-token"));
        Assert.That(target.Pairing, Is.Null);
    }

    [Test]
    public void ATokenFileWithNoTokenIsRefusedNotPassedOver()
    {
        var empty = Path.Combine(folder, "empty");
        File.WriteAllText(empty, "  \n");
        Assert.Throws<UnattendedReachRefusedException>(() => UnattendedReach.Target("ws://127.0.0.1:47851/realtime", empty));
        Assert.Throws<UnattendedReachRefusedException>(() => UnattendedReach.Target("ws://127.0.0.1:47851/realtime", Path.Combine(folder, "missing")));
    }

    [TestCase("not an address")]
    [TestCase("/tmp/plane")]
    [TestCase("http://127.0.0.1:47851/realtime")]
    public void AnEndpointThatIsNotARealtimeAddressIsRefused(string endpoint)
    {
        // A real token, so only the endpoint can be what is refused.
        var file = Path.Combine(folder, "access-token");
        File.WriteAllText(file, "scratch-token");
        var refusal = Assert.Throws<UnattendedReachRefusedException>(() => UnattendedReach.Target(endpoint, file))!;
        Assert.That(refusal.Message, Is.EqualTo(UnattendedReach.NotAnEndpoint));
    }

    [Test]
    public void ACheckKeepsNewProjectsDraftsInItsOwnStoreForItsLife()
    {
        var store = new MemoryCreationDraftStore();
        Assert.That(store.Load(), Is.Empty);
        var draft = new CreationDraft();
        store.Save(new[] { draft });
        Assert.That(store.Load(), Is.EqualTo(new[] { draft }));
        store.Save(Array.Empty<CreationDraft>());
        Assert.That(store.Load(), Is.Empty);
    }
}
