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

        /// <summary>The work's goal: its objective.</summary>
        public string Goal = "";

        /// <summary>One plain answer: what needs the person, else that nothing does and what it did last.</summary>
        public IReadOnlyList<string> Answer = Array.Empty<string>();
        public Color AnswerColor = WorkspaceVisuals.SecondaryColor;
        public ControlsMode Mode;
        public IReadOnlyList<WorkspaceAction> Actions = Array.Empty<WorkspaceAction>();
        public string? WhyNoActions;
        public string? Prompt;
        public string? ConfirmLabel;

        /// <summary>The confirmation can be given now; an approval's only once its whole request has shown.</summary>
        public bool CanConfirm = true;
        public IReadOnlyList<PresetInstruction> Presets = Array.Empty<PresetInstruction>();
        public string? Notice;
        public IReadOnlyList<string> Feedback = Array.Empty<string>();
        public string ActivityCaption = "Recent activity";
        public IReadOnlyList<(string Text, bool Claim)> Activity = Array.Empty<(string, bool)>();
    }

    /// <summary>
    /// The expanded workspace: the same workstream as its character, leading with its goal and one
    /// plain answer (what needs the person, else what it did last), then the actions offered, and
    /// under them the person's questions as tabs: What is it doing?, Help me understand, What was
    /// checked?, and, while a request waits, What do you need from me? (<see cref="WorkspaceSections"/>). The layout is fixed, so nothing jumps while the work
    /// changes. It shows words it is given and derives nothing; <see cref="WorkspaceDirector"/> fills
    /// it from the client core.
    ///
    /// At its design distance (<see cref="WorkspaceVisuals.PanelDistance"/>) it spans about 34 by 27
    /// degrees, and it keeps that angular size wherever it opens: all of it fits the comfortable
    /// middle of a narrower field of view than the Quest 3's, and nothing essential sits at an edge.
    ///
    /// Titles, objectives, what needs the person, prompts, requests and activity come from outside,
    /// so every label shows its text through <see cref="WorkspaceVisuals.SetLiteral"/>, and a line
    /// cut short ends in an ellipsis. Agent text leans as a claim, drawn by
    /// <see cref="WorkspaceVisuals.Lean"/> rather than TextMeshPro's italics, which drop the ellipsis.
    /// </summary>
    public sealed class WorkspacePanel : MonoBehaviour
    {
        public const float Width = 0.80f;
        public const float Height = 0.62f;

        private const float Margin = 0.035f;
        private const float Left = -Width / 2f + Margin;
        private const float Right = Width / 2f - Margin;
        private const float ContentWidth = Width - 2f * Margin;
        private const float Top = Height / 2f - 0.03f;
        private const float HeaderWidth = ContentWidth - 0.19f;
        private const float Gap = 0.02f;
        private const float ActionsTop = 0.056f;
        private const int MaxFeedback = 2;
        private const int MaxActivity = 4;
        private const float LineHeight = 0.029f;

        /// <summary>The row under the actions for the tabs that choose the details (<see cref="WorkspaceSections"/>): its top and height.</summary>
        public const float TabsTop = -0.026f;

        public const float TabsHeight = 0.056f;

        /// <summary>
        /// Under the tabs, the details they choose between: the requests and recent activity, or the
        /// Understanding or Evaluation section. In the panel's coordinates.
        /// </summary>
        public const float DetailsTop = -0.094f;

        public const float DetailsBottom = -Height / 2f + 0.014f;

        public const float DetailsLeft = Left;

        public const float DetailsWidth = ContentWidth;

        private readonly List<PanelButton> actionButtons = new List<PanelButton>();
        private readonly List<PanelButton> presetButtons = new List<PanelButton>();
        private readonly List<TextMeshPro> feedbackLines = new List<TextMeshPro>();
        private readonly List<TextMeshPro> activityLines = new List<TextMeshPro>();
        private readonly List<bool> leaning = new List<bool>();
        private readonly List<WorkspaceAction> shownActions = new List<WorkspaceAction>();
        private readonly List<PresetInstruction> shownPresets = new List<PresetInstruction>();
        private TextMeshPro title = null!;
        private TextMeshPro status = null!;
        private TextMeshPro execution = null!;
        private TextMeshPro goal = null!;
        private TextMeshPro answer = null!;
        private TextMeshPro controlsText = null!;
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
            WorkspaceVisuals.Plate(transform, "Background", new Vector2(Width, Height), WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            // The background takes the ray, so a character behind the panel is not pointed at through it.
            PointerTarget.Rectangle(gameObject, new Vector2(Width, Height), ray: true, poke: false);

            title = Line("Title", WorkspaceVisuals.TitleSize, WorkspaceVisuals.TextColor, Top, HeaderWidth, 0.045f);
            status = Line("Status", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, Top - 0.048f, HeaderWidth, 0.034f);
            execution = Line("Execution", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor, Top - 0.084f, ContentWidth, 0.028f);
            goal = Line("Goal", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, Top - 0.114f, ContentWidth, 0.028f);
            var divider = WorkspaceVisuals.Plate(transform, "Divider", new Vector2(ContentWidth, 0.002f), WorkspaceVisuals.DividerColor, WorkspaceVisuals.PanelControlOrder);
            divider.transform.localPosition = new Vector3(0f, Top - 0.15f, -0.001f);
            answer = Line("Answer", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, Top - 0.16f, ContentWidth, 0.058f, wrap: true);
            controlsText = Line("Controls text", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor, ActionsTop, ContentWidth, PanelButton.Height, wrap: true);
            controlsText.alignment = TextAlignmentOptions.MidlineLeft;
            // Under the tabs: the requests, then the recent activity, unless a section shows there.
            for (var index = 0; index < MaxFeedback; index++)
            {
                feedbackLines.Add(Line("Request " + index, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, DetailsTop - index * LineHeight, ContentWidth, LineHeight));
            }
            var activityTop = DetailsTop - MaxFeedback * LineHeight - 0.004f;
            activityCaption = Line("Activity caption", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, activityTop, ContentWidth, 0.024f);
            for (var index = 0; index < MaxActivity; index++)
            {
                var line = Line("Activity " + index, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor, activityTop - 0.024f - index * LineHeight, ContentWidth, LineHeight);
                var slot = index;
                leaning.Add(false);
                line.OnPreRenderText += info =>
                {
                    if (leaning[slot]) WorkspaceVisuals.Lean(info);
                };
                activityLines.Add(line);
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
            WorkspaceVisuals.SetLiteral(title, content.Title);
            WorkspaceVisuals.SetLiteral(status, content.Status);
            status.color = content.StatusColor;
            WorkspaceVisuals.SetLiteral(execution, content.Execution);
            WorkspaceVisuals.SetLiteral(goal, content.Goal);
            WorkspaceVisuals.SetLiteralLines(answer, content.Answer);
            answer.color = content.AnswerColor;

            // The collapse control sits in the top right corner, away from every action.
            var collapseWidth = collapse.Measure("Collapse", 0.17f);
            collapse.Show("Collapse", new Vector2(Right - collapseWidth / 2f, Top - PanelButton.Height / 2f), collapseWidth);
            ShowControls(content);

            var feedback = new List<(string Text, Color Color)>();
            if (content.Notice != null) feedback.Add((content.Notice, WorkspaceVisuals.AttentionColor));
            foreach (var line in content.Feedback) feedback.Add((line, WorkspaceVisuals.TextColor));
            if (feedback.Count == 0) feedback.Add(("Requests: none yet", WorkspaceVisuals.SecondaryColor));
            for (var index = 0; index < feedbackLines.Count; index++)
            {
                WorkspaceVisuals.SetLiteral(feedbackLines[index], index < feedback.Count ? feedback[index].Text : "");
                if (index < feedback.Count) feedbackLines[index].color = feedback[index].Color;
            }

            WorkspaceVisuals.SetLiteral(activityCaption, content.ActivityCaption);
            // The newest activity is at the bottom, like a log.
            var first = Math.Max(0, content.Activity.Count - activityLines.Count);
            for (var index = 0; index < activityLines.Count; index++)
            {
                var line = activityLines[index];
                var source = first + index;
                if (source >= content.Activity.Count)
                {
                    WorkspaceVisuals.SetLiteral(line, "");
                    continue;
                }
                var (text, claim) = content.Activity[source];
                WorkspaceVisuals.SetLiteral(line, text);
                if (leaning[index] != claim)
                {
                    leaning[index] = claim;
                    line.havePropertiesChanged = true;
                }
                line.color = claim ? WorkspaceVisuals.ClaimColor : WorkspaceVisuals.TextColor;
            }
        }

        /// <summary>Shows the requests and recent activity under the tabs, or hides them while a section shows there.</summary>
        public void ShowActivity(bool shown)
        {
            foreach (var line in feedbackLines) line.gameObject.SetActive(shown);
            activityCaption.gameObject.SetActive(shown);
            foreach (var line in activityLines) line.gameObject.SetActive(shown);
        }

        /// <summary>
        /// Shows the row for the current mode. A button that stays keeps its state, so a press in
        /// progress, a hover, or its settling time survive the panel refreshing; only buttons the
        /// row no longer needs are hidden.
        /// </summary>
        private void ShowControls(PanelContent content)
        {
            var center = ActionsTop - PanelButton.Height / 2f;
            var right = Right;
            var shown = new HashSet<PanelButton>();
            shownActions.Clear();
            shownPresets.Clear();
            WorkspaceVisuals.SetLiteral(controlsText, "");

            switch (content.Mode)
            {
                case ControlsMode.Actions:
                    shownActions.AddRange(content.Actions);
                    ShowRow(actionButtons, shownActions.ConvertAll(WorkspaceText.Label), center, shown);
                    if (shownActions.Count == 0) SetControlsText(content.WhyNoActions ?? "", Left, right, WorkspaceVisuals.SecondaryColor);
                    break;
                case ControlsMode.Confirm:
                    // The confirmation goes where no action button was, so pressing twice in one place never
                    // confirms. Its place is kept while it cannot be given yet, so Cancel never moves and the
                    // confirmation appears where nothing was.
                    var confirmLabel = content.ConfirmLabel ?? "Confirm";
                    var confirmWidth = confirm.Measure(confirmLabel, 0.2f);
                    if (content.CanConfirm)
                    {
                        confirm.Show(confirmLabel, new Vector2(right - confirmWidth / 2f, center), confirmWidth, confirm: true);
                        shown.Add(confirm);
                    }
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
            WorkspaceVisuals.SetLiteral(controlsText, text);
            controlsText.color = color;
            controlsText.rectTransform.localPosition = new Vector3(left, ActionsTop, -0.001f);
            controlsText.rectTransform.sizeDelta = new Vector2(Math.Max(0.05f, right - left), PanelButton.Height);
        }

        private TextMeshPro Line(string name, float size, Color color, float top, float width, float height, bool wrap = false)
        {
            var text = WorkspaceVisuals.Text(transform, name, size, color, new Vector2(width, height), TextAlignmentOptions.TopLeft, wrap,
                WorkspaceVisuals.PanelTextOrder);
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
