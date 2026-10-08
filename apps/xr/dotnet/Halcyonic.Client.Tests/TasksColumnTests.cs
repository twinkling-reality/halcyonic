using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A host for a column's tests: a projection, the clocks, a page height and what the column asked of it.</summary>
internal sealed class FakeMenuHost : IMenuHost
{
    public ClientProjection? State { get; set; }

    public bool Connected { get; set; } = true;

    public bool Demonstration { get; set; }

    public double Now { get; set; }

    public DateTimeOffset Clock { get; set; } = new DateTimeOffset(2026, 10, 2, 15, 18, 0, TimeSpan.Zero);

    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public TextSize TextSize { get; set; } = TextSize.Standard;

    public bool VoiceOffered { get; set; }

    public ControlPlaneApi? Api => null;

    public bool KeyboardOffered { get; set; }

    /// <summary>The page height on this stage, beside the menu: four rows' worth unless set.</summary>
    public float Height { get; set; } = MenuPage.Rows(4);

    public List<string> Opened { get; } = new();

    public List<CommandEnvelope> Sent { get; } = new();

    /// <summary>The acknowledgement a send waits for, held by the test; with none, there's no session to send through.</summary>
    public TaskCompletionSource<CommandAckMessage>? Ack { get; set; }

    public Task<CommandAckMessage>? Submit(CommandEnvelope command)
    {
        Sent.Add(command);
        return Ack?.Task;
    }

    public void OpenKeyboard(string text, string prompt, Action<string> done)
    {
    }

    public int RowsOf(string words, float columnDegrees) => 1;

    public int RowsOf(PageLine line, float columnDegrees) => 1;

    public bool FitsHalf(PageLine answer, float columnDegrees) => true;

    public int TitleRows(string subject, float columnDegrees) => 1;

    public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

    public float PageHeight(int subjectRows, bool besideMenu) => Height;

    public void OpenFile(string workstreamId) => Opened.Add("file " + workstreamId);

    public void OpenNewProject(string? projectId, string? projectName) => Opened.Add("new project " + projectId);
}

[TestFixture]
public class TasksColumnTests
{
    private static ClientProjection StateWith(IEnumerable<WorkstreamView> workstreams, bool twoProjects = false)
    {
        var snapshot = Samples.Snapshot(1, workstreams);
        if (twoProjects) snapshot.Projects.Add(new ProjectView { ProjectId = "other", Name = "Docs site", CreatedAt = snapshot.Projects[0].CreatedAt, UpdatedAt = snapshot.Projects[0].UpdatedAt });
        var state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static WorkstreamView Task(string id, WorkstreamStatus status, int minute, AttentionLevel attention = AttentionLevel.None, string? project = null)
    {
        var task = Samples.Workstream(id, status, level: attention);
        task.Title = "Task " + id;
        task.UpdatedAt = $"2026-10-02T09:{minute:00}:00.000Z";
        if (project != null) task.ProjectId = project;
        return task;
    }

    [Test]
    public void WhatWaitsComesFirstThenWhatRunsThenWhatEnded()
    {
        var host = new FakeMenuHost
        {
            State = StateWith(new[]
            {
                Task("done", WorkstreamStatus.Completed, 30),
                Task("running", WorkstreamStatus.Running, 20),
                Task("asks", WorkstreamStatus.WaitingForHuman, 10, AttentionLevel.ActionRequired),
            }),
        };
        var frame = new TasksColumn(host).Frame!;
        Assert.That(frame.Lines.Select(line => line.Key), Is.EqualTo(new[] { "asks", "running", "done" }));
        Assert.That(frame.Subject, Is.EqualTo("1 task is waiting for you"));
        Assert.That(frame.SubjectWaits, Is.True, "its subject takes the waiting colour");
        var waiting = frame.Lines[0];
        Assert.That((waiting.Tone, waiting.Opens, waiting.WordsAreData, waiting.Action), Is.EqualTo((LineTone.Waiting, true, true, TasksColumn.OpenTask)));
        Assert.That(frame.Lines.All(line => line.Fact == null), Is.True, "one project: no fact");
        Assert.That(frame.Sections, Is.Empty, "the director adds the menu's places");
    }

    [Test]
    public void WithMoreThanOneProjectEachRowNamesItsProject()
    {
        var host = new FakeMenuHost
        {
            State = StateWith(new[] { Task("a", WorkstreamStatus.Running, 1), Task("b", WorkstreamStatus.Running, 2, project: "other") }, twoProjects: true),
        };
        var frame = new TasksColumn(host).Frame!;
        Assert.That(frame.Lines.Select(line => (line.Fact, line.FactIsData)), Is.EquivalentTo(new[] { ("Sample", true), ("Docs site", true) }));
        Assert.That(frame.Subject, Is.EqualTo("Nothing is waiting for you."));
        Assert.That(frame.SubjectWaits, Is.False);
    }

    [Test]
    public void APageHoldsTheRowsThatFitBesideAFileOnThisStage()
    {
        var tasks = Enumerable.Range(0, 7).Select(index => Task("t" + index, WorkstreamStatus.Running, index)).ToArray();
        var four = new TasksColumn(new FakeMenuHost { State = StateWith(tasks) });
        Assert.That(four.Rows, Is.EqualTo(4));
        var low = new FakeMenuHost { State = StateWith(tasks), Height = MenuPage.Rows(3) + 0.001f };
        var three = new TasksColumn(low);
        Assert.That(three.Rows, Is.EqualTo(3), "as on a Quest 3S at the far stage");
        Assert.That(three.Frame!.Lines, Has.Count.EqualTo(3));
        Assert.That(new TasksColumn(new FakeMenuHost { State = StateWith(tasks), TextSize = TextSize.Larger }).Rows, Is.EqualTo(3), "never more than a list's page");
        Assert.That(new TasksColumn(new FakeMenuHost { State = StateWith(tasks), Height = 0f }).Rows, Is.EqualTo(1), "at least one row");

        // Text a step larger in Settings: the rows are read again, never more than a page holds at that size.
        var growing = new FakeMenuHost { State = StateWith(tasks) };
        var column = new TasksColumn(growing);
        Assert.That(column.Frame!.Lines, Has.Count.EqualTo(4));
        growing.TextSize = TextSize.Larger;
        // The frame first, as the navigator asks for it, never the rows before it.
        Assert.That(column.Frame!.Lines, Has.Count.EqualTo(3), "read again at the larger text");
        Assert.That(column.Rows, Is.EqualTo(3));
        growing.TextSize = TextSize.Standard;
        Assert.That(column.Rows, Is.EqualTo(4), "and again back at the standard");

        // Paging: Next page, then First page on the last, which goes back.
        Assert.That(three.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next page"));
        three.Act(Footer.NextPage, null);
        three.Act(Footer.NextPage, null);
        Assert.That(three.Frame!.Lines.Select(line => line.Key), Is.EqualTo(new[] { "t0" }));
        Assert.That(three.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("First page"));
        three.Act(Footer.NextPage, null);
        Assert.That(three.Frame!.Lines, Has.Count.EqualTo(3));
    }

    [Test]
    public void PressingARowOpensItsFileAndItsRowStaysChosen()
    {
        var host = new FakeMenuHost { State = StateWith(new[] { Task("a", WorkstreamStatus.Running, 1), Task("b", WorkstreamStatus.Running, 2) }) };
        var tasks = new TasksColumn(host);
        tasks.Act(TasksColumn.OpenTask, "a");
        tasks.Act(TasksColumn.OpenTask, "gone");
        Assert.That(host.Opened, Is.EqualTo(new[] { "file a" }), "only a task on the stage");
        var changes = 0;
        tasks.Changed += () => changes++;
        tasks.Showing("a");
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(tasks.Frame!.Lines.Single(line => line.Chosen).Key, Is.EqualTo("a"));
        tasks.Showing(null);
        Assert.That(tasks.Frame!.Lines.Any(line => line.Chosen), Is.False, "the file closed");
    }

    [Test]
    public void TheBarSaysWhatWaitsAndDotsTasks()
    {
        var waiting = TasksColumn.Bar(MenuPlace.Usage, StateWith(new[] { Task("a", WorkstreamStatus.WaitingForHuman, 1, AttentionLevel.ActionRequired), Task("b", WorkstreamStatus.Running, 2) }));
        Assert.That((waiting.Chosen, waiting.ClosedLine, waiting.Waits(MenuPlace.Tasks), waiting.Waits(MenuPlace.Usage)),
            Is.EqualTo((MenuPlace.Usage, "1 task is waiting for you", true, false)));
        var quiet = TasksColumn.Bar(MenuPlace.Tasks, StateWith(new[] { Task("b", WorkstreamStatus.Running, 2) }));
        Assert.That((quiet.ClosedLine, quiet.Waits(MenuPlace.Tasks)), Is.EqualTo(("Nothing is waiting for you.", false)));
        Assert.That(TasksColumn.Bar(MenuPlace.Tasks, null).ClosedLine, Is.EqualTo("Nothing is waiting for you."), "before the first snapshot");
    }

    [Test]
    public void WhileTheComputerHasNoTaskAtAllTheBarSaysNothingIsRunningYet()
    {
        var none = TasksColumn.Bar(MenuPlace.Tasks, StateWith(Array.Empty<WorkstreamView>()));
        Assert.That((none.ClosedLine, none.Waits(MenuPlace.Tasks)), Is.EqualTo(("Nothing is running yet", false)));
        // A session's state before its first snapshot is empty, not known to be: it claims nothing about the computer's work.
        Assert.That(TasksColumn.Bar(MenuPlace.Tasks, new ClientProjection()).ClosedLine, Is.EqualTo("Nothing is waiting for you."));
    }

    [Test]
    public void ItsCloseClosesTheMenuAndItChangesWhenTheJournalMoves()
    {
        var host = new FakeMenuHost { State = StateWith(Array.Empty<WorkstreamView>()) };
        var tasks = new TasksColumn(host);
        Assert.That(tasks.Frame!.Lines.Single().Words, Is.EqualTo(TasksText.None));
        var closed = false;
        tasks.Closed += () => closed = true;
        tasks.Act(Footer.Close, null);
        Assert.That(closed, Is.True);
        var changes = 0;
        tasks.Changed += () => changes++;
        tasks.Tick();
        tasks.Tick();
        Assert.That(changes, Is.EqualTo(1), "once for the journal as it stands, then only as it moves");
        Assert.That(host.Sent, Is.Empty, "Tasks never sends");
    }
}
