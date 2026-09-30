#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The answer to What do you need from me?, in the workspace's details area: what the work asks
    /// for, the request as the runtime reported it, wrapped over up to three rows, and what each answer
    /// does. The client core writes every word (<see cref="WorkspaceText.NeedFromYou"/>). A longer
    /// request ends in an ellipsis here; the whole of it shows, in parts and never cut, when the person
    /// chooses Approve or Deny (<see cref="RequestView"/>). Every label shows its text by the one rule
    /// for text Halcyonic did not write.
    /// </summary>
    public sealed class NeedView : MonoBehaviour
    {
        private const float Pitch = 0.027f;
        private const int RequestRows = 3;

        private TextMeshPro asks = null!;
        private TextMeshPro request = null!;
        private TextMeshPro notes = null!;
        private NeedAnswer? shown;

        public static NeedView Create(Transform panel)
        {
            var go = new GameObject("What do you need from me");
            go.transform.SetParent(panel, false);
            var view = go.AddComponent<NeedView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        /// <summary>Every label it draws, for the editor's check that none interprets what it shows.</summary>
        public IEnumerable<TextMeshPro> Labels => new[] { asks, request, notes };

        public void Show(NeedAnswer answer)
        {
            if (shown != null && shown.Asks == answer.Asks && shown.Request == answer.Request
                && string.Join("\n", shown.Notes) == string.Join("\n", answer.Notes)) return;
            shown = answer;
            WorkspaceVisuals.SetLiteral(asks, answer.Asks);
            var top = WorkspacePanel.DetailsTop - Pitch;
            request.gameObject.SetActive(answer.Request != null);
            if (answer.Request != null)
            {
                WorkspaceVisuals.SetLiteral(request, answer.Request);
                request.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top, -0.001f);
                request.rectTransform.sizeDelta = new Vector2(WorkspacePanel.DetailsWidth, RequestRows * Pitch);
                request.ForceMeshUpdate();
                top -= Mathf.Clamp(request.textInfo.lineCount, 1, RequestRows) * Pitch + 0.006f;
            }
            WorkspaceVisuals.SetLiteralLines(notes, answer.Notes);
            notes.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top, -0.001f);
            notes.rectTransform.sizeDelta = new Vector2(WorkspacePanel.DetailsWidth, top - WorkspacePanel.DetailsBottom);
        }

        private void Build()
        {
            asks = WorkspaceVisuals.Text(transform, "Asks", WorkspaceVisuals.DetailSize, WorkspaceVisuals.AttentionColor,
                new Vector2(WorkspacePanel.DetailsWidth, Pitch), TextAlignmentOptions.TopLeft, order: WorkspaceVisuals.PanelTextOrder);
            asks.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, WorkspacePanel.DetailsTop, -0.001f);
            request = WorkspaceVisuals.Text(transform, "Request", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                new Vector2(WorkspacePanel.DetailsWidth, RequestRows * Pitch), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            notes = WorkspaceVisuals.Text(transform, "What answers do", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor,
                new Vector2(WorkspacePanel.DetailsWidth, Pitch), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
        }
    }
}
