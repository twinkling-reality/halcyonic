#nullable enable
using System;
using Oculus.Interaction;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Lets the gaze drive the peek: an Interaction SDK gaze interactor that hovers what the person
    /// looks at and never selects. It uses the gaze conecaster Stage.unity carries, whose gaze is the
    /// head's direction where eyes are not tracked, as on a Quest 3 (<c>StageSetup</c>). Opening stays
    /// with the hand ray's pinch and the poke, so a look can never act by itself, and where there is
    /// no gaze the hand ray still peeks.
    /// </summary>
    public sealed class GazeHover : MonoBehaviour, ISelector
    {
        // Gaze only hovers: nothing ever selects through it.
        event Action ISelector.WhenSelected
        {
            add { }
            remove { }
        }

        event Action ISelector.WhenUnselected
        {
            add { }
            remove { }
        }

        /// <summary>The gaze interactor, or null when the scene has no gaze conecaster or no camera.</summary>
        public static GazeHover? Create(Transform parent)
        {
            var conecaster = FindAnyObjectByType<GazeConecaster>();
            var head = WorkspaceVisuals.Head;
            if (conecaster == null || head == null) return null;
            var go = new GameObject("Gaze hover");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var hover = go.AddComponent<GazeHover>();
            var interactor = go.AddComponent<GazeInteractor>();
            interactor.InjectAllGazeInteractor(hover, head, conecaster);
            go.SetActive(true);
            return hover;
        }
    }
}
