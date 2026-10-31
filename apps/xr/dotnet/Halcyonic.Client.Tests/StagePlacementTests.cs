using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class StagePlacementTests
{
    private static readonly Vector3 Eyes = new Vector3(0f, 1.2f, 0f);
    private static readonly AgentShelfPose ManualPose = new AgentShelfPose(new Vector3(0f, 0.75f, 1.2f), 0f);
    private static readonly AgentShelfPose DeskPose = new AgentShelfPose(new Vector3(0f, 0.74f, 0.55f), 0f);

    private static AgentShelfDraft StartDraft()
    {
        var draft = new AgentShelfDraft();
        draft.Begin(ManualPose, Eyes);
        return draft;
    }

    private static RoomStatus ReadyRoomStatus() =>
        RoomStatus.Initial.With(
            preferred: RoomSpace.Room,
            passthrough: PassthroughState.Running,
            scan: RoomScan.Read);

    [Test]
    public void ApplyOnSurfaceSuggestionPreservesSurfaceAndPromotesToSavedAnchorInMemory()
    {
        var draft = StartDraft();
        var generation = draft.Generation;
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        var status = ReadyRoomStatus();

        Assert.That(
            session.SuggestSurface(draft, generation, DeskPose, SurfaceKind.Desk, "room-office"),
            Is.True);
        Assert.That(draft.Candidate, Is.EqualTo(DeskPose));

        Assert.That(session.TryApply(draft, status, memory, out var applied), Is.True);
        Assert.That(applied.Kind, Is.EqualTo(StageSuggestionKind.NewSurface));
        Assert.That(applied.Pose, Is.EqualTo(DeskPose));
        Assert.That(applied.Surface, Is.EqualTo(SurfaceKind.Desk));
        Assert.That(applied.RoomId, Is.EqualTo("room-office"));
        Assert.That(applied.Status.Placement, Is.EqualTo(StagePlacement.OnSurfaceThisSession));
        Assert.That(applied.Status.Surface, Is.EqualTo(SurfaceKind.Desk));

        const string anchorUuid = "0123456789abcdef0123456789abcdef";
        var savedStatus = session.RecordCreatedAnchor(
            applied.Status,
            memory,
            SurfaceKind.Desk,
            applied.RoomId,
            anchorUuid,
            saved: true,
            out var dropped);

        Assert.That(savedStatus.Placement, Is.EqualTo(StagePlacement.OnSurface));
        Assert.That(savedStatus.Surface, Is.EqualTo(SurfaceKind.Desk));
        Assert.That(memory.AnchorFor("room-office"), Is.EqualTo(anchorUuid));
        Assert.That(dropped, Is.Empty);
    }

    [Test]
    public void UnsavedOrFailedAnchorKeepsSessionSurfaceStatusWithoutWritingMemory()
    {
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        var status = ReadyRoomStatus().With(
            placement: StagePlacement.OnSurfaceThisSession,
            surface: SurfaceKind.Other);

        var unsaved = session.RecordCreatedAnchor(
            status,
            memory,
            SurfaceKind.Other,
            "room-office",
            "fedcba9876543210fedcba9876543210",
            saved: false,
            out var droppedUnsaved);

        Assert.That(unsaved.Placement, Is.EqualTo(StagePlacement.OnSurfaceThisSession));
        Assert.That(unsaved.Surface, Is.EqualTo(SurfaceKind.Other));
        Assert.That(memory.AnchorFor("room-office"), Is.Null);
        Assert.That(droppedUnsaved, Is.Empty);

        var missing = session.RecordCreatedAnchor(
            status,
            memory,
            SurfaceKind.Other,
            "room-office",
            anchorUuid: null,
            saved: false,
            out var droppedMissing);

        Assert.That(missing.Placement, Is.EqualTo(StagePlacement.OnSurfaceThisSession));
        Assert.That(missing.Surface, Is.EqualTo(SurfaceKind.Other));
        Assert.That(memory.AnchorFor("room-office"), Is.Null);
        Assert.That(droppedMissing, Is.Empty);
    }

    [Test]
    public void ReplacingSavedRoomAnchorReturnsDroppedAnchorToErase()
    {
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        const string firstUuid = "11111111111111111111111111111111";
        const string secondUuid = "22222222222222222222222222222222";
        memory.Remember("room-office", firstUuid);

        var status = ReadyRoomStatus().With(
            placement: StagePlacement.OnSurfaceThisSession,
            surface: SurfaceKind.Desk);

        var next = session.RecordCreatedAnchor(
            status,
            memory,
            SurfaceKind.Desk,
            "room-office",
            secondUuid,
            saved: true,
            out var dropped);

        Assert.That(next.Placement, Is.EqualTo(StagePlacement.OnSurface));
        Assert.That(memory.AnchorFor("room-office"), Is.EqualTo(secondUuid));
        Assert.That(dropped, Is.EqualTo(new[] { firstUuid }));
    }

    [Test]
    public void MovingOrClearingPreviewAfterSurfaceSuggestionAppliesAsManualSessionShelf()
    {
        var draft = StartDraft();
        var generation = draft.Generation;
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        var status = ReadyRoomStatus().With(surface: SurfaceKind.Desk);

        Assert.That(
            session.SuggestSurface(draft, generation, DeskPose, SurfaceKind.Desk, "room-office"),
            Is.True);

        // Moving the preview away from the suggested surface spot leaves manual session placement.
        session.ClearSuggestion();
        Assert.That(draft.Move(new Vector3(0.05f, 0f, 0f)), Is.True);

        Assert.That(session.TryApply(draft, status, memory, out var applied), Is.True);
        Assert.That(applied.Kind, Is.EqualTo(StageSuggestionKind.Manual));
        Assert.That(applied.Surface, Is.Null);
        Assert.That(applied.RoomId, Is.Null);
        Assert.That(applied.Status.Placement, Is.EqualTo(StagePlacement.OnShelfThisSession));
        Assert.That(applied.Status.Surface, Is.Null);
    }

    [Test]
    public void SavedAnchorRestoreRequiresApplyAndTransitionsToBackOnSurface()
    {
        var draft = StartDraft();
        var generation = draft.Generation;
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        const string savedUuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string recentUuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        memory.Remember("room-office", savedUuid);
        memory.Remember("room-den", recentUuid);

        // With a known room id, look up that room's anchor; without a room id, fall back to MostRecentAnchor.
        Assert.That(session.SavedAnchorToTry(memory, "room-office"), Is.EqualTo(savedUuid));
        Assert.That(session.SavedAnchorToTry(memory, null), Is.EqualTo(recentUuid));

        Assert.That(
            session.SuggestRestored(draft, generation, DeskPose, SurfaceKind.Desk, "room-office", savedUuid),
            Is.True);
        // Suggesting a restored anchor only updates the preview candidate; it does not apply before Apply.
        Assert.That(draft.Candidate, Is.EqualTo(DeskPose));

        var status = ReadyRoomStatus();
        Assert.That(session.TryApply(draft, status, memory, out var applied), Is.True);
        Assert.That(applied.Kind, Is.EqualTo(StageSuggestionKind.RestoredSurface));
        Assert.That(applied.Pose, Is.EqualTo(DeskPose));
        Assert.That(applied.RestoredAnchorUuid, Is.EqualTo(savedUuid));
        Assert.That(applied.Surface, Is.EqualTo(SurfaceKind.Desk));
        Assert.That(applied.Status.Placement, Is.EqualTo(StagePlacement.BackOnSurface));
        Assert.That(applied.Status.Surface, Is.EqualTo(SurfaceKind.Desk));
        // Touching room-office makes its anchor most recent again.
        Assert.That(memory.MostRecentAnchor, Is.EqualTo(savedUuid));
    }

    [Test]
    public void MissingDeviceAnchorIsForgottenFromMemory()
    {
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        const string missingUuid = "cccccccccccccccccccccccccccccccc";
        memory.Remember("room-office", missingUuid);

        session.RecordMissingAnchor(memory, missingUuid);
        Assert.That(memory.AnchorFor("room-office"), Is.Null);
        Assert.That(memory.MostRecentAnchor, Is.Null);
    }

    [Test]
    public void DirectDraftMoveClearsPendingSuggestionEvenWithoutExplicitClearCall()
    {
        var draft = StartDraft();
        var generation = draft.Generation;
        var session = new StagePlacementSession();
        var memory = new PlacementMemory();
        var status = ReadyRoomStatus();
        const string savedUuid = "dddddddddddddddddddddddddddddddd";

        Assert.That(
            session.SuggestRestored(draft, generation, DeskPose, SurfaceKind.Desk, "room-office", savedUuid),
            Is.True);
        Assert.That(session.HasPendingSuggestion, Is.True);

        // AgentShelfDrag.Follow moves Draft directly and does not call session.ClearSuggestion().
        // Even if the user drags away and drags back to the original pose, the suggestion is cleared on the first move.
        Assert.That(draft.Move(new Vector3(0.05f, 0f, 0f)), Is.True);
        Assert.That(session.HasPendingSuggestion, Is.False);
        Assert.That(draft.Move(new Vector3(-0.05f, 0f, 0f)), Is.True);
        Assert.That(draft.Candidate, Is.EqualTo(DeskPose));

        Assert.That(session.TryApply(draft, status, memory, out var applied), Is.True);
        Assert.That(applied.Kind, Is.EqualTo(StageSuggestionKind.Manual));
        Assert.That(applied.Status.Placement, Is.EqualTo(StagePlacement.OnShelfThisSession));
        Assert.That(applied.Status.Surface, Is.Null);
    }

    [Test]
    public void RestoredShelfValidationChecksRectangularShelfFootprintAndIdentifiesNarrowDeskSurface()
    {
        var viewer = new Viewer(new RoomPoint(0f, 1.2f, 0f), new PlanPoint(0f, 1f));
        var middle = new RoomPoint(0f, 0.74f, 0.65f);
        var width = AgentShelfDraft.Width(1);
        const float depth = AgentShelfDraft.Depth;

        // A compact 0.48 m wide desk fits the 1-character rectangular shelf (~0.304 m wide by 0.26 m deep)
        // even though the legacy 60-degree arc at 0.65 m reach (0.65 m chord width) would overhang its sides.
        var narrowDesk = new RoomSurface(
            SurfaceKind.Desk,
            0.74f,
            new[]
            {
                new PlanPoint(-0.24f, 0.40f),
                new PlanPoint(0.24f, 0.40f),
                new PlanPoint(0.24f, 0.90f),
                new PlanPoint(-0.24f, 0.90f),
            });

        Assert.That(
            StagePlacementSession.RestoredShelfSuits(
                middle,
                yaw: 0f,
                width,
                depth,
                viewer,
                new[] { narrowDesk },
                System.Array.Empty<RoomObstacle>()),
            Is.True);
        Assert.That(
            StagePlacementSession.ShelfSurfaceUnder(
                middle,
                yaw: 0f,
                width,
                depth,
                new[] { narrowDesk },
                System.Array.Empty<RoomObstacle>()),
            Is.EqualTo(SurfaceKind.Desk));

        // An obstacle standing inside the front half of the rectangular shelf footprint (away from the 0.65 m arc radius)
        // rejects the restored shelf placement and clears surface identification.
        var frontObstacle = new RoomObstacle(
            0.74f,
            0.95f,
            new[]
            {
                new PlanPoint(-0.04f, 0.54f),
                new PlanPoint(0.04f, 0.54f),
                new PlanPoint(0.04f, 0.58f),
                new PlanPoint(-0.04f, 0.58f),
            });

        Assert.That(
            StagePlacementSession.RestoredShelfSuits(
                middle,
                yaw: 0f,
                width,
                depth,
                viewer,
                new[] { narrowDesk },
                new[] { frontObstacle }),
            Is.False);
        Assert.That(
            StagePlacementSession.ShelfSurfaceUnder(
                middle,
                yaw: 0f,
                width,
                depth,
                new[] { narrowDesk },
                new[] { frontObstacle }),
            Is.Null);
    }
}
