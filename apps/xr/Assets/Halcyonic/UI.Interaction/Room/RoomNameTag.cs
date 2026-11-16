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
            tag.transform.localRotation = Quaternion.identity;
            tag.transform.localPosition = new Vector3(0f, -tag.Size.y / 2f - RoomType.Units(UnderDegrees * RoomGeometry.DpPerDegree), 0f);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void MeasureForTheShelf() => CharacterStage.WidestDrawnName = WidestUnder;

        /// <summary>The longest whole-word start of the stand-in name that the tag lays on two lines, per text size.</summary>
        private static (int Version, string Words)? twoLines;

        /// <summary>
        /// The widest tag the shelf can show, stood under the measuring character as the shelf stands a tag (AUDIT's 13:47Z, B'): a name
        /// filling two lines at the tag's bound (the height of every filmed tag; a longer name only grows downward), Needs You, picked.
        /// Null where the redesign is off, so the older label is measured.
        /// </summary>
        private static Transform? WidestUnder(CharacterView view)
        {
            if (!RoomDesign.Enabled) return null;
            var tag = view.Labels.GetComponentInChildren<RoomTag>(true);
            if (tag == null) tag = RoomTag.Create(view.Labels);
            if (twoLines is not { } known || known.Version != GlazeText.Version) twoLines = known = (GlazeText.Version, TwoLines(tag));
            tag.Show(LineWords.TheirName(known.Words), picked: true, needsYou: true, mostWordsDp: RoomTag.NameWordsMostDp);
            tag.transform.localRotation = Quaternion.identity;
            tag.transform.localPosition = new Vector3(0f, -tag.Size.y / 2f - RoomType.Units(UnderDegrees * RoomGeometry.DpPerDegree), 0f);
            tag.Words.ForceMeshUpdate(true);
            return tag.transform;
        }

        private static string TwoLines(RoomTag tag)
        {
            var words = CharacterStage.WidestStandIn.Split(' ');
            var fits = words[0];
            for (var count = 2; count <= words.Length; count++)
            {
                var tried = string.Join(" ", words, 0, count);
                tag.Show(LineWords.TheirName(tried), picked: true, needsYou: true, mostWordsDp: RoomTag.NameWordsMostDp);
                tag.Words.ForceMeshUpdate(true);
                if (tag.Words.textInfo.lineCount > 2) break;
                fits = tried;
            }
            return fits;
        }

        private void OnDestroy()
        {
            if (view != null) view.Shown -= Show;
        }
    }
}
