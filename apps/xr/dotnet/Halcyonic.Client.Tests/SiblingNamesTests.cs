using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// No screen names a sibling product, in any build (the lead's ruling, 2026-11-13; LANE-RULES 19): where an understanding
/// or a measurement came from is said in the product's own words, with the capture extra or without it, a provider's id
/// and the version it names for itself stay data, and the host's message for why there is none is never drawn. The
/// source line hands what is missing and why apart and is drawn as one phrase, never joined on a dot. The tooling guard
/// (tooling/sibling-names.test.ts) holds every drawable literal to the same.
/// </summary>
[TestFixture, NonParallelizable]
public class SiblingNamesTests
{
    private static readonly string[] Siblings = { "Salidium", "Seorak" };

    [TearDown]
    public void Off() => CaptureNames.UseGeneric(false);

    [TestCase(false)]
    [TestCase(true)]
    public void NoAnswerOrSourceLineNamesASiblingProductOrJoinsOnADot(bool generic)
    {
        CaptureNames.UseGeneric(generic);
        foreach (var section in Answers.Everything())
        {
            Assert.That(section.Provenance, Does.Not.Contain(" · "), "the source line, " + section.Provenance);
            foreach (var line in section.Lines.Where(line => line.Source))
                Assert.That(line.Text, Does.Not.Contain(" · "), "a second source's line, " + line.Text);
            foreach (var text in Drawn(section))
                foreach (var sibling in Siblings)
                    Assert.That(text, Does.Not.Contain(sibling).IgnoreCase, "drawn: " + text);
        }
    }

    /// <summary>
    /// The three cases a person meets on Checks: an answer, a source with none (its host's message naming the provider and a
    /// command for it, as the host's own failure text does), and this headset's read failed.
    /// </summary>
    [Test]
    public void ChecksSaysWhereItCameFromInItsOwnWordsInEachOfTheThreeCases()
    {
        var answered = Answers.Checked(Intelligence.Verified, ControlPlaneApiTests.Available);
        Assert.That(answered.Provenance, Is.EqualTo("Explanation, 20 Sep 16:20"));
        Assert.That((answered.From!.Source, answered.From.State, answered.From.Why), Is.EqualTo(("Explanation", (string?)null, (Notice?)null)));
        Assert.That(answered.Lines.First(line => line.Source).Text, Is.EqualTo("Measurement, Read 1 min ago"));

        var none = Answers.Checked(
            Intelligence.Failure("unauthorized", "credential_missing", "No Salidium credential is configured. Create one with `salidium consumer create halcyonic`."),
            Intelligence.Failure("unavailable", "not_running", "Seorak is not running."));
        Assert.That(none.Provenance, Is.EqualTo("Conclusions Not Allowed, Credential Missing, set one up on your computer"));
        Assert.That((none.From!.State, none.From.Why?.Title), Is.EqualTo(("Conclusions Not Allowed", "Credential Missing")), "what is missing and why, apart");
        var measurement = none.Lines.First(line => line.Source);
        Assert.That(measurement.Text, Is.EqualTo("Measurement Unavailable, Not Running, start it on your computer"));
        Assert.That((measurement.From!.State, measurement.From.Why?.Title), Is.EqualTo(("Measurement Unavailable", "Not Running")));
        foreach (var text in Drawn(none))
        {
            foreach (var sibling in Siblings) Assert.That(text, Does.Not.Contain(sibling).IgnoreCase, "drawn: " + text);
            Assert.That(text, Does.Not.Contain("consumer create"), "the host's message is never drawn");
        }

        var unread = CheckedPresenter.Present(Intelligence.ExecutionId, null, false, "The computer took too long to answer.", null, false, null,
            Intelligence.At("2026-09-26T18:02:00.000Z"), Intelligence.Utc);
        Assert.That(unread.Provenance, Is.EqualTo("Understanding Unreadable, The computer took too long to answer."));
        Assert.That(unread.From!.State, Is.EqualTo("Understanding Unreadable"));
    }

    /// <summary>
    /// The measurement's and the understanding's read failures are this headset's own sentences: the error a read keeps is
    /// IntelligenceText.WhyUnread's, never the exception's message, so a message naming the provider and a command for it
    /// never reaches the Checks page (AUDIT's MUST O, 2026-11-13).
    /// </summary>
    [Test]
    public void AFailedReadNeverCarriesItsMessageToTheChecksPage()
    {
        const string Leak = "Seorak refused at http://127.0.0.1:7600; run salidium consumer create";
        foreach (var error in new Exception[]
        {
            new OperationCanceledException(Leak),
            new ControlPlaneRequestException(Leak, new System.Net.Http.HttpRequestException(Leak)),
            new ControlPlaneRequestException(Leak, "too_many_requests", 429),
            new InvalidOperationException(Leak),
        })
        {
            var why = IntelligenceText.WhyUnread(error, "Access Refused");
            var section = CheckedPresenter.Present(Intelligence.ExecutionId, null, false, why, null, false, why,
                Intelligence.At("2026-09-26T18:02:00.000Z"), Intelligence.Utc);
            foreach (var text in Drawn(section).Append(why))
            {
                foreach (var sibling in Siblings) Assert.That(text, Does.Not.Contain(sibling).IgnoreCase, "drawn: " + text);
                Assert.That(text, Does.Not.Contain("consumer create").And.Not.Contain("127.0.0.1"), "drawn: " + text);
            }
        }
    }

    [Test]
    public void UsageSaysWhereItsRowsComeFromInItsOwnWords()
    {
        Assert.That(UsageLeftPresenter.FromSource, Is.EqualTo("Measurement, Provider Reported"));
        Assert.That(UsageLeftPresenter.Simulated, Is.EqualTo("Simulated Measurement"));
    }

    /// <summary>Every text a section can put on a screen: its source line and each line's tag, words and chip.</summary>
    private static IEnumerable<string> Drawn(SectionPresentation section) =>
        new[] { section.Provenance }.Concat(section.Lines.SelectMany(line => new[] { line.Tag, line.Text, line.Words, line.Chip ?? "" }));
}
