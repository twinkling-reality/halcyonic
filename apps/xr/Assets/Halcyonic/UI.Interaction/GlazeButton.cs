#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a button does, which sets its look (ADR 0023): roles, never screens, choose colours.</summary>
    public enum ButtonRole
    {
        /// <summary>The one action a screen leads to, filled with the accent.</summary>
        Primary,

        /// <summary>Any other action.</summary>
        Secondary,

        /// <summary>An action that cannot be taken back: outlined in red; its confirmation, <see cref="GlazeButton.On"/>, solid red.</summary>
        Destructive,

        /// <summary>A choice that shows or hides something, as a project on the rail: outlined in the accent while on.</summary>
        Filter,

        /// <summary>A choice or a fact in a panel's list: a raised tile, edged in the accent while chosen.</summary>
        Choice,

        /// <summary>Goes to work that waits for the person, and only that: filled in the attention colour.</summary>
        Attention,
    }

    /// <summary>
    /// A button of the interface (ADR 0023): pinched from afar with a hand ray, or poked. Its label
    /// always says what it does; a second, smaller line under it can say more, such as a project's
    /// counts. It is 60 dp tall, or 48 dp when compact, never less, and it shows every state: at rest,
    /// pointed at, pressed, unavailable, set aside while the app lacks focus, and done. Built in units
    /// of its distance from the eyes, under a parent scaled by that distance. As a row of a panel's
    /// list (<see cref="ShowRow"/>) its words are left-aligned: a small line over the title, the title
    /// and its detail in lines of their own, and a word at its right saying what pressing it does.
    /// </summary>
    /// <remarks>
    /// A press within <see cref="SettleSeconds"/> of the button taking a new role, new words or
    /// becoming available is not a press of it, so a confirmation needs a separate, deliberate
    /// gesture, and a hold button starts its hold after <see cref="HoldSeconds"/> and ends it when let
    /// go, dropped or taken away, as hold to talk needs.
    /// </remarks>
    public sealed class GlazeButton : MonoBehaviour
    {
        public const float HeightDegrees = Glaze.TargetDegrees;

        public const float CompactHeightDegrees = Glaze.MinimumTargetDegrees;

        /// <summary>A pinch or poke that began before the button took its current role is not a press of it.</summary>
        public const float SettleSeconds = 0.35f;

        /// <summary>How long a hold button must be held before its hold starts (ADR 0021).</summary>
        public const float HoldSeconds = 0.3f;

        private const float PaddingDegrees = 1.25f;
        private const float CompactPaddingDegrees = 0.9f;
        private const float RowPaddingDegrees = 1f;
        private const float RowVerticalPaddingDegrees = 0.5f;
        private const float RowEndGapDegrees = 0.75f;
        private const float EdgeDegrees = 0.12f;
        private const float HoverEdgeDegrees = 0.15f;

        /// <summary>How much smaller a press draws the button's plate, for the time a press shows; its words keep their size.</summary>
        private const float PressedScale = 0.97f;

        /// <summary>Set aside while the app lacks focus: this visible.</summary>
        private const float AwayOpacity = 0.45f;

        private Surface plate = null!;
        private TextMeshPro label = null!;
        private TextMeshPro detail = null!;
        private TextMeshPro? overline;
        private TextMeshPro? end;
        private PointerTarget target = null!;
        private ButtonRole role;
        private bool compact;
        private bool row;
        private bool on;
        private bool available = true;
        private bool done;
        private GlazeTone? detailTone;
        private Vector2 size;
        private float shownAt;
        private float flash;
        private float pressedAt = -1f;
        private bool holding;
        private bool isStatic;
        private float rowStack;
        private int paintedState = -1;

        /// <summary>A press; on a hold button, a press let go before its hold started.</summary>
        public event Action? Pressed;

        /// <summary>A hold button was held for <see cref="HoldSeconds"/>.</summary>
        public event Action? HoldStarted;

        /// <summary>A hold that started has ended: let go on the button (true), or dropped (false).</summary>
        public event Action<bool>? HoldEnded;

        /// <summary>A hold button: it starts something while held, as hold to talk does, instead of acting on a press.</summary>
        public bool Holds { get; set; }

        /// <summary>A hold started and has not ended.</summary>
        public bool Holding => holding;

        /// <summary>
        /// A row that only says something, as a step and how it went: drawn as words on the panel,
        /// with no tile, taking no press and never lighting up when pointed at.
        /// </summary>
        public bool Static
        {
            get => isStatic;
            set
            {
                if (isStatic == value) return;
                isStatic = value;
                paintedState = -1;
            }
        }

        /// <summary>Presses are ignored while false, as while a panel grows or the app lacks focus.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        /// <summary>The button's size, in its parent's units.</summary>
        public Vector2 Size => size;

        public float Width => size.x;

        public TextMeshPro Label => label;

        /// <summary>The second line, while one shows.</summary>
        public TextMeshPro? Detail => detail.gameObject.activeSelf ? detail : null;

        /// <summary>A row's line over its title, while one shows.</summary>
        public TextMeshPro? Overline => overline != null && overline.gameObject.activeSelf ? overline : null;

        /// <summary>A row's word at its right end, while one shows.</summary>
        public TextMeshPro? End => end != null && end.gameObject.activeSelf ? end : null;

        public PointerTarget Target => target;

        /// <summary>What the button does, which sets its look; a button can change role, as Forget becomes its confirmation.</summary>
        public ButtonRole Role
        {
            get => role;
            set
            {
                if (role == value) return;
                role = value;
                paintedState = -1;
                shownAt = Time.unscaledTime;
            }
        }

        public static float HeightOf(bool compact) => GlazeTokens.Units(compact ? CompactHeightDegrees : HeightDegrees);

        public static GlazeButton Create(Transform parent, string name, ButtonRole role, bool compact = false, int order = 11)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<GlazeButton>();
            button.role = role;
            button.compact = compact;
            button.plate = Surface.Create(go.transform, "Plate", order);
            button.label = GlazeText.Create(go.transform, "Label", compact ? GlazeType.Badge : GlazeType.Body, GlazeTokens.Text,
                TextAlignmentOptions.Center, order + 1, strong: true);
            button.label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.label.textWrappingMode = TextWrappingModes.NoWrap;
            button.detail = GlazeText.Create(go.transform, "Detail", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.Center, order + 1);
            button.detail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.detail.textWrappingMode = TextWrappingModes.NoWrap;
            button.detail.gameObject.SetActive(false);
            var height = HeightOf(compact);
            button.target = PointerTarget.Rectangle(go, new Vector2(height, height), ray: true, poke: true);
            button.target.Selected += button.OnSelected;
            button.target.Released += button.OnReleased;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with this label and second line: the wider and the padding, never narrower than tall.</summary>
        public float Measure(string text, string? detailText = null)
        {
            var padding = GlazeTokens.Units(compact ? CompactPaddingDegrees : PaddingDegrees);
            var widest = label.GetPreferredValues(LabelText.ForTextMeshPro(text)).x;
            if (detailText != null) widest = Mathf.Max(widest, detail.GetPreferredValues(LabelText.ForTextMeshPro(detailText)).x);
            return Mathf.Max(HeightOf(compact), widest + 2f * padding);
        }

        /// <summary>
        /// Shows the button centred at <paramref name="center"/>, in its parent's units, as wide as
        /// <paramref name="width"/>, its label and second line as written, the line in
        /// <paramref name="tone"/>'s colour when given.
        /// </summary>
        public void Show(string text, Vector2 center, float width, string? detailText = null, GlazeTone? tone = null)
        {
            var changed = !gameObject.activeSelf || label.text != LabelText.ForTextMeshPro(text)
                || (detailText == null ? detail.gameObject.activeSelf : !detail.gameObject.activeSelf || detail.text != LabelText.ForTextMeshPro(detailText));
            GlazeText.SetLiteral(label, text);
            detailTone = tone;
            var height = HeightOf(compact);
            size = new Vector2(Mathf.Max(width, height), height);
            var padding = GlazeTokens.Units(compact ? CompactPaddingDegrees : PaddingDegrees);
            var inner = size.x - 2f * padding;
            if (detailText == null)
            {
                detail.gameObject.SetActive(false);
                GlazeText.Lay(label, inner, 1);
                label.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            }
            else
            {
                GlazeText.SetLiteral(detail, detailText);
                detail.gameObject.SetActive(true);
                GlazeText.Lay(label, inner, 1);
                GlazeText.Lay(detail, inner, 1);
                // The label over the line, the pair centred in the button.
                var labelLine = GlazeText.LineHeight(label);
                var detailLine = GlazeText.LineHeight(detail);
                var top = (labelLine + detailLine) / 2f;
                label.transform.localPosition = new Vector3(0f, top - labelLine / 2f, -0.0005f);
                detail.transform.localPosition = new Vector3(0f, top - labelLine - detailLine / 2f, -0.0005f);
            }
            Place(center, changed);
        }

        /// <summary>
        /// Lays the button out as a row of a panel's list at <paramref name="width"/>, without showing
        /// it, and says how tall it needs to be: at least <paramref name="minimum"/>, a target's height
        /// unless given, more for lines that wrap. The detail is <paramref name="words"/>' short one
        /// where its own doesn't fit whole.
        /// </summary>
        public float LayRow(PanelRow words, float width, float? minimum = null)
        {
            EnsureRow();
            var padding = GlazeTokens.Units(RowPaddingDegrees);
            var gap = GlazeTokens.Units(RowEndGapDegrees);
            var inner = width - 2f * padding;
            var stack = 0f;
            var endWidth = 0f;
            if (words.End != null)
            {
                GlazeText.SetLiteral(end!, words.End);
                endWidth = end!.GetPreferredValues(end.text).x;
                GlazeText.Lay(end, endWidth + 0.001f, 1);
                end.gameObject.SetActive(true);
            }
            else end!.gameObject.SetActive(false);
            if (words.Overline != null)
            {
                GlazeText.SetLiteral(overline!, words.Overline);
                GlazeText.Lay(overline!, inner - (endWidth > 0f ? endWidth + gap : 0f), 1);
                overline!.gameObject.SetActive(true);
                stack += GlazeText.LineHeight(overline);
            }
            else overline!.gameObject.SetActive(false);
            // Without a line over the title, the end word stands beside the words, so they wrap short of it.
            var textWidth = words.Overline == null && endWidth > 0f ? inner - endWidth - gap : inner;
            label.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(GlazeText.DegreesOf(words.Card ? GlazeType.Title : GlazeType.Body)));
            GlazeText.SetLiteral(label, words.Title);
            var (titleLines, _) = GlazeText.Lay(label, textWidth, Mathf.Max(1, words.TitleLines));
            stack += Mathf.Max(1, titleLines) * GlazeText.LineHeight(label);
            if (words.Detail != null)
            {
                GlazeText.SetLiteral(detail, words.Detail);
                GlazeText.Lay(detail, textWidth, Mathf.Max(1, words.DetailLines));
                if (detail.isTextTruncated && words.ShortDetail != null)
                {
                    GlazeText.SetLiteral(detail, words.ShortDetail);
                }
                var (detailLines, _) = GlazeText.Lay(detail, textWidth, Mathf.Max(1, words.DetailLines));
                detail.gameObject.SetActive(true);
                stack += Mathf.Max(1, detailLines) * GlazeText.LineHeight(detail);
            }
            else detail.gameObject.SetActive(false);
            detailTone = words.DetailTone;
            paintedState = -1;
            rowStack = stack;
            return Mathf.Max(minimum ?? HeightOf(compact), stack + 2f * GlazeTokens.Units(RowVerticalPaddingDegrees));
        }

        /// <summary>
        /// Shows the button as a row of a panel's list, <paramref name="size"/> in its parent's units
        /// or taller where its words need it, centred at <paramref name="center"/>: its words laid out
        /// by <see cref="LayRow"/> and stacked in the middle of its height, its end word at its right.
        /// </summary>
        public void ShowRow(PanelRow words, Vector2 center, Vector2 size, float? minimum = null)
        {
            var before = label.text + "\n" + (detail.gameObject.activeSelf ? detail.text : "");
            var needed = LayRow(words, size.x, minimum);
            var changed = !gameObject.activeSelf || before != label.text + "\n" + (detail.gameObject.activeSelf ? detail.text : "");
            this.size = new Vector2(size.x, Mathf.Max(size.y, needed));
            var padding = GlazeTokens.Units(RowPaddingDegrees);
            var left = -this.size.x / 2f + padding;
            var right = this.size.x / 2f - padding;
            // The words, stacked, in the middle of the row's height.
            var y = rowStack / 2f;
            if (overline!.gameObject.activeSelf)
            {
                overline.transform.localPosition = new Vector3(left, y, -0.0005f);
                if (end!.gameObject.activeSelf) end.transform.localPosition = new Vector3(right - end.rectTransform.sizeDelta.x, y, -0.0005f);
                y -= GlazeText.LineHeight(overline);
            }
            else if (end!.gameObject.activeSelf)
            {
                end.transform.localPosition = new Vector3(right - end.rectTransform.sizeDelta.x, GlazeText.LineHeight(end) / 2f, -0.0005f);
            }
            label.transform.localPosition = new Vector3(left, y, -0.0005f);
            y -= Mathf.Clamp(label.textInfo.lineCount, 1, Mathf.Max(1, words.TitleLines)) * GlazeText.LineHeight(label);
            if (detail.gameObject.activeSelf) detail.transform.localPosition = new Vector3(left, y, -0.0005f);
            Place(center, changed);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// For a filter: whether what it shows or hides is shown now, as a tab whether it is the one
        /// showing; for a choice, or a tab in the attention colour, whether it is chosen, edged in the
        /// accent; for a destructive action, that this is its confirmation, solid red.
        /// </summary>
        public bool On
        {
            get => on;
            set
            {
                if (on == value) return;
                on = value;
                paintedState = -1;
            }
        }

        /// <summary>
        /// Whether it can be pressed at all; an unavailable button is outlined, quiet and takes no
        /// press. Becoming available counts as a new role: a press already under way is not one of it.
        /// </summary>
        public bool Available
        {
            get => available;
            set
            {
                if (available == value) return;
                available = value;
                paintedState = -1;
                if (value) shownAt = Time.unscaledTime;
            }
        }

        /// <summary>Whether what it asked for is done, as the runtime confirmed: shown in the success colours.</summary>
        public bool Done
        {
            get => done;
            set
            {
                if (done == value) return;
                done = value;
                paintedState = -1;
            }
        }

        /// <summary>Turns the labels into a row's, left-aligned and wrapping, with a line over the title and a word at the end.</summary>
        private void EnsureRow()
        {
            if (row) return;
            row = true;
            var order = label.sortingOrder;
            foreach (var text in new[] { label, detail })
            {
                text.rectTransform.pivot = new Vector2(0f, 1f);
                text.alignment = TextAlignmentOptions.TopLeft;
                text.textWrappingMode = TextWrappingModes.Normal;
            }
            overline = GlazeText.Create(transform, "Overline", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, order);
            overline.rectTransform.pivot = new Vector2(0f, 1f);
            overline.textWrappingMode = TextWrappingModes.NoWrap;
            end = GlazeText.Create(transform, "End", GlazeType.Caption, GlazeTokens.Text, TextAlignmentOptions.TopLeft, order, strong: true);
            end.rectTransform.pivot = new Vector2(0f, 1f);
            end.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private void Place(Vector2 center, bool changed)
        {
            target.Resize(size);
            // In front of whatever it sits on, so a ray finds the button first.
            transform.localPosition = new Vector3(center.x, center.y, -GlazeTokens.Units(0.1f));
            if (changed) shownAt = Time.unscaledTime;
            paintedState = -1;
            gameObject.SetActive(true);
            Paint();
        }

        private void OnSelected()
        {
            if (isStatic || !available || !Accepting() || Time.unscaledTime - shownAt < SettleSeconds) return;
            if (Holds)
            {
                pressedAt = Time.unscaledTime;
                return;
            }
            flash = Glaze.PressSeconds + Glaze.ReleaseSeconds;
            Pressed?.Invoke();
        }

        private void OnReleased(bool cancelled)
        {
            if (pressedAt < 0f) return;
            var started = holding;
            EndPress();
            if (started) HoldEnded?.Invoke(!cancelled);
            else if (!cancelled) Pressed?.Invoke();
        }

        private void EndPress()
        {
            pressedAt = -1f;
            holding = false;
        }

        private void OnDisable()
        {
            if (pressedAt < 0f) return;
            var started = holding;
            EndPress();
            if (started) HoldEnded?.Invoke(false);
        }

        private void Update()
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime);
            if (pressedAt >= 0f)
            {
                if (!Accepting() || !target.HandHovered)
                {
                    var started = holding;
                    EndPress();
                    if (started) HoldEnded?.Invoke(false);
                }
                else if (!holding && Time.unscaledTime - pressedAt >= HoldSeconds)
                {
                    holding = true;
                    HoldStarted?.Invoke();
                }
            }
            Paint();
        }

        /// <summary>
        /// For the editor's renders, which have no hands: paints the button as pointed at or pressed.
        /// The next frame paints it as it really is.
        /// </summary>
        public void PaintForRender(bool hoveredNow, bool pressedNow)
        {
            paintedState = -1;
            Paint(hoveredNow, pressedNow);
        }

        /// <summary>Paints the state it is in now, only when that changed.</summary>
        private void Paint() => Paint(target.Hovered, flash > 0f || holding);

        private void Paint(bool pointed, bool pressed)
        {
            var hovered = pointed && available && !isStatic;
            pressed &= !isStatic;
            var away = FocusGuard.InputSuspended;
            var state = (pressed ? 1 : 0) | (hovered ? 2 : 0) | (away ? 4 : 0);
            if (state == paintedState) return;
            paintedState = state;

            Color fill, text, edge = Color.clear;
            var edgeWidth = 0f;
            var accent = Glaze.Tone(GlazeTone.Accent);
            if (isStatic)
            {
                fill = Color.clear;
                text = GlazeTokens.Text;
            }
            else if (!available)
            {
                fill = Color.clear;
                text = GlazeTokens.ColorOf(Glaze.TextDisabled);
                edge = GlazeTokens.ColorOf(GlazeColor.Hex(0x3A4658));
                edgeWidth = GlazeTokens.Units(0.1f);
            }
            else if (done)
            {
                var success = Glaze.Tone(GlazeTone.Success);
                fill = GlazeTokens.ColorOf(success.Container);
                text = GlazeTokens.ColorOf(success.Foreground);
                edge = GlazeTokens.ColorOf(success.Strong, 0.5f);
                edgeWidth = GlazeTokens.Units(0.1f);
            }
            else
            {
                switch (role)
                {
                    case ButtonRole.Primary:
                        fill = GlazeTokens.ColorOf(pressed ? GlazeColor.Hex(0x6E93D8) : hovered ? GlazeColor.Hex(0x98BAFA) : accent.Strong);
                        text = GlazeTokens.ColorOf(accent.OnStrong);
                        break;
                    case ButtonRole.Attention:
                        var attention = Glaze.Tone(GlazeTone.Attention);
                        fill = GlazeTokens.ColorOf(pressed ? GlazeColor.Hex(0xD9A93F) : hovered ? attention.Foreground : attention.Strong);
                        text = GlazeTokens.ColorOf(attention.OnStrong);
                        if (on)
                        {
                            // The tab showing: edged in the accent, as a chosen choice is.
                            edge = GlazeTokens.ColorOf(accent.Strong);
                            edgeWidth = GlazeTokens.Units(HoverEdgeDegrees);
                        }
                        break;
                    case ButtonRole.Destructive when on:
                        var confirming = Glaze.Tone(GlazeTone.Failure);
                        fill = GlazeTokens.ColorOf(pressed ? GlazeColor.Hex(0xD1665B) : hovered ? confirming.Foreground : confirming.Strong);
                        text = GlazeTokens.ColorOf(confirming.OnStrong);
                        break;
                    case ButtonRole.Destructive:
                        var failure = Glaze.Tone(GlazeTone.Failure);
                        fill = pressed || hovered ? GlazeTokens.ColorOf(failure.Container) : Color.clear;
                        text = GlazeTokens.ColorOf(failure.Foreground);
                        edge = GlazeTokens.ColorOf(failure.Strong);
                        edgeWidth = GlazeTokens.Units(EdgeDegrees);
                        break;
                    case ButtonRole.Filter when !on:
                        // Quiet while off; pointed at or pressed, its fill lightens and so must its word.
                        fill = GlazeTokens.ColorOf(pressed ? Glaze.ControlPressed : hovered ? Glaze.ControlHover : Glaze.Raised);
                        text = pressed || hovered ? GlazeTokens.Text : GlazeTokens.TextSecondary;
                        break;
                    case ButtonRole.Choice:
                        // A tile a step above the panel; chosen, the accent's own soft fill and its edge.
                        fill = GlazeTokens.ColorOf(on ? accent.Container : pressed ? Glaze.ControlHover : hovered ? Glaze.Control : Glaze.Raised);
                        text = GlazeTokens.Text;
                        if (on)
                        {
                            edge = GlazeTokens.ColorOf(accent.Strong);
                            edgeWidth = GlazeTokens.Units(HoverEdgeDegrees);
                        }
                        break;
                    default:
                        fill = GlazeTokens.ColorOf(pressed ? Glaze.ControlPressed : hovered ? Glaze.ControlHover : Glaze.Control);
                        text = GlazeTokens.Text;
                        if (role == ButtonRole.Filter)
                        {
                            edge = GlazeTokens.ColorOf(accent.Strong);
                            edgeWidth = GlazeTokens.Units(EdgeDegrees);
                        }
                        break;
                }
                if (hovered && edgeWidth == 0f)
                {
                    // Pointed at: a ring of the accent inside the outline, besides the lighter fill.
                    edge = GlazeTokens.ColorOf(accent.Strong, 0.55f);
                    edgeWidth = GlazeTokens.Units(HoverEdgeDegrees);
                }
            }
            plate.Draw(pressed ? size * PressedScale : size, GlazeTokens.Units(row ? Glaze.RowRadiusDegrees : Glaze.ButtonRadiusDegrees), fill, edge, edgeWidth);
            var opacity = away ? AwayOpacity : 1f;
            plate.Fade(opacity);
            label.color = new Color(text.r, text.g, text.b, opacity);
            var quiet = available && !done ? GlazeTokens.TextSecondary : text;
            var line = detailTone.HasValue && available && !done ? GlazeTokens.ColorOf(Glaze.Tone(detailTone.Value).Foreground)
                : role == ButtonRole.Primary || role == ButtonRole.Attention || (role == ButtonRole.Destructive && on) ? text : quiet;
            detail.color = new Color(line.r, line.g, line.b, opacity);
            if (overline != null) overline.color = new Color(quiet.r, quiet.g, quiet.b, opacity);
            if (end != null)
            {
                var act = available && !done ? GlazeTokens.ColorOf(accent.Foreground) : text;
                end.color = new Color(act.r, act.g, act.b, opacity);
            }
        }
    }
}
