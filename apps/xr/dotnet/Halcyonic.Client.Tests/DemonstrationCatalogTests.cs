using System;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The gallery's sample catalog in the recorded demonstration, and none in a recorded run (ADR 0039).</summary>
public class DemonstrationCatalogTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");

    [Test]
    public void TheDemonstrationHoldsTheStandInsCatalogMarkedSimulated()
    {
        var recording = Demonstration.Recording();
        Assert.That(recording.Catalog, Is.Not.Null);
        Assert.That(recording.Catalog!.Source.Synthetic, Is.True);
        Assert.That(recording.Catalog.Projects, Has.Count.EqualTo(8));
        Assert.That(recording.Catalog.Projects.All(project => project.Local.ProjectIds.Count == 0 && project.Local.Suggestions.Count == 0), Is.True);
    }

    [Test]
    public void ItsTimesMoveWithThePlaybackSoItsProjectsStayAsRecentAsRecorded()
    {
        var recording = Demonstration.Recording();
        var recorded = recording.Catalog!;
        var moved = recording.CatalogAt(Now)!;
        var shift = Now - recording.CatalogReadAt;
        Assert.That(DateTimeOffset.Parse(moved.Sync.RepositoriesAt!), Is.EqualTo(DateTimeOffset.Parse(recorded.Sync.RepositoriesAt!) + shift));
        Assert.That(Now - DateTimeOffset.Parse(moved.Sync.RepositoriesAt!), Is.EqualTo(TimeSpan.FromMinutes(4)));
        var first = moved.Projects[0];
        Assert.That(DateTimeOffset.Parse(first.State.LastActivityAt!), Is.EqualTo(DateTimeOffset.Parse(recorded.Projects[0].State.LastActivityAt!) + shift));
        Assert.That(recorded.Sync.RepositoriesAt, Is.Not.EqualTo(moved.Sync.RepositoriesAt), "a copy: the recording keeps its own times");
        Assert.That(recording.CatalogAt(Now)!.Sync.RepositoriesAt, Is.EqualTo(moved.Sync.RepositoriesAt));
    }

    [Test]
    public void ACatalogThatIsNotTheStandInsIsRefused()
    {
        var real = Demonstration.Edit(document => document["catalog"]!["answer"]!["source"]!["synthetic"] = false);
        Assert.That(() => DemonstrationRecording.Parse(real), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("marked as simulated"));
        var unavailable = Demonstration.Edit(document => document["catalog"]!["answer"] = Newtonsoft.Json.Linq.JObject.Parse(
            "{\"availability\":\"unavailable\",\"reason\":{\"code\":\"not_running\",\"message\":\"x\"}}"));
        Assert.That(() => DemonstrationRecording.Parse(unavailable), Throws.InstanceOf<InvalidDataException>());
        var unreadable = Demonstration.Edit(document => document["catalog"]!["answer"]!["sync"]!["repositories_at"] = "yesterday");
        Assert.That(() => DemonstrationRecording.Parse(unreadable), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void ARecordedRunThatCarriesACatalogIsRefused()
    {
        var demonstration = (Newtonsoft.Json.Linq.JObject)Json.Parse(Demonstration.Text());
        var run = RecordedRunTestsShapes.AsRecordedRun(document => document["catalog"] = demonstration["catalog"]!.DeepClone());
        Assert.That(() => DemonstrationRecording.Parse(run), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("no catalog"));
    }

    [Test]
    public void TheGalleryShowsTheSampleWhileTheDemonstrationPlaysAndNoneWhileARecordedRunPlays()
    {
        var recording = Demonstration.Recording();
        AvailableCatalog? playing = null;
        var reads = new DemonstrationCatalogReads(_ => playing, () => Now);
        var state = new ClientProjection();
        var overview = WorkOverview.Of(state, new StageVisibility(), _ => false);
        Gallery Shown() => Gallery.Build(state, overview, reads.ReadCatalogAsync().Result, null, Now, TimeZoneInfo.Utc);

        // The fallback plays the recorded run, then the demonstration, then the run again.
        foreach (var demonstration in new[] { false, true, false })
        {
            playing = demonstration ? recording.CatalogAt(Now) : null;
            var gallery = Shown();
            var read = reads.ReadCatalogAsync().Result;
            Assert.That(read.Recorded, Is.True, "never taken for a live answer");
            if (demonstration)
            {
                Assert.That(gallery.Samples, Has.Count.EqualTo(8));
                Assert.That(gallery.SampleLabel, Is.EqualTo("Sample projects"));
                Assert.That(gallery.Items, Is.Empty, "the sample is never among real projects");
                Assert.That(gallery.Note, Is.Null);
                Assert.That(gallery.Age, Is.EqualTo("Checked today at 11:56"));
            }
            else
            {
                Assert.That(gallery.Samples, Is.Empty);
                Assert.That(gallery.Note, Is.EqualTo("Your other projects aren't part of this recording."));
                Assert.That(read.Response, Is.InstanceOf<UnavailableCatalog>());
            }
        }
    }
}
