using System;
using System.Collections.Generic;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewWorkDraftTests
{
    private static NewWorkDraft Draft() => new(new CommandFactory(new ClientInfo
    {
        Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest",
    })) { ProjectId = Guid.NewGuid().ToString("D"), Objective = "Build the search screen and test its filters." };

    private static RuntimeDescriptor Runtime(string id, ModelChoice choice = ModelChoice.Listed) => new()
    {
        RuntimeId = id,
        DisplayName = id,
        Kind = "mock",
        ModelChoice = choice,
        Capabilities = new RuntimeCapabilities { StartExecution = true },
    };

    private static RuntimeModel Model(string reference, ModelServed served = ModelServed.Unknown) => new()
    {
        ModelRef = reference, DisplayName = reference, Served = served,
        ToolCalling = ModelToolCalling.Unknown, ContextTokens = null,
    };

    [Test]
    public void StartsWithOnlyAModelFromTheChosenRuntimesOwnList()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("opencode"));
        Assert.That(draft.Problem, Is.EqualTo("Reading this runtime's models."));
        var model = Model("ollama/gpt-4o:latest", ModelServed.ThisMac);
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { model } },
        });
        Assert.That(draft.Problem, Is.EqualTo("Choose a model."));
        Assert.Throws<ArgumentException>(() => draft.ChooseModel(Model("hosted/other")));
        draft.ChooseModel(model);
        Assert.That(draft.Problem, Is.Null);
        var workstream = draft.CreateWorkstream();
        Assert.That(workstream.Payload.Title, Is.EqualTo(draft.Objective));
        Assert.That(workstream.Payload.Objective, Is.EqualTo(draft.Objective));
        var start = draft.StartExecution(Guid.NewGuid().ToString("D"));
        Assert.That(start.Payload.ModelRef, Is.EqualTo(model.ModelRef));
        Assert.That(start.Payload.RuntimeId, Is.EqualTo("opencode"));
        Assert.That(start.Payload.Instruction, Is.EqualTo(draft.Objective));
        Assert.That(start.Payload.Options, Is.Empty);
    }

    [Test]
    public void RuntimeChangeAndUnavailableListClearThePreviousChoice()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("opencode"));
        var model = Model("ollama/local");
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { model } },
        });
        draft.ChooseModel(model);
        draft.ChooseRuntime(Runtime("codex"));
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { model } },
        });
        Assert.That(draft.Models, Is.Empty);
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "codex", Result = new UnavailableModels
            {
                Reason = new ErrorInfo { Code = "timeout", Message = "The runtime did not answer." },
            },
        });
        Assert.That(draft.Problem, Does.Contain("did not answer"));
        Assert.Throws<InvalidOperationException>(() => draft.CreateWorkstream());
    }

    [Test]
    public void AListWithoutModelChoiceLeavesSelectionToTheRuntime()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("plain", ModelChoice.None));
        Assert.That(draft.Problem, Is.Null);
        Assert.That(draft.StartExecution(Guid.NewGuid().ToString("D")).Payload.ModelRef, Is.Null);
    }

    [Test]
    public void ObjectiveMustBeTypedAndStaysWithinTheContract()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("plain", ModelChoice.None));
        draft.Objective = "   ";
        Assert.That(draft.Problem, Is.EqualTo("Type an objective."));
        draft.Objective = new string('x', 4001);
        Assert.That(draft.Problem, Does.Contain("4,000"));
        draft.Objective = "Fix <b>search</b>\nwith tests";
        Assert.That(draft.CreateWorkstream().Payload.Title, Is.EqualTo("Fix <b>search</b> with tests"));
        Assert.That(draft.CreateWorkstream().Payload.Objective, Is.EqualTo(draft.Objective));
    }
}
