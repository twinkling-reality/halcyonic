#nullable enable
using System;
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

        /// <summary>
        /// Stands the action at the front lip of the shelf's first place, facing the eyes the shelf was placed for and sized
        /// for them as the characters' labels are, its middle on the tags' line 0.8 degrees under the characters' feet.
        /// </summary>
        public void Place()
        {
            if (stage == null || holder == null) return;
            var stands = RoomDesign.Enabled && stage.Visible;
            if (holder.gameObject.activeSelf != stands) holder.gameObject.SetActive(stands);
            if (!stands) return;
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
        }
    }
}
