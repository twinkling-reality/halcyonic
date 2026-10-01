#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The peek: a card for one character (<see cref="PeekCardView"/>) with its state, the reason in
    /// a sentence or two and what opening it is for, fading in and out as
    /// <see cref="Halcyonic.Client.PeekChoice"/> decides. It hangs just under the character's label,
    /// where the banner steps aside for it (<see cref="AmbientCover"/>); while a panel is open under
    /// the labels, or the characters stand on a surface, it stands just above the highest the
    /// character reaches instead (ADR 0023). Beside a window it stands out from its character, away
    /// from the window's lane, reaching no nearer the lane than the character does. It faces the
    /// eyes, nearer than the characters, at <see cref="WorkspaceVisuals.PeekDistance"/> or less, so no
    /// character hides it.
    /// </summary>
    public sealed class PeekLabel : MonoBehaviour
    {
        /// <summary>The angle between the card and what it hangs under or stands over: more than the degree kept between things.</summary>
        private const float GapDegrees = 1.25f;

        /// <summary>How near the card comes, as a share of the character's distance, when the characters stand near.</summary>
        private const float NearerShare = 0.8f;

        private PeekCardView card = null!;
        private CharacterTarget? character;
        private IEnumerable<CharacterTarget>? neighbors;
        private bool above;
        private bool besideWindow;

        /// <summary>The card, for renders and their checks.</summary>
        public PeekCardView Card => card;

        public static PeekLabel Create(Transform parent)
        {
            var go = new GameObject("Peek");
            go.transform.SetParent(parent, false);
            var peek = go.AddComponent<PeekLabel>();
            peek.card = PeekCardView.Create(go.transform);
            AmbientCover.Add(go, panel: false);
            go.SetActive(false);
            return peek;
        }

        /// <summary>
        /// Shows <paramref name="peek"/> for <paramref name="target"/>, as visible as
        /// <paramref name="opacity"/>, from 0 to 1, above the character when <paramref name="aboveCharacter"/>,
        /// and under its label and those of <paramref name="others"/> it would pass in front of otherwise;
        /// out from the window's lane when the characters stand <paramref name="beside"/> one.
        /// </summary>
        public void Show(CharacterTarget target, IEnumerable<CharacterTarget> others, PeekCard peek, float opacity, bool aboveCharacter, bool beside = false)
        {
            besideWindow = beside;
            if (opacity <= 0f)
            {
                Hide();
                return;
            }
            character = target;
            neighbors = others;
            above = aboveCharacter;
            gameObject.SetActive(true);
            card.Show(peek);
            card.Fade(Mathf.SmoothStep(0f, 1f, opacity));
            Place();
        }

        public void Hide()
        {
            character = null;
            neighbors = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (character == null)
            {
                Hide();
                return;
            }
            Place();
        }

        /// <summary>
        /// Hangs the card just under the character's label, or stands it just over the highest its
        /// body reaches: its middle where that puts it, facing the eyes with no roll, at the angular
        /// size it was built for.
        /// </summary>
        private void Place()
        {
            if (character == null) return;
            var eyes = WorkspaceVisuals.HeadPosition;
            var view = character.View;
            var place = view.transform.position;
            var yaw = Mathf.Atan2(place.x - eyes.x, place.z - eyes.z) * Mathf.Rad2Deg;
            // The card's size as angles: it is built in units of its distance.
            var half = Mathf.Atan(card.Height / 2f) * Mathf.Rad2Deg;
            var halfWidth = Mathf.Atan(PeekCardView.Width / 2f) * Mathf.Rad2Deg;
            if (besideWindow)
            {
                // Out from the lane: the card's inner edge where the character's label's is, never nearer the lane.
                var outward = Mathf.Sign(view.transform.localPosition.x);
                var labelHalf = Mathf.Atan2(view.LabelHalfWidth * view.transform.lossyScale.x, Vector3.Distance(eyes, place)) * Mathf.Rad2Deg;
                yaw += outward * Mathf.Max(0f, halfWidth - labelHalf);
            }
            float edge;
            if (above)
            {
                edge = Elevation(eyes, place + Vector3.up * (CharacterView.HighestReach * view.transform.lossyScale.y)) + GapDegrees;
            }
            else
            {
                // Under its own label, and under any neighbour's it reaches across, whichever is lower.
                edge = Elevation(eyes, place + Vector3.up * (view.LabelBottom * view.transform.lossyScale.y));
                if (neighbors != null)
                {
                    foreach (var other in neighbors)
                    {
                        if (other == null || other == character) continue;
                        var otherView = other.View;
                        var otherPlace = otherView.transform.position;
                        var scale = otherView.transform.lossyScale.y;
                        var otherYaw = Mathf.Atan2(otherPlace.x - eyes.x, otherPlace.z - eyes.z) * Mathf.Rad2Deg;
                        var otherHalf = Mathf.Atan2(otherView.LabelHalfWidth * scale, Vector3.Distance(eyes, otherPlace)) * Mathf.Rad2Deg;
                        if (Mathf.Abs(Mathf.DeltaAngle(yaw, otherYaw)) >= halfWidth + otherHalf + GapDegrees) continue;
                        edge = Mathf.Min(edge, Elevation(eyes, otherPlace + Vector3.up * (otherView.LabelBottom * scale)));
                    }
                }
                edge -= GapDegrees;
            }
            var elevation = above ? edge + half : edge - half;
            var distance = Mathf.Min(WorkspaceVisuals.PeekDistance, NearerShare * Vector3.Distance(eyes, place));
            var direction = Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward;
            transform.SetPositionAndRotation(eyes + direction * distance, Quaternion.LookRotation(direction, Vector3.up));
            transform.localScale = Vector3.one * distance;
            // The card hangs from its top edge's middle; its own middle goes where the direction points.
            card.transform.localPosition = new Vector3(0f, card.Height / 2f, 0f);
        }

        /// <summary>The elevation of a point from the eyes, in degrees, up from eye level.</summary>
        private static float Elevation(Vector3 eyes, Vector3 point)
        {
            var toward = point - eyes;
            return Mathf.Atan2(toward.y, Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 0.01f)) * Mathf.Rad2Deg;
        }
    }
}
