#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// New Project as the redesign's shelf's first place (DESIGN's BUILD-SPEC-v4 2.2; the lead, 2026-11-11): the action with
    /// the plus glyph on the name tags' line, at the person's left of the characters, with no squircle and no tag of its
    /// own. It stands only while the redesign draws and the shelf stands, and a press raises <see cref="Pressed"/>, which
    /// the director turns into New Project, as the menu's own New Project does.
    /// </summary>
    public sealed class RoomNewProjectPlace : MonoBehaviour
    {
        /// <summary>The action's words: the product's own.</summary>
        public const string Words = "New Project";

        private CharacterStage stage = null!;
        private Transform holder = null!;
        private RoomButton button = null!;

        /// <summary>Raised when the action is pressed.</summary>
        public event Action? Pressed;

        /// <summary>The action as it stands now; null while it does not.</summary>
        public RoomButton? Button => holder != null && holder.gameObject.activeSelf ? button : null;

        /// <summary>The place for <paramref name="shelf"/>, made once.</summary>
        public static RoomNewProjectPlace On(CharacterStage shelf)
        {
            var made = shelf.GetComponent<RoomNewProjectPlace>();
            if (made != null) return made;
            made = shelf.gameObject.AddComponent<RoomNewProjectPlace>();
            made.stage = shelf;
            made.holder = new GameObject("New Project place").transform;
            made.holder.SetParent(shelf.transform, false);
            made.button = RoomButton.Create(made.holder, "New Project");
            made.button.Pressed += () => made.Pressed?.Invoke();
            made.Place();
            return made;
        }

        private void LateUpdate() => Place();

        /// <summary>How far New Project's drawn place stands from a character's drawn tag beside it, at the least, as the eyes see it, in degrees.</summary>
        public const float ClearOfTagDegrees = 1f;

        private static readonly List<RoomNameTag> NameTags = new();
        private static readonly List<TMPro.TMP_Text> Texts = new();
        private static readonly List<RoomSurface> Plates = new();
        private static readonly List<Renderer> Marks = new();

        /// <summary>
        /// Turns New Project's <paramref name="place"/> about the <paramref name="eyes"/>, to the person's left and never toward the
        /// characters, until its drawn right edge (the words and the glyph) stands <see cref="ClearOfTagDegrees"/> left of the drawn left edge
        /// of every tag under <paramref name="shelf"/> (its words and its glass): a long name's tag runs past half a place, and today's place
        /// stood on it (FIELD's F236, AUDIT 2026-11-16T17:46Z). Measured each time from what is drawn; nothing is allocated.
        /// </summary>
        public static void StandClear(Transform place, RoomButton button, Transform shelf, Vector3 eyes) => Turn(place, eyes, ClearTurn(place, button, shelf, eyes));

        /// <summary>
        /// How long the place takes to settle on a new turn when a tag beside it widens or narrows (Needs You arriving, a name changed): it
        /// eases there, never jumps under the person's eye (AUDIT 2026-11-16T18:04Z), settling within about four times this.
        /// </summary>
        public const float EaseSeconds = 0.15f;

        private float turned;
        private float turning;
        private bool stood;

        /// <summary>
        /// The turn <paramref name="turned"/> eased toward <paramref name="target"/> over <paramref name="seconds"/>, both in degrees, with its speed
        /// in <paramref name="speed"/>; once within a hundredth of a degree it is the target exactly and still, so a steady tag holds the place
        /// steady to the pixel.
        /// </summary>
        public static float Eased(float turned, float target, ref float speed, float seconds)
        {
            var next = Mathf.SmoothDamp(turned, target, ref speed, EaseSeconds, Mathf.Infinity, Mathf.Max(seconds, 0f));
            if (Mathf.Abs(next - target) < 0.01f) { speed = 0f; return target; }
            return next;
        }

        /// <summary>Turns <paramref name="place"/> about the vertical through <paramref name="eyes"/> by <paramref name="degrees"/> to the person's left.</summary>
        public static void Turn(Transform place, Vector3 eyes, float degrees)
        {
            if (degrees <= 0f) return;
            var turn = Quaternion.AngleAxis(-degrees, Vector3.up);
            place.SetPositionAndRotation(eyes + turn * (place.position - eyes), turn * place.rotation);
        }

        /// <summary>
        /// How many degrees to the person's left <paramref name="place"/>, as it stands, must turn about the eyes for its drawn words and glyph
        /// to stand <see cref="ClearOfTagDegrees"/> left of every drawn tag under <paramref name="shelf"/>; 0 where they already do.
        /// </summary>
        public static float ClearTurn(Transform place, RoomButton button, Transform shelf, Vector3 eyes)
        {
            var ahead = place.position - eyes;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-6f) return 0f;
            var tagLeft = float.PositiveInfinity;
            shelf.GetComponentsInChildren(false, NameTags);
            foreach (var nameTag in NameTags)
            {
                if (nameTag.Tag is not RoomTag tag) continue;
                tag.GetComponentsInChildren(false, Texts);
                foreach (var text in Texts) tagLeft = Mathf.Min(tagLeft, Across(text.GetComponent<Renderer>(), eyes, ahead, left: true));
                tag.GetComponentsInChildren(false, Plates);
                foreach (var plate in Plates) tagLeft = Mathf.Min(tagLeft, Across(plate.Glass, eyes, ahead, left: true));
            }
            if (float.IsPositiveInfinity(tagLeft)) return 0f;
            var placeRight = Across(button.Words.GetComponent<Renderer>(), eyes, ahead, left: false);
            button.Glyph.GetComponentsInChildren(false, Marks);
            foreach (var mark in Marks) placeRight = Mathf.Max(placeRight, Across(mark, eyes, ahead, left: false));
            if (float.IsNegativeInfinity(placeRight)) return 0f;
            return Mathf.Max(0f, ClearOfTagDegrees - (tagLeft - placeRight));
        }

        /// <summary>
        /// The bearing, about the vertical through the eyes, of a drawn part's leftmost (or rightmost) corner from the bearing
        /// <paramref name="ahead"/>, in degrees, rightward positive; infinite the far way for a part not drawn.
        /// </summary>
        private static float Across(Renderer? part, Vector3 eyes, Vector3 ahead, bool left)
        {
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) return left ? float.PositiveInfinity : float.NegativeInfinity;
            var bounds = part.bounds;
            var most = left ? float.PositiveInfinity : float.NegativeInfinity;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(corner % 2 == 0 ? bounds.min.x : bounds.max.x, corner < 4 ? bounds.min.y : bounds.max.y, corner % 4 < 2 ? bounds.min.z : bounds.max.z);
                var away = point - eyes;
                away.y = 0f;
                var bearing = Vector3.SignedAngle(ahead, away, Vector3.up);
                most = left ? Mathf.Min(most, bearing) : Mathf.Max(most, bearing);
            }
            return most;
        }

        /// <summary>
        /// Stands the action at the front lip of the shelf's first place, facing the eyes the shelf was placed for and sized
        /// for them as the characters' labels are, its middle on the tags' line 0.8 degrees under the characters' feet.
        /// </summary>
        public void Place()
        {
            if (stage == null || holder == null) return;
            var stands = RoomDesign.Enabled && stage.Visible;
            if (holder.gameObject.activeSelf != stands) holder.gameObject.SetActive(stands);
            if (!stands) { stood = false; return; }
            var shelf = stage.Shelf;
            // The characters face the eyes, so the shelf's front lip is toward the eyes from each place.
            var lip = shelf.TransformPoint(stage.PlaceOf(0)) - shelf.forward * (AgentShelfDraft.Depth / 2f) * shelf.lossyScale.z;
            var away = lip - stage.ShelfViewer;
            var horizontal = new Vector3(away.x, 0f, away.z);
            if (horizontal.sqrMagnitude < 1e-6f) return;
            holder.SetPositionAndRotation(lip, Quaternion.LookRotation(horizontal, Vector3.up));
            var parent = holder.parent != null ? holder.parent.lossyScale.x : 1f;
            holder.localScale = Vector3.one * (away.magnitude / Mathf.Max(parent, 0.001f));
            var target = RoomType.Units(RoomButton.HeightDp);
            var width = button.Measure(Words, 0f).x;
            var under = RoomType.Units(RoomNameTag.UnderDegrees * RoomGeometry.DpPerDegree);
            button.Show(Words, GlazeIcon.CreateProject, isMain: false, available: true, new Vector2(0f, -under - target / 2f), width);
            // Measured from the place's own spot each frame, so a steady tag gives a steady turn; eased only while it stands, so a place
            // that comes into being stands clear at once.
            var target = ClearTurn(holder, button, shelf, stage.ShelfViewer);
            if (stood) turned = Eased(turned, target, ref turning, Time.deltaTime);
            else { turned = target; turning = 0f; }
            stood = true;
            Turn(holder, stage.ShelfViewer, turned);
        }
    }
}
