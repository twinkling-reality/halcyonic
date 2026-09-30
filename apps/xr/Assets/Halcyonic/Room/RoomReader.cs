#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>What reading the room found.</summary>
    internal sealed class RoomRead
    {
        private RoomRead(RoomScan scan, string detail, string? roomId, List<RoomSurface>? surfaces, List<RoomObstacle>? obstacles)
        {
            Scan = scan;
            Detail = detail;
            RoomId = roomId;
            Surfaces = surfaces;
            Obstacles = obstacles;
        }

        /// <see cref="RoomScan.Read"/> with the room's layout, or why there is none.
        public RoomScan Scan { get; }

        /// <summary>For the log: counts and results, never ids or positions.</summary>
        public string Detail { get; }

        /// <summary>The scene room's UUID, which keeps its placement; null without a room.</summary>
        public string? RoomId { get; }

        public List<RoomSurface>? Surfaces { get; }

        public List<RoomObstacle>? Obstacles { get; }

        public static RoomRead Without(RoomScan scan, string detail) => new RoomRead(scan, detail, null, null, null);

        public static RoomRead Of(string roomId, List<RoomSurface> surfaces, List<RoomObstacle> obstacles) =>
            new RoomRead(RoomScan.Read, $"{surfaces.Count} surfaces, {obstacles.Count} objects", roomId, surfaces, obstacles);
    }

    /// <summary>
    /// Reads the room the person is in through the MR Utility Kit: its scene model from the headset,
    /// the surfaces something can stand on, and the objects that stand on them, in Unity's world
    /// coordinates. MRUK is created here, configured never to load on its own, never to ask for
    /// space setup, and never to move the camera rig's tracking space (world lock off): the stage
    /// keeps its place with its own spatial anchor.
    /// </summary>
    internal static class RoomReader
    {
        /// <summary>A plane or a volume's top faces up when its normal is within 12 degrees of up.</summary>
        private const float FacingUp = 0.978f;

        private const MRUKAnchor.SceneLabels Desks = MRUKAnchor.SceneLabels.TABLE;

        /// <summary>
        /// Furniture tops that can hold the lineup when no desk can. A couch's box top is its backrest,
        /// not a surface, so couches are only obstacles.
        /// </summary>
        private const MRUKAnchor.SceneLabels OtherSurfaces =
            MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.OTHER;

        /// <summary>The room's shell and what hangs on it, none of which stands on a surface.</summary>
        private const MRUKAnchor.SceneLabels Shell =
            MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.WALL_FACE |
            MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
            MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME |
            MRUKAnchor.SceneLabels.WALL_ART | MRUKAnchor.SceneLabels.GLOBAL_MESH;

        /// <summary>The MR Utility Kit, created once under <paramref name="parent"/>, configured before it wakes.</summary>
        public static MRUK Ensure(Transform parent)
        {
            if (MRUK.Instance != null) return MRUK.Instance;
            var host = new GameObject("MR Utility Kit");
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            var mruk = host.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings
            {
                DataSource = MRUK.SceneDataSource.Device,
                LoadSceneOnStartup = false,
                RoomPrefabs = Array.Empty<GameObject>(),
                SceneJsons = Array.Empty<TextAsset>(),
            };
            mruk.EnableWorldLock = false;
            host.SetActive(true);
            return mruk;
        }

        /// <summary>
        /// Loads the scene model and finds the room around <paramref name="eyes"/>. Needs the spatial
        /// data permission; without it MRUK reads nothing and says so.
        /// </summary>
        public static async Task<RoomRead> ReadAsync(MRUK mruk, Vector3 eyes)
        {
            MRUK.LoadDeviceResult result;
            try
            {
                result = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false, removeMissingRooms: true);
            }
            catch (Exception error)
            {
                return RoomRead.Without(RoomScan.Unavailable, "loading failed: " + error.GetType().Name);
            }
            switch (result)
            {
                case MRUK.LoadDeviceResult.Success:
                    break;
                case MRUK.LoadDeviceResult.NoScenePermission:
                case MRUK.LoadDeviceResult.FailurePermissionInsufficient:
                    return RoomRead.Without(RoomScan.NoAccess, result.ToString());
                case MRUK.LoadDeviceResult.NoRoomsFound:
                    return RoomRead.Without(RoomScan.NotSetUp, result.ToString());
                default:
                    return RoomRead.Without(RoomScan.Unavailable, result.ToString());
            }
            if (mruk.Rooms.Count == 0) return RoomRead.Without(RoomScan.NotSetUp, "no rooms");
            var current = mruk.GetCurrentRoom();
            // The current room is the last one the headset was in, or the first; only one around the eyes counts.
            var room = current != null && current.IsPositionInRoom(eyes, testVerticalBounds: false)
                ? current
                : mruk.Rooms.FirstOrDefault(candidate => candidate.IsPositionInRoom(eyes, testVerticalBounds: false));
            if (room == null) return RoomRead.Without(RoomScan.OutsideRooms, $"{mruk.Rooms.Count} rooms, none around the person");
            var (surfaces, obstacles) = Describe(room);
            return RoomRead.Of(room.Anchor.Uuid.ToString("N"), surfaces, obstacles);
        }

        /// <summary>The room's surfaces facing up, and every object with a volume, in world coordinates.</summary>
        public static (List<RoomSurface> Surfaces, List<RoomObstacle> Obstacles) Describe(MRUKRoom room)
        {
            var surfaces = new List<RoomSurface>();
            var obstacles = new List<RoomObstacle>();
            foreach (var anchor in room.Anchors)
            {
                if (anchor == null || anchor.HasAnyLabel(Shell)) continue;
                var kind = anchor.HasAnyLabel(Desks) ? SurfaceKind.Desk
                    : anchor.HasAnyLabel(OtherSurfaces) ? SurfaceKind.Other
                    : (SurfaceKind?)null;
                if (kind.HasValue && TopOf(anchor, out var height, out var outline))
                {
                    surfaces.Add(new RoomSurface(kind.Value, height, outline));
                }
                if (VolumeOf(anchor, out var bottom, out var top, out var footprint))
                {
                    obstacles.Add(new RoomObstacle(bottom, top, footprint));
                }
            }
            return (surfaces, obstacles);
        }

        /// <summary>
        /// The top of a table or other furniture: the outline of its plane where the scene model has
        /// one, otherwise of its volume's top face. A plane's normal is the anchor's +Z, and a volume
        /// reaches from its bottom at its local minimum Z to its top at its maximum Z (MRUK aligns
        /// prefabs to a volume's bottom at its minimum Z), so the top's height is the volume's maximum
        /// Z wherever there is a volume, whichever end of it the anchor's origin sits at.
        /// </summary>
        private static bool TopOf(MRUKAnchor anchor, out float height, out List<PlanPoint> outline)
        {
            height = 0f;
            outline = new List<PlanPoint>();
            var pose = anchor.transform;
            if (Vector3.Dot(pose.forward, Vector3.up) < FacingUp) return false;
            var top = anchor.VolumeBounds.HasValue ? anchor.VolumeBounds.Value.max.z : 0f;
            var corners = new List<Vector3>();
            if (anchor.PlaneRect.HasValue && anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3)
            {
                corners.AddRange(anchor.PlaneBoundary2D.Select(point => pose.TransformPoint(new Vector3(point.x, point.y, top))));
            }
            else if (anchor.PlaneRect.HasValue)
            {
                var rect = anchor.PlaneRect.Value;
                corners.AddRange(Corners(rect.xMin, rect.xMax, rect.yMin, rect.yMax, top).Select(pose.TransformPoint));
            }
            else if (anchor.VolumeBounds.HasValue)
            {
                var box = anchor.VolumeBounds.Value;
                corners.AddRange(Corners(box.min.x, box.max.x, box.min.y, box.max.y, top).Select(pose.TransformPoint));
            }
            if (corners.Count < 3) return false;
            height = corners.Average(corner => corner.y);
            outline = corners.Select(corner => new PlanPoint(corner.x, corner.z)).ToList();
            return true;
        }

        /// <summary>
        /// An object's volume: its footprint and how high it reaches. Upright volumes keep their
        /// turned footprint; anything tilted is widened to the box around it, never narrowed.
        /// </summary>
        private static bool VolumeOf(MRUKAnchor anchor, out float bottom, out float top, out List<PlanPoint> footprint)
        {
            bottom = top = 0f;
            footprint = new List<PlanPoint>();
            if (!anchor.VolumeBounds.HasValue) return false;
            var box = anchor.VolumeBounds.Value;
            var pose = anchor.transform;
            var all = Corners(box.min.x, box.max.x, box.min.y, box.max.y, box.min.z)
                .Concat(Corners(box.min.x, box.max.x, box.min.y, box.max.y, box.max.z))
                .Select(pose.TransformPoint)
                .ToList();
            bottom = all.Min(corner => corner.y);
            top = all.Max(corner => corner.y);
            if (Vector3.Dot(pose.forward, Vector3.up) >= FacingUp)
            {
                footprint = all.Take(4).Select(corner => new PlanPoint(corner.x, corner.z)).ToList();
            }
            else
            {
                float minX = all.Min(c => c.x), maxX = all.Max(c => c.x), minZ = all.Min(c => c.z), maxZ = all.Max(c => c.z);
                footprint = new List<PlanPoint>
                {
                    new PlanPoint(minX, minZ), new PlanPoint(maxX, minZ), new PlanPoint(maxX, maxZ), new PlanPoint(minX, maxZ),
                };
            }
            return true;
        }

        /// <summary>A rectangle's corners in order around it, in the anchor's local XY plane at a height along its +Z.</summary>
        private static IEnumerable<Vector3> Corners(float minX, float maxX, float minY, float maxY, float z)
        {
            yield return new Vector3(minX, minY, z);
            yield return new Vector3(maxX, minY, z);
            yield return new Vector3(maxX, maxY, z);
            yield return new Vector3(minX, maxY, z);
        }
    }
}
