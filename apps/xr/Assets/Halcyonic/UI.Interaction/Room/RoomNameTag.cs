#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The redesign's name tag under a character on the shelf (DESIGN's BUILD-SPEC-v4 2.2): FIELD's tag in the panel's
    /// material, one line of the task's name 0.8 degrees under the character's feet, the picked one ringed. While the
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
            tag.Show(LineWords.TheirName(view.Title), picked);
            tag.transform.localRotation = Quaternion.identity;
            tag.transform.localPosition = new Vector3(0f, -tag.Size.y / 2f - RoomType.Units(UnderDegrees * RoomGeometry.DpPerDegree), 0f);
        }

        private void OnDestroy()
        {
            if (view != null) view.Shown -= Show;
        }
    }
}
