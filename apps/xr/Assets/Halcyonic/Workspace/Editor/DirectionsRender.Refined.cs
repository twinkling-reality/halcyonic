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
    /// D, refined (the owner's notes of 2026-10-02 on D, styled). Every foreground surface stands on
    /// one upright plane facing the person, no pitch and none angled against another, edges aligned;
    /// detail slides out to the right along the same plane. Each part is its own rounded shape: the
    /// subject on top, the sections as separate shapes in a row under it, the content under them. Type
    /// only steps down: the subject largest, then the content, then small facts and the prompts. One
    /// selection treatment: a lit shape with a crisp frame. The places are single words.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> RefinedShots() => new (string, bool, Action<Shot>)[]
        {
            ("r1-arriving-tasks-file", false, shot => RefinedHero(shot)),
            ("r2-waiting-approve", false, shot => RefinedApproval(shot)),
        };

        /// <summary>The plane's horizontal distance from the eyes; the content's middle is about 0.46 m away.</summary>
        private const float PlaneMeters = 0.40f;

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

        private static Board SubjectShape(Shot shot, string name, string subject, Color colour, float width, CharacterPresentation? who, float? height = null)
        {
            var board = shot.Board(name, PlaneMeters);
            board.Width = width;
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            StateBadgeView? badge = null;
            var badgeWidth = 0f;
            if (who != null)
            {
                badge = StateBadgeView.Create(board.Content, "Badge", 58);
                badge.Show(StateLanguage.BadgeOf(who));
                badgeWidth = badge.Width + U(GroupGap);
            }
            // The subject whole, on two lines where one won't hold it.
            var (title, titleHeight) = Text(board, "Title", subject, GlazeType.Display, colour, left, 0f, right - left - badgeWidth, 2, strong: false);
            if (lightMaterial == null)
            {
                lightMaterial = new Material(title.fontSharedMaterial) { name = "Glaze light" };
                lightMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, -0.12f);
                ShaderUtilities.UpdateShaderRatios(lightMaterial);
            }
            title.fontSharedMaterial = lightMaterial;
            var line = GlazeText.LineHeight(title);
            board.Height = height ?? Mathf.Max(U(RowA), titleHeight + 2f * U(0.75f));
            var top = -(board.Height - titleHeight) / 2f;
            title.transform.localPosition = new Vector3(left, top, -U(0.05f));
            if (badge != null) badge.transform.localPosition = new Vector3(right - badge.Width / 2f, top - line / 2f, -U(0.05f));
            Shape(board, 0f, -board.Height / 2f, width, board.Height, sheen: true);
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

        /// <summary>The prompts in the content shape's last row: Close far left, the main action far right; the small size.</summary>
        private static void RefinedFooter(Board board, float left, float right, float middle, Prompt? farLeft, Prompt? nearLeft, Prompt? nearRight, Prompt? farRight)
        {
            float WidthOfPrompt(Prompt prompt) => U(1.3f) + U(Grid) + WidthOf(board, prompt.Words, GlazeType.Caption) * (prompt.Main ? 1.05f : 1f) + U(1.2f);
            void Draw(Prompt prompt, float x)
            {
                var width = WidthOfPrompt(prompt);
                Target(board, "Prompt " + prompt.Words, x + width / 2f, middle, width, compact: false);
                if (prompt.PointedAt) Select(board, x + width / 2f, middle, width, U(GlazeButton.HeightDegrees) - U(0.4f), lit: false);
                var cap = U(1.3f);
                var capX = x + U(0.6f) + cap / 2f;
                if (prompt.Main) Plate(board, "Cap", capX, middle, cap, cap, cap / 2f, Accent, order: 62, depth: -U(0.04f));
                else Plate(board, "Cap", capX, middle, cap, cap, cap / 2f, Color.clear, new Color(1f, 1f, 1f, 0.6f), U(0.07f), 62, -U(0.04f));
                Glyph(board, "Cap icon", prompt.Icon, capX, middle, prompt.Main ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).OnStrong) : GlazeTokens.Text, 0.95f);
                var (words, _) = Fact(board, "Prompt words " + prompt.Words, prompt.Words, capX + cap / 2f + U(Grid), middle + U(LabelSize) * 0.6f, U(20f),
                    prompt.Main ? AccentText : GlazeTokens.Text);
                if (prompt.Main) words.fontSharedMaterial = Text(board, "Strong probe", "", GlazeType.Caption, GlazeTokens.Text, 0f, 0f, U(1f), strong: true).Label.fontSharedMaterial;
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
        // The plane: one upright plane facing the person, every part placed on it by its top-left corner.

        private static void OnPlane(Board board, Vector3 eyes, float yaw, float left, float top)
        {
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            var origin = eyes + forward * PlaneMeters;
            board.Content.localPosition = new Vector3(0f, board.Height / 2f, 0f);
            var center = origin + right * ((left + board.Width / 2f) * PlaneMeters) + Vector3.up * ((top - board.Height / 2f) * PlaneMeters);
            board.Root.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>
        /// Lays columns on the plane, each a list of parts stacked from one top line with the rows'
        /// gaps between them, the columns side by side 15 mm apart and the whole centred on
        /// <paramref name="yaw"/>; then lowers the top line until every part keeps a degree and a little
        /// from every label, by its outline.
        /// </summary>
        private static void LayOnPlane(Shot shot, float yaw, IReadOnlyList<IReadOnlyList<Board>> stacks)
        {
            var gap = 0.015f / PlaneMeters;
            var total = stacks.Sum(stack => stack[0].Width) + gap * (stacks.Count - 1);
            var top = -Mathf.Tan(16.5f * Mathf.Deg2Rad);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var x = -total / 2f;
                foreach (var stack in stacks)
                {
                    var y = top;
                    foreach (var part in stack)
                    {
                        OnPlane(part, shot.Eyes, yaw, x, y);
                        y -= part.Height + U(RowGap);
                    }
                    x += stack[0].Width + gap;
                }
                var labels = shot.Characters.Select(character => GlazeChecks.Of("label", shot.Eyes, character.View.Label.gameObject)).ToList();
                var least = stacks.SelectMany(stack => stack).Min(part => labels.Min(label => OutlineApart(Outline(part, shot.Eyes), label)));
                if (least >= 1.15f) break;
                top -= Mathf.Tan(0.25f * Mathf.Deg2Rad);
            }
            columns.Clear();
            columns.AddRange(stacks.Select(stack => stack.ToList()));
        }

        // ---------------------------------------------------------------------------------------------
        // The two frames.

        /// <summary>The hero: the menu open on Tasks, its waiting task chosen, and that task's file slid out beside it.</summary>
        private static void RefinedHero(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var menuWidth = 2f * U(16f);
            var fileWidth = 2f * U(18f);

            var fileHead = SubjectShape(shot, "File subject", OpenedTitle, GlazeTokens.Text, fileWidth, who);
            var menuHead = SubjectShape(shot, "Menu subject", "1 task is waiting for you", AmberText, menuWidth, null, fileHead.Height);
            var menuTabs = SectionShapes(shot, "Places", Places, menuWidth, chosen: 0, waiting: 0);
            var fileTabs = SectionShapes(shot, "File sections", FileSections, fileWidth, chosen: 0, waiting: 0);

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
                Shape(file, cx, fy - tall / 2f, half, tall, lit: chosen);
                Target(file, "Answer " + answer, cx, fy - tall / 2f, half, compact: true);
                Body(file, "Answer " + answer + " words", answer, shapeLeft + inset, fy - tall / 2f + U(BodySize) * 0.6f, half - U(5f), chosen ? GlazeTokens.Text : new Color(0.93f, 0.95f, 0.97f, 0.9f), strong: chosen);
                if (chosen) Fact(file, "Chosen", "Chosen", shapeLeft + half - inset, fy - tall / 2f + U(LabelSize) * 0.6f, U(5f), GlazeTokens.Text, TextAlignmentOptions.TopRight);
            }
            fy -= tall + file.TargetGap;
            Shape(file, (fl + fr) / 2f, fy - tall / 2f, fr - fl + 2f * inset, tall);
            Target(file, "Type my own answer", (fl + fr) / 2f, fy - tall / 2f, fr - fl + 2f * inset, compact: true);
            Glyph(file, "Type", GlazeIcon.Type, fl + U(IconColumn) / 2f, fy - tall / 2f, GlazeTokens.Text, 1.1f);
            Body(file, "Type words", "Type my own answer", fl + U(IconColumn) + U(Grid), fy - tall / 2f + U(BodySize) * 0.6f, fr - fl - U(IconColumn) - U(Grid), new Color(0.93f, 0.95f, 0.97f, 0.9f));
            fy -= tall;

            // Both content shapes end on one line, their footers on it.
            var bodyHeight = Mathf.Max(-my, -fy) + FooterRoom(menu) + U(GroupGap) * 0.5f;
            ContentShape(menu, menuWidth, bodyHeight, new Prompt("Close", GlazeIcon.Close), null, null, new Prompt("Next page", GlazeIcon.Next));
            ContentShape(file, fileWidth, bodyHeight, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Hold to talk", GlazeIcon.HoldToTalk),
                new Prompt("Send answer", GlazeIcon.SendAnswer, main: true));

            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { menuHead, menuTabs, menu }, new[] { fileHead, fileTabs, file } });
            Projection(shot, slot, fileHead, fileHead);
        }

        /// <summary>The approval: the task's file alone, upright under its task, Approve pointed at.</summary>
        private static void RefinedApproval(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var width = 2f * U(19f);
            var head = SubjectShape(shot, "File subject", OpenedTitle, GlazeTokens.Text, width, who);
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
            y -= Fact(file, "Consequence", "Approve lets it go ahead. Deny refuses; it may try another way.", left, y, right - left).Height;
            var height = -y + U(GroupGap) + FooterRoom(file);
            ContentShape(file, width, height, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Deny", GlazeIcon.Deny),
                new Prompt("Approve", GlazeIcon.Approve, main: true, pointedAt: true));
            LayOnPlane(shot, CardYaw(shot, slot, 20f, 0f), new List<IReadOnlyList<Board>> { new[] { head, tabs, file } });
            Projection(shot, slot, head, head);
        }

        // ---------------------------------------------------------------------------------------------
        // Renders of an upright plane: an upright image plane shifted down (as architecture is
        // photographed), so upright surfaces stay rectangles, as they look in the headset with depth.

        private static Texture2D CaptureUpright(Vector3 eyes, Transform parent, float yaw, float left, float right, float bottom, float top, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.Euler(0f, yaw, 0f));
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
            return CaptureUpright(eyes, parent, yaw, -half, half, bottom, top, WideWidth, WideHeight);
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
            return CaptureUpright(shot.Eyes, shot.Root, yaw, minX, maxX, minY, maxY, width, height);
        }

        // ---------------------------------------------------------------------------------------------
        // The plane's own checks: one plane, upright; type that only steps down in each column.

        private static IEnumerable<string> PlaneChecks(Shot shot)
        {
            if (columns.Count == 0 || !shot.Name.StartsWith("r", StringComparison.Ordinal)) yield break;
            var parts = columns.SelectMany(column => column).ToList();
            var first = parts[0].Root;
            foreach (var part in parts)
            {
                var forward = part.Root.forward;
                var pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(pitch) > 0.05f) yield return shot.Name + ": " + part.Name + " is pitched " + GlazeChecks.Degrees(pitch) + " degrees; every surface stands upright.";
                if (Vector3.Angle(forward, first.forward) > 0.05f) yield return shot.Name + ": " + part.Name + " is turned against " + parts[0].Name + "; every surface shares one plane.";
                var offPlane = Vector3.Dot(part.Root.position - first.position, first.forward);
                if (Mathf.Abs(offPlane) > 0.0005f) yield return shot.Name + ": " + part.Name + " stands " + (offPlane * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm off the plane.";
            }
            for (var a = 0; a < columns.Count; a++)
            {
                for (var index = 1; index < columns[a].Count; index++)
                {
                    var above = columns[a][index - 1];
                    var below = columns[a][index];
                    var gap = (above.Root.position.y - above.Height / 2f * PlaneMeters) - (below.Root.position.y + below.Height / 2f * PlaneMeters);
                    if (gap < U(RowGap) * PlaneMeters * 0.98f) yield return shot.Name + ": " + above.Name + " and " + below.Name + " are " + (gap * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart on the plane.";
                }
            }
            foreach (var column in columns)
            {
                // Rows of text from the top: a row is the labels whose heights overlap; its size, its largest.
                var labels = column.SelectMany(part => part.Root.GetComponentsInChildren<TMP_Text>(false))
                    .Where(label => !string.IsNullOrEmpty(label.text) && !GlazeIcons.IsIcon(label) && (protoIcons == null || label.font != protoIcons)
                        && label.textInfo.characterCount > 0)
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
