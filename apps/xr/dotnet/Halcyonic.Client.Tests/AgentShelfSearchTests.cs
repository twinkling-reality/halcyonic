using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Halcyonic.Client;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The one shelf search the headset's placement and the Editor's measured search share, in ART's order (X18, 2026-10-31).</summary>
public sealed class AgentShelfSearchTests
{
    private static readonly Vector3 Eyes = new Vector3(0f, 1.6f, 0f);

    // A console from 15 degrees left to 15 right, its lower edge 10 degrees down.
    private static readonly ConsoleExtent Console = new ConsoleExtent(-15f, 15f, -10f);

    [Test]
    public void TheFirstSpotIsTheDesignedHomeBesideTheConsolesRightFoot()
    {
        var home = AgentShelfSearch.Candidates(Eyes, 0f, Console).First();
        var half = MathF.Atan(AgentShelfDraft.Width(1) / 2f / 1.9f) * 180f / MathF.PI;
        Assert.That(home.Yaw, Is.EqualTo((15f + half) * Widening(home) + AgentShelfSearch.HomeGapDegrees).Within(1e-3f), "its near edge 1.5 degrees outside the frame, widened");
        Assert.That(home.Yaw, Is.GreaterThanOrEqualTo(15f + AgentShelfSearch.HomeGapDegrees + half), "never nearer than ART's gap");
        Assert.That(Horizontal(home), Is.EqualTo(1.9f).Within(1e-4f), "at the home's depth, not the console's");
        var consoleFoot = 1.9f * MathF.Tan(10f * MathF.PI / 180f);
        Assert.That(Eyes.Y - home.Position.Y, Is.EqualTo(consoleFoot + AgentShelfSearch.HomeDropBelowConsole).Within(1e-4f), "its top just under the lower edge");
    }

    [Test]
    public void ItSearchesTheRightSideFirstOutwardThenTheLeftMirroredTheFarTurnLast()
    {
        var candidates = AgentShelfSearch.Candidates(Eyes, 0f, Console).ToList();
        var yaws = candidates.Select(candidate => candidate.Yaw).Distinct().ToList();
        var right = yaws.TakeWhile(yaw => yaw > 0f).ToList();
        var left = yaws.SkipWhile(yaw => yaw > 0f).ToList();
        Assert.That(right.Concat(left), Is.EqualTo(yaws), "every right candidate before any left one");
        Assert.That(right, Is.Ordered.Ascending, "the smallest turn from home first");
        Assert.That(right.Zip(right.Skip(1), (a, b) => b - a).Take(right.Count - 2), Has.All.EqualTo(AgentShelfSearch.TurnStepDegrees).Within(1e-3f));
        Assert.That(right[^1], Is.EqualTo(AgentShelfSearch.MaximumTurnDegrees), "the far turn last, never first");
        Assert.That(left, Is.Ordered.Descending);
        Assert.That(left[0], Is.LessThan(-15f), "beside the console's left edge");
        Assert.That(left[^1], Is.EqualTo(-AgentShelfSearch.MaximumTurnDegrees));
        Assert.That(yaws, Has.All.InRange(-AgentShelfSearch.MaximumTurnDegrees, AgentShelfSearch.MaximumTurnDegrees));
    }

    [Test]
    public void AtEachTurnTheHomesDepthThenNearerThenFartherAndTheHomesHeightThenLowerThenHigher()
    {
        var home = AgentShelfSearch.Candidates(Eyes, 0f, Console).First();
        var first = AgentShelfSearch.Candidates(Eyes, 0f, Console).Where(candidate => candidate.Yaw == home.Yaw).ToList();
        var distances = first.Select(Horizontal).Select(d => MathF.Round(d, 3)).Distinct().ToList();
        Assert.That(distances, Is.EqualTo(new[] { 1.9f, 1.8f, 1.5f, 1.2f, 2f }), "the home's depth, nearer, then farther");
        var drops = first.Take(first.Count / distances.Count).Select(candidate => MathF.Round(Eyes.Y - candidate.Position.Y, 3)).ToList();
        var homeDrop = drops[0];
        Assert.That(drops.Skip(1).TakeWhile(drop => drop > homeDrop), Is.Ordered.Ascending, "then lower");
        Assert.That(drops.Skip(1).SkipWhile(drop => drop > homeDrop), Is.Ordered.Descending, "then higher");
        Assert.That(drops.Skip(1).SkipWhile(drop => drop > homeDrop), Has.All.LessThan(homeDrop));
        Assert.That(drops.Skip(1).TakeWhile(drop => drop > homeDrop).Count(), Is.GreaterThan(0), "lower ones come before any higher one");
    }

    /// <summary>
    /// Beside a console at touch distance the home stands at 1.9 m, where a grounded figure (0.22 m) reads at its designed 6.6
    /// degrees, never at the console's 0.46 m (ART's addendum (2), LARGER-SHELF, 2026-10-31).
    /// </summary>
    [Test]
    public void BesideANearConsoleTheHomeStandsAtOnePointNineMetres()
    {
        var near = new ConsoleExtent(-28.4f, 28.4f, -15.6f);
        var home = AgentShelfSearch.Candidates(Eyes, 0f, near).First();
        Assert.That(AgentShelfSearch.HomeDistance, Is.EqualTo(1.9f));
        Assert.That(Horizontal(home), Is.EqualTo(1.9f).Within(1e-4f));
        var figure = 2f * MathF.Atan(0.22f / 2f / Horizontal(home)) * 180f / MathF.PI;
        Assert.That(figure, Is.InRange(6f, 9f), "the figure's apparent height, ART's render range");
        var half = MathF.Atan(AgentShelfDraft.Width(1) / 2f / 1.9f) * 180f / MathF.PI;
        Assert.That(home.Yaw, Is.EqualTo((28.4f + half) * Widening(home) + AgentShelfSearch.HomeGapDegrees).Within(1e-3f), "still just right of the console's edge");
    }

    /// <summary>
    /// The home's near edge is the whole envelope's, its label included: at 1.9 m a label that keeps its size by angle is wider
    /// than the bare shelf, and the home stands that much farther out so its 1.5 degree gap holds (LARGER-SHELF L1, 2026-10-31;
    /// the gate measured a 10.9 degree label beside a 9.1 degree shelf). An envelope narrower than the shelf never narrows it.
    /// </summary>
    [Test]
    public void TheHomesNearEdgeIsTheWholeEnvelopesLabelIncluded()
    {
        var bare = MathF.Atan(AgentShelfDraft.Width(1) / 2f / AgentShelfSearch.HomeDistance) * 180f / MathF.PI;
        var asked = new List<float>();
        var labelled = AgentShelfSearch.Candidates(Eyes, 0f, Console, distance => { asked.Add(distance); return 5.45f; }).First();
        Assert.That(labelled.Yaw, Is.EqualTo((15f + 5.45f) * Widening(labelled) + AgentShelfSearch.HomeGapDegrees).Within(1e-3f), "beside the label's near edge");
        Assert.That(asked, Is.EqualTo(new[] { AgentShelfSearch.HomeDistance }), "measured once, at the home's distance");
        var narrow = AgentShelfSearch.Candidates(Eyes, 0f, Console, _ => 1f).First();
        Assert.That(narrow.Yaw, Is.EqualTo((15f + bare) * Widening(narrow) + AgentShelfSearch.HomeGapDegrees).Within(1e-3f), "never nearer than the bare shelf allows");
        var left = AgentShelfSearch.Candidates(Eyes, 0f, Console, _ => 5.45f).First(candidate => candidate.Yaw < 0f);
        Assert.That(left.Yaw, Is.EqualTo(-labelled.Yaw).Within(1e-3f), "mirrored on the left");
        var draft = new AgentShelfDraft();
        Assert.That(AgentShelfSearch.Begin(draft, Eyes, 0f, out _, Console, _ => 5.45f), Is.True);
        Assert.That(draft.Candidate!.Value.Yaw, Is.EqualTo(labelled.Yaw).Within(1e-4f), "Begin offers the same home");
    }

    /// <summary>
    /// The home keeps the plane's own clearance of the shelf's character (WorkspacePlacement: the plane's and the character's
    /// half-widths widened below eye level, and 1.5 degrees), so a plane laid again later still stands clear of it; and no farther
    /// out than that (LARGER-SHELF L1, 2026-10-31: at 1.9 m the gate found the plane over the character by its widening).
    /// </summary>
    [Test]
    public void TheHomeKeepsThePlanesOwnClearanceOfTheCharacterBelowEyeLevel()
    {
        var console = new ConsoleExtent(-18f, 18f, -15.6f);
        var home = AgentShelfSearch.Candidates(Eyes, 0f, console, _ => 5.45f).First();
        var elevation = MathF.Atan2(home.Position.Y - Eyes.Y, Horizontal(home)) * 180f / MathF.PI;
        // As wide as the console, so tall that it is never under a label or over a body: clear only when beside it.
        var plane = new PanelSize(0.46f, 0.46f * MathF.Tan(18f * MathF.PI / 180f), 0.5f);
        var ahead = new PanelDirection(0f, 0f, true, false);
        BodyInView At(float yaw) => new BodyInView(yaw, elevation, 0.05f, elevation - 10f, 5.45f);
        Assert.That(WorkspacePlacement.Clears(ahead, new[] { At(home.Yaw) }, plane), Is.True, "the console clear of the character at home");
        Assert.That(WorkspacePlacement.Clears(ahead, new[] { At(home.Yaw - 0.1f) }, plane), Is.False, "and no farther out than it needs");
        var left = AgentShelfSearch.Candidates(Eyes, 0f, console, _ => 5.45f).First(candidate => candidate.Yaw < 0f);
        Assert.That(left.Yaw, Is.EqualTo(-home.Yaw).Within(1e-3f), "mirrored on the left");
    }

    [Test]
    public void ItNeverOffersAShelfNearerThanOnePointTwoMetres()
    {
        foreach (var console in new ConsoleExtent?[] { Console, new ConsoleExtent(-5f, 5f, -30f), null })
        {
            var candidates = AgentShelfSearch.Candidates(Eyes, 0f, console).ToList();
            Assert.That(candidates.Min(Horizontal), Is.GreaterThanOrEqualTo(AgentShelfSearch.MinimumDistance - 1e-4f), console?.RightYaw.ToString() ?? "no console");
            // Each stands inside the draft's own supported region, so none is refused for where it is alone.
            foreach (var candidate in candidates)
            {
                var draft = new AgentShelfDraft();
                draft.Begin(candidate, Eyes);
                Assert.That(draft.Candidate, Is.Not.Null, candidate.Position + " " + candidate.Yaw);
            }
        }
        Assert.That(AgentShelfSearch.MinimumDistance, Is.EqualTo(1.2f), "ART's rule for characters; the person's own moves may go to the draft's 0.4 m");
    }

    [Test]
    public void ACandidateThePredicatesRefuseIsNeverTheResult()
    {
        var all = AgentShelfSearch.Candidates(Eyes, 0f, Console).ToList();
        var keep = all[all.Count / 2];
        var offered = new List<AgentShelfPose>();
        var draft = new AgentShelfDraft { Allows = pose => { offered.Add(pose); return Same(pose, keep); } };
        Assert.That(AgentShelfSearch.Begin(draft, Eyes, 0f, out var tried, Console), Is.True);
        Assert.That(tried, Is.EqualTo(all.Count / 2 + 1));
        Assert.That(Same(draft.Candidate!.Value, keep), Is.True, "the first candidate the predicates keep");
        Assert.That(offered.Take(offered.Count - 1).Any(pose => Same(pose, keep)), Is.False, "everything before it was refused");

        var refuser = new AgentShelfDraft { Allows = _ => false };
        Assert.That(AgentShelfSearch.Begin(refuser, Eyes, 0f, out var every, Console), Is.False);
        Assert.That(every, Is.EqualTo(all.Count));
        Assert.That(refuser.Candidate, Is.Null, "no fallback when every candidate is refused");
    }

    [Test]
    public void ItTurnsWithWhereThePersonFaces()
    {
        var ahead = AgentShelfSearch.Candidates(Eyes, 0f).ToList();
        var turned = AgentShelfSearch.Candidates(Eyes, 90f).ToList();
        Assert.That(turned, Has.Count.EqualTo(ahead.Count));
        Assert.That(turned[0].Yaw, Is.EqualTo(ahead[0].Yaw + 90f).Within(1e-3f));
        Assert.That(turned.Select(candidate => candidate.Yaw), Has.All.InRange(50f, 130f));
        var radians = turned[0].Yaw * MathF.PI / 180f;
        Assert.That(turned[0].Position.X - Eyes.X, Is.EqualTo(MathF.Sin(radians) * Horizontal(turned[0])).Within(1e-4f));
    }

    /// <summary>
    /// A shelf placed while only the menu shows stays clear once a wider file column takes the menu's place: the home is beside the
    /// widest console the workspace can take (X18, 2026-10-31; AppearanceFlowCheck showed the file covering it before).
    /// </summary>
    [Test]
    public void TheHomeStandsBesideTheWidestConsoleSoAFileOpeningLaterNeverCoversIt()
    {
        var menu = new ConsoleExtent(-18f, 18f, -13f);
        var widest = menu.WidenedTo(0.6f, 0.68f);
        var extra = (MathF.Atan(0.34f) - MathF.Atan(0.3f)) * 180f / MathF.PI;
        Assert.That((widest.LeftYaw, widest.RightYaw), Is.EqualTo((-18f - extra, 18f + extra)).Within(1e-3f), "both edges out, about the same middle");
        Assert.That(widest.BottomElevation, Is.EqualTo(-13f));
        Assert.That(menu.WidenedTo(0.68f, 0.6f).RightYaw, Is.EqualTo(18f), "never narrower than what shows");
        var besideMenu = AgentShelfSearch.Candidates(Eyes, 0f, menu).First();
        var besideWidest = AgentShelfSearch.Candidates(Eyes, 0f, widest).First();
        Assert.That(besideWidest.Yaw - besideMenu.Yaw, Is.EqualTo(extra * Widening(besideMenu)).Within(0.01f), "the home moves out with the wider console's edge");
    }

    /// <summary>The plane's clearance widening at the shelf's place below eye level, as WorkspacePlacement measures a body's.</summary>
    private static float Widening(AgentShelfPose pose) => 1f / MathF.Max(MathF.Cos(MathF.Atan2(Eyes.Y - pose.Position.Y, Horizontal(pose))), 0.3f);

    private static float Horizontal(AgentShelfPose pose) => new Vector2(pose.Position.X - Eyes.X, pose.Position.Z - Eyes.Z).Length();

    private static bool Same(AgentShelfPose a, AgentShelfPose b) => Vector3.Distance(a.Position, b.Position) < 1e-5f && Math.Abs(a.Yaw - b.Yaw) < 1e-4f;
}
