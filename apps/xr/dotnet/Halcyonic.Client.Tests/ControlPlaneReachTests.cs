#nullable enable
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The night's security fix (JUDGE's alert, 2026-11-12): the menu director, its plane and Hold to Talk reach a computer only
/// through what their setup hands them, never the device's stored settings, so an Editor walk or check against a scratch plane
/// can never read, send to or show the owner's own computer.
/// </summary>
public sealed class ControlPlaneReachTests
{
    [Test]
    public void TheDirectorItsPlaneAndHoldToTalkNeverReadTheStoredSettings()
    {
        var folder = Repository.PathTo("apps/xr/Assets/Halcyonic/Workspace");
        var files = Directory.GetFiles(folder, "MenuDirector*.cs").Concat(Directory.GetFiles(folder, "MenuPlane*.cs"))
            .Append(Path.Combine(folder, "HoldToTalk.cs")).ToList();
        Assert.That(files, Has.Count.GreaterThan(4));
        var reaching = files.Where(file => File.ReadAllText(file).Contains("ControlPlaneSettings.")).Select(Path.GetFileName).ToList();
        Assert.That(reaching, Is.Empty, "these read the stored settings instead of their setup's");
    }
}
