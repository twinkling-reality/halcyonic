using System.IO;
using System.Linq;
using System.Xml;
using Halcyonic.XR.Editor;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The preview relay in development builds only (ADR 0052, the lead's decision of 2026-11-11): the project's own manifest
/// carries none of it; a development build's gets the service and its two permissions; a release build's carries neither,
/// even where Unity reused a Gradle project a development build made.
/// </summary>
public class RelayManifestTests
{
    private const string Android = RelayManifest.AndroidNamespace;

    /// <summary>The project's own Android manifest, as Unity starts the library's from it.</summary>
    private static XmlDocument ProjectManifest()
    {
        var document = new XmlDocument();
        document.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../Assets/Plugins/Android/AndroidManifest.xml"));
        return document;
    }

    private static string[] Permissions(XmlDocument document) =>
        document.DocumentElement!.SelectNodes("uses-permission")!.Cast<XmlElement>().Select(element => element.GetAttribute("name", Android)).ToArray();

    private static XmlElement[] Services(XmlDocument document) =>
        document.DocumentElement!.SelectNodes("application/service")!.Cast<XmlElement>()
            .Where(element => element.GetAttribute("name", Android) == RelayManifest.Service).ToArray();

    [Test]
    public void AReleaseManifestCarriesNeitherPermissionNorTheService()
    {
        var project = ProjectManifest();
        Assert.That(Permissions(project), Has.None.EqualTo(RelayManifest.ForegroundService).And.None.EqualTo(RelayManifest.SpecialUse),
            "the project's own manifest names neither");
        Assert.That(Services(project), Is.Empty);

        var release = ProjectManifest();
        RelayManifest.Apply(release, include: false);
        Assert.That(Permissions(release), Has.None.EqualTo(RelayManifest.ForegroundService).And.None.EqualTo(RelayManifest.SpecialUse));
        Assert.That(Services(release), Is.Empty);
    }

    [Test]
    public void ADevelopmentManifestGetsTheServiceAndBothPermissionsOnce()
    {
        var development = ProjectManifest();
        RelayManifest.Apply(development, include: true);
        RelayManifest.Apply(development, include: true);
        Assert.That(Permissions(development).Count(name => name == RelayManifest.ForegroundService), Is.EqualTo(1));
        Assert.That(Permissions(development).Count(name => name == RelayManifest.SpecialUse), Is.EqualTo(1));
        var service = Services(development).Single();
        Assert.That(service.GetAttribute("exported", Android), Is.EqualTo("false"));
        Assert.That(service.GetAttribute("foregroundServiceType", Android), Is.EqualTo("specialUse"));
        var property = (XmlElement)service.SelectSingleNode("property")!;
        Assert.That(property.GetAttribute("name", Android), Is.EqualTo(RelayManifest.SubtypeProperty));
        Assert.That(property.GetAttribute("value", Android), Is.EqualTo(RelayManifest.Subtype));
    }

    [Test]
    public void AReleaseBuildAfterADevelopmentBuildTakesThemOutAgain()
    {
        var reused = ProjectManifest();
        RelayManifest.Apply(reused, include: true);
        RelayManifest.Apply(reused, include: false);
        Assert.That(Permissions(reused), Has.None.EqualTo(RelayManifest.ForegroundService).And.None.EqualTo(RelayManifest.SpecialUse));
        Assert.That(Services(reused), Is.Empty);
        Assert.That(Permissions(reused), Is.EquivalentTo(Permissions(ProjectManifest())), "and nothing else of the manifest moves");
    }
}
