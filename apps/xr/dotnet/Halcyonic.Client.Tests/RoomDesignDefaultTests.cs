#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's default is on in every build, release included (ON-1b): the Unity layer's line that turns it on must not sit
/// inside a region compiled only into development builds or the editor, where a release build would never see it.
/// </summary>
public sealed class RoomDesignDefaultTests
{
    [Test]
    public void TheLineThatTurnsTheRedesignOnIsCompiledIntoReleaseBuilds()
    {
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(Repository.PathTo("apps/xr/Assets/Halcyonic"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("Editor")))
        {
            var conditions = new Stack<string>();
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("#if ")) conditions.Push(line.Substring(4));
                else if (line.StartsWith("#elif ")) { conditions.Pop(); conditions.Push(line.Substring(6)); }
                else if (line.StartsWith("#else")) conditions.Push("!(" + conditions.Pop() + ")");
                else if (line.StartsWith("#endif")) conditions.Pop();
                else if (line.Contains("RoomDesign.Enabled = true") && line.Contains("=>"))
                {
                    found.Add(Path.GetFileName(file));
                    Assert.That(conditions.Where(condition => condition.Contains("DEVELOPMENT_BUILD") || condition.Contains("UNITY_EDITOR")), Is.Empty,
                        Path.GetFileName(file) + " turns the redesign on only in development builds or the editor");
                }
            }
        }
        Assert.That(found, Has.Count.EqualTo(1), "one line turns the redesign on by default");
    }
}
