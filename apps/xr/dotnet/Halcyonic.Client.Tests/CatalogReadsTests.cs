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
    public void OpeningAsksOnceAndTheAnswerIsKept()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        Assert.That(feed.Loading, Is.False);
        feed.Open(Start);
        feed.Open(Start.AddSeconds(1));
        Assert.That(reader.Asked, Is.EqualTo(1));
        Assert.That(feed.Loading, Is.True);
        reader.Answer();
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Last?.Response, Is.InstanceOf<UnavailableCatalog>());
        feed.Close();
        feed.Open(Start.AddSeconds(5));
        Assert.That(reader.Asked, Is.EqualTo(1), "reopening shows the last answer until the interval passes");
        Assert.That(feed.Last, Is.Not.Null);
    }

    [Test]
    public void WhileOpenItAsksAgainOnceTheIntervalPassesAndNeverWhileClosed()
    {
        var reader = new Reader();
        var feed = new CatalogFeed(reader.Read);
        feed.Open(Start);
        reader.Answer();
        feed.Poll();
        feed.Tick(Start + CatalogFeed.Interval - TimeSpan.FromSeconds(1));
        Assert.That(reader.Asked, Is.EqualTo(1));
        feed.Tick(Start + CatalogFeed.Interval);
        Assert.That(reader.Asked, Is.EqualTo(2));
        Assert.That(feed.Last, Is.Not.Null, "the last answer stays shown while a new one is read");
        reader.Answer("not_ready");
        feed.Poll();
        feed.Close();
        feed.Tick(Start + CatalogFeed.Interval + CatalogFeed.Interval + CatalogFeed.Interval);
        Assert.That(reader.Asked, Is.EqualTo(2));
        feed.Open(Start + CatalogFeed.Interval + CatalogFeed.Interval + CatalogFeed.Interval);
        Assert.That(reader.Asked, Is.EqualTo(3), "reopened after the interval, it asks");
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
        Assert.That(feed.Error, Is.EqualTo("your computer didn't answer in time. Press Refresh to try again."));
        Assert.That(feed.Last, Is.Not.Null);
    }

    [Test]
    public void AReadThatThrowsAtOnceIsAFailedRead()
    {
        var feed = new CatalogFeed(_ => throw new UnaskedReadException(IntelligenceText.NotConnected));
        feed.Open(Start);
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Error, Is.EqualTo("your computer isn't connected. Press Refresh when it is."));
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
