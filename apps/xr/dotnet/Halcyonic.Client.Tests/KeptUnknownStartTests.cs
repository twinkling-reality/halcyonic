using System.Collections.Generic;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A start whose outcome is unknown, kept on the device for the journal it was sent to.</summary>
public class KeptUnknownStartTests
{
    private sealed class Preferences : IDevicePreferences
    {
        public Dictionary<string, string> Kept { get; } = new();

        public string? Get(string key) => Kept.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, string? value)
        {
            if (value == null) Kept.Remove(key);
            else Kept[key] = value;
        }
    }

    private static ClientProjection OnJournal(string id, JournalOrigin origin = JournalOrigin.Live)
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, journal: Samples.Journal(id, origin)), new StateChanges());
        return state;
    }

    [Test]
    public void EachJournalKeepsItsOwnWhereverItsComputerIsReachedFrom()
    {
        var preferences = new Preferences();
        ClientProjection? state = OnJournal("journal-1");
        var kept = new KeptUnknownStart(preferences, () => state);
        kept.Id = "01a0dcf1-5a80-7000-8000-0000000000c1";

        state = OnJournal("journal-2");
        Assert.That(kept.Id, Is.Null, "another journal's start never shows");
        kept.Id = "01a0dcf1-5a80-7000-8000-0000000000c2";

        state = OnJournal("journal-1");
        Assert.That(kept.Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c1"), "the same journal, over USB or paired, finds it");
        Assert.That(new KeptUnknownStart(preferences, () => state).Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c1"),
            "and another reader, such as the entry panel, at once");
        kept.Id = null;
        Assert.That(kept.Id, Is.Null);
        state = OnJournal("journal-2");
        Assert.That(kept.Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c2"), "clearing one leaves the other");
    }

    [Test]
    public void NothingIsReadOrKeptWithoutALiveJournal()
    {
        var preferences = new Preferences();
        ClientProjection? state = null;
        var kept = new KeptUnknownStart(preferences, () => state);
        kept.Id = "01a0dcf1-5a80-7000-8000-0000000000c1";
        Assert.That(preferences.Kept, Is.Empty, "before the first snapshot");

        state = OnJournal("demonstration", JournalOrigin.Fixture);
        kept.Id = "01a0dcf1-5a80-7000-8000-0000000000c1";
        Assert.That((kept.Id, preferences.Kept.Count), Is.EqualTo(((string?)null, 0)), "nor in the demonstration");
    }

    [Test]
    public void OneKeptForEveryJournalByAnEarlierBuildGoesToTheFirstJournalReadUnlessItHasItsOwn()
    {
        var preferences = new Preferences();
        preferences.Set(KeptUnknownStart.Preference, "01a0dcf1-5a80-7000-8000-0000000000e1");
        ClientProjection? state = OnJournal("journal-1");
        var kept = new KeptUnknownStart(preferences, () => state);
        Assert.That(kept.Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000e1"));
        Assert.That(preferences.Get(KeptUnknownStart.Preference), Is.Null, "moved, never left for every journal");
        state = OnJournal("journal-2");
        Assert.That(kept.Id, Is.Null);

        var own = new Preferences();
        own.Set(KeptUnknownStart.Preference, "01a0dcf1-5a80-7000-8000-0000000000e1");
        own.Set(KeptUnknownStart.Preference + ".journal-1", "01a0dcf1-5a80-7000-8000-0000000000c1");
        Assert.That(new KeptUnknownStart(own, () => OnJournal("journal-1")).Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c1"),
            "a journal's own is never replaced");
    }
}
