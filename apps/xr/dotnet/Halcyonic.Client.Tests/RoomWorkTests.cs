using System.Collections.Generic;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The agent's cursor label (DESIGN's BUILD-SPEC-v4 2.3; the lead's ruling of 2026-11-10): a verb from the tool under way,
/// then at most the file's own name; never a count, a command's line, a path or a place inside a file; and no step where
/// the runtime reports none.
/// </summary>
public class RoomWorkTests
{
    private static ToolActivityView Tool(string id, string name, string? title, string startedAt = "2026-11-10T09:00:00.000Z") =>
        new() { ToolCallId = id, ToolName = name, Title = title, StartedAt = startedAt };

    private static ExecutionView Execution(params ToolActivityView[] tools) => new()
    {
        ExecutionId = "execution",
        ActiveTools = new List<ToolActivityView>(tools),
        ToolActivity = tools.Length > 0 ? ExecutionViewToolActivity.Running : ExecutionViewToolActivity.None,
    };

    private static string Label(ExecutionView execution) => RoomWork.Of(execution)!.Words(_ => true);

    [Test]
    public void NothingUnderWayGivesNoStep()
    {
        Assert.That(RoomWork.Of(null), Is.Null);
        Assert.That(RoomWork.Of(Execution()), Is.Null);
        var unknown = Execution();
        unknown.ToolActivity = ExecutionViewToolActivity.Unknown;
        Assert.That(RoomWork.Of(unknown), Is.Null, "a runtime that reports no tools shows no step");
    }

    [Test]
    public void ReadingAndEditingSayTheirVerbAndTheFilesOwnName()
    {
        Assert.That(Label(Execution(Tool("a", "Read", "/Users/someone/game/src/game.js"))), Is.EqualTo("Reading game.js"));
        Assert.That(Label(Execution(Tool("b", "Edit", "src/balloons.js"))), Is.EqualTo("Editing balloons.js"));
        Assert.That(Label(Execution(Tool("c", "write", "C:\\game\\src\\score.js"))), Is.EqualTo("Editing score.js"));
        Assert.That(Label(Execution(Tool("d", "MultiEdit", "README.md"))), Is.EqualTo("Editing README.md"));
        Assert.That(Label(Execution(Tool("e", "read", null))), Is.EqualTo("Reading"), "no title, the verb alone");
    }

    [Test]
    public void AFileChangeNamesItsOneFileAndNeverTwo()
    {
        Assert.That(Label(Execution(Tool("a", "fileChange", "update src/balloons.js"))), Is.EqualTo("Editing balloons.js"));
        Assert.That(Label(Execution(Tool("b", "fileChange", "add assets/golden balloon.png"))), Is.EqualTo("Editing golden balloon.png"));
        Assert.That(Label(Execution(Tool("c", "fileChange", "update src/a.js\nupdate src/b.js"))), Is.EqualTo("Editing"), "several files, the verb alone");
        Assert.That(Label(Execution(Tool("d", "fileChange", "move src/a.js to src/b.js"))), Is.EqualTo("Editing"), "a move names two files");
    }

    [Test]
    public void ACommandNeverShowsItsLine()
    {
        foreach (var tool in new[] { "Bash", "shell", "commandExecution" })
        {
            var step = RoomWork.Of(Execution(Tool("a", tool, "/bin/zsh -lc 'npm test -- --watch=false'")))!;
            Assert.That(step.Verb, Is.EqualTo("Running Command"));
            Assert.That(step.Name, Is.Null);
            Assert.That(step.Words(_ => true), Is.EqualTo("Running Command"));
        }
    }

    [Test]
    public void TestsReadRunningTestsWithNoCountAndNoLabel()
    {
        var execution = Execution(Tool("a", "Bash", "npm test"));
        execution.ActiveTestRun = new TestRunView { TestRunId = "tests-1", Label = "42 tests in 3 files", StartedAt = "2026-11-10T09:00:01.000Z" };
        var step = RoomWork.Of(execution)!;
        Assert.That(step.Words(_ => true), Is.EqualTo("Running Tests"));
        Assert.That(step.Key, Is.EqualTo("tests-1"));
    }

    [Test]
    public void AnyOtherToolReadsTheProductsWordAndNeverItsTitle()
    {
        Assert.That(Label(Execution(Tool("a", "Grep", "balloon"))), Is.EqualTo("Running"));
        Assert.That(Label(Execution(Tool("b", "webSearch", "how to draw a balloon"))), Is.EqualTo("Running"));
        Assert.That(Label(Execution(Tool("c", "mcpToolCall", "server tool"))), Is.EqualTo("Running"));
    }

    [Test]
    public void TheNewestToolIsTheStepAndItsIdIsTheKey()
    {
        var execution = Execution(
            Tool("first", "Read", "src/game.js", "2026-11-10T09:00:00.000Z"),
            Tool("second", "Edit", "src/score.js", "2026-11-10T09:00:02.000Z"),
            Tool("third", "Read", "src/menu.js", "2026-11-10T09:00:01.000Z"));
        var step = RoomWork.Of(execution)!;
        Assert.That(step.Key, Is.EqualTo("second"));
        Assert.That(step.Words(_ => true), Is.EqualTo("Editing score.js"));

        // When it completes, the view drops it, and the step falls to the one still under way.
        execution.ActiveTools.RemoveAt(1);
        Assert.That(RoomWork.Of(execution)!.Key, Is.EqualTo("third"));
    }

    [Test]
    public void ALineThatDoesNotFitWholeIsTheVerbAlone()
    {
        var step = RoomWork.Of(Execution(Tool("a", "Edit", "src/a-very-long-file-name-that-runs-past-the-label.generated.js")))!;
        Assert.That(step.Words(words => words.Length <= 24), Is.EqualTo("Editing"));
        Assert.That(step.Words(_ => true), Is.EqualTo("Editing a-very-long-file-name-that-runs-past-the-label.generated.js"));
    }

    [Test]
    public void AFileNameIsThePathsLastPartReadAsWritten()
    {
        Assert.That(RoomWork.FileName("src/game.js"), Is.EqualTo("game.js"));
        Assert.That(RoomWork.FileName("  src/levels/  "), Is.EqualTo("levels"));
        Assert.That(RoomWork.FileName("game.js"), Is.EqualTo("game.js"));
        Assert.That(RoomWork.FileName("src/my  notes\t.txt"), Is.EqualTo("my  notes .txt"), "spaces as written and a tab as one space, as outside text reads");
        Assert.That(RoomWork.FileName(" \t "), Is.Null);
        Assert.That(RoomWork.FileName("/"), Is.Null);
        Assert.That(RoomWork.FileName("src/a.js\nsrc/b.js"), Is.Null);
    }
}
