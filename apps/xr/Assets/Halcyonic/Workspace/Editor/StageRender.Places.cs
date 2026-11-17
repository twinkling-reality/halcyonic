#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class StageRender
    {
        /// <summary>The teams the places check stands, in RoomPlaces' order: ring one's four, then one in ring two (FIELD's hero, Weather Widget).</summary>
        private static readonly (string Project, string[] Tasks)[] PlaceTeams =
        {
            ("Storefront API", new[] { "Fix the checkout total when a coupon and a gift card are both applied", "Add rate limits", "Write the refund tests" }),
            ("Kite Run", new[] { "Make the kite turn faster", "Add a night level", "Tune the wind", "Fix the score on restart", "Add sounds" }),
            ("Field Notes", new[] { "Sync notes between devices" }),
            ("Recipe Box", new[] { "Import recipes from a link", "Scale servings" }),
            ("Weather Widget", new[] { "Show the hourly forecast", "Add a dark look", "Cache the last reading" }),
        };

        /// <summary>
        /// The places of the projects beside the person's shelf (the lead's 2026-11-17T18:56Z and 19:05Z seated rule; FIELD's places sheet v4),
        /// stood by the stage's own rule (<see cref="CharacterStage.PlacePose"/>, <see cref="CharacterStage.LayPlace"/>) at both text sizes, the tags as the room draws them: every place's tags and bodies stand
        /// between the front's 30 degrees and the seated reach's 100 either side; neighbouring places in a ring keep the lead's 4 degrees;
        /// neighbouring tags in a row never meet (half a degree); the back row stands behind the front and no back-row tag stands over a
        /// front-row body as the eyes see it (AUDIT3's R1); ring two stands further and lower. The frames, looking left, right and from
        /// above, are in Builds/PlacesRenders for AUDIT's look beside the sheet.
        /// </summary>
        public static void PlacesCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(Places);
            foreach (var failure in failures) Debug.LogError("Halcyonic: places: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: places: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        private static List<string> Places(string variant)
        {
            var failures = new List<string>();
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PlacesRenders", variant));
            Directory.CreateDirectory(folder);
            var was = RoomDesign.Enabled;
            GameObject? root = null;
            RenderTexture? texture = null;
            try
            {
                RoomDesign.Enabled = true;
                CharacterStage.SayLayout = true;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Made after the new scene, which unloads every asset nothing references.
                texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                root = new GameObject("Places");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var shelfHeight = EyeHeight - 0.45f;
                float Yaw(Vector3 point) { var away = point - eyes; return Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg; }
                float Up(Vector3 point) { var away = point - eyes; return Mathf.Atan2(away.y, new Vector2(away.x, away.z).magnitude) * Mathf.Rad2Deg; }
                Rect Seen(IEnumerable<Vector3> corners)
                {
                    var list = corners.ToList();
                    var left = list.Min(Yaw);
                    var bottom = list.Min(Up);
                    return Rect.MinMaxRect(left, bottom, list.Max(Yaw), list.Max(Up));
                }
                IEnumerable<Vector3> TagCorners(RoomTag tag)
                {
                    for (var x = -1; x <= 1; x += 2)
                        for (var y = -1; y <= 1; y += 2)
                            yield return tag.transform.TransformPoint(new Vector3(x * tag.Size.x / 2f, y * tag.Size.y / 2f, 0f));
                }
                var extents = new List<(PlaceBearing Bearing, Rect Seen)>();
                // Each tag's height as the eyes see it, by ring and row: a tag keeps its size as seen at every ring and row (the lead's
                // 2026-11-17T20:4xZ MUST, as a game's name plates do), never growing past the front row of ring one.
                var tagHeights = new List<(int Ring, bool Back, string Name, float Degrees)>();
                for (var index = 0; index < PlaceTeams.Length; index++)
                {
                    var (project, tasks) = PlaceTeams[index];
                    var bearing = PlaceBearings.Of(index);
                    var place = new GameObject("Place " + project).transform;
                    place.SetParent(root.transform, false);
                    var (at, facing) = CharacterStage.PlacePose(eyes, 0f, shelfHeight, bearing);
                    place.SetPositionAndRotation(at, facing);
                    var views = new List<(CharacterView View, RoomNameTag Tag)>();
                    for (var task = 0; task < tasks.Length; task++)
                    {
                        var view = CharacterView.Create(place, "place-" + index + "-" + task);
                        view.Grounded = true;
                        view.TagMostDegrees = CharacterStage.PlaceTagMostDegrees;
                        var activity = task == 0 && index == 0 ? CharacterActivity.WaitingForHuman : CharacterActivity.Working;
                        var shown = new CharacterPresentation("place-" + index + "-" + task, tasks[task], activity, CharacterPresenter.LabelOf(activity),
                            activity == CharacterActivity.WaitingForHuman ? AttentionLevel.ActionRequired : AttentionLevel.None, Array.Empty<string>(), 0, false, false, false);
                        var tag = RoomNameTag.On(view);
                        view.Show(shown);
                        views.Add((view, tag));
                    }
                    // Laid by the stage's own rule: the pitch of the widest tag as drawn there, and the back row risen clear of the front.
                    foreach (var each in views) Advance(each.View, 1f);
                    var (pitch, rowMost, rise) = CharacterStage.LayPlace(views.Select(each => each.View).ToList(), AgentShelfDraft.Scale, bearing.Distance, PlaceRule.Default, eyes);
                    WorkspaceRender.ForceMeshes(root);
                    var tags = views.Select(each => each.Tag.Tag).ToList();
                    if (tags.Any(tag => tag == null)) { failures.Add(project + ": a character stands with no tag"); continue; }
                    var seenTags = tags.Select(tag => Seen(TagCorners(tag!))).ToList();
                    // The row is FIELD's rule's own answer for the tags as drawn (PlacePitch.RowMost: as many as the span holds with its outer tags
                    // inside it), never fewer; each tag's drawn width and its bound are said, for FIELD's sheet.
                    var drawnDegrees = tags.Select(tag => Seen(TagCorners(tag!)).width).ToList();
                    // Read with half a degree's margin: a tag's angle moves by about that much with where in the row it stands, so a place right at
                    // the line (Kite Run's 10.2 to 10.6 degrees against the 10.53 its row of three holds) may keep the safer row.
                    var ruleRow = Math.Min(PlacePitch.RowMost(drawnDegrees.Max() + (float)PlacePitch.TagsApartDegrees, PlacePitch.DegreesOf(CharacterStage.BodyWidth * AgentShelfDraft.Scale, bearing.Distance)), PlaceRule.Default.FullMost);
                    if (rowMost < ruleRow) failures.Add(project + ": " + views.Count + " stand in rows of " + rowMost + " where its tags as drawn let " + ruleRow + " stand");
                    Debug.Log("Halcyonic: places: " + variant + " " + project + " tags drawn " + string.Join(", ", drawnDegrees.Select(Degrees)) + " degrees wide, bounds "
                        + string.Join(", ", views.Select(each => Degrees(each.View.TagMostDegrees ?? 0f))) + ".");
                    // The type's size as seen: one tag unit's angle at the tag's distance (its words wrap to more or fewer lines, so its height varies).
                    for (var task = 0; task < tags.Count; task++)
                        tagHeights.Add((bearing.Ring, task >= rowMost, project + " " + (task + 1),
                            tags[task]!.transform.lossyScale.x / Vector3.Distance(tags[task]!.transform.position, eyes) * Mathf.Rad2Deg * 1000f));
                    var seenBodies = views.Select(each => Seen(each.View.BodyCorners())).ToList();
                    var whole = seenTags.Concat(seenBodies).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                    extents.Add((bearing, whole));
                    var inner = Mathf.Min(Mathf.Abs(whole.xMin), Mathf.Abs(whole.xMax));
                    var outer = Mathf.Max(Mathf.Abs(whole.xMin), Mathf.Abs(whole.xMax));
                    if (Mathf.Sign(whole.xMin) != Mathf.Sign(whole.xMax) || inner < PlaceBearings.FrontClearDegrees - 0.05f)
                        failures.Add(project + ": the place reaches " + Degrees(inner) + " degrees from ahead, into the front's " + Degrees(PlaceBearings.FrontClearDegrees));
                    if (outer > PlaceBearings.ReachDegrees + 0.05f)
                        failures.Add(project + ": the place reaches " + Degrees(outer) + " degrees round, past the seated reach of " + Degrees(PlaceBearings.ReachDegrees));
                    var front = Mathf.Min(rowMost, views.Count);
                    for (var task = 1; task < front; task++)
                    {
                        // Seen from the eyes the person's left is the larger yaw's other side: order by yaw.
                        var a = seenTags[task - 1];
                        var b = seenTags[task];
                        var gap = Mathf.Max(a.xMin, b.xMin) - Mathf.Min(a.xMax, b.xMax);
                        if (gap < (float)PlacePitch.TagsApartDegrees - 0.05f)
                            failures.Add(project + ": tags " + task + " and " + (task + 1) + " of the front row stand " + Degrees(gap) + " degrees apart, under half a degree");
                    }
                    for (var back = front; back < views.Count; back++)
                    {
                        if (Vector3.Distance(views[back].View.transform.position, eyes) <= views.Take(front).Max(each => Vector3.Distance(each.View.transform.position, eyes)) - 0.05f)
                            failures.Add(project + ": back-row character " + (back - front + 1) + " does not stand behind the front row");
                        for (var task = 0; task < front; task++)
                            if (seenTags[back].Overlaps(seenBodies[task]))
                                failures.Add(project + ": back-row character " + (back - front + 1) + "'s tag stands over front-row character " + (task + 1) + "'s body as the eyes see it");
                    }
                    Debug.Log("Halcyonic: places: " + variant + " " + project + " at " + Degrees(bearing.YawDegrees) + " degrees, ring " + (bearing.Ring + 1) + ": " + views.Count
                        + " in rows of " + rowMost + ", " + pitch.ToString("0.000", CultureInfo.InvariantCulture) + " m apart, the back row " + rise.ToString("0.00", CultureInfo.InvariantCulture) + " m up, seen from " + Degrees(whole.xMin) + " to " + Degrees(whole.xMax) + " degrees.");
                }
                // The tags' size as seen: none more than 3 percent larger than ring one's front row (the lead's MUST: never past it), none more
                // than 7 percent smaller.
                var frontOne = tagHeights.Where(each => each.Ring == 0 && !each.Back).Select(each => each.Degrees).DefaultIfEmpty(0f).Average();
                foreach (var (ring, back, name, degrees) in tagHeights)
                    if (frontOne > 0f && (degrees > frontOne * 1.03f || degrees < frontOne * 0.93f))
                        failures.Add(name + "'s tag's type stands " + Degrees(degrees) + " as seen (ring " + (ring + 1) + (back ? ", back row" : ", front row") + ") against ring one's front row's " + Degrees(frontOne));
                Debug.Log("Halcyonic: places: " + variant + " tags' type as seen (millidegrees a tag unit): ring one's front row " + Degrees(frontOne) + "; ring two "
                    + Degrees(tagHeights.Where(each => each.Ring == 1).Select(each => each.Degrees).DefaultIfEmpty(0f).Average()) + "; back rows "
                    + Degrees(tagHeights.Where(each => each.Back).Select(each => each.Degrees).DefaultIfEmpty(0f).Average()) + ".");
                // Neighbouring places in a ring keep the lead's 4 degrees, and ring two stands further and lower than ring one.
                foreach (var a in extents)
                    foreach (var b in extents)
                        if (a.Bearing.Ring == b.Bearing.Ring && a.Bearing.YawDegrees < b.Bearing.YawDegrees && Mathf.Sign(a.Bearing.YawDegrees) == Mathf.Sign(b.Bearing.YawDegrees)
                            && b.Seen.xMin - a.Seen.xMax < PlaceBearings.GapDegrees - 0.05f)
                            failures.Add("the places at " + Degrees(a.Bearing.YawDegrees) + " and " + Degrees(b.Bearing.YawDegrees) + " degrees stand " + Degrees(b.Seen.xMin - a.Seen.xMax) + " apart, under " + Degrees(PlaceBearings.GapDegrees));
                // The frames: left, right, and from above.
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                camera.fieldOfView = 90f;
                foreach (var (name, yaw) in new[] { ("left", -65f), ("right", 65f) })
                {
                    camera.transform.SetPositionAndRotation(eyes, Quaternion.Euler(18f, yaw, 0f));
                    var image = WorkspaceRender.Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                camera.transform.SetPositionAndRotation(eyes + new Vector3(0f, 3.2f, 0.4f), Quaternion.Euler(90f, 0f, 0f));
                var above = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, "above.png"), above.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(above);
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                RoomDesign.Enabled = was;
                CharacterStage.SayLayout = false;
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            return failures;
        }
    }
}
