using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

internal static class Intelligence
{
    public const string ExecutionId = "01a0dcf1-5a80-7000-8000-0000000000e1";

    /// <summary>
    /// What the control plane serves for Salidium's retained "verified" session, mapped as
    /// packages/integrations/salidium maps it, from a Salidium 0.6.0 daemon: a real source's answer.
    /// </summary>
    public const string Verified = """
        {
          "execution_id": "01a0dcf1-5a80-7000-8000-0000000000e1",
          "result": {
            "availability": "available",
            "understanding": {
              "source": {
                "system": "salidium",
                "synthetic": false,
                "version": "0.6.0",
                "contract": {
                  "name": "salidium.consumer",
                  "major": 1,
                  "minor": 0
                },
                "instance_id": "5f0e2b7c9a1d4e3f8b6a0c2d4e6f8a1b",
                "generated_at": "2026-09-20T16:20:00.000Z",
                "evidence_sequence": 22
              },
              "verdict": {
                "headline": "4 files changed, unverified",
                "tone": "attention",
                "because": "1 file changed after the last passing check (118/118 tests passed (vitest)).",
                "at": "2026-09-20T15:40:50.000Z",
                "epistemic": "observed"
              },
              "latest_statement": {
                "text": "Fixed the double charge with one idempotency key per order.",
                "author": "agent",
                "at": "2026-09-20T15:41:10.000Z",
                "epistemic": "reported"
              },
              "waiting": null,
              "changes": {
                "summary": "4 files changed (+43 −6) · 2 commands",
                "files": [
                  {
                    "path": "src/payments/refunds.ts",
                    "change_count": 1,
                    "lines_added": 5,
                    "lines_removed": 1,
                    "kinds": [
                      "update"
                    ],
                    "last_changed_at": "2026-09-20T15:41:00.000Z",
                    "coverage": {
                      "verified_after": false,
                      "by": null,
                      "epistemic": "inferred"
                    },
                    "reason": {
                      "text": "I will add an idempotency key in ChargeService so a retried charge returns the first one.",
                      "author": "agent",
                      "at": "2026-09-20T15:40:15.000Z",
                      "epistemic": "reported"
                    }
                  },
                  {
                    "path": "src/payments/ChargeService.test.ts",
                    "change_count": 1,
                    "lines_added": 6,
                    "lines_removed": 0,
                    "kinds": [
                      "update"
                    ],
                    "last_changed_at": "2026-09-20T15:40:40.000Z",
                    "coverage": {
                      "verified_after": true,
                      "by": "118/118 tests passed (vitest)",
                      "epistemic": "inferred"
                    },
                    "reason": null
                  },
                  {
                    "path": "src/checkout/RetryWorker.ts",
                    "change_count": 1,
                    "lines_added": 8,
                    "lines_removed": 2,
                    "kinds": [
                      "update"
                    ],
                    "last_changed_at": "2026-09-20T15:40:30.000Z",
                    "coverage": {
                      "verified_after": true,
                      "by": "118/118 tests passed (vitest)",
                      "epistemic": "inferred"
                    },
                    "reason": {
                      "text": "I will add an idempotency key in ChargeService so a retried charge returns the first one.",
                      "author": "agent",
                      "at": "2026-09-20T15:40:15.000Z",
                      "epistemic": "reported"
                    }
                  },
                  {
                    "path": "src/payments/ChargeService.ts",
                    "change_count": 1,
                    "lines_added": 24,
                    "lines_removed": 3,
                    "kinds": [
                      "update"
                    ],
                    "last_changed_at": "2026-09-20T15:40:25.000Z",
                    "coverage": {
                      "verified_after": true,
                      "by": "118/118 tests passed (vitest)",
                      "epistemic": "inferred"
                    },
                    "reason": {
                      "text": "I will add an idempotency key in ChargeService so a retried charge returns the first one.",
                      "author": "agent",
                      "at": "2026-09-20T15:40:15.000Z",
                      "epistemic": "reported"
                    }
                  }
                ],
                "commits": []
              },
              "verification": {
                "summary": "118/118 tests passed (vitest) (before latest changes)",
                "latest_by_method": [
                  {
                    "method": "test",
                    "runner": "vitest",
                    "label": "118/118 tests passed (vitest)",
                    "outcome": "pass",
                    "at": "2026-09-20T15:40:50.000Z",
                    "scope": "unknown",
                    "counts": {
                      "passed": 118,
                      "failed": null,
                      "skipped": null,
                      "total": 118
                    },
                    "exit": {
                      "code": 0,
                      "observation": "explicit"
                    },
                    "caveats": [],
                    "stale": true,
                    "later_unreadable": 0,
                    "epistemic": "observed"
                  }
                ],
                "unverified_files": [
                  "src/payments/refunds.ts"
                ],
                "statements": [
                  {
                    "text": "All tests pass. The staging key ghp_[GITHUB_TOKEN#1] was never used by the fix.",
                    "author": "agent",
                    "at": "2026-09-20T15:40:55.000Z",
                    "epistemic": "reported"
                  }
                ]
              },
              "review": {
                "summary": "2 items need attention",
                "open": 2,
                "resolved": 0,
                "groups": [
                  {
                    "rule": "destructive:rm-rf",
                    "label": "Recursive force delete",
                    "severity": "medium",
                    "occurrences": 1,
                    "latest_at": "2026-09-20T15:41:02.000Z",
                    "items": [
                      {
                        "label": "Recursive force delete",
                        "instance": "rm -rf node_modules/.cache",
                        "created_at": "2026-09-20T15:41:02.000Z",
                        "repeats": 1,
                        "epistemic": "observed"
                      }
                    ]
                  },
                  {
                    "rule": "changes-unverified",
                    "label": "1 file changed since the last passing check",
                    "severity": "low",
                    "occurrences": 1,
                    "latest_at": "2026-09-20T15:41:10.000Z",
                    "items": [
                      {
                        "label": "1 file changed since the last passing check",
                        "instance": null,
                        "created_at": "2026-09-20T15:41:10.000Z",
                        "repeats": 1,
                        "epistemic": "inferred"
                      }
                    ]
                  }
                ]
              },
              "remaining": {
                "summary": "1 of 3 steps remaining",
                "items": [
                  {
                    "text": "Document refund behaviour for support",
                    "status": "pending",
                    "source": "plan",
                    "epistemic": "planned"
                  }
                ]
              },
              "explanation": {
                "status": "generated",
                "current": true,
                "based_on_sequence": 21,
                "generated_at": "2026-09-20T15:41:20.000Z",
                "model": "claude-opus-5",
                "epistemic": "explained",
                "content": {
                  "what": {
                    "summary": "Some customers were charged twice when checkout retried a payment.",
                    "currently": null
                  },
                  "why": {
                    "summary": "Two paths could charge the same order, and neither checked the other.",
                    "lanes": [
                      {
                        "title": "Checkout request",
                        "steps": [
                          "Times out, then retries",
                          "Creates a charge"
                        ]
                      },
                      {
                        "title": "Retry worker",
                        "steps": [
                          "Picks the same order up",
                          "Creates another charge"
                        ]
                      }
                    ],
                    "chain": [
                      "Two charges for one order",
                      "The card is billed twice"
                    ]
                  },
                  "how": {
                    "summary": "One idempotency key per order, sent with every charge.",
                    "root": "ChargeService.ts",
                    "steps": [
                      "Derive a key per order",
                      "Send it with the charge",
                      "The worker reuses it"
                    ]
                  },
                  "approach_change": null
                }
              }
            }
          }
        }
        """;

    public static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    public static UnderstandingResponse Understanding(string json) => HalcyonicJson.Deserialize<UnderstandingResponse>(json, strict: true);

    public static EvaluationResponse Evaluation(string json) => HalcyonicJson.Deserialize<EvaluationResponse>(json, strict: true);

    public static string Edit(string json, Action<JObject> edit)
    {
        var document = (JObject)Json.Parse(json);
        edit(document);
        return document.ToString(Formatting.None);
    }

    public static JObject UnderstandingOf(JObject response) => (JObject)response["result"]!["understanding"]!;

    public static JObject EvaluationOf(JObject response) => (JObject)response["result"]!["evaluation"]!;

    public static DateTimeOffset At(string timestamp) => DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture);

    public static IntelligenceRead<T> Live<T>(T response, string readAt) where T : class => new(response, At(readAt), recorded: false);

    public static string Failure(string availability, string code, string message) =>
        "{\"execution_id\":\"" + ExecutionId + "\",\"result\":{\"availability\":\"" + availability + "\",\"reason\":{\"code\":\"" + code
        + "\",\"message\":\"" + message + "\"}}}";

    public static string[] Texts(SectionPresentation section) => section.Lines.Select(line => line.Text).ToArray();

    /// <summary>The line whose text starts so, which must be there.</summary>
    public static SectionLine Line(SectionPresentation section, string start) =>
        section.Lines.Single(line => line.Text.StartsWith(start, StringComparison.Ordinal));
}

public class UnderstandingPresenterTests
{
    private static SectionPresentation Present(string json, string now = "2026-09-20T16:22:00.000Z", int maxLines = 20) =>
        UnderstandingPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"),
            loading: false, error: null, Intelligence.At(now), Intelligence.Utc, maxLines);

    [Test]
    public void ShowsTheSourcesConclusionsEachWithItsClassAndSaysWhereTheyCameFrom()
    {
        var section = Present(Intelligence.Verified);

        Assert.That(section.Title, Is.EqualTo("Understanding"));
        Assert.That(section.Provenance, Is.EqualTo("From Salidium 0.6.0, 2 minutes ago"));
        Assert.That(section.Simulated, Is.False);
        Assert.That(section.Lines.Select(line => (line.Tag, line.Text)), Is.EqualTo(new[]
        {
            ("observed", "4 files changed, unverified. 1 file changed after the last passing check (118/118 tests passed (vitest))."),
            ("reported", "Agent says: “Fixed the double charge with one idempotency key per order.”"),
            ("observed", "4 files changed (+43 −6) · 2 commands"),
            ("inferred", "1 file not checked after the last change: refunds.ts"),
            ("observed", "Tests passed: 118/118 tests passed (vitest); files changed since"),
            ("observed", "2 items need attention: Recursive force delete; 1 file changed since the last passing check"),
            ("explained", "Explanation: One idempotency key per order, sent with every charge."),
            ("planned", "Planned, not done: Document refund behaviour for support"),
        }));
        Assert.That(Intelligence.Line(section, "Agent says").Tone, Is.EqualTo(SectionTone.Claim), "the agent's words are a quote, shown apart from facts");
        Assert.That(Intelligence.Line(section, "Explanation").Tone, Is.EqualTo(SectionTone.Claim), "an explanation is never evidence");
        Assert.That(Intelligence.Line(section, "4 files changed, unverified").Tone, Is.EqualTo(SectionTone.Attention));
        Assert.That(Intelligence.Line(section, "Tests passed").Tone, Is.EqualTo(SectionTone.Good));
    }

    [Test]
    public void EveryClassIsTheSourcesOwnAndNothingIsUpgraded()
    {
        var classes = new[] { "observed", "reported", "inferred", "planned", "explained" };
        foreach (var word in classes)
        {
            var json = Intelligence.Edit(Intelligence.Verified, response =>
            {
                var understanding = Intelligence.UnderstandingOf(response);
                understanding["verdict"]!["epistemic"] = word;
                understanding["latest_statement"]!["epistemic"] = word;
                understanding["verification"]!["latest_by_method"]![0]!["epistemic"] = word;
                understanding["remaining"]!["items"]![0]!["epistemic"] = word;
            });
            var section = Present(json);
            Assert.That(Intelligence.Line(section, "4 files changed, unverified").Tag, Is.EqualTo(word));
            Assert.That(Intelligence.Line(section, "Agent says").Tag, Is.EqualTo(word), "shown as the source classed it, reported or not");
            Assert.That(Intelligence.Line(section, "Tests passed").Tag, Is.EqualTo(word));
            Assert.That(Intelligence.Line(section, "Planned").Tag, Is.EqualTo(word));
        }
        Assert.That(Present(Intelligence.Verified).Lines.All(line => line.Tag.Length == 0 || classes.Contains(line.Tag)), Is.True);
    }

    [Test]
    public void KeepsTheMostImportantLinesWhenTheyDoNotAllFitInReadingOrder()
    {
        var section = Present(Intelligence.Verified, maxLines: 3);
        Assert.That(Intelligence.Texts(section).Select(text => text.Split(':')[0]), Is.EqualTo(new[]
        {
            "4 files changed, unverified. 1 file changed after the last passing check (118/118 tests passed (vitest)).",
            "Agent says",
            "Tests passed",
        }));
        Assert.That(Present(Intelligence.Verified, maxLines: 0).Lines, Is.Empty);
    }

    [Test]
    public void WhatTheSourceDoesNotHaveIsLeftOutOrSaidToBeUnknownNeverBlank()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var understanding = Intelligence.UnderstandingOf(response);
            understanding["verdict"]!["because"] = null;
            understanding["latest_statement"] = null;
            var run = understanding["verification"]!["latest_by_method"]![0]!;
            run["outcome"] = "unknown";
            run["counts"] = null;
            run["stale"] = false;
            run["later_unreadable"] = 2;
            understanding["explanation"] = JObject.Parse(
                "{\"status\":\"none\",\"current\":false,\"based_on_sequence\":null,\"generated_at\":null,\"model\":null,\"epistemic\":\"explained\",\"content\":null}");
        });
        var section = Present(json);
        Assert.That(Intelligence.Texts(section)[0], Is.EqualTo("4 files changed, unverified"));
        Assert.That(Intelligence.Texts(section).Any(text => text.StartsWith("Agent says", StringComparison.Ordinal)), Is.False);
        Assert.That(Intelligence.Texts(section).Any(text => text.StartsWith("Explanation", StringComparison.Ordinal)), Is.False);
        var unknown = Intelligence.Line(section, "Tests outcome unknown");
        Assert.That(unknown.Text, Is.EqualTo("Tests outcome unknown: 118/118 tests passed (vitest); 2 later runs unreadable"));
        Assert.That(unknown.Tone, Is.EqualTo(SectionTone.Secondary));

        var noChecks = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var verification = (JObject)Intelligence.UnderstandingOf(response)["verification"]!;
            verification["latest_by_method"] = new JArray();
            verification["summary"] = "No checks ran";
        });
        var line = Intelligence.Line(Present(noChecks), "No checks ran");
        Assert.That(line.Tag, Is.Empty, "the source's summary carries no class of its own");
    }

    [Test]
    public void WhatTheSourceSaysTwiceIsShownOnce()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var understanding = Intelligence.UnderstandingOf(response);
            var run = understanding["verification"]!["latest_by_method"]![0]!;
            run["outcome"] = "fail";
            run["label"] = "3 of 118 tests failed (vitest)";
            understanding["verdict"]!["headline"] = "3 tests failing";
            understanding["verdict"]!["because"] = "3 of 118 tests failed (vitest)";
            understanding["remaining"]!["items"] = JArray.Parse(
                "[{\"text\":\"3 of 118 tests failed (vitest)\",\"status\":\"failing\",\"source\":\"verification\",\"epistemic\":\"observed\"},"
                + "{\"text\":\"Document refund behaviour for support\",\"status\":\"pending\",\"source\":\"plan\",\"epistemic\":\"planned\"}]");
        });
        var texts = Intelligence.Texts(Present(json));
        Assert.That(texts[0], Is.EqualTo("3 tests failing"), "the reason is the run's own label, said on the run's line");
        Assert.That(texts.Count(text => text.Contains("3 of 118 tests failed", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(texts, Does.Contain("Planned, not done: Document refund behaviour for support"));

        var waiting = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var understanding = Intelligence.UnderstandingOf(response);
            understanding["verdict"]!["headline"] = "Waiting for you";
            understanding["verdict"]!["because"] = "Run: git push origin main";
            understanding["waiting"] = JObject.Parse(
                "{\"kind\":\"permission\",\"summary\":\"Run: git push origin main\",\"since\":\"2026-09-20T16:05:25.000Z\",\"epistemic\":\"observed\"}");
        });
        var shown = Intelligence.Texts(Present(waiting));
        Assert.That(shown[0], Is.EqualTo("Waiting for you. Run: git push origin main"));
        Assert.That(shown.Any(text => text.StartsWith("Waiting for permission", StringComparison.Ordinal)), Is.False);
        var question = Intelligence.Edit(waiting, response => Intelligence.UnderstandingOf(response)["waiting"]!["kind"] = "question");
        Assert.That(Intelligence.Texts(Present(Intelligence.Edit(question, response =>
            Intelligence.UnderstandingOf(response)["waiting"]!["summary"] = "Which branch?"))), Does.Contain("Waiting for an answer: Which branch?"));
    }

    [Test]
    public void AnExplanationThatIsNotCurrentSaysSoAndOneBeingWrittenIsNotShownAsContent()
    {
        var older = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["explanation"]!["current"] = false);
        Assert.That(Intelligence.Line(Present(older), "Explanation").Text, Does.EndWith("(from before the latest evidence)"));
        var writing = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var explanation = Intelligence.UnderstandingOf(response)["explanation"]!;
            explanation["status"] = "generating";
            explanation["content"] = null;
        });
        var line = Intelligence.Line(Present(writing), "An explanation is being written");
        Assert.That(line.Tag, Is.EqualTo("explained"));
    }

    [Test]
    public void EachAvailabilityIsSaidInWords()
    {
        var cases = new Dictionary<string, (string Json, string Provenance)>
        {
            ["not_found"] = (Intelligence.Failure("not_found", "not_observed", "Salidium has not observed this session."),
                "No understanding yet: Salidium has not observed this session."),
            ["unavailable"] = (Intelligence.Failure("unavailable", "not_running", "Salidium is not running: it has not published its discovery file."),
                "Understanding unavailable: Salidium is not running: it has not published its discovery file."),
            ["incompatible"] = (Intelligence.Failure("incompatible", "invalid_document", "The session report does not match Salidium consumer contract v1."),
                "Understanding unreadable: The session report does not match Salidium consumer contract v1."),
            ["unauthorized"] = (Intelligence.Failure("unauthorized", "credential_missing", "No Salidium credential is configured."),
                "Understanding not allowed: No Salidium credential is configured."),
        };
        foreach (var (availability, (json, provenance)) in cases)
        {
            var section = Present(json);
            Assert.That(section.Provenance, Is.EqualTo(provenance), availability);
            Assert.That(section.Lines, Is.Empty, availability);
            Assert.That(section.Simulated, Is.False);
        }
    }

    [Test]
    public void ReadingAndFailedReadsAreSaidInWords()
    {
        var now = Intelligence.At("2026-09-20T16:22:00.000Z");
        Assert.That(UnderstandingPresenter.Present(null, null, false, null, now, Intelligence.Utc, 7).Provenance,
            Is.EqualTo("Nothing to understand until work starts."));
        Assert.That(UnderstandingPresenter.Present(Intelligence.ExecutionId, null, true, null, now, Intelligence.Utc, 7).Provenance,
            Is.EqualTo("Asking what the understanding source concluded…"));
        var failed = UnderstandingPresenter.Present(Intelligence.ExecutionId, null, false, "The control plane could not be reached: refused", now, Intelligence.Utc, 7);
        Assert.That(failed.Provenance, Is.EqualTo("Could not read the understanding: The control plane could not be reached: refused"));
        Assert.That(failed.ProvenanceTone, Is.EqualTo(SectionTone.Problem));

        var read = Intelligence.Live(Intelligence.Understanding(Intelligence.Verified), "2026-09-20T16:21:30.000Z");
        Assert.That(UnderstandingPresenter.Present(Intelligence.ExecutionId, read, true, null, now, Intelligence.Utc, 7).Provenance,
            Is.EqualTo("From Salidium 0.6.0, 2 minutes ago · reading again…"), "the last answer stays while a new one is read");
        Assert.That(UnderstandingPresenter.Present(Intelligence.ExecutionId, read, false, "timed out", now, Intelligence.Utc, 7).Provenance,
            Is.EqualTo("From Salidium 0.6.0, 2 minutes ago · could not read it again: timed out"));
    }

    [Test]
    public void HowLongAgoIsSaidInWords()
    {
        string Ago(string now) => Present(Intelligence.Verified, now).Provenance;
        Assert.That(Ago("2026-09-20T16:20:40.000Z"), Does.EndWith(", just now"));
        Assert.That(Ago("2026-09-20T16:19:00.000Z"), Does.EndWith(", just now"), "a source clock ahead of this one is not in the future");
        Assert.That(Ago("2026-09-20T16:21:05.000Z"), Does.EndWith(", 1 minute ago"));
        Assert.That(Ago("2026-09-20T19:30:00.000Z"), Does.EndWith(", 3 hours ago"));
        Assert.That(Ago("2026-09-23T09:00:00.000Z"), Does.EndWith(", on 20 Sep at 16:20"));
    }

    [Test]
    public void ASimulatedAnswerSaysSoAndWhenItWasRecorded()
    {
        var recording = Demonstration.Recording();
        var directed = recording.Understanding.Keys.Single(id => recording.Evaluation[id].Count > 20);
        var approval = recording.Nodes[0].Events.Count;
        var answer = recording.UnderstandingAt(directed, 0, approval)!;
        var read = new IntelligenceRead<UnderstandingResponse>(answer.Response, answer.ReadAt, recorded: true);

        var section = UnderstandingPresenter.Present(directed, read, false, null, Intelligence.At("2026-11-20T10:00:00.000Z"), Intelligence.Utc, 7);

        Assert.That(section.Provenance, Is.EqualTo("Simulated, not from Salidium · recorded at 09:00:08"));
        Assert.That(section.Simulated, Is.True);
        Assert.That(section.ProvenanceTone, Is.EqualTo(SectionTone.Attention));
        Assert.That(Intelligence.Texts(section)[0], Is.EqualTo("Waiting for you. Run make migrate to add the sign-in attempts table to the development database"));
        Assert.That(section.Lines[0].Tag, Is.EqualTo("observed"));
    }

    [Test]
    public void TextFromTheSourceIsShownAsItIsWrittenAndNeverAsMarkupOrControl()
    {
        var hostile = "<color=#f00>Done</color>\u202E <sprite=0>\u200B\nnext\tline \\u003Cb\\u003E";
        var json = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["verdict"]!["headline"] = hostile);
        var text = Intelligence.Texts(Present(json))[0];
        Assert.That(text, Does.StartWith("<color=#f00>Done</color>‹U+202E› <sprite=0>‹U+200B› next line \\u003Cb\\u003E."), "markup characters stay as written, and what would not show as itself shows its code");
        Assert.That(text.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format), Is.False);
    }
}

public class EvaluationPresenterTests
{
    private static SectionPresentation Present(string json, string now = "2026-09-26T18:02:00.000Z", bool recorded = false) =>
        EvaluationPresenter.Present(Intelligence.ExecutionId,
            new IntelligenceRead<EvaluationResponse>(Intelligence.Evaluation(json), Intelligence.At("2026-09-26T18:01:00.000Z"), recorded),
            loading: false, error: null, Intelligence.At(now), Intelligence.Utc);

    [Test]
    public void ShowsEachPartWithItsOwnAvailabilityCoverageAndFreshnessNeverCombined()
    {
        var section = Present(ControlPlaneApiTests.Available);

        Assert.That(section.Title, Is.EqualTo("Evaluation"));
        Assert.That(section.Provenance, Is.EqualTo("From Seorak, read 1 minute ago"));
        Assert.That(section.Lines.Select(line => (line.Tag, line.Text, line.Detail)), Is.EqualTo(new[]
        {
            ("Cost", "About $1.37. Estimated from token counts at list prices. Not a bill.", false),
            ("", "available · 1 of 1 session, complete · fresh, data to 17:58:12", true),
            ("Outcome", "commits landed unknown · no tool errors · ended: the person exited", false),
            ("", "uncommitted: known once it ends · 3-day line survival: pending", false),
            ("", "partly available: not yet computed · 0 of 1 session, incomplete: still being computed, a gap not named · recomputing, no data yet", true),
            ("Checks", "test: 4 passed of 5 runs (80%)", false),
            ("", "available · 1 of 1 session, complete · fresh, data to 17:58:12", true),
        }));
        Assert.That(section.Lines.Count(line => line.Detail), Is.EqualTo(3), "one statement per part");
        Assert.That(section.Lines.Where(line => line.Detail).Select(line => line.Tone),
            Is.EqualTo(new[] { SectionTone.Secondary, SectionTone.Attention, SectionTone.Secondary }));
        Assert.That(Intelligence.Texts(section).Any(text => text.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
    }

    [Test]
    public void APartReadsStaleOnceItsStaleAtHasPassedWhateverItsStateSaid()
    {
        var section = Present(ControlPlaneApiTests.Available, now: "2026-09-26T18:06:00.000Z");
        var statuses = section.Lines.Where(line => line.Detail).ToList();
        Assert.That(statuses.Select(line => line.Text), Has.All.EndWith("stale since 18:05"));
        Assert.That(statuses.Select(line => line.Tone), Has.All.EqualTo(SectionTone.Attention));
    }

    [Test]
    public void ARecordedAnswerIsDescribedAsOfWhenItWasRecorded()
    {
        var section = Present(ControlPlaneApiTests.Available, now: "2026-11-20T10:00:00.000Z", recorded: true);
        Assert.That(section.Provenance, Is.EqualTo("From Seorak, recorded at 18:01:00"));
        Assert.That(section.Lines[1].Text, Does.EndWith("fresh, data to 17:58:12"), "not stale on this device's later clock");
    }

    [Test]
    public void WhatTheSourceDoesNotHaveReadsAsUnknownOrPendingNeverZero()
    {
        var json = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
        {
            var evaluation = Intelligence.EvaluationOf(response);
            evaluation["cost"]!["estimated_usd"] = null;
            evaluation["outcome"]!["availability"] = JObject.Parse("{\"state\":\"unavailable\",\"reason\":\"not_captured\"}");
            evaluation["outcome"]!["measure"] = null;
            evaluation["verification"]!["lens"]!["by_kind"] = JArray.Parse("[{\"label\":\"test\",\"runs\":null,\"passed\":null,\"pass_rate\":null}]");
        });
        var section = Present(json);
        Assert.That(Intelligence.Line(section, "Unknown").Text, Is.EqualTo("Unknown: unpriced, or not measured yet."));
        Assert.That(Intelligence.Line(section, "Unknown").Tag, Is.EqualTo("Cost"));
        Assert.That(Intelligence.Texts(section), Does.Contain("Nothing measured yet."));
        Assert.That(Intelligence.Texts(section)[3], Does.StartWith("unavailable: not captured"));
        Assert.That(Intelligence.Texts(section), Does.Contain("test: passes unknown of runs unknown (no pass rate)"));

        var measured = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
        {
            var measure = Intelligence.EvaluationOf(response)["outcome"]!["measure"]!;
            measure["commits_landed"] = 0;
            measure["error_count"] = 2;
            measure["first_error_at"] = "2026-09-26T17:44:10.000Z";
            measure["end_reason"] = null;
            measure["uncommitted"] = JObject.Parse("{\"files_touched\":3,\"lines_added\":41,\"lines_removed\":7,\"generated_lines_excluded\":120}");
            measure["line_survival"] = JObject.Parse(
                "{\"rung\":\"3d\",\"fate\":\"retained\",\"rate\":0.9,\"lines_authored\":40,\"lines_surviving\":36,\"commits_checked\":2}");
        });
        var outcome = Present(measured);
        Assert.That(Intelligence.Texts(outcome)[2], Is.EqualTo("no commits landed · 2 tool errors, the first at 17:44 · not ended, or its end not captured"));
        Assert.That(outcome.Lines[2].Tone, Is.EqualTo(SectionTone.Attention));
        Assert.That(Intelligence.Texts(outcome)[3],
            Is.EqualTo("uncommitted: 3 files, +41 −7 (120 generated lines apart) · lines kept after 3 days: 90% (36 of 40 lines)"));

        var noLens = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
            Intelligence.EvaluationOf(response)["verification"]!["lens"] = null);
        Assert.That(Intelligence.Texts(Present(noLens)), Does.Contain("No verification lens."));
        var empty = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
            Intelligence.EvaluationOf(response)["verification"]!["lens"] = JObject.Parse("{\"by_kind\":[],\"empty_reason\":\"No verification result was captured.\"}"));
        Assert.That(Intelligence.Texts(Present(empty)), Does.Contain("No verification result was captured."));
    }

    [Test]
    public void TheCostIsAlwaysTheSourcesEstimateWithItsNote()
    {
        foreach (var (usd, amount) in new[] { (0.0, "less than $0.01"), (0.004, "less than $0.01"), (0.387, "$0.39"), (12.5, "$12.50") })
        {
            var json = Intelligence.Edit(ControlPlaneApiTests.Available, response => Intelligence.EvaluationOf(response)["cost"]!["estimated_usd"] = usd);
            Assert.That(Present(json).Lines[0].Text, Is.EqualTo("About " + amount + ". Estimated from token counts at list prices. Not a bill."));
        }
    }

    [Test]
    public void EachAvailabilityIsSaidInWords()
    {
        var cases = new Dictionary<string, string>
        {
            ["not_found"] = "No evaluation yet: Seorak has not captured this session.",
            ["unavailable"] = "Evaluation unavailable: Seorak has not captured this session.",
            ["incompatible"] = "Evaluation unreadable: Seorak has not captured this session.",
            ["unauthorized"] = "Evaluation not allowed: Seorak has not captured this session.",
        };
        foreach (var (availability, provenance) in cases)
        {
            var section = Present(Intelligence.Failure(availability, "some_code", "Seorak has not captured this session."));
            Assert.That(section.Provenance, Is.EqualTo(provenance));
            Assert.That(section.Lines, Is.Empty);
        }
        var now = Intelligence.At("2026-09-26T18:02:00.000Z");
        Assert.That(EvaluationPresenter.Present(null, null, false, null, now, Intelligence.Utc).Provenance, Is.EqualTo("Nothing to evaluate until work starts."));
        Assert.That(EvaluationPresenter.Present(Intelligence.ExecutionId, null, true, null, now, Intelligence.Utc).Provenance,
            Is.EqualTo("Asking what the evaluation source measured…"));
    }

    [Test]
    public void ASimulatedAnswerSaysSo()
    {
        var json = Intelligence.Edit(ControlPlaneApiTests.Available, response => Intelligence.EvaluationOf(response)["source"]!["synthetic"] = true);
        var section = Present(json, recorded: true);
        Assert.That(section.Provenance, Is.EqualTo("Simulated, not from Seorak · recorded at 18:01:00"));
        Assert.That(section.Simulated, Is.True);
    }
}

public class IntelligenceWordsTests
{
    [Test]
    public void TheSectionsAreNamedForWhatTheyDoAndOnlyTheProvenanceNamesAProduct()
    {
        Assert.That(IntelligenceText.TitleOf(SectionKind.Understanding), Is.EqualTo("Understanding"));
        Assert.That(IntelligenceText.TitleOf(SectionKind.Evaluation), Is.EqualTo("Evaluation"));
        var now = Intelligence.At("2026-09-26T18:02:00.000Z");
        var sections = new[]
        {
            UnderstandingPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(Intelligence.Verified), "2026-09-20T16:21:30.000Z"),
                false, null, now, Intelligence.Utc, 20),
            EvaluationPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Evaluation(ControlPlaneApiTests.Available), "2026-09-26T18:01:00.000Z"),
                false, null, now, Intelligence.Utc),
        };
        var products = new[] { "Salidium", "Seorak" };
        foreach (var section in sections)
        {
            Assert.That(products.Any(product => section.Provenance.Contains(product, StringComparison.Ordinal)), Is.True, section.Title);
            foreach (var line in section.Lines)
            {
                foreach (var product in products) Assert.That(line.Text + line.Tag, Does.Not.Contain(product), section.Title);
            }
            foreach (var brand in new[] { "Meta", "Quest", "Claude", "Anthropic", "Codex", "OpenAI" })
            {
                Assert.That(section.Title + section.Provenance, Does.Not.Contain(brand));
            }
        }
    }

    /// <summary>
    /// Source text follows the one rule for text Halcyonic did not write (<see cref="LabelTextTests"/>):
    /// line breaks as spaces, what would not show as itself shown as its code point, markup as written.
    /// </summary>
    [Test]
    public void PlainTextShowsControlAndFormatCharactersByCodeAndCollapsesSpaceButKeepsMarkupAsWritten()
    {
        Assert.That(IntelligenceText.Plain(null), Is.Empty);
        Assert.That(IntelligenceText.Plain("  a\r\n\tb\u2028c\u2029 "), Is.EqualTo("a b c"));
        Assert.That(IntelligenceText.Plain("\u202Eevil\u202C\u200Bzero\u00ADsoft\uFEFFbom\u0007bell\u001B[31m"),
            Is.EqualTo("‹U+202E›evil‹U+202C›‹U+200B›zero‹U+00AD›soft‹U+FEFF›bom‹U+0007›bell‹U+001B›[31m"));
        Assert.That(IntelligenceText.Plain("<b>bold</b> <noparse> &lt;"), Is.EqualTo("<b>bold</b> <noparse> &lt;"));
        Assert.That(IntelligenceText.Plain("emoji 😀 stays"), Is.EqualTo("emoji 😀 stays"), "a surrogate pair is kept whole");
        Assert.That(IntelligenceText.Plain("verified\u0003 until the next run"), Is.EqualTo(LabelText.Plain("verified\u0003 until the next run")), "one rule");
    }

    [Test]
    public void AFileIsNamedByItsLastPart()
    {
        Assert.That(IntelligenceText.FileName("src/middleware/rate-limit.ts"), Is.EqualTo("rate-limit.ts"));
        Assert.That(IntelligenceText.FileName(@"C:\repo\file.cs"), Is.EqualTo("file.cs"));
        Assert.That(IntelligenceText.FileName("README.md"), Is.EqualTo("README.md"));
    }
}

public class IntelligenceFeedTests
{
    private static readonly DateTimeOffset Start = Intelligence.At("2026-09-29T09:00:00.000Z");

    /// <summary>Reads that finish only when a test says so, and what was asked.</summary>
    private sealed class Reads
    {
        public List<(string ExecutionId, TaskCompletionSource<IntelligenceRead<UnderstandingResponse>> Answer, CancellationToken Cancel)> Asked { get; } = new();

        public Task<IntelligenceRead<UnderstandingResponse>> Read(string executionId, CancellationToken cancel)
        {
            var answer = new TaskCompletionSource<IntelligenceRead<UnderstandingResponse>>();
            Asked.Add((executionId, answer, cancel));
            return answer.Task;
        }

        public void Answer(int index)
        {
            var response = Intelligence.Understanding(Intelligence.Verified);
            Asked[index].Answer.SetResult(new IntelligenceRead<UnderstandingResponse>(response, Start, recorded: false));
        }
    }

    [Test]
    public void AsksOnceWhenShownAndShowsTheAnswerOncePolled()
    {
        var reads = new Reads();
        var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        feed.Show(Intelligence.ExecutionId, "a", Start);
        feed.Show(Intelligence.ExecutionId, "a", Start.AddSeconds(1));
        Assert.That(reads.Asked, Has.Count.EqualTo(1));
        Assert.That(feed.Loading, Is.True);
        Assert.That(feed.Poll(), Is.False, "nothing has arrived");

        reads.Answer(0);
        var version = feed.Version;
        Assert.That(feed.Poll(), Is.True);
        Assert.That(feed.Version, Is.GreaterThan(version));
        Assert.That(feed.Loading, Is.False);
        Assert.That(feed.Last, Is.Not.Null);
        feed.Show(Intelligence.ExecutionId, "a", Start.AddMinutes(5));
        Assert.That(reads.Asked, Has.Count.EqualTo(1), "an answer held is not asked for again without a refresh");
        Assert.That(new IntelligenceFeed<UnderstandingResponse>(reads.Read).Loading, Is.False);
    }

    [Test]
    public void ARefreshAsksAgainAndKeepsTheLastAnswerWhileItReads()
    {
        var reads = new Reads();
        var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        feed.Show(Intelligence.ExecutionId, null, Start);
        reads.Answer(0);
        feed.Poll();
        var first = feed.Last;

        feed.Refresh(null, Start.AddSeconds(2));
        Assert.That(reads.Asked, Has.Count.EqualTo(2));
        Assert.That(feed.Loading, Is.True);
        Assert.That(feed.Last, Is.SameAs(first), "the last answer stays while a new one is read");

        feed.Refresh(null, Start.AddSeconds(3));
        Assert.That(reads.Asked[1].Cancel.IsCancellationRequested, Is.True, "a read replaced by another is abandoned");
        reads.Answer(2);
        feed.Poll();
        Assert.That(feed.Last, Is.Not.SameAs(first));
    }

    [Test]
    public void FollowingAsksAgainOnlyOnceTheExecutionChangedAndTheIntervalPassed()
    {
        var reads = new Reads();
        var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        var interval = TimeSpan.FromSeconds(2);
        feed.Show(Intelligence.ExecutionId, "a", Start, interval);
        reads.Answer(0);
        feed.Poll();

        feed.Show(Intelligence.ExecutionId, "a", Start.AddSeconds(10), interval);
        Assert.That(reads.Asked, Has.Count.EqualTo(1), "nothing changed");
        feed.Show(Intelligence.ExecutionId, "b", Start.AddSeconds(1), interval);
        Assert.That(reads.Asked, Has.Count.EqualTo(1), "changed, but asked a moment ago");
        feed.Show(Intelligence.ExecutionId, "b", Start.AddSeconds(2), interval);
        Assert.That(reads.Asked, Has.Count.EqualTo(2));
        feed.Show(Intelligence.ExecutionId, "c", Start.AddSeconds(9), interval);
        Assert.That(reads.Asked, Has.Count.EqualTo(2), "one read at a time");

        var budgeted = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        budgeted.Show(Intelligence.ExecutionId, "a", Start);
        reads.Answer(2);
        budgeted.Poll();
        budgeted.Show(Intelligence.ExecutionId, "z", Start.AddHours(1));
        Assert.That(reads.Asked, Has.Count.EqualTo(3), "without an interval it never asks by itself");
    }

    [Test]
    public void AnotherExecutionForgetsWhatWasReadAndDropsAReadInFlight()
    {
        var reads = new Reads();
        var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        feed.Show(Intelligence.ExecutionId, null, Start);
        feed.Show("01a0dcf1-5a80-7000-8000-0000000000e2", null, Start);
        Assert.That(reads.Asked.Select(ask => ask.ExecutionId), Is.EqualTo(new[] { Intelligence.ExecutionId, "01a0dcf1-5a80-7000-8000-0000000000e2" }));
        Assert.That(reads.Asked[0].Cancel.IsCancellationRequested, Is.True);
        reads.Answer(0);
        Assert.That(feed.Poll(), Is.False, "the abandoned read's answer is not shown");
        Assert.That(feed.Last, Is.Null);

        feed.Show(null, null, Start);
        Assert.That(feed.ExecutionId, Is.Null);
        Assert.That(feed.Loading, Is.False);
        Assert.That(reads.Asked, Has.Count.EqualTo(2), "no execution, nothing to ask");
        feed.Clear();
        Assert.That(feed.ExecutionId, Is.Null);
    }

    [Test]
    public void AFailedReadSaysWhyAndIsNotRetriedUntilRefreshed()
    {
        var reads = new Reads();
        var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read);
        feed.Show(Intelligence.ExecutionId, null, Start);
        reads.Asked[0].Answer.SetException(new ControlPlaneRequestException("The control plane could not be reached: refused"));
        feed.Poll();
        Assert.That(feed.Error, Is.EqualTo("The control plane could not be reached: refused"));
        feed.Show(Intelligence.ExecutionId, null, Start.AddMinutes(1));
        Assert.That(reads.Asked, Has.Count.EqualTo(1));

        feed.Refresh(null, Start.AddMinutes(1));
        reads.Asked[1].Answer.SetCanceled();
        feed.Poll();
        Assert.That(feed.Error, Is.EqualTo("The control plane did not answer in time."));

        var throwing = new IntelligenceFeed<UnderstandingResponse>((_, _) => throw new InvalidOperationException("no reader"));
        throwing.Show(Intelligence.ExecutionId, null, Start);
        throwing.Poll();
        Assert.That(throwing.Error, Is.EqualTo("no reader"));
    }
}

public class DemonstrationReadsTests
{
    private RealtimeSession? session;

    [TearDown]
    public async Task StopSession()
    {
        if (session != null) await session.StopAsync();
    }

    private static string DirectedId(DemonstrationRecording recording)
    {
        var directed = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer.ExecutionId;
        Assert.That(recording.Understanding.ContainsKey(directed), Is.True);
        return directed;
    }

    [Test]
    public void TheBundledRecordingHoldsOnlySimulatedAnswersAndEveryOneReadsStrictly()
    {
        var document = (JObject)Json.Parse(Demonstration.Text());
        var answers = 0;
        foreach (var name in new[] { "understanding", "evaluation" })
        {
            foreach (var property in ((JObject)document[name]!).Properties())
            {
                foreach (var entry in (JArray)property.Value)
                {
                    var text = entry["response"]!.ToString(Formatting.None);
                    if (name == "understanding") AssertReadsStrictly<UnderstandingResponse>(text);
                    else AssertReadsStrictly<EvaluationResponse>(text);
                    answers++;
                }
            }
        }
        var recording = Demonstration.Recording();
        Assert.That(recording.Understanding.Values.Sum(list => list.Count) + recording.Evaluation.Values.Sum(list => list.Count), Is.EqualTo(answers));
        Assert.That(recording.Understanding.Keys, Has.Count.EqualTo(3), "every execution of the story");
        foreach (var answer in recording.Understanding.Values.SelectMany(list => list))
        {
            var source = ((AvailableUnderstanding)answer.Response.Result).Understanding.Source;
            Assert.That(source.Synthetic, Is.True);
            Assert.That(source.Version, Is.EqualTo("simulated"));
            Assert.That(source.InstanceId, Is.EqualTo(new string('0', 32)));
        }
        foreach (var answer in recording.Evaluation.Values.SelectMany(list => list))
        {
            Assert.That(((AvailableEvaluation)answer.Response.Result).Evaluation.Source.Synthetic, Is.True);
        }
    }

    [Test]
    public void TheAnswerInForceFollowsThePathThroughTheTree()
    {
        var recording = Demonstration.Recording();
        var directed = DirectedId(recording);
        var beginning = recording.Nodes[0];
        string Headline(RecordedAnswer<UnderstandingResponse>? answer) =>
            ((AvailableUnderstanding)answer!.Response.Result).Understanding.Verdict.Headline;

        Assert.That(recording.UnderstandingAt(directed, 0, 1), Is.Null, "before the work starts there is no answer");
        Assert.That(Headline(recording.UnderstandingAt(directed, 0, beginning.Events.Count)), Is.EqualTo("Waiting for you"));

        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        Assert.That(Headline(recording.UnderstandingAt(directed, approve.Node, 0)), Is.EqualTo("Waiting for you"), "a branch starts from its answer's point");
        var approved = recording.Nodes[approve.Node];
        Assert.That(Headline(recording.UnderstandingAt(directed, approve.Node, approved.Events.Count)), Is.EqualTo("1 test failing"));
        var instruct = Demonstration.InstructionsAfterApproving()[0];
        var instructed = recording.Nodes[instruct.Node];
        Assert.That(Headline(recording.UnderstandingAt(directed, instruct.Node, instructed.Events.Count)), Is.EqualTo("2 files changed, verified"));

        var deny = Demonstration.AtApproval(DemonstrationAnswerKind.Deny);
        var denied = recording.Nodes[deny.Node];
        Assert.That(Headline(recording.UnderstandingAt(directed, deny.Node, denied.Events.Count)), Is.EqualTo("2 files changed, unverified"));

        var lens = ((AvailableEvaluation)recording.EvaluationAt(directed, approve.Node, approved.Events.Count)!.Response.Result).Evaluation.Verification.Lens!;
        Assert.That(lens.ByKind.Single().Runs, Is.EqualTo(1));
        Assert.That(lens.ByKind.Single().Passed, Is.EqualTo(0));
        Assert.That(recording.UnderstandingAt("01a0dcf1-5a80-7000-8000-0000000000e9", 0, beginning.Events.Count), Is.Null);
        Assert.That(recording.UnderstandingAt(directed, 99, 1), Is.Null);
    }

    [Test]
    public void AnythingThatCouldPassForARealSourceOrContradictTheRecordingIsRefused()
    {
        JObject Understanding(JObject document) => (JObject)document["understanding"]!;
        JArray First(JObject document) => (JArray)Understanding(document).Properties().First().Value;
        var cases = new Dictionary<string, string>
        {
            ["a real source's answer"] = Demonstration.Edit(document =>
                First(document)[0]["response"]!["result"]!["understanding"]!["source"]!["synthetic"] = false),
            ["a refusal naming a source"] = Demonstration.Edit(document =>
                First(document)[0]["response"]!["result"] = JObject.Parse("{\"availability\":\"unavailable\",\"reason\":{\"code\":\"not_running\",\"message\":\"Salidium is not running.\"}}")),
            ["another execution's answer"] = Demonstration.Edit(document =>
                First(document)[0]["response"]!["execution_id"] = "01a0dcf1-5a80-7000-8000-0000000000e9"),
            ["a node that is not there"] = Demonstration.Edit(document => First(document)[0]["node"] = 99),
            ["inside an instant"] = Demonstration.Edit(document =>
            {
                var events = (JArray)document["nodes"]![0]!["events"]!;
                First(document)[0]["after"] = Enumerable.Range(1, events.Count - 1).First(i => (long)events[i]["at_ms"]! == (long)events[i - 1]["at_ms"]!);
            }),
            ["out of order"] = Demonstration.Edit(document =>
            {
                var list = First(document);
                var first = list[0];
                list.RemoveAt(0);
                list.Insert(1, first);
            }),
            ["no time read"] = Demonstration.Edit(document => ((JObject)First(document)[0]).Remove("read_at")),
            ["not keyed by execution"] = Demonstration.Edit(document => document["evaluation"] = new JArray()),
        };
        foreach (var (name, text) in cases)
        {
            Assert.Throws<InvalidDataException>(() => DemonstrationRecording.Parse(text), name);
        }
    }

    [Test]
    public void ARecordingWithoutAnswersStillPlays()
    {
        var recording = DemonstrationRecording.Parse(Demonstration.Edit(document =>
        {
            document.Remove("understanding");
            document.Remove("evaluation");
        }));
        Assert.That(recording.Understanding, Is.Empty);
        Assert.That(recording.Evaluation, Is.Empty);
        Assert.That(recording.UnderstandingAt(DirectedId(Demonstration.Recording()), 0, recording.Nodes[0].Events.Count), Is.Null);
    }

    /// <summary>
    /// What a judge sees in the sections: read where the playback stands, through the same interface
    /// the workspace reads a control plane with, marked recorded, simulated in words.
    /// </summary>
    [Test]
    public async Task ReadsAnswerWhereThePlaybackStandsAndFollowTheJudgesAnswer()
    {
        var player = new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, Demonstration.Fast());
        session = player.Session;
        IIntelligenceReader reads = player.Reads;
        var early = await AssertNothingRecorded(reads, Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer.ExecutionId);
        Assert.That(early, Is.EqualTo("The demonstration is still being read.").Or.EqualTo(DemonstrationReads.NothingRecorded));

        session.Start();
        await Pumping.Until(session, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.WaitingForHuman, "the directed work needs a person");
        var execution = Demonstration.DirectedExecution(session)!;

        var understanding = await reads.ReadUnderstandingAsync(execution.ExecutionId, CancellationToken.None);
        Assert.That(understanding.Recorded, Is.True);
        var section = UnderstandingPresenter.Present(execution.ExecutionId, understanding, false, null, DateTimeOffset.UtcNow, Intelligence.Utc, 7);
        Assert.That(section.Provenance, Does.StartWith("Simulated, not from Salidium · recorded at 09:00:0"));
        Assert.That(section.Lines[0].Text, Does.StartWith("Waiting for you. Run make migrate"));
        var evaluation = await reads.ReadEvaluationAsync(execution.ExecutionId, CancellationToken.None);
        var measured = EvaluationPresenter.Present(execution.ExecutionId, evaluation, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(measured.Provenance, Does.StartWith("Simulated, not from Seorak · recorded at"));
        Assert.That(measured.Lines.Select(line => line.Text), Does.Contain("Nothing measured yet."), "the outcome waits for the turn to end");
        Assert.That(measured.Lines[3].Text, Does.StartWith("unavailable: not yet computed"), "the outcome's own statement about itself");

        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        await session.SubmitAsync(new CommandFactory(Samples.Client).RespondToApproval(approve.Answer.ExecutionId, approve.Answer.ApprovalId!, ApprovalDecision.Approve));
        await Pumping.Until(session, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.Completed, "the approved turn ends");

        understanding = await reads.ReadUnderstandingAsync(execution.ExecutionId, CancellationToken.None);
        section = UnderstandingPresenter.Present(execution.ExecutionId, understanding, false, null, DateTimeOffset.UtcNow, Intelligence.Utc, 7);
        Assert.That(section.Lines[0].Text, Does.StartWith("1 test failing"));
        Assert.That(section.Lines.Select(line => line.Tag), Does.Contain("explained"));
        evaluation = await reads.ReadEvaluationAsync(execution.ExecutionId, CancellationToken.None);
        measured = EvaluationPresenter.Present(execution.ExecutionId, evaluation, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(measured.Lines.Select(line => line.Text), Does.Contain("test: 0 passed of 1 run (0%)"));

        Assert.That(await AssertNothingRecorded(reads, "01a0dcf1-5a80-7000-8000-0000000000e9"), Is.EqualTo(DemonstrationReads.NothingRecorded));
    }

    /// <summary>
    /// Reads strictly and writes back the same document. Numbers compare by value: a rate of 0 comes
    /// back as 0.0, the same number.
    /// </summary>
    private static void AssertReadsStrictly<T>(string json) where T : class
    {
        var written = HalcyonicJson.Serialize(HalcyonicJson.Deserialize<T>(json, strict: true));
        Assert.That(Same(Json.Parse(json), Json.Parse(written)), Is.True, "the round trip changed the document:\n" + json + "\n" + written);
    }

    private static bool Same(JToken a, JToken b)
    {
        if (a.Type is JTokenType.Integer or JTokenType.Float && b.Type is JTokenType.Integer or JTokenType.Float)
        {
            return (double)a == (double)b;
        }
        if (a is JObject left && b is JObject right)
        {
            return left.Properties().Count() == right.Properties().Count()
                && left.Properties().All(property => right.TryGetValue(property.Name, out var other) && Same(property.Value, other));
        }
        if (a is JArray first && b is JArray second)
        {
            return first.Count == second.Count && first.Zip(second).All(pair => Same(pair.First, pair.Second));
        }
        return JToken.DeepEquals(a, b);
    }

    private static async Task<string> AssertNothingRecorded(IIntelligenceReader reads, string executionId)
    {
        try
        {
            await reads.ReadUnderstandingAsync(executionId, CancellationToken.None);
        }
        catch (ControlPlaneRequestException error)
        {
            return error.Message;
        }
        Assert.Fail("An answer was read where the recording holds none.");
        return "";
    }
}
