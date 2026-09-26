using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

internal static class Repository
{
    public static readonly string Root = FindRoot();

    public static string PathTo(string relative) => Path.Combine(Root, relative);

    public static string[] Trace() =>
        File.ReadAllLines(PathTo("fixtures/traces/multiple_workstreams.jsonl")).Where(line => line.Length > 0).ToArray();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "pnpm-workspace.yaml")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("The tests must run inside the Halcyonic repository.");
    }
}

internal static class Json
{
    /// <summary>Parses without turning timestamps into dates, so comparisons see the exact wire text.</summary>
    public static JToken Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }

    /// <summary>Reads the JSON strictly, writes it back, and requires the same document.</summary>
    public static T AssertRoundTrips<T>(string json) where T : class
    {
        var value = HalcyonicJson.Deserialize<T>(json, strict: true);
        var written = HalcyonicJson.Serialize(value);
        Assert.That(JToken.DeepEquals(Parse(json), Parse(written)), Is.True, "the round trip changed the document:\n" + json + "\n" + written);
        return value;
    }
}

internal static class Pumping
{
    /// <summary>Pumps the session until the condition holds, as a game loop would each frame.</summary>
    public static async Task<StateChanges> Until(
        RealtimeSession session,
        Func<RealtimeSession, bool> condition,
        string description,
        TimeSpan? timeout = null,
        Func<string>? diagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        var seen = new StateChanges();
        while (true)
        {
            Merge(seen, session.Pump());
            if (condition(session)) return seen;
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting until " + description + ". Status: " + session.Status + "."
                    + (diagnostics == null ? "" : "\n" + diagnostics()));
            }
            await Task.Delay(5);
        }
    }

    private static void Merge(StateChanges into, StateChanges from)
    {
        foreach (var e in from.Events) into.Events.Add(e);
        foreach (var error in from.ServerErrors) into.ServerErrors.Add(error);
        into.Projects.UnionWith(from.Projects);
        into.Workstreams.UnionWith(from.Workstreams);
        into.Executions.UnionWith(from.Executions);
        into.Commands.UnionWith(from.Commands);
    }
}

internal static class Samples
{
    public const string JournalId = "01a0dcf1-5a80-7000-8000-000000000001";
    public const string ProjectId = "01a0dcf1-5a80-7000-8000-0000000000a1";
    public const string Time = "2026-09-26T09:00:00.000Z";

    public static readonly ClientInfo Client = new() { Name = "halcyonic-client-tests", Version = "0.1.0", DeviceLabel = null };

    public static JournalInfo Journal(string id = JournalId, JournalOrigin origin = JournalOrigin.Live) =>
        new() { JournalId = id, Origin = origin };

    public static WorkstreamView Workstream(
        string id,
        WorkstreamStatus status = WorkstreamStatus.Created,
        string? executionId = null,
        AttentionLevel level = AttentionLevel.None,
        params AttentionReason[] reasons) =>
        new()
        {
            WorkstreamId = id,
            ProjectId = ProjectId,
            Title = "Workstream " + id,
            Objective = null,
            Status = status,
            Attention = new Attention { Level = level, Reasons = reasons.ToList() },
            CurrentExecutionId = executionId,
            ExecutionIds = executionId == null ? new List<string>() : new List<string> { executionId },
            CreatedAt = Time,
            UpdatedAt = Time,
        };

    public static ExecutionView Execution(string id, string workstreamId, ExecutionStatus status, bool synthetic = true) =>
        new()
        {
            ExecutionId = id,
            WorkstreamId = workstreamId,
            ProjectId = ProjectId,
            Runtime = new RuntimeRef { RuntimeId = "mock", Kind = "mock", DisplayName = "Mock runtime", Synthetic = synthetic },
            NativeId = null,
            Instruction = "Do the work.",
            Status = status,
            StatusReason = null,
            PendingApprovals = new List<ApprovalView>(),
            ActiveTools = new List<ToolActivityView>(),
            ActiveTestRun = null,
            LastTestRun = null,
            TurnCount = 1,
            CreatedAt = Time,
            StartedAt = Time,
            UpdatedAt = Time,
        };

    public static RuntimeDescriptor MockRuntime() =>
        new()
        {
            RuntimeId = "mock",
            Kind = "mock",
            DisplayName = "Mock runtime",
            Synthetic = true,
            Capabilities = new RuntimeCapabilities
            {
                StartExecution = true,
                InstructAtRest = true,
                InstructWhileRunning = false,
                RespondToApproval = true,
                Interrupt = true,
            },
        };

    public static Snapshot Snapshot(
        long position,
        IEnumerable<WorkstreamView>? workstreams = null,
        IEnumerable<ExecutionView>? executions = null,
        JournalInfo? journal = null) =>
        new()
        {
            Journal = journal ?? Journal(),
            Position = position,
            Projects = new List<ProjectView>
            {
                new() { ProjectId = ProjectId, Name = "Sample", CreatedAt = Time, UpdatedAt = Time },
            },
            Workstreams = (workstreams ?? Array.Empty<WorkstreamView>()).ToList(),
            Executions = (executions ?? Array.Empty<ExecutionView>()).ToList(),
            Commands = new List<CommandView>(),
            Runtimes = new List<RuntimeDescriptor> { MockRuntime() },
        };

    /// <summary>An event message whose envelope comes from the recorded trace.</summary>
    public static EventMessage Event(long position, params WorkstreamView[] workstreams) =>
        new()
        {
            Position = position,
            Event = HalcyonicJson.Deserialize<EventEnvelope>(Repository.Trace()[0]),
            Changes = new EntityChanges
            {
                Projects = new List<ProjectView>(),
                Workstreams = workstreams.ToList(),
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView>(),
            },
        };

    public static WelcomeMessage Welcome(bool resumed, long head, string journalId = JournalId) =>
        new() { Journal = Journal(journalId), Head = head, Resumed = resumed, ServerTime = Time };
}
