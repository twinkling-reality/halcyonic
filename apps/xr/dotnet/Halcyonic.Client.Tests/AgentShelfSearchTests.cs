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

    // A console from 15 degrees left to 15 right, its lower edge 10 degrees down, 1.5 m away.
    private static readonly ConsoleExtent Console = new ConsoleExtent(-15f, 15f, -10f, 1.5f);

    [Test]
    public void TheFirstSpotIsTheDesignedHomeBesideTheConsolesRightFoot()
    {
        var home = AgentShelfSearch.Candidates(Eyes, 0f, Console).First();
        var half = MathF.Atan(AgentShelfDraft.Width(1) / 2f / 1.5f) * 180f / MathF.PI;
        Assert.That(home.Yaw, Is.EqualTo(15f + AgentShelfSearch.HomeGapDegrees + half).Within(1e-3f), "its near edge 1.5 degrees outside the frame");
        Assert.That(Horizontal(home), Is.EqualTo(1.5f).Within(1e-4f), "at the console's distance");
        var consoleFoot = 1.5f * MathF.Tan(10f * MathF.PI / 180f);
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
    public void AtEachTurnTheConsolesDepthThenNearerThenFartherAndTheHomesHeightThenLowerThenHigher()
    {
        var home = AgentShelfSearch.Candidates(Eyes, 0f, Console).First();
        var first = AgentShelfSearch.Candidates(Eyes, 0f, Console).Where(candidate => candidate.Yaw == home.Yaw).ToList();
        var distances = first.Select(Horizontal).Select(d => MathF.Round(d, 3)).Distinct().ToList();
        Assert.That(distances, Is.EqualTo(new[] { 1.5f, 1.2f, 1.8f, 2f }), "the console's depth, nearer, then farther");
        var drops = first.Take(first.Count / distances.Count).Select(candidate => MathF.Round(Eyes.Y - candidate.Position.Y, 3)).ToList();
        var homeDrop = drops[0];
        Assert.That(drops.Skip(1).TakeWhile(drop => drop > homeDrop), Is.Ordered.Ascending, "then lower");
        Assert.That(drops.Skip(1).SkipWhile(drop => drop > homeDrop), Is.Ordered.Descending, "then higher");
        Assert.That(drops.Skip(1).SkipWhile(drop => drop > homeDrop), Has.All.LessThan(homeDrop));
        Assert.That(drops.Skip(1).TakeWhile(drop => drop > homeDrop).Count(), Is.GreaterThan(0), "lower ones come before any higher one");
    }

    [Test]
    public void ItNeverOffersAShelfNearerThanOnePointTwoMetres()
    {
        foreach (var console in new ConsoleExtent?[] { Console, new ConsoleExtent(-5f, 5f, -30f, 0.6f), null })
        {
            var candidates = AgentShelfSearch.Candidates(Eyes, 0f, console).ToList();
            Assert.That(candidates.Min(Horizontal), Is.GreaterThanOrEqualTo(AgentShelfSearch.MinimumDistance - 1e-4f), console?.Distance.ToString() ?? "no console");
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
        var menu = new ConsoleExtent(-18f, 18f, -13f, 0.46f);
        var widest = menu.WidenedTo(0.6f, 0.68f);
        var extra = (MathF.Atan(0.34f) - MathF.Atan(0.3f)) * 180f / MathF.PI;
        Assert.That((widest.LeftYaw, widest.RightYaw), Is.EqualTo((-18f - extra, 18f + extra)).Within(1e-3f), "both edges out, about the same middle");
        Assert.That((widest.BottomElevation, widest.Distance), Is.EqualTo((-13f, 0.46f)));
        Assert.That(menu.WidenedTo(0.68f, 0.6f).RightYaw, Is.EqualTo(18f), "never narrower than what shows");
        var besideMenu = AgentShelfSearch.Candidates(Eyes, 0f, menu).First();
        var besideWidest = AgentShelfSearch.Candidates(Eyes, 0f, widest).First();
        Assert.That(besideWidest.Yaw - besideMenu.Yaw, Is.EqualTo(extra).Within(0.05f), "the home moves out with the wider console's edge");
    }

    private static float Horizontal(AgentShelfPose pose) => new Vector2(pose.Position.X - Eyes.X, pose.Position.Z - Eyes.Z).Length();

    private static bool Same(AgentShelfPose a, AgentShelfPose b) => Vector3.Distance(a.Position, b.Position) < 1e-5f && Math.Abs(a.Yaw - b.Yaw) < 1e-4f;
}
