#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.Workspace;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>Explicit room and optional shelf controls. No window position is guessed and no placement enables agents before Apply.</summary>
    internal sealed class RoomControls : MonoBehaviour, IRoomSettings
    {
        private const string AgentsPreference = "Halcyonic.AgentsEnabled";
        private RoomPlacement placement = null!;
        private WorkspaceDirector? director;
        public static RoomControls Create(Transform parent, RoomPlacement placement)
        {
            var go = new GameObject("Room controls");
            go.transform.SetParent(parent, false);
            var controls = go.AddComponent<RoomControls>();
            controls.placement = placement;
            controls.director = placement.GetComponent<WorkspaceDirector>();
            if (controls.director != null) controls.director.Room = controls;
            placement.Draft.Changed += controls.PreviewChanged;
            return controls;
        }
        public RoomStatus Status => placement.Status;
        public RoomOffer Offer => placement.Offer;
        public StageArrangement? Arrangement => null;
        public bool AgentsEnabled => director != null && director.Presentation.AgentsEnabled;
        public bool ShelfPlaced => placement.Preferred.HasValue;
        public void Present() { }
        public void SwitchSpace()
        {
            if (!FocusGuard.InputSuspended && placement.Status.CanSwitch)
                placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);
        }
        public void TakeOffer() { if (FocusGuard.InputSuspended) return; if (placement.Draft.Candidate == null) { PlaceAgents(); return; } placement.TakeOffer(); }
        public void NextArrangement() { }
        public void ChooseArrangement(StageArrangement arrangement) { }
        public void ToggleAgents()
        {
            if (director == null || FocusGuard.InputSuspended || director.Presentation.Minimized) return;
            if (!AgentsEnabled && !ShelfPlaced) { PlaceAgents(); return; }
            SetEnabled(!AgentsEnabled);
        }
        private void SetEnabled(bool value)
        {
            if (director == null || value && !ShelfPlaced) return;
            director.Presentation.SetAgentsEnabled(value);
            PlayerPrefs.SetInt(AgentsPreference, value ? 1 : 0);
            PlayerPrefs.Save();
            // A remembered preference never grants placement in a new tracking session.
        }
        public void PlaceAgents()
        {
            if (director == null || FocusGuard.InputSuspended || director.Presentation.Minimized || director.MenuHost == null) return;
            director.EnsureAgentStage();
            if (!placement.BeginPreview()) return;
            var column = new AgentShelfColumn(director.MenuHost, placement.Draft, placement.MovePreview,
                () => { if (placement.ApplyPreview()) SetEnabled(true); }, placement.CancelPreview, placement.FindRoomSurface);
            if (!director.OpenLocal(column)) column.FocusLeft();
        }
        private void PreviewChanged() => director?.EnsureAgentStage().PreviewShelf(placement.Preview);
        private void OnDestroy()
        {
            if (placement != null) { placement.Draft.Changed -= PreviewChanged; placement.CancelPreview(); }
            if (director != null && ReferenceEquals(director.Room, this)) director.Room = null;
        }
    }
}
