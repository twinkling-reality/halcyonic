#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The redesign's name tag under a character on the shelf (DESIGN's BUILD-SPEC-v4 2.2): FIELD's tag in the panel's
    /// material, one line of the task's name 0.8 degrees under the character's feet, saying Needs You beside it where the
    /// task needs the person (<see cref="CharacterLabel.NeedsPerson"/>), the picked one ringed. While the
    /// redesign draws, the character's older label (badge, plate and marks) does not stand; off, the older label stands as
    /// ever and the tag does not.
    /// </summary>
    public sealed class RoomNameTag : MonoBehaviour
    {
        /// <summary>The name tag stands this far under the character's feet (DESIGN 1.1).</summary>
        public const float UnderDegrees = 0.8f;

        private CharacterView view = null!;
        private RoomTag? tag;
        private bool picked;

        /// <summary>The tag as it stands, while the redesign draws the shelf; null otherwise.</summary>
        public RoomTag? Tag => tag != null && tag.gameObject.activeSelf ? tag : null;

        /// <summary>Whether this character is the picked one, its task open beside the menu: its tag wears the ring.</summary>
        public bool Picked
        {
            get => picked;
            set
            {
                if (picked == value) return;
                picked = value;
                Show();
            }
        }

        /// <summary>The tag for <paramref name="character"/>, made once; it follows what the character shows.</summary>
        public static RoomNameTag On(CharacterView character)
        {
            var made = character.GetComponent<RoomNameTag>();
            if (made != null) return made;
            made = character.gameObject.AddComponent<RoomNameTag>();
            made.view = character;
            character.Shown += made.Show;
            character.LabelsPlaced += made.Place;
            made.Show();
            return made;
        }

        /// <summary>Stands the tag, or the older label, as the redesign is now.</summary>
        public void Show()
        {
            if (view == null) return;
            if (!RoomDesign.Enabled)
            {
                tag?.Hide();
                if (!view.Label.gameObject.activeSelf) view.Label.gameObject.SetActive(true);
                return;
            }
            view.Label.gameObject.SetActive(false);
            tag ??= RoomTag.Create(view.Labels);
            // Where the task needs the person the tag says so in words beside the name (ADR 0023; AUDIT, 2026-11-11).
            tag.Show(LineWords.TheirName(view.Title), picked, needsYou: view.Presentation is CharacterPresentation shown && CharacterLabel.Of(shown).NeedsPerson,
                mostWordsDp: RoomTag.NameWordsMostDp);
            Place();
        }

        /// <summary>Stands the tag where its character's labels now stand, its words as they are: each time the shelf places them.</summary>
        public void Place()
        {
            if (tag == null || !tag.gameObject.activeSelf) return;
            tag.transform.localRotation = Quaternion.identity;
            // Under the feet (BUILD-SPEC-v4 2.2), and under the body as the eyes see it too: the tag stands on the shelf lip's plane,
            // in front of the character, so seen from above the body would rise over a tag laid only under the lip (AUDIT
            // 2026-11-16 13:58Z, the lead's 15:0xZ). Its top stands the gap under the lip, or under the body's lowest point carried
            // along the eyes' line onto that plane, whichever is lower.
            var gap = RoomType.Units(UnderDegrees * RoomGeometry.DpPerDegree);
            var top = Mathf.Min(-gap, BodyFootOnPlane() - gap);
            tag.transform.localPosition = new Vector3(0f, top - tag.Size.y / 2f, 0f);
        }

        /// <summary>
        /// The lowest the character's body stands on the tag's plane (the labels' space, z = 0) as the eyes the shelf was placed
        /// for see it; where no eyes are known yet, its lowest point straight across.
        /// </summary>
        private float BodyFootOnPlane()
        {
            var labels = view.Labels;
            var from = view.ShelfViewer is Vector3 eyes ? labels.InverseTransformPoint(eyes) : (Vector3?)null;
            var lowest = float.PositiveInfinity;
            foreach (var corner in view.BodyCorners())
            {
                var at = labels.InverseTransformPoint(corner);
                var y = at.y;
                if (from is Vector3 eye && Mathf.Abs(at.z - eye.z) > 1e-6f) y = eye.y + (-eye.z / (at.z - eye.z)) * (at.y - eye.y);
                lowest = Mathf.Min(lowest, y);
            }
            return float.IsPositiveInfinity(lowest) ? 0f : lowest;
        }

        private void OnDestroy()
        {
            if (view != null)
            {
                view.Shown -= Show;
                view.LabelsPlaced -= Place;
            }
        }
    }
}
