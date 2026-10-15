using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
namespace Halcyonic.Client.Tests;

[TestFixture]
public class AgentShelfTests
{
    private static AgentShelfDraft Draft()
    {
        var draft = new AgentShelfDraft();
        draft.Begin(new AgentShelfPose(new Vector3(0f, 1.15f, 1.2f), 0f), new Vector3(0f, 1.6f, 0f));
        return draft;
    }
    [Test]
    public void MovementAndCancellationCannotPublishOrAcceptLateRoomSuggestions()
    {
        var draft = Draft();
        var generation = draft.Generation;
        var original = draft.Candidate!.Value.Position;
        Assert.That(draft.Move(Vector3.UnitX * 0.1f, 5f), Is.True);
        Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(original + Vector3.UnitX * 0.1f));
        draft.Cancel();
        Assert.That(draft.Candidate, Is.Null);
        Assert.That(draft.Suggest(generation, new AgentShelfPose(original, 0f)), Is.False);
        Assert.That(draft.Apply(_ => throw new Exception("cancelled placement must never publish")), Is.False);
    }
    [Test]
    public void ApplyConsumesOnePreviewBeforeCallingItsOwner()
    {
        var draft = Draft(); var calls = 0; var generation = draft.Generation;
        Assert.That(draft.Apply(pose =>
        {
            calls++;
            Assert.That(pose.Position, Is.EqualTo(new Vector3(0f, 1.15f, 1.2f)));
            Assert.That(draft.Candidate, Is.Null);
            Assert.That(draft.Suggest(generation, pose), Is.False);
            Assert.That(draft.Apply(_ => true), Is.False);
            return true;
        }), Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }
    [Test]
    public void InvalidAndOutOfReachMovesLeaveTheVisiblePreviewUnchanged()
    {
        var draft = Draft(); var original = draft.Candidate!.Value.Position;
        foreach (var delta in new[] { new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), Vector3.UnitY, Vector3.UnitZ * 3f, -Vector3.UnitZ })
        {
            Assert.That(draft.Move(delta), Is.False);
            Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(original));
        }
    }
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void EveryPlacementControlIsOfferedAcrossMeasuredPagesAndUndrawnOrUnseenControlsCannotAct(TextSize size)
    {
        var host = new FakeMenuHost { TextSize = size, Height = MenuPage.Words(1) + 2 * MenuPage.Target() + 2 * MenuPage.TargetGap, MeasureTitle = (_, _) => 2 };
        var draft = Draft(); var moved = new List<string>(); var applied = 0; var reads = 0; var cancelled = 0;
        var column = new AgentShelfColumn(host, draft, moved.Add, () => applied++, () => { cancelled++; draft.Cancel(); }, () => reads++);
        var seen = new HashSet<string>();
        for (var page = 0; page < 5; page++)
        {
            var frame = column.Frame!;
            Assert.That(column.Frame, Is.SameAs(frame), "the offered frame remains stable until Changed");
            column.Act(AgentShelfColumn.Apply, null); column.Act(AgentShelfColumn.Move, "Left");
            Assert.That(applied, Is.Zero); Assert.That(moved, Is.Empty);
            column.Drawn(frame, null);
            var physical = MenuPage.Words(frame.Lines[0].Rows) + frame.Lines.Skip(1).Count() * MenuPage.Target() + (frame.Lines.Count - 1) * MenuPage.TargetGap;
            Assert.That(physical, Is.LessThanOrEqualTo(host.Height + 0.0001f));
            foreach (var line in frame.Lines.Skip(1)) seen.Add(line.Key ?? line.Action!);
            var absent = new[] { "Left", "Right", "Closer", "Farther", "Up", "Down", "Turn left", "Turn right" }.First(key => !frame.Lines.Any(line => line.Key == key));
            column.Act(AgentShelfColumn.Move, absent); Assert.That(moved, Is.Empty, "an unoffered movement has no authority");
            if (page < 4) column.Act(Footer.NextPage, null);
        }
        Assert.That(seen, Is.EquivalentTo(new[] { "Left", "Right", "Closer", "Farther", "Up", "Down", "Turn left", "Turn right", AgentShelfColumn.ReadRoom }));
        Assert.That(reads, Is.Zero);
        column.FocusLeft(); column.Act(AgentShelfColumn.Apply, null);
        Assert.That((applied, cancelled), Is.EqualTo((0, 1)));
        Assert.That(host.Sent, Is.Empty);
    }
    [Test]
    public void ACurrentDrawCanMoveThenMustDrawAgainBeforeApply()
    {
        var host = new FakeMenuHost(); var draft = Draft(); var calls = 0;
        var column = new AgentShelfColumn(host, draft, _ => draft.Move(Vector3.UnitX * 0.05f), () => draft.Apply(_ => { calls++; return true; }), draft.Cancel, () => { });
        var frame = column.Frame!; column.Drawn(frame, null);
        column.Act(AgentShelfColumn.Move, "Left");
        column.Act(AgentShelfColumn.Apply, null); Assert.That(calls, Is.Zero);
        var current = column.Frame!; column.Drawn(frame, null);
        column.Act(AgentShelfColumn.Apply, null); Assert.That(calls, Is.Zero);
        column.Drawn(current, null); column.Act(AgentShelfColumn.Apply, null);
        Assert.That(calls, Is.EqualTo(1)); Assert.That(draft.Candidate, Is.Null);
    }
    [TestCase("apply")]
    [TestCase("expired")]
    [TestCase("cancel")]
    [TestCase("tick")]
    public void TaskPlacementKeepsSelectionUntilApplyAndExpiredContextCancels(string outcome)
    {
        var host = new FakeMenuHost(); var draft = Draft();
        var selected = "previous-task"; var current = true; var cancelled = 0;
        var column = new AgentShelfColumn(host, draft, _ => { },
            () => draft.Apply(_ => { selected = "exact-opened-task"; return true; }),
            () => { cancelled++; draft.Cancel(); }, () => { }, "<task title>", () => current);
        Assert.That(selected, Is.EqualTo("previous-task"));
        var frame = column.Frame!;
        Assert.That(frame.SubjectIsData, Is.True);
        Assert.That(frame.Subject, Is.EqualTo("<task title>"));
        column.Drawn(frame, null);
        if (outcome == "expired" || outcome == "tick") current = false;
        if (outcome == "tick") { Assert.That(column.CurrentDraw, Is.False); column.Tick(); }
        else column.Act(outcome == "cancel" ? Footer.Close : AgentShelfColumn.Apply, null);
        var applied = outcome == "apply";
        Assert.That(selected, Is.EqualTo(applied ? "exact-opened-task" : "previous-task"));
        Assert.That(cancelled, Is.EqualTo(applied ? 0 : 1));
        Assert.That(draft.Candidate, Is.Null);
        column.Act(AgentShelfColumn.Apply, null);
        Assert.That(cancelled, Is.EqualTo(applied ? 0 : 1));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void ARealSurfaceSuggestionChecksTheWholeShelfAndObstaclesRatherThanTheOldArcAlone()
    {
        var surface = new RoomSurface(SurfaceKind.Desk, 0.75f, new[] { new PlanPoint(-1f, 0.1f), new PlanPoint(1f, 0.1f), new PlanPoint(1f, 1f), new PlanPoint(-1f, 1f) });
        var middle = new PlanPoint(0f, 0.6f);
        Assert.That(StageSurfaces.ShelfFits(surface, Array.Empty<RoomObstacle>(), middle, 0f, AgentShelfDraft.Width(6), AgentShelfDraft.Depth), Is.True);
        Assert.That(StageSurfaces.ShelfFits(surface, Array.Empty<RoomObstacle>(), new PlanPoint(0.5f, 0.6f), 0f, AgentShelfDraft.Width(6), AgentShelfDraft.Depth), Is.False, "the far end hangs off the desk");
        var obstruction = new RoomObstacle(0.74f, 0.9f, new[] { new PlanPoint(-0.03f, 0.55f), new PlanPoint(0.03f, 0.55f), new PlanPoint(0.03f, 0.65f), new PlanPoint(-0.03f, 0.65f) });
        Assert.That(StageSurfaces.ShelfFits(surface, new[] { obstruction }, middle, 0f, AgentShelfDraft.Width(6), AgentShelfDraft.Depth), Is.False, "a small object wholly inside the shelf footprint still blocks it");
    }

    [Test]
    public void ADirectGrabNeedsCurrentDrawAndStopsWhenAuthorityOrItsGenerationChanges()
    {
        var draft = Draft(); var drag = new AgentShelfDrag(draft); var original = draft.Candidate!.Value.Position;
        Assert.That(drag.Begin(Vector3.Zero, currentDraw: false), Is.False);
        Assert.That(drag.Begin(new Vector3(float.NaN, 0, 0), currentDraw: true), Is.False);
        Assert.That(drag.Begin(Vector3.Zero, currentDraw: true), Is.True);
        Assert.That(drag.Follow(Vector3.UnitX * 0.05f, authorized: true), Is.True);
        Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(original + Vector3.UnitX * 0.05f));
        Assert.That(drag.Follow(Vector3.UnitX * 0.1f, authorized: false), Is.False);
        Assert.That(drag.Active, Is.False);
        Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(original + Vector3.UnitX * 0.05f));
        drag.Begin(Vector3.Zero, currentDraw: true);
        draft.Cancel(); draft.Begin(new AgentShelfPose(original, 0), new Vector3(0, 1.6f, 0));
        Assert.That(drag.Follow(Vector3.UnitX * 0.1f, authorized: true), Is.False, "a replaced preview cannot inherit a held gesture");
        Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(original));
    }
    [Test]
    public void ApplyRechecksTheOwnersPhysicalVisibilityGuard()
    {
        var draft = Draft(); var visible = true; draft.Allows = _ => visible;
        Assert.That(draft.Move(Vector3.UnitX * 0.05f), Is.True);
        visible = false;
        Assert.That(draft.Move(Vector3.UnitX * 0.05f), Is.False);
        Assert.That(draft.Apply(_ => throw new Exception("an unseen preview cannot authorize placement")), Is.False);
        visible = true;
        Assert.That(draft.Apply(_ => true), Is.True);
    }

    [Test]
    public void TheRoomSearchKeepsLookingWhenItsNearestCandidateCannotFitTheActualShelf()
    {
        var surface = new RoomSurface(SurfaceKind.Desk, 0.75f, new[] { new PlanPoint(-1f, 0.1f), new PlanPoint(1f, 0.1f), new PlanPoint(1f, 1.2f), new PlanPoint(-1f, 1.2f) });
        var eyes = new Viewer(new RoomPoint(0, 1.2f, 0), new PlanPoint(0, 1));
        var regular = StageSurfaces.Choose(new[] { surface }, Array.Empty<RoomObstacle>(), eyes);
        Assert.That(regular, Is.Not.Null); Assert.That(regular!.Reach, Is.LessThan(0.8f));
        var shelf = StageSurfaces.Choose(new[] { surface }, Array.Empty<RoomObstacle>(), eyes, spot => spot.Reach >= 0.8f
            && StageSurfaces.ShelfFits(spot.Surface, Array.Empty<RoomObstacle>(), spot.Point.Plan, spot.Turn, AgentShelfDraft.Width(6), AgentShelfDraft.Depth));
        Assert.That(shelf, Is.Not.Null); Assert.That(shelf!.Reach, Is.GreaterThanOrEqualTo(0.8f));
        Assert.That(StageSurfaces.Choose(new[] { surface }, Array.Empty<RoomObstacle>(), eyes, _ => false), Is.Null);
    }

}
