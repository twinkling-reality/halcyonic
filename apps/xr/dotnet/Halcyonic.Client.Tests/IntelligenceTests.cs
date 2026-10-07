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
              "revision": {
                "at_start": null,
                "at_latest_turn_end": null
              },
              "changes": {
                "summary": "4 files changed (+43 −6) · 2 commands",
                "files": [
                  {
                    "path": "src/payments/refunds.ts",
                    "repository_path": null,
                    "change_count": 1,
                    "lines_added": 5,
                    "lines_removed": 1,
                    "lines_removed_exact": null,
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
                    "repository_path": null,
                    "change_count": 1,
                    "lines_added": 6,
                    "lines_removed": 0,
                    "lines_removed_exact": null,
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
                    "repository_path": null,
                    "change_count": 1,
                    "lines_added": 8,
                    "lines_removed": 2,
                    "lines_removed_exact": null,
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
                    "repository_path": null,
                    "change_count": 1,
                    "lines_added": 24,
                    "lines_removed": 3,
                    "lines_removed_exact": null,
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

public class IntelligenceWordsTests
{
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
        reads.Asked[0].Answer.SetException(new ControlPlaneRequestException("The control plane could not be reached: refused", new System.Net.Http.HttpRequestException("refused")));
        feed.Poll();
        Assert.That(feed.Error, Is.EqualTo("your computer didn't answer. Press Refresh to try again."));
        feed.Show(Intelligence.ExecutionId, null, Start.AddMinutes(1));
        Assert.That(reads.Asked, Has.Count.EqualTo(1));

        feed.Refresh(null, Start.AddMinutes(1));
        reads.Asked[1].Answer.SetCanceled();
        feed.Poll();
        Assert.That(feed.Error, Is.EqualTo("your computer didn't answer in time. Press Refresh to try again."));

        var throwing = new IntelligenceFeed<UnderstandingResponse>((_, _) => throw new InvalidOperationException("no reader"));
        throwing.Show(Intelligence.ExecutionId, null, Start);
        throwing.Poll();
        Assert.That(throwing.Error, Is.EqualTo("something went wrong. Press Refresh to try again."));
        var unasked = new IntelligenceFeed<UnderstandingResponse>((_, _) => throw new UnaskedReadException(IntelligenceText.NotConnected));
        unasked.Show(Intelligence.ExecutionId, null, Start);
        unasked.Poll();
        Assert.That(unasked.Error, Is.EqualTo("your computer isn't connected. Press Refresh when it is."));
    }

    /// <summary>
    /// A failed read is said by what failed, never the error's message, which is the control plane's or the
    /// runtime's own and can hold an address (the review's leak 3, settled by the coordinator, 2026-10-07).
    /// </summary>
    [Test]
    public void AFailedReadIsNeverSaidByItsMessage()
    {
        const string Leak = "The control plane refused the request: 401 Unauthorized (http://192.168.1.20:47801/api/executions/x/evaluation)";
        const string Paired = "Your computer refused this headset's pairing.";
        var cases = new (Exception Error, string Why)[]
        {
            (new OperationCanceledException(Leak), "your computer didn't answer in time. Press Refresh to try again."),
            (new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak)), "your computer didn't answer. Press Refresh to try again."),
            (new UnaskedReadException(IntelligenceText.NotConnected), "your computer isn't connected. Press Refresh when it is."),
            (new ControlPlaneRequestException(Leak, "too_many_requests", 429),
                "your computer is turning this headset away for a minute after too many tries. Press Refresh after a minute."),
            (new ControlPlaneRequestException(Leak, "device_revoked", 401), IntelligenceText.AfterColon(ConnectionText.PairingRefused)),
            (new ControlPlaneRequestException(Leak, "unauthorized", 401), IntelligenceText.AfterColon(Paired)),
            // A code counts only with the status that carries it.
            (new ControlPlaneRequestException(Leak, "device_revoked", 500), "something went wrong. Press Refresh to try again."),
            (new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak, new CertificateMismatchException("ab"))),
                IntelligenceText.AfterColon(ConnectionText.NotThePairedComputer)),
            (new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak, new TokenNotSentException(LoopbackProofOutcome.Unproved, new Uri("http://127.0.0.1:47800/")))),
                "what answered couldn't prove it holds the access code, so the headset sent nothing. Check that this app is running there, then press Refresh."),
            (new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak, new TokenNotSentException(LoopbackProofOutcome.Unreachable, new Uri("http://127.0.0.1:47800/")))),
                "your computer didn't answer. Press Refresh to try again."),
            (new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak, new System.IO.InvalidDataException(Leak))), "something went wrong. Press Refresh to try again."),
            (new Newtonsoft.Json.JsonReaderException(Leak), IntelligenceText.AfterColon(ConnectionText.Unreadable)),
            (new InvalidOperationException(Leak), "something went wrong. Press Refresh to try again."),
        };
        foreach (var (error, why) in cases)
        {
            var reads = new Reads();
            var feed = new IntelligenceFeed<UnderstandingResponse>(reads.Read, () => Paired);
            feed.Show(Intelligence.ExecutionId, null, Start);
            reads.Asked[0].Answer.SetException(error);
            feed.Poll();
            Assert.That(feed.Error, Is.EqualTo(why), error.GetType().Name);
            Assert.That(feed.Error, Does.Not.Contain("192.168").And.Not.Contain("401").And.Not.Contain("control plane"));
        }
    }

    [Test]
    public void ASentenceReusedAfterAColonLowersOnlyItsFirstLetter()
    {
        Assert.That(IntelligenceText.AfterColon(ConnectionText.Unreadable), Is.EqualTo("your computer sent something this app can't read. Install the same version on both."));
        Assert.That(IntelligenceText.AfterColon("What answered isn't it."), Is.EqualTo("what answered isn't it."));
        Assert.That(IntelligenceText.AfterColon(""), Is.EqualTo(""));
    }

    /// <summary>
    /// A source that answered without conclusions is said by its code, never its message, which is the source's
    /// or its reader's and can hold an address, a path or the source's own words (the review's leak 3).
    /// </summary>
    [Test]
    public void ASourcesReasonIsSaidByItsCodeNeverItsMessage()
    {
        const string Leak = "Seorak at http://127.0.0.1:7600 refused: visit http://203.0.113.9/unlock";
        foreach (var (code, why) in new[]
        {
            ("not_running", "it isn't running on your computer. Start it there, then press Refresh."),
            ("timed_out", "it didn't answer on your computer. Press Refresh in a moment."),
            ("rate_limited", "it asked for a pause after too many requests. Press Refresh a little later."),
            ("credential_malformed", "the credential set up for it on your computer isn't the right kind, so it wasn't sent. Set up another there."),
            ("insufficient_scope", "the credential set up on your computer can't read this. Set up one that can."),
            ("result_limit", "it stopped at a limit before it found this task. Press Refresh to try again."),
            ("instance_mismatch", "what answered isn't it. Check its setup on your computer."),
            ("invalid_evaluation", "its answer isn't one this app can read. Check its version on your computer."),
            ("native_id_unknown", "the agent app hasn't said which session this is yet. Press Refresh in a moment."),
            ("something_new", "it didn't say why. Press Refresh to try again."),
        })
        {
            Assert.That(IntelligenceText.Why(new ErrorInfo { Code = code, Message = Leak }), Is.EqualTo(why), code);
        }
        var now = Intelligence.At(Answers.Now);
        var unavailable = new IntelligenceRead<EvaluationResponse>(new EvaluationResponse
        {
            ExecutionId = Intelligence.ExecutionId,
            Result = new UnavailableEvaluation { Reason = new ErrorInfo { Code = "not_running", Message = Leak } },
        }, now, recorded: false);
        var line = EvaluationPresenter.Measurement(unavailable, false, null, now, Intelligence.Utc).Single().Text;
        Assert.That(line, Is.EqualTo("From Seorak · Evaluation unavailable: it isn't running on your computer. Start it there, then press Refresh."));
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
        // At the agent's question the stand-in waits for the person, as it does at an approval.
        Assert.That(Headline(recording.UnderstandingAt(directed, 0, beginning.Events.Count)), Is.EqualTo("Waiting for you"));
        var answered = Demonstration.Answered();
        Assert.That(Headline(recording.UnderstandingAt(directed, Demonstration.AtQuestion().Node, answered.Events.Count)), Is.EqualTo("Waiting for you"));

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
        Assert.That(early, Is.EqualTo(DemonstrationReads.StillBeingRead).Or.EqualTo(DemonstrationReads.NothingRecorded));

        session.Start();
        await Demonstration.ToTheApprovalAsync(session);
        var execution = Demonstration.DirectedExecution(session)!;

        var understanding = await reads.ReadUnderstandingAsync(execution.ExecutionId, CancellationToken.None);
        Assert.That(understanding.Recorded, Is.True);
        var section = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, execution.ExecutionId, understanding, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(section.Provenance, Does.StartWith("Simulated explanation · recorded at 09:00:0"));
        Assert.That(section.Lines[0].Text, Is.EqualTo("2 files changed: 2 new"));
        var evaluation = await reads.ReadEvaluationAsync(execution.ExecutionId, CancellationToken.None);
        var measured = CheckedPresenter.Present(execution.ExecutionId, understanding, false, null, evaluation, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        var source = measured.Lines.Single(line => line.Source);
        Assert.That(source.Text, Does.StartWith("Simulated measurement · recorded at"));
        Assert.That(measured.Lines.Select(line => line.Text), Does.Contain("Nothing measured yet."), "the outcome waits for the turn to end");
        Assert.That(measured.Lines.Last().Text, Does.StartWith("unavailable: not yet computed"), "the outcome's own statement about itself");

        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        await session.SubmitAsync(new CommandFactory(Samples.Client).RespondToApproval(approve.Answer.ExecutionId, approve.Answer.ApprovalId!, ApprovalDecision.Approve));
        await Pumping.Until(session, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.Completed, "the approved turn ends");

        understanding = await reads.ReadUnderstandingAsync(execution.ExecutionId, CancellationToken.None);
        section = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, execution.ExecutionId, understanding, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(section.Lines.Select(line => line.Text), Does.Contain("New: src/middleware/rate-limit.ts (+57 −0)"), "by its path in its repository");
        Assert.That(section.Lines[1].Text, Is.EqualTo("At commit 3e7b0c2 on sign-in-rate-limit, where it started"));
        var flow = UnderstandingPresenter.Present(UnderstandPrompt.HowBuilt, execution.ExecutionId, understanding, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(flow.Steps, Is.True, "a judge can step through how it was built once the first round ends");
        Assert.That(flow.Lines.Select(line => line.Tag), Does.Contain("explained"));
        evaluation = await reads.ReadEvaluationAsync(execution.ExecutionId, CancellationToken.None);
        measured = CheckedPresenter.Present(execution.ExecutionId, understanding, false, null, evaluation, false, null, DateTimeOffset.UtcNow, Intelligence.Utc);
        Assert.That(measured.Lines.Select(line => line.Text), Does.Contain("test: 0 passed of 1 run (0%)"));
        Assert.That(measured.Lines[0].Text, Does.StartWith("Tests failed at "));

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
        catch (UnaskedReadException error)
        {
            return error.Message;
        }
        Assert.Fail("An answer was read where the recording holds none.");
        return "";
    }
}
