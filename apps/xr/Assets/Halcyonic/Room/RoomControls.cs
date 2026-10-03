#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.Workspace;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The room's part of the menu's Settings, Your space (<see cref="SpaceSettings"/>, ADR 0026): a
    /// switch between the real room and a virtual space and, when it would help, an offer to allow
    /// room access or to set up the room, each row's side panel saying where the agents are and why;
    /// when that changes the stage's banner shows it as a short notice, so the news reaches the person
    /// without anything coming up in front of them. While the stage stands in front of the person,
    /// where the characters stand (<see cref="CharacterStage.SetArrangement"/>), stepping through all
    /// three so a session on the headset can compare them. The first time the person comes back from
    /// another window while the characters stand in front, the banner offers it once. Changes are
    /// ignored while <see cref="FocusGuard.InputSuspended"/>.
    /// </summary>
    /// <remarks>
    /// Nothing here is needed to get started: the room placement shows the real room where it works
    /// and the virtual space otherwise by itself.
    /// </remarks>
    internal sealed class RoomControls : MonoBehaviour, IRoomSettings
    {
        /// <summary>Kept on the device once the banner has offered to stand the characters for a window.</summary>
        private const string WindowOfferedPreference = "halcyonic.stage.windowOffered";

        private RoomPlacement placement = null!;
        private CharacterStage? stage;
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

        public RoomStatus Status => placement.Status;

        public RoomOffer Offer => placement.Offer;

        public StageArrangement? Arrangement => ArrangementOffered ? stage!.Arrangement : (StageArrangement?)null;

        private void Build()
        {
            stage = placement.GetComponent<CharacterStage>();
            // The menu's Settings offers the room through the workspace director, on the same stage.
            if (placement.TryGetComponent<WorkspaceDirector>(out var director)) director.Room = this;
            placement.StatusChanged += OnStatusChanged;
            FocusGuard.FoldChanged += OnFoldChanged;
            OnStatusChanged(placement.Status);
        }

        private void OnDestroy()
        {
            FocusGuard.FoldChanged -= OnFoldChanged;
            if (placement == null) return;
            placement.StatusChanged -= OnStatusChanged;
        }

        public void SwitchSpace()
        {
            if (FocusGuard.InputSuspended || !placement.Status.CanSwitch) return;
            placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);
        }

        public void TakeOffer()
        {
            if (FocusGuard.InputSuspended || placement.Offer == RoomOffer.None) return;
            placement.TakeOffer();
        }

        public void NextArrangement()
        {
            if (stage == null || FocusGuard.InputSuspended || !ArrangementOffered) return;
            stage.SetArrangement(SpaceSettings.Next(stage.Arrangement));
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
            if (status.Line == shownLine) return;
            shownLine = status.Line;
            if (stage != null) stage.ShowNotice(shownLine);
        }
    }
}
