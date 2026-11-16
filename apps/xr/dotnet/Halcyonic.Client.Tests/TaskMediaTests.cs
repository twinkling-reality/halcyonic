using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Protocol 3's C# client half (the lead's ruling of 2026-11-16 03:18Z): what a task was given, and the new refusals.</summary>
public class TaskMediaTests
{
    [Test]
    public void ATaskWithNoMediaHasNone()
    {
        Assert.That(TaskMedia.Of(null), Is.Empty);
        Assert.That(TaskMedia.Of(new ExecutionView { ExecutionId = "e" }), Is.Empty, "a view before protocol 3 carries no media");
    }

    [Test]
    public void APhotoAndAVideoKeepTheirOrderNameAndStill()
    {
        var execution = new ExecutionView
        {
            ExecutionId = "e",
            Media = new List<ExecutionMediaView>
            {
                new() { Path = "media/camel-ride.mov", MediaType = MediaType.VideoQuicktime, Still = "media/.stills/camel-ride-1.jpg" },
                new() { Path = "media/dune.heic", MediaType = MediaType.ImageHeic, Still = null },
            },
        };
        var items = TaskMedia.Of(execution);
        Assert.That(items.Select(item => (item.Name.Text, item.Kind, item.Still)), Is.EqualTo(new[]
        {
            ("camel-ride.mov", TaskMediaKind.Video, (string?)"media/.stills/camel-ride-1.jpg"),
            ("dune.heic", TaskMediaKind.Photo, (string?)null),
        }));
        Assert.That(items[0].Name.Runs.All(run => run.Theirs), "the file's name is the person's, never ours");
    }

    [Test]
    public void EveryMediaTypeHasAKind()
    {
        foreach (MediaType type in System.Enum.GetValues(typeof(MediaType)))
            Assert.DoesNotThrow(() => TaskMedia.KindOf(type), type.ToString());
    }

    [Test]
    public void TheNewRefusalsAreSaidInSentencesWithAWayOn()
    {
        Assert.That(WorkspaceText.WhyRefused(RejectionCode.MediaNotKept), Is.EqualTo(WorkspaceText.MediaNotKept));
        Assert.That(WorkspaceText.WhyRefused(RejectionCode.MediaFull), Is.EqualTo(WorkspaceText.MediaFull));
        Assert.That(WorkspaceText.WhyRefused(RejectionCode.MediaPresent), Is.EqualTo(WorkspaceText.MediaPresent));
        foreach (var code in new[] { "media_not_kept", "media_full", "media_present" })
            Assert.That(WorkspaceText.WhyFailed(code), Is.Not.Null, "the host's failure by the same code says the same: " + code);
        Assert.That(new[] { WorkspaceText.MediaNotKept, WorkspaceText.MediaFull, WorkspaceText.MediaPresent }, Has.None.Contain("·"));
    }
}
