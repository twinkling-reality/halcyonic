using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Watch It Work's count of changed files, in AUDIT2's words (03:06Z and 03:07Z), one source for the view and its renders.</summary>
public class WatchCountTests
{
    [Test]
    public void AtWorkTheCountSaysSoFarAndOneFileIsOneFile()
    {
        Assert.That(WorkspaceText.WatchCount(3, atWork: true), Is.EqualTo("Changed 3 Files So Far"));
        Assert.That(WorkspaceText.WatchCount(1, atWork: true), Is.EqualTo("Changed 1 File So Far"));
    }

    [Test]
    public void OnceTheTurnEndsTheCountIsTheStepsOwn()
    {
        Assert.That(WorkspaceText.WatchCount(3, atWork: false), Is.EqualTo("Changed 3 Files"));
        Assert.That(WorkspaceText.WatchCount(3, atWork: false), Is.EqualTo(TaskSteps.Changed(3)), "the watch and the steps say one count alike");
    }

    [Test]
    public void NothingChangedSaysYetOnlyWhileAtWork()
    {
        Assert.That(WorkspaceText.WatchCount(0, atWork: true), Is.EqualTo("No Files Changed Yet"));
        Assert.That(WorkspaceText.WatchCount(0, atWork: false), Is.Null, "an ended turn's Yet would be false");
    }
}
