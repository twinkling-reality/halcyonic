#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.Workspace;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The room's controls, in their section of the Settings sheet (ADR 0023): a switch between the
    /// real room and a virtual space; when it would help, an offer to allow room access or to set up
    /// the room; and, while the stage stands in front of the person, Make room for a window, which
    /// turns the lineup to their right (<see cref="CharacterStage.SetAside"/>). Under the section's
    /// heading, one line says where the agents are and why; when it changes, the stage's banner shows
    /// it as a short notice, so the news reaches the person without anything coming up in front of
    /// them. The buttons ignore input while <see cref="FocusGuard.InputSuspended"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here is needed to get started: the room placement shows the real room where it works
    /// and the virtual space otherwise by itself.
    /// </remarks>
    internal sealed class RoomControls : MonoBehaviour
    {
        private RoomPlacement placement = null!;
        private CharacterStage? stage;
        private SettingsSection section = null!;
        private GlazeButton switchButton = null!;
        private GlazeButton offerButton = null!;
        private GlazeButton asideButton = null!;
        private string shownLine = "";

        public static RoomControls Create(Transform parent, RoomPlacement placement)
        {
            var go = new GameObject("Room controls");
            go.transform.SetParent(parent, false);
            var controls = go.AddComponent<RoomControls>();
            controls.placement = placement;
            controls.Build();
            return controls;
        }

        /// <summary>
        /// Says the room's line now, on the banner, as when the room is about to ask for access: it
        /// used to bring the controls up in front of the person.
        /// </summary>
        public void Present()
        {
            if (stage != null && shownLine.Length > 0) stage.ShowNotice(shownLine);
        }

        private void Build()
        {
            stage = placement.GetComponent<CharacterStage>();
            section = SettingsSheet.On(placement.gameObject).Section(SettingsText.YourRoom, 0);
            switchButton = section.Button("Space switch", ButtonRole.Secondary);
            switchButton.Pressed += OnSwitch;
            offerButton = section.Button("Room offer", ButtonRole.Secondary);
            offerButton.Pressed += OnOffer;
            asideButton = section.Button("Aside", ButtonRole.Secondary);
            asideButton.Pressed += OnAside;
            placement.StatusChanged += OnStatusChanged;
            placement.Changed += OnPlacementChanged;
            OnStatusChanged(placement.Status);
        }

        private void OnDestroy()
        {
            if (placement == null) return;
            placement.StatusChanged -= OnStatusChanged;
            placement.Changed -= OnPlacementChanged;
        }

        /// <summary>A stage that moved onto a surface or off it changes what is offered.</summary>
        private void OnPlacementChanged() => Layout();

        private void OnSwitch() =>
            placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);

        private void OnOffer() => placement.TakeOffer();

        /// <summary>Turns the lineup to the right to make room for a window in front, or back.</summary>
        private void OnAside()
        {
            if (stage == null || FocusGuard.InputSuspended) return;
            stage.SetAside(!stage.Aside);
            Layout();
        }

        /// <summary>The aside choice is offered while the stage stands in front of the person, not on a surface.</summary>
        private bool AsideOffered => stage != null && placement.Preferred == null;

        private void OnStatusChanged(RoomStatus status)
        {
            if (status.Line != shownLine)
            {
                shownLine = status.Line;
                if (stage != null) stage.ShowNotice(shownLine);
            }
            Layout();
        }

        /// <summary>The section's line and the buttons that apply now, each with its label, the others hidden.</summary>
        private void Layout()
        {
            var status = placement.Status;
            var offer = placement.Offer;
            section.Say(shownLine);
            section.Offer(switchButton, status.CanSwitch ? status.SwitchLabel : null);
            section.Offer(offerButton, offer != RoomOffer.None ? RoomStatus.OfferLabel(offer) : null);
            section.Offer(asideButton, AsideOffered ? (stage!.Aside ? SettingsText.CharactersInFront : SettingsText.MakeRoomForWindow) : null);
        }
    }
}
