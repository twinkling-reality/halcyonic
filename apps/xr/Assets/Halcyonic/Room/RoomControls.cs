#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.Workspace;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The room's controls, in their section of the Settings sheet (ADR 0023), Your room: a switch
    /// between the real room and a virtual space and, when it would help, an offer to allow room
    /// access or to set up the room; under its heading, one line says where the agents are and why,
    /// and when it changes the stage's banner shows it as a short notice, so the news reaches the
    /// person without anything coming up in front of them. Under those, while the stage stands in
    /// front of the person, where the characters stand (<see cref="CharacterStage.SetArrangement"/>):
    /// a line saying which and that the window is assumed straight ahead, and a button for each of
    /// the other two, so a session on the headset can compare all three. The first time the person
    /// comes back from another window while the characters stand in front, the banner offers it once.
    /// The buttons ignore input while <see cref="FocusGuard.InputSuspended"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here is needed to get started: the room placement shows the real room where it works
    /// and the virtual space otherwise by itself.
    /// </remarks>
    internal sealed class RoomControls : MonoBehaviour
    {
        /// <summary>Kept on the device once the banner has offered to stand the characters for a window.</summary>
        private const string WindowOfferedPreference = "halcyonic.stage.windowOffered";

        private readonly GlazeButton[] arrangementButtons = new GlazeButton[2];
        private readonly StageArrangement[] arrangementTargets = new StageArrangement[2];
        private RoomPlacement placement = null!;
        private CharacterStage? stage;
        private SettingsSection section = null!;
        private SettingsSection arrangementSection = null!;
        private GlazeButton switchButton = null!;
        private GlazeButton offerButton = null!;
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
            arrangementSection = SettingsSheet.On(placement.gameObject).Continuation(section);
            for (var index = 0; index < arrangementButtons.Length; index++)
            {
                var which = index;
                arrangementButtons[index] = arrangementSection.Button("Arrangement " + index, ButtonRole.Secondary);
                arrangementButtons[index].Pressed += () => OnArrange(which);
            }
            placement.StatusChanged += OnStatusChanged;
            placement.Changed += OnPlacementChanged;
            FocusGuard.FoldChanged += OnFoldChanged;
            OnStatusChanged(placement.Status);
        }

        private void OnDestroy()
        {
            FocusGuard.FoldChanged -= OnFoldChanged;
            if (placement == null) return;
            placement.StatusChanged -= OnStatusChanged;
            placement.Changed -= OnPlacementChanged;
        }

        /// <summary>A stage that moved onto a surface or off it changes what is offered.</summary>
        private void OnPlacementChanged() => Layout();

        private void OnSwitch() =>
            placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);

        private void OnOffer() => placement.TakeOffer();

        /// <summary>Stands the characters as the button pressed says.</summary>
        private void OnArrange(int index)
        {
            if (stage == null || FocusGuard.InputSuspended) return;
            stage.SetArrangement(arrangementTargets[index]);
            Layout();
        }

        /// <summary>
        /// The first time focus comes back from another window after it stayed away, with the
        /// characters standing in front of the person, the banner says once that Settings can move
        /// them: now the person is back, with input ready, and can.
        /// </summary>
        private void OnFoldChanged()
        {
            if (FocusGuard.Folded || stage == null || !ArrangementOffered || stage.Arrangement != StageArrangement.InFront) return;
            if (PlayerPrefs.GetInt(WindowOfferedPreference, 0) == 1) return;
            PlayerPrefs.SetInt(WindowOfferedPreference, 1);
            PlayerPrefs.Save();
            stage.ShowNotice(SettingsText.WindowOffer);
        }

        /// <summary>Where the characters stand is the person's choice while the stage stands in front of them, not on a surface.</summary>
        private bool ArrangementOffered => stage != null && placement.Preferred == null;

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
            // Under them, where the characters stand now, and a button for each of the other two.
            arrangementSection.Say(ArrangementOffered ? SettingsText.Arrangement(stage!.Arrangement) : SettingsText.ArrangedByRoom);
            var next = 0;
            foreach (StageArrangement arrangement in System.Enum.GetValues(typeof(StageArrangement)))
            {
                if (!ArrangementOffered || arrangement == stage!.Arrangement || next == arrangementButtons.Length) continue;
                arrangementTargets[next] = arrangement;
                arrangementSection.Offer(arrangementButtons[next++], SettingsText.ChangeTo(arrangement));
            }
            for (; next < arrangementButtons.Length; next++) arrangementSection.Offer(arrangementButtons[next], null);
        }
    }
}
