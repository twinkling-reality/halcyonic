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

        /// <summary>The four corners of <paramref name="view"/>'s tag as drawn now, in the world; null where it draws none. Published by the room's
        /// parts as <see cref="DrawnTagSize"/> is, so a place's back row can rise until its tags clear the front row's bodies as the eyes see them.</summary>
        public static System.Func<CharacterView, Vector3[]?>? DrawnTagCorners { get; set; }

        /// <summary>Whether a place's layout says each pass it reads (its renders set it, for the places check's log); nothing else does.</summary>
        public static bool SayLayout { get; set; }

        /// <summary>Lays <paramref name="view"/>'s tag again with its words as they are, after its bound changed (published by the room's parts).</summary>
        public static System.Action<CharacterView>? RedrawTag { get; set; }

        /// <summary>How high a back row may rise to clear the front row's bodies, in metres: past it the row stands as high as it can.</summary>
        public const float BackRowRiseMost = 0.8f;

        /// <summary>The step a back row rises by while its tags stand over the front row's bodies, in metres.</summary>
        public const float BackRowRiseStep = 0.02f;

        /// <summary>How far a tag's plate draws past its laid size as the eyes see it, both sides together, in degrees: its glass's edge and rim.</summary>
        public const float PlateReachDegrees = 0.8f;

        /// <summary>The least angle kept between a back-row tag's foot and a front-row body's top, in degrees (the gap kept between things).</summary>
        public const float BackRowClearDegrees = 1f;

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

        /// <summary>
        /// The widest a place's tag stands as seen, pads included, in degrees: its span holding the rule's one row with half a degree between tags
        /// (FIELD's places sheet v4: one row of three; (32.6 - 2 x 0.5) / 3 = 10.53). Longer names break at a word onto more lines, never cut. The
        /// person's shelf keeps the tag's own bound.
        /// </summary>
        public static float PlaceTagMostDegrees => PlaceTagMostDegreesIn(PlaceBearings.SpanDegrees);

        /// <summary>The widest a tag stands in a place <paramref name="spanDegrees"/> wide: its share of the rule's one row (past four places, narrower).</summary>
        public static float PlaceTagMostDegreesIn(float spanDegrees) =>
            (float)((spanDegrees - (PlaceRule.Default.FullMost - 1) * PlacePitch.TagsApartDegrees) / PlaceRule.Default.FullMost);

        /// <summary>The gap between a place's lowest tag and its sign, in metres at 1.4 m.</summary>
        public const float SignGap = 0.02f;

        private sealed class OtherPlace
        {
            public Transform Root = null!;
            public Transform Sign = null!;
            public Transform More = null!;
            public LineRenderer Arc = null!;
            public LineRenderer[] Ticks = null!;
            public readonly Dictionary<string, CharacterView> Views = new Dictionary<string, CharacterView>(System.StringComparer.Ordinal);
            public List<CharacterView> Laid = new List<CharacterView>();
        }

        /// <summary>The room's floor, in the world's metres: a farther ring never stands below it.</summary>
        public const float FloorHeight = 0f;

        /// <summary>The step a farther ring is lowered or raised by while it stands over a nearer ring's team as the eyes see it, in metres.</summary>
        public const float RingStandStep = 0.05f;

        /// <summary>The least angle kept between a farther ring's team and a nearer ring's at the same bearing, as the eyes see them, in degrees.</summary>
        public const float RingClearDegrees = 1f;

        /// <summary>
        /// Stands a farther ring's place (<paramref name="root"/>, its team <paramref name="views"/>) clear of the nearer ring's team at its bearing
        /// (<paramref name="nearer"/>) as the eyes see them (AUDIT's 21:2xZ: tags over tags, bodies over bodies): first lowered, nearer the floor
        /// as FIELD's sheet has it, until its whole team stands under the nearer team's lowest tag; where the floor comes first, raised instead
        /// until it stands over the nearer team's heads. <paramref name="relay"/> lays the place again after each move. Returns whether it stands clear.
        /// </summary>
        public static bool StandClearOfNearer(Transform root, IReadOnlyList<CharacterView> views, IReadOnlyList<CharacterView> nearer, Vector3 eyes, float floor, System.Action relay)
        {
            float Up(Vector3 point) { var away = point - eyes; return Mathf.Atan2(away.y, new Vector2(away.x, away.z).magnitude) * Mathf.Rad2Deg; }
            IEnumerable<Vector3> Drawn(IEnumerable<CharacterView> team)
            {
                foreach (var view in team)
                {
                    foreach (var corner in view.BodyCorners()) yield return corner;
                    if (DrawnTagCorners?.Invoke(view) is Vector3[] tag) foreach (var corner in tag) yield return corner;
                }
            }
            var nearLowest = Drawn(nearer).Select(Up).DefaultIfEmpty(0f).Min();
            var nearHighest = Drawn(nearer).Select(Up).DefaultIfEmpty(0f).Max();
            bool Under() => Drawn(views).Select(Up).DefaultIfEmpty(0f).Max() <= nearLowest - RingClearDegrees;
            bool Over() => Drawn(views).Select(Up).DefaultIfEmpty(0f).Min() >= nearHighest + RingClearDegrees;
            var start = root.position;
            while (!Under() && root.position.y - RingStandStep >= floor)
            {
                root.position -= Vector3.up * RingStandStep;
                relay();
            }
            if (Under()) return true;
            root.position = start;
            relay();
            for (var step = 0; step < 40 && !Over(); step++)
            {
                root.position += Vector3.up * RingStandStep;
                relay();
            }
            return Over();
        }

        private readonly Dictionary<string, OtherPlace> others = new Dictionary<string, OtherPlace>(System.StringComparer.Ordinal);
        private readonly List<(RoomPlace Place, PlaceBearing Bearing)> otherOrder = new List<(RoomPlace, PlaceBearing)>();
        private float? aheadHeld;
        private bool gatherPending;
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
                var bearing = PlaceBearings.Of(index, wanted.Count);
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
                    // Its place's share of the row, as the place's span is now (narrower past four places).
                    view.TagMostDegrees = PlaceTagMostDegreesIn(bearing.SpanDegrees);
                    view.Show(CharacterPresenter.Present(state.Workstreams[id], state, live));
                }
                LayOtherPlace(stood, shown, bearing, eyes);
            }
            // A place found one to a row stands its crowd on the next refresh, once.
            if (gatherPending)
            {
                gatherPending = false;
                Refresh();
                return;
            }
            // A farther ring stands clear of the nearer ring's team at its bearing, as the eyes see them.
            for (var index = 0; index < otherOrder.Count; index++)
            {
                var (place, bearing) = otherOrder[index];
                if (bearing.Ring == 0) continue;
                var nearerIndex = index - PlaceBearings.Centres.Length;
                if (nearerIndex < 0 || !others.TryGetValue(otherOrder[nearerIndex].Place.ProjectId, out var nearer)) continue;
                var stood = others[place.ProjectId];
                var shownHere = stood.Laid.Select(view => view.WorkstreamId).ToList();
                StandClearOfNearer(stood.Root, stood.Laid, nearer.Laid, eyes, FloorHeight, () => LayOtherPlace(stood, shownHere, bearing, eyes));
            }
        }

        /// <summary>Stands a place's characters in rows, its floor arc and its sign's point, for the eyes at <paramref name="eyes"/>.</summary>
        private void LayOtherPlace(OtherPlace stood, IReadOnlyList<string> shown, PlaceBearing bearing, Vector3 eyes)
        {
            var distance = bearing.Distance;
            stood.Laid = shown.Select(id => stood.Views[id]).ToList();
            var (_, rowMost, _) = LayPlace(stood.Laid, radius, distance, Places.Rule, eyes, bearing.SpanDegrees);
            // A place laid one to a row gathers its team at once (AUDIT's 21:4xZ); said once, so the next refresh stands its crowd.
            var projectId = others.First(pair => ReferenceEquals(pair.Value, stood)).Key;
            if (rowMost == 1 && stood.Laid.Count > 1 && Places.OneARow.Add(projectId)) gatherPending = true;
            // FIELD's places sheet v4: the floor arc under the team's tags, the place's name once under the arc, and "{n} More" at the
            // place's inner end over the front row's heads.
            var arcY = StandSignPoints(stood.Root, stood.Laid, bearing.SpanDegrees, bearing.YawDegrees, distance, eyes, stood.Sign, stood.More);
            var half = bearing.SpanDegrees / 2f;
            var middle = stood.Root.eulerAngles.y;
            DrawFloorArc(stood.Arc, stood.Ticks, eyes, stood.Root.position.y + arcY, distance, middle - half, middle + half);
        }

        private RoomPlace? ownPlace;
        private bool ownGatherPending;
        private Transform? ownRoot, ownSign, ownMore;
        private string? ownSignFor;

        /// <summary>The place the person's shelf stands under the redesign (the selected task's project's), or null.</summary>
        public RoomPlace? OwnPlace => ownPlace;

        /// <summary>
        /// Lays the person's shelf as a place (the lead's 2026-11-17T22:4xZ ruling): every settled character of its place by <see cref="LayOwnShelf"/>,
        /// within the fitted shelf's span; one to a row, it gathers behind "{n} More" as a place of the room does; and where it gathers or is spread,
        /// its sign stands as theirs do (<see cref="PlaceCreated"/>). Nothing under the older shelf, which keeps <see cref="CharacterPlace"/>.
        /// </summary>
        private void LayOwnShelfNow()
        {
            if (ownPlace is not RoomPlace place || !onSurface || arc == null) { StandOwnSign(null, null); return; }
            var laid = new List<CharacterView>();
            foreach (var id in place.Shown)
                if (views.TryGetValue(id, out var view) && standings.TryGetValue(id, out var standing) && standing.Settled) laid.Add(view);
            if (laid.Count == 0) { StandOwnSign(null, null); return; }
            var distance = Across(arc.position, shelfViewer);
            var (_, rowMost, _) = LayOwnShelf(laid, radius, distance, Places.Rule, shelfViewer);
            if (rowMost == 1 && laid.Count > 1 && Places.OneARow.Add(place.ProjectId)) ownGatherPending = true;
            StandOwnSign(place, laid);
        }

        /// <summary>The person's shelf's sign, where its place gathers a crowd or is spread open: none otherwise, so a shelf of a few stands as ever.</summary>
        private void StandOwnSign(RoomPlace? place, IReadOnlyList<CharacterView>? laid)
        {
            var wanted = place != null && laid != null && (place.More > 0 || Places.Spread.Contains(place.ProjectId));
            if (!wanted || ownSignFor != place!.ProjectId)
            {
                if (ownSign != null) for (var child = ownSign.childCount - 1; child >= 0; child--) Destroy(ownSign.GetChild(child).gameObject);
                ownSignFor = null;
            }
            if (!wanted) return;
            if (ownRoot == null)
            {
                ownRoot = new GameObject("Own place").transform;
                ownRoot.SetParent(arc, false);
                ownSign = new GameObject("Place sign point").transform;
                ownSign.SetParent(ownRoot, false);
                ownMore = new GameObject("Place more point").transform;
                ownMore.SetParent(ownRoot, false);
            }
            ownRoot.localPosition = Vector3.right * OwnShelfAcross(radius);
            ownRoot.localRotation = Quaternion.identity;
            // Its More at the shelf's right end, away from New Project's place at its left.
            StandSignPoints(ownRoot, laid!, OwnShelfSpanDegrees(Across(arc.position, shelfViewer)), -1f, Across(arc.position, shelfViewer), shelfViewer, ownSign!, ownMore!);
            if (ownSignFor == null)
            {
                ownSignFor = place!.ProjectId;
                PlaceCreated?.Invoke(place.ProjectId, ownSign!);
            }
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

        /// <summary>The floor arc's gap under the place's lowest tag, the name's under the arc, and the More's middle over the front heads (half its glass and a degree), in metres at 1.4 m.</summary>
        public const float ArcUnderTags = 0.02f, NameUnderArc = 0.02f, MoreOverHeads = 0.065f;

        /// <summary>How far in from the place's inner edge its More stands, in degrees about the eyes (FIELD's sheet: three degrees).</summary>
        public const float MoreInDegrees = 3f;

        /// <summary>
        /// Stands a place's sign points, as FIELD's places sheet v4 draws them, in the place's own space: <paramref name="name"/> centred under
        /// the floor arc, which stands under the team's lowest tag; <paramref name="more"/> at the place's inner end (toward ahead), over the
        /// front row's heads. Both stand upright facing the eyes, sized for their distance (the room's units are 1 at it). Returns the floor
        /// arc's height in the place's space. The stage and its check stand them by this one rule.
        /// </summary>
        public static float StandSignPoints(Transform root, IReadOnlyList<CharacterView> team, float spanDegrees, float yawDegrees, float distance, Vector3 eyes,
            Transform name, Transform more)
        {
            var width = distance / PlaceBearings.RingOneDistance;
            var lowest = 0f;
            var highestBody = 0f;
            var nearest = 0f;
            foreach (var view in team)
            {
                if (DrawnTagCorners?.Invoke(view) is Vector3[] tag)
                    foreach (var corner in tag)
                    {
                        var local = root.InverseTransformPoint(corner);
                        lowest = Mathf.Min(lowest, local.y);
                        nearest = Mathf.Min(nearest, local.z);
                    }
                foreach (var corner in view.BodyCorners()) highestBody = Mathf.Max(highestBody, root.InverseTransformPoint(corner).y);
            }
            var arcY = lowest - ArcUnderTags * width;
            // At the tags' own depth: they stand on the lip, nearer the eyes than the characters, and a name further back would read higher.
            name.localPosition = new Vector3(0f, arcY - NameUnderArc * width, nearest);
            // The inner end: toward ahead, the person's right for a place on the left.
            var inner = yawDegrees < 0f ? 1f : -1f;
            var across = (float)PlacePitch.MetresOf(Mathf.Max(0f, spanDegrees - 2f * MoreInDegrees), distance) / 2f;
            more.localPosition = new Vector3(inner * across, highestBody + MoreOverHeads * width, 0f);
            foreach (var point in new[] { name, more })
            {
                var away = point.position - eyes;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-6f) point.rotation = Quaternion.LookRotation(away, Vector3.up);
                point.localScale = Vector3.one * Vector3.Distance(point.position, eyes) / Mathf.Max(1e-4f, root.lossyScale.x);
            }
            return arcY;
        }

        /// <summary>The point a place's "{n} More" stands at (<see cref="StandSignPoints"/>); null for a place not standing.</summary>
        public Transform? MorePointOf(string projectId) => others.TryGetValue(projectId, out var stood) ? stood.More
            : ownSignFor == projectId ? ownMore : null;

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
        public static (float Pitch, int RowMost) PlaceRows(float widestTag, float scale, float distance, PlaceRule rule, float spanDegrees = PlaceBearings.SpanDegrees)
        {
            if (widestTag <= 0f) widestTag = WidestTagMetres * distance / PlaceBearings.RingOneDistance;
            var tagDegrees = PlacePitch.DegreesOf(widestTag, distance);
            var bodyDegrees = PlacePitch.DegreesOf(BodyWidth * scale, distance);
            var pitch = (float)PlacePitch.MetresOf(PlacePitch.PitchDegrees(tagDegrees, bodyDegrees), distance);
            return (pitch, Mathf.Min(PlacePitch.RowMost(tagDegrees, bodyDegrees, spanDegrees), rule.FullMost));
        }

        /// <summary>
        /// Stands a place's characters, <paramref name="views"/> in its order, under the place's root (each view's parent) for the eyes at
        /// <paramref name="eyes"/>, by the one rule the stage and its check share. First at the longest name's pitch; then at the pitch of its
        /// widest tag AS DRAWN there (a tag's size follows where it stands); then, where a back row stands, that row rises in steps until no
        /// back-row tag stands over a front-row body as the eyes see them (AUDIT3's R1: seen from a seated person's eyes, a row behind
        /// appears higher, so a tag hung under its feet would cover the heads in front). Returns the pitch, the row and the back row's rise.
        /// </summary>
        /// <param name="across">How far right of the root's middle the team's middle stands, in the root's metres: the person's shelf stands its
        /// team right of New Project's place (<see cref="LayOwnShelf"/>); a place of the room stands it on its middle.</param>
        public static (float Pitch, int RowMost, float Rise) LayPlace(IReadOnlyList<CharacterView> views, float scale, float distance, PlaceRule rule, Vector3 eyes,
            float spanDegrees = PlaceBearings.SpanDegrees, float across = 0f)
        {
            var count = views.Count;
            var rise = BackRowRise;
            // How far the front row's tags stand across the floor: each row behind stands its pitch wider by its own distance over it, so its
            // tags stand as far apart as the eyes see the front row's (a row further back at the front's metres reads closer together).
            var rowDistance = distance;
            var (pitch, rowMost) = PlaceRows(0f, scale, distance, rule, spanDegrees);
            void Lay()
            {
                for (var index = 0; index < count; index++)
                {
                    var (local, size) = PlaceSpot(index, count, rowMost, pitch, rise, rowDistance);
                    var view = views[index];
                    view.transform.localPosition = local + Vector3.right * across;
                    view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    view.transform.localScale = Vector3.one * scale * size;
                    view.PlaceShelfLabel(eyes);
                }
                // Each pass reads the tags as they finally stand: the back row's type held to the front's before anything is measured.
                HoldBackRowType(views, rowMost, eyes);
            }
            Lay();
            float WidestDegrees()
            {
                var most = 0f;
                foreach (var view in views)
                {
                    // As the eyes see it: the yaw between the tag's drawn corners, where they are published; else its width at the lip.
                    if (DrawnTagCorners?.Invoke(view) is Vector3[] corners)
                    {
                        float Yaw(Vector3 point) => Mathf.Atan2(point.x - eyes.x, point.z - eyes.z) * Mathf.Rad2Deg;
                        most = Mathf.Max(most, corners.Max(Yaw) - corners.Min(Yaw));
                    }
                    else if (DrawnTagSize?.Invoke(view) is Vector2 drawn)
                        most = Mathf.Max(most, (float)PlacePitch.DegreesOf(drawn.x, Across(view.Labels.position, eyes)));
                }
                return most;
            }
            // The row from the tags as the EYES see them (FIELD's PlacePitch is in degrees from the eyes): a place's tags stand lower and nearer or
            // further than its floor arc's distance, so their metres are read at their own distance, never at the arc's.
            // Laid, read and laid again until the row stands still: a row's tags read as they stand in it (three passes at most).
            for (var settle = 0; settle < 3; settle++)
            {
                var widestSeen = WidestDegrees();
                if (widestSeen <= 0f) break;
                var rowWas = rowMost;
                var bodyDegrees = PlacePitch.DegreesOf(BodyWidth * scale, distance);
                // The plates' glass reaches past the tag's laid size, so the half degree between tags is read on the plates (AUDIT's 21:03Z).
                var pitchDegrees = PlacePitch.PitchDegrees(widestSeen + PlateReachDegrees, bodyDegrees);
                // Tags move with their characters, so a pitch in metres that sets the tags pitchDegrees apart as the eyes see them, across the
                // floor at the tags' own distance, is the characters' pitch.
                var tagDistance = views.Take(Mathf.Min(rowMost, count)).Average(view => Across(view.Labels.position, eyes));
                pitch = (float)PlacePitch.MetresOf(pitchDegrees, tagDistance);
                rowDistance = tagDistance;
                rowMost = Mathf.Min(PlacePitch.RowMost(widestSeen + PlateReachDegrees, bodyDegrees, spanDegrees), rule.FullMost);
                if (SayLayout) Debug.Log("Halcyonic: places: layout pass " + settle + ": widest tag " + widestSeen.ToString("0.00") + " degrees, body " + bodyDegrees.ToString("0.00") + ", rows of " + rowMost + ".");
                Lay();
                if (settle > 0 && rowMost == rowWas) break;
            }
            if (count <= rowMost || DrawnTagCorners == null) return (pitch, rowMost, rise);
            float Yaw(Vector3 point) => Mathf.Atan2(point.x - eyes.x, point.z - eyes.z) * Mathf.Rad2Deg;
            float Up(Vector3 point) { var away = point - eyes; return Mathf.Atan2(away.y, new Vector2(away.x, away.z).magnitude) * Mathf.Rad2Deg; }
            bool Covers()
            {
                for (var back = rowMost; back < count; back++)
                {
                    if (DrawnTagCorners(views[back]) is not Vector3[] tag) continue;
                    float tagLeft = tag.Min(Yaw), tagRight = tag.Max(Yaw), tagFoot = tag.Min(Up);
                    // Every nearer row's bodies, so a place one or two to a row keeps each row's tags clear of the row before it too.
                    for (var front = 0; front < back && front / rowMost < back / rowMost; front++)
                    {
                        var body = views[front].BodyCorners().ToList();
                        if (body.Count == 0) continue;
                        if (tagRight <= body.Min(Yaw) || tagLeft >= body.Max(Yaw)) continue;
                        if (tagFoot < body.Max(Up) + BackRowClearDegrees) return true;
                    }
                }
                return false;
            }
            while (Covers() && rise < BackRowRiseMost)
            {
                rise = Mathf.Min(BackRowRiseMost, rise + BackRowRiseStep);
                Lay();
            }
            HoldBackRowType(views, rowMost, eyes);
            return (pitch, rowMost, rise);
        }

        /// <summary>
        /// The person's shelf's span as the eyes see it from <paramref name="distance"/>: the character places of the fitted shelf at its fullest row
        /// (<see cref="FittedWidth"/> of one row's count and New Project's place, the lead's 2026-11-14T02:21Z ruling 1 (a)), New Project's place left
        /// out. The lead's 2026-11-17T22:4xZ ruling: the person's shelf is a place laid by <see cref="LayPlace"/>, the fitted width its width bound;
        /// one span whatever the count, so a tag's bound never changes as tasks come and go.
        /// </summary>
        public static float OwnShelfSpanDegrees(float distance)
        {
            var row = PlaceRule.Default.FullMost;
            var pitch = PitchOf(row + 1, FittedWidth(row + 1));
            return 2f * Mathf.Atan(row * pitch / 2f / Mathf.Max(1e-3f, distance)) * Mathf.Rad2Deg;
        }

        /// <summary>How far right of the shelf's middle its team's middle stands, in the shelf's metres: right of New Project's place.</summary>
        public static float OwnShelfAcross(float scale) =>
            RoomDesign.Enabled ? 0.5f * PitchOf(ShelfPlaces, FittedWidth(ShelfPlaces)) * scale / AgentShelfDraft.Scale : 0f;

        /// <summary>
        /// Lays the person's shelf's team, <paramref name="views"/> in its place's order under the shelf (each view's parent), as a place: its tags
        /// bounded as a place's are within the fitted shelf's span (<see cref="OwnShelfSpanDegrees"/>), the pitch from its widest tag as drawn, its
        /// rows rising until clear (<see cref="LayPlace"/>), the team right of New Project's place. The stage and its check lay it by this one rule.
        /// </summary>
        public static (float Pitch, int RowMost, float Rise) LayOwnShelf(IReadOnlyList<CharacterView> views, float scale, float distance, PlaceRule rule, Vector3 eyes) =>
            LayPlace(views, scale, distance, rule, eyes, OwnShelfSpanDegrees(distance), OwnShelfAcross(scale));

        /// <summary>
        /// A back row's tags keep the front row's type size as seen and never stand larger (the lead's 2026-11-17T20:4xZ MUST): each back-row
        /// character's labels shrink by how much larger a tag unit stands at its distance than the front row's mean.
        /// </summary>
        private static void HoldBackRowType(IReadOnlyList<CharacterView> views, int rowMost, Vector3 eyes)
        {
            if (views.Count <= rowMost) return;
            // The type as seen: a label's scale over its tag's distance from the eyes.
            float? TypeAt(CharacterView view)
            {
                if (DrawnTagCorners?.Invoke(view) is not Vector3[] corners) return null;
                var centre = (corners[0] + corners[1] + corners[2] + corners[3]) / 4f;
                return view.Labels.lossyScale.x / Mathf.Max(1e-4f, Vector3.Distance(centre, eyes));
            }
            var front = views.Take(rowMost).Select(TypeAt).Where(type => type.HasValue).Select(type => type!.Value).DefaultIfEmpty(0f).Average();
            if (front <= 0f) return;
            // Twice, since a tag moves as its labels scale.
            for (var pass = 0; pass < 2; pass++)
                for (var index = rowMost; index < views.Count; index++)
                    if (TypeAt(views[index]) is float type && type > front) views[index].Labels.localScale *= front / type;
        }

        /// <summary>How far <paramref name="point"/> stands from <paramref name="eyes"/> across the floor: what a yaw angle is read at.</summary>
        private static float Across(Vector3 point, Vector3 eyes) => Mathf.Max(1e-4f, new Vector2(point.x - eyes.x, point.z - eyes.z).magnitude);

        /// <summary>
        /// Where a place's character <paramref name="index"/> of <paramref name="count"/> stands in the place's own space, and at what size:
        /// rows of <paramref name="rowMost"/>, each centred at <paramref name="pitch"/> a character, the first in front; each further row
        /// behind and above it at the back row's size (FIELD's places sheet: three in front, two behind).
        /// </summary>
        /// <param name="frontDistance">How far the front row's tags stand from the eyes across the floor: a row behind widens its pitch by its
        /// extra depth over it, so its tags keep the front's apparent spacing; none keeps one pitch for every row.</param>
        public static (Vector3 Local, float Scale) PlaceSpot(int index, int count, int rowMost, float pitch, float rise = BackRowRise, float? frontDistance = null)
        {
            rowMost = Mathf.Max(1, rowMost);
            var row = index / rowMost;
            var inRow = Mathf.Min(rowMost, count - row * rowMost);
            var rowPitch = frontDistance is float front && front > 1e-3f ? pitch * (front + BackRowDepth * row) / front : pitch;
            var across = (index - row * rowMost - (inRow - 1) * 0.5f) * rowPitch;
            return row == 0 ? (new Vector3(across, 0f, 0f), 1f) : (new Vector3(across, rise * row, BackRowDepth * row), BackRowScale);
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
            var more = new GameObject("Place more point").transform;
            more.SetParent(root, false);
            floorArcMaterial ??= new Material(Shader.Find("Sprites/Default"));
            return new OtherPlace
            {
                Root = root,
                Sign = sign,
                More = more,
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
