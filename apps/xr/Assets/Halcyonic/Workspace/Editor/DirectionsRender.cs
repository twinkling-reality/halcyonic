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
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Prototype renders of the headset redesign's directions, for the owner to choose between; not
    /// product code. Each direction is shown at the same five moments: arriving with work on the
    /// stage, a task waiting for you, understanding what a task did, creating a project, and a video
    /// beside the characters while something waits. Every surface is built from the interface's own
    /// parts (Surface, GlazeText, GlazeButton, StateBadgeView and the characters) at their tokens'
    /// sizes, and held to the same rules as the product's renders: text at least the caption's size,
    /// targets 60 dp (48 compact) and 12 mm apart, and every surface a degree clear of every
    /// character, label and other surface. The words are proposals and follow WORDS.md. Saved in
    /// apps/xr/Builds/DirectionRenders, which git ignores. Halcyonic > Render the Redesign Directions.
    /// </summary>
    public static partial class DirectionsRender
    {
        private const float EyeHeight = 1.2f;

        /// <summary>Touch distance (ADR 0023).</summary>
        private const float Touch = 0.46f;

        private const int WideWidth = 1600;
        private const int WideHeight = 1200;

        /// <summary>The wide view's vertical field, looking 18 degrees down as the product's renders do.</summary>
        private const float WideFieldOfView = 76f;

        private const float LookDown = 18f;

        /// <summary>A Quest 3's resolution near the middle of its lenses, for close-ups.</summary>
        private const float PixelsPerDegree = 25f;

        /// <summary>What a surface keeps from the labels above it, as the workspace keeps 1.2.</summary>
        private const float LabelClearance = 1.5f;

        private const float Pad = 1.25f;
        private const float Section = 0.75f;
        private const float LineGap = 0.4f;
        private const float PanelRadius = 1.5f;

        private static readonly Color Accent = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).Strong);

        [MenuItem("Halcyonic/Render the Redesign Directions")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Directions render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "DirectionRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var only = Environment.GetEnvironmentVariable("HALCYONIC_DIRECTIONS_ONLY");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (var (name, window, build) in Shots())
                {
                    if (!string.IsNullOrEmpty(only) && !name.StartsWith(only, StringComparison.Ordinal)) continue;
                    failures.AddRange(Take(folder, name, window, build));
                }
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: directions render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: directions render: every check passed; the renders are in " + folder);
            return failures;
        }

        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> Shots() => new (string, bool, Action<Shot>)[]
        {
            ("a1-arriving", false, shot => ShelfDock(shot, story: false)),
            ("a2-waiting-question", false, shot => ShelfQuestion(shot)),
            ("a2-waiting-approve", false, shot => ShelfApproval(shot)),
            ("a3-understanding", false, shot => ShelfUnderstanding(shot)),
            ("a4-creating", false, shot => ShelfCreating(shot)),
            ("a5-video-watching", true, shot => Folded(shot)),
            ("a5-video-opened", true, shot => ShelfBesideWindow(shot)),
        }.Concat(MenuShots()).Concat(StyledShots()).Concat(RefinedShots()).Concat(RolloutShots()).Concat(HeadsUpShots());

        // ---------------------------------------------------------------------------------------------
        // The work on the stage.

        private enum Stage
        {
            Waiting,
            Checked,
            Creating,
        }

        private static CharacterPresentation Character(int slot, string title, CharacterActivity activity, AttentionLevel attention = AttentionLevel.None,
            string[]? notes = null, string[]? details = null, int approvals = 0) =>
            new CharacterPresentation("lane-v-" + slot.ToString(CultureInfo.InvariantCulture), title, activity, CharacterPresenter.LabelOf(activity), attention,
                notes ?? Array.Empty<string>(), approvals, false, false, false, details);

        /// <summary>The task this prototype opens.</summary>
        private const string OpenedTitle = "Add rate limiting to the sign-in endpoint";

        private static CharacterPresentation Opened(Stage stage) => stage switch
        {
            Stage.Checked => Character(3, OpenedTitle, CharacterActivity.TurnFinished, AttentionLevel.Notice,
                new[] { "Checks: 1 failed, 23 passed" }, new[] { "1 failed, 23 passed" }),
            Stage.Creating => Character(3, OpenedTitle, CharacterActivity.Working),
            _ => Character(3, OpenedTitle, CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
                new[] { "It asks you: How long should a sign-in lockout last?" }),
        };

        private static IReadOnlyList<CharacterPresentation> Six(Stage stage) => new[]
        {
            Character(0, "Paginate the order history endpoint", CharacterActivity.Working),
            Character(1, "Send an order confirmation email", CharacterActivity.TurnFinished),
            Character(2, "Refresh the checkout copy", CharacterActivity.Verifying),
            Opened(stage),
            Character(4, "Upgrade the image pipeline", CharacterActivity.TurnFinished),
            Character(5, "Tidy the release notes", CharacterActivity.Working),
        };

        /// <summary>Beside a window: the lineup's four places, what waits in an upper one.</summary>
        private static IReadOnlyList<CharacterPresentation> Four() => new[]
        {
            Character(0, "Paginate the order history endpoint", CharacterActivity.Working),
            Opened(Stage.Waiting),
            Character(2, "Send an order confirmation email", CharacterActivity.TurnFinished),
            Character(4, "Refresh the checkout copy", CharacterActivity.Verifying),
        };

        // ---------------------------------------------------------------------------------------------
        // A shot: the stage, the surfaces a direction shows at one moment, the renders and the checks.

        private sealed class Shot
        {
            public Shot(string name, Transform root, Vector3 eyes, List<(CharacterView View, CharacterTarget Target)> characters, bool window)
            {
                Name = name;
                Root = root;
                Eyes = eyes;
                Characters = characters;
                Window = window;
            }

            public string Name { get; }

            public Transform Root { get; }

            public Vector3 Eyes { get; }

            public List<(CharacterView View, CharacterTarget Target)> Characters { get; }

            public bool Window { get; }

            public List<Board> Boards { get; } = new List<Board>();

            /// <summary>Surfaces that stand among the characters, as the hint does, checked apart from labels but not bodies.</summary>
            public List<GameObject> Floating { get; } = new List<GameObject>();

            /// <summary>The surfaces a close-up frames; all of them when empty.</summary>
            public List<Board> Framed { get; } = new List<Board>();

            public float LabelsBottom => Characters.Min(character => GlazeChecks.Of("label", Eyes, character.View.Label.gameObject).Bottom);

            public int SlotOf(string title) => Characters.FindIndex(character => character.View.Presentation?.Title == title);

            public float YawOf(int slot)
            {
                var toward = Characters[slot].View.Body.position - Eyes;
                return Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            }

            public Board Board(string name, float distance = Touch)
            {
                var board = DirectionsRender.NewBoard(Root, name, distance);
                Boards.Add(board);
                return board;
            }
        }

        private static IEnumerable<string> Take(string folder, string name, bool window, Action<Shot> build)
        {
            var failures = new List<string>();
            var root = new GameObject("Directions render " + name);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var characters = window
                    ? WorkspaceRender.Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null, (_, slot) => Four()[slot], besideWindow: true)
                    : WorkspaceRender.Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null,
                        (_, slot) => Six(name.Contains("understanding") ? Stage.Checked : name.Contains("creating") ? Stage.Creating : Stage.Waiting)[slot]);
                if (window) VideoWindow(root.transform, eyes);
                var shot = new Shot(name, root.transform, eyes, characters, window);
                lightLines.Clear();
                glowBottoms.Clear();
                build(shot);
                WorkspaceRender.ForceMeshes(root);
                failures.AddRange(Checks(shot));
                failures = KeptToFail(name, failures);
                if (name.StartsWith("h", StringComparison.Ordinal))
                {
                    // Heads-up: the view with the head level and looking ahead, then each glance item close.
                    Save(folder, name, FieldView(eyes, root.transform));
                    Save(folder, name + "-closeup-work", GlanceCloseUp(shot, glances[0]));
                    Save(folder, name + "-closeup-waits", GlanceCloseUp(shot, glances[1]));
                    return failures;
                }
                if (name.StartsWith("r", StringComparison.Ordinal))
                {
                    // A plane: level and upright, as architecture is photographed, then as the eyes see it.
                    Save(folder, name, WideUpright(eyes, root.transform));
                    if (facing == Facing.Upright) Save(folder, name + "-closeup", CloseUpUpright(shot));
                    Save(folder, name + "-eye", EyeView(shot, wide: true));
                    Save(folder, name + "-eye-closeup", EyeView(shot, wide: false));
                    return failures;
                }
                Save(folder, name, Wide(eyes, root.transform));
                var framed = shot.Framed.Count > 0 ? shot.Framed : shot.Boards;
                // Each surface faces the eyes, so two side by side look rolled in one flat close-up: one each.
                if (framed.Count == 1) Save(folder, name + "-closeup", CloseUp(eyes, root.transform, framed));
                else
                {
                    foreach (var board in framed)
                    {
                        var slug = new string(board.Name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
                        Save(folder, name + "-closeup-" + slug, CloseUp(eyes, root.transform, new[] { board }));
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add(name + ": " + error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            return failures;
        }

        /// <summary>
        /// The rules the product's renders hold every surface to: text large enough, targets large
        /// enough and 12 mm apart, every surface a degree from each other and from every label and
        /// body, and every surface's corners inside a Quest 3S's field with the head level.
        /// </summary>
        private static IEnumerable<string> Checks(Shot shot)
        {
            var failures = new List<string>();
            var eyes = shot.Eyes;
            var extents = new List<GlazeChecks.Extent>();
            foreach (var character in shot.Characters)
            {
                // A character the shot hides, as heads-up does while walking, stands nowhere.
                if (!character.View.gameObject.activeInHierarchy) continue;
                if (character.View.Label.gameObject.activeInHierarchy) extents.Add(GlazeChecks.Of(character.View.WorkstreamId + "'s label", eyes, character.View.Label.gameObject));
                extents.Add(WorkspaceRender.BodyExtent(character.View, eyes));
            }
            var surfaces = shot.Boards.Select(board => GlazeChecks.Of(board.Name, eyes, board.Plates.Select(plate => (Renderer)plate.Renderer))).ToList();
            foreach (var floating in shot.Floating) surfaces.Add(GlazeChecks.Of(floating.name, eyes, floating));
            // Surfaces keep a degree from each other, by their boxes as seen from the eyes; the parts of one
            // composition on one upright plane are checked on that plane instead (PlaneChecks).
            if (!shot.Name.StartsWith("r", StringComparison.Ordinal)) failures.AddRange(GlazeChecks.Apart(surfaces).Select(failure => shot.Name + ": " + failure));
            // And from every label and body, by their outlines: a surface tipped to face the eyes from
            // below them is a trapezoid there, narrow where the labels are and wide only at its low corners.
            foreach (var board in shot.Boards)
            {
                var outline = Outline(board, eyes);
                foreach (var extent in extents)
                {
                    var apart = OutlineApart(outline, extent);
                    if (apart < GlazeChecks.GapDegrees - 1e-3f) failures.Add(shot.Name + ": " + board.Name + "'s outline and " + extent + " are " + GlazeChecks.Degrees(apart) + " degrees apart, under 1.0.");
                }
            }
            foreach (var floating in shot.Floating)
            {
                var box = GlazeChecks.Of(floating.name, eyes, floating);
                foreach (var failure in GlazeChecks.Apart(new List<GlazeChecks.Extent>(extents.Where(extent => extent.Name.EndsWith("label", StringComparison.Ordinal))) { box }))
                {
                    if (failure.Contains(box.Name + " (")) failures.Add(shot.Name + ": " + failure);
                }
            }
            foreach (var board in shot.Boards)
            {
                failures.AddRange(GlazeChecks.TextLargeEnough(board.Root.gameObject, eyes, shot.Name + " " + board.Name));
                failures.AddRange(GlazeChecks.TargetsLargeEnough(board.Buttons, eyes, shot.Name + " " + board.Name));
                failures.AddRange(TargetsApart(shot.Name, board));
                var extent = GlazeChecks.Of(board.Name, eyes, board.Plates.Select(plate => (Renderer)plate.Renderer));
                Debug.Log("Halcyonic: directions render " + shot.Name + ": " + extent + ", " + board.Buttons.Count(button => button.gameObject.activeInHierarchy)
                    + " targets, " + Words(board.Root.gameObject) + " words, at most " + DrawCalls(board.Root.gameObject) + " draw calls before batching.");
                // A Quest 3S's narrower field, turned to the surface with the head tipped 18 degrees down, as the
                // earlier rounds assumed; the refined shots use the product's FieldChecks (PlaneChecks).
                var middleYaw = (extent.Left + extent.Right) / 2f;
                if (!shot.Name.StartsWith("r", StringComparison.Ordinal) && !shot.Name.StartsWith("h", StringComparison.Ordinal)
                    && (extent.Right - middleYaw > 48f - 1.5f || extent.Bottom < -(45f - 1.5f) - 18f))
                {
                    failures.Add(shot.Name + ": " + extent + " reaches past a Quest 3S's field.");
                }
            }
            failures.AddRange(PlaneChecks(shot));
            failures.AddRange(HeadsUpChecks(shot));
            Debug.Log("Halcyonic: directions render " + shot.Name + ": the whole scene, characters included, at most " + DrawCalls(shot.Root.gameObject)
                + " draw calls before batching (budget 220).");
            return failures;
        }

        /// <summary>A surface's plates' outlines as seen from the eyes, sampled along every edge, in yaw and elevation.</summary>
        private static List<Vector2> Outline(Board board, Vector3 eyes)
        {
            var points = new List<Vector2>();
            foreach (var plate in board.Plates)
            {
                if (!plate.gameObject.activeInHierarchy || plate.Size.x < U(4f)) continue;
                for (var step = 0; step <= 24; step++)
                {
                    var t = step / 24f - 0.5f;
                    foreach (var local in new[] { new Vector3(t, -0.5f, 0f), new Vector3(t, 0.5f, 0f), new Vector3(-0.5f, t, 0f), new Vector3(0.5f, t, 0f) })
                    {
                        var toward = plate.transform.TransformPoint(local) - eyes;
                        var level = Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 1e-4f);
                        points.Add(new Vector2(Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, level) * Mathf.Rad2Deg));
                    }
                }
            }
            return points;
        }

        /// <summary>How far an outline keeps from a box: the least, over the outline's points, of how far each stands outside it; negative when the box is inside.</summary>
        private static float OutlineApart(List<Vector2> outline, GlazeChecks.Extent box)
        {
            if (outline.Count == 0) return float.MaxValue;
            var least = float.MaxValue;
            foreach (var point in outline)
            {
                var outside = Mathf.Max(Mathf.Max(box.Left - point.x, point.x - box.Right), Mathf.Max(box.Bottom - point.y, point.y - box.Top));
                least = Mathf.Min(least, outside);
            }
            // A box wholly inside the outline: its middle lies between the outline's points at its height.
            var middle = new Vector2((box.Left + box.Right) / 2f, (box.Bottom + box.Top) / 2f);
            var row = outline.Where(point => Mathf.Abs(point.y - middle.y) < 0.6f).ToList();
            if (row.Count > 1 && middle.x > row.Min(point => point.x) && middle.x < row.Max(point => point.x)) return -1f;
            return least;
        }

        /// <summary>Every two targets on a surface stand 12 mm apart or more, edge to edge.</summary>
        private static IEnumerable<string> TargetsApart(string shot, Board board)
        {
            var shown = board.Buttons.Where(button => button.gameObject.activeInHierarchy).ToList();
            for (var a = 0; a < shown.Count; a++)
            {
                for (var b = a + 1; b < shown.Count; b++)
                {
                    var gap = Gap(shown[a], shown[b]) * board.Distance;
                    if (gap < Glaze.TargetGapMeters - 0.0005f)
                    {
                        yield return shot + ": " + board.Name + "'s " + shown[a].name + " and " + shown[b].name + " are "
                            + (gap * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart, under 12.";
                    }
                }
            }
        }

        /// <summary>The gap between two buttons' rectangles in their board's units: the larger of the horizontal and vertical gaps.</summary>
        private static float Gap(GlazeButton a, GlazeButton b)
        {
            Rect RectOf(GlazeButton button)
            {
                var p = button.transform.localPosition;
                var parent = button.transform.parent;
                var offset = parent == null ? Vector3.zero : parent.localPosition;
                return new Rect(p.x + offset.x - button.Size.x / 2f, p.y + offset.y - button.Size.y / 2f, button.Size.x, button.Size.y);
            }
            var ra = RectOf(a);
            var rb = RectOf(b);
            var horizontal = Mathf.Max(rb.xMin - ra.xMax, ra.xMin - rb.xMax);
            var vertical = Mathf.Max(rb.yMin - ra.yMax, ra.yMin - rb.yMax);
            return Mathf.Max(horizontal, vertical);
        }

        /// <summary>An upper bound on a surface's draw calls before batching, as MeasureRender counts: every material of every renderer that draws.</summary>
        private static int DrawCalls(GameObject root) => root.GetComponentsInChildren<Renderer>(false)
            .Where(renderer => renderer.enabled)
            .Sum(renderer => renderer.sharedMaterials.Length);

        /// <summary>How many words the person reads on a surface.</summary>
        private static int Words(GameObject root) => root.GetComponentsInChildren<TMP_Text>(false)
            .Where(label => !GlazeIcons.IsIcon(label))
            .Sum(label => label.GetParsedText().Split(new[] { ' ', '\n', '·' }, StringSplitOptions.RemoveEmptyEntries).Count(word => word.Any(char.IsLetterOrDigit)));

        // ---------------------------------------------------------------------------------------------
        // Rendering.

        private static Texture2D Capture(Vector3 eyes, Transform parent, Quaternion rotation, float verticalField, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, rotation);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = verticalField;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            camera.aspect = (float)width / height;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(go);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            return image;
        }

        /// <summary>The person's view, looking 18 degrees down, a little more than a Quest 3 shows.</summary>
        private static Texture2D Wide(Vector3 eyes, Transform parent) =>
            Capture(eyes, parent, Quaternion.Euler(LookDown, 0f, 0f), WideFieldOfView, WideWidth, WideHeight);

        /// <summary>The surfaces at a Quest 3's pixels per degree, framed in the camera's own view with two degrees round them.</summary>
        private static Texture2D CloseUp(Vector3 eyes, Transform parent, IReadOnlyList<Board> boards)
        {
            var points = new List<Vector3>();
            foreach (var plate in boards.SelectMany(board => board.Plates))
            {
                if (!plate.gameObject.activeInHierarchy) continue;
                foreach (var x in new[] { -0.5f, 0.5f })
                {
                    foreach (var y in new[] { -0.5f, 0.5f }) points.Add(plate.transform.TransformPoint(new Vector3(x, y, 0f)));
                }
            }
            var forward = points.Aggregate(Vector3.zero, (sum, point) => sum + (point - eyes).normalized).normalized;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            for (var pass = 0; pass < 4; pass++)
            {
                minX = minY = float.MaxValue;
                maxX = maxY = float.MinValue;
                var inverse = Quaternion.Inverse(rotation);
                foreach (var point in points)
                {
                    var local = inverse * (point - eyes);
                    minX = Mathf.Min(minX, local.x / local.z);
                    maxX = Mathf.Max(maxX, local.x / local.z);
                    minY = Mathf.Min(minY, local.y / local.z);
                    maxY = Mathf.Max(maxY, local.y / local.z);
                }
                var middle = rotation * new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 1f);
                rotation = Quaternion.LookRotation(middle, Vector3.up);
            }
            var margin = 2f * Mathf.Deg2Rad;
            var halfX = Mathf.Tan(Mathf.Atan((maxX - minX) / 2f) + margin);
            var halfY = Mathf.Tan(Mathf.Atan((maxY - minY) / 2f) + margin);
            var vertical = 2f * Mathf.Atan(halfY) * Mathf.Rad2Deg;
            var height = Mathf.Min(1500, Mathf.RoundToInt(vertical * PixelsPerDegree));
            var width = Mathf.RoundToInt(height * halfX / halfY);
            return Capture(eyes, parent, rotation, vertical, width, height);
        }

        private static void Save(string folder, string name, Texture2D image)
        {
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        /// <summary>A browser video window of a typical size, 1.4 by 0.79 m at 1.6 m, centered at eye level, as AmbientRender's.</summary>
        private static void VideoWindow(Transform parent, Vector3 eyes)
        {
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "Video window";
            UnityEngine.Object.DestroyImmediate(window.GetComponent<Collider>());
            window.transform.SetParent(parent, false);
            window.transform.SetPositionAndRotation(eyes + Vector3.forward * 1.6f, Quaternion.identity);
            window.transform.localScale = new Vector3(1.4f, 0.79f, 1f);
            var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = VideoFrame() };
            var renderer = window.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 100;
        }

        /// <summary>A stand-in frame of a video: dusk over hills, with a player's progress bar.</summary>
        private static Texture2D VideoFrame()
        {
            const int width = 640;
            const int height = 360;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top = new Color(0.10f, 0.16f, 0.32f);
            var low = new Color(0.93f, 0.55f, 0.32f);
            var far = new Color(0.20f, 0.17f, 0.30f);
            var near = new Color(0.08f, 0.07f, 0.12f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var t = y / (float)height;
                    var sky = Color.Lerp(low, top, Mathf.SmoothStep(0.15f, 1f, t));
                    var sun = new Vector2(x - width * 0.62f, y - height * 0.42f).magnitude;
                    if (sun < 26f) sky = Color.Lerp(new Color(1f, 0.9f, 0.7f), sky, sun / 26f * 0.4f);
                    var ridge = height * (0.34f + 0.06f * Mathf.Sin(x * 0.011f) + 0.03f * Mathf.Sin(x * 0.031f + 1.3f));
                    var hill = height * (0.22f + 0.05f * Mathf.Sin(x * 0.007f + 2.1f) + 0.02f * Mathf.Sin(x * 0.043f));
                    var colour = y < hill ? near : y < ridge ? far : sky;
                    if (y >= 10 && y < 14 && x >= 24 && x < width - 24) colour = x < 24 + (width - 48) * 0.35f ? new Color(0.95f, 0.95f, 0.97f) : new Color(0.45f, 0.45f, 0.5f);
                    image.SetPixel(x, y, colour);
                }
            }
            image.Apply();
            return image;
        }

        // ---------------------------------------------------------------------------------------------
        // A surface at touch distance, in units of its distance from the eyes, laid out from its top.

        private sealed class Board
        {
            public Board(Transform root, Transform content, float distance, string name)
            {
                Root = root;
                Content = content;
                Distance = distance;
                Name = name;
            }

            public Transform Root { get; }

            /// <summary>Everything on the surface, its top edge at y = 0; moved so the surface is centred on <see cref="Root"/>.</summary>
            public Transform Content { get; }

            public float Distance { get; }

            public string Name { get; }

            public List<GlazeButton> Buttons { get; } = new List<GlazeButton>();

            public List<Surface> Plates { get; } = new List<Surface>();

            /// <summary>12 mm in the surface's units.</summary>
            public float TargetGap => Glaze.TargetGapMeters / Distance;

            public float Height { get; set; }

            public float Width { get; set; }
        }

        private static Board NewBoard(Transform parent, string name, float distance)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * distance;
            var content = new GameObject("Content").transform;
            content.SetParent(root, false);
            return new Board(root, content, distance, name);
        }

        private static float U(float degrees) => GlazeTokens.Units(degrees);

        /// <summary>The extent of a surface's plates as seen from the eyes.</summary>
        private static GlazeChecks.Extent ExtentOf(Shot shot, Board board) =>
            GlazeChecks.Of(board.Name, shot.Eyes, board.Plates.Select(plate => (Renderer)plate.Renderer));

        /// <summary>
        /// Stands a surface under the labels at <paramref name="yaw"/>, as high as it may: its top
        /// corners, which look nearer eye level than its top edge's middle, a degree and a little
        /// under the lowest label.
        /// </summary>
        private static void PlaceUnderLabels(Shot shot, Board board, float yaw, float? fixedTop = null)
        {
            if (fixedTop is float set)
            {
                PlaceByTop(board, shot.Eyes, yaw, set);
                return;
            }
            var limit = shot.LabelsBottom - 1.15f;
            var top = limit - 0.4f;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                PlaceByTop(board, shot.Eyes, yaw, top);
                var extent = ExtentOf(shot, board);
                if (extent.Top <= limit + 1e-3f) break;
                top -= extent.Top - limit + 0.02f;
            }
        }

        /// <summary>A card at <paramref name="cardYaw"/> and an answer beside it on <paramref name="side"/>, their tops level, 1.6 degrees apart.</summary>
        private static void PlaceBeside(Shot shot, Board card, Board sheet, float cardYaw, int side, float? fixedTop = null, bool centered = false)
        {
            for (var pass = 0; pass < (centered ? 3 : 1); pass++)
            {
                PlaceUnderLabels(shot, card, cardYaw, fixedTop);
                var sheetYaw = cardYaw + side * 34f;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    PlaceUnderLabels(shot, sheet, sheetYaw, fixedTop);
                    var c = ExtentOf(shot, card);
                    var a = ExtentOf(shot, sheet);
                    // Apart measures the larger of the two gaps; beside each other, the horizontal one, at the lower corners.
                    var gap = side < 0 ? c.Left - a.Right : a.Left - c.Right;
                    if (Mathf.Abs(gap - 1.6f) < 0.05f) break;
                    sheetYaw += side * (1.6f - gap) * 0.9f;
                }
                if (!centered) break;
                // The pair centred on straight ahead, by its top corners.
                var left = Mathf.Min(TopCorner(shot, card, -1f), TopCorner(shot, sheet, -1f));
                var right = Mathf.Max(TopCorner(shot, card, 1f), TopCorner(shot, sheet, 1f));
                cardYaw -= (left + right) / 2f;
            }
        }

        /// <summary>The yaw of a surface's top left (-1) or top right (1) corner as seen from the eyes.</summary>
        private static float TopCorner(Shot shot, Board board, float side)
        {
            var corner = board.Root.TransformPoint(new Vector3(side * board.Width / 2f, board.Height / 2f, 0f)) - shot.Eyes;
            return Mathf.Atan2(corner.x, corner.z) * Mathf.Rad2Deg;
        }

        /// <summary>Stands a surface laid out <paramref name="height"/> tall with its top edge at <paramref name="topPitch"/>, facing the eyes.</summary>
        private static void PlaceByTop(Board board, Vector3 eyes, float yaw, float topPitch)
        {
            board.Content.localPosition = new Vector3(0f, board.Height / 2f, 0f);
            var center = topPitch - Mathf.Atan(board.Height / 2f) * Mathf.Rad2Deg;
            var forward = Quaternion.Euler(-center, yaw, 0f) * Vector3.forward;
            board.Root.SetPositionAndRotation(eyes + forward * board.Distance, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>Stands a surface with its centre at <paramref name="pitch"/>.</summary>
        private static void PlaceByCenter(Board board, Vector3 eyes, float yaw, float pitch)
        {
            board.Content.localPosition = new Vector3(0f, board.Height / 2f, 0f);
            var forward = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            board.Root.SetPositionAndRotation(eyes + forward * board.Distance, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>The yaw a surface <paramref name="halfWidthDegrees"/> wide whose left edge is at <paramref name="leftYaw"/> centres on.</summary>
        private static float CenterFromLeft(float leftYaw, float halfWidthDegrees) => leftYaw + halfWidthDegrees;

        private static Surface Plate(Board board, string name, float centerX, float centerY, float width, float height, float radius, Color fill,
            Color edge = default, float edgeWidth = 0f, int order = 50, float depth = 0f)
        {
            var plate = Surface.Create(board.Content, name, order);
            plate.transform.localPosition = new Vector3(centerX, centerY, depth);
            plate.Draw(new Vector2(width, height), radius, fill, edge, edgeWidth);
            board.Plates.Add(plate);
            return plate;
        }

        /// <summary>A label whose box's top-left corner is at (<paramref name="left"/>, <paramref name="top"/>); says how tall its lines are.</summary>
        private static (TextMeshPro Label, float Height) Text(Board board, string name, string text, GlazeType type, Color color, float left, float top, float width,
            int maxLines = 1, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, bool lean = false, int order = 56, bool? strong = null)
        {
            var label = GlazeText.Create(board.Content, name, type, color, alignment, order, strong);
            label.rectTransform.pivot = alignment == TextAlignmentOptions.TopRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            label.transform.localPosition = new Vector3(left, top, -U(0.05f));
            if (lean) label.OnPreRenderText += GlazeText.Lean;
            GlazeText.SetLiteral(label, text);
            var (lines, _) = GlazeText.Lay(label, width, maxLines);
            if (lean) label.ForceMeshUpdate(true);
            return (label, Mathf.Max(1, lines) * GlazeText.LineHeight(label));
        }

        private static float WidthOf(Board board, string text, GlazeType type)
        {
            var probe = GlazeText.Create(board.Content, "Measure", type, Color.white, TextAlignmentOptions.TopLeft, 0);
            var width = probe.GetPreferredValues(LabelText.ForTextMeshPro(text)).x;
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            return width;
        }

        private static GlazeButton Button(Board board, string name, ButtonRole role, string text, float centerX, float centerY, float? width = null,
            GlazeIcon? icon = null, bool compact = false, string? detail = null, GlazeTone? tone = null)
        {
            var button = GlazeButton.Create(board.Content, name, role, compact, order: 60);
            var w = width ?? button.Measure(text, detail, icon);
            button.Show(text, new Vector2(centerX, centerY), w, detail, tone, icon);
            board.Buttons.Add(button);
            return button;
        }

        /// <summary>A button measured as it would show, so a row can be laid out before it is.</summary>
        private static float Measure(Board board, ButtonRole role, string text, GlazeIcon? icon = null, bool compact = false, string? detail = null)
        {
            var probe = GlazeButton.Create(board.Content, "Measure", role, compact);
            var width = probe.Measure(text, detail, icon);
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            return width;
        }

        private static GlazeButton Row(Board board, string name, ButtonRole role, PanelRow words, float left, float top, float width, float? minimum = null)
        {
            var button = GlazeButton.Create(board.Content, name, role, false, order: 60);
            var height = button.LayRow(words, width, minimum);
            button.ShowRow(words, new Vector2(left + width / 2f, top - height / 2f), new Vector2(width, height), minimum);
            board.Buttons.Add(button);
            return button;
        }

        private static StateBadgeView Badge(Board board, CharacterPresentation who, float left, float centerY)
        {
            var badge = StateBadgeView.Create(board.Content, "Badge", 58);
            badge.Show(StateLanguage.BadgeOf(who));
            badge.transform.localPosition = new Vector3(left + badge.Width / 2f, centerY, -U(0.05f));
            return badge;
        }

        /// <summary>
        /// The task's own character, small, at the card's left, as the notch shows its figure beside
        /// its card: the same body, its state's glow and face, standing a little in front of the card.
        /// </summary>
        private static void Portrait(Board board, CharacterPresentation who, string identity, float centerX, float centerY, float diameterDegrees)
        {
            var view = CharacterView.Create(board.Content, identity);
            view.Show(who);
            view.Label.gameObject.SetActive(false);
            var scale = Mathf.Tan(diameterDegrees / 2f * Mathf.Deg2Rad) / CharacterView.BodyRadius;
            view.transform.localScale = Vector3.one * scale;
            view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            view.transform.localPosition = Vector3.zero;
            // Its body's centre where asked, in front of the card by its own radius.
            var body = board.Content.InverseTransformPoint(view.Body.position);
            view.transform.localPosition = new Vector3(centerX - body.x, centerY - body.y, -U(diameterDegrees / 2f + 0.3f));
        }

        /// <summary>The companion's mark: a soft ring of light, no eyes and no body, so it never reads as a task (ADR 0025).</summary>
        private static void CompanionMark(Board board, float centerX, float centerY, float diameterDegrees)
        {
            var tone = Glaze.Tone(GlazeTone.Simulated);
            var size = U(diameterDegrees);
            Plate(board, "Companion glow", centerX, centerY, size * 1.25f, size * 1.25f, size * 0.625f, GlazeTokens.ColorOf(tone.Strong, 0.18f), order: 54, depth: -U(0.02f));
            Plate(board, "Companion", centerX, centerY, size, size, size / 2f, GlazeTokens.ColorOf(tone.Container), GlazeTokens.ColorOf(tone.Strong), U(0.35f), 55, -U(0.04f));
            Plate(board, "Companion core", centerX, centerY, size * 0.32f, size * 0.32f, size * 0.16f, GlazeTokens.ColorOf(tone.Strong), order: 56, depth: -U(0.06f));
        }

        /// <summary>A thin line from a surface's top to the character it belongs to, as the workspace links its panel today.</summary>
        private static void Tether(Shot shot, Board board, int slot)
        {
            var go = new GameObject("Tether");
            go.transform.SetParent(shot.Root, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Sprites/Default"));
            var from = board.Root.TransformPoint(new Vector3(0f, board.Height / 2f, 0f));
            var label = shot.Characters[slot].View.Label;
            var bounds = GlazeChecks.Of("label", shot.Eyes, label.gameObject);
            var toward = Quaternion.Euler(-(bounds.Bottom - 0.4f), shot.YawOf(slot), 0f) * Vector3.forward;
            var to = shot.Eyes + toward * CharacterStage.DefaultDistance;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.0012f;
            line.endWidth = 0.006f;
            var colour = Accent;
            line.startColor = new Color(colour.r, colour.g, colour.b, 0.7f);
            line.endColor = new Color(colour.r, colour.g, colour.b, 0.15f);
            line.sortingOrder = 40;
        }

        /// <summary>A small caption chip, such as an evidence class, or at the content's size where a caption would read too small; says how wide it is.</summary>
        private static float Chip(Board board, string text, GlazeTone tone, float left, float centerY, GlazeType type = GlazeType.Caption)
        {
            var colours = Glaze.Tone(tone);
            var body = type == GlazeType.Body;
            var (label, height) = Text(board, "Chip " + text, text, type, GlazeTokens.ColorOf(colours.Foreground), left + U(0.45f), centerY + U(body ? 0.72f : 0.6f), U(12f));
            var width = Mathf.Min(U(12f), label.GetPreferredValues(label.text).x) + U(0.9f);
            Plate(board, "Chip plate " + text, left + width / 2f, centerY, width, U(body ? 1.75f : 1.45f), U(body ? 0.875f : 0.725f), GlazeTokens.ColorOf(colours.Container),
                GlazeTokens.ColorOf(colours.Foreground, 0.5f), U(0.08f), 55, -U(0.02f));
            return width;
        }

        // ---------------------------------------------------------------------------------------------
        // A card's header: who it is about, what it is, its state, and the few controls a card keeps.

        private static float Header(Board board, float left, float right, float top, string title, CharacterPresentation? who, bool companion,
            params (string Text, GlazeIcon? Icon)[] controls) => Header(board, left, right, top, title, who, null, companion, controls);

        private static float Header(Board board, float left, float right, float top, string title, CharacterPresentation? who, string? identity, bool companion,
            params (string Text, GlazeIcon? Icon)[] controls)
        {
            const float portrait = 5f;
            var x = left;
            if (who != null) Portrait(board, who, identity ?? who.WorkstreamId, left + U(portrait / 2f), top - U(portrait / 2f), portrait);
            if (who != null || companion)
            {
                if (companion) CompanionMark(board, left + U(portrait / 2f), top - U(portrait / 2f), portrait * 0.82f);
                x = left + U(portrait) + U(0.9f);
            }
            // The card's own controls, compact, at its top right.
            var end = right;
            var heights = U(GlazeButton.CompactHeightDegrees);
            foreach (var (text, icon) in controls.Reverse())
            {
                var width = Measure(board, ButtonRole.Secondary, text, icon, compact: true);
                Button(board, text, ButtonRole.Secondary, text, end - width / 2f, top - heights / 2f, width, icon, compact: true);
                end -= width + board.TargetGap;
            }
            var titleWidth = end - x - U(0.5f);
            var (_, titleHeight) = Text(board, "Title", title, who == null ? GlazeType.Display : GlazeType.Title, GlazeTokens.Text, x, top - U(0.2f), titleWidth);
            if (who != null) Badge(board, who, x, top - U(0.2f) - titleHeight - U(0.35f) - StateBadgeView.Height / 2f);
            return top - Mathf.Max(U(portrait), who != null ? titleHeight + U(0.55f) + StateBadgeView.Height : titleHeight + U(0.4f), heights);
        }

        // ---------------------------------------------------------------------------------------------
        // Direction A: a card and its shelf. The card says what the task needs now; the shelf under it
        // holds what you can ask and tell it; an answer opens beside the card; the dock starts new work.

        /// <summary>
        /// The dock in the rail's place: one field to start work, Hold to talk beside it, and the shelf
        /// it rests on with what waits for you and three quiet places. In the story direction the
        /// shelf tells the latest moments of the work instead.
        /// </summary>
        private static void ShelfDock(Shot shot, bool story)
        {
            var board = shot.Board("Dock");
            var width = U(46f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = -U(Pad);
            var tall = U(GlazeButton.HeightDegrees);
            var talk = Measure(board, ButtonRole.Secondary, "Hold to talk", GlazeIcon.HoldToTalk);
            var fieldWidth = right - left - talk - board.TargetGap;
            Field(board, "What would you like to make?", left, y, fieldWidth);
            Button(board, "Hold to talk", ButtonRole.Secondary, "Hold to talk", right - talk / 2f, y - tall / 2f, talk, GlazeIcon.HoldToTalk);
            y -= tall + board.TargetGap + U(0.3f);
            var shelfTop = y + U(0.6f);
            if (story)
            {
                y -= Row(board, "Waiting", ButtonRole.Choice, new PanelRow { Title = "“" + OpenedTitle + "” is waiting for you", DetailTone = GlazeTone.Attention, Detail = "It asks you a question", End = "Open" }, left, y, right - left).Size.y + board.TargetGap;
                y -= Row(board, "Finished", ButtonRole.Choice, new PanelRow { Title = "“Send an order confirmation email” finished this round", Detail = "3 minutes ago", End = "Look" }, left, y, right - left).Size.y + board.TargetGap;
                y -= Row(board, "Checking", ButtonRole.Choice, new PanelRow { Title = "“Refresh the checkout copy” is checking its work", End = "Look" }, left, y, right - left).Size.y + board.TargetGap;
            }
            else
            {
                y -= Row(board, "Waiting", ButtonRole.Choice, new PanelRow { Title = "1 task is waiting for you", Detail = OpenedTitle, DetailTone = GlazeTone.Attention, End = "Open" }, left, y, right - left).Size.y + board.TargetGap;
            }
            var compact = U(GlazeButton.CompactHeightDegrees);
            var x = left;
            foreach (var (text, icon) in new (string, GlazeIcon)[] { ("Projects", GlazeIcon.ConnectProjects), ("Usage left", GlazeIcon.UsageLeft), ("Settings", GlazeIcon.Settings) })
            {
                var w = Measure(board, ButtonRole.Secondary, text, icon, compact: true);
                Button(board, text, ButtonRole.Secondary, text, x + w / 2f, y - compact / 2f, w, icon, compact: true);
                x += w + board.TargetGap;
            }
            y -= compact + U(Pad);
            board.Height = -y;
            board.Width = width;
            // The field rests in the top of a darker tray, one outline round the pair, as the request box sits on its shelf.
            Plate(board, "Dock", 0f, -board.Height / 2f, width, board.Height, U(PanelRadius), GlazeTokens.ColorOf(Glaze.Panel), GlazeTokens.ColorOf(Glaze.Outline, 0.55f), U(0.1f));
            Plate(board, "Shelf", 0f, (shelfTop + y + U(Pad) - U(0.6f)) / 2f, width - U(0.5f), shelfTop - (y + U(Pad) - U(0.6f)), U(PanelRadius - 0.25f), GlazeTokens.ColorOf(Glaze.Well), order: 51, depth: -U(0.01f));
            PlaceByTop(board, shot.Eyes, 0f, story ? -24f : -27.5f);
            // The first time: how to open a task, above the one that waits.
            Hint(shot, shot.SlotOf(OpenedTitle));
        }

        /// <summary>A field: a sunk well with its placeholder, pressed to type (the system keyboard), 60 dp tall.</summary>
        private static void Field(Board board, string placeholder, float left, float top, float width)
        {
            var tall = U(GlazeButton.HeightDegrees);
            var field = Button(board, "Field", ButtonRole.Choice, "", left + width / 2f, top - tall / 2f, width);
            field.Label.gameObject.SetActive(false);
            Plate(board, "Field well", left + width / 2f, top - tall / 2f, width, tall, U(Glaze.RowRadiusDegrees), GlazeTokens.ColorOf(Glaze.Well),
                GlazeTokens.ColorOf(Glaze.Outline, 0.8f), U(0.1f), 52);
            Text(board, "Placeholder", placeholder, GlazeType.Body, GlazeTokens.TextSecondary, left + U(1.1f), top - (tall - U(Glaze.BodyDegrees) * 1.15f) / 2f, width - U(2.2f), order: 62);
        }

        /// <summary>The one-time hint over the first task that waits: "Look, then pinch".</summary>
        private static void Hint(Shot shot, int slot)
        {
            if (slot < 0) return;
            var view = shot.Characters[slot].View;
            var board = NewBoard(shot.Root, "Hint", CharacterStage.DefaultDistance);
            var text = WorkspaceText.OpenHint;
            var width = WidthOf(board, text, GlazeType.Body) + U(2f);
            var height = U(2.4f);
            Plate(board, "Hint", 0f, -height / 2f, width, height, height / 2f, GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), Accent, U(0.1f));
            Text(board, "Hint words", text, GlazeType.Body, GlazeTokens.Text, -width / 2f + U(1f), -(height - U(Glaze.BodyDegrees) * 1.15f) / 2f, width - U(2f));
            board.Height = height;
            var top = GlazeChecks.Of("body", shot.Eyes, view.Body.gameObject).Top;
            PlaceByTop(board, shot.Eyes, shot.YawOf(slot), top + 1.2f + Mathf.Atan(height) * Mathf.Rad2Deg);
            shot.Floating.Add(board.Root.gameObject);
        }

        /// <summary>Where a task's card opens: under its character, clear of every label, kept far enough in that an answer fits beside it.</summary>
        private static float CardYaw(Shot shot, int slot, float halfWidth, float sheetWidth)
        {
            var yaw = shot.YawOf(slot);
            var room = 34f - sheetWidth - halfWidth;
            return Mathf.Clamp(yaw, -room, room);
        }

        private static float Inner(float width) => width - 2f * U(Pad);

        /// <summary>A waiting task opened: its question, every answer at once, and one way to send.</summary>
        private static void ShelfQuestion(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var board = shot.Board("Card");
            var width = U(36f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = Header(board, left, right, -U(Pad), OpenedTitle, who, shot.Characters[slot].View.WorkstreamId, false, ("Close", GlazeIcon.Close));
            y -= U(Section);
            y -= Text(board, "Asks", "It asks: “How long should a sign-in lockout last?”", GlazeType.Title, GlazeTokens.Text, left, y, right - left, 2, lean: true).Height;
            y -= U(Section);
            var half = (right - left - board.TargetGap) / 2f;
            var tall = U(GlazeButton.HeightDegrees);
            var first = Row(board, "15 minutes", ButtonRole.Choice, new PanelRow { Title = "15 minutes", Chosen = true, End = "Chosen" }, left, y, half);
            first.On = true;
            Row(board, "1 hour", ButtonRole.Choice, new PanelRow { Title = "1 hour" }, left + half + board.TargetGap, y, half);
            y -= tall + board.TargetGap;
            Button(board, "Type my own", ButtonRole.Secondary, "Type my own", left + half / 2f, y - tall / 2f, half, GlazeIcon.Type);
            Button(board, "Hold to talk", ButtonRole.Secondary, "Hold to talk", left + half + board.TargetGap + half / 2f, y - tall / 2f, half, GlazeIcon.HoldToTalk);
            y -= tall + board.TargetGap;
            var more = Measure(board, ButtonRole.Secondary, "More", compact: true);
            Button(board, "More", ButtonRole.Secondary, "More", left + more / 2f, y - tall / 2f, more, compact: true);
            var send = Measure(board, ButtonRole.Primary, "Send answer", GlazeIcon.SendAnswer);
            Button(board, "Send answer", ButtonRole.Primary, "Send answer", right - send / 2f, y - tall / 2f, send, GlazeIcon.SendAnswer);
            y -= tall + U(Pad);
            Finish(board, width, y, Glaze.Panel);
            PlaceUnderLabels(shot, board, CardYaw(shot, slot, 18f, 0f));
            Tether(shot, board, slot);
        }

        /// <summary>The approval after the question: the whole request, and Yes where no control stood.</summary>
        private static void ShelfApproval(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var board = shot.Board("Card");
            var width = U(36f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = Header(board, left, right, -U(Pad), OpenedTitle, who, shot.Characters[slot].View.WorkstreamId, false, ("Close", GlazeIcon.Close));
            y -= U(Section);
            y -= Text(board, "Wants", "It wants to run a command:", GlazeType.Body, GlazeTokens.Text, left, y, right - left).Height + U(LineGap);
            var well = U(3f);
            Plate(board, "Command", 0f, y - well / 2f, right - left, well, U(Glaze.RowRadiusDegrees), GlazeTokens.ColorOf(Glaze.Well), order: 52);
            Text(board, "Command words", "make migrate", GlazeType.Body, GlazeTokens.Text, left + U(0.9f), y - (well - U(Glaze.BodyDegrees) * 1.15f) / 2f, right - left - U(1.8f), order: 56);
            y -= well + U(Section);
            var tall = U(GlazeButton.HeightDegrees);
            // Confirming: Yes stands where no control stood, Cancel where Approve was pressed.
            var yes = Measure(board, ButtonRole.Primary, "Yes, approve", GlazeIcon.Approve);
            Text(board, "Asks to confirm", "Approve the request above?", GlazeType.Body, GlazeTokens.Text, left, y - (tall - U(Glaze.BodyDegrees) * 1.15f) / 2f, right - left - yes - U(1f));
            var confirm = Button(board, "Yes, approve", ButtonRole.Primary, "Yes, approve", right - yes / 2f, y - tall / 2f, yes, GlazeIcon.Approve);
            confirm.On = true;
            y -= tall + board.TargetGap;
            var more = Measure(board, ButtonRole.Secondary, "More", compact: true);
            Button(board, "More", ButtonRole.Secondary, "More", left + more / 2f, y - tall / 2f, more, compact: true);
            var cancel = Measure(board, ButtonRole.Secondary, "Cancel", GlazeIcon.Close);
            Button(board, "Cancel", ButtonRole.Secondary, "Cancel", right - cancel / 2f, y - tall / 2f, cancel, GlazeIcon.Close);
            y -= tall + U(Pad);
            Finish(board, width, y, Glaze.Panel);
            PlaceUnderLabels(shot, board, CardYaw(shot, slot, 18f, 0f));
            Tether(shot, board, slot);
        }

        /// <summary>
        /// Understanding a task: the card says its state; its shelf leads each question with a
        /// sentence you can open; What changed? is open beside it, its evidence classes as chips and
        /// its source in a footer.
        /// </summary>
        private static void ShelfUnderstanding(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var board = shot.Board("Card and shelf");
            var width = U(34f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = Header(board, left, right, -U(Pad), OpenedTitle, who, shot.Characters[slot].View.WorkstreamId, false, ("Close", GlazeIcon.Close));
            y -= U(Section);
            y -= Text(board, "Latest", "Nothing is waiting for you. Latest: ran npm test.", GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left).Height;
            y -= U(Pad);
            var cardBottom = y;
            y -= U(Pad) * 0.8f;
            var shelfTop = y;
            var rowWidth = right - left;
            var opened = Row(board, "What changed", ButtonRole.Choice, new PanelRow { Title = "2 files changed, both new", Chosen = true, End = "Open" }, left, y, rowWidth);
            opened.On = true;
            y -= opened.Size.y + board.TargetGap;
            y -= Row(board, "What was checked", ButtonRole.Choice, new PanelRow { Title = "Tests failed after the last change", End = "See" }, left, y, rowWidth).Size.y + board.TargetGap;
            var tall = U(GlazeButton.HeightDegrees);
            var talk = Measure(board, ButtonRole.Secondary, "Hold to talk", GlazeIcon.HoldToTalk);
            Field(board, "Tell it what to do next", left, y, rowWidth - talk - board.TargetGap);
            Button(board, "Hold to talk", ButtonRole.Secondary, "Hold to talk", right - talk / 2f, y - tall / 2f, talk, GlazeIcon.HoldToTalk);
            y -= tall + U(Pad);
            board.Height = -y;
            board.Width = width;
            Plate(board, "Shelf", 0f, (cardBottom + y) / 2f + U(0.6f), width, cardBottom - y + U(1.2f), U(PanelRadius), GlazeTokens.ColorOf(Glaze.Well),
                GlazeTokens.ColorOf(Glaze.Outline, 0.55f), U(0.1f), 49);
            Plate(board, "Card", 0f, cardBottom / 2f, width, -cardBottom, U(PanelRadius), GlazeTokens.ColorOf(Glaze.Panel), GlazeTokens.ColorOf(Glaze.Outline, 0.55f), U(0.1f), 50, -U(0.01f));
            _ = shelfTop;

            // What changed?, beside it on the side toward the person's middle.
            var sheet = shot.Board("Answer: What changed?");
            var sheetWidth = U(31f);
            var sl = -sheetWidth / 2f + U(Pad);
            var sr = sheetWidth / 2f - U(Pad);
            var sy = Header(sheet, sl, sr, -U(Pad), "What changed?", null, false, ("Close", GlazeIcon.Close));
            sy -= U(Section);
            sy -= Text(sheet, "Lead", "2 files changed, both new.", GlazeType.Body, GlazeTokens.Text, sl, sy, sr - sl).Height + U(Section);
            foreach (var (kind, file, lines) in new[] { ("New", "0012_sign_in_attempts.sql", "+14"), ("New", "src/auth/rate-limit.ts", "+57") })
            {
                var h = Text(sheet, "File " + file, kind + " · " + file, GlazeType.Body, GlazeTokens.Text, sl, sy, sr - sl - U(4f)).Height;
                Text(sheet, "Lines " + file, lines, GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground), sr, sy, U(4f), alignment: TextAlignmentOptions.TopRight);
                sy -= h + U(LineGap);
            }
            sy -= U(Section) - U(LineGap);
            var chip = Chip(sheet, "Inferred", GlazeTone.Unknown, sl, sy - U(0.75f));
            sy -= Text(sheet, "Inferred line", "Nothing checked these files after the last change.", GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground),
                sl + chip + U(0.6f), sy, sr - sl - chip - U(0.6f), 2).Height + U(Section);
            var said = Chip(sheet, "Agent says", GlazeTone.Neutral, sl, sy - U(0.75f));
            sy -= Text(sheet, "Reason", "“Add a table of failed sign-ins, then limit tries per address and per account.”", GlazeType.Body, GlazeTokens.Text,
                sl + said + U(0.6f), sy, sr - sl - said - U(0.6f), 3, lean: true).Height + U(Section);
            sy -= Text(sheet, "Source", "From Salidium · 2 minutes ago", GlazeType.Caption, GlazeTokens.TextSecondary, sl, sy, sr - sl).Height + U(Pad);
            Finish(sheet, sheetWidth, sy, Glaze.Panel);

            // The card on its character's side, the answer beside it toward the person's middle.
            var side = shot.YawOf(slot) >= 0f ? -1 : 1;
            PlaceBeside(shot, board, sheet, -side * 17.5f, side);
            Tether(shot, board, slot);
        }

        /// <summary>Creating a project: the companion's turn as a card, every suggestion at once, in the dock's place.</summary>
        private static void ShelfCreating(Shot shot)
        {
            var board = shot.Board("Create a project");
            var width = U(44f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = Header(board, left, right, -U(Pad), "Create a project", null, true, ("Go on without it", null), ("Close", GlazeIcon.Close));
            y -= U(Section);
            y -= Text(board, "Companion says", "The companion says: “A running club could use a page that keeps everyone's race times in one place.”",
                GlazeType.Body, GlazeTokens.Text, left, y, right - left, 2, lean: true).Height + U(Section);
            y -= Text(board, "Question", "Who enters the times after each race?", GlazeType.Title, GlazeTokens.Text, left, y, right - left).Height + U(Section);
            var tall = U(GlazeButton.HeightDegrees);
            var x = left;
            foreach (var answer in new[] { "Each runner", "One organiser", "Both", "Not sure yet" })
            {
                var w = Measure(board, ButtonRole.Choice, answer);
                Button(board, answer, ButtonRole.Choice, answer, x + w / 2f, y - tall / 2f, w);
                x += w + board.TargetGap;
            }
            y -= tall + board.TargetGap;
            var type = Measure(board, ButtonRole.Secondary, "Type my own", GlazeIcon.Type);
            Button(board, "Type my own", ButtonRole.Secondary, "Type my own", left + type / 2f, y - tall / 2f, type, GlazeIcon.Type);
            var talk = Measure(board, ButtonRole.Secondary, "Hold to talk", GlazeIcon.HoldToTalk);
            Button(board, "Hold to talk", ButtonRole.Secondary, "Hold to talk", left + type + board.TargetGap + talk / 2f, y - tall / 2f, talk, GlazeIcon.HoldToTalk);
            var recap = Measure(board, ButtonRole.Primary, "Make the recap", GlazeIcon.Next);
            Button(board, "Make the recap", ButtonRole.Primary, "Make the recap", right - recap / 2f, y - tall / 2f, recap, GlazeIcon.Next);
            y -= tall + U(Section);
            y -= Text(board, "Disclaimer", "The companion is an AI on your computer. It can be wrong, and you can change everything before you start.",
                GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left, 2).Height + U(Pad);
            Finish(board, width, y, Glaze.Panel);
            PlaceUnderLabels(shot, board, 0f);
        }

        /// <summary>A surface's plate behind what was laid out, and its size.</summary>
        private static void Finish(Board board, float width, float bottom, GlazeColor fill)
        {
            board.Height = -bottom;
            board.Width = width;
            Plate(board, board.Name, 0f, -board.Height / 2f, width, board.Height, U(PanelRadius), GlazeTokens.ColorOf(fill), GlazeTokens.ColorOf(Glaze.Outline, 0.55f), U(0.1f), 49);
        }

        /// <summary>While a video window has focus: the characters either side, and one line under the window, folded from the dock.</summary>
        private static void Folded(Shot shot)
        {
            var board = shot.Board("Folded dock");
            var text = "1 task is waiting for you";
            var width = WidthOf(board, text, GlazeType.Body) + U(2.4f) + U(1.6f);
            var height = U(2.6f);
            Plate(board, "Strip", 0f, -height / 2f, width, height, height / 2f, GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Strong), U(0.12f));
            var icon = GlazeIcons.Create(board.Content, "Icon", GlazeIcons.BadgeDegrees, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground), 57);
            GlazeIcons.Show(icon, GlazeIcon.WaitingForYou);
            icon.transform.localPosition = new Vector3(-width / 2f + U(1.2f) + U(0.6f), -height / 2f, -U(0.05f));
            Text(board, "Strip words", text, GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground), -width / 2f + U(2.6f), -(height - U(Glaze.BodyDegrees) * 1.15f) / 2f, width - U(3.2f));
            board.Height = height;
            PlaceByTop(board, shot.Eyes, 0f, -15.5f);
        }

        /// <summary>Back from the video: the waiting task's card under the window, tethered to its character at the side.</summary>
        private static void ShelfBesideWindow(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var board = shot.Board("Card");
            var width = U(36f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = Header(board, left, right, -U(Pad), OpenedTitle, who, shot.Characters[slot].View.WorkstreamId, false, ("Close", GlazeIcon.Close));
            y -= U(Section);
            y -= Text(board, "Asks", "It asks: “How long should a sign-in lockout last?”", GlazeType.Title, GlazeTokens.Text, left, y, right - left, 2, lean: true).Height + U(Section);
            var half = (right - left - board.TargetGap) / 2f;
            var tall = U(GlazeButton.HeightDegrees);
            Row(board, "15 minutes", ButtonRole.Choice, new PanelRow { Title = "15 minutes" }, left, y, half);
            Row(board, "1 hour", ButtonRole.Choice, new PanelRow { Title = "1 hour" }, left + half + board.TargetGap, y, half);
            y -= tall + board.TargetGap;
            var more = Measure(board, ButtonRole.Secondary, "More", compact: true);
            Button(board, "More", ButtonRole.Secondary, "More", left + more / 2f, y - tall / 2f, more, compact: true);
            var send = Measure(board, ButtonRole.Primary, "Send answer", GlazeIcon.SendAnswer);
            var sendButton = Button(board, "Send answer", ButtonRole.Primary, "Send answer", right - send / 2f, y - tall / 2f, send, GlazeIcon.SendAnswer);
            sendButton.Available = false;
            y -= tall + U(Pad);
            Finish(board, width, y, Glaze.Panel);
            PlaceByTop(board, shot.Eyes, 0f, -15.5f);
            Tether(shot, board, slot);
        }
    }
}
