using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The headset's own reading of a person's world (ADR 0049, AUDIT S1 and S2).</summary>
public class WorldOfTests
{
    private static string Repository => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", ".."));

    /// <summary>The world starter's own description, parsed as the headset parses what the Mac sends.</summary>
    private static WorldDescription Starting() =>
        JsonConvert.DeserializeObject<WorldDescription>(File.ReadAllText(Path.Combine(Repository, "starters", "world", "template", "world.json")))!;

    private static ProjectView Project(string id, string name, string? starter = null, WorldDescription? world = null, string observedAt = "2026-11-11T07:00:00.000Z") => new()
    {
        ProjectId = id,
        Name = name,
        Starter = starter,
        CreatedAt = "2026-11-11T06:00:00.000Z",
        UpdatedAt = "2026-11-11T06:00:00.000Z",
        World = world == null ? null : new ProjectWorldView { Description = world, ObservedAt = observedAt },
    };

    [Test]
    public void The_starters_description_keeps_every_rule_and_reads_as_dusk()
    {
        var description = Starting();
        Assert.That(WorldOf.Problem(description), Is.Null);
        var mood = WorldOf.Read(description, new List<ProjectView>(), out var why);
        Assert.That(why, Is.Null);
        Assert.That(mood!.Night, Is.LessThan(0.5));
    }

    [Test]
    public void A_dark_sky_reads_as_night_and_a_set_sun_pulls_toward_it()
    {
        var description = Starting();
        description.Sky.Zenith = "#05070F";
        description.Sky.Horizon = "#1B2238";
        Assert.That(WorldOf.NightOf(description), Is.GreaterThan(0.9));
        var dusk = Starting();
        dusk.Disc!.Height = -10;
        Assert.That(WorldOf.NightOf(dusk), Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Every_rule_the_bindings_do_not_enforce_is_checked_again()
    {
        var cases = new (string Where, System.Action<WorldDescription> Break)[]
        {
            ("/format", d => d.Format = 2),
            ("/sky/zenith", d => d.Sky.Zenith = "red"),
            ("/sky/band", d => d.Sky.Band = "#12345"),
            ("/disc/height", d => d.Disc!.Height = 91),
            ("/disc/size", d => d.Disc!.Size = 0),
            ("/stars", d => d.Stars = 1.01),
            ("/stars", d => d.Stars = double.NaN),
            ("/clouds", d => d.Clouds.Colour = "url(x)"),
            ("/water", d => d.Water.Ripple = -0.1),
            ("/ground", d => d.Ground.Radius = 1000),
            ("/wind", d => d.Wind.Bearing = 181),
            ("/places", d => d.Places = Enumerable.Repeat(new WorldPlace { Project = "Garden", Bearing = 0, Distance = 5 }, 9).ToList()),
            ("/places/0/distance", d => d.Places = new List<WorldPlace> { new() { Project = "Garden", Bearing = 0, Distance = 81 } }),
            ("/places/0/project", d => d.Places = new List<WorldPlace> { new() { Project = "   ", Bearing = 0, Distance = 5 } }),
            ("/places/0/project", d => d.Places = new List<WorldPlace> { new() { Project = new string('x', 201), Bearing = 0, Distance = 5 } }),
        };
        foreach (var (where, breakIt) in cases)
        {
            var description = Starting();
            breakIt(description);
            Assert.That(WorldOf.Problem(description), Is.EqualTo(where), where);
            Assert.That(WorldOf.Read(description, new List<ProjectView>(), out var why), Is.Null, where);
            Assert.That(why, Is.EqualTo(where));
        }
    }

    [Test]
    public void A_place_is_matched_to_a_project_by_ordinal_equality_only_and_names_are_never_carried()
    {
        var description = Starting();
        description.Places = new List<WorldPlace>
        {
            new() { Project = "Garden", Bearing = 10, Distance = 8 },
            new() { Project = "garden", Bearing = 20, Distance = 8 },
            new() { Project = "Line one\nline two ‮", Bearing = 30, Distance = 8 },
            new() { Project = "Garden", Bearing = 40, Distance = 8 },
        };
        var projects = new List<ProjectView>
        {
            Project("p-garden", "Garden"),
            Project("p-other", "Line one\nline two"),
        };
        var mood = WorldOf.Read(description, projects, out _);
        Assert.That(mood!.PlaceProjectIds, Is.EqualTo(new[] { "p-garden" }));
    }

    [Test]
    public void The_persons_world_is_the_world_project_read_most_recently()
    {
        var projects = new List<ProjectView>
        {
            Project("p-app", "App", "iwsdk-vr"),
            Project("p-old", "Old World", "world", Starting(), "2026-11-11T06:30:00.000Z"),
            Project("p-new", "New World", "world", Starting(), "2026-11-11T07:30:00.000Z"),
            Project("p-none", "Empty World", "world"),
        };
        Assert.That(WorldOf.Project(projects)!.ProjectId, Is.EqualTo("p-new"));
        Assert.That(WorldOf.Project(projects.Take(1)), Is.Null);
    }
}
