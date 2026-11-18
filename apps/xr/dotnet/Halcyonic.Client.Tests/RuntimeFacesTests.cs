using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Every headset face names an agent app by what it is (<see cref="RuntimeFaces"/>), never its maker's name nor a model's id, and
/// the host is "This Computer"; the picker alone keeps each app's own name (AUDIT2's rule 182).
/// </summary>
[TestFixture, NonParallelizable]
public class RuntimeFacesTests
{
    private const string ListedModel = "ollama/qwen3.6";

    private static readonly string[] MakersWords =
        { "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode", "Kiro", "Antigravity", "Google", "Cursor", "qwen", "ollama", "Mac" };

    /// <summary>The verified kinds with their own display names, and the head each face says instead.</summary>
    private static readonly (string Kind, string Name, string Head)[] Apps =
    {
        ("claude-agent", "Claude Code 1.0", "Hosted Models"),
        ("codex", "Codex 0.157.0", "Local Models"),
        ("opencode", "OpenCode 1.2", "Your Model Setup"),
        ("kiro", "Kiro 3.4.0", "Signed-In App"),
        ("antigravity", "Antigravity 1.0", "Signed-In App"),
        ("cursor", "Cursor 2.0", "Signed-In App"),
    };

    [TearDown]
    public void Off() => CaptureNames.UseGeneric(false);

    private static RuntimeDescriptor Runtime(string kind, string name) => new()
    {
        RuntimeId = kind,
        Kind = kind,
        DisplayName = name,
        Synthetic = false,
        ModelChoice = ModelChoice.Listed,
        Capabilities = new RuntimeCapabilities { StartExecution = true },
    };

    /// <summary>What the faces say of <paramref name="runtime"/>, each with where it is drawn.</summary>
    private static IEnumerable<(string Face, string? Words)> Faces(RuntimeDescriptor runtime)
    {
        var reference = new RuntimeRef { RuntimeId = runtime.RuntimeId, Kind = runtime.Kind, DisplayName = runtime.DisplayName, Synthetic = runtime.Synthetic };
        var agent = KitPresenter.AgentOf(runtime, null, ListedModel);
        yield return ("RuntimeFaces.Head", RuntimeFaces.Head(runtime));
        yield return ("RuntimeFaces.Head of a run", RuntimeFaces.Head(reference));
        yield return ("RuntimeFaces.Line", RuntimeFaces.Line(runtime.Kind));
        yield return ("RuntimeFaces.SecondLine", RuntimeFaces.SecondLine(runtime.Kind));
        yield return ("KitPresenter.AgentOf", agent);
        yield return ("KitMomentWords.AgentChip", KitMomentWords.AgentChip(agent!));
        yield return ("KitMomentWords.HostChip", KitMomentWords.HostChip(KitMomentWords.ThisMac));
        yield return ("KitMomentWords.YourComputer", KitMomentWords.YourComputer);
        yield return ("EntryText.RuntimeName", EntryText.RuntimeName(runtime));
        yield return ("EntryText.RuntimeName live", EntryText.RuntimeName(runtime, live: true));
        yield return ("EntryText.RuntimeNameRuns", EntryText.RuntimeNameRuns(runtime).Text);
        yield return ("EntryText.RuntimeNameRuns live", EntryText.RuntimeNameRuns(runtime, live: true).Text);
    }

    [Test]
    public void NoFaceNamesTheMakerTheModelOrTheMac()
    {
        foreach (var (kind, name, head) in Apps)
        {
            var runtime = Runtime(kind, name);
            Assert.That(RuntimeFaces.Head(runtime), Is.EqualTo(head), kind + " is named by what it is");
            Assert.That(RuntimeFaces.Line(kind), Is.Not.Null.And.Not.Empty, kind + " has a line under its head");
            Assert.That(KitPresenter.AgentOf(runtime, null, ListedModel), Is.EqualTo(head), kind + ": a listed model's id stays on the details");
            Assert.That(KitMomentWords.AgentChip(KitPresenter.AgentOf(runtime, null, ListedModel)!), Is.EqualTo("Agent: " + head));
            foreach (var (face, words) in Faces(runtime))
            {
                if (words == null) continue;
                foreach (var maker in MakersWords)
                    Assert.That(words, Does.Not.Contain(maker).IgnoreCase, kind + "'s " + face + " says \"" + words + "\"");
            }
        }
        Assert.That(KitMomentWords.HostChip(KitMomentWords.ThisMac), Is.EqualTo("Host: This Computer"));
        Assert.That(KitMomentWords.YourComputer, Is.EqualTo("Observed on This Computer"));
    }

    [Test]
    public void ASyntheticAppIsThePracticeRunAndAnUnnamedKindIsACodingAgent()
    {
        var practice = Runtime("codex", "Codex 0.157.0");
        practice.Synthetic = true;
        Assert.That((RuntimeFaces.Head(practice), EntryText.RuntimeName(practice), EntryText.RuntimeNameRuns(practice).Text),
            Is.EqualTo(("Practice Run", "Practice Run", "Practice Run")), "a synthetic runtime is the practice run whatever its kind");
        Assert.That(RuntimeFaces.Head(Runtime("something-new", "Something New 1.0")), Is.EqualTo(RuntimeFaces.Unnamed));
        Assert.That(RuntimeFaces.Head((string?)null), Is.EqualTo("Coding Agent"));
    }

    [Test]
    public void ThePickerKeepsEachAppsOwnName()
    {
        foreach (var (kind, name, _) in Apps)
        {
            var runtime = Runtime(kind, name);
            Assert.That(EntryText.RuntimeOwnNameRuns(runtime).Text, Does.Contain(name), kind + "'s picker choice");
            Assert.That(EntryText.RuntimeOwnNameRuns(runtime).AnyTheirs, Is.True, "the app's own name leans as outside text");
        }
        // Signed-in apps share one head, so the picker is where they are told apart: their keys and their own names differ.
        var signedIn = Apps.Where(app => app.Head == "Signed-In App").Select(app => Runtime(app.Kind, app.Name)).ToList();
        Assert.That(signedIn.Select(RuntimeFaces.Head).Distinct().Count(), Is.EqualTo(1));
        Assert.That(signedIn.Select(runtime => runtime.RuntimeId).Distinct().Count(), Is.EqualTo(signedIn.Count));
        Assert.That(signedIn.Select(runtime => EntryText.RuntimeOwnNameRuns(runtime).Text).Distinct().Count(), Is.EqualTo(signedIn.Count));

        CaptureNames.UseGeneric(true);
        Assert.That(EntryText.RuntimeOwnNameRuns(Runtime("codex", "Codex 0.157.0")).Text, Is.EqualTo(CaptureNames.Agent), "under the capture extra the picker is generic");
    }
}
