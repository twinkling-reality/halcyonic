#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The whole request an approval answers, under the actions while its confirmation is asked:
    /// wrapped over the details area and, when it does not fit there, in parts the person steps
    /// through with the buttons in the tab row, the caption saying which part shows. Nothing of it is
    /// ever cut: the parts together are the whole request, and <see cref="WorkspaceSteering"/>
    /// offers the approval's confirmation only once the last part has shown.
    /// </summary>
    public sealed class RequestView : MonoBehaviour
    {
        private const float ButtonGap = 0.016f;

        private TextMeshPro caption = null!;
        private TextMeshPro text = null!;
        private PanelButton previous = null!;
        private PanelButton next = null!;
        private string? shown;

        /// <summary>Raised when a press shows another part.</summary>
        public event Action? Turned;

        /// <summary>The part showing, from 1.</summary>
        public int Part { get; private set; } = 1;

        /// <summary>How many parts the request takes.</summary>
        public int Parts { get; private set; } = 1;

        /// <summary>The label that shows the request, for the editor's check.</summary>
        public TextMeshPro Text => text;

        public static RequestView Create(Transform panel, Func<bool> accepting)
        {
            var go = new GameObject("Request");
            go.transform.SetParent(panel, false);
            var view = go.AddComponent<RequestView>();
            view.Build(accepting);
            go.SetActive(false);
            return view;
        }

        /// <summary>Shows the request from its first part, unless it is showing already.</summary>
        public void Show(string request)
        {
            if (request == shown && gameObject.activeSelf) return;
            shown = request;
            gameObject.SetActive(true);
            WorkspaceVisuals.SetLiteral(text, request);
            text.pageToDisplay = 1;
            text.ForceMeshUpdate(true);
            Parts = Mathf.Max(1, text.textInfo.pageCount);
            Part = 1;
            Layout();
        }

        public void Hide()
        {
            shown = null;
            gameObject.SetActive(false);
        }

        /// <summary>Shows the part <paramref name="by"/> after the one showing, or before it when negative.</summary>
        public void Turn(int by)
        {
            var part = Mathf.Clamp(Part + by, 1, Parts);
            if (part == Part) return;
            Part = part;
            text.pageToDisplay = part;
            Layout();
            Turned?.Invoke();
        }

        private void Build(Func<bool> accepting)
        {
            caption = WorkspaceVisuals.Text(transform, "Caption", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                new Vector2(WorkspacePanel.DetailsWidth, WorkspacePanel.TabsHeight), TextAlignmentOptions.MidlineLeft, order: WorkspaceVisuals.PanelTextOrder);
            caption.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, WorkspacePanel.TabsTop, -0.001f);
            text = WorkspaceVisuals.Text(transform, "Whole request", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                new Vector2(WorkspacePanel.DetailsWidth, WorkspacePanel.DetailsTop - WorkspacePanel.DetailsBottom), TextAlignmentOptions.TopLeft,
                wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            // Parts, never an ellipsis: what does not fit shows on the next part.
            text.overflowMode = TextOverflowModes.Page;
            text.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, WorkspacePanel.DetailsTop, -0.001f);
            previous = Button("Previous part", accepting, () => Turn(-1));
            next = Button("Next part", accepting, () => Turn(1));
        }

        private PanelButton Button(string name, Func<bool> accepting, Action pressed)
        {
            var button = PanelButton.Create(transform, name, WorkspacePanel.TabsHeight, WorkspaceVisuals.DetailSize);
            button.Accepting = accepting;
            button.Pressed += pressed;
            return button;
        }

        /// <summary>
        /// The caption at the left of the tab row, and the buttons at its right, each always in its
        /// own place, so a button that goes never leaves another where it was.
        /// </summary>
        private void Layout()
        {
            var center = WorkspacePanel.TabsTop - WorkspacePanel.TabsHeight / 2f;
            var right = WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth;
            var nextWidth = next.Measure(WorkspaceText.NextPart, 0.12f);
            var previousWidth = previous.Measure(WorkspaceText.PreviousPart, 0.12f);
            if (Part < Parts) next.Show(WorkspaceText.NextPart, new Vector2(right - nextWidth / 2f, center), nextWidth);
            else next.Hide();
            var previousRight = right - nextWidth - ButtonGap;
            if (Part > 1) previous.Show(WorkspaceText.PreviousPart, new Vector2(previousRight - previousWidth / 2f, center), previousWidth);
            else previous.Hide();
            var captionWidth = Parts > 1 ? previousRight - previousWidth - ButtonGap - WorkspacePanel.DetailsLeft : WorkspacePanel.DetailsWidth;
            caption.rectTransform.sizeDelta = new Vector2(captionWidth, WorkspacePanel.TabsHeight);
            WorkspaceVisuals.SetLiteral(caption, WorkspaceText.RequestCaption(Part, Parts));
        }
    }
}
