#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The expanded workspace (ADR 0023): the same workstream as its character, on a
    /// <see cref="PanelFrame"/> at touch distance beside it. Each screen is a <see cref="PanelModel"/>
    /// from <see cref="WorkspaceScreens"/>: the work's title, goal and state along the top, the
    /// person's questions as tabs under them, the answer to the one chosen, and the actions the work
    /// offers on the bar. A section, Understanding or Evaluation, it draws itself under the section's
    /// heading (<see cref="SectionView"/>). It shows what it is given and decides nothing:
    /// <see cref="WorkspaceDirector"/> builds each screen and acts on what is pressed.
    /// </summary>
    public sealed class WorkspacePanel : MonoBehaviour
    {
        private PanelFrame frame = null!;
        private SectionView section = null!;

        /// <summary>The frame drawing each screen.</summary>
        public PanelFrame Frame => frame;

        /// <summary>The section drawn under its heading, for the editor's checks.</summary>
        public SectionView Section => section;

        /// <summary>Every part showing, for the editor's checks that each shows what it was given, whole.</summary>
        public IEnumerable<Component> ShownParts
        {
            get
            {
                foreach (var button in frame.Buttons) yield return button;
                foreach (var label in frame.Labels) yield return label;
                if (!section.gameObject.activeSelf) yield break;
                foreach (var label in section.Labels)
                {
                    if (label.gameObject.activeSelf) yield return label;
                }
            }
        }

        /// <summary>Presses are ignored while this is false.</summary>
        public Func<bool> Accepting
        {
            get => frame.Accepting;
            set => frame.Accepting = value;
        }

        public static WorkspacePanel Create(Transform parent)
        {
            var go = new GameObject("Workspace panel");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<WorkspacePanel>();
            panel.frame = PanelFrame.Create(go.transform, "Frame");
            panel.section = SectionView.Create(panel.frame.Content);
            return panel;
        }

        /// <summary>
        /// The rows a page of a section's answer holds under <paramref name="model"/>'s heading, as
        /// the frame lays the screen out, under <paramref name="provenance"/> with the longest name a
        /// page has before it.
        /// </summary>
        public AnswerRoom Room(PanelModel model, string provenance)
        {
            frame.Show(model);
            return section.Room(frame.CustomBody, frame.Notch, "Step 10 of 10 · " + provenance);
        }

        /// <summary>Draws a screen, and the section under its heading while it shows one.</summary>
        public void Show(PanelModel model, SectionPresentation? shownSection)
        {
            frame.Show(model);
            if (model.CustomBody && shownSection != null) section.Show(shownSection, frame.CustomBody, frame.Notch);
            else section.Hide();
        }
    }
}
