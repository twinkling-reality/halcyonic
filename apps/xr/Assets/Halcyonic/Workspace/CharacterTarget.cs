#nullable enable
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// What makes a character peekable and openable by hand: a sphere around its body for a hand ray
    /// and for the gaze, and a surface just in front of it, always facing the person, for a poke.
    /// Attached at runtime to the body of each view <see cref="CharacterStage"/> creates, so it moves
    /// with the body and the character's own code does not change.
    /// </summary>
    public sealed class CharacterTarget : MonoBehaviour
    {
        /// <summary>
        /// The sphere that holds the body in every state (<see cref="CharacterView.BodyRadius"/>):
        /// neighbours on the arc stand farther apart than two of these, so targets never overlap.
        /// </summary>
        private const float RayRadius = CharacterView.BodyRadius;

        /// <summary>Just in front of that sphere, toward the person.</summary>
        private const float PokeDistance = CharacterView.BodyRadius * 1.15f;

        private static readonly Vector2 PokeSize = Vector2.one * (2f * CharacterView.BodyRadius);

        /// <summary>
        /// How near a fingertip must come to the poke surface to hover it, in the character's units,
        /// and at most the Interaction SDK's default of 0.15 m: a character on a desk, within reach of
        /// hands that type, is hovered only by a finger about to touch it.
        /// </summary>
        private const float PokeReach = 0.6f * CharacterView.BodyRadius;

        private const float SdkPokeEnter = 0.15f;
        private const float SdkPokeExit = 0.2f;

        private Transform pokeSurface = null!;
        private float pokeScale = -1f;

        public string WorkstreamId { get; private set; } = "";

        public CharacterView View { get; private set; } = null!;

        /// <summary>The sphere: hand rays and the gaze.</summary>
        public PointerTarget Ray { get; private set; } = null!;

        public PointerTarget Poke { get; private set; } = null!;

        /// <summary>A hand ray or a finger is on the character.</summary>
        public bool HandHovered => Ray.HandHovered || Poke.HandHovered;

        public bool GazeHovered => Ray.GazeHovered;

        public bool Hovered => HandHovered || GazeHovered;

        /// <summary>The center of the character's body, where the peek and the workspace attach.</summary>
        public Vector3 BodyPosition => transform.position;

        /// <summary>How much the stage scales the character; its targets scale with it.</summary>
        public float Scale => transform.lossyScale.x;

        public static CharacterTarget Attach(CharacterView view, string workstreamId)
        {
            var host = new GameObject("Workspace target");
            host.transform.SetParent(view.Body, false);
            var target = host.AddComponent<CharacterTarget>();
            target.WorkstreamId = workstreamId;
            target.View = view;
            target.Ray = PointerTarget.Sphere(host, RayRadius, gaze: true);
            var poke = new GameObject("Poke surface");
            poke.transform.SetParent(host.transform, false);
            target.pokeSurface = poke.transform;
            target.Poke = PointerTarget.Rectangle(poke, PokeSize, ray: false, poke: true);
            target.FacePerson();
            return target;
        }

        private void LateUpdate() => FacePerson();

        /// <summary>The poke surface stays between the body and the person, wherever the person stands.</summary>
        private void FacePerson()
        {
            var scale = Scale;
            var rotation = WorkspaceVisuals.FacingPerson(BodyPosition);
            pokeSurface.SetPositionAndRotation(BodyPosition - rotation * Vector3.forward * (PokeDistance * scale), rotation);
            if (Mathf.Abs(scale - pokeScale) > 0.01f)
            {
                // The stage moved it between the arc and a desk: the fingertip's reach follows its size.
                pokeScale = scale;
                var enter = Mathf.Min(SdkPokeEnter, PokeReach * scale);
                Poke.SetPokeReach(enter, Mathf.Min(SdkPokeExit, enter * SdkPokeExit / SdkPokeEnter));
            }
        }
    }
}
