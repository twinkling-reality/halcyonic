using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A player build keeps a shader only where an asset in it references the shader or Always Included lists it, and
/// Shader.Find finds nothing for one it stripped: the headset then draws Unity's error magenta, which Editor renders never
/// show (DEVICE's D-XR-3: every kit capsule button magenta, Kit Pill found by name and referenced by no asset). Every
/// Halcyonic shader the shipping code names must be referenced by a Resources material or Always Included, and every
/// material the code loads from Resources must exist.
/// </summary>
public class ShadersInBuildTests
{
    private static readonly string Assets = Repository.PathTo("apps/xr/Assets");

    /// <summary>The shipping code's sources: Halcyonic's own, outside Editor folders.</summary>
    private static IEnumerable<string> ShippingSources() =>
        Directory.EnumerateFiles(Path.Combine(Assets, "Halcyonic"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Segments(path).Contains("Editor"));

    private static string[] Segments(string path) => Path.GetRelativePath(Assets, path).Split(Path.DirectorySeparatorChar);

    private static string GuidOf(string asset) =>
        Regex.Match(File.ReadAllText(asset + ".meta"), "^guid: ([0-9a-f]{32})", RegexOptions.Multiline).Groups[1].Value;

    [Test]
    public void EveryHalcyonicShaderTheCodeNamesShipsInThePlayerBuild()
    {
        var shaders = Directory.EnumerateFiles(Assets, "*.shader", SearchOption.AllDirectories)
            .Select(path => (Name: Regex.Match(File.ReadAllText(path), "^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Multiline).Groups[1].Value,
                Guid: GuidOf(path), Path: path))
            .Where(shader => shader.Name.StartsWith("Halcyonic/"))
            .ToList();
        Assert.That(shaders, Is.Not.Empty, "the project's Halcyonic shaders were not found");
        // What the build keeps: a shader a Resources material references, one in a Resources folder itself, or one Always Included.
        var kept = Directory.EnumerateFiles(Assets, "*.mat", SearchOption.AllDirectories)
            .Where(path => Segments(path).Contains("Resources"))
            .Select(path => Regex.Match(File.ReadAllText(path), "m_Shader: \\{fileID: 4800000, guid: ([0-9a-f]{32})").Groups[1].Value)
            .Concat(shaders.Where(shader => Segments(shader.Path).Contains("Resources")).Select(shader => shader.Guid))
            .Concat(Regex.Matches(AlwaysIncluded(), "guid: ([0-9a-f]{32})").Select(match => match.Groups[1].Value))
            .ToHashSet();
        // Named anywhere in the shipping code, as Shader.Find's argument or a constant passed to it.
        var named = ShippingSources()
            .SelectMany(path => shaders.Where(shader => File.ReadAllText(path).Contains("\"" + shader.Name + "\"")).Select(shader => (Source: path, Shader: shader)))
            .ToList();
        Assert.That(named, Is.Not.Empty, "no shipping code names a Halcyonic shader: the scan no longer sees Shader.Find");
        var stripped = named.Where(each => !kept.Contains(each.Shader.Guid))
            .Select(each => each.Shader.Name + " (named in " + Path.GetRelativePath(Assets, each.Source) + ")").ToList();
        Assert.That(stripped, Is.Empty, "named in shipping code, but no Resources material references it and Always Included does not list it, so the player build strips it");
    }

    [Test]
    public void EveryMaterialTheCodeLoadsFromResourcesExists()
    {
        var paths = ShippingSources()
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "Resources\\.Load<Material>\\(\"([^\"]+)\"\\)|const string MaterialPath = \"([^\"]+)\"")
                .Select(match => (Source: path, Resource: match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)))
            .ToList();
        Assert.That(paths, Is.Not.Empty, "no shipping code loads a material from Resources: the scan no longer sees it");
        var materials = Directory.EnumerateFiles(Assets, "*.mat", SearchOption.AllDirectories)
            .Select(path => Segments(path))
            .Where(segments => segments.Contains("Resources"))
            .Select(segments => string.Join("/", segments.SkipWhile(segment => segment != "Resources").Skip(1)))
            .Select(resource => resource.Substring(0, resource.Length - ".mat".Length))
            .ToHashSet();
        var missing = paths.Where(each => !materials.Contains(each.Resource))
            .Select(each => each.Resource + " (loaded in " + Path.GetRelativePath(Assets, each.Source) + ")").ToList();
        Assert.That(missing, Is.Empty, "loaded from Resources, but no material is there");
    }

    private static string AlwaysIncluded()
    {
        var settings = File.ReadAllText(Repository.PathTo("apps/xr/ProjectSettings/GraphicsSettings.asset"));
        var list = Regex.Match(settings, "m_AlwaysIncludedShaders:\\n((?:  - .*\\n)*)");
        Assert.That(list.Success, "GraphicsSettings.asset has no Always Included list to read");
        return list.Groups[1].Value;
    }
}
