#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// D, styled (the owner's choice of 2026-10-02 and their changes): one strict system. Three type
    /// roles only: Title 24 dp light, Body 18 dp, Label 15 dp in tracked capitals by presentation (with
    /// small facts in the Label size, sentence case). An 8 dp grid: 24 dp panel padding, 16 dp between
    /// groups, 8 dp from a label to its content, 12 mm between targets, 48 dp rows. One left content
    /// line, values to the right content line, icons in a 24 dp column. Glass panels with a gradient,
    /// a sheen and corner ticks, no blur. Prompts as a game shows them: a key cap with the action's icon,
    /// then its words, no plate, Close far left and the main action far right. A line with more opens a
    /// connected side panel. The file opens out of its character with a light line.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> StyledShots() => new (string, bool, Action<Shot>)[]
        {
            ("s1-arriving-tasks", false, shot => StyledTasks(shot)),
            ("s2-waiting-approve", false, shot => StyledApproval(shot)),
            ("s3-understanding-changed", false, shot => StyledChanged(shot)),
        };

        // The type scale: three sizes, in degrees at the eye (1 dp = 0.0625 degrees).
        private const float TitleSize = Glaze.DisplayDegrees;   // 24 dp
        private const float BodySize = Glaze.BodyDegrees;       // 18 dp
        private const float LabelSize = Glaze.CaptionDegrees;   // 15 dp

        // The grid: 8 dp steps.
        private const float Grid = 0.5f;                        // 8 dp
        private const float PanelPadding = 3 * Grid;            // 24 dp
        private const float GroupGap = 2 * Grid;                // 16 dp
        private const float LabelGap = Grid;                    // 8 dp
        private const float RowHeight = Glaze.MinimumTargetDegrees; // 48 dp
        private const float IconColumn = 3 * Grid;              // 24 dp

        private const float GlassOpacity = 0.88f;
        private const float GlassRadius = 0.6f;

        private static Color Secondary => GlazeTokens.TextSecondary;

        private static Color AccentText => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).Foreground);

        private static Color AmberText => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground);

        private static Material? lightMaterial;
        private static TMP_FontAsset? protoIcons;

        /// <summary>
        /// A temporary font of the file-type glyphs, Material Symbols Rounded (Apache-2.0) in the Glaze
        /// style, cut for these renders only and never committed; lane U adds them to the atlas.
        /// </summary>
        private static TMP_FontAsset? ProtoIcons()
        {
            if (protoIcons != null) return protoIcons;
            var path = Environment.GetEnvironmentVariable("HALCYONIC_PROTO_ICONS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            protoIcons = TMP_FontAsset.CreateFontAsset(path, 0, 56, 7, GlyphRenderMode.SDFAA, 512, 512);
            return protoIcons;
        }

        private const string GlyphCode = "";
        private const string GlyphDatabase = "";
        private const string GlyphDescription = "";
        private const string GlyphChevron = "";
        private const string GlyphFolder = "";

        // ---------------------------------------------------------------------------------------------
        // Type: the three roles, and nothing else.

        private static (TextMeshPro Label, float Height) Title(Board board, string text, float left, float top, float width)
        {
            var (label, height) = Text(board, "Title", text, GlazeType.Display, GlazeTokens.Text, left, top, width, strong: false);
            if (lightMaterial == null)
            {
                lightMaterial = new Material(label.fontSharedMaterial) { name = "Glaze light" };
                lightMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, -0.12f);
                ShaderUtilities.UpdateShaderRatios(lightMaterial);
            }
            label.fontSharedMaterial = lightMaterial;
            return (label, height);
        }

        private static (TextMeshPro Label, float Height) Body(Board board, string name, string text, float left, float top, float width, Color? colour = null,
            int lines = 1, bool lean = false, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, bool strong = false) =>
            Text(board, name, text, GlazeType.Body, colour ?? GlazeTokens.Text, left, top, width, lines, alignment, lean, strong: strong);

        /// <summary>A label: the small size in tracked capitals, for section names, group names and the names of facts.</summary>
        private static (TextMeshPro Label, float Height) Caps(Board board, string name, string text, float left, float top, float width, Color? colour = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, bool strong = false)
        {
            var (label, height) = Text(board, name, text, GlazeType.Caption, colour ?? Secondary, left, top, width, 1, alignment, strong: strong);
            label.fontStyle |= FontStyles.UpperCase;
            label.characterSpacing = 8f;
            label.ForceMeshUpdate(true);
            return (label, height);
        }

        /// <summary>A small fact: the small size in sentence case, as a time or a source.</summary>
        private static (TextMeshPro Label, float Height) Fact(Board board, string name, string text, float left, float top, float width, Color? colour = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) =>
            Text(board, name, text, GlazeType.Caption, colour ?? Secondary, left, top, width, 1, alignment);

        private static float CapsWidth(Board board, string text)
        {
            var (probe, _) = Caps(board, "Measure", text, 0f, 0f, U(40f));
            var width = probe.GetPreferredValues(probe.text).x * 1.18f;
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            return width;
        }

        private static TextMeshPro Glyph(Board board, string name, GlazeIcon icon, float centerX, float centerY, Color colour, float degrees = 1.2f)
        {
            var glyph = GlazeIcons.Create(board.Content, name, degrees, colour, 63);
            GlazeIcons.Show(glyph, icon);
            glyph.transform.localPosition = new Vector3(centerX, centerY, -U(0.06f));
            return glyph;
        }

        /// <summary>A glyph from the temporary file-type font; nothing when the font was not given.</summary>
        private static void ProtoGlyph(Board board, string name, string glyph, float centerX, float centerY, Color colour, float degrees = 1.2f)
        {
            var font = ProtoIcons();
            if (font == null) return;
            var label = GlazeIcons.Create(board.Content, name, degrees, colour, 63);
            label.font = font;
            label.text = glyph;
            label.transform.localPosition = new Vector3(centerX, centerY, -U(0.06f));
            label.ForceMeshUpdate(true);
        }

        // ---------------------------------------------------------------------------------------------
        // Glass: the panel colour, a little transparent, a gradient from the top, a sheen line, a
        // hairline edge and corner ticks. One plate and three thin strokes; no blur.

        private static Texture2D? gradient;

        private static void Glass(Board board, float width, float height, bool connected = false)
        {
            board.Height = height;
            board.Width = width;
            var cy = -height / 2f;
            Plate(board, board.Name, 0f, cy, width, height, U(GlassRadius), GlazeTokens.ColorOf(Glaze.Panel, GlassOpacity), new Color(1f, 1f, 1f, 0.12f), U(0.06f), 48);
            // The light from the top: a gradient over the upper part of the plate.
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
            var sheen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheen.name = "Glass light";
            UnityEngine.Object.DestroyImmediate(sheen.GetComponent<Collider>());
            sheen.transform.SetParent(board.Content, false);
            var inset = U(GlassRadius) * 0.35f;
            sheen.transform.localPosition = new Vector3(0f, -U(0.04f) - (height * 0.55f) / 2f, -U(0.005f));
            sheen.transform.localScale = new Vector3(width - 2f * inset, height * 0.55f, 1f);
            var renderer = sheen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = gradient };
            renderer.sortingOrder = 49;
            // The sheen along the top edge, and ticks at the corners.
            Plate(board, "Sheen", 0f, -U(0.1f), width - 2f * U(GlassRadius), U(0.06f), 0f, new Color(1f, 1f, 1f, 0.22f), order: 50, depth: -U(0.01f));
            var arm = U(1.0f);
            var weight = U(0.08f);
            var tick = new Color(1f, 1f, 1f, 0.38f);
            foreach (var (sx, sy) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
            {
                var x = sx * (width / 2f + U(0.35f));
                var y = cy + sy * (height / 2f + U(0.35f));
                Plate(board, "Tick", x - sx * arm / 2f, y, arm, weight, 0f, tick, order: 50, depth: -U(0.01f));
                Plate(board, "Tick", x, y - sy * arm / 2f, weight, arm, 0f, tick, order: 50, depth: -U(0.01f));
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Prompts: a key cap with the action's icon, then its words; the hit area unseen, 60 dp tall.

        private sealed class Prompt
        {
            public Prompt(string words, GlazeIcon icon, bool main = false, bool pointedAt = false)
            {
                Words = words;
                Icon = icon;
                Main = main;
                PointedAt = pointedAt;
            }

            public string Words { get; }

            public GlazeIcon Icon { get; }

            public bool Main { get; }

            public bool PointedAt { get; }
        }

        private static float PromptWidth(Board board, Prompt prompt) =>
            U(1.45f) + U(Grid) + WidthOf(board, prompt.Words, GlazeType.Body) * (prompt.Main ? 1.04f : 1f) + U(1.2f);

        private static void DrawPrompt(Board board, Prompt prompt, float left, float centerY)
        {
            var width = PromptWidth(board, prompt);
            var hit = Button(board, "Prompt " + prompt.Words, ButtonRole.Secondary, "", left + width / 2f, centerY, width);
            hit.Label.gameObject.SetActive(false);
            // Pointed at: a soft light behind the prompt and its words at full white; at rest, nothing.
            Restyle(hit, Color.clear, Color.clear);
            if (prompt.PointedAt)
            {
                var arm = U(0.7f);
                var weight = U(0.07f);
                var half = new Vector2(width / 2f - U(0.1f), U(GlazeButton.HeightDegrees) / 2f - U(0.1f));
                foreach (var (sx, sy) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
                {
                    var cx = left + width / 2f + sx * half.x;
                    var cy = centerY + sy * half.y;
                    Plate(board, "Pointed", cx - sx * arm / 2f, cy, arm, weight, 0f, Accent, order: 64, depth: -U(0.05f));
                    Plate(board, "Pointed", cx, cy - sy * arm / 2f, weight, arm, 0f, Accent, order: 64, depth: -U(0.05f));
                }
            }
            var cap = U(1.45f);
            var capX = left + U(0.6f) + cap / 2f;
            if (prompt.Main) Plate(board, "Cap", capX, centerY, cap, cap, cap / 2f, Accent, order: 62, depth: -U(0.04f));
            else Plate(board, "Cap", capX, centerY, cap, cap, cap / 2f, Color.clear, new Color(1f, 1f, 1f, 0.6f), U(0.07f), 62, -U(0.04f));
            Glyph(board, "Cap icon", prompt.Icon, capX, centerY, prompt.Main ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).OnStrong) : GlazeTokens.Text, 0.95f);
            var wordsLeft = capX + cap / 2f + U(Grid);
            Body(board, "Words " + prompt.Words, prompt.Words, wordsLeft, centerY + U(BodySize) * 0.6f, U(30f),
                prompt.Main ? AccentText : prompt.PointedAt ? GlazeTokens.Text : new Color(0.93f, 0.95f, 0.97f, 0.88f), strong: prompt.Main);
        }

        /// <summary>The footer: Close far left, the main action far right, a second beside it; a hairline above. Says where it ends.</summary>
        private static float Footer(Board board, float left, float right, float top, Prompt? farLeft, Prompt? nearLeft, Prompt? nearRight, Prompt? farRight)
        {
            Plate(board, "Footer line", 0f, top, right - left + 2f * U(PanelPadding) - U(1.2f), U(0.05f), 0f, new Color(1f, 1f, 1f, 0.1f), order: 53, depth: -U(0.02f));
            var tall = U(GlazeButton.HeightDegrees);
            var y = top - board.TargetGap - tall / 2f;
            var x = left - U(0.6f);
            foreach (var prompt in new[] { farLeft, nearLeft })
            {
                if (prompt == null) continue;
                DrawPrompt(board, prompt, x, y);
                x += PromptWidth(board, prompt) + board.TargetGap;
            }
            var end = right + U(0.6f);
            foreach (var prompt in new[] { farRight, nearRight })
            {
                if (prompt == null) continue;
                var width = PromptWidth(board, prompt);
                DrawPrompt(board, prompt, end - width, y);
                end -= width + board.TargetGap;
            }
            return top - board.TargetGap - tall;
        }

        // ---------------------------------------------------------------------------------------------
        // Sections across the top, and rows.

        private static float StyledSections(Board board, IReadOnlyList<string> names, float left, float right, float top, int chosen, int waiting)
        {
            var tall = U(RowHeight);
            var widths = names.Select(name => WidthOf(board, name, GlazeType.Caption) * 1.05f + U(1.0f)).ToList();
            var gap = Mathf.Max(board.TargetGap, (right - left + U(1.0f) - widths.Sum()) / Mathf.Max(1, names.Count - 1));
            var x = left - U(0.5f);
            for (var index = 0; index < names.Count; index++)
            {
                var width = widths[index];
                var hit = Button(board, "Section " + names[index], ButtonRole.Secondary, "", x + width / 2f, top - tall / 2f, width, compact: true);
                hit.Label.gameObject.SetActive(false);
                Restyle(hit, Color.clear, Color.clear);
                var chosenOne = index == chosen;
                var (words, _) = Fact(board, "Section words " + names[index], names[index], x + U(0.5f), top - tall / 2f + U(LabelSize) * 0.6f, width, chosenOne ? GlazeTokens.Text : Secondary);
                words.characterSpacing = 2f;
                if (chosenOne) words.fontSharedMaterial = Text(board, "Strong probe", "", GlazeType.Caption, GlazeTokens.Text, 0f, 0f, U(1f), strong: true).Label.fontSharedMaterial;
                if (chosenOne) Plate(board, "Chosen line", x + width / 2f, top - tall + U(0.25f), width - U(1.0f), U(0.12f), U(0.06f), Accent, order: 62, depth: -U(0.03f));
                if (index == waiting) Plate(board, "Waiting dot", x + width - U(0.15f), top - tall / 2f + U(0.55f), U(0.42f), U(0.42f), U(0.21f), Amber, order: 62, depth: -U(0.03f));
                x += width + gap;
            }
            Plate(board, "Sections line", 0f, top - tall - U(0.1f), right - left + 2f * U(PanelPadding) - U(1.2f), U(0.05f), 0f, new Color(1f, 1f, 1f, 0.1f), order: 53, depth: -U(0.02f));
            return top - tall - U(0.1f);
        }

        /// <summary>
        /// A row: an icon in its column, the words on the content line, a small fact on the right content
        /// line, a chevron when it opens more; a 48 dp target, a hairline in the 12 mm gap under it.
        /// Says the row's middle, where a connector leaves it.
        /// </summary>
        private static float StyledRow(Board board, string words, float left, float right, float top, Action<float, float> icon, string? fact = null,
            Color? factColour = null, bool more = false, bool chosen = false, bool divider = true, Color? wordsColour = null)
        {
            var tall = U(RowHeight);
            var middle = top - tall / 2f;
            var hit = Button(board, "Row " + words, ButtonRole.Choice, "", (left + right) / 2f, middle, right - left + U(1.0f), compact: true);
            hit.Label.gameObject.SetActive(false);
            Restyle(hit, Color.clear, Color.clear);
            if (chosen) Plate(board, "Chosen bar", left - U(0.85f), middle, U(0.16f), tall - U(0.9f), U(0.08f), Accent, order: 62, depth: -U(0.03f));
            icon(left + U(IconColumn) / 2f, middle);
            var factWidth = fact == null ? 0f : WidthOf(board, fact, GlazeType.Caption) + U(0.3f);
            var end = right - (more ? U(1.8f) : 0f);
            Body(board, "Row words " + words, words, left + U(IconColumn) + U(Grid), middle + U(BodySize) * 0.6f, end - left - U(IconColumn) - U(Grid) - factWidth - U(1.0f),
                wordsColour ?? GlazeTokens.Text);
            if (fact != null) Fact(board, "Row fact " + words, fact, end, middle + U(LabelSize) * 0.6f, factWidth, factColour, TextAlignmentOptions.TopRight);
            if (more) ProtoGlyph(board, "More", GlyphChevron, right - U(0.7f), middle, chosen ? GlazeTokens.Text : Secondary, 1.3f);
            if (divider) Plate(board, "Divider", (left + right) / 2f, top - tall - board.TargetGap / 2f, right - left, U(0.04f), 0f, new Color(1f, 1f, 1f, 0.07f), order: 53, depth: -U(0.02f));
            return middle;
        }

        /// <summary>A light line from a chosen row to the side panel it opened, a small node at each end.</summary>
        private static void Connector(Shot shot, Board from, float rowY, float rowRight, Board to)
        {
            var start = from.Content.TransformPoint(new Vector3(rowRight + U(1.3f), rowY, -U(0.04f)));
            var end = to.Content.TransformPoint(new Vector3(-to.Width / 2f, rowY, -U(0.04f)));
            var go = new GameObject("Connector");
            go.transform.SetParent(shot.Root, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.0009f;
            line.endWidth = 0.0009f;
            line.startColor = Accent;
            line.endColor = Accent;
            line.sortingOrder = 70;
        }

        // ---------------------------------------------------------------------------------------------
        // The three moments.

        private static readonly string[] MenuSections = { "Tasks", "Projects", "New project", "Usage left", "Settings" };

        /// <summary>The menu open on Tasks: every task, what waits first, grouped by project; one chosen, its details beside.</summary>
        private static void StyledTasks(Shot shot)
        {
            var menu = shot.Board("Menu");
            var width = U(40f);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var y = -U(PanelPadding) + U(Grid);
            y = StyledSections(menu, MenuSections, left, right, y, chosen: 0, waiting: 0);
            y -= U(GroupGap);
            var rows = new (string Group, string Words, GlazeIcon Icon, GlazeTone Tone, string Fact, bool Chosen)[]
            {
                ("Waiting for you", "Add rate limiting to the sign-in endpoint", GlazeIcon.WaitingForYou, GlazeTone.Attention, "Storefront API", true),
                ("Storefront API", "Paginate the order history endpoint", GlazeIcon.Working, GlazeTone.Active, "4 min", false),
                ("", "Send an order confirmation email", GlazeIcon.FinishedThisRound, GlazeTone.Success, "20 min ago", false),
            };
            var chosenMiddle = 0f;
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row.Group.Length > 0)
                {
                    y -= Caps(menu, "Group " + row.Group, row.Group, left, y, right - left, row.Tone == GlazeTone.Attention ? AmberText : Secondary).Height + U(LabelGap);
                }
                var tone = Glaze.Tone(row.Tone);
                var middle = StyledRow(menu, row.Words, left, right, y, (cx, cy) => Glyph(menu, "State", row.Icon, cx, cy, GlazeTokens.ColorOf(tone.Foreground)),
                    row.Fact, row.Tone == GlazeTone.Attention ? AmberText : Secondary, more: true, chosen: row.Chosen, divider: index < rows.Length - 1 && rows[index + 1].Group.Length == 0);
                if (row.Chosen) chosenMiddle = middle;
                y -= U(RowHeight) + (index < rows.Length - 1 ? menu.TargetGap : 0f);
                if (index < rows.Length - 1 && rows[index + 1].Group.Length > 0) y -= U(GroupGap);
            }
            y -= menu.TargetGap;
            y = Footer(menu, left, right, y, new Prompt("Close", GlazeIcon.Close), new Prompt("Next page", GlazeIcon.Next), null, new Prompt("Open its file", GlazeIcon.OpenNow, main: true));
            y -= U(PanelPadding) - U(Grid);
            Glass(menu, width, -y);

            // Its details beside: label above value, as a case file reads.
            var side = shot.Board("Details");
            var sideWidth = U(25f);
            var sl = -sideWidth / 2f + U(PanelPadding);
            var sr = sideWidth / 2f - U(PanelPadding);
            var sy = -U(PanelPadding);
            sy -= Body(side, "Heading", "Add rate limiting to the sign-in endpoint", sl, sy, sr - sl, lines: 2).Height + U(GroupGap);
            foreach (var (name, value, colour) in new (string, string, Color)[]
            {
                ("What it needs", "An answer: how long a lockout lasts", AmberText),
                ("Agent app and model", "OpenCode · qwen3.6", GlazeTokens.Text),
                ("Running for", "12 min", GlazeTokens.Text),
                ("Project", "Storefront API", GlazeTokens.Text),
            })
            {
                sy -= Caps(side, "Fact name " + name, name, sl, sy, sr - sl).Height + U(LabelGap) * 0.5f;
                sy -= Body(side, "Fact " + name, value, sl, sy, sr - sl, colour).Height + U(GroupGap);
            }
            var sideFooterTop = y + U(PanelPadding) - U(Grid) + U(GlazeButton.HeightDegrees) + side.TargetGap;
            var end = Footer(side, sl, sr, sideFooterTop, new Prompt("Close details", GlazeIcon.Close), null, null, null);
            Glass(side, sideWidth, -(end - U(PanelPadding) + U(Grid)));
            PlaceBeside(shot, menu, side, -14.2f, 1, centered: true);
            Connector(shot, menu, chosenMiddle, right, side);
        }

        /// <summary>A waiting approval: the file on What it needs, only the command and what each answer does; Approve pointed at.</summary>
        private static void StyledApproval(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var file = shot.Board("Task file");
            var width = U(40f);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var y = StyledHeader(file, who, left, right);
            y = StyledSections(file, SectionNames, left, right, y, chosen: 0, waiting: 0);
            y -= U(GroupGap) + U(Grid);
            y -= Caps(file, "Wants", "It wants to run a command", left, y, right - left).Height + U(LabelGap);
            var well = U(RowHeight);
            Plate(file, "Command", (left + right) / 2f, y - well / 2f, right - left, well, U(0.4f), GlazeTokens.ColorOf(Glaze.Well, 0.9f), new Color(1f, 1f, 1f, 0.08f), U(0.05f), 52);
            ProtoGlyph(file, "Command icon", "", left + U(IconColumn) / 2f + U(0.3f), y - well / 2f, Secondary, 1.1f);
            Body(file, "Command words", "make migrate", left + U(IconColumn) + U(Grid), y - well / 2f + U(BodySize) * 0.6f, right - left - U(IconColumn) - U(1f));
            y -= well + U(GroupGap);
            y -= Fact(file, "Consequence", "Approve lets it go ahead. Deny refuses; it may try another way.", left, y, right - left).Height + U(GroupGap);
            y = Footer(file, left, right, y, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Deny", GlazeIcon.Deny),
                new Prompt("Approve", GlazeIcon.Approve, main: true, pointedAt: true));
            y -= U(PanelPadding) - U(Grid);
            Glass(file, width, -y);
            PlaceUnderLabels(shot, file, CardYaw(shot, slot, 22f, 0f));
            Projection(shot, slot, file, file);
        }

        /// <summary>The title row: the task's title in the Title role on the content line, its badge on the right content line.</summary>
        private static float StyledHeader(Board board, CharacterPresentation who, float left, float right)
        {
            var y = -U(PanelPadding) + U(Grid);
            var badge = StateBadgeView.Create(board.Content, "Badge", 58);
            badge.Show(StateLanguage.BadgeOf(who));
            var (_, height) = Title(board, OpenedTitle, left, y, right - left - badge.Width - U(GroupGap));
            badge.transform.localPosition = new Vector3(right - badge.Width / 2f, y - height / 2f, -U(0.05f));
            return y - height - U(Grid);
        }

        /// <summary>What it changed, a few lines; the first opens the changed files in a panel beside it.</summary>
        private static void StyledChanged(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var file = shot.Board("Task file");
            var width = U(40f);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var y = StyledHeader(file, who, left, right);
            y = StyledSections(file, SectionNames, left, right, y, chosen: 2, waiting: -1);
            y -= file.TargetGap + U(Grid);
            var first = StyledRow(file, "2 files changed, both new", left, right, y,
                (cx, cy) => ProtoGlyph(file, "Files", GlyphDescription, cx, cy, GlazeTokens.Text), more: true, chosen: true);
            y -= U(RowHeight) + file.TargetGap;
            StyledRow(file, "Why it changed them", left, right, y, (cx, cy) => Glyph(file, "Why", GlazeIcon.TellIt, cx, cy, GlazeTokens.Text, 1.1f), "Agent says", Secondary, more: true, divider: false);
            y -= U(RowHeight) + U(GroupGap);
            // A line that is not a target: the inferred clause, chipped, never read as fact.
            var chip = Chip(file, "Inferred", GlazeTone.Unknown, left, y - U(0.75f));
            y -= Body(file, "Inferred line", "Nothing checked them after the last change.", left + chip + U(Grid), y, right - left - chip - U(Grid), AmberText).Height + U(GroupGap);
            y -= Fact(file, "Source", "From Salidium · 2 minutes ago", left, y, right - left).Height + U(GroupGap) * 0.5f;
            y = Footer(file, left, right, y, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Hold to talk", GlazeIcon.HoldToTalk),
                new Prompt("Tell it", GlazeIcon.TellIt, main: true));
            y -= U(PanelPadding) - U(Grid);
            Glass(file, width, -y);

            // The changed files beside it, each with its kind's icon, and the source on this page too.
            var side = shot.Board("Changed files");
            var sideWidth = U(25f);
            var sl = -sideWidth / 2f + U(PanelPadding);
            var sr = sideWidth / 2f - U(PanelPadding);
            var sy = -U(PanelPadding);
            sy -= Caps(side, "Heading", "2 files changed, both new", sl, sy, sr - sl).Height + U(GroupGap);
            foreach (var (glyph, name, kind, lines) in new[] { (GlyphDatabase, "0012_sign_in_attempts.sql", "New", "+14"), (GlyphCode, "src/auth/rate-limit.ts", "New", "+57") })
            {
                var middle = sy - U(RowHeight) / 2f;
                ProtoGlyph(side, "Kind " + name, glyph, sl + U(IconColumn) / 2f, middle, GlazeTokens.Text);
                Body(side, "File " + name, name, sl + U(IconColumn) + U(Grid), middle + U(BodySize) * 0.6f + U(0.5f), sr - sl - U(IconColumn) - U(Grid) - U(4f));
                Fact(side, "Kind words " + name, kind, sl + U(IconColumn) + U(Grid), middle - U(0.3f), U(8f));
                Body(side, "Lines " + name, lines, sr, middle + U(BodySize) * 0.6f, U(4f), GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground), alignment: TextAlignmentOptions.TopRight);
                sy -= U(RowHeight) + U(Grid);
                Plate(side, "Divider", (sl + sr) / 2f, sy + U(Grid) / 2f, sr - sl, U(0.04f), 0f, new Color(1f, 1f, 1f, 0.07f), order: 53, depth: -U(0.02f));
            }
            sy -= U(Grid);
            sy -= Fact(side, "Source", "From Salidium · 2 minutes ago", sl, sy, sr - sl).Height;
            var sideFooterTop = y + U(PanelPadding) - U(Grid) + U(GlazeButton.HeightDegrees) + side.TargetGap;
            var end = Footer(side, sl, sr, sideFooterTop, new Prompt("Close details", GlazeIcon.Close), null, null, null);
            Glass(side, sideWidth, -(end - U(PanelPadding) + U(Grid)));
            PlaceBeside(shot, file, side, -14.2f, 1, centered: true);
            Connector(shot, file, first, right, side);
            Projection(shot, slot, file, file);
        }
    }
}
