using System;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The kid flow's commands (M1.8e): each well formed, with a fresh id, and nothing else in it.</summary>
public class CommandFactoryTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private readonly CommandFactory commands = new(Samples.Client, () => new DateTimeOffset(2026, 10, 31, 5, 0, 0, TimeSpan.Zero));

    private static JObject Payload(CommandEnvelope command) =>
        (JObject)JObject.Parse(HalcyonicJson.Serialize(command))["payload"]!;

    [Test]
    public void TheCheckThePreviewAndPlayOnLaptopNameOnlyTheProject()
    {
        var built = new (CommandEnvelope Command, string Type)[]
        {
            (commands.CheckProject(ProjectId), "project.check"),
            (commands.StartPreview(ProjectId), "project.preview.start"),
            (commands.StopPreview(ProjectId), "project.preview.stop"),
            (commands.OpenOnMac(ProjectId), "project.open_on_mac"),
        };
        foreach (var (command, type) in built)
        {
            Assert.That(command.CommandType, Is.EqualTo(type));
            Assert.That(Payload(command), Is.EqualTo(new JObject { ["project_id"] = ProjectId }).Using(JToken.EqualityComparer), type);
            Assert.That(command.Client, Is.SameAs(Samples.Client));
            Assert.That(command.IssuedAt, Is.EqualTo("2026-10-31T05:00:00.000Z"));
        }
        Assert.That(commands.OpenOnMac(ProjectId).CommandId, Is.Not.EqualTo(commands.OpenOnMac(ProjectId).CommandId), "a new command is a new request");
    }

    [Test]
    public void AVrVersionNamesTheOriginalAndTheNewFolder()
    {
        var command = commands.MakeVrVersion(ProjectId, "/Users/someone/HalcyonicProjects", "garden-vr");
        Assert.That(command.CommandType, Is.EqualTo("project.make_vr_version"));
        Assert.That(
            Payload(command),
            Is.EqualTo(new JObject
            {
                ["original_project_id"] = ProjectId,
                ["root"] = "/Users/someone/HalcyonicProjects",
                ["folder_name"] = "garden-vr",
            }).Using(JToken.EqualityComparer));
    }
}
