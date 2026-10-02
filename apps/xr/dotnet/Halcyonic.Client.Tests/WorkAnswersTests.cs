using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

internal static class Answers
{
    public const string Now = "2026-09-20T16:22:00.000Z";

    public static SectionPresentation Understand(UnderstandPrompt prompt, string json, string now = Now, AnswerRoom? room = null) =>
        UnderstandingPresenter.Present(prompt, Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"),
            loading: false, error: null, Intelligence.At(now), Intelligence.Utc, room);

    public static SectionPresentation Checked(string understanding, string evaluation, string now = "2026-09-26T18:02:00.000Z", bool recorded = false) =>
        CheckedPresenter.Present(Intelligence.ExecutionId,
            new IntelligenceRead<UnderstandingResponse>(Intelligence.Understanding(understanding), Intelligence.At("2026-09-20T16:21:30.000Z"), recorded),
            false, null,
            new IntelligenceRead<EvaluationResponse>(Intelligence.Evaluation(evaluation), Intelligence.At("2026-09-26T18:01:00.000Z"), recorded),
            false, null, Intelligence.At(now), Intelligence.Utc);

    /// <summary>The measurement's lines: those after the evaluation source's provenance line.</summary>
    public static IReadOnlyList<SectionLine> Measurement(SectionPresentation section) =>
        section.Lines.SkipWhile(line => !line.Source).Skip(1).ToList();

    public static (string Tag, string Text)[] Pairs(IEnumerable<SectionLine> lines) => lines.Select(line => (line.Tag, line.Text)).ToArray();

    public static string Files(string json, string files) =>
        Intelligence.Edit(json, response => Intelligence.UnderstandingOf(response)["changes"]!["files"] = JArray.Parse(files));

    public static string File(string path, string kinds, int added, int removed, string at, string? reason = null, string author = "agent") =>
        "{\"path\":\"" + path + "\",\"repository_path\":null,\"change_count\":1,\"lines_added\":" + added + ",\"lines_removed\":" + removed
        + ",\"lines_removed_exact\":null,\"kinds\":" + kinds
        + ",\"last_changed_at\":\"" + at + "\",\"coverage\":{\"verified_after\":false,\"by\":null,\"epistemic\":\"inferred\"},\"reason\":"
        + (reason == null ? "null" : "{\"text\":\"" + reason + "\",\"author\":\"" + author + "\",\"at\":\"2026-09-20T15:39:00.000Z\",\"epistemic\":\"reported\"}")
        + "}";

    public static string Explanation(string json, string status, bool current = false, bool content = false) =>
        Intelligence.Edit(json, response =>
        {
            var explanation = (JObject)Intelligence.UnderstandingOf(response)["explanation"]!;
            explanation["status"] = status;
            explanation["current"] = current;
            if (!content) explanation["content"] = null;
        });
}

public class WhatChangedTests
{
    [Test]
    public void EachFileSaysHowItChangedAndByHowManyLinesAsTheSourceObserved()
    {
        var section = Answers.Understand(UnderstandPrompt.WhatChanged, Intelligence.Verified);

        Assert.That(section.Provenance, Is.EqualTo("From Salidium 0.6.0, 2 minutes ago"));
        Assert.That(section.Steps, Is.False);
        Assert.That(Answers.Pairs(section.Lines), Is.EqualTo(new[]
        {
            ("observed", "4 files changed: 4 edited"),
            ("observed", "Edited: refunds.ts (+5 −1)"),
            ("observed", "Edited: ChargeService.test.ts (+6 −0)"),
            ("observed", "Edited: RetryWorker.ts (+8 −2)"),
            ("observed", "Edited: ChargeService.ts (+24 −3)"),
            ("inferred", "1 file not checked after the last change: refunds.ts"),
            ("planned", "Planned, not done: Document refund behaviour for support"),
        }));
        Assert.That(section.Lines.Any(line => line.Tone == SectionTone.Claim), Is.False, "nothing here is anyone's claim");
    }

    [Test]
    public void NewRemovedAndMovedFilesAreSaidInWordsAndCountedByKind()
    {
        var json = Answers.Files(Intelligence.Verified, "[" + string.Join(",",
            Answers.File("src/limits/rate-limit.ts", "[\"add\",\"update\"]", 57, 0, "2026-09-20T15:40:00.000Z"),
            Answers.File("src/limits/memory-store.ts", "[\"update\",\"delete\"]", 0, 31, "2026-09-20T15:40:10.000Z"),
            Answers.File("docs/limits.md", "[\"move\"]", 0, 0, "2026-09-20T15:40:20.000Z"),
            Answers.File("src/config.ts", "[\"move\",\"update\"]", 3, 1, "2026-09-20T15:40:30.000Z"),
            Answers.File("src/odd.ts", "[\"add\",\"delete\"]", 4, 4, "2026-09-20T15:40:40.000Z"),
            Answers.File("src/unsaid.ts", "[]", 1, 1, "2026-09-20T15:40:50.000Z")) + "]");
        var texts = Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhatChanged, json));

        Assert.That(texts[0], Is.EqualTo("6 files changed: 1 new, 1 moved and edited, 1 moved, 1 removed, 1 new, removed, 1 changed"));
        Assert.That(texts.Skip(1).Take(6), Is.EqualTo(new[]
        {
            "New: rate-limit.ts (+57 −0)",
            "Removed: memory-store.ts (+0 −31)",
            "Moved: limits.md (+0 −0)",
            "Moved and edited: config.ts (+3 −1)",
            "New, removed: odd.ts (+4 −4)",
            "Changed: unsaid.ts (+1 −1)",
        }));
    }

    [Test]
    public void AFileShowsByItsPathInItsRepositoryWhereTheSourceResolvedOneAndALowerBoundSaysSo()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var files = (JArray)Intelligence.UnderstandingOf(response)["changes"]!["files"]!;
            files[0]!["repository_path"] = "src/payments/refunds.ts";
            files[0]!["lines_removed_exact"] = false;
            files[1]!["lines_removed_exact"] = true;
        });
        var texts = Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhatChanged, json));
        Assert.That(texts.Skip(1).Take(2), Is.EqualTo(new[]
        {
            "Edited: src/payments/refunds.ts (+5 −1 or more)",
            "Edited: ChargeService.test.ts (+6 −0)",
        }), "a file with no repository path keeps its name; an exact count reads as before");
    }

    [Test]
    public void TheCommitsTheWorkStartedFromAndStandsAtAreSaidUnderTheCount()
    {
        string Anchor(string? head, string? branch) =>
            "{\"head\":" + (head == null ? "null" : "\"" + head + "\"") + ",\"branch\":" + (branch == null ? "null" : "\"" + branch + "\"")
            + ",\"at\":\"2026-09-20T15:40:05.000Z\",\"epistemic\":\"observed\"}";
        string? Line(string? start, string? latest)
        {
            var json = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["revision"] =
                JObject.Parse("{\"at_start\":" + (start ?? "null") + ",\"at_latest_turn_end\":" + (latest ?? "null") + "}"));
            var lines = Answers.Understand(UnderstandPrompt.WhatChanged, json).Lines;
            return lines[1].Text.StartsWith("Edited", StringComparison.Ordinal) ? null : lines[1].Text;
        }
        const string A = "3f9a2c1d8e7b6a5f4c3d2e1f0a9b8c7d6e5f4a3b";
        const string B = "8b1e4d7a2c9f6b3e0d5a8c1f4b7e2d9a6c3f0b5e";
        Assert.That(Line(Anchor(A, "fix/double-charge"), Anchor(B, "fix/double-charge")), Is.EqualTo("From commit 3f9a2c1 to 8b1e4d7 on fix/double-charge"));
        Assert.That(Line(Anchor(A, "main"), Anchor(B, "fix/x")), Is.EqualTo("From commit 3f9a2c1 on main to 8b1e4d7 on fix/x"));
        Assert.That(Line(Anchor(A, "main"), Anchor(A, "main")), Is.EqualTo("At commit 3f9a2c1 on main, where it started"));
        Assert.That(Line(null, Anchor(B, null)), Is.EqualTo("At commit 8b1e4d7"), "a detached HEAD names no branch");
        Assert.That(Line(Anchor(A, "main"), null), Is.EqualTo("Started at commit 3f9a2c1 on main"));
        Assert.That(Line(null, Anchor(null, "main")), Is.EqualTo("In a repository with no commits yet, on main"));
        Assert.That(Line(Anchor(null, "main"), Anchor(null, "main")), Is.EqualTo("In a repository with no commits yet, on main"));
        Assert.That(Line(Anchor(null, null), null), Is.EqualTo("Started in a repository with no commits yet"));
        Assert.That(Line(Anchor(null, "main"), Anchor(B, "main")), Is.EqualTo("From a repository with no commits yet to 8b1e4d7 on main"));
        Assert.That(Line(null, null), Is.Null, "nothing is said when the source saw neither boundary");
        var json = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["revision"] =
            JObject.Parse("{\"at_start\":null,\"at_latest_turn_end\":" + Anchor(B, "main") + "}"));
        var line = Answers.Understand(UnderstandPrompt.WhatChanged, json).Lines[1];
        Assert.That((line.Tag, line.Tone), Is.EqualTo(("observed", SectionTone.Secondary)));
    }

    [Test]
    public void FilesOfOneNameKeepAsManyFoldersAsTellThemApart()
    {
        var json = Answers.Files(Intelligence.Verified, "[" + string.Join(",",
            Answers.File("src/api/index.ts", "[\"update\"]", 1, 0, "2026-09-20T15:40:00.000Z"),
            Answers.File("src/web/index.ts", "[\"update\"]", 2, 0, "2026-09-20T15:40:10.000Z"),
            Answers.File("src/web/views/index.ts", "[\"update\"]", 3, 0, "2026-09-20T15:40:20.000Z"),
            Answers.File("README.md", "[\"update\"]", 4, 0, "2026-09-20T15:40:30.000Z")) + "]");
        var texts = Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhatChanged, json));
        Assert.That(texts.Skip(1).Take(4), Is.EqualTo(new[]
        {
            "Edited: api/index.ts (+1 −0)",
            "Edited: web/index.ts (+2 −0)",
            "Edited: views/index.ts (+3 −0)",
            "Edited: README.md (+4 −0)",
        }));
    }

    [Test]
    public void CommitsAreListedShortAndNothingChangedIsSaidInTheSourcesWords()
    {
        var committed = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["changes"]!["commits"] = JArray.Parse(
            "[{\"sha\":\"0e9269a3c1d2b4e5f6a7b8c9d0e1f2a3b4c5d6e7\",\"at\":\"2026-09-20T15:41:30.000Z\"},{\"sha\":\"27c9dbb\",\"at\":\"2026-09-20T15:42:00.000Z\"}]"));
        Assert.That(Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhatChanged, committed)), Does.Contain("2 commits: 0e9269a, 27c9dbb"));

        var none = Intelligence.Edit(Answers.Files(Intelligence.Verified, "[]"), response =>
            Intelligence.UnderstandingOf(response)["changes"]!["summary"] = "No files changed");
        var section = Answers.Understand(UnderstandPrompt.WhatChanged, none);
        Assert.That(Intelligence.Texts(section)[0], Is.EqualTo("No files changed"));
        Assert.That(Intelligence.Texts(section).Any(text => text.Contains("checked after", StringComparison.Ordinal)), Is.False,
            "no coverage line about no files");
    }

    [Test]
    public void AListThatDoesNotFitKeepsTheMostRecentFilesAndCountsTheRest()
    {
        var files = Enumerable.Range(1, 6).Select(index =>
            Answers.File("src/f" + index + ".ts", "[\"update\"]", index, 0, "2026-09-20T15:40:0" + index + ".000Z"));
        var json = Answers.Files(Intelligence.Verified, "[" + string.Join(",", files) + "]");
        var section = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, Intelligence.ExecutionId,
            Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"), false, null, Intelligence.At(Answers.Now), Intelligence.Utc,
            new AnswerRoom(5));
        Assert.That(Intelligence.Texts(section), Is.EqualTo(new[]
        {
            "6 files changed: 6 edited",
            "Edited: f1.ts (+1 −0)",
            "Edited: f2.ts (+2 −0)",
            "And 4 more files",
            "1 file not checked after the last change: refunds.ts",
        }), "the count, as many files as fit, how many more, and whether a check ran after them; nothing past the room");
        var roomy = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, Intelligence.ExecutionId,
            Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"), false, null, Intelligence.At(Answers.Now), Intelligence.Utc,
            new AnswerRoom(9));
        Assert.That(Intelligence.Texts(roomy).Count(text => text.StartsWith("Edited", StringComparison.Ordinal)), Is.EqualTo(6));
        Assert.That(Intelligence.Texts(roomy), Does.Contain("Planned, not done: Document refund behaviour for support"), "what is not done yet where there is room");
    }
}

public class WhyChangedTests
{
    [Test]
    public void EachReasonIsTheAgentsQuotedInTheOrderItSaidItWithTheFilesChangedAfter()
    {
        var section = Answers.Understand(UnderstandPrompt.WhyChanged, Intelligence.Verified);

        Assert.That(section.Lines.Select(line => (line.Tag, line.Text, line.Tone, line.Detail)), Is.EqualTo(new[]
        {
            ("reported", "Agent says: “I will add an idempotency key in ChargeService so a retried charge returns the first one.”", SectionTone.Claim, false),
            ("", "Said before it changed refunds.ts, RetryWorker.ts and ChargeService.ts", SectionTone.Secondary, true),
            ("", "No reason given before it changed ChargeService.test.ts", SectionTone.Secondary, false),
        }));
        Assert.That(section.Lines[0].Rows, Is.EqualTo(2), "a reason may wrap rather than be cut");
    }

    [Test]
    public void ReasonsComeInTheOrderTheyWereSaidAndKeepTheirAuthor()
    {
        var json = Answers.Files(Intelligence.Verified, "[" + string.Join(",",
            Answers.File("b.ts", "[\"update\"]", 1, 0, "2026-09-20T15:40:00.000Z", reason: "Second, I change b."),
            Answers.File("a.ts", "[\"update\"]", 1, 0, "2026-09-20T15:40:10.000Z", reason: "First, a subagent changes a.", author: "subagent")) + "]");
        json = Intelligence.Edit(json, response =>
            Intelligence.UnderstandingOf(response)["changes"]!["files"]![0]!["reason"]!["at"] = "2026-09-20T15:39:30.000Z");
        var texts = Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhyChanged, json));
        Assert.That(texts, Is.EqualTo(new[]
        {
            "Subagent says: “First, a subagent changes a.”",
            "Said before it changed a.ts",
            "Agent says: “Second, I change b.”",
            "Said before it changed b.ts",
        }));
    }

    [Test]
    public void ManyFilesAreNamedTheFirstFewThenHowManyMore()
    {
        var files = Enumerable.Range(1, 5).Select(index =>
            Answers.File("src/f" + index + ".ts", "[\"update\"]", 1, 0, "2026-09-20T15:40:0" + index + ".000Z", reason: "One reason."));
        var texts = Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhyChanged, Answers.Files(Intelligence.Verified, "[" + string.Join(",", files) + "]")));
        Assert.That(texts[1], Is.EqualTo("Said before it changed f1.ts, f2.ts, f3.ts and 2 more files"));
    }

    [Test]
    public void NoFilesChangedMeansNoReasons()
    {
        var section = Answers.Understand(UnderstandPrompt.WhyChanged, Answers.Files(Intelligence.Verified, "[]"));
        Assert.That(Intelligence.Texts(section), Is.EqualTo(new[] { "No files changed, so there are no reasons to show." }));
    }
}

public class HowBuiltTests
{
    private static IReadOnlyList<SectionPresentation> Steps(string json, int rows = 6) =>
        AnswerPages.Split(Answers.Understand(UnderstandPrompt.HowBuilt, json), new AnswerRoom(rows));

    [Test]
    public void TheExplanationIsAFlowSteppedThroughEachStepSayingItIsAModelsAndCurrent()
    {
        var section = Answers.Understand(UnderstandPrompt.HowBuilt, Intelligence.Verified);
        Assert.That(section.Steps, Is.True);
        var pages = AnswerPages.Split(section, new AnswerRoom(6));
        Assert.That(pages.Select(page => Intelligence.Texts(page)), Is.EqualTo(new[]
        {
            new[] { "What it is about · explained by a model, up to date", "Some customers were charged twice when checkout retried a payment." },
            new[] { "Why · explained by a model, up to date", "Two paths could charge the same order, and neither checked the other.",
                "1. Two charges for one order", "2. The card is billed twice" },
            new[] { "Why, part by part · explained by a model, up to date", "Checkout request", "1. Times out, then retries", "2. Creates a charge" },
            new[] { "Why, part by part · explained by a model, up to date", "Retry worker", "1. Picks the same order up", "2. Creates another charge" },
            new[] { "How · explained by a model, up to date", "One idempotency key per order, sent with every charge.", "Starts at ChargeService.ts",
                "1. Derive a key per order" },
            new[] { "How · explained by a model, up to date", "2. Send it with the charge", "3. The worker reuses it" },
            new[] { "The evidence", "4 files changed: 4 edited", "1 file not checked after the last change: refunds.ts",
                "Tests passed at 15:40: 118/118 tests passed (vitest)" },
        }), "a step too long for a page goes on to the next under its heading again");
        Assert.That(pages.Take(6).Select(page => page.Lines[0].Tone), Has.All.EqualTo(SectionTone.Secondary));
        Assert.That(pages.Select(page => page.Provenance), Has.All.EqualTo("From Salidium 0.6.0, 2 minutes ago"), "where it comes from stays in view");
        Assert.That(AnswerPages.Caption(section, 1, pages.Count), Is.EqualTo("Step 2 of 7"));
    }

    [Test]
    public void EveryLineOfTheExplanationIsAClaimAndTheEvidenceBesideItIsNot()
    {
        var pages = Steps(Intelligence.Verified);
        foreach (var line in pages.Take(6).SelectMany(page => page.Lines).Where(line => line.Tag.Length > 0))
        {
            Assert.That((line.Tag, line.Tone), Is.EqualTo(("explained", SectionTone.Claim)), line.Text);
        }
        Assert.That((pages[2].Lines[1].Tag, pages[2].Lines[1].Tone), Is.EqualTo(("explained", SectionTone.Claim)), "a lane's title is the model's words too");
        Assert.That(pages.Last().Lines.Where(line => line.Tag.Length > 0).Select(line => line.Tag), Is.EqualTo(new[] { "observed", "inferred", "observed" }));
        Assert.That(pages.SelectMany(page => page.Lines).Any(line => line.Text.Contains("claude", StringComparison.OrdinalIgnoreCase)), Is.False,
            "the model's name is not shown");
    }

    [Test]
    public void AnExplanationWrittenBeforeTheLatestEvidenceSaysSoOnEveryStep()
    {
        var older = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["explanation"]!["current"] = false);
        var pages = Steps(older);
        Assert.That(pages.Take(6).Select(page => page.Lines[0].Text), Has.All.EndWith(" · explained by a model before the latest evidence"));
        Assert.That(pages.Take(6).Select(page => page.Lines[0].Tone), Has.All.EqualTo(SectionTone.Attention));
    }

    [Test]
    public void WhatItIsNowAndAChangeOfApproachAreStepsOfTheirOwn()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var content = Intelligence.UnderstandingOf(response)["explanation"]!["content"]!;
            content["what"]!["currently"] = "Retries reuse the first charge.";
            content["why"]!["lanes"] = new JArray();
            content["approach_change"] = JObject.Parse(
                "{\"from\":\"Lock the order row\",\"from_steps\":[\"Lock\"],\"why\":\"Locks timed out under load.\",\"to\":\"One key per order\",\"to_steps\":[\"Key\"]}");
        });
        var pages = Steps(json);
        Assert.That(Intelligence.Texts(pages[0]), Is.EqualTo(new[]
        {
            "What it is about · explained by a model, up to date", "Some customers were charged twice when checkout retried a payment.",
            "Now: Retries reuse the first charge.",
        }));
        var change = pages.Where(page => page.Lines[0].Text.StartsWith("A change of approach", StringComparison.Ordinal))
            .SelectMany(page => page.Lines.Skip(1)).Select(line => line.Text);
        Assert.That(change, Is.EqualTo(new[]
        {
            "At first: Lock the order row", "Then: One key per order", "Because: Locks timed out under load.",
        }));
    }

    [Test]
    public void WithoutAnExplanationItSaysWhyAndShowsTheEvidence()
    {
        var cases = new Dictionary<string, string>
        {
            ["none"] = "No explanation was written for this work.",
            ["disabled"] = "Explanations are turned off on your computer, so there's none for this work.",
            ["generating"] = "An explanation is being written. Until it is, here is the evidence.",
            ["unavailable"] = "No explanation can be written now.",
            ["failed"] = "The explanation could not be written.",
        };
        foreach (var (status, why) in cases)
        {
            var section = Answers.Understand(UnderstandPrompt.HowBuilt, Answers.Explanation(Intelligence.Verified, status));
            Assert.That(section.Steps, Is.False, status);
            Assert.That(Answers.Pairs(section.Lines), Is.EqualTo(new[]
            {
                ("explained", why),
                ("", "The evidence"),
                ("observed", "4 files changed: 4 edited"),
                ("inferred", "1 file not checked after the last change: refunds.ts"),
                ("observed", "Tests passed at 15:40: 118/118 tests passed (vitest)"),
            }), status);
        }
        var generatedWithout = Answers.Understand(UnderstandPrompt.HowBuilt, Answers.Explanation(Intelligence.Verified, "generated"));
        Assert.That(generatedWithout.Steps, Is.False);
        Assert.That(generatedWithout.Lines[0].Text, Is.EqualTo("No explanation can be written now."));
    }

    [Test]
    public void WithoutChecksTheEvidenceSaysSoInTheSourcesWords()
    {
        var json = Intelligence.Edit(Answers.Explanation(Intelligence.Verified, "none"), response =>
        {
            var verification = (JObject)Intelligence.UnderstandingOf(response)["verification"]!;
            verification["latest_by_method"] = new JArray();
            verification["summary"] = "No checks yet";
        });
        Assert.That(Intelligence.Texts(Answers.Understand(UnderstandPrompt.HowBuilt, json)).Last(), Is.EqualTo("No checks yet"));
    }
}

public class UnderstandingAnswersTests
{
    [Test]
    public void EachAvailabilityIsSaidInWordsForEveryAnswer()
    {
        var cases = new Dictionary<string, (string Json, string Provenance)>
        {
            ["not_found"] = (Intelligence.Failure("not_found", "not_observed", "Salidium has not observed this session."),
                "From Salidium · No understanding yet: Salidium has not observed this session."),
            ["unavailable"] = (Intelligence.Failure("unavailable", "not_running", "Salidium is not running: it has not published its discovery file."),
                "From Salidium · Understanding unavailable: Salidium is not running: it has not published its discovery file."),
            ["incompatible"] = (Intelligence.Failure("incompatible", "invalid_document", "The session report does not match Salidium consumer contract v1."),
                "From Salidium · Understanding unreadable: The session report does not match Salidium consumer contract v1."),
            ["unauthorized"] = (Intelligence.Failure("unauthorized", "credential_missing", "No Salidium credential is configured."),
                "From Salidium · Understanding not allowed: No Salidium credential is configured."),
        };
        foreach (UnderstandPrompt prompt in Enum.GetValues(typeof(UnderstandPrompt)))
        {
            foreach (var (availability, (json, provenance)) in cases)
            {
                var section = Answers.Understand(prompt, json);
                Assert.That(section.Provenance, Is.EqualTo(provenance), availability);
                Assert.That(section.Lines, Is.Empty, availability);
                Assert.That(section.Simulated, Is.False);
            }
        }
    }

    [Test]
    public void TheControlPlanesOwnAnswerBeforeAnySourceIsAskedNamesNoSource()
    {
        var notAsked = Intelligence.Failure("not_found", "native_id_unknown", "The runtime has not reported its session id yet.");
        Assert.That(Answers.Understand(UnderstandPrompt.WhatChanged, notAsked).Provenance,
            Is.EqualTo("No understanding yet: The runtime has not reported its session id yet."));
        var section = Answers.Checked(notAsked, notAsked);
        Assert.That(section.Provenance, Is.EqualTo("No understanding yet: The runtime has not reported its session id yet."));
        Assert.That(section.Lines.Single(line => line.Source).Text, Is.EqualTo("No evaluation yet: The runtime has not reported its session id yet."));
    }

    [Test]
    public void ReadingAndFailedReadsAreSaidInWords()
    {
        var now = Intelligence.At(Answers.Now);
        Assert.That(UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, null, null, false, null, now, Intelligence.Utc).Provenance,
            Is.EqualTo("Nothing to understand until work starts."));
        Assert.That(UnderstandingPresenter.Present(UnderstandPrompt.WhyChanged, Intelligence.ExecutionId, null, true, null, now, Intelligence.Utc).Provenance,
            Is.EqualTo("Asking what the understanding source concluded…"));
        var failed = UnderstandingPresenter.Present(UnderstandPrompt.HowBuilt, Intelligence.ExecutionId, null, false, "The control plane could not be reached: refused",
            now, Intelligence.Utc);
        Assert.That(failed.Provenance, Is.EqualTo("Could not read the understanding: The control plane could not be reached: refused"));
        Assert.That(failed.ProvenanceTone, Is.EqualTo(SectionTone.Problem));

        var read = Intelligence.Live(Intelligence.Understanding(Intelligence.Verified), "2026-09-20T16:21:30.000Z");
        Assert.That(UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, Intelligence.ExecutionId, read, true, null, now, Intelligence.Utc).Provenance,
            Is.EqualTo("From Salidium 0.6.0, 2 minutes ago · reading again…"), "the last answer stays while a new one is read");
        Assert.That(UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, Intelligence.ExecutionId, read, false, "timed out", now, Intelligence.Utc).Provenance,
            Is.EqualTo("From Salidium 0.6.0, 2 minutes ago · could not read it again: timed out"));
    }

    [Test]
    public void HowLongAgoIsSaidInWords()
    {
        string Ago(string now) => Answers.Understand(UnderstandPrompt.WhatChanged, Intelligence.Verified, now).Provenance;
        Assert.That(Ago("2026-09-20T16:20:40.000Z"), Does.EndWith(", just now"));
        Assert.That(Ago("2026-09-20T16:19:00.000Z"), Does.EndWith(", just now"), "a source clock ahead of this one is not in the future");
        Assert.That(Ago("2026-09-20T16:21:05.000Z"), Does.EndWith(", 1 minute ago"));
        Assert.That(Ago("2026-09-20T19:30:00.000Z"), Does.EndWith(", 3 hours ago"));
        Assert.That(Ago("2026-09-23T09:00:00.000Z"), Does.EndWith(", on 20 Sep at 16:20"));
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
                understanding["changes"]!["files"]![0]!["reason"]!["epistemic"] = word;
                understanding["verification"]!["latest_by_method"]![0]!["epistemic"] = word;
                understanding["remaining"]!["items"]![0]!["epistemic"] = word;
            });
            Assert.That(Intelligence.Line(Answers.Understand(UnderstandPrompt.WhatChanged, json), "Planned").Tag, Is.EqualTo(word));
            Assert.That(Answers.Understand(UnderstandPrompt.WhyChanged, json).Lines[0].Tag, Is.EqualTo(word), "shown as the source classed it, reported or not");
            Assert.That(Intelligence.Line(Answers.Checked(json, ControlPlaneApiTests.Available), "Tests passed").Tag, Is.EqualTo(word));
        }
    }

    [Test]
    public void ASimulatedAnswerSaysSoAndWhenItWasRecorded()
    {
        var recording = Demonstration.Recording();
        var directed = recording.Understanding.Keys.Single(id => recording.Evaluation[id].Count > 20);
        // The story asks its question first; its first answer runs on to the approval.
        var beginning = recording.Nodes[0];
        var answered = beginning.BranchesAfter(beginning.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Answer).Node;
        var answer = recording.UnderstandingAt(directed, answered, recording.Nodes[answered].Events.Count)!;
        var read = new IntelligenceRead<UnderstandingResponse>(answer.Response, answer.ReadAt, recorded: true);

        var section = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, directed, read, false, null, Intelligence.At("2026-11-20T10:00:00.000Z"),
            Intelligence.Utc);

        Assert.That(section.Provenance, Does.Match(@"^Simulated explanation · recorded at \d\d:\d\d:\d\d$"));
        Assert.That(section.Simulated, Is.True);
        Assert.That(section.ProvenanceTone, Is.EqualTo(SectionTone.Attention));
        Assert.That(Intelligence.Texts(section)[0], Is.EqualTo("2 files changed: 2 new"));
        Assert.That(section.Lines[0].Tag, Is.EqualTo("observed"));
    }

    [Test]
    public void TextFromTheSourceIsShownAsItIsWrittenAndNeverAsMarkupOrControl()
    {
        var hostile = "<color=#f00>Done</color>‮ <sprite=0>​\nnext\tline \\u003Cb\\u003E";
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var understanding = Intelligence.UnderstandingOf(response);
            understanding["verdict"]!["headline"] = hostile;
            understanding["changes"]!["files"]![0]!["path"] = "src/\u202Eevil\u200B.ts";
            understanding["changes"]!["files"]![0]!["reason"]!["text"] = hostile;
            understanding["explanation"]!["content"]!["what"]!["summary"] = hostile;
        });
        var texts = new[]
        {
            Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhatChanged, json))[1],
            Intelligence.Texts(Answers.Understand(UnderstandPrompt.WhyChanged, json))[0],
            Intelligence.Texts(Answers.Understand(UnderstandPrompt.HowBuilt, json))[1],
        };
        Assert.That(texts[2], Is.EqualTo("<color=#f00>Done</color>‹U+202E› <sprite=0>‹U+200B› next line \\u003Cb\\u003E"),
            "markup characters stay as written, and what would not show as itself shows its code");
        foreach (var text in texts)
        {
            Assert.That(text, Does.Contain("‹U+202E›"));
            Assert.That(text.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format), Is.False);
        }
    }
}

public class CheckedTests
{
    [Test]
    public void EachRunSaysWhenItRanAndAfterWhichChangeThenTheMeasurementUnderItsOwnSource()
    {
        var section = Answers.Checked(Intelligence.Verified, ControlPlaneApiTests.Available);

        Assert.That(section.Kind, Is.EqualTo(SectionKind.Checked));
        Assert.That(section.Provenance, Is.EqualTo("From Salidium 0.6.0, on 20 Sep at 16:20"));
        Assert.That(section.Lines.Select(line => (line.Tag, line.Text, line.Detail, line.Source)), Is.EqualTo(new[]
        {
            ("observed", "Tests passed at 15:40: 118/118 tests passed (vitest)", false, false),
            ("", "Then refunds.ts changed, so it no longer covers it", true, false),
            ("inferred", "1 file not checked after the last change: refunds.ts", false, false),
            ("observed", "2 items need attention: Recursive force delete; 1 file changed since the last passing check", false, false),
            ("reported", "Agent says: “All tests pass. The staging key ghp_[GITHUB_TOKEN#1] was never used by the fix.”", false, false),
            ("", "From Seorak, read 1 minute ago", false, true),
            ("Checks", "test: 4 passed of 5 runs (80%)", false, false),
            ("", "available · 1 of 1 session, complete · fresh, data to 17:58:12", true, false),
            ("Cost", "About $1.37. Estimated from token counts at list prices. Not a bill.", false, false),
            ("", "available · 1 of 1 session, complete · fresh, data to 17:58:12", true, false),
            ("Outcome", "commits landed unknown · no tool errors · ended: the person exited", false, false),
            ("", "uncommitted: known once it ends · 3-day line survival: pending", false, false),
            ("", "partly available: not yet computed · 0 of 1 session, incomplete: still being computed, a gap not named · recomputing, no data yet", true, false),
        }));
        Assert.That(Intelligence.Line(section, "Tests passed").Tone, Is.EqualTo(SectionTone.Good));
        Assert.That(Intelligence.Line(section, "Then refunds.ts").Tone, Is.EqualTo(SectionTone.Attention));
        Assert.That(Intelligence.Line(section, "Agent says").Tone, Is.EqualTo(SectionTone.Claim));
        Assert.That(section.Lines.Where(line => line.Detail && line.Tag.Length == 0 && line.Text.StartsWith("available", StringComparison.Ordinal)).Select(line => line.Tone),
            Has.All.EqualTo(SectionTone.Secondary));
        Assert.That(Intelligence.Texts(section).Any(text => text.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
    }

    [Test]
    public void ARunSaysTheChangeItRanAfterOrThatNoneHadHappened()
    {
        var after = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var understanding = Intelligence.UnderstandingOf(response);
            understanding["changes"]!["files"]![0]!["last_changed_at"] = "2026-09-20T15:40:45.000Z";
            understanding["verification"]!["latest_by_method"]![0]!["stale"] = false;
            understanding["verification"]!["latest_by_method"]![0]!["later_unreadable"] = 2;
        });
        var detail = Intelligence.Line(Answers.Checked(after, ControlPlaneApiTests.Available), "Ran after");
        Assert.That((detail.Text, detail.Tone), Is.EqualTo(("Ran after the last change, to refunds.ts at 15:40; 2 later runs couldn't be read", SectionTone.Secondary)));

        var staleOnly = Intelligence.Edit(after, response => Intelligence.UnderstandingOf(response)["verification"]!["latest_by_method"]![0]!["stale"] = true);
        Assert.That(Intelligence.Line(Answers.Checked(staleOnly, ControlPlaneApiTests.Available), "Files changed after").Tone, Is.EqualTo(SectionTone.Attention),
            "the source's own word that files changed since stands even when the times say otherwise");

        var none = Answers.Files(after, "[]");
        Assert.That(Intelligence.Texts(Answers.Checked(none, ControlPlaneApiTests.Available)), Does.Contain("Ran before any file changed; 2 later runs couldn't be read"));

        var two = Intelligence.Edit(Intelligence.Verified, response =>
            Intelligence.UnderstandingOf(response)["changes"]!["files"]![1]!["last_changed_at"] = "2026-09-20T15:41:05.000Z");
        Assert.That(Intelligence.Texts(Answers.Checked(two, ControlPlaneApiTests.Available)),
            Does.Contain("Then refunds.ts and ChargeService.test.ts changed, so it no longer covers them"));
    }

    [Test]
    public void RunsShowInTheOrderTheyRanEachWithItsOutcome()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var runs = (JArray)Intelligence.UnderstandingOf(response)["verification"]!["latest_by_method"]!;
            var build = (JObject)runs[0]!.DeepClone();
            build["method"] = "build";
            build["label"] = "tsc -b";
            build["outcome"] = "fail";
            build["at"] = "2026-09-20T15:39:00.000Z";
            var lint = (JObject)runs[0]!.DeepClone();
            lint["method"] = "lint";
            lint["label"] = "biome check";
            lint["outcome"] = "unknown";
            lint["at"] = "2026-09-20T15:41:30.000Z";
            lint["stale"] = false;
            runs.Add(build);
            runs.Add(lint);
        });
        var section = Answers.Checked(json, ControlPlaneApiTests.Available);
        var runs = section.Lines.Where(line => !line.Detail && line.Tag == "observed" && !line.Text.StartsWith("2 items", StringComparison.Ordinal)).ToList();
        Assert.That(runs.Select(line => (line.Text, line.Tone)), Is.EqualTo(new[]
        {
            ("Build failed at 15:39: tsc -b", SectionTone.Problem),
            ("Tests passed at 15:40: 118/118 tests passed (vitest)", SectionTone.Good),
            ("Lint outcome unknown at 15:41: biome check", SectionTone.Secondary),
        }));
        Assert.That(Intelligence.Texts(section),
            Does.Contain("Then refunds.ts, ChargeService.test.ts, RetryWorker.ts and 1 more file changed, so it no longer covers them"),
            "the build ran before the first change");
        Assert.That(Intelligence.Texts(section), Does.Contain("Ran after the last change, to refunds.ts at 15:41"));
    }

    [Test]
    public void WithNoRunsTheSourcesOwnWordsSaySo()
    {
        var json = Intelligence.Edit(Intelligence.Verified, response =>
        {
            var verification = (JObject)Intelligence.UnderstandingOf(response)["verification"]!;
            verification["latest_by_method"] = new JArray();
            verification["summary"] = "No checks yet";
        });
        var line = Answers.Checked(json, ControlPlaneApiTests.Available).Lines[0];
        Assert.That((line.Tag, line.Text), Is.EqualTo(("", "No checks yet")), "the source's summary carries no class of its own");
    }

    [Test]
    public void EachSourceSaysWhyItHasNothingWhileTheOtherStillAnswers()
    {
        var unobserved = Intelligence.Failure("unavailable", "runtime_not_observed", "Salidium does not observe sessions of the opencode runtime.");
        var section = Answers.Checked(unobserved, ControlPlaneApiTests.Available);
        Assert.That(section.Provenance, Is.EqualTo("From Salidium · Understanding unavailable: Salidium does not observe sessions of the opencode runtime."));
        Assert.That(section.Lines[0].Text, Is.EqualTo("From Seorak, read 1 minute ago"));
        Assert.That(section.Lines.Count, Is.EqualTo(8), "the measurement stands whole");

        var cases = new Dictionary<string, string>
        {
            ["not_found"] = "From Seorak · No evaluation yet: Seorak has not captured this session.",
            ["unavailable"] = "From Seorak · Evaluation unavailable: Seorak has not captured this session.",
            ["incompatible"] = "From Seorak · Evaluation unreadable: Seorak has not captured this session.",
            ["unauthorized"] = "From Seorak · Evaluation not allowed: Seorak has not captured this session.",
        };
        foreach (var (availability, words) in cases)
        {
            var measured = Answers.Checked(Intelligence.Verified, Intelligence.Failure(availability, "some_code", "Seorak has not captured this session."));
            var source = measured.Lines.Single(line => line.Source);
            Assert.That(source.Text, Is.EqualTo(words), availability);
            Assert.That(measured.Lines.Last(), Is.SameAs(source), "nothing measured follows it");
        }
    }

    [Test]
    public void ReadsInFlightOrFailedAreSaidForEachSource()
    {
        var now = Intelligence.At("2026-09-26T18:02:00.000Z");
        Assert.That(CheckedPresenter.Present(null, null, false, null, null, false, null, now, Intelligence.Utc).Provenance,
            Is.EqualTo("Nothing is checked until work starts."));
        var asking = CheckedPresenter.Present(Intelligence.ExecutionId, null, true, null, null, true, null, now, Intelligence.Utc);
        Assert.That(asking.Provenance, Is.EqualTo("Asking what the understanding source concluded…"));
        Assert.That(Intelligence.Texts(asking), Is.EqualTo(new[] { "Asking what the evaluation source measured…" }));
        var failed = CheckedPresenter.Present(Intelligence.ExecutionId, null, false, null, null, false, "timed out", now, Intelligence.Utc);
        Assert.That((failed.Lines[0].Text, failed.Lines[0].Tone), Is.EqualTo(("Could not read the evaluation: timed out", SectionTone.Problem)));
        var notYet = CheckedPresenter.Present(Intelligence.ExecutionId, null, false, null, null, false, null, now, Intelligence.Utc);
        Assert.That(notYet.Provenance + " / " + notYet.Lines[0].Text, Is.EqualTo("Not read yet. / Not read yet."));

        var measured = new IntelligenceRead<EvaluationResponse>(Intelligence.Evaluation(ControlPlaneApiTests.Available), Intelligence.At("2026-09-26T18:01:00.000Z"), false);
        var again = CheckedPresenter.Present(Intelligence.ExecutionId, null, false, null, measured, true, null, now, Intelligence.Utc);
        Assert.That(again.Lines[0].Text, Is.EqualTo("From Seorak, read 1 minute ago · reading again…"));
    }

    [Test]
    public void APartReadsStaleOnceItsStaleAtHasPassedWhateverItsStateSaid()
    {
        var section = Answers.Checked(Intelligence.Verified, ControlPlaneApiTests.Available, now: "2026-09-26T18:06:00.000Z");
        var statuses = Answers.Measurement(section).Where(line => line.Detail).ToList();
        Assert.That(statuses.Count, Is.EqualTo(3), "one statement per part");
        Assert.That(statuses.Select(line => line.Text), Has.All.EndWith("stale since 18:05"));
        Assert.That(statuses.Select(line => line.Tone), Has.All.EqualTo(SectionTone.Attention));
    }

    [Test]
    public void ARecordedAnswerIsDescribedAsOfWhenItWasRecorded()
    {
        var section = Answers.Checked(Intelligence.Verified, ControlPlaneApiTests.Available, now: "2026-11-20T10:00:00.000Z", recorded: true);
        Assert.That(section.Provenance, Is.EqualTo("From Salidium 0.6.0, recorded at 16:21:30"));
        Assert.That(section.Lines.Single(line => line.Source).Text, Is.EqualTo("From Seorak, recorded at 18:01:00"));
        Assert.That(Answers.Measurement(section)[1].Text, Does.EndWith("fresh, data to 17:58:12"), "not stale on this device's later clock");
    }

    [Test]
    public void WhatTheMeasurementDoesNotHaveReadsAsUnknownOrPendingNeverZero()
    {
        var json = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
        {
            var evaluation = Intelligence.EvaluationOf(response);
            evaluation["cost"]!["estimated_usd"] = null;
            evaluation["outcome"]!["availability"] = JObject.Parse("{\"state\":\"unavailable\",\"reason\":\"not_captured\"}");
            evaluation["outcome"]!["measure"] = null;
            evaluation["verification"]!["lens"]!["by_kind"] = JArray.Parse("[{\"label\":\"test\",\"runs\":null,\"passed\":null,\"pass_rate\":null}]");
        });
        var lines = Answers.Measurement(Answers.Checked(Intelligence.Verified, json));
        Assert.That(Answers.Pairs(lines.Where(line => !line.Detail)), Is.EqualTo(new[]
        {
            ("Checks", "test: passes unknown of runs unknown (no pass rate)"),
            ("Cost", "Unknown: unpriced, or not measured yet."),
            ("Outcome", "Nothing measured yet."),
        }));
        Assert.That(lines.Last().Text, Does.StartWith("unavailable: not captured"));

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
        var outcome = Answers.Measurement(Answers.Checked(Intelligence.Verified, measured));
        Assert.That((outcome[4].Text, outcome[4].Tone),
            Is.EqualTo(("no commits landed · 2 tool errors, the first at 17:44 · not ended, or its end not captured", SectionTone.Attention)));
        Assert.That(outcome[5].Text, Is.EqualTo("uncommitted: 3 files, +41 −7 (120 generated lines apart) · lines kept after 3 days: 90% (36 of 40 lines)"));

        var noLens = Intelligence.Edit(ControlPlaneApiTests.Available, response => Intelligence.EvaluationOf(response)["verification"]!["lens"] = null);
        Assert.That(Answers.Measurement(Answers.Checked(Intelligence.Verified, noLens))[0].Text, Is.EqualTo("No verification lens."));
        var empty = Intelligence.Edit(ControlPlaneApiTests.Available, response =>
            Intelligence.EvaluationOf(response)["verification"]!["lens"] = JObject.Parse("{\"by_kind\":[],\"empty_reason\":\"No verification result was captured.\"}"));
        Assert.That(Answers.Measurement(Answers.Checked(Intelligence.Verified, empty))[0].Text, Is.EqualTo("No verification result was captured."));
    }

    [Test]
    public void TheCostIsAlwaysTheSourcesEstimateWithItsNote()
    {
        foreach (var (usd, amount) in new[] { (0.0, "less than $0.01"), (0.004, "less than $0.01"), (0.387, "$0.39"), (12.5, "$12.50") })
        {
            var json = Intelligence.Edit(ControlPlaneApiTests.Available, response => Intelligence.EvaluationOf(response)["cost"]!["estimated_usd"] = usd);
            Assert.That(Intelligence.Line(Answers.Checked(Intelligence.Verified, json), "About").Text,
                Is.EqualTo("About " + amount + ". Estimated from token counts at list prices. Not a bill."));
        }
    }

    [Test]
    public void ASimulatedAnswerSaysSoForEachSource()
    {
        var understanding = Intelligence.Edit(Intelligence.Verified, response => Intelligence.UnderstandingOf(response)["source"]!["synthetic"] = true);
        var evaluation = Intelligence.Edit(ControlPlaneApiTests.Available, response => Intelligence.EvaluationOf(response)["source"]!["synthetic"] = true);
        var both = Answers.Checked(understanding, evaluation, recorded: true);
        Assert.That(both.Provenance, Is.EqualTo("Simulated checks · recorded at 16:21:30"));
        Assert.That(both.ProvenanceTone, Is.EqualTo(SectionTone.Attention));
        var source = both.Lines.Single(line => line.Source);
        Assert.That((source.Text, source.Tone), Is.EqualTo(("Simulated measurement · recorded at 18:01:00", SectionTone.Attention)));
        Assert.That(both.Simulated, Is.True);
        Assert.That(Answers.Checked(Intelligence.Verified, evaluation).Simulated, Is.True, "either source simulated makes the answer simulated");
    }
}

public class AnswerPagesTests
{
    private static SectionLine Line(string text, int rows = 0, bool detail = false, bool source = false, bool startsPage = false, bool repeats = false) =>
        new("", text, SectionTone.Normal, detail, rows, source, startsPage, repeats);

    private static string[][] Split(int rows, params SectionLine[] lines) =>
        AnswerPages.Split(new SectionPresentation(SectionKind.Checked, "From somewhere", SectionTone.Secondary, lines, simulated: false), new AnswerRoom(rows))
            .Select(page => page.Lines.Select(line => line.Text).ToArray()).ToArray();

    [Test]
    public void FillsEachPageWithTheRowsThatFitAndKeepsADetailWithItsLine()
    {
        Assert.That(Split(4, Line("a"), Line("b", rows: 2), Line("c"), Line("d"), Line("d's detail", detail: true), Line("e")), Is.EqualTo(new[]
        {
            new[] { "a", "b", "c" },
            new[] { "d", "d's detail", "e" },
        }), "a detail takes two rows unless told otherwise");
    }

    [Test]
    public void AProvenanceLineNeverEndsAPage()
    {
        Assert.That(Split(2, Line("a"), Line("From elsewhere", source: true), Line("b")), Is.EqualTo(new[]
        {
            new[] { "a" },
            new[] { "From elsewhere", "b" },
        }));
    }

    [Test]
    public void EachStepStartsAPageAndAStepThatGoesOnShowsItsHeadingAgain()
    {
        Assert.That(Split(4,
            Line("note", startsPage: true, repeats: true), Line("Why", repeats: true), Line("w1"),
            Line("note", startsPage: true, repeats: true), Line("How", repeats: true), Line("h1", rows: 2), Line("h2"), Line("h3")), Is.EqualTo(new[]
        {
            new[] { "note", "Why", "w1" },
            new[] { "note", "How", "h1" },
            new[] { "note", "How", "h2", "h3" },
        }));
        Assert.That(Split(5,
            Line("note", startsPage: true, repeats: true), Line("h1"), Line("h2"), Line("h3"), Line("h4"), Line("h5")), Is.EqualTo(new[]
        {
            new[] { "note", "h1", "h2", "h3" },
            new[] { "note", "h4", "h5" },
        }), "a step never goes on with a single line where the page before can spare one");
    }

    [Test]
    public void AnEmptyAnswerIsOnePageAndEveryPageKeepsTheProvenance()
    {
        var pages = AnswerPages.Split(new SectionPresentation(SectionKind.Understanding, "Not read yet.", SectionTone.Secondary, Array.Empty<SectionLine>(), false), new AnswerRoom(6));
        Assert.That(pages.Count, Is.EqualTo(1));
        Assert.That(pages[0].Provenance, Is.EqualTo("Not read yet."));
        Assert.That(AnswerPages.Caption(pages[0], 0, 3), Is.EqualTo("Part 1 of 3"));
    }
}

public class AnswerWordsTests
{
    [Test]
    public void OnlyAProvenanceLineNamesAProductAndNoAnswerNamesABrand()
    {
        var sections = Enum.GetValues(typeof(UnderstandPrompt)).Cast<UnderstandPrompt>()
            .Select(prompt => Answers.Understand(prompt, Intelligence.Verified))
            .Append(Answers.Checked(Intelligence.Verified, ControlPlaneApiTests.Available))
            .ToList();
        var products = new[] { "Salidium", "Seorak" };
        foreach (var section in sections)
        {
            Assert.That(section.Provenance, Does.Contain("Salidium"));
            foreach (var line in section.Lines.Where(line => !line.Source))
            {
                foreach (var product in products) Assert.That(line.Text + line.Tag, Does.Not.Contain(product));
            }
            foreach (var line in section.Lines.Prepend(new SectionLine("", section.Provenance, SectionTone.Normal)))
            {
                foreach (var brand in new[] { "Meta", "Quest", "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode" })
                {
                    Assert.That(line.Text, Does.Not.Contain(brand), "the model that wrote the explanation is not named");
                }
            }
        }
    }

    [Test]
    public void HalcyonicsOwnWordsFollowTheGuide()
    {
        var words = new[]
        {
            "How · explained by a model, up to date",
            "How · explained by a model before the latest evidence",
            "No explanation was written for this work.",
            "Explanations are turned off on your computer, so there's none for this work.",
            "An explanation is being written. Until it is, here is the evidence.",
            "No files changed, so there are no reasons to show.",
            "Nothing is checked until work starts.",
        };
        foreach (var text in words)
        {
            Assert.That(text, Does.Not.Contain("!"));
            Assert.That(text, Does.Not.Contain("—"));
            Assert.That(text.Split(". ").Length, Is.LessThanOrEqualTo(2), "at most two short sentences");
        }
    }
}
