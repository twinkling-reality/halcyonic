using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The headset's own reading of a person's world (ADR 0049 format 2, AUDIT S1 and S2).</summary>
public class WorldOfTests
{
    private static string Repository => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", ".."));

    private static string Tracked(string path) => File.ReadAllText(Path.Combine(Repository, "starters", "world", path));

    /// <summary>The world starter's own description, parsed as the headset parses what the Mac sends.</summary>
    private static WorldDescription Starting() => JsonConvert.DeserializeObject<WorldDescription>(Tracked("template/world.json"))!;

    /// <summary>The scene the starter begins in, as its manifest names it.</summary>
    private static WorldScene Cylinder()
    {
        var scene = WorldOf.Scene(Tracked("scenes/cylinder.json"), out var why);
        Assert.That(why, Is.Null);
        return scene!;
    }

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
    public void The_starters_description_keeps_every_rule_and_mixes_every_light_of_its_scene_as_made()
    {
        var scene = Cylinder();
        var mix = WorldOf.Read(Starting(), scene, new List<ProjectView>(), out var why);
        Assert.That(why, Is.Null);
        Assert.That(mix!.Lights.Select(l => l.Light), Is.EqualTo(scene.Lights.Select(l => l.Light)), "in the manifest's order");
        Assert.That(mix.Lights.All(l => l.Brightness == 1), Is.True, "every light as made");
        foreach (var light in mix.Lights)
        {
            var (r, g, b) = WorldOf.Linear(Starting().Lights.Single(l => l.Light == light.Light).Colour);
            Assert.That((light.Red, light.Green, light.Blue), Is.EqualTo((r, g, b)), light.Light + ": its tint, in linear light");
        }
        Assert.That(mix.Night, Is.EqualTo(0));
        Assert.That(mix.HazeThickness, Is.EqualTo(Starting().Haze.Thickness), "the haze as the starter gives it");
    }

    [Test]
    public void Day_lights_fade_by_the_square_of_what_is_left_of_dusk_night_lights_come_with_it_and_the_rest_hold()
    {
        var description = Starting();
        description.Night = 0.75;
        description.Lights.Single(l => l.Light == "sunset").Brightness = 2;
        var mix = WorldOf.Read(description, Cylinder(), new List<ProjectView>(), out _)!;
        Assert.That(mix.Lights.Single(l => l.Light == "sunset").At(mix.Night), Is.EqualTo(2 * 0.25 * 0.25).Within(1e-9));
        Assert.That(mix.Lights.Single(l => l.Light == "moon").At(mix.Night), Is.EqualTo(0.75).Within(1e-9));
        Assert.That(mix.Lights.Single(l => l.Light == "towns").At(mix.Night), Is.EqualTo(1));
    }

    [Test]
    public void A_tint_is_read_in_linear_light()
    {
        var description = Starting();
        description.Lights[0].Colour = "#808000";
        var light = WorldOf.Read(description, Cylinder(), new List<ProjectView>(), out _)!.Lights.Single(l => l.Light == description.Lights[0].Light);
        Assert.That(light.Red, Is.EqualTo(0.2158605).Within(1e-6));
        Assert.That(light.Blue, Is.EqualTo(0));
    }

    [Test]
    public void Every_rule_the_bindings_do_not_enforce_and_every_rule_of_its_scene_is_checked_again()
    {
        var cases = new (string Where, System.Action<WorldDescription> Break)[]
        {
            ("/format", d => d.Format = 1),
            ("/scene", d => d.Scene = "castle"),
            ("/scene", d => d.Scene = "../x"),
            ("/night", d => d.Night = 1.01),
            ("/night", d => d.Night = double.NaN),
            ("/lights", d => d.Lights.RemoveAt(0)),
            ("/lights/6/light", d => d.Lights.Add(new WorldLight { Light = d.Lights[0].Light, Brightness = 1, Colour = "#FFFFFF" })),
            ("/lights/0/light", d => d.Lights[0].Light = "rain"),
            ("/lights/0/light", d => d.Lights[0].Light = "Sunset"),
            ("/lights/0/brightness", d => d.Lights[0].Brightness = 4.01),
            ("/lights/0/brightness", d => d.Lights[0].Brightness = -0.01),
            ("/lights/0/colour", d => d.Lights[0].Colour = "red"),
            ("/haze/thickness", d => d.Haze.Thickness = 4.5),
            ("/haze/colour", d => d.Haze.Colour = "url(x)"),
            ("/places", d => d.Places = Enumerable.Range(0, 9).Select(n => new WorldPlace { Project = "P" + n, At = "tower" }).ToList()),
            ("/places/0/at", d => d.Places = new List<WorldPlace> { new() { Project = "Garden", At = "castle" } }),
            ("/places/1/at", d => d.Places = new List<WorldPlace> { new() { Project = "Garden", At = "tower" }, new() { Project = "Other", At = "tower" } }),
            ("/places/0/project", d => d.Places = new List<WorldPlace> { new() { Project = "   ", At = "tower" } }),
            ("/places/0/project", d => d.Places = new List<WorldPlace> { new() { Project = new string('x', 201), At = "tower" } }),
        };
        foreach (var (where, breakIt) in cases)
        {
            var description = Starting();
            breakIt(description);
            Assert.That(WorldOf.Problem(description, Cylinder()), Is.EqualTo(where), where);
            Assert.That(WorldOf.Read(description, Cylinder(), new List<ProjectView>(), out var why), Is.Null, where);
            Assert.That(why, Is.EqualTo(where));
        }
    }

    [Test]
    public void A_manifest_that_names_a_light_or_place_twice_or_holds_an_unknown_member_is_not_one()
    {
        var json = Tracked("scenes/cylinder.json");
        Assert.That(WorldOf.Scene(json.Replace("\"light\": \"beam\"", "\"light\": \"sunset\""), out var twice), Is.Null);
        Assert.That(twice, Is.EqualTo("/lights/1/light"));
        Assert.That(WorldOf.Scene(json.Replace("\"place\": \"lake\"", "\"place\": \"tower\""), out var place), Is.Null);
        Assert.That(place, Is.EqualTo("/places/1/place"));
        Assert.That(WorldOf.Scene(json.Replace("\"format\": 1,", "\"format\": 1, \"picture\": \"a.png\","), out var unknown), Is.Null);
        Assert.That(unknown, Is.EqualTo("not a manifest"));
    }

    [Test]
    public void A_place_is_matched_to_a_project_by_ordinal_equality_only_and_names_are_never_carried()
    {
        var description = Starting();
        description.Places = new List<WorldPlace>
        {
            new() { Project = "Garden", At = "tower" },
            new() { Project = "garden", At = "lake" },
            new() { Project = "Line one\nline two ‮", At = "left" },
            new() { Project = "Portfolio", At = "far_end" },
        };
        var projects = new List<ProjectView>
        {
            Project("p-garden", "Garden"),
            Project("p-other", "Line one\nline two"),
            Project("p-portfolio", "Portfolio"),
        };
        var mix = WorldOf.Read(description, Cylinder(), projects, out _)!;
        Assert.That(mix.Places.Select(p => (p.Place, p.ProjectId)), Is.EqualTo(new[] { ("tower", "p-garden"), ("far_end", "p-portfolio") }));
    }

    [Test]
    public void Every_scene_the_headset_build_holds_is_the_starters_own_manifest_byte_for_byte()
    {
        var scenes = Path.Combine(Repository, "apps", "xr", "Assets", "Halcyonic", "World", "Resources", "HalcyonicWorld", "Scenes");
        if (!Directory.Exists(scenes)) return;
        foreach (var folder in Directory.GetDirectories(scenes))
        {
            var held = Path.Combine(folder, "scene.json");
            if (!File.Exists(held)) continue;
            var name = Path.GetFileName(folder);
            Assert.That(File.ReadAllBytes(held), Is.EqualTo(File.ReadAllBytes(Path.Combine(Repository, "starters", "world", "scenes", name + ".json"))), name);
        }
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
