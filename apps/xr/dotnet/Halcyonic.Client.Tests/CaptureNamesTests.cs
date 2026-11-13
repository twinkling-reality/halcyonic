using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The development build's capture extra for footage free of product names (lead 11-04): under it the headset draws a
/// generic agent app, model and host wherever it would draw theirs; without it, the real names. Where an understanding or a
/// measurement came from is said in the product's own words in every build, with the extra or without it
/// (SiblingNamesTests).
/// </summary>
[TestFixture, NonParallelizable]
public class CaptureNamesTests
{
    [TearDown]
    public void Off() => CaptureNames.UseGeneric(false);

    [Test]
    public void UnderTheExtraNoAgentAppModelHostOrSiblingSourceIsNamed()
    {
        CaptureNames.UseGeneric(true);
        var (state, work, execution, runtime) = Running();

        Assert.That(KitPresenter.AgentOf(runtime, execution), Is.EqualTo(CaptureNames.Agent), "the Agent chip");
        Assert.That(KitPresenter.AgentOf(runtime, execution, "qwen3.6:35b-a3b-nvfp4"), Is.EqualTo(CaptureNames.Model), "the Agent chip's model");
        Assert.That(WorkspacePresentationState.ScreenOf(state.Projects.Values.Single(), state, barOpen: false).Runtime, Is.EqualTo(CaptureNames.Agent),
            "the screen's title chip");
        Assert.That(EntryText.RuntimeName(runtime), Is.EqualTo(CaptureNames.Agent), "Create's agent apps");
        Assert.That(KitMomentWords.ThisMac, Is.EqualTo("This Computer"), "the Host chip");
        Assert.That(KitMomentWords.YourComputer, Is.EqualTo("Observed on This Computer"));

        var words = Words(state, work, execution);
        foreach (var name in new[] { "Codex", "qwen", "Mac", "Salidium", "Seorak" })
            Assert.That(words, Does.Not.Contain(name), "the moments and Activity name " + name);
        Assert.That(words, Does.Contain(CaptureNames.Agent));
    }

    [Test]
    public void WithoutTheExtraTheRealNamesAreDrawn()
    {
        var (state, work, execution, runtime) = Running();

        Assert.That(CaptureNames.Generic, Is.False, "off by default");
        Assert.That(KitPresenter.AgentOf(runtime, execution), Is.EqualTo("Codex"));
        Assert.That(KitPresenter.AgentOf(runtime, execution, "qwen3.6:35b-a3b-nvfp4"), Does.StartWith("qwen3.6"));
        Assert.That(KitMomentWords.ThisMac, Is.EqualTo("This Mac"));
        Assert.That(Words(state, work, execution), Does.Contain("Codex"));
    }

    /// <summary>What the task's moments and Activity say, gathered as text.</summary>
    private static string Words(ClientProjection state, WorkstreamView work, ExecutionView execution)
    {
        var activity = new ActivityLog();
        activity.Record(new[]
        {
            ActivityLogBoundTests.Journaled(1, execution.ExecutionId, "execution.created",
                "{\"runtime\":{\"runtime_id\":\"codex\",\"kind\":\"codex\",\"display_name\":\"Codex\",\"synthetic\":false},\"instruction\":\"Do the work.\",\"directory\":null}"),
            ActivityLogBoundTests.Journaled(2, execution.ExecutionId, "runtime.execution.started", "{\"native_id\":\"session-1\"}"),
        });
        var slot = WorkspacePresentationState.MomentSlotOf(work, state, includeIdleFallback: true);
        return string.Join("\n", activity.For(execution.ExecutionId).Select(entry => entry.Text))
            + "\n" + JsonConvert.SerializeObject(slot, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
    }

    /// <summary>
    /// Under the extra an approval's request names its folder by the folder's own name, never the host path that holds the
    /// home folder's name (DEMO's rehearsal, 2026-11-04); the command itself reads as the agent gave it. The run's Folder
    /// line drops the parent folder too. Without the extra both read as given.
    /// </summary>
    [Test]
    public void UnderTheExtraAnApprovalsFolderIsItsOwnNameNeverTheHomeFolder()
    {
        const string summary = "git -C . commit -am \"Golden fireflies\"\nin /Users/someone/star-catcher";
        var approval = new ApprovalView
        {
            ApprovalId = "a1", Subject = new ToolUseSubject { ToolName = "commandExecution", Summary = summary }, Approvable = true, RequestedAt = Samples.Time,
        };
        var (state, work, _, _) = Running("/Users/someone/star-catcher");
        string Words()
        {
            var workspace = WorkspacePresenter.Present(work, state, new ActivityLog(), true);
            var activity = new ActivityLog();
            activity.Record(new[]
            {
                ActivityLogBoundTests.Journaled(1, "e-1", "runtime.approval.requested",
                    "{\"approval_id\":\"a1\",\"subject\":{\"kind\":\"tool_use\",\"tool_name\":\"commandExecution\",\"summary\":" + Newtonsoft.Json.JsonConvert.ToString(summary) + "},\"complete\":true}"),
            });
            return string.Join("\n", new[] { WorkspaceText.Request(approval) }
                .Concat(WorkspaceText.RunDetails(workspace, System.TimeZoneInfo.Utc).Select(line => line.Line))
                .Concat(activity.For("e-1").Select(entry => entry.Text)));
        }

        Assert.That(Words(), Does.Contain("/Users/someone/star-catcher"), "without the extra, as given");
        CaptureNames.UseGeneric(true);
        var generic = Words();
        Assert.That(generic, Does.Not.Contain("someone").And.Not.Contain("/Users"));
        Assert.That(CaptureNames.Request(summary), Is.EqualTo("git -C . commit -am \"Golden fireflies\"\nin star-catcher"), "the command unchanged");
        Assert.That(WorkspaceText.Request(approval), Does.StartWith("commandExecution: git -C . commit -am \"Golden fireflies\"").And.EndWith("in star-catcher"));
        Assert.That(generic, Does.Contain("Folder · star-catcher"));
    }

    private static (ClientProjection, WorkstreamView, ExecutionView, RuntimeDescriptor) Running(string? directory = null)
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = "codex";
        runtime.Kind = "codex";
        runtime.DisplayName = "Codex";
        runtime.Synthetic = false;
        var execution = Samples.Execution("e-1", "w-1", ExecutionStatus.Running, synthetic: false);
        execution.Runtime = new RuntimeRef { RuntimeId = "codex", Kind = "codex", DisplayName = "Codex", Synthetic = false };
        execution.ModelRef = "qwen3.6:35b-a3b-nvfp4";
        execution.Directory = directory;
        var work = Samples.Workstream("w-1", WorkstreamStatus.Starting, "e-1");
        var snapshot = Samples.Snapshot(2, new[] { work }, new[] { execution });
        snapshot.Runtimes = new List<RuntimeDescriptor> { runtime };
        var state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        return (state, work, execution, runtime);
    }
}
