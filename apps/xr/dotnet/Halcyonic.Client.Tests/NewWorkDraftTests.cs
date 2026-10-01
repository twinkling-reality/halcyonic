using System;
using System.Collections.Generic;
using System.Linq;
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
        Assert.That(draft.Model, Is.SameAs(model), "the model on this Mac is chosen for the person");
        Assert.That(draft.ModelPreselected, Is.True);
        Assert.Throws<ArgumentException>(() => draft.ChooseModel(Model("hosted/other")));
        Assert.That(draft.ChooseModel(model), Is.True);
        Assert.That(draft.ModelPreselected, Is.False, "now the person's own choice");
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
        var model = Model("ollama/local", ModelServed.ThisMac);
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { model } },
        });
        Assert.That(draft.Model, Is.SameAs(model));
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
        Assert.That(draft.Title, Is.EqualTo("Fix <b>search</b> with tests"));
        Assert.That(draft.CreateWorkstream().Payload.Title, Is.EqualTo("Fix <b>search</b> with tests"));
        Assert.That(draft.CreateWorkstream().Payload.Objective, Is.EqualTo(draft.Objective));
    }

    [Test]
    public void AModelThatRunsElsewhereIsNeverChosenWithoutASecondPress()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("opencode"));
        var hosted = Model("opencode/space-bunny-free", ModelServed.Remote);
        var unknown = Model("gateway/unknown");
        var local = Model("ollama/qwen3.6", ModelServed.ThisMac);
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { hosted, unknown, local } },
        });
        Assert.That(draft.Model, Is.SameAs(local), "the first model on this Mac, though a hosted one is listed first");

        Assert.That(draft.ChooseModel(hosted), Is.False);
        Assert.That(draft.Model, Is.SameAs(local), "one press changes nothing");
        Assert.That(draft.PendingModel, Is.SameAs(hosted));
        Assert.That(draft.ChooseModel(unknown), Is.False, "pressing another model starts over");
        Assert.That(draft.PendingModel, Is.SameAs(unknown));
        Assert.That(draft.ChooseModel(hosted), Is.False);
        Assert.That(draft.ChooseModel(hosted), Is.True, "a second press in a row chooses it");
        Assert.That(draft.Model, Is.SameAs(hosted));
        Assert.That(draft.PendingModel, Is.Null);
        Assert.That(draft.StartExecution(Guid.NewGuid().ToString("D")).Payload.ModelRef, Is.EqualTo("opencode/space-bunny-free"));
    }

    [Test]
    public void NoStartIsBuiltWithoutAModelForARuntimeThatListsThem()
    {
        var draft = Draft();
        draft.ChooseRuntime(Runtime("opencode"));
        var hosted = Model("hosted/only", ModelServed.Remote);
        draft.SetModels(new RuntimeModelsResponse
        {
            RuntimeId = "opencode", Result = new AvailableModels { Models = new List<RuntimeModel> { hosted } },
        });
        Assert.That(draft.Model, Is.Null, "nothing on this Mac, so nothing is chosen");
        Assert.That(draft.Problem, Is.EqualTo("Choose a model."));
        Assert.Throws<InvalidOperationException>(() => draft.StartExecution(Guid.NewGuid().ToString("D")));

        var none = Draft();
        none.ChooseRuntime(Runtime("claude", ModelChoice.None));
        Assert.That(none.StartExecution(Guid.NewGuid().ToString("D")).Payload.ModelRef, Is.Null, "a runtime that lists no models keeps its own choice");
    }

    [Test]
    public void TheMacsModelsComeFirstAndTheBestLocalOneIsChosen()
    {
        // As OpenCode listed them in the fifth headset session: seven hosted models first, then the Mac's.
        RuntimeModel Listed(string reference, ModelServed served, ModelToolCalling tools, int? context) => new()
        {
            ModelRef = reference, DisplayName = reference, Served = served, ToolCalling = tools, ContextTokens = context,
        };
        var listed = new List<RuntimeModel>();
        for (var i = 0; i < 7; i++) listed.Add(Listed("opencode/zen-" + i, ModelServed.Remote, ModelToolCalling.Declared, 200_000));
        listed.Add(Listed("ollama/small:latest", ModelServed.ThisMac, ModelToolCalling.Declared, 32_768));
        listed.Add(Listed("ollama/no-tools:latest", ModelServed.ThisMac, ModelToolCalling.NotDeclared, 1_000_000));
        listed.Add(Listed("gateway/unknown", ModelServed.Unknown, ModelToolCalling.Unknown, null));
        listed.Add(Listed("ollama/qwen3.6:35b-a3b-nvfp4", ModelServed.ThisMac, ModelToolCalling.Declared, 262_144));

        var draft = Draft();
        draft.ChooseRuntime(Runtime("opencode"));
        draft.SetModels(new RuntimeModelsResponse { RuntimeId = "opencode", Result = new AvailableModels { Models = listed } });

        Assert.That(draft.Models.Take(3).Select(model => model.ModelRef),
            Is.EqualTo(new[] { "ollama/small:latest", "ollama/no-tools:latest", "ollama/qwen3.6:35b-a3b-nvfp4" }), "the Mac's, in the runtime's order");
        Assert.That(draft.Elsewhere, Is.EqualTo(3));
        Assert.That(draft.Models.Skip(3).Select(model => model.ModelRef), Has.Member("gateway/unknown").And.Member("opencode/zen-0"));
        Assert.That(draft.Model!.ModelRef, Is.EqualTo("ollama/qwen3.6:35b-a3b-nvfp4"), "tool calling declared, then the larger context");
        Assert.That(draft.ModelPreselected, Is.True);
        Assert.That(EntryText.ElsewhereDivider(draft.Models.Skip(draft.Elsewhere)), Does.StartWith("Not known to run on your Mac"));
        Assert.That(EntryText.ElsewhereDivider(draft.Models.Skip(draft.Elsewhere).Where(model => model.Served == ModelServed.Remote)),
            Is.EqualTo("Runs on a remote service: your code and instructions go there."));
    }
}
