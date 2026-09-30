#nullable enable
using Halcyonic.Client;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using Numerics = System.Numerics;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The pose and the on state of a hand ray for a seated person, in place of the Interaction SDK's
    /// <see cref="HandPointerPose"/> on each hand ray of Meta's rig. The SDK's hand ray is on whenever
    /// the headset reports a valid pointer pose, which runs from the shoulder through the hand, so it
    /// reaches the characters only from a hand held near shoulder height. This one runs through the
    /// index knuckle from a pivot below the shoulder, and is on unless the palm faces the floor
    /// (resting or typing) or the eyes (the system gesture): see <see cref="SeatedPointing"/>.
    /// <c>StageSetup</c> puts it on the rig's pointer pose object, disables the SDK's pointer pose
    /// there, and gives it the rig's hand and headset.
    /// </summary>
    public sealed class SeatedHandRay : MonoBehaviour, IActiveState
    {
        [Tooltip("The hand whose ray this is.")]
        [SerializeField, Interface(typeof(IHand))]
        private Object _hand = null!;

        [Tooltip("The headset, for where the person's eyes are.")]
        [SerializeField, Interface(typeof(IHmd))]
        private Object _hmd = null!;

        private bool started;
        private IHand? hand;

        /// <summary>The hand, also before this object first becomes active, as while the app starts without focus.</summary>
        public IHand Hand => hand ??= (_hand as IHand)!;

        private IHmd? Hmd => _hmd as IHmd;

        /// <summary>The ray is on (<see cref="IActiveState"/>).</summary>
        public bool Active { get; private set; }

        /// <summary>The palm faces the eyes, the headset's own menu gesture: a pinch then belongs to the system.</summary>
        public bool PalmFacesHead { get; private set; }

        private void Start()
        {
            this.BeginStart(ref started);
            this.AssertField(Hand, nameof(Hand));
            this.EndStart(ref started);
        }

        private void OnEnable()
        {
            if (started) Hand.WhenHandUpdated += OnHandUpdated;
        }

        private void OnDisable()
        {
            if (started) Hand.WhenHandUpdated -= OnHandUpdated;
            Active = false;
            PalmFacesHead = false;
        }

        private void OnHandUpdated()
        {
            if (!TryGetEyes(out var eyes)
                || !Hand.GetJointPose(HandJointId.HandIndex1, out var knuckle)
                || !Hand.GetJointPose(HandJointId.HandPalm, out var palm))
            {
                Active = false;
                PalmFacesHead = false;
                return;
            }
            var palmar = Hand.Handedness == Handedness.Right ? Constants.RightPalmar : Constants.LeftPalmar;
            var posture = new HandPosture(
                Hand.IsConnected && Hand.IsTrackedDataValid && Hand.IsHighConfidence,
                Hand.Handedness == Handedness.Right,
                ToNumerics(knuckle.position),
                ToNumerics(palm.position),
                ToNumerics(palm.rotation * palmar),
                Hand.Scale);
            var ray = SeatedPointing.Aim(new HeadPose(ToNumerics(eyes.position), ToNumerics(eyes.rotation * Vector3.forward)), posture);
            Active = ray.Active;
            PalmFacesHead = ray.PalmFacesHead;
            var direction = ToUnity(ray.Direction);
            // The rig's ray interactor casts along this transform's forward, from its position.
            transform.SetPositionAndRotation(ToUnity(ray.Origin),
                Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up));
        }

        private bool TryGetEyes(out Pose eyes)
        {
            if (Hmd != null && Hmd.TryGetRootPose(out eyes)) return true;
            var head = WorkspaceVisuals.Head;
            eyes = head != null ? new Pose(head.position, head.rotation) : default;
            return head != null;
        }

        private static Numerics.Vector3 ToNumerics(Vector3 value) => new Numerics.Vector3(value.x, value.y, value.z);

        private static Vector3 ToUnity(Numerics.Vector3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
