#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Oculus.Interaction;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Look and pinch, through the Interaction SDK's gaze interaction (v207): a gaze interactor that
    /// hovers what the person looks at, and selects it on a pinch of either hand, at any height. It
    /// uses the gaze conecaster Stage.unity carries, whose gaze is the head's direction where eyes are
    /// not tracked, as on a Quest 3 (<c>StageSetup</c>). This component is the interactor's selector:
    /// a pinch selects only when the workspace director says a gaze peek is showing and no hand ray or
    /// finger is on a target (the <c>pinchTarget</c> it is given), and never while the palm faces the
    /// eyes, the headset's menu gesture. Pointing with a ray and poking work as before.
    /// </summary>
    public sealed class GazeHover : MonoBehaviour, ISelector
    {
        private readonly List<Pincher> hands = new List<Pincher>();
        private Func<(string? Target, PinchRefusal Block)> pinchDecision = () => (null, PinchRefusal.NoGazePeek);
        private Pincher? selectingWith;

        public event Action? WhenSelected;

        public event Action? WhenUnselected;

        /// <summary>The workstream the current pinch is for, from when it began until it ends; null otherwise.</summary>
        public string? Armed { get; private set; }

        /// <summary>How many hands can look and pinch: none where the scene has no seated hand rays.</summary>
        public int Hands => hands.Count;

        /// <summary>The gaze interactor, or null when the scene has no gaze conecaster or no camera.</summary>
        public static GazeHover? Create(Transform parent, Func<(string? Target, PinchRefusal Block)> pinchDecision)
        {
            var conecaster = FindAnyObjectByType<GazeConecaster>();
            var head = WorkspaceVisuals.Head;
            if (conecaster == null || head == null) return null;
            var go = new GameObject("Gaze hover");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var hover = go.AddComponent<GazeHover>();
            hover.pinchDecision = pinchDecision;
            // The hands the rays use; found while the app lacks focus too, when the rays are hidden.
            foreach (var ray in FindObjectsByType<SeatedHandRay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var hand = ray.Hand;
                if (hand == null) continue;
                var pinch = go.AddComponent<IndexPinchSelector>();
                pinch.InjectAllIndexPinchSelector(hand);
                hover.hands.Add(new Pincher(hover, pinch, ray));
            }
            var interactor = go.AddComponent<GazeInteractor>();
            interactor.InjectAllGazeInteractor(hover, head, conecaster);
            go.SetActive(true);
            return hover;
        }

        private void OnEnable()
        {
            foreach (var hand in hands) hand.Listen(true);
        }

        private void OnDisable()
        {
            foreach (var hand in hands) hand.Listen(false);
            if (selectingWith != null) Released(selectingWith);
        }

        private void Pinched(Pincher hand)
        {
            if (selectingWith != null)
            {
                LogRefusal("another pinch is selecting");
                return;
            }
            if (FocusGuard.InputSuspended)
            {
                LogRefusal("input focus lost");
                return;
            }
            if (hand.Ray.PalmFacesHead)
            {
                LogRefusal("palm faces head");
                return;
            }
            var (target, block) = pinchDecision();
            if (target == null)
            {
                LogRefusal(block.ToString());
                return;
            }
            Armed = target;
            selectingWith = hand;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                "Halcyonic interaction: look pinch accepted by gate for {0}", target);
            WhenSelected?.Invoke();
        }

        private void LogRefusal(string reason) => Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
            "Halcyonic interaction: look pinch refused: {0}", reason);

        private void Released(Pincher hand)
        {
            if (selectingWith != hand) return;
            selectingWith = null;
            Armed = null;
            WhenUnselected?.Invoke();
        }

        /// <summary>One hand's pinch, and the ray that says whether its palm faces the eyes.</summary>
        private sealed class Pincher
        {
            private readonly IndexPinchSelector pinch;
            private readonly Action pinched;
            private readonly Action released;

            public Pincher(GazeHover owner, IndexPinchSelector pinch, SeatedHandRay ray)
            {
                this.pinch = pinch;
                Ray = ray;
                pinched = () => owner.Pinched(this);
                released = () => owner.Released(this);
            }

            public SeatedHandRay Ray { get; }

            public void Listen(bool listening)
            {
                pinch.WhenSelected -= pinched;
                pinch.WhenUnselected -= released;
                if (!listening) return;
                pinch.WhenSelected += pinched;
                pinch.WhenUnselected += released;
            }
        }
    }
}
