using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The model side of the Usage card's Assistant row (ADR 0030, 2026-11-12): where the assistant runs, the host's list of
/// where it may, and the press that is the host's entry Use Model. Fake keys only; nothing leaves this computer.
/// </summary>
public class AssistantPlaceTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    [Test]
    public void TheRowNamesTheModelAndWhereItRunsOrTheHostsReason()
    {
        var local = AssistantPlace.Row(new AvailableWorkspaceAgent { Model = "local-model:tag" });
        Assert.That((local.Model, local.Provider, local.Reason, local.OnThisComputer), Is.EqualTo(("local-model:tag", (string?)null, (string?)null, true)));
        var hosted = AssistantPlace.Row(new AvailableWorkspaceAgent { Model = "test-model-1", Served = AssistantServed.Remote, Provider = "Test Hosted" });
        Assert.That((hosted.Model, hosted.Provider, hosted.OnThisComputer), Is.EqualTo(("test-model-1", "Test Hosted", false)));
        var unsaid = AssistantPlace.Row(new AvailableWorkspaceAgent { Model = "test-model-1", Served = AssistantServed.Remote });
        Assert.That((unsaid.Model, unsaid.Provider, unsaid.Reason), Is.EqualTo(((string?)null, (string?)null, (string?)null)),
            "a remote assistant that does not say where it runs is not shown as running anywhere");
        var refused = AssistantPlace.Row(new UnavailableWorkspaceAgent
        { Reason = new ErrorInfo { Code = "key_refused", Message = "Test Hosted refused its key. Give it a new key on this computer." } });
        Assert.That((refused.Model, refused.Reason), Is.EqualTo(((string?)null, "Test Hosted refused its key. Give it a new key on this computer.")));
        Assert.That(AssistantPlace.Row(null).Model, Is.Null);
    }

    [Test]
    public void TheOptionsAreTheHostsRowsInItsOrderAndARowThatOffersNothingIsNotChoosable()
    {
        var list = HalcyonicJson.Deserialize<ChoicesResponse>("""
            {"scope":{"project_id":null,"workstream_id":null,"execution_id":null},"list":"assistant_models","position":3,"digest":"d","total":3,"default":null,
             "rows":[{"index":0,"kind":"model","label":"This Computer's Model","detail":"local-model:tag","theirs":false,"actions":["assistant.model.choose"]},
                     {"index":1,"kind":"model","label":"test-model-1","detail":"On Test Hosted","theirs":true,"actions":["assistant.model.choose"]},
                     {"index":2,"kind":"model","label":"test-model-2","detail":"Other Hosted refused its key. Give it a new key on this computer.","theirs":true,"actions":[]}]}
            """);
        var options = AssistantPlace.Options(list);
        Assert.That(options.Select(option => (option.Index, option.Label, option.Theirs, option.Choosable)), Is.EqualTo(new[]
        {
            (0, "This Computer's Model", false, true),
            (1, "test-model-1", true, true),
            (2, "test-model-2", true, false),
        }));
        Assert.That(AssistantPlace.Options(null), Is.Empty);
    }

    [Test]
    public void ThePressIsTheHostsEntryByTheRowsIndexAndTheListsDigest()
    {
        var press = JObject.Parse(HalcyonicJson.Serialize(Commands.UseModel(1, "digest-of-the-read")));
        Assert.That(press["command_type"]!.ToString(), Is.EqualTo("workspace.request.submit"));
        var payload = (JObject)press["payload"]!;
        Assert.That(payload["project_id"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That(payload["text"]!.ToString(), Is.EqualTo("Use Model"));
        Assert.That(JToken.DeepEquals(payload["act"], JObject.Parse(
            """{"command_type":"assistant.model.choose","values":{"model":1},"lists":{"assistant_models":"digest-of-the-read"}}""")), Is.True);
        var confirm = JObject.Parse(HalcyonicJson.Serialize(Commands.ConfirmRequest("r", "s", "rv")));
        Assert.That(JToken.DeepEquals(confirm["payload"], JObject.Parse("""{"request_id":"r","text":null,"confirm":{"step_id":"s","review":"rv"}}""")), Is.True);
    }

    [Test]
    public async Task ThroughARealControlPlaneAStoredKeyOffersItsModelsAndThePressChoosesThem()
    {
        var dataDir = Directory.CreateTempSubdirectory("halcyonic-assistant-place-").FullName;
        try
        {
            // A fake key in the plane's own data folder, and every provider pointed at a closed port on this computer.
            var keyFile = Path.Combine(dataDir, "meta-api-key");
            File.WriteAllText(keyFile, "LLM_fake_client_test_not_real\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var closed = new TcpListener(IPAddress.Loopback, 0);
            closed.Start();
            var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
            closed.Stop();
            var environment = new Dictionary<string, string> { ["HALCYONIC_ASSISTANT_UPSTREAM"] = $"http://127.0.0.1:{closedPort}/v1" };
            using var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, ControlPlaneProcess.FreePort(), environment: environment);
            var baseUri = ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint);
            using var api = new ControlPlaneApi(baseUri, controlPlane.AccessToken);
            using var http = new HttpClient { BaseAddress = baseUri };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);

            // Stored, not chosen: this computer stays the assistant's place, and the key's models are offered.
            var before = AssistantPlace.Row(await api.GetWorkspaceAgentAsync());
            Assert.That(before.Provider, Is.Null);
            var list = await api.GetAssistantModelsAsync();
            var options = AssistantPlace.Options(list);
            Assert.That(options.Select(option => (option.Label, option.Detail)).Skip(1), Is.EqualTo(new[]
            { ("muse-wonder-1.0", "On Meta, Paid by You"), ("muse-spark-1.3", "On Meta, Paid by You") }));
            Assert.That(options[0].Label, Is.EqualTo("This Computer's Model"));

            // The press, then the review card's one press.
            var requestId = (await Post(http, Commands.UseModel(1, list.Digest)))["result"]!["request_id"]!.ToString();
            WorkspaceRequestStep? review = null;
            for (var tries = 0; tries < 100 && review == null; tries++)
            {
                review = AssistantPlace.WaitingReview((await api.ReadWorkspaceRequestAsync(requestId)).Request);
                if (review == null) await Task.Delay(100);
            }
            Assert.That(review, Is.Not.Null, "the press waits for its review card");
            Assert.That(review!.Proposal!.WhatHappens, Does.Contain("charges you money for each request"));
            Assert.That(AssistantPlace.OnePress(review), Is.False, "a hosted choice is reviewed first");
            Assert.That(AssistantPlace.Ended((await api.ReadWorkspaceRequestAsync(requestId)).Request), Is.False);
            await Post(http, Commands.ConfirmRequest(requestId, review.StepId, review.Proposal.Review!));

            // Chosen: the assistant now runs on Meta, here unreachable by design (the closed port), and says so.
            WorkspaceAgentStatus after = await api.GetWorkspaceAgentAsync();
            for (var tries = 0; tries < 50 && after is not UnavailableWorkspaceAgent { Reason.Code: "provider_unreachable" }; tries++)
            {
                await Task.Delay(100);
                after = await api.GetWorkspaceAgentAsync();
            }
            Assert.That(((UnavailableWorkspaceAgent)after).Reason.Code, Is.EqualTo("provider_unreachable"));
            Assert.That(AssistantPlace.Row(after).Reason, Does.Contain("could not be reached"));
            Assert.That(File.ReadAllText(Path.Combine(dataDir, "meta-api-key")).Trim(), Is.EqualTo("LLM_fake_client_test_not_real"));
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Test]
    public void TheHostsOwnOfferIsOnePressAndAPressThatEndedWaitsOnNothing()
    {
        static WorkspaceRequestView View(string status, string? confirm)
        {
            var step = new JObject
            {
                ["step_id"] = "s", ["tool_name"] = "use_this_computer", ["status"] = "waiting", ["command_id"] = null,
                ["command_status"] = null, ["observation_position"] = 0, ["active_turn_id"] = null, ["had_active_turn"] = false,
                ["observation_after"] = 0, ["observed_turn_id"] = null, ["runtime_status"] = null, ["summary"] = "x",
            };
            if (confirm != null)
                step["proposal"] = new JObject
                {
                    ["command_type"] = "assistant.model.choose", ["confirm"] = confirm, ["what_happens"] = "x", ["review"] = "d",
                    ["said"] = new JArray(),
                };
            var view = new JObject
            {
                ["request_id"] = "r", ["journal_id"] = "j", ["project_id"] = null, ["workstream_id"] = null, ["execution_id"] = null,
                ["original_text"] = "Use Model", ["status"] = status, ["reply"] = null,
                ["steps"] = confirm == null ? new JArray() : new JArray(step), ["earlier_steps"] = 0,
                ["created_at"] = Samples.Time, ["updated_at"] = Samples.Time,
            };
            return HalcyonicJson.Deserialize<WorkspaceRequestView>(view.ToString());
        }
        var offer = View("waiting_for_human", "none");
        Assert.That(AssistantPlace.OnePress(AssistantPlace.WaitingReview(offer)), Is.True);
        Assert.That(AssistantPlace.Ended(offer), Is.False);
        Assert.That(AssistantPlace.Ended(View("failed", null)), Is.True, "refused before any review: nothing waits");
        Assert.That(AssistantPlace.Ended(View("running", null)), Is.False, "still preparing: read it again");
    }

    private static async Task<JObject> Post(HttpClient http, CommandEnvelope command)
    {
        using var body = new StringContent(HalcyonicJson.Serialize(command), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("api/commands", body);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        var sent = (JObject)json["command"]!;
        Assert.That(sent["status"]!.ToString(), Is.Not.EqualTo("rejected"), sent.ToString());
        return sent;
    }
}
