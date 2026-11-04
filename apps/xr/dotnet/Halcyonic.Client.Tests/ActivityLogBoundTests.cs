using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What the activity log keeps over a long connection (U6): bounded across tasks, without losing a line the open
/// file or a peek shows.
/// </summary>
public class ActivityLogBoundTests
{
    [Test]
    public void AToolsNameIsHeldOnlyUntilItsCompletionLine()
    {
        var log = new ActivityLog();
        long position = 0;
        var events = new List<StoredEvent>();
        for (var call = 0; call < 1000; call++)
        {
            events.Add(Journaled(++position, "t00", "runtime.tool.started", ToolStarted(call, "bash")));
            events.Add(Journaled(++position, "t00", "runtime.tool.completed", ToolCompleted(call)));
        }
        events.Add(Journaled(++position, "t00", "runtime.tool.started", ToolStarted(1000, "edit")));
        log.Record(events);

        Assert.That(log.OpenToolCalls, Is.EqualTo(1), "only the call still open keeps its name");
        log.Record(new[] { Journaled(++position, "t00", "runtime.tool.completed", ToolCompleted(1000)) });
        Assert.That(log.For("t00").Last().Text, Is.EqualTo("Succeeded · edit · Reported"), "its completion line still names it");
        Assert.That(log.OpenToolCalls, Is.Zero);
    }

    [Test]
    public void AllLinesStayForThe32TasksMostRecentlyRecordedOrReadAndTheLast3ForTheRest()
    {
        var log = new ActivityLog();
        long position = 0;
        for (var task = 0; task < 40; task++)
        {
            log.Record(Rounds(Task(task), 42, ref position));
            // The open file reads its task's lines as the others stream in, which keeps them whole.
            if (task == 20) Assert.That(log.For(Task(0)), Has.Count.EqualTo(200));
        }

        Assert.That(log.For(Task(0)), Has.Count.EqualTo(200), "the task read is kept whole");
        for (var task = 1; task <= 8; task++)
            Assert.That(log.For(Task(task)).Select(entry => entry.Text), Is.EqualTo(new[] { "Succeeded · bash · Reported", Said, "Round Ended" }),
                "the 8 least recently recorded or read keep their last 3 lines");
        for (var task = 9; task < 40; task++) Assert.That(log.For(Task(task)), Has.Count.EqualTo(200));
        Assert.That(Enumerable.Range(0, 40).Sum(task => log.For(Task(task)).Count), Is.EqualTo(32 * 200 + 8 * 3));
        Assert.That(log.OpenToolCalls, Is.Zero);
    }

    [Test]
    public void OpeningATrimmedTasksFileReadsItsHistoryBackWhole()
    {
        var log = new ActivityLog();
        var history = new Dictionary<string, List<StoredEvent>>();
        long position = 0;
        for (var task = 0; task < 40; task++)
        {
            history[Task(task)] = Rounds(Task(task), 42, ref position);
            log.Record(history[Task(task)]);
        }
        Assert.That(log.For(Task(0)), Has.Count.EqualTo(3), "the least recently recorded task keeps its last lines");

        // Opening the file reads the task's history again (WorkspaceDirector.ReadHistory).
        log.Record(history[Task(0)]);
        var whole = new ActivityLog();
        whole.Record(history[Task(0)]);

        Assert.That(log.For(Task(0)).Select(entry => (entry.Position, entry.Text)), Is.EqualTo(whole.For(Task(0)).Select(entry => (entry.Position, entry.Text))));
        Assert.That(log.For(Task(0)), Has.Count.EqualTo(200));
    }

    [Test]
    public void APeekAtATrimmedTaskSaysWhatItDidLastAsAWholeLogWould()
    {
        long position = 0;
        // Its last rounds said nothing, so its last 3 lines are rounds' and what it did last is older.
        var quiet = Rounds(Task(0), 10, ref position);
        foreach (var round in new[] { "r-quiet-1", "r-quiet-2" })
        {
            quiet.Add(Journaled(++position, Task(0), "runtime.turn.started", "{\"turn_id\":\"" + round + "\"}"));
            quiet.Add(Journaled(++position, Task(0), "runtime.turn.completed", "{\"turn_id\":\"" + round + "\"}"));
        }
        var log = new ActivityLog();
        var whole = new ActivityLog();
        log.Record(quiet);
        whole.Record(quiet);
        for (var task = 1; task <= 32; task++) log.Record(Rounds(Task(task), 2, ref position));
        Assert.That(log.For(Task(0)).Select(entry => entry.Text), Is.EqualTo(new[] { Said, "Round Ended", "Round Started", "Round Ended" }),
            "its last 3 lines, and the last that is not a round's");

        var state = new ClientProjection();
        var workstream = Samples.Workstream("w-" + Task(0), WorkstreamStatus.Completed, Task(0));
        state.ApplySnapshot(
            Samples.Snapshot(position, new[] { workstream }, new[] { Samples.Execution(Task(0), workstream.WorkstreamId, ExecutionStatus.Completed) }),
            new StateChanges());
        var peek = PeekCard.Of(WorkspacePresenter.Present(workstream, state, log, live: true));
        var expected = PeekCard.Of(WorkspacePresenter.Present(workstream, state, whole, live: true));

        Assert.That(peek.Reason, Is.EqualTo(expected.Reason));
        Assert.That(peek.Reason, Does.Contain("Still working on the waves"), "what it did last: its message");
    }

    private const string Said = "Still working on the waves.";

    private static string Task(int task) => "t" + task.ToString("00");

    /// <summary>Rounds of one task: started, a tool started and done, the agent's message, ended; 5 lines each.</summary>
    private static List<StoredEvent> Rounds(string executionId, int rounds, ref long position)
    {
        var events = new List<StoredEvent>();
        for (var round = 0; round < rounds; round++)
        {
            var turn = "{\"turn_id\":\"r" + round + "\"}";
            events.Add(Journaled(++position, executionId, "runtime.turn.started", turn));
            events.Add(Journaled(++position, executionId, "runtime.tool.started", ToolStarted(round, "bash")));
            events.Add(Journaled(++position, executionId, "runtime.tool.completed", ToolCompleted(round)));
            events.Add(Journaled(++position, executionId, "runtime.agent_message", "{\"text\":\"" + Said + "\"}"));
            events.Add(Journaled(++position, executionId, "runtime.turn.completed", turn));
        }
        return events;
    }

    private static string ToolStarted(int call, string name) =>
        "{\"tool_call_id\":\"c" + call + "\",\"tool_name\":\"" + name + "\",\"title\":null}";

    private static string ToolCompleted(int call) => "{\"tool_call_id\":\"c" + call + "\",\"outcome\":\"succeeded\"}";

    /// <summary>
    /// A runtime's internal id for a tool reads as a person's word in Activity, Halcyonic's own (JUDGE's frames,
    /// 2026-11-04: "Tool · commandExecution · /bin/zsh ..."); a tool's own name, as "shell" or "edit", stays the agent's.
    /// </summary>
    [TestCase("commandExecution", "Command")]
    [TestCase("fileChange", "File Change")]
    public void ARuntimesInternalToolIdReadsAsAPersonsWord(string id, string word)
    {
        var log = new ActivityLog();
        log.Record(new[]
        {
            Journaled(1, "e1", "runtime.tool.started", "{\"tool_call_id\":\"t1\",\"tool_name\":\"" + id + "\",\"title\":\"make test\"}"),
            Journaled(2, "e1", "runtime.tool.completed", "{\"tool_call_id\":\"t1\",\"outcome\":\"succeeded\"}"),
        });
        var entries = log.For("e1");
        Assert.That(entries.Select(entry => entry.Text), Is.EqualTo(new[] { "Tool · " + word + " · make test", "Succeeded · " + word + " · Reported" }));
        Assert.That(entries[0].Words.Runs.Select(run => (run.Text, run.Theirs)).First(), Is.EqualTo(("Tool · " + word + " · ", false)), "our word, upright");
    }

    internal static StoredEvent Journaled(long position, string executionId, string type, string payload) => new()
    {
        Position = position,
        Event = HalcyonicJson.Deserialize<EventEnvelope>("{\"schema_version\":1,\"event_id\":\"01a0dcf1-6e6c-74af-93ca-" + position.ToString("x12")
            + "\",\"event_type\":\"" + type + "\",\"project_id\":\"" + Samples.ProjectId + "\",\"workstream_id\":\"w-" + executionId + "\","
            + "\"execution_id\":\"" + executionId + "\",\"source\":{\"kind\":\"runtime\",\"runtime_id\":\"mock\"}"
            + ",\"source_native_id\":null,\"sequence\":null,\"occurred_at\":\"2026-11-03T21:00:00.000Z\",\"correlation_id\":null,\"causation_id\":null,"
            + "\"provenance\":{\"epistemic\":\"" + (type == "runtime.agent_message" ? "reported" : "observed") + "\",\"native_type\":null},\"payload\":" + payload
            + ",\"ingested_at\":\"2026-11-03T21:00:00.000Z\"}"),
    };
}
