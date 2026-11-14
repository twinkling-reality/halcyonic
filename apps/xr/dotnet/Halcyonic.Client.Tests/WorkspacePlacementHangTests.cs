using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's anchored bar with the band hanging under it (the lead's 2026-11-14T00:24Z; AUDIT's 00:27Z): a body clear of the bar's
/// own box but inside the band's reach is not clear of the plane, and one beside the bar, above the band, still is.
/// </summary>
public class WorkspacePlacementHangTests
{
    private static PanelSize Box(float halfWidthDegrees, float halfHeightDegrees) =>
        new(1f, MathF.Tan(halfWidthDegrees * MathF.PI / 180f), MathF.Tan(halfHeightDegrees * MathF.PI / 180f));

    [Test]
    public void ABodyUnderTheBandIsNotClearThoughItClearsTheBar()
    {
        var at = new PanelDirection(0f, -25f, true, false);
        var bar = Box(14f, 5f);
        var band = Box(27f, 5f);
        var under = new[] { new BodyInView(0f, -40f, 1f) };
        Assert.That(WorkspacePlacement.Clears(at, under, bar), Is.True, "the bar alone stands wholly above it");
        Assert.That(WorkspacePlacement.ClearsWithHang(at, under, bar, band, hangBelowDegrees: 15f), Is.False, "the band hangs over it");
    }

    [Test]
    public void ABodyFarBesideBothStaysClear()
    {
        var at = new PanelDirection(0f, -25f, true, false);
        var beside = new[] { new BodyInView(60f, -40f, 1f) };
        Assert.That(WorkspacePlacement.ClearsWithHang(at, beside, Box(14f, 5f), Box(27f, 5f), 15f), Is.True);
    }
}
