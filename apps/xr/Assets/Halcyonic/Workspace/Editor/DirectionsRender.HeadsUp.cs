#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Heads-up, a concept (the owner's references of 2026-10-02, an augmented-reality running display
    /// among them): while the person walks round the room, or while a 2D window has the focus, the stage
    /// steps aside and two small glance items stand at the edges of the view, each facing the eyes; a
    /// glance and a pinch on the one that waits opens the menu. Rendered as the person sees it, the head
    /// level and looking ahead, in a Quest 3S's field.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> HeadsUpShots() => new (string, bool, Action<Shot>)[]
        {
            ("h1-heads-up-walking", false, shot => HeadsUp(shot, walking: true)),
            ("h2-heads-up-video", true, shot => HeadsUp(shot, walking: false)),
        };

        /// <summary>From the eyes to a glance item.</summary>
        private const float GlanceMeters = 1.0f;

        /// <summary>How far to the side of the view's middle the glance items stand, and how far below eye level.</summary>
        private const float GlanceYaw = 33f;
        private const float GlanceElevation = -8f;

        /// <summary>The glance items of the shot being built, for its captures and checks.</summary>
        private static readonly List<Board> glances = new List<Board>();

        private static void HeadsUp(Shot shot, bool walking)
        {
            glances.Clear();
            var slot = shot.SlotOf(OpenedTitle);
            if (walking)
            {
                // Walking, the stage hides: nothing stands in the person's way.
                foreach (var character in shot.Characters) character.View.gameObject.SetActive(false);
                Floor(shot);
            }
            else
            {
                // Beside a window, the characters huddle small in a row under the item that waits, their labels away.
                var index = 0;
                foreach (var character in shot.Characters)
                {
                    var view = character.View;
                    view.Label.gameObject.SetActive(false);
                    var yaw = GlanceYaw + (index - (shot.Characters.Count - 1) / 2f) * 2.4f;
                    var direction = Quaternion.Euler(22f, yaw, 0f) * Vector3.forward;
                    view.transform.position = shot.Eyes + direction * 1.6f;
                    view.transform.localScale *= 0.22f;
                    index++;
                }
            }

            // The work, at the left edge: what is working, what waits for you, how long it has run.
            var work = shot.Board("Glance, the work", GlanceMeters);
            var width = U(12f);
            var rows = new (GlazeIcon Icon, Color Colour, string Figure, string Words)[]
            {
                (GlazeIcon.Working, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground), "3", "working"),
                (GlazeIcon.WaitingForYou, AmberText, "1", "waiting for you"),
                (GlazeIcon.Starting, Secondary, "42 min", "so far"),
            };
            var left = -width / 2f + U(1f);
            var y = -U(0.9f);
            foreach (var (icon, colour, figure, words) in rows)
            {
                GlanceRow(work, icon, colour, figure, words, left, y);
                y -= U(2.6f);
            }
            work.Width = width;
            work.Height = -y + U(0.3f);
            Shape(work, 0f, -work.Height / 2f, width, work.Height, sheen: true);
            FacingAt(work, shot.Eyes, -GlanceYaw, GlanceElevation, GlanceMeters);
            glances.Add(work);

            // What waits, at the right edge: how long, and which task; pointed at, the frame, and the pinch.
            var waits = shot.Board("Glance, what waits", GlanceMeters);
            var plate = U(5.6f);
            waits.Width = width;
            var wl = -width / 2f + U(1f);
            GlanceRow(waits, GlazeIcon.WaitingForYou, AmberText, "2 min", "waiting", wl, -U(0.9f));
            Body(waits, "Which", "Add rate limiting to the sign-in endpoint", wl, -U(3.4f), width - U(2f), GlazeTokens.Text);
            Shape(waits, 0f, -plate / 2f, width, plate, sheen: true);
            Target(waits, "Open the menu", 0f, -plate / 2f, width, compact: false);
            if (walking) Select(waits, 0f, -plate / 2f, width, plate, lit: false);
            // While a window has the focus, the first pinch here only brings it back (Meta's focus rule).
            var hint = walking ? "Pinch to open" : "Pinch to come back";
            var hintY = -plate - waits.TargetGap - U(GlazeButton.HeightDegrees) / 2f;
            Body(waits, "Hint", hint, wl, hintY + U(BodySize) * 0.6f, width - U(1f), Secondary);
            waits.Height = plate + waits.TargetGap + U(GlazeButton.HeightDegrees);
            FacingAt(waits, shot.Eyes, GlanceYaw, GlanceElevation, GlanceMeters);
            glances.Add(waits);
        }

        /// <summary>A glance row: the icon, a figure at the title's size, and its words at the small size, on one baseline.</summary>
        private static void GlanceRow(Board board, GlazeIcon icon, Color colour, string figure, string words, float left, float top)
        {
            Glyph(board, "Icon " + words, icon, left + U(0.6f), top - U(TitleSize) * 0.62f, colour, 1.1f);
            var (big, bigHeight) = Text(board, "Figure " + words, figure, GlazeType.Display, colour, left + U(1.7f), top, U(8f), strong: false);
            var bigWidth = WidthOf(board, figure, GlazeType.Display);
            var smallTop = top - (GlazeText.LineHeight(big) - U(LabelSize) * 1.25f) * 0.72f;
            Text(board, "Words " + words, words, GlazeType.Caption, Secondary, left + U(1.7f) + bigWidth + U(Grid), smallTop, U(9f));
        }

        /// <summary>Stands a board at <paramref name="yaw"/> and <paramref name="elevation"/> from the eyes, facing them squarely, never rolled.</summary>
        private static void FacingAt(Board board, Vector3 eyes, float yaw, float elevation, float distance)
        {
            var direction = Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward;
            board.Content.localPosition = new Vector3(0f, board.Height / 2f, 0f);
            board.Root.SetPositionAndRotation(eyes + direction * distance, Quaternion.LookRotation(direction, Vector3.up));
        }

        /// <summary>The room's floor, faintly, so a still reads as standing in it: a grid every half meter, 1.6 m under standing eyes.</summary>
        private static void Floor(Shot shot)
        {
            // The grid is drawn into one texture across the floor: the sprite shader does not tile.
            const int Size = 1536;
            const int Every = Size / 24;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 16 };
            var pixels = new Color32[Size * Size];
            var line = new Color32(140, 158, 184, 34);
            for (var z = 0; z < Size; z++)
            {
                for (var x = 0; x < Size; x++) pixels[z * Size + x] = x % Every < 2 || z % Every < 2 ? line : new Color32(0, 0, 0, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Floor";
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(shot.Root, false);
            floor.transform.SetPositionAndRotation(shot.Eyes + new Vector3(0f, -1.6f, 4f), Quaternion.Euler(90f, 0f, 0f));
            floor.transform.localScale = new Vector3(12f, 12f, 1f);
            var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
            var renderer = floor.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 1;
        }

        // ---------------------------------------------------------------------------------------------
        // Renders: the view with the head level and looking ahead, in a Quest 3S's field, and each glance
        // item close, aimed where the eyes turn to read it.

        private static Texture2D FieldView(Vector3 eyes, Transform parent)
        {
            var across = Mathf.Tan(48f * Mathf.Deg2Rad);
            var up = Mathf.Tan(45f * Mathf.Deg2Rad);
            return CaptureFrustum(eyes, parent, Quaternion.identity, -across, across, -up, up, WideWidth, Mathf.RoundToInt(WideWidth * up / across));
        }

        private static Texture2D GlanceCloseUp(Shot shot, Board glance) => AimedView(shot, new[] { glance }, wide: false);

        // ---------------------------------------------------------------------------------------------
        // Checks: inside a Quest 3S's field with the head level and looking ahead, less its margin;
        // every item facing the eyes squarely; and no text under 14 dp as the eyes see it.

        private static IEnumerable<string> HeadsUpChecks(Shot shot)
        {
            if (!shot.Name.StartsWith("h", StringComparison.Ordinal)) yield break;
            var corners = glances.SelectMany(glance => new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                .Select(y => glance.Root.TransformPoint(new Vector3(x * glance.Width, y * glance.Height, 0f))))).ToList();
            foreach (var failure in FieldChecks.Inside(shot.Name + " glance items", corners, shot.Eyes, shot.Eyes + Vector3.forward, 0f, FieldChecks.Quest3S))
            {
                yield return failure;
            }
            foreach (var glance in glances)
            {
                var off = Vector3.Angle(glance.Root.forward, glance.Root.position - shot.Eyes);
                if (off > 0.5f) yield return shot.Name + ": " + glance.Name + " is " + GlazeChecks.Degrees(off) + " degrees off square to the eyes; what is read faces them.";
            }
            foreach (var failure in SeenTextFailures(shot, glances)) yield return failure;
        }
    }
}
