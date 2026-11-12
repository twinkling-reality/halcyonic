#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A test's control plane never reaches the account's task copies (AUDIT3's ALERT, 2026-11-12): its copies' folder lies beside
/// its scratch data, never the default ~/.halcyonic-tasks, where a plane's start sweeps every copy its journal does not hold.
/// </summary>
public sealed class ControlPlaneProcessTests
{
    [Test]
    public void ATestPlanesTaskCopiesLieBesideItsScratchDataNeverInTheAccountsFolder()
    {
        var data = Path.Combine(Path.GetTempPath(), "halcyonic-test-" + Guid.NewGuid().ToString("N"));
        var tasks = ControlPlaneProcess.TasksDirFor(data);
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".halcyonic-tasks");
        Assert.That(tasks, Is.Not.EqualTo(home));
        Assert.That(tasks.StartsWith(home, StringComparison.Ordinal), Is.False, "never under the account's own folder");
        Assert.That(Path.GetDirectoryName(tasks), Is.EqualTo(Path.GetDirectoryName(Path.GetFullPath(data))), "beside its data, as the plane requires");
        Assert.That(tasks.StartsWith(Path.GetFullPath(data) + Path.DirectorySeparatorChar, StringComparison.Ordinal), Is.False, "never inside its data");
        Assert.That(File.ReadAllText(Repository.PathTo("apps/xr/dotnet/Halcyonic.Client.Tests/ControlPlaneProcess.cs")), Does.Contain("start.Environment[\"HALCYONIC_TASKS_DIR\"]"),
            "the one helper that starts a test plane sets it");
    }
}
