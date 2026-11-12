#nullable enable
using System;
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

    /// <summary>
    /// AUDIT3's alert (2026-11-12): in batch mode the stored settings refuse every fallback before reading anything, and
    /// the checks hand the director stand-ins of their own instead of setting the settings' statics.
    /// </summary>
    [Test]
    public void InBatchModeTheStoredSettingsRefuseEveryFallback()
    {
        var settings = File.ReadAllText(Repository.PathTo("apps/xr/Assets/Halcyonic/Scripts/ControlPlaneSettings.cs"));
        var target = settings[settings.IndexOf("public static ControlPlaneTarget? Target()", StringComparison.Ordinal)..];
        var body = target[(target.IndexOf('{') + 1)..].TrimStart();
        Assert.That(body, Does.StartWith("if (Application.isBatchMode)"), "Target must refuse its fallbacks before reading any");
        Assert.That(body[..body.IndexOf(';')], Does.Contain("UnattendedReach.Target("));
        var store = settings[settings.IndexOf("public static IPairingStore PairingStore", StringComparison.Ordinal)..];
        Assert.That(store[..store.IndexOf(';')], Does.Contain("Application.isBatchMode").And.Contain("throw new UnattendedReachRefusedException"));
        var token = settings[settings.IndexOf("public static string? ReadAccessToken()", StringComparison.Ordinal)..];
        Assert.That(token[(token.IndexOf('{') + 1)..].TrimStart(), Does.StartWith("if (Application.isBatchMode) throw new UnattendedReachRefusedException"));

        var director = File.ReadAllText(Repository.PathTo("apps/xr/Assets/Halcyonic/Workspace/WorkspaceDirector.cs"));
        Assert.That(director.Split("ControlPlaneSettings.").Length - 1, Is.EqualTo(2), "only TargetOf's and ApiOf's defaults read the stored settings");
        var editor = Directory.GetFiles(Repository.PathTo("apps/xr/Assets"), "*.cs", SearchOption.AllDirectories)
            .Where(file => file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("typeof(ControlPlaneSettings)")).Select(Path.GetFileName).ToList();
        Assert.That(editor, Is.Empty, "these set the stored settings' statics instead of handing in stand-ins");
    }
}
