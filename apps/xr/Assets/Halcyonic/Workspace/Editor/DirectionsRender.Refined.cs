#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// D, refined (the owner's notes of 2026-10-02 on D, styled). Every foreground surface lies on one
    /// plane, none angled against another, edges aligned; detail slides out to the right along the same
    /// plane. Each part is its own rounded shape: the subject on top, the sections as separate shapes in a
    /// row under it, the content under them. Type only steps down: the subject largest, then the content
    /// and the prompts, then small facts. One selection treatment: a lit shape with a crisp frame. The
    /// places are single words. The plane stands upright (r1, r2) or, as chosen the same day, is tipped
    /// back as a whole to face the eyes at its centre (r3, r4): upright, the text low on it shrinks as the
    /// eyes see it.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> RefinedShots() => new (string, bool, Action<Shot>)[]
        {
            ("r1-arriving-tasks-file", false, shot => Lay(Facing.Upright, () => RefinedHero(shot))),
            ("r2-waiting-approve", false, shot => Lay(Facing.Upright, () => RefinedApproval(shot))),
            ("r3-arriving-tasks-file-facing", false, shot => Lay(Facing.Eyes, () => RefinedHero(shot))),
            ("r4-waiting-approve-facing", false, shot => Lay(Facing.Eyes, () => RefinedApproval(shot))),
            ("r5-menu-alone-facing", false, shot => Lay(Facing.Eyes, () => RefinedHero(shot, withFile: false))),
            ("r6-file-slides-out-staying", false, shot => Lay(Facing.Stay, () => RefinedHero(shot))),
            ("r7-arriving-tasks-file-split", false, shot => Lay(Facing.Eyes, () => RefinedHero(shot, split: true))),
            ("r8-waiting-approve-split", false, shot => Lay(Facing.Eyes, () => RefinedApproval(shot, split: true))),
        };

        /// <summary>
        /// The split header's state pill: the character's own badge on the file's title plate, as on its
        /// label on the stage, its word at the content's 18 dp rather than the stage's 16.
        /// </summary>
        private const float PillScale = 1.125f;

        /// <summary>
        /// How the plane stands: upright; tipped back as a whole to face the eyes at its centre; or, when
        /// a file slides out beside the menu, staying where the menu alone put it, the file to its right
        /// (Stay, kept to show what re-centring the plane avoids).
        /// </summary>
        private enum Facing { Upright, Eyes, Stay }

        private static Facing facing = Facing.Upright;

        /// <summary>
        /// Shots kept to show what a rule catches, with the failures each must show. Those are expected and
        /// logged; a shot that stops showing one fails the run, and any other failure still counts.
        /// </summary>
        private static readonly Dictionary<string, string[]> MustFail = new Dictionary<string, string[]>
        {
            // An upright plane below the eyes: its low text shrinks as the eyes see it.
            ["r1-arriving-tasks-file"] = new[] { "as the eyes see it" },
            ["r2-waiting-approve"] = new[] { "as the eyes see it" },
            // A plane that stays where the menu alone put it while the file slides out to its right: off
            // square, the file's far text shrunk, a corner past a Quest 3S's field, and its top in a label.
            ["r6-file-slides-out-staying"] = new[] { "off square to the eyes", "as the eyes see it", "lie outside the field", "'s outline and" },
        };

        private static List<string> KeptToFail(string name, List<string> failures)
        {
            if (!MustFail.TryGetValue(name, out var expected)) return failures;
            var left = failures.Where(failure => !expected.Any(failure.Contains)).ToList();
            foreach (var phrase in expected)
            {
                var shown = failures.Count(failure => failure.Contains(phrase));
                if (shown == 0) left.Add(name + ": kept to fail \"" + phrase + "\", it no longer does; the check has stopped catching it.");
                else Debug.Log("Halcyonic: directions render " + name + ": as expected, " + shown + " failures \"" + phrase + "\".");
            }
            return left;
        }

        private static void Lay(Facing how, Action build)
        {
            facing = how;
            build();
        }

        /// <summary>
        /// Upright, the plane's horizontal distance from the eyes (the content's middle is then about
        /// 0.46 m away); facing the eyes, the distance to the composition's centre, ADR 0023's 0.46 m.
        /// </summary>
        private static float PlaneMeters => facing == Facing.Upright ? 0.40f : 0.46f;

        private const float RowA = 3.2f;       // the subject's shape
        private const float RowB = 3.0f;       // the sections' shapes: 48 dp targets
        private const float RowGap = 1.0f;     // between the shapes of one column: a degree, as between any two things
        private const float ShapeRadius = 0.9f;  // every rounded shape's corners
        private const float RowInset = 0.7f;     // a shape round words reaches this far past the content line

        private static readonly Color LitFill = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color LitFrame = new Color(1f, 1f, 1f, 0.78f);
        private static readonly Color PointedFrame = new Color(1f, 1f, 1f, 0.42f);

        private static readonly string[] Places = { "Tasks", "Projects", "Usage", "Settings" };
        private static readonly string[] FileSections = { "Waiting", "Activity", "Changes", "Checks" };

        /// <summary>The surfaces of one composition, on one plane, for the plane's own checks.</summary>
        private static readonly List<List<Board>> columns = new List<List<Board>>();

        // ---------------------------------------------------------------------------------------------
        // Shapes, and the one selection treatment.

        /// <summary>A rounded glass shape: the panel colour a little transparent, a hairline edge; lit when chosen.</summary>
        private static void Shape(Board board, float centerX, float centerY, float width, float height, bool lit = false, bool pointed = false, bool sheen = false)
        {
            Plate(board, board.Name + " shape", centerX, centerY, width, height, U(ShapeRadius), GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), new Color(1f, 1f, 1f, 0.12f), U(0.06f), 48);
            if (sheen)
            {
                Plate(board, "Sheen", centerX, centerY + height / 2f - U(0.1f), width - 2f * U(ShapeRadius), U(0.06f), 0f, new Color(1f, 1f, 1f, 0.2f), order: 50, depth: -U(0.01f));
                Gloss(board, centerX, centerY, width, height);
            }
            if (lit || pointed) Select(board, centerX, centerY, width, height, lit);
        }

        /// <summary>The gloss: a light from the shape's top edge fading out by a third of its height, its top corners the shape's own.</summary>
        private static void Gloss(Board board, float centerX, float centerY, float width, float height)
        {
            if (gradient == null)
            {
                gradient = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < 64; y++)
                {
                    var x01 = Mathf.Clamp01((y / 63f - 0.35f) / 0.65f);
                    var alpha = 0.06f * x01 * x01 * (3f - 2f * x01);
                    for (var x = 0; x < 2; x++) gradient.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
                gradient.Apply();
            }
            // Inside the hairline: the outline runs up the left side, round both top corners and down the right.
            var inset = U(0.06f);
            var band = Mathf.Min(height, U(9f)) - inset;
            var half = width / 2f - inset;
            var radius = Mathf.Max(0f, Mathf.Min(U(ShapeRadius), height / 2f) - inset);
            var top = centerY + height / 2f - inset;
            const int Steps = 10;
            var outline = new List<Vector2> { new Vector2(-half, -band) };
            for (var step = 0; step <= Steps; step++)
            {
                var angle = Mathf.PI - step * Mathf.PI / 2f / Steps;
                outline.Add(new Vector2(-half + radius + radius * Mathf.Cos(angle), -radius + radius * Mathf.Sin(angle)));
            }
            for (var step = 0; step <= Steps; step++)
            {
                var angle = Mathf.PI / 2f - step * Mathf.PI / 2f / Steps;
                outline.Add(new Vector2(half - radius + radius * Mathf.Cos(angle), -radius + radius * Mathf.Sin(angle)));
            }
            outline.Add(new Vector2(half, -band));
            var vertices = new List<Vector3> { new Vector3(centerX, top - band / 2f, -U(0.005f)) };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            foreach (var point in outline)
            {
                vertices.Add(new Vector3(centerX + point.x, top + point.y, -U(0.005f)));
                uvs.Add(new Vector2(0.5f, 1f + point.y / band));
            }
            var triangles = new List<int>();
            for (var index = 1; index <= outline.Count; index++)
            {
                triangles.Add(0);
                triangles.Add(index);
                triangles.Add(index == outline.Count ? 1 : index + 1);
            }
            var mesh = new Mesh { name = "Gloss" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            var go = new GameObject("Gloss");
            go.transform.SetParent(board.Content, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = gradient };
            renderer.sortingOrder = 49;
        }

        /// <summary>
        /// A shape inside the content's, as an answer: its hairline alone, drawn over the content's glass
        /// so its order never depends on the camera; lit when chosen.
        /// </summary>
        private static void InnerShape(Board board, float centerX, float centerY, float width, float height, bool lit = false)
        {
            if (lit) Select(board, centerX, centerY, width, height, lit: true);
            else Plate(board, "Inner shape", centerX, centerY, width, height, U(ShapeRadius), Color.clear, new Color(1f, 1f, 1f, 0.12f), U(0.06f), 51, -U(0.015f));
        }

        /// <summary>
        /// The one selection treatment: chosen, the shape lights up and gains a crisp white frame and its
        /// words go full white; pointed at, the frame alone, fainter. No underline, no bar.
        /// </summary>
        private static void Select(Board board, float centerX, float centerY, float width, float height, bool lit)
        {
            Plate(board, lit ? "Lit" : "Pointed", centerX, centerY, width, height, U(ShapeRadius), lit ? LitFill : Color.clear,
                lit ? LitFrame : PointedFrame, U(lit ? 0.08f : 0.06f), 51, -U(0.015f));
        }

        private static GlazeButton Target(Board board, string name, float centerX, float centerY, float width, bool compact)
        {
            var hit = Button(board, name, ButtonRole.Secondary, "", centerX, centerY, width, compact: compact);
            hit.Label.gameObject.SetActive(false);
            Restyle(hit, Color.clear, Color.clear);
            return hit;
        }

        // ---------------------------------------------------------------------------------------------
        // The column's three parts: the subject, the sections, the content.

        /// <summary>
        /// The subject alone in its shape, whole, on two lines where one won't hold it. The task's state
        /// is not repeated here: it shows on its character, joined by the light line, and as the dot on
        /// the lit section.
        /// </summary>
        /// <param name="reserve">
        /// Room above the plate for a state pill on its top edge, and as much inside it under the pill:
        /// every column's subject keeps it, pill or not, so the plates and their titles stay level.
        /// </param>
        /// <param name="who">With the split header, the task whose state pill stands on the plate's top edge, at its left.</param>
        private static Board SubjectShape(Shot shot, string name, string subject, Color colour, float width, float? height = null, float reserve = 0f,
            CharacterPresentation? who = null)
        {
            var board = shot.Board(name, PlaneMeters);
            board.Width = width;
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var (title, titleHeight) = Text(board, "Title", subject, GlazeType.Display, colour, left, 0f, right - left, 2, strong: false);
            if (lightMaterial == null)
            {
                lightMaterial = new Material(title.fontSharedMaterial) { name = "Glaze light" };
                lightMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, -0.12f);
                ShaderUtilities.UpdateShaderRatios(lightMaterial);
            }
            title.fontSharedMaterial = lightMaterial;
            board.Height = height ?? reserve + Mathf.Max(U(RowA), reserve + titleHeight + 2f * U(0.75f));
            var plate = board.Height - reserve;
            title.transform.localPosition = new Vector3(left, -reserve - reserve - (plate - reserve - titleHeight) / 2f, -U(0.05f));
            Shape(board, 0f, -reserve - plate / 2f, width, plate, sheen: true);
            if (who != null)
            {
                var pill = StateBadgeView.Create(board.Content, "State pill", 58);
                pill.transform.localScale = Vector3.one * PillScale;
                pill.Show(StateLanguage.BadgeOf(who));
                pill.transform.localPosition = new Vector3(left + pill.Width * PillScale / 2f, -reserve, -U(0.06f));
            }
            return board;
        }

        private static Board SectionShapes(Shot shot, string name, IReadOnlyList<string> names, float width, int chosen, int waiting)
        {
            var board = shot.Board(name, PlaneMeters);
            var height = U(RowB);
            board.Width = width;
            board.Height = height;
            var gap = board.TargetGap;
            var each = (width - gap * (names.Count - 1)) / names.Count;
            for (var index = 0; index < names.Count; index++)
            {
                var cx = -width / 2f + each / 2f + index * (each + gap);
                var cy = -height / 2f;
                var chosenOne = index == chosen;
                Shape(board, cx, cy, each, height, lit: chosenOne);
                Target(board, "Section " + names[index], cx, cy, each, compact: true);
                var wordWidth = WidthOf(board, names[index], GlazeType.Body) * (chosenOne ? 1.04f : 1f);
                var dot = index == waiting ? U(0.42f) + U(Grid) : 0f;
                var startX = cx - (wordWidth + dot) / 2f;
                if (index == waiting) Plate(board, "Waiting dot", startX + U(0.21f), cy, U(0.42f), U(0.42f), U(0.21f), Amber, order: 62, depth: -U(0.03f));
                Body(board, "Section " + names[index] + " words", names[index], startX + dot, cy + U(BodySize) * 0.6f, wordWidth + U(0.4f),
                    chosenOne ? GlazeTokens.Text : Secondary, strong: chosenOne);
            }
            return board;
        }

        /// <summary>A row of the content: icon, words, a small fact, a chevron when it opens more; lit when chosen.</summary>
        private static void RefinedRow(Board board, string words, float left, float right, float top, Action<float, float> icon, string? fact = null,
            Color? factColour = null, bool chosen = false, bool more = false)
        {
            var tall = U(RowHeight);
            var middle = top - tall / 2f;
            var inset = U(RowInset);
            if (chosen) Select(board, (left + right) / 2f, middle, right - left + 2f * inset, tall, lit: true);
            Target(board, "Row " + words, (left + right) / 2f, middle, right - left + 2f * inset, compact: true);
            icon(left + U(IconColumn) / 2f, middle);
            var factWidth = fact == null ? 0f : WidthOf(board, fact, GlazeType.Caption) + U(0.3f);
            var end = right - (more ? U(1.6f) : 0f);
            Body(board, "Row words " + words, words, left + U(IconColumn) + U(Grid), middle + U(BodySize) * 0.6f,
                end - left - U(IconColumn) - U(Grid) - factWidth - U(0.8f), chosen ? GlazeTokens.Text : new Color(0.93f, 0.95f, 0.97f, 0.9f));
            if (fact != null) Fact(board, "Row fact " + words, fact, end, middle + U(LabelSize) * 0.6f, factWidth, factColour, TextAlignmentOptions.TopRight);
            if (more) ProtoGlyph(board, "More", GlyphChevron, right - U(0.5f), middle, chosen ? GlazeTokens.Text : Secondary, 1.2f);
        }

        /// <summary>
        /// The prompts in the content shape's last row: Close far left, the main action far right. Their
        /// words are the content's size: they stand at the plane's far corners, where 15 dp reads under
        /// 14 dp as the eyes see it.
        /// </summary>
        private static void RefinedFooter(Board board, float left, float right, float middle, Prompt? farLeft, Prompt? nearLeft, Prompt? nearRight, Prompt? farRight)
        {
            float WidthOfPrompt(Prompt prompt) => U(1.45f) + U(Grid) + WidthOf(board, prompt.Words, GlazeType.Body) * (prompt.Main ? 1.05f : 1f) + U(1.2f);
            void Draw(Prompt prompt, float x)
            {
                var width = WidthOfPrompt(prompt);
                Target(board, "Prompt " + prompt.Words, x + width / 2f, middle, width, compact: false);
                if (prompt.PointedAt) Select(board, x + width / 2f, middle, width, U(GlazeButton.HeightDegrees) - U(0.4f), lit: false);
                var cap = U(1.45f);
                var capX = x + U(0.6f) + cap / 2f;
                if (prompt.Main) Plate(board, "Cap", capX, middle, cap, cap, cap / 2f, Accent, order: 62, depth: -U(0.04f));
                else Plate(board, "Cap", capX, middle, cap, cap, cap / 2f, Color.clear, new Color(1f, 1f, 1f, 0.6f), U(0.07f), 62, -U(0.04f));
                Glyph(board, "Cap icon", prompt.Icon, capX, middle, prompt.Main ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).OnStrong) : GlazeTokens.Text, 0.95f);
                Body(board, "Prompt words " + prompt.Words, prompt.Words, capX + cap / 2f + U(Grid), middle + U(BodySize) * 0.6f, U(20f),
                    prompt.Main ? AccentText : GlazeTokens.Text, strong: prompt.Main);
            }
            var x = left - U(0.6f);
            foreach (var prompt in new[] { farLeft, nearLeft })
            {
                if (prompt == null) continue;
                Draw(prompt, x);
                x += WidthOfPrompt(prompt) + board.TargetGap;
            }
            var end = right + U(0.6f);
            foreach (var prompt in new[] { farRight, nearRight })
            {
                if (prompt == null) continue;
                var width = WidthOfPrompt(prompt);
                Draw(prompt, end - width);
                end -= width + board.TargetGap;
            }
        }

        /// <summary>The content shape's plate and its footer row, once its height is known; the footer stands at its bottom.</summary>
        private static void ContentShape(Board board, float width, float height, Prompt? farLeft, Prompt? nearLeft, Prompt? nearRight, Prompt? farRight)
        {
            board.Width = width;
            board.Height = height;
            Shape(board, 0f, -height / 2f, width, height, sheen: true);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var footerMiddle = -height + U(1.0f) + U(GlazeButton.HeightDegrees) / 2f;
            Plate(board, "Footer line", 0f, footerMiddle + U(GlazeButton.HeightDegrees) / 2f + board.TargetGap / 2f, width - U(2f), U(0.05f), 0f,
                new Color(1f, 1f, 1f, 0.09f), order: 53, depth: -U(0.02f));
            RefinedFooter(board, left, right, footerMiddle, farLeft, nearLeft, nearRight, farRight);
        }

        /// <summary>The room a content shape's footer takes: the 12 mm gap, the 60 dp prompts and the bottom padding.</summary>
        private static float FooterRoom(Board board) => board.TargetGap + U(GlazeButton.HeightDegrees) + U(1.0f);

        // ---------------------------------------------------------------------------------------------
        // The plane: one plane for every part, each placed on it by its top-left corner. Upright, the
        // plane stands PlaneMeters ahead and the top line is measured from eye level; facing the eyes, the
        // plane is square to the line of sight at the composition's centre, PlaneMeters away, and the top
        // line is measured from that centre.

        private static void OnPlane(Board board, Vector3 eyes, Quaternion plane, float left, float top)
        {
            var origin = eyes + plane * Vector3.forward * PlaneMeters;
            board.Content.localPosition = new Vector3(0f, board.Height / 2f, 0f);
            var center = origin + plane * new Vector3((left + board.Width / 2f) * PlaneMeters, (top - board.Height / 2f) * PlaneMeters, 0f);
            board.Root.SetPositionAndRotation(center, plane);
        }

        /// <summary>
        /// Lays columns on the plane, each a list of parts stacked from one top line with the rows'
        /// gaps between them, the columns side by side 15 mm apart and the whole centred on
        /// <paramref name="yaw"/>; then lowers the whole until every part keeps a degree and a little
        /// from every label, by its outline.
        /// </summary>
        private static void LayOnPlane(Shot shot, float yaw, IReadOnlyList<IReadOnlyList<Board>> stacks)
        {
            var gap = 0.015f / PlaneMeters;
            // Staying, the plane is where the first column alone put it, and the rest slide out to its right.
            var placed = facing == Facing.Stay ? stacks.Take(1).ToList() : stacks.ToList();
            var total = placed.Sum(stack => stack[0].Width) + gap * (placed.Count - 1);
            var tall = stacks.Max(stack => stack.Sum(part => part.Height) + U(RowGap) * (stack.Count - 1));
            var lower = 0f;
            for (var attempt = 0; attempt < 80; attempt++)
            {
                // Upright, the top line starts 16.5 degrees under eye level; facing, the centre starts
                // where that would put the top.
                var plane = facing == Facing.Upright
                    ? Quaternion.Euler(0f, yaw, 0f)
                    : Quaternion.Euler(16.5f + lower + Mathf.Atan(tall / 2f) * Mathf.Rad2Deg, yaw, 0f);
                var top = facing == Facing.Upright ? -Mathf.Tan((16.5f + lower) * Mathf.Deg2Rad) : tall / 2f;
                var x = -total / 2f;
                foreach (var stack in stacks)
                {
                    var y = top;
                    foreach (var part in stack)
                    {
                        OnPlane(part, shot.Eyes, plane, x, y);
                        y -= part.Height + U(RowGap);
                    }
                    x += stack[0].Width + gap;
                }
                var labels = shot.Characters.Select(character => GlazeChecks.Of("label", shot.Eyes, character.View.Label.gameObject)).ToList();
                var least = placed.SelectMany(stack => stack).Min(part => labels.Min(label => OutlineApart(Outline(part, shot.Eyes), label)));
                if (least >= 1.15f) break;
                lower += 0.25f;
            }
            columns.Clear();
            columns.AddRange(stacks.Select(stack => stack.ToList()));
            // Where the plane ended up, along its middle: its size, how far below eye level, how far away.
            var middle = CompositionCenter(stacks.SelectMany(stack => stack).ToList());
            var up = stacks[0][0].Root.up * (tall / 2f * PlaneMeters);
            float Below(Vector3 point) => -Mathf.Asin((point.y - shot.Eyes.y) / (point - shot.Eyes).magnitude) * Mathf.Rad2Deg;
            Debug.Log("Halcyonic: directions render " + shot.Name + ": the plane holds " + GlazeChecks.Degrees(2f * Mathf.Atan(total / 2f) * Mathf.Rad2Deg) + " by "
                + GlazeChecks.Degrees(2f * Mathf.Atan(tall / 2f) * Mathf.Rad2Deg) + " degrees, from " + GlazeChecks.Degrees(Below(middle + up)) + " to " + GlazeChecks.Degrees(Below(middle - up))
                + " degrees below eye level along its middle, its bottom " + ((middle - up) - shot.Eyes).magnitude.ToString("0.00", CultureInfo.InvariantCulture) + " m away.");
        }

        /// <summary>The composition's centre: the middle of its parts' corners.</summary>
        private static Vector3 CompositionCenter(IReadOnlyList<Board> parts)
        {
            var corners = parts.SelectMany(part => new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                .Select(y => part.Root.TransformPoint(new Vector3(x * part.Width, y * part.Height, 0f))))).ToList();
            var min = corners.Aggregate(Vector3.Min);
            var max = corners.Aggregate(Vector3.Max);
            return (min + max) / 2f;
        }

        // ---------------------------------------------------------------------------------------------
        // The two frames.

        /// <summary>
        /// The hero: the menu open on Tasks, its waiting task chosen, and that task's file slid out beside
        /// it; without the file, the menu as it stands before the row is pressed.
        /// </summary>
        private static void RefinedHero(Shot shot, bool withFile = true, bool split = false)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var menuWidth = 2f * U(16f);
            var fileWidth = 2f * U(18f);
            var reserve = split ? StateBadgeView.Height * PillScale / 2f : 0f;
            var who = split ? shot.Characters[slot].View.Presentation : null;

            var fileHead = withFile ? SubjectShape(shot, "File subject", OpenedTitle, GlazeTokens.Text, fileWidth, null, reserve, who) : null;
            var menuHead = SubjectShape(shot, "Menu subject", "1 task is waiting for you", AmberText, menuWidth, fileHead?.Height, reserve);
            var menuTabs = SectionShapes(shot, "Places", Places, menuWidth, chosen: 0, waiting: 0);
            var fileTabs = withFile ? SectionShapes(shot, "File sections", FileSections, fileWidth, chosen: 0, waiting: 0) : null;

            // The menu's content: what waits first, then each project's tasks.
            var menu = shot.Board("Tasks", PlaneMeters);
            var ml = -menuWidth / 2f + U(PanelPadding);
            var mr = menuWidth / 2f - U(PanelPadding);
            var my = -U(PanelPadding);
            RefinedRow(menu, "Add rate limiting to the sign-in endpoint", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.WaitingForYou, cx, cy, AmberText),
                "Storefront API", AmberText, chosen: true, more: true);
            my -= U(RowHeight) + menu.TargetGap;
            RefinedRow(menu, "Paginate the order history endpoint", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.Working, cx, cy, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground)),
                "4 min", more: true);
            my -= U(RowHeight) + menu.TargetGap;
            RefinedRow(menu, "Send an order confirmation email", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.FinishedThisRound, cx, cy, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground)),
                "Finished", more: true);
            my -= U(RowHeight) + menu.TargetGap;
            RefinedRow(menu, "Refresh the checkout copy", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.CheckingItsWork, cx, cy, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground)),
                "Docs site", more: true);
            my -= U(RowHeight);

            var stacks = new List<IReadOnlyList<Board>> { new[] { menuHead, menuTabs, menu } };
            if (fileHead == null || fileTabs == null)
            {
                ContentShape(menu, menuWidth, -my + FooterRoom(menu) + U(GroupGap) * 0.5f, new Prompt("Close", GlazeIcon.Close), null, null, new Prompt("Next page", GlazeIcon.Next));
                LayOnPlane(shot, 0f, stacks);
                return;
            }

            // The file's content: the question and its answers, nothing else.
            var file = shot.Board("Waiting", PlaneMeters);
            var fl = -fileWidth / 2f + U(PanelPadding);
            var fr = fileWidth / 2f - U(PanelPadding);
            var fy = -U(PanelPadding);
            fy -= Body(file, "Question", "It asks: “How long should a sign-in lockout last?”", fl, fy, fr - fl, lines: 2, lean: true).Height + U(GroupGap);
            var inset = U(RowInset);
            var half = (fr - fl + 2f * inset - file.TargetGap) / 2f;
            var tall = U(RowHeight);
            foreach (var (answer, index) in new[] { ("15 minutes", 0), ("1 hour", 1) })
            {
                var shapeLeft = fl - inset + index * (half + file.TargetGap);
                var cx = shapeLeft + half / 2f;
                var chosen = index == 0;
                InnerShape(file, cx, fy - tall / 2f, half, tall, lit: chosen);
                Target(file, "Answer " + answer, cx, fy - tall / 2f, half, compact: true);
                Body(file, "Answer " + answer + " words", answer, shapeLeft + inset, fy - tall / 2f + U(BodySize) * 0.6f, half - U(5f), chosen ? GlazeTokens.Text : new Color(0.93f, 0.95f, 0.97f, 0.9f), strong: chosen);
                if (chosen) Fact(file, "Chosen", "Chosen", shapeLeft + half - inset, fy - tall / 2f + U(LabelSize) * 0.6f, U(5f), GlazeTokens.Text, TextAlignmentOptions.TopRight);
            }
            fy -= tall + file.TargetGap;
            InnerShape(file, (fl + fr) / 2f, fy - tall / 2f, fr - fl + 2f * inset, tall);
            Target(file, "Type my own answer", (fl + fr) / 2f, fy - tall / 2f, fr - fl + 2f * inset, compact: true);
            Glyph(file, "Type", GlazeIcon.Type, fl + U(IconColumn) / 2f, fy - tall / 2f, GlazeTokens.Text, 1.1f);
            Body(file, "Type words", "Type my own answer", fl + U(IconColumn) + U(Grid), fy - tall / 2f + U(BodySize) * 0.6f, fr - fl - U(IconColumn) - U(Grid), new Color(0.93f, 0.95f, 0.97f, 0.9f));
            fy -= tall;

            // Both content shapes end on one line, their footers on it.
            var bodyHeight = Mathf.Max(-my, -fy) + FooterRoom(menu) + U(GroupGap) * 0.5f;
            ContentShape(menu, menuWidth, bodyHeight, new Prompt("Close", GlazeIcon.Close), null, null, new Prompt("Next page", GlazeIcon.Next));
            ContentShape(file, fileWidth, bodyHeight, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Hold to talk", GlazeIcon.HoldToTalk),
                new Prompt("Send answer", GlazeIcon.SendAnswer, main: true));

            stacks.Add(new[] { fileHead, fileTabs, file });
            LayOnPlane(shot, 0f, stacks);
            Projection(shot, slot, fileHead, fileHead, reserve);
        }

        /// <summary>The approval: the task's file alone, upright under its task, Approve pointed at.</summary>
        private static void RefinedApproval(Shot shot, bool split = false)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var width = 2f * U(19f);
            var head = SubjectShape(shot, "File subject", OpenedTitle, GlazeTokens.Text, width, null, split ? StateBadgeView.Height * PillScale / 2f : 0f,
                split ? shot.Characters[slot].View.Presentation : null);
            var tabs = SectionShapes(shot, "File sections", FileSections, width, chosen: 0, waiting: 0);
            var file = shot.Board("Waiting", PlaneMeters);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var y = -U(PanelPadding);
            y -= Body(file, "Wants", "It wants to run a command:", left, y, right - left).Height + U(Grid);
            var well = U(RowHeight);
            Plate(file, "Command", (left + right) / 2f, y - well / 2f, right - left + 2f * U(RowInset), well, U(ShapeRadius), GlazeTokens.ColorOf(Glaze.Well, 0.95f), new Color(1f, 1f, 1f, 0.08f), U(0.05f), 52);
            ProtoGlyph(file, "Command icon", "", left + U(IconColumn) / 2f, y - well / 2f, Secondary, 1.1f);
            Body(file, "Command words", "make migrate", left + U(IconColumn) + U(Grid), y - well / 2f + U(BodySize) * 0.6f, right - left - U(IconColumn) - U(1f));
            y -= well + U(GroupGap);
            // What each answer does is the content, at its size: type never grows toward the prompts.
            y -= Body(file, "Consequence", "Approve lets it go ahead. Deny refuses; it may try another way.", left, y, right - left, Secondary, lines: 2).Height;
            var height = -y + U(GroupGap) + FooterRoom(file);
            ContentShape(file, width, height, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Deny", GlazeIcon.Deny),
                new Prompt("Approve", GlazeIcon.Approve, main: true, pointedAt: true));
            LayOnPlane(shot, CardYaw(shot, slot, 20f, 0f), new List<IReadOnlyList<Board>> { new[] { head, tabs, file } });
            Projection(shot, slot, head, head, split ? StateBadgeView.Height * PillScale / 2f : 0f);
        }

        // ---------------------------------------------------------------------------------------------
        // Renders. Level ones keep an upright image plane shifted down, as architecture is photographed,
        // so upright surfaces stay rectangles: that flatters an upright plane below the eyes, whose low
        // parts the eyes see smaller. The eye's own view aims where the person looks, at the
        // composition's centre, with the image plane square to that line, as the headset draws it then.

        private static Texture2D CaptureFrustum(Vector3 eyes, Transform parent, Quaternion look, float left, float right, float bottom, float top, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, look);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            var near = camera.nearClipPlane;
            camera.projectionMatrix = Matrix4x4.Frustum(left * near, right * near, bottom * near, top * near, near, camera.farClipPlane);
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

        /// <summary>The person's view of the stage and the plane, turned to face the plane: from 14 degrees above eye level to 48 below, upright.</summary>
        private static Texture2D WideUpright(Vector3 eyes, Transform parent)
        {
            var top = Mathf.Tan(14f * Mathf.Deg2Rad);
            var bottom = -Mathf.Tan(48f * Mathf.Deg2Rad);
            var half = (top - bottom) * WideWidth / WideHeight / 2f;
            var yaw = columns.Count == 0 ? 0f : columns[0][0].Root.eulerAngles.y;
            return CaptureFrustum(eyes, parent, Quaternion.Euler(0f, yaw, 0f), -half, half, bottom, top, WideWidth, WideHeight);
        }

        /// <summary>The plane's composition straight on, at a Quest 3's pixels per degree near its middle, with a margin round it.</summary>
        private static Texture2D CloseUpUpright(Shot shot)
        {
            var parts = columns.SelectMany(column => column).ToList();
            var first = parts[0].Root;
            var yaw = first.eulerAngles.y;
            var forward = first.forward;
            var right = first.right;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var part in parts)
            {
                foreach (var x in new[] { -0.5f, 0.5f })
                {
                    foreach (var y in new[] { -0.5f, 0.5f })
                    {
                        var point = part.Root.TransformPoint(new Vector3(x * part.Width, y * part.Height, 0f)) - shot.Eyes;
                        var depth = Vector3.Dot(point, forward);
                        minX = Mathf.Min(minX, Vector3.Dot(point, right) / depth);
                        maxX = Mathf.Max(maxX, Vector3.Dot(point, right) / depth);
                        minY = Mathf.Min(minY, point.y / depth);
                        maxY = Mathf.Max(maxY, point.y / depth);
                    }
                }
            }
            var margin = Mathf.Tan(2.5f * Mathf.Deg2Rad);
            minX -= margin; maxX += margin; minY -= margin; maxY += margin;
            var pixelsPerUnit = PixelsPerDegree * Mathf.Rad2Deg;
            var width = Mathf.Min(2400, Mathf.RoundToInt((maxX - minX) * pixelsPerUnit));
            var height = Mathf.RoundToInt(width * (maxY - minY) / (maxX - minX));
            return CaptureFrustum(shot.Eyes, shot.Root, Quaternion.Euler(0f, yaw, 0f), minX, maxX, minY, maxY, width, height);
        }

        /// <summary>
        /// What the eyes see looking at the composition: aimed at its centre, square to that line. Wide,
        /// the stage too; close, the composition with a margin, at a Quest 3's pixels per degree there.
        /// </summary>
        private static Texture2D EyeView(Shot shot, bool wide) => AimedView(shot, columns.SelectMany(column => column).ToList(), wide);

        /// <summary>The eyes' view aimed at the middle of <paramref name="parts"/>, square to that line; wide, the stage too.</summary>
        private static Texture2D AimedView(Shot shot, IReadOnlyList<Board> parts, bool wide)
        {
            var look = Quaternion.LookRotation(CompositionCenter(parts) - shot.Eyes, Vector3.up);
            var inverse = Quaternion.Inverse(look);
            var points = parts.SelectMany(part => new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                .Select(y => part.Root.TransformPoint(new Vector3(x * part.Width, y * part.Height, 0f))))).ToList();
            if (wide)
            {
                foreach (var character in shot.Characters)
                {
                    foreach (var renderer in character.View.GetComponentsInChildren<Renderer>(false))
                    {
                        var bounds = renderer.bounds;
                        points.Add(bounds.min);
                        points.Add(bounds.max);
                        points.Add(new Vector3(bounds.min.x, bounds.max.y, bounds.min.z));
                        points.Add(new Vector3(bounds.max.x, bounds.min.y, bounds.max.z));
                    }
                }
            }
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var point in points)
            {
                var local = inverse * (point - shot.Eyes);
                if (local.z <= 0.05f) continue;
                minX = Mathf.Min(minX, local.x / local.z);
                maxX = Mathf.Max(maxX, local.x / local.z);
                minY = Mathf.Min(minY, local.y / local.z);
                maxY = Mathf.Max(maxY, local.y / local.z);
            }
            var margin = Mathf.Tan((wide ? 2f : 2.5f) * Mathf.Deg2Rad);
            minX -= margin; maxX += margin; minY -= margin; maxY += margin;
            var width = wide ? WideWidth : Mathf.Min(2400, Mathf.RoundToInt((maxX - minX) * PixelsPerDegree * Mathf.Rad2Deg));
            var height = Mathf.RoundToInt(width * (maxY - minY) / (maxX - minX));
            return CaptureFrustum(shot.Eyes, shot.Root, look, minX, maxX, minY, maxY, width, height);
        }

        /// <summary>
        /// Text as the eyes see it, for <paramref name="parts"/>: the angle each label's em spans from the
        /// eyes, which shrinks where a surface is seen at a slant, never under Meta's 14 dp.
        /// </summary>
        private static IEnumerable<string> SeenTextFailures(Shot shot, IEnumerable<Board> parts)
        {
            var seen = parts.SelectMany(part => part.Root.GetComponentsInChildren<TMP_Text>(false))
                .Where(label => !string.IsNullOrEmpty(label.text) && !GlazeIcons.IsIcon(label) && (protoIcons == null || label.font != protoIcons) && label.textInfo.characterCount > 0)
                .Select(label =>
                {
                    var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                    var middle = label.transform.TransformPoint(label.textBounds.center);
                    var half = label.transform.up * (em / 2f);
                    var angle = Vector3.Angle(middle + half - shot.Eyes, middle - half - shot.Eyes);
                    return (Name: label.name, Seen: angle, Share: angle / Glaze.DegreesOf(em, GlazeChecks.PlaneDistance(shot.Eyes, label.transform)));
                })
                .OrderBy(label => label.Seen)
                .ToList();
            foreach (var label in seen.Where(label => label.Seen < Glaze.MinimumTextDegrees - 0.0005f))
            {
                yield return shot.Name + ": " + label.Name + " is " + GlazeChecks.Degrees(label.Seen) + " degrees as the eyes see it (" + Mathf.RoundToInt(label.Share * 100f)
                    + " percent of its size); no text under 14 dp, " + Glaze.MinimumTextDegrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees.";
            }
            if (seen.Count > 0)
            {
                var least = seen.OrderBy(label => label.Share).First();
                Debug.Log("Halcyonic: directions render " + shot.Name + ": as the eyes see it, the smallest text is " + seen[0].Name + " at " + seen[0].Seen.ToString("0.00", CultureInfo.InvariantCulture)
                    + " degrees; the most shrunk is " + least.Name + " at " + Mathf.RoundToInt(least.Share * 100f) + " percent of its size.");
            }
        }

        // ---------------------------------------------------------------------------------------------
        // The plane's own checks: one plane, upright or facing the eyes at its centre, never rolled; parts
        // apart on it and columns aligned; one selection treatment; type that only steps down in each
        // column; text as the eyes see it.

        private static IEnumerable<string> PlaneChecks(Shot shot)
        {
            if (columns.Count == 0 || !shot.Name.StartsWith("r", StringComparison.Ordinal)) yield break;
            var parts = columns.SelectMany(column => column).ToList();
            var plane = parts[0].Root;
            foreach (var part in parts)
            {
                if (Vector3.Angle(part.Root.forward, plane.forward) > 0.05f) yield return shot.Name + ": " + part.Name + " is turned against " + parts[0].Name + "; every surface shares one plane.";
                var offPlane = Vector3.Dot(part.Root.position - plane.position, plane.forward);
                if (Mathf.Abs(offPlane) > 0.0005f) yield return shot.Name + ": " + part.Name + " stands " + (offPlane * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm off the plane.";
                if (Mathf.Abs(part.Root.right.y) > 0.001f) yield return shot.Name + ": " + part.Name + " is rolled; its rows run level.";
            }
            if (facing == Facing.Upright)
            {
                var pitch = Mathf.Asin(Mathf.Clamp(plane.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(pitch) > 0.05f) yield return shot.Name + ": the plane is pitched " + GlazeChecks.Degrees(pitch) + " degrees; it stands upright.";
            }
            else
            {
                var off = Vector3.Angle(plane.forward, CompositionCenter(parts) - shot.Eyes);
                if (off > 0.5f) yield return shot.Name + ": the plane is " + GlazeChecks.Degrees(off) + " degrees off square to the eyes at its centre; it faces them there.";
            }
            // No two parts closer than the rows' gap, measured on the plane.
            for (var a = 0; a < parts.Count; a++)
            {
                for (var b = a + 1; b < parts.Count; b++)
                {
                    var offset = parts[b].Root.position - parts[a].Root.position;
                    var across = Mathf.Abs(Vector3.Dot(offset, plane.right)) - (parts[a].Width + parts[b].Width) / 2f * PlaneMeters;
                    var down = Mathf.Abs(Vector3.Dot(offset, plane.up)) - (parts[a].Height + parts[b].Height) / 2f * PlaneMeters;
                    var gap = across > 0f && down > 0f ? Mathf.Sqrt(across * across + down * down) : Mathf.Max(across, down);
                    if (gap < U(RowGap) * PlaneMeters * 0.98f)
                    {
                        yield return shot.Name + ": " + parts[a].Name + " and " + parts[b].Name + " are " + (gap * 1000f).ToString("0.0", CultureInfo.InvariantCulture)
                            + " mm apart on the plane; parts keep " + (U(RowGap) * PlaneMeters * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + ".";
                    }
                }
            }
            // Edges aligned: the columns start on one line and end on one line.
            var tops = columns.Select(column => Vector3.Dot(column[0].Root.position - plane.position, plane.up) + column[0].Height / 2f * PlaneMeters).ToList();
            var bottoms = columns.Select(column => Vector3.Dot(column[column.Count - 1].Root.position - plane.position, plane.up) - column[column.Count - 1].Height / 2f * PlaneMeters).ToList();
            if (tops.Max() - tops.Min() > 0.0005f) yield return shot.Name + ": the columns' tops are " + ((tops.Max() - tops.Min()) * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart; they start on one line.";
            if (bottoms.Max() - bottoms.Min() > 0.0005f) yield return shot.Name + ": the columns' bottoms are " + ((bottoms.Max() - bottoms.Min()) * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart; they end on one line.";
            // One selection treatment: what's chosen lights its own shape and what's pointed at gains a
            // fainter frame; the accent marks only the main action's cap, and no bar or underline marks anything.
            foreach (var part in parts)
            {
                foreach (var plate in part.Plates)
                {
                    var fill = plate.Fill;
                    if (plate.name == "Lit" && fill != LitFill) yield return shot.Name + ": a lit shape in " + part.Name + " is filled otherwise; there is one selection treatment.";
                    if (plate.name == "Pointed" && fill.a > 0f) yield return shot.Name + ": a pointed frame in " + part.Name + " is filled; pointed at is the frame alone.";
                    var accent = Mathf.Abs(fill.r - Accent.r) + Mathf.Abs(fill.g - Accent.g) + Mathf.Abs(fill.b - Accent.b) < 0.05f && fill.a > 0.3f;
                    if (accent && plate.name != "Cap") yield return shot.Name + ": " + plate.name + " in " + part.Name + " is in the accent, which marks only the main action's cap.";
                    if (Mathf.Min(plate.Size.x, plate.Size.y) < U(0.2f) && fill.a > 0.2f) yield return shot.Name + ": " + plate.name + " in " + part.Name + " is a bar; nothing is marked by a bar or an underline.";
                }
            }
            // Inside a Quest 3S's field as the product's FieldChecks see it: the head level and turned to the
            // composition's centre, tipped down only by ReadingPitch for a composition taller than designed.
            var centre = CompositionCenter(parts);
            var corners = parts.SelectMany(part => new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                .Select(y => part.Root.TransformPoint(new Vector3(x * part.Width, y * part.Height, 0f))))).ToList();
            var sideways = corners.Select(corner => Vector3.Dot(corner - centre, plane.right)).ToList();
            var upward = corners.Select(corner => Vector3.Dot(corner - centre, plane.up)).ToList();
            var size = new PanelSize((centre - shot.Eyes).magnitude, (sideways.Max() - sideways.Min()) / 2f, (upward.Max() - upward.Min()) / 2f);
            foreach (var failure in FieldChecks.Inside(shot.Name + " composition", corners, shot.Eyes, centre, WorkspacePlacement.ReadingPitch(size), FieldChecks.Quest3S))
            {
                yield return failure;
            }
            foreach (var failure in SeenTextFailures(shot, parts)) yield return failure;
            foreach (var column in columns)
            {
                // Rows of text from the top: a row is the labels whose heights overlap; its size, its largest.
                // The split header's state pill reads with the subject it stands on: the one exception to
                // type only stepping down (ADR 0026).
                var labels = column.SelectMany(part => part.Root.GetComponentsInChildren<TMP_Text>(false))
                    .Where(label => !string.IsNullOrEmpty(label.text) && !GlazeIcons.IsIcon(label) && (protoIcons == null || label.font != protoIcons)
                        && label.textInfo.characterCount > 0 && label.GetComponentInParent<StateBadgeView>() == null)
                    .Select(label =>
                    {
                        var bounds = label.textBounds;
                        var top = label.transform.TransformPoint(new Vector3(0f, bounds.max.y, 0f)).y;
                        var bottom = label.transform.TransformPoint(new Vector3(0f, bounds.min.y, 0f)).y;
                        var size = Glaze.DegreesOf(label.fontSize * 0.1f * label.transform.lossyScale.y, GlazeChecks.PlaneDistance(shot.Eyes, label.transform));
                        return (Name: label.name, Top: top, Bottom: bottom, Size: size);
                    })
                    .OrderByDescending(label => label.Top)
                    .ToList();
                var rows = new List<(float Top, float Bottom, float Size, string Name)>();
                foreach (var label in labels)
                {
                    var row = rows.FindIndex(existing => label.Top > existing.Bottom && label.Bottom < existing.Top);
                    if (row < 0) rows.Add((label.Top, label.Bottom, label.Size, label.Name));
                    else
                    {
                        var existing = rows[row];
                        rows[row] = (Mathf.Max(existing.Top, label.Top), Mathf.Min(existing.Bottom, label.Bottom), Mathf.Max(existing.Size, label.Size),
                            existing.Size >= label.Size ? existing.Name : label.Name);
                    }
                }
                for (var index = 1; index < rows.Count; index++)
                {
                    if (rows[index].Size > rows[index - 1].Size + 0.01f)
                    {
                        yield return shot.Name + ": " + rows[index].Name + " (" + GlazeChecks.Degrees(rows[index].Size) + " degrees) stands under the smaller "
                            + rows[index - 1].Name + " (" + GlazeChecks.Degrees(rows[index - 1].Size) + "); type only steps down.";
                    }
                }
                Debug.Log("Halcyonic: directions render " + shot.Name + ": type from the top, " + string.Join(", ", rows.Select(row => GlazeChecks.Degrees(row.Size))) + ".");
            }
        }
    }
}
