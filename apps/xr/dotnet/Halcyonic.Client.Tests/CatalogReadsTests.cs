using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>When the gallery asks the control plane for the catalog, and what it keeps (ADR 0039).</summary>
public class CatalogReadsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-29T15:00:00Z");

    private sealed class Reader
    {
        public int Asked { get; private set; }
        public TaskCompletionSource<IntelligenceRead<CatalogResponse>>? Pending { get; private set; }

        public Task<IntelligenceRead<CatalogResponse>> Read(CancellationToken cancellationToken)
        {
            Asked++;
            Pending = new TaskCompletionSource<IntelligenceRead<CatalogResponse>>();
            return Pending.Task;
        }

        public void Answer(string code = "not_running") =>
            Pending!.SetResult(new IntelligenceRead<CatalogResponse>(
                new UnavailableCatalog { Reason = new ErrorInfo { Code = code, Message = "x" } }, Start, recorded: false));
    }

    [Test]
    public void OpeningAsksAndTheAnswerIsKeptAfterTheGalleryCloses()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        Assert.That(feed.Loading, Is.False);
        feed.Open(Start);
        feed.Open(Start.AddSeconds(1));
        Assert.That(reader.Asked, Is.EqualTo(1), "a read under way is not asked again");
        Assert.That(feed.Loading, Is.True);
        reader.Answer();
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Last?.Response, Is.InstanceOf<UnavailableCatalog>());
        feed.Close();
        Assert.That(feed.Last, Is.Not.Null, "the last answer stays shown");
    }

    [Test]
    public void ItAsksEachTimeThePersonOpensTheirProjectsAndNeverAsTimePasses()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        feed.Open(Start);
        reader.Answer();
        feed.Poll();
        // However long the gallery stays open, nothing more is asked (AUDIT's S2): no timer reads it.
        for (var minute = 1; minute <= 30; minute++) feed.Poll();
        Assert.That(reader.Asked, Is.EqualTo(1));
        feed.Close();
        feed.Open(Start.AddSeconds(5));
        Assert.That(reader.Asked, Is.EqualTo(2), "opening it again asks again, however soon");
        Assert.That(feed.Last, Is.Not.Null, "the last answer stays shown while a new one is read");
    }

    [Test]
    public void OpeningAnAlreadyOpenGalleryAsksNothing()
    {
        // A caller that says the gallery is open on every frame must not turn it into back-to-back reads.
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        feed.Open(Start);
        reader.Answer();
        feed.Poll();
        for (var frame = 1; frame <= 90; frame++) feed.Open(Start.AddSeconds(frame / 90.0));
        Assert.That(reader.Asked, Is.EqualTo(1));
        feed.Refresh(Start.AddSeconds(2));
        Assert.That(reader.Asked, Is.EqualTo(2), "only the person's Refresh asks while it stays open");
    }

    [Test]
    public void RefreshAsksNowAndAFailedReadSaysWhyAndKeepsTheLastAnswer()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        feed.Open(Start);
        reader.Answer();
        feed.Poll();
        feed.Refresh(Start.AddSeconds(2));
        Assert.That(reader.Asked, Is.EqualTo(2));
        reader.Pending!.SetException(new OperationCanceledException());
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Error, Is.EqualTo("Computer Timed Out · Refresh"));
        Assert.That(feed.Last, Is.Not.Null);
    }

    [Test]
    public void AReadThatThrowsAtOnceIsAFailedRead()
    {
        var feed = new CatalogFeed(_ => throw new UnaskedReadException(IntelligenceText.NotConnected));
        feed.Open(Start);
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Error, Is.EqualTo("Not Connected · Refresh Once Connected"));
    }

    [Test]
    public void AnythingShownChangingMovesTheVersion()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        var versions = new List<int> { feed.Version };
        feed.Open(Start);
        versions.Add(feed.Version);
        reader.Answer();
        feed.Poll();
        versions.Add(feed.Version);
        Assert.That(versions, Is.Unique);
        Assert.That(feed.Poll(), Is.False, "nothing new");
    }
}
