#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>What the actions row shows.</summary>
    public enum ControlsMode
    {
        /// <summary>The actions offered now, or why there are none.</summary>
        Actions,

        /// <summary>A question naming what would be sent, with a separate button to confirm it.</summary>
        Confirm,

        /// <summary>The system keyboard is open.</summary>
        Typing,

        /// <summary>Preset instructions, where the system keyboard is not supported.</summary>
        Presets,
    }

    /// <summary>Everything the panel shows, already in words (<see cref="WorkspaceText"/>).</summary>
    public sealed class PanelContent
    {
        public string Title = "";
        public string Status = "";
        public Color StatusColor = WorkspaceVisuals.TextColor;
        public string Execution = "";
        public string Objective = "";
        public IReadOnlyList<string> Attention = Array.Empty<string>();
        public Color AttentionColor = WorkspaceVisuals.TextColor;
        public ControlsMode Mode;
        public IReadOnlyList<WorkspaceAction> Actions = Array.Empty<WorkspaceAction>();
        public string? WhyNoActions;
        public string? Prompt;
        public string? ConfirmLabel;
        public IReadOnlyList<PresetInstruction> Presets = Array.Empty<PresetInstruction>();
        public string? Notice;
        public IReadOnlyList<string> Feedback = Array.Empty<string>();
        public string ActivityCaption = "Recent activity";
        public IReadOnlyList<(string Text, bool Claim)> Activity = Array.Empty<(string, bool)>();
    }

    /// <summary>
    /// The expanded workspace: the same workstream as its character, with its objective, execution,
    /// what needs the person, the actions offered, how requests are going, and recent activity. The
    /// layout is fixed, so nothing jumps while the work changes. It shows words it is given and
    /// derives nothing; <see cref="WorkspaceDirector"/> fills it from the client core.
    /// </summary>
    public sealed class WorkspacePanel : MonoBehaviour
    {
        public const float Width = 0.86f;
        public const float Height = 0.80f;

        private const float Left = -Width / 2f + 0.04f;
        private const float ContentWidth = Width - 0.08f;
        private const float Gap = 0.02f;
        private const float ActionsTop = 0.078f;
        private const int MaxFeedback = 3;
        private const int MaxActivity = 6;
        private const float LineHeight = 0.031f;

        private readonly List<PanelButton> actionButtons = new List<PanelButton>();
        private readonly List<PanelButton> presetButtons = new List<PanelButton>();
        private readonly List<TextMeshPro> feedbackLines = new List<TextMeshPro>();
        private readonly List<TextMeshPro> activityLines = new List<TextMeshPro>();
        private readonly List<WorkspaceAction> shownActions = new List<WorkspaceAction>();
        private readonly List<PresetInstruction> shownPresets = new List<PresetInstruction>();
        private TextMeshPro title = null!;
        private TextMeshPro status = null!;
        private TextMeshPro execution = null!;
        private TextMeshPro objective = null!;
        private TextMeshPro attention = null!;
        private TextMeshPro controlsText = null!;
        private TextMeshPro feedbackCaption = null!;
        private TextMeshPro activityCaption = null!;
        private PanelButton collapse = null!;
        private PanelButton confirm = null!;
        private PanelButton cancel = null!;

        public event Action<WorkspaceAction>? ActionPressed;

        public event Action? ConfirmPressed;

        public event Action? CancelPressed;

        public event Action? CollapsePressed;

        public event Action<PresetInstruction>? PresetPressed;

        /// <summary>Buttons ignore presses while this is false.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        public static WorkspacePanel Create(Transform parent)
        {
            var go = new GameObject("Workspace panel");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<WorkspacePanel>();
            panel.Build();
            return panel;
        }

        private void Build()
        {
            WorkspaceVisuals.Plate(transform, "Background", new Vector2(Width, Height), WorkspaceVisuals.PanelColor, WorkspaceVisuals.PlateOrder);
            // The background takes the ray, so a character behind the panel is not pointed at through it.
            PointerTarget.Rectangle(gameObject, new Vector2(Width, Height), ray: true, poke: false);

            title = Line("Title", WorkspaceVisuals.TitleSize, WorkspaceVisuals.TextColor, 0.36f, ContentWidth - 0.2f, 0.05f);
            status = Line("Status", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, 0.305f, ContentWidth - 0.2f, 0.036f);
            execution = Line("Execution", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor, 0.266f, ContentWidth, 0.03f);
            objective = Line("Objective", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, 0.231f, ContentWidth, 0.058f, wrap: true);
            var divider = WorkspaceVisuals.Plate(transform, "Divider", new Vector2(ContentWidth, 0.002f), WorkspaceVisuals.DividerColor, WorkspaceVisuals.ControlOrder);
            divider.transform.localPosition = new Vector3(0f, 0.166f, -0.001f);
            attention = Line("Attention", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, 0.152f, ContentWidth, 0.064f, wrap: true);
            controlsText = Line("Controls text", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor, ActionsTop, ContentWidth, PanelButton.Height, wrap: true);
            controlsText.alignment = TextAlignmentOptions.MidlineLeft;
            feedbackCaption = Line("Requests caption", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, -0.012f, ContentWidth, 0.026f);
            feedbackCaption.text = "Requests";
            for (var index = 0; index < MaxFeedback; index++)
            {
                feedbackLines.Add(Line("Request " + index, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, -0.038f - index * LineHeight, ContentWidth, LineHeight));
            }
            activityCaption = Line("Activity caption", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, -0.14f, ContentWidth, 0.026f);
            for (var index = 0; index < MaxActivity; index++)
            {
                activityLines.Add(Line("Activity " + index, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, -0.166f - index * LineHeight, ContentWidth, LineHeight));
            }

            collapse = Button("Collapse", () => CollapsePressed?.Invoke());
            confirm = Button("Confirm", () => ConfirmPressed?.Invoke());
            cancel = Button("Cancel", () => CancelPressed?.Invoke());
            for (var index = 0; index < 4; index++)
            {
                var slot = index;
                actionButtons.Add(Button("Action " + index, () =>
                {
                    if (slot < shownActions.Count) ActionPressed?.Invoke(shownActions[slot]);
                }));
            }
            for (var index = 0; index < WorkspaceText.PresetInstructions.Count; index++)
            {
                var slot = index;
                presetButtons.Add(Button("Preset " + index, () =>
                {
                    if (slot < shownPresets.Count) PresetPressed?.Invoke(shownPresets[slot]);
                }));
            }
        }

        public void Show(PanelContent content)
        {
            title.text = content.Title;
            status.text = content.Status;
            status.color = content.StatusColor;
            execution.text = content.Execution;
            objective.text = content.Objective;
            attention.text = content.Attention.Count == 0 ? "Nothing needs you right now." : string.Join("\n", content.Attention);
            attention.color = content.Attention.Count == 0 ? WorkspaceVisuals.SecondaryColor : content.AttentionColor;

            // The collapse control sits in the top right corner, away from every action.
            var collapseWidth = collapse.Measure("Collapse", 0.17f);
            collapse.Show("Collapse", new Vector2(Width / 2f - 0.04f - collapseWidth / 2f, 0.36f - PanelButton.Height / 2f), collapseWidth);
            ShowControls(content);

            var feedback = new List<(string Text, Color Color)>();
            if (content.Notice != null) feedback.Add((content.Notice, WorkspaceVisuals.AttentionColor));
            foreach (var line in content.Feedback) feedback.Add((line, WorkspaceVisuals.TextColor));
            feedbackCaption.text = feedback.Count == 0 ? "Requests: none yet" : "Requests";
            for (var index = 0; index < feedbackLines.Count; index++)
            {
                feedbackLines[index].text = index < feedback.Count ? feedback[index].Text : "";
                if (index < feedback.Count) feedbackLines[index].color = feedback[index].Color;
            }

            activityCaption.text = content.ActivityCaption;
            // The newest activity is at the bottom, like a log.
            var first = Math.Max(0, content.Activity.Count - activityLines.Count);
            for (var index = 0; index < activityLines.Count; index++)
            {
                var line = activityLines[index];
                var source = first + index;
                if (source >= content.Activity.Count)
                {
                    line.text = "";
                    continue;
                }
                var (text, claim) = content.Activity[source];
                line.text = text;
                line.fontStyle = claim ? FontStyles.Italic : FontStyles.Normal;
                line.color = claim ? WorkspaceVisuals.ClaimColor : WorkspaceVisuals.TextColor;
            }
        }

        /// <summary>
        /// Shows the row for the current mode. A button that stays keeps its state, so a press in
        /// progress, a hover, or its settling time survive the panel refreshing; only buttons the
        /// row no longer needs are hidden.
        /// </summary>
        private void ShowControls(PanelContent content)
        {
            var center = ActionsTop - PanelButton.Height / 2f;
            var right = Width / 2f - 0.04f;
            var shown = new HashSet<PanelButton>();
            shownActions.Clear();
            shownPresets.Clear();
            controlsText.text = "";

            switch (content.Mode)
            {
                case ControlsMode.Actions:
                    shownActions.AddRange(content.Actions);
                    ShowRow(actionButtons, shownActions.ConvertAll(WorkspaceText.Label), center, shown);
                    if (shownActions.Count == 0) SetControlsText(content.WhyNoActions ?? "", Left, right, WorkspaceVisuals.SecondaryColor);
                    break;
                case ControlsMode.Confirm:
                    // The confirmation goes where no action button was, so pressing twice in one place never confirms.
                    var confirmLabel = content.ConfirmLabel ?? "Confirm";
                    var confirmWidth = confirm.Measure(confirmLabel, 0.2f);
                    confirm.Show(confirmLabel, new Vector2(right - confirmWidth / 2f, center), confirmWidth, confirm: true);
                    shown.Add(confirm);
                    var cancelWidth = cancel.Measure("Cancel", 0.15f);
                    var cancelRight = right - confirmWidth - Gap;
                    cancel.Show("Cancel", new Vector2(cancelRight - cancelWidth / 2f, center), cancelWidth);
                    shown.Add(cancel);
                    SetControlsText(content.Prompt ?? "", Left, cancelRight - cancelWidth - Gap, WorkspaceVisuals.AttentionColor);
                    break;
                case ControlsMode.Typing:
                    var typingCancel = cancel.Measure("Cancel", 0.15f);
                    cancel.Show("Cancel", new Vector2(right - typingCancel / 2f, center), typingCancel);
                    shown.Add(cancel);
                    SetControlsText(content.Prompt ?? "", Left, right - typingCancel - Gap, WorkspaceVisuals.TextColor);
                    break;
                case ControlsMode.Presets:
                    var presetCancel = cancel.Measure("Cancel", 0.15f);
                    cancel.Show("Cancel", new Vector2(right - presetCancel / 2f, center), presetCancel);
                    shown.Add(cancel);
                    shownPresets.AddRange(content.Presets);
                    ShowRow(presetButtons, shownPresets.ConvertAll(preset => preset.Label), center, shown);
                    break;
            }

            foreach (var button in actionButtons) HideUnless(button, shown);
            foreach (var button in presetButtons) HideUnless(button, shown);
            HideUnless(confirm, shown);
            HideUnless(cancel, shown);
        }

        private static void HideUnless(PanelButton button, HashSet<PanelButton> shown)
        {
            if (!shown.Contains(button)) button.Hide();
        }

        /// <summary>Buttons left to right from the panel's left edge.</summary>
        private static void ShowRow(List<PanelButton> buttons, List<string> labels, float center, HashSet<PanelButton> shown)
        {
            var x = Left;
            for (var index = 0; index < buttons.Count && index < labels.Count; index++)
            {
                var width = buttons[index].Measure(labels[index], 0.14f);
                buttons[index].Show(labels[index], new Vector2(x + width / 2f, center), width);
                shown.Add(buttons[index]);
                x += width + Gap;
            }
        }

        private void SetControlsText(string text, float left, float right, Color color)
        {
            controlsText.text = text;
            controlsText.color = color;
            controlsText.rectTransform.localPosition = new Vector3(left, ActionsTop, -0.001f);
            controlsText.rectTransform.sizeDelta = new Vector2(Math.Max(0.05f, right - left), PanelButton.Height);
        }

        private TextMeshPro Line(string name, float size, Color color, float top, float width, float height, bool wrap = false)
        {
            var text = WorkspaceVisuals.Text(transform, name, size, color, new Vector2(width, height), TextAlignmentOptions.TopLeft, wrap);
            text.rectTransform.localPosition = new Vector3(Left, top, -0.001f);
            return text;
        }

        private PanelButton Button(string name, Action pressed)
        {
            var button = PanelButton.Create(transform, name);
            button.Accepting = () => Accepting();
            button.Pressed += pressed;
            return button;
        }
    }
}
