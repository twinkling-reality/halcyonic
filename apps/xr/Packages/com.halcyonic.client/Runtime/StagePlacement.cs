#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>What kind of shelf candidate the person is applying from the placement preview.</summary>
    public enum StageSuggestionKind
    {
        Manual,
        NewSurface,
        RestoredSurface,
    }

    /// <summary>The committed result of applying the current shelf preview.</summary>
    public readonly struct StageAppliedPreview
    {
        public StageAppliedPreview(
            AgentShelfPose pose,
            StageSuggestionKind kind,
            RoomStatus status,
            SurfaceKind? surface,
            string? roomId,
            string? restoredAnchorUuid)
        {
            Pose = pose;
            Kind = kind;
            Status = status;
            Surface = surface;
            RoomId = roomId;
            RestoredAnchorUuid = restoredAnchorUuid;
        }

        public AgentShelfPose Pose { get; }
        public StageSuggestionKind Kind { get; }
        public RoomStatus Status { get; }
        public SurfaceKind? Surface { get; }
        public string? RoomId { get; }
        public string? RestoredAnchorUuid { get; }
    }

    /// <summary>
    /// Coordinates room-surface and saved-anchor suggestions inside the explicit shelf placement preview
    /// so that Apply preserves surface placement and spatial anchor persistence without bypassing preview authority.
    /// </summary>
    public sealed class StagePlacementSession
    {
        private const float SameMeters = 0.0001f;
        private const float SameDegrees = 0.001f;
        private const float KeptNearerBy = 0.05f;
        private const float KeptFartherBy = 0.3f;
        private const float KeptTurnSlack = 25f;
        private const float KeptHeightSlack = 0.05f;

        private readonly struct PendingSuggestion
        {
            public PendingSuggestion(
                int generation,
                AgentShelfPose pose,
                StageSuggestionKind kind,
                SurfaceKind? surface,
                string? roomId,
                string? restoredAnchorUuid)
            {
                Generation = generation;
                Pose = pose;
                Kind = kind;
                Surface = surface;
                RoomId = roomId;
                RestoredAnchorUuid = restoredAnchorUuid;
            }

            public int Generation { get; }
            public AgentShelfPose Pose { get; }
            public StageSuggestionKind Kind { get; }
            public SurfaceKind? Surface { get; }
            public string? RoomId { get; }
            public string? RestoredAnchorUuid { get; }
        }

        private PendingSuggestion? pending;
        private AgentShelfDraft? trackedDraft;

        public bool HasPendingSuggestion => pending.HasValue;

        public void ClearSuggestion() => pending = null;

        private void TrackDraft(AgentShelfDraft draft)
        {
            if (ReferenceEquals(trackedDraft, draft)) return;
            if (trackedDraft != null) trackedDraft.Changed -= ClearSuggestion;
            trackedDraft = draft;
            trackedDraft.Changed += ClearSuggestion;
        }

        public string? SavedAnchorToTry(PlacementMemory memory, string? roomId)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            return !string.IsNullOrEmpty(roomId) ? memory.AnchorFor(roomId!) : memory.MostRecentAnchor;
        }

        public void RecordMissingAnchor(PlacementMemory memory, string anchorUuid)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (string.IsNullOrEmpty(anchorUuid)) return;
            memory.Forget(anchorUuid);
        }

        public bool SuggestSurface(
            AgentShelfDraft draft,
            int generation,
            AgentShelfPose pose,
            SurfaceKind surface,
            string? roomId)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            TrackDraft(draft);
            if (!draft.Suggest(generation, pose))
            {
                pending = null;
                return false;
            }
            pending = new PendingSuggestion(
                draft.Generation,
                pose,
                StageSuggestionKind.NewSurface,
                surface,
                roomId,
                restoredAnchorUuid: null);
            return true;
        }

        public bool SuggestRestored(
            AgentShelfDraft draft,
            int generation,
            AgentShelfPose pose,
            SurfaceKind? surface,
            string? roomId,
            string anchorUuid)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (string.IsNullOrEmpty(anchorUuid)) throw new ArgumentException("Anchor UUID is required.", nameof(anchorUuid));
            TrackDraft(draft);
            if (!draft.Suggest(generation, pose))
            {
                pending = null;
                return false;
            }
            pending = new PendingSuggestion(
                draft.Generation,
                pose,
                StageSuggestionKind.RestoredSurface,
                surface,
                roomId,
                anchorUuid);
            return true;
        }

        public bool TryApply(
            AgentShelfDraft draft,
            RoomStatus currentStatus,
            PlacementMemory memory,
            out StageAppliedPreview applied)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            var generationBeforeApply = draft.Generation;
            var candidateBeforeApply = pending;
            pending = null;
            StageAppliedPreview result = default;

            var committed = draft.Apply(candidate =>
            {
                if (candidateBeforeApply is PendingSuggestion suggestion
                    && suggestion.Generation == generationBeforeApply
                    && SamePose(suggestion.Pose, candidate))
                {
                    if (suggestion.Kind == StageSuggestionKind.RestoredSurface)
                    {
                        if (!string.IsNullOrEmpty(suggestion.RoomId))
                        {
                            memory.Touch(suggestion.RoomId!);
                        }
                        var restoredStatus = currentStatus.With(
                            placement: StagePlacement.BackOnSurface,
                            surface: suggestion.Surface,
                            clearSurface: suggestion.Surface == null);
                        result = new StageAppliedPreview(
                            candidate,
                            StageSuggestionKind.RestoredSurface,
                            restoredStatus,
                            suggestion.Surface,
                            suggestion.RoomId,
                            suggestion.RestoredAnchorUuid);
                        return true;
                    }

                    if (suggestion.Kind == StageSuggestionKind.NewSurface && suggestion.Surface is SurfaceKind surface)
                    {
                        var surfaceStatus = currentStatus.With(
                            placement: StagePlacement.OnSurfaceThisSession,
                            surface: surface,
                            clearSurface: false);
                        result = new StageAppliedPreview(
                            candidate,
                            StageSuggestionKind.NewSurface,
                            surfaceStatus,
                            surface,
                            suggestion.RoomId,
                            restoredAnchorUuid: null);
                        return true;
                    }
                }

                var manualStatus = currentStatus.With(
                    placement: StagePlacement.OnShelfThisSession,
                    clearSurface: true);
                result = new StageAppliedPreview(
                    candidate,
                    StageSuggestionKind.Manual,
                    manualStatus,
                    surface: null,
                    roomId: null,
                    restoredAnchorUuid: null);
                return true;
            });

            applied = committed ? result : default;
            return committed;
        }

        public RoomStatus RecordCreatedAnchor(
            RoomStatus currentStatus,
            PlacementMemory memory,
            SurfaceKind surface,
            string? roomId,
            string? anchorUuid,
            bool saved,
            out IReadOnlyList<string> droppedAnchors)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (!saved || string.IsNullOrEmpty(anchorUuid))
            {
                droppedAnchors = Array.Empty<string>();
                return currentStatus.With(
                    placement: StagePlacement.OnSurfaceThisSession,
                    surface: surface,
                    clearSurface: false);
            }

            droppedAnchors = !string.IsNullOrEmpty(roomId)
                ? memory.Remember(roomId!, anchorUuid!)
                : Array.Empty<string>();
            return currentStatus.With(
                placement: StagePlacement.OnSurface,
                surface: surface,
                clearSurface: false);
        }

        /// <summary>
        /// Whether a saved anchor pose still suits the person where they sit now and, when the room's
        /// surfaces were read, fits the rectangular shelf on a surface clear of obstacles.
        /// </summary>
        public static bool RestoredShelfSuits(
            RoomPoint middle,
            float yaw,
            float width,
            float depth,
            Viewer viewer,
            IEnumerable<RoomSurface>? surfaces,
            IEnumerable<RoomObstacle>? obstacles)
        {
            if (!(width > 0f) || !(depth > 0f)) return false;
            var toMiddle = middle.Plan - viewer.Eyes.Plan;
            var reach = toMiddle.Length;
            if (reach < StageSurfaces.NearestReach - KeptNearerBy || reach > StageSurfaces.FarthestReach + KeptFartherBy) return false;
            if (MathF.Abs(StageSurfaces.TurnBetween(viewer.Facing.Heading, toMiddle.Heading)) > StageSurfaces.WidestTurn + KeptTurnSlack) return false;
            var drop = viewer.Eyes.Y - middle.Y;
            if (drop < StageSurfaces.LeastDrop - KeptHeightSlack || drop > StageSurfaces.MostDrop + KeptHeightSlack) return false;
            if (surfaces == null) return true;
            return ShelfSurfaceUnder(middle, yaw, width, depth, surfaces, obstacles) != null;
        }

        /// <summary>
        /// Which kind of surface a rectangular shelf stands on at <paramref name="middle"/> and <paramref name="yaw"/>,
        /// when the room's layout is known and the shelf fits on the surface clear of obstacles.
        /// </summary>
        public static SurfaceKind? ShelfSurfaceUnder(
            RoomPoint middle,
            float yaw,
            float width,
            float depth,
            IEnumerable<RoomSurface>? surfaces,
            IEnumerable<RoomObstacle>? obstacles)
        {
            if (surfaces == null || !(width > 0f) || !(depth > 0f)) return null;
            IReadOnlyList<RoomObstacle> blocking = obstacles as IReadOnlyList<RoomObstacle>
                ?? (obstacles != null ? new List<RoomObstacle>(obstacles) : Array.Empty<RoomObstacle>());
            SurfaceKind? found = null;
            foreach (var surface in surfaces)
            {
                if (MathF.Abs(surface.Height - middle.Y) > KeptHeightSlack) continue;
                if (StageSurfaces.ShelfFits(surface, blocking, middle.Plan, yaw, width, depth))
                {
                    if (surface.Kind == SurfaceKind.Desk) return SurfaceKind.Desk;
                    found = surface.Kind;
                }
            }
            return found;
        }

        private static bool SamePose(AgentShelfPose a, AgentShelfPose b) =>
            Vector3.Distance(a.Position, b.Position) <= SameMeters
            && MathF.Abs(DeltaDegrees(a.Yaw, b.Yaw)) <= SameDegrees;

        private static float DeltaDegrees(float a, float b)
        {
            var delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
