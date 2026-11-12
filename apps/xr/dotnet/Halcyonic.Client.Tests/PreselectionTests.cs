#nullable enable
using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>One rule for every choice (the lead, 2026-11-12): one ready answer is taken; several, the last used here, else anywhere; else asked.</summary>
public sealed class PreselectionTests
{
    private static readonly string A = "a", B = "b", C = "c";

    [Test]
    public void OneReadyAnswerIsTakenWhateverWasUsedBefore() =>
        Assert.That(Preselection.Pick(new[] { A }, new[] { B }, new[] { C }), Is.SameAs(A));

    [Test]
    public void SeveralTakeTheLastUsedHereElseAnywhere()
    {
        Assert.That(Preselection.Pick(new[] { A, B, C }, new[] { B, A }, new[] { C }), Is.SameAs(B), "the newest used here");
        Assert.That(Preselection.Pick(new[] { A, B, C }, Array.Empty<string>(), new[] { C, A }), Is.SameAs(C), "else the newest anywhere");
        Assert.That(Preselection.Pick(new[] { A, B }, new[] { "gone" }, new[] { "gone too", B }), Is.SameAs(B), "an answer no longer ready is passed over");
    }

    [Test]
    public void SeveralWithNoneUsedAndZeroReadyAreAsked()
    {
        Assert.That(Preselection.Pick(new[] { A, B }, Array.Empty<string>(), Array.Empty<string>()), Is.Null, "several, none used: asked");
        Assert.That(Preselection.Pick(Array.Empty<string>(), new[] { A }, new[] { A }), Is.Null, "none ready: nothing taken");
    }
}
