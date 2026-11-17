#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    public sealed partial class CharacterStage
    {
        /// <summary>
        /// Where ahead is, as a yaw in the world's degrees, from the eyes: the plane's resting heading (MenuPlane publishes it once it is
        /// laid); null before. The other projects' places stand about it (<see cref="PlaceBearings"/>), the front 30 degrees either side
        /// kept clear for the work or the show.
        /// </summary>
        public static System.Func<float?>? AheadYawDegrees { get; set; }

        /// <summary>
        /// How wide and tall <paramref name="view"/>'s tag stands as drawn now, in metres in the world; null where it draws none. The room's
        /// parts publish it (RoomNameTag), which this assembly cannot see, so a place's pitch comes from its widest DRAWN tag (FIELD's
        /// <see cref="PlacePitch"/>), never a typed width.
        /// </summary>
        public static System.Func<CharacterView, Vector2?>? DrawnTagSize { get; set; }

        /// <summary>Raised once for each other project's place as it first stands, with the point its sign hangs from (its top middle), so
        /// the room's parts can stand <c>RoomPlaceSignView</c> there; <see cref="OtherPlaces"/> says what each place holds now.</summary>
        public event System.Action<string, Transform>? PlaceCreated;

        /// <summary>
        /// The projects' places other than the person's shelf, as they stand now, in <see cref="RoomPlaces"/>' order: each its
        /// <see cref="RoomPlace"/> and its bearing about ahead (the lead's 2026-11-17T18:56Z, one place per project; FIELD's places sheet v4).
        /// </summary>
        public IReadOnlyList<(RoomPlace Place, PlaceBearing Bearing)> OtherPlaces => otherOrder;

        /// <summary>A body's width at a character's own scale of one, in metres (PlacePitchTests: 0.17 m at full size).</summary>
        public const float BodyWidth = 0.17f;

        /// <summary>The widest tag a name draws, in metres at 1.4 m (FIELD's 200 dp name with its pads at the larger text size): the pitch
        /// before any tag has drawn.</summary>
        public const float WidestTagMetres = 0.224f * 1.15f;

        /// <summary>
        /// The floor arc's line and its end ticks, white only (no hue in the chrome): the sheet's rgba(255,255,255,.28) and .5, given here as
        /// linear alphas, since Unity blends in linear and a page's alpha would render far brighter (0.28 and 0.5 sRGB are 0.064 and 0.214).
        /// </summary>
        public const float FloorArcAlpha = 0.064f, FloorTickAlpha = 0.214f;

        /// <summary>The floor arc's width and its ticks' height, in metres at 1.4 m (the sheet's 1.2 px line and 14 px tick at its scale).</summary>
        public const float FloorArcWidth = 0.0025f, FloorTickHeight = 0.03f;

        /// <summary>The gap between a place's lowest tag and its sign, in metres at 1.4 m.</summary>
        public const float SignGap = 0.02f;

        private sealed class OtherPlace
        {
            public Transform Root = null!;
            public Transform Sign = null!;
            public LineRenderer Arc = null!;
            public LineRenderer[] Ticks = null!;
            public readonly Dictionary<string, CharacterView> Views = new Dictionary<string, CharacterView>(System.StringComparer.Ordinal);
        }

        private readonly Dictionary<string, OtherPlace> others = new Dictionary<string, OtherPlace>(System.StringComparer.Ordinal);
        private readonly List<(RoomPlace Place, PlaceBearing Bearing)> otherOrder = new List<(RoomPlace, PlaceBearing)>();
        private float? aheadHeld;
        private Transform? placesRoot;

        /// <summary>The other places show while the shelf does, and hide with it.</summary>
        private void ShowOtherPlacesWithShelf()
        {
            if (placesRoot != null && placesRoot.gameObject.activeSelf != arc.gameObject.activeSelf) placesRoot.gameObject.SetActive(arc.gameObject.activeSelf);
        }
        private static Material? floorArcMaterial;

        /// <summary>Whether <paramref name="workstreamId"/>'s character stands at another project's place.</summary>
        public bool StandsElsewhere(string workstreamId) => others.Values.Any(place => place.Views.ContainsKey(workstreamId));

        /// <summary>The character <paramref name="workstreamId"/> at another project's place, or null.</summary>
        public CharacterView? ElsewhereView(string workstreamId)
        {
            foreach (var place in others.Values)
                if (place.Views.TryGetValue(workstreamId, out var view)) return view;
            return null;
        }

        /// <summary>Lets go of where ahead was held, so the next stand asks again: the person placed the shelf anew.</summary>
        private void ForgetAhead() => aheadHeld = null;

        /// <summary>
        /// Stands every project's place but the person's own (<paramref name="own"/>) at its bearing about ahead, held from the first stand so a
        /// place never moves when the plane opens elsewhere or another place comes or goes (the sheet: "A place never moves"). One project,
        /// or the older shelf, stands none, so the room is as it was. Each place stands its shown characters in rows at the pitch of its
        /// widest drawn tag, a floor arc under them ending in a tick each side, and the point its sign hangs from.
        /// </summary>
        private void StandOtherPlaces(RealtimeSession session, RoomPlace? own, bool live)
        {
            var state = session.State;
            otherOrder.Clear();
            var wanted = RoomDesign.Enabled && !BesideAWindow && onSurface && own != null
                ? Places.Places.Where(place => !ReferenceEquals(place, own)).ToList()
                : new List<RoomPlace>();
            foreach (var gone in others.Keys.Where(id => wanted.All(place => place.ProjectId != id)).ToList())
            {
                Destroy(others[gone].Root.gameObject);
                others.Remove(gone);
            }
            if (wanted.Count == 0) return;
            var eyes = shelfViewer;
            aheadHeld ??= AheadYawDegrees?.Invoke() ?? (head != null ? head.eulerAngles.y : arc.eulerAngles.y);
            var ahead = aheadHeld.Value;
            for (var index = 0; index < wanted.Count; index++)
            {
                var place = wanted[index];
                var bearing = PlaceBearings.Of(index);
                otherOrder.Add((place, bearing));
                if (!others.TryGetValue(place.ProjectId, out var stood))
                {
                    stood = MakeOtherPlace(place.ProjectId);
                    others[place.ProjectId] = stood;
                    PlaceCreated?.Invoke(place.ProjectId, stood.Sign);
                }
                var (at, facing) = PlacePose(eyes, ahead, arc.position.y, bearing);
                stood.Root.SetPositionAndRotation(at, facing);
                // Its characters: those that leave go, those that come stand, each shown as the shelf shows its own.
                var shown = place.Shown.Where(state.Workstreams.ContainsKey).ToList();
                foreach (var id in stood.Views.Keys.Where(id => !shown.Contains(id)).ToList())
                {
                    Destroy(stood.Views[id].gameObject);
                    stood.Views.Remove(id);
                }
                foreach (var id in shown)
                {
                    if (!stood.Views.TryGetValue(id, out var view))
                    {
                        view = CharacterView.Create(stood.Root, id);
                        view.Person = head;
                        view.Grounded = true;
                        stood.Views[id] = view;
                        CharacterCreated?.Invoke(id, view);
                    }
                    view.Show(CharacterPresenter.Present(state.Workstreams[id], state, live));
                }
                LayOtherPlace(stood, shown, bearing, eyes);
            }
        }

        /// <summary>Stands a place's characters in rows, its floor arc and its sign's point, for the eyes at <paramref name="eyes"/>.</summary>
        private void LayOtherPlace(OtherPlace stood, IReadOnlyList<string> shown, PlaceBearing bearing, Vector3 eyes)
        {
            var distance = bearing.Distance;
            var scale = radius;
            var widestTag = 0f;
            var tallestTag = 0f;
            foreach (var view in stood.Views.Values)
                if (DrawnTagSize?.Invoke(view) is Vector2 size) { widestTag = Mathf.Max(widestTag, size.x); tallestTag = Mathf.Max(tallestTag, size.y); }
            var (pitch, rowMost) = PlaceRows(widestTag, scale, distance, Places.Rule);
            for (var index = 0; index < shown.Count; index++)
            {
                var view = stood.Views[shown[index]];
                var (local, size) = PlaceSpot(index, shown.Count, rowMost, pitch);
                view.transform.localPosition = local;
                view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                view.transform.localScale = Vector3.one * scale * size;
                view.PlaceShelfLabel(eyes);
            }
            // The floor arc spans the place, tags and all, about the eyes at its distance, under the feet, a tick at each end.
            var half = (float)PlaceBearings.SpanDegrees / 2f;
            var middle = stood.Root.eulerAngles.y;
            DrawFloorArc(stood.Arc, stood.Ticks, eyes, stood.Root.position.y, distance, middle - half, middle + half);
            var width = distance / PlaceBearings.RingOneDistance;
            // The sign hangs under the place's lowest tag, at the front row's middle, sized for its distance (the room's units are 1 at it).
            var under = tallestTag > 0f ? tallestTag : WidestTagMetres * 0.3f;
            stood.Sign.localPosition = new Vector3(0f, -(under + SignGap * width), 0f);
            stood.Sign.rotation = Quaternion.LookRotation(stood.Sign.position - eyes, Vector3.up);
            stood.Sign.localScale = Vector3.one * Vector3.Distance(stood.Sign.position, eyes);
        }

        private LineRenderer? shelfArc;
        private LineRenderer[]? shelfTicks;

        /// <summary>
        /// The person's shelf under the redesign stands on a floor arc as every place does, the slab under its characters retired (the lead's
        /// 2026-11-17T19:25Z ruling 3: the places' floor arcs replace it): the arc about the eyes through the shelf's middle, as wide as its
        /// places, a tick at each end. The older shelf keeps its slab.
        /// </summary>
        private void StandShelfFloorArc()
        {
            var redesign = RoomDesign.Enabled && !BesideAWindow;
            if (shelfSurface != null && shelfSurface.gameObject.activeSelf == redesign) shelfSurface.gameObject.SetActive(!redesign);
            var drawn = redesign && arc.gameObject.activeInHierarchy;
            if (!drawn)
            {
                if (shelfArc != null && shelfArc.gameObject.activeSelf) { shelfArc.gameObject.SetActive(false); foreach (var tick in shelfTicks!) tick.gameObject.SetActive(false); }
                return;
            }
            if (shelfArc == null)
            {
                floorArcMaterial ??= new Material(Shader.Find("Sprites/Default"));
                shelfArc = FloorLine(transform, "Shelf floor arc", 24, FloorArcAlpha);
                shelfTicks = new[] { FloorLine(transform, "Shelf floor arc tick", 2, FloorTickAlpha), FloorLine(transform, "Shelf floor arc tick", 2, FloorTickAlpha) };
            }
            if (!shelfArc.gameObject.activeSelf) { shelfArc.gameObject.SetActive(true); foreach (var tick in shelfTicks!) tick.gameObject.SetActive(true); }
            var eyes = shelfViewer;
            float YawOf(Vector3 point) => Mathf.Atan2(point.x - eyes.x, point.z - eyes.z) * Mathf.Rad2Deg;
            var half = ShelfWidth / 2f - MinimumFittedWidth / 2f;
            var left = YawOf(arc.TransformPoint(new Vector3(-half, 0f, 0f)));
            var right = YawOf(arc.TransformPoint(new Vector3(half, 0f, 0f)));
            var distance = new Vector2(arc.position.x - eyes.x, arc.position.z - eyes.z).magnitude;
            DrawFloorArc(shelfArc, shelfTicks!, eyes, arc.position.y, distance, left, left + Mathf.DeltaAngle(left, right));
        }

        /// <summary>Lays a floor arc about <paramref name="eyes"/> at <paramref name="height"/> and <paramref name="distance"/> from one yaw to another, a tick at each end.</summary>
        private static void DrawFloorArc(LineRenderer line, LineRenderer[] ticks, Vector3 eyes, float height, float distance, float fromYaw, float toYaw)
        {
            var points = line.positionCount;
            for (var step = 0; step < points; step++)
            {
                var yaw = Mathf.Lerp(fromYaw, toYaw, step / (float)(points - 1));
                line.SetPosition(step, new Vector3(eyes.x, height, eyes.z) + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance);
            }
            var width = distance / PlaceBearings.RingOneDistance;
            line.widthMultiplier = FloorArcWidth * width;
            for (var end = 0; end < 2; end++)
            {
                var foot = line.GetPosition(end == 0 ? 0 : points - 1);
                ticks[end].SetPosition(0, foot - Vector3.up * FloorTickHeight * width / 2f);
                ticks[end].SetPosition(1, foot + Vector3.up * FloorTickHeight * width / 2f);
                ticks[end].widthMultiplier = FloorArcWidth * width;
            }
        }

        /// <summary>
        /// Where a place whose bearing is <paramref name="bearing"/> stands for the eyes at <paramref name="eyes"/>, ahead at <paramref name="ahead"/>
        /// degrees of the world's yaw, the person's shelf at <paramref name="shelfHeight"/>: its floor arc's middle, at the bearing's distance
        /// and drop, and its facing, away from the eyes.
        /// </summary>
        public static (Vector3 At, Quaternion Facing) PlacePose(Vector3 eyes, float ahead, float shelfHeight, PlaceBearing bearing)
        {
            var facing = Quaternion.Euler(0f, ahead + bearing.YawDegrees, 0f);
            return (new Vector3(eyes.x, shelfHeight - bearing.Drop, eyes.z) + facing * Vector3.forward * bearing.Distance, facing);
        }

        /// <summary>
        /// A place's pitch in metres and how many stand in its front row: the pitch from its widest tag as drawn (<paramref name="widestTag"/>
        /// metres, or the longest name's before any has drawn), never under a body and a degree and a half (FIELD's <see cref="PlacePitch"/>);
        /// the row as many as the place's span holds with its outer tags inside it, and never more than <paramref name="rule"/>'s one row.
        /// </summary>
        public static (float Pitch, int RowMost) PlaceRows(float widestTag, float scale, float distance, PlaceRule rule)
        {
            if (widestTag <= 0f) widestTag = WidestTagMetres * distance / PlaceBearings.RingOneDistance;
            var tagDegrees = PlacePitch.DegreesOf(widestTag, distance);
            var bodyDegrees = PlacePitch.DegreesOf(BodyWidth * scale, distance);
            var pitch = (float)PlacePitch.MetresOf(PlacePitch.PitchDegrees(tagDegrees, bodyDegrees), distance);
            return (pitch, Mathf.Min(PlacePitch.RowMost(tagDegrees, bodyDegrees), rule.FullMost));
        }

        /// <summary>
        /// Where a place's character <paramref name="index"/> of <paramref name="count"/> stands in the place's own space, and at what size:
        /// rows of <paramref name="rowMost"/>, each centred at <paramref name="pitch"/> a character, the first in front; each further row
        /// behind and above it at the back row's size (FIELD's places sheet: three in front, two behind).
        /// </summary>
        public static (Vector3 Local, float Scale) PlaceSpot(int index, int count, int rowMost, float pitch)
        {
            rowMost = Mathf.Max(1, rowMost);
            var row = index / rowMost;
            var inRow = Mathf.Min(rowMost, count - row * rowMost);
            var across = (index - row * rowMost - (inRow - 1) * 0.5f) * pitch;
            return row == 0 ? (new Vector3(across, 0f, 0f), 1f) : (new Vector3(across, BackRowRise * row, BackRowDepth * row), BackRowScale);
        }

        private OtherPlace MakeOtherPlace(string projectId)
        {
            var root = new GameObject("Place " + projectId).transform;
            if (placesRoot == null)
            {
                placesRoot = new GameObject("Other places").transform;
                placesRoot.SetParent(transform, false);
            }
            root.SetParent(placesRoot, false);
            var sign = new GameObject("Place sign point").transform;
            sign.SetParent(root, false);
            floorArcMaterial ??= new Material(Shader.Find("Sprites/Default"));
            return new OtherPlace
            {
                Root = root,
                Sign = sign,
                Arc = FloorLine(root, "Floor arc", 24, FloorArcAlpha),
                Ticks = new[] { FloorLine(root, "Floor arc tick", 2, FloorTickAlpha), FloorLine(root, "Floor arc tick", 2, FloorTickAlpha) },
            };
        }

        private static LineRenderer FloorLine(Transform parent, string name, int points, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = floorArcMaterial;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.numCapVertices = 2;
            line.startColor = line.endColor = new Color(1f, 1f, 1f, alpha);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
