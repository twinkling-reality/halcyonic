using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ControlPlaneApiTests
{
    private const string ExecutionId = "01a0dcf1-5a80-7000-8000-0000000000e1";

    /// <summary>
    /// What the control plane serves for a session Seorak has measured but that has not matured: an
    /// estimated cost, an outcome whose unknowns stay null beside a measured zero, and one kind of check.
    /// </summary>
    private const string Available = """
        {
          "execution_id": "01a0dcf1-5a80-7000-8000-0000000000e1",
          "result": {
            "availability": "available",
            "evaluation": {
              "source": { "system": "seorak", "api_version": "v1" },
              "cost": {
                "availability": { "state": "available", "reason": null },
                "coverage": {
                  "requested": { "from": "2026-06-28", "through": "2026-09-26" },
                  "observed": { "from": "2026-09-26", "through": "2026-09-26" },
                  "matched_sessions": 1,
                  "included_sessions": 1,
                  "complete": true,
                  "omissions": []
                },
                "freshness": {
                  "state": "fresh",
                  "generated_at": "2026-09-26T18:00:00.000Z",
                  "data_through": "2026-09-26T17:58:12.000Z",
                  "stale_at": "2026-09-26T18:05:00.000Z"
                },
                "estimated_usd": 1.37,
                "note": "Estimated from token counts at list prices. Not a bill."
              },
              "outcome": {
                "availability": { "state": "partial", "reason": "not_yet_computed" },
                "coverage": {
                  "requested": { "from": "2026-06-28", "through": "2026-09-26" },
                  "observed": null,
                  "matched_sessions": 1,
                  "included_sessions": 0,
                  "complete": false,
                  "omissions": ["projection_pending", "unknown"]
                },
                "freshness": {
                  "state": "revalidating",
                  "generated_at": "2026-09-26T18:00:00.000Z",
                  "data_through": null,
                  "stale_at": "2026-09-26T18:05:00.000Z"
                },
                "measure": {
                  "commits_landed": null,
                  "uncommitted": null,
                  "line_survival": null,
                  "error_count": 0,
                  "first_error_at": null,
                  "end_reason": "prompt_input_exit"
                }
              },
              "verification": {
                "availability": { "state": "available", "reason": null },
                "coverage": {
                  "requested": { "from": "2026-06-28", "through": "2026-09-26" },
                  "observed": { "from": "2026-09-26", "through": "2026-09-26" },
                  "matched_sessions": 1,
                  "included_sessions": 1,
                  "complete": true,
                  "omissions": []
                },
                "freshness": {
                  "state": "fresh",
                  "generated_at": "2026-09-26T18:00:00.000Z",
                  "data_through": "2026-09-26T17:58:12.000Z",
                  "stale_at": "2026-09-26T18:05:00.000Z"
                },
                "lens": {
                  "by_kind": [{ "label": "test", "runs": 5, "passed": 4, "pass_rate": 0.8 }],
                  "empty_reason": null
                }
              }
            }
          }
        }
        """;

    /// <summary>Answers every request with one canned response and records what was asked.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string body;

        public CannedHandler(HttpStatusCode status, string body)
        {
            this.status = status;
            this.body = body;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ControlPlaneApi Api(CannedHandler handler) =>
        new(new Uri("http://127.0.0.1:47800/"), "test-token", handler);

    [Test]
    public async Task ReadsAnEvaluationKeepingEveryUnknownAsNull()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Available);
        using var api = Api(handler);
        var response = await api.GetEvaluationAsync(ExecutionId);

        var request = handler.Requests.Single();
        Assert.That(request.RequestUri, Is.EqualTo(new Uri("http://127.0.0.1:47800/api/executions/" + ExecutionId + "/evaluation")));
        Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer test-token"));

        Assert.That(response.Result, Is.TypeOf<AvailableEvaluation>());
        var evaluation = ((AvailableEvaluation)response.Result).Evaluation;
        Assert.That(evaluation.Source.System, Is.EqualTo("seorak"));
        Assert.That(evaluation.Cost.EstimatedUsd, Is.EqualTo(1.37));
        Assert.That(evaluation.Cost.Note, Is.EqualTo("Estimated from token counts at list prices. Not a bill."));
        Assert.That(evaluation.Cost.Freshness.StaleAt, Is.EqualTo("2026-09-26T18:05:00.000Z"));

        Assert.That(evaluation.Outcome.Availability.Reason, Is.EqualTo(EvaluationAvailabilityReason.NotYetComputed));
        Assert.That(
            evaluation.Outcome.Coverage.Omissions,
            Is.EqualTo(new[] { EvaluationCoverageOmission.ProjectionPending, EvaluationCoverageOmission.Unknown }),
            "an omission Seorak added after the contract arrives as unknown");
        Assert.That(evaluation.Outcome.Freshness.State, Is.EqualTo(EvaluationFreshnessState.Revalidating));
        var measure = evaluation.Outcome.Measure!;
        Assert.That(measure.CommitsLanded, Is.Null);
        Assert.That(measure.LineSurvival, Is.Null);
        Assert.That(measure.ErrorCount, Is.EqualTo(0));
        Assert.That(measure.EndReason, Is.EqualTo(EvaluationEndReason.PromptInputExit));

        var tests = evaluation.Verification.Lens!.ByKind.Single();
        Assert.That(tests.Label, Is.EqualTo("test"));
        Assert.That(tests.Runs, Is.EqualTo(5));
        Assert.That(tests.Passed, Is.EqualTo(4));
        Assert.That(tests.PassRate, Is.EqualTo(0.8));

        Json.AssertRoundTrips<EvaluationResponse>(Available);
    }

    [Test]
    public async Task ReadsWhyThereIsNoEvaluation()
    {
        var body = "{\"execution_id\":\"" + ExecutionId + "\",\"result\":{\"availability\":\"unauthorized\","
            + "\"reason\":{\"code\":\"credential_missing\",\"message\":\"No Seorak credential is configured.\"}}}";
        using var api = Api(new CannedHandler(HttpStatusCode.OK, body));
        var response = await api.GetEvaluationAsync(ExecutionId);
        Assert.That(response.Result, Is.TypeOf<UnauthorizedEvaluation>());
        Assert.That(((UnauthorizedEvaluation)response.Result).Reason.Code, Is.EqualTo("credential_missing"));
        Json.AssertRoundTrips<EvaluationResponse>(body);
    }

    [Test]
    public void ReportsARefusalWithTheControlPlanesReason()
    {
        var body = "{\"error\":{\"code\":\"execution_not_found\",\"message\":\"Execution " + ExecutionId
            + " does not exist.\",\"issues\":[]}}";
        using var api = Api(new CannedHandler(HttpStatusCode.NotFound, body));
        var error = Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetEvaluationAsync(ExecutionId));
        Assert.That(error!.Message, Does.EndWith("Execution " + ExecutionId + " does not exist."));
    }
}
